# 📖 Glosario Técnico: AirWitness Dictionary

Este documento define la terminología específica utilizada en el desarrollo y operación de la plataforma **AirWitness PRO**.

---

### 🟢 A
- **Acoustic Fingerprint (Huella Acústica)**: Un resumen digital condensado de una señal de audio que permite su identificación sin necesidad de metadatos.
- **ApplicationDbContext**: Clase de Entity Framework Core que actúa como el puente principal entre el código C# y la base de datos PostgreSQL.

### 🟢 C
- **Confidence Score (Puntuación de Confianza)**: Un valor entre 0 y 1 que indica qué tan probable es que una detección sea correcta. En AirWitness, valores > 0.15 suelen considerarse hits válidos.
- **CustomAuthStateProvider**: Servicio encargado de gestionar quién puede entrar al "grid" de monitoreo, integrando la seguridad MasterPassword.

### 🟢 E
- **Evidence Buffer (Búfer de Evidencia)**: Segmento de memoria que almacena temporalmente los samples de audio antes, durante y después de un match para generar el archivo MP3 final.
- **Executive Neon UI**: El sistema de diseño premium utilizado en AirWitness, basado en CSS moderno, glassmorphism y animaciones dinámicas.

### 🟢 F
- **FFmpeg**: Motor multimedia industrial utilizado por AirWitness para sintonizar (tuning) las radios de internet y decodificar el audio a formato PCM Float32.
- **fpcalc (Chromaprint)**: Herramienta de línea de comandos para generar huellas de audio (usada en la versión v1, ahora integrada nativamente en el v3 Kernel).

### 🟢 H
- **HLS (HTTP Live Streaming)**: Protocolo común de transmisión de audio por internet que AirWitness soporta para el monitoreo de radios modernas.

### 🟢 K
- **Kernel Nativo**: El corazón del sistema v3 donde SoundFingerprinting corre directo en .NET, eliminando la latencia de procesos externos.

### 🟢 M
- **Master Audio**: El archivo MP3 de referencia (comercial) que el sistema busca incansablemente en todas las señales de radio activas.
- **Match Record**: Registro histórico guardado en PostgreSQL que certifica que un comercial sonó en una radio específica a una hora determinada.

### 🟢 P
- **PCM Float32 (5512Hz)**: El formato de audio "crudo" que procesa el Kernel. 32 bits de precisión a una frecuencia optimizada para detección de audio, no para escucha humana.
- **Post-roll**: Tiempo extra de audio capturado *después* de que el comercial termina para verificar el contexto de salida.

### 🟢 S
- **SoundFingerprinting**: La librería de procesamiento digital de señales (DSP) que impulsa la detección acústica en AirWitness.
- **Stream Tuner**: Componente que se conecta a las URLs de radio y mantiene la conexión viva frente a micro-cortes de red.

### 🟢 W
- **WorkerOrchestrator**: El director de orquesta que inicia, detiene y vigila todos los hilos de monitoreo individuales.
