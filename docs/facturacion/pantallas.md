---
title: Pantallas de facturación
status: draft
depends_on: [facturacion/factura-venta, facturacion/programadas, plataforma/contenedor]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-01 16:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Listado filtrable de facturas y programaciones con selección explícita, ficha de factura y acciones contextuales. Código: `src/ProConta.Facturacion.UI` (`ListadoFacturasVM` y `VistaListadoFacturas`, `EditorFacturaVM` y `VistaEditorFactura`, `DialogosFacturacion`), en el menú Facturación › Facturas emitidas.
**Aceptación:** comparación con la maqueta; selección, fila activa y universo filtrado no se confunden.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 11:38 · El prototipo de referencia es la maqueta completa `sources/maquetas/Facturas/`, que se integra en el lienzo del contenedor y sustituye a su listado simple de facturas.
- 2026-10-29 11:38 · Programadas y emitidas siguen en la misma pantalla. El diseño se hace para 2560×1440; a resoluciones menores basta con que sea funcional. El separador entre las secciones de programadas y emitidas se puede arrastrar para repartir la altura.
- 2026-10-29 12:05 · La maqueta de Facturas se valida primero en su página propia y, una vez validada, se reescribe en el formato del lienzo dentro del contenedor, porque el lienzo no admite incrustar otra página; así cada ronda de cambios se hace una sola vez.
- 2026-10-29 12:32 · Validado por el propietario el prototipo de Facturas emitidas y programadas (listado y detalle), maqueta `sources/maquetas/Facturas/` con la ronda 1 de cambios. Queda pendiente reescribirlo dentro del lienzo del contenedor.
- 2026-10-29 12:53 · Reescrito dentro del contenedor: la pestaña «Facturas emitidas» de `sources/maquetas/Contenedor/` y del lienzo muestra la pantalla validada; el lienzo añade una mesa de trabajo a 2560 × 1440. Pendiente del último vistazo del propietario en el lienzo.
- 2026-10-29 13:04 · Validado por el propietario el prototipo de Facturas emitidas y programadas dentro del contenedor (lienzo, versión 17).
- 2026-11-01 17:15 · Facturas emitidas: búsqueda (número, cliente, NIF, descripción, notas y facturas relacionadas) y filtros de estado, VeriFactu, serie, origen y fechas, con «Limpiar filtros»; fila activa (la del detalle lateral), casillas de marca y ámbito de las acciones colectivas, que son las marcadas o, sin marcas, la activa (quitar una fila del filtro la desmarca); desplegable de líneas en la fila; pie con recuento y total. Validar y Contabilizar actúan sobre el ámbito; Abrir, Duplicar, Rectificar y Eliminar borrador sobre la activa. VeriFactu, PDF, Enviar y Generar programadas se ven desactivados con el motivo; la sección de programadas llega con facturacion/programadas.
- 2026-11-01 17:15 · Editor de la factura, según el prototipo `sources/maquetas/FacturaBorrador/`: una pestaña por factura; cliente con sus cuentas de cliente y de retención en la sociedad y «Asignar…» cuando faltan; datos de la factura; líneas con cálculo al escribir (descuento en % y después fijo, redondeo por línea, suplidos sin IVA, total con IVA que recalcula la base); totales con desglose de IVA; vista previa del asiento con el mismo constructor que la contabilización (las cuentas que faltan en rojo); historial; errores en el campo y en un resumen con su acción correctora. Validar guarda antes los cambios pendientes. Una factura validada se ve en solo lectura y solo se cambian sus notas.
- 2026-11-01 17:15 · El diálogo «Rectificar» no pide motivo, a diferencia del prototipo: el servicio no lo guarda todavía. Queda pendiente decidir dónde se guarda (historial de las dos facturas) si se quiere.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 10 RF-FAC-10: listado, selección, proforma, contadores
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/WpfFacturas/docs/WPF_FACTURAS.md: pantallas anteriores
- 2026-10-28 12:28 · From sources/maquetas/Facturas/: maqueta de facturas
- 2026-10-28 12:28 · draft: Construir el listado y la ficha de facturas según la maqueta.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de Facturas emitidas, listado y detalle de factura (sources/maquetas/Facturas/ y lienzo del contenedor), antes de construir su UI WPF.
- 2026-10-29 11:38 · Sí, integrada en el contenedor
- 2026-10-29 11:38 · La resolución de las pantallas (al menos las nuestras) que usarán ProContaVF es de 2560 x 1440, no vamos a condicionar el diseño de pantalla por resoluciones inferiores mientras sigan siendo funcionales. Lo único que añadiría es poder modificar el tamaño de la sección de programadas y emitidas para poder ajustar medidas para resoluciones más bajas.
- 2026-10-29 12:32 · La maqueta la veo ok.
- 2026-10-29 13:04 · Por lo demás, veo bien la maqueta, lo único que falta es ver qué funcionalidad de podemos dar a una doble pantalla.
- 2026-11-01 16:18 · 2. Ok
- 2026-11-01 16:18 · answer: editor del borrador sin prototipo → (a) prototipo HTML corto del editor para validarlo antes de WPF

## Hand-over
- 2026-11-01 17:15 · Hecho: las dos pantallas compiladas y en el menú. Pendiente: probarlas en el equipo del propietario y validarlas contra la maqueta; el motivo de la rectificación y la doble pantalla quedan por decidir.
