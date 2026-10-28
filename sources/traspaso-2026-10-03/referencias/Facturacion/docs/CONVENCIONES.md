# Convenciones de codigo

## Principios

- Los nombres deben expresar la intencion funcional, no el tipo fisico de la
  variable.
- El codigo propio se escribira en español.
- No se mezclaran español e ingles en un mismo identificador.
- Los nombres impuestos por .NET y por bibliotecas externas se conservaran.
- Se priorizara la coherencia sobre las abreviaturas.

## Tipos y API publica

Clases, estructuras, enumeraciones, propiedades, eventos y metodos publicos
utilizan `PascalCase`:

```vbnet
Public NotInheritable Class Factura
    Public Property IdFactura As Integer
    Public Property ImporteTotal As Decimal

    Public Function Validar() As ResultadoValidacion
    End Function

    Public Sub Emitir(operador As String)
    End Sub
End Class
```

No se utilizaran prefijos como `cls`, `str`, `lng`, `obj` o similares.

## Interfaces

Las interfaces utilizan el prefijo `I`:

```vbnet
IRepositorioFacturas
IServicioVeriFactu
IServicioCorreo
IGeneradorFacturaPdf
```

## Enumeraciones

Las enumeraciones se nombran en singular y sus miembros utilizan `PascalCase`:

```vbnet
Public Enum EstadoFactura As Byte
    PendienteValidar = 0
    Validada = 1
    Rectificada = 2
    Borrador = 3
    RectificacionPendiente = 4
End Enum
```

Los códigos se conservan explícitos porque se almacenan en Access y forman parte
del contrato persistente.

## Campos privados

Los campos privados utilizan `_camelCase`:

```vbnet
Private ReadOnly _lineas As List(Of LineaFactura)
Private ReadOnly _repositorio As IRepositorioFacturas
Private _haCambiado As Boolean
```

El prefijo `_` permite distinguirlos de propiedades y parametros, especialmente
porque VB.NET no diferencia identificadores solamente por mayusculas.

## Variables locales y parametros

Utilizan `camelCase` y no llevan prefijos de tipo:

```vbnet
Dim factura As Factura
Dim fechaDesde As Date
Dim importeTotal As Decimal

Public Function Obtener(idFactura As Integer) As Factura
End Function
```

## Metodos

Los metodos comienzan con un verbo que describa la operacion:

```text
Obtener
Buscar
Crear
Guardar
Eliminar
Validar
Calcular
Generar
Emitir
Rectificar
Enviar
Exportar
Cancelar
Actualizar
```

Se evitaran nombres vagos como `Hacer`, `Gestionar`, `Procesar` o `Ejecutar`
cuando exista un verbo mas preciso.

Se utilizara `Sub` para ordenes sin resultado y `Function` cuando exista un valor
de retorno significativo:

```vbnet
factura.Recalcular()
factura.Emitir(operador)

Dim resultado = factura.Validar()
Dim rectificativa = factura.CrearRectificativa(...)
```

## Busquedas

La semantica sera consistente:

- `Obtener`: devuelve una entidad concreta identificada de forma unica.
- `Buscar`: devuelve cero o varias entidades segun criterios.
- `Existe`: devuelve un booleano.
- `ObtenerPorCliente`, `ObtenerPendientesVeriFactu`: consultas con un significado
  concreto.

No se alternara sin motivo entre `Get`, `Load`, `Find`, `Select`, `Obtener`,
`Cargar` y `Buscar`.

## Documentacion obligatoria de procedimientos

Toda `Function` y todo `Sub` debe estar precedido por un bloque de comentarios
XML de VB.NET. Esta regla se aplica tambien a:

- Metodos privados y protegidos.
- Constructores `Sub New`.
- Manejadores de eventos.
- Implementaciones de interfaces.
- Metodos sobrescritos.
- Funciones auxiliares pequeñas.

El bloque debe describir:

1. El objeto, finalidad o efecto funcional del procedimiento.
2. Todos los parametros aceptados.
3. El retorno directo, si es una funcion.
4. Los retornos indirectos mediante `ByRef`, propiedades o variables globales.
5. Los efectos externos relevantes, como escrituras en base de datos, archivos,
   correo o VeriFactu.

### Plantilla para una funcion

```vbnet
''' <summary>
''' Calcula los importes fiscales de una factura a partir de sus lineas activas.
''' </summary>
''' <param name="factura">
''' Factura que se desea calcular. Debe contener al menos una linea activa.
''' </param>
''' <param name="recalcularLineas">
''' Indica si deben recalcularse las bases y cuotas de cada linea antes de
''' obtener los totales de cabecera.
''' </param>
''' <returns>
''' Totales calculados de la factura, incluidos base imponible, IVA, retencion e
''' importe total.
''' </returns>
''' <remarks>
''' Retorno indirecto: actualiza los importes de las lineas cuando
''' <paramref name="recalcularLineas"/> es verdadero. No modifica la base de
''' datos.
''' </remarks>
Public Function CalcularTotales(
    factura As Factura,
    recalcularLineas As Boolean
) As TotalesFactura
```

### Plantilla para un procedimiento

```vbnet
''' <summary>
''' Guarda una factura en borrador dentro de la transaccion recibida.
''' </summary>
''' <param name="factura">
''' Factura que se desea insertar o actualizar.
''' </param>
''' <param name="transaccion">
''' Transaccion OLE DB activa utilizada para ejecutar todas las escrituras.
''' </param>
''' <remarks>
''' Retorno directo: ninguno.
''' Retorno indirecto: asigna <c>IdFactura</c> a la factura cuando se inserta un
''' registro nuevo. Modifica las tablas Facturas y LineasFactura.
''' </remarks>
Public Sub Guardar(
    factura As Factura,
    transaccion As OleDbTransaction
)
```

### Plantilla con parametros `ByRef`

```vbnet
''' <summary>
''' Intenta obtener una factura sin generar una excepcion cuando no existe.
''' </summary>
''' <param name="idFactura">
''' Identificador interno de la factura buscada.
''' </param>
''' <param name="factura">
''' Parametro de salida. Recibe la factura encontrada o <c>Nothing</c> si no
''' existe.
''' </param>
''' <returns>
''' <c>True</c> cuando se encuentra la factura; en caso contrario,
''' <c>False</c>.
''' </returns>
''' <remarks>
''' Retorno indirecto: establece el parametro <paramref name="factura"/>.
''' No modifica la base de datos.
''' </remarks>
Public Function IntentarObtener(
    idFactura As Integer,
    ByRef factura As Factura
) As Boolean
```

### Plantilla sin parametros

Cuando un procedimiento no acepte parametros, se indicara expresamente en
`<remarks>`:

```vbnet
''' <summary>
''' Recalcula los totales de la factura actual.
''' </summary>
''' <remarks>
''' Parametros: ninguno.
''' Retorno directo: ninguno.
''' Retorno indirecto: actualiza las propiedades de totales de la factura.
''' No modifica la base de datos.
''' </remarks>
Public Sub Recalcular()
```

### Manejadores de eventos

```vbnet
''' <summary>
''' Emite la factura seleccionada cuando el usuario pulsa el boton Emitir.
''' </summary>
''' <param name="sender">
''' Control que origino el evento.
''' </param>
''' <param name="e">
''' Datos del evento de pulsacion.
''' </param>
''' <remarks>
''' Retorno directo: ninguno.
''' Retorno indirecto: puede guardar y emitir la factura, actualizar el grid y
''' mostrar mensajes al usuario.
''' </remarks>
Private Async Sub cmdEmitir_Click(
    sender As Object,
    e As EventArgs
) Handles cmdEmitir.Click
```

### Variables globales y efectos externos

Las salidas mediante estado global se evitaran siempre que sea razonable. Cuando
sean necesarias deben identificarse por nombre:

```vbnet
''' <remarks>
''' Retorno directo: ninguno.
''' Retorno indirecto: actualiza <c>SesionActual.UltimaSociedad</c> y guarda la
''' preferencia en ConfiguracionUsuario.
''' </remarks>
```

No son suficientes comentarios como "guarda la factura" o "devuelve el
resultado" si no explican el contrato, las entradas y las salidas.

## Booleanos

Los booleanos deben poder leerse como una afirmacion o capacidad:

```vbnet
EstaEmitida
EsRectificativa
TieneLineas
PuedeModificarse
RequiereEnvio
HaCambiado
```

## Colecciones

Las colecciones se nombran en plural:

```vbnet
facturas
lineas
errores
enviosPendientes
facturasPorId
```

## Codigo asincrono

Todo metodo asincrono utiliza el sufijo `Async` y devuelve `Task` o
`Task(Of T)`:

```vbnet
Public Function EnviarAVeriFactuAsync(
    idFactura As Integer,
    cancellationToken As CancellationToken
) As Task(Of ResultadoEnvioVeriFactu)
```

`Async Sub` se reserva para manejadores de eventos de WinForms.

El sufijo `Async` solo se usa cuando el contrato representa una espera real,
por ejemplo HTTP, SMTP, AEAT o una orquestacion que depende de ellas. Las
operaciones locales de dominio, Access y sistema de archivos son sincronas si
su implementacion tambien lo es; no se envuelven con `Task.FromResult` ni
`Task.CompletedTask` para aparentar asincronia.

## Excepciones y resultados

Las excepciones utilizan el sufijo `Exception`:

```vbnet
FacturaNoEncontradaException
FacturaNoValidaException
FacturaEmitidaException
NumeracionAgotadaException
ErrorVeriFactuException
```

Los errores ordinarios de validacion se devolveran mediante objetos como
`ResultadoValidacion`, no como excepciones.

Las operaciones complejas devolveran resultados descriptivos en lugar de un
booleano sin contexto:

```vbnet
ResultadoEmision
ResultadoEnvioVeriFactu
ResultadoGeneracionProgramada
```

## Eventos

Los eventos describen un hecho ocurrido:

```vbnet
FacturaGuardada
FacturaEmitida
EstadoCambiado
EnvioFinalizado
EnvioFallido
```

Los manejadores internos pueden describir su reaccion o mantener la convencion
natural de WinForms:

```vbnet
Private Sub cmdEmitir_Click(...) Handles cmdEmitir.Click
Private Sub AlEmitirseFactura(sender As Object, e As FacturaEmitidaEventArgs)
```

## Controles WinForms

Los controles son la unica excepcion a la regla general que evita prefijos de
tipo:

| Control | Prefijo | Ejemplo |
|---|---|---|
| Formulario | `frm` | `frmFactura` |
| Boton | `cmd` | `cmdEmitir` |
| TextBox | `txt` | `txtNombreCliente` |
| Label | `lbl` | `lblImporteTotal` |
| ComboBox | `cbo` | `cboSerie` |
| CheckBox | `chk` | `chkActiva` |
| DataGridView | `grd` | `grdFacturas` |
| DateTimePicker | `dtp` | `dtpFechaFactura` |
| NumericUpDown | `num` | `numCantidad` |
| MenuStrip | `mnu` | `mnuPrincipal` |
| ContextMenuStrip | `ctx` | `ctxFacturas` |
| ToolStrip | `tls` | `tlsFacturas` |
| TabControl | `tab` | `tabFactura` |
| Panel | `pnl` | `pnlTotales` |
| BindingSource | `bs` | `bsFacturas` |
| ErrorProvider | `err` | `errValidacion` |

Se utilizara `cmd` para botones por coherencia con el entorno Access/VB del
proyecto.

## Acronimos

En codigo .NET se trataran como palabras normales:

```vbnet
NifCliente
TipoIva
ImporteIva
RutaPdf
ConfiguracionSmtp
RespuestaAeat
CodigoQr
```

Se respetara la grafia oficial de `VeriFactu`.

Los nombres actuales de Access, como `NIF`, `IVA` o `PDFFactura`, no se
renombraran solamente por estilo. El repositorio realizara el mapeo:

```vbnet
factura.NifCliente = lector.GetString("NIF")
```

## Namespaces

```text
ProConta.Facturacion
ProConta.Facturacion.Dominio
ProConta.Facturacion.Aplicacion
ProConta.Facturacion.Persistencia
ProConta.Facturacion.Integraciones
ProConta.Facturacion.Integraciones.VeriFactu
ProConta.Facturacion.Integraciones.Correo
ProConta.Facturacion.Informes
```

## Acceso desde la interfaz

Los formularios llaman a servicios de aplicación y no utilizan repositorios
directamente. Para el ciclo manual de una factura, el punto de entrada es
`ServicioFacturas`. Esta separación garantiza que cálculo, validación,
inmutabilidad y persistencia se apliquen igual aunque la operación se inicie
desde pantallas diferentes.

El sufijo `Async` solo se usa cuando la operación espera realmente trabajo
asíncrono, como una comunicación con VeriFactu, correo o HTTP. Las operaciones
locales sobre Access, cálculos y creación de objetos conservan nombres
síncronos.

## Base de datos

Se mantiene el protocolo existente:

- Tablas en plural.
- Claves primarias y externas como `IdEntidad`.
- Fechas como `Fecha...` y fechas con hora como `FechaHora...`.
- Importes como `Importe...`, `BaseImponible` o `Cuota...`.
- Indices ordinarios con `IX_`.
- Indices unicos con `UX_`.
- Relaciones con `FK_`.
- Claves primarias con `PK_`.
