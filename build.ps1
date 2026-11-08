$ErrorActionPreference = 'Stop'
$releaseRoot = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force $releaseRoot | Out-Null
# A new staging folder prevents assets from older builds entering the package.
$destination = Join-Path $releaseRoot ('build-' + [Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $PSScriptRoot 'OpenXR-Changer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item (Join-Path $PSScriptRoot 'LICENSE.txt') $destination
Copy-Item (Join-Path $PSScriptRoot 'README.txt') $destination
$items = @('OpenXR-Changer.exe','lang','assets','LICENSE.txt','README.txt') | ForEach-Object { Join-Path $destination $_ }
Compress-Archive -Path $items -DestinationPath (Join-Path $releaseRoot 'OpenXR-Changer-win-x64.zip') -Force
[System.IO.File]::WriteAllText((Join-Path $releaseRoot 'build-location.txt'), $destination)
Write-Output "Portable build: $destination"
