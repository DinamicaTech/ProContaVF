---
title: Modelos tributarios
status: draft
depends_on: [contabilidad/asientos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
---
## Summary
Cálculo trazable y borrador de los modelos 303 y 390, separado de la presentación a la AEAT; el PDF oficial rellenado y el fichero de intercambio con la AEAT quedan para después de la Versión Mínima Viable.
**Aceptación:** un modelo incompleto no ofrece presentación válida.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Primeros modelos: 303 y 390 (cálculo trazable y borrador). Después de la Versión Mínima Viable: reproducir en PDF el modelo oficial rellenado (con las plantillas oficiales si se encuentran, o a partir de los PDF resultantes de presentar un modelo) y generar el fichero de intercambio con la AEAT para presentarlo.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 16 RF-MOD-01, 02: cálculo, borrador y plantillas por año
- 2026-10-28 12:28 · draft: Implementar el cálculo y borrador de los modelos 303 y 390.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿qué modelos primero? (a) 303 y 390 ✅ (b) también 111/115/347
- 2026-10-31 16:54 · 5. Ok, aunque creo que esta parte está bastante poco desarrollada. Mi idea era poder replicar en PDF el modelo oficial con la información rellenada, pero no tengo claro si es fácil encontrar las plantillas de los PDFs oficiales, si no, tendremos que usar los PDFs resultado de presentar un modelo.
- 2026-10-31 16:54 · Al margen de poder pre-visualizar un modelo tributario, creo que hay un formato de intercambio (XML supongo) con la agencia tributaria para presentar un modelo. Todo esto para después de la Versión Mínima Viable (VMV), que no será muy mínima tampoco
- 2026-10-31 16:54 · answer: qué modelos primero → (a) 303 y 390; la reproducción del PDF oficial rellenado y la presentación por fichero de intercambio con la AEAT quedan para después de la Versión Mínima Viable
