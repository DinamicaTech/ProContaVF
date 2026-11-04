---
title: Pantallas de facturación
status: draft
depends_on: [facturacion/factura-venta, facturacion/programadas, plataforma/contenedor]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-01 17:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
  - 2026-11-04 14:15 · Revisión visual de facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GJ1hUmWghpJQT8AjYb4rrpj
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
- 2026-11-01 18:15 · Facturas emitidas: búsqueda (número, cliente, NIF, descripción, notas y facturas relacionadas) y filtros de estado, VeriFactu, serie, origen y fechas, con «Limpiar filtros»; fila activa (la del detalle lateral), casillas de marca y ámbito de las acciones colectivas, que son las marcadas o, sin marcas, la activa (quitar una fila del filtro la desmarca); desplegable de líneas en la fila; pie con recuento y total. Validar y Contabilizar actúan sobre el ámbito; Abrir, Duplicar, Rectificar y Eliminar borrador sobre la activa. VeriFactu, PDF, Enviar y Generar programadas se ven desactivados con el motivo; la sección de programadas llega con facturacion/programadas.
- 2026-11-01 18:15 · Editor de la factura, según el prototipo `sources/maquetas/FacturaBorrador/`: una pestaña por factura; cliente con sus cuentas de cliente y de retención en la sociedad y «Asignar…» cuando faltan; datos de la factura; líneas con cálculo al escribir (descuento en % y después fijo, redondeo por línea, suplidos sin IVA, total con IVA que recalcula la base); totales con desglose de IVA; vista previa del asiento con el mismo constructor que la contabilización (las cuentas que faltan en rojo); historial; errores en el campo y en un resumen con su acción correctora. Validar guarda antes los cambios pendientes. Una factura validada se ve en solo lectura y solo se cambian sus notas.
- 2026-11-01 18:15 · El diálogo «Rectificar» no pide motivo, a diferencia del prototipo: el servicio no lo guarda todavía. Queda pendiente decidir dónde se guarda (historial de las dos facturas) si se quiere.
- 2026-11-02 12:50 · Nuevo diseño del editor (del propietario): arriba a la izquierda el cliente compacto (cuenta contable, nombre, NIF, id de cliente y domicilio; sin la cuenta de retención, que se sigue pidiendo con «Asignar…» en los avisos al validar) con «Cambiar cliente…» y el enlace a su ficha, y debajo el historial, que termina a la altura de «Datos de la factura» y tiene su propio desplazamiento. Arriba a la derecha «Datos de la factura», con el asiento en una segunda pestaña (vista previa para comprobar el funcionamiento, sin ocupar sitio fijo). Las líneas ocupan todo el ancho, con la descripción ampliada en la misma línea y altura variable. Los totales van debajo, en las columnas de base, cuota y total; la fila de suplidos solo aparece si los hay (no están en la base imponible) y el desglose por tipo solo con más de un tipo de IVA. Se quitan las etiquetas de ayuda. F2 añade una línea (de momento con el buscador de conceptos actual).
- 2026-11-02 12:50 · El diálogo «Rectificar» pide el motivo, obligatorio; el motivo se ve y se ajusta en «Datos de la factura» mientras la rectificativa es borrador, sale en el detalle del listado y la búsqueda lo encuentra.
- 2026-11-02 12:50 · Ocultar y mover columnas de la tabla de líneas queda para más adelante.
- 2026-11-04 14:15 · Revisión visual en el equipo del propietario contra la copia de pruebas: en el editor, cantidad, precio, Dto. %, retención % y día de pago se ven como números sin «€» (el precio conserva hasta 4 decimales); cambiar la retención recalcula el total a cobrar y el asiento; al abrir o volver al editor el foco pasa a la pantalla para que F2 y F3 funcionen sin hacer clic; la fecha cero de Access (30/12/1899) que dejaba el ERP antiguo se muestra vacía.
- 2026-11-04 18:05 · Ajustes del editor y del listado (propietario, 2026-11-04): la pantalla se llama «Facturas» (menú, pestaña y «Volver a Facturas»); Descripción general y la descripción de cada línea tienen la altura de un campo y crecen con el texto; Descripción general y Notas internas tienen el mismo ancho; una factura nueva propone hoy como fecha de operación y periodo; la tabla de líneas añade «Fecha» (entrega o consumo, LineasFactura.Fecha) y «Centro de coste» (de la lista de centros activos e imputables de la sociedad, propuesto por el concepto y modificable), y las líneas se reordenan con ↑ ↓ o Alt+↑ ↓ (el orden se guarda en NumeroLinea); el resumen del listado muestra una fila de IVA por tipo («IVA 21 %») y «Retención 19 %»; el estado de la factura se ve como el segmento elegido de un selector (como «Tipo de persona» en Terceros).

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
- 2026-11-01 17:18 · 2. Ok
- 2026-11-01 17:18 · answer: editor del borrador sin prototipo → (a) prototipo HTML corto del editor para validarlo antes de WPF
- 2026-11-04 17:28 · Ajustar la altura de 'Descripción' (al crear una factura) al contenido, de serie está a triple altura aunque esté vacío
- 2026-11-04 17:28 · La línea factura tiene un campo fecha (LineasFactura.Fecha) que sirve para indicar la fecha de entrega/consumo (por ejemplo, al facturar alquileres turísticos, indica la fecha de entrada). Añádelo a la grid de líneas para poderlo notificar si corresponde.
- 2026-11-04 17:28 · Fecha de operación, Periodo desde y hasta, por defecto, today().
- 2026-11-04 17:28 · Poder modificar el orden de las líneas factura. En facturas más complicadas, las líneas se organizan por grupos (por ejemplo, 'alquileres junio', 'alquileres julio'...), con lo que la posición de la línea tiene un significado semántico.
- 2026-11-04 17:28 · En el menú de la izquierda, en lugar de 'Facturas emitidas' -> 'Facturas' ya que la pantalla de facturas no solo muestra emitidas si no que permite generar nuevas facturas
- 2026-11-04 17:28 · En la pantalla 'Facturas emitidas', en el resumen de la derecha (Base imponible, IVA, retención, Total a cobrar...) poner el % de IVA (una línea por % si hay más de un % de IVA en la factura) y el % de retención.
- 2026-11-04 17:28 · El óvalo 'Validada' que aparece arriba del resumen de la factura, con un estilo como el de Tipo de persona en terceros (Empresa, Particular...)
- 2026-11-04 17:50 · En borrador nuevo, la altura de descripción igual que la del resto de campos
- 2026-11-04 17:50 · La anchura de Descripción general y Notas internas que sea la misma

## Hand-over
- 2026-11-01 18:15 · Hecho: las dos pantallas compiladas y en el menú. Pendiente: probarlas en el equipo del propietario y validarlas contra la maqueta; el motivo de la rectificación y la doble pantalla quedan por decidir.
- 2026-11-02 12:50 · Hecho: el nuevo diseño del editor y el motivo de la rectificación. Pendiente: probarlo en el equipo del propietario y su validación.
- 2026-11-04 14:15 · Hecho: primera revisión visual (Facturas emitidas, desplegar filas, factura nueva con F3 y F2, suplido, retención, pestaña Asiento, Rectificar… con motivo, cerrar con cambios) y correcciones de lo encontrado. Pendiente: segunda pasada visual con las correcciones, paridad con 2 o 3 facturas reales de ProConta y la validación del propietario.
- 2026-11-04 18:05 · Hecho: ajustes del 04/11 (Facturas, fecha y centro de coste en las líneas, orden de líneas, fechas por defecto, IVA por tipo, estado). Pendiente: la validación del propietario. La conversión de los centros de coste del ERP queda para implantacion/migracion-erp (ver facturacion/conceptos 2026-11-04 18:05).
