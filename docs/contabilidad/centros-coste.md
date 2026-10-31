---
title: Centros de coste
status: draft
depends_on: [contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 20:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Centros de coste jerárquicos por sociedad y revisiones de cuenta a fecha de corte; saldos como cachés reconstruibles (la reconstrucción de saldos de cuentas está en contabilidad/cuenta-contable). Contrato `ICatalogoCentrosCoste` (src/ProConta.Contabilidad/CentrosCoste): listar la jerarquía, comprobar que un centro admite apuntes y dar de alta con su ruta jerárquica.
**Aceptación:** la caché reconstruida coincide con los apuntes.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 20:05 · El contexto de los centros de coste es la sociedad (CentrosCoste.IdSociedad), no el ejercicio. La ruta jerárquica es «CÓDIGO/CÓDIGO…» (única por sociedad, índice existente), con nivel, orden entre hermanos, ruta de orden y raíz calculados al dar de alta. Un centro admite apuntes si es de la sociedad, está activo y es imputable.
- 2026-10-31 20:05 · F_CDP y F_DEP (vinculadas) son los centros de coste y departamentos del ERP; siguen vinculadas y su traspaso a CentrosCoste queda para la migración del ERP.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-CUE-03: revisiones, cachés y centros de coste
- 2026-10-28 12:28 · draft: Implementar centros de coste y revisiones de cuenta.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 0, 3.12: «Regenera saldos» recalcula los saldos por periodo (0 apertura, 1-12 meses, 13 regularización, 14 cierre) a partir de los apuntes; es la reconstrucción de la caché de saldos.
- 2026-10-31 17:53 · Derived from architecture: el catálogo de centros de coste que necesita el motor de asientos (comprobar centro imputable) y su alta jerárquica.

## Hand-over
- 2026-10-31 20:05 · Hecho: contrato y alta jerárquica, con pruebas (ruta y nivel, repetido, padre de otra sociedad, imputable o no). Pendiente: revisiones de cuenta a fecha de corte (falta su tabla), modificación y baja de centros, saldos por centro.
