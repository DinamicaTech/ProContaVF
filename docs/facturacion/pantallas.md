---
title: Pantallas de facturación
status: draft
depends_on: [facturacion/factura-venta, facturacion/programadas, plataforma/contenedor]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Listado filtrable de facturas y programaciones con selección explícita, ficha de factura y acciones contextuales.
**Aceptación:** comparación con la maqueta; selección, fila activa y universo filtrado no se confunden.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 11:38 · El prototipo de referencia es la maqueta completa `sources/maquetas/Facturas/`, que se integra en el lienzo del contenedor y sustituye a su listado simple de facturas.
- 2026-10-29 11:38 · Programadas y emitidas siguen en la misma pantalla. El diseño se hace para 2560×1440; a resoluciones menores basta con que sea funcional. El separador entre las secciones de programadas y emitidas se puede arrastrar para repartir la altura.
- 2026-10-29 12:05 · La maqueta de Facturas se valida primero en su página propia y, una vez validada, se reescribe en el formato del lienzo dentro del contenedor, porque el lienzo no admite incrustar otra página; así cada ronda de cambios se hace una sola vez.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-10: listado, selección, proforma, contadores
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/WpfFacturas/docs/WPF_FACTURAS.md: pantallas anteriores
- 2026-10-28 12:28 · From sources/maquetas/Facturas/: maqueta de facturas
- 2026-10-28 12:28 · draft: Construir el listado y la ficha de facturas según la maqueta.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de Facturas emitidas, listado y detalle de factura (sources/maquetas/Facturas/ y lienzo del contenedor), antes de construir su UI WPF.
- 2026-10-29 11:38 · Sí, integrada en el contenedor
- 2026-10-29 11:38 · La resolución de las pantallas (al menos las nuestras) que usarán ProContaVF es de 2560 x 1440, no vamos a condicionar el diseño de pantalla por resoluciones inferiores mientras sigan siendo funcionales. Lo único que añadiría es poder modificar el tamaño de la sección de programadas y emitidas para poder ajustar medidas para resoluciones más bajas.
