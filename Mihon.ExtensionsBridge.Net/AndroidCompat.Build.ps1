[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$bridgeRoot = $scriptRoot
$androidLayerRoot = [IO.Path]::Combine($bridgeRoot, 'Android.Compatibility.Layer')
$androidCompatDir = [IO.Path]::Combine($androidLayerRoot, 'AndroidCompat')

if (-not (Test-Path $androidCompatDir)) {
    throw "AndroidCompat workspace not found at $androidCompatDir"
}

$androidJarPath = [IO.Path]::Combine($androidCompatDir, 'lib','android.jar')
$androidScript = [IO.Path]::Combine($androidCompatDir, 'getAndroid.ps1')

if (-not (Test-Path $androidScript)) {
    throw "Android bootstrap script not found at $androidScript"
}

if ($Force -or -not (Test-Path $androidJarPath)) {
    Write-Host 'Recreating Android stubs...'
    $global:LASTEXITCODE = 0
    & $androidScript

    if (-not $?) {
        throw 'getAndroid.ps1 failed'
    }
} else {
    Write-Host "android.jar already present at $androidJarPath. Use -Force to recreate."
}

$IsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$gradleWrapper = if ($IsWindows) { [IO.Path]::Combine($androidLayerRoot, 'gradlew.bat') } else { [IO.Path]::Combine($androidLayerRoot, 'gradlew') }

if (-not (Test-Path $gradleWrapper)) {
    throw "Gradle wrapper not found at $gradleWrapper"
}

if (-not $IsWindows) {
    chmod +x $gradleWrapper | Out-Null
}

Push-Location $androidLayerRoot
try {
    Write-Host 'Building AndroidCompat jar with Gradle...'
    $arguments = @('fatJar', ':AndroidCompat:jar', '--no-daemon', '--stacktrace')
    $global:LASTEXITCODE = 0
    & $gradleWrapper @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Gradle build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
