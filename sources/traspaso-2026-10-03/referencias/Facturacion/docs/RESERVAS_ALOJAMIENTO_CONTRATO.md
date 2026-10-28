# Contrato de reservas de alojamiento

Estado: contrato técnico para el submódulo de Facturación. La emisión queda pendiente de decisiones fiscales y de comprobar los valores heredados. Fuente de alcance: RESERVAS_ALOJAMIENTO.md de ActualizadorEsquema (29-09-2026). El inventario físico de MigradorERP solo acredita 21 columnas y su nulabilidad.

## Evidencia del procedimiento heredado `FacturarPisos`

El código entregado por el usuario calcula `AñoReserva` y `MesReserva` desde `FechaInicio` para filas sin `IdFactura`; procesa únicamente entradas anteriores al primer día del mes en curso y ordena/agrupa por `Origen`, año, mes y `Alojamiento`. Crea una factura por grupo y una línea por reserva. La factura usa como cliente a la persona asociada a `Origen` (plataforma), mientras el alojamiento se guarda como beneficiario y aporta el concepto/analítica. Esta lectura describe la ejecución actual; falta confirmar si todas esas reglas deben mantenerse.

La línea ejecutada factura `ImporteNeto` y registra `GastosIntermediario` como comisión y, junto con limpieza, como coste. Dos comentarios del mismo procedimiento proponen `ImporteBruto`, pero el usuario confirmó el 29-09-2026 que el importe a facturar es **`ImporteNeto`**. `ImporteBruto`, comisión y limpieza se conservan como datos originales de conciliación, sin sumarlos a la factura emitida. El código asigna `TipoIVA = 0` e importe IVA cero; el usuario confirmó que, por ahora, sus facturas emitidas no repercuten IVA. Tampoco inspecciona `Tipo` para distinguir cancelaciones o ajustes.

La cuota cero no equivale por sí sola a una calificación fiscal. El usuario confirmó el 29-09-2026 que **en este caso no hay servicios de hostelería**. Para estas reservas, la línea se prepara como operación sujeta y exenta por el artículo 20 (`CausaExencion = E1`), con IVA y recargo cero, impuesto `01` y clave de régimen general `01`; la calificación no exenta queda vacía. Esta política se aplica solo a los inmuebles y servicios confirmados; un cambio en la prestación exige revisar la clasificación antes de emitir. La AEAT indica que el alquiler de apartamentos turísticos sin servicios de hostelería puede estar exento y que con ellos tributa al 10 %. El usuario indicó además que las facturas **recibidas** de las plataformas intracomunitarias no llevan IVA de origen y se autorrepercuten; esto pertenece al flujo de FacturasCompra/IVA soportado y devengado, no incrementa el importe ni la cuota de las facturas emitidas de reservas. La deducibilidad de esa cuota no se presupone.

Fuentes oficiales consultadas: [AEAT, IVA en apartamento turístico](https://sede.agenciatributaria.gob.es/Sede/iva/iva-operaciones-inmobiliarias/alquilo-apartamento-turistico-tengo-que-iva.html); [AEAT, prestaciones de servicios intracomunitarias](https://sede.agenciatributaria.gob.es/Sede/iva/iva-operaciones-comercio-exterior/prestaciones-servicios.html).

El procedimiento heredado actualiza `IdFactura` fila por fila, sin una transacción que abarque lote, factura, líneas y reservas. La nueva implementación debe usar el lote único y una operación transaccional; no debe reproducir esa secuencia. La búsqueda de alojamientos por igualdad de nombres en `DatosContacto` se sustituye por correspondencias explícitas a `IdActivo`. La descripción heredada incluye `Huesped`; su exposición en factura y documentos queda pendiente de justificación funcional y revisión de privacidad.

## Propiedad y relaciones

Facturación es dueña de `ReservasAlojamiento`, `CanalesReserva`, `ImportacionesReservasAlojamiento`, `LotesFacturacionReservas` y `ReservasLoteFacturacion`. `IdActivo` apunta a `ActivosInmuebles.IdActivo` (y por esa clave a `Activos`); la sociedad debe coincidir con `Activos.IdSociedad`. El esquema vigente de Activos define `ActivosInmuebles` como subtipo con PK/FK `IdActivo`. El adaptador MigradorERP conserva el nombre `ReservasPisosTuristicos` solo como origen.

### CanalesReserva

`IdCanalReserva` (PK), `Codigo` (texto estable, único, no anulable), `Nombre`, `EstaActivo`. Semillas propuestas: `AIRBNB` y `BOOKING`; se permite añadir otros canales sin alterar el esquema. `Origen` se normaliza mediante una correspondencia explícita de alias a código de canal; valor no reconocido genera incidencia. Nunca se infiere el canal a partir del código de reserva.

### ReservasAlojamiento

`IdReservaAlojamiento` (PK interna), `IdSociedad`, `IdCanalReserva`, `IdActivo` (anulable mientras se resuelve una importación), `CodigoReservaExterno`, `FechaInicioEstancia`, `Noches`, `MonedaOrigen`, `ImporteBrutoOrigen`, `ImporteNetoOrigen`, `GastosIntermediarioOrigen`, `GastosLimpiezaOrigen`, `PagadoOrigen`, `TipoOrigen`, `FechaOrigen`, `AnoIngresoOrigen`, `MesReservaOrigen`, `AnoReservaOrigen`, `AlojamientoOrigen`, `ReferenciaOrigen`, `DetallesOrigen`, `HuespedOrigen`, `IdFacturaOrigen`, `IdFacturaHistorica` (anulable, solo tras mapa de identidad), `IdImportacionReserva`, `IdRegistroOrigen`, `HuellaRegistroOrigen`, `EstadoRevision`, `FechaHoraImportacion`.

Los importes originales se copian sin transformar, en `Currency` de Access. `PagadoOrigen` se conserva como número porque el inventario lo tipa numérico; no se convierte en estado de cobro. Las fechas y años ambiguos siguen etiquetados «Origen». `HuespedOrigen` es dato personal: no se incluye en diagnósticos, resúmenes ni claves de deduplicación. Se recomienda limitar su acceso y retención según la política de datos de la aplicación. `FechaInicioEstancia` y `Noches` describen la estancia, pero no determinan por sí solos el mes facturable.

La identidad de fila importada es `(IdImportacionReserva, IdRegistroOrigen)`; para reimportaciones, `(IdentificadorFuente, IdRegistroOrigen)` permite localizar la misma fila. `CodigoReservaExterno` puede repetirse por canal o representar varios movimientos: índice de búsqueda `(IdCanalReserva, CodigoReservaExterno)`, sin unicidad. Una huella distinta en la misma identidad de origen se registra como revisión; nunca sobrescribe silenciosamente una reserva que ya pertenece a una factura emitida.

### ImportacionesReservasAlojamiento

`IdImportacionReserva` (PK), `IdentificadorFuente` (estable, por ejemplo instancia ERP), `TablaOrigen`, `HuellaArchivoOrigen`, `VersionImportador`, `FechaHoraInicio`, `FechaHoraFin`, `Estado`, `NumeroLeidas`, `NumeroAceptadas`, `NumeroRechazadas`, `ResumenSinDatosPersonales`. Una tabla de incidencias almacena `IdImportacionReserva`, `IdRegistroOrigen`, `Codigo` y descripción sin nombre del huésped. La correspondencia `AlojamientoOrigen -> IdActivo` debe ser aprobada de forma explícita y conservarse con versión; no hay emparejamiento difuso. Ningún ID de factura de ERP se convierte en FK hasta pasar por un mapa de identidad comprobado.

## Lotes mensuales e idempotencia

`LotesFacturacionReservas`: `IdLoteFacturacionReserva` (PK), `IdSociedad`, `IdCanalReserva`, `IdActivo`, `InicioMes` (día 1), `Estado` (`Preparado`, `Borrador`, `Emitido`, `Incidencia`), `IdFactura` (anulable), `FechaHoraCreacion`, `FechaHoraEmision`, `VersionReglas`. Índice único obligatorio `(IdSociedad, IdCanalReserva, IdActivo, InicioMes)`. El valor de `InicioMes` lo aporta la política de imputación aún pendiente; el modelo de dominio `ClaveLoteReservas` valida y compara estos cuatro componentes.

`ReservasLoteFacturacion`: `IdLoteFacturacionReserva`, `IdReservaAlojamiento`, `ImporteAplicado`, `MonedaAplicada`, `DecisionInclusion`, `VersionReglas`; PK `(IdLoteFacturacionReserva, IdReservaAlojamiento)`. Cada reserva elegible solo puede estar en un lote activo: índice único de `IdReservaAlojamiento` si la política define un movimiento indivisible. Si permite fraccionar estancias entre meses, sustituirlo por una identidad de tramo única antes de materializar el esquema.

El proceso reclama la clave única dentro de una transacción ACE/OLE DB, recupera el lote existente si otro intento ganó la carrera y compara su composición. Guarda la relación exacta de movimientos antes de construir la factura. La vinculación lote-factura y la creación del borrador se confirman juntas. Reintentar `Preparado` o `Borrador` reutiliza el mismo lote y factura; `Emitido` devuelve la factura existente sin modificarla. La numeración y emisión siguen la transacción y la inmutabilidad del servicio actual de Facturación. Una modificación posterior del origen crea incidencia o rectificación explícita, nunca edita la factura emitida.

## Puerta de emisión

Hasta contar con decisiones verificadas, ninguna reserva se declara facturable automáticamente ni se llama a `CrearBorrador` o `ValidarAsync`. Se debe fijar:

1. Confirmar que se mantiene `FechaInicio` como fecha que determina el mes, también en estancias que cruzan meses, y que solo se factura un mes cerrado.
2. `ImporteNeto` está fijado; quedan por definir divisas, redondeo y conciliación de comisión y limpieza.
3. Confirmar que la plataforma es el destinatario fiscal de cada grupo y definir su correspondencia con `Clientes`.
4. La causa de exención E1 y cuota cero están fijadas para este caso sin servicios de hostelería; faltan la serie, el tipo de factura y las fechas fiscales.
5. Significado de `Tipo`, cancelaciones y ajustes; efecto en grupos ya emitidos.
6. Mapa de `IdFactura` histórico y conducta ante reservas ya facturadas.

Antes de generar lotes hay que perfilar valores reales de `Origen`, `Tipo`, fechas, moneda, importes e `IdFactura` en una **copia** de la fuente, sin incluir datos del huésped en los informes. Las bases originales no se modifican.

## Dependencia de ActualizadorEsquema

El manifiesto reproducible de estas tablas e índices corresponde al worktree `ProConta.ActualizadorEsquema` y a su creador de base maestra. Este documento es la propuesta de contrato que debe consumir ese manifiesto; no se ha editado ese worktree. Antes de incorporar el esquema debe resolverse si la identidad facturable es reserva completa o tramo mensual, y verificar que `IdSociedad`, `IdActivo` e `IdFactura` mantienen las FK y tipos del modelo maestro. MigradorERP puede desarrollar el perfilado y mapa de identidades en paralelo, sin cargar filas en la maestra hasta entonces.
