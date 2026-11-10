<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · compras

## ConfiguracionReconocimientoFacturasCompra

- Dueño funcional: `compras/reconocimiento`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracion | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Activo | YESNO | no |  |  |
| SociedadTieneVies | YESNO | no |  |  |
| UsarTextract | YESNO | no |  |  |
| RegionAWS | TEXT(30) | sí |  |  |
| ClaveAccesoAWS | TEXT(128) | sí |  |  |
| SecretoAWSProtegido | LONGTEXT | sí |  |  |
| UmbralAceptacion | DECIMAL | no |  |  |
| UmbralDiscrepanciaImporte | CURRENCY | no |  |  |
| Version | LONG | no |  |  |

Índices:
- FK_CRFC_Sociedad: IdSociedad
- PK_ConfiguracionReconocimientoFC (clave primaria): IdConfiguracion
- UX_CRFC_Sociedad (único): IdSociedad

Relaciones:
- FK_CRFC_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## DesglosesFacturaCompra

- Dueño funcional: `compras/factura-compra`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDesgloseFacturaCompra | COUNTER | no |  |  |
| IdRegistro | LONG | no |  |  |
| Orden | SHORT | no |  |  |
| TipoIVA | DECIMAL | no |  |  |
| BaseImponible | CURRENCY | no |  |  |
| CuotaIVA | CURRENCY | no |  |  |
| Confianza | DECIMAL | sí |  |  |
| Evidencia | TEXT(255) | sí |  |  |

Índices:
- FK_DFC_FacturaCompra: IdRegistro
- PK_DesglosesFacturaCompra (clave primaria): IdDesgloseFacturaCompra
- UX_DFC_FacturaOrden (único): IdRegistro, Orden

Relaciones:
- FK_DFC_FacturaCompra: (IdRegistro) → FacturasCompra (IdRegistro); borrado NO ACTION, actualización NO ACTION

## FacturasCompra

- Dueño funcional: `compras/factura-compra`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRegistro | COUNTER | no |  |  |
| IdSociedad | LONG | sí |  |  |
| Sociedad | TEXT(3) | sí |  |  |
| Ejercicio | TEXT(4) | sí |  |  |
| CuentaContableProveedor | TEXT(30) | sí |  |  |
| NumeroFactura | TEXT(255) | sí |  |  |
| Fecha | DATETIME | sí |  |  |
| Concepto | TEXT(255) | sí |  |  |
| ConceptoGasto | TEXT(10) | sí |  |  |
| CuentaFormaPago | TEXT(255) | sí |  |  |
| FechaVencimiento | TEXT(255) | sí |  |  |
| Pagada | YESNO | no |  |  |
| Archivo | TEXT(255) | sí |  |  |
| MD5 | TEXT(32) | sí |  |  |
| ImporteBI | CURRENCY | sí |  |  |
| ImporteIVA | CURRENCY | sí |  |  |
| ImporteTotal | CURRENCY | sí |  |  |
| ImporteBI0 | CURRENCY | sí |  |  |
| ImporteBI1 | CURRENCY | sí |  |  |
| ImporteBI2 | CURRENCY | sí |  |  |
| ImporteBI3 | CURRENCY | sí |  |  |
| ImporteBI4 | CURRENCY | sí |  |  |
| TipoIVA1 | SINGLE | sí |  |  |
| TipoIVA2 | SINGLE | sí |  |  |
| TipoIVA3 | SINGLE | sí |  |  |
| TipoIVA4 | SINGLE | sí |  |  |
| Exportada | YESNO | no |  |  |
| DIAAPU | SHORT | sí |  |  |
| FECAPU | DATETIME | sí |  |  |
| ASIAPU | LONG | sí |  |  |
| FechaHoraExportacion | DATETIME | sí |  |  |
| ArchivoOriginal | TEXT(255) | sí |  |  |
| FechaHoraRegistro | DATETIME | sí |  |  |
| Duplicada | YESNO | no |  |  |
| Carpeta | TEXT(255) | sí |  |  |
| NombreFichero | TEXT(255) | sí |  |  |
| TipoOperacion | LONG | sí |  |  |
| TipoProducto | LONG | sí |  |  |
| IVASuplido | CURRENCY | sí |  |  |
| IdRegistroFacturaImportacionSuplido | LONG | sí |  |  |
| IdRegistroFacturaTransporte | LONG | sí |  |  |
| NotasFactura | LONGTEXT | sí |  |  |
| TextoAsiento | LONGTEXT | sí |  |  |
| ImporteDolares | CURRENCY | sí |  |  |
| NIF | TEXT(255) | sí |  |  |
| PorcentajeRetencion | SINGLE | sí |  |  |
| TipoRetencion | BYTE | sí |  |  |
| IdRegistroIVA | LONG | sí |  |  |
| InversionSujetoPasivo | YESNO | no |  |  |
| LineaN43 | LONG | sí |  |  |
| ASIAPURectificativa | LONG | sí |  |  |
| IdRegistroIVARectificativa | LONG | sí |  |  |
| Suplido | CURRENCY | sí |  |  |
| IdDepartamento | LONG | sí |  |  |
| IdSubDepartamento | LONG | sí |  |  |
| IdSociedadEjercicio | LONG | sí |  |  |
| IdProveedor | LONG | sí |  |  |
| ImporteRetencion | CURRENCY | sí |  |  |
| Divisa | TEXT(3) | sí |  |  |
| ImporteTotalDivisa | CURRENCY | sí |  |  |
| TipoCambioAplicado | DECIMAL | sí |  |  |
| FechaTipoCambio | DATETIME | sí |  |  |
| IdRegistroFacturaRectificada | LONG | sí |  |  |
| NombreProveedorAeat | TEXT(150) | sí |  |  |
| ConfianzaImportacion | DECIMAL | sí |  |  |
| RequiereRevisionImportacion | YESNO | no | False |  |
| FuenteImportacion | TEXT(10) | sí |  |  |
| LOGImportacion | LONGTEXT | sí |  |  |
| Version | LONG | no | 0 |  |
| IdCentroCoste | LONG | sí |  |  |
| IdFormaPago | BYTE | sí |  |  |
| IdAsientoContable | LONG | sí |  |  |
| IdAsientoAnulado | LONG | sí |  |  |
| GeneracionExportacion | LONG | sí |  |  |
| OrigenDocumento | TEXT(10) | sí |  |  |
| ConceptoFactura | TEXT(255) | sí |  |  |
| ConceptoAbreviado | TEXT(60) | sí |  |  |

Índices:
- FK_FacturasCompra_FormaPago: IdFormaPago
- IX_FacturasCompra_CentroCoste: IdCentroCoste
- IX_FacturasCompra_MD5: MD5
- IX_FacturasCompra_Proveedor: IdProveedor
- IX_FacturasCompra_SociedadEjercicio: IdSociedadEjercicio
- UX_FacturasCompra_IdRegistro (único): IdRegistro

Relaciones:
- FK_FacturasCompra_FormaPago: (IdFormaPago) → FormasPago (Id); borrado NO ACTION, actualización NO ACTION

## PlantillasReconocimientoCompra

- Dueño funcional: `compras/reconocimiento`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPlantilla | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Carpeta | TEXT(255) | no |  |  |
| Campo | TEXT(10) | no |  |  |
| Pagina | SHORT | no |  |  |
| Ancla | TEXT(120) | no |  |  |
| Posicion | TEXT(10) | no |  |  |
| Palabras | SHORT | no |  |  |
| X | DOUBLE | no |  |  |
| Y | DOUBLE | no |  |  |
| DesplazamientoX | DOUBLE | no |  |  |
| Aciertos | LONG | no |  |  |
| FechaAlta | DATETIME | no |  |  |
| FechaUso | DATETIME | no |  |  |
| NIF | TEXT(20) | sí |  |  |

Índices:
- FK_PRC_Sociedad: IdSociedad
- IX_PRC_SociedadCarpeta: IdSociedad, Carpeta
- IX_PRC_SociedadNif: IdSociedad, NIF
- PK_PlantillasReconocimientoCompra (clave primaria): IdPlantilla

Relaciones:
- FK_PRC_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## TextosClaveCompra

- Dueño funcional: `compras/factura-compra`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTextoClave | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Texto | TEXT(100) | no |  |  |
| IdIdentidadContable | LONG | sí |  |  |
| IdCentroCoste | LONG | sí |  |  |

Índices:
- FK_TCC_CentroCoste: IdCentroCoste
- FK_TCC_Identidad: IdIdentidadContable
- FK_TCC_Sociedad: IdSociedad
- PK_TextosClaveCompra (clave primaria): IdTextoClave
- UX_TCC_SociedadTexto (único): IdSociedad, Texto

Relaciones:
- FK_TCC_CentroCoste: (IdCentroCoste) → CentrosCoste (IdCentroCoste); borrado NO ACTION, actualización NO ACTION
- FK_TCC_Identidad: (IdIdentidadContable) → IdentidadesContables (IdIdentidadContable); borrado NO ACTION, actualización NO ACTION
- FK_TCC_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

