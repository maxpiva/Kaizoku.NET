Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$bridgeRoot = $scriptRoot

$androidJar = [IO.Path]::Combine($bridgeRoot, 'Android.Compatibility.Layer', 'AndroidCompat', 'build', 'libs', 'AndroidCompat-1.0-all.jar')
if (-not (Test-Path $androidJar)) {
    Write-Warning "AndroidCompat fat jar not found at $androidJar. Run AndroidCompat.Build.ps1 first."
    exit 1
}

$builderDir = [IO.Path]::Combine($bridgeRoot, 'IKVM.Android.Compatibility.Layer.Builder')
$patcherDir = [IO.Path]::Combine($bridgeRoot, 'IKVM.Android.Compatibility.Layer.CILPatcher')

Push-Location $builderDir
try {
    Write-Host 'Building IKVM.Android.Compatibility.Layer.Builder (Release)...'
    dotnet build -c Release 
}
finally {
    Pop-Location
}

$builderOutput = [IO.Path]::Combine($builderDir, 'bin', 'Release', 'net8.0', 'Android.Compat.dll')
if (-not (Test-Path $builderOutput)) {
    Write-Warning "Builder output not found at $builderOutput."
    exit 1
}

Push-Location $patcherDir
try {
    Write-Host 'Building IKVM.Android.Compatibility.Layer.CILPatcher (Release)...'
    dotnet build -c Release 
}
finally {
    Pop-Location
}

$patcherExe = [IO.Path]::Combine($patcherDir, 'bin', 'Release', 'net8.0', 'IKVM.Android.Compatibility.Layer.CILPatcher.exe')
if (-not (Test-Path $patcherExe)) {
    Write-Warning "CIL patcher executable not found at $patcherExe."
    exit 1
}

$libsDir = [IO.Path]::Combine($bridgeRoot, 'libs')
New-Item -ItemType Directory -Path $libsDir -Force | Out-Null
$patchedDll = [IO.Path]::Combine($libsDir, 'Android.Compat.dll')

Write-Host 'Running CIL patcher...'
$global:LASTEXITCODE = 0
& $patcherExe $builderOutput $patchedDll
if ($LASTEXITCODE -ne 0) {
    Write-Warning "CIL patcher exited with code $LASTEXITCODE."
    exit $LASTEXITCODE
}

if (Test-Path $patchedDll) {
    Write-Host "Android.Compat.dll successfully generated at $patchedDll"
} else {
    Write-Warning "Android.Compat.dll was not created at $patchedDll."
    exit 1
}
