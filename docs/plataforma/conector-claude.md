---
title: Conector de Claude Desktop
status: draft
depends_on: [contabilidad/asientos, contabilidad/plantillas-asiento, contabilidad/diario, pantalla-de-inicio, datos/esquema]
threads:
  - 2026-11-16 09:58 · Conectar Claude Desktop con ProContaVF | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5UWQaWzwuZVTBM6RqkBGiW
---
## Summary
Conector local de Claude Desktop (servidor MCP por entrada y salida estándar) que deja a Claude consultar la contabilidad de ProContaVF y proponer asientos manuales, nuevos o de corrección, que solo se graban cuando el operador acepta la propuesta.
Es el ejecutable `ProContaConector.exe` (proyecto `src/ProConta.Conector`): Claude Desktop lo arranca con `--bd <accdb>` y `--operador <nombre>`, abre la BD en modo compartido (funciona con ProContaVF abierto) y no arranca si el esquema de la BD no es el del programa. No tiene reglas contables propias ni SQL libre: lee con los servicios del Diario, del catálogo de cuentas, de las plantillas y de la salud, y escribe solo con el motor de asientos.
Herramientas: de lectura `sociedades`, `buscar_asientos`, `ver_asiento`, `buscar_cuentas`, `mayor_cuenta` y `avisos_salud`; `proponer_asientos`, que valida con el motor en una transacción que siempre se revierte y devuelve un número de propuesta; y `grabar_propuesta`, la única que escribe.
Viene de la idea creative-lab/asistente-ia, que sigue en el laboratorio para lo que queda (subcuentas propuestas por Claude, botón en los avisos de salud, asientos a partir de documentos).

## Decisions
- 2026-11-16 09:58 · Creado al salir del laboratorio la parte del conector de la idea creative-lab/asistente-ia, para ejecutarla hoy, antes del arranque en producción. Es un nodo de plataforma porque el conector servirá a todas las áreas; la primera versión solo toca asientos.
- 2026-11-16 09:58 · Vía: conector local de Claude Desktop con el SDK oficial de MCP para C# (NuGet `ModelContextProtocol` 2.3.0, con `Microsoft.Extensions.Hosting` 10.0.0), aprobado por el propietario. Sin API de Anthropic ni clave: usa la suscripción de Claude Desktop. Los datos que Claude lee se envían a Anthropic dentro de la conversación.
- 2026-11-16 09:58 · Alcance de la primera versión: consultas de solo lectura, alta de asientos manuales nuevos y corrección de asientos con un asiento que anula el original y otro corregido. No borra, no modifica asientos existentes, no da de alta cuentas y no toca facturas.
- 2026-11-16 09:58 · Propuesta y grabación: `proponer_asientos` construye los asientos y los pasa por `IMotorAsientos.GuardarManualesAsync` sobre una BD de simulación que revierte siempre la transacción, así que se aplican todas las reglas del motor (cuadre, cuentas imputables, ejercicio y trimestre cerrados) sin grabar. La propuesta queda en memoria del conector con su número, la versión del asiento de origen y los asientos tal como se validaron; `grabar_propuesta` graba exactamente eso, una sola vez, y la rechaza si el asiento de origen ha cambiado de versión. Las propuestas se pierden al cerrar Claude Desktop.
- 2026-11-16 09:58 · Corrección de un asiento: los asientos nuevos son de tipo Rectificación y se relacionan con el de origen en RelacionesAsientos («CORRECCION»); el que lo anula (`anula`) queda punteado junto con los apuntes del origen, como en las plantillas «Anular asiento». Un asiento con libro de IVA (de una factura) no se corrige con asientos sueltos y el conector explica cómo se corrige: compras desde su factura, ventas con rectificativa, importados de ContaSol con «Corregir a mano» del Diario para las cuentas que no son de IVA. Un asiento de otro proceso automático tampoco, porque al regenerarse su lote la corrección quedaría duplicada. Excepción: los importados de ContaSol sin libro de IVA se corrigen como uno manual, porque su lote no se regenera en el día a día.
- 2026-11-16 09:58 · Trazabilidad: los asientos grabados llevan en Notas el motivo y «Propuesto por el asistente IA (Claude)», y el registro de cambios y OperadorCreacion guardan «<operador> · asistente IA».
- 2026-11-16 09:58 · Doble confirmación: Claude solo llama a `grabar_propuesta` cuando el operador acepta la propuesta (lo dicen las instrucciones del servidor) y Claude Desktop pide permiso en cada llamada a esa herramienta, la única marcada como de escritura.
- 2026-11-16 09:58 · Los textos de la BD (descripciones, conceptos, nombres, notas) son datos para Claude, nunca instrucciones (instrucciones del servidor, regla 7).

## Requirements
- 2026-11-16 09:26 · Analiza la posibilidad de integrar una IA (Claude, en este caso) dentro del propio ProcontaVF. La idea es poder lanzar prompts que puedan interactuar con la BD.
- 2026-11-16 09:26 · Por ejemplo: Corrige el asiento de enero 321 con IVA descuadrado (la IA haría una propuesta y si es OK, crearía un asiento rectificativo y uno corregido utilizando las clase CAsiento)
- 2026-11-16 09:35 · La opción de poder usar Claude Desktop a través de un conector me parece perfecta, avanza el análisis de esta idea.
- 2026-11-16 09:35 · Cuando implantarlo: hoy mismo. Quiero pasar a producción y es una herramienta que creo que vamos a necesitar desde buen principio.
- 2026-11-16 09:42 · Ok, pero también creación de asientos manuales nuevos, no solo corrección.

## Configuración en Claude Desktop
En Claude Desktop: Configuración › Desarrollador › Editar configuración, y en `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "procontavf": {
      "command": "C:\\Docs\\ProConta\\Conector\\ProContaConector.exe",
      "args": ["--bd", "<ruta del accdb>", "--operador", "<operador>"]
    }
  }
}
```

Después hay que cerrar y volver a abrir Claude Desktop. Las herramientas de lectura se pueden permitir siempre; `grabar_propuesta` conviene dejarla en «preguntar siempre».

## Criterios de aceptación
- Una propuesta descuadrada, con una cuenta inexistente, en un ejercicio cerrado, sin motivo o con un apunte a Debe y Haber a la vez vuelve con el error y su «Qué hacer», y no graba nada.
- Proponer no deja nada en Asientos ni en RelacionesAsientos.
- Grabar crea el que anula y el corregido en una transacción, de tipo Rectificación, relacionados con el origen, cuadrados, con el motivo y la marca del asistente, y puntea el origen; grabar dos veces no duplica.
- Un asiento manual nuevo sin origen se graba como Ordinario.
- Si el origen cambia después de proponer, no se graba.
- Un asiento automático que no es de ContaSol no recibe propuesta; uno importado de ContaSol sin IVA sí.
- Pruebas: `tests/ProConta.Conector.Tests` (12, contra la BD de pruebas) y prueba de punta a punta por MCP con la copia de validación `C:\Docs\ProContaVF-validacion\ProConta-conector-01.accdb`.

## Estado (2026-11-16 09:58)
Hecho: conector, 12 pruebas contra Access y prueba de punta a punta por MCP con la copia de validación (consultas, rechazo de un asiento de factura y corrección grabada del asiento 11377 en la copia). Pendiente: que el propietario lo pruebe en Claude Desktop con la copia de validación y, cuando ProContaVF esté instalado en producción (C:\Docs\ProConta), copiar el conector junto al programa y apuntar la configuración a la BD de producción.
