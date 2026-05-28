param(
    [switch]$InstallSdk,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\EmxClips\EmxClips.csproj"
$LocalDotnetDir = Join-Path $Root ".tools\dotnet"
$LocalDotnet = Join-Path $LocalDotnetDir "dotnet.exe"

function Get-DotnetPath {
    if (Test-Path $LocalDotnet) {
        return $LocalDotnet
    }

    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

function Has-Sdk($dotnetPath) {
    if (-not $dotnetPath) {
        return $false
    }

    if (-not (Test-Path $dotnetPath)) {
        return $false
    }

    $sdks = & $dotnetPath --list-sdks
    return -not [string]::IsNullOrWhiteSpace(($sdks -join ""))
}

if ($InstallSdk -and -not (Has-Sdk $LocalDotnet)) {
    New-Item -ItemType Directory -Force -Path $LocalDotnetDir | Out-Null

    $installer = Join-Path $Root ".tools\dotnet-install.ps1"
    if (-not (Test-Path $installer)) {
        Invoke-WebRequest `
            -Uri "https://dot.net/v1/dotnet-install.ps1" `
            -OutFile $installer
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer `
        -Channel 8.0 `
        -InstallDir $LocalDotnetDir `
        -Architecture x64
}

$Dotnet = Get-DotnetPath

if (-not (Has-Sdk $Dotnet)) {
    Write-Host "No .NET SDK found."
    Write-Host "Run: .\scripts\build.ps1 -InstallSdk"
    exit 1
}

if ($Publish) {
    & $Dotnet publish $Project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true
} else {
    & $Dotnet build $Project --configuration Debug
}
