---
title: Pantalla Bancos
status: draft
depends_on: [norma43, plataforma/contenedor, plataforma/ui-base]
threads:
  - 2026-11-12 16:28 · Importar extractos Norma 43 | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GSwSSQpRLu74C8SixB5rybA
---
## Summary
Pantalla «Bancos»: una fila por cuenta bancaria de la sociedad con su saldo actual según el banco (saldo final del último extracto) y según la contabilidad (su 572), el número de movimientos y la fecha del último; al seleccionar una cuenta, sus lotes Norma 43 con estado e incidencias, también los virtuales de los splits cuando existan. «Importar N43» en la cabecera; «Revertir lote…» y «Ver incidencias» en el menú contextual.
**Aceptación:** los saldos y recuentos coinciden con las líneas importadas; importar desde la pantalla refresca la cuenta; revertir pide motivo y se bloquea con explicación si el lote tiene imputaciones o asientos; una cuenta desconocida se puede dar de alta desde el aviso de la importación.

## Decisions
- 2026-11-12 16:50 · Creado como hijo de `norma43` a petición del propietario, en el hilo «Importar extractos Norma 43». Se muestran los dos saldos (banco y contable de la 572) para ver la diferencia.

## Requirements
- 2026-11-12 16:47 · P5. Quizá una pantalla Bancos que, de paso, nos informe del saldo actual de todas las cuentas, número de movimientos, fecha del último movimiento, lotes N43 importados de cada banco (incluyendo los 'virtuales' para splitear líneas), etc. En esa misma pantalla un 'Importar N43'.
