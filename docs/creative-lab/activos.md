---
title: Activos
status: idea
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Repensar la gestión de activos: empezó para inmuebles y se ha convertido en activos genéricos (inversiones, posiciones, amortización).

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-08 10:12 · Derived from contabilidad/procesos (propietario 2026-11-08 09:12): Inmuebles pasará a Activos; PlantillasInmuebles (cuentas de 5 cifras, p. ej. 62550 seguros → 62550XX por activo de los tipos 1 a 5) genera las más de 20 subcuentas de cada activo con descripción uniforme, y CuentasInmuebles materializa las generadas (cambiar la descripción en la plantilla las actualiza todas y dice en qué subcuenta va, p. ej., la amortización de cada activo). El Departamento/SubDepartamento del inmueble pasa a ser su centro de coste del árbol nuevo; la actividad a efectos de IVA (Actividades, versión 31) la hereda de ese centro, así que un activo no necesita actividad propia. Las series 472 son por actividad, no por activo: no chocan con las subcuentas por activo. Al rediseñar: que el activo apunte a su centro de coste y que, en compras, una cuenta de gasto de un activo proponga su centro (y con él su actividad).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 20 RF-ACT-01..05: activos e inversiones
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Activos/docs/MODELO_ACTIVOS.md: modelo anterior
- 2026-10-28 13:14 · Q4) c) Es una tabla que empezó para gestionar inmuebles pero ya se ha convertido en gestión de activos más genéricos. Hemos de re-pensarla un poco
- 2026-10-28 12:28 · idea: Rediseñar la gestión de activos genéricos (inmuebles, inversiones, amortización) antes de construirla.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 3.1, 3.2, 3.10: generar amortizaciones (682/282, prorrata del primer año), generar subcuentas de inmuebles según la plantilla de su tipo y liquidar ETFs; se conservan sus funcionalidades al rediseñar activos.
