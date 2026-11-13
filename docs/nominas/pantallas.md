---
title: Pantallas de nóminas
status: draft
depends_on: [nominas, plataforma/contenedor, terceros/pantallas, correo/recepcion]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-13 09:57 · Importar y contabilizar nóminas | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GStciZsBng954MDwM2Rk57Y
---
## Summary
Importación, mapeo de columnas, resolución de empleados y revisión de la contabilización de nóminas. Pantalla «Nóminas» del menú (`src/ProConta.Nominas.UI`: `NominasVM`, `VistaNominas`, `DialogosNominas`, `ModuloNominas`) con los periodos importados del ejercicio arriba y las nóminas del periodo abajo, y pestaña «Nóminas» de la ficha del tercero (`NominasEmpleadoVM`, `VistaNominasEmpleado`). Datos de `ConsultaNominas` (src/ProConta.Nominas/Consulta).
**Aceptación:** empleado ambiguo exige resolver antes de confirmar.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: la lista «Resumen Nóminas» de `frmNominas` (importar, exportar a contabilidad, deshacer, refrescar, doble clic a los asientos del mes), en `sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md` § 1-3 y 6.
- 2026-11-13 11:05 · Maqueta validada por el propietario (2026-11-13 10:42) en sources/maquetas/Nominas/proconta-nominas.html: gestión de nóminas por mes y paga con estado y detalle por trabajador, acciones en el menú contextual (importar, resolver trabajador, contabilizar, deshacer, reemplazar) y pestaña «Nóminas» de la ficha del tercero.

- 2026-11-13 15:37 · Pantalla Nóminas (PR 3): una pestaña por sesión, abierta desde el menú «Nóminas». Arriba, los periodos del ejercicio activo (uno por mes y paga; «v2», «v3»… si es otra versión) con origen, trabajadores, bruto, SS empresa, IRPF, neto, coste, estado y asientos; filtro Todas · Pendientes · Contabilizadas y «Ver versiones reemplazadas» (también las deshechas, en cursiva). Selección múltiple; menú contextual: Contabilizar (Ctrl+E), Deshacer la contabilización…, Reemplazar con otro fichero…, Ver los asientos, Abrir el correo de origen, Importar fichero…, Exportar a Excel y Actualizar (F5); cada opción no disponible dice por qué. Aviso en la cabecera con el primer periodo con trabajadores sin resolver y «Resolver…», que los resuelve uno a uno y ofrece contabilizar. Abajo, las nóminas del periodo con trabajador («Sin resolver» si no se reconoce), código, cuenta 465, importes, cuadre (✓ o la diferencia) y asiento, con los totales y el fichero al pie; menú contextual: Abrir la ficha del empleado (en su pestaña Nóminas), Ver el asiento (doble clic) y Resolver trabajador….
- 2026-11-13 15:37 · Importar (botón «Importar fichero…» o soltando el .xls/.xlsx en la pantalla): si trae columnas nuevas, se elige el campo de cada una o se ignora (se recuerda para la sociedad); «ya importado» lleva a su periodo; «otra versión» ofrece ver los cambios y reemplazar; si no, un resumen (sociedad por NIF, periodo, paga, fecha, filas, cuadre y avisos) con «Solo importar» o «Importar y contabilizar» («Resolver y contabilizar…» si hay trabajadores sin reconocer: se resuelven uno a uno y se contabiliza). Resolver un trabajador: los candidatos (nunca se asigna solo) o el alta como empleado nuevo con su código y la siguiente 465 libre; las nóminas con el mismo código se resuelven juntas. Reemplazar enseña los importes que cambian por trabajador (y quién no cambia), avisa de la anulación si el trimestre está cerrado y pide el motivo; deshacer pide el motivo.
- 2026-11-13 15:37 · Pestaña «Nóminas» de la ficha del tercero (PR 3): solo se ve si es empleado de la sociedad activa. Código en la gestoría, cuenta 465 (la del ejercicio, o la más reciente), primera nómina; filtro por año (cada ejercicio con nóminas y «Todos»; empieza en el activo); tarjetas de bruto, IRPF retenido, neto y coste empresa de lo que se ve (para el certificado de retenciones y el 190); y sus nóminas vigentes de todos los ejercicios con su asiento y estado. Menú contextual: Ver el asiento (doble clic), Abrir el periodo en Nóminas (solo los del ejercicio activo) y Exportar a Excel.
- 2026-11-13 15:37 · Sin «Abrir el fichero original» de la maqueta: la importación guarda el nombre del fichero, no su ruta (el que llega por correo se abre desde Correo recibido). El código del trabajador y la 465 aún no se cambian en Roles (la Roles de la ficha no muestra el rol Empleado): se asignan al resolver el trabajador; queda pendiente para cuando el propietario lo pida.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 18 RF-NOM-01: mapeo y resolución con selección manual
- 2026-10-28 12:28 · draft: Construir la pantalla de importación de nóminas.
- 2026-10-29 17:45 · Derived from nominas: la pantalla parte del fichero Excel (o del recibido por correo), no del portapapeles, y ofrece lo que hoy hace «Resumen Nóminas» (sources/proconta-antiguo/nominas/ANALISIS-FUNCIONAL.md § 1-3, 6).
- 2026-11-13 10:07 · Puedes preparar las maquetas de la pantalla de gestión de nóminas y la de nómina de un tercero para validarlas ?

## Hand-over
- 2026-11-13 15:37 · Hecho (PR 3, pendiente de validar por el propietario): pantalla Nóminas, pestaña Nóminas de la ficha del tercero y Excel recibidos por correo (correo/recepcion). Pruebas: tests/ProConta.Nominas.Tests/PantallasNominasAccessTests.cs (Access: periodos, versiones, pestaña del empleado y correo), tests/ProConta.Nominas.UI.Tests (sin BD: cambios entre versiones, filas y pestaña del empleado) y el reparto al módulo en tests/ProConta.Correo.Tests/RecepcionAccessTests.cs. Pendiente: cambiar el código y la 465 en Roles.
