---
title: Asistente IA
status: idea
depends_on: [contabilidad/asientos, contabilidad/plantillas-asiento, pantalla-de-inicio]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-16 09:40 · Conector de Claude para ProContaVF | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G5UWQaWzwuZVTBM6RqkBGiW
---
## Summary
Asistente con IA que consulta la BD de ProContaVF y propone asientos y correcciones (por ejemplo, «corrige el asiento 321 de enero con el IVA descuadrado»), siempre validados por el programa y confirmados por el operador antes de grabarse con el motor de asientos.
La vía elegida (2026-11-16) es un conector local de Claude Desktop: un ejecutable de ProContaVF que expone a Claude herramientas de lectura, de propuesta y de grabación sobre los servicios del programa, sin chat propio ni API de pago. Claude nunca escribe SQL ni toca la BD directamente.
El conector salió del laboratorio ejecutado el 2026-11-16 y ahora es plataforma/conector-claude. Aquí siguen como idea las ampliaciones: subcuentas propuestas por Claude, el botón «Corregir con Claude» en los avisos de salud, las correcciones de facturas y los asientos a partir de documentos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-16 09:58 · Leaves the lab: executed. La parte del conector (consultas, asientos manuales nuevos y corrección de asientos) es ahora el nodo plataforma/conector-claude, con el análisis y las decisiones en vigor; el análisis de abajo queda como antecedente. Las ampliaciones siguen aquí como idea.
- 2026-11-16 09:40 · Se amplía la idea con la del propietario de lanzar prompts que interactúan con la BD (antes «Asistente IA de asientos»). La integración será un conector local de Claude Desktop (servidor MCP) y no un chat dentro de ProContaVF con la API de Anthropic: usa la suscripción de Claude del propietario, sin coste por uso ni pantalla nueva. Ver el análisis más abajo.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 17 RF-IA-01..03 y 29 P-15: asistente contable
- 2026-10-28 12:28 · From docs/README.md (descripción del propietario): asistente para generación de asientos
- 2026-10-28 13:14 · Q7) b)
- 2026-10-28 12:28 · idea: Un asistente con IA propone asientos a partir de documentos o texto, revisados por el usuario antes de grabarlos con el motor de asientos.
- 2026-11-06 12:08 · Apunta en creative labs la posibilidad de que al crear una subcuenta, se pueda usar un asistente IA (p.e. necesito una subcuenta para contabilizar los gastos de un seguro de responsabilidad civil)
- 2026-11-16 09:26 · Analiza la posibilidad de integrar una IA (Claude, en este caso) dentro del propio ProcontaVF. La idea es poder lanzar prompts que puedan interactuar con la BD.
- 2026-11-16 09:26 · Por ejemplo: Corrige el asiento de enero 321 con IVA descuadrado (la IA haría una propuesta y si es OK, crearía un asiento rectificativo y uno corregido utilizando las clase CAsiento)
- 2026-11-16 09:26 · idea: Lanzar prompts a Claude que consulten la BD de ProContaVF y propongan correcciones (p. ej. un asiento rectificativo y uno corregido), que solo se graban con el motor de asientos cuando el operador acepta la propuesta.
- 2026-11-16 09:35 · Ok, amplía el nodo de Asistente IA de asientos.
- 2026-11-16 09:35 · La opción de poder usar Claude Desktop a través de un conector me parece perfecta, avanza el análisis de esta idea.
- 2026-11-16 09:35 · Cuando implantarlo: hoy mismo. Quiero pasar a producción y es una herramienta que creo que vamos a necesitar desde buen principio.
- 2026-11-16 09:40 · [answered 2026-11-16 09:42] question: ¿Sale hoy del laboratorio para ejecutarse? Opciones: (a) sí, como nodo nuevo plataforma/conector-claude, porque es una pieza técnica que servirá a todas las áreas; (b) sí, como contabilidad/asistente-ia, si solo va a tocar asientos; (c) no, sigue como idea. Recomendado: (a).
- 2026-11-16 09:40 · [answered 2026-11-16 09:42] question: ¿Qué alcance tiene la primera versión? Opciones: (a) consultas de solo lectura y corrección de asientos manuales con rectificativo y corregido; (b) lo mismo más el alta de subcuentas propuesta por Claude; (c) solo consultas. Recomendado: (a); las subcuentas, las facturas y los avisos de salud van después.
- 2026-11-16 09:40 · [answered 2026-11-16 09:42] question: ¿Se añade el SDK oficial de MCP para C# (paquete NuGet ModelContextProtocol)? Es la librería con la que el ejecutable habla con Claude Desktop; sin ella habría que programar el protocolo a mano. Recomendado: sí.

- 2026-11-16 09:42 · answer: ¿Sale hoy del laboratorio? → Sí, como plataforma/conector-claude («1. Ok»).
- 2026-11-16 09:42 · answer: ¿Alcance de la primera versión? → «Ok, pero también creación de asientos manuales nuevos, no solo corrección.»
- 2026-11-16 09:42 · answer: ¿SDK oficial de MCP para C#? → Sí («3. Ok»).

## Análisis (2026-11-16)

### Por qué un conector de Claude Desktop
- **Sin coste por uso.** Claude Desktop usa la suscripción del propietario; un chat dentro de ProContaVF necesitaría la API de Anthropic, que se paga aparte por tokens, y una clave de API que guardar.
- **Sin pantalla nueva.** La conversación, el historial y la confirmación de cada herramienta ya los da Claude Desktop. ProContaVF solo aporta las herramientas.
- **Mismas reglas que el programa.** El conector no tiene lógica contable propia: llama al motor de asientos, al catálogo de cuentas y al resto de servicios, así que se aplican el cuadre, los ejercicios y trimestres cerrados, la versión del asiento y el registro de cambios igual que en las pantallas.
- **Datos que salen del equipo.** Los datos que Claude lee (apuntes, conceptos, nombres de terceros) se envían a Anthropic dentro de la conversación, como ya ocurre al trabajar con Claude en este proyecto.

### Cómo funciona
1. Un ejecutable nuevo, `ProConta.Conector` (servidor MCP por entrada y salida estándar), que Claude Desktop arranca solo. Se configura una vez en `claude_desktop_config.json` con la ruta del ejecutable y la BD de producción (`--bd`).
2. Abre la BD en modo compartido, como ProContaVF, así que funciona con el programa abierto. Si el esquema de la BD no es el que espera, no arranca y lo explica (las migraciones solo las hace ProContaVF).
3. Las herramientas son de tres tipos:
   - **Lectura** (Claude Desktop puede usarlas sin preguntar): sociedades y ejercicios; buscar asientos por número, fecha, cuenta o texto; ver un asiento con sus apuntes, su libro de IVA, su lote y su factura de origen; buscar cuentas del plan; saldo y mayor de una cuenta; avisos de salud de la pantalla de inicio.
   - **Propuesta**: `proponer_asientos` recibe los asientos (fecha, descripción, apuntes), el motivo y el asiento de origen; los valida con las mismas reglas del motor sin grabar nada y devuelve un número de propuesta con el resumen en tabla. Una propuesta no válida vuelve con los errores para que Claude la corrija.
   - **Grabación**: `grabar_propuesta` graba una propuesta validada, tal cual se validó, con el motor de asientos (alta de asientos manuales en una transacción, tipo Rectificación, relación con el asiento de origen y punteo, como ya hace contabilidad/plantillas-asiento). Claude Desktop pide permiso en cada llamada; además, Claude solo la usa cuando el operador ha dicho que la propuesta está bien.
4. Si el asiento de origen ha cambiado desde la propuesta (otra versión), no se graba y se pide proponer de nuevo.
5. Los asientos grabados llevan el motivo en sus notas con la marca «Propuesto por el asistente IA», y el registro de cambios guarda como operador «<operador> · asistente IA».

### Límites de la primera versión
- No escribe SQL, no borra, no modifica asientos existentes ni crea cuentas: solo da de alta asientos manuales nuevos.
- **Asientos de factura.** El IVA de un asiento con libro de IVA sale de la factura, y el motor no deja cambiarlo a mano. Si el asiento a corregir es de una factura (como suele pasar con un IVA descuadrado), el conector no propone un asiento suelto: explica que hay que corregir la factura (compras) o emitir una rectificativa (ventas) y en qué pantalla.
- Los textos que lee de la BD (conceptos, nombres, notas) son datos, nunca instrucciones para Claude.

### Más adelante
- Alta de subcuentas propuesta por Claude (requisito del 2026-11-06), con nombre y código validados contra el plan.
- Botón «Corregir con Claude» en los avisos de salud, que copia el aviso como prompt.
- Correcciones de facturas y otras áreas, cada una con su herramienta de propuesta y de grabación.

### Criterios de aceptación propuestos
- Una propuesta descuadrada, con una cuenta inexistente o en un periodo cerrado vuelve con el error y no graba nada.
- Una propuesta que no se graba no deja rastro en Asientos ni en Apuntes.
- Grabar una propuesta crea el rectificativo y el corregido en una transacción, relacionados con el de origen, con motivo y en el registro de cambios.
- Si el asiento de origen cambia después de la propuesta, la grabación se rechaza.
- Un asiento con libro de IVA no recibe propuesta de asiento suelto.
- Pruebas automáticas de las herramientas contra la BD de pruebas y una prueba de punta a punta en Claude Desktop con la copia de validación antes de apuntar el conector a producción.

### Esfuerzo y riesgo
Alrededor de un día: el proyecto nuevo, unas ocho herramientas, las pruebas y la configuración de Claude Desktop. El riesgo principal es abrir una segunda vía de escritura en la BD de producción; se limita porque todo pasa por el motor de asientos y cada grabación pide permiso.
