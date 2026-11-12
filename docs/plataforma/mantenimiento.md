---
title: Mantenimiento de parámetros
depends_on: [plataforma/ui-base, seguridad/permisos]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 10:15 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-01 17:20 · Construir facturación de venta | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GCSSp6rAPiW3SbbKR5GnpnG
  - 2026-11-12 13:15 · Mejorar campos de la pantalla de Parámetros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GYK24dS6zjwpb1yF1Vr5iMz
---
## Summary
Pantalla genérica y extensible (CFG-MANT) para parámetros únicos y colecciones 1:N, agrupados por categorías, alimentada por proveedores que publica cada área dueña de sus datos; no adquiere la propiedad de las tablas. Código: contrato y servicio en `src/ProConta.Mantenimiento` (`IProveedorMantenimiento`, `ProveedorTablaAccess`, `ServicioMantenimiento`), pantalla en `src/ProConta.Mantenimiento.UI` (menú Configuración › Parámetros y tablas) y proveedores en cada área (`ProveedoresOrganizacion`, `ProveedoresFacturacion`). Cada campo lleva una ayuda descriptiva (lo comprueba `AyudasMantenimientoTests`) y sus etiquetas ocupan hasta dos líneas.
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
- 2026-11-10 20:05 · Hecho: proveedores «Cuenta de correo» y «Plantillas de correo» (categoría Correo, en `src/ProConta.Correo/Mantenimiento.cs`) y el parámetro general «Desviar todo el correo a».
- 2026-11-11 11:00 · Hecho: proveedor «Remitentes autorizados» (categoría Correo, por ejercicio); uno nuevo nace activo y con permiso para enviar documentos.
- 2026-11-12 11:45 · Para los proveedores de activos (activos 2026-11-12 11:45): categoría «Activos»; tipo de campo «lista abierta» (desplegable en el que también se escribe: el valor es la opción elegida o el texto escrito, y el proveedor decide qué hacer con un texto nuevo); acciones con propuesta (el proveedor calcula primero qué hará, la pantalla lo muestra y solo ejecuta si el operador confirma; si no hay nada que hacer, solo informa); y las acciones del proveedor también en el menú contextual de la lista (criterio de UI del propietario 2026-11-05).
- 2026-11-12 13:15 · Acordado con el propietario, en tres partes: (1) etiquetas de hasta dos líneas en lugar de cortarse, alineadas por fila, y una ayuda descriptiva en cada campo de cada proveedor, con «Hasta N caracteres» solo como añadido; una prueba falla si algún campo queda sin ayuda; (2) los campos de cuenta abren el buscador de cuentas del ejercicio, con «Crear subcuenta» y filtro por prefijo; (3) las claves foráneas a tablas 1:N (forma de pago, serie, retención…) usan un selector genérico de tablas 1:N: el valor no se escribe a mano y el alta del registro se hace desde el selector.
- 2026-11-12 13:15 · Hecho (parte 1): etiquetas en dos líneas, ayuda en los 93 campos que no la tenían y etiquetas del grupo «Cuentas» sin el «Cuenta de» inicial. La ayuda se escribe en cada proveedor, no se lee de la descripción del campo en la BD (solo está en la maestra).
- 2026-11-12 13:40 · Hecho (parte 2): los campos de cuenta no se escriben; la cuenta se elige siempre en el buscador de cuentas del ejercicio de trabajo (el de contabilidad/cuenta-contable, con «Crear subcuenta»), que abren la lupa, el clic en el campo, Intro, Espacio y F4; Supr vacía el campo. Cada campo declara sus grupos (`GruposCuenta`: 472, 477, 475, 57 y 431, 6, 7…) y el buscador se abre filtrado por ellos, con la casilla para quitar el filtro. Lo pone el contenedor (`ISelectorCuentas`); sin él, el campo se escribe como antes.

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
- 2026-11-12 11:45 · Derived from activos: categoría «Activos» con los proveedores «Tipos de activo», «Tipos de subcuenta» y «Plantillas de subcuentas»; desplegable en el que se elige o se escribe (al añadir una línea de plantilla, un nombre nuevo crea el tipo de subcuenta por dentro) y acciones con propuesta y confirmación («Generar subcuentas», «Borrar subcuentas sin apuntes»).
- 2026-11-12 11:49 · Ok a todo, lo único es que 'Formas de pago' no debería usar el buscador de cuentas si no un buscador genérico de tablas 1:n.
- 2026-11-12 11:49 · Igual que este campo, supongo que hay alguno más que necesitará la 1:n genérica.
- 2026-11-12 11:49 · Y prefiero no dejar escribir en el valor del campo de claves foráneas a tablas 1:n y mostrar siempre el selector.
- 2026-11-12 11:49 · El alta de registros se puede hacer directamente desde esta tabla.
- 2026-11-12 12:16 · Permite entrar texto libre cuando la idea es siempre a través del selector. Ya no debería aparecer nunca el label de error ni el botón 'Buscar o crear', que ya es la lupa

## Hand-over
- 2026-11-01 18:15 · Hecho: contrato, servicio, proveedores y pantalla, con pruebas sin BD (validación, búsqueda, permisos, solo consulta) y contra Access (series y conceptos). Pendiente: probar la pantalla en el equipo del propietario; Correo, VeriFactu y Ubicación de datos llegan con sus nodos.
- 2026-11-10 19:40 · Derived from correo/envio: proveedores «Cuenta de correo» (por ejercicio, con contraseña cifrada y las acciones «Probar envío» y «Probar recepción») y «Plantillas de correo» (por empresa y tipo), y el parámetro general «Desviar todo el correo a».
