---
title: ProContaVF
depends_on: []
threads:
  - 2026-10-28 12:30 · Nuevo proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
ProContaVF es un software con doble funcionalidad: por un lado, un sistema de facturación muy automatizado compatible con VeriFactu; por otro, un módulo de contabilidad lo más automatizado posible (nóminas, facturas de venta, facturas de compra, importación Norma 43, conciliación bancaria asistida, asistente para generación de asientos, mayor "vitaminado", informes en tablas dinámicas Excel, ratios, etc.).
Sustituye al ERP actual (DeporWin) y a ContaSol para varias sociedades y ejercicios. La primera prioridad es poder facturar con VeriFactu y retirar el ERP; después, la contabilidad se incorpora área a área sin duplicar motores. Es una reconstrucción del proyecto ProConta anterior, cuya especificación y paquete de traspaso están en `sources/`.

## Decisions
- 2026-10-28 12:30 · Proyecto creado con Claude Visual Project.
- 2026-10-28 13:30 · Requisitos iniciales cargados en el árbol desde `sources/01_ESPECIFICACION_FUNCIONAL.docx` (copia en md en `sources/traspaso-2026-10-03/01_ESPECIFICACION_FUNCIONAL.md`), el paquete de traspaso del 03/10/2026 y las respuestas del propietario. Las maquetas HTML aprobadas están en `sources/maquetas/`, con datos ficticios.
- 2026-10-28 13:30 · Precedencia: prevalecen las decisiones explícitas y posteriores del propietario sobre documentos antiguos, comportamiento accidental y datos simulados; el código antiguo sirve para recuperar algoritmos y casos de prueba, no sus fallos (spec § 1.1).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 1: propósito, clasificación Acordado/Documentado/Propuesto/Pendiente y precedencia.
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 2 RF-GEN-03, 04: identidad de tercero transversal y servicios reutilizables; sin almacén, inventario, cálculo laboral completo ni presentación telemática de todos los modelos.
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 28: estado de partida y deuda que no debe convertirse en requisito.
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 30: orden de reconstrucción recomendado (configuración, base visual, acceso y contenedor; terceros y mantenimiento; facturación con VeriFactu; programaciones y correo; migración del ERP; compras, contabilidad, nóminas, N43; documentos, informes, gráficos) y criterio de terminado por módulo.
- 2026-10-28 12:28 · La idea es que trabajes estos requerimientos así como la carpeta con el proyecto actual, me hagas las preguntas que consideres relevantes para crear la estructura de nodos del proyecto y derives a cada nodo las tareas que le correspondan.
- 2026-10-28 12:28 · Será un proyecto relativamente grande (acabaremos con unas 200 tablas en la base de datos), por lo que es importante construir un buen árbol por nodos funcionales con un nivel de detalle adecuado.
