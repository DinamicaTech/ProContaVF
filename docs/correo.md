---
title: Correo
depends_on: [facturacion/pantallas, facturacion/programadas, terceros/pantallas, plataforma/contenedor, plataforma/mantenimiento]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-10 19:40 · Desarrollar envío y recepción de correo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GT4mx65rhF9fy8aRE77SaGH
---
## Summary
Correo saliente (cuentas, transporte, lotes y seguimiento) y entrante (Mail Dispatcher), común para todas las áreas: ninguna duplica transporte, cola ni historial.
Una cuenta de correo por sociedad y ejercicio (por ejemplo dinamica2026@proconta.es) sirve para enviar y para recibir; el envío y la recepción los procesa la propia aplicación mientras está abierta. Se entrega en dos partes: envío y pantallas (correo/envio, correo/pantallas) y después recepción (correo/recepcion).

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-10 19:40 · Alcance acordado con el propietario (Q1-Q10 del hilo «Desarrollar envío y recepción de correo»): circuito completo de envío y recepción; Norma 43, nóminas y comandos de informe se conectan cuando existan sus motores. El correo antiguo (ConfiguracionCorreo, EnviosCorreo, ConfiguracionMailDispatcher y ProcesamientosMailDispatcher) no se migra y sus tablas se quedan como están; PlantillasCorreo y TiposCorreo siguen como catálogo de plantillas. Dependencias nuevas aprobadas: MailKit y MimeKit (MIT).
- 2026-11-10 19:40 · Excepción a «una sola rama» de la regla Breakdown, a petición del propietario (Q9): dos entregas, cada una con su rama y su PR; primero envío y pantallas, después recepción.
- 2026-11-10 19:40 · La propia aplicación procesa el envío y la recepción mientras está abierta (Q3), sin servicio de Windows ni tarea programada; con varias sesiones abiertas, solo una procesa a la vez.

## Requirements
- 2026-11-08 12:30 · Puedes hacer todo el desarrollo para poder enviar y recibir correos según las especificaciones?
- 2026-11-08 12:30 · Primero, analiza la documentación y plantéame las preguntas que necesites para poder hacer todo el desarrollo.
- 2026-11-10 19:26 · answer: Q1 alcance de la recepción → A: todo el circuito; lo que necesita un motor aún sin construir queda pendiente con incidencia.
- 2026-11-10 19:26 · answer: Q3 proceso con la app cerrada → C, de momento es la opción más sencilla y más teniendo en cuenta que habrá que realizar tareas con el correo recibido que requieren la aplicación completa
- 2026-11-10 19:26 · answer: Q8 historial antiguo → No se migran
- 2026-11-10 19:26 · answer: Q9 entregas → A: dos entregas, primero envío y pantallas y después recepción.
- 2026-11-10 19:26 · Ok a MailKit y Mimekit.
- 2026-11-10 19:26 · Ok a dejar fuera los puntos que indicas.
