---
title: Factura de compra
status: draft
depends_on: [datos/esquema, datos/persistencia, nucleo, terceros/tercero]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 07:46 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-11-05 11:24 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Ente FacturaCompra con sus desgloses: inserción atómica, MD5 compatible del PDF para duplicados, configuración por sociedad y destino por sociedad/ejercicio.
**Aceptación:** PDF duplicado no crea segunda compra; un corte no deja desgloses huérfanos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 07:46 · La factura de compra guarda su moneda y sus importes originales (los euros se calculan con el cambio del día de la factura) y, si es una rectificativa o un abono del proveedor, la factura a la que rectifica.
- 2026-11-05 11:24 · Estructura nueva de FacturasCompra: sociedad-ejercicio explícito (IdSociedadEjercicio), desgloses de IVA en una tabla hija (tipo, base, cuota, confianza y evidencia) en lugar de las columnas fijas BI0-BI4, moneda e importe original con el cambio aplicado y su fecha, factura rectificada, confianza, «requiere revisión» y log del reconocimiento (JSON versionado), además de la configuración del reconocimiento por sociedad. La migración del esquema solo crea la estructura; el traslado de las facturas antiguas al formato nuevo va en la migración del ERP (implantacion/migracion-erp), como el resto de correcciones de datos.
- 2026-11-05 11:24 · Contabilizar no registra el pago: el pago al proveedor se concilia después con el banco (norma43). La forma de pago del proveedor se guarda como dato de la factura.
- 2026-11-05 12:43 · Hecho (entrega 1): la estructura nueva es la versión 17 del esquema (ver datos/esquema). Los desgloses de la factura van en DesglosesFacturaCompra (tipo en porcentaje, base, cuota, confianza y evidencia; uno por factura y orden); las columnas fijas ImporteBI0-4 y TipoIVA1-4 quedan como dato heredado que el programa nuevo no escribe. Registrar inserta cabecera y desgloses en una sola transacción, que vuelve a buscar el MD5 antes de insertar: el mismo PDF, aunque tenga otro nombre o esté en otra carpeta, no crea una segunda factura, y un corte no deja desgloses sueltos. El MD5 no es índice único (el ERP tiene PDF repetidos marcados como Duplicada), así que dos operadores importando el mismo PDF en el mismo instante podrían duplicarlo; la pantalla lo detectará como duplicado.
- 2026-11-05 12:43 · En moneda extranjera, la factura guarda el total original (ImporteTotalDivisa), el cambio y su fecha; en euros, total = total en divisa × cambio, cuota = cuota × cambio y base = total − cuota, redondeando a céntimos alejándose de cero, para que base + cuota = total también en euros. Sin cambio oficial, los euros quedan vacíos y la factura pide revisión.
- 2026-11-05 12:43 · El proveedor de una factura nueva se toma de la carpeta del PDF como hacía el ERP (la carpeta que más se le parece entre las de sus facturas anteriores, sin empate entre proveedores distintos), y su NIF y su cuenta del ejercicio, del tercero (CuentasProveedoresSociedad → Proveedores → Terceros), no del NIF grabado en las facturas del ERP, que a menudo es de otro proveedor.
- 2026-11-05 18:00 · Datos que edita la pantalla, en las columnas que ya tenía FacturasCompra: tipo de operación (TipoOperacion: 0 nacional, 1 intracomunitaria, 2 importación), tipo de producto (TipoProducto: 1 servicios, 2 bienes), clase de retención (TipoRetencion: 1 profesionales, 2 arrendamientos; los códigos se han deducido de los datos del ERP), suplido, cuenta de gasto (ConceptoGasto), forma de pago (CuentaFormaPago) y notas (NotasFactura); el centro de coste va en la columna nueva IdCentroCoste (versión 18). Una factura cuadra si base + IVA − retención + suplido = total, y está lista con proveedor, número, fecha e importes que cuadran.
- 2026-11-05 18:00 · Guardar desde la pantalla solo vale para facturas sin contabilizar y comprueba la versión: si otro operador la cambió mientras tanto, no se pisa y se le pide actualizar. Los desgloses se sustituyen enteros en la misma transacción. Una factura lista deja de pedir revisión.

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-05: duplicados, persistencia atómica, configuración
- 2026-10-28 12:28 · draft: Implementar el ente FacturaCompra y su persistencia con control de duplicados.
- 2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
- 2026-10-31 07:41 · 4. Ver relacionadas lo que hará es mostrar la factura original (en el caso de estar sobre una rectificativa) o viceversa
- 2026-11-05 11:18 · P5. Sí, cada sociedad una carpeta raíz dentro de la carpeta datos que es donde estará la BD compartida
- 2026-11-05 11:18 · answer: sociedad de cada PDF → cada sociedad tiene su carpeta raíz dentro de la carpeta de datos de la BD compartida
- 2026-11-05 11:18 · P8 a
- 2026-11-05 11:18 · answer: facturas antiguas → la migración del esquema solo crea la estructura nueva; su traslado va en la migración del ERP
