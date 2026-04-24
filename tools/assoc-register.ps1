Param(
  [string]$ExePath = "$PSScriptRoot\..\bin\Debug\net6.0-windows\PercysLibrary.exe",
  [string]$IconPath = "$PSScriptRoot\..\logo de archivo.ico"
)

$ErrorActionPreference = 'Stop'

function Set-Assoc($ext, $progId) {
  New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
  New-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name '(Default)' -Value $progId -Force | Out-Null
}

function Set-ProgId($progId, $friendly, $exe, $icon) {
  $base = "HKCU:\Software\Classes\$progId"
  New-Item -Path $base -Force | Out-Null
  New-ItemProperty -Path $base -Name '(Default)' -Value $friendly -Force | Out-Null
  New-Item -Path "$base\DefaultIcon" -Force | Out-Null
  New-ItemProperty -Path "$base\DefaultIcon" -Name '(Default)' -Value $icon -Force | Out-Null
  New-Item -Path "$base\shell\open\command" -Force | Out-Null
  New-ItemProperty -Path "$base\shell\open\command" -Name '(Default)' -Value '"' + $exe + '" "%1"' -Force | Out-Null
}

Write-Host "Registrando asociaciones de archivo para Percy's Library (perfil de usuario)" -ForegroundColor Cyan

if (-not (Test-Path $ExePath)) { Write-Warning "No se encontró el ejecutable en $ExePath" }
if (-not (Test-Path $IconPath)) { Write-Warning "No se encontró el icono en $IconPath" }

$progId = 'PercysLibrary.File'
Set-ProgId -progId $progId -friendly "Percy's Library" -exe $ExePath -icon $IconPath

$exts = '.cbz','.cbr','.cb7','.cbt','.zip','.rar','.7z','.tar','.pdf','.epub','.jpg','.jpeg','.png','.gif','.bmp','.webp','.heic','.tif','.tiff','.avif'
foreach ($e in $exts) { Set-Assoc -ext $e -progId $progId }

Write-Host "Hecho. Es posible que debas reiniciar el Explorador de Windows o cerrar sesión para ver los iconos actualizados." -ForegroundColor Yellow
