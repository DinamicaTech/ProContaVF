---
title: Modelo 123
status: draft
depends_on: [contabilidad/modelos-tributarios, contabilidad/modelos-tributarios/presentacion, contabilidad/modelos-tributarios/modelo-193, contabilidad/procesos/aplicacion-resultado]
threads:
  - 2026-11-18 13:05 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
---
## Summary
Retenciones del capital mobiliario de los dividendos de cada trimestre en que se reparten: número de rentas, base y retenciones de los socios; fichero con el diseño de registro de la AEAT (`sources/aeat/DR123.xls`), validación con ServaliDos y presentación con la pieza común `presentacion`, con el mismo contrato que el 111 y el 115.
Hecho, pendiente de validar con la pantalla única «Modelos tributarios» (sin menú propio): `Modelo123` (`src/ProConta.Contabilidad/Modelos/ModeloRetenciones.cs`), su página en `FicheroRetenciones` y `Servicio123`. El 2T de 2025 de la copia de pruebas da lo mismo que el de la gestoría y pasa ServaliDos.
Después de la Versión Mínima Viable.
**Aceptación:** el fichero generado con datos de prueba pasa la validación de la AEAT (ServaliDos) sin errores; las retenciones del trimestre coinciden con las del reparto de dividendos y con el 193 del año; en el entorno de pruebas de la AEAT se presenta y queda registrado con su CSV y justificante.

## Decisions
- 2026-11-18 13:05 · Creado como borrador desde el hilo «Completar los modelos tributarios y su pantalla» (tanda 5 del hilo coordinador, pedido en la tanda 1: trimestral y desde el reparto de dividendos), en la rama claude/project-thread-v1z0ue, sin menú ni pantalla y sin cambio de esquema.
- 2026-11-18 13:05 · Cálculo (`Modelo123.Calcular`, sin BD): 01 número de rentas (socios distintos con dividendo en el trimestre), 04 base (dividendos íntegros) y 07 retenciones; 02, 05 y 08 (resto de rentas del capital mobiliario) a cero porque la sociedad no paga otras; 03, 06 y 09 los totales; 10 y 11 (periodificación) a cero; 12 = 09 + 11; 13 lo declarado en las anteriores del periodo (complementaria); 14 = 12 − 13. Forma de pago, complementaria y comprobaciones como el 111 y el 115 (`CalculoRetenciones.Cerrar`): domiciliación del 1 al 15 del mes siguiente al trimestre, fuera de él NRC; la complementaria solo puede subir el resultado.
- 2026-11-18 13:05 · De dónde salen los dividendos (`Servicio193.PerceptoresAsync`, la misma lectura que el 193, por fechas): los repartos grabados en el programa con fecha en el trimestre (bruto y retención de cada socio); si no hay (los migrados del ERP son solo asientos), lo abonado en el trimestre en la subcuenta de retención de cada socio, con la base reconstruida al porcentaje de retención de dividendos del ejercicio (19 % sin él), y se avisa de comprobar el bruto con el acta.
- 2026-11-18 13:05 · `Servicio123`: `TrimestresAsync` (estado de los cuatro trimestres con los mismos estados que el 303; un trimestre sin dividendos no se presenta y lo dice), `DeclaracionAsync` (declaración y `DeclaracionAeat` con modelo 123 y periodo nT para la pieza común) y `DividendosAsync` (de dónde sale cada casilla). Fichero: cabecera &lt;T1230EEEEnT0000&gt; con &lt;AUX&gt;, página 01 de 600 posiciones (razón social en 80, número de rentas en 15, importes en 17), complementaria «X» con el justificante anterior e IBAN si se domicilia.
- 2026-11-18 13:05 · Contraste con el 123 del 2T de 2025 de la gestoría: iguales 01 (2 rentas), 07, 09, 12 y 14 (23.456,80) y la forma de pago (I); la base 04 sale 123.456,84 frente a 123.456,80 porque se reconstruye desde la retención (el reparto de 2025 no está grabado en el programa). ServaliDos valida el 2T de 2025 sin errores ni avisos. No se ha presentado en el entorno de pruebas: el 2T de 2025 sale a ingresar fuera del plazo de domiciliación y la presentación con ingreso necesita un NRC de un pago real.
- 2026-11-18 13:05 · Pruebas: `Modelo123Tests` (casillas, complementaria, forma de pago y posiciones del fichero) y `Modelo123AccessTests` (dividendos del trimestre desde un reparto grabado y desde la subcuenta de retención, trimestre sin dividendos y validación con un cliente de la AEAT simulado).

## Requirements
- 2026-11-18 13:05 · Derived from contabilidad/modelos-tributarios/presentacion: generar el fichero del modelo con el diseño de registro de su ejercicio para validarlo y presentarlo con la pieza común.

## Hand-over
- 2026-11-18 13:05 · Hecho en la rama claude/project-thread-v1z0ue (sin PR: la abre el hilo coordinador). Pendiente: presentar en pruebas un 123 con importe (dentro del plazo de domiciliación o con un NRC); la base exacta cuando el reparto esté grabado en el programa (contabilidad/procesos/aplicacion-resultado); validación del propietario.
