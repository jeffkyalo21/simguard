<#
.SYNOPSIS
    Smoke test of the CSV imports (issue #14). Contract in imports.md.

.DESCRIPTION
    Runs against a running Development API on a THROWAWAY database: the
    imported departments, employees, SIMs and usage rows are not removed.

    Checks:
      - roles: Admin and Auditor get 403 on every import endpoint, no token
        gets 401, SecurityOfficer is allowed
      - template download for every type (exact header, two sample rows)
      - file-wide rejections: wrong header, binary file, not UTF-8, not .csv,
        empty file, ragged row, file over 25 MB
      - per type: preview with row errors, all-or-nothing commit (422),
        skipInvalid, a second commit of the same file leaves everything
        unchanged, updates are counted
      - SIM changes of carrier or tier are audited per SIM
      - usage coverage (scorable, not enough history, gaps) and the tier
        mismatch warning
      - the demo files from ml-service/scripts/make_import_files.py: all
        four committed, the usage file committed twice (second time all
        unchanged), then the 100000 row usage file, timed
      - import history and the import.committed audit rows

    Accounts are found like roles-smoke.ps1 does: -AdminSecretsFile (written
    by auth-smoke.ps1), -OfficerSecretsFile and -AuditorSecretsFile (written
    by roles-smoke.ps1), or throwaway accounts the Admin creates. Passwords,
    tokens and MFA secrets are never printed.

    Start the API with RateLimiting__Auth__PermitLimit=1000 for this run.

.EXAMPLE
    python ml-service/scripts/make_import_files.py
    powershell -NoProfile -File backend/docs/imports-smoke.ps1 -AdminSecretsFile $env:TEMP\simguard-smoke-admin.json
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$AdminSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-admin.json'),
    [string]$OfficerSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-officer.json'),
    [string]$AuditorSecretsFile = (Join-Path $env:TEMP 'simguard-smoke-auditor.json'),
    [string]$SamplesDir = (Join-Path $PSScriptRoot '..\..\ml-service\data\import_samples')
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

    $email = "imports-$($Role.ToLowerInvariant())-$runId@simguard.local"
    $temp = New-TestPassword
    Check "Admin creates a throwaway $Role" 201 (Invoke-Api POST '/api/users' @{ email = $email; name = "Imports $Label"; role = $Role; temporaryPassword = $temp } $AdminToken)
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
$officerAccount = Get-RoleToken 'Officer' 'SecurityOfficer' $OfficerSecretsFile $admin
$auditorAccount = Get-RoleToken 'Auditor' 'Auditor' $AuditorSecretsFile $admin
$officer = $officerAccount.Token
$auditor = $auditorAccount.Token

$departmentsHeader = 'name'
$employeesHeader = 'name,email,department,job_role'
$simsHeader = 'iccid,phone_number,carrier,tier,status'
$allocationsHeader = 'iccid,employee_email,start_date'
$usageHeader = 'iccid,date,total_download_mb,total_upload_mb,data_session_count,data_duration_s,hotspot_sessions,public_apn_sessions,unique_towers,geo_flag_count,peak_mb,offpeak_mb,total_mb,roaming_sessions,billed_mb,tier,voice_minutes_used,sms_count,voice_minutes_remaining,sms_remaining'

# --- Roles -------------------------------------------------------------------
$probe = New-Csv @($departmentsHeader, "Roles Probe $runId")
foreach ($pair in @(@('Admin', $admin), @('Auditor', $auditor))) {
    $label = $pair[0]; $token = $pair[1]
    Check "$label GET /api/imports" 403 (Invoke-Api GET '/api/imports' $null $token)
    Check "$label GET template" 403 (Invoke-Api GET '/api/imports/sims/template' $null $token)
    Check "$label POST preview" 403 (Invoke-Import 'departments' $probe 'probe.csv' $token)
    Check "$label POST commit" 403 (Invoke-Import 'departments' $probe 'probe.csv' $token 'mode=commit')
}
Check 'No token POST preview' 401 (Invoke-Import 'departments' $probe 'probe.csv' $null)
Check 'Officer GET /api/imports' 200 (Invoke-Api GET '/api/imports' $null $officer)

# --- Templates ---------------------------------------------------------------
$headers = [ordered]@{ departments = $departmentsHeader; employees = $employeesHeader; sims = $simsHeader; allocations = $allocationsHeader; usage = $usageHeader }
foreach ($type in $headers.Keys) {
    $t = Invoke-Api GET "/api/imports/$type/template" $null $officer
    Check "Template $type" 200 $t
    $lines = @($t.Text -split "`r`n" | Where-Object { $_ -ne '' })
    Check-True "Template $type is text/csv with the exact header and two sample rows" (($t.ContentType -eq 'text/csv') -and ($lines.Count -eq 3) -and ($lines[0] -eq $headers[$type])) "lines=$($lines.Count)"
}
Check 'Template of an unknown type' 404 (Invoke-Api GET '/api/imports/carriers/template' $null $officer)
Check 'Import of an unknown type' 404 (Invoke-Import 'carriers' $probe 'probe.csv' $officer)
Check 'Unknown mode' 400 (Invoke-Import 'departments' $probe 'probe.csv' $officer 'mode=apply')

# The template of each type previews cleanly (sims and usage need data, so
# only the first two are checked here).
foreach ($type in @('departments')) {
    $t = Invoke-Api GET "/api/imports/$type/template" $null $officer
    $p = Invoke-Import $type $utf8.GetBytes($t.Text) "$type-template.csv" $officer
    Check-True "Template $type previews without errors" (($p.Status -eq 200) -and ($p.Data.errorCount -eq 0)) (Counts $p)
}

# --- File-wide rejections ----------------------------------------------------
$r = Invoke-Import 'departments' (New-Csv @('title', 'X')) 'wrong.csv' $officer
Check 'Wrong header' 400 $r
Check-True 'Wrong header message names the missing and unknown columns' (($r.Data.detail -match 'missing columns: name') -and ($r.Data.detail -match 'unknown columns: title')) $r.Data.detail

$r = Invoke-Import 'sims' (New-Csv @('ICCID, Phone_Number ,carrier,tier', 'x,y,z,w')) 'short.csv' $officer
Check-True 'Header without status is rejected (case and spacing do not matter)' (($r.Status -eq 400) -and ($r.Data.detail -match 'missing columns: status\)') -and ($r.Data.detail -notmatch 'unknown columns')) $r.Data.detail

$r = Invoke-Import 'departments' (New-Csv @('name')) 'header-only.csv' $officer
Check 'Header row only, no data' 400 $r

$r = Invoke-Import 'departments' (New-Csv @('name,name', 'a,b')) 'dup.csv' $officer
Check-True 'Repeated column rejected' (($r.Status -eq 400) -and ($r.Data.detail -match 'repeated columns')) $r.Data.detail

$binary = [byte[]](0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00) + (New-Object byte[] 64)
$r = Invoke-Import 'departments' $binary 'archive.csv' $officer
Check-True 'Binary file rejected' (($r.Status -eq 400) -and ($r.Data.detail -match 'binary')) $r.Data.detail

$latin1 = [byte[]](0x6E, 0x61, 0x6D, 0x65, 0x0D, 0x0A, 0x43, 0x61, 0x66, 0xE9, 0x0D, 0x0A)
$r = Invoke-Import 'departments' $latin1 'latin1.csv' $officer
Check-True 'Not UTF-8 rejected' (($r.Status -eq 400) -and ($r.Data.detail -match 'UTF-8')) $r.Data.detail

Check 'Not a .csv file name' 400 (Invoke-Import 'departments' $probe 'departments.xlsx' $officer)
Check 'Empty file' 400 (Invoke-Import 'departments' ([byte[]]@()) 'empty.csv' $officer)

$r = Invoke-Import 'employees' (New-Csv @($employeesHeader, 'A,a@example.com,Finance')) 'ragged.csv' $officer
Check-True 'Ragged row rejected with its row number' (($r.Status -eq 400) -and ($r.Data.detail -match 'Row 2')) $r.Data.detail

$r = Invoke-Import 'departments' (New-Csv @('name', '"Unclosed')) 'quote.csv' $officer
Check-True 'Unclosed quote rejected' (($r.Status -eq 400) -and ($r.Data.detail -match 'quoted value')) $r.Data.detail

$big = New-Object byte[] (25MB + 1024)
$headerBytes = $utf8.GetBytes("name`r`n")
[Array]::Copy($headerBytes, $big, $headerBytes.Length)
for ($i = $headerBytes.Length; $i -lt $big.Length; $i++) { $big[$i] = 0x61 }
Check 'File over 25 MB' 413 (Invoke-Import 'departments' $big 'big.csv' $officer 'mode=commit')
$big = $null

# --- Departments -------------------------------------------------------------
$deptA = "Imports Ops $runId"
$deptB = "Imports Finance $runId"
$deptFile = New-Csv @(
    $departmentsHeader,
    $deptA,
    '"' + $deptB + '"',
    '""',
    $deptA.ToUpperInvariant()
)
$r = Invoke-Import 'departments' $deptFile 'departments.csv' $officer
Check 'Departments preview' 200 $r (Counts $r)
Check-True 'Departments preview: 2 to insert, blank and duplicate rows are errors' (($r.Data.status -eq 'preview') -and ($r.Data.totalRows -eq 4) -and ($r.Data.inserted -eq 2) -and ($r.Data.errorCount -eq 2) -and (Has-Error $r 4 'name') -and (Has-Error $r 5 'name')) (Counts $r)

$r = Invoke-Import 'departments' $deptFile 'departments.csv' $officer 'mode=commit'
Check 'Departments commit with errors is rejected' 422 $r (Counts $r)
Check-True 'Rejected commit wrote nothing' (($r.Data.status -eq 'rejected') -and ($r.Data.inserted -eq 0) -and ($null -ne $r.Data.importRunId))
$p = Invoke-Import 'departments' $deptFile 'departments.csv' $officer
Check-True 'Preview after the rejected commit still shows 2 to insert' ($p.Data.inserted -eq 2) (Counts $p)

$r = Invoke-Import 'departments' $deptFile 'departments.csv' $officer 'mode=commit&skipInvalid=true'
Check 'Departments commit with skipInvalid' 200 $r (Counts $r)
Check-True 'skipInvalid imported the 2 valid rows' (($r.Data.status -eq 'committed') -and ($r.Data.inserted -eq 2) -and ($r.Data.errorCount -eq 2)) (Counts $r)

$deptClean = New-Csv @($departmentsHeader, $deptA, $deptB)
$r = Invoke-Import 'departments' $deptClean 'departments.csv' $officer 'mode=commit'
Check-True 'Departments second commit: all unchanged' (($r.Status -eq 200) -and ($r.Data.unchanged -eq 2) -and ($r.Data.inserted -eq 0) -and ($r.Data.updated -eq 0)) (Counts $r)

$r = Invoke-Import 'departments' (New-Csv @($departmentsHeader, $deptA.ToLowerInvariant())) 'departments.csv' $officer
Check-True 'Departments: other casing of an existing name is an update' (($r.Data.updated -eq 1) -and ($r.Data.inserted -eq 0)) (Counts $r)

# --- Employees ---------------------------------------------------------------
$empFile = New-Csv @(
    $employeesHeader,
    "Ann Imports,ann.$runId@example.com,$deptA,Engineer",
    "Ben Imports,ben.$runId@example.com,$($deptB.ToLowerInvariant()),Accountant",
    "Cy Imports,cy.$runId@example.com,No Such Department $runId,Clerk",
    "Dee Imports,not-an-email,$deptA,Clerk",
    "Ann Again,ANN.$runId@EXAMPLE.COM,$deptA,Engineer",
    "Eve Imports,eve.$runId@example.com,$deptA,"
)
$r = Invoke-Import 'employees' $empFile 'employees.csv' $officer
Check-True 'Employees preview: unknown department, bad email, duplicate email, missing job_role' (($r.Status -eq 200) -and ($r.Data.inserted -eq 2) -and ($r.Data.errorCount -eq 4) -and (Has-Error $r 4 'department') -and (Has-Error $r 5 'email') -and (Has-Error $r 6 'email') -and (Has-Error $r 7 'job_role')) (Counts $r)
Check 'Employees commit with errors is rejected' 422 (Invoke-Import 'employees' $empFile 'employees.csv' $officer 'mode=commit')
$r = Invoke-Import 'employees' $empFile 'employees.csv' $officer 'mode=commit&skipInvalid=true'
Check-True 'Employees skipInvalid commit inserted 2' (($r.Status -eq 200) -and ($r.Data.inserted -eq 2)) (Counts $r)
$empClean = New-Csv @(
    $employeesHeader,
    "Ann Imports,ann.$runId@example.com,$deptA,Engineer",
    "Ben Imports,ben.$runId@example.com,$deptB,Accountant"
)
$r = Invoke-Import 'employees' $empClean 'employees.csv' $officer 'mode=commit'
Check-True 'Employees second commit: all unchanged' (($r.Status -eq 200) -and ($r.Data.unchanged -eq 2) -and ($r.Data.inserted -eq 0) -and ($r.Data.updated -eq 0)) (Counts $r)
$r = Invoke-Import 'employees' (New-Csv @($employeesHeader, "Ann Imports,ann.$runId@example.com,$deptB,Lead Engineer")) 'employees.csv' $officer 'mode=commit'
Check-True 'Employees: new department and job role is an update' (($r.Status -eq 200) -and ($r.Data.updated -eq 1)) (Counts $r)

# --- SIMs --------------------------------------------------------------------
function Iccid([int]$k) { return '8925499' + $runId.ToString('0000000000') + $k.ToString('00') }
function Phone([int]$k) { return '+2541' + (($runId * 10 + $k) % 100000000).ToString('00000000') }

$simFile = New-Csv @(
    $simsHeader,
    "$(Iccid 1),$(Phone 1),Safaricom,field,",
    "$(Iccid 2),$(Phone 2),airtel,Manager,available",
    "$(Iccid 3),$(Phone 3),TELKOM,professional,inactive",
    "$(Iccid 4),$(Phone 4),Orange,field,",
    "$(Iccid 5),$(Phone 5),Safaricom,gold,",
    "$(Iccid 6),0712345678,Safaricom,field,",
    "12345,$(Phone 7),Safaricom,field,",
    "$(Iccid 8),$(Phone 8),Safaricom,field,assigned",
    "$(Iccid 9),$(Phone 1),Safaricom,field,",
    "$(Iccid 1),$(Phone 10),Safaricom,field,"
)
$r = Invoke-Import 'sims' $simFile 'sims.csv' $officer
Check-True 'SIMs preview: 3 to insert, 7 row errors' (($r.Status -eq 200) -and ($r.Data.inserted -eq 3) -and ($r.Data.errorCount -eq 7)) (Counts $r)
Check-True 'SIMs errors: carrier, tier, phone, iccid, status, phone in use, repeated iccid' ((Has-Error $r 5 'carrier') -and (Has-Error $r 6 'tier') -and (Has-Error $r 7 'phone_number') -and (Has-Error $r 8 'iccid') -and (Has-Error $r 9 'status') -and (Has-Error $r 10 'phone_number') -and (Has-Error $r 11 'iccid'))
Check 'SIMs commit with errors is rejected' 422 (Invoke-Import 'sims' $simFile 'sims.csv' $officer 'mode=commit')
$r = Invoke-Import 'sims' $simFile 'sims.csv' $officer 'mode=commit&skipInvalid=true'
Check-True 'SIMs skipInvalid commit inserted 3' (($r.Status -eq 200) -and ($r.Data.inserted -eq 3)) (Counts $r)

$list = Invoke-Api GET "/api/sims?search=$(Iccid 1)" $null $officer
$sim1 = @($list.Data.items | Where-Object { $_.iccid -eq (Iccid 1) })[0]
Check-True 'Imported SIM: available, tier field, Safaricom, tier in the SIM response' (($null -ne $sim1) -and ($sim1.status -eq 'available') -and ($sim1.tier -eq 'field') -and ($sim1.operator -eq 'Safaricom')) "status=$($sim1.status) tier=$($sim1.tier)"

$simClean = New-Csv @(
    $simsHeader,
    "$(Iccid 1),$(Phone 1),Safaricom,field,",
    "$(Iccid 2),$(Phone 2),Airtel,manager,",
    "$(Iccid 3),$(Phone 3),Telkom,professional,inactive"
)
$r = Invoke-Import 'sims' $simClean 'sims.csv' $officer 'mode=commit'
Check-True 'SIMs second commit: all unchanged' (($r.Status -eq 200) -and ($r.Data.unchanged -eq 3) -and ($r.Data.inserted -eq 0) -and ($r.Data.updated -eq 0)) (Counts $r)

$simChange = New-Csv @(
    $simsHeader,
    "$(Iccid 1),$(Phone 1),Safaricom,support,",
    "$(Iccid 2),$(Phone 2),Telkom,manager,",
    "$(Iccid 3),$(Phone 3),Telkom,professional,"
)
$r = Invoke-Import 'sims' $simChange 'sims.csv' $officer 'mode=commit'
Check-True 'SIMs: tier change and carrier change are 2 updates (empty status keeps it)' (($r.Status -eq 200) -and ($r.Data.updated -eq 2) -and ($r.Data.unchanged -eq 1)) (Counts $r)

$tierAudit = Invoke-Api GET "/api/audit-logs?action=sim.tier_changed&simId=$($sim1.id)" $null $auditor
$tierRow = @($tierAudit.Data.items)[0]
Check-True 'Tier change audited per SIM (old field, new support, SecurityOfficer)' (($tierAudit.Data.total -eq 1) -and ($tierRow.oldValues.tier -eq 'field') -and ($tierRow.newValues.tier -eq 'support') -and ($tierRow.actorRole -eq 'SecurityOfficer') -and ($tierRow.category -eq 'organisation')) "rows=$($tierAudit.Data.total)"
$sim2 = @((Invoke-Api GET "/api/sims?search=$(Iccid 2)" $null $officer).Data.items)[0]
$carrierAudit = Invoke-Api GET "/api/audit-logs?action=sim.updated&simId=$($sim2.id)" $null $auditor
$carrierRow = @($carrierAudit.Data.items)[0]
Check-True 'Carrier change audited per SIM (Airtel to Telkom)' (($carrierAudit.Data.total -eq 1) -and ($carrierRow.oldValues.operator -eq 'Airtel') -and ($carrierRow.newValues.operator -eq 'Telkom')) "rows=$($carrierAudit.Data.total)"

# --- Allocations -------------------------------------------------------------
# Iccid 1 and 2 are available, Iccid 3 is inactive. ann and ben were imported above.
$allocBad = New-Csv @(
    $allocationsHeader,
    "$(Iccid 1),ANN.$runId@example.com,2025-02-01",
    "$(Iccid 2),ben.$runId@example.com,",
    "$(Iccid 3),ann.$runId@example.com,",
    "$(Iccid 77),ann.$runId@example.com,",
    "$(Iccid 2),ann.$runId@example.com,",
    "$(Iccid 1),nobody.$runId@example.com,2025-13-40"
)
$r = Invoke-Import 'allocations' $allocBad 'allocations.csv' $officer
Check-True 'Allocations preview: 2 to insert, errors for inactive SIM, unknown SIM, repeated ICCID' (($r.Status -eq 200) -and ($r.Data.inserted -eq 2) -and (Has-Error $r 4 'iccid') -and (Has-Error $r 5 'iccid') -and (Has-Error $r 6 'iccid')) (Counts $r)
$simBefore = @((Invoke-Api GET "/api/sims?search=$(Iccid 1)" $null $officer).Data.items)[0]
Check-True 'Allocations preview saves nothing (SIM still available)' ($simBefore.status -eq 'available') "status=$($simBefore.status)"
Check 'Allocations commit with errors is rejected' 422 (Invoke-Import 'allocations' $allocBad 'allocations.csv' $officer 'mode=commit')
$r = Invoke-Import 'allocations' $allocBad 'allocations.csv' $officer 'mode=commit&skipInvalid=true'
Check-True 'Allocations skipInvalid commit inserted 2' (($r.Status -eq 200) -and ($r.Data.inserted -eq 2)) (Counts $r)

$simAfter = @((Invoke-Api GET "/api/sims?search=$(Iccid 1)" $null $officer).Data.items)[0]
Check-True 'Allocated SIM became assigned' ($simAfter.status -eq 'assigned') "status=$($simAfter.status)"
$alloc = @((Invoke-Api GET "/api/allocations?simId=$($simAfter.id)&active=true" $null $officer).Data.items)[0]
Check-True 'Allocation has the start date and is active' (($null -ne $alloc) -and ($alloc.isActive -eq $true) -and ($alloc.assignmentDate -like '2025-02-01*')) "date=$($alloc.assignmentDate)"
$allocAudit = Invoke-Api GET "/api/audit-logs?action=allocation.created&simId=$($simAfter.id)" $null $auditor
Check-True 'Allocation audited per SIM (SecurityOfficer)' (($allocAudit.Data.total -eq 1) -and (@($allocAudit.Data.items)[0].actorRole -eq 'SecurityOfficer')) "rows=$($allocAudit.Data.total)"

$allocClean = New-Csv @(
    $allocationsHeader,
    "$(Iccid 1),ann.$runId@example.com,",
    "$(Iccid 2),ben.$runId@example.com,"
)
$r = Invoke-Import 'allocations' $allocClean 'allocations.csv' $officer 'mode=commit'
Check-True 'Allocations second commit: same employee is unchanged' (($r.Status -eq 200) -and ($r.Data.unchanged -eq 2) -and ($r.Data.inserted -eq 0)) (Counts $r)
$allocOther = New-Csv @($allocationsHeader, "$(Iccid 1),ben.$runId@example.com,")
$r = Invoke-Import 'allocations' $allocOther 'allocations.csv' $officer 'mode=commit'
Check-True 'Allocations: a different employee for an allocated SIM is a row error (422)' (($r.Status -eq 422) -and (Has-Error $r 2 'employee_email')) (Counts $r)

# --- Usage -------------------------------------------------------------------
function UsageRow([string]$Iccid, [datetime]$Day, [string]$Tier, [string]$TotalMb = '120.75', [string]$Sms = '4') {
    return "$Iccid,$($Day.ToString('yyyy-MM-dd')),100.5,20.25,3,600,0,0,2,0,80,40.75,$TotalMb,0,120.75,$Tier,5.5,$Sms,194.5,96"
}

$start = [datetime]'2025-01-01'
$usageLines = New-Object System.Collections.Generic.List[string]
$usageLines.Add($usageHeader)
# SIM 1 (support): 20 consecutive days, scorable. The last row says field: a warning.
for ($d = 0; $d -lt 20; $d++) { $usageLines.Add((UsageRow (Iccid 1) $start.AddDays($d) $(if ($d -eq 19) { 'field' } else { 'support' }))) }
# SIM 2 (manager): 10 days, not enough history.
for ($d = 0; $d -lt 10; $d++) { $usageLines.Add((UsageRow (Iccid 2) $start.AddDays($d) 'manager')) }
# SIM 3 (professional): days 1 to 10 and 13 to 20, a gap of 2 days.
for ($d = 0; $d -lt 20; $d++) { if ($d -ne 10 -and $d -ne 11) { $usageLines.Add((UsageRow (Iccid 3) $start.AddDays($d) 'professional')) } }
$usageGood = $usageLines.ToArray()

$badLines = [System.Collections.Generic.List[string]]::new([string[]]$usageGood)
$badLines.Add((UsageRow (Iccid 1) $start 'support'))                          # repeated (iccid, date)
$badLines.Add((UsageRow (Iccid 2) $start.AddDays(40) 'manager' '-1'))         # negative total_mb
$badLines.Add((UsageRow (Iccid 2) $start.AddDays(41) 'manager' '120' ''))     # empty sms_count
$badLines.Add((UsageRow (Iccid 99) $start 'field'))                           # unknown iccid
$badLines.Add(((UsageRow (Iccid 2) $start 'manager') -replace '2025-01-01', '2025-13-01'))  # bad date
$badLines.Add((UsageRow (Iccid 2) $start.AddDays(42) 'gold'))                 # bad tier
$badRows = $usageGood.Count
$r = Invoke-Import 'usage' (New-Csv $badLines.ToArray()) 'usage.csv' $officer
Check-True 'Usage preview: 48 to insert, 6 row errors' (($r.Status -eq 200) -and ($r.Data.inserted -eq 48) -and ($r.Data.errorCount -eq 6)) (Counts $r)
Check-True 'Usage errors: repeated date, negative, empty, unknown iccid, date, tier' ((Has-Error $r ($badRows + 1) 'date') -and (Has-Error $r ($badRows + 2) 'total_mb') -and (Has-Error $r ($badRows + 3) 'sms_count') -and (Has-Error $r ($badRows + 4) 'iccid') -and (Has-Error $r ($badRows + 5) 'date') -and (Has-Error $r ($badRows + 6) 'tier'))
Check 'Usage commit with errors is rejected' 422 (Invoke-Import 'usage' (New-Csv $badLines.ToArray()) 'usage.csv' $officer 'mode=commit')

$r = Invoke-Import 'usage' (New-Csv $usageGood) 'usage.csv' $officer 'mode=commit'
Check 'Usage commit' 200 $r (Counts $r)
$c = $r.Data.coverage
Check-True 'Usage: 48 inserted, tier mismatch is a warning' (($r.Data.inserted -eq 48) -and (@($r.Data.warnings | Where-Object { $_.column -eq 'tier' -and $_.row -eq 21 }).Count -eq 1)) (Counts $r)
Check-True 'Coverage: 3 SIMs, 1 scorable, 2 not enough history, 1 with gaps' (($c.sims -eq 3) -and ($c.scorable -eq 1) -and ($c.notEnoughHistory -eq 2) -and ($c.withGaps -eq 1) -and ($c.minimumDays -eq 15) -and ($c.firstDate -eq '2025-01-01') -and ($c.lastDate -eq '2025-01-20')) "sims=$($c.sims) scorable=$($c.scorable) short=$($c.notEnoughHistory) gaps=$($c.withGaps)"
$gapSim = @($c.details | Where-Object { $_.iccid -eq (Iccid 3) })[0]
Check-True 'Coverage of the SIM with a gap: 18 days, 1 gap, 2 missing, latest run 8' (($gapSim.days -eq 18) -and ($gapSim.gapCount -eq 1) -and ($gapSim.missingDays -eq 2) -and ($gapSim.latestRunDays -eq 8) -and (-not $gapSim.scorable))

$r = Invoke-Import 'usage' (New-Csv $usageGood) 'usage.csv' $officer 'mode=commit'
Check-True 'Usage second commit: 48 unchanged' (($r.Status -eq 200) -and ($r.Data.unchanged -eq 48) -and ($r.Data.inserted -eq 0) -and ($r.Data.updated -eq 0)) (Counts $r)
$changed = [string[]]$usageGood.Clone()
$changed[1] = UsageRow (Iccid 1) $start 'support' '999.5'
$r = Invoke-Import 'usage' (New-Csv $changed) 'usage.csv' $officer 'mode=commit'
Check-True 'Usage: one changed value is 1 update, 47 unchanged' (($r.Status -eq 200) -and ($r.Data.updated -eq 1) -and ($r.Data.unchanged -eq 47)) (Counts $r)
$usage = Invoke-Api GET "/api/sims/$($sim1.id)/usage?from=2025-01-01&to=2025-01-01" $null $officer
Check-True 'The updated usage row reads back' ((@($usage.Data.days)[0].totalMb -eq 999.5)) "totalMb=$(@($usage.Data.days)[0].totalMb)"

# --- Demo files --------------------------------------------------------------
$timings = @()
$demo = [ordered]@{ departments = 'departments.csv'; employees = 'employees.csv'; sims = 'sims.csv'; allocations = 'allocations.csv'; usage = 'usage.csv' }
$haveSamples = Test-Path (Join-Path $SamplesDir 'usage_perf_100k.csv')
Check-True 'Demo files exist (run ml-service/scripts/make_import_files.py)' $haveSamples $SamplesDir

if ($haveSamples) {
    foreach ($type in $demo.Keys) {
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $SamplesDir $demo[$type]))
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $r = Invoke-Import $type $bytes $demo[$type] $officer 'mode=commit'
        $watch.Stop()
        Check "Demo $($demo[$type]) commit" 200 $r "$(Counts $r) in $([math]::Round($watch.Elapsed.TotalSeconds, 1)) s"
        $timings += "$($demo[$type]) $($r.Data.totalRows) rows $([math]::Round($watch.Elapsed.TotalSeconds, 1)) s"
        if ($type -eq 'usage') {
            $c = $r.Data.coverage
            Check-True 'Demo usage coverage: 1000 SIMs, 2025-01-01 to 2025-03-31, all scorable' (($c.sims -eq 1000) -and ($c.scorable -eq 1000) -and ($c.withGaps -eq 0) -and ($c.firstDate -eq '2025-01-01') -and ($c.lastDate -eq '2025-03-31')) "scorable=$($c.scorable) short=$($c.notEnoughHistory) gaps=$($c.withGaps)"
            $watch = [System.Diagnostics.Stopwatch]::StartNew()
            $again = Invoke-Import $type $bytes $demo[$type] $officer 'mode=commit'
            $watch.Stop()
            Check-True 'Demo usage second commit: 90000 unchanged' (($again.Status -eq 200) -and ($again.Data.unchanged -eq 90000) -and ($again.Data.inserted -eq 0) -and ($again.Data.updated -eq 0)) "$(Counts $again) in $([math]::Round($watch.Elapsed.TotalSeconds, 1)) s"
            $timings += "usage.csv again (unchanged) $([math]::Round($watch.Elapsed.TotalSeconds, 1)) s"
        }
    }

    foreach ($type in @('departments', 'employees', 'sims', 'allocations')) {
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $SamplesDir $demo[$type]))
        $again = Invoke-Import $type $bytes $demo[$type] $officer 'mode=commit'
        Check-True "Demo $($demo[$type]) second commit: all unchanged" (($again.Status -eq 200) -and ($again.Data.unchanged -eq $again.Data.totalRows) -and ($again.Data.inserted -eq 0) -and ($again.Data.updated -eq 0)) (Counts $again)
    }

    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $SamplesDir 'usage_perf_100k.csv'))
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Invoke-Import 'usage' $bytes 'usage_perf_100k.csv' $officer 'mode=commit'
    $watch.Stop()
    $seconds = [math]::Round($watch.Elapsed.TotalSeconds, 1)
    Check-True "100000 row usage commit in under 60 s ($seconds s)" (($r.Status -eq 200) -and ($r.Data.totalRows -eq 100000) -and ($r.Data.inserted -eq 100000) -and ($watch.Elapsed.TotalSeconds -lt 60)) (Counts $r)
    $timings += "usage_perf_100k.csv 100000 rows $seconds s"
}

# --- History and audit -------------------------------------------------------
$history = Invoke-Api GET '/api/imports?pageSize=100' $null $officer
Check 'Officer GET /api/imports' 200 $history "runs=$($history.Data.total)"
$statuses = @($history.Data.items | ForEach-Object { $_.status } | Sort-Object -Unique)
Check-True 'History has committed and rejected runs, newest first, with the uploader' (($statuses -contains 'committed') -and ($statuses -contains 'rejected') -and (@($history.Data.items)[0].uploadedByEmail -eq $officerAccount.Email) -and ([datetime]@($history.Data.items)[0].startedAt -ge [datetime]@($history.Data.items)[-1].startedAt)) "statuses=$($statuses -join ',')"
$usageRuns = Invoke-Api GET '/api/imports?type=usage' $null $officer
Check-True 'History filtered by type' (($usageRuns.Status -eq 200) -and (@($usageRuns.Data.items | Where-Object { $_.type -ne 'usage' }).Count -eq 0) -and ($usageRuns.Data.total -gt 0))
Check 'History with an unknown type' 400 (Invoke-Api GET '/api/imports?type=carriers' $null $officer)
$sha = @($usageRuns.Data.items)[0].sha256
Check-True 'History rows carry a SHA-256' ($sha -match '^[0-9a-f]{64}$')

$auditRows = Invoke-Api GET "/api/audit-logs?action=import.committed&actorEmail=$([uri]::EscapeDataString($officerAccount.Email))&pageSize=100" $null $auditor
$auditItems = @($auditRows.Data.items)
Check-True 'Every commit wrote import.committed (organisation, SecurityOfficer)' (($auditItems.Count -gt 0) -and (@($auditItems | Where-Object { $_.category -ne 'organisation' -or $_.actorRole -ne 'SecurityOfficer' }).Count -eq 0) -and ($null -ne $auditItems[0].newValues.importRunId)) "rows=$($auditRows.Data.total)"
$adminAudit = Invoke-Api GET '/api/audit-logs?action=import.committed' $null $admin
Check-True 'Admin does not see import audit rows' (($adminAudit.Status -eq 200) -and ($adminAudit.Data.total -eq 0))

if ($timings.Count -gt 0) {
    Write-Host 'Timings:'
    $timings | ForEach-Object { Write-Host "  $_" }
}

Write-Results
