# Configuración global de datos

`ProConta.Configuracion` es la única biblioteca autorizada para interpretar la
ubicación de los datos de ProConta. Los módulos consumidores reciben una
instancia de `IUbicacionDatosAplicacion`; no leen argumentos, variables de
entorno ni JSON por su cuenta.

## Contrato

```vbnet
Public Interface IUbicacionDatosAplicacion
    ReadOnly Property DirectorioDatos As String
    ReadOnly Property RutaBaseDatos As String
End Interface
```

`DirectorioDatos` es la carpeta absoluta que contiene el archivo Access fijado por la composición (`ProcontaVF.accdb` de forma predeterminada) y de
la que parten Reporting, FacturasCompra, Nominas, FicherosN43 y otros datos
externos. `RutaBaseDatos` es siempre la combinación canónica de esa carpeta y
el nombre exacto de la base. `NombreBaseDatosEsperado` permite consultar ese nombre.

## Selección explícita del nombre

El constructor admite `nombreBaseDatosEsperado` como último parámetro opcional.
Si se omite, el contrato histórico continúa exigiendo `ProcontaVF.accdb`. La
aplicación integrada que trabaja sobre la copia preparada debe componerse así:

```vbnet
Dim resolutor As New ResolutorUbicacionDatos(
    nombreBaseDatosEsperado:="ProConta.accdb")
Dim resultado = resolutor.Resolver({
    "--base-datos",
    "C:\Docs\VeriFactu\ProConta.accdb"
})
```

El parámetro es únicamente un nombre de archivo `.accdb`, no una ruta. La ruta
completa continúa entrando por `--base-datos`. Si se proporcionan a la vez
`--directorio-datos` y `--base-datos`, ambos deben identificar la misma
ubicación; cualquier contradicción detiene el arranque.

La elección no busca alternativas, no copia bases y no vuelve a
`ProcontaVF.accdb` si falta `ProConta.accdb`. La preferencia persistida conserva
el directorio; el nombre fijado por la composición recompone siempre la ruta
exacta al arrancar.

## Precedencia

1. `--directorio-datos`.
2. `--base-datos`, durante la transición de compatibilidad.
3. `PROCONTA_DIRECTORIO_DATOS`.
4. `%LocalAppData%\ProConta\proconta.config.json`.
5. `proconta.config.json` junto al ejecutable, durante la transición.
6. `%LocalAppData%\ProConta\ultima-ubicacion-datos.json`, escrito por el
   asistente.
7. Asistente interactivo, únicamente en la aplicación de escritorio.

Una fuente presente pero incorrecta detiene la resolución y devuelve su error;
no se oculta un argumento mal escrito usando silenciosamente otra fuente de
menor prioridad. Los procesos desatendidos nunca abren el asistente.

## Formato

El formato vigente es:

```json
{
  "DirectorioDatos": "C:\\Datos\\ProConta"
}
```

Los archivos antiguos pueden conservar temporalmente:

```json
{
  "BaseDatos": "C:\\Datos\\ProConta\\ProcontaVF.accdb"
}
```

La configuración solo contiene rutas. No admite credenciales ni datos
empresariales. La última selección se sustituye de forma atómica.

## Composición de consumidores

Cada ejecutable resuelve una sola vez y entrega el contrato a sus servicios:

```vbnet
Public Sub New(ubicacionDatos As IUbicacionDatosAplicacion)
    _directorioDatos = ubicacionDatos.DirectorioDatos
    _rutaBaseDatos = ubicacionDatos.RutaBaseDatos
End Sub
```

Este patrón es aplicable a Reporting, Seguridad, Nóminas, Norma43,
FacturasCompra y Mail Dispatcher. El asistente WPF pertenece solo al arranque
interactivo; la biblioteca central no referencia UI.

## Asistente WPF y LEOC

Cuando el arranque de escritorio no resuelve una ruta, muestra el asistente
WPF. Este valida carpeta, nombre exacto de la base y acceso de lectura y
escritura antes de guardar. `Ctrl+Mayús+D` abre su fuente
`AsistenteUbicacionDatosWpf.leoc` en el entorno de desarrollo.
