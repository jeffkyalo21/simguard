<#
.SYNOPSIS
    Smoke test of the emergency Admin recovery option (SimGuard:Seed:RecoverAdmin).

.DESCRIPTION
    Recovery runs only while the API starts, so this script starts and stops
    the API itself, several times, against ONE empty throwaway database. It
    never touches any other database. Checks, in order:

      1. variable missing   an Admin that was disabled and demoted stays that way
      2. RecoverAdmin=false the same
      3. RecoverAdmin=true without a password: nothing changes
      4. RecoverAdmin=true  the existing Admin is repaired (enabled, Admin role,
                            MFA reset, must change password, new password works,
                            old password does not) and a normal Security Officer
                            is untouched
      5. RecoverAdmin=true  an Admin that does not exist is created
      6. variable removed   nothing happens again, no new audit entry
      and "admin.recovered" system audit entries exist without any password.

    The API runs in Development here only so the script can set test passwords
    when it creates accounts. Recovery itself behaves the same in any environment.
    Passwords, tokens and authenticator secrets are never printed.

    Needs the built API (dotnet build or publish) and an empty database that the
    API may migrate. Create and drop that database yourself, for example a
    sg_check_recovery database in a throwaway postgres.

.EXAMPLE
    $env:SIMGUARD_RECOVERY_SMOKE_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=sg_check_recovery;Username=...;Password=...'
    powershell -NoProfile -File backend/docs/recovery-smoke.ps1 -ApiDll C:\scratch\out\SimGuard.Api.dll -Port 5448
#>
param(
    [Parameter(Mandatory)][string]$ApiDll,
    [int]$Port = 5448
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$connection = $env:SIMGUARD_RECOVERY_SMOKE_CONNECTION
if (-not $connection) { throw 'Set SIMGUARD_RECOVERY_SMOKE_CONNECTION to the connection string of an empty throwaway database.' }
if (-not (Test-Path $ApiDll)) { throw "ApiDll not found: $ApiDll" }

$BaseUrl = "http://127.0.0.1:$Port"
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$script:results = New-Object System.Collections.Generic.List[object]
$script:api = $null
$script:logFile = Join-Path $env:TEMP "simguard-recovery-smoke-$Port.log"

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null)

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
    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) { $data = $text | ConvertFrom-Json }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Text = $text }
}

function Check-True {
    param([string]$Step, [bool]$Condition, [string]$Note = '')
    $script:results.Add([pscustomobject]@{ Step = $Step; Expected = 'true'; Actual = "$Condition".ToLowerInvariant(); Result = if ($Condition) { 'PASS' } else { 'FAIL' }; Note = $Note })
}

function Write-Results {
    $script:results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "$($script:results.Count) checks, $($failed.Count) failed."
    if ($failed.Count -gt 0) { exit 1 }
}

# RFC 6238 TOTP (SHA-1, 6 digits, 30 seconds), same as the other smoke scripts.
function Get-Totp {
    param([string]$Base32Secret)

    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $clean = $Base32Secret.ToUpperInvariant().Replace(' ', '').TrimEnd('=')
    $bits = ''
    foreach ($c in $clean.ToCharArray()) { $bits += [Convert]::ToString($alphabet.IndexOf($c), 2).PadLeft(5, '0') }
    $keyBytes = New-Object byte[] ([math]::Floor($bits.Length / 8))
    for ($i = 0; $i -lt $keyBytes.Length; $i++) { $keyBytes[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2) }
    $counter = [long][math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
    $counterBytes = [BitConverter]::GetBytes($counter)
    [Array]::Reverse($counterBytes)
    $hash = [System.Security.Cryptography.HMACSHA1]::new($keyBytes).ComputeHash($counterBytes)
    $offset = $hash[$hash.Length - 1] -band 0x0F
    $binary = (([int]$hash[$offset] -band 0x7F) -shl 24) -bor ([int]$hash[$offset + 1] -shl 16) -bor ([int]$hash[$offset + 2] -shl 8) -bor [int]$hash[$offset + 3]
    return ($binary % 1000000).ToString('000000')
}

function New-TestPassword {
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return 'Sg-' + [Convert]::ToBase64String($bytes).Replace('+', 'x').Replace('/', 'y')
}

function Stop-Api {
    if ($script:api -and -not $script:api.HasExited) {
        Stop-Process -Id $script:api.Id -Force -ErrorAction SilentlyContinue
        $script:api.WaitForExit(10000) | Out-Null
    }
    $script:api = $null
}

# Starts the API with exactly these SimGuard__Seed__* variables (others cleared).
function Start-Api {
    param([hashtable]$Seed = @{})

    Stop-Api
    foreach ($name in 'AdminEmail', 'AdminPassword', 'RecoverAdmin', 'OfficerEmail', 'OfficerPassword') {
        Remove-Item "Env:SimGuard__Seed__$name" -ErrorAction SilentlyContinue
    }
    foreach ($key in $Seed.Keys) { Set-Item "Env:SimGuard__Seed__$key" $Seed[$key] }

    # Development also reads the developer's user-secrets. An empty command-line
    # value overrides them (an empty environment variable cannot exist on Windows),
    # so a setting this step does not give is really missing.
    $blank = @()
    foreach ($name in 'AdminEmail', 'AdminPassword', 'RecoverAdmin', 'OfficerEmail', 'OfficerPassword') {
        if (-not $Seed.ContainsKey($name)) { $blank += "--SimGuard:Seed:$name=" }
    }

    $env:ConnectionStrings__DefaultConnection = $connection
    $env:Database__MigrateOnStartup = 'true'
    $env:Jwt__Key = $script:jwtKey
    $env:Brevo__ApiKey = ''
    $env:RateLimiting__Auth__PermitLimit = '1000'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Scoring__Enabled = 'false'

    $script:api = Start-Process dotnet -ArgumentList (@("`"$ApiDll`"", '--urls', $BaseUrl) + $blank) -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $script:logFile -RedirectStandardError "$($script:logFile).err"
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        try { if ((Invoke-Api GET '/health').Status -eq 200) { return } } catch { }
    }
    throw "The API did not start. See $($script:logFile)."
}

function Get-Log { if (Test-Path $script:logFile) { Get-Content -Raw $script:logFile } else { '' } }

function Get-Token {
    param([string]$Email, [string]$Password, [string]$TotpSecret)
    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password }
    if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaRequired') { return $null }
    $verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login.Data.mfaToken; code = (Get-Totp $TotpSecret) }
    if ($verify.Status -ne 200) { return $null }
    return $verify.Data.accessToken
}

# First sign-in of a new or reset account: MFA enrolment, then the forced password change.
function Complete-FirstSignIn {
    param([string]$Email, [string]$Password)

    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $Password }
    if ($login.Data.status -ne 'mfaEnrollmentRequired') { throw "First sign-in of $Email failed (status $($login.Status))." }
    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    if ($start.Data.authenticatorUri -notmatch 'secret=([A-Z2-7]+)') { throw 'No authenticator secret.' }
    $secret = $Matches[1]
    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $secret) } $login.Data.mfaToken
    $newPassword = New-TestPassword
    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $Password; newPassword = $newPassword } $confirm.Data.accessToken
    if ($changed.Status -ne 200) { throw "Password change of $Email failed (status $($changed.Status))." }
    [pscustomobject]@{ Email = $Email; Password = $newPassword; Totp = $secret; Token = $changed.Data.accessToken }
}

function Get-UserByEmail {
    param([string]$Token, [string]$Email)
    $list = Invoke-Api GET '/api/users?pageSize=100' $null $Token
    @($list.Data.items | Where-Object { $_.email -eq $Email })[0]
}

function Get-RecoveredAudit {
    param([string]$Token)
    $r = Invoke-Api GET '/api/audit-logs?action=admin.recovered&pageSize=50' $null $Token
    [pscustomobject]@{ Count = [int]$r.Data.total; Text = $r.Text }
}

try {
    $script:jwtKey = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
    $seedA = New-TestPassword
    $seedO = New-TestPassword
    $emailA = 'recover.admin@recovery.test'
    $emailB = 'second.admin@recovery.test'
    $emailC = 'missing.admin@recovery.test'
    $emailO = 'plain.officer@recovery.test'
    $recoveryPassword = New-TestPassword

    # --- Setup: Admin A (seeded), Security Officer, second Admin B -------------
    Start-Api @{ AdminEmail = $emailA; AdminPassword = $seedA; OfficerEmail = $emailO; OfficerPassword = $seedO }
    $a = Complete-FirstSignIn $emailA $seedA
    $o = Complete-FirstSignIn $emailO $seedO
    $bTemp = New-TestPassword
    $created = Invoke-Api POST '/api/users' @{ email = $emailB; name = 'Second Admin'; role = 'Admin'; temporaryPassword = $bTemp } $a.Token
    Check-True 'Setup: Admin A creates the second Admin B' ($created.Status -eq 201)
    $b = Complete-FirstSignIn $emailB $bTemp

    $aRow = Get-UserByEmail $b.Token $emailA
    $demote = Invoke-Api PUT "/api/users/$($aRow.id)/role" @{ role = 'Auditor' } $b.Token
    $disable = Invoke-Api POST "/api/users/$($aRow.id)/disable" $null $b.Token
    Check-True 'Setup: B demotes A to Auditor and disables A' (($demote.Status -eq 200) -and ($disable.Status -eq 200))
    $officerBefore = (Get-UserByEmail $b.Token $emailO) | ConvertTo-Json -Compress

    function Assert-StillDamaged {
        param([string]$Label)
        $bToken = Get-Token $emailB $b.Password $b.Totp
        $row = Get-UserByEmail $bToken $emailA
        Check-True "$Label : A stays disabled and Auditor" (($row.isDisabled -eq $true) -and ($row.role -eq 'Auditor'))
        $login = Invoke-Api POST '/api/auth/login' @{ email = $emailA; password = $recoveryPassword }
        Check-True "$Label : the recovery password does not work" ($login.Status -eq 401)
        Check-True "$Label : no admin.recovered audit entry" ((Get-RecoveredAudit $bToken).Count -eq 0)
        Check-True "$Label : the Officer record is unchanged" ((((Get-UserByEmail $bToken $emailO) | ConvertTo-Json -Compress)) -eq $officerBefore)
    }

    # --- 1. variable missing ----------------------------------------------------
    Start-Api @{ AdminEmail = $emailA; AdminPassword = $recoveryPassword }
    Assert-StillDamaged 'Variable missing'

    # --- 2. RecoverAdmin=false --------------------------------------------------
    Start-Api @{ AdminEmail = $emailA; AdminPassword = $recoveryPassword; RecoverAdmin = 'false' }
    Assert-StillDamaged 'RecoverAdmin=false'

    # --- 3. RecoverAdmin=true, no password --------------------------------------
    Start-Api @{ AdminEmail = $emailA; RecoverAdmin = 'true' }
    Assert-StillDamaged 'RecoverAdmin=true without a password'
    Check-True 'RecoverAdmin=true without a password: the log says nothing was changed' ((Get-Log) -match 'Nothing was changed')

    # --- 4. RecoverAdmin=true, existing Admin -----------------------------------
    Start-Api @{ AdminEmail = $emailA; AdminPassword = $recoveryPassword; RecoverAdmin = 'true' }
    Check-True 'Repair: the log warns to remove the variable' ((Get-Log) -match 'Remove SimGuard__Seed__RecoverAdmin')
    Check-True 'Repair: the password is not in the log' (-not ((Get-Log) -like "*$recoveryPassword*"))
    $oldLogin = Invoke-Api POST '/api/auth/login' @{ email = $emailA; password = $a.Password }
    Check-True 'Repair: the old password no longer works' ($oldLogin.Status -eq 401)
    $bToken = Get-Token $emailB $b.Password $b.Totp
    $row = Get-UserByEmail $bToken $emailA
    Check-True 'Repair: A is an enabled Admin with MFA reset and a password change due' (($row.role -eq 'Admin') -and ($row.isDisabled -eq $false) -and ($row.mfaEnabled -eq $false) -and ($row.mustChangePassword -eq $true) -and (-not $row.lockedOutUntil))
    Check-True 'Repair: the Officer record is unchanged' ((((Get-UserByEmail $bToken $emailO) | ConvertTo-Json -Compress)) -eq $officerBefore)
    Check-True 'Repair: the Officer still signs in with the same credentials' ([bool](Get-Token $emailO $o.Password $o.Totp))
    $a2 = Complete-FirstSignIn $emailA $recoveryPassword
    $me = Invoke-Api GET '/api/auth/me' $null $a2.Token
    Check-True 'Repair: A signs in, enrols MFA again, changes the password and is an Admin' (($me.Status -eq 200) -and ($me.Data.role -eq 'Admin'))
    $audit = Get-RecoveredAudit $bToken
    Check-True 'Repair: one admin.recovered system entry, no password in it' (($audit.Count -eq 1) -and ($audit.Text -match 'repaired') -and (-not ($audit.Text -like "*$recoveryPassword*")))

    # --- 5. RecoverAdmin=true, missing Admin ------------------------------------
    $createdPassword = New-TestPassword
    Start-Api @{ AdminEmail = $emailC; AdminPassword = $createdPassword; RecoverAdmin = 'true' }
    $bToken = Get-Token $emailB $b.Password $b.Totp
    $cRow = Get-UserByEmail $bToken $emailC
    Check-True 'Create: the missing Admin C exists with the Admin role' (($cRow.role -eq 'Admin') -and ($cRow.isDisabled -eq $false))
    $cLogin = Invoke-Api POST '/api/auth/login' @{ email = $emailC; password = $createdPassword }
    Check-True 'Create: C can start the first sign-in with the given password' ($cLogin.Data.status -eq 'mfaEnrollmentRequired')
    Check-True 'Create: A (not named) is untouched and still signs in' ([bool](Get-Token $emailA $a2.Password $a2.Totp))
    $audit = Get-RecoveredAudit $bToken
    Check-True 'Create: a second admin.recovered entry, no password in it' (($audit.Count -eq 2) -and ($audit.Text -match 'created') -and (-not ($audit.Text -like "*$createdPassword*")))

    # --- 6. variable removed ----------------------------------------------------
    Start-Api @{ AdminEmail = $emailC; AdminPassword = (New-TestPassword) }
    $bToken = Get-Token $emailB $b.Password $b.Totp
    Check-True 'Removed: no new audit entry' ((Get-RecoveredAudit $bToken).Count -eq 2)
    Check-True 'Removed: C still has the password it was created with' ((Invoke-Api POST '/api/auth/login' @{ email = $emailC; password = $createdPassword }).Data.status -eq 'mfaEnrollmentRequired')
}
finally {
    Stop-Api
    foreach ($name in 'AdminEmail', 'AdminPassword', 'RecoverAdmin', 'OfficerEmail', 'OfficerPassword') {
        Remove-Item "Env:SimGuard__Seed__$name" -ErrorAction SilentlyContinue
    }
    Remove-Item Env:ConnectionStrings__DefaultConnection, Env:Jwt__Key -ErrorAction SilentlyContinue
    Remove-Item $script:logFile, "$($script:logFile).err" -ErrorAction SilentlyContinue
}

Write-Results
