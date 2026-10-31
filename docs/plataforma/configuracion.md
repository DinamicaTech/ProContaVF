---
title: Configuración global
status: draft
depends_on: [datos/esquema]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 18:40 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Resolutor único de la ubicación de datos y de la ruta exacta de la BD (CFG-GLOBAL) en `src/ProConta.Plataforma.Configuracion`: precedencia argumento `--bd`, variable `PROCONTA_BD` y configuración local en el perfil del usuario de Windows (`%LocalAppData%\ProContaVF\configuracion.json`), fuera de Access; `ComprobadorArranque` comprueba la versión y la compatibilidad del esquema al arrancar sin crear ni migrar nada.
**Aceptación:** la de spec § 4: selección cancelada no altera nada, ruta inválida informa ubicación y acción, el arranque no crea ni migra otra BD.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 16:58 · Las subcuentas de Norma 43 (traspasos, 555, divisiones, banco ficticio de remesas, prefijo de gastos) se parametrizan por sociedad y ejercicio en la configuración global, con los valores por defecto de `norma43/pantallas`; el diálogo de subcuentas de líneas N43 tendrá un botón que salta directamente a esa sección.
- 2026-10-31 18:40 · Precedencia de la ubicación de datos: argumento `--bd <ruta del accdb>`, variable de entorno `PROCONTA_BD` y configuración local del usuario; sin ninguna, se pide elegir la BD. Una fuente prioritaria inválida es un error que nombra la fuente, la ruta y la acción: nunca se pasa en silencio a la siguiente. Dos `--bd` distintos son error. Se simplifica la precedencia antigua de RF-CFG-02 (sin configuración junto al ejecutable ni «última selección» aparte, que aquí es la configuración local).
- 2026-10-31 18:40 · La configuración local es `%LocalAppData%\ProContaVF\configuracion.json` (solo la ruta de la BD, nunca secretos), escrita de forma atómica. Elegir una BD primero la valida (`ValidarSeleccion`) y solo se guarda al confirmar (`GuardarSeleccion`): cancelar no cambia nada. Una configuración dañada se informa y no se borra.
- 2026-10-31 18:40 · Al arrancar, `ComprobadorArranque` abre la BD en solo lectura y usa el diagnóstico de datos/esquema: solo una BD actualizada es compatible; con pendientes indica ejecutar el ActualizadorEsquema; más nueva, parcial o ajena se rechaza con su motivo. Nunca crea ni migra una BD.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 4 RF-CFG-01..05: resolutor único, precedencia de fuentes, configuración fuera de Access, comprobación de esquema al arrancar
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ConfiguracionGlobal/docs/CONTRATO_PUBLICO.md y CONFIGURACION_DATOS.md: contrato anterior
- 2026-10-28 12:28 · draft: Implementar la resolución de la ruta de datos y la comprobación de compatibilidad del esquema al arrancar.
- 2026-10-29 16:37 · Sí, la configuración de subcuentas desde la configuración global, cuando esté, podremos añadir un botón para que desde el popup de subcuentas se pueda saltar directamente a la configuración de subcuentas.
- 2026-10-31 17:53 · Derived from architecture: el resolutor de la ubicación de datos con la configuración local en el perfil del usuario de Windows y la comprobación de compatibilidad del esquema al arrancar (lógica, sin pantalla).

## Hand-over
- 2026-10-31 18:40 · Hecho: resolutor, configuración local y comprobación de arranque, con 11 pruebas (9 sin BD y 2 contra Access). Pendiente: el selector de BD en pantalla (lo usará plataforma/contenedor), el modo desatendido y la parametrización de subcuentas de Norma 43 (decisión 2026-10-29 16:58), que se hará con su nodo.
