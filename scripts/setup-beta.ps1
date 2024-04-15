#---------------------------------------------------------------------------------------------
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License. See License in the project root for license information.
#---------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "This script installs Dev Proxy on your machine. It runs the following steps:"
Write-Host ""
Write-Host "1. Create the 'devproxy' directory in the current working folder"
Write-Host "2. Download the latest beta Dev Proxy release"
Write-Host "3. Unzip the release in the devproxy directory"
Write-Host "4. Configure devproxy and its files as executable (Linux and macOS only)"
Write-Host "5. Configure new version notifications for the beta channel"
Write-Host "6. Add the devproxy directory to your PATH environment variable in `$PROFILE.CurrentUserAllHosts"
Write-Host ""
Write-Host "Continue (y/n)? " -NoNewline
$response = [System.Console]::ReadKey().KeyChar

if ($response -notin @('y', 'Y')) {
    Write-Host "`nExiting"
    exit 1
}

Write-Host "`n"

New-Item -ItemType Directory -Force -Path .\devproxy -ErrorAction Stop | Out-Null
Set-Location .\devproxy | Out-Null

# Get the full path of the current directory
$full_path = Resolve-Path .

# Get the latest beta Dev Proxy version
Write-Host "Getting latest beta Dev Proxy version..."
$response = Invoke-RestMethod -Uri "https://api.github.com/repos/microsoft/dev-proxy/releases?per_page=1" -ErrorAction Stop
$version = $response[0].tag_name
Write-Host "Latest beta version is $version"

# Download Dev Proxy
Write-Host "Downloading Dev Proxy $version..."
$base_url = "https://github.com/microsoft/dev-proxy/releases/download/$version/dev-proxy"

# Check system architecture
# empty in Windows PowerShell
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
# fallback for Windows PowerShell
$os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
$isX64 = [Environment]::Is64BitOperatingSystem

if ($os -match "Windows") {
    if ($isX64) {
        $url = "$base_url-win-x64-$version.zip"
    } else {
        $url = "$base_url-win-x86-$version.zip"
    }
} elseif ($IsLinux) {
    if ($arch -eq "X64") {
        $url = "$base_url-linux-x64-$version.zip"
    } elseif ($arch -eq "Arm64") {
        $url = "$base_url-linux-arm64-$version.zip"
    } else {
        Write-Host "Unsupported architecture $arch. Aborting"
        exit 1
    }
} elseif ($IsMacOS) {
    # temporary workaround to install devproxy on Mx macs
    if ($arch -eq "X64" -or $arch -eq "Arm64") {
        $url = "$base_url-osx-x64-$version.zip"
    } else {
        Write-Host "Unsupported architecture $arch. Aborting"
        exit 1
    }
} else {
    Write-Host "Unsupported OS $os. Aborting"
    exit 1
}

Invoke-WebRequest -Uri $url -OutFile devproxy.zip -ErrorAction Stop
Add-Type -AssemblyName System.IO.Compression.FileSystem
Expand-Archive -Path devproxy.zip -DestinationPath . -Force -ErrorAction Stop
Remove-Item devproxy.zip

if ($IsLinux -or $IsMacOS) {
    Write-Host "Configuring devproxy and its files as executable..."
    chmod +x ./devproxy ./libe_sqlite3.dylib
}

Write-Host "Configuring new version notifications for the beta channel..."
(Get-Content -Path devproxyrc.json) -replace '"newVersionNotification": "stable"', '"newVersionNotification": "beta"' | Set-Content -Path devproxyrc.json

if (!(Test-Path $PROFILE.CurrentUserAllHosts)) {
    Write-Host "Creating `$PROFILE.CurrentUserAllHosts..."
    New-Item -ItemType File -Force -Path $PROFILE.CurrentUserAllHosts | Out-Null
}

if (!(Select-String -Path $PROFILE.CurrentUserAllHosts -Pattern "devproxy")) {
    Write-Host "Adding devproxy to `$PROFILE.CurrentUserAllHosts..."
    Add-Content -Path $PROFILE.CurrentUserAllHosts -Value "$([Environment]::NewLine)`$env:PATH += `"$([IO.Path]::PathSeparator)$full_path`""
}

Write-Host "Dev Proxy $version installed!"
Write-Host
Write-Host "To get started, run:"
Write-Host "    . `$PROFILE.CurrentUserAllHosts"
Write-Host "    devproxy --help"