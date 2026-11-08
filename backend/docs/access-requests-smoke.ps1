<#
.SYNOPSIS
    Smoke test of the access requests (landing page "Register" form).
    Contract in access-requests.md.

.DESCRIPTION
    Runs against a running Development API on a THROWAWAY database. Checks:
      - POST /api/access-requests is open to anyone (no token), returns 202
        with the same body for a new and a repeated email, validates every
        field (400), and stores one pending request per email
      - GET /api/access-requests and PATCH .../status are Admin only
        (SecurityOfficer and Auditor get 403, no token gets 401)
      - listing and filtering by status, approve and decline, a second
        review of the same request is a 409, an unknown id is a 404
      - approving creates the account (requested role, generated password
        emailed, never returned); an existing account is left alone
      - the review writes a user.access_request_reviewed audit row that the
        Admin can read
      - -NotDevelopment (API started in another environment): the Admin
        cannot choose a password for anyone, by invite, approval or reset
      - the request form is rate limited (429) with -RateLimitOnly
      - emails: by default the API has no Brevo key and reports emailSent
        false. With -EmailStubLog <file> the API must run with Brevo__ApiKey
        set and Brevo__BaseUrl pointing at a stub that appends one JSON line
        per request ({ apiKey, body }) to that file; the script then checks
        the three emails and that the password is in the account email

    Accounts are found like imports-smoke.ps1 does. Start the API with
    RateLimiting__Auth__PermitLimit=1000 and
    RateLimiting__AccessRequests__PermitLimit=1000 for the main run. Run
    -RateLimitOnly against an API that uses the default limit (5 per hour).

.EXAMPLE
    powershell -NoProfile -File backend/docs/access-requests-smoke.ps1 -AdminSecretsFile $env:TEMP\simguard-smoke-admin.json
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [string]$OfficerSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-officer.json'),
    [string]$AuditorSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-auditor.json'),
    [switch]$RateLimitOnly,
    [switch]$NotDevelopment,
    [string]$EmailStubLog = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromMinutes(5)
$script:results = New-Object System.Collections.Generic.List[object]
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Response($response) {
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null

    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) {
        $data = $text | ConvertFrom-Json
    }

    [pscustomobject]@{
        Status      = [int]$response.StatusCode
        Data        = $data
        Text        = $text
        ContentType = if ($response.Content.Headers.ContentType) { $response.Content.Headers.ContentType.MediaType } else { $null }
    }
}

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

    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# POST /api/imports/{type} with the bytes as the multipart field "file".
function Invoke-Import {
    param(
        [string]$Type,
        [byte[]]$Bytes,
        [string]$FileName,
        [string]$Token,
        [string]$Query = 'mode=preview'
    )

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$BaseUrl/api/imports/$Type`?$Query")

    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }

    $content = New-Object System.Net.Http.MultipartFormDataContent
    $file = New-Object System.Net.Http.ByteArrayContent(, $Bytes)
    $file.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('text/csv')
    $content.Add($file, 'file', $FileName)
    $request.Content = $content

    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# CSV text from lines, CRLF line ends, UTF-8 without BOM.
function New-Csv([string[]]$Lines) {
    return $utf8.GetBytes(($Lines -join "`r`n") + "`r`n")
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

function Counts($r) {
    if ($null -eq $r.Data) { return "status=$($r.Status)" }
    $d = $r.Data
    return "status=$($d.status) total=$($d.totalRows) valid=$($d.validRows) ins=$($d.inserted) upd=$($d.updated) same=$($d.unchanged) err=$($d.errorCount) warn=$($d.warningCount)"
}

function Has-Error($r, [int]$Row, [string]$Column) {
    return @($r.Data.errors | Where-Object { $_.row -eq $Row -and $_.column -eq $Column }).Count -gt 0
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

function Get-RoleToken {
    param([string]$Label, [string]$Role, [string]$SecretsFile, [string]$AdminToken)

    if (Test-Path $SecretsFile) {
        $saved = Get-Content -Raw $SecretsFile | ConvertFrom-Json
        $token = Get-Token $Label $saved.email $saved.password $saved.totpSecret
        return [pscustomobject]@{ Token = $token; Email = $saved.email }
    }

    $email = "access-$($Role.ToLowerInvariant())-$runId@simguard.local"
    $temp = New-TestPassword
    Check "Admin creates a throwaway $Role" 201 (Invoke-Api POST '/api/users' @{ email = $email; name = "Access $Label"; role = $Role; temporaryPassword = $temp } $AdminToken)
    $session = Complete-FirstSignIn $Label $email $temp
    return [pscustomobject]@{ Token = $session.Token; Email = $email }
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 240 | Write-Host
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

# Against an API that is NOT in Development the Admin cannot choose anyone's
# password. Nothing else runs: the other checks need Development accounts.
if ($NotDevelopment) {
    $pw = New-TestPassword
    $none = Invoke-Api POST '/api/users' @{ email = "nodev-$runId@simguard.local"; name = 'No Dev'; role = 'Auditor'; temporaryPassword = $pw } $admin
    Check 'Invite with a chosen password is refused' 400 $none
    Check-True 'The error names temporaryPassword' (@($none.Data.errors.PSObject.Properties.Name) -contains 'temporaryPassword')
    Check 'Password reset by hand is refused' 400 (Invoke-Api POST '/api/users/1/password/reset' @{ temporaryPassword = $pw } $admin)
    $v = @{ name = 'Visitor nodev'; email = "visitor.nodev.$runId@example.com"; organisation = 'Org nodev'; role = 'SecurityOfficer'; note = 'Smoke test' }
    Check 'Request for the approval check' 202 (Invoke-Api POST '/api/access-requests' $v)
    $vId = @((Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin).Data.items | Where-Object { $_.email -eq $v.email.ToLowerInvariant() })[0].id
    Check 'Approval with a chosen password is refused' 400 (Invoke-Api PATCH "/api/access-requests/$vId/status" @{ status = 'approved'; temporaryPassword = $pw } $admin)
    $still = @((Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin).Data.items | Where-Object { $_.id -eq $vId })
    Check-True 'The refused approval left the request pending' ($still.Count -eq 1)
    $made = Invoke-Api POST '/api/users' @{ email = "nodev2-$runId@simguard.local"; name = 'No Dev Two'; role = 'Auditor' } $admin
    Check 'Invite without a password works' 201 $made
    Check-True 'Invite emailSent is false without a Brevo key' ($made.Data.emailSent -eq $false)
    Write-Results
    return
}

$officerAccount = Get-RoleToken 'Officer' 'SecurityOfficer' $OfficerSecretsFile $admin
$auditorAccount = Get-RoleToken 'Auditor' 'Auditor' $AuditorSecretsFile $admin
$officer = $officerAccount.Token
$auditor = $auditorAccount.Token

# --- Access requests ---------------------------------------------------------
function New-Body([string]$Tag, [string]$Role = 'SecurityOfficer') {
    return @{ name = "Visitor $Tag"; email = "visitor.$Tag.$runId@example.com"; organisation = "Org $Tag"; role = $Role; note = 'Smoke test' }
}

if ($RateLimitOnly) {
    $codes = @()
    for ($i = 1; $i -le 7; $i++) { $codes += (Invoke-Api POST '/api/access-requests' (New-Body "rl$i")).Status }
    Check-True 'Rate limit: 5 accepted, then 429' ((@($codes | Where-Object { $_ -eq 202 }).Count -eq 5) -and ($codes[5] -eq 429) -and ($codes[6] -eq 429)) ($codes -join ',')

    # A call from a trusted proxy (this script runs on loopback) is counted per
    # visitor address in X-Forwarded-For.
    $other = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$BaseUrl/api/access-requests")
    $other.Headers.Add('X-Forwarded-For', '203.0.113.77')
    $other.Content = New-Object System.Net.Http.StringContent((New-Body 'xff' | ConvertTo-Json -Compress), [System.Text.Encoding]::UTF8, 'application/json')
    Check 'Another visitor address (X-Forwarded-For) has its own limit' 202 (Read-Response ($client.SendAsync($other).GetAwaiter().GetResult()))
    Write-Results
    return
}

# Anyone can send a request.
$first = New-Body 'a'
$r = Invoke-Api POST '/api/access-requests' $first
Check 'Anonymous request is accepted' 202 $r
Check-True 'Response is { received: true } and nothing else' (($r.Data.received -eq $true) -and (@($r.Data.PSObject.Properties).Count -eq 1)) $r.Text

# The same email again (other casing) gives the same answer and stores nothing new.
$again = New-Body 'a'
$again.email = $again.email.ToUpperInvariant()
$r2 = Invoke-Api POST '/api/access-requests' $again
Check 'Repeated email is accepted' 202 $r2
Check-True 'Repeated email gets the identical response' ($r2.Text -eq $r.Text)

# Validation.
$bad = Invoke-Api POST '/api/access-requests' @{ name = ''; email = 'nope'; organisation = ''; role = 'Admin'; note = ('x' * 501) }
Check 'Invalid request' 400 $bad
$fields = @($bad.Data.errors.PSObject.Properties.Name)
Check-True 'Errors name name, email, organisation, role and note' (($fields -contains 'name') -and ($fields -contains 'email') -and ($fields -contains 'organisation') -and ($fields -contains 'role') -and ($fields -contains 'note')) ($fields -join ',')
Check 'Role Admin cannot be requested' 400 (Invoke-Api POST '/api/access-requests' (New-Body 'adminrole' 'Admin'))
Check 'Role is case insensitive' 202 (Invoke-Api POST '/api/access-requests' (New-Body 'b' 'auditor'))

# Only the Admin reads and reviews.
foreach ($pair in @(@('Officer', $officer), @('Auditor', $auditor))) {
    Check "$($pair[0]) GET /api/access-requests" 403 (Invoke-Api GET '/api/access-requests' $null $pair[1])
    Check "$($pair[0]) PATCH status" 403 (Invoke-Api PATCH '/api/access-requests/1/status' @{ status = 'approved' } $pair[1])
}
Check 'No token GET' 401 (Invoke-Api GET '/api/access-requests')
Check 'No token PATCH' 401 (Invoke-Api PATCH '/api/access-requests/1/status' @{ status = 'approved' })

# Listing.
$list = Invoke-Api GET '/api/access-requests?pageSize=100' $null $admin
Check 'Admin GET /api/access-requests' 200 $list
$mine = @($list.Data.items | Where-Object { $_.email -like "visitor.*.$runId@example.com" })
Check-True 'Two requests stored (the repeated email added none), newest first' (($mine.Count -eq 2) -and ($mine[0].email -like 'visitor.b.*')) "count=$($mine.Count)"
$a = @($mine | Where-Object { $_.email -like 'visitor.a.*' })[0]
Check-True 'Stored request: lower case email, pending, requested role, note, not reviewed' (($a.email -ceq $a.email.ToLowerInvariant()) -and ($a.status -eq 'pending') -and ($a.role -eq 'SecurityOfficer') -and ($a.note -eq 'Smoke test') -and ($null -eq $a.reviewedAt))
Check 'Unknown status filter' 400 (Invoke-Api GET '/api/access-requests?status=open' $null $admin)
$pending = Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin
Check-True 'Status filter returns only pending' ((@($pending.Data.items | Where-Object { $_.status -ne 'pending' }).Count -eq 0) -and ($pending.Data.total -ge 2))

# Review.
Check 'Review with a bad status' 400 (Invoke-Api PATCH "/api/access-requests/$($a.id)/status" @{ status = 'pending' } $admin)
Check 'Review of an unknown id' 404 (Invoke-Api PATCH '/api/access-requests/2147483000/status' @{ status = 'approved' } $admin)
$done = Invoke-Api PATCH "/api/access-requests/$($a.id)/status" @{ status = 'approved' } $admin
Check 'Admin approves' 200 $done
Check-True 'Approved request has review time and reviewer' (($done.Data.status -eq 'approved') -and ($null -ne $done.Data.reviewedAt) -and ($done.Data.reviewedByEmail -eq $adminEmail)) "by=$($done.Data.reviewedByEmail)"
Check 'Second review of the same request' 409 (Invoke-Api PATCH "/api/access-requests/$($a.id)/status" @{ status = 'declined' } $admin)
$b = @($mine | Where-Object { $_.email -like 'visitor.b.*' })[0]
$declined = Invoke-Api PATCH "/api/access-requests/$($b.id)/status" @{ status = 'declined' } $admin
Check-True 'Admin declines' (($declined.Status -eq 200) -and ($declined.Data.status -eq 'declined')) "status=$($declined.Status)"

# Approval creates the account and emails a generated password. Without
# -EmailStubLog the API has no Brevo key, so emailSent is false.
$stub = [bool]$EmailStubLog
Check-True 'Approval created the account (accountCreated true) and reports emailSent' (($done.Data.accountCreated -eq $true) -and ($done.Data.emailSent -eq $stub)) "accountCreated=$($done.Data.accountCreated) emailSent=$($done.Data.emailSent)"
Check-True 'Approval response carries no password' ($done.Text -notmatch '(?i)password')
Check-True 'Decline reports whether the email was sent and creates no account' (($declined.Data.emailSent -eq $stub) -and ($null -eq $declined.Data.accountCreated)) "emailSent=$($declined.Data.emailSent)"
$c = New-Body 'c'
Check 'Third request' 202 (Invoke-Api POST '/api/access-requests' $c)
$cId = @((Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin).Data.items | Where-Object { $_.email -eq $c.email.ToLowerInvariant() })[0].id
Check 'Password longer than 128 characters' 400 (Invoke-Api PATCH "/api/access-requests/$cId/status" @{ status = 'approved'; temporaryPassword = ('p' * 129) } $admin)
$withPw = Invoke-Api PATCH "/api/access-requests/$cId/status" @{ status = 'approved'; temporaryPassword = 'Temp-pass-12345' } $admin
Check-True 'Approve with a Development password: account created, emailSent matches the setup' (($withPw.Status -eq 200) -and ($withPw.Data.status -eq 'approved') -and ($withPw.Data.accountCreated -eq $true) -and ($withPw.Data.emailSent -eq $stub)) "status=$($withPw.Status) emailSent=$($withPw.Data.emailSent)"

# The account made by approval signs in with that password and has the requested role.
$cSession = Complete-FirstSignIn 'Approved visitor c' $c.email.ToLowerInvariant() 'Temp-pass-12345'
$cMe = Invoke-Api GET '/api/auth/me' $null $cSession.Token
Check-True 'Approved account has the requested role' (($cMe.Status -eq 200) -and ($cMe.Data.role -eq 'SecurityOfficer')) "role=$($cMe.Data.role)"

# An approval for an email that already has an account (the Officer) creates
# nothing and leaves that password alone.
$d = New-Body 'd'
$d.email = $officerAccount.Email
Check 'Request from an existing account email' 202 (Invoke-Api POST '/api/access-requests' $d)
$dId = @((Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin).Data.items | Where-Object { $_.email -eq $d.email.ToLowerInvariant() })[0].id
$sent = Invoke-Api PATCH "/api/access-requests/$dId/status" @{ status = 'approved'; temporaryPassword = 'Temp-pass-67890' } $admin
Check-True 'Approve for an existing account: accountCreated false, no email due' (($sent.Status -eq 200) -and ($sent.Data.status -eq 'approved') -and ($sent.Data.accountCreated -eq $false) -and ($null -eq $sent.Data.emailSent)) "accountCreated=$($sent.Data.accountCreated) emailSent=$($sent.Data.emailSent)"
Check 'The existing account did not get the offered password' 401 (Invoke-Api POST '/api/auth/login' @{ email = $officerAccount.Email; password = 'Temp-pass-67890' })

if ($stub) {
    Start-Sleep -Milliseconds 500
    $calls = @(Get-Content $EmailStubLog | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
    $received = @($calls | Where-Object { $_.body.subject -like 'We received*' -and $_.body.to[0].email -eq $first.email.ToLowerInvariant() })
    Check-True 'Received email: one per new request, not for the repeated email' ($received.Count -eq 1) "count=$($received.Count)"
    $ready = @($calls | Where-Object { $_.body.subject -like 'Your SimGuard account*' })
    Check-True 'Account emails: one per created account (a and c), none for the existing Officer account' (($ready.Count -eq 2) -and (@($ready | Where-Object { $_.body.to[0].email -eq $officerAccount.Email.ToLowerInvariant() }).Count -eq 0)) "count=$($ready.Count)"
    $readyA = @($ready | Where-Object { $_.body.to[0].email -like 'visitor.a.*' })
    $readyC = @($ready | Where-Object { $_.body.to[0].email -like 'visitor.c.*' })
    Check-True 'Account email c: sign-in link, email and the Development password' (($readyC.Count -eq 1) -and ($readyC[0].body.textContent -like '*Temp-pass-12345*') -and ($readyC[0].body.textContent -like '*/login*') -and ($readyC[0].body.htmlContent -like '*Temp-pass-12345*'))
    Check-True 'No email carries the password offered for the existing account' (@($calls | Where-Object { $_.body.textContent -like '*Temp-pass-67890*' }).Count -eq 0)

    # The password the Admin never saw: generated by the API, only in the email.
    $generated = if ($readyA.Count -eq 1 -and $readyA[0].body.textContent -match 'Temporary password: (\S+)') { $Matches[1] } else { $null }
    Check-True 'Account email a carries a generated 20 character password' (($null -ne $generated) -and ($generated.Length -eq 20)) "length=$($generated.Length)"
    if ($generated) {
        $aSession = Complete-FirstSignIn 'Approved visitor a (generated password)' $first.email.ToLowerInvariant() $generated
        $aMe = Invoke-Api GET '/api/auth/me' $null $aSession.Token
        Check-True 'Generated-password account has the requested role' (($aMe.Status -eq 200) -and ($aMe.Data.role -eq 'SecurityOfficer')) "role=$($aMe.Data.role)"

        # Resend: a new generated password replaces the old one, by email only.
        $aUser = @((Invoke-Api GET '/api/users?role=SecurityOfficer&pageSize=100' $null $admin).Data.items | Where-Object { $_.email -eq $first.email.ToLowerInvariant() })[0]
        Check-True 'Admin finds the new user' ($null -ne $aUser)
        $resent = Invoke-Api POST "/api/users/$($aUser.id)/signin-email" $null $admin
        Check-True 'Resend sign-in email reports emailSent true' (($resent.Status -eq 200) -and ($resent.Data.emailSent -eq $true) -and ($resent.Data.mustChangePassword -eq $true)) "status=$($resent.Status)"
        Start-Sleep -Milliseconds 300
        $calls2 = @(Get-Content $EmailStubLog | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
        $again2 = @($calls2 | Where-Object { $_.body.subject -like 'Your SimGuard account*' -and $_.body.to[0].email -like 'visitor.a.*' })
        $newPw = if ($again2.Count -eq 2 -and $again2[1].body.textContent -match 'Temporary password: (\S+)') { $Matches[1] } else { $null }
        Check-True 'Resend sent a different generated password' (($null -ne $newPw) -and ($newPw -ne $generated) -and ($newPw -ne $aSession.Password))
        Check 'The old password no longer works' 401 (Invoke-Api POST '/api/auth/login' @{ email = $first.email.ToLowerInvariant(); password = $aSession.Password })
        if ($newPw) {
            $relogin = Invoke-Api POST '/api/auth/login' @{ email = $first.email.ToLowerInvariant(); password = $newPw }
            Check-True 'The emailed password signs in (MFA step follows)' (($relogin.Status -eq 200) -and ($relogin.Data.status -eq 'mfaRequired')) "status=$($relogin.Data.status)"
        }
    }

    $decl = @($calls | Where-Object { $_.body.subject -like 'About your SimGuard*' })
    Check-True 'Decline email sent to the declined visitor' (($decl.Count -eq 1) -and ($decl[0].body.to[0].email -like 'visitor.b.*'))
    Check-True 'Sent from the verified sender with the API key header' (($ready[0].body.sender.email -eq 'no-reply@sim-guard.app') -and ($ready[0].apiKey -eq 'test-key'))
}

# A reviewed email can ask again.
Check 'After a decision the same email can ask again' 202 (Invoke-Api POST '/api/access-requests' $first)
$list2 = Invoke-Api GET '/api/access-requests?status=pending&pageSize=100' $null $admin
Check-True 'The new request is pending' (@($list2.Data.items | Where-Object { $_.email -eq $first.email.ToLowerInvariant() }).Count -eq 1)

# Audit.
$audit = Invoke-Api GET '/api/audit-logs?action=user.access_request_reviewed' $null $admin
$row = @($audit.Data.items | Where-Object { $_.newValues.accessRequestId -eq $a.id })[0]
Check-True 'Review audited as a system row the Admin can read (old pending, new approved)' (($audit.Status -eq 200) -and ($null -ne $row) -and ($row.category -eq 'system') -and ($row.oldValues.status -eq 'pending') -and ($row.newValues.status -eq 'approved') -and ($row.actorRole -eq 'Admin')) "rows=$($audit.Data.total)"

Write-Results
