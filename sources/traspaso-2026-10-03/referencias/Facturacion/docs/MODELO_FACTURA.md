# Modelo de dominio de factura

## Importes y redondeo

`LineaFactura.BaseImponible` es el importe maestro. Al asignarlo se calculan la
cuota de IVA, el recargo de equivalencia y el importe con impuestos.

La interfaz también puede asignar `ImporteConImpuestos`. En ese caso el modelo
calcula la base implícita y reparte las cuotas conservando exactamente el total
introducido. Desde ese momento la base calculada vuelve a ser el dato maestro.

Todos los importes monetarios se redondean a dos decimales mediante
`MidpointRounding.AwayFromZero`. Primero se calculan y redondean las líneas y
después se agregan sus valores. Este orden evita que pantalla, PDF, Access y
VeriFactu obtengan céntimos distintos.

Los totales de `Factura` son propiedades de solo lectura:

- `BaseImponible`: suma de bases no suplidas.
- `Iva`: cuotas de IVA y recargo de equivalencia.
- `ImporteBruto`: suma final de líneas, incluidos suplidos y tributos.
- `Retencion`: porcentaje de retención sobre la base no suplida.
- `ImporteTotal`: bruto menos retención.

## Validación previa a la emisión

`Factura.ValidarParaEmision` devuelve conjuntamente errores y advertencias. No
reserva número ni modifica la factura.

Son errores bloqueantes:

- estado distinto de Borrador;
- cliente no asignado;
- ausencia de líneas;
- descripción general o tipo de factura vacíos;
- período facturado con fecha inicial posterior a la final;
- fecha anterior a la última factura numerada de la misma serie;
- tipos de IVA, recargo o retención fuera del intervalo 0–100;
- línea sin descripción o desglose fiscal básico;
- tipo impositivo en una operación exenta o no sujeta.

Una factura cuyo total sea exactamente cero es válida, pero devuelve la
advertencia `IMPORTE_CERO`. La interfaz deberá pedir confirmación para detectar
precios o líneas olvidados sin prohibir casos fiscales legítimos.

Las validaciones detalladas que dependen de combinaciones de claves de la AEAT
continúan ejecutándose en el adaptador VeriFactu inmediatamente antes del envío.

## Numeración y fechas

Los borradores no reciben `NumeroFactura`. Al validar se consulta la fecha
máxima ya numerada de la serie. La nueva fecha debe ser igual o posterior; solo
después se reserva transaccionalmente el siguiente número.

La generación desde facturas programadas aplica ya esta comprobación dentro de
la misma transacción que reserva la numeración.

## Campos modificables después de emitir

`Factura.CampoModificable` y `LineaFactura.CampoModificable` disponen de una
sobrecarga tipada y otra por nombre para el enlace de controles. El nombre se
recibe `ByVal`, no `ByRef`, porque la consulta no debe modificarlo.

Una vez que la factura abandona Borrador solo se consideran modificables:

| Objeto | Campos |
|---|---|
| Factura | `NotasInternas`, `FechaExportacionContable`, `IdAsientoContable`, `LoteExportacionContable`, `PdfFactura`, `Historial` |
| Línea | `NotasInternas`, `CuentaContable`, `IdDepartamento`, `IdCentroCoste` |

La ruta PDF y el historial son técnicamente modificables por servicios
operativos, aunque no deben presentarse como cajas de texto al usuario.

Se congelan también los campos comerciales que no viajan a VeriFactu —por
ejemplo forma de pago, período o descripción individual de línea— porque
cambiarlos permitiría producir dos documentos distintos con el mismo número.

## Datos utilizados por VeriFactu

El adaptador actual utiliza directamente de la cabecera:

- número, fecha de factura y fecha de operación;
- NIF y nombre del emisor;
- tipo de factura y tipo de rectificativa;
- identificación, país y nombre o razón social del cliente;
- descripción general de la operación;
- tipo e importe de retención.

Las líneas no se envían como líneas comerciales. Se agregan por impuesto,
clave de régimen, calificación, causa de exención, tipo de IVA y tipo de recargo,
sumando base y cuotas. `EsSuplido` determina si la línea queda fuera del
desglose fiscal.

## API del ciclo manual

```vbnet
Dim servicio = ComposicionNucleoFacturacion.CrearServicioFacturas(
    rutaBaseDatos,
    directorioDatosVeriFactu,
    directorioFacturasPdf)

Dim factura = servicio.CrearBorrador(
    idSociedadEjercicio:=24,
    idCliente:=125,
    fechaInicioValidez:=#8/1/2026#,
    fechaFinValidez:=#8/31/2026#,
    descripcion:="Alquiler agosto 2026")

Dim importes = servicio.GuardarBorrador(factura)
Dim resultado = Await servicio.ValidarAsync(
    {factura.IdFactura},
    fechaFactura:=Date.Today)
Dim rectificativas = servicio.CrearRectificativa(
    {factura.IdFactura},
    "Cambio solicitado")

servicio.EliminarBorrador(factura.IdFactura)
```

`CrearBorrador` devuelve una factura persistida, sin líneas ni número.
`GuardarBorrador` recalcula los totales y vuelve a congelar el cliente, sin
exigir que el borrador ya esté completo. `ValidarAsync` no numera cuando
existen errores o advertencias sin confirmar; la interfaz puede presentar
`resultado.Validacion.Incidencias` y repetir con
`aceptarAdvertencias:=True` cuando el usuario confirme una advertencia.
