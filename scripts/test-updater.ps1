param([switch]$Acknowledge)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source } else { Join-Path $root '.tools/dotnet/dotnet.exe' }
& $dotnet build tests/Dedsec.UpdateFixture -c Release
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
$id = [Guid]::NewGuid().ToString('N')
$qa = Join-Path $root "artifacts/qa/updater-$id"
$target = Join-Path $qa 'app'
$stage = Join-Path $env:LOCALAPPDATA "Dedsec/Monitor/Updates/test-$id/payload"
New-Item -ItemType Directory -Force $target,$stage | Out-Null
Copy-Item tests/Dedsec.UpdateFixture/bin/Release/net10.0/* $target -Force
Move-Item -LiteralPath (Join-Path $target 'Dedsec.UpdateFixture.exe') -Destination (Join-Path $target 'Dedsec.Monitor.exe')
Set-Content (Join-Path $target 'payload.txt') 'OLD' -NoNewline
Copy-Item "$target/*" $stage -Force
$newPayload = if ($Acknowledge) { 'NEW-ACK' } else { 'NEW' }
Set-Content (Join-Path $stage 'payload.txt') $newPayload -NoNewline
$files = @(Get-ChildItem $stage -File | ForEach-Object Name) + 'app-files.json'
ConvertTo-Json -InputObject $files | Set-Content (Join-Path $stage 'app-files.json')
$ack = Join-Path (Split-Path $stage) 'startup.ready'
$env:DOTNET_ROOT = Split-Path $dotnet
$updater = Start-Process artifacts/updater/Dedsec.Updater.exe -ArgumentList @('2147483647',('"'+$target+'"'),('"'+$stage+'"'),('"'+$ack+'"')) -PassThru -WindowStyle Hidden
if (-not $updater.WaitForExit(45000)) { throw 'Updater fixture test timed out.' }
for ($i=0; $i -lt 40; $i++) {
    $log = Join-Path $target 'fixture-launches.log'
    if ((Test-Path $log) -and @(Get-Content $log).Count -ge $(if ($Acknowledge) {1} else {2})) { break }
    Start-Sleep -Milliseconds 100
}
if ($updater.ExitCode -ne $(if ($Acknowledge) {0} else {1})) { throw "Unexpected updater exit $($updater.ExitCode)" }
if ((Get-Content (Join-Path $target 'payload.txt') -Raw) -ne $(if ($Acknowledge) {$newPayload} else {'OLD'})) { throw 'Updated file state is incorrect.' }
$launches = @(Get-Content (Join-Path $target 'fixture-launches.log'))
if ($launches[0] -ne $newPayload -or (-not $Acknowledge -and ($launches.Count -lt 2 -or $launches[1] -ne 'OLD'))) { throw 'Update lifecycle did not match expected launches.' }
$result = if ($Acknowledge) { 'success' } else { 'rollback' }
[pscustomobject]@{ UpdatedProgramStarted=$true; Result=$result; Verified=$true; Log=$log } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $root "artifacts/qa/updater-$result-result.json")
