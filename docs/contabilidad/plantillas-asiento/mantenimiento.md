---
title: Mantenimiento de plantillas de asiento
status: draft
depends_on: [contabilidad/plantillas-asiento]
---
## Summary
Pantalla para que el propietario cree, edite y desactive sus propias plantillas de asiento (guías y correcciones) sobre el motor de contabilidad/plantillas-asiento, sin pasar por un hilo.
**Aceptación:** una plantilla creada o editada en la pantalla se valida con `MotorPlantillas.Validar` (fórmulas, lados y nombres de datos), su vista previa sale de su ejemplo y aparece donde le toca según su clase y origen (guías en el asiento manual, correcciones en el asistente de corrección del Mayor); las plantillas de sistema no se editan.

## Decisions
- 2026-11-13 08:19 · Creado como borrador: el propietario deja el mantenimiento de plantillas (antes «PR 3» de contabilidad/plantillas-asiento) para una segunda fase.

## Requirements
- 2026-11-13 08:17 · Lo dejamos para una segunda fase
- 2026-11-13 08:19 · draft: Construir el mantenimiento de plantillas de asiento para que el propietario cree y edite sus propias guías y correcciones (descripción, datos de entrada, líneas con fórmulas, ejemplo y vista previa).
