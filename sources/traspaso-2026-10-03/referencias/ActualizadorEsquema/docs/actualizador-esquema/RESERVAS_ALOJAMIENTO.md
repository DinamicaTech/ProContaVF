# Reservas de alojamiento: alcance para la maestra

Decisión del usuario (29-09-2026): entre las 38 tablas del inventario ERP que exigían una decisión especial, solo se trasladará `ReservasPisosTuristicos`. Las otras 37 no se cargarán como tablas operativas en la maestra. Su análisis de descarte y posible archivo permanece en el proceso de migración del ERP; esta decisión no modifica la base original.

## Nombre y propiedad

El modelo de destino se llamará **`ReservasAlojamiento`**. «Alojamiento» describe el hecho de negocio sin limitarlo a pisos turísticos ni a una plataforma concreta. Pertenece funcionalmente a Facturación y se relaciona con `ActivosInmuebles` mediante `IdActivo`. Los canales de procedencia se guardarán como datos en un catálogo `CanalesReserva` (por ejemplo, Airbnb y Booking), no como columnas ni tablas distintas por plataforma. La tabla original conserva su nombre solo en el adaptador del MigradorERP y en la trazabilidad de origen.

La reserva y sus importes siguen siendo datos propios del alojamiento. Una abstracción genérica de «operaciones ERP» ocultaría reglas importantes de estancia y de facturación. Se separarán los campos funcionales comunes de los valores cuyo significado en el origen aún no se ha acreditado.

## Inventario físico del origen

La copia inspeccionada contiene 21 campos, todos anulables salvo `Id`: `Origen`, `Fecha`, `Tipo`, `CodigoReserva`, `FechaInicio`, `Noches`, `Huesped`, `Alojamiento`, `Detalles`, `Referencia`, `Moneda`, `ImporteNeto`, `Pagado`, `GastosIntermediario`, `GastosLimpieza`, `ImporteBruto`, `AñoIngreso`, `MesReserva`, `AñoReserva`, `IdFactura`, `Id`. La inspección fue solo de metadatos; no demuestra la semántica ni la calidad de sus valores.

Correspondencias propuestas:

| Origen | Destino o tratamiento |
|---|---|
| `Id`, `CodigoReserva`, `Origen` | Clave y referencia de origen, código externo y canal. La unicidad debe verificarse por canal y referencia, sin asumir que `CodigoReserva` es globalmente único. |
| `Alojamiento` | Correspondencia explícita al `IdActivo` de un `ActivosInmuebles`; conservar el texto de origen para auditar rechazos. Nunca unir inmuebles solo por parecido de nombre. |
| `FechaInicio`, `Noches`, `Fecha`, `AñoIngreso`, `MesReserva`, `AñoReserva` | Fecha de entrada y noches como hechos de estancia. El significado de las otras cuatro columnas y el mes que determina la factura deben verificarse antes de calcular períodos. |
| `ImporteBruto`, `ImporteNeto`, `GastosIntermediario`, `GastosLimpieza`, `Pagado`, `Moneda` | Conservar importes y divisa originales. No recalcular uno a partir de otros ni interpretar `Pagado` como estado o importe sin contrastar valores. |
| `Tipo`, `Detalles`, `Referencia`, `Huesped` | Conservar trazabilidad. `Tipo` puede representar una clase de movimiento, pero su catálogo real está pendiente de perfilar. `Huesped` es dato personal y no debe salir en informes técnicos. |
| `IdFactura` | Referencia histórica que requiere un mapa de identidad ERP→`Facturas`; no enlazarla por coincidencia numérica. |

## Facturación mensual

Un proceso de Facturación agrupará reservas **por canal, inmueble y mes** y generará una factura agregada para cada grupo. Un `LoteFacturacionReservas` identificará esa combinación y la factura resultante; una relación entre lote y reservas conservará exactamente qué movimientos se incluyeron. Una clave única por sociedad, canal, inmueble y período, junto a estados del lote, impedirá emitir dos veces al repetir el proceso. La factura emitida conservará sus importes y datos fiscales materializados y seguirá las reglas de inmutabilidad de Facturación.

Antes de implementar la emisión hay que fijar: qué fecha asigna una reserva al mes; si `Tipo` incluye cancelaciones o ajustes; qué importe se factura y cómo se tratan comisiones y limpieza; quién es el destinatario fiscal de cada factura; qué serie y tratamiento fiscal corresponde; y cómo se vinculan facturas históricas ya emitidas. Estas reglas no se deducen de los nombres de columna.

El módulo tendrá un hilo de desarrollo propio vinculado a Facturación. ActualizadorEsquema incorporará su manifiesto de datos cuando el contrato del módulo esté acordado; MigradorERP será responsable de cargar las filas heredadas sobre copias y registrar correspondencias e incidencias.
