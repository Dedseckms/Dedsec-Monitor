param([switch]$Installer, [switch]$SkipTests, [string]$BuildLabel = '1.0.1-preview')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($BuildLabel -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') { throw 'Invalid build label' }
Set-Location $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source } else { Join-Path $root '.tools/dotnet/dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0' }
& "$PSScriptRoot/fetch-presentmon.ps1"
& "$PSScriptRoot/assets.ps1"
if (-not $SkipTests) {
    & $dotnet test Dedsec.Monitor.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts/publish'))
if (-not $publishDirectory.StartsWith([System.IO.Path]::GetFullPath($root) + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish directory escaped the project root.'
}
if (Test-Path $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
& $dotnet publish src/Dedsec.Monitor/Dedsec.Monitor.csproj -c Release -r win-x64 --self-contained true -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
& $dotnet publish src/Dedsec.Updater/Dedsec.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/updater
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed' }
Copy-Item artifacts/updater/Dedsec.Updater.exe artifacts/publish/tools/Dedsec.Updater.exe -Force
$files = @(Get-ChildItem artifacts/publish -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($publishDirectory,$_.FullName).Replace('\','/') }) + 'app-files.json'
ConvertTo-Json -InputObject $files | Set-Content artifacts/publish/app-files.json -Encoding utf8
Compress-Archive -Path artifacts/publish/* -DestinationPath "artifacts/Dedsec-Monitor-$BuildLabel-Portable-x64.zip" -Force
if ($Installer) {
    $candidates = @(
        (Join-Path $root '.tools/inno/ISCC.exe'),
        "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
        "$env:ProgramFiles/Inno Setup 6/ISCC.exe",
        "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe"
    )
    $compiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Install Inno Setup 6.7.1 or later: https://jrsoftware.org/isdl.php' }
    & $compiler /Qp "/DAppVersion=$BuildLabel" installer/Dedsec.Monitor.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
}
Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' } | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
} | Set-Content artifacts/SHA256SUMS.txt -Encoding utf8
