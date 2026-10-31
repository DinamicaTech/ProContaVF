<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · facturacion

## Conceptos

- Dueño funcional: `facturacion/conceptos`
- CatÃ¡logo de conceptos facturables y su imputaciÃ³n contable predeterminada por sociedad y ejercicio.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConcepto | LONG | no |  | Identificador manual y Ãºnico del concepto; permite conservar los identificadores migrados del ERP. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y ejercicio para los que se configura el concepto. Relacionado con Sociedades.IdSociedad. |
| IdTipoConcepto | LONG | sí |  | ClasificaciÃ³n opcional del concepto. Relacionado con TiposConcepto.IdTipoConcepto. |
| Nombre | TEXT(255) | no |  | DescripciÃ³n abreviada que aparece como encabezado de la lÃ­nea en la factura. |
| Descripcion | LONGTEXT | sí |  | DescripciÃ³n extensa y multilÃ­nea que aparece debajo del nombre en la factura. |
| CuentaContable | TEXT(7) | sí |  | Cuenta contable de venta predeterminada, con hasta 7 caracteres, utilizada en la exportaciÃ³n contable. |
| IdDepartamento | LONG | sí |  | Departamento analÃ­tico predeterminado. Corresponde a F_DEP.CODDEP. |
| IdCentroCoste | LONG | sí |  | Centro de coste predeterminado. Corresponde a F_CDP.CODCDP dentro del departamento indicado. |
| PrecioUnitario | CURRENCY | sí | 0 | Precio unitario predeterminado al insertar el concepto en una factura. |
| TipoIVA | DOUBLE | sí |  | Porcentaje de IVA predeterminado al insertar el concepto en una factura. |
| Activo | YESNO | no | True | Indica si el concepto puede utilizarse en nuevas facturas. |
| Notas | LONGTEXT | sí |  | Observaciones internas sobre el concepto, no visibles para el cliente. |

Índices:
- FK_Conceptos_TiposConcepto: IdTipoConcepto
- FK_SociedadesEjercicios_Conceptos: IdSociedadEjercicio
- IX_Conceptos_CentroCoste: IdDepartamento, IdCentroCoste
- IX_Conceptos_IdSociedad: IdSociedadEjercicio
- IX_Conceptos_IdTipoConcepto: IdTipoConcepto
- IX_Conceptos_Nombre: Nombre
- PK_Conceptos (clave primaria): IdConcepto

Relaciones:
- FK_Conceptos_F_DEP: (IdDepartamento) → F_DEP (CODDEP); borrado NO ACTION, actualización NO ACTION
- FK_Conceptos_TiposConcepto: (IdTipoConcepto) → TiposConcepto (IdTipoConcepto); borrado NO ACTION, actualización NO ACTION
- FK_SociedadesEjercicios_Conceptos: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## Contratos

- Dueño funcional: `facturacion/contratos`
- Contratos de alquiler que pueden originar facturación periódica.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdContrato | COUNTER | no |  | Identificador autonumérico del contrato. Clave primaria. |
| FechaFirma | DATETIME | sí |  | Fecha en la que se firmó el contrato. |
| IdInmueble | LONG | no |  | Inmueble objeto del contrato. Referencia futura a Inmuebles.Id. |
| FechaInicio | DATETIME | no |  | Fecha de inicio de vigencia del contrato. |
| FechaFin | DATETIME | sí |  | Fecha opcional de finalización del contrato; no puede ser anterior a FechaInicio. |
| ImporteMensual | CURRENCY | no | 0 | Importe mensual base pactado en el contrato. |
| FormaDePago | BYTE | sí |  | Código de la forma de pago. Referencia futura a FormasPago.Codigo. |
| DiaDePago | BYTE | no | 0 | Día mensual previsto de pago: 0 indica sin día fijo; valores permitidos de 0 a 31. |
| ImporteIBI | CURRENCY | sí | 0 | Importe de IBI repercutido según el contrato. |
| ImporteComunidad | CURRENCY | sí | 0 | Importe de gastos de comunidad repercutido según el contrato. |
| ImporteBasuras | CURRENCY | sí | 0 | Importe de tasas de basuras repercutido según el contrato. |
| CondicionesEspeciales | LONGTEXT | sí |  | Texto libre con cláusulas o condiciones especiales del contrato. |
| IdSociedad | TEXT(3) | no |  | Empresa permanente propietaria del contrato. RelaciÃ³n con Empresas.IdEmpresa; no depende del ejercicio contable. |
| IdCliente | LONG | sí |  | Cliente titular del contrato. Referencia a Clientes.IdCliente. |
| TipoIncremento | BYTE | sí |  | Tipo de revision anual: 0 o nulo = sin revision; 1 = IPC; 2 = IRAV. |
| ValorIncremento | DOUBLE | sí |  | Porcentaje oficial de IPC o IRAV materializado para la proxima revision. |
| FechaActualizacionValorIncremento | DATETIME | sí |  | Fecha en que se consulto y materializo el porcentaje oficial. |
| FechaAplicacionIncremento | DATETIME | sí |  | Primer dia del periodo de facturacion desde el que se aplicara el incremento. |
| FechaAplicadaIncremento | DATETIME | sí |  | Fecha en que el incremento se aplico efectivamente a la factura programada. |
| Notas | LONGTEXT | sí |  | Observaciones internas y registro legible de las actualizaciones del contrato. |

Índices:
- FK_Contratos_Clientes: IdCliente
- FK_Sociedades_Contratos: IdSociedad
- IX_Contratos_FechaInicio: FechaInicio
- IX_Contratos_FormaDePago: FormaDePago
- IX_Contratos_IdEmpresa: IdSociedad
- IX_Contratos_IdInmueble: IdInmueble
- PK_Contratos (clave primaria): IdContrato

Relaciones:
- FK_Contratos_Clientes: (IdCliente) → Clientes (IdCliente); borrado NO ACTION, actualización NO ACTION
- FK_Inmuebles_Contratos: (IdInmueble) → Inmuebles (Id); borrado NO ACTION, actualización NO ACTION
- FK_Sociedades_Contratos: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## DesglosesIVARepercutido

- Dueño funcional: `facturacion/factura-venta`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDesgloseIVARepercutido | COUNTER | no |  |  |
| IdIVARepercutido | LONG | no |  |  |
| Orden | LONG | no |  |  |
| ClaveRegimen | TEXT(10) | sí |  |  |
| CalificacionOperacion | TEXT(30) | no |  |  |
| CausaExencion | TEXT(10) | sí |  |  |
| BaseImponible | CURRENCY | no |  |  |
| TipoIVA | DECIMAL | no |  |  |
| CuotaRepercutida | CURRENCY | no |  |  |
| TipoRecargoEquivalencia | DECIMAL | no |  |  |
| CuotaRecargoEquivalencia | CURRENCY | no |  |  |
| IdCuentaIVARepercutido | LONG | sí |  |  |

Índices:
- FK_DetIVAR_Cabecera: IdIVARepercutido
- PK_DesglosesIVARepercutido (clave primaria): IdDesgloseIVARepercutido

Relaciones:
- FK_DetIVAR_Cabecera: (IdIVARepercutido) → IVARepercutido (IdIVARepercutido); borrado NO ACTION, actualización NO ACTION

## EstadoContratoTerceros

- Dueño funcional: `facturacion/contratos`
- TERCEROS_VF_1 solo ACTIVO después de verificar datos, esquema, índices y relaciones. El marcador se publica al final.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| Contrato | TEXT(40) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 40, obligatorio True. |
| Estado | TEXT(15) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 15, obligatorio True. ACTIVO solo tras postcondiciones verificadas. |
| IdPlan | TEXT(36) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 36, obligatorio True. |
| HuellaPlan | TEXT(64) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 64, obligatorio True. |
| FechaActivacion | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |

Índices:
- PK_EstadoContratoTerceros (clave primaria): Contrato

## EstadosFactura

- Dueño funcional: `facturacion/factura-venta`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEstado | BYTE | sí |  |  |
| Nombre | TEXT(40) | no |  |  |
| Descripcion | TEXT(255) | sí |  |  |
| EsModificable | YESNO | no |  |  |
| EsTerminal | YESNO | no |  |  |

Índices:
- PK_EstadosFactura (clave primaria): IdEstado

## Facturas

- Dueño funcional: `facturacion/factura-venta`
- Cabecera comercial y fiscal de las facturas emitidas.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdFactura | COUNTER | no |  | Identificador interno autonumérico de la factura. Clave primaria; no es el número fiscal. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y ejercicio emisor. Referencia futura a Sociedades.IdSociedad. |
| IdSerie | LONG | no |  | Serie utilizada para numerar la factura. Referencia a SeriesFacturacion.IdSerie. |
| NumeroFactura | TEXT(60) | sí |  | Número fiscal completo y visible, incluido el prefijo. Único dentro de cada sociedad. |
| IdCliente | LONG | sí |  | Cliente titular de la factura. Referencia a Clientes.IdCliente. |
| IdContrato | LONG | sí |  | Contrato que originó la factura, si procede. Referencia a Contratos.IdContrato. |
| CIF | TEXT(25) | sí |  | NIF/CIF o identificador fiscal del cliente congelado en el momento de emisión. |
| Nombre | TEXT(150) | sí |  | Nombre habitual del cliente congelado en el momento de emisión. |
| RazonSocial | TEXT(200) | sí |  | Razón social fiscal del cliente congelada en el momento de emisión. |
| PaisCliente | TEXT(2) | sí |  | Código ISO 3166-1 alfa-2 del país del cliente, cuando sea necesario. |
| TipoIdentificacionCliente | BYTE | sí |  | Tipo de identificación AEAT para identificadores extranjeros: 2=NIF-IVA, 3=Pasaporte, 4=Documento oficial del país de residencia, 5=Certificado de residencia, 6=Otro documento probatorio, 7=No censado. |
| NIFEmisor | TEXT(25) | sí |  | NIF del obligado emisor congelado en el momento de emisión. |
| NombreEmisor | TEXT(200) | sí |  | Nombre o razón social del obligado emisor congelado en el momento de emisión. |
| FechaFactura | DATETIME | sí |  | Fecha fiscal de expedición que figura en la factura y se comunica a VeriFactu. |
| FechaOperacion | DATETIME | sí |  | Fecha de realización o devengo de la operación cuando difiere de FechaFactura. |
| FechaInicioValidez | DATETIME | sí |  | Inicio del periodo facturado, por ejemplo el primer día del mes de alquiler. |
| FechaFinValidez | DATETIME | sí |  | Fin del periodo facturado; no puede ser anterior a FechaInicioValidez. |
| FechaCreacion | DATETIME | no | Now() | Fecha y hora interna de creación del borrador. Valor predeterminado: momento actual. |
| FechaHoraEmision | DATETIME | sí |  | Fecha y hora en la que se asigna el número y la factura pasa a ser definitiva e inmutable. No indica por sí sola envío a AEAT. |
| FormaDePago | BYTE | sí |  | Código de la forma de pago pactada. Referencia futura a FormasPago.Codigo. |
| DiaDePago | BYTE | sí |  | Día previsto de pago: 0 indica sin día fijo; valores permitidos de 0 a 31. |
| Descripcion | LONGTEXT | no |  | Descripción de la operación facturada; se utiliza en el registro VeriFactu. |
| NotasInternas | LONGTEXT | sí |  | Observaciones internas que no forman parte del registro VeriFactu. |
| BaseImponible | CURRENCY | no | 0 | Suma de bases imponibles de la factura antes de impuestos y retenciones. |
| IVA | CURRENCY | no | 0 | Total de cuotas de IVA repercutidas. |
| ImporteBruto | CURRENCY | no | 0 | Importe antes de practicar retenciones; normalmente BaseImponible más IVA y otros conceptos. |
| TipoRetencion | SINGLE | sí |  | Porcentaje de retención aplicado a la factura. |
| Retencion | CURRENCY | no | 0 | Importe total retenido, que reduce el importe a pagar. |
| ImporteTotal | CURRENCY | no | 0 | Importe final de la factura después de impuestos y retenciones. |
| TipoFactura | TEXT(2) | no | "F1" | Código oficial del tipo de factura. Referencia a TiposFactura.TipoFactura; F1 por defecto. |
| TipoRectificativa | TEXT(1) | sí |  | Solo para tipos R1-R5: I=rectificación por diferencias; S=rectificación por sustitución; Nulo en facturas no rectificativas. |
| EstadoFactura | BYTE | no | 0 | Estado del ciclo fiscal: 0=PendienteValidar; 1=Validada; 2=Rectificada; 3=Borrador; 4=RectificacionPendiente. Los estados distintos de Borrador no permiten ediciÃ³n directa. |
| IdFacturaRectificada | LONG | sí |  | Factura original corregida por esta factura rectificativa. Referencia única opcional a Facturas.IdFactura. |
| FechaExportacionContable | DATETIME | sí |  | Fecha y hora en que la factura fue exportada definitivamente a contabilidad. |
| IdAsientoContable | LONG | sí |  | Identificador o número del asiento contable generado para la factura. |
| LoteExportacionContable | LONG | sí |  | Lote que agrupó la exportación de la factura. Referencia a LotesExportacionContable.IdLote. |
| PDFFactura | TEXT(255) | sí |  | Ruta del Ãºnico documento PDF generado para la factura. El archivo no se incrusta en la base de datos. |
| Historial | LONGTEXT | sí |  | Registro cronolÃ³gico interno en texto, con una lÃ­nea por evento de la factura: fecha, hora, operador y acciÃ³n. |
| IdFacturaProgramada | LONG | sí |  | Programacion concreta que origino la factura; vacio para facturas creadas manualmente. |
| IdLoteFacturaProgramada | LONG | sí |  | Lote durante el cual se genero la factura; vacio para facturas creadas manualmente. |
| FechaProgramadaOrigen | DATETIME | sí |  | Fecha prevista de la ocurrencia procesada. Junto con IdFacturaProgramada evita generar dos veces la misma factura. |

Índices:
- EstadosFacturaFacturas: EstadoFactura
- FK_Facturas_Clientes: IdCliente
- FK_Facturas_Contratos: IdContrato
- FK_Facturas_LoteProgramado: IdLoteFacturaProgramada
- FK_Facturas_ProgramacionOrigen: IdFacturaProgramada
- FK_Facturas_Rectificada: IdFacturaRectificada
- FK_Facturas_Series: IdSerie
- FK_Facturas_TiposFactura: TipoFactura
- FK_LotesExportacionContable_Facturas: LoteExportacionContable
- FK_SociedadesEjercicios_Facturas: IdSociedadEjercicio
- IX_Facturas_EstadoFactura: EstadoFactura
- IX_Facturas_FechaFactura: FechaFactura
- IX_Facturas_IdAsientoContable: IdAsientoContable
- IX_Facturas_IdCliente: IdCliente
- IX_Facturas_IdContrato: IdContrato
- IX_Facturas_IdFacturaProgramada: IdFacturaProgramada
- IX_Facturas_IdLoteFacturaProgramada: IdLoteFacturaProgramada
- IX_Facturas_IdSerie: IdSerie
- IX_Facturas_LoteExportacionContable: LoteExportacionContable
- IX_Facturas_TipoFactura: TipoFactura
- PK_Facturas (clave primaria): IdFactura
- UX_Facturas_IdFacturaRectificada (único): IdFacturaRectificada
- UX_Facturas_ProgramacionFecha (único): IdFacturaProgramada, FechaProgramadaOrigen
- UX_Facturas_SociedadNumero (único): IdSociedadEjercicio, NumeroFactura

Relaciones:
- EstadosFacturaFacturas: (EstadoFactura) → EstadosFactura (IdEstado); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_Clientes: (IdCliente) → Clientes (IdCliente); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_Contratos: (IdContrato) → Contratos (IdContrato); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_LoteProgramado: (IdLoteFacturaProgramada) → LotesFacturasProgramadas (IdLoteFacturaProgramada); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_ProgramacionOrigen: (IdFacturaProgramada) → FacturasProgramadas (IdFacturaProgramada); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_Rectificada: (IdFacturaRectificada) → Facturas (IdFactura); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_Series: (IdSerie) → SeriesFacturacion (IdSerie); borrado NO ACTION, actualización NO ACTION
- FK_Facturas_TiposFactura: (TipoFactura) → TiposFactura (TipoFactura); borrado NO ACTION, actualización NO ACTION
- FK_LotesExportacionContable_Facturas: (LoteExportacionContable) → LotesExportacionContable (IdLote); borrado NO ACTION, actualización NO ACTION
- FK_SociedadesEjercicios_Facturas: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## FacturasProgramadas

- Dueño funcional: `facturacion/programadas`
- Definiciones reutilizables para generar facturas futuras de forma periodica o puntual.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdFacturaProgramada | COUNTER | no |  | Identificador autonumerico de la programacion. |
| IdSociedadEjercicio | LONG | no |  | Sociedad que emitira las facturas generadas. |
| IdContrato | LONG | sí |  | Contrato de origen, si la programacion procede de un contrato. |
| IdCliente | LONG | sí |  | Cliente destinatario de las facturas. |
| IdSerie | LONG | no |  | Serie que se aplicara al emitir cada factura. |
| TipoFactura | TEXT(2) | no | "F1" | Tipo fiscal de las facturas generadas, definido en TiposFactura. |
| FechaInicio | DATETIME | no |  | Fecha desde la que puede ejecutarse la programacion. |
| FechaFin | DATETIME | sí |  | Fecha final opcional; despues de ella no se generan nuevas facturas. |
| Periodicidad | TEXT(12) | no | "MENSUAL" | Frecuencia: MENSUAL, TRIMESTRAL, SEMESTRAL, ANUAL o PUNTUAL. |
| DiaEmision | BYTE | sí |  | Dia preferente del mes para generar la factura, de 1 a 31. |
| ProximaFechaEmision | DATETIME | no |  | Siguiente fecha programada pendiente de generar. |
| FechaUltimaGeneracion | DATETIME | sí |  | Fecha y hora de la ultima generacion completada. |
| FormaDePago | BYTE | sí |  | Forma de pago que se copiara a la factura. |
| DiaDePago | BYTE | sí |  | Dia de pago que se copiara a la factura, de 0 a 31. |
| TipoRetencion | SINGLE | sí |  | Porcentaje de retencion aplicable a la factura. |
| Descripcion | LONGTEXT | sí |  | Descripcion general que se copiara a la factura. |
| NotasInternas | LONGTEXT | sí |  | Observaciones internas de la programacion. |
| Activa | YESNO | no | True | Indica si la programacion se incluye en nuevos procesos. |
| FechaAlta | DATETIME | no | Now() | Fecha y hora de creacion de la programacion. |
| OperadorAlta | TEXT(30) | sí |  | Usuario u operador que creo la programacion. |
| Historial | LONGTEXT | sí |  | Registro acumulativo de cambios, ejecuciones y errores. |
| EnviarPorEmail | YESNO | no | False | Indica si, despuÃ©s de validar y generar el PDF, el proceso automÃ¡tico debe enviarlo al cliente. False significa que no se espera ningÃºn envÃ­o y no constituye una incidencia. |

Índices:
- FK_Clientes_FacturasProgramadas: IdCliente
- FK_FacturasProgramadas_Contratos: IdContrato
- FK_FacturasProgramadas_Series: IdSerie
- FK_FacturasProgramadas_TiposFactura: TipoFactura
- FK_SociedadesEjercicios_FacturasProgramadas: IdSociedadEjercicio
- IX_FacturasProgramadas_IdCliente: IdCliente
- IX_FacturasProgramadas_IdContrato: IdContrato
- IX_FacturasProgramadas_IdSociedad: IdSociedadEjercicio
- IX_FacturasProgramadas_ProximaFecha: Activa, ProximaFechaEmision
- PK_FacturasProgramadas (clave primaria): IdFacturaProgramada

Relaciones:
- FK_Clientes_FacturasProgramadas: (IdCliente) → Clientes (IdCliente); borrado NO ACTION, actualización NO ACTION
- FK_FacturasProgramadas_Contratos: (IdContrato) → Contratos (IdContrato); borrado NO ACTION, actualización NO ACTION
- FK_FacturasProgramadas_Series: (IdSerie) → SeriesFacturacion (IdSerie); borrado NO ACTION, actualización NO ACTION
- FK_FacturasProgramadas_TiposFactura: (TipoFactura) → TiposFactura (TipoFactura); borrado NO ACTION, actualización NO ACTION
- FK_SociedadesEjercicios_FacturasProgramadas: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## FacturasProgramadasBak

- Dueño funcional: `facturacion/programadas`
- Definiciones reutilizables para generar facturas futuras de forma periodica o puntual.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdFacturaProgramada | COUNTER | no |  | Identificador autonumerico de la programacion. |
| IdSociedad | LONG | no |  | Sociedad que emitira las facturas generadas. |
| IdContrato | LONG | sí |  | Contrato de origen, si la programacion procede de un contrato. |
| IdCliente | LONG | no |  | Cliente destinatario de las facturas. |
| IdSerie | LONG | no |  | Serie que se aplicara al emitir cada factura. |
| TipoFactura | TEXT(2) | no | "F1" | Tipo fiscal de las facturas generadas, definido en TiposFactura. |
| FechaInicio | DATETIME | no |  | Fecha desde la que puede ejecutarse la programacion. |
| FechaFin | DATETIME | sí |  | Fecha final opcional; despues de ella no se generan nuevas facturas. |
| Periodicidad | TEXT(12) | no | "MENSUAL" | Frecuencia: MENSUAL, TRIMESTRAL, SEMESTRAL, ANUAL o PUNTUAL. |
| DiaEmision | BYTE | sí |  | Dia preferente del mes para generar la factura, de 1 a 31. |
| ProximaFechaEmision | DATETIME | no |  | Siguiente fecha programada pendiente de generar. |
| FechaUltimaGeneracion | DATETIME | sí |  | Fecha y hora de la ultima generacion completada. |
| FormaDePago | BYTE | sí |  | Forma de pago que se copiara a la factura. |
| DiaDePago | BYTE | sí |  | Dia de pago que se copiara a la factura, de 0 a 31. |
| TipoRetencion | SINGLE | sí |  | Porcentaje de retencion aplicable a la factura. |
| Descripcion | LONGTEXT | sí |  | Descripcion general que se copiara a la factura. |
| NotasInternas | LONGTEXT | sí |  | Observaciones internas de la programacion. |
| Activa | YESNO | no | True | Indica si la programacion se incluye en nuevos procesos. |
| FechaAlta | DATETIME | no | Now() | Fecha y hora de creacion de la programacion. |
| OperadorAlta | TEXT(30) | sí |  | Usuario u operador que creo la programacion. |
| Historial | LONGTEXT | sí |  | Registro acumulativo de cambios, ejecuciones y errores. |

Índices:
- IX_FacturasProgramadas_IdCliente: IdCliente
- IX_FacturasProgramadas_IdContrato: IdContrato
- IX_FacturasProgramadas_IdSociedad: IdSociedad
- IX_FacturasProgramadas_ProximaFecha: Activa, ProximaFechaEmision
- PK_FacturasProgramadas (clave primaria): IdFacturaProgramada

## LineasContrato

- Dueño funcional: `facturacion/contratos`
- LÃ­neas periÃ³dicas o puntuales que un contrato genera en las facturas.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLineaContrato | COUNTER | no |  | Identificador autonumÃ©rico interno de la lÃ­nea de contrato. |
| IdContrato | LONG | no |  | Contrato al que pertenece la lÃ­nea. Relacionado con Contratos.IdContrato. |
| NumeroLinea | SHORT | no |  | Orden de la lÃ­nea dentro del contrato. Es Ãºnico para cada contrato. |
| IdConcepto | LONG | no |  | Concepto facturable utilizado para generar la lÃ­nea de factura. |
| Descripcion | LONGTEXT | sí |  | Texto extenso opcional que sustituye la descripciÃ³n predeterminada del concepto al generar la factura. |
| Importe | CURRENCY | no | 0 | Importe unitario que se aplicarÃ¡ al generar la lÃ­nea de factura. |
| Periodicidad | TEXT(10) | no | "MENSUAL" | Frecuencia de facturaciÃ³n: MENSUAL, TRIMESTRAL, ANUAL o PUNTUAL. |
| MesFacturacion | BYTE | sí |  | Mes de facturaciÃ³n (1 a 12). En periodicidad trimestral indica el primer mes del ciclo; en anual, el mes de emisiÃ³n. |
| FechaInicio | DATETIME | sí |  | Primera fecha desde la que puede generarse esta lÃ­nea. Si estÃ¡ vacÃ­a se utiliza la fecha de inicio del contrato. |
| FechaFin | DATETIME | sí |  | Ãšltima fecha hasta la que puede generarse esta lÃ­nea. Si estÃ¡ vacÃ­a se utiliza la fecha de fin del contrato. |
| Activo | YESNO | no | True | Indica si la lÃ­nea debe incluirse al generar nuevas facturas del contrato. |

Índices:
- FK_LineasContrato_Conceptos: IdConcepto
- FK_LineasContrato_Contratos: IdContrato
- IX_LineasContrato_IdConcepto: IdConcepto
- PK_LineasContrato (clave primaria): IdLineaContrato
- UX_LineasContrato_ContratoNumero (único): IdContrato, NumeroLinea

Relaciones:
- FK_LineasContrato_Conceptos: (IdConcepto) → Conceptos (IdConcepto); borrado NO ACTION, actualización NO ACTION
- FK_LineasContrato_Contratos: (IdContrato) → Contratos (IdContrato); borrado NO ACTION, actualización NO ACTION

## LineasFactura

- Dueño funcional: `facturacion/factura-venta`
- Líneas comerciales y desglose tributario de las facturas.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLineaFactura | COUNTER | no |  | Identificador interno autonumérico de la línea. Clave primaria. |
| IdFactura | LONG | no |  | Factura a la que pertenece la línea. Referencia a Facturas.IdFactura. |
| NumeroLinea | SHORT | no |  | Número de orden de la línea dentro de la factura. Debe ser mayor que cero y único en cada factura. |
| IdConcepto | LONG | sí |  | Concepto de facturación opcional. Referencia futura a un catálogo de conceptos como alquiler, IBI, comunidad o basuras. |
| Descripcion | LONGTEXT | no |  | Descripción visible para el cliente y congelada al emitir la factura. |
| NotasInternas | LONGTEXT | sí |  | Observaciones internas de la línea que no se muestran al cliente ni se envían a VeriFactu. |
| Fecha | DATETIME | sí |  | Fecha de ejecución del servicio de la línea; útil cuando una factura agrupa servicios o alquileres de fechas distintas. |
| Cantidad | DOUBLE | no | 1 | Número de unidades facturadas. Valor predeterminado: 1. |
| PrecioUnitario | CURRENCY | no | 0 | Precio por unidad antes de descuentos e impuestos. |
| DescuentoPorcentaje | DOUBLE | no | 0 | Porcentaje de descuento aplicado, entre 0 y 100. |
| DescuentoImporte | CURRENCY | no | 0 | Importe monetario del descuento aplicado a la línea. |
| BaseImponible | CURRENCY | no | 0 | Base imponible de la línea después de descuentos. |
| Impuesto | TEXT(2) | no | "01" | Código de impuesto AEAT de la lista L1. Valor habitual 01=IVA. |
| ClaveRegimen | TEXT(2) | no | "01" | Clave de régimen fiscal AEAT de las listas L8A/L8B. Valor habitual 01=Régimen general. |
| CalificacionOperacion | TEXT(2) | no | "S1" | Calificación AEAT: S1=Sujeta y no exenta sin inversión del sujeto pasivo; S2=Sujeta y no exenta con inversión; N1=No sujeta por artículos 7, 14 u otros; N2=No sujeta por reglas de localización. |
| CausaExencion | TEXT(2) | sí |  | Causa de exención AEAT cuando proceda: E1, E2, E3, E4, E5 o E6; nulo si la operación no está exenta. |
| TipoIVA | DOUBLE | no | 21 | Porcentaje de IVA aplicado. Valor habitual: 21. |
| CuotaIVA | CURRENCY | no | 0 | Importe de IVA repercutido para la línea. |
| TipoRecargoEquivalencia | DOUBLE | no | 0 | Porcentaje de recargo de equivalencia; 0 cuando no se aplica. |
| CuotaRecargoEquivalencia | CURRENCY | no | 0 | Importe del recargo de equivalencia calculado para la línea. |
| EsSuplido | YESNO | no | False | Indica que la línea corresponde a un suplido y requiere tratamiento separado de la base imponible ordinaria. |
| ImporteTotal | CURRENCY | no | 0 | Importe total congelado de la línea: base imponible más IVA y, en su caso, recargo. |
| Nombre | TEXT(255) | sí |  | DescripciÃ³n abreviada y visible que encabeza la lÃ­nea de factura; se copia del concepto y queda congelada. |
| CuentaContable | TEXT(7) | sí |  | Cuenta contable de venta copiada del concepto y congelada para la exportaciÃ³n contable. |
| IdDepartamento | LONG | sí |  | Departamento analÃ­tico copiado del concepto y congelado para la exportaciÃ³n contable. |
| IdCentroCoste | LONG | sí |  | Centro de coste copiado del concepto y congelado para la exportaciÃ³n contable. |

Índices:
- FK_Facturas_LineasFactura: IdFactura
- FK_LineasFactura_Conceptos: IdConcepto
- IX_LineasFactura_IdConcepto: IdConcepto
- IX_LineasFactura_IdFactura: IdFactura
- PK_LineasFactura (clave primaria): IdLineaFactura
- UX_LineasFactura_FacturaNumero (único): IdFactura, NumeroLinea

Relaciones:
- FK_Facturas_LineasFactura: (IdFactura) → Facturas (IdFactura); borrado NO ACTION, actualización NO ACTION
- FK_LineasFactura_Conceptos: (IdConcepto) → Conceptos (IdConcepto); borrado NO ACTION, actualización NO ACTION

## LineasFacturasProgramadas

- Dueño funcional: `facturacion/programadas`
- Lineas que se copiaran a cada factura creada desde una programacion.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLineaFacturaProgramada | COUNTER | no |  | Identificador autonumerico de la linea programada. |
| IdFacturaProgramada | LONG | no |  | Programacion a la que pertenece la linea. |
| NumeroLinea | SHORT | no |  | Orden de la linea dentro de la programacion. |
| IdConcepto | LONG | sí |  | Concepto de facturacion de origen. |
| Nombre | TEXT(255) | sí |  | Descripcion abreviada que aparecera como titulo de la linea. |
| Descripcion | LONGTEXT | no |  | Descripcion detallada que se copiara a la factura. |
| NotasInternas | LONGTEXT | sí |  | Observaciones no visibles para el cliente. |
| Cantidad | DOUBLE | no | 1 | Numero de unidades facturadas. |
| PrecioUnitario | CURRENCY | no |  | Precio por unidad antes de descuentos e impuestos. |
| DescuentoPorcentaje | DOUBLE | no | 0 | Porcentaje de descuento aplicado. |
| DescuentoImporte | CURRENCY | no | 0 | Importe fijo de descuento aplicado. |
| Impuesto | TEXT(2) | no | "01" | Codigo de impuesto utilizado por VeriFactu. |
| ClaveRegimen | TEXT(2) | no | "01" | Clave del regimen fiscal de la operacion. |
| CalificacionOperacion | TEXT(2) | no |  | Calificacion fiscal de la operacion. |
| CausaExencion | TEXT(2) | sí |  | Causa de exencion cuando la operacion esta exenta. |
| TipoIVA | DOUBLE | no | 21 | Porcentaje de IVA aplicable. |
| TipoRecargoEquivalencia | DOUBLE | no | 0 | Porcentaje de recargo de equivalencia aplicable. |
| EsSuplido | YESNO | no | False | Indica si la linea corresponde a un suplido. |
| CuentaContable | TEXT(7) | sí |  | Cuenta de ventas que se copiara a la linea de factura. |
| IdDepartamento | LONG | sí |  | Departamento analitico asociado. |
| IdCentroCoste | LONG | sí |  | Centro de coste analitico asociado. |
| Activa | YESNO | no | True | Indica si la linea se copiara en nuevas facturas. |

Índices:
- FK_LineasFacturasProgramadas_Cabecera: IdFacturaProgramada
- FK_LineasFacturasProgramadas_Conceptos: IdConcepto
- IX_LineasFacturasProgramadas_Concepto: IdConcepto
- PK_LineasFacturasProgramadas (clave primaria): IdLineaFacturaProgramada
- UX_LineasFacturasProgramadas_Numero (único): IdFacturaProgramada, NumeroLinea

Relaciones:
- FK_LineasFacturasProgramadas_Cabecera: (IdFacturaProgramada) → FacturasProgramadas (IdFacturaProgramada); borrado NO ACTION, actualización NO ACTION
- FK_LineasFacturasProgramadas_Conceptos: (IdConcepto) → Conceptos (IdConcepto); borrado NO ACTION, actualización NO ACTION

## LotesExportacionContable

- Dueño funcional: `facturacion/factura-venta`
- Lotes de exportación masiva de facturas a contabilidad.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLote | COUNTER | no |  | Identificador autonumérico del lote de exportación contable. Clave primaria. |
| FechaHora | DATETIME | no | Now() | Fecha y hora en la que se creó o ejecutó el lote. Valor predeterminado: momento actual. |
| Operador | TEXT(30) | no |  | Usuario u operador que ejecutó la exportación contable, hasta 30 caracteres. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y ejercicio contable a los que pertenece el lote. Un lote no puede mezclar sociedades. |

Índices:
- FK_SociedadesEjercicios_LotesExportacionContable: IdSociedadEjercicio
- IX_LotesExportacionContable_FechaHora: FechaHora
- IX_LotesExportacionContable_IdSociedad: IdSociedadEjercicio
- PK_LotesExportacionContable (clave primaria): IdLote

Relaciones:
- FK_SociedadesEjercicios_LotesExportacionContable: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## LotesFacturasProgramadas

- Dueño funcional: `facturacion/programadas`
- Ejecuciones del proceso masivo de generacion de facturas programadas.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLoteFacturaProgramada | COUNTER | no |  | Identificador autonumerico del lote de generacion. |
| IdSociedadEjercicio | LONG | no |  | Sociedad cuyas programaciones fueron procesadas. |
| FechaHoraInicio | DATETIME | no | Now() | Fecha y hora de inicio del proceso. |
| FechaHoraFin | DATETIME | sí |  | Fecha y hora de finalizacion del proceso. |
| FechaGeneracionHasta | DATETIME | no |  | Fecha limite usada para seleccionar programaciones vencidas. |
| Estado | TEXT(12) | no | "PROCESANDO" | Estado del lote: PROCESANDO, COMPLETADO, ERROR o ANULADO. |
| NumeroProgramaciones | LONG | no | 0 | Numero de programaciones examinadas por el proceso. |
| NumeroFacturasGeneradas | LONG | no | 0 | Numero de facturas creadas correctamente. |
| Operador | TEXT(30) | sí |  | Usuario u operador que inicio el proceso. |
| DescripcionError | LONGTEXT | sí |  | Detalle del error general del lote. |
| Historial | LONGTEXT | sí |  | Registro acumulativo de operaciones realizadas sobre el lote. |

Índices:
- FK_SociedadesEjercicios_LotesFacturasProgramadas: IdSociedadEjercicio
- IX_LotesFacturasProgramadas_Sociedad: IdSociedadEjercicio
- PK_LotesFacturasProgramadas (clave primaria): IdLoteFacturaProgramada

Relaciones:
- FK_SociedadesEjercicios_LotesFacturasProgramadas: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## SeriesFacturacion

- Dueño funcional: `facturacion/factura-venta`
- Series y rangos de numeración por sociedad y ejercicio.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdSerie | COUNTER | no |  | Identificador autonumérico de la serie de facturación. Clave primaria. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y ejercicio que utiliza la serie. Referencia futura a Sociedades.IdSociedad. |
| Descripcion | TEXT(100) | no |  | Descripción funcional de la serie de facturación. |
| RangoDesde | LONG | no |  | Primer número correlativo permitido para la serie. |
| RangoHasta | LONG | no |  | Último número correlativo permitido para la serie. |
| Prefijo | TEXT(20) | no |  | Texto antepuesto al número correlativo, por ejemplo R2026. |
| UltimoNumero | LONG | sí |  | Último correlativo emitido. Nulo antes de emitir la primera factura; se actualiza transaccionalmente. |
| EsRectificativa | YESNO | no | False | Indica si la serie se utiliza exclusivamente para numerar facturas rectificativas. Solo debe existir una por sociedad. |
| EsPredeterminada | YESNO | no | 0 | Serie normal seleccionada automÃ¡ticamente al crear una factura manual. Debe existir una sola por sociedad. |

Índices:
- FK_SociedadesEjercicios_SeriesFacturacion: IdSociedadEjercicio
- IX_SeriesFacturacion_IdSociedad: IdSociedadEjercicio
- PK_SeriesFacturacion (clave primaria): IdSerie
- UX_SeriesFacturacion_SociedadPrefijo (único): IdSociedadEjercicio, Prefijo

Relaciones:
- FK_SociedadesEjercicios_SeriesFacturacion: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## TiposConcepto

- Dueño funcional: `facturacion/conceptos`
- ClasificaciÃ³n funcional de los conceptos facturables.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoConcepto | LONG | no |  | Identificador manual del tipo de concepto. |
| Descripcion | TEXT(80) | no |  | Nombre descriptivo del tipo de concepto. |
| Activo | YESNO | no | True | Indica si el tipo puede seleccionarse en nuevas operaciones. |
| Notas | LONGTEXT | sí |  | Observaciones internas sobre el tipo de concepto. |

Índices:
- IX_TiposConcepto_Descripcion: Descripcion
- PK_TiposConcepto (clave primaria): IdTipoConcepto

## TiposDeIVA

- Dueño funcional: `facturacion/factura-venta`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| Id | COUNTER | no |  | Identificador interno autonumérico del tipo de IVA. |
| Valor | DOUBLE | no |  | Porcentaje de IVA que se aplicará; por ejemplo, 21 representa el 21 %. |
| Descripcion | TEXT(100) | no |  | Nombre comprensible del tipo de IVA que se mostrará al operador. |
| Activo | YESNO | no |  | Indica si el tipo de IVA puede seleccionarse en conceptos y líneas de factura. |

Índices:
- PK_TiposDeIVA (clave primaria): Id
- UX_TiposDeIVA_Valor (único): Valor

## TiposFactura

- Dueño funcional: `facturacion/factura-venta`
- Catálogo de tipos de factura admitidos por la AEAT.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| TipoFactura | TEXT(2) | no |  | Código oficial AEAT: F1, F2, F3, R1, R2, R3, R4 o R5. Clave primaria. |
| Descripcion | TEXT(255) | no |  | Descripción fiscal del tipo de factura. |
| EsRectificativa | YESNO | no | False | Indica si el código corresponde a una factura rectificativa: Sí para R1-R5; No para F1-F3. |

Índices:
- PK_TiposFactura (clave primaria): TipoFactura

## TiposOperacion

- Dueño funcional: `facturacion/factura-venta`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTipoOperacion | COUNTER | no |  |  |
| Codigo | TEXT(30) | no |  |  |
| Nombre | TEXT(150) | no |  |  |
| Libro | TEXT(20) | no |  |  |
| CodigoContaSol | LONG | sí |  |  |
| EstaActivo | YESNO | no |  |  |

Índices:
- PK_TiposOperacion (clave primaria): IdTipoOperacion
- UX_TiposOperacion_Codigo (único): Codigo

## VeriFactuConfiguracion

- Dueño funcional: `facturacion/verifactu`
- ConfiguraciÃ³n de la integraciÃ³n VeriFactu para cada sociedad y ejercicio.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdVeriFactuConfiguracion | COUNTER | no |  | Identificador autonumÃ©rico interno de la configuraciÃ³n VeriFactu. |
| Activo | YESNO | no | False | Indica si la emisiÃ³n y remisiÃ³n VeriFactu estÃ¡ habilitada para esta sociedad. |
| Entorno | TEXT(10) | sí | "PRUEBAS" | Entorno de comunicaciÃ³n: PRUEBAS o PRODUCCION. |
| CertificateThumbprint | TEXT(64) | sí |  | Huella digital del certificado instalado en el almacÃ©n de certificados de Windows. MÃ©todo recomendado. |
| CertificateSerial | TEXT(100) | sí |  | NÃºmero de serie del certificado instalado; alternativa a CertificateThumbprint. |
| CertificatePath | TEXT(255) | sí |  | Ruta a un certificado PFX; alternativa al certificado instalado en Windows. |
| CertificatePasswordProtegida | LONGTEXT | sí |  | ContraseÃ±a cifrada del certificado PFX. Nunca debe almacenarse aquÃ­ en texto claro. |
| IDVersion | TEXT(3) | sí | "1.0" | VersiÃ³n del esquema VeriFactu utilizada para generar los registros. Actualmente 1.0. |
| VeriFactuEndPointPrefix | TEXT(255) | sí | "https://prewww1.aeat.es/wlpl/TIKE-CONT/ws/SistemaFacturacion/VerifactuSOAP" | Endpoint AEAT para enviar registros de alta y anulaciÃ³n. Debe corresponder con el entorno seleccionado. |
| VeriFactuEndPointValidatePrefix | TEXT(255) | sí | "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR" | Endpoint AEAT para validar el cÃ³digo QR. Debe corresponder con el entorno seleccionado. |
| VeriFactuHashAlgorithm | TEXT(2) | sí | "01" | Tipo de huella: 01=SHA-256, Ãºnico valor actualmente admitido. |
| VeriFactuHashInputEncoding | TEXT(20) | sí | "UTF-8" | CodificaciÃ³n del texto de entrada empleado para calcular la huella. Valor habitual: UTF-8. |
| SkipNifAeatValidation | YESNO | no | False | SÃ­=omite la validaciÃ³n en lÃ­nea del NIF con la AEAT; No=realiza la validaciÃ³n. |
| SkipViesVatNumberValidation | YESNO | no | False | SÃ­=omite la validaciÃ³n VIES de NIF-IVA intracomunitarios; No=realiza la validaciÃ³n. |
| LoggingEnabled | YESNO | no | True | Indica si la librerÃ­a debe generar su registro de mensajes. |
| FechaModificacion | DATETIME | sí | Now() | Fecha y hora de la Ãºltima modificaciÃ³n de esta configuraciÃ³n. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizÃ³ la Ãºltima modificaciÃ³n. |
| ProductorNIF | TEXT(25) | sí |  | NIF de la persona o entidad productora del sistema informÃ¡tico. |
| ProductorNombre | TEXT(200) | sí |  | Nombre o razÃ³n social de la persona o entidad productora del sistema informÃ¡tico. |
| NombreSistemaInformatico | TEXT(30) | sí |  | Nombre declarado del sistema informÃ¡tico de facturaciÃ³n. Valor habitual: ProConta. |
| IdSistemaInformatico | TEXT(2) | sí |  | Identificador de dos caracteres declarado para el sistema informÃ¡tico. |
| VersionSistemaInformatico | TEXT(50) | sí |  | VersiÃ³n instalada del sistema informÃ¡tico que genera el registro. |
| NumeroInstalacion | TEXT(100) | sí |  | Identificador estable y Ãºnico de esta instalaciÃ³n de ProConta. |
| TipoUsoPosibleSoloVerifactu | TEXT(1) | sí |  | S/N. Indica si el sistema puede funcionar exclusivamente como VeriFactu. |
| TipoUsoPosibleMultiOT | TEXT(1) | sí |  | S/N. Indica si el sistema admite varios obligados tributarios. |
| IndicadorMultiplesOT | TEXT(1) | sí |  | S/N. Indica si esta instalaciÃ³n gestiona varios obligados tributarios. |
| IdSociedad | TEXT(3) | sí |  |  |

Índices:
- FK_Sociedades_VeriFactuConfiguracion: IdSociedad
- PK_VeriFactuConfiguracion (clave primaria): IdVeriFactuConfiguracion
- UX_VeriFactuConfiguracion_IdSociedad (único): IdSociedad

Relaciones:
- FK_Sociedades_VeriFactuConfiguracion: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## VeriFactuEnvios

- Dueño funcional: `facturacion/verifactu`
- Historial inmutable de registros e intentos de envío a VeriFactu.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEnvio | COUNTER | no |  | Identificador autonumérico del intento de comunicación con VeriFactu. Clave primaria. |
| IdFactura | LONG | no |  | Factura asociada al intento. Referencia a Facturas.IdFactura. |
| TipoRegistro | TEXT(10) | no |  | Operación realizada: ALTA=registro de alta de factura; ANULACION=registro de anulación. |
| NumeroIntento | SHORT | no | 1 | Número correlativo del intento para la factura y tipo de registro, comenzando en 1. |
| Entorno | TEXT(10) | no | "PRUEBAS" | Entorno de destino: PRUEBAS o PRODUCCION. |
| EstadoEnvio | TEXT(30) | no | "PENDIENTE" | Resultado tÃ©cnico del intento: ACEPTADO, ACEPTADO_DUPLICADO, RECHAZADO, ERROR_PREVIO_ENVIO o COMUNICACION_DUDOSA. Los errores legados pueden conservar el valor ERROR. |
| FechaHoraGeneracion | DATETIME | no | Now() | Fecha y hora local en la que se generó el registro o XML. |
| FechaHoraHusoGenRegistro | TEXT(35) | sí |  | Valor ISO 8601 exacto de FechaHoraHusoGenRegistro, incluida la zona horaria, utilizado en el XML y en la huella. |
| FechaHoraEnvio | DATETIME | sí |  | Fecha y hora en la que se inició la comunicación con la AEAT. |
| Endpoint | TEXT(255) | sí |  | Dirección del servicio AEAT utilizada para el envío. |
| XMLSolicitud | LONGTEXT | sí |  | XML exacto generado y enviado. No debe modificarse después de la emisión. |
| TipoHuella | TEXT(2) | no | "01" | Código AEAT del algoritmo de huella. 01=SHA-256. |
| Huella | TEXT(64) | sí |  | Huella SHA-256 del registro, representada mediante 64 caracteres hexadecimales. |
| HuellaAnterior | TEXT(64) | sí |  | Huella del registro anterior en la cadena del obligado emisor; nulo en el primer registro. |
| URLValidacion | LONGTEXT | sí |  | URL incluida en el código QR para cotejo o remisión de la factura. |
| IdLoteEnvio | LONG | sí |  | Lote o peticiÃ³n AEAT que contiene este intento de envÃ­o. Relacionado con VeriFactuLotesEnvio.IdLoteEnvio. |

Índices:
- FK_Facturas_VeriFactuEnvios: IdFactura
- FK_VeriFactuEnvios_Lotes: IdLoteEnvio
- IX_VeriFactuEnvios_Estado: EstadoEnvio
- IX_VeriFactuEnvios_FechaEnvio: FechaHoraEnvio
- IX_VeriFactuEnvios_IdFactura: IdFactura
- IX_VeriFactuEnvios_IdLoteEnvio: IdLoteEnvio
- PK_VeriFactuEnvios (clave primaria): IdEnvio
- UX_VeriFactuEnvios_Intento (único): IdFactura, TipoRegistro, NumeroIntento

Relaciones:
- FK_Facturas_VeriFactuEnvios: (IdFactura) → Facturas (IdFactura); borrado NO ACTION, actualización NO ACTION
- FK_VeriFactuEnvios_Lotes: (IdLoteEnvio) → VeriFactuLotesEnvio (IdLoteEnvio); borrado NO ACTION, actualización NO ACTION

## VeriFactuLotesEnvio

- Dueño funcional: `facturacion/verifactu`
- Peticiones o lotes remitidos a la AEAT, cada uno con uno o varios intentos de envÃ­o de facturas.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdLoteEnvio | COUNTER | no |  | Identificador autonumÃ©rico de la peticiÃ³n o lote VeriFactu. |
| FechaHoraGeneracion | DATETIME | sí | Now() | Fecha y hora en que se construyÃ³ el XML completo del lote. |
| FechaHoraEnvio | DATETIME | sí |  | Fecha y hora en que se remitiÃ³ el lote a la AEAT. |
| Entorno | TEXT(10) | no | "PRUEBAS" | Entorno al que se dirige el lote: PRUEBAS o PRODUCCION. |
| Estado | TEXT(15) | no | "PENDIENTE" | Estado del lote: PENDIENTE, GENERADO, ENVIANDO, RESPONDIDO o ERROR. |
| XMLSolicitud | LONGTEXT | sí |  | XML completo e inmutable enviado a la AEAT para este lote. |
| RespuestaCompleta | LONGTEXT | sí |  | Respuesta completa recibida de la AEAT para el lote. |
| Operador | TEXT(30) | sí |  | Usuario u operador que iniciÃ³ el envÃ­o. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y obligado tributario al que pertenece la peticiÃ³n AEAT. Un lote no puede mezclar emisores. |

Índices:
- FK_SociedadesEjercicios_VeriFactuLotesEnvio: IdSociedadEjercicio
- IX_VeriFactuLotesEnvio_Estado: Estado
- IX_VeriFactuLotesEnvio_FechaHora: FechaHoraEnvio
- IX_VeriFactuLotesEnvio_IdSociedad: IdSociedadEjercicio
- PK_VeriFactuLotesEnvio (clave primaria): IdLoteEnvio

Relaciones:
- FK_SociedadesEjercicios_VeriFactuLotesEnvio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## VeriFactuRespuestas

- Dueño funcional: `facturacion/verifactu`
- Respuesta técnica y fiscal asociada a un intento de envío VeriFactu.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRespuesta | COUNTER | no |  | Identificador autonumérico de la respuesta procesada. Clave primaria. |
| IdEnvio | LONG | no |  | Intento al que corresponde la respuesta. Referencia única a VeriFactuEnvios.IdEnvio. |
| FechaHoraRespuesta | DATETIME | no | Now() | Fecha y hora local en que se recibió o registró la respuesta. |
| ResultCode | TEXT(50) | sí |  | Código devuelto por la clase COM VeriFactu. 0 indica que la llamada no produjo un error técnico de la librería. |
| ResultMessage | LONGTEXT | sí |  | Mensaje devuelto por la clase COM VeriFactu. |
| EstadoRegistro | TEXT(30) | sí |  | Estado AEAT: Correcto, AceptadoConErrores o Incorrecto; nulo si no hubo respuesta fiscal interpretable. |
| CSV | TEXT(100) | sí |  | Código Seguro de Verificación devuelto por la AEAT cuando proceda. |
| CodigoError | TEXT(50) | sí |  | Código de validación o error comunicado por la AEAT. |
| DescripcionError | LONGTEXT | sí |  | Descripción detallada del error comunicado por la AEAT. |
| EsDuplicado | YESNO | no | False | Indica que la AEAT rechazÃ³ el reenvÃ­o como duplicado y proporcionÃ³ el estado del registro almacenado previamente. |
| EstadoRegistroDuplicado | TEXT(30) | sí |  | Estado del registro previamente almacenado: Correcta, AceptadaConErrores o Anulada. |
| TiempoEspera | LONG | no | 0 | Tiempo de espera indicado por la AEAT antes de realizar el siguiente envío, expresado en segundos. |
| RespuestaCompleta | LONGTEXT | sí |  | Contenido completo de la respuesta XML/SOAP devuelta por la AEAT o por la librería. |

Índices:
- FK_VeriFactuEnvios_Respuestas: IdEnvio
- IX_VeriFactuRespuestas_Estado: EstadoRegistro
- PK_VeriFactuRespuestas (clave primaria): IdRespuesta
- UX_VeriFactuRespuestas_IdEnvio (único): IdEnvio

Relaciones:
- FK_VeriFactuEnvios_Respuestas: (IdEnvio) → VeriFactuEnvios (IdEnvio); borrado NO ACTION, actualización NO ACTION

