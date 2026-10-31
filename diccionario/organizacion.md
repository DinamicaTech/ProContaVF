<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · organizacion

## FormasPago

- Dueño funcional: `organizacion/formas-pago`
- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.

## F_CDP

- Dueño funcional: `organizacion/geografia`
- Tabla **vinculada** a otro fichero: su estructura no se lee desde la maestra.

## Sociedades

- Dueño funcional: `organizacion/sociedades-ejercicios`
- Maestro vigente de empresas emisoras.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSociedad | TEXT(3) | no |  | Código estable de la empresa. Clave primaria de tres caracteres, procedente de Sociedades.Codigo. |
| Nombre | TEXT(255) | no |  | Nombre o denominación vigente de la empresa. |
| CIF | TEXT(10) | no |  | NIF/CIF de la empresa obligada a expedir las facturas. |
| FormaPagoDefecto | LONG | sí |  | Código de la forma de pago utilizada por defecto. Referencia a FormasPago.Codigo. |
| Usuario | LONG | sí |  | Identificador del usuario asociado a la configuración de la empresa. |
| RutaFacturasExportadas | TEXT(255) | sí |  | Ruta donde se archivan las facturas exportadas correctamente. |
| RutaFacturasDuplicadas | TEXT(255) | sí |  | Ruta utilizada para facturas o archivos detectados como duplicados. |
| TipoRetencion | LONG | sí |  | Tipo o código de retención configurado por defecto para la empresa. |
| PorcentajeRetencion | LONG | sí |  | Porcentaje de retención aplicado por defecto. |
| TipoIVA1 | SINGLE | sí |  | Tipo de IVA predeterminado número 1 de la empresa. |
| TipoIVA2 | SINGLE | sí |  | Tipo de IVA predeterminado número 2 de la empresa. |
| TipoIVA3 | SINGLE | sí |  | Tipo de IVA predeterminado número 3 de la empresa. |
| TipoIVA4 | SINGLE | sí |  | Tipo de IVA predeterminado número 4 de la empresa. |
| BDFacturasVenta | TEXT(255) | sí |  | Ruta o identificador de la base de datos histórica de facturas de venta. |
| BDERPGestion | TEXT(255) | sí |  | Ruta o identificador de la base de datos del ERP de gestión. |
| BDExportacion | TEXT(255) | sí |  | Ruta o identificador de la base utilizada para exportaciones. |
| CuentaTipoIVA0 | TEXT(10) | sí |  | Cuenta contable asociada al tipo de IVA 0. |
| CuentaTipoIVA1 | TEXT(10) | sí |  | Cuenta contable asociada al tipo de IVA 1. |
| CuentaTipoIVA2 | TEXT(10) | sí |  | Cuenta contable asociada al tipo de IVA 2. |
| CuentaTipoIVA3 | TEXT(10) | sí |  | Cuenta contable asociada al tipo de IVA 3. |
| CuentaTipoIVA4 | TEXT(10) | sí |  | Cuenta contable asociada al tipo de IVA 4. |
| CuentaSuplidoIVA | TEXT(255) | sí |  | Cuenta contable utilizada para el IVA de suplidos. |
| CuentaRetencionIRPF | TEXT(10) | sí |  | Cuenta contable general para retenciones de IRPF. |
| CuentaSuplido | TEXT(255) | sí |  | Cuenta contable utilizada para suplidos. |
| CuentaIVAIntracomunitarioRepercutidoServicios | TEXT(10) | sí |  | Cuenta de IVA repercutido en operaciones intracomunitarias de servicios. |
| CuentaIVAIntracomunitarioRepercutidoBienes | TEXT(10) | sí |  | Cuenta de IVA repercutido en operaciones intracomunitarias de bienes. |
| CuentaIVAIntracomunitarioSoportadoServicios | TEXT(10) | sí |  | Cuenta de IVA soportado en operaciones intracomunitarias de servicios. |
| CuentaIVAIntracomunitarioSoportadoBienes | TEXT(10) | sí |  | Cuenta de IVA soportado en operaciones intracomunitarias de bienes. |
| CuentaIVAInversionSujetoPasivoRepercutido | TEXT(10) | sí |  | Cuenta de IVA repercutido para inversión del sujeto pasivo. |
| CuentaIVAInversionSujetoPasivoSoportado | TEXT(10) | sí |  | Cuenta de IVA soportado para inversión del sujeto pasivo. |
| Cuenta113ParaCuadres | TEXT(10) | sí |  | Cuenta 113 utilizada en procesos de cuadre contable. |
| CuentaRetencionServicios | TEXT(10) | sí |  | Cuenta contable para retenciones de servicios profesionales. |
| CuentaRetencionAlquileres | TEXT(10) | sí |  | Cuenta contable para retenciones de alquileres. |
| CuentaPeriodificacionVentas | TEXT(10) | sí |  | Cuenta contable utilizada para periodificar ingresos por ventas. |
| Cuenta570Tesoreria | TEXT(10) | sí |  | Cuenta contable 570 utilizada para movimientos de caja o tesorería. |

Índices:
- IX_Empresas_CIF: CIF
- PK_Empresas (clave primaria): IdSociedad

## SociedadesEjercicios

- Dueño funcional: `organizacion/sociedades-ejercicios`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSociedadEjercicio | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | sí |  |  |
| Ejercicio | TEXT(4) | sí |  |  |
| Nombre | TEXT(255) | sí |  |  |
| CIF | TEXT(10) | sí |  |  |
| FormaPagoDefecto | LONG | sí |  |  |
| Usuario | LONG | sí |  |  |
| RutaFacturasAExportar | TEXT(255) | sí |  |  |
| RutaFacturasExportadas | TEXT(255) | sí |  |  |
| RutaFacturasDuplicadas | TEXT(255) | sí |  |  |
| TipoRetencion | LONG | sí |  |  |
| PorcentajeRetencion | LONG | sí |  |  |
| TipoIVA1 | SINGLE | sí |  |  |
| TipoIVA2 | SINGLE | sí |  |  |
| TipoIVA3 | SINGLE | sí |  |  |
| TipoIVA4 | SINGLE | sí |  |  |
| BDFacturasVenta | TEXT(255) | sí |  |  |
| BDERPGestion | TEXT(255) | sí |  |  |
| BDExportacion | TEXT(255) | sí |  |  |
| TrimestreActivo | LONG | sí |  |  |
| Activa | YESNO | no |  |  |
| CuentaTipoIVA0 | TEXT(10) | sí |  |  |
| CuentaTipoIVA1 | TEXT(10) | sí |  |  |
| CuentaTipoIVA2 | TEXT(10) | sí |  |  |
| CuentaTipoIVA3 | TEXT(10) | sí |  |  |
| CuentaTipoIVA4 | TEXT(10) | sí |  |  |
| CuentaSuplidoIVA | TEXT(255) | sí |  |  |
| CuentaRetencionIRPF | TEXT(10) | sí |  |  |
| CuentaSuplido | TEXT(255) | sí |  |  |
| CuentaIVAIntracomunitarioRepercutidoServicios | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioRepercutidoBienes | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioSoportadoServicios | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioSoportadoBienes | TEXT(10) | sí |  |  |
| CuentaIVAInversionSujetoPasivoRepercutido | TEXT(10) | sí |  |  |
| CuentaIVAInversionSujetoPasivoSoportado | TEXT(10) | sí |  |  |
| Cuenta113ParaCuadres | TEXT(10) | sí |  |  |
| CuentaRetencionServicios | TEXT(10) | sí |  |  |
| CuentaRetencionAlquileres | TEXT(10) | sí |  |  |
| NumIVSDuplicados | SHORT | sí |  |  |
| CuentaPeriodificacionVentas | TEXT(10) | sí |  |  |
| Cuenta570Tesoreria | TEXT(10) | sí |  |  |
| PorcentajeProrrataAñoAnterior | SINGLE | sí |  |  |

Índices:
- FK_Sociedades_SociedadesEjercicios: IdSociedad
- PK_SociedadesEjercicios (clave primaria): IdSociedadEjercicio

Relaciones:
- FK_Sociedades_SociedadesEjercicios: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

