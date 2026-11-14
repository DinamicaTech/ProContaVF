---
title: Diario
depends_on: [contabilidad/asientos, contabilidad/mayor]
status: draft
threads:
  - 2026-11-14 21:50 · Refinar la pantalla Diario | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G1BkGHWh1giMA94fyxjnwdv
---
## Summary
Pantalla Contabilidad › Diario sobre las tablas Asientos y Apuntes: todos los apuntes del ejercicio, en dos vistas («Por asiento», con cada asiento completo, plegable y con totales por mes; «Lista de apuntes», solo los que cumplen el filtro y ordenable por cualquier columna), con filtros, selección múltiple, sumas de lo filtrado y de lo seleccionado, ficha del asiento con edición rápida, menú contextual con las operaciones sobre la selección, borrado de asientos manuales con copia consultable y exportación a PDF o Excel.
La maqueta validada está en `sources/maquetas/Diario/proconta-diario.html` (versión «Fable», elegida el 2026-11-14). Falta implementarla.

## Decisions
- 2026-11-14 21:50 · Maqueta elegida: la versión «Fable» (`sources/maquetas/Diario/proconta-diario.html`), refinada en este hilo; la versión «Opus» (rama claude/diario-opus) queda descartada. Nombres y atajos iguales que en el Mayor: «Puntear» F4, «Asignar número de factura…» Ctrl+N.
- 2026-11-14 21:50 · Número de asiento = Asientos.IdAsiento: fijo, en orden de alta y con huecos (asientos borrados); no se renumera nunca. Columna «Id apunte» = Apuntes.IdApunte, visible de fábrica.
- 2026-11-14 21:50 · Cabecera solo con acciones generales: «+ Nuevo asiento», «Exportar…» (PDF o Excel de todas las filas filtradas, con las columnas visibles y sus totales; no hay libro Diario oficial con «suma y sigue»), «Asientos borrados…» y «Columnas…». Las acciones sobre apuntes van en el menú contextual (regla «Menú contextual» de global-rules).
- 2026-11-14 21:50 · Columnas: selector en lista, guardado por operador como en el Mayor, con «(oculta de fábrica)» y botón «De fábrica». De fábrica: Asiento, Id apunte, Fecha, Origen, Subcuenta, Descripción, Concepto, Documento, Debe, Haber, Centro de coste, Punteado y Bloqueos. Ocultas: Orden, Contrapartida, Vencimiento, Fecha del documento, Grabado el, Modificado el y Operador.
- 2026-11-14 21:50 · Ficha del asiento (un apunte seleccionado): se cambian concepto, documento, vencimiento, punteado y centro de coste de cualquier apunte, también de asientos automáticos y de trimestres cerrados (no tienen efecto contable directo; el centro solo cuenta para la prorrata, que se calcula a final de año), con Guardar y Cancelar al pie, por el motor de asientos y con registro de cambios.
- 2026-11-14 21:50 · Eliminar: solo asientos manuales y solo si no lo impide la regla del trimestre cerrado (contabilidad/asientos). Pide el motivo (obligatorio) y deja una copia en AsientosBorrados/ApuntesBorrados (copia de Asientos/Apuntes con fecha, operador y motivo del borrado), consultable desde «Asientos borrados…». Los números no se reutilizan.
- 2026-11-14 21:50 · Corregir asientos automáticos: «Modificar el asiento…» abre la pantalla de asiento y deja cambiar subcuentas e importes si no lo impide el trimestre cerrado, salvo los apuntes de la 572 (banco). En los asientos que llevan libro de IVA (facturas de ventas y compras) se cambian las subcuentas de gasto, ingreso o tercero, pero no las líneas de IVA (472, 477, 4751) ni los importes: se corrigen en la factura, para que el 303 siga cuadrando con la contabilidad. Pide el motivo. Regenerar el origen sustituye el asiento sin avisar: quien regenera asume deshacer el cambio a mano (propietario, 2026-11-14 21:27).
- 2026-11-14 21:50 · Columna «Bloqueos»: 🔒 trimestre cerrado con cuentas fiscales (no se cambian cuentas ni importes ni se borra), 🏦 apunte de la 572 de un asiento automático, 🧾 apunte de una factura con libro de IVA.
- 2026-11-14 21:31 · Ejercicios (propietario, 2026-11-14 21:31): se ven el ejercicio de trabajo y el anterior, como en el Mayor. Los apuntes del anterior se modifican si no está cerrado; si la apertura del ejercicio siguiente ya existe, el Diario avisa de que ha quedado desactualizada y ofrece regenerarla.

## Requirements
- 2026-11-14 20:48 · Ok, la maqueta de Fable, efectivamente, es más completa que la de Opus, por lo que la damos por buena a falta de refinarla y resolver dudas en un nuevo hilo.
- 2026-11-14 21:13 · 1) El asiento sí tiene un número de asiento (Asientos.IdAsiento)
- 2026-11-14 21:13 · 2) Permitir modificar en la misma pantalla el concepto, documento, vencimiento, punteado y centro (diría que modificar el Centro de coste no tiene un efecto contable directo, solo para el cálculo de prorrata, pero eso ya se hace a final de año) en cualquier caso.
- 2026-11-14 21:13 · 3) Sí se puede eliminar un asiento manual siempre que no incumpla las reglas de trimestre cerrado y dejando un registro en 'AsientosBorrados/ApuntesBorrados', que básicamente será una copia de 'Asientos/Apuntes' que se podrá consultar desde esta pantalla. Al eliminar un asiento, se pide el motivo y queda registrado.
- 2026-11-14 21:13 · 4) En formato PDF bulk (de la información filtrada) y en Excel tenemos suficiente
- 2026-11-14 21:13 · 5) Puede ser útil poder trabajar con dos ejercicios, pero no sé si eso genera alguna inconsistencia si dejamos modificar apuntes del ejercicio anterior al ejercicio activo (siempre que no esté cerrado)
- 2026-11-14 21:13 · Al abrir la pantalla del asiento, poder modificar subcuentas e importes si no incumple reglas de trimestre cerrado, aunque sea de un asiento automático siempre que no modifiquemos una cuenta 572. Vaya, hemos de poder hacer una corrección, por ejemplo, en un asiento de una nómina porque ha habido un error. Quiero evitar tener que entrar en la BD a modificar algo porque por la interfaz no puedo.
- 2026-11-14 21:13 · Selector de qué columnas a mostrar en la grid, tal como tenía la maqueta de Opus
- 2026-11-14 21:13 · Añadir IdApunte como columna de la grid
- 2026-11-14 21:27 · Entiendo que solo se regeneran asientos automáticos bajo petición. Si alguien modifica un apunte automático, hemos de asumir que sabe lo que está haciendo y no lanzará la regeneración de asientos automáticos.
- 2026-11-14 21:27 · 2. Ok
- 2026-11-14 21:50 · [answered 2026-11-14 21:31] question: Dos ejercicios a la vez. Opciones: (a) solo el ejercicio de trabajo; (b) el de trabajo y el anterior, como el Mayor, editando el anterior si no está cerrado y, si la apertura del siguiente ya existe, avisando de que ha quedado desactualizada con un botón para regenerarla (recomendada); (c) varios ejercicios, solo lectura los que no son el de trabajo.
- 2026-11-14 21:50 · draft: Implementar la pantalla Diario según la maqueta sources/maquetas/Diario/proconta-diario.html y las decisiones de este nodo, con lo que pide al motor de contabilidad/asientos.
- 2026-11-14 21:31 · Ok a la propuesta de los dos ejercicios
- 2026-11-14 21:31 · answer: Dos ejercicios a la vez → (b) el de trabajo y el anterior, editando el anterior si no está cerrado y avisando de que la apertura queda desactualizada, con un botón para regenerarla.
