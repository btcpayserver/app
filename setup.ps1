# Check if not in a CI environment
if (-not (Test-Path Env:CI)) {
    # Initialize the server submodule
    Write-Host "Initializing and updating submodules..."
    git submodule init
    if ($LASTEXITCODE -eq 0) {
        git submodule update --recursive
    } else {
        Write-Error "git submodule init failed."
        exit 1
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "git submodule update --recursive failed."
        exit 1
    }

    # Install the workloads
    Write-Host "Restoring dotnet workloads..."
    dotnet workload restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet workload restore failed."
        exit 1
    }
}

# Create appsettings file to include app plugin when running the server
$appsettings = "submodules/btcpayserver/BTCPayServer/appsettings.dev.json"
if (-not (Test-Path $appsettings -PathType Leaf)) {
    Write-Host "Creating $appsettings..."
    $content = '{ "DEBUG_PLUGINS": "../../../BTCPayServer.Plugins.App/bin/Debug/net8.0/BTCPayServer.Plugins.App.dll" }'
    Set-Content -Path $appsettings -Value $content -Encoding UTF8
}

# Publish plugin to share its dependencies with the server
$originalLocation = Get-Location
$pluginDir = "BTCPayServer.Plugins.App"

if (Test-Path $pluginDir) {
    Write-Host "Changing directory to $pluginDir..."
    Set-Location $pluginDir

    Write-Host "Publishing plugin..."
    dotnet publish -c Debug -o "bin/Debug/net8.0"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed."
        Set-Location $originalLocation # Ensure we return to original location on error
        exit 1
    }

    Write-Host "Returning to original directory..."
    Set-Location $originalLocation
} else {
    Write-Error "Plugin directory $pluginDir not found."
    exit 1
}

Write-Host "Setup complete."
