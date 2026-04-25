# Script para iniciar el túnel de AirWitness y obtener la URL pública
# Asegúrate de tener instalado cloudflared

$Port = 5010
$LogFile = "tunnel.log"

Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host "   AIRWITNESS PRO - INICIADOR DE TÚNEL REMOTO       " -ForegroundColor White -BackgroundColor Blue
Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host "Iniciando túnel hacia http://localhost:$Port..."

# Eliminar log anterior si existe
if (Test-Path $LogFile) { Remove-Item $LogFile }

# Iniciar cloudflared en segundo plano redirigiendo el error (donde sale la URL) al log
Start-Process cloudflared -ArgumentList "tunnel --url http://localhost:$Port" -RedirectStandardError $LogFile -NoNewWindow

Write-Host "Esperando a que Cloudflare genere la URL..." -ForegroundColor Yellow

# Esperar unos segundos a que aparezca la URL en el log
$Url = $null
$Timeout = 15
$Elapsed = 0

while ($null -eq $Url -and $Elapsed -lt $Timeout) {
    Start-Sleep -Seconds 1
    if (Test-Path $LogFile) {
        $Content = Get-Content $LogFile
        $Line = $Content | Select-String -Pattern "https://.*\.trycloudflare\.com"
        if ($Line) {
            $Url = $Line.Matches.Value
        }
    }
    $Elapsed++
}

if ($Url) {
    Write-Host "`n====================================================" -ForegroundColor Green
    Write-Host "  URL PÚBLICA ACTIVA:" -ForegroundColor White
    Write-Host "  $Url" -ForegroundColor Cyan -BackgroundColor Black
    Write-Host "====================================================`n" -ForegroundColor Green
    Write-Host "Presiona CTRL+C para cerrar el túnel cuando termines."
    
    # Mantener el script vivo y mostrando el log por si hay errores
    Get-Content $LogFile -Wait
} else {
    Write-Host "`nError: No se pudo obtener la URL de Cloudflare." -ForegroundColor Red
    Write-Host "Asegúrate de que 'cloudflared' esté instalado y que no haya otro túnel usando el puerto."
    if (Test-Path $LogFile) {
        Write-Host "Contenido del log:"
        Get-Content $LogFile
    }
}
