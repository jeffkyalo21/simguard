<#
.SYNOPSIS
    Smoke test of the read endpoints added for the frontend wiring (issue #14).

.DESCRIPTION
    Runs against a running Development API with the demo data imported
    (import-demo-data.ps1, usage from 2025-01-01 to 2025-03-31) and prints
    the real status code of every step next to the expected one:
      401 without a token and the role matrix (Admin 403, Security Officer
      and Auditor 200) for GET /api/tiers, /api/usage/analytics,
      /api/sims/{id}/usage/cycle and /api/dashboard/summary; the tier
      allowance table; the billing cycle of one SIM; the fleet analytics
      with its filters and 400s; the as-of date on the dashboard and the
      SIM usage; allowance and monthly cost on SIM responses; the risk
      history `to` date; departmentName on allocations. Part 2: the
      field named on a duplicate SIM 409, occurredOn on alert activity, and
      applying a tier recommendation (runs one with ?force=true, needs the
      ML service).

    The allocation check creates a department, an employee and one
    allocation of an available SIM, then returns the SIM. The per-SIM
    checks use -UsageSimId, or the first SIM with a tier when it is 0. The department,
    employee and allocation history stay, as do the two test accounts.

    Admin credentials come from -AdminSecretsFile (written by
    auth-smoke.ps1) or from the environment variables below; the
    environment wins. Passwords, tokens and MFA secrets are never printed.

    The sign-in endpoints are rate limited (10 per minute). Start the API
    with RateLimiting__Auth__PermitLimit=1000 for this run.

.EXAMPLE
    $env:SIMGUARD_SMOKE_ADMIN_EMAIL = 'admin@simguard.local'
    $env:SIMGUARD_SMOKE_ADMIN_PASSWORD = '<the Admin password>'
    $env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET = '<the Admin authenticator secret>'
    powershell -NoProfile -File backend/docs/wiring-smoke.ps1
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [int]$UsageSimId = 0
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

    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) {
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

$officerEmail = "wiring-officer-$runId@simguard.local"
$auditorEmail = "wiring-auditor-$runId@simguard.local"
$officerTemp = New-TestPassword
$auditorTemp = New-TestPassword
Check 'Create Security Officer' 201 (Invoke-Api POST '/api/users' @{ email = $officerEmail; name = 'Wiring Officer'; role = 'SecurityOfficer'; temporaryPassword = $officerTemp } $admin)
Check 'Create Auditor' 201 (Invoke-Api POST '/api/users' @{ email = $auditorEmail; name = 'Wiring Auditor'; role = 'Auditor'; temporaryPassword = $auditorTemp } $admin)
$officer = Complete-FirstSignIn 'Officer' $officerEmail $officerTemp
$auditor = Complete-FirstSignIn 'Auditor' $auditorEmail $auditorTemp

$roles = [ordered]@{ Admin = $admin; Officer = $officer; Auditor = $auditor }
# Admin is the SimGuard operator and gets 403 on organisation data.
$orgRead = @{ Admin = 403; Officer = 200; Auditor = 200 }

# The SIM for the per-SIM checks: -UsageSimId, or the first SIM with a tier.
if ($UsageSimId -le 0) {
    $UsageSimId = @((Invoke-Api GET '/api/sims?pageSize=50' $null $officer).Data.items | Where-Object { $_.tier }) | Select-Object -First 1 -ExpandProperty id
}
Check-True 'A SIM with a tier is available for the per-SIM checks' ($UsageSimId -gt 0) "simId=$UsageSimId"

# --- Anonymous ---------------------------------------------------------------
Check 'GET /api/tiers without token' 401 (Invoke-Api GET '/api/tiers')
Check 'GET /api/usage/analytics without token' 401 (Invoke-Api GET '/api/usage/analytics')
Check "GET /api/sims/$UsageSimId/usage/cycle without token" 401 (Invoke-Api GET "/api/sims/$UsageSimId/usage/cycle")

# --- Role matrix -------------------------------------------------------------
foreach ($role in $roles.Keys) {
    foreach ($path in @('/api/tiers', '/api/usage/analytics', "/api/sims/$UsageSimId/usage/cycle", '/api/dashboard/summary')) {
        Check "$role GET $path" $orgRead[$role] (Invoke-Api GET $path $null $roles[$role])
    }
}

# --- Tier allowances ---------------------------------------------------------
$tiers = @((Invoke-Api GET '/api/tiers' $null $auditor).Data)
Check-True 'Four tiers, smallest bundle first' (($tiers.Count -eq 4) -and ($tiers[0].tier -eq 'support') -and ($tiers[3].tier -eq 'field')) "$(($tiers | ForEach-Object { $_.tier }) -join ',')"
$field = $tiers | Where-Object { $_.tier -eq 'field' } | Select-Object -First 1
Check-True 'Field tier is 100 GB, 500 min, 340 SMS, 5000 KES' (($field.dataMb -eq 102400) -and ($field.voiceMinutes -eq 500) -and ($field.sms -eq 340) -and ($field.monthlyCost -eq 5000))

# --- SIM responses -----------------------------------------------------------
$sim = (Invoke-Api GET "/api/sims/$UsageSimId" $null $officer).Data
Check-True "SIM $UsageSimId has a tier allowance" ($null -ne $sim.allowance -and $sim.allowance.tier -eq $sim.tier) "tier=$($sim.tier)"
Check-True "SIM $UsageSimId monthly cost is the tier cost, not 0" (($sim.monthlyCost -gt 0) -and ($sim.monthlyCost -eq $sim.allowance.monthlyCost)) "monthlyCost=$($sim.monthlyCost)"
$page = (Invoke-Api GET '/api/sims?pageSize=5' $null $officer).Data
Check-True 'SIM list items carry allowance' (@($page.items | Where-Object { $_.tier -and $null -eq $_.allowance }).Count -eq 0)

# --- Billing cycle -----------------------------------------------------------
$cycle = (Invoke-Api GET "/api/sims/$UsageSimId/usage/cycle" $null $officer).Data
$cdays = @($cycle.days)
Check-True 'Cycle holds the as-of date 2025-03-31' (($cycle.asOf -eq '2025-03-31') -and ($cycle.cycle -eq 2) -and ($cycle.cycleStart -eq '2025-03-02') -and ($cycle.cycleEnd -eq '2025-03-31')) "asOf=$($cycle.asOf) cycle=$($cycle.cycle) start=$($cycle.cycleStart)"
Check-True 'Cycle is 30 days with 30 elapsed' (($cycle.cycleLength -eq 30) -and ($cycle.daysElapsed -eq 30) -and ($cdays.Count -eq 30))
Check-True 'Cycle allowance matches the SIM tier' ($null -ne $cycle.allowance -and $cycle.allowance.tier -eq $cycle.tier)
Check-True 'Cumulative data ends on the used total' ([math]::Abs($cdays[-1].cumulativeDataMb - $cycle.used.dataMb) -lt 0.2) "last=$($cdays[-1].cumulativeDataMb) used=$($cycle.used.dataMb)"
Check-True 'Used SMS is the sum of the days' ($cycle.used.sms -eq ($cdays | Measure-Object -Property sms -Sum).Sum)
Check 'Cycle for unknown SIM' 404 (Invoke-Api GET '/api/sims/999999/usage/cycle' $null $officer)

# --- Usage analytics ---------------------------------------------------------
$analytics = (Invoke-Api GET '/api/usage/analytics' $null $officer).Data
Check-True 'Analytics default is 30 days up to the as-of date' (($analytics.days -eq 30) -and ($analytics.asOf -eq '2025-03-31') -and ($analytics.from -eq '2025-03-02') -and ($analytics.to -eq '2025-03-31')) "from=$($analytics.from) to=$($analytics.to)"
Check-True 'Analytics has SIMs, 30 daily totals and paid above 0' (($analytics.totals.simCount -gt 0) -and (@($analytics.daily).Count -eq 30) -and ($analytics.totals.dataPaidMb -gt 0)) "sims=$($analytics.totals.simCount) utilisation=$($analytics.totals.utilisationPercent)%"
Check-True 'Top and least used lists have at most 10 rows' ((@($analytics.topSims).Count -le 10) -and (@($analytics.topSims).Count -gt 0) -and (@($analytics.leastUsedSims).Count -le 10))
Check-True 'Top SIMs are sorted by data used' ((@($analytics.topSims).Count -lt 2) -or ($analytics.topSims[0].dataUsedMb -ge $analytics.topSims[1].dataUsedMb))
$byTierCount = ($analytics.byTier | Measure-Object -Property simCount -Sum).Sum
Check-True 'Tier groups add up to the SIM count' ($byTierCount -eq $analytics.totals.simCount)
$week = (Invoke-Api GET '/api/usage/analytics?days=7' $null $officer).Data
Check-True 'Seven days start on 2025-03-25' (($week.from -eq '2025-03-25') -and (@($week.daily).Count -eq 7))
$fieldOnly = (Invoke-Api GET '/api/usage/analytics?tier=FIELD' $null $officer).Data
Check-True 'Tier filter ignores case and keeps only field SIMs' ((@($fieldOnly.byTier).Count -eq 1) -and ($fieldOnly.byTier[0].name -eq 'field'))
$noDept = (Invoke-Api GET '/api/usage/analytics?departmentId=999999' $null $officer).Data
Check-True 'Unknown department gives no SIMs' ($noDept.totals.simCount -eq 0)
Check 'Analytics with days=15' 400 (Invoke-Api GET '/api/usage/analytics?days=15' $null $officer)
Check 'Analytics with an unknown tier' 400 (Invoke-Api GET '/api/usage/analytics?tier=gold' $null $officer)

# --- As-of dates -------------------------------------------------------------
$dashboard = (Invoke-Api GET '/api/dashboard/summary?days=30' $null $officer).Data
Check-True 'Dashboard asOf is 2025-03-31' ($dashboard.asOf -eq '2025-03-31') "asOf=$($dashboard.asOf)"
Check-True 'Dashboard window ends on the as-of date' ("$($dashboard.to)" -match '^2025-03-31' -or ([datetime]$dashboard.to).ToUniversalTime().ToString('yyyy-MM-dd') -eq '2025-03-31') "to=$($dashboard.to)"
Check-True 'Dashboard has data waste' ($null -ne $dashboard.dataWaste -and $dashboard.dataWaste.paidMb -gt 0) "unused=$($dashboard.dataWaste.unusedPercent)%"
Check-True 'Dashboard tier changes is a list' ($null -ne $dashboard.PSObject.Properties['tierChanges'])
$usage = (Invoke-Api GET "/api/sims/$UsageSimId/usage" $null $officer).Data
Check-True 'SIM usage carries asOf' ($usage.asOf -eq '2025-03-31')

# --- Risk history to ---------------------------------------------------------
$risk = Invoke-Api GET "/api/sims/$UsageSimId/risk?days=90&to=2025-03-31" $null $officer
Check "GET /api/sims/$UsageSimId/risk with to" 200 $risk
$history = @($risk.Data.history)
Check-True 'Risk history ends on or before to' (($history.Count -eq 0) -or ($history[-1].date -le '2025-03-31')) "points=$($history.Count)"
Check 'Risk with a bad to date' 400 (Invoke-Api GET "/api/sims/$UsageSimId/risk?to=soon" $null $officer)

# --- Allocations departmentName ---------------------------------------------
$department = Invoke-Api POST '/api/departments' @{ name = "Wiring Dept $runId" } $officer
Check 'Officer creates a department' 201 $department
$employee = Invoke-Api POST '/api/employees' @{ name = "Wiring Employee $runId"; email = "wiring-employee-$runId@simguard.local"; departmentId = $department.Data.id; jobRole = 'Tester' } $officer
Check 'Officer creates an employee' 201 $employee
$available = @((Invoke-Api GET '/api/sims?status=available&pageSize=5' $null $officer).Data.items | Where-Object { $_.id -ne $UsageSimId }) | Select-Object -First 1
$allocation = Invoke-Api POST '/api/allocations' @{ simId = $available.id; employeeId = $employee.Data.id; assignedBy = $employee.Data.id } $officer
Check 'Officer allocates an available SIM' 201 $allocation
Check-True 'Allocation response has the department name' ($allocation.Data.departmentName -eq "Wiring Dept $runId")
$byName = @((Invoke-Api GET "/api/sims?search=Wiring Employee $runId" $null $officer).Data.items | Where-Object { $_.id -eq $available.id })
Check-True 'SIM search finds the SIM by its holder name' ($byName.Count -eq 1)
$byEmail = @((Invoke-Api GET "/api/sims?search=wiring-employee-$runId@" $null $officer).Data.items | Where-Object { $_.id -eq $available.id })
Check-True 'SIM search finds the SIM by its holder email' ($byEmail.Count -eq 1)
$listed = @((Invoke-Api GET "/api/allocations?employeeId=$($employee.Data.id)&active=true" $null $auditor).Data.items | Where-Object { $_.id -eq $allocation.Data.id })
Check-True 'Allocation list item has the department name' (($listed.Count -eq 1) -and ($listed[0].departmentName -eq "Wiring Dept $runId"))
Check 'Officer returns the SIM' 200 (Invoke-Api POST "/api/allocations/$($allocation.Data.id)/return" $null $officer)
$afterReturn = @((Invoke-Api GET "/api/sims?search=Wiring Employee $runId" $null $officer).Data.items)
Check-True 'SIM search no longer finds a returned SIM by its old holder' ($afterReturn.Count -eq 0)

# --- SIM duplicate fields (part 2) ------------------------------------------
$dupPhone = Invoke-Api POST '/api/sims' @{ phoneNumber = $sim.phoneNumber; iccid = "89254$runId".PadRight(19, '7'); carrierId = $sim.carrierId; planType = 'support'; monthlyCost = 0 } $officer
Check 'Duplicate phone number' 409 $dupPhone
Check-True 'Duplicate phone 409 names the phoneNumber field' ($null -ne $dupPhone.Data.errors.phoneNumber)
$dupIccid = Invoke-Api POST '/api/sims' @{ phoneNumber = '+254799' + ("$runId".Substring("$runId".Length - 6)); iccid = $sim.iccid; carrierId = $sim.carrierId; planType = 'support'; monthlyCost = 0 } $officer
Check 'Duplicate ICCID' 409 $dupIccid
Check-True 'Duplicate ICCID 409 names the iccid field' ($null -ne $dupIccid.Data.errors.iccid)

# --- Activity occurredOn (part 2) -------------------------------------------
$alert = @((Invoke-Api GET '/api/alerts?pageSize=1' $null $officer).Data.items) | Select-Object -First 1
$activity = @((Invoke-Api GET "/api/sims/$($alert.simId)/activity?pageSize=100" $null $officer).Data.items)
$opened = @($activity | Where-Object { $_.kind -eq 'alertOpened' -and $_.referenceId -eq $alert.id }) | Select-Object -First 1
Check-True 'alertOpened activity carries the detection date as occurredOn' ($null -ne $opened -and $opened.occurredOn -eq $alert.detectedOn) "occurredOn=$($opened.occurredOn) detectedOn=$($alert.detectedOn)"
Check-True 'Other activity rows have no occurredOn' (@($activity | Where-Object { $_.kind -ne 'alertOpened' -and $null -ne $_.occurredOn }).Count -eq 0)

# --- Tier recommendation apply (part 2) -------------------------------------
# ?force=true skips the run interval (Development only), so the check can
# repeat on the same database.
$run = Invoke-Api POST '/api/tier-recommendations/run?force=true' $null $officer
Check-True 'Officer starts a tier recommendation run' (@(202, 409) -contains $run.Status) "status=$($run.Status)"
for ($i = 0; $i -lt 60; $i++) {
    $tierStatus = (Invoke-Api GET '/api/tier-recommendations/status' $null $officer).Data
    if (-not $tierStatus.running) { break }
    Start-Sleep -Seconds 2
}
Check-True 'Tier recommendation run finished' (-not $tierStatus.running) "status=$($tierStatus.lastRun.status)"
$open = @((Invoke-Api GET '/api/tier-recommendations?status=open&pageSize=5' $null $auditor).Data.items)
Check-True 'Open tier recommendations exist' ($open.Count -gt 0) "open=$($open.Count)"
if ($open.Count -gt 0) {
    $target = $open[0]
    Check 'Auditor cannot apply' 403 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/apply" $null $auditor)
    Check 'Admin cannot apply' 403 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/apply" $null $admin)
    $applied = Invoke-Api POST "/api/tier-recommendations/$($target.id)/apply" $null $officer
    Check 'Officer applies a recommendation' 200 $applied
    Check-True 'Applied recommendation has status applied and appliedAt' (($applied.Data.status -eq 'applied') -and ($null -ne $applied.Data.appliedAt))
    $after = (Invoke-Api GET "/api/sims/$($target.simId)" $null $officer).Data
    Check-True 'SIM is now on the recommended tier' ($after.tier -eq $target.recommendedTier) "tier=$($after.tier)"
    Check 'Applying again' 409 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/apply" $null $officer)
    Check 'Dismissing an applied recommendation' 409 (Invoke-Api POST "/api/tier-recommendations/$($target.id)/dismiss" $null $officer)
    $appliedList = @((Invoke-Api GET '/api/tier-recommendations?status=applied&pageSize=100' $null $auditor).Data.items | Where-Object { $_.id -eq $target.id })
    Check-True 'Applied filter lists it' ($appliedList.Count -eq 1)
    $audit = @((Invoke-Api GET '/api/audit-logs?action=tier_recommendation.applied&pageSize=5' $null $auditor).Data.items)
    Check-True 'Apply is in the audit log' ($audit.Count -gt 0)
}
Check 'Apply unknown recommendation' 404 (Invoke-Api POST '/api/tier-recommendations/999999/apply' $null $officer)

Write-Results
