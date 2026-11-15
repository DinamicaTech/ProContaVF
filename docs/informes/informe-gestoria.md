---
title: Informe para la gestoría
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/procesos/prorrata, contabilidad/balances, informes/ratios, informes/excel, facturacion/factura-venta, compras/factura-compra, contabilidad/centros-coste, plataforma/contenedor]
threads:
  - 2026-11-15 18:10 · Mejorar el informe trimestral para la gestoría | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GDKdsJYPwYjUXMKLnUhFQKN
---
## Summary
Libro Excel que se envía a la gestoría cada trimestre para cerrar el IVA y revisar facturas emitidas y recibidas, ratios y balances: sustituye al libro AsientosIVA<año>-<trimestre>.XLSX que se alimentaba de consultas de la ProConta anterior. Este nodo compone el libro (portada, controles y sello) y pide cada cifra al nodo que la calcula; no calcula reglas fiscales ni contables propias.
En construcción: primera entrega, el libro generado desde el menú Informes para el ejercicio de trabajo y un trimestre. El envío por correo queda en el borrador hijo `informes/informe-gestoria/envio-correo` y la petición por correo en `correo/recepcion/comandos-informe`.
**Aceptación:** con la BD de pruebas, el resultado del 303 de cada trimestre de 2026 coincide con el del libro anterior (1T 1.341,25 €, 2T 3.026,65 € y 3T 3.847,80 €, con prorrata provisional del 15 %) o la diferencia queda explicada apunte a apunte; la hoja Controles marca los apuntes de IVA sin centro de coste o sin fecha válida; el libro abre en Excel sin vínculos externos ni pasos manuales.

## Decisions
- 2026-11-15 18:10 · Creado desde el hilo «Mejorar el informe trimestral para la gestoría». La tarea empezó en contabilidad/balances y el propietario eligió un nodo propio (2026-11-15 13:26), porque el informe reúne IVA, facturas, ratios, balances y envío.
- 2026-11-15 18:10 · Desglose aceptado por el propietario (2026-11-15 18:06). Este nodo: composición del libro, hoja Resumen, hoja Controles y sello (fecha y hora de los datos, ejercicio, trimestre). contabilidad/modelos-tributarios: cálculo del 303 por trimestre. contabilidad/procesos/prorrata: porcentaje provisional, real acumulado y regularización estimada. contabilidad/balances: pérdidas y ganancias y situación por centro de coste. informes/ratios: ratios y rangos, los mismos que la pantalla de inicio. facturacion/factura-venta y compras/factura-compra: listados de emitidas y recibidas. informes/excel: libro de varias hojas con fórmulas, tablas y gráficos. Envío y comando por correo: solo anotados como borradores.
- 2026-11-15 18:10 · Alcance de la primera entrega: el 303 y la prorrata se limitan al cálculo, sin pantalla de modelos ni asiento de regularización. Maqueta de referencia y revisión del libro anterior en los ficheros del proyecto (procontavf/informe-gestoria); no van al repositorio porque llevan datos reales.

## Requirements
- 2026-11-15 11:53 · Te adjunto un informe Excel que envío a la gestoría cada trimestre para que puedan hacer el cierre del IVA, revisar facturas (emitidas y recibidas), ratios y balances.
- 2026-11-15 11:53 · Puedes revisarlo y hacer una maqueta de un Excel mejorado con el mismo objetivo, pero visualmente más atractivo y revisar que el cálculo del IVA (no deducible, prorrata y 100% deducible) sea correcto.
- 2026-11-15 11:53 · La idea es poder construir este Excel por petición de forma automática y consultarlo o enviarlo por correo a la gestoría o que la gestoría envíe un comando email para obtener este informe.
- 2026-11-15 11:53 · Y si se te ocurren ideas para añadir en el informe, serán bienvenidas.
- 2026-11-15 13:25 · A (informes/informe-gestoria)
- 2026-11-15 13:25 · Falta añadir columnas con retención y centro de coste a facturas de venta
- 2026-11-15 13:25 · La pestaña controles resulta muy útil como herramienta de validación
- 2026-11-15 18:06 · Ok al desglose.
