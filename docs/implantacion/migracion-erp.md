---
title: Migración del ERP
status: draft
depends_on: [terceros/tercero, facturacion/factura-venta, contabilidad/asientos, facturacion/remesas-sepa, creative-lab/tesoreria, architecture]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 16:05 · Analizar la migración del ERP | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GHRgCCJAyW6rPfAPdHnyMXG
---
## Summary
Migración única desde ProcontaEstructura.accdb (antes deporwin.mdb) para sustituir la instalación actual, con inventario completo, mapas de identidades y conciliación; nada se reenvía a la AEAT ni se activa al cargar.
El mapa de tablas (abajo) da a cada una de las 135 tablas del origen su destino en el esquema, su transformación, su clave de mapa y su nodo dueño; las tablas `F_???` (salvo `F_NOM`) son del importador de ContaSol y las tablas sin dueño o sin destino quedan como preguntas abiertas, nunca descartadas por omisión. Cubre todos los ejercicios con facturas (el ejercicio sale de la fecha); los vencimientos (`Pagos`) solo desde 2022.
**Aceptación:** ensayo repetible en base nueva; original sin cambios; ninguna tabla omitida en silencio. Del mapa: cada una de las 135 tablas del inventario aparece exactamente una vez, con su nodo dueño o como pregunta abierta.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · La migración cubre todos los ejercicios con facturas emitidas. El ERP es un modelo continuo, sin ejercicios (a diferencia de ContaSol): el ejercicio de cada registro se deduce de su fecha (resuelve P-13).
- 2026-11-01 16:05 · Mapa de tablas a nivel de tabla, hecho sin abrir la BD: fuente, el inventario de metadatos del 28/09/2026 (`sources/traspaso-2026-10-03/referencias/Migracion/docs/MIGRADOR_ERP_TABLAS.md`, 135 tablas y 114 vistas); destino, el diccionario de datos versión 9 (`diccionario/`) y su dueño funcional. Las 114 vistas son evidencia de reglas, no fuentes de datos. El detalle por campo, los recuentos de filas y los años con datos se harán en un perfilado posterior sobre una copia de la BD en el equipo del propietario.
- 2026-11-01 16:05 · Las 17 tablas `F_???` del origen (`F_347`, `F_349`, `F_APU`, `F_BAN`, `F_CDP`, `F_CLI`, `F_DEP`, `F_DIA`, `F_EFC`, `F_EFP`, `F_EMP`, `F_IVR`, `F_IVS`, `F_MAE`, `F_OAS`, `F_PRO`, `F_SAL`) son del importador de ContaSol (RF-ERP-03) y la migración no las carga: solo consume sus mapas de identidad (`MapasIdentidadContaSol`) para enlazar los datos del ERP. `F_NOM`, `F_NOM_ALIAS`, `F_APUMANUAL`, `F_Anulacion_Asientos` y `F_CLIAGRUPADOS` sí entran en el análisis del ERP. Excepción en estudio: `F_CDP` y `F_DEP` (ver M-01).
- 2026-11-01 16:05 · `Pagos` son los vencimientos de las facturas de venta: su dueño es facturacion/factura-venta y se importan solo desde 2022, porque los registros anteriores tienen ruido. El esquema aún no tiene tabla de vencimientos: la pedirá facturacion/factura-venta a datos/esquema cuando se implemente.
- 2026-11-01 16:05 · `Recibos` y `RemesasRecibos` son las remesas SEPA: su dueño es el nodo nuevo facturacion/remesas-sepa, que las importa; después se decidirá si ProContaVF genera remesas SEPA.
- 2026-11-01 16:05 · La migración documental queda fuera (RF-ERP-04): `Documentos`, `DocumentosFacturas` y `DocumentosPersonas` no se cargan; se conserva el origen como archivo.
- 2026-11-01 16:18 · La cuenta de retención IRPF se guarda por cliente y sociedad en el rol Cliente, como identidad contable junto a su 430 (columna nueva de CuentasClientesSociedad, nodo terceros/roles; la usa facturacion/factura-venta/contabilizacion). La migración da a cada cliente la cuenta de retención del ConceptoCaja de su inmueble a través del contrato; si un cliente obtiene dos cuentas distintas, queda como incidencia y no se elige ninguna en silencio (resuelve M-11).
- 2026-11-01 16:05 · Las líneas «Derived from» en los nodos dueños se escriben cuando se implemente cada parte, no ahora, para no chocar con los hilos que programan esos nodos.

## Mapa de tablas
Origen: ProcontaEstructura.accdb. Destino: tablas del diccionario versión 9. «Sin tabla» quiere decir que el nodo dueño tendrá que pedir su tabla a datos/esquema. Todas las transformaciones: origen solo lectura, mapa de identidad (origen, tabla, clave original → clave destino), inactivos referenciados incluidos, importes históricos conservados sin recalcular, nada se activa ni se envía al cargar.

| # | Bloque | Tablas de origen | Destino | Transformación y clave de mapa | Dueño |
|---|---|---|---|---|---|
| 1 | Sociedades | `Sociedades`, `Sistema` | Sociedades, SociedadesEjercicios, Configuracion | Reconciliar con las sociedades que ya tiene la maestra; crear un SociedadEjercicio por cada año con datos (P-13). Clave: `Sociedades.IdSociedad`. | organizacion/sociedades-ejercicios |
| 2 | Formas de pago | `FormasPago` | FormasPago | Ya copiada por la migración de esquema v2 (`--origen-formas-pago`) con sus códigos; solo verificar. | organizacion/formas-pago |
| 3 | Geografía | `Calles`, `Poblaciones`, `Provincias` | Sin tabla (se deduce de DomiciliosTercero) | Usar solo para normalizar los domicilios de terceros; no se cargan como catálogo. | organizacion/geografia |
| 4 | Terceros | `Personas`, `DatosContacto` | Terceros, DomiciliosTercero, MapasOrigenTerceros, Clientes, DatosContacto | Una identidad por `Personas.IdPersona` (MapasOrigenTerceros); nunca fusionar por NIF; inactivos referenciados entran inactivos. | terceros/tercero, terceros/roles, terceros/contactos |
| 5 | Analítica | `Departamentos` | CentrosCoste | Por sociedad, con su jerarquía. Clave: código del departamento. | contabilidad/centros-coste |
| 6 | Cuentas | `CuentasContables`, `RegistroCambioCuentasContables` | IdentidadesContables, CuentasContables, CuentasContablesLegacy | Cuenta por ejercicio deducido de la fecha; las recodificaciones se conservan como historia. Las cuentas en sí vienen de ContaSol (F_MAE). | contabilidad/cuenta-contable |
| 7 | Conceptos | `ConceptosCaja`, `Agrupaciones` | Conceptos, TiposConcepto | Base: script de ensayo anterior, pero con inactivos referenciados y cuentas/analítica resueltas por mapa. Hoy cada concepto de un inmueble lleva su cuenta de retención IRPF (p. ej. Id 4754 → 4735079); esa cuenta no se copia al concepto: pasa a CuentasClientesSociedad (terceros/roles), por cliente y sociedad, a través del inmueble y el contrato (M-11). Clave: `ConceptosCaja.Id`. | facturacion/conceptos |
| 8 | Facturas de venta | `FacturasVenta`, `LineasFacturaVenta`, `FechasFactura`, `EstadosFacturacion` | Facturas, LineasFactura, DesglosesIVARepercutido, SeriesFacturacion | Todos los ejercicios con facturas; número y serie del origen (`Numero`, `CodigoSerie`, `NumeroFacturaManual`), nunca el ID; instantáneas de emisor y cliente; rectificativas 1:1; sin VeriFactu, correo ni PDF regenerado. Clave: `FacturasVenta.Id`, líneas `(Id, NumLinea)`. | facturacion/factura-venta |
| 9 | Vencimientos | `Pagos` | Sin tabla (vencimientos de factura) | Solo desde 2022; enlazar cada vencimiento a su factura por mapa. Clave: `Pagos.Codigo`. | facturacion/factura-venta |
| 10 | Fraccionamiento | `EsquemasFraccionamiento`, `FraccionesEsquema` | Sin tabla | Plantillas para repartir una factura en vencimientos; ver M-04. | facturacion/factura-venta (propuesto) |
| 11 | Programaciones | `FacturasProgramadas`, `LineasFacturaProgramada` | FacturasProgramadas, LineasFacturasProgramadas | Periodicidad y próxima fecha validadas; cargadas desactivadas, sin generar facturas. Clave: `FacturasProgramadas.Id`, líneas `(Id, NumLinea)`. | facturacion/programadas |
| 12 | Reservas | `ReservasPisosTuristicos` | Sin tabla | Ventas registradas antes de facturar. | facturacion/reservas |
| 13 | Remesas SEPA | `Recibos`, `RemesasRecibos`, `DatosBancarios`, `InmueblesRecibosSEPA`, `InmueblesTitularesSEPA` | Sin tabla | Recibos con su remesa, estado y devoluciones; mandatos e IBAN del deudor desde DatosBancarios (a confirmar en el perfilado). Claves: `Recibos.Codigo`, `RemesasRecibos.Codigo`. | facturacion/remesas-sepa |
| 14 | Compras | `FacturasCompra` | FacturasCompra, DesglosesIVASoportado | Misma estructura que la maestra: copiar el dato registrado sin volver a reconocer el PDF; proveedor y cuentas por mapa. Clave: `IdRegistro`. | compras/factura-compra |
| 15 | Clasificación de compras | `PalabrasClaveSubDepartamento`, `PalabrasClaveTipoGasto`, `TiposOperacionCompra` | Sin tabla (reglas); TiposOperacion | Reglas del reconocimiento de gastos. | compras/reconocimiento |
| 16 | Asientos del ERP | `F_APUMANUAL`, `F_Anulacion_Asientos` | Asientos, Apuntes, RelacionesAsientos | Solo lo que no esté ya en ContaSol; ver M-02. | contabilidad/asientos |
| 17 | Agrupación de clientes | `F_CLIAGRUPADOS` | Sin decidir | Ver M-02. | terceros/roles (propuesto) |
| 18 | Configuración IVA | `TiposDeIVA`, `CategoriasIVA`, `CategoriasIVAPorDepartamento`, `CuentasIVA`, `CuentaBalanceIVA`, `IAE` | TiposDeIVA (catálogo técnico), ConfiguracionesContables | Traducir por sociedad y ejercicio; el catálogo técnico de la maestra manda. | contabilidad/procesos (TiposDeIVA: facturacion/factura-venta) |
| 19 | Balances e informes | `EsquemaBalance`, `EsquemaBalanceGrupos`, `EsquemaBalancePyGApartados`, `FormatoExportacionExcel` | ModelosBalance, NodosBalance; sin tabla para Excel | Traducir solo los formatos admitidos. | contabilidad/balances, informes/excel |
| 20 | Norma 43 histórico | `N43LotesImportacion`, `N43Cabeceras`, `N43Lineas`, `N43BBVA`, `N43LineasBBVA`, `N43PayPal`, `N43Tarjetas` | LotesImportacionBancaria, ExtractosBancarios, LineasExtractoBancario, ImputacionesLineaExtractoBancario, ContabilizacionesLineaExtracto | Conservar imputaciones y enlaces a asientos, no reimportar ficheros. Claves: `IdLote`, `IdRegistro`, `(IdRegistro, NumLinea)`. | norma43 |
| 21 | Norma 43 reglas | `N43EntidadesBancarias`, `EntidadesBancarias`, `N43Esquema`, `N43FormatoFicherosTarjetas`, `N43PalabrasClave`, `DatosBancariosGestion` | EntidadesFinancieras, Bancos, CuentasBancarias; sin tabla para reglas | Separar el catálogo del producto de la configuración de la instalación. | norma43 |
| 22 | Nóminas | `F_NOM`, `F_NOM_ALIAS`, `AliasCamposF_NOM`, `CamposF_NOM`, `PlantillasApuntes` | Sin tabla (faltan las de nóminas en el esquema) | Ver M-03. | nominas |
| 23 | Activos | `Inmuebles`, `CuentasInmuebles`, `PlantillasInmuebles`, `TiposInmueble` | Inmuebles (hoy vinculada), CuentasActivo, PlantillasCuentasActivo, TiposActivo | Base: importador anterior de Activos, completando los campos que no trasladaba. | creative-lab/activos |
| 24 | Seguridad | `Operadores`, `_ControlAcceso`, `_ConverOperador` | OperadoresSeguridad, GruposOperadores | Sin contraseñas antiguas; ver M-05 para las dos auxiliares. | seguridad/permisos |
| 25 | Documentos | `Documentos`, `DocumentosFacturas`, `DocumentosPersonas`, `PlantillasPDF` | No se cargan | Excluidos por RF-ERP-04 (las plantillas PDF se rehacen en informes/reporting). | documentos |

**Tablas sin dueño** (preguntas M-06 a M-10, en Requirements):

| Grupo | Tablas de origen |
|---|---|
| Países, monedas y cambios | `CodigosPais`, `Paises`, `PaisesMonedas`, `PrefijosVIES`, `Monedas`, `CambioEuroDolar`, `CambioEuroLibra` |
| Inspección de IVA | `InspeccionIVAActividades`, `InspeccionIVAEquivalenciasCuentasGasto`, `InspeccionIVANacionalidades`, `InspeccionIVATiposDeGasto` |
| Mayores y cálculos derivados | `MayorClientesDepor2012`, `MayorClientesDepor2013`, `MayorClientesDepor2014`, `MayorClientesDepor2015`, `MayorClientesDepor2016`, `MayorClientesDepor2017`, `MayorClientesDepor2018`, `MayorClientesDepor2019`, `MayorClientesDepor2020`, `MayorClientesDepor2021`, `MayorClientesDepor2022`, `MayorClientesDepor2023`, `MayorClientesDepor2024`, `CuadreMayor`, `SaldoClientesCalculado`, `SaldoClientesCalculadoFacturas`, `BalancesIVACalculado`, `BalancesIVAs4` |
| Copias | `FacturasCompra1`, `FacturasProgramadas1`, `FMAE2023Backup`, `FMAE2024AntesCambioF_MAE`, `FPRO2024AntesCambioF_PRO`, `N43Lineas2`, `N43LineasBAK`, `N43LineasBak2` |
| Auxiliares | `_Tablas`, `Errores de pegado`, `Fechas`, `Frases`, `Repeat`, `Validaciones`, `zLogMaterializacion` |

**Del importador de ContaSol** (no se cargan aquí): `F_347`, `F_349`, `F_APU`, `F_BAN`, `F_CDP`, `F_CLI`, `F_DEP`, `F_DIA`, `F_EFC`, `F_EFP`, `F_EMP`, `F_IVR`, `F_IVS`, `F_MAE`, `F_OAS`, `F_PRO`, `F_SAL`.

Recuento: 74 tablas en los 25 bloques, 44 sin dueño y 17 de ContaSol, 135 en total.

## Hand-over
Hecho (2026-11-01): mapa a nivel de tabla, preguntas M-01 a M-10 y nodo facturacion/remesas-sepa. Pendiente: que el propietario responda las preguntas; el perfilado sobre una copia de la BD (campos, filas, años con datos, unicidad de claves, contenido exclusivo de las copias); después, el detalle por campo de cada bloque con su nodo dueño, y la implementación del borrador.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 26.1 RF-ERP-01..05: migración única, coordinador, clasificación ContaSol, preservación
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Migracion/docs/: migrador anterior y tablas
- 2026-10-28 12:28 · draft: Implementar y ensayar la migración del ERP sobre copias, con conciliación.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-13 ¿cobertura temporal? (a) todos los ejercicios con facturas emitidas ✅ (b) solo el ejercicio en curso
- 2026-10-31 16:54 · 19. a. Aunque el ERP no tiene ejercicios (a diferencia de ContaSol), es un modelo continuo.
- 2026-10-31 16:54 · answer: P-13 cobertura temporal → (a) todos los ejercicios con facturas emitidas; el ERP no tiene ejercicios (a diferencia de ContaSol), es un modelo continuo
- 2026-11-01 15:54 · Derived from coordinador: preparar el mapa de tablas de ProcontaEstructura.accdb (origen, destino, transformación, dueño y tablas sin dueño), según RF-ERP-01..05 y la decisión P-13.
- 2026-11-01 15:58 · Bueno, las tablas de Pagos, Recibos y Remesas sí tienen dueño. La primera correspondería a los vencimientos de las facturas (aunque en datos históricos puede haber un poco más de ruidos). Las dos segundas corresponden a las remesas SEPA.
- 2026-11-01 16:01 · 1. Solo de venta. Filtraremos el período de importación solo a partir del 2022 para eliminar registros antiguos con ruido.
- 2026-11-01 16:05 · question: M-01 `F_CDP` y `F_DEP` siguen el patrón `F_???` de ContaSol, pero la maestra las vincula como centros de coste y departamentos del ERP (datos/esquema 2026-10-31 18:55). (a) excepción como `F_NOM`: las carga la migración del ERP en CentrosCoste ✅ recomendada, porque son la analítica que usa el ERP (b) las carga el importador de ContaSol y la migración solo enlaza.
- 2026-11-01 16:05 · question: M-02 `F_APUMANUAL`, `F_Anulacion_Asientos` y `F_CLIAGRUPADOS` no son de ContaSol por el patrón. (a) perfilar y cargar solo lo que no esté ya en los asientos de ContaSol ✅ recomendada (b) cargarlas siempre (riesgo de duplicar asientos) (c) archivar sin cargar.
- 2026-11-01 16:05 · question: M-03 Nóminas (`F_NOM`, alias, campos, `PlantillasApuntes`): el esquema aún no tiene sus tablas. (a) migrar alias, campos y plantillas cuando nominas tenga sus tablas, y el histórico de nóminas solo como enlace a sus asientos ya importados de ContaSol ✅ recomendada (b) migrar también todo el histórico de nóminas (c) archivar sin cargar.
- 2026-11-01 16:05 · question: M-04 `EsquemasFraccionamiento` y `FraccionesEsquema` (reparto de una factura en vencimientos). (a) dueño facturacion/factura-venta, junto con los vencimientos ✅ recomendada (b) dueño facturacion/remesas-sepa (c) no cargarlas.
- 2026-11-01 16:05 · question: M-05 `_ControlAcceso` y `_ConverOperador` (2 columnas cada una): ¿son permisos de operador o control de acceso de la actividad deportiva antigua? (a) perfilar y, si son permisos, pasarlas a seguridad/permisos ✅ recomendada (b) no cargarlas.
- 2026-11-01 16:05 · question: M-06 Países, monedas y cambios (`CodigosPais`, `Paises`, `PaisesMonedas`, `PrefijosVIES`, `Monedas`, `CambioEuroDolar`, `CambioEuroLibra`). (a) dar a organizacion/geografia un catálogo de países con prefijo VIES y no cargar monedas ni cambios (los importes en divisa ya están en cada factura) ✅ recomendada (b) cargar también monedas y cambios en un nodo nuevo (c) no cargar nada.
- 2026-11-01 16:05 · question: M-07 Inspección de IVA (`InspeccionIVAActividades`, `InspeccionIVAEquivalenciasCuentasGasto`, `InspeccionIVANacionalidades`, `InspeccionIVATiposDeGasto`). (a) no cargarlas y conservar el origen como archivo ✅ recomendada (b) dueño contabilidad/modelos-tributarios (c) nodo nuevo.
- 2026-11-01 16:05 · question: M-08 Mayores y cálculos derivados (`MayorClientesDepor2012` a `2024`, `CuadreMayor`, `SaldoClientes*`, `BalancesIVA*`). (a) no cargarlos: se reconstruyen desde los asientos y el Mayor nuevo no cuadra contra el ERP; usarlos solo como control en la conciliación ✅ recomendada (b) cargarlos como archivo de consulta.
- 2026-11-01 16:05 · question: M-09 Copias (`FacturasCompra1`, `FacturasProgramadas1`, `FMAE2023Backup`, `FMAE2024AntesCambioF_MAE`, `FPRO2024AntesCambioF_PRO`, `N43Lineas2`, `N43LineasBAK`, `N43LineasBak2`). (a) compararlas con su tabla principal en el perfilado y excluirlas si no tienen filas exclusivas ✅ recomendada (b) excluirlas sin comparar.
- 2026-11-01 16:05 · question: M-10 Auxiliares (`_Tablas`, `Errores de pegado`, `Fechas`, `Frases`, `Repeat`, `Validaciones`, `zLogMaterializacion`). (a) no cargarlas, salvo revisar `Frases` por si son textos de factura que pasen a facturacion/conceptos ✅ recomendada (b) no cargar ninguna.
- 2026-11-01 16:25 · [answered 2026-11-01 16:18] question: M-11 Cuenta de retención IRPF: en el ERP cuelga de `ConceptosCaja`, un concepto por inmueble que apunta a una subcuenta de retención (lo explica el propietario en el hilo de facturación de venta, 2026-11-01 16:18). (a) la decide facturacion/factura-venta; si la guarda por cliente y sociedad en el rol Cliente, la migración da a cada cliente la cuenta del ConceptoCaja de su inmueble a través del contrato, y deja como incidencia el cliente con dos cuentas distintas ✅ recomendada (b) copiarla tal cual en Conceptos.
- 2026-11-01 16:18 · answer: M-11 cuenta de retención IRPF → (a) por cliente y sociedad en el rol Cliente; cada cliente hereda en la migración la cuenta del ConceptoCaja de su inmueble a través del contrato (confirmado por el propietario en el hilo de facturación de venta: «Me parece perfecto»)
