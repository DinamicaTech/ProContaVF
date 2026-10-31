<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · terceros

## Clientes

- Dueño funcional: `terceros/roles`
- Maestro de clientes de facturación.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCliente | LONG | no |  | Identificador numérico del cliente, asignado manualmente. Clave primaria. |
| IdContrato | LONG | sí |  | Contrato único asociado al cliente. Referencia opcional a Contratos.IdContrato. |
| CuentaContable | TEXT(15) | sí |  | Cuenta contable del cliente, normalmente del grupo 43. |
| Notas | LONGTEXT | sí |  | Observaciones internas sobre el cliente. |
| FechaAlta | DATETIME | sí | Date() | Fecha de alta del cliente en la aplicación. Valor predeterminado: fecha actual. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. True inicial para clientes existentes; estado operativo nuevo del rol. |

Índices:
- FK_Clientes_Contratos: IdContrato
- FK_Clientes_Terceros: IdTercero
- IX_Clientes_CuentaContable: CuentaContable
- PK_Clientes (clave primaria): IdCliente
- UX_Clientes_IdContrato (único): IdContrato
- UX_Clientes_IdTercero (único): IdTercero

Relaciones:
- FK_Clientes_Contratos: (IdContrato) → Contratos (IdContrato); borrado NO ACTION, actualización NO ACTION
- FK_Clientes_Terceros: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## Contactos

- Dueño funcional: `terceros/contactos`
- Rol de contacto asociado a una identidad Terceros; una identidad puede tener varios roles.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdContacto | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| Cargo | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Departamento | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| NotasContacto | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |

Índices:
- FK_Contactos_Terceros: IdTercero
- PK_Contactos (clave primaria): IdContacto
- UX_Contactos_IdTercero (único): IdTercero

Relaciones:
- FK_Contactos_Terceros: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## ContactosLibresTercero

- Dueño funcional: `terceros/contactos`
- Agrupación libre de datos de contacto de un tercero, sin identidad fiscal ni rol.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdContactoLibre | COUNTER | no |  | Clave autonumérica del grupo libre. |
| IdTercero | LONG | no |  | Titular obligatorio; FK a Terceros.IdTercero. |
| Nombre | TEXT(150) | no |  | Nombre libre de persona o departamento; obligatorio, hasta 150 caracteres. Puede repetirse. |
| Departamento | TEXT(100) | sí |  | Departamento descriptivo opcional, hasta 100 caracteres. |

Índices:
- FK_ContactosLibres_Tercero: IdTercero
- PK_ContactosLibresTercero (clave primaria): IdContactoLibre
- UX_ContactosLibres_Titular (único): IdTercero, IdContactoLibre

Relaciones:
- FK_ContactosLibres_Tercero: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## CuentasClientesEjercicio

- Dueño funcional: `terceros/roles`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaClienteEjercicio | COUNTER | no |  |  |
| IdCliente | LONG | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdCuentaContable | LONG | no |  |  |
| EsPredeterminada | YESNO | no |  |  |

Índices:
- FK_CCE_Cliente: IdCliente
- FK_CCE_Cuenta: IdCuentaContable
- FK_CCE_Ejercicio: IdSociedadEjercicio
- PK_CuentasClientesEjercicio (clave primaria): IdCuentaClienteEjercicio

Relaciones:
- FK_CCE_Cliente: (IdCliente) → Clientes (IdCliente); borrado NO ACTION, actualización NO ACTION
- FK_CCE_Cuenta: (IdCuentaContable) → CuentasContables (IdCuentaContable); borrado NO ACTION, actualización NO ACTION
- FK_CCE_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION

## CuentasProveedoresEjercicio

- Dueño funcional: `terceros/roles`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdCuentaProveedorEjercicio | COUNTER | no |  |  |
| IdProveedor | LONG | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| IdCuentaContable | LONG | no |  |  |
| EsPredeterminada | YESNO | no |  |  |

Índices:
- FK_CPE_Cuenta: IdCuentaContable
- FK_CPE_Ejercicio: IdSociedadEjercicio
- FK_CPE_Proveedor: IdProveedor
- PK_CuentasProveedoresEjercicio (clave primaria): IdCuentaProveedorEjercicio

Relaciones:
- FK_CPE_Cuenta: (IdCuentaContable) → CuentasContables (IdCuentaContable); borrado NO ACTION, actualización NO ACTION
- FK_CPE_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_CPE_Proveedor: (IdProveedor) → Proveedores (IdProveedor); borrado NO ACTION, actualización NO ACTION

## DatosContacto

- Dueño funcional: `terceros/contactos`
- Datos de contacto adicionales; flags de finalidad solo tras decisión explícita del usuario.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDatoContacto | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| Tipo | TEXT(20) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 20, obligatorio True. |
| Etiqueta | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Valor | TEXT(254) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 254, obligatorio True. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |
| EnvioFacturas | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |
| EnviosAdministrativos | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |
| IdContactoLibre | LONG | sí |  | Grupo opcional del mismo tercero; NULL indica dato general. La aplicación valida la titularidad. |

Índices:
- FK_DatosContacto_Grupo: IdContactoLibre
- FK_DatosContacto_Tercero: IdTercero
- IX_DatosContacto_Grupo: IdTercero, IdContactoLibre
- IX_DatosContacto_Tercero: IdTercero
- PK_DatosContacto (clave primaria): IdDatoContacto

Relaciones:
- FK_DatosContacto_Grupo: (IdContactoLibre) → ContactosLibresTercero (IdContactoLibre); borrado NO ACTION, actualización NO ACTION
- FK_DatosContacto_Tercero: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## DomiciliosTercero

- Dueño funcional: `terceros/tercero`
- Domicilios adicionales de una identidad; el domicilio escalar principal permanece en Terceros.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdDomicilioTercero | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| TipoDomicilio | TEXT(30) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 30, obligatorio True. |
| Direccion | TEXT(255) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 255, obligatorio True. |
| CodigoPostal | TEXT(20) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 20, obligatorio False. |
| Poblacion | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Provincia | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Pais | TEXT(3) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 3, obligatorio False. |
| FechaInicio | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |
| FechaFin | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |
| EsPrincipal | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |

Índices:
- FK_Domicilios_Terceros: IdTercero
- IX_Domicilios_IdTercero: IdTercero
- PK_DomiciliosTercero (clave primaria): IdDomicilioTercero

Relaciones:
- FK_Domicilios_Terceros: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## HistorialTerceros

- Dueño funcional: `terceros/tercero`
- Auditoría por campo de modificaciones posteriores; la carga histórica VF no inventa eventos.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdHistorialTercero | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| IdOperacion | TEXT(36) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 36, obligatorio True. |
| FechaHora | DATETIME | no |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio True. |
| Operador | TEXT(255) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 255, obligatorio True. |
| Entidad | TEXT(50) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 50, obligatorio True. |
| IdRegistro | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| Operacion | TEXT(15) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 15, obligatorio True. |
| Campo | TEXT(64) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 64, obligatorio True. |
| ValorAntiguo | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |
| ValorNuevo | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |

Índices:
- IX_HistorialTerceros_Fecha: IdTercero, FechaHora, IdHistorialTercero
- PK_HistorialTerceros (clave primaria): IdHistorialTercero

## MapasOrigenTerceros

- Dueño funcional: `terceros/tercero`
- Correspondencias de procedencia VF por rol e ID. UNION ALL lógico; la huella evita duplicar el mismo origen.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdMapaOrigenTercero | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| OrigenSistema | TEXT(20) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 20, obligatorio True. |
| ContextoOrigen | TEXT(150) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 150, obligatorio True. |
| TipoEntidad | TEXT(40) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 40, obligatorio True. |
| ClaveOrigen | TEXT(150) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 150, obligatorio True. |
| HuellaOrigen | TEXT(64) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 64, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| FechaDecision | DATETIME | no |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio True. |
| CriterioDecision | TEXT(100) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio True. |
| Observaciones | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |

Índices:
- FK_MapasOrigen_Terceros: IdTercero
- IX_MapasOrigenTerceros_IdTercero: IdTercero
- PK_MapasOrigenTerceros (clave primaria): IdMapaOrigenTercero
- UX_MapasOrigenTerceros_Huella (único): HuellaOrigen

Relaciones:
- FK_MapasOrigen_Terceros: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## Perfiles

- Dueño funcional: `terceros/perfiles-notas`
- Catálogo técnico de perfiles de terceros; no concede permisos.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPerfil | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| Nombre | TEXT(100) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio True. |
| Descripcion | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |

Índices:
- PK_Perfiles (clave primaria): IdPerfil
- UX_Perfiles_Nombre (único): Nombre

## PerfilesTerceros

- Dueño funcional: `terceros/perfiles-notas`
- Asignaciones de perfiles a terceros; vigencia propia e historial separado.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdPerfilTercero | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| IdPerfil | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. |
| FechaAsignacion | DATETIME | no |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio True. |
| FechaRetirada | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |

Índices:
- FK_PerfilesTerceros_Perfil: IdPerfil
- FK_PerfilesTerceros_Tercero: IdTercero
- IX_PerfilesTerceros_Perfil: IdPerfil
- PK_PerfilesTerceros (clave primaria): IdPerfilTercero
- UX_PerfilesTerceros_Asignacion (único): IdTercero, IdPerfil

Relaciones:
- FK_PerfilesTerceros_Perfil: (IdPerfil) → Perfiles (IdPerfil); borrado NO ACTION, actualización NO ACTION
- FK_PerfilesTerceros_Tercero: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## Proveedores

- Dueño funcional: `terceros/roles`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdProveedor | COUNTER | no |  |  |
| Activo | YESNO | no |  |  |
| IdTercero | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. FK a Terceros.IdTercero; una identidad por origen sin fusiones. |
| NotasProveedor | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |

Índices:
- FK_Proveedores_Terceros: IdTercero
- PK_Proveedores (clave primaria): IdProveedor
- UX_Proveedores_IdTercero (único): IdTercero

Relaciones:
- FK_Proveedores_Terceros: (IdTercero) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## RelacionesContactos

- Dueño funcional: `terceros/contactos`
- Vínculos explícitos entre contacto y organización, mediante FKs a Contactos y Terceros.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdRelacionContacto | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdContacto | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| IdTerceroOrganizacion | LONG | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| TipoRelacion | TEXT(40) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 40, obligatorio True. |
| FechaInicio | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |
| FechaFin | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. |
| Observaciones | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |

Índices:
- FK_RelContactos_Contacto: IdContacto
- FK_RelContactos_Organizacion: IdTerceroOrganizacion
- IX_RelContactos_Organizacion: IdTerceroOrganizacion
- PK_RelacionesContactos (clave primaria): IdRelacionContacto
- UX_RelContactos_ContactoOrganizacionTipoInicio (único): IdContacto, IdTerceroOrganizacion, TipoRelacion, FechaInicio

Relaciones:
- FK_RelContactos_Contacto: (IdContacto) → Contactos (IdContacto); borrado NO ACTION, actualización NO ACTION
- FK_RelContactos_Organizacion: (IdTerceroOrganizacion) → Terceros (IdTercero); borrado NO ACTION, actualización NO ACTION

## Terceros

- Dueño funcional: `terceros/tercero`
- Identidad común. TipoPersona NULL significa clasificación pendiente; Activo=True indica disponibilidad operativa desde el corte, no estado histórico. NIF no es único.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdTercero | COUNTER | no |  | Contrato TERCEROS_VF_1: tipo ACE 3, longitud 0, obligatorio True. |
| TipoPersona | TEXT(10) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 10, obligatorio False. NULL: clasificación pendiente; valores editables FISICA, JURIDICA, ENTIDAD. |
| Nombre | TEXT(200) | no |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 200, obligatorio True. |
| RazonSocial | TEXT(200) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 200, obligatorio False. |
| NIF | TEXT(30) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 30, obligatorio False. |
| Telefono | TEXT(50) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 50, obligatorio False. |
| Email | TEXT(254) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 254, obligatorio False. |
| Direccion | TEXT(255) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 255, obligatorio False. |
| CodigoPostal | TEXT(20) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 20, obligatorio False. |
| Poblacion | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Provincia | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| Pais | TEXT(3) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 3, obligatorio False. |
| Notas | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |
| Activo | YESNO | no |  | Contrato TERCEROS_VF_1: tipo ACE 11, longitud 0, obligatorio True. True inicial significa disponibilidad operativa, no actividad histórica. |
| FechaAlta | DATETIME | sí |  | Contrato TERCEROS_VF_1: tipo ACE 7, longitud 0, obligatorio False. NULL al migrar: la fecha antigua del cliente sigue en Clientes.FechaAlta. |
| NombrePersona | TEXT(150) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 150, obligatorio False. |
| PrimerApellido | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| SegundoApellido | TEXT(100) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 100, obligatorio False. |
| NombreComercial | TEXT(150) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 150, obligatorio False. |
| TipoIdentificacion | TEXT(30) | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 30, obligatorio False. |
| NotasInternas | LONGTEXT | sí |  | Contrato TERCEROS_VF_1: tipo ACE 130, longitud 0, obligatorio False. |

Índices:
- IX_Terceros_NIF: NIF
- IX_Terceros_Nombre: Nombre
- PK_Terceros (clave primaria): IdTercero

