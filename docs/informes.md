---
title: Informes
depends_on: [contabilidad/mayor, contabilidad/asientos, contabilidad/modelos-tributarios, correo/recepcion/comandos-informe]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-12 15:19 · Diseñar el módulo de informes | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GP8fCDxMMPiRJ86PrDVije2
---
## Summary
Informes, gráficos y análisis: motor de informes con plantillas, motor de gráficos, informes en tablas dinámicas Excel y ratios. No calculan reglas de negocio ajenas.
Diseño acordado el 2026-11-12: dos familias de informe. Los documentos con forma fija (factura, proforma, modelos tributarios) llevan una plantilla .frx y salen en PDF; las extracciones de datos (diario, mayor, sumas y saldos, balances, ventas y compras por tercero y mes, libros registro de IVA) siguen un contrato común, en el que el nodo dueño del dato aporta solo sus filas e informes pone las salidas (Excel de datos, Excel con tabla dinámica y PDF de listado) y el diálogo de parámetros del menú Informes. Todos los PDF salen con texto real por un único camino: .frx › HTML con FastReport de código abierto › PDF con WebView2 (informes/reporting).

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-12 15:19 · Dos familias de informe. (1) Documentos con forma fija: factura, proforma y modelos tributarios; una plantilla .frx recibe un modelo ya calculado y la salida es PDF. (2) Extracciones de datos: diario, mayor, sumas y saldos, balances, ventas y compras por tercero y mes y libros registro de IVA; filas tipadas con parámetros comunes (sociedad, ejercicio, fechas, cuentas) que pueden salir en Excel de datos, en Excel con tabla dinámica o en PDF de listado.
- 2026-11-12 15:19 · Contrato común de las extracciones: el nodo dueño del dato aporta solo su proveedor de filas (con sus columnas tipadas y sus parámetros) y no conoce formatos; informes pone las salidas reutilizables y un diálogo de parámetros común en el menú Informes. Un informe nuevo es su consulta más su definición. El contrato, el catálogo y el PDF son de informes/reporting; las salidas Excel, de informes/excel.
- 2026-11-12 15:19 · Un solo camino para todos los PDF, con texto real y sin licencias de pago: plantilla .frx › HTML con FastReport de código abierto › PDF con WebView2 (detalle y prueba en informes/reporting 2026-11-12 15:19). Se descartan la licencia comercial de FastReport (es anual), MigraDoc y QuestPDF (el diseño se programa en C#) y un conversor propio de .frx a HTML (sería escribir otro motor de informes).
- 2026-11-12 15:19 · Imprimir lo que hay en pantalla no es un informe del catálogo: el Mayor sigue imprimiendo su vista con FlowDocument (contabilidad/mayor 2026-11-11 18:30). Lo que se pide sin pantalla, como un mayor por un comando de correo, va por el catálogo (borrador correo/recepcion/comandos-informe).
- 2026-11-12 15:19 · Orden de construcción: diario y mayor en Excel (datos y tabla dinámica), sumas y saldos, ventas y compras por tercero y mes, libros registro de IVA; el motor PDF nuevo (factura primero) y los PDF de listado van en paralelo en informes/reporting.

## Requirements
- 2026-11-12 11:24 · Vamos a debatir el desarrollo de este módulo de reporting.
- 2026-11-12 11:24 · Por un lado, tenemos impresión de facturas en PDF y modelos contables (también en PDF) y luego extracción de datos como un mayor, diario, balances, etc. que pueden funcionar en PDF o en Excel/Excel+Tabla dinámica.
- 2026-11-12 13:11 · Ok con FlowDocument, lo único que no tengo claro si nos sirve para pedir un mayor mediante un comando de email
- 2026-11-12 15:19 · Ok, adelante
