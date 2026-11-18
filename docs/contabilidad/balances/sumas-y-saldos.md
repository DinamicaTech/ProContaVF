---
title: Balance de sumas y saldos
status: draft
depends_on: [contabilidad/balances, informes/excel, informes/reporting, documentos]
threads:
  - 2026-11-18 20:20 · Generar el balance de sumas y saldos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G6QzkqEn4FcaLnVbaykDLT9
---
## Summary
Balance de sumas y saldos del ejercicio de trabajo, por cuenta y subcuenta y entre fechas, en Excel y en PDF desde el menú Informes. Es uno de los pendientes de contabilidad/balances y la base del libro de Inventarios y Cuentas Anuales (contabilidad/procesos/legalizacion-libros).
Hecho, pendiente de validar: Informes › «Sumas y saldos…» pide fechas (del ejercicio de trabajo), niveles (grupo, subgrupo, cuenta y subcuenta), si entra el cierre y el formato; calcula con los apuntes contabilizados como el Mayor (`src/ProConta.Contabilidad/Balances/SumasYSaldos.cs`), genera el PDF (HTML › PDF con WebView2) o el Excel (ClosedXML) con `src/ProConta.Informes/InformeSumasYSaldos.cs`, lo guarda por el gestor documental como «Informe contable» en «<año>\Informes», vinculado al ejercicio, y lo abre.
**Aceptación:** en la copia de pruebas, el total de sumas del Debe es igual al del Haber, y los saldos de cada subcuenta coinciden con el Mayor bajo las mismas fechas; los niveles de cuenta suman sus subcuentas.

## Decisions
- 2026-11-18 12:20 · Creado como borrador desde el hilo «Registrar las funcionalidades que faltan» (revisión de funcionalidades antes del arranque), con el visto bueno del propietario (2026-11-18 12:18).
- 2026-11-18 20:20 · Parámetros (diálogo de Informes › «Sumas y saldos…», recuerda lo último elegido mientras el programa está abierto): Desde y Hasta, las dos del ejercicio de trabajo (por defecto, del 1 de enero al 31 de diciembre, o hasta hoy si el ejercicio está en curso); niveles grupo (1 dígito), subgrupo (2), cuenta (3) y subcuenta (el código del apunte), por defecto cuenta y subcuenta; «Incluir el asiento de cierre», desmarcada por defecto (con él todos los saldos quedan a cero); formato PDF o Excel.
- 2026-11-18 20:20 · Cálculo: los apuntes contabilizados (EstadoAsiento 1) con fecha contable entre las dos fechas, sin el cierre salvo que se pida, como el Mayor; la apertura y la regularización entran si caen en las fechas. Columnas: cuenta, nombre, sumas Debe y Haber, saldo deudor y acreedor. Solo salen las cuentas con movimiento. Cada nivel suma sus subcuentas y su saldo es el neto de sus sumas. Las filas van en orden de código, cada nivel antes de sus hijos. Los totales son los del nivel más detallado que se muestra, para que cuadren con lo que se ve.
- 2026-11-18 20:20 · Nombres: los del plan del ejercicio; los grupos y subgrupos, que el plan no tiene (empieza en 3 dígitos), con los del PGC de pymes 2008 escritos en el programa. Una subcuenta con apuntes que no está en el plan sale como «(no está en el plan del ejercicio)».
- 2026-11-18 20:20 · Salidas: PDF en A4 vertical por el motor de informes (HTML › PDF con WebView2, como el Diario), con los niveles por encima del más detallado en negrita y sombreados y los totales al final; Excel con la tabla «TablaSumasYSaldos» con filtros (columna Nivel para filtrar) y los totales debajo con fórmulas SUMIFS sobre el nivel más detallado. Nombre del fichero: «Sumas y saldos <año> <dd-MM> a <dd-MM> (<aaaammdd-HHmm>)».
- 2026-11-18 20:20 · Guardado por el gestor documental (opción B del propietario, 2026-11-18 19:44): tipo de documento nuevo «Informe contable» (INFORME_CONTABLE, carpeta «Informes», por año; esquema 76, migración 20261118_2000_DOCUMENTOS_INFORME_CONTABLE), en «<año>\Informes» de la carpeta de documentos de la empresa, vinculado al ejercicio, y se abre la copia guardada. Si el gestor no puede guardarlo, se abre desde la carpeta temporal y la barra de estado dice por qué. Los dos balances del menú siguen guardándose en Documentos\ProContaVF\Informes.
- 2026-11-18 20:20 · Pruebas: `SumasYSaldosTests` (Debe = Haber, saldos deudores = acreedores, cada nivel suma sus subcuentas, orden, totales del nivel más detallado, validación de parámetros), `SumasYSaldosAccessTests` (contra Access: el saldo de cada subcuenta es el del Mayor en tres periodos y el cierre solo entra si se pide) y `SumasYSaldosInformeTests` (HTML y Excel).

## Requirements
- 2026-11-18 12:16 · Ok, nuevo nodo en draft, lo activo más tarde
- 2026-11-18 12:18 · draft: Balance de sumas y saldos del ejercicio de trabajo, por cuenta y subcuenta, entre fechas, en Excel y en PDF desde el menú Informes.
- 2026-11-18 19:44 · Opción B, aunque no sé si cambia el esquema, solo sería un TipoDocumento más, no ?

## Hand-over
- 2026-11-18 20:20 · Construido en la rama claude/sumas-y-saldos (clon C:\Docs\Codigo\ProContaVF-sumas-saldos), con las pruebas en verde. Falta la validación del propietario con la copia ProConta-sumas-saldos-01.accdb; al validarlo, quitar `status: draft`.
