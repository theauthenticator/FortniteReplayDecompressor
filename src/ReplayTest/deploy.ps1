# Quick Deploy - Run from ReplayTest project directory
# Usage: .\deploy.ps1

$ParserDir = "C:\opt\parser"
$ReplayDir = "C:\replays"

Write-Host "`n🚀 Quick Deploy - Fortnite Replay Parser" -ForegroundColor Cyan
Write-Host "======================================`n" -ForegroundColor Cyan

# Find .csproj in current directory
$csproj = Get-ChildItem -Filter "*.csproj" | Select-Object -First 1
if (!$csproj) {
    Write-Host "❌ No .csproj found in current directory" -ForegroundColor Red
    Write-Host "   Run this script from the ReplayTest directory" -ForegroundColor Yellow
    exit 1
}

Write-Host "📁 Found: $($csproj.Name)`n" -ForegroundColor Green

# Create directories
Write-Host "📂 Setting up directories..." -ForegroundColor Cyan
@($ParserDir, $ReplayDir) | ForEach-Object {
    if (!(Test-Path $_)) {
        New-Item -ItemType Directory -Path $_ -Force | Out-Null
        Write-Host "   ✅ Created: $_" -ForegroundColor Green
    }
}

# Build
Write-Host "`n🔨 Building..." -ForegroundColor Cyan
dotnet publish $csproj.FullName `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $ParserDir `
    /p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n✅ Build complete!" -ForegroundColor Green

# Verify
$exePath = Join-Path $ParserDir "ReplayTest.exe"
if (Test-Path $exePath) {
    $size = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
    Write-Host "✅ ReplayTest.exe deployed ($size MB)" -ForegroundColor Green
} else {
    Write-Host "❌ ReplayTest.exe not found!" -ForegroundColor Red
    exit 1
}

# Test
Write-Host "`n🧪 Testing parser..." -ForegroundColor Cyan
$env:ASPNETCORE_URLS = "http://localhost:5001"
$env:REPLAY_DIRECTORY = $ReplayDir

$job = Start-Job -ScriptBlock {
    param($exe, $envUrls, $envReplay)
    $env:ASPNETCORE_URLS = $envUrls
    $env:REPLAY_DIRECTORY = $envReplay
    & $exe --service
} -ArgumentList $exePath, "http://localhost:5001", $ReplayDir

Start-Sleep -Seconds 3

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5001/health" -TimeoutSec 5 -ErrorAction Stop
    if ($response.StatusCode -eq 200) {
        Write-Host "   ✅ Parser is running!" -ForegroundColor Green
    }
} catch {
    Write-Host "   ⚠️  Parser health check failed (this is OK if not configured)" -ForegroundColor Yellow
} finally {
    Stop-Job -Job $job -ErrorAction SilentlyContinue
    Remove-Job -Job $job -ErrorAction SilentlyContinue
}

# Summary
Write-Host "`n" -NoNewline
Write-Host "=" * 50 -ForegroundColor Cyan
Write-Host "✅ Deployment Complete!" -ForegroundColor Green
Write-Host "=" * 50 -ForegroundColor Cyan
Write-Host ""
Write-Host "Parser Location: " -NoNewline -ForegroundColor White
Write-Host $ParserDir -ForegroundColor Yellow
Write-Host "Replay Location: " -NoNewline -ForegroundColor White
Write-Host $ReplayDir -ForegroundColor Yellow
Write-Host ""
Write-Host "Add to your .env file:" -ForegroundColor White
Write-Host "PARSER_DIR=$ParserDir" -ForegroundColor Gray
Write-Host "REPLAY_DIR=$ReplayDir" -ForegroundColor Gray
Write-Host "PARSER_EXE=ReplayTest.exe" -ForegroundColor Gray
Write-Host ""