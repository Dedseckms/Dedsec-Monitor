param([string]$BuildLabel = '1.0.1-preview')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$existing = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D17C8746-C637-4EA0-935E-E61BC1C33C04}_is1' -ErrorAction SilentlyContinue
if ($existing) { throw 'An existing Dedsec installation is registered; disposable installer testing would overwrite its registration.' }
$qa = Join-Path $root ('artifacts/qa/install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $qa | Out-Null
$installPath = Join-Path $qa 'app'
$setup = Join-Path $root "artifacts/Dedsec-Monitor-$BuildLabel-Setup-x64.exe"
$installer = Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + $installPath + '"'),('/LOG="' + (Join-Path $qa 'setup.log') + '"')) -PassThru -WindowStyle Hidden
if (-not $installer.WaitForExit(60000)) { throw 'Installer timed out.' }
if ($installer.ExitCode -ne 0) { throw "Installer exit code: $($installer.ExitCode)" }
$exe = Join-Path $installPath 'Dedsec.Monitor.exe'
if (-not (Test-Path $exe)) { throw 'Installed executable is missing.' }
$screenPath = Join-Path $qa 'screenshots'
$app = Start-Process $exe -ArgumentList @('--smoke-test','--screenshots',('"' + $screenPath + '"')) -PassThru -WindowStyle Hidden
if (-not $app.WaitForExit(60000)) { throw 'Installed app smoke test timed out.' }
if ($app.ExitCode -ne 0) { throw "Installed app smoke test failed: $($app.ExitCode)" }
$diagnostics = Get-Content (Join-Path $screenPath 'diagnostics.json') -Raw | ConvertFrom-Json
if ($diagnostics.RecordedSamples -le 0 -or -not $diagnostics.OverlayShown -or -not $diagnostics.OverlayHidden) { throw 'Installed app checks failed.' }
$uninstaller = Start-Process (Join-Path $installPath 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $qa 'uninstall.log') + '"')) -PassThru -WindowStyle Hidden
if (-not $uninstaller.WaitForExit(60000)) { throw 'Uninstaller timed out.' }
for ($attempt = 0; $attempt -lt 20 -and (Test-Path $exe); $attempt++) { Start-Sleep -Milliseconds 250 }
if (Test-Path $exe) { throw 'Uninstall left the installed application executable behind.' }
[pscustomobject]@{
    Installed = $true
    LaunchedSelfContained = $true
    SensorCount = $diagnostics.SensorCount
    SessionSamples = $diagnostics.RecordedSamples
    OverlayShown = $diagnostics.OverlayShown
    OverlayHidden = $diagnostics.OverlayHidden
    Uninstalled = $true
    Logs = $qa
} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root 'artifacts/qa/installer-result.json')
