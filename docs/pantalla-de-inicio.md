---
title: Pantalla de inicio
depends_on: [plataforma/contenedor]
status: draft
threads:
  - 2026-11-15 07:44 · Diseñar la pantalla de inicio | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEsFAruSGScKUCmRygBp3MV
---
## Summary
Pantalla de inicio (pestaña Inicio del contenedor) de la sociedad y el ejercicio de trabajo: indicadores financieros (ratios), comparativos de facturación con ejercicios anteriores y otra información útil o vistosa, más un apartado de «salud» de la contabilidad con las tareas pendientes (p. ej. cerrar un IVA trimestral) y los avisos de cosas a solucionar, cada uno con acceso directo a la pantalla que lo resuelve.
La pantalla solo muestra: los ratios los calcula informes/ratios, los gráficos los dibuja informes/graficos, las tareas salen de contabilidad/procesos y cada aviso lo da el nodo dueño del dato.
Es un borrador: el primer paso es la maqueta `sources/maquetas/Inicio/proconta-inicio.html` (datos ficticios) para debatirla con el propietario.
**Aceptación:** el propietario valida la maqueta, y cada indicador, tarea y aviso tiene su nodo de origen con su línea «Derived from»; cada aviso abre la pantalla que lo resuelve.

## Decisions
- 2026-11-15 07:44 · Creado como hijo de la raíz porque reúne datos de varias áreas (contabilidad, facturación, compras, Norma 43) y no pertenece a ninguna. Reparto acordado con el propietario: diseño de la pantalla aquí; la pestaña Inicio de plataforma/contenedor pasa a mostrarla; los ratios (informes/ratios), los gráficos (informes/graficos), las tareas (contabilidad/procesos) y los avisos (nodo dueño de cada dato) reciben su línea «Derived from» cuando se acuerde la maqueta, porque hasta entonces no se sabe qué indicadores y avisos entran.
- 2026-11-15 08:45 · Segunda versión de la maqueta, a petición del propietario: añade «Lo más urgente», previsión de cierre de facturación (facturado más comprometido por programadas y reservas), ratios compactos con rango de referencia, tesorería con previsión a 60 días y saldo por banco, agenda de los próximos 30 días, ingresos y gastos por mes y, en Salud, estado de los periodos, tendencia de los avisos, comprobaciones correctas y resumen de otras sociedades. Sigue sin validar: nada de esto es decisión hasta que el propietario lo debata.

## Requirements
- 2026-11-15 07:38 · Vamos a trabajar la pantalla de inicio ('home') para que muestre indicadores financieros (ratios), comparativos de facturación y otra información que pueda ser útil o vistosa.
- 2026-11-15 07:38 · También un apartado de 'salud' de la contabilidad en la que aparecerán tareas pendientes (por ejemplo, cerrar un IVA trimestral) y avisos de cosas a solucionar de la contabilidad.
- 2026-11-15 07:38 · El primer paso es diseñar una maqueta con Fable para poder debatirla.
- 2026-11-15 08:29 · ¿Puedes hacer una segunda revisión para aportar mejoras y sugerencias?
- 2026-11-15 07:38 · draft: Diseñar con una maqueta HTML la pantalla de inicio (indicadores, comparativos de facturación y salud de la contabilidad), debatirla con el propietario y, cuando la valide, repartir las líneas «Derived from» y abrir la implementación.
