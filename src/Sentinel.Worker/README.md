# AirWitness - Sentinel.Worker (The Ear)

Bienvenido a la Fase 1. Este componente está a cargo de escuchar audio en streaming infinito y buscar apariciones exactas de un patrón comercial (huella).

## Instalación de dependencias del SO

### Windows
1. Descargar **FFmpeg**: https://github.com/BtbN/FFmpeg-Builds/releases (Versión recomendada: `ffmpeg-master-latest-win64-gpl.zip`).
   - Extraer y colocar la carpeta `bin` en el Path del sistema (`C:\ffmpeg\bin`).
2. Descargar **fpcalc / Chromaprint**: https://acoustid.org/chromaprint
   - Extraer el archivo `.zip`.
   - Copiar/Mover internamente `fpcalc.exe` y asegurarte de tenerlo en el PATH de Windows o en el mismo directorio de esta aplicación, pero como vamos a usar la envoltura en C interna puede que la librería requiera el .dll también. Al instalar desde pip en windows usualmente resuelve las librerias core, pero recomendamos colocar `fpcalc.exe` en tu PATH.

### Linux (Ubuntu/Debian)
```bash
sudo apt update
sudo apt install ffmpeg libchromaprint-tools python3-pip
```

## Configuración y Ejecución

1. Entra a este directorio y activa tu entorno virtual:
```bash
python -m venv venv
venv\Scripts\activate # o en linux source venv/bin/activate
```
2. Instala dependencias Python:
```bash
pip install -r requirements.txt
```
3. Ejecutar el script:
```bash
python monitor.py --master ../../assets/comercial.mp3 --url http://193.203.20.14:8000/stream
```

*(Puedes modificar los parámetros a través de la CLI, usa `--help` para más detalles).*
