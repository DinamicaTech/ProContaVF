---
title: Facturación programada
status: draft
depends_on: [facturacion/factura-venta, correo/envio]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Programaciones con cadencia y ancla de día, generación por lotes idempotente por sociedad y envío según preferencia.
**Aceptación:** la de spec § 12.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-02 12:50 · Propuesta acordada con el propietario, sin código: la programada se edita con el mismo editor que la factura. Una casilla «Periódica» en «Datos de la factura» (no en «Tipo», que es el tipo fiscal de la AEAT) muestra una pestaña «Programación» con periodicidad, fecha de inicio, día de emisión y final (fecha o número de repeticiones). El periodo desde/hasta es el del primer recibo y avanza en cada factura generada; la fecha de operación sigue siendo un dato fiscal de cada factura. Una programada es una plantilla sin número que genera borradores o facturas en cada vencimiento. Se hará en otra entrega.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 12 RF-PRO-01..06: programaciones, anclas, lotes, concurrencia, envío, duplicado
- 2026-10-28 12:28 · draft: Implementar programaciones y la generación por lotes.
