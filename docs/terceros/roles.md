---
title: Roles del tercero
depends_on: [terceros/tercero, contabilidad/cuenta-contable, organizacion/formas-pago]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 18:54 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 19:05 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-01 10:46 · Desarrollar la pantalla de Terceros | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GVR2rh2hJF6h9BMYjKSu8jn
---
## Summary
Roles opcionales de una identidad (Cliente, Proveedor, Contacto) con IDs funcionales propios y cuentas contables por rol y sociedad, apuntando a la identidad contable. Código: `ServicioRoles` (src/ProConta.Terceros/Roles): consultar los roles, darlos de alta o desactivarlos de forma idempotente dentro del guardado de la ficha y leer o asignar la cuenta del cliente y las de proveedor y acreedor por sociedad, resueltas en el ejercicio de trabajo.
**Aceptación:** añadir un rol no duplica la identidad; la cuenta asignada en un ejercicio se ve en el siguiente de la misma sociedad y no en otra sociedad.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 18:14 · Al guardar o contabilizar una factura de compra, los valores por defecto del proveedor que se hayan cambiado en ella (tipo de operación, tipo de producto, retención, país, forma de pago y la cuenta de gasto si estaba vacía o era la genérica) se actualizan en su ficha sin preguntar, para no entorpecer las contabilizaciones masivas; cada cambio queda en el historial de la ficha.
- 2026-10-31 07:46 · La moneda de las facturas del proveedor es otro de sus valores por defecto: se propone en sus facturas y, si se cambia en una, se actualiza en su ficha como el resto (decisión de 2026-10-29 18:14).
- 2026-10-31 16:54 · Particular (persona física) y Asociación (entidad sin personalidad) son tipos de persona, no roles; los roles siguen siendo Cliente, Proveedor y Contacto.
- 2026-10-31 16:54 · Proveedor/Acreedor: el rol Proveedor tiene dos cuentas opcionales (proveedor y acreedor) por sociedad y ejercicio; no son dos roles (resuelve P-04).
- 2026-10-31 19:05 · Alta de rol idempotente: si el tercero ya tiene el rol se devuelve su ID (y se reactiva si estaba inactivo). IdCliente no es autonumérico (conserva los números del ERP): el nuevo es el siguiente libre dentro de la transacción; IdProveedor e IdContacto son autonuméricos.
- 2026-10-31 19:05 · [replaced by 2026-11-01 09:52] Cuentas por rol y ejercicio en CuentasClientesEjercicio y CuentasProveedoresEjercicio; se lee la predeterminada (EsPredeterminada) del ejercicio del contexto y solo se asigna una cuenta de ese mismo ejercicio. El Contacto no tiene cuenta.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-01 09:52 · Cuentas de los roles por sociedad, no por ejercicio: el rol Cliente y el rol Proveedor guardan su cuenta por sociedad apuntando a la identidad contable (IdentidadesContables), que da la continuidad entre ejercicios aunque cambie el código; en cada ejercicio se usa la cuenta de esa identidad en ese ejercicio y, si aún no existe, se ofrece crearla con el mismo código. El proveedor tiene dos cuentas opcionales, de proveedor y de acreedor. Tablas nuevas CuentasClientesSociedad y CuentasProveedoresSociedad; las filas de CuentasClientesEjercicio y CuentasProveedoresEjercicio se trasladan a ellas y esas dos tablas se retiran. El Contacto no tiene cuenta.
- 2026-11-01 10:46 · Migración `20261101_1000_CUENTAS_ROLES_SOCIEDAD` (versión 8 del esquema): tablas CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable; única por cliente y sociedad) y CuentasProveedoresSociedad (con Clase PROVEEDOR o ACREEDOR; única por proveedor, sociedad y clase). Traslada las filas por ejercicio a la identidad de su cuenta y a la sociedad de su ejercicio (las de proveedor como PROVEEDOR) y retira CuentasClientesEjercicio y CuentasProveedoresEjercicio. Si para un rol y sociedad hay varias identidades gana la predeterminada del ejercicio más reciente y, a igualdad, la asignada la última. Antes de cambiar nada comprueba que todas las filas se pueden trasladar y, si no, para y dice cuáles. En una copia de la maestra real: 73 filas de clientes pasan a 58 cuentas por sociedad y 412 de proveedores a 394; 15 clientes y 18 proveedores tenían varias cuentas predeterminadas en el mismo ejercicio.
- 2026-11-01 10:46 · Una cuenta por sociedad se resuelve en el ejercicio de trabajo con la identidad; si la identidad aún no tiene cuenta en ese ejercicio, la ficha dice «La cuenta X aún no existe en el ejercicio N» y ofrece crearla con el mismo código. Si se escribe un código que no existe, el error lo explica y ofrece crear la cuenta con el nombre del tercero. La cuenta se crea al momento, tras confirmación, porque es del plan contable; su asignación al rol se guarda con la ficha.
- 2026-11-01 10:46 · Desactivar un rol conserva su fila y su Id. En la pestaña Roles, el cliente tiene su cuenta, el proveedor la de proveedor y la de acreedor (cada una en su fila con su descripción), el contacto cargo, departamento y notas, y el cliente y el proveedor sus notas.
- 2026-11-01 15:48 · Validado por el propietario (pantalla de Terceros y revisión 1).
- 2026-11-01 18:15 · Cuenta de retención del cliente por sociedad (migración `20261101_1632_CUENTA_RETENCION_CLIENTE`): CuentasClientesSociedad tiene una clase, CLIENTE o RETENCION, como las cuentas del proveedor; las filas existentes son CLIENTE y el índice único pasa a ser por cliente, sociedad y clase. Se edita en la pestaña Roles junto a la cuenta del cliente y la usa la contabilización de ventas; la factura ofrece asignarla o crearla («Retención IRPF <cliente>», 473) cuando falta.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 7 RF-TER-03, 04: roles idempotentes y cuentas por rol y sociedad/ejercicio
- 2026-10-28 12:28 · draft: Implementar los roles del tercero y sus cuentas por sociedad y ejercicio.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: ¿Asociación y Particular son roles o tipos de persona? (a) tipos/clasificación: Particular es persona física y Asociación entidad sin personalidad, y los roles siguen siendo Cliente/Proveedor/Contacto ✅ (b) roles nuevos
- 2026-10-31 16:54 · 33. a
- 2026-10-31 16:54 · answer: Asociación y Particular → (a) tipos/clasificación de persona; los roles siguen siendo Cliente/Proveedor/Contacto
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-04 ¿cómo se representa la pareja Proveedor/Acreedor? (a) dos cuentas opcionales del rol Proveedor ✅ (b) dos roles distintos
- 2026-10-31 16:54 · 34. a
- 2026-10-31 16:54 · answer: P-04 Proveedor/Acreedor → (a) dos cuentas opcionales del rol Proveedor
- 2026-10-29 18:14 · * Puede entorpecer exportaciones masivas de facturas. Si se ha modificado alguno de esos parámetros, asumimos que son los correctos y actualizamos, no tengo claro en qué circunstancia se pueden modificar sin quererlos actualizar en el proveedor.
- 2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
- 2026-10-31 16:54 · Derived from compras/reconocimiento: el rol Proveedor guarda el nombre de la carpeta donde están sus PDF (`<ejercicio>\Proveedores\<carpeta>`), por defecto su nombre comercial.
- 2026-10-31 17:53 · Derived from architecture: los roles Cliente, Proveedor y Contacto idempotentes y la cuenta de cliente y de proveedor por sociedad y ejercicio.
- 2026-11-01 09:48 · Sí, tiene sentido que las cuentas sean por sociedad, pero no tanto por ejercicio.
- 2026-11-01 09:55 · Derived from terceros/pantallas: cuenta de acreedor del proveedor, editable desde la ficha.
- 2026-11-01 09:52 · A. El concepto de Identidad traslada las M:N a otro sitio más cómodo y permite una continuidad entre ejercicios de una entidad sin depender de la cuenta contable.
- 2026-11-01 09:52 · answer: cuenta de los roles → (A) por sociedad, apuntando a la identidad contable
- 2026-11-01 17:18 · 4. La cuenta de retención sería diferente para cada cliente.
- 2026-11-01 17:20 · Derived from facturacion/factura-venta/contabilizacion: cuenta de retención del rol Cliente por sociedad (identidad contable), editable en la pestaña Roles.
- 2026-11-05 11:24 · Derived from compras: el rol Proveedor guarda la carpeta de sus PDF y sus valores por defecto (tipo de operación, tipo de producto, retención, país, forma de pago, cuenta de gasto y moneda), que la factura de compra propone y actualiza en su ficha según las decisiones 2026-10-29 18:14 y 2026-10-31 07:46.

## Hand-over
- 2026-10-31 19:05 · Hecho: roles y cuentas por ejercicio, con pruebas (rol idempotente sin duplicar identidad, cuentas distintas por ejercicio, cuenta de otro ejercicio rechazada). Pendiente, porque necesita columnas nuevas y lo pedirá compras: distinguir cuenta de proveedor y de acreedor (decisión 2026-10-31 16:54), valores por defecto del proveedor (tipo de operación, producto, retención, país, forma de pago, cuenta de gasto, moneda) y su actualización desde la factura (decisiones 2026-10-29 18:14 y 2026-10-31 07:46), y la carpeta de PDF del proveedor.
- 2026-11-01 10:46 · Hecho: roles en la ficha y cuentas por sociedad con la identidad contable, con su migración y pruebas. Pendiente: los valores por defecto del proveedor (llegan con compras), los datos comerciales del cliente (con facturación) y la carpeta de PDF del proveedor.
