---
title: Compras
depends_on: [terceros/roles, contabilidad/procesos, organizacion/sociedades-ejercicios, facturacion/verifactu, datos/esquema, implantacion/migracion-erp]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-05 11:24 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Facturas de compra: reconocimiento automático de los PDF, registro con control de duplicados y contabilización. Es la parte más importante para el propietario.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-05 11:24 · Plan acordado en tres entregas, cada una con su PR y validable por separado: (1) reconocimiento y registro (ente factura de compra con su estructura nueva, escaneo de la carpeta, duplicados por MD5 y motor VB portado a C#, con un informe TXT tabulado de aciertos sobre el corpus 2025-2026 comparado con lo grabado); (2) la pantalla WPF del prototipo validado con su modo «Probar»; (3) contabilización (asiento, libro de IVA soportado, deshacer, rehacer y rectificativa). La recepción por correo queda para cuando exista el motor de correo (correo/recepcion) y usará la misma entrada que la carpeta.
- 2026-11-05 11:24 · Afecta también a terceros/roles (carpeta y valores por defecto del proveedor), contabilidad/procesos (libro de IVA soportado y cuentas de compras de la sociedad), organizacion/sociedades-ejercicios (carpeta raíz de la sociedad), facturacion/verifactu (certificado para la consulta de NIF sin VeriFactu), datos/esquema (migraciones) e implantacion/migracion-erp (traslado de las facturas antiguas). Preguntas y desglose en el hilo (copias en la carpeta del proyecto `procontavf/compras/`).

## Requirements
- 2026-10-28 12:28 · Quizá la parte más importante es el módulo de FacturasCompra ya que se ha hecho un entrenamiento algo laborioso para afinar el reconocimiento automático de los campos de facturas de compra (PDF).
- 2026-11-05 10:45 · Implementar el módulo de gestión de facturas de compra, incluyendo reconocimiento de campos y contabilización.
- 2026-11-05 10:45 · En una primera fase, trabajará sobre un repositorio de facturas PDFs en una carpeta.
- 2026-11-05 10:45 · Cuando tengamos el motor de correo electrónico en marcha, podrá trabajar sobre facturas enviadas por email a un email de ProContaVF.
- 2026-11-05 11:18 · P9 Ok
- 2026-11-05 11:18 · answer: entregas → tres: reconocimiento y registro, pantalla, contabilización
- 2026-11-05 11:24 · Ok, adelante
