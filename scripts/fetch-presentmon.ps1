$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root 'vendor/PresentMon.exe'
$expected = '364e5d98d4d134bd54dd25c22ed2ca2f4883f8bc3ed6502bee0c151e3436d30c'
New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
if (-not (Test-Path $path)) {
    Invoke-WebRequest 'https://github.com/GameTechDev/PresentMon/releases/download/v2.3.1/PresentMon-2.3.1-x64.exe' -OutFile $path
}
if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    throw 'PresentMon checksum mismatch. Delete vendor/PresentMon.exe and retry.'
}
Write-Output 'PresentMon 2.3.1: SHA-256 verified.'
$driver = Join-Path $root 'vendor/PawnIO_setup.exe'
if (-not (Test-Path $driver)) {
    Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $driver
}
if ((Get-FileHash $driver -Algorithm SHA256).Hash.ToLowerInvariant() -ne '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032') {
    throw 'PawnIO installer checksum mismatch.'
}
if ((Get-AuthenticodeSignature $driver).Status -ne 'Valid') { throw 'PawnIO installer signature is invalid.' }
Write-Output 'PawnIO 2.2.0 installer: SHA-256 and Authenticode verified.'
