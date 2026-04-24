# Asociar Percy’s Library como app predeterminada

Puedes asociar Percy’s Library para abrir archivos compatibles (cbz, cbr, cb7, cbt, zip, rar, 7z, tar, pdf, epub, jpg, jpeg, png, gif, bmp, webp, heic, tif, tiff, avif).

Opciones:

- Método recomendado (Windows UI):
  1. Clic derecho sobre un archivo (por ejemplo, .cbz) → Abrir con → Elegir otra aplicación.
  2. Marcar “Usar siempre esta aplicación para abrir .cbz”.
  3. Buscar Percy's Library (PercysLibrary.exe) en `bin/Debug/net6.0-windows` o la carpeta donde tengas el .exe publicado.
  4. Repetir para otras extensiones que quieras asociar.

- Método avanzado (Símbolo del sistema/PowerShell) con ProgID personalizado:
  1. Publica o apunta al exe: `C:\Ruta\PercysLibrary.exe`.
  2. Abre PowerShell como administrador y define el ProgID y comandos:

     Nota: reemplaza la ruta al exe por la tuya.

     ```powershell
     $exe = "C:\\Ruta\\PercysLibrary.exe"
     $progId = "PercysLibrary.File"

     # Registrar comando de apertura
     New-Item -Path "HKCU:\Software\Classes\$progId" -Force | Out-Null
     New-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "FriendlyTypeName" -Value "Archivo de Percy’s Library" -PropertyType String -Force | Out-Null

     New-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Force | Out-Null
     New-ItemProperty -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Name "(default)" -Value ('"' + $exe + '" "%1"') -PropertyType String -Force | Out-Null

     # Opcional: icono
     New-Item -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Force | Out-Null
     New-ItemProperty -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Name "(default)" -Value ($exe + ",0") -PropertyType String -Force | Out-Null

     # Asociar extensiones deseadas al ProgID (usuario actual)
     $exts = @(".cbz",".cbr",".cb7",".cbt",".zip",".rar",".7z",".tar",".pdf",".epub",".jpg",".jpeg",".png",".gif",".bmp",".webp",".heic",".tif",".tiff",".avif")
     foreach ($ext in $exts) {
       New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
       New-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name "(default)" -Value $progId -PropertyType String -Force | Out-Null
     }

     Write-Host "Asociaciones creadas para el usuario actual. Puede que tengas que reiniciar el explorador o sesión."
     ```

  3. Después de esto, doble clic en los archivos abrirá con Percy’s Library. Si Windows muestra un aviso de elección de app, selecciona Percy’s Library y marca “Usar siempre”.

Seguridad y Reversión
- Este script escribe en HKCU (usuario actual), no requiere privilegios de administrador para esa rama. Aun así, úsalo con cuidado.
- Para revertir, elimina las claves en `HKCU:\Software\Classes` para el ProgID y extensiones afectadas o reasocia desde “Aplicaciones predeterminadas”.

Soporte de parámetros
- La app ya soporta abrir un archivo pasado por línea de comandos: `PercysLibrary.exe "C:\ruta\archivo.cbz"`.