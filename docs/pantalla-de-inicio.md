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
Es un borrador: el diseño está en la maqueta `sources/maquetas/Inicio/proconta-inicio.html` (versión 3, datos ficticios), con las respuestas del propietario del 2026-11-15 ya recogidas en Decisions; falta su visto bueno para repartir las líneas «Derived from» y abrir la implementación. La previsión de tesorería y la agenda quedan para después del arranque.
**Aceptación:** el propietario valida la maqueta, y cada indicador, tarea y aviso tiene su nodo de origen con su línea «Derived from»; cada aviso abre la pantalla que lo resuelve.

## Decisions
- 2026-11-15 07:44 · Creado como hijo de la raíz porque reúne datos de varias áreas (contabilidad, facturación, compras, Norma 43) y no pertenece a ninguna. Reparto acordado con el propietario: diseño de la pantalla aquí; la pestaña Inicio de plataforma/contenedor pasa a mostrarla; los ratios (informes/ratios), los gráficos (informes/graficos), las tareas (contabilidad/procesos) y los avisos (nodo dueño de cada dato) reciben su línea «Derived from» cuando se acuerde la maqueta, porque hasta entonces no se sabe qué indicadores y avisos entran.
- 2026-11-15 08:45 · Segunda versión de la maqueta, a petición del propietario: añade «Lo más urgente», previsión de cierre de facturación (facturado más comprometido por programadas y reservas), ratios compactos con rango de referencia, tesorería con previsión a 60 días y saldo por banco, agenda de los próximos 30 días, ingresos y gastos por mes y, en Salud, estado de los periodos, tendencia de los avisos, comprobaciones correctas y resumen de otras sociedades. Sigue sin validar: nada de esto es decisión hasta que el propietario lo debata.
- 2026-11-15 11:47 · Indicadores de cabecera: facturación del ejercicio, resultado antes de impuestos, tesorería, pendiente de cobro, pendiente de pago e IVA estimado del trimestre activo (útil para la tesorería y la previsión de pagos). De momento no hay más.
- 2026-11-15 11:47 · Dos métricas de comparación, ambas visibles: (1) comparativo real: el acumulado hasta hoy frente al acumulado del ejercicio comparado hasta la misma fecha; (2) proyección de cierre: total del ejercicio comparado × (acumulado actual hasta hoy / acumulado del ejercicio comparado hasta la misma fecha), que estima el ejercicio completo y lo compara con el anterior completo. La proyección se muestra distinguida del dato real.
- 2026-11-15 11:47 · La pantalla muestra solo la sociedad de trabajo; no hay vista ni resumen de otras sociedades.
- 2026-11-15 11:47 · Los rangos de referencia de los ratios son los estándar, no configurables por sociedad.
- 2026-11-15 11:47 · Salud: de momento sin más comprobaciones que las de la maqueta y sin posponer ni silenciar avisos; se revisará con el uso si resultan molestos. Un aviso desaparece solo cuando deja de cumplirse su condición.
- 2026-11-15 11:47 · La previsión de tesorería y la agenda (necesitan vencimientos fiables de cobros y pagos) se hacen después del arranque en producción; el saldo real de tesorería y el saldo por banco no dependen de ello.
- 2026-11-15 11:47 · Los nueve bloques (facturación, ratios, tesorería, agenda, ingresos y gastos, pendiente de cobro, gastos por naturaleza, ocupación turística y salud) se ven por defecto; «Personalizar» permite ocultarlos por operador.

## Requirements
- 2026-11-15 07:38 · Vamos a trabajar la pantalla de inicio ('home') para que muestre indicadores financieros (ratios), comparativos de facturación y otra información que pueda ser útil o vistosa.
- 2026-11-15 07:38 · También un apartado de 'salud' de la contabilidad en la que aparecerán tareas pendientes (por ejemplo, cerrar un IVA trimestral) y avisos de cosas a solucionar de la contabilidad.
- 2026-11-15 07:38 · El primer paso es diseñar una maqueta con Fable para poder debatirla.
- 2026-11-15 08:29 · ¿Puedes hacer una segunda revisión para aportar mejoras y sugerencias?
- 2026-11-15 11:47 · answer: ¿sobran o faltan indicadores? → De momento no se me ocurren más indicadores. El IVA estimado es útil para gestión de tesorería y previsión de pagos.
- 2026-11-15 11:47 · answer: ¿comparar a la misma fecha o con el ejercicio completo? → La comparación hasta la misma fecha del ejercicio anterior nos da la evolución entre un ejercicio y otro. La comparación con el ejercicio completo no aporta una información directa clara, excepto que ponderemos multiplicándolo por ([Valor total hasta la fecha actual]/[Valor total hasta la fecha ejercicio anterior]) en cuyo caso nos ofrece una proyección del total del ejercicio actual (versus ejercicio anterior), que sí que resulta útil ya que nos ofrece estimación de todo el ejercicio y comparativo del año anterior. Son dos métricas diferentes, la primera es un comparativo con datos reales, la segunda una proyección. Ambas son útiles.
- 2026-11-15 11:47 · answer: ¿una sociedad o todas? → Solo una sociedad
- 2026-11-15 11:47 · answer: ¿rangos de los ratios propios o estándar? → Estándar
- 2026-11-15 11:47 · answer: ¿más comprobaciones de salud, silenciar o posponer? → No se me ocurren más indicadores de salud. De momento sin silenciar ni posponer. Luego veré con el uso si resultan molestos.
- 2026-11-15 11:47 · answer: ¿previsión de tesorería y agenda ahora o después del arranque? → Después del arranque
- 2026-11-15 11:47 · answer: ¿qué bloques quitar de la vista por defecto? → La verdad es que los nueve son interesantes, yo no deshabilitaría ninguno por defecto
- 2026-11-15 07:38 · draft: Diseñar con una maqueta HTML la pantalla de inicio (indicadores, comparativos de facturación y salud de la contabilidad), debatirla con el propietario y, cuando la valide, repartir las líneas «Derived from» y abrir la implementación.
