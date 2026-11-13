<#
.SYNOPSIS
    Smoke test of the carrier usage feed (settings, test connection, sync, importer).

.DESCRIPTION
    Starts the carrier mock service itself (backend/SimGuard.CarrierMock) on
    -MockPort with a small data folder it writes to a temporary directory, and
    drives a running Development API against it. Prints the real result of
    every step next to the expected one:

      401 without a token, 403 for the Auditor on save, test and sync,
      validation of the feed address and the key, the key is write-only,
      wrong key gives 401 with a clear message, test connection ok,
      sync catches up several days, a second run adds no duplicates,
      unknown ICCIDs are counted as rejected, 429 and 500 recover with
      retries, a long outage fails with the reason and keeps the cursor,
      an empty day does not move the cursor, a slow answer times out and
      recovers, only one run per carrier at a time, import history rows,
      scoring is queued after an import, audit rows without the key, and
      the key never appears in any response or in the API log.

    Start the API first, on a throwaway database that you drop afterwards,
    with these settings (environment variables), so that the three accounts
    exist and the run is quick:

      ConnectionStrings__DefaultConnection  a throwaway database (never "simguard")
      SimGuard__Seed__AdminEmail / AdminPassword
      SimGuard__Seed__OfficerEmail / OfficerPassword
      SimGuard__Seed__AuditorEmail / AuditorPassword
      RateLimiting__Auth__PermitLimit=1000
      CarrierFeed__RetryBaseDelayMs=100
      CarrierFeed__RequestTimeoutSeconds=2
      CarrierFeed__MaxAttempts=3
      CarrierFeed__StartupDelaySeconds=3600
      DataProtection__KeysPath               a temporary folder

    The script signs in with the seeded temporary passwords (first sign-in:
    MFA enrolment and password change), read from:

      SIMGUARD_SMOKE_ADMIN_EMAIL / SIMGUARD_SMOKE_ADMIN_PASSWORD
      SIMGUARD_SMOKE_OFFICER_EMAIL / SIMGUARD_SMOKE_OFFICER_PASSWORD
      SIMGUARD_SMOKE_AUDITOR_EMAIL / SIMGUARD_SMOKE_AUDITOR_PASSWORD

    Pass -ApiLogFile (the API's redirected output) to also check that the
    API key never appears in the log. Passwords, tokens and keys are never
    printed. The mock process and its temporary files are removed at the end.

.EXAMPLE
    powershell -NoProfile -File backend/docs/carrier-feed-smoke.ps1 -BaseUrl http://localhost:5392 -MockPort 5391 -ApiLogFile $env:TEMP\api.log
#>
param(
    [string]$BaseUrl = 'http://localhost:5275',
    [int]$MockPort = 5391,
    [string]$ApiLogFile = '',
    [string]$MockProject = (Join-Path $PSScriptRoot '..\SimGuard.CarrierMock')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromSeconds(120)
$script:results = New-Object System.Collections.Generic.List[object]
$script:allText = New-Object System.Text.StringBuilder
$mockUrl = "http://127.0.0.1:$MockPort"

function Read-Response($response) {
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    [void]$script:allText.AppendLine($text)
    $data = $null
    if ($text -and ($text.TrimStart().StartsWith('{') -or $text.TrimStart().StartsWith('['))) {
        $data = $text | ConvertFrom-Json
    }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Text = $text }
}

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null, [string]$Base = $BaseUrl)

    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::new($Method), "$Base$Path")
    if ($Token) {
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 6 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
    }
    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

function Check {
    param([string]$Step, [int]$Expected, $Result, [string]$Note = '')
    $actual = if ($Result -is [int]) { $Result } else { $Result.Status }
    $script:results.Add([pscustomobject]@{
        Step = $Step; Expected = $Expected; Actual = $actual
        Result = if ($actual -eq $Expected) { 'PASS' } else { 'FAIL' }; Note = $Note
    })
}

function Check-True {
    param([string]$Step, [bool]$Condition, [string]$Note = '')
    $script:results.Add([pscustomobject]@{
        Step = $Step; Expected = 'true'; Actual = "$Condition".ToLowerInvariant()
        Result = if ($Condition) { 'PASS' } else { 'FAIL' }; Note = $Note
    })
}

function Check-Skip {
    param([string]$Step, [string]$Note)
    $script:results.Add([pscustomobject]@{ Step = $Step; Expected = '-'; Actual = '-'; Result = 'SKIP'; Note = $Note })
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

function New-ApiKey {
    $bytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    return 'fk' + (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

# First sign-in of a seeded account: login, MFA enrolment, password change.
function Complete-FirstSignIn {
    param([string]$Label, [string]$Email, [string]$TemporaryPassword)
    $login = Invoke-Api POST '/api/auth/login' @{ email = $Email; password = $TemporaryPassword }
    if ($login.Status -ne 200) { throw "$Label login failed (status $($login.Status))." }
    $start = Invoke-Api POST '/api/auth/mfa/enroll/start' $null $login.Data.mfaToken
    if ($start.Data.authenticatorUri -notmatch 'secret=([A-Z2-7]+)') { throw 'The authenticator URI has no secret.' }
    $confirm = Invoke-Api POST '/api/auth/mfa/enroll/confirm' @{ code = (Get-Totp $Matches[1]) } $login.Data.mfaToken
    $changed = Invoke-Api POST '/api/auth/change-password' @{ currentPassword = $TemporaryPassword; newPassword = (New-TestPassword) } $confirm.Data.accessToken
    Check "$Label first sign-in (login, MFA enrolment, password change)" 200 $changed
    return $changed.Data.accessToken
}

function Write-Results {
    $script:results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $failed = @($script:results | Where-Object { $_.Result -eq 'FAIL' }).Count
    $skipped = @($script:results | Where-Object { $_.Result -eq 'SKIP' }).Count
    Write-Host "$($script:results.Count - $failed - $skipped) passed, $failed failed, $skipped skipped"
    return $failed
}

# Admin routes of the mock need X-Admin-Key (MOCK_ADMIN_KEY, set below).
function Invoke-Mock {
    param([string]$Path, $Body = $null, [string]$AdminKey = $mockAdminKey)
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "$mockUrl$Path")
    if ($AdminKey) { $request.Headers.TryAddWithoutValidation('X-Admin-Key', $AdminKey) | Out-Null }
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Depth 6 -Compress } else { '{}' }
    $request.Content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, 'application/json')
    Read-Response ($client.SendAsync($request).GetAwaiter().GetResult())
}

# Waits until the carrier's last sync time changes and the run is no longer
# running, then returns the carrier row.
function Wait-Sync {
    param([int]$CarrierId, [string]$Token, [string]$PreviousAt, [int]$TimeoutSeconds = 90)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $row = @((Invoke-Api GET '/api/carriers' $null $Token).Data) | Where-Object { $_.id -eq $CarrierId } | Select-Object -First 1
        if ($row.lastSyncAt -and "$($row.lastSyncAt)" -ne $PreviousAt -and $row.lastSyncStatus -ne 'running') { return $row }
        Start-Sleep -Milliseconds 300
    }
    throw "The sync of carrier $CarrierId did not finish within $TimeoutSeconds seconds."
}

function Get-Carrier {
    param([int]$CarrierId, [string]$Token)
    @((Invoke-Api GET '/api/carriers' $null $Token).Data) | Where-Object { $_.id -eq $CarrierId } | Select-Object -First 1
}

# Starts a sync as the officer and waits for it.
function Sync-Now {
    param([int]$CarrierId)
    $previous = "$((Get-Carrier $CarrierId $officer).lastSyncAt)"
    $start = Invoke-Api POST "/api/carriers/$CarrierId/feed/sync" $null $officer
    if ($start.Status -ne 202) { throw "Sync now returned $($start.Status): $($start.Text)" }
    return Wait-Sync $CarrierId $officer $previous
}

function Get-FeedRuns {
    param([string]$CarrierName)
    @((Invoke-Api GET '/api/imports?source=carrier%20feed&pageSize=100' $null $officer).Data.items) |
        Where-Object { $_.carrierName -eq $CarrierName } | Sort-Object id
}

# --- Settings ----------------------------------------------------------------
$adminEmail = $env:SIMGUARD_SMOKE_ADMIN_EMAIL
$adminTemp = $env:SIMGUARD_SMOKE_ADMIN_PASSWORD
$officerEmail = $env:SIMGUARD_SMOKE_OFFICER_EMAIL
$officerTemp = $env:SIMGUARD_SMOKE_OFFICER_PASSWORD
$auditorEmail = $env:SIMGUARD_SMOKE_AUDITOR_EMAIL
$auditorTemp = $env:SIMGUARD_SMOKE_AUDITOR_PASSWORD

if (-not ($adminEmail -and $adminTemp -and $officerEmail -and $officerTemp -and $auditorEmail -and $auditorTemp)) {
    throw 'Set SIMGUARD_SMOKE_ADMIN_*, SIMGUARD_SMOKE_OFFICER_* and SIMGUARD_SMOKE_AUDITOR_* (email and password) for the accounts seeded in the API.'
}

$runId = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$nameA = "SmokeA$runId"
$nameB = "SmokeB$runId"
$keyA = New-ApiKey
$keyB = New-ApiKey
$wrongKey = New-ApiKey
$mockAdminKey = New-ApiKey
$days = 1..8 | ForEach-Object { ([datetime]'2025-02-01').AddDays($_ - 1).ToString('yyyy-MM-dd') }
$firstDay = $days[0]

# Real SIMs created through the API (3 for A, 2 for B) and two ICCIDs only the
# carrier knows (unknown to SimGuard, so they must be rejected).
function New-Iccid([int]$Index) { '8925403' + ($runId * 40 + $Index).ToString('000000000000') }
function New-Phone([int]$Index) { '+2547' + ((($runId * 40 + $Index) % 100000000).ToString('00000000')) }
$simsA = 1..3 | ForEach-Object { New-Iccid $_ }
$simsB = 4..5 | ForEach-Object { New-Iccid $_ }
$unknown = 6..7 | ForEach-Object { New-Iccid $_ }

$work = Join-Path ([System.IO.Path]::GetTempPath()) "carrier-feed-smoke-$runId"
$mockProcess = $null
$exitCode = 1

try {
    # --- Mock data and process ----------------------------------------------
    New-Item -ItemType Directory -Force $work | Out-Null
    $simLines = @('iccid,carrier')
    foreach ($i in $simsA + $unknown) { $simLines += "$i,$nameA" }
    foreach ($i in $simsB) { $simLines += "$i,$nameB" }
    Set-Content -Path (Join-Path $work 'sims.csv') -Value $simLines -Encoding ascii

    $header = 'iccid,date,total_download_mb,total_upload_mb,data_session_count,data_duration_s,hotspot_sessions,public_apn_sessions,unique_towers,geo_flag_count,peak_mb,offpeak_mb,total_mb,roaming_sessions,billed_mb,tier,voice_minutes_used,sms_count,voice_minutes_remaining,sms_remaining'
    $usageLines = New-Object System.Collections.Generic.List[string]
    $usageLines.Add($header)
    $n = 0
    foreach ($iccid in $simsA + $unknown + $simsB) {
        $n++
        $d = 0
        foreach ($day in $days) {
            $d++
            $down = 100 + 10 * $n + $d
            $usageLines.Add("$iccid,$day,$down,20,3,600,0,0,2,0,$($down / 2),$($down / 2 + 20),$($down + 20),0,$($down + 20),field,5.5,2,150,100")
        }
    }
    Set-Content -Path (Join-Path $work 'usage.csv') -Value $usageLines -Encoding ascii

    & dotnet build $MockProject -nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the carrier mock.' }
    $dll = Get-ChildItem -Path (Join-Path $MockProject 'bin') -Recurse -Filter 'SimGuard.CarrierMock.dll' | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    $env:CarrierMock__DataDir = $work
    $env:CarrierMock__SessionsFile = (Join-Path $work 'none.csv')
    $env:CarrierMock__StartDate = $firstDay
    # The nightly release stays off so a run across 01:00 Nairobi cannot move the pointer.
    $env:MOCK_SCHEDULER_ENABLED = 'false'
    $env:MOCK_ADMIN_KEY = $mockAdminKey
    Set-Item -Path "Env:CarrierMock__Keys__$nameA" -Value $keyA
    Set-Item -Path "Env:CarrierMock__Keys__$nameB" -Value $keyB
    $mockLog = Join-Path $work 'mock.log'
    $mockProcess = Start-Process dotnet -ArgumentList "`"$($dll.FullName)`"", '--urls', $mockUrl -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $mockLog -RedirectStandardError (Join-Path $work 'mock-err.log')

    $ready = $false
    for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
        try { $ready = (Invoke-WebRequest "$mockUrl/" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'The carrier mock did not start.' }

    # --- Mock contract --------------------------------------------------------
    Check 'Mock: admin route without the admin key' 401 (Invoke-Mock '/admin/release-next-day' $null '')
    Check 'Mock: admin route with a carrier key instead of the admin key' 401 (Invoke-Mock '/admin/release-next-day' $null $keyA)
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$mockUrl/v1/usage/summary?date=$firstDay")
    Check 'Mock: summary without a key' 401 (Read-Response ($client.SendAsync($req).GetAwaiter().GetResult()))
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$mockUrl/v1/usage/summary?date=$firstDay")
    $req.Headers.TryAddWithoutValidation('X-Api-Key', $wrongKey) | Out-Null
    Check 'Mock: summary with a wrong key' 401 (Read-Response ($client.SendAsync($req).GetAwaiter().GetResult()))
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$mockUrl/v1/usage/summary?date=2025-02-05&pageSize=2")
    $req.Headers.TryAddWithoutValidation('X-Api-Key', $keyA) | Out-Null
    Check 'Mock: a date after the current date' 404 (Read-Response ($client.SendAsync($req).GetAwaiter().GetResult()))
    $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "$mockUrl/v1/usage/summary?date=$firstDay&pageSize=2&page=2")
    $req.Headers.TryAddWithoutValidation('X-Api-Key', $keyA) | Out-Null
    $page2 = Read-Response ($client.SendAsync($req).GetAwaiter().GetResult())
    Check 'Mock: page 2 of the summary' 200 $page2 "total=$($page2.Data.total) items=$(@($page2.Data.items).Count)"
    Check-True 'Mock: carrier A has 5 SIMs (3 known, 2 unknown) and pages of 2' (($page2.Data.total -eq 5) -and (@($page2.Data.items).Count -eq 2))

    # --- Sign in --------------------------------------------------------------
    $admin = Complete-FirstSignIn 'Admin' $adminEmail $adminTemp
    $officer = Complete-FirstSignIn 'Officer' $officerEmail $officerTemp
    $auditor = Complete-FirstSignIn 'Auditor' $auditorEmail $auditorTemp

    # --- Carriers and SIMs -----------------------------------------------------
    $carrierA = (Invoke-Api POST '/api/carriers' @{ name = $nameA } $officer).Data
    $carrierB = (Invoke-Api POST '/api/carriers' @{ name = $nameB } $officer).Data
    Check-True 'New carriers have no feed set up' (($carrierA.feedKeySet -eq $false) -and ($carrierA.feedEnabled -eq $false) -and ($null -eq $carrierA.lastSyncStatus))
    $simIdsA = @{}
    $index = 0
    foreach ($iccid in $simsA + $simsB) {
        $index++
        $carrierId = if ($simsA -contains $iccid) { $carrierA.id } else { $carrierB.id }
        $sim = Invoke-Api POST '/api/sims' @{ phoneNumber = (New-Phone $index); iccid = $iccid; carrierId = $carrierId; planType = 'Postpaid'; monthlyCost = 1500 } $officer
        if ($sim.Status -ne 201) { throw "Could not create SIM $iccid (status $($sim.Status))." }
        $simIdsA[$iccid] = $sim.Data.id
    }

    # --- Access ---------------------------------------------------------------
    $feedPath = "/api/carriers/$($carrierA.id)/feed"
    $settings = @{ baseUrl = $mockUrl; enabled = $true; startDate = $firstDay; apiKey = $wrongKey }
    Check 'PUT feed without a token' 401 (Invoke-Api PUT $feedPath $settings)
    Check 'POST feed/test without a token' 401 (Invoke-Api POST "$feedPath/test")
    Check 'POST feed/sync without a token' 401 (Invoke-Api POST "$feedPath/sync")
    Check 'Auditor cannot save feed settings' 403 (Invoke-Api PUT $feedPath $settings $auditor)
    Check 'Auditor cannot test the connection' 403 (Invoke-Api POST "$feedPath/test" $null $auditor)
    Check 'Auditor cannot sync' 403 (Invoke-Api POST "$feedPath/sync" $null $auditor)
    Check 'Admin cannot save feed settings (operator, not organisation data)' 403 (Invoke-Api PUT $feedPath $settings $admin)
    Check 'Unknown carrier on save' 404 (Invoke-Api PUT '/api/carriers/999999/feed' $settings $officer)

    # --- Validation -------------------------------------------------------------
    $badUrl = Invoke-Api PUT $feedPath @{ baseUrl = 'not a url'; enabled = $false } $officer
    Check 'Bad feed address' 400 $badUrl
    Check-True 'Bad address error is on baseUrl' (@($badUrl.Data.errors.baseUrl).Count -gt 0)
    Check 'Plain http to a public host' 400 (Invoke-Api PUT $feedPath @{ baseUrl = 'http://carrier.example.com'; enabled = $false } $officer)
    Check 'Enabled without a key' 400 (Invoke-Api PUT $feedPath @{ baseUrl = $mockUrl; enabled = $true } $officer)
    Check 'Key with a space' 400 (Invoke-Api PUT $feedPath @{ baseUrl = $mockUrl; enabled = $false; apiKey = 'has space' } $officer)
    Check 'Sync before the feed is set up' 400 (Invoke-Api POST "$feedPath/sync" $null $officer)

    # --- Wrong key ---------------------------------------------------------------
    $saved = Invoke-Api PUT $feedPath $settings $officer
    Check 'Officer saves feed settings (wrong key first)' 200 $saved
    Check-True 'Response says the key is saved and never contains it' (($saved.Data.feedKeySet -eq $true) -and ($null -eq $saved.Data.apiKey) -and ($saved.Text -notlike "*$wrongKey*"))
    Check-True 'Response carries address, enabled and start date' (($saved.Data.feedBaseUrl -eq $mockUrl) -and ($saved.Data.feedEnabled -eq $true) -and ("$($saved.Data.feedStartDate)" -eq $firstDay))
    $listed = Get-Carrier $carrierA.id $officer
    Check-True 'GET /api/carriers shows keySet true without a key field' (($listed.feedKeySet -eq $true) -and ($listed.PSObject.Properties.Name -notcontains 'apiKey'))

    $wrong = Invoke-Api POST "$feedPath/test" $null $officer
    Check 'Test connection with a wrong key (call itself succeeds)' 200 $wrong "$($wrong.Data.message)"
    Check-True 'Wrong key: ok false and a clear message about the key (401)' (($wrong.Data.ok -eq $false) -and ($wrong.Data.message -like '*rejected the API key*401*'))

    $syncWrong = Sync-Now $carrierA.id
    Check-True 'Sync with a wrong key fails with the reason' (($syncWrong.lastSyncStatus -eq 'failed') -and ($syncWrong.lastSyncMessage -like '*rejected the API key*'))
    Check-True 'The failed run keeps the cursor empty' ($null -eq $syncWrong.lastSyncDate)
    $failedRuns = @(Get-FeedRuns $nameA)
    Check-True 'The failed run is in the import history as failed' (($failedRuns.Count -eq 1) -and ($failedRuns[0].status -eq 'failed') -and ($failedRuns[0].source -eq 'carrier feed'))

    # --- Right key, test connection ------------------------------------------
    $settings.apiKey = $keyA
    Check 'Officer replaces the key' 200 (Invoke-Api PUT $feedPath $settings $officer)
    $ok = Invoke-Api POST "$feedPath/test" $null $officer
    Check 'Test connection with the right key' 200 $ok "$($ok.Data.message)"
    Check-True 'Right key: ok true and "Connected"' (($ok.Data.ok -eq $true) -and ($ok.Data.message -like 'Connected*'))
    $unsaved = Invoke-Api POST "$feedPath/test" @{ baseUrl = $mockUrl; apiKey = $wrongKey } $officer
    Check-True 'Test connection can try unsaved values (a wrong typed key fails)' (($unsaved.Status -eq 200) -and ($unsaved.Data.ok -eq $false))
    $badAddress = Invoke-Api POST "$feedPath/test" @{ baseUrl = 'http://127.0.0.1:1'; apiKey = $keyA } $officer
    Check-True 'Test connection to an address nobody listens on gives a plain error' (
        ($badAddress.Status -eq 200) -and ($badAddress.Data.ok -eq $false) -and
        ($badAddress.Data.message -match '^(Could not reach the carrier feed|The carrier feed did not answer in time)')) "$($badAddress.Data.message)"
    Check 'Test connection with no key and no address in the request body is fine' 200 (Invoke-Api POST "$feedPath/test" $null $officer)

    # --- Catch up several days ---------------------------------------------------
    $scoringBefore = (Invoke-Api GET '/api/admin/scoring/status' $null $officer).Data
    $maxRunBefore = [int](@($scoringBefore.runs | ForEach-Object { $_.id }) + 0 | Measure-Object -Maximum).Maximum
    Invoke-Mock '/admin/set-date' @{ date = $days[3] } | Out-Null
    $first = Sync-Now $carrierA.id
    Check-True 'Sync catches up 4 days (cursor on the 4th day)' (($first.lastSyncStatus -eq 'succeeded') -and ("$($first.lastSyncDate)" -eq $days[3])) "$($first.lastSyncMessage)"

    $usageIds = $simsA | ForEach-Object { $simIdsA[$_] }
    $perSim = $usageIds | ForEach-Object { @((Invoke-Api GET "/api/sims/$_/usage?from=2025-02-01&to=2025-02-08" $null $officer).Data.days).Count }
    Check-True 'Every carrier A SIM has 4 usage days' (@($perSim | Where-Object { $_ -eq 4 }).Count -eq 3) "days=$($perSim -join ',')"
    $otherSim = @((Invoke-Api GET "/api/sims/$($simIdsA[$simsB[0]])/usage?from=2025-02-01&to=2025-02-08" $null $officer).Data.days).Count
    Check-True 'A carrier B SIM got nothing from carrier A (own slice of SIMs)' ($otherSim -eq 0) "days=$otherSim"

    $runs = @(Get-FeedRuns $nameA)
    $run1 = $runs | Select-Object -Last 1
    Check-True 'History row: source, carrier, 12 imported, 8 rejected (2 unknown ICCIDs x 4 days)' (
        ($runs.Count -eq 2) -and ($run1.source -eq 'carrier feed') -and ($run1.carrierName -eq $nameA) -and
        ($run1.status -eq 'committed') -and ($run1.validRows -eq 12) -and ($run1.invalidRows -eq 8) -and ($run1.inserted -eq 12) -and ($run1.errorCount -eq 0)) `
        "valid=$($run1.validRows) rejected=$($run1.invalidRows) inserted=$($run1.inserted)"
    Check-True 'Unknown ICCIDs are rejected rows, not errors' (($run1.invalidRows -eq 8) -and ($run1.errorCount -eq 0))

    # Scoring is queued after the import (trigger import, new run).
    $queued = $null
    for ($i = 0; $i -lt 30 -and -not $queued; $i++) {
        $status = (Invoke-Api GET '/api/admin/scoring/status' $null $officer).Data
        $queued = @($status.runs | Where-Object { $_.trigger -eq 'import' -and $_.id -gt $maxRunBefore }) | Select-Object -First 1
        if (-not $queued) { Start-Sleep -Milliseconds 500 }
    }
    Check-True 'A scoring run with trigger "import" was queued after the import' ($null -ne $queued) "runId=$($queued.id) status=$($queued.status)"

    # --- Running twice adds no duplicates ---------------------------------------
    $again = Sync-Now $carrierA.id
    Check-True 'A second sync with nothing new succeeds and says so' (($again.lastSyncStatus -eq 'succeeded') -and ($again.lastSyncMessage -like '*Caught up*') -and ("$($again.lastSyncDate)" -eq $days[3])) "$($again.lastSyncMessage)"
    Check-True 'It adds no history row' (@(Get-FeedRuns $nameA).Count -eq 2)

    $repull = $settings.Clone()
    $repull.startDate = $days[2]
    Check 'Choosing an earlier start date to pull two days again' 200 (Invoke-Api PUT $feedPath $repull $officer)
    $second = Sync-Now $carrierA.id
    $run2 = @(Get-FeedRuns $nameA) | Select-Object -Last 1
    Check-True 'Re-pulled days change nothing: 6 rows, all unchanged, 0 inserted' (
        ($second.lastSyncStatus -eq 'succeeded') -and ($run2.id -ne $run1.id) -and ($run2.validRows -eq 6) -and ($run2.inserted -eq 0) -and ($run2.updated -eq 0) -and ($run2.unchanged -eq 6)) `
        "valid=$($run2.validRows) inserted=$($run2.inserted) updated=$($run2.updated) unchanged=$($run2.unchanged)"
    $perSim = $usageIds | ForEach-Object { @((Invoke-Api GET "/api/sims/$_/usage?from=2025-02-01&to=2025-02-08" $null $officer).Data.days).Count }
    Check-True 'Still 4 usage days per SIM (no duplicates)' (@($perSim | Where-Object { $_ -eq 4 }).Count -eq 3) "days=$($perSim -join ',')"
    Check-True 'The cursor is back on the 4th day' ("$($second.lastSyncDate)" -eq $days[3])

    # --- One day at a time -------------------------------------------------------
    Invoke-Mock '/admin/advance-day' | Out-Null
    $day5 = Sync-Now $carrierA.id
    Check-True 'After advance-day the sync takes exactly one new day' (($day5.lastSyncStatus -eq 'succeeded') -and ("$($day5.lastSyncDate)" -eq $days[4])) "$($day5.lastSyncMessage)"

    # --- 429 and 500 recover ------------------------------------------------------
    Invoke-Mock '/admin/advance-day' | Out-Null
    Invoke-Mock '/admin/fault' @{ fault = 'rate_limit'; count = 2 } | Out-Null
    $rate = Sync-Now $carrierA.id
    Check-True '429 twice: backs off, retries and imports the day' (($rate.lastSyncStatus -eq 'succeeded') -and ("$($rate.lastSyncDate)" -eq $days[5])) "$($rate.lastSyncMessage)"

    Invoke-Mock '/admin/advance-day' | Out-Null
    Invoke-Mock '/admin/fault' @{ fault = 'server_error'; count = 2 } | Out-Null
    $five = Sync-Now $carrierA.id
    Check-True '500 twice: retries and imports the day' (($five.lastSyncStatus -eq 'succeeded') -and ("$($five.lastSyncDate)" -eq $days[6])) "$($five.lastSyncMessage)"

    # --- A long outage fails with the reason and keeps the cursor --------------------
    Invoke-Mock '/admin/advance-day' | Out-Null
    Invoke-Mock '/admin/fault' @{ fault = 'server_error' } | Out-Null
    $down = Sync-Now $carrierA.id
    Check-True 'Long 500 outage: failed with the reason' (($down.lastSyncStatus -eq 'failed') -and ($down.lastSyncMessage -like '*server error*500*')) "$($down.lastSyncMessage)"
    Check-True 'The cursor did not move' ("$($down.lastSyncDate)" -eq $days[6])
    $lastRun = @(Get-FeedRuns $nameA) | Select-Object -Last 1
    Check-True 'The outage is in the history as failed with the message' (($lastRun.status -eq 'failed') -and ($lastRun.message -like '*server error*') -and ($lastRun.errorCount -eq 1))
    Invoke-Mock '/admin/reset' | Out-Null

    # --- An empty day does not move the cursor ----------------------------------------
    # days[7] is released but not synced yet; the carrier answers it with no rows once.
    # (A date after the end of the data is a 404 "no more days", never an empty day.)
    Invoke-Mock '/admin/fault' @{ fault = 'empty_day'; count = 1 } | Out-Null
    $empty = Sync-Now $carrierA.id
    Check-True 'An empty day succeeds without moving the cursor' (($empty.lastSyncStatus -eq 'succeeded') -and ("$($empty.lastSyncDate)" -eq $days[6]) -and ($empty.lastSyncMessage -like '*no rows*')) "$($empty.lastSyncMessage)"
    Invoke-Mock '/admin/reset' | Out-Null

    $recovered = Sync-Now $carrierA.id
    Check-True 'After the outage the next run catches up' (($recovered.lastSyncStatus -eq 'succeeded') -and ("$($recovered.lastSyncDate)" -eq $days[7])) "$($recovered.lastSyncMessage)"

    # --- Slow answer: timeout, retry, and one run at a time ------------------------------
    Invoke-Mock '/admin/set-date' @{ date = $days[7] } | Out-Null
    $repull.startDate = $days[7]
    Check 'Pull the last day again for the timeout test' 200 (Invoke-Api PUT $feedPath $repull $officer)
    Invoke-Mock '/admin/fault' @{ fault = 'slow'; count = 1; delayMs = 3500 } | Out-Null
    $previousAt = "$((Get-Carrier $carrierA.id $officer).lastSyncAt)"
    $startSlow = Invoke-Api POST "$feedPath/sync" $null $officer
    Check 'Sync now answers at once while it runs in the background' 202 $startSlow
    Check-True 'The answer shows the run as running' ($startSlow.Data.lastSyncStatus -eq 'running')
    $second = Invoke-Api POST "$feedPath/sync" $null $officer
    Check 'A second Sync now while one is running' 409 $second "$($second.Data.detail)"
    $slow = Wait-Sync $carrierA.id $officer $previousAt
    Check-True 'A slow answer times out, is retried and succeeds' (($slow.lastSyncStatus -eq 'succeeded') -and ("$($slow.lastSyncDate)" -eq $days[7])) "$($slow.lastSyncMessage)"
    Invoke-Mock '/admin/reset' | Out-Null

    # --- Carrier B has its own key and SIMs ---------------------------------------------
    $pathB = "/api/carriers/$($carrierB.id)/feed"
    Check 'Carrier B saves its own key' 200 (Invoke-Api PUT $pathB @{ baseUrl = "$mockUrl/"; enabled = $true; startDate = $firstDay; apiKey = $keyB } $officer)
    $rowB = Get-Carrier $carrierB.id $officer
    Check-True 'A trailing slash on the address is removed' ($rowB.feedBaseUrl -eq $mockUrl)
    $syncB = Sync-Now $carrierB.id
    $runB = @(Get-FeedRuns $nameB) | Select-Object -Last 1
    Check-True 'Carrier B imports 8 days for its 2 SIMs and rejects nothing' (($syncB.lastSyncStatus -eq 'succeeded') -and ($runB.validRows -eq 16) -and ($runB.invalidRows -eq 0)) "valid=$($runB.validRows) rejected=$($runB.invalidRows)"
    $bDays = @((Invoke-Api GET "/api/sims/$($simIdsA[$simsB[0]])/usage?from=2025-02-01&to=2025-02-08" $null $officer).Data.days).Count
    Check-True 'Carrier B SIM has 8 usage days' ($bDays -eq 8)

    # --- Audit ------------------------------------------------------------------------------
    foreach ($action in @('carrier.feed_saved', 'carrier.feed_tested', 'carrier.feed_sync_requested')) {
        $rows = @((Invoke-Api GET "/api/audit-logs?action=$action&pageSize=100" $null $auditor).Data.items)
        $mine = @($rows | Where-Object { $_.newValues.carrierId -eq $carrierA.id })
        Check-True "Audit has $action rows for carrier A (organisation category)" (($mine.Count -ge 1) -and ($mine[0].category -eq 'organisation')) "count=$($mine.Count)"
    }
    $savedRows = @((Invoke-Api GET '/api/audit-logs?action=carrier.feed_saved&pageSize=100' $null $auditor).Data.items) | Where-Object { $_.newValues.carrierId -eq $carrierA.id }
    Check-True 'A saved-feed audit row says keyChanged and keySet but has no key' (
        (@($savedRows | Where-Object { $_.newValues.keyChanged -eq $true -and $_.newValues.keySet -eq $true }).Count -ge 1) -and
        (@($savedRows | Where-Object { $_.newValues.PSObject.Properties.Name -contains 'apiKey' }).Count -eq 0))

    # --- The key never appears -------------------------------------------------------------------
    $everything = $script:allText.ToString()
    Check-True 'No API response contained any carrier API key' (
        ($everything -notlike "*$keyA*") -and ($everything -notlike "*$keyB*") -and ($everything -notlike "*$wrongKey*"))
    if ($ApiLogFile -and (Test-Path $ApiLogFile)) {
        $log = Get-Content -Raw $ApiLogFile
        Check-True 'The API log never contains a carrier API key' (($log -notlike "*$keyA*") -and ($log -notlike "*$keyB*") -and ($log -notlike "*$wrongKey*"))
    }
    else {
        Check-Skip 'The API log never contains a carrier API key' 'Pass -ApiLogFile to check the log.'
    }
    $mockText = Get-Content -Raw $mockLog
    Check-True 'The mock log does not contain the keys either (keys travel in a header only)' (($mockText -notlike "*$keyA*") -and ($mockText -notlike "*$keyB*"))
}
finally {
    if ($mockProcess -and -not $mockProcess.HasExited) {
        Stop-Process -Id $mockProcess.Id -Force
        [void]$mockProcess.WaitForExit(10000)
    }
    Get-ChildItem Env: | Where-Object { $_.Name -like 'CarrierMock__*' -or $_.Name -like 'MOCK_*' } | ForEach-Object { Remove-Item "Env:$($_.Name)" }
    for ($i = 0; $i -lt 10 -and (Test-Path $work); $i++) {
        try { Remove-Item -Recurse -Force $work -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 }
    }
}

$failed = Write-Results
if ($failed -gt 0) { exit 1 }
