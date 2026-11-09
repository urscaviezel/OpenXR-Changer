$ErrorActionPreference = 'Stop'
$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install Inno Setup 6 to build the installer.' }
$helperBuild = Join-Path $PSScriptRoot ('artifacts/helper-' + [Guid]::NewGuid().ToString('N'))
dotnet publish (Join-Path $PSScriptRoot 'helper/OpenXR-Changer.Helper.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $helperBuild
if ($LASTEXITCODE -ne 0) { throw 'Helper build failed' }
& $compiler "/DHelperBuildDir=$helperBuild" (Join-Path $PSScriptRoot 'installer/OpenXR-Changer-Helper.iss')
if ($LASTEXITCODE -ne 0) { throw 'Helper installer build failed' }
& (Join-Path $PSScriptRoot 'build.ps1')
$build = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'artifacts/build-location.txt'))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'artifacts/OpenXR-Changer-Helper-Setup.exe') -Destination (Join-Path $build 'assets')
$items = @('OpenXR-Changer.exe','lang','assets','LICENSE.txt','README.txt') | ForEach-Object { Join-Path $build $_ }
Compress-Archive -Path $items -DestinationPath (Join-Path $PSScriptRoot 'artifacts/OpenXR-Changer-win-x64.zip') -Force
[xml]$project = Get-Content (Join-Path $PSScriptRoot 'OpenXR-Changer.csproj')
$version = $project.Project.PropertyGroup.Version
& $compiler "/DBuildDir=$build" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer/OpenXR-Changer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
