# Motor «Modelos contables»

## Límites del producto

El motor separa tres resultados distintos:

1. **Cálculo/borrador contable:** resultado reproducible, con casillas y origen de cada importe.
2. **Representación FRX:** documento visual para revisión humana, siempre marcado `BORRADOR - NO PRESENTADO`.
3. **Exportación/presentación AEAT:** no implementada. Un PDF o FRX no es una declaración presentable ni sustituye el diseño de registro oficial.

No se implementan certificado, Cl@ve, NRC, firma, envío ni consulta a la AEAT.

La AEAT publica diseños de registro para el intercambio de datos y genera la copia PDF desde sus propios formularios después de cargar, validar o presentar una declaración. No se ha localizado una plantilla PDF maestra, estable y reutilizable por aplicaciones externas. Por ello, las plantillas FRX son hojas de trabajo propias de ProConta: reproducen identificación visual, devengo, casillas, importes y comprobaciones, pero nunca incorporan logotipos, CSV, justificantes ni apariencia de documento ya presentado.

## Inventario y soporte (definiciones 2025)

Fecha de consulta oficial: **24-08-2026**. Antes de calcular otro ejercicio debe crearse y verificarse una definición nueva.

| Modelo | Periodicidad habitual | Fuente ProConta necesaria | Estado del gestor | Fuente oficial y norma/documento |
|---|---|---|---|---|
| 111 | mensual/trimestral | Nóminas y perceptores; facturas profesionales clasificadas | No soportado: falta libro de perceptores con clave/subclave | [Instrucciones AEAT 111](https://sede.agenciatributaria.gob.es/Sede/todas-gestiones/impuestos-tasas/pagos-cuenta/modelo-111-reten_____moniales-imputaciones-renta-autoliquidacion_/instrucciones.html), Orden EHA/3127/2009 y diseño v1.8 |
| 115 | mensual/trimestral | Pagos de alquiler, arrendador e inmueble | No soportado: `Contratos` contiene alquileres emitidos, no rentas satisfechas | [Diseños 100-199 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-100-199.html), Orden EHA/3435/2007 |
| 303 | mensual/trimestral | `IVARepercutido`, `DesglosesIVARepercutido`, `IVASoportado`, `DesglosesIVASoportado`, `PeriodosIVA` | Vertical de régimen general; parcial hasta cubrir operaciones especiales, liquidaciones previas y conciliación. La hoja de trabajo 2026 presenta las casillas verificadas 01/03, 04/06, 07/09, 45 y 46 cuando corresponden. | [Instrucciones 303 de 2026](https://sede.agenciatributaria.gob.es/Sede/todas-gestiones/impuestos-tasas/iva/modelo-303-iva-autoliquidacion_/instrucciones-2026.html), Orden EHA/3786/2008 y modificaciones |
| 123 | mensual/trimestral | Perceptores y rendimientos de capital mobiliario | No soportado: no existe libro específico | [Diseños 100-199 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-100-199.html), diseño vigente por ejercicio |
| 202 | abril/octubre/diciembre según período | Último 200, modalidad, cifra de negocios, base y pagos previos | No soportado: faltan ajustes y antecedentes fiscales | [Diseños 200-299 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-200-299.html), instrucciones 2025 y siguientes |
| 190 | anual | Perceptores 111 con detalle anual | No soportado: falta libro de perceptores | [Diseños 100-199 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-100-199.html), diseño vigente por ejercicio |
| 180 | anual | Arrendadores, inmuebles y referencias catastrales | No soportado: faltan datos del inmueble pagado | [Diseños 100-199 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-100-199.html), diseño vigente por ejercicio |
| 390 | anual | Libros IVA completos y conciliación de todos los 303 | Agregación de régimen general; parcial por las mismas limitaciones del 303 | [Diseños 300-399 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-300-399.html), diseño vigente por ejercicio |
| 193 | anual | Perceptores y rendimientos de capital mobiliario | No soportado: falta libro de perceptores | [Diseños 100-199 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-100-199.html), diseño vigente por ejercicio |
| 347 | anual, desglose trimestral | Operaciones por tercero, metálico, inmuebles y exclusiones | No soportado: los indicadores IVA no cubren todas las claves ni exclusiones | [Diseños 300-399 AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/disenos-registro/modelos-300-399.html), Orden HAP/1732/2014 y diseño vigente |
| 200 | anual por período impositivo | Cierre, estados, caracteres, ajustes, deducciones y pagos | No soportado: un balance contable no basta | [Modelo 200, documentos 2025](https://sede.agenciatributaria.gob.es/Sede/procedimientoini/GE04.shtml), Orden anual y Manual práctico de Sociedades 2025 |

Los plazos concretos se consultan cada año en el [calendario oficial de la AEAT](https://sede.agenciatributaria.gob.es/Sede/ayuda/calendario-contribuyente.html); no se codifican fechas móviles como reglas permanentes.

## Contrato para Motor Reporting

Reporting consume `ResultadoModeloContable`, nunca gestores, conexiones Access ni objetos FastReport. `AdaptadorReportingModelos.Adaptar` entrega:

- parámetros `Modelo`, `NombreModelo`, `Ejercicio`, `Periodo`, `MarcaBorrador` y `EsCompleto`;
- tabla `Casillas` con `Codigo`, `Descripcion`, `Estado` e `Importe`;
- la trazabilidad permanece en `ResultadoModeloContable.Casillas().Origenes` para una futura banda de auditoría.

Reporting seleccionará la plantilla por `Plantillas/ModelosContables/{ejercicio}/Modelo{n}.frx`. La ausencia de plantilla o una definición normativa distinta debe detener el render, no reutilizar silenciosamente otro ejercicio.

## Protocolo anual

1. Confirmar calendario, instrucciones, formulario y diseño de registro en AEAT/BOE.
2. Registrar URL, fecha, ejercicio, versión y orden normativa en `VersionNormativaModelo`.
3. Comparar casillas, claves, validaciones, periodicidad y supuestos de aplicación.
4. Crear definición y FRX del nuevo ejercicio; nunca sobrescribir la versión anterior.
5. Añadir pruebas sintéticas de cada cambio y casos de datos ausentes.
6. Renderizar todas las páginas, revisar paginación y mantener la marca de borrador.
7. Habilitar `EsCompleto` solo cuando todas las fuentes exigibles estén conciliadas. `EsExportableAeat` permanece `False` hasta que exista un exportador oficial independiente y validado.

## Referencia orientativa del usuario

El archivo `D:\descargas\modelos_fiscales_pyme.pdf` no estaba disponible en el entorno de ejecución el 24-08-2026. El catálogo indicado en el encargo se ha usado como inventario; ninguna definición se ha derivado de ese PDF.
