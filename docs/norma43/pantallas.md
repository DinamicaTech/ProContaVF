---
title: Pantalla de líneas bancarias
status: draft
depends_on: [norma43, plataforma/contenedor]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Tabla densa de líneas bancarias con filtros, detalle y acciones (también las de conciliación).
**Aceptación:** búsqueda y orden mantienen la selección por ID; las acciones indican su alcance real.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 14:40 · Referencia funcional: la pantalla "Gestión de Lineas N43" de ProConta antiguo (`frmLineasN43` y su ficha `frmLineaN43`), descrita en `sources/proconta-antiguo/norma43/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen. La maqueta autónoma anterior no se ha encontrado (la carpeta de maquetas está vacía); el prototipo se propondrá a partir de este análisis.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-03, 04: pantalla fiel a maqueta, selección explícita
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Norma43/docs/ENTREGA_VISUAL_LINEAS_BANCARIAS_20261001.md: entrega visual anterior
- 2026-10-28 12:28 · draft: Construir la pantalla de líneas bancarias según la maqueta.
- 2026-10-28 12:28 · question: La maqueta autónoma no está en el paquete (C:\Docs\VeriFactu\Maquetas\LineasBancarias\lineas-bancarias-proconta.html): (a) la copias a ProConta y la subo a sources/maquetas ✅
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de las líneas bancarias Norma 43 (lienzo del contenedor, 2050 × 1110) antes de construir su UI WPF.
- 2026-10-29 14:12 · Y si revisar primero el formulario frmLineasN43 con su código igual que has hecho con el Mayor vitaminado ?
