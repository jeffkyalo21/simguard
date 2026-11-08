<#
.SYNOPSIS
    Smoke test of the carriers and daily usage endpoints (issue #51).

.DESCRIPTION
    Runs against a running Development API and prints the real status code
    of every step next to the expected one:
      401 without a token, list per role, add, duplicate name 409 (with the
      error on the name field), invalid names 400, rename, unknown ids 404,
      SIM create by carrierId and by carrier name, unknown carriers 400,
      blocked remove with the SIM count, allowed remove, the audit rows,
      403 for every write by an Admin and an Auditor, and the SIM
      usage read (default range, explicit range, bad ranges, unknown SIM).

    The usage checks need daily usage for -UsageSimId (default 125). Load it
    first with ml-service/scripts/load_daily_usage.py.

    The script signs in as the Development Admin, creates its own
    Security Officer and Auditor and does every carrier and SIM change as
    the Security Officer (Admin gets 403 on organisation data). Admin credentials come from
    -AdminSecretsFile (written by auth-smoke.ps1) or from the environment
    variables below; the environment wins. Passwords, tokens and MFA
    secrets are never printed.

    The sign-in endpoints are rate limited (10 per minute). Start the API
    with RateLimiting__Auth__PermitLimit=1000 for this run.

.EXAMPLE
    $env:SIMGUARD_SMOKE_ADMIN_EMAIL = 'admin@simguard.local'
    $env:SIMGUARD_SMOKE_ADMIN_PASSWORD = '<the Admin password>'
    $env:SIMGUARD_SMOKE_ADMIN_TOTP_SECRET = '<the Admin authenticator secret>'
    powershell -NoProfile -File backend/docs/carriers-smoke.ps1
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [int]$UsageSimId = 125
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

$officerEmail = "carriers-officer-$runId@simguard.local"
$auditorEmail = "carriers-auditor-$runId@simguard.local"
$officerTemp = New-TestPassword
$auditorTemp = New-TestPassword
Check 'Create Security Officer' 201 (Invoke-Api POST '/api/users' @{ email = $officerEmail; name = 'Carriers Officer'; role = 'SecurityOfficer'; temporaryPassword = $officerTemp } $admin)
Check 'Create Auditor' 201 (Invoke-Api POST '/api/users' @{ email = $auditorEmail; name = 'Carriers Auditor'; role = 'Auditor'; temporaryPassword = $auditorTemp } $admin)
$officer = Complete-FirstSignIn 'Officer' $officerEmail $officerTemp
$auditor = Complete-FirstSignIn 'Auditor' $auditorEmail $auditorTemp

$roles = [ordered]@{ Admin = $admin; Officer = $officer; Auditor = $auditor }
# Admin is the SimGuard operator and gets 403 on organisation data.
$orgRead = @{ Admin = 403; Officer = 200; Auditor = 200 }

# --- Anonymous ---------------------------------------------------------------
Check 'GET /api/carriers without token' 401 (Invoke-Api GET '/api/carriers')
Check 'POST /api/carriers without token' 401 (Invoke-Api POST '/api/carriers' @{ name = "Anon $runId" })
Check "GET /api/sims/$UsageSimId/usage without token" 401 (Invoke-Api GET "/api/sims/$UsageSimId/usage")

# --- List --------------------------------------------------------------------
foreach ($role in $roles.Keys) {
    $list = Invoke-Api GET '/api/carriers' $null $roles[$role]
    Check "$role GET /api/carriers" $orgRead[$role] $list "count=$(@($list.Data).Count)"
}
$before = @((Invoke-Api GET '/api/carriers' $null $officer).Data)
$first = $before | Select-Object -First 1
Check-True 'List items have id, name, createdAt and simCount' ($null -ne $first -and $null -ne $first.id -and $first.name -and $first.createdAt -and $null -ne $first.simCount)
$safaricom = $before | Where-Object { $_.name -eq 'Safaricom' } | Select-Object -First 1
Check-True 'Safaricom exists with SIMs' ($null -ne $safaricom -and $safaricom.simCount -gt 0) "simCount=$($safaricom.simCount)"

# --- Add, duplicate, invalid -------------------------------------------------
$name = "Smoke Carrier $runId"
$created = Invoke-Api POST '/api/carriers' @{ name = "  $name  " } $officer
Check 'Officer adds a carrier (name trimmed)' 201 $created
Check-True 'New carrier has the trimmed name and simCount 0' (($created.Data.name -eq $name) -and ($created.Data.simCount -eq 0))
$carrierId = $created.Data.id

$duplicate = Invoke-Api POST '/api/carriers' @{ name = $name.ToUpperInvariant() } $officer
Check 'Duplicate name, other casing' 409 $duplicate
Check-True 'Duplicate 409 has the error on name' (@($duplicate.Data.errors.name).Count -gt 0) "$($duplicate.Data.detail)"
Check 'Name of 1 character' 400 (Invoke-Api POST '/api/carriers' @{ name = 'x' } $officer)
Check 'Name of 41 characters' 400 (Invoke-Api POST '/api/carriers' @{ name = ('y' * 41) } $officer)
Check 'Blank name' 400 (Invoke-Api POST '/api/carriers' @{ name = '   ' } $officer)
Check 'Missing name' 400 (Invoke-Api POST '/api/carriers' @{ } $officer)

# --- Writes by other roles --------------------------------------------------
foreach ($role in @('Admin', 'Auditor')) {
    $token = $roles[$role]
    Check "$role POST /api/carriers" 403 (Invoke-Api POST '/api/carriers' @{ name = "$role $runId" } $token)
    Check "$role PUT /api/carriers/$carrierId" 403 (Invoke-Api PUT "/api/carriers/$carrierId" @{ name = "$role $runId" } $token)
    Check "$role DELETE /api/carriers/$carrierId" 403 (Invoke-Api DELETE "/api/carriers/$carrierId" $null $token)
}

# --- Rename ------------------------------------------------------------------
$renamedName = "Smoke Renamed $runId"
$renamed = Invoke-Api PUT "/api/carriers/$carrierId" @{ name = $renamedName } $officer
Check 'Officer renames the carrier' 200 $renamed
Check-True 'Rename returns the new name' ($renamed.Data.name -eq $renamedName)
Check 'Rename to an existing name (safaricom)' 409 (Invoke-Api PUT "/api/carriers/$carrierId" @{ name = 'safaricom' } $officer)
Check 'Rename with an invalid name' 400 (Invoke-Api PUT "/api/carriers/$carrierId" @{ name = 'z' } $officer)
Check 'Rename unknown carrier' 404 (Invoke-Api PUT '/api/carriers/999999' @{ name = "Nobody $runId" } $officer)
Check 'Delete unknown carrier' 404 (Invoke-Api DELETE '/api/carriers/999999' $null $officer)

# --- SIMs use the carrier ----------------------------------------------------
function New-SimBody([int]$Index) {
    $n = ($runId * 40 + $Index) % 100000000
    return @{
        phoneNumber = '+2547' + $n.ToString('00000000')
        iccid = '8925402' + ($runId * 40 + $Index).ToString('000000000000')
        planType = 'Postpaid'
        monthlyCost = 1500
    }
}

$body = New-SimBody 1
$body.carrierId = $carrierId
$simById = Invoke-Api POST '/api/sims' $body $officer
Check 'Officer creates a SIM by carrierId' 201 $simById
Check-True 'SIM response has carrierId and operator = carrier name' (($simById.Data.carrierId -eq $carrierId) -and ($simById.Data.operator -eq $renamedName))

$body = New-SimBody 2
$body.operator = $renamedName.ToLowerInvariant()
$simByName = Invoke-Api POST '/api/sims' $body $officer
Check 'Officer creates a SIM by carrier name (other casing)' 201 $simByName
Check-True 'SIM by name points to the same carrier' ($simByName.Data.carrierId -eq $carrierId)

$body = New-SimBody 3
$body.carrierId = 999999
Check 'Create SIM with unknown carrierId' 400 (Invoke-Api POST '/api/sims' $body $officer)
$body = New-SimBody 3
$body.operator = "No Such Carrier $runId"
$unknownName = Invoke-Api POST '/api/sims' $body $officer
Check 'Create SIM with unknown carrier name' 400 $unknownName
Check-True 'Unknown carrier name error is on operator' (@($unknownName.Data.errors.operator).Count -gt 0)

$move = @{ phoneNumber = $simByName.Data.phoneNumber; iccid = $simByName.Data.iccid; carrierId = 999999; planType = 'Postpaid'; monthlyCost = 1500 }
Check 'Update SIM to unknown carrierId' 400 (Invoke-Api PUT "/api/sims/$($simByName.Data.id)" $move $officer)

$listed = @((Invoke-Api GET '/api/carriers' $null $officer).Data) | Where-Object { $_.id -eq $carrierId }
Check-True 'List shows simCount 2 for the carrier' ($listed.simCount -eq 2)
$filtered = Invoke-Api GET "/api/sims?carrierId=$carrierId" $null $officer
Check-True 'GET /api/sims?carrierId= returns its 2 SIMs' ($filtered.Data.total -eq 2)

# --- Remove ------------------------------------------------------------------
$blocked = Invoke-Api DELETE "/api/carriers/$carrierId" $null $officer
Check 'Remove carrier used by SIMs' 409 $blocked "$($blocked.Data.detail)"
Check-True 'Blocked remove reports simCount 2' ($blocked.Data.simCount -eq 2)

Check 'Delete first smoke SIM' 204 (Invoke-Api DELETE "/api/sims/$($simById.Data.id)" $null $officer)
Check 'Delete second smoke SIM' 204 (Invoke-Api DELETE "/api/sims/$($simByName.Data.id)" $null $officer)
Check 'Remove carrier with no SIMs' 204 (Invoke-Api DELETE "/api/carriers/$carrierId" $null $officer)
$after = @((Invoke-Api GET '/api/carriers' $null $officer).Data)
Check-True 'Removed carrier is gone from the list' (@($after | Where-Object { $_.id -eq $carrierId }).Count -eq 0)

# --- Audit -------------------------------------------------------------------
foreach ($action in @('carrier.created', 'carrier.renamed', 'carrier.deleted')) {
    $rows = @((Invoke-Api GET "/api/audit-logs?action=$action&pageSize=100" $null $auditor).Data.items)
    $mine = @($rows | Where-Object { ($_.newValues -and $_.newValues.id -eq $carrierId) -or ($_.oldValues -and $_.oldValues.id -eq $carrierId) })
    Check-True "Audit has one $action row" ($mine.Count -eq 1)
    if ($action -eq 'carrier.renamed' -and $mine.Count -eq 1) {
        Check-True 'Rename audit has old and new names' (($mine[0].oldValues.name -eq $name) -and ($mine[0].newValues.name -eq $renamedName))
    }
}

# --- Daily usage -------------------------------------------------------------
foreach ($role in $roles.Keys) {
    Check "$role GET /api/sims/$UsageSimId/usage" $orgRead[$role] (Invoke-Api GET "/api/sims/$UsageSimId/usage" $null $roles[$role])
}
$usage = (Invoke-Api GET "/api/sims/$UsageSimId/usage" $null $officer).Data
$days = @($usage.days)
Check-True 'Default range is 30 days ending on the latest usage day' (($days.Count -eq 30) -and ($days[-1].date -eq $usage.to)) "from=$($usage.from) to=$($usage.to)"
Check-True 'Totals match the days' (($usage.totals.days -eq $days.Count) -and ($usage.totals.smsCount -eq ($days | Measure-Object -Property smsCount -Sum).Sum))
Check-True 'Day rows carry the contract fields' ($null -ne $days[0].totalDownloadMb -and $null -ne $days[0].dataDurationS -and $null -ne $days[0].smsRemaining -and $days[0].tier)

$january = (Invoke-Api GET "/api/sims/$UsageSimId/usage?from=2025-01-01&to=2025-01-31" $null $officer).Data
$jdays = @($january.days)
Check-True 'January has 31 days, oldest first' (($jdays.Count -eq 31) -and ($jdays[0].date -eq '2025-01-01') -and ($jdays[-1].date -eq '2025-01-31'))
Check 'Usage with from after to' 400 (Invoke-Api GET "/api/sims/$UsageSimId/usage?from=2025-02-01&to=2025-01-01" $null $officer)
Check 'Usage range over 366 days' 400 (Invoke-Api GET "/api/sims/$UsageSimId/usage?from=2024-01-01&to=2025-03-31" $null $officer)
Check 'Usage with a bad date' 400 (Invoke-Api GET "/api/sims/$UsageSimId/usage?from=yesterday" $null $officer)
Check 'Usage for unknown SIM' 404 (Invoke-Api GET '/api/sims/999999/usage' $null $officer)
Check "Delete SIM $UsageSimId that has usage" 409 (Invoke-Api DELETE "/api/sims/$UsageSimId" $null $officer)

Write-Results
