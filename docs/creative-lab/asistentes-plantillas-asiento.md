---
title: Plantillas asistente de asiento
status: idea
depends_on: [contabilidad/plantillas-asiento]
---
## Summary
Plantillas «asistente» que piden unos datos básicos y proponen un asiento complejo, como el reparto de dividendos. Queda por decidir cómo programar plantillas de esa complejidad con el motor de plantillas de asiento (recorrer las subcuentas de un grupo, el porcentaje de participación de cada socio).

## Decisions
- 2026-11-12 20:11 · Creado como idea en el laboratorio, a petición del propietario, al definir las clases de plantilla de contabilidad/plantillas-asiento (la clase Asistente existe, pero los asistentes complejos quedan aparcados aquí).

## Requirements
- 2026-11-12 20:11 · idea: Plantillas asistente que piden unos datos básicos y proponen un asiento complejo, por ejemplo «Reparto de dividendos»: pide el importe total, calcula el IRPF, selecciona las cuentas de todos los socios y pinta la propuesta de asiento; queda por decidir cómo programar plantillas de esta complejidad (recorrer subcuentas de un grupo, participación de cada socio).
