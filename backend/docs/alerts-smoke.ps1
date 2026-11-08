<#
.SYNOPSIS
    Smoke test of the alerts, risk score, activity, dashboard, audit log and
    demo seed endpoints (issue #13).

.DESCRIPTION
    Runs against a running Development API and prints the real status code
    of every step next to the expected one:
      401 and 403 per role on every endpoint, demo seeding (and reseeding),
      alert filters, sorting and paging, the alert review transitions
      (including a 409 and the reopen flow), the audit row written by a
      status change, risk history (including a SIM without scores), risk
      score filters, the SIM activity timeline and the dashboard summary.

    The script signs in as the Development Admin, creates its own
    Security Officer and Auditor and does every organisation action as the
    Security Officer (Admin gets 403 on organisation data). Admin credentials come from
    -AdminSecretsFile (written by auth-smoke.ps1) or from the environment
    variables below; the environment wins. Passwords, tokens and MFA
    secrets are never printed.

    The sign-in endpoints are rate limited (10 per minute). Start the API
    with RateLimiting__Auth__PermitLimit=1000 for this run.

    With -ProductionBaseUrl, the script also signs in to a second API
    instance started with ASPNETCORE_ENVIRONMENT=Production (same database)
    and checks that POST /api/dev/seed-demo does not exist there (404).

.EXAMPLE
    $env:SIMGUARD_SMOKE_ADMIN_EMAIL = 'admin@simguard.local'
    $env:SIMGUARD_SMOKE_ADMIN_PASSWORD = '<the Admin password>'
    $env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET = '<the Admin authenticator secret>'
    powershell -NoProfile -File backend/docs/alerts-smoke.ps1
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [string]$ProductionBaseUrl = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$script:results = New-Object System.Collections.Generic.List[object]

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [string]$Token = $null,
        [string]$Base = $BaseUrl
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$Base$Path")

    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 6 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
    }

    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null

    if ($text -and $text.TrimStart().StartsWith('{')) {
        $data = $text | ConvertFrom-Json
    }

    [pscustomobject]@{
        Status = [int]$response.StatusCode
        Data   = $data
    }
}

function Check {
    param([string]$Step, [int]$Expected, $Result, [string]$Note = '')

    $actual = if ($Result -is [int]) { $Result } else { $Result.Status }
    $script:results.Add([pscustomobject]@{
        Step     = $Step
        Expected = $Expected
        Actual   = $actual
        Result   = if ($actual -eq $Expected) { 'PASS' } else { 'FAIL' }
        Note     = $Note
    })
}

function Check-True {
    param([string]$Step, [bool]$Condition, [string]$Note = '')

    $script:results.Add([pscustomobject]@{
        Step     = $Step
        Expected = 'true'
        Actual   = "$Condition".ToLowerInvariant()
        Result   = if ($Condition) { 'PASS' } else { 'FAIL' }
        Note     = $Note
    })
}

# Same as auth-smoke.ps1: RFC 6238 TOTP (SHA-1, 6 digits, 30 seconds).
function Get-Totp {
    param([string]$Base32Secret, [int]$OffsetSteps = 0)

    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $clean = $Base32Secret.ToUpperInvariant().Replace(' ', '').TrimEnd('=')
    $bits = ''
    foreach ($c in $clean.ToCharArray()) {
        $bits += [Convert]::ToString($alphabet.IndexOf($c), 2).PadLeft(5, '0')
    }
    $keyBytes = New-Object byte[] ([math]::Floor($bits.Length / 8))
    for ($i = 0; $i -lt $keyBytes.Length; $i++) {
        $keyBytes[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2)
    }

    $counter = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30) + $OffsetSteps
    $counterBytes = [BitConverter]::GetBytes($counter)
    [Array]::Reverse($counterBytes)

    $hmac = [System.Security.Cryptography.HMACSHA1]::new($keyBytes)
    $hash = $hmac.ComputeHash($counterBytes)
    $offset = $hash[$hash.Length - 1] -band 0x0F
    $binary = (([int]$hash[$offset] -band 0x7F) -shl 24) -bor ([int]$hash[$offset + 1] -shl 16) -bor ([int]$hash[$offset + 2] -shl 8) -bor [int]$hash[$offset + 3]
    return ($binary % 1000000).ToString('000000')
}

function New-TestPassword {
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return 'Sg-' + [Convert]::ToBase64String($bytes).Replace('+', 'x').Replace('/', 'y')
}

function Get-SecretFromUri([string]$Uri) {
    if ($Uri -match 'secret=([A-Z2-7]+)') { return $Matches[1] }
    throw 'The authenticator URI has no secret.'
}

# Password plus TOTP sign-in for an account that already has MFA.
function Get-AdminToken {
    param([string]$Base)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminPassword } -Base $Base
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaRequired') {
        throw "Admin login failed on $Base (status $($login.Status))."
    }
    $verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login.Data.mfaToken; code = (Get-Totp $adminTotpSecret) } -Base $Base
    if ($verify.Status -ne 200) {
        throw "Admin MFA verify failed on $Base (status $($verify.Status))."
    }
    return $verify.Data.accessToken
}

# Login, MFA enrolment and forced password change for a new account.
function Complete-FirstSignIn {
    param([string]$Label, [string]$Email, [string]$TemporaryPassword)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $TemporaryPassword }
    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    $secret = Get-SecretFromUri $start.Data.authenticatorUri
    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $secret) } $login.Data.mfaToken
    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $TemporaryPassword; newPassword = (New-TestPassword) } $confirm.Data.accessToken
    Check "$Label first sign-in (login, MFA enrolment, password change)" 200 $changed
    return $changed.Data.accessToken
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    Write-Host "$($script:results.Count - $failed) passed, $failed failed"
    if ($failed -gt 0) { exit 1 }
}

# --- Admin credentials -------------------------------------------------------
$adminEmail = $null
$adminPassword = $null
$adminTotpSecret = $null

if (Test-Path $AdminSecretsFile) {
    $saved = Get-Content -Raw $AdminSecretsFile | ConvertFrom-Json
    $adminEmail = $saved.email
    $adminPassword = $saved.password
    $adminTotpSecret = $saved.totpSecret
}
if ($env:SIMGUARD_SMOKE_ADMIN_EMAIL) { $adminEmail = $env:SIMGUARD_SMOKE_ADMIN_EMAIL }
if ($env:SIMGUARD_SMOKE_ADMIN_PASSWORD) { $adminPassword = $env:SIMGUARD_SMOKE_ADMIN_PASSWORD }
if ($env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET) { $adminTotpSecret = $env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET }

if (-not $adminEmail -or -not $adminPassword -or -not $adminTotpSecret) {
    throw 'Admin credentials missing. Provide -AdminSecretsFile or SIMGUARD_SMOKE_ADMIN_EMAIL, SIMGUARD_SMOKE_ADMIN_PASSWORD and SIMGUARD_SMOKE_ADMIN_TOTP_SECRET.'
}

$runId = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

# --- Sign in and create the test accounts ------------------------------------
$admin = Get-AdminToken $BaseUrl
Check 'Admin sign-in (password and TOTP)' 200 (Invoke-Api GET '/api/auth/me' $null $admin)

$officerEmail = "alerts-officer-$runId@simguard.local"
$auditorEmail = "alerts-auditor-$runId@simguard.local"
$officerTemp = New-TestPassword
$auditorTemp = New-TestPassword
Check 'Create Security Officer' 201 (Invoke-Api POST '/api/users' @{ email = $officerEmail; name = 'Alerts Officer'; role = 'SecurityOfficer'; temporaryPassword = $officerTemp } $admin)
Check 'Create Auditor' 201 (Invoke-Api POST '/api/users' @{ email = $auditorEmail; name = 'Alerts Auditor'; role = 'Auditor'; temporaryPassword = $auditorTemp } $admin)
$officer = Complete-FirstSignIn 'Officer' $officerEmail $officerTemp
$auditor = Complete-FirstSignIn 'Auditor' $auditorEmail $auditorTemp

$roles = [ordered]@{ Admin = $admin; Officer = $officer; Auditor = $auditor }
# Admin is the SimGuard operator and gets 403 on organisation data.
$orgRead = @{ Admin = 403; Officer = 200; Auditor = 200 }

# --- Anonymous ---------------------------------------------------------------
foreach ($path in @('/api/alerts', '/api/alerts/1', '/api/sims/1/risk', '/api/risk-scores', '/api/sims/1/activity', '/api/dashboard/summary', '/api/audit-logs')) {
    Check "GET $path without token" 401 (Invoke-Api GET $path)
}
Check 'PATCH /api/alerts/1/status without token' 401 (Invoke-Api PATCH '/api/alerts/1/status' @{ status = 'cleared'; resolution = 'falsePositive' })
Check 'POST /api/dev/seed-demo without token' 401 (Invoke-Api POST '/api/dev/seed-demo')

# --- Test data: department, employee and SIMs --------------------------------
$department = Invoke-Api POST '/api/departments' @{ name = "Alerts Smoke $runId" } $officer
Check 'Officer creates department (audited)' 201 $department
$employee = Invoke-Api POST '/api/employees' @{ name = "Alerts Smoke $runId"; email = "alerts-employee-$runId@simguard.local"; departmentId = $department.Data.id; jobRole = 'Field Agent' } $officer
Check 'Officer creates employee (audited)' 201 $employee

# Enough SIMs for the seeder (30 scored SIMs) plus a few new ones every run.
$existing = (Invoke-Api GET '/api/sims?pageSize=1' $null $officer).Data.total
$toCreate = [math]::Max(3, 32 - $existing)
$createdSims = @()
for ($i = 0; $i -lt $toCreate; $i++) {
    $n = ($runId * 40 + $i) % 100000000
    $sim = Invoke-Api POST '/api/sims' @{
        phoneNumber = '+2547' + $n.ToString('00000000')
        iccid = '8925401' + ($runId * 40 + $i).ToString('000000000000')
        operator = 'Safaricom'
        planType = 'Postpaid'
        monthlyCost = 1500
    } $officer
    if ($sim.Status -ne 201) { throw "Could not create SIM $i (status $($sim.Status))." }
    $createdSims += $sim.Data
}
Check-True "Officer created $toCreate SIMs" ($createdSims.Count -eq $toCreate) "SIMs before: $existing"

# --- Demo seed ---------------------------------------------------------------
Check 'Officer POST /api/dev/seed-demo' 403 (Invoke-Api POST '/api/dev/seed-demo' $null $officer)
Check 'Auditor POST /api/dev/seed-demo' 403 (Invoke-Api POST '/api/dev/seed-demo' $null $auditor)
$seed1 = Invoke-Api POST '/api/dev/seed-demo' $null $admin
Check 'Admin POST /api/dev/seed-demo' 200 $seed1 "alerts=$($seed1.Data.alertsCreated) scores=$($seed1.Data.riskScoresCreated) sims=$($seed1.Data.simsUsed)"
$seed2 = Invoke-Api POST '/api/dev/seed-demo' $null $admin
Check 'Admin POST /api/dev/seed-demo again' 200 $seed2 "removed alerts=$($seed2.Data.alertsRemoved) scores=$($seed2.Data.riskScoresRemoved)"
Check-True 'Reseed removes exactly its own earlier rows' (($seed2.Data.alertsRemoved -eq $seed1.Data.alertsCreated) -and ($seed2.Data.riskScoresRemoved -eq $seed1.Data.riskScoresCreated))
Check-True 'Seed creates 60 alerts and 90 days per scored SIM' (($seed2.Data.alertsCreated -eq 60) -and ($seed2.Data.riskScoresCreated -eq 90 * $seed2.Data.simsUsed))

# A SIM created after seeding has no risk scores.
$n = ($runId * 40 + 39) % 100000000
$emptySim = Invoke-Api POST '/api/sims' @{ phoneNumber = '+2547' + $n.ToString('00000000'); iccid = '8925401' + ($runId * 40 + 39).ToString('000000000000'); operator = 'Airtel'; planType = 'Prepaid'; monthlyCost = 500 } $officer
Check 'Officer creates a SIM without scores' 201 $emptySim

# --- Alerts: read per role, filters, sorting, paging -------------------------
foreach ($role in $roles.Keys) {
    $list = Invoke-Api GET '/api/alerts' $null $roles[$role]
    Check "$role GET /api/alerts" $orgRead[$role] $list "total=$($list.Data.total)"
}

$all = Invoke-Api GET '/api/alerts?pageSize=100' $null $officer
$items = @($all.Data.items)
Check-True 'Default order is newest first' ((@($items | ForEach-Object { [DateTime]$_.createdAt }) -join '|') -eq (@($items | ForEach-Object { [DateTime]$_.createdAt } | Sort-Object -Descending) -join '|'))
Check-True 'List items carry phone number, reasons and riskLevel' (($items[0].simPhoneNumber -like '+254*') -and (@($items[0].reasons).Count -gt 0) -and ($items[0].riskLevel -in @('high', 'medium', 'low')))
Check-True 'Types in the list are within the vocabulary' (@($items | Where-Object { $_.type -notin @('inactiveBilling', 'sharing', 'reselling', 'ghostUsage', 'other') }).Count -eq 0)

$flagged = Invoke-Api GET '/api/alerts?status=flagged&pageSize=100' $null $officer
Check 'Filter status=flagged' 200 $flagged "total=$($flagged.Data.total)"
Check-True 'status=flagged returns only flagged' (@($flagged.Data.items | Where-Object { $_.status -ne 'flagged' }).Count -eq 0)

$sharing = Invoke-Api GET '/api/alerts?type=sharing&pageSize=100' $null $officer
Check 'Filter type=sharing' 200 $sharing "total=$($sharing.Data.total)"
Check-True 'type=sharing returns only sharing' (@($sharing.Data.items | Where-Object { $_.type -ne 'sharing' }).Count -eq 0)

$someSim = $items[0].simId
$bySim = Invoke-Api GET "/api/alerts?simId=$someSim&pageSize=100" $null $officer
Check 'Filter simId' 200 $bySim "simId=$someSim total=$($bySim.Data.total)"
Check-True 'simId filter returns only that SIM' (@($bySim.Data.items | Where-Object { $_.simId -ne $someSim }).Count -eq 0)

$high = Invoke-Api GET '/api/alerts?minScore=0.9&pageSize=100' $null $officer
Check 'Filter minScore=0.9' 200 $high "total=$($high.Data.total)"
Check-True 'minScore filter returns scores >= 0.9' (@($high.Data.items | Where-Object { [double]$_.riskScore -lt 0.9 }).Count -eq 0)

$fromUtc = [DateTime]::UtcNow.AddDays(-30).ToString('yyyy-MM-ddTHH:mm:ssZ')
$toUtc = [DateTime]::UtcNow.AddDays(-7).ToString('yyyy-MM-ddTHH:mm:ssZ')
$range = Invoke-Api GET "/api/alerts?from=$fromUtc&to=$toUtc&pageSize=100" $null $officer
Check 'Filter from/to' 200 $range "total=$($range.Data.total)"
Check-True 'from/to filter stays inside the window' (@($range.Data.items | Where-Object { ([DateTime]$_.createdAt).ToUniversalTime() -lt [DateTime]::UtcNow.AddDays(-30).AddMinutes(-1) -or ([DateTime]$_.createdAt).ToUniversalTime() -gt [DateTime]::UtcNow.AddDays(-7).AddMinutes(1) }).Count -eq 0)

$byScore = Invoke-Api GET '/api/alerts?sort=riskScore&order=desc&pageSize=100' $null $officer
$scores = @($byScore.Data.items | ForEach-Object { [double]$_.riskScore })
Check 'Sort riskScore desc' 200 $byScore
Check-True 'riskScore desc is sorted' (($scores -join '|') -eq (@($scores | Sort-Object -Descending) -join '|'))
$asc = Invoke-Api GET '/api/alerts?sort=createdAt&order=asc&pageSize=1' $null $officer
Check-True 'createdAt asc starts with the oldest' ([DateTime]$asc.Data.items[0].createdAt -le [DateTime]$items[$items.Count - 1].createdAt)

$page1 = Invoke-Api GET '/api/alerts?page=1&pageSize=5' $null $officer
$page2 = Invoke-Api GET '/api/alerts?page=2&pageSize=5' $null $officer
Check 'Paging page=2&pageSize=5' 200 $page2 "items=$(@($page2.Data.items).Count) total=$($page2.Data.total)"
Check-True 'Pages do not overlap' (@($page1.Data.items | Where-Object { $_.id -in @($page2.Data.items | ForEach-Object { $_.id }) }).Count -eq 0)
Check 'Bad status filter' 400 (Invoke-Api GET '/api/alerts?status=open' $null $officer)
Check 'Bad type filter' 400 (Invoke-Api GET '/api/alerts?type=fraud' $null $officer)
Check 'minScore above 1' 400 (Invoke-Api GET '/api/alerts?minScore=5' $null $officer)
Check 'Bad sort' 400 (Invoke-Api GET '/api/alerts?sort=simId' $null $officer)
Check 'from after to' 400 (Invoke-Api GET "/api/alerts?from=$toUtc&to=$fromUtc" $null $officer)
Check 'pageSize=0' 400 (Invoke-Api GET '/api/alerts?pageSize=0' $null $officer)

# --- Alert detail ------------------------------------------------------------
$target = @($flagged.Data.items)[0]
foreach ($role in $roles.Keys) {
    Check "$role GET /api/alerts/{id}" $orgRead[$role] (Invoke-Api GET "/api/alerts/$($target.id)" $null $roles[$role])
}
$detail = (Invoke-Api GET "/api/alerts/$($target.id)" $null $officer).Data
Check-True 'Detail has reasons, SIM and at most 5 other recent alerts' ((@($detail.reasons).Count -gt 0) -and ($detail.sim.id -eq $target.simId) -and (@($detail.recentAlerts).Count -le 5) -and (@($detail.recentAlerts | Where-Object { $_.id -eq $target.id }).Count -eq 0))
Check 'GET /api/alerts/{unknown}' 404 (Invoke-Api GET '/api/alerts/999999999' $null $officer)

# Allocate the alert's SIM when it is free, so the detail and the timeline
# show an allocation.
$allocated = $false
if ($detail.sim.status -eq 'available') {
    $alloc = Invoke-Api POST '/api/allocations' @{ simId = $target.simId; employeeId = $employee.Data.id; assignedBy = $employee.Data.id } $officer
    Check 'Officer allocates the alert SIM (audited)' 201 $alloc
    $allocated = $alloc.Status -eq 201
    $withAlloc = (Invoke-Api GET "/api/alerts/$($target.id)" $null $officer).Data
    Check-True 'Detail shows the current allocation' ($withAlloc.currentAllocation.employeeName -eq "Alerts Smoke $runId")
    $listed = (Invoke-Api GET "/api/alerts?simId=$($target.simId)" $null $officer).Data.items[0]
    Check-True 'List item shows employee and department' (($listed.employeeName -eq "Alerts Smoke $runId") -and ($listed.departmentName -eq "Alerts Smoke $runId"))
}

# --- Alert review transitions ------------------------------------------------
$statusPath = "/api/alerts/$($target.id)/status"
Check 'Auditor PATCH status' 403 (Invoke-Api PATCH $statusPath @{ status = 'cleared'; resolution = 'falsePositive' } $auditor)
Check 'PATCH flagged to escalated (not allowed)' 409 (Invoke-Api PATCH $statusPath @{ status = 'escalated' } $officer)
Check 'PATCH flagged to flagged (same status)' 409 (Invoke-Api PATCH $statusPath @{ status = 'flagged' } $officer)
Check 'PATCH unknown status' 400 (Invoke-Api PATCH $statusPath @{ status = 'resolved' } $officer)
Check 'PATCH missing status' 400 (Invoke-Api PATCH $statusPath @{} $officer)
Check 'PATCH clear without resolution' 400 (Invoke-Api PATCH $statusPath @{ status = 'cleared' } $officer)
Check 'PATCH clear with unknown resolution' 400 (Invoke-Api PATCH $statusPath @{ status = 'cleared'; resolution = 'maybe' } $officer)
Check 'PATCH clear with 501-character note' 400 (Invoke-Api PATCH $statusPath @{ status = 'cleared'; resolution = 'falsePositive'; resolutionNote = ('x' * 501) } $officer)
Check 'PATCH unknown alert' 404 (Invoke-Api PATCH '/api/alerts/999999999/status' @{ status = 'cleared'; resolution = 'falsePositive' } $officer)

$clear = Invoke-Api PATCH $statusPath @{ status = 'cleared'; resolution = 'falsePositive'; resolutionNote = 'Smoke: device swap approved.' } $officer
Check 'Officer clears flagged alert' 200 $clear "status=$($clear.Data.status) resolution=$($clear.Data.resolution)"
Check-True 'Clear sets resolvedAt, resolution and resolvedBy' (($clear.Data.status -eq 'cleared') -and ($null -ne $clear.Data.resolvedAt) -and ($clear.Data.resolution -eq 'falsePositive') -and ($clear.Data.resolvedBy.email -eq $officerEmail))
Check-True 'riskLevel is unchanged by the review' ($clear.Data.riskLevel -eq $target.riskLevel)
Check 'PATCH cleared to flagged (not allowed)' 409 (Invoke-Api PATCH $statusPath @{ status = 'flagged' } $officer)
Check 'PATCH escalated with resolution (not a close)' 400 (Invoke-Api PATCH $statusPath @{ status = 'escalated'; resolution = 'falsePositive' } $officer)

$reopen = Invoke-Api PATCH $statusPath @{ status = 'escalated' } $officer
Check 'Officer reopens cleared alert (to escalated)' 200 $reopen "status=$($reopen.Data.status)"
Check-True 'Reopen clears resolvedAt, resolution, note and resolvedBy' (($reopen.Data.status -eq 'escalated') -and ($null -eq $reopen.Data.resolvedAt) -and ($null -eq $reopen.Data.resolution) -and ($null -eq $reopen.Data.resolutionNote) -and ($null -eq $reopen.Data.resolvedBy))
Check 'Officer moves escalated to flagged' 200 (Invoke-Api PATCH $statusPath @{ status = 'flagged' } $officer)
$final = Invoke-Api PATCH $statusPath @{ status = 'cleared'; resolution = 'confirmedMisuse' } $officer
Check 'Officer clears it as confirmed misuse' 200 $final "resolvedBy=$($final.Data.resolvedBy.name)"

# --- Audit log ---------------------------------------------------------------
$auditPath = "/api/audit-logs?action=alert.status_changed&simId=$($target.simId)&pageSize=100"
Check 'Officer GET /api/audit-logs' 403 (Invoke-Api GET '/api/audit-logs' $null $officer)
Check 'Auditor GET /api/audit-logs' 200 (Invoke-Api GET '/api/audit-logs' $null $auditor)
$auditRows = Invoke-Api GET $auditPath $null $auditor
Check 'Auditor GET /api/audit-logs?action=alert.status_changed&simId=' 200 $auditRows "rows=$($auditRows.Data.total)"
$clearRow = @($auditRows.Data.items | Where-Object { $_.newValues.alertId -eq $target.id -and $_.oldValues.status -eq 'flagged' -and $_.newValues.status -eq 'cleared' -and $_.newValues.resolution -eq 'falsePositive' -and $_.actorEmail -eq $officerEmail })
Check-True 'Status change wrote an audit row with actor, old and new values' ($clearRow.Count -eq 1) "actor=$($clearRow[0].actorEmail) old=$($clearRow[0].oldValues.status) new=$($clearRow[0].newValues.status) resolution=$($clearRow[0].newValues.resolution)"
Check-True 'Audit row has actorUserId, SIM phone and no employee link' (($clearRow[0].actorUserId -gt 0) -and ($clearRow[0].affectedSimPhoneNumber -like '+254*') -and ($null -eq $clearRow[0].employeeId))
Check-True 'Audit row has actorRole SecurityOfficer and category organisation' (($clearRow[0].actorRole -eq 'SecurityOfficer') -and ($clearRow[0].category -eq 'organisation'))
$adminAudit = Invoke-Api GET $auditPath $null $admin
Check-True 'Admin sees no organisation audit rows' (($adminAudit.Status -eq 200) -and ($adminAudit.Data.total -eq 0))
$adminAll = Invoke-Api GET '/api/audit-logs?pageSize=100' $null $admin
Check-True 'Admin audit rows are all system rows' (($adminAll.Data.total -gt 0) -and (@($adminAll.Data.items | Where-Object { $_.category -ne 'system' }).Count -eq 0))
Check-True 'Four status changes recorded, newest first' ((@($auditRows.Data.items | Where-Object { $_.newValues.alertId -eq $target.id }).Count -eq 4) -and (@($auditRows.Data.items)[0].newValues.status -eq 'cleared') -and (@($auditRows.Data.items)[0].newValues.resolution -eq 'confirmedMisuse'))

$byActor = Invoke-Api GET "/api/audit-logs?actorEmail=$($officerEmail.ToUpperInvariant())&pageSize=100" $null $auditor
Check 'Filter actorEmail (case-insensitive)' 200 $byActor "rows=$($byActor.Data.total)"
Check-True 'actorEmail filter returns only that actor' ((@($byActor.Data.items).Count -gt 0) -and (@($byActor.Data.items | Where-Object { $_.actorEmail -ne $officerEmail }).Count -eq 0))
$simCreated = Invoke-Api GET "/api/audit-logs?action=sim.created&simId=$($emptySim.Data.id)" $null $auditor
Check-True 'SIM creation is audited with the Officer as actor' (($simCreated.Data.total -eq 1) -and ($simCreated.Data.items[0].actorEmail -eq $officerEmail) -and ($simCreated.Data.items[0].newValues.phoneNumber -eq $emptySim.Data.phoneNumber))
$deptCreated = Invoke-Api GET "/api/audit-logs?action=department.created&from=$([DateTime]::UtcNow.AddMinutes(-30).ToString('yyyy-MM-ddTHH:mm:ssZ'))" $null $auditor
Check-True 'Department creation is audited' (@($deptCreated.Data.items | Where-Object { $_.newValues.id -eq $department.Data.id }).Count -eq 1)
$seedRows = Invoke-Api GET '/api/audit-logs?action=dev.demo_seeded&pageSize=2' $null $auditor
Check-True 'Demo seeding is audited' ($seedRows.Data.total -ge 2)
$auditPage = Invoke-Api GET '/api/audit-logs?page=2&pageSize=3' $null $auditor
Check 'Audit paging page=2&pageSize=3' 200 $auditPage "items=$(@($auditPage.Data.items).Count) total=$($auditPage.Data.total)"
Check 'Audit from after to' 400 (Invoke-Api GET "/api/audit-logs?from=$toUtc&to=$fromUtc" $null $auditor)

# --- Risk --------------------------------------------------------------------
$scoredSim = (Invoke-Api GET '/api/risk-scores?pageSize=1' $null $officer).Data.items[0].simId
foreach ($role in $roles.Keys) {
    Check "$role GET /api/sims/{id}/risk" $orgRead[$role] (Invoke-Api GET "/api/sims/$scoredSim/risk" $null $roles[$role])
}
$risk = (Invoke-Api GET "/api/sims/$scoredSim/risk" $null $officer).Data
Check-True 'Risk has latest, riskLevel and 90 days of history, oldest first' (($null -ne $risk.latest) -and ($risk.latest.riskLevel -in @('high', 'medium', 'low')) -and (@($risk.history).Count -eq 90) -and ($risk.history[0].date -lt $risk.history[89].date))
Check-True 'Latest riskLevel follows the bucket' ((@{ flag = 'high'; escalated = 'medium'; clear = 'low' })[$risk.latest.bucket] -eq $risk.latest.riskLevel)
$risk30 = Invoke-Api GET "/api/sims/$scoredSim/risk?days=30" $null $officer
Check 'Risk days=30' 200 $risk30 "history=$(@($risk30.Data.history).Count)"
Check 'Risk days=0' 400 (Invoke-Api GET "/api/sims/$scoredSim/risk?days=0" $null $officer)
Check 'Risk days=366' 400 (Invoke-Api GET "/api/sims/$scoredSim/risk?days=366" $null $officer)
$empty = Invoke-Api GET "/api/sims/$($emptySim.Data.id)/risk" $null $officer
Check 'Risk for a SIM without scores' 200 $empty "latest=$(if ($null -eq $empty.Data.latest) { 'null' } else { 'set' }) history=$(@($empty.Data.history).Count)"
Check-True 'SIM without scores: latest null, empty history' (($null -eq $empty.Data.latest) -and (@($empty.Data.history).Count -eq 0))
Check 'Risk for unknown SIM' 404 (Invoke-Api GET '/api/sims/999999999/risk' $null $officer)

foreach ($role in $roles.Keys) {
    Check "$role GET /api/risk-scores" $orgRead[$role] (Invoke-Api GET '/api/risk-scores' $null $roles[$role])
}
$flagScores = Invoke-Api GET '/api/risk-scores?bucket=flag&pageSize=100' $null $officer
Check 'Filter bucket=flag' 200 $flagScores "total=$($flagScores.Data.total)"
Check-True 'bucket=flag returns flag rows with riskLevel high' (@($flagScores.Data.items | Where-Object { $_.bucket -ne 'flag' -or $_.riskLevel -ne 'high' }).Count -eq 0)
$fromDate = [DateTime]::UtcNow.AddDays(-10).ToString('yyyy-MM-dd')
$toDate = [DateTime]::UtcNow.AddDays(-5).ToString('yyyy-MM-dd')
$simScores = Invoke-Api GET "/api/risk-scores?simId=$scoredSim&from=$fromDate&to=$toDate" $null $officer
Check 'Filter simId, from, to' 200 $simScores "total=$($simScores.Data.total)"
Check-True 'simId and date filters: 6 days, newest first' (($simScores.Data.total -eq 6) -and ($simScores.Data.items[0].date -eq $toDate))
Check 'Bad bucket' 400 (Invoke-Api GET '/api/risk-scores?bucket=high' $null $officer)
Check 'Risk scores from after to' 400 (Invoke-Api GET "/api/risk-scores?from=$toDate&to=$fromDate" $null $officer)
$scorePage = Invoke-Api GET '/api/risk-scores?page=3&pageSize=7' $null $officer
Check-True 'Risk score paging' (($scorePage.Status -eq 200) -and (@($scorePage.Data.items).Count -eq 7) -and ($scorePage.Data.page -eq 3))

# --- Activity ----------------------------------------------------------------
foreach ($role in $roles.Keys) {
    Check "$role GET /api/sims/{id}/activity" $orgRead[$role] (Invoke-Api GET "/api/sims/$($target.simId)/activity" $null $roles[$role])
}
$activity = (Invoke-Api GET "/api/sims/$($target.simId)/activity?pageSize=100" $null $officer).Data
$kinds = @($activity.items | ForEach-Object { $_.kind } | Sort-Object -Unique)
Check-True 'Activity merges alerts (opened and cleared)' (($kinds -contains 'alertOpened') -and ($kinds -contains 'alertCleared')) "kinds: $($kinds -join ',')"
if ($allocated) {
    Check-True 'Activity includes the allocation' ($kinds -contains 'allocationStarted')
}
$times = @($activity.items | ForEach-Object { [DateTime]$_.occurredAt })
Check-True 'Activity is newest first' (($times -join '|') -eq (@($times | Sort-Object -Descending) -join '|'))
$activityPage = Invoke-Api GET "/api/sims/$($target.simId)/activity?page=1&pageSize=2" $null $officer
Check-True 'Activity paging' (($activityPage.Status -eq 200) -and (@($activityPage.Data.items).Count -le 2) -and ($activityPage.Data.total -eq $activity.total))
Check 'Activity for unknown SIM' 404 (Invoke-Api GET '/api/sims/999999999/activity' $null $officer)
$simAudit = (Invoke-Api GET "/api/sims/$($emptySim.Data.id)/activity" $null $officer).Data
Check-True 'Activity shows the sim.created audit row' (@($simAudit.items | Where-Object { $_.kind -eq 'audit' -and $_.action -eq 'sim.created' -and $_.actorEmail -eq $officerEmail }).Count -eq 1)

# --- Dashboard ---------------------------------------------------------------
foreach ($role in $roles.Keys) {
    Check "$role GET /api/dashboard/summary" $orgRead[$role] (Invoke-Api GET '/api/dashboard/summary' $null $roles[$role])
}
foreach ($d in @(7, 30, 90)) {
    $summary = Invoke-Api GET "/api/dashboard/summary?days=$d" $null $officer
    Check "Dashboard days=$d" 200 $summary "activeSims=$($summary.Data.activeSims) flagged=$($summary.Data.flagged) toReview=$($summary.Data.toReview) resolved=$($summary.Data.resolved)"
}
Check 'Dashboard days=14' 400 (Invoke-Api GET '/api/dashboard/summary?days=14' $null $officer)
Check 'Dashboard days=abc' 400 (Invoke-Api GET '/api/dashboard/summary?days=abc' $null $officer)
$summary = (Invoke-Api GET '/api/dashboard/summary?days=90' $null $officer).Data
Check-True 'Dashboard flagged and toReview match the alert list' (($summary.flagged -eq (Invoke-Api GET '/api/alerts?status=flagged&pageSize=1' $null $officer).Data.total) -and ($summary.toReview -eq (Invoke-Api GET '/api/alerts?status=escalated&pageSize=1' $null $officer).Data.total))
Check-True 'alertsByType has the four types plus other' ((@($summary.alertsByType.PSObject.Properties.Name) -join ',') -eq 'inactiveBilling,sharing,reselling,ghostUsage,other')
Check-True 'needsReview has 5 flagged or escalated alerts with SIM and reasons' ((@($summary.needsReview).Count -eq 5) -and (@($summary.needsReview | Where-Object { $_.status -notin @('flagged', 'escalated') -or -not $_.simPhoneNumber -or @($_.reasons).Count -eq 0 }).Count -eq 0))
Check-True 'tierChanges is empty and dataWaste is null' ((@($summary.tierChanges).Count -eq 0) -and ($null -eq $summary.dataWaste))

# --- Seed endpoint outside Development ---------------------------------------
if ($ProductionBaseUrl) {
    $prodAdmin = Get-AdminToken $ProductionBaseUrl
    Check 'Production: Admin token works' 200 (Invoke-Api GET '/api/auth/me' $null $prodAdmin -Base $ProductionBaseUrl)
    Check 'Production: POST /api/dev/seed-demo as Admin' 404 (Invoke-Api POST '/api/dev/seed-demo' $null $prodAdmin -Base $ProductionBaseUrl)
}

Write-Results
