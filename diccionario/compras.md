<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · compras

## DesglosesIVASoportado

- Dueño funcional: `compras/factura-compra`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDesgloseIVASoportado | COUNTER | no |  |  |
| IdIVASoportado | LONG | no |  |  |
| Orden | LONG | no |  |  |
| ClaveRegimen | TEXT(10) | sí |  |  |
| CalificacionOperacion | TEXT(30) | no |  |  |
| CausaExencion | TEXT(10) | sí |  |  |
| BaseImponible | CURRENCY | no |  |  |
| TipoIVA | DECIMAL | no |  |  |
| CuotaSoportada | CURRENCY | no |  |  |
| CuotaDevengadaAutorrepercutida | CURRENCY | no |  |  |
| PorcentajeDeducible | DECIMAL | no |  |  |
| CuotaDeducible | CURRENCY | no |  |  |
| IdCuentaIVASoportado | LONG | sí |  |  |
| IdCuentaIVARepercutido | LONG | sí |  |  |

Índices:
- FK_DetIVAS_Cabecera: IdIVASoportado
- PK_DesglosesIVASoportado (clave primaria): IdDesgloseIVASoportado

Relaciones:
- FK_DetIVAS_Cabecera: (IdIVASoportado) → IVASoportado (IdIVASoportado); borrado NO ACTION, actualización NO ACTION

## FacturasCompra

- Dueño funcional: `compras/factura-compra`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRegistro | COUNTER | no |  |  |
| IdSociedad | LONG | sí |  |  |
| Sociedad | TEXT(3) | sí |  |  |
| Ejercicio | TEXT(4) | sí |  |  |
| CuentaContableProveedor | TEXT(10) | sí |  |  |
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
| Carpeta | TEXT(100) | sí |  |  |
| NombreFichero | TEXT(100) | sí |  |  |
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

