<#
.SYNOPSIS
    End-to-end smoke test of SimGuard authentication and authorization.

.DESCRIPTION
    Runs the full flow against a running API and prints the real status code
    of every step next to the expected one:
      seed Admin login, MFA enrolment, forced password change, MFA verify,
      recovery code, Security Officer and Auditor creation, 401 and 403 on
      write endpoints, last-Admin protection, disable and enable, MFA reset,
      lockout, auth events and CORS.

    Passwords, tokens and MFA secrets are never printed. The seeded Admin's
    new password, TOTP secret and recovery codes are written to
    -AdminSecretsFile (outside the repository) so the Development Admin
    account stays usable after the run.

    The sign-in endpoints are rate limited. Start the API with a higher limit
    for the main run (RateLimiting__Auth__PermitLimit=1000), then run again
    with -RateLimitOnly against an API that uses the default limit.

.EXAMPLE
    $env:SIMGUARD_SMOKE_ADMIN_EMAIL = 'admin@simguard.local'
    $env:SIMGUARD_SMOKE_ADMIN_PASSWORD = '<the seed password>'
    powershell -NoProfile -File backend/docs/auth-smoke.ps1
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AllowedOrigin = 'http://localhost:3000',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [switch]$RateLimitOnly
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
        [hashtable]$Headers = @{}
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$BaseUrl$Path")

    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }

    foreach ($key in $Headers.Keys) {
        [void]$request.Headers.TryAddWithoutValidation($key, [string]$Headers[$key])
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 5 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
    }

    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null

    if ($text -and $text.TrimStart().StartsWith('{')) {
        $data = $text | ConvertFrom-Json
    }

    [pscustomobject]@{
        Status   = [int]$response.StatusCode
        Data     = $data
        Response = $response
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

# RFC 6238 TOTP (SHA-1, 6 digits, 30 seconds), as used by Identity's
# authenticator token provider.
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
    # PowerShell keeps byte -shl byte as a byte, so widen to int first.
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

# Signs in a freshly created account: login, MFA enrolment, password change.
# Returns the final access token and the account's TOTP secret.
function Complete-FirstSignIn {
    param([string]$Label, [string]$Email, [string]$TemporaryPassword, [string]$NewPassword)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $TemporaryPassword }
    Check "$Label login (temporary password)" 200 $login "status=$($login.Data.status)"

    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    Check "$Label MFA enroll start" 200 $start
    $secret = Get-SecretFromUri $start.Data.authenticatorUri

    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $secret) } $login.Data.mfaToken
    Check "$Label MFA enroll confirm" 200 $confirm "recoveryCodes=$($confirm.Data.recoveryCodes.Count) mustChangePassword=$($confirm.Data.mustChangePassword)"

    $blocked = Invoke-Api GET '/api/sims' $null $confirm.Data.accessToken
    Check "$Label GET /api/sims before password change" 403 $blocked

    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $TemporaryPassword; newPassword = $NewPassword } $confirm.Data.accessToken
    Check "$Label change password" 200 $changed

    [pscustomobject]@{
        Token         = $changed.Data.accessToken
        OldToken      = $confirm.Data.accessToken
        Secret        = $secret
        RecoveryCodes = $confirm.Data.recoveryCodes
    }
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    Write-Host "$($script:results.Count - $failed) passed, $failed failed"
    if ($failed -gt 0) { exit 1 }
}

# -----------------------------------------------------------------------------
if ($RateLimitOnly) {
    $statuses = @()
    for ($i = 1; $i -le 12; $i++) {
        $statuses += (Invoke-Api POST '/api/auth/login' @{ email = 'nobody@simguard.local'; password = 'wrong-password-123' }).Status
    }
    Write-Host "12 rapid logins: $($statuses -join ', ')"
    Check-True 'Rate limiter returns 429 on the sign-in endpoints' ($statuses -contains 429) "statuses: $($statuses -join ',')"
    Write-Results
    return
}

$adminEmail = $env:SIMGUARD_SMOKE_ADMIN_EMAIL
$adminPassword = $env:SIMGUARD_SMOKE_ADMIN_PASSWORD
if (-not $adminEmail -or -not $adminPassword) {
    throw 'Set SIMGUARD_SMOKE_ADMIN_EMAIL and SIMGUARD_SMOKE_ADMIN_PASSWORD (the seed Admin credentials).'
}

$runId = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

# --- Anonymous ---------------------------------------------------------------
Check 'GET / (anonymous)' 200 (Invoke-Api GET '/')
Check 'GET /health/database without token' 401 (Invoke-Api GET '/health/database')
Check 'GET /api/sims without token' 401 (Invoke-Api GET '/api/sims')
Check 'POST /api/departments without token' 401 (Invoke-Api POST '/api/departments' @{ name = "Smoke $runId" })
Check 'GET /api/auth/me without token' 401 (Invoke-Api GET '/api/auth/me')
Check 'POST /api/auth/login wrong password' 401 (Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = 'definitely-not-it-123' })
Check 'POST /api/auth/login unknown email' 401 (Invoke-Api POST '/api/auth/login' @{ email = "nobody-$runId@simguard.local"; password = 'definitely-not-it-123' })
Check 'POST /api/auth/login empty body' 400 (Invoke-Api POST '/api/auth/login' @{})

# --- Seed Admin: first sign-in ----------------------------------------------
$login = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminPassword }
Check 'Admin login (seed password)' 200 $login "status=$($login.Data.status)"
Check-True 'Admin login asks for MFA enrolment' ($login.Data.status -eq 'mfaEnrollmentRequired')

$enrollToken = $login.Data.mfaToken
Check 'Enrolment token rejected by GET /api/sims' 401 (Invoke-Api GET '/api/sims' $null $enrollToken)
Check 'Enrolment token rejected by GET /api/auth/me' 401 (Invoke-Api GET '/api/auth/me' $null $enrollToken)
Check 'Enroll start without token' 401 (Invoke-Api POST '/api/auth/mfa/enroll/start')

$start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $enrollToken
Check 'Admin MFA enroll start' 200 $start "uri starts with otpauth://totp/: $($start.Data.authenticatorUri.StartsWith('otpauth://totp/'))"
$adminSecret = Get-SecretFromUri $start.Data.authenticatorUri

$wrongCode = if ((Get-Totp $adminSecret) -eq '000000') { '111111' } else { '000000' }
Check 'Admin MFA enroll confirm wrong code' 400 (Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = $wrongCode } $enrollToken)

$confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $adminSecret) } $enrollToken
Check 'Admin MFA enroll confirm' 200 $confirm "recoveryCodes=$($confirm.Data.recoveryCodes.Count) mustChangePassword=$($confirm.Data.mustChangePassword)"
Check-True 'Enroll confirm returns 10 recovery codes' ($confirm.Data.recoveryCodes.Count -eq 10)
$adminRecoveryCodes = @($confirm.Data.recoveryCodes)

$pendingToken = $confirm.Data.accessToken
Check 'GET /api/sims before forced password change' 403 (Invoke-Api GET '/api/sims' $null $pendingToken)
$me = Invoke-Api GET '/api/auth/me' $null $pendingToken
Check 'GET /api/auth/me before password change' 200 $me "role=$($me.Data.role) mfaEnabled=$($me.Data.mfaEnabled) mustChangePassword=$($me.Data.mustChangePassword)"

Check 'Change password too short' 400 (Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $adminPassword; newPassword = 'short' } $pendingToken)

$adminNewPassword = New-TestPassword
$changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $adminPassword; newPassword = $adminNewPassword } $pendingToken
Check 'Admin change password' 200 $changed

# Saved so the Development Admin stays usable after the run.
@{ email = $adminEmail; password = $adminNewPassword; totpSecret = $adminSecret; recoveryCodes = $adminRecoveryCodes } |
    ConvertTo-Json | Set-Content -Path $AdminSecretsFile -Encoding UTF8

Check 'Old token after password change' 401 (Invoke-Api GET '/api/sims' $null $pendingToken)

# --- Admin: normal sign-in with MFA verify -----------------------------------
$login2 = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminNewPassword }
Check 'Admin login (new password)' 200 $login2 "status=$($login2.Data.status)"
Check-True 'Admin login asks for MFA code' ($login2.Data.status -eq 'mfaRequired')
Check 'MFA challenge token rejected by GET /api/sims' 401 (Invoke-Api GET '/api/sims' $null $login2.Data.mfaToken)
Check 'MFA verify wrong code' 401 (Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login2.Data.mfaToken; code = (Get-Totp $adminSecret -OffsetSteps 20) })
$verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login2.Data.mfaToken; code = (Get-Totp $adminSecret) }
Check 'MFA verify TOTP' 200 $verify "mustChangePassword=$($verify.Data.mustChangePassword)"
$admin = $verify.Data.accessToken

Check 'GET /api/users as Admin' 200 (Invoke-Api GET '/api/users' $null $admin)
Check 'GET /api/sims as Admin (organisation data)' 403 (Invoke-Api GET '/api/sims' $null $admin)
Check 'GET /health/database as Admin' 200 (Invoke-Api GET '/health/database' $null $admin)

# Recovery code: works once.
$login3 = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminNewPassword }
$recovery = $adminRecoveryCodes[0]
Check 'MFA verify recovery code' 200 (Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login3.Data.mfaToken; code = $recovery })
$login4 = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminNewPassword }
Check 'MFA verify same recovery code again' 401 (Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login4.Data.mfaToken; code = $recovery })
@{ email = $adminEmail; password = $adminNewPassword; totpSecret = $adminSecret; recoveryCodes = @($adminRecoveryCodes | Select-Object -Skip 1) } |
    ConvertTo-Json | Set-Content -Path $AdminSecretsFile -Encoding UTF8

# --- User administration -----------------------------------------------------
$officerEmail = "officer-$runId@simguard.local"
$auditorEmail = "auditor-$runId@simguard.local"
$officerTemp = New-TestPassword
$auditorTemp = New-TestPassword

$officer = Invoke-Api POST '/api/users' @{ email = $officerEmail; name = 'Smoke Officer'; role = 'SecurityOfficer'; temporaryPassword = $officerTemp } $admin
Check 'Create Security Officer' 201 $officer "role=$($officer.Data.role) mustChangePassword=$($officer.Data.mustChangePassword)"
$auditor = Invoke-Api POST '/api/users' @{ email = $auditorEmail; name = 'Smoke Auditor'; role = 'Auditor'; temporaryPassword = $auditorTemp } $admin
Check 'Create Auditor' 201 $auditor "role=$($auditor.Data.role)"
Check 'Create duplicate email' 409 (Invoke-Api POST '/api/users' @{ email = $auditorEmail; name = 'Again'; role = 'Auditor'; temporaryPassword = (New-TestPassword) } $admin)
Check 'Create with unknown role' 400 (Invoke-Api POST '/api/users' @{ email = "x-$runId@simguard.local"; name = 'X'; role = 'Root'; temporaryPassword = (New-TestPassword) } $admin)
Check 'Create with short password' 400 (Invoke-Api POST '/api/users' @{ email = "y-$runId@simguard.local"; name = 'Y'; role = 'Auditor'; temporaryPassword = 'short' } $admin)
Check 'GET /api/users as Admin' 200 (Invoke-Api GET '/api/users' $null $admin)

$officerPassword = New-TestPassword
$auditorPassword = New-TestPassword
$officerSession = Complete-FirstSignIn 'Officer' $officerEmail $officerTemp $officerPassword
$auditorSession = Complete-FirstSignIn 'Auditor' $auditorEmail $auditorTemp $auditorPassword
$officerToken = $officerSession.Token
$auditorToken = $auditorSession.Token

# --- Role matrix on organisation endpoints ----------------------------------
# Admin is the SimGuard operator: no organisation reads or writes. The
# Security Officer manages the organisation; the Auditor only reads.
Check 'Auditor GET /api/sims' 200 (Invoke-Api GET '/api/sims' $null $auditorToken)
Check 'Officer GET /api/sims' 200 (Invoke-Api GET '/api/sims' $null $officerToken)
Check 'Auditor POST /api/departments' 403 (Invoke-Api POST '/api/departments' @{ name = "Smoke $runId" } $auditorToken)
Check 'Admin POST /api/departments' 403 (Invoke-Api POST '/api/departments' @{ name = "Smoke $runId" } $admin)
$department = Invoke-Api POST '/api/departments' @{ name = "Smoke $runId" } $officerToken
Check 'Officer POST /api/departments' 201 $department

$phone = '+2547' + ($runId % 100000000).ToString('00000000')
$simBody = @{ phoneNumber = $phone; iccid = "8925499$runId"; operator = 'Safaricom'; planType = 'Postpaid'; monthlyCost = 10 }
Check 'Admin POST /api/sims' 403 (Invoke-Api POST '/api/sims' $simBody $admin)
$sim = Invoke-Api POST '/api/sims' $simBody $officerToken
Check 'Officer POST /api/sims' 201 $sim
$simId = $sim.Data.id
$gone = Invoke-Api PATCH "/api/sims/$simId/status" @{ status = 'inactive' } $officerToken
Check-True 'PATCH /api/sims/{id}/status no longer exists (404 or 405)' (@(404, 405) -contains [int]$gone.Status) "status=$($gone.Status)"
Check 'Auditor POST /api/allocations' 403 (Invoke-Api POST '/api/allocations' @{ simId = $simId; employeeId = 1; assignedBy = 1 } $auditorToken)
Check 'Admin DELETE /api/sims/{id}' 403 (Invoke-Api DELETE "/api/sims/$simId" $null $admin)
Check 'Officer DELETE /api/sims/{id} (cleanup)' 204 (Invoke-Api DELETE "/api/sims/$simId" $null $officerToken)
Check 'Officer DELETE /api/departments/{id} (cleanup)' 204 (Invoke-Api DELETE "/api/departments/$($department.Data.id)" $null $officerToken)

Check 'Officer GET /api/users' 403 (Invoke-Api GET '/api/users' $null $officerToken)
Check 'Auditor POST /api/users' 403 (Invoke-Api POST '/api/users' @{ email = "z-$runId@simguard.local"; name = 'Z'; role = 'Admin'; temporaryPassword = (New-TestPassword) } $auditorToken)
Check 'Auditor GET /api/auth/events' 200 (Invoke-Api GET '/api/auth/events?pageSize=5' $null $auditorToken)
Check 'Officer GET /api/auth/events' 403 (Invoke-Api GET '/api/auth/events' $null $officerToken)

# --- Last Admin and self-protection ------------------------------------------
$adminId = (Invoke-Api GET '/api/auth/me' $null $admin).Data.id
Check 'Admin disables self' 409 (Invoke-Api POST "/api/users/$adminId/disable" $null $admin)
Check 'Admin demotes self' 409 (Invoke-Api PUT "/api/users/$adminId/role" @{ role = 'Auditor' } $admin)
$resendSelf = Invoke-Api POST "/api/users/$adminId/signin-email" $null $admin
Check 'Admin resends own sign-in email' 409 $resendSelf
Check-True 'Own-account resend says why' ([string]$resendSelf.Data.detail -like '*your own account*') ([string]$resendSelf.Data.detail)

# The system is never left with no active Admin. Reduce it to one active Admin
# (a second Admin disables the seed Admin) and check nobody can remove the
# last one. A caller who is not the target is itself an active Admin, so
# sequentially the last-Admin guard is reached through the own-account rule;
# the database lock covers concurrent calls. The seed Admin is enabled again
# afterwards and the second Admin is disabled.
$secondEmail = "second-admin-$runId@simguard.local"
$secondTemp = New-TestPassword
$secondAdmin = Invoke-Api POST '/api/users' @{ email = $secondEmail; name = 'Smoke Second Admin'; role = 'Admin'; temporaryPassword = $secondTemp } $admin
Check 'Create a second Admin' 201 $secondAdmin
$secondId = $secondAdmin.Data.id
$secondSession = Complete-FirstSignIn 'Second Admin' $secondEmail $secondTemp (New-TestPassword)
$second = $secondSession.Token
Check 'Second Admin disables the seed Admin (two were active, allowed)' 200 (Invoke-Api POST "/api/users/$adminId/disable" $null $second)
$activeAdmins = @((Invoke-Api GET '/api/users?role=Admin&pageSize=100' $null $second).Data.items | Where-Object { -not $_.isDisabled })
Check-True 'Exactly one active Admin is left' ($activeAdmins.Count -eq 1) "count=$($activeAdmins.Count)"
Check 'Last active Admin cannot disable self' 409 (Invoke-Api POST "/api/users/$secondId/disable" $null $second)
Check 'Last active Admin cannot demote self' 409 (Invoke-Api PUT "/api/users/$secondId/role" @{ role = 'Auditor' } $second)
Check 'Last active Admin cannot resend own sign-in email' 409 (Invoke-Api POST "/api/users/$secondId/signin-email" $null $second)
$stillActive = @((Invoke-Api GET '/api/users?role=Admin&pageSize=100' $null $second).Data.items | Where-Object { -not $_.isDisabled })
Check-True 'Still one active Admin after the refused calls' (($stillActive.Count -eq 1) -and ($stillActive[0].id -eq $secondId)) "count=$($stillActive.Count)"
Check 'Disabled seed Admin token is rejected' 401 (Invoke-Api GET '/api/users' $null $admin)
Check 'Second Admin enables the seed Admin again' 200 (Invoke-Api POST "/api/users/$adminId/enable" $null $second)
$relogin = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminNewPassword }
Check 'Seed Admin login after enable' 200 $relogin "status=$($relogin.Data.status)"
$reverify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $relogin.Data.mfaToken; code = (Get-Totp $adminSecret -OffsetSteps 1) }
Check 'Seed Admin MFA verify after enable' 200 $reverify
$admin = $reverify.Data.accessToken
Check 'Seed Admin disables the second Admin (two active, allowed)' 200 (Invoke-Api POST "/api/users/$secondId/disable" $null $admin)

# --- Role change, disable, enable, MFA reset ---------------------------------
$auditorId = $auditor.Data.id
$officerId = $officer.Data.id

$roleChange = Invoke-Api PUT "/api/users/$officerId/role" @{ role = 'Auditor' } $admin
Check 'Change Officer role to Auditor' 200 $roleChange "role=$($roleChange.Data.role)"
Check 'Officer old token after role change' 401 (Invoke-Api GET '/api/sims' $null $officerToken)
Check 'Change role back to SecurityOfficer' 200 (Invoke-Api PUT "/api/users/$officerId/role" @{ role = 'SecurityOfficer' } $admin)

Check 'Disable Auditor' 200 (Invoke-Api POST "/api/users/$auditorId/disable" $null $admin)
Check 'Disabled Auditor token' 401 (Invoke-Api GET '/api/sims' $null $auditorToken)
Check 'Disabled Auditor login (correct password)' 401 (Invoke-Api POST '/api/auth/login' @{ email = $auditorEmail; password = $auditorPassword })
Check 'Enable Auditor' 200 (Invoke-Api POST "/api/users/$auditorId/enable" $null $admin)

$mfaReset = Invoke-Api POST "/api/users/$auditorId/mfa/reset" $null $admin
Check 'Reset Auditor MFA' 200 $mfaReset "mfaEnabled=$($mfaReset.Data.mfaEnabled)"
$afterReset = Invoke-Api POST '/api/auth/login' @{ email = $auditorEmail; password = $auditorPassword }
Check 'Auditor login after MFA reset' 200 $afterReset "status=$($afterReset.Data.status)"
Check-True 'Auditor must enrol again' ($afterReset.Data.status -eq 'mfaEnrollmentRequired')

# --- Lockout -----------------------------------------------------------------
$lockoutEmail = "lockout-$runId@simguard.local"
$lockoutPassword = New-TestPassword
$lockUser = Invoke-Api POST '/api/users' @{ email = $lockoutEmail; name = 'Smoke Lockout'; role = 'Auditor'; temporaryPassword = $lockoutPassword } $admin
Check 'Create lockout test user' 201 $lockUser
for ($i = 1; $i -le 5; $i++) {
    Check "Lockout user wrong password $i" 401 (Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = "wrong-password-$i-xyz" })
}
Check 'Lockout user correct password while locked' 401 (Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = $lockoutPassword })
$locked = Invoke-Api GET "/api/users/$($lockUser.Data.id)" $null $admin
Check-True 'Admin sees lockedOutUntil' ($null -ne $locked.Data.lockedOutUntil) "lockedOutUntil=$($locked.Data.lockedOutUntil)"
$lockEvents = Invoke-Api GET "/api/auth/events?type=lockout&userId=$($lockUser.Data.id)" $null $admin
Check 'GET /api/auth/events?type=lockout' 200 $lockEvents "total=$($lockEvents.Data.total)"
$resetPassword = New-TestPassword
Check 'Admin resets password (clears lockout)' 200 (Invoke-Api POST "/api/users/$($lockUser.Data.id)/password/reset" @{ temporaryPassword = $resetPassword } $admin)
$unlocked = Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = $resetPassword }
Check 'Login after password reset' 200 $unlocked "status=$($unlocked.Data.status)"

# Unlock: lock the account again, then clear the lockout without touching the password.
for ($i = 1; $i -le 5; $i++) {
    Check "Relock wrong password $i" 401 (Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = "wrong-again-$i-xyz" })
}
Check 'Locked account refuses the right password' 401 (Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = $resetPassword })
$unlock = Invoke-Api POST "/api/users/$($lockUser.Data.id)/unlock" $null $admin
Check 'Admin unlocks the account' 200 $unlock
Check-True 'Unlock clears lockedOutUntil' ($null -eq $unlock.Data.lockedOutUntil)
Check 'Login after unlock (same password)' 200 (Invoke-Api POST '/api/auth/login' @{ email = $lockoutEmail; password = $resetPassword })
Check 'Unlock of an unlocked account is fine' 200 (Invoke-Api POST "/api/users/$($lockUser.Data.id)/unlock" $null $admin)
Check 'Unlock of an unknown user' 404 (Invoke-Api POST '/api/users/2147483000/unlock' $null $admin)

# Invite without a password: the API generates one and emails it. No Brevo key
# here, so emailSent is false; the account exists and must change its password.
$inviteEmail = "invite-$runId@simguard.local"
$invite = Invoke-Api POST '/api/users' @{ email = $inviteEmail; name = 'Smoke Invite'; role = 'Auditor' } $admin
Check 'Invite without a password' 201 $invite
Check-True 'Invite reports emailSent false, mustChangePassword true' (($invite.Data.emailSent -eq $false) -and ($invite.Data.mustChangePassword -eq $true)) "emailSent=$($invite.Data.emailSent)"
Check-True 'Invite response carries no password' ($invite.Text -notmatch '(?i)password"\s*:\s*"') $invite.Text
$resend = Invoke-Api POST "/api/users/$($invite.Data.id)/signin-email" $null $admin
Check 'Resend sign-in email' 200 $resend
Check-True 'Resend reports emailSent false and no password' (($resend.Data.emailSent -eq $false) -and ($resend.Text -notmatch '(?i)password"\s*:\s*"')) "emailSent=$($resend.Data.emailSent)"
Check 'Resend for an unknown user' 404 (Invoke-Api POST '/api/users/2147483000/signin-email' $null $admin)
Check 'Disable the invited user' 200 (Invoke-Api POST "/api/users/$($invite.Data.id)/disable" $null $admin)
Check 'Resend for a disabled user' 409 (Invoke-Api POST "/api/users/$($invite.Data.id)/signin-email" $null $admin)

$events = Invoke-Api GET '/api/auth/events?pageSize=100' $null $admin
$types = @($events.Data.items | ForEach-Object { $_.eventType } | Sort-Object -Unique)
Check 'GET /api/auth/events as Admin' 200 $events "types: $($types -join ',')"
Check 'GET /api/auth/events bad type' 400 (Invoke-Api GET '/api/auth/events?type=nope' $null $admin)

# --- Profile: name, recovery codes, delete my account -------------------------
$delEmail = "delete-me-$runId@simguard.local"
$delTemp = New-TestPassword
$delPassword = New-TestPassword
$delUser = Invoke-Api POST '/api/users' @{ email = $delEmail; name = 'Smoke Leaver'; role = 'SecurityOfficer'; temporaryPassword = $delTemp } $admin
Check 'Create the account to delete' 201 $delUser
$delId = $delUser.Data.id
$delSession = Complete-FirstSignIn 'Leaver' $delEmail $delTemp $delPassword
$del = $delSession.Token

$renamed = Invoke-Api PUT '/api/auth/me' @{ name = 'Smoke Leaver Renamed' } $del
Check 'Update own name' 200 $renamed
Check-True 'Name changed, email and role unchanged' (($renamed.Data.name -eq 'Smoke Leaver Renamed') -and ($renamed.Data.email -eq $delEmail) -and ($renamed.Data.role -eq 'SecurityOfficer'))
Check 'Update own name with an empty name' 400 (Invoke-Api PUT '/api/auth/me' @{ name = ' ' } $del)

Check 'New recovery codes with a wrong password' 400 (Invoke-Api POST '/api/auth/mfa/recovery-codes' @{ password = 'wrong-password'; code = (Get-Totp $delSession.Secret) } $del)
Check 'New recovery codes with a wrong code' 400 (Invoke-Api POST '/api/auth/mfa/recovery-codes' @{ password = $delPassword; code = '000000' } $del)
$newCodes = Invoke-Api POST '/api/auth/mfa/recovery-codes' @{ password = $delPassword; code = (Get-Totp $delSession.Secret) } $del
Check 'New recovery codes with password and code' 200 $newCodes
Check-True 'Ten new codes, different from the old ones' (($newCodes.Data.recoveryCodes.Count -eq 10) -and (-not ($newCodes.Data.recoveryCodes | Where-Object { $delSession.RecoveryCodes -contains $_ })))

Check 'Delete account with a wrong password' 400 (Invoke-Api POST '/api/auth/me/delete' @{ password = 'wrong-password'; code = (Get-Totp $delSession.Secret) } $del)
Check 'Delete account with a wrong MFA code' 400 (Invoke-Api POST '/api/auth/me/delete' @{ password = $delPassword; code = '000000' } $del)
Check 'Delete account without a password' 400 (Invoke-Api POST '/api/auth/me/delete' @{ code = (Get-Totp $delSession.Secret) } $del)
Check 'Token still works after refused deletes' 200 (Invoke-Api GET '/api/auth/me' $null $del)

# The last active Admin cannot delete themselves. A third Admin disables the
# seed Admin, is then the only active one, and is refused.
$thirdEmail = "third-admin-$runId@simguard.local"
$thirdTemp = New-TestPassword
$thirdPassword = New-TestPassword
$thirdAdmin = Invoke-Api POST '/api/users' @{ email = $thirdEmail; name = 'Smoke Third Admin'; role = 'Admin'; temporaryPassword = $thirdTemp } $admin
Check 'Create a third Admin' 201 $thirdAdmin
$thirdSession = Complete-FirstSignIn 'Third Admin' $thirdEmail $thirdTemp $thirdPassword
$third = $thirdSession.Token
Check 'Third Admin disables the seed Admin' 200 (Invoke-Api POST "/api/users/$adminId/disable" $null $third)
$lastRefused = Invoke-Api POST '/api/auth/me/delete' @{ password = $thirdPassword; code = (Get-Totp $thirdSession.Secret) } $third
Check 'Last active Admin cannot delete their account' 409 $lastRefused
Check-True 'The refusal says why in plain words' ([string]$lastRefused.Data.detail -like '*last active Admin*') ([string]$lastRefused.Data.detail)
Check 'Last Admin token still works after the refusal' 200 (Invoke-Api GET '/api/auth/me' $null $third)
Check 'Third Admin enables the seed Admin again' 200 (Invoke-Api POST "/api/users/$adminId/enable" $null $third)
$relogin2 = Invoke-Api POST '/api/auth/login' @{ email = $adminEmail; password = $adminNewPassword }
$reverify2 = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $relogin2.Data.mfaToken; code = (Get-Totp $adminSecret -OffsetSteps 1) }
Check 'Seed Admin signs in again' 200 $reverify2
$admin = $reverify2.Data.accessToken
Check 'An Admin may delete their account when another is active' 204 (Invoke-Api POST '/api/auth/me/delete' @{ password = $thirdPassword; code = (Get-Totp $thirdSession.Secret) } $third)

# A Security Officer deletes themselves.
$gone = Invoke-Api POST '/api/auth/me/delete' @{ password = $delPassword; code = (Get-Totp $delSession.Secret) } $del
Check 'Security Officer deletes their account' 204 $gone
Check 'The old token is rejected after deletion' 401 (Invoke-Api GET '/api/auth/me' $null $del)
Check 'The old password no longer signs in' 401 (Invoke-Api POST '/api/auth/login' @{ email = $delEmail; password = $delPassword })

$deletedRows = @((Invoke-Api GET '/api/audit-logs?action=user.self_deleted&pageSize=100' $null $admin).Data.items | Where-Object { $_.actorUserId -eq $delId })
Check-True 'The audit row remains with the user id and role' (($deletedRows.Count -eq 1) -and ($deletedRows[0].actorRole -eq 'SecurityOfficer')) "rows=$($deletedRows.Count)"
$rowJson = $deletedRows | ConvertTo-Json -Depth 6 -Compress
Check-True 'The audit row has no email, password or code' (($rowJson -notmatch [regex]::Escape($delEmail)) -and ($rowJson -notmatch [regex]::Escape($delPassword))) 'checked the audit row text'
$deletedUser = (Invoke-Api GET "/api/users/$delId" $null $admin).Data
Check-True 'The user record is disabled with a placeholder name and email' (($deletedUser.isDisabled -eq $true) -and ($deletedUser.name -eq "Deleted user $delId") -and ($deletedUser.email -notlike '*delete-me*')) "name=$($deletedUser.name)"

$again = Invoke-Api POST '/api/users' @{ email = $delEmail; name = 'Smoke Leaver Again'; role = 'Auditor'; temporaryPassword = (New-TestPassword) } $admin
Check 'The same email can be invited again' 201 $again

# --- CORS --------------------------------------------------------------------
$preflightHeaders = @{ 'Origin' = $AllowedOrigin; 'Access-Control-Request-Method' = 'POST'; 'Access-Control-Request-Headers' = 'content-type,authorization' }
$allowed = Invoke-Api OPTIONS '/api/auth/login' $null $null $preflightHeaders
$allowOrigin = ($allowed.Response.Headers | Where-Object { $_.Key -eq 'Access-Control-Allow-Origin' }).Value -join ','
$allowCredentials = ($allowed.Response.Headers | Where-Object { $_.Key -eq 'Access-Control-Allow-Credentials' }).Value -join ','
Check 'CORS preflight from allowed origin' 204 $allowed "Allow-Origin=$allowOrigin Allow-Credentials=$allowCredentials"
Check-True 'CORS echoes only the allowed origin' ($allowOrigin -eq $AllowedOrigin)

$evilHeaders = @{ 'Origin' = 'http://evil.example'; 'Access-Control-Request-Method' = 'POST' }
$denied = Invoke-Api OPTIONS '/api/auth/login' $null $null $evilHeaders
$deniedOrigin = ($denied.Response.Headers | Where-Object { $_.Key -eq 'Access-Control-Allow-Origin' }).Value -join ','
Check-True 'CORS preflight from other origin has no Allow-Origin' ([string]::IsNullOrEmpty($deniedOrigin)) "status=$($denied.Status) Allow-Origin='$deniedOrigin'"

Write-Host "Admin credentials for the Development database were saved to $AdminSecretsFile"
Write-Results
