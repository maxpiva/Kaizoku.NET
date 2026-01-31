[CmdletBinding()]
param(
    [string]$Destination = $null,
    [switch]$Force,
    [string]$SourceRef = "master"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$bridgeRoot = (Resolve-Path ([IO.Path]::Combine($scriptRoot, '..'))).Path
$IsWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

if (-not $Destination) {
    $Destination = [IO.Path]::Combine($bridgeRoot, 'build', 'artifacts', 'androidcompat')
}

$destinationJar = [IO.Path]::Combine($Destination, 'androidcompat.jar')
$refMarker = [IO.Path]::Combine($Destination, 'source-ref.txt')
$shouldRebuild = $Force -or -not (Test-Path $destinationJar)

if (-not $shouldRebuild) {
    if (-not (Test-Path $refMarker)) {
        $shouldRebuild = $true
    } else {
        $existingRef = Get-Content -Path $refMarker -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($existingRef -ne $SourceRef) {
            $shouldRebuild = $true
        }
    }
}

if (-not $shouldRebuild) {
    Write-Host "AndroidCompat jar already exists at $destinationJar for ref $SourceRef. Use -Force to rebuild."
    return
}

$fetchScript = [IO.Path]::Combine($scriptRoot, 'ensure-suwayomi-sources.ps1')
if (-not (Test-Path $fetchScript)) {
    throw "Source fetcher not found at $fetchScript"
}

$fetchArgs = @{ SourceRef = $SourceRef; CacheRoot = [IO.Path]::Combine($bridgeRoot, 'build', 'cache') }
if ($Force) {
    $fetchArgs.Force = $true
}

$repoRoot = & $fetchScript @fetchArgs
if (-not $repoRoot) {
    throw "Failed to prepare Suwayomi sources for ref $SourceRef."
}

$repoRoot = (Resolve-Path $repoRoot).Path

$replacementsRoot = [IO.Path]::Combine($bridgeRoot, 'Replacements')
if (Test-Path $replacementsRoot) {
    Write-Host 'Applying replacement files to prepared sources...'
    $replacementItems = Get-ChildItem -Path $replacementsRoot -Force
    foreach ($item in $replacementItems) {
        $destinationPath = [IO.Path]::Combine($repoRoot, $item.Name)
        if ($item.PSIsContainer) {
            if (-not (Test-Path $destinationPath)) {
                New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
            }

            $childItems = @(Get-ChildItem -Path $item.FullName -Force)
            if ($childItems.Count -gt 0) {
                $wildcardPath = Join-Path $item.FullName '*'
                Copy-Item -Path $wildcardPath -Destination $destinationPath -Recurse -Force
            }
        } else {
            $parentDirectory = Split-Path -Parent $destinationPath
            if ($parentDirectory -and -not (Test-Path $parentDirectory)) {
                New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
            }
            Copy-Item -Path $item.FullName -Destination $destinationPath -Force
        }
    }
}

$androidLibDir = [IO.Path]::Combine($repoRoot, 'AndroidCompat', 'lib')
New-Item -ItemType Directory -Path $androidLibDir -Force | Out-Null

$libsVersionsPath = [IO.Path]::Combine($repoRoot, 'gradle', 'libs.versions.toml')
if (Test-Path $libsVersionsPath) {
    $originalContent = Get-Content -Path $libsVersionsPath -Raw
    $updatedContent = $originalContent -replace 'jvmTarget\s*=\s*"[^"]+"', 'jvmTarget = "1.8"'
    if ($updatedContent -ne $originalContent) {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($libsVersionsPath, $updatedContent, $utf8NoBom)
        Write-Host 'Adjusted Gradle JVM target to Java 8 for AndroidCompat workspace.'
    }
}

$gradleWrapper = if ($IsWindows) { [IO.Path]::Combine($repoRoot, 'gradlew.bat') } else { [IO.Path]::Combine($repoRoot, 'gradlew') }
if (-not (Test-Path $gradleWrapper)) {
    throw "Gradle wrapper not found in prepared sources at $gradleWrapper"
}

if (-not $IsWindows) {
    chmod +x $gradleWrapper | Out-Null
}

$androidScript = [IO.Path]::Combine($repoRoot, 'AndroidCompat', 'getAndroid.ps1')
if (-not (Test-Path $androidScript)) {
    throw "AndroidCompat bootstrap script not found at $androidScript"
}

Push-Location $repoRoot
try {
    Write-Host 'Preparing Android stubs...'
    $global:LASTEXITCODE = 0
    & $androidScript

    if (-not $?) {
        throw 'getAndroid.ps1 failed'
    }

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

$libsDir = [IO.Path]::Combine($repoRoot, 'AndroidCompat', 'build', 'libs')
if (-not (Test-Path $libsDir)) {
    throw "AndroidCompat build output not found at $libsDir"
}

$fatJar = Get-ChildItem -Path $libsDir -Filter '*-all.jar' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$jar = if ($fatJar) { $fatJar } else { Get-ChildItem -Path $libsDir -Filter '*.jar' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 }

if (-not $jar) {
    throw "No AndroidCompat jar produced in $libsDir"
}

if (-not $fatJar) {
    Write-Warning 'Fat jar not found; using latest jar as a fallback.'
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item $jar.FullName -Destination $destinationJar -Force
Set-Content -Path $refMarker -Value $SourceRef

Write-Host "AndroidCompat jar copied to $destinationJar"
