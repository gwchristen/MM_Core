<#
.SYNOPSIS
  Installs .NET 8 SDK (if needed), configures NuGet, and adds required packages to a WPF project.
  Optionally installs Visual Studio VSIX extensions.

.PARAMETER Project
  Path to your .csproj (default: CmdRunnerPro.csproj)

.PARAMETER Proxy
  Optional HTTP/HTTPS proxy (e.g. http://user:pass@host:port). Sets HTTP_PROXY/HTTPS_PROXY for the session.

.PARAMETER VsixPaths
  Optional array of .vsix file paths to install (if you really meant Visual Studio extensions).
  Example: -VsixPaths 'C:\temp\SomeExtension.vsix','C:\temp\Another.vsix'

.PARAMETER Retries
  Retry attempts for restore/build (default: 2)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install-deps.ps1 -Project .\CmdRunnerPro.csproj -Verbose

.EXAMPLE (behind proxy)
  .\install-deps.ps1 -Project .\CmdRunnerPro.csproj -Proxy 'http://proxy.mycorp.local:8080' -Verbose

#>

[CmdletBinding()]
param(
  [string]$Project = ".\CmdRunnerPro.csproj",
  [string]$Proxy,
  [string[]]$VsixPaths,
  [int]$Retries = 2
)

$ErrorActionPreference = 'Stop'

function Write-Section($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

function Ensure-Tls12 {
  # NuGet.org requires modern TLS; enforce .NET to use TLS 1.2+
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
}

function Set-ProxyIfSpecified {
  param([string]$Proxy)
  if ($Proxy) {
    Write-Host "Using proxy: $Proxy" -ForegroundColor Yellow
    $env:HTTP_PROXY = $Proxy
    $env:HTTPS_PROXY = $Proxy
  }
}

function Ensure-DotNet8 {
  Write-Section "Checking .NET SDK"
  $sdks = (& dotnet --list-sdks) 2>$null
  if ($LASTEXITCODE -ne 0) { $sdks = @() }

  if ($sdks -match '^8\.') {
    Write-Host ".NET 8 SDK is installed." -ForegroundColor Green
    return
  }

  Write-Host ".NET 8 SDK not found. Installing via winget..." -ForegroundColor Yellow
  $winget = Get-Command winget -ErrorAction SilentlyContinue
  if (-not $winget) {
    throw "winget is not available. Please install .NET 8 SDK manually from https://dotnet.microsoft.com/download"
  }

  # Winget ID for .NET 8 SDK
  & winget install --id Microsoft.DotNet.SDK.8 --silent --accept-source-agreements --accept-package-agreements
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to install .NET 8 SDK via winget."
  }
}

function Ensure-NuGetSource {
  Write-Section "Configuring NuGet sources"
  $sources = & dotnet nuget list source --format short
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to list NuGet sources. Ensure dotnet SDK is installed and PATH is set."
  }

  if ($sources -notmatch 'nuget.org') {
    Write-Host "Adding nuget.org source…" -ForegroundColor Yellow
    & dotnet nuget add source "https://api.nuget.org/v3/index.json" -n "nuget.org" | Out-Null
  } else {
    Write-Host "nuget.org source already present." -ForegroundColor Green
  }

  # Enable the source in case it's disabled
  & dotnet nuget enable source "nuget.org" | Out-Null
}

function DotNet-WithRetry {
  param(
    [Parameter(Mandatory)] [string[]]$Args,
    [int]$Retries = 2
  )
  $attempt = 0
  while ($true) {
    $attempt++
    & dotnet @Args
    if ($LASTEXITCODE -eq 0) { return }
    if ($attempt -gt $Retries) {
      throw "dotnet $($Args -join ' ') failed after $Retries retries."
    }
    Write-Warning "dotnet $($Args -join ' ') failed (attempt $attempt). Retrying in 3s..."
    Start-Sleep -Seconds 3
  }
}

function Ensure-Packages {
  param([string]$ProjectPath)

  Write-Section "Adding packages to $ProjectPath"
  if (-not (Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
  }

  # Restore first (useful if the project already references packages)
  DotNet-WithRetry -Args @('restore', $ProjectPath) -Retries $Retries

  # Add required packages (no fixed versions so NuGet resolves latest stable compatible)
  DotNet-WithRetry -Args @('add', $ProjectPath, 'package', 'MaterialDesignThemes') -Retries $Retries
  DotNet-WithRetry -Args @('add', $ProjectPath, 'package', 'System.IO.Ports') -Retries $Retries

  Write-Section "Final restore + build"
  DotNet-WithRetry -Args @('restore', $ProjectPath) -Retries $Retries
  DotNet-WithRetry -Args @('build', $ProjectPath, '-c', 'Debug', '-v', 'm') -Retries $Retries

  Write-Host "Packages installed and project built successfully." -ForegroundColor Green
  Write-Section "Installed packages"
  & dotnet list $ProjectPath package
}

function Install-VSIX {
  param([string[]]$VsixPaths)

  if (-not $VsixPaths -or $VsixPaths.Count -eq 0) { return }

  Write-Section "Installing VSIX extensions"
  # Locate VSIXInstaller.exe via vswhere (installed with VS)
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found. Install Visual Studio 2022 or VS Build Tools."
  }

  $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
  if (-not $installPath) {
    throw "Visual Studio installation not found."
  }

  $vsixInstaller = Join-Path $installPath 'Common7\IDE\VSIXInstaller.exe'
  if (-not (Test-Path $vsixInstaller)) {
    throw "VSIXInstaller.exe not found under $installPath"
  }

  foreach ($vsix in $VsixPaths) {
    if (-not (Test-Path $vsix)) { throw "VSIX not found: $vsix" }
    Write-Host "Installing $vsix..." -ForegroundColor Yellow
    & $vsixInstaller /quiet $vsix
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to install VSIX: $vsix (exit $LASTEXITCODE)"
    }
  }
  Write-Host "VSIX install complete." -ForegroundColor Green
}

# ----------------- Main -----------------
try {
  Ensure-Tls12
  Set-ProxyIfSpecified -Proxy $Proxy
  Ensure-DotNet8
  Ensure-NuGetSource
  Ensure-Packages -ProjectPath (Resolve-Path $Project).Path
  Install-VSIX -VsixPaths $VsixPaths
  Write-Host "`nAll done." -ForegroundColor Green
}
catch {
  Write-Error $_.Exception.Message
  exit 1
}