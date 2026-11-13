param([int]$TargetProcessId = 0, [string]$PublishFolder = 'artifacts/publish')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path (Join-Path $root $PublishFolder) 'Dedsec.Monitor.exe'
if (-not (Test-Path $exe)) { throw 'Run ./scripts/build.ps1 first.' }
$output = Join-Path $root 'docs/screenshots'
$arguments = @('--screenshots', ('"' + $output + '"'), '--smoke-test')
if ($TargetProcessId -gt 0) { $arguments += @('--target', $TargetProcessId.ToString()) }
$process = Start-Process $exe -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit(60000)) { throw 'Screenshot run did not finish within 60 seconds.' }
if ($process.ExitCode -ne 0) { throw "Screenshot smoke check failed: $($process.ExitCode)" }
Write-Output "Screenshots saved: $output"
