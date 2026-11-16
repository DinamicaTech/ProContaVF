---
title: Plataforma
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Piezas técnicas compartidas por toda la aplicación: configuración de datos, base visual WPF, contenedor principal, mantenimiento genérico de parámetros, atajos de teclado, entrega (compilación, pruebas, versiones, copias de seguridad) y el conector de Claude Desktop, que deja a Claude consultar la BD y proponer asientos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 16:34 · Nuevo hijo `plataforma/atajos-de-teclado`: catálogo único de operativas y sus atajos.
- 2026-11-16 09:58 · Nuevo hijo `plataforma/conector-claude`: conector local de Claude Desktop para consultar la BD y proponer asientos manuales, que salen del laboratorio (creative-lab/asistente-ia).

## Requirements
