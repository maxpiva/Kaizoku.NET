$frontendPath = "./KaizokuFrontend"
$backendPath = "./KaizokuBackend"

$frontendOutput = Join-Path $frontendPath "out"
$backendWWWZip = Join-Path $backendPath "wwwroot.zip"
$backendWWWHash = Join-Path $backendPath "wwwroot.sha256"

Write-Host "Building frontend..." -ForegroundColor Cyan
Push-Location $frontendPath
npm i -g pnpm
pnpm install
Remove-Item "out" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".next" -Recurse -Force -ErrorAction SilentlyContinue
npm run build
Pop-Location
if (Test-Path $backendWWWZip) {
    Remove-Item $backendWWWZip
}
Write-Host "Copying frontend files to Backend..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $frontendOutput '*') -DestinationPath $backendWWWZip
$hash = Get-FileHash -Path $backendWWWZip -Algorithm SHA256
$hash.Hash | Out-File -FilePath $backendWWWHash -Encoding ASCII
