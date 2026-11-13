---
title: Nóminas
status: draft
depends_on: [contabilidad/asientos, datos/esquema, datos/persistencia, contabilidad/plantillas-asiento, terceros/roles, correo/recepcion, implantacion/migracion-erp]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-13 09:57 · Importar y contabilizar nóminas | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GStciZsBng954MDwM2Rk57Y
---
## Summary
Importación de nóminas directamente desde el fichero Excel, también recibido por correo en ProContaVF (detección de cabecera, mapeo y alias por sociedad, resolución de empleados) y su contabilización con el motor común de plantillas (contabilidad/plantillas-asiento) y el motor de asientos; reversión y reimportación controladas. Proyecto `ProConta.Nominas` (lector del Excel de la gestoría con ExcelDataReader, servicio de importación) y rol Empleado en Terceros (`ServicioEmpleados`, cuenta 465 por sociedad). Plan y entregas en project-files procontavf/nominas/plan-nominas.md: PR 1 importación (hecha), PR 2 contabilización e histórico, PR 3 pantallas y correo.
**Aceptación:** la de spec § 18.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: `frmNominas` («Resumen Nóminas») y su contabilización con plantilla y cuenta 465 por trabajador, en `sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen (plantilla, contabilizar y deshacer sin filtrar por sociedad, importación sin control de mes repetido ni de cuadre bruto − retenciones = neto, búsqueda del trabajador por nombre exacto).
- 2026-10-29 17:45 · La nueva versión no importa desde el portapapeles: lee directamente el fichero Excel de nóminas, también cuando llega por correo al buzón de ProContaVF, que lo pasa a este nodo para parsearlo, importarlo y generar los asientos (correo/recepcion).
- 2026-10-29 17:50 · La contabilización de nóminas usa el motor común contabilidad/plantillas-asiento, con plantillas por sociedad y tipo de paga, en lugar de un motor de plantillas propio.
- 2026-10-31 16:54 · Libros de ejemplo y asientos esperados (A08): los Excel de ProConta/Nominas/2025 y 2026 y los originales de D:\SynologyBox\SynologyDrive\<ejercicio>\Nominas, fuera de Git.
- 2026-10-31 16:54 · Los valores reales de la plantilla de contabilización (PlantillasApuntes) se leen de ProcontaEstructura.accdb al activar este nodo y se cargan en contabilidad/plantillas-asiento; no se suben al repositorio.
- 2026-11-13 11:05 · Formato de entrada: el Excel de la gestoría (.xls BIFF o .xlsx), leído con ExcelDataReader (MIT; aprobado por el propietario 2026-11-13 10:07) porque ClosedXML no lee .xls. El lector localiza la cabecera (fila con «TRABAJADOR» y al menos dos importes), toma de las filas de arriba el tipo de paga, el periodo («DEL … AL …») y la empresa con su NIF, lee las filas hasta «TOTAL EMPRESA» y exige que la suma de las filas cuadre con ese total. La columna sin cabecera a la izquierda del trabajador es su código en la gestoría. Importes redondeados a 2 decimales (AwayFromZero) al leer.
- 2026-11-13 11:05 · Campos: los nombres de F_NOM (Bruto, IRPF111, TC1, SSEmp, Neto…). Alias de cabecera por sociedad en AliasCamposNomina (campo vacío = columna ignorada), con alias por defecto para las cabeceras de la gestoría. Una cabecera desconocida para la importación hasta que se le asigna campo o se ignora. Control de cuadre por fila (aviso, no error): Neto = Bruto + DtoReEsp + InRetEsp + RetEmbarg − Autonomo − IRPF111 − (TC1 − SSEmp) − |AnticOtro| − |FaltaPrea|; cuadra en los 12 Excel de 2026.
- 2026-11-13 11:05 · Fecha de la nómina (confirmada por el propietario 2026-11-13 11:21): la «FECHA COBRO» del Excel si viene; si no, el último día del periodo. Fecha del asiento: la de la nómina, salvo que caiga en un trimestre cerrado; entonces el día 1 del primer mes del trimestre contable activo (misma regla que compras; sin excepciones en el motor de asientos).
- 2026-11-13 11:05 · Deshacer y reemplazar en cualquier trimestre. Si la nómina está en un trimestre cerrado no se tocan sus asientos: se genera su anulación (y los asientos nuevos) con la fecha del día 1 del trimestre activo. Reemplazar una importación sin contabilizar exige motivo; reemplazar una contabilizada pasa por deshacer (PR 2).
- 2026-11-13 11:05 · El trabajador es el rol Empleado de Terceros (tablas Empleados y EmpleadosSociedad, dueño terceros/roles), con código en la gestoría y cuenta 465 por sociedad (si no tiene, se crea la siguiente libre con su nombre). Reconocimiento: por código en la sociedad, o por NIF solo si el tercero ya es empleado de esa sociedad; en otro caso se proponen candidatos (mismo NIF, nombre parecido) y se resuelve a mano, nunca de forma automática. La ficha del tercero tiene la pestaña «Nóminas» solo con el rol Empleado.
- 2026-11-13 11:05 · Importación (ImportacionesNomina + Nominas, una fila por trabajador): idempotente por huella del fichero dentro del ejercicio-sociedad; el NIF de la empresa del Excel decide la sociedad y el periodo el ejercicio; si ya hay una importación vigente del mismo mes y tipo de paga, es «otra versión» y se ofrece reemplazar. Estados: PENDIENTE, INCIDENCIAS, CONTABILIZADA, REEMPLAZADA, DESHECHA.
- 2026-11-13 11:05 · Lo que llega por correo se contabiliza automáticamente si no queda nada por resolver (P6); si no, queda en la pantalla con sus incidencias.
- 2026-11-13 11:05 · Contabilización (PR 2): un asiento por trabajador con la plantilla de sistema «Nómina» del motor común, cargada con las 14 líneas de PlantillasApuntes tal cual, sin centro de coste. El histórico F_NOM 2022–2026 se migra enlazado a sus asientos ya importados; enero–septiembre de 2026 no se recontabilizan y sirven de prueba de paridad (asientos 827–1151).
- 2026-11-13 11:05 · Pantallas según la maqueta validada (sources/maquetas/Nominas/, propietario 2026-11-13 10:42).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 18 RF-NOM-01..05: importación, idempotencia, plantillas, reversión
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Nominas/docs/: interfaz anterior
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Implementar la importación y contabilización de nóminas.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-05/A08 ¿libros de ejemplo y asientos esperados? (a) los de ProConta/Nominas/2025 y 2026 de tu disco ✅
- 2026-10-31 16:54 · 23. Ok, los originales igualmente están en D:\SynologyBox\SynologyDrive\2026\Nominas (y otros ejercicios)
- 2026-10-31 16:54 · answer: P-05/A08 libros de ejemplo → (a) los de ProConta/Nominas/2025 y 2026; los originales están en D:\SynologyBox\SynologyDrive\<ejercicio>\Nominas
- 2026-10-29 16:55 · frmNominas: Importa y contabiliza nóminas a partir del portapapeles. El código actual es bastante operativo.
- 2026-10-29 17:14 · frmNominas actualmente importa desde el portapapeles, pero la idea es que la nueva versión ya sea capaz de trabajar directamente con el fichero Excel. De hecho, la idea es enviar el fichero de nóminas al email de ProContaVF para que lo parsee, importe y genere asientos.
- 2026-10-29 17:45 · [answered 2026-10-31 16:54] question: Los valores reales de la plantilla de contabilización (PlantillasApuntes: cuentas, importes, condiciones y lados) son datos de la base y no se han exportado: (a) leerlos de ProcontaEstructura.accdb cuando se active este nodo, sin subirlos al repositorio (recomendado) (b) los facilita el propietario
- 2026-10-31 16:54 · 24. Ok
- 2026-10-31 16:54 · answer: valores reales de la plantilla → (a) leerlos de ProcontaEstructura.accdb cuando se active este nodo, sin subirlos al repositorio
- 2026-10-29 17:50 · Derived from contabilidad/plantillas-asiento: definir las plantillas de contabilización de nóminas (hoy PlantillasApuntes) en el motor común de plantillas.
- 2026-11-13 10:07 · P3. La fecha del asiento es la fecha de la nómina (se puede exportar a un trimestre cerrado, no nos afecta a IVA ni retenciones de proveedores ni inquilinos)
- 2026-11-13 10:07 · P4. Cualquier trimestre (ver punto anterior)
- 2026-11-13 10:07 · Ok a ExcelDataReader
- 2026-11-13 10:07 · Añadimos una pestaña en terceros: 'Nominas' ?
- 2026-11-13 10:07 · Puedes preparar las maquetas de la pantalla de gestión de nóminas y la de nómina de un tercero para validarlas ?
- 2026-11-13 10:11 · Rectifico P3. La fecha es la de la nómina si no es de un trimestre cerrado, en caso contrario, el día 1 del primer mes del trimestre contable activo.
- 2026-11-13 10:49 · Ok, avanza lo que puedas hasta que cierre el otro hilo y así nos aseguramos no colisionar.
- 2026-11-13 11:21 · Sí, la fecha de cobro y, en su defecto, último día del período

## Hand-over
- 2026-11-13 11:05 · Hecho (PR 1, sin validar): esquema (migración 20261113_1100_NOMINAS_IMPORTACION), lector del Excel, rol Empleado y servicio de importación con alias, reconocimiento de trabajadores, idempotencia, otra versión, otra sociedad y otro ejercicio; pruebas sin BD y de Access (estas se ejecutan en el equipo del propietario). Los 12 Excel reales de 2026 se leen sin incidencias. Pendiente: PR 2 (plantilla «Nómina» con sus líneas, contabilizar, deshacer y reemplazar con la regla de fecha, migración de F_NOM, alias y plantilla, prueba de paridad) y PR 3 (pantalla de gestión, pestaña Nóminas del tercero y conexión con la recepción de correo).
