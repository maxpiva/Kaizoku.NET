$backendPath = "./KaizokuBackend"
$trayPath = "./KaizokuTray"
$project = "KaizokuBackend.csproj"
$projectTray = "KaizokuTray.csproj"

$trayOutputBase = "bin/App"

# Build Backend for all targets
Push-Location $backendPath
dotnet restore $project

$runtimeIds = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64")

foreach ($rid in $runtimeIds) {
    dotnet publish $project -c Release -r $rid
}
Pop-Location

# Build Tray as self-contained single file for all targets
Push-Location $trayPath

foreach ($rid in $runtimeIds) {
    $outputPath = "$trayOutputBase/$rid"
    dotnet publish $projectTray -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugSymbols=false `
        -o $outputPath
}

# Remove unneeded runtimeconfig.json
foreach ($rid in $runtimeIds) {
    $jsonPath = "$trayOutputBase/$rid/KaizokuBackend.runtimeconfig.json"
    if (Test-Path $jsonPath) {
        Remove-Item $jsonPath -Force
    }
}


$trayBaseName = "KaizokuTray"
$finalName = "Kaizoku.NET"

$version = $null

foreach ($rid in $runtimeIds) {
    $outputPath = "$trayOutputBase/$rid"
    $isWindows = $rid -like "win-*"
    $ext = if ($isWindows) { ".exe" } else { "" }

    $oldExe = Join-Path $outputPath "$trayBaseName$ext"
    $newExe = Join-Path $outputPath "$finalName$ext"

    # Rename Tray to Final
    if (Test-Path $oldExe) {
        Rename-Item -Path $oldExe -NewName (Split-Path $newExe -Leaf)
    }

    # Extract version from win-x64 only
    if ($rid -eq "win-x64" -and -not $version) {
        $exePath = Resolve-Path $newExe
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
    }
}


# Create ZIPs using the retrieved version
foreach ($rid in $runtimeIds) {
    $outputPath = "$trayOutputBase/$rid"
    $zipName = "bin/Kaizoku.NET-$rid-v$version.zip"

    if (Test-Path $zipName) {
        Remove-Item $zipName -Force
    }

    Compress-Archive -Path "$outputPath/*" -DestinationPath $zipName
}
Pop-Location
