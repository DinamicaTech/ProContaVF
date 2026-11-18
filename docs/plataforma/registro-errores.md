---
title: Registro de errores
status: draft
depends_on: [plataforma/contenedor, plataforma/conector-claude, plataforma/configuracion]
threads:
  - 2026-11-18 18:48 · Registrar los errores inesperados | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G2FNuf3HdhCg2nAo5Gwvfe1
---
## Summary
Fichero de registro de los errores inesperados (los que ningún código controla) de ProContaVF y de su conector de Claude Desktop, para poder averiguar la causa de un fallo en producción.
Los dos programas escriben en el mismo fichero, uno por mes, en `%LocalAppData%\ProContaVF\errores\errores-AAAA-MM.log`, y al arrancar borran los de más de 12 meses. Cada error lleva fecha y hora, programa, versión, entorno, BD, operador, sociedad y ejercicio, pantalla activa, dónde se capturó, equipo y el detalle técnico completo. El registro común es `RegistroErrores` (en `src/ProConta.Plataforma.Configuracion`); nunca lanza: si no puede escribir, el aviso lo dice.
ProContaVF (`ErroresInesperados` en `src/ProConta.App`): un error en una pantalla no cierra el programa; se guarda y sale el aviso «Algo ha fallado» con la ruta del fichero, «Abrir la carpeta», «Copiar» y «Seguir trabajando». Si ocurre antes de abrir la ventana principal, el botón es «Cerrar ProContaVF»; si es de otro hilo y obliga a cerrar, queda guardado y un cuadro de Windows dice dónde. Las tareas en segundo plano que fallan sin que nadie espere su resultado solo se guardan.
Conector: si una herramienta falla de forma inesperada, se guarda con la sociedad que pidió y Claude recibe qué ha fallado y la ruta del fichero; los errores no controlados del proceso también se guardan.
Los errores ya explicados (BD no encontrada, esquema antiguo, validaciones…) siguen con su aviso y no van al fichero. Para validarlo, `--provocar-error` en ProContaVF (falla la pantalla activa al abrir la ventana principal) y en el conector (falla la herramienta «sociedades»), sin tocar datos.
**Aceptación:** un error provocado a propósito en una pantalla y otro en el conector quedan en el fichero con fecha, versión, sociedad, pantalla y detalle técnico; el mensaje que ve el usuario dice dónde está el fichero; el programa no se cierra sin dejar rastro.

## Decisions
- 2026-11-18 12:20 · Creado como borrador desde el hilo «Registrar las funcionalidades que faltan» (revisión de funcionalidades antes del arranque), con el visto bueno del propietario (2026-11-18 12:18).
- 2026-11-18 18:37 · Desglose acordado con el propietario («Ok, adelante con todo»): un fichero común por mes en `%LocalAppData%\ProContaVF\errores`, conservado 12 meses; en ProContaVF, aviso con la ruta, «Abrir la carpeta» y «Copiar» sin cerrar el programa; en el conector, el error y la ruta van a Claude en la respuesta de la herramienta; argumento `--provocar-error` para validarlo sin botones nuevos; solo los errores inesperados van al fichero, los ya explicados siguen con su aviso.
- 2026-11-18 18:48 · Construido: `RegistroErrores` y `ContextoError` en ProConta.Plataforma.Configuracion (escritura compartida con reintentos porque los dos programas usan el mismo fichero), `ErroresInesperados` y `VentanaPrincipal.Errores.cs` en ProConta.App, y `Proteger` en cada herramienta del conector (el SDK de MCP se traga la excepción y devolvería un error genérico, así que se captura dentro de cada herramienta). Pruebas sin BD en ProConta.Plataforma.Configuracion.Tests (RegistroErroresTests) y ProConta.Conector.Tests (ErroresInesperadosConectorTests).

## Requirements
- 2026-11-18 12:16 · Sí, un registro de errores nos puede resultar necesario. Como nuevo nodo en draft, lo activaré cuando modelos tributarios haya acabado
- 2026-11-18 12:18 · draft: Guardar en un fichero de registro todos los errores inesperados de ProContaVF y de su conector, con la fecha, la versión, la sociedad, la pantalla y el detalle técnico, y decirle al usuario en el mensaje de error dónde está ese fichero.

## Hand-over
- 2026-11-18 18:48 · Hecho y probado: pruebas automáticas y el conector con `--provocar-error` contra la copia ProConta-errores-01 (el error queda en el fichero y la respuesta da la ruta). Falta: validar en pantalla ProContaVF con `--provocar-error` y, cuando el propietario lo dé por bueno, quitar `status: draft`.
