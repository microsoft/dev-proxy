$versionString = "v0.17.0-beta.4"
$version = $versionString.Substring(1)
$isBeta = $version.Contains("-beta")

# Remove old installer if any
Get-Item ../bld/*installer* -ErrorAction SilentlyContinue | Remove-Item

if (-not (Test-Path ../bld)) {
    Write-Error "Build directory not found. Run local-build.ps1 first."
    exit 1
}

if ($isBeta) {
    # Rename executable for beta
    Rename-Item -Path ../bld/devproxy.exe -NewName devproxy-beta.exe
}

# Add installer icon
Copy-Item ../media/icon.ico ../bld/icon.ico

# Set installer filename
$installer = $isBeta ? "install-beta.iss" : "install.iss"

# Copy installer file
Copy-Item "../$installer" "../bld/$installer"

# Set version in installer script
if ($isBeta) {
    (Get-Content "../bld/$installer") -replace "#define MyAppVersion .*", "#define MyAppVersion `"$version`"" | Set-Content "../bld/$installer"
}


ISCC.exe "../bld/$installer" /F"dev-proxy-installer-win-x64-$versionString"