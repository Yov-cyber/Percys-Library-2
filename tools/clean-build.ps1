param(
    [string]$Configuration = "Debug"
)

# Hacer el script robusto ante errores no críticos en limpieza
$ErrorActionPreference = "Continue"

# Directorio raíz del workspace (padre de la carpeta 'tools')
$workspace = Resolve-Path (Join-Path $PSScriptRoot "..")
Write-Host "Workspace: $workspace"

# Intentar cerrar cualquier instancia en ejecución de PercysLibrary
Write-Host "Terminando procesos PercysLibrary si están en ejecución..."
try {
    Get-Process PercysLibrary -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
} catch {}

# Esperar a que ejecutables/DLL no estén bloqueados (net6 y net8)
$exePathNet6 = Join-Path $workspace "bin/Debug/net6.0-windows/PercysLibrary.exe"
$exePathNet8 = Join-Path $workspace "bin/Debug/net8.0-windows10.0.19041/PercysLibrary.exe"
$dllSkiaNet8 = Join-Path $workspace "bin/Debug/net8.0-windows10.0.19041/SkiaSharp.dll"

foreach ($p in @($exePathNet6,$exePathNet8,$dllSkiaNet8)) {
    if (Test-Path $p) {
        Write-Host "Esperando a que se libere: $p"
        for ($i = 0; $i -lt 50; $i++) {
            try {
                $fs = [System.IO.File]::Open($p, 'Open', 'ReadWrite', 'None')
                $fs.Close()
                Write-Host "Archivo liberado: $p"
                break
            } catch {
                Start-Sleep -Milliseconds 200
            }
        }
    }
}

# Intentar detectar procesos .NET que tengan módulos cargados desde nuestro bin Debug net8
try {
    $binPath = (Join-Path $workspace "bin/Debug/net8.0-windows10.0.19041")
    Write-Host "Buscando procesos que bloquean archivos en: $binPath"
    $lockers = @()
    foreach ($proc in Get-Process -ErrorAction SilentlyContinue) {
        try {
            if ($proc.Modules | Where-Object { $_.FileName -like "$binPath*" }) {
                $lockers += $proc
            }
        } catch { }
    }
    if ($lockers.Count -gt 0) {
        Write-Host ("Deteniendo procesos que bloquean bin: {0}" -f (($lockers | ForEach-Object { $_.Name + ' (' + $_.Id + ')' }) -join ', '))
        $lockers | Stop-Process -Force -ErrorAction SilentlyContinue
    }
} catch { }

# Limpiar bin y obj en todo el árbol
Write-Host "Eliminando carpetas bin/obj..."
Get-ChildItem -Path $workspace -Directory -Include bin,obj -Recurse -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Limpiar posibles restos en %LOCALAPPDATA%\PercysLibrary (si se usó salida redirigida)
$localOut = Join-Path $env:LOCALAPPDATA "PercysLibrary"
if (Test-Path $localOut) {
    Write-Host "Eliminando salida local: $localOut"
    try { Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $localOut } catch {}
}

# Apagar servidores de compilación para evitar reutilización de caché/rutas antiguas
Write-Host "dotnet build-server shutdown..."
dotnet build-server shutdown | Out-Null

# Limpiar y compilar solución
Write-Host "dotnet clean..."
dotnet clean (Join-Path $workspace "ComicReader.sln") -c $Configuration

Write-Host "dotnet build..."
dotnet build (Join-Path $workspace "ComicReader.sln") -c $Configuration
