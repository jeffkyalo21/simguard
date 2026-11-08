<#
.SYNOPSIS
    Imports the demo data (departments, employees, SIMs, allocations, daily usage) through
    the CSV import API (issue #14, see backend/docs/imports.md).

.DESCRIPTION
    Signs in as a SecurityOfficer, then for each file in this order:
    departments.csv, employees.csv, sims.csv, allocations.csv, usage.csv from
    ml-service/data/import_samples (written by
    ml-service/scripts/make_import_files.py):

      1. previews it and stops if the preview reports any row error,
      2. commits it and prints the counts and the time taken.

    It works against any API and database it is pointed at; imports are
    upserts, so running it twice changes nothing the second time. Run it on
    a fresh test database first. After the usage commit the API queues a
    scoring run for the imported SIMs and dates (trigger "import").

    Credentials come from environment variables, as in the smoke scripts,
    and are never printed:

      SIMGUARD_OFFICER_EMAIL        the SecurityOfficer's email
      SIMGUARD_OFFICER_PASSWORD     the SecurityOfficer's password
      SIMGUARD_OFFICER_TOTP_SECRET  the SecurityOfficer's authenticator secret (base32)

    The account must have finished its first sign-in (MFA enrolled, password
    changed).

.EXAMPLE
    venv/Scripts/python scripts/make_import_files.py      # from ml-service/
    $env:SIMGUARD_OFFICER_EMAIL = 'officer@example.com'
    $env:SIMGUARD_OFFICER_PASSWORD = '<password>'
    $env:SIMGUARD_OFFICER_TOTP_SECRET = '<authenticator secret>'
    powershell -NoProfile -File backend/scripts/import-demo-data.ps1 -BaseUrl http://localhost:5275
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$SamplesDir = (Join-Path $PSScriptRoot '..\..\ml-service\data\import_samples')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromMinutes(10)

function Read-Response($response) {
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null
    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) {
        $data = $text | ConvertFrom-Json
    }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Text = $text }
}

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
    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

function Invoke-Import {
    param([string]$Type, [string]$Path, [string]$Token, [string]$Mode)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$BaseUrl/api/imports/$Type`?mode=$Mode")
    $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    $content = New-Object System.Net.Http.MultipartFormDataContent
    $file = New-Object System.Net.Http.ByteArrayContent(, $bytes)
    $file.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('text/csv')
    $content.Add($file, 'file', [System.IO.Path]::GetFileName($Path))
    $request.Content = $content
    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# RFC 6238 TOTP (SHA-1, 6 digits, 30 seconds), same as the smoke scripts.
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

function Format-Counts($d) {
    return "total=$($d.totalRows) inserted=$($d.inserted) updated=$($d.updated) unchanged=$($d.unchanged) errors=$($d.errorCount) warnings=$($d.warningCount)"
}

# --- Sign in -------------------------------------------------------------------
$email = $env:SIMGUARD_OFFICER_EMAIL
$password = $env:SIMGUARD_OFFICER_PASSWORD
$totpSecret = $env:SIMGUARD_OFFICER_TOTP_SECRET

if (-not $email -or -not $password -or -not $totpSecret) {
    throw 'Set SIMGUARD_OFFICER_EMAIL, SIMGUARD_OFFICER_PASSWORD and SIMGUARD_OFFICER_TOTP_SECRET (a SecurityOfficer account).'
}

$login = Invoke-Api POST '/api/auth/login' @{ email = $email; password = $password }
if ($login.Status -ne 200 -or $login.Data.status -ne 'mfaRequired') {
    throw "SecurityOfficer login failed (status $($login.Status)). The account must have finished its first sign-in."
}
$verify = Invoke-Api POST '/api/auth/mfa/verify' @{ mfaToken = $login.Data.mfaToken; code = (Get-Totp $totpSecret) }
if ($verify.Status -ne 200) {
    throw "SecurityOfficer MFA verify failed (status $($verify.Status))."
}
$token = $verify.Data.accessToken
$password = $null

$me = Invoke-Api GET '/api/auth/me' $null $token
if ($me.Data.role -ne 'SecurityOfficer') {
    throw "The account has role '$($me.Data.role)'; imports need a SecurityOfficer."
}

# --- Import ------------------------------------------------------------------
$files = @(
    @{ Type = 'departments'; File = 'departments.csv' },
    @{ Type = 'employees'; File = 'employees.csv' },
    @{ Type = 'sims'; File = 'sims.csv' },
    @{ Type = 'allocations'; File = 'allocations.csv' },
    @{ Type = 'usage'; File = 'usage.csv' }
)

foreach ($f in $files) {
    $path = Join-Path $SamplesDir $f.File
    if (-not (Test-Path $path)) {
        throw "$path not found. Run ml-service/scripts/make_import_files.py first."
    }
}

$summary = New-Object System.Collections.Generic.List[object]
$total = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($f in $files) {
    $path = Join-Path $SamplesDir $f.File

    $preview = Invoke-Import $f.Type $path $token 'preview'
    if ($preview.Status -ne 200) {
        throw "Preview of $($f.File) failed (status $($preview.Status)): $($preview.Text)"
    }
    Write-Host "$($f.Type) preview: $(Format-Counts $preview.Data)"
    if ($preview.Data.errorCount -gt 0) {
        $first = @($preview.Data.errors | Select-Object -First 5 | ForEach-Object { "row $($_.row) $($_.column): $($_.message)" })
        throw "Preview of $($f.File) has $($preview.Data.errorCount) row errors; nothing was committed for it. First: $($first -join '; ')"
    }

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $commit = Invoke-Import $f.Type $path $token 'commit'
    $watch.Stop()
    if ($commit.Status -ne 200) {
        throw "Commit of $($f.File) failed (status $($commit.Status)): $($commit.Text)"
    }
    Write-Host "$($f.Type) commit:  $(Format-Counts $commit.Data) ($([math]::Round($watch.Elapsed.TotalSeconds, 1)) s)"

    $coverage = $commit.Data.coverage
    $summary.Add([pscustomobject]@{
        Type      = $f.Type
        Rows      = $commit.Data.totalRows
        Inserted  = $commit.Data.inserted
        Updated   = $commit.Data.updated
        Unchanged = $commit.Data.unchanged
        Warnings  = $commit.Data.warningCount
        RunId     = $commit.Data.importRunId
        Seconds   = [math]::Round($watch.Elapsed.TotalSeconds, 1)
        Coverage  = if ($coverage) { "sims=$($coverage.sims) scorable=$($coverage.scorable) $($coverage.firstDate)..$($coverage.lastDate)" } else { '' }
    })
}

$total.Stop()
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "Done in $([math]::Round($total.Elapsed.TotalSeconds, 1)) s."
