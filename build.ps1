# Builds Samurai GBA and its installer. Requires: .NET 8 SDK (Windows) and Inno Setup 6.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish SamuraiGBA.csproj -c Release -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Bundle the mGBA libretro core so the app works offline (otherwise it offers to download it on first launch).
try {
    $zip = Join-Path $env:TEMP "mgba_libretro.zip"
    Invoke-WebRequest "https://buildbot.libretro.com/nightly/windows/x86_64/latest/mgba_libretro.dll.zip" -OutFile $zip
    Expand-Archive $zip -DestinationPath publish -Force
} catch {
    Write-Warning "Could not bundle the core ($($_.Exception.Message)). The app will download it on first launch."
}

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($iscc) { $isccPath = $iscc.Source }
else {
    $isccPath = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $isccPath) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php" }

& $isccPath "installer\SamuraiGBA.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "`nDone: installer\Output\SamuraiGBA-Setup.exe"
