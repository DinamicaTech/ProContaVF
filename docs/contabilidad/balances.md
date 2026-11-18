---
title: Balances
depends_on: [contabilidad/asientos, informes/reporting, informes/excel, plataforma/contenedor, datos/esquema]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-11 15:00 · Generar balances en Excel | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRuPaDaEwyqkxbhZeBADiWX
---
## Summary
Balance de sumas y saldos, de situación, pérdidas y ganancias y comparativos, con trazabilidad al origen; los presenta informes/reporting.
Hechos el balance de situación y la cuenta de pérdidas y ganancias del ejercicio de trabajo, como en el ProConta anterior: `src/ProConta.Contabilidad/Balances` clasifica cada apunte contabilizado con el modelo PGC Pymes 2008 de ModelosBalance, NodosBalance y ReglasCuentasBalance y los presenta informes/excel en un libro con tabla dinámica (menú Informes). El balance de sumas y saldos, entre fechas y en PDF o Excel, lo lleva su hijo contabilidad/balances/sumas-y-saldos. Pendientes: comparativos y PyG detallado.
**Aceptación:** cada cifra enlaza a su origen (asiento y orden del apunte en la hoja de datos); el activo cuadra con el patrimonio neto y el pasivo, y el resultado de pérdidas y ganancias con el que la regularización lleva a la 129.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Formatos de referencia de los balances: BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx de la carpeta ProConta (resuelve P-09).
- 2026-11-11 15:00 · Balance de situación y cuenta de pérdidas y ganancias del ejercicio de trabajo, con el método del ProConta anterior (sources/proconta-antiguo/menu-principal § 4 y su versión VB.NET): cada apunte contabilizado (EstadoAsiento 1) se clasifica por la regla de ReglasCuentasBalance de más prioridad y prefijo más largo, con la ruta bloque › apartado › título › subtítulo de su nodo, y su importe es (Debe − Haber) × Multiplicador (Haber − Debe en pérdidas y ganancias y en el pasivo). Pérdidas y ganancias: solo cuentas de los grupos 6 y 7, sin la regularización (TipoAsiento 2) ni el cierre (3). Situación: todo salvo el cierre, apertura incluida; el resultado del ejercicio sale de las 6 y 7 (VII. Resultado del ejercicio) o de la 129 si ya se regularizó.
- 2026-11-11 15:00 · Modelo: el PGC Pymes 2008 de la maestra (2 modelos, 89 nodos y 139 reglas, sembrado por la migración heredada 20260822_001). Las BD hechas con la copia-estructura anterior lo dejaron vacío: la migración 20261111_1430_ESQUEMA_BALANCES_PGC_PYMES_2008 lo carga igual que la maestra si las tres tablas están vacías, y la copia-estructura lo conserva desde ahora (ver datos/esquema).
- 2026-11-11 15:00 · Mejoras sobre el original acordadas con el propietario: la cuenta de 3 dígitos lleva su propio nombre del ejercicio (no el de una subcuenta), las fechas son las del asiento, la situación tiene un primer nivel ACTIVO / PATRIMONIO NETO Y PASIVO, los títulos de pérdidas y ganancias llevan su número del modelo oficial («4. Aprovisionamientos.») y apartados y títulos salen en el orden oficial. Las cuentas con apuntes que el modelo no clasifica no se pierden en silencio: se listan con su saldo en la hoja SinClasificar y en la barra de estado.
- 2026-11-11 15:40 · Reglas añadidas al modelo PGC Pymes 2008 para las cuentas con apuntes en ContaSol 2018–2026 que la maestra no clasificaba (Ronald, 2026-11-11 16:35: «Ok a Completar el esquema»): en situación, 211 › A.II Inmovilizado material, 240 › A.IV Inversiones en empresas del grupo l/p, 542, 545 y 552 › B.IV Inversiones financieras c/p, 119 › A.1 III. Reservas, 171 › B.II Deudas a largo plazo, 163 › B.III Deudas con empresas del grupo l/p, y todo el grupo 6 y 7 › VII. Resultado del ejercicio con la prioridad más baja; en pérdidas y ganancias, 754 › 5. Otros ingresos de explotación, 771 › 11. Deterioro y resultado por enajenaciones del inmovilizado, 760 › 13.b Otros ingresos financieros y 666 › 17. Deterioro y resultado por enajenaciones de instrumentos financieros. Las añade la migración 20261111_1540_REGLAS_BALANCES_CONTASOL si el modelo no tiene ya ese prefijo. Con ellas, en la v2g todos los ejercicios 2018–2026 cuadran sin cuentas sin clasificar.
- 2026-11-15 13:55 · Cada regla del modelo lleva además el código de su apartado y de su título (NodosBalance.Codigo, por ejemplo «ACT.B» y «ACT.B.6.017»), para que informes/ratios clasifique las masas del balance de situación sin listas de cuentas fijas (ver informes/ratios 2026-11-15 13:55). El balance en Excel no cambia.
- 2026-11-15 19:00 · Cumplida la línea «Derived from informes/informe-gestoria» del 2026-11-15 18:10: `GeneradorBalances.GenerarAsync` admite una fecha de corte, y el informe para la gestoría agrega con ella pérdidas y ganancias por centro de coste raíz y situación por apartado, título y subtítulo, sin duplicar reglas.
- 2026-11-15 19:34 · Cada fila clasificada de los balances lleva además la actividad de IVA de su centro de coste, para que el informe para la gestoría presente pérdidas y ganancias por actividad (informes/informe-gestoria 2026-11-15 19:24); el libro de balances del menú Informes no cambia.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 15 RF-BAL-01: balances por contrato y ejemplos esperados
- 2026-10-28 12:28 · From ProConta/BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx (disco del propietario): formatos de referencia
- 2026-10-28 12:28 · draft: Implementar los balances con sus ejemplos esperados.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-09 ¿los formatos de balance son los xlsx _Nuevo de la carpeta ProConta? (a) sí ✅ (b) otros
- 2026-10-31 16:54 · 4. a
- 2026-10-31 16:54 · answer: P-09 formatos de balance → (a) los xlsx _Nuevo de la carpeta ProConta
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 4: exportaciones Balance PyG, PyG detallado y Balance de situación del menú antiguo.
- 2026-11-11 14:23 · Ahora se trata de incorporar al proyecto la generación de un Balance de Situación y un Balance de Resultados en Excel y formato tabla dinámica.
- 2026-11-11 14:23 · Estos dos informes aparecerán dentro del menú 'Informes'
- 2026-11-11 14:31 · Ok, adelante con todo
- 2026-11-11 16:35 · Ok a Completar el esquema
- 2026-11-11 16:51 · im-presionante, cuadra al céntimo :-)
- 2026-11-15 11:57 · Derived from pantalla-de-inicio: cifras consultables sin pasar por Excel, a una fecha y por mes, del ejercicio de trabajo y del comparado: resultado antes de impuestos, ingresos y gastos por mes y gastos por naturaleza (por cuenta de 3 dígitos). Fase 1.
- 2026-11-15 18:10 · Derived from informes/informe-gestoria: pérdidas y ganancias y balance de situación del ejercicio de trabajo a fin de un trimestre, con el importe por centro de coste raíz, consultables sin pasar por su Excel, para las hojas PyG y Situación del informe.
