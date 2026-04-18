$ErrorActionPreference = "Stop"

Write-Host "Descargando Chromaprint (fpcalc)..."
Invoke-WebRequest -Uri "https://github.com/acoustid/chromaprint/releases/download/v1.5.1/chromaprint-fpcalc-1.5.1-windows-x86_64.zip" -OutFile "chromaprint.zip"

Write-Host "Extrayendo Chromaprint..."
Expand-Archive -Path "chromaprint.zip" -DestinationPath "." -Force
Move-Item -Path "chromaprint-fpcalc-1.5.1-windows-x86_64\fpcalc.exe" -Destination "fpcalc.exe" -Force
Remove-Item -Path "chromaprint-fpcalc-1.5.1-windows-x86_64" -Recurse -Force
Remove-Item -Path "chromaprint.zip" -Force
Write-Host "fpcalc.exe listo."

Write-Host "Descargando FFmpeg (esto puede tomar 1 o 2 minutos)..."
# Usamos el release de GyanD que pesa menos (~35MB) y descarga mas rapido.
Invoke-WebRequest -Uri "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip" -OutFile "ffmpeg.zip"

Write-Host "Extrayendo FFmpeg..."
Expand-Archive -Path "ffmpeg.zip" -DestinationPath "." -Force
Move-Item -Path "ffmpeg-7.1-essentials_build\bin\ffmpeg.exe" -Destination "ffmpeg.exe" -Force
Move-Item -Path "ffmpeg-7.1-essentials_build\bin\ffprobe.exe" -Destination "ffprobe.exe" -Force
Remove-Item -Path "ffmpeg-7.1-essentials_build" -Recurse -Force
Remove-Item -Path "ffmpeg.zip" -Force

Write-Host "ffmpeg.exe listo!"
Write-Host "El ambiente est  ahora 100% configurado."
