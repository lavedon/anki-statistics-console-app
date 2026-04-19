dotnet publish -c Release --arch x64 --nologo --self-contained true

$src = "C:\my-coding-projects\ankiStats\bin\Release\net10.0\win-x64\publish\ankiStats.exe"
$dst = "C:\tools\as.exe"

try {
    Copy-Item -Path $src -Destination $dst -Force
    copy-Item -Path "C:\my-coding-projects\ankiStats\bin\Debug\net10.0\Data\leetcode.db" -Destination "C:\Tools\Data" -Force
    Write-Host "Published to $dst" -ForegroundColor Green
} catch {
    Write-Host "File locked, stopping ankiStats process..." -ForegroundColor Yellow
    Stop-Process -Name "ankiStats" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Copy-Item -Path $src -Destination $dst -Force
    Write-Host "Published to $dst" -ForegroundColor Green
}
