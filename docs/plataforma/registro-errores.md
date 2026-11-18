---
title: Registro de errores
status: draft
depends_on: [plataforma/contenedor, plataforma/conector-claude, plataforma/configuracion]
---
## Summary
Fichero de registro de los errores inesperados de ProContaVF y de su conector de Claude Desktop, para poder averiguar la causa de un fallo en producción. Hoy el programa principal no captura ni guarda los errores no controlados (solo lo hace la aplicación de muestra de la base visual). Borrador: se activará cuando terminen los modelos tributarios.
**Aceptación:** un error provocado a propósito en una pantalla y otro en el conector quedan en el fichero con fecha, versión, sociedad, pantalla y detalle técnico; el mensaje que ve el usuario dice dónde está el fichero; el programa no se cierra sin dejar rastro.

## Decisions
- 2026-11-18 12:20 · Creado como borrador desde el hilo «Registrar las funcionalidades que faltan» (revisión de funcionalidades antes del arranque), con el visto bueno del propietario (2026-11-18 12:18).

## Requirements
- 2026-11-18 12:16 · Sí, un registro de errores nos puede resultar necesario. Como nuevo nodo en draft, lo activaré cuando modelos tributarios haya acabado
- 2026-11-18 12:18 · draft: Guardar en un fichero de registro todos los errores inesperados de ProContaVF y de su conector, con la fecha, la versión, la sociedad, la pantalla y el detalle técnico, y decirle al usuario en el mensaje de error dónde está ese fichero.
