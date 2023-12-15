#---------------------------------------------------------------------------------------------
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License. See License in the project root for license information.
#---------------------------------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path .\devproxy -ErrorAction Stop | Out-Null
Set-Location .\devproxy | Out-Null

# Get the full path of the current directory
$full_path = Resolve-Path .

# Get the latest Dev Proxy version
Write-Host "Getting latest Dev Proxy version..."
$response = Invoke-RestMethod -Uri "https://api.github.com/repos/microsoft/dev-proxy/releases/latest" -ErrorAction Stop
$version = $response.tag_name
Write-Host "Latest version is $version"

# Download Dev Proxy
Write-Host "Downloading Dev Proxy $version..."
$base_url = "https://github.com/microsoft/dev-proxy/releases/download/$version/dev-proxy"

# Check system architecture
$os = $PSVersionTable.OS
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

if ($os -match "Windows") {
    if ($arch -eq "X64") {
        $url = "$base_url-win-x64-$version.zip"
    } elseif ($arch -eq "X86") {
        $url = "$base_url-win-x86-$version.zip"
    } else {
        Write-Host "Unsupported architecture $arch. Aborting"
        exit 1
    }
} elseif ($os -match "Linux") {
    if ($arch -eq "X64") {
        $url = "$base_url-linux-x64-$version.zip"
    } else {
        Write-Host "Unsupported architecture $arch. Aborting"
        exit 1
    }
} elseif ($os -match "Darwin") {
    if ($arch -eq "X64") {
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

if (!(Test-Path $PROFILE)) {
    New-Item -ItemType File -Force -Path $PROFILE
}

if (!(Select-String -Path $PROFILE -Pattern "devproxy")) {
    Add-Content -Path $PROFILE -Value "`$env:PATH += `":$full_path`""
}

Write-Host "Dev Proxy $version installed!"
Write-Host
Write-Host "To get started, run:"
Write-Host "    . $PROFILE"
Write-Host "    devproxy --help"