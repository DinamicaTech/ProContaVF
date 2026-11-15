---
title: Motor de gráficos
status: draft
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Motor de gráficos neutral para WPF e informes: el consumidor aporta series, el motor dibuja, sin interpolar huecos.
**Aceptación:** los datos del gráfico coinciden con el origen; los huecos se ven.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 24 RF-GRA-01, 02: motor neutral, huecos visibles
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Graficos/docs/MOTOR_GRAFICOS.md: motor anterior
- 2026-10-28 12:28 · draft: Implementar el motor de gráficos.
- 2026-11-15 11:57 · Derived from pantalla-de-inicio: motor de gráficos en WPF propio, sin biblioteca nueva, con los tipos de la maqueta `sources/maquetas/Inicio/proconta-inicio.html`: barras agrupadas por mes con tramo rayado para la proyección, líneas (con área, tramo discontinuo y marca de «hoy»), barras horizontales con marca de referencia, barra apilada de antigüedad, minigráficos y franja de rango de los ratios; todos con tooltip al pasar el ratón y sin interpolar huecos. Fase 1 de la pantalla de inicio.
