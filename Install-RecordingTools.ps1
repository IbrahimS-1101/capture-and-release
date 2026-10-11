param([string]$AppDirectory=(Join-Path $PSScriptRoot 'app'),[string]$ArchivePath='')
# Downloads directly from the upstream build provider; no FFmpeg binary is redistributed here.
$ErrorActionPreference='Stop'
$expectedHash='467cde100a47ed4b03a897988aeb4a296890c1e2b2d2864204657d002bc5fb90'
$url='https://github.com/GyanD/codexffmpeg/releases/download/8.0.1/ffmpeg-8.0.1-full_build.zip'
$tools=Join-Path ([IO.Path]::GetFullPath($AppDirectory)) 'tools'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('CaptureAndRelease-setup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    if(!$ArchivePath) {
        Write-Host 'Downloading FFmpeg 8.0.1 from Gyan Doshi via GitHub (about 234 MB).'
        Write-Host 'This one-time setup uses the internet. Recording afterward works offline.'
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $ArchivePath=Join-Path $temporary 'ffmpeg.zip'
        $ProgressPreference='SilentlyContinue'
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $ArchivePath
    }
    if((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash){throw 'FFmpeg download failed SHA-256 verification. Nothing was installed.'}
    Write-Host 'Checksum verified. Extracting recording tools and their notices...'
    $extracted=Join-Path $temporary 'extracted'
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extracted
    $package=Join-Path $extracted 'ffmpeg-8.0.1-full_build'
    foreach($file in @('bin\ffmpeg.exe','bin\ffprobe.exe','LICENSE','README.txt')) {
        if(!(Test-Path -LiteralPath (Join-Path $package $file))){throw "Unexpected FFmpeg package: missing $file"}
    }
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    Copy-Item -LiteralPath (Join-Path $package 'bin\ffmpeg.exe') -Destination (Join-Path $tools 'ffmpeg.exe')
    Copy-Item -LiteralPath (Join-Path $package 'bin\ffprobe.exe') -Destination (Join-Path $tools 'ffprobe.exe')
    Copy-Item -LiteralPath (Join-Path $package 'LICENSE') -Destination (Join-Path $tools 'FFmpeg-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $package 'README.txt') -Destination (Join-Path $tools 'FFmpeg-README.txt')
    Write-Host 'Ready. Open app\CaptureAndRelease.exe.'
} finally {
    # Only remove our freshly created, uniquely named temporary directory.
    $resolvedTemporary=[IO.Path]::GetFullPath($temporary)
    $resolvedTempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolvedTemporary.StartsWith($resolvedTempRoot,[StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $resolvedTemporary -Leaf).StartsWith('CaptureAndRelease-setup-')){throw 'Refusing unsafe temporary cleanup path'}
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force -ErrorAction SilentlyContinue
}
