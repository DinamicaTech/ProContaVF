---
title: Plantillas de asiento
status: draft
depends_on: [contabilidad/asientos, contabilidad/cuenta-contable]
---
## Summary
Motor genérico de plantillas de apuntes predefinidos («asientos tipo»): a partir de un apunte o de unos datos de entrada propone un asiento según una plantilla (anular, cambiar de cuenta, contra-apunte, ajuste de saldo inicial, paso a tesorería o a deuda con socios…) y lo genera con el motor de asientos, siempre con un motivo obligatorio. Es el único motor de plantillas: también lo usa nominas para contabilizar. Toma como base el asistente «Modificación de un asiento» de ProConta antiguo (`frmModificarAsiento`).
**Aceptación:** cada plantilla del asistente antiguo genera el mismo asiento (cuentas, lados, importe, fecha y nota) que describe el análisis, cuadrado y con su motivo; ninguna opción tiene dos nombres distintos; cancelar no escribe nada.

## Decisions
- 2026-10-29 17:45 · Creado como borrador a partir del análisis del asistente de asientos tipo.
- 2026-10-29 17:45 · Referencia funcional: `frmModificarAsiento` de ProConta antiguo, descrito en `sources/proconta-antiguo/asientos-tipo/ANALISIS-FUNCIONAL.md` (opciones, asiento que genera cada una con la rutina común de rectificación, motivo obligatorio y fechas), incluida su lista de defectos, que no se reproducen. Las cuentas de la sociedad que usa (113 de cuadres, 570 de tesorería, 5510001 de socios) se parametrizan por sociedad y ejercicio.
- 2026-10-29 17:50 · Un único motor de plantillas de apuntes: lo usan los asientos tipo y la contabilización de nóminas (y más adelante otros procesos); nóminas no tiene plantillas propias.

## Requirements
- 2026-10-29 16:55 · frmModificarAsiento: Es un asistente para crear 'asientos tipo'. Puede ser la base para un motor algo más genérico de plantillas de apuntes predefinidas.
- 2026-10-29 16:55 · draft: Construir un motor genérico de plantillas de apuntes predefinidos (asientos tipo) a partir del asistente frmModificarAsiento (sources/proconta-antiguo/asientos-tipo/ANALISIS-FUNCIONAL.md), usado por el Mayor, los asientos manuales y el motor de asientos.
- 2026-10-29 17:45 · [answered 2026-10-29 17:39] question: ¿Las plantillas de contabilización de nóminas (PlantillasApuntes, sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md § 4) usan también este motor? (a) sí, un único motor de plantillas para asientos tipo y nóminas, y más adelante otros procesos (recomendado: evita dos motores de plantillas) (b) no, nóminas mantiene sus plantillas propias
- 2026-10-29 17:39 · answer: ¿Nóminas usa también este motor? → Ok, tiene sentido (opción a)
