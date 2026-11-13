<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · organizacion

## FormasPago

- Dueño funcional: `organizacion/formas-pago`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| Id | BYTE | no |  |  |
| Descripcion | TEXT(100) | no |  |  |
| SEPA | YESNO | no |  |  |

Índices:
- PK_FormasPago (clave primaria): Id

## FormasPagoSociedad

- Dueño funcional: `organizacion/formas-pago`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdFormaPagoSociedad | COUNTER | no |  |  |
| IdFormaPago | BYTE | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| IdIdentidadContable | LONG | sí |  |  |

Índices:
- FK_FPS_FormaPago: IdFormaPago
- FK_FPS_Identidad: IdIdentidadContable
- FK_FPS_Sociedad: IdSociedad
- PK_FormasPagoSociedad (clave primaria): IdFormaPagoSociedad
- UX_FPS_SociedadForma (único): IdSociedad, IdFormaPago

Relaciones:
- FK_FPS_FormaPago: (IdFormaPago) → FormasPago (Id); borrado NO ACTION, actualización NO ACTION
- FK_FPS_Identidad: (IdIdentidadContable) → IdentidadesContables (IdIdentidadContable); borrado NO ACTION, actualización NO ACTION
- FK_FPS_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## Sociedades

- Dueño funcional: `organizacion/sociedades-ejercicios`
- Maestro vigente de empresas emisoras.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSociedad | TEXT(3) | no |  | Código estable de la empresa. Clave primaria de tres caracteres, procedente de Sociedades.Codigo. |
| Nombre | TEXT(255) | no |  | Nombre o denominación vigente de la empresa. |
| CIF | TEXT(10) | no |  | NIF/CIF de la empresa obligada a expedir las facturas. |
| Cuenta113ParaCuadres | TEXT(10) | sí |  | Cuenta 113 utilizada en procesos de cuadre contable. |
| Cuenta570Tesoreria | TEXT(10) | sí |  | Cuenta contable 570 utilizada para movimientos de caja o tesorería. |
| DireccionFiscal | TEXT(255) | sí |  |  |
| CodigoPostalFiscal | TEXT(20) | sí |  |  |
| PoblacionFiscal | TEXT(100) | sí |  |  |
| ProvinciaFiscal | TEXT(100) | sí |  |  |
| RutaDocumentos | TEXT(255) | sí |  |  |

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
| FormaPagoDefecto | LONG | sí |  |  |
| TrimestreActivo | LONG | sí |  |  |
| Activa | YESNO | no |  |  |
| CuentaSuplido | TEXT(255) | sí |  |  |
| CuentaIVAIntracomunitarioRepercutidoServicios | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioRepercutidoBienes | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioSoportadoServicios | TEXT(10) | sí |  |  |
| CuentaIVAIntracomunitarioSoportadoBienes | TEXT(10) | sí |  |  |
| CuentaIVAInversionSujetoPasivoRepercutido | TEXT(10) | sí |  |  |
| CuentaIVAInversionSujetoPasivoSoportado | TEXT(10) | sí |  |  |
| CuentaRetencionServicios | TEXT(10) | sí |  |  |
| CuentaRetencionAlquileres | TEXT(10) | sí |  |  |
| PorcentajeProrrataAñoAnterior | SINGLE | sí |  |  |
| EjercicioCerrado | YESNO | no | No |  |
| TipoIVAGeneral | SINGLE | sí |  |  |
| CuentaIVAImportacion | TEXT(10) | sí |  |  |
| CuentaRedondeoGastos | TEXT(10) | sí |  |  |
| CuentaRedondeoIngresos | TEXT(10) | sí |  |  |
| CarpetaFacturasCompra | TEXT(255) | sí |  |  |

Índices:
- FK_Sociedades_SociedadesEjercicios: IdSociedad
- PK_SociedadesEjercicios (clave primaria): IdSociedadEjercicio

Relaciones:
- FK_Sociedades_SociedadesEjercicios: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## TiposRetencion

- Dueño funcional: `organizacion/sociedades-ejercicios`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoRetencion | COUNTER | no |  |  |
| Porcentaje | DECIMAL | no |  |  |
| Descripcion | TEXT(100) | no |  |  |
| Clase | BYTE | no |  |  |
| VigenteDesde | DATETIME | sí |  |  |
| VigenteHasta | DATETIME | sí |  |  |
| Activo | YESNO | no |  |  |

Índices:
- PK_TiposRetencion (clave primaria): IdTipoRetencion

