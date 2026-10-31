<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · norma43

## Bancos

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdBanco | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| CodigoContaSol | TEXT(30) | no |  |  |
| Nombre | TEXT(150) | no |  |  |
| IdCuentaContable | LONG | sí |  |  |
| Iban | TEXT(34) | sí |  |  |
| Bic | TEXT(20) | sí |  |  |
| Activo | YESNO | no |  |  |

Índices:
- FK_Bancos_Cuenta: IdCuentaContable
- FK_Bancos_Sociedad: IdSociedad
- PK_Bancos (clave primaria): IdBanco
- UX_Bancos_SociedadCodigo (único): IdSociedad, CodigoContaSol

Relaciones:
- FK_Bancos_Cuenta: (IdCuentaContable) → CuentasContables (IdCuentaContable); borrado NO ACTION, actualización NO ACTION
- FK_Bancos_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## ComplementosLineaExtractoBancario

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdComplementoLineaExtracto | COUNTER | no |  |  |
| IdLineaExtractoBancario | LONG | no |  |  |
| OrdinalComplemento | LONG | no |  |  |
| NumeroLineaFisica | LONG | no |  |  |
| TipoRegistro | TEXT(2) | no |  |  |
| CodigoDato | TEXT(2) | sí |  |  |
| Concepto1 | TEXT(59) | sí |  |  |
| Concepto2 | TEXT(38) | sí |  |  |
| Divisa | TEXT(3) | sí |  |  |
| Importe | CURRENCY | sí |  |  |
| TextoOriginal | TEXT(80) | no |  |  |

Índices:
- FK_Complementos_Linea: IdLineaExtractoBancario
- PK_ComplementosLineaExtracto (clave primaria): IdComplementoLineaExtracto
- UX_ComplementosLinea_Ordinal (único): IdLineaExtractoBancario, OrdinalComplemento

Relaciones:
- FK_Complementos_Linea: (IdLineaExtractoBancario) → LineasExtractoBancario (IdLineaExtractoBancario); borrado NO ACTION, actualización NO ACTION

## ContabilizacionesLineaExtracto

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdContabilizacionLineaExtracto | COUNTER | no |  |  |
| IdLineaExtractoBancario | LONG | no |  |  |
| IdAsiento | LONG | no |  |  |
| Estado | TEXT(20) | no |  |  |
| VersionProceso | LONG | no |  |  |
| FechaHora | DATETIME | no |  |  |
| Operador | TEXT(100) | sí |  |  |

Índices:
- FK_Contabilizaciones_Linea: IdLineaExtractoBancario
- PK_ContabilizacionesLineaExtracto (clave primaria): IdContabilizacionLineaExtracto

Relaciones:
- FK_Contabilizaciones_Linea: (IdLineaExtractoBancario) → LineasExtractoBancario (IdLineaExtractoBancario); borrado NO ACTION, actualización NO ACTION

## CuentasBancarias

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaBancaria | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| IdEntidadFinanciera | LONG | sí |  |  |
| EntidadOriginal | TEXT(4) | no |  |  |
| Oficina | TEXT(4) | no |  |  |
| Cuenta | TEXT(10) | no |  |  |
| Iban | TEXT(34) | sí |  |  |
| Divisa | TEXT(3) | no |  |  |
| Nombre | TEXT(150) | sí |  |  |
| Activa | YESNO | no |  |  |

Índices:
- FK_CuentasBancarias_Entidad: IdEntidadFinanciera
- FK_CuentasBancarias_Sociedad: IdSociedad
- PK_CuentasBancarias (clave primaria): IdCuentaBancaria
- UX_CuentasBancarias_Coordenadas (único): IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa

Relaciones:
- FK_CuentasBancarias_Entidad: (IdEntidadFinanciera) → EntidadesFinancieras (IdEntidadFinanciera); borrado NO ACTION, actualización NO ACTION
- FK_CuentasBancarias_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## EntidadesFinancieras

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEntidadFinanciera | COUNTER | no |  |  |
| Codigo | TEXT(4) | no |  |  |
| Nombre | TEXT(150) | sí |  |  |
| Activa | YESNO | no |  |  |

Índices:
- PK_EntidadesFinancieras (clave primaria): IdEntidadFinanciera
- UX_EntidadesFinancieras_Codigo (único): Codigo

## ExtractosBancarios

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdExtractoBancario | COUNTER | no |  |  |
| IdLoteImportacionBancaria | LONG | no |  |  |
| OrdinalExtracto | LONG | no |  |  |
| IdCuentaBancaria | LONG | sí |  |  |
| Entidad | TEXT(4) | no |  |  |
| Oficina | TEXT(4) | no |  |  |
| Cuenta | TEXT(10) | no |  |  |
| Divisa | TEXT(3) | no |  |  |
| FechaInicio | DATETIME | no |  |  |
| FechaFin | DATETIME | no |  |  |
| SentidoSaldoInicial | TEXT(1) | no |  |  |
| SaldoInicial | CURRENCY | no |  |  |
| Modalidad | TEXT(1) | sí |  |  |
| Nombre | TEXT(150) | sí |  |  |
| NumeroApuntesDebeDeclarado | LONG | no |  |  |
| TotalDebeDeclarado | CURRENCY | no |  |  |
| NumeroApuntesHaberDeclarado | LONG | no |  |  |
| TotalHaberDeclarado | CURRENCY | no |  |  |
| NumeroApuntesDebeCalculado | LONG | no |  |  |
| TotalDebeCalculado | CURRENCY | no |  |  |
| NumeroApuntesHaberCalculado | LONG | no |  |  |
| TotalHaberCalculado | CURRENCY | no |  |  |
| CodigoSeguridad | TEXT(1) | sí |  |  |
| SaldoFinal | CURRENCY | no |  |  |
| EstadoCuadre | TEXT(20) | no |  |  |
| TextoCabeceraOriginal | TEXT(80) | no |  |  |
| TextoCierreOriginal | TEXT(80) | no |  |  |

Índices:
- FK_Extractos_Cuenta: IdCuentaBancaria
- FK_Extractos_Lote: IdLoteImportacionBancaria
- PK_ExtractosBancarios (clave primaria): IdExtractoBancario
- UX_ExtractosBanco_Ordinal (único): IdLoteImportacionBancaria, OrdinalExtracto

Relaciones:
- FK_Extractos_Cuenta: (IdCuentaBancaria) → CuentasBancarias (IdCuentaBancaria); borrado NO ACTION, actualización NO ACTION
- FK_Extractos_Lote: (IdLoteImportacionBancaria) → LotesImportacionBancaria (IdLoteImportacionBancaria); borrado NO ACTION, actualización NO ACTION

## ImputacionesLineaExtractoBancario

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdImputacionLineaExtractoBancario | COUNTER | no |  |  |
| IdLineaExtractoBancario | LONG | no |  |  |
| IdIdentidadContable | LONG | no |  |  |
| Rol | TEXT(30) | no |  |  |
| ImporteFirmado | CURRENCY | no |  |  |
| Origen | TEXT(30) | no |  |  |
| Confianza | DECIMAL | sí |  |  |
| Estado | TEXT(20) | no |  |  |
| Concepto | LONGTEXT | sí |  |  |
| IdCentroCoste | LONG | sí |  |  |

Índices:
- FK_Imputaciones_Identidad: IdIdentidadContable
- FK_Imputaciones_Linea: IdLineaExtractoBancario
- IX_ImputacionesLinea_Linea: IdLineaExtractoBancario
- PK_ImputacionesLineaExtracto (clave primaria): IdImputacionLineaExtractoBancario

Relaciones:
- FK_Imputaciones_Identidad: (IdIdentidadContable) → IdentidadesContables (IdIdentidadContable); borrado NO ACTION, actualización NO ACTION
- FK_Imputaciones_Linea: (IdLineaExtractoBancario) → LineasExtractoBancario (IdLineaExtractoBancario); borrado NO ACTION, actualización NO ACTION

## IncidenciasImportacionBancaria

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIncidenciaImportacionBancaria | COUNTER | no |  |  |
| IdLoteImportacionBancaria | LONG | no |  |  |
| NumeroLineaFisica | LONG | sí |  |  |
| Codigo | TEXT(50) | no |  |  |
| Severidad | TEXT(20) | no |  |  |
| Mensaje | LONGTEXT | no |  |  |
| RegistroOriginal | TEXT(80) | sí |  |  |
| EstadoResolucion | TEXT(20) | no |  |  |

Índices:
- FK_IncidenciasBanco_Lote: IdLoteImportacionBancaria
- PK_IncidenciasImportacionBancaria (clave primaria): IdIncidenciaImportacionBancaria

Relaciones:
- FK_IncidenciasBanco_Lote: (IdLoteImportacionBancaria) → LotesImportacionBancaria (IdLoteImportacionBancaria); borrado NO ACTION, actualización NO ACTION

## LineasExtractoBancario

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLineaExtractoBancario | COUNTER | no |  |  |
| IdExtractoBancario | LONG | no |  |  |
| OrdinalMovimiento | LONG | no |  |  |
| NumeroLineaFisica | LONG | no |  |  |
| OficinaOrigen | TEXT(4) | sí |  |  |
| FechaOperacion | DATETIME | no |  |  |
| FechaValor | DATETIME | no |  |  |
| ConceptoComun | TEXT(2) | sí |  |  |
| ConceptoPropio | TEXT(3) | sí |  |  |
| Sentido | TEXT(1) | no |  |  |
| Importe | CURRENCY | no |  |  |
| Documento | TEXT(10) | sí |  |  |
| Referencia1 | TEXT(12) | sí |  |  |
| Referencia2 | TEXT(16) | sí |  |  |
| ConceptoCompleto | LONGTEXT | sí |  |  |
| EntidadOficinaDestino | TEXT(9) | sí |  |  |
| TextoOriginal | TEXT(80) | no |  |  |

Índices:
- FK_LineasExtracto_Extracto: IdExtractoBancario
- PK_LineasExtractoBancario (clave primaria): IdLineaExtractoBancario
- UX_LineasExtracto_Ordinal (único): IdExtractoBancario, OrdinalMovimiento

Relaciones:
- FK_LineasExtracto_Extracto: (IdExtractoBancario) → ExtractosBancarios (IdExtractoBancario); borrado NO ACTION, actualización NO ACTION

## LotesImportacionBancaria

- Dueño funcional: `norma43`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLoteImportacionBancaria | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| Formato | TEXT(20) | no |  |  |
| VersionEsquema | TEXT(30) | no |  |  |
| NombreFichero | TEXT(255) | no |  |  |
| RutaOrigen | LONGTEXT | sí |  |  |
| HashSha256Contenido | TEXT(64) | no |  |  |
| TamanoBytes | LONG | no |  |  |
| Codificacion | TEXT(30) | no |  |  |
| Estado | TEXT(30) | no |  |  |
| FechaHoraInicio | DATETIME | no |  |  |
| FechaHoraFin | DATETIME | sí |  |  |
| NumeroRegistrosDeclarado | LONG | sí |  |  |
| NumeroRegistrosLeido | LONG | no |  |  |
| NumeroExtractos | LONG | no |  |  |
| NumeroMovimientos | LONG | no |  |  |
| NumeroErrores | LONG | no |  |  |
| NumeroAdvertencias | LONG | no |  |  |

Índices:
- FK_LotesBanco_Sociedad: IdSociedad
- PK_LotesImportacionBancaria (clave primaria): IdLoteImportacionBancaria
- UX_LotesBanco_Hash (único): IdSociedad, Formato, HashSha256Contenido

Relaciones:
- FK_LotesBanco_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

