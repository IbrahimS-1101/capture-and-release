param([string]$OutputDirectory=(Join-Path $PSScriptRoot '..\app'))
$ErrorActionPreference='Stop'
$sourceDirectory=$PSScriptRoot
$outputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$buildDirectory=[IO.Path]::GetFullPath((Join-Path $sourceDirectory '..\.build'))
New-Item -ItemType Directory -Force $buildDirectory,(Join-Path $outputDirectory 'tools') | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_HOME=Join-Path $buildDirectory 'dotnet-home'
$env:MSBuildEnableWorkloadResolver='false'
dotnet build (Join-Path $sourceDirectory 'CaptureAndRelease.csproj') -c Release -o $outputDirectory "-p:BaseIntermediateOutputPath=$buildDirectory\obj-local-screen\" --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'The .NET build failed'}
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vs){throw 'Install Visual Studio C++ desktop build tools and a recent Windows SDK'}
$nativeScript=Join-Path $buildDirectory 'build-native.cmd'
$lines=@('@echo off',"call `"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul",'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /EHsc /O2 /MT /Brepro /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 `"$sourceDirectory\ProcessAudio.cpp`" /Fo:`"$buildDirectory\ProcessAudio.obj`" /Fe:`"$outputDirectory\tools\ProcessAudio.exe`" /link mmdevapi.lib ole32.lib d3d11.lib dxgi.lib")
[IO.File]::WriteAllLines($nativeScript,$lines)
& $env:ComSpec /c "`"$nativeScript`""
if($LASTEXITCODE -ne 0){throw 'The native application-audio helper build failed'}
Write-Output 'Built CaptureAndRelease.exe. Install FFmpeg separately with Install-RecordingTools.ps1.'
