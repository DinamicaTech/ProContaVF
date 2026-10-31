---
title: Prototipo del Mayor
status: draft
depends_on: [plataforma/ui-base]
threads:
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Prototipo HTML de la pantalla del Mayor, hecho en el lienzo de diseño del contenedor con la estética de las maquetas, para validarlo con el propietario antes de construir su UI WPF.
**Aceptación:** el propietario valida el prototipo; cubre las funciones del Mayor antiguo descritas en `sources/proconta-antiguo/mayor/ANALISIS-FUNCIONAL.md` con los cambios acordados en contabilidad/mayor, sin reproducir sus defectos, e incluye vacíos, errores y cuentas con muchos movimientos.

## Decisions
- 2026-10-29 10:25 · Creado como borrador hijo de contabilidad/mayor para no sobrecargar el nodo transversal plataforma/ui-base.
- 2026-10-31 08:43 · Plan acordado con el propietario: el Mayor es una pestaña del contenedor (Contabilidad → Mayor) y el mismo componente va embebido en la pestaña Contabilidad de la ficha de terceros (filtrado por sus cuentas) y sustituye al mayor provisional de Norma 43. Cabecera con selector de cuenta (saldo, apuntes, cuadrada), cuentas recientes y mayor combinado, y periodo por ejercicios de la sociedad con selección múltiple como en Norma 43. Vistas Normal, Compacto, Solo pendientes, Agrupar 700 y Solo no punteados. Todas las filas y totales sobre todo; total de la selección; Supr quita filas solo de la vista y recalcula el saldo con aviso de las ocultas; los filtros por factura se ven como etiquetas que se quitan. Acciones en menú contextual: ver asiento, línea Norma 43 o historial; ir a la subcuenta destino; buscar factura o importe; puntear; asignar número de factura con vista previa; cambiar concepto, subcuenta origen o destino; asistente de corrección (8 tipos) con asiento propuesto; marcar cuadrada con comentario; imprimir y exportar a Excel. Escenarios: vacío, error de carga, 5.000 movimientos, ejercicio cerrado en solo lectura y sin permiso.
- 2026-10-31 09:40 · Ronda 1 del prototipo, en el lienzo (versión 28, `?escenario=mayor`, `mayor-1440`, `mayor-grande`, `mayor-asistente` y `mayor-tercero`) y en `sources/maquetas/Contenedor/`: pestaña Mayor con cuentas recientes y grupos, selector de cuentas con sus saldos y filtros del antiguo (con «Con movimientos» que sí filtra por apuntes), flechas que llegan a la última cuenta, código corto para ver un grupo, varios ejercicios, vistas Normal, Compacto, Solo pendientes y Agrupar 700, todas las filas pintando solo las visibles (banco con 5.000 movimientos), saldo de la apertura de los ejercicios posteriores que continúa sin sumar en los totales, aviso de saldo parcial cuando hay filtros o filas quitadas, menú contextual con todas las acciones y atajos, historial por apunte con lote, numeración automática con propuestas que se marcan, asistente de corrección con el asiento propuesto y su fecha, y cuadre que solo guarda si cambia algo. La contrapartida ignora las cuentas de IVA para decidir si hay una sola; si quedan varias, dice «Varias». Los apuntes de Norma 43 exportados forman parte del mayor y su cuenta se cambia desde la línea bancaria. Escenarios de demostración: cuenta vacía, error de carga, carga, ejercicio cerrado, perfil de consulta y sin permiso.

## Requirements
- 2026-10-29 10:14 · El prototipo del mayor quizá es más conveniente que lo haga un nodo que cuelgue de contabilidad/mayor para no sobrecargar este nodo transversal.
- 2026-10-29 10:17 · draft: Crear y validar con el propietario el prototipo HTML de la pantalla del Mayor, a partir del Mayor de ProConta antiguo con los cambios acordados.
- 2026-10-29 10:45 · Derived from terceros/pantallas: el prototipo incluye el Mayor embebido en la pestaña Contabilidad de la ficha de terceros, con las mismas funciones y filtrado por las cuentas del tercero.
- 2026-10-31 08:41 · Está Ok, ahora a por el Mayor vitaminado, ánimos.
- 2026-10-31 08:43 · 1. Desaparece 2. Ok 3. Ok
