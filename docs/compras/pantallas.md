---
title: Pantallas de compras
status: draft
depends_on: [compras/reconocimiento, plataforma/contenedor]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Revisión de la factura reconocida con confianza, candidatos y motivos visibles, y pantalla de pruebas no destructiva del reconocimiento.
**Aceptación:** se puede corregir cada campo antes de registrar; la pantalla de pruebas no escribe.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:40 · Referencia funcional: el formulario de importación de facturas de compra de ProConta antiguo (`frmEntradaFacturas`), descrito en `sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen.
- 2026-10-29 18:14 · Acordado con el propietario: la captura del portapapeles se sustituye por seleccionar texto en el visor del PDF, que rellena el campo marcado; la pantalla de pruebas del reconocimiento es un modo «Probar» de esta misma pantalla, que compara el resultado con lo grabado sin escribir nada; no hay filtro libre Q+ (peligroso) ni paradas de depuración.
- 2026-10-29 18:53 · Propuesto el prototipo de la importación de facturas de compra dentro del contenedor (`sources/maquetas/Contenedor/`, `?escenario=compras`, `compras-1440` y `compras-prueba`), pendiente de validación: lista con pestañas Pendientes y Contabilizadas, filtros en fichas y columnas redimensionables por operador; ficha central con proveedor por NIF (alta o salto a Terceros), matriz de IVA por tipo, retención, suplidos, tipo de operación, inversión del sujeto pasivo, divisa, cuenta de gasto y centro de coste, con la confianza y el motivo de cada campo reconocido; visor de PDF simulado que resalta el origen de cada campo; contabilizar (F12, una o varias) con el asiento propuesto, avisos y bloqueos; deshacer, rehacer y rectificativa como acciones distintas; al cambiar de factura se guardan sus cambios sin preguntar. En el lienzo, tableros «Facturas de compra · 2560 × 1440», «Facturas de compra · 1440 × 900» y «Reconocimiento en modo prueba · 2560 × 1440» (versión 21).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-06, 07: pantalla de pruebas y revisión final
- 2026-10-28 12:28 · draft: Construir la pantalla de revisión de facturas de compra y la de pruebas del reconocimiento.
- 2026-10-29 10:20 · Derived from plataforma/ui-base: crear y validar con el propietario el prototipo HTML de la importación de facturas de compra (aún no existe; se añade al lienzo del contenedor) antes de construir su UI WPF.
- 2026-10-29 18:14 · * A veces un asiento queda descuadrado por tema de redondeos (principalmente con apuntes de IVA), si es el caso, hay que ajustarlo con un apunte de diferencias de redondeo o similar
- 2026-10-29 18:14 · * Ok, rectificativas al trimestre activo
- 2026-10-29 18:14 · * Ok
- 2026-10-29 18:14 · * Ok, puede ser peligroso, sí
- 2026-10-29 18:14 · * Ok, las paradas son puntos de debug, ahora no tienen sentido
- 2026-10-29 18:14 · * Captura del porpapeles: Ok, mejor, así no tocamos el portapapeles
- 2026-10-29 18:14 · * Puede entorpecer exportaciones masivas de facturas. Si se ha modificado alguno de esos parámetros, asumimos que son los correctos y actualizamos, no tengo claro en qué circunstancia se pueden modificar sin quererlos actualizar en el proveedor.
- 2026-10-29 18:14 · * Ok
