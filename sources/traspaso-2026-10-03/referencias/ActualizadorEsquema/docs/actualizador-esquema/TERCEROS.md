# Contrato de Terceros para la BD Maestra

Decisión del usuario (29-09-2026): `Terceros` será la identidad común de personas y organizaciones. Contendrá los datos compartidos, como nombre y dirección. `Clientes`, `Proveedores`, `Contactos` y los demás submodelos contendrán solo datos propios de cada función. Un tercero puede tener varias funciones simultáneas. La cuenta contable pertenece a la función correspondiente, no a `Terceros`.

La interfaz empezará en la ficha de Terceros y mostrará pestañas para los submodelos activos de ese tercero. La existencia de una pestaña no crea otra identidad: cliente, proveedor y contacto comparten `IdTercero`. Un contacto podrá relacionarse con otro tercero cuando represente a una empresa; esa relación debe modelarse explícitamente, separada de su identidad.

## Implicaciones para el esquema existente

La maestra 0.18 todavía tiene `Clientes` y `Proveedores` con nombre, NIF, dirección, teléfono y correo repetidos, y no tiene `Terceros`. Se requiere una evolución del contrato antes de considerarla definitiva. Las referencias actuales de `Contratos`, `Facturas`, `FacturasProgramadas`, libros IVA, facturas de compra y asociaciones contables apuntan a `IdCliente` o `IdProveedor`; deben conservar su significado funcional al incorporar la identidad común. Las facturas emitidas conservarán sus datos fiscales materializados y no se reescribirán a partir de una ficha viva.

El modelo debe preservar cuentas distintas para las funciones cliente y proveedor de un mismo tercero. Las asociaciones actuales `CuentasClientesEjercicio` y `CuentasProveedoresEjercicio` ya expresan esa diferencia por ejercicio; su clave y relación con las funciones se revisarán junto con el módulo de Cuentas Contables. No se pondrá una cuenta contable única en `Terceros`.

## Importación ERP y ContaSol

MigradorERP debe cargar `Personas`, `DatosContacto` y domicilios como identidades y relaciones, y enlazar después los papeles de cliente y proveedor. Importador ContaSol aporta `F_CLI` y `F_PRO` por ejercicio; las correspondencias entre ambos orígenes y `Terceros` deben quedar registradas. Un NIF coincidente es una señal para revisar la identidad, no una regla suficiente para fusionar registros sin contrastar nombres y procedencia. Tampoco se crearán dos terceros porque una persona tenga dos funciones.

El hilo propio de Terceros definirá el DDL, las reglas de identidad, direcciones y contactos, las APIs y la ficha con pestañas. ActualizadorEsquema incorporará el manifiesto resultante y sus pruebas sobre una maestra nueva; no aplicará silenciosamente una transformación de datos a la BD de pruebas. Las bases originales permanecen intactas.
