---
title: Mantenimiento de parámetros
depends_on: [plataforma/ui-base, seguridad/permisos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 17:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
---
## Summary
Pantalla genérica y extensible (CFG-MANT) para parámetros únicos y colecciones 1:N, agrupados por categorías, alimentada por proveedores que publica cada área dueña de sus datos; no adquiere la propiedad de las tablas. Código: contrato y servicio en `src/ProConta.Mantenimiento` (`IProveedorMantenimiento`, `ProveedorTablaAccess`, `ServicioMantenimiento`), pantalla en `src/ProConta.Mantenimiento.UI` (menú Configuración › Parámetros y tablas) y proveedores en cada área (`ProveedoresOrganizacion`, `ProveedoresFacturacion`).
**Aceptación:** los ejemplos de aceptación de spec § 8.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 08:14 · El propietario valida el funcionamiento del prototipo de la pantalla genérica de mantenimiento 1:N con configuración (`sources/maquetas/Configuracion/`, revisión de validación 1) como referencia para su UI WPF; el aspecto aún es el de la maqueta original.
- 2026-10-31 08:15 · Acordado con el propietario: la pantalla se integra en el contenedor con su estilo (pestañas «Parámetros y tablas auxiliares» y «Sociedades y ejercicios», lienzo versión 27, `?escenario=config`) sin cambiar su funcionamiento. La empresa y el ejercicio de trabajo los fija el contenedor y el perfil de consulta del operador deja la pantalla en solo lectura.
- 2026-10-31 08:41 · El propietario valida la integración de la pantalla en el contenedor (lienzo versión 27).
- 2026-10-31 16:54 · Cada proveedor del mantenimiento declara su ámbito (global, sociedad o ejercicio) y el servicio lo aplica junto con los permisos (resuelve RF-MAN-05).
- 2026-11-01 18:15 · Proveedores de este desarrollo: Configuración general (fila única), Empresas, Ejercicios (de la empresa de trabajo, con sus cuentas de IVA y suplidos y la configuración de facturación), Series de facturación (con el servicio tipado de series), Formas de pago (con su catálogo), Tipos de IVA, Tipos de concepto y Conceptos (Duplicar, Ver uso y baja según uso); Tipos y Estados de factura en solo lectura.
- 2026-11-01 18:15 · El servicio comprueba en cada guardado, baja o acción el permiso MANTENIMIENTO.EDITAR, la solo lectura del proveedor y la validación de cada campo (todos los errores a la vez, en su campo). La búsqueda no distingue tildes y encuentra mantenimientos, campos y sinónimos. Bajas restringidas por uso con el recuento de dónde se usa.
- 2026-11-01 18:15 · Las tablas sencillas no tienen columna de versión: si dos operadores cambian el mismo registro a la vez, gana el último que guarda. Las que tienen invariantes (series, conceptos, formas de pago) pasan por su servicio tipado.
- 2026-11-04 17:57 · Validado por el propietario (facturación de venta, PR #14).
- 2026-11-05 11:20 · Configuración general añade «Activar VeriFactu» (facturacion/verifactu), y Empresas, domicilio fiscal, código postal, población, provincia y carpeta de documentos (facturacion/factura-venta).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 8 RF-MAN-01..05: pantalla por proveedores, editores, servicios tipados para datos con invariantes, catálogo conocido
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/WpfConfiguracion/docs/: contrato e integración anteriores
- 2026-10-28 12:28 · From sources/maquetas/Configuracion/: maqueta de Configuración
- 2026-10-28 12:28 · draft: Construir el mantenimiento genérico con los proveedores del catálogo conocido.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-MAN-05 ¿ámbito de cada proveedor (global, sociedad o ejercicio)? (a) lo declara cada proveedor y el servicio lo aplica ✅ (b) todos por sociedad
- 2026-10-31 16:54 · 29. a
- 2026-10-31 16:54 · answer: RF-MAN-05 ámbito de cada proveedor → (a) lo declara cada proveedor y el servicio lo aplica
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de la pantalla genérica de mantenimiento 1:N con configuración (sources/maquetas/Configuracion/) antes de construir su UI WPF.
- 2026-10-31 08:14 · Ok a Mantenimientos 1:N y Configuración
- 2026-10-31 08:41 · Está Ok, ahora a por el Mayor vitaminado, ánimos.
- 2026-11-01 17:18 · 5. Ok
- 2026-11-01 17:18 · answer: proveedores en este desarrollo → (a) la pantalla genérica con Configuración general, Empresas, Ejercicios, Series de facturación, Formas de pago, Tipos de IVA, Tipos de concepto y Conceptos; Tipos y Estados de factura en solo lectura; Correo, VeriFactu y Ubicación de datos con sus nodos
- 2026-11-04 17:57 · Ok, validado
- 2026-11-04 23:55 · Derived from plataforma/atajos-de-teclado: acoger el proveedor «Atajos de teclado» (un `IProveedorMantenimiento` de ámbito global): lista de operativas con id, texto, tecla por defecto y tecla asignada, captura de la tecla pulsándola, aviso de conflicto con la corrección propuesta y restaurar valores por defecto.
- 2026-11-05 11:20 · Derived from facturacion/verifactu: casilla «Activar VeriFactu» en Configuración general; y de facturacion/factura-venta: domicilio fiscal y carpeta de documentos en Empresas.

## Hand-over
- 2026-11-01 18:15 · Hecho: contrato, servicio, proveedores y pantalla, con pruebas sin BD (validación, búsqueda, permisos, solo consulta) y contra Access (series y conceptos). Pendiente: probar la pantalla en el equipo del propietario; Correo, VeriFactu y Ubicación de datos llegan con sus nodos.
