FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY src/Sentinel.Dashboard/Sentinel.Dashboard.csproj Sentinel.Dashboard/
RUN dotnet restore Sentinel.Dashboard/Sentinel.Dashboard.csproj

# Copiar el resto del código y compilar web app
COPY src/Sentinel.Dashboard/ Sentinel.Dashboard/
RUN dotnet publish Sentinel.Dashboard/Sentinel.Dashboard.csproj -c Release -o /app/publish /p:UseAppHost=false

# ----------------- ETAPA DE EJECUCIÓN -----------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Cambiamos a root para instalar herramientas del sistema opertaivo
USER root

# Instalamos Python 3, FFmpeg y las herramientas de Chromaprint (fpcalc)
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
       python3 \
       python3-pip \
       ffmpeg \
       libchromaprint-tools \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copiamos la app compilada
COPY --from=build /app/publish .

# Copiamos la carpeta de scripts de Python (Sentinel.Worker)
# La pondremos directamente en /app/worker
COPY src/Sentinel.Worker /app/worker

# Damos permisos completos temporalmente para asegurar la creación de la carpeta de evidencias
RUN chmod -R 777 /app/worker
RUN mkdir -p /app/worker/evidencia && chmod -R 777 /app/worker/evidencia
RUN mkdir -p /app/wwwroot/assets && chmod -R 777 /app/wwwroot/assets

# Volvemos al usuario estándar de ASP.NET
USER app

# Variables de Entorno para orquestador
ENV WORKER_DIR="/app/worker"
ENV EVIDENCE_DIR="/app/worker/evidencia"
ENV ASPNETCORE_URLS="http://+:5010"
ENV ASPNETCORE_ENVIRONMENT="Production"

EXPOSE 5010

ENTRYPOINT ["dotnet", "Sentinel.Dashboard.dll"]
