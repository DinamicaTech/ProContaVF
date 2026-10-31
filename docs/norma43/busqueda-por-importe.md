---
title: Búsqueda por importe
status: draft
depends_on: [norma43/pantallas, compras, facturacion, contabilidad/asientos]
threads:
  - 2026-10-29 17:19 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Desde el menú contextual de la pantalla de líneas N43, con una o varias líneas, busca para cada una las facturas o cobros cuyo importe coincide y permite enlazar el elegido. Sustituye a `frmN43BusquedaPorImporte` de ProConta antiguo, sin sus defectos.
**Aceptación:** cada línea muestra sus candidatos ordenados por cercanía de fecha; elegir uno enlaza el documento, anota su número en la línea y pone la cuenta del tercero si la línea no tiene cuenta o no está exportada; en ventas la factura queda exportada con su asiento; nunca aparecen documentos de otra sociedad.

## Decisions
- 2026-10-29 17:18 · Creado como hijo de `norma43` a petición del propietario. Referencia funcional: `frmN43BusquedaPorImporte` (exportado solo en el clon local del propietario, `sources/proconta-antiguo/norma43/`, sección de su INDICE.md).
- 2026-10-29 17:18 · Criterios de búsqueda, heredados del formulario antiguo: cargos contra facturas de compra de la sociedad, importe ±0,02 € y fecha ±180 días, sin otra línea enlazada, las 10 más cercanas en fecha; abonos contra cobros pendientes de enlazar, importe ±0,02 € y fecha de cobro (o de vencimiento si no tiene) entre 270 días antes y 30 días después de la fecha del banco; si no hay ninguno, contra facturas emitidas con importe ±0,02 € y fecha hasta 5 días después. A diferencia del antiguo, los abonos también se filtran por sociedad.
- 2026-10-29 17:18 · Al elegir un candidato: se enlaza el documento con la línea y se anota su número; la cuenta del proveedor o cliente se pone en la línea solo si no tiene cuenta o no está exportada. En ventas, la factura queda marcada como exportada con su asiento (IdAsiento) y el asiento lleva el número de factura. Doble clic en un candidato abre el documento correcto (compra o venta), a diferencia del antiguo.
- 2026-10-29 17:18 · Se abre desde el menú contextual de `norma43/pantallas` («Buscar por importe…»), con selección simple o múltiple.
- 2026-10-29 17:38 · El propietario valida el diseño y su prototipo en la pantalla de líneas N43.

## Requirements
- 2026-10-29 17:08 · No veo las opciones de 'Buscar por importe' (sería un contextual que hace lo que hay en el formulario frmN43BusquedaPorImporte) ni la de 'Movimientos similares', que ejecuta el formulario frmMovimientosN43Similares. Ambas funcionalidades se merecen un nodo propio por debajo de Norma43
- 2026-10-29 17:18 · Sí, en la factura de venta ahora ya se marcará como exportada, con su IdAsiento y en el asiento aparecerá el número de factura.
