# MigradorERP: inventario completo de tablas de origen

Regla de propiedad confirmada: `F_???` significa exactamente tres caracteres después de `F_`, con la excepción de `F_NOM`. Las tablas que cumplen ese criterio corresponden a Importador ContaSol y quedan fuera de la carga ERP, aunque aparezcan en esta copia. Los nombres más largos, como `F_NOM_ALIAS` y `F_APUMANUAL`, siguen sujetos al análisis ERP. El catálogo recoge objetos observados; no asigna a MigradorERP la carga de todas sus filas.

Inspección: 28/09/2026. Fuente: metadatos ACE/OLE DB de una copia verificada de ProcontaEstructura.accdb. Se enumeraron 135 tablas. No se leyeron filas empresariales. La clasificación es provisional y no autoriza ningún descarte.

El plan, las fuentes y los límites de esta extracción están en [MIGRADOR_ERP.md](C:/Docs/VeriFactu/ProConta.Migracion/docs/MIGRADOR_ERP.md). Los nombres y campos son metadatos técnicos. Las claves que no devuelve el proveedor no se presumen inexistentes: el extractor siguiente deberá verificar unicidad y relaciones. No se incluyen valores de registros, importes ni credenciales.

| Tabla | Columnas | PK devuelta por ACE | Bloque | Destino propuesto | Estado/condición |
|---|---:|---|---|---|---|
| `_ControlAcceso` | 2 | No devuelta | Seguridad | Identidades/grupos/auditoría | Adaptar permisos; no copiar contraseñas legacy |
| `_ConverOperador` | 2 | No devuelta | Seguridad | Identidades/grupos/auditoría | Adaptar permisos; no copiar contraseñas legacy |
| `_Tablas` | 3 | No devuelta | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `Agrupaciones` | 16 | No devuelta | Conceptos | Conceptos/configuración | Script parcial de ensayo; adaptar |
| `AliasCamposF_NOM` | 3 | Empresa, Origen | Nóminas | Empleados/nóminas/plantillas/vínculos | Traslado histórico pendiente; no recontabilizar |
| `BalancesIVACalculado` | 34 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `BalancesIVAs4` | 32 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `Calles` | 7 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `CambioEuroDolar` | 2 | No devuelta | Tipos de cambio | Referencia histórica de cambio | Validar procedencia y usos; no sustituir importes históricos |
| `CambioEuroLibra` | 2 | No devuelta | Tipos de cambio | Referencia histórica de cambio | Validar procedencia y usos; no sustituir importes históricos |
| `CamposF_NOM` | 2 | Campo | Nóminas | Empleados/nóminas/plantillas/vínculos | Traslado histórico pendiente; no recontabilizar |
| `CategoriasIVA` | 3 | IdCategoria | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `CategoriasIVAPorDepartamento` | 3 | CODCDP | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `CodigosPais` | 9 | No devuelta | Geografía/divisas | Catálogos y equivalencias | Distinguir catálogo estándar y configuración local |
| `ConceptosCaja` | 83 | No devuelta | Conceptos | Conceptos/configuración | Script parcial de ensayo; adaptar |
| `CuadreMayor` | 5 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `CuentaBalanceIVA` | 6 | TipoIva, RepSop, TipoOperacion, TipoProducto | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `CuentasContables` | 2 | No devuelta | Cuentas | Identidades y cuentas anuales | Revisar contra contrato vigente |
| `CuentasInmuebles` | 3 | IdInmueble, IdPlantilla | Activos | Activos, cuentas y datos específicos | Importador parcial; revisar campos no trasladados |
| `CuentasIVA` | 4 | IdCategoriaIVA, IdTipoIVA, SoportadoRepercutido | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `DatosBancarios` | 18 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `DatosBancariosGestion` | 15 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `DatosContacto` | 9 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `Departamentos` | 2 | No devuelta | Analítica | Centros de coste y jerarquía | Equivalencias por sociedad pendientes |
| `Documentos` | 34 | No devuelta | Documentos | Originales, enlaces y plantillas | Contrato documental y contenido externo por verificar |
| `DocumentosFacturas` | 2 | No devuelta | Documentos | Originales, enlaces y plantillas | Contrato documental y contenido externo por verificar |
| `DocumentosPersonas` | 2 | No devuelta | Documentos | Originales, enlaces y plantillas | Contrato documental y contenido externo por verificar |
| `EntidadesBancarias` | 2 | No devuelta | Reglas bancarias | Catálogos y reglas Norma 43 | Distinguir datos funcionales y formatos del producto |
| `Errores de pegado` | 57 | No devuelta | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `EsquemaBalance` | 12 | No devuelta | Reporting | Definiciones y plantillas | Traducir formatos admitidos y separar configuración |
| `EsquemaBalanceGrupos` | 5 | No devuelta | Reporting | Definiciones y plantillas | Traducir formatos admitidos y separar configuración |
| `EsquemaBalancePyGApartados` | 2 | No devuelta | Reporting | Definiciones y plantillas | Traducir formatos admitidos y separar configuración |
| `EsquemasFraccionamiento` | 2 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `EstadosFacturacion` | 14 | No devuelta | Ventas | Facturas/líneas/estado histórico | Script parcial; falta contrato histórico completo |
| `F_347` | 46 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `F_349` | 21 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `F_Anulacion_Asientos` | 7 | No devuelta | Contadores/anulaciones | Reglas o archivo de referencia | Antecedente propone exclusión; contrastar antes de descartar |
| `F_APU` | 24 | No devuelta | Asientos | Asientos/apuntes/revisiones | Adaptación ERP al motor actual pendiente |
| `F_APUMANUAL` | 14 | No devuelta | Asientos | Asientos/apuntes/revisiones | Adaptación ERP al motor actual pendiente |
| `F_BAN` | 48 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `F_CDP` | 4 | No devuelta | Analítica | Centros de coste y jerarquía | Equivalencias por sociedad pendientes |
| `F_CLI` | 62 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `F_CLIAGRUPADOS` | 2 | No devuelta | Contadores/anulaciones | Reglas o archivo de referencia | Antecedente propone exclusión; contrastar antes de descartar |
| `F_DEP` | 3 | No devuelta | Analítica | Centros de coste y jerarquía | Equivalencias por sociedad pendientes |
| `F_DIA` | 3 | No devuelta | Contadores/anulaciones | Reglas o archivo de referencia | Antecedente propone exclusión; contrastar antes de descartar |
| `F_EFC` | 20 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `F_EFP` | 20 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `F_EMP` | 50 | No devuelta | Empresa ContaSol | Contexto del origen contable | No cargar como otra sociedad sin reconciliar identidad |
| `F_IVR` | 88 | No devuelta | Libros IVA | Libros y desgloses | Despivotar y vincular sin duplicar |
| `F_IVS` | 90 | No devuelta | Libros IVA | Libros y desgloses | Despivotar y vincular sin duplicar |
| `F_MAE` | 11 | No devuelta | Cuentas | Identidades y cuentas anuales | Revisar contra contrato vigente |
| `F_NOM` | 36 | No devuelta | Nóminas | Empleados/nóminas/plantillas/vínculos | Traslado histórico pendiente; no recontabilizar |
| `F_NOM_ALIAS` | 2 | Alias | Nóminas | Empleados/nóminas/plantillas/vínculos | Traslado histórico pendiente; no recontabilizar |
| `F_OAS` | 4 | No devuelta | Asientos | Asientos/apuntes/revisiones | Adaptación ERP al motor actual pendiente |
| `F_PRO` | 57 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `F_SAL` | 62 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `FacturasCompra` | 55 | No devuelta | Compra | Compras, clasificación y vínculos | Sin migrador ERP integral acreditado |
| `FacturasCompra1` | 55 | No devuelta | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `FacturasProgramadas` | 59 | No devuelta | Programaciones | Programaciones/líneas | Script parcial; validar periodicidad e identidades |
| `FacturasProgramadas1` | 59 | No devuelta | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `FacturasVenta` | 68 | No devuelta | Ventas | Facturas/líneas/estado histórico | Script parcial; falta contrato histórico completo |
| `Fechas` | 1 | No devuelta | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `FechasFactura` | 4 | IdFactura | Ventas | Facturas/líneas/estado histórico | Script parcial; falta contrato histórico completo |
| `FMAE2023Backup` | 11 | CODMAE | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `FMAE2024AntesCambioF_MAE` | 57 | CODMAE | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `FormasPago` | 64 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `FormatoExportacionExcel` | 8 | TipoInforme, Campo | Reporting | Definiciones y plantillas | Traducir formatos admitidos y separar configuración |
| `FPRO2024AntesCambioF_PRO` | 57 | CODPRO | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `FraccionesEsquema` | 11 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `Frases` | 2 | No devuelta | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `IAE` | 3 | Codigo | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `Inmuebles` | 21 | Id | Activos | Activos, cuentas y datos específicos | Importador parcial; revisar campos no trasladados |
| `InmueblesRecibosSEPA` | 6 | Titular, Fecha | SEPA legacy | Tesorería/archivo según decisión | Excluido del importador Activos; no implica descarte global |
| `InmueblesTitularesSEPA` | 3 | No devuelta | SEPA legacy | Tesorería/archivo según decisión | Excluido del importador Activos; no implica descarte global |
| `InspeccionIVAActividades` | 6 | DEPCDP, CODCDP | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `InspeccionIVAEquivalenciasCuentasGasto` | 10 | No devuelta | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `InspeccionIVANacionalidades` | 2 | DNI | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `InspeccionIVATiposDeGasto` | 2 | Codigo | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `LineasFacturaProgramada` | 39 | No devuelta | Programaciones | Programaciones/líneas | Script parcial; validar periodicidad e identidades |
| `LineasFacturaVenta` | 44 | No devuelta | Ventas | Facturas/líneas/estado histórico | Script parcial; falta contrato histórico completo |
| `MayorClientesDepor2012` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2013` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2014` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2015` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2016` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2017` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2018` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2019` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2020` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2021` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2022` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2023` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `MayorClientesDepor2024` | 11 | No devuelta | Históricos de comparación | Archivo y control | Evitar duplicidad con contabilidad; confirmar alcance por ejercicio |
| `Monedas` | 3 | No devuelta | Geografía/divisas | Catálogos y equivalencias | Distinguir catálogo estándar y configuración local |
| `N43BBVA` | 8 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43Cabeceras` | 35 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43EntidadesBancarias` | 11 | No devuelta | Reglas bancarias | Catálogos y reglas Norma 43 | Distinguir datos funcionales y formatos del producto |
| `N43Esquema` | 9 | No devuelta | Reglas bancarias | Catálogos y reglas Norma 43 | Distinguir datos funcionales y formatos del producto |
| `N43FormatoFicherosTarjetas` | 6 | No devuelta | Reglas bancarias | Catálogos y reglas Norma 43 | Distinguir datos funcionales y formatos del producto |
| `N43Lineas` | 41 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43Lineas2` | 40 | NumLinea | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `N43LineasBAK` | 41 | No devuelta | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `N43LineasBak2` | 41 | NumLinea | Copias/versiones | Comparación y archivo | Comprobar información exclusiva; no omitir por nombre |
| `N43LineasBBVA` | 40 | NumLinea | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43LotesImportacion` | 6 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43PalabrasClave` | 17 | No devuelta | Reglas bancarias | Catálogos y reglas Norma 43 | Distinguir datos funcionales y formatos del producto |
| `N43PayPal` | 80 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `N43Tarjetas` | 33 | No devuelta | Banca histórica | Lotes/extractos/líneas y referencias | Mapa histórico pendiente; evitar doble fuente |
| `Operadores` | 4 | No devuelta | Seguridad | Identidades/grupos/auditoría | Adaptar permisos; no copiar contraseñas legacy |
| `Pagos` | 81 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `Paises` | 7 | No devuelta | Geografía/divisas | Catálogos y equivalencias | Distinguir catálogo estándar y configuración local |
| `PaisesMonedas` | 4 | No devuelta | Geografía/divisas | Catálogos y equivalencias | Distinguir catálogo estándar y configuración local |
| `PalabrasClaveSubDepartamento` | 3 | No devuelta | Compra | Compras, clasificación y vínculos | Sin migrador ERP integral acreditado |
| `PalabrasClaveTipoGasto` | 2 | No devuelta | Compra | Compras, clasificación y vínculos | Sin migrador ERP integral acreditado |
| `Personas` | 143 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `PlantillasApuntes` | 9 | No devuelta | Nóminas | Empleados/nóminas/plantillas/vínculos | Traslado histórico pendiente; no recontabilizar |
| `PlantillasInmuebles` | 6 | Id, Tipo | Activos | Activos, cuentas y datos específicos | Importador parcial; revisar campos no trasladados |
| `PlantillasPDF` | 6 | No devuelta | Documentos | Originales, enlaces y plantillas | Contrato documental y contenido externo por verificar |
| `Poblaciones` | 10 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `PrefijosVIES` | 4 | No devuelta | Geografía/divisas | Catálogos y equivalencias | Distinguir catálogo estándar y configuración local |
| `Provincias` | 6 | No devuelta | Terceros | Clientes/proveedores y domicilios | Mapeo parcial; inactivos y deduplicación pendientes |
| `Recibos` | 24 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `RegistroCambioCuentasContables` | 7 | No devuelta | Cuentas | Identidades y cuentas anuales | Revisar contra contrato vigente |
| `RemesasRecibos` | 29 | No devuelta | Tesorería | Bancos/cobros/pagos/mandatos/remesas | Destino y cobertura global por cerrar |
| `Repeat` | 3 | Repeticiones, Repeticion | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `ReservasPisosTuristicos` | 21 | No devuelta | Reservas | Por definir | Decidir alcance y archivo; sin destino acreditado |
| `SaldoClientesCalculado` | 4 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `SaldoClientesCalculadoFacturas` | 8 | No devuelta | Derivados | Reconstrucción o referencia de control | Antecedentes proponen derivación; confirmar uso/contenido |
| `Sistema` | 10 | No devuelta | Contexto | Sociedades/ejercicios y configuración | Resolver fuente autoritativa y separación anual |
| `Sociedades` | 42 | No devuelta | Contexto | Sociedades/ejercicios y configuración | Resolver fuente autoritativa y separación anual |
| `TiposDeIVA` | 4 | IdTipoIVA | Configuración IVA | Configuración fiscal y analítica | Traducir por ejercicio/módulo, con cobertura explícita |
| `TiposInmueble` | 2 | IdTipo | Activos | Activos, cuentas y datos específicos | Importador parcial; revisar campos no trasladados |
| `TiposOperacionCompra` | 2 | IdTipoOperacion | Compra | Compras, clasificación y vínculos | Sin migrador ERP integral acreditado |
| `Validaciones` | 7 | IdCheck | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |
| `zLogMaterializacion` | 8 | Id | Auxiliares | Metadatos/archivo/decisión | No cargar automáticamente; documentar función y descarte |

## Cobertura del inventario

Las 135 tablas tienen una fila y una clasificación inicial. Esto mide la cobertura del inventario, no el porcentaje implementado del migrador. Las tablas de copias, auxiliares y derivados necesitan una decisión documentada; un nombre parecido o la ausencia de PK no permite descartar datos.

El proveedor expuso también 114 vistas. Se tratarán como evidencia de reglas y consultas de contraste, no como otras 114 fuentes de datos independientes. No se ejecutaron. La migración de reglas, informes y formatos requiere revisar sus definiciones y sustituir solo las que corresponda en los módulos actuales.

Este catálogo no incluye todavía archivos externos referenciados, todas las consultas de acción, módulos VBA ni datos nuevos de la BD de pruebas. La disponibilidad, contenido y procedencia de esos elementos siguen pendientes del perfilado y del alcance definitivo.
