<#
.SYNOPSIS
    Smoke test of the role by endpoint matrix (issue #14).

.DESCRIPTION
    Runs against a running Development API. For each role (Admin,
    SecurityOfficer, Auditor) it calls one representative endpoint per group
    and checks the expected status:

      group          endpoint                          Admin  Officer  Auditor
      org read       GET /api/sims                     403    200      200
      org write      POST /api/departments             403    201      403
      status change  PATCH /api/sims/{id}/status       403    200      403
      tier change    PATCH /api/sims/{id}/tier         403    200      403
      users          GET /api/users                    200    403      403
      audit logs     GET /api/audit-logs               200    403      200
      auth events    GET /api/auth/events              200    403      200
      health         GET /health/database              200    200      200

    It also checks the tier endpoint (400 and 404, the audit row with old and
    new tier) and audit log filtering by role: Admin sees only "system" rows,
    the Auditor sees every row.

    Accounts:
      Admin: -AdminSecretsFile (written by auth-smoke.ps1) or the
        SIMGUARD_SMOKE_ADMIN_EMAIL, _PASSWORD and _TOTP_SECRET variables.
      Officer and Auditor, in this order:
        1. -OfficerSecretsFile / -AuditorSecretsFile from an earlier run
           (email, password, totpSecret): password and TOTP sign-in.
        2. SIMGUARD_SMOKE_OFFICER_EMAIL and SIMGUARD_SMOKE_OFFICER_PASSWORD
           (and the AUDITOR equivalents): the accounts the Development seed
           creates from SimGuard:Seed:OfficerEmail/OfficerPassword and
           AuditorEmail/AuditorPassword. The script completes their first
           sign-in (MFA enrolment, password change) and writes the secrets
           file for the next run.
        3. Otherwise the Admin creates a throwaway account through
           POST /api/users.
      Secrets files belong outside the repository. Passwords, tokens and MFA
      secrets are never printed.

    The sign-in endpoints are rate limited (10 per minute). Start the API
    with RateLimiting__Auth__PermitLimit=1000 for this run.

.EXAMPLE
    powershell -NoProfile -File backend/docs/roles-smoke.ps1 -AdminSecretsFile $env:TEMP\simguard-smoke-admin.json
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [string]$OfficerSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-officer.json'),
    [string]$AuditorSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-auditor.json')
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
        [string]$Token = $null
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")

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
    param([string]$Base32Secret)

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

    $counter = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
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
function Get-Token {
    param([string]$Label, [string]$Email, [string]$Password, [string]$TotpSecret)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password }
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaRequired') {
        throw "$Label login failed (status $($login.Status))."
    }
    $verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login.Data.mfaToken; code = (Get-Totp $TotpSecret) }
    if ($verify.Status -ne 200) {
        throw "$Label MFA verify failed (status $($verify.Status))."
    }
    return $verify.Data.accessToken
}

# Login, MFA enrolment and forced password change for a new account.
# Returns the access token, the new password and the authenticator secret.
function Complete-FirstSignIn {
    param([string]$Label, [string]$Email, [string]$TemporaryPassword)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $TemporaryPassword }
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaEnrollmentRequired') {
        throw "$Label first login failed (status $($login.Status))."
    }
    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    $secret = Get-SecretFromUri $start.Data.authenticatorUri
    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $secret) } $login.Data.mfaToken
    $newPassword = New-TestPassword
    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $TemporaryPassword; newPassword = $newPassword } $confirm.Data.accessToken
    Check "$Label first sign-in (login, MFA enrolment, password change)" 200 $changed
    return [pscustomobject]@{ Token = $changed.Data.accessToken; Password = $newPassword; TotpSecret = $secret }
}

# Signs in an Officer or Auditor: saved secrets, then the seeded account
# from the environment, then a throwaway account created by the Admin.
function Get-RoleToken {
    param([string]$Label, [string]$Role, [string]$SecretsFile, [string]$EnvPrefix, [string]$AdminToken)

    if (Test-Path $SecretsFile) {
        $saved = Get-Content -Raw $SecretsFile | ConvertFrom-Json
        $token = Get-Token $Label $saved.email $saved.password $saved.totpSecret
        Check-True "$Label signs in from the secrets file" ([bool]$token)
        return [pscustomobject]@{ Token = $token; Email = $saved.email }
    }

    $email = [Environment]::GetEnvironmentVariable("SIMGUARD_SMOKE_$($EnvPrefix)_EMAIL")
    $password = [Environment]::GetEnvironmentVariable("SIMGUARD_SMOKE_$($EnvPrefix)_PASSWORD")

    if ($email -and $password) {
        $session = Complete-FirstSignIn "$Label (seeded)" $email $password
        @{ email = $email; password = $session.Password; totpSecret = $session.TotpSecret } |
            ConvertTo-Json | Set-Content -Path $SecretsFile -Encoding UTF8
        return [pscustomobject]@{ Token = $session.Token; Email = $email }
    }

    $email = "roles-$($Role.ToLowerInvariant())-$runId@simguard.local"
    $temp = New-TestPassword
    Check "Admin creates a throwaway $Role" 201 (Invoke-Api POST '/api/users' @{ email = $email; name = "Roles $Label"; role = $Role; temporaryPassword = $temp } $AdminToken)
    $session = Complete-FirstSignIn $Label $email $temp
    return [pscustomobject]@{ Token = $session.Token; Email = $email }
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    Write-Host "$($script:results.Count - $failed) passed, $failed failed"
    if ($failed -gt 0) { exit 1 }
}

# --- Accounts ----------------------------------------------------------------
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

$admin = Get-Token 'Admin' $adminEmail $adminPassword $adminTotpSecret
Check 'Admin sign-in (password and TOTP)' 200 (Invoke-Api GET '/api/auth/me' $null $admin)
$officerAccount = Get-RoleToken 'Officer' 'SecurityOfficer' $OfficerSecretsFile 'OFFICER' $admin
$auditorAccount = Get-RoleToken 'Auditor' 'Auditor' $AuditorSecretsFile 'AUDITOR' $admin
$officer = $officerAccount.Token
$auditor = $auditorAccount.Token

$roles = [ordered]@{ Admin = $admin; Officer = $officer; Auditor = $auditor }
$meRoles = @{ Admin = 'Admin'; Officer = 'SecurityOfficer'; Auditor = 'Auditor' }
foreach ($role in $roles.Keys) {
    $me = Invoke-Api GET '/api/auth/me' $null $roles[$role]
    Check-True "$role token has role $($meRoles[$role])" ($me.Data.role -eq $meRoles[$role]) "role=$($me.Data.role)"
}

# A SIM for the status and tier checks, created by the Officer.
$n = $runId % 100000000
$sim = Invoke-Api POST '/api/sims' @{ phoneNumber = '+2547' + $n.ToString('00000000'); iccid = '8925403' + $runId.ToString('000000000000'); operator = 'Safaricom'; planType = 'Postpaid'; monthlyCost = 1000 } $officer
Check 'Officer creates the test SIM' 201 $sim
$simId = $sim.Data.id

# --- Matrix ------------------------------------------------------------------
$matrix = @(
    @{ Group = 'org read';      Expect = @{ Admin = 403; Officer = 200; Auditor = 200 } },
    @{ Group = 'org write';     Expect = @{ Admin = 403; Officer = 201; Auditor = 403 } },
    @{ Group = 'status change'; Expect = @{ Admin = 403; Officer = 200; Auditor = 403 } },
    @{ Group = 'tier change';   Expect = @{ Admin = 403; Officer = 200; Auditor = 403 } },
    @{ Group = 'users';         Expect = @{ Admin = 200; Officer = 403; Auditor = 403 } },
    @{ Group = 'audit logs';    Expect = @{ Admin = 200; Officer = 403; Auditor = 200 } },
    @{ Group = 'auth events';   Expect = @{ Admin = 200; Officer = 403; Auditor = 200 } },
    @{ Group = 'health';        Expect = @{ Admin = 200; Officer = 200; Auditor = 200 } }
)

$createdDepartments = @()
foreach ($row in $matrix) {
    foreach ($role in $roles.Keys) {
        $token = $roles[$role]
        switch ($row.Group) {
            'org read'      { $label = 'GET /api/sims'; $r = Invoke-Api GET '/api/sims?pageSize=1' $null $token }
            'org write'     {
                $label = 'POST /api/departments'
                $r = Invoke-Api POST '/api/departments' @{ name = "Roles $role $runId" } $token
                if ($r.Status -eq 201) { $createdDepartments += $r.Data.id }
            }
            'status change' { $label = 'PATCH /api/sims/{id}/status'; $r = Invoke-Api PATCH "/api/sims/$simId/status" @{ status = 'inactive' } $token }
            'tier change'   { $label = 'PATCH /api/sims/{id}/tier'; $r = Invoke-Api PATCH "/api/sims/$simId/tier" @{ tier = 'field' } $token }
            'users'         { $label = 'GET /api/users'; $r = Invoke-Api GET '/api/users' $null $token }
            'audit logs'    { $label = 'GET /api/audit-logs'; $r = Invoke-Api GET '/api/audit-logs?pageSize=1' $null $token }
            'auth events'   { $label = 'GET /api/auth/events'; $r = Invoke-Api GET '/api/auth/events?pageSize=1' $null $token }
            'health'        { $label = 'GET /health/database'; $r = Invoke-Api GET '/health/database' $null $token }
        }
        Check "[$($row.Group)] $role $label" $row.Expect[$role] $r
    }
}

# --- Tier endpoint -----------------------------------------------------------
$tierPath = "/api/sims/$simId/tier"
$toManager = Invoke-Api PATCH $tierPath @{ tier = '  MANAGER ' } $officer
Check 'Officer changes tier field to manager (trimmed, any case)' 200 $toManager
Check-True 'Tier response has simId, tier and previousTier' (($toManager.Data.simId -eq $simId) -and ($toManager.Data.tier -eq 'manager') -and ($toManager.Data.previousTier -eq 'field'))
$same = Invoke-Api PATCH $tierPath @{ tier = 'manager' } $officer
Check 'Officer sets the same tier again' 200 $same
Check 'Unknown tier' 400 (Invoke-Api PATCH $tierPath @{ tier = 'gold' } $officer)
Check 'Missing tier' 400 (Invoke-Api PATCH $tierPath @{} $officer)
Check 'Tier for unknown SIM' 404 (Invoke-Api PATCH '/api/sims/999999999/tier' @{ tier = 'field' } $officer)

# --- Audit log filtering by role ---------------------------------------------
$tierRows = Invoke-Api GET "/api/audit-logs?action=sim.tier_changed&simId=$simId&pageSize=100" $null $auditor
Check 'Auditor GET tier audit rows' 200 $tierRows "rows=$($tierRows.Data.total)"
$items = @($tierRows.Data.items)
Check-True 'Two tier changes audited (same tier writes none), newest first' (($items.Count -eq 2) -and ($items[0].oldValues.tier -eq 'field') -and ($items[0].newValues.tier -eq 'manager') -and ($null -eq $items[1].oldValues.tier) -and ($items[1].newValues.tier -eq 'field'))
Check-True 'Tier audit rows: organisation, SecurityOfficer, the Officer email' (@($items | Where-Object { $_.category -ne 'organisation' -or $_.actorRole -ne 'SecurityOfficer' -or $_.actorEmail -ne $officerAccount.Email }).Count -eq 0)

$adminTier = Invoke-Api GET "/api/audit-logs?action=sim.tier_changed&simId=$simId" $null $admin
Check-True 'Admin sees no organisation rows (tier changes hidden)' (($adminTier.Status -eq 200) -and ($adminTier.Data.total -eq 0))
$adminAll = Invoke-Api GET '/api/audit-logs?pageSize=100' $null $admin
Check-True 'Admin audit rows are all system rows' (($adminAll.Data.total -gt 0) -and (@($adminAll.Data.items | Where-Object { $_.category -ne 'system' }).Count -eq 0)) "rows=$($adminAll.Data.total)"
$auditorAll = Invoke-Api GET '/api/audit-logs?pageSize=100' $null $auditor
$categories = @($auditorAll.Data.items | ForEach-Object { $_.category } | Sort-Object -Unique)
Check-True 'Auditor sees system and organisation rows' (($categories -contains 'system') -and ($categories -contains 'organisation')) "categories=$($categories -join ',')"
Check-True 'Auditor sees more rows than Admin' ($auditorAll.Data.total -gt $adminAll.Data.total) "auditor=$($auditorAll.Data.total) admin=$($adminAll.Data.total)"

# --- Cleanup -----------------------------------------------------------------
Check 'Officer deletes the test SIM' 204 (Invoke-Api DELETE "/api/sims/$simId" $null $officer)
foreach ($id in $createdDepartments) {
    Check 'Officer deletes the test department' 204 (Invoke-Api DELETE "/api/departments/$id" $null $officer)
}

Write-Results
