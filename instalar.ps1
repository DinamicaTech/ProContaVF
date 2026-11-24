# Instala la versión compilada de ProContaVF en la carpeta de producción (docs/plataforma/entrega.md).
# Uso: instalar.cmd [carpeta destino]   (por defecto C:\Docs\ProConta)
#  1. Compila la solución en Release x64 (compilar.cmd).
#  2. Comprueba que ProContaVF no está abierto desde la carpeta destino.
#  3. Guarda los ficheros del programa que va a sustituir en <destino>\versiones\<fecha hora>.
#  4. Copia el programa (con su carpeta Conector) en <destino> y el ActualizadorEsquema en <destino>\herramientas\ActualizadorEsquema.
# Nunca toca las carpetas de datos (datos, datospruebas, documentos…): solo sustituye los ficheros que trae la versión nueva.
# El esquema de la BD no se actualiza aquí: al abrir la BD, ProContaVF ofrece «Actualizar ahora» con copia previa.
param([string]$Destino = 'C:\Docs\ProConta')
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "Compilando la solución en Release x64…"
& cmd /c "`"$raiz\compilar.cmd`""
if ($LASTEXITCODE -ne 0) { throw "La compilación ha fallado (código $LASTEXITCODE). Corrige el error y vuelve a ejecutar instalar.cmd; no se ha copiado nada." }

$programa = Join-Path $raiz 'src\ProConta.App\bin\Release\net10.0-windows'
$actualizador = Join-Path $raiz 'src\ProConta.Datos.Esquema.Consola\bin\Release\net10.0-windows'
if (-not (Test-Path (Join-Path $programa 'ProContaVF.exe'))) { throw "No encuentro ProContaVF.exe en $programa después de compilar." }
if (-not (Test-Path (Join-Path $programa 'Conector\ProContaConector.exe'))) { throw "No encuentro el conector de Claude en $programa\Conector después de compilar." }

$abiertos = Get-Process -Name ProContaVF, ActualizadorEsquema -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($Destino, [StringComparison]::OrdinalIgnoreCase) }
if ($abiertos) {
    $lista = ($abiertos | ForEach-Object { "$($_.ProcessName) ($($_.Path))" }) -join ', '
    throw "Hay programas abiertos desde $Destino ($lista). Ciérralos y vuelve a ejecutar instalar.cmd; no se ha copiado nada."
}
# El conector lo tiene abierto Claude Desktop mientras está en marcha: si es así, se instala todo menos la carpeta Conector, que sigue
# con la versión anterior hasta la próxima instalación con Claude Desktop cerrado.
$conectorEnUso = [bool](Get-Process -Name ProContaConector -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($Destino, [StringComparison]::OrdinalIgnoreCase) })
$excluir = if ($conectorEnUso) { @('Conector') } else { @() }
if ($conectorEnUso) { Write-Host "AVISO: Claude Desktop está usando el conector, así que la carpeta Conector no se actualiza. Para actualizarla, cierra Claude Desktop del todo y vuelve a ejecutar instalar.cmd." }

New-Item -ItemType Directory -Force -Path $Destino | Out-Null
$sello = Get-Date -Format 'yyyyMMdd-HHmmss'
$respaldo = Join-Path $Destino "versiones\$sello"

# Respaldo: solo los ficheros y carpetas del destino que la versión nueva va a sustituir.
$origenes = @(@{ Desde = $programa; Hacia = $Destino }, @{ Desde = $actualizador; Hacia = (Join-Path $Destino 'herramientas\ActualizadorEsquema') })
$guardados = 0
foreach ($o in $origenes) {
    foreach ($item in Get-ChildItem -LiteralPath $o.Desde) {
        if ($o.Hacia -eq $Destino -and $excluir -contains $item.Name) { continue }
        $existente = Join-Path $o.Hacia $item.Name
        if (Test-Path -LiteralPath $existente) {
            $relativo = $existente.Substring($Destino.TrimEnd('\').Length).TrimStart('\')
            $copia = Join-Path $respaldo $relativo
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $copia) | Out-Null
            Copy-Item -LiteralPath $existente -Destination $copia -Recurse -Force
            $guardados++
        }
    }
}
if ($guardados -gt 0) { Write-Host "Versión anterior guardada en $respaldo ($guardados elementos)." } else { Write-Host "No había versión anterior en $Destino." }

foreach ($o in $origenes) {
    New-Item -ItemType Directory -Force -Path $o.Hacia | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $o.Desde) {
        if ($o.Hacia -eq $Destino -and $excluir -contains $item.Name) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $o.Hacia -Recurse -Force
    }
}

$version = (Get-Item (Join-Path $Destino 'ProContaVF.exe')).VersionInfo.ProductVersion
Write-Host "Instalada la versión $version en $Destino."
Write-Host "Ábrela con: `"$Destino\ProContaVF.exe`" --bd `"<ruta de la BD>`""
