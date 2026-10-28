# DAT-TERCEROS — responsabilidades y contrato público

## 1. Identidad y estado comprobado

| Dato | Valor |
| --- | --- |
| Clave | DAT-TERCEROS |
| Módulo | Identidad de terceros y funciones Cliente, Proveedor y Contacto |
| Hilo responsable | Modelo y gestión de Terceros · `01a0ecfd-9332-7d73-ab9d-d79492aa73bf` |
| Responsable funcional | DAT-TERCEROS mantiene las reglas de identidad y funciones descritas aquí; el usuario valida los requisitos. No consta otra persona designada formalmente. |
| Ruta comprobada | `C:/Docs/VeriFactu/ProConta.Terceros` |
| Rama comprobada | `feature/terceros` |
| Commit de código inspeccionado | `01a3dc93a986bff1b42bab376a5f7ab9f853fadb` |
| Incremento de código anterior | `0c37a2618947149e4bd2fe04edb6071394b501eb` |
| Fecha de revisión | 30/09/2026, Europe/Madrid |
| Estado Git inicial | Sin cambios en archivos versionados ni archivos nuevos no ignorados; HEAD asociado a la rama indicada. |
| Estado de normalización | Ficha inicial publicada. Implementación parcial en rama; integración conjunta y normalización pendientes. |

Esta ficha describe lo comprobado en ese commit. No certifica despliegue en una
maestra, incorporación en otras ramas ni conformidad del conjunto de módulos.
Esta revisión incorpora el incremento de Perfiles y el alojamiento del futuro
Mayor; no acredita implementación del módulo contable.

Referencias de diseño, sin duplicar su contenido:

- [Contrato de identidad y adopción](TERCEROS_CONTRATO.md): modelo, procedencia, migración e incrementos.
- [DDL de identidad y funciones](../database/migrations/20260929_001_terceros_maestra_nueva.sql).
- [DDL de instantánea fiscal de ventas](../database/migrations/20260929_002_facturas_instantanea_terceros.sql).
- [DDL de historial](../database/migrations/20260930_001_historial_terceros.sql).
- [DDL de perfiles](../database/migrations/20260930_002_perfiles_terceros.sql).
- [Revisión de la maqueta](REVISION_MAQUETA_TERCEROS.md).
- [Requisitos de la siguiente maqueta, 01/10/2026](TERCEROS_CONTRATO.md#requisitos-de-la-siguiente-maqueta-01102026): domicilio asistido, Roles/cuentas, Ventas con dos listados y Notas. Actualización exclusivamente documental posterior al código inspeccionado; implementación pendiente y nueva maqueta aún no recibida. No modifica la evidencia de pruebas de §7.

## 2. Responsabilidad y exclusiones

DAT-TERCEROS mantiene la identidad común, sus funciones opcionales, domicilios
adicionales, relaciones entre contactos y organizaciones, catálogo y asignación
de perfiles, correspondencias de origen y auditoría de las operaciones implementadas. Conserva separadas las
claves IdTercero, IdCliente, IdProveedor e IdContacto. No fusiona por NIF.

La ficha de terceros presenta esa información y aloja los listados de facturas
que suministren sus módulos propietarios y el futuro Mayor contable. La ubicación del código en los
proyectos de Facturación es la organización actual del repositorio; no atribuye
a Facturación la propiedad funcional de la identidad compartida.

Quedan fuera de su autoridad:

- Autenticar, otorgar permisos, crear sesiones o decidir el contexto autorizado: corresponde a SEG-ACCESO y a los contratos de contexto que se acuerden.
- Orquestar toda la aplicación: UI-PRINCIPAL presenta y conecta pantallas; la ficha no establece el contexto global.
- Definir la infraestructura visual compartida: corresponde a UI-BASE; esta implementación usa WinForms/Krypton, sin constituir por ello un estándar visual ya normalizado.
- Emitir, rectificar, imprimir o enviar facturas: corresponden a FacturasVenta/FacturasCompra y sus servicios de documentos y correo.
- Asignar cuentas por sociedad/ejercicio, contabilizar o determinar reglas fiscales: corresponden a los módulos contables y de facturación propietarios.
- Ejecutar migraciones sobre bases reales: ActualizadorEsquema despliega estructura; MigradorERP/ContaSol resuelven la procedencia y carga con sus contratos.
- Ser propietario de cualquier tabla que un mantenimiento genérico pueda mostrar. CFG-MANT no adquiere propiedad de negocio por presentarla.

## 3. Capacidades ofrecidas

### Condiciones comunes de consumo

La API actual es `ServicioTerceros`, sobre `IRepositorioTerceros`; los tipos son
DTO y servicios, no la jerarquía CTerceros/CClientes/CProveedores/CContactos
acordada como destino. Sus declaraciones están en
[ServicioTerceros.vb](../src/ProConta.Facturacion.Aplicacion/Terceros/ServicioTerceros.vb).
La implementación ACE está en
[RepositorioTercerosAccess.vb](../src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioTercerosAccess.vb).

**Contexto de datos:** una `FabricaConexionesAccess` configurada con la ruta de
la maestra destino. La identidad y las funciones no reciben IdSociedad ni
IdSociedadEjercicio; sus consultas abarcan la base configurada. No debe
interpretarse esto como autorización para todas las sociedades.

**Permisos actuales (P0):** ninguna de las capacidades de datos siguientes
consulta permisos de SEG-ACCESO. Su integración deberá autorizar lecturas,
escrituras y acceso al historial. No existen códigos de permiso DAT-TERCEROS
verificables que puedan prometerse aquí. El callback de operador identifica
el origen del cambio, pero no autentica ni autoriza al llamante.

**Errores comunes (E0):** las validaciones del servicio usan
`ArgumentNullException`, `ArgumentException` o `ArgumentOutOfRangeException`.
Ausencias incompatibles con una escritura y conflictos funcionales pueden
producir `InvalidOperationException`; los errores de apertura, esquema,
restricciones y ejecución ACE se propagan como errores de persistencia, sin
un catálogo estable de códigos propio. Los formularios los presentan mediante
`ServicioMensajesAplicacion`. No hay un resultado genérico que convierta todos
los fallos en False o Nothing.

### 3.1. Consultar identidad — implementada en rama

- **Interfaz y métodos:** `ServicioTerceros.Buscar(texto)`, `Obtener(idTercero)` y `ObtenerPorCliente(idCliente)`; equivalentes en `IRepositorioTerceros`, en el archivo enlazado arriba.
- **Entradas/salidas:** texto parcial → `IReadOnlyList(Of Tercero)`; clave positiva → `Tercero` o Nothing. Buscar con texto vacío devuelve todas las fichas, sin paginación. El DTO incluye los identificadores de las funciones existentes.
- **Errores/permisos/contexto:** E0 y P0; base destino con el esquema completo de identidades y funciones. Las claves de búsqueda individual deben ser positivas.
- **Efectos:** lectura desconectada. No filtra automáticamente por Activo ni considera el NIF una clave única. No existe actualmente un `ObtenerPorProveedor` o `ObtenerPorContacto` público equivalente.

### 3.2. Crear o modificar identidad — implementada en rama

- **Interfaz y métodos:** `ServicioTerceros.Crear(tercero)` y `Actualizar(tercero)`; `IRepositorioTerceros.Crear/Actualizar`.
- **Entradas/salidas:** DTO `Tercero` → DTO guardado; Crear exige IdTercero=0 y devuelve la clave asignada. Actualizar exige una clave positiva existente.
- **Errores/permisos/contexto:** E0, P0, esquema de Terceros y HistorialTerceros, callback de operador no vacío y de hasta 255 caracteres. TipoPersona admite FISICA/JURIDICA/ENTIDAD. La validación actual exige Nombre para los tres tipos; la diferenciación completa de nombres/apellidos y denominación legal sigue pendiente.
- **Efectos:** escribe Terceros y su historial en una transacción por llamada. No activa funciones, no cambia cuentas ni reescribe facturas históricas. Un fallo de auditoría revierte la modificación.

### 3.3. Mantener funciones — implementada en rama

- **Interfaz y métodos:** `ObtenerCliente(idTercero)`, `ObtenerProveedor(idTercero)`, `ObtenerContacto(idTercero)` y `GuardarCliente/GuardarProveedor/GuardarContacto(idTercero, datos)` en `ServicioTerceros` e `IRepositorioTerceros`.
- **Entradas/salidas:** las lecturas reciben **IdTercero** y devuelven `DatosFuncionCliente`, `DatosFuncionProveedor` o `DatosFuncionContacto`, o Nothing. Guardar recibe esos datos y devuelve el IdCliente/IdProveedor/IdContacto funcional, conservando el existente.
- **Errores/permisos/contexto:** E0 y P0; tercero existente, DTO no nulo, claves positivas, tablas de funciones e historial, operador válido.
- **Efectos:** alta o modificación de una función con su historial, dentro de su propia transacción. No duplica Terceros. Los DTO admiten Activo; no hay API de borrado físico. El campo IdContrato de Cliente referencia un dato cuyo negocio pertenece a Contratos.
- **Límite:** crear identidad y luego función son dos llamadas y dos transacciones. La API actual no ofrece una unidad de trabajo pública para hacerlas atómicas en conjunto.

### 3.4. Domicilios adicionales — implementada en rama, alcance limitado

- **Interfaz y métodos:** `ServicioTerceros.ListarDomicilios(idTercero)` y `AgregarDomicilio(domicilio)`; equivalentes en `IRepositorioTerceros`.
- **Entradas/salidas:** clave titular → lista de `DomicilioTercero`; DTO nuevo con IdTercero → DTO con IdDomicilioTercero generado.
- **Errores/permisos/contexto:** E0 y P0; dirección y finalidad obligatorias, identificador nuevo a cero, FechaFin no anterior a FechaInicio cuando ambas existen; tabla e historial disponibles.
- **Efectos:** lectura o alta auditada en transacción. No modifica el domicilio principal. No existen aún métodos públicos para editar, eliminar o cerrar vigencia de un domicilio existente.

### 3.5. Relacionar contactos y organizaciones — implementada en rama, alcance limitado

- **Interfaz y método:** `ServicioTerceros.VincularContacto(idContacto, idTerceroOrganizacion, tipoRelacion)`; equivalente en `IRepositorioTerceros`.
- **Entradas/salidas:** IdContacto funcional, IdTercero de la organización y texto no vacío; no devuelve valor.
- **Errores/permisos/contexto:** E0 y P0. Deben existir ambas identidades y ser distintas; la organización debe ser JURIDICA o ENTIDAD.
- **Efectos:** inserta relación e historial para ambos terceros en una transacción. Una relación activa ya existente con el mismo tipo no se vuelve a insertar. No se ofrece todavía consulta pública de relaciones ni operación para terminar su vigencia.

### 3.6. Historial por campo — implementada en rama

- **Interfaz y método:** `ServicioTerceros.ListarHistorial(idTercero)` / `IRepositorioTerceros.ListarHistorial`; persistencia en [RepositorioTercerosAccess.Historial.vb](../src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioTercerosAccess.Historial.vb).
- **Entradas/salidas:** IdTercero positivo → `IReadOnlyList(Of CambioTercero)`, descendente por fecha y clave. Campos del resultado en [CambioTercero.vb](../src/ProConta.Facturacion.Aplicacion/Terceros/CambioTercero.vb).
- **Errores/permisos/contexto:** E0 y P0; historial existente. La lectura no solicita operador. La escritura interna exige `RepositorioTercerosAccess.New(fabrica, obtenerOperador As Func(Of String))`.
- **Efectos:** consulta sin cambios. Las operaciones de 3.2 a 3.5 y los perfiles de 3.11 generan diferencias reales y omiten valores iguales; preservan NULL y textos extensos. No existe método público para editar o borrar el historial.
- **Tiempo y alcance:** FechaHora se almacena como UTC; `CambioTercero.FechaHora` convierte a la zona local del equipo. No es el instante de autenticación. El IdOperacion agrupa los campos de una captura de registro, no todas las llamadas de un flujo. No audita SQL externo, MapasOrigenTerceros ni el futuro DatosContacto. No es una protección del archivo Access frente a modificaciones directas.

### 3.7. Correspondencias de origen — implementada en rama

- **Interfaz y métodos:** [ServicioCorrespondenciasTerceros](../src/ProConta.Facturacion.Aplicacion/Terceros/ServicioCorrespondenciasTerceros.vb): `Buscar(origenSistema, contextoOrigen, tipoEntidad, claveOrigen)`, `Registrar(correspondencia)` y `CalcularHuella(...)`. `IRepositorioCorrespondenciasTerceros` ofrece `Buscar(huellaOrigen)` y `Registrar(correspondencia)`; [repositorio ACE](../src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioCorrespondenciasTercerosAccess.vb).
- **Entradas/salidas:** tupla externa → `CorrespondenciaTercero` o Nothing; tupla, IdTercero y criterio → mapa nuevo o existente compatible; CalcularHuella devuelve SHA-256 hexadecimal. La definición de la tupla y su serialización está en el contrato de adopción enlazado en §1.
- **Errores/permisos/contexto:** E0 y P0; componentes no vacíos, IdTercero positivo existente y criterio obligatorio. Una tupla ya asignada a otro tercero o una huella cuya tupla discrepa producen conflicto explícito. La concurrencia entre inserciones puede producir error del índice único ACE; no hay resolución automática de ese conflicto.
- **Efectos:** Registrar inserta únicamente MapasOrigenTerceros; no crea ni fusiona identidades. FechaDecision utiliza hora local `Date.Now`, a diferencia del UTC del historial. No hay auditoría de operador en este repositorio ni operación pública para reasignar un mapa. No comparte transacción pública con el alta de identidad/función.

### 3.8. Ficha, historial y facturas alojadas — implementada en rama; compras pendiente

- **Interfaz real:** [VistaTerceros](../src/ProConta.Facturacion.App/Terceros/VistaTerceros.vb), `New(servicio, Optional crearFacturasVenta As Func(Of Long, Control), Optional crearFacturasCompra As Func(Of Long, Control), Optional crearMayor As Func(Of SolicitudMayorTercero, Control), Optional obtenerContextoMayor As Func(Of ContextoSociedadEjercicio))`, `SeleccionarTercero(idTercero)` y [ActualizarContextoFacturas()](../src/ProConta.Facturacion.App/Terceros/VistaTerceros.HistorialFacturas.vb). Todos los argumentos opcionales tienen valor predeterminado Nothing; el proveedor de contexto es obligatorio si se aporta fábrica de Mayor.
- **Entradas/salidas:** servicio y fábricas opcionales → control WinForms/Krypton. La fábrica de ventas recibe IdCliente; la de compras, IdProveedor. No son interfaces de dominio: retornan controles y pertenecen a la capa UI.
- **Errores/permisos/contexto:** E0 presentado por el servicio central de mensajes; seleccionar un tercero inexistente falla explícitamente. Faltan comprobaciones de autorización integradas: alojar un listado no otorga permisos. El llamante proporciona los servicios y el contexto sociedad/ejercicio autorizado.
- **Efectos:** la ficha consulta y permite las escrituras de 3.2 a 3.5 y 3.11. Los listados se crean al entrar en la pestaña y se liberan con ella. Su menú y las escrituras de facturas pertenecen al módulo suministrador. ActualizarContextoFacturas reconstruye también Contabilidad; su nombre se conserva por compatibilidad.
- **Ventas implementadas:** [FrmPrincipal.CrearListaVentasTercero](../src/ProConta.Facturacion.App/FrmPrincipal.vb) es un adaptador **privado**, no una nueva API pública. Instancia [VistaFacturas](../src/ProConta.Facturacion.App/Facturas/VistaFacturas.vb), enlaza la navegación al editor y usa [ServicioConsultaFacturacion.New(repositorio, idClienteFijo)](../src/ProConta.Facturacion.Aplicacion/ConsultasFacturacion/ServicioConsultaFacturacion.vb). El filtro permanente sobrevive a limpiar filtros y a filtros de resultados. Contexto y cabecera muestran sociedad/ejercicio; no es una lista de todos los ejercicios.
- **Compras pendiente:** existe el argumento de fábrica y la pestaña, pero el ensamblado actual no suministra el listado general de compras ni su menú/editor. Con fábrica ausente se muestra la indisponibilidad; no debe presentarse como integración operativa.

### 3.9. Compatibilidad y detección de esquema — implementadas en rama, no certifican adopción

- **Interfaz técnica:** [EsquemaTercerosAccess.EstaDisponible(conexion)](../src/ProConta.Facturacion.Persistencia.Access/Terceros/EsquemaTercerosAccess.vb) y `TieneCampoFactura(conexion, campo)` reciben una conexión OleDb abierta y devuelven Boolean, sin cambios ni control de permisos (P0). Los errores de metadatos/conexión se propagan.
- **Límite:** EstaDisponible comprueba únicamente la existencia física de Terceros. No valida todas las tablas, HistorialTerceros, índices, FKs o versión del contrato; no es un comprobador de despliegue completo.
- **Consumidores adaptados:** el inventario de rutas de lectura y de instantánea fiscal está en [Consumidores y estado del incremento](TERCEROS_CONTRATO.md#consumidores-que-deben-cambiar-con-la-adopción). Son adaptaciones de repositorios de otros dominios, no servicios nuevos de DAT-TERCEROS para emitir documentos. Sus errores, autorizaciones, escrituras y contexto deben conservar el contrato del dominio propietario.

### 3.10. Capacidades acordadas sin API operativa

| Capacidad | Estado | Contrato previsto y límites |
| --- | --- | --- |
| Fachada CTerceros concreta y clases derivadas | Propuesta acordada; implementación pendiente | La clase actual `Tercero` es NotInheritable y se usa con servicios. No hay constructores/métodos de esas cuatro clases que un consumidor pueda llamar. Deben definirse su alcance transaccional y convivencia con DTO/repositorios. |
| qClientes, qProveedores y qContactos | Propuesta acordada; publicación SQL pendiente | Proyecciones de lectura con una fila por función y claves estables, sin multiplicar filas por contactos. No hay QueryDefs desplegadas verificadas. Entradas/salidas SQL concretas, errores de versión y control de acceso pendientes; no habilitan escrituras. |
| DatosContacto y selección de destinatarios por uso | Propuesta acordada; implementación pendiente | Flags y alternativa de email único descritos en [decisiones del modelo](TERCEROS_CONTRATO.md#decisiones-de-modelo-posteriores-al-primer-incremento). `ObtenerEmailsDestino` es un nombre propuesto en la conversación, no un método existente. No debe atribuirse esta regla al correo actual. |
| Nombre/apellidos de persona física y Razón social de organización | Requisitos validados el 01/10; implementación parcial | Persona física: Nombre obligatorio, ambos apellidos opcionales. Organización: Razón social obligatoria y nombre comercial opcional. Los tres valores TipoPersona existen; los campos separados de apellidos, NombreComercial y la proyección NombreCompleto todavía no aparecen en el DTO/DDL actual. La validación vigente aún exige Nombre para los tres tipos. |

Estas propuestas requieren base/contexto y autorización definidos con sus
consumidores antes de ofrecerse como capacidades públicas implementadas.

La [validación funcional del 01/10](TERCEROS_CONTRATO.md#validación-funcional-de-contactos-e-identificación-01102026)
confirma envío a todos los emails activos marcados para la finalidad, alternativa
de email único activo sin cambiar flags, y registro de incidencia sin abortar
el proceso cuando hay varias direcciones sin selección. Los datos desactivados
no participan en comunicaciones automáticas. Esta actualización es documental;
no añade API ni cambia los resultados de verificación del código de §7.

### 3.11. Catálogo y asignaciones de perfiles — implementados en rama

- **Interfaz:** [ServicioTerceros.Perfiles](../src/ProConta.Facturacion.Aplicacion/Terceros/ServicioTerceros.Perfiles.vb); persistencia en [RepositorioTercerosAccess.Perfiles](../src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioTercerosAccess.Perfiles.vb).
- **Lecturas:** `ListarPerfiles()` → catálogo completo `IReadOnlyList(Of Perfil)`; `ListarPerfilesTercero(idTercero)` → asignaciones vigentes, incluidas las de perfiles inactivos en el catálogo. DTO: IdPerfil, Nombre, Descripcion y Activo; este último se refiere al catálogo.
- **Catálogo:** `GuardarPerfil(perfil)` → perfil con clave estable; cero para alta, positiva existente para edición. Nombre no vacío de hasta 100 caracteres, único tras recortar extremos. Desactivar el catálogo impide nuevas asignaciones y conserva las existentes. Se auditan diferencias en las fichas que lo tienen asignado; no hay historial global para perfiles sin titulares.
- **Asignación:** `AsignarPerfiles(idTercero, idsPerfiles)` y `QuitarPerfiles(...)` no devuelven valor. Eliminan duplicados del lote, exigen claves positivas y omiten una selección vacía. Cada lote se confirma junto con su historial o se revierte completo. La retirada es lógica; reasignar conserva IdPerfilTercero. Repetir el estado vigente no crea entradas.
- **Errores/permisos/contexto:** E0 y P0, operador válido, identidad y perfiles existentes; asignar un perfil inactivo falla salvo que ya estuviera asignado. Tablas Perfiles, PerfilesTerceros e HistorialTerceros requeridas. Clasificación global de la maestra, independiente de sociedad/ejercicio; no activa funciones comerciales ni concede permisos.
- **UI:** [VistaPerfilesTercero](../src/ProConta.Facturacion.App/Terceros/VistaPerfilesTercero.vb) ofrece arrastre interno, doble clic, botones, selección múltiple, Intro/Supr y filtros en ambas listas; [FrmCatalogoPerfiles](../src/ProConta.Facturacion.App/Terceros/FrmCatalogoPerfiles.vb) mantiene el catálogo. Cada movimiento se guarda inmediatamente y así se indica; Guardar identidad no engloba ni revierte esas operaciones.

### 3.12. Contabilidad — alojamiento implementado, Mayor pendiente

- **Solicitud:** [SolicitudMayorTercero](../src/ProConta.Facturacion.Aplicacion/Terceros/SolicitudMayorTercero.vb), inmutable, con IdTercero e IdSociedadEjercicio positivos, e IdCliente/IdProveedor opcionales positivos. Las claves funcionales no se sustituyen por IdTercero.
- **Entrada UI:** fábrica `crearMayor` del constructor de VistaTerceros; recibe la solicitud al entrar en la pestaña y devuelve un `Control` nuevo e independiente. `obtenerContextoMayor` debe proporcionar el contexto vigente; Nothing o un contexto sin clave válida impiden la carga. Una fábrica que devuelve Nothing produce error explícito.
- **Ciclo de vida:** la pestaña existe para toda identidad guardada. Se carga bajo demanda y libera el control al cambiar de tercero, contexto o cerrar. El propietario del Mayor gestiona refresco, cuentas, consultas, filtros, saldos, permisos, menú e interacción. El anfitrión no calcula apuntes ni reconstruye el Mayor a partir de facturas.
- **Estado real:** la composición proporciona el contexto pero todavía no aporta fábrica contable; la pestaña muestra un mensaje informativo. No hay módulo/hilo del Mayor creado por este incremento ni contrato acordado con ese futuro propietario. El punto de extensión es provisional de esta rama; queda por validar con Contabilidad y SEG-ACCESO.

## 4. Datos propios, consumidos y autoridad de escritura

Los campos e índices se consultan en los DDL enlazados en §1. Esta tabla expresa
propiedad funcional y rutas de escritura; no concede acceso a una base real.

| Datos | Dueño funcional | Quién escribe / cómo se consumen |
| --- | --- | --- |
| Terceros | DAT-TERCEROS | RepositorioTercerosAccess, a través de ServicioTerceros. Consumidores consultan identidades; NIF no es único. |
| Clientes, Proveedores, Contactos | DAT-TERCEROS, en cuanto a funciones de identidad | GuardarCliente/Proveedor/Contacto. Facturación, compras y contabilidad conservan los identificadores funcionales. IdContrato no traslada el negocio de Contratos a DAT-TERCEROS. |
| DomiciliosTercero, RelacionesContactos | DAT-TERCEROS | Operaciones implementadas del repositorio; no existe mantenimiento genérico completo de vigencias. |
| Perfiles, PerfilesTerceros | DAT-TERCEROS | ServicioTerceros mantiene catálogo y asignaciones. Lecturas SQL de asignaciones vigentes filtran PerfilesTerceros.Activo=True; Perfiles.Activo=False no retira una asignación previa. No escribir por SQL externo. |
| HistorialTerceros | DAT-TERCEROS | Escritura interna del repositorio junto con la mutación; la UI solo consulta. No hay borrado en cascada definido. |
| MapasOrigenTerceros | DAT-TERCEROS para la correspondencia de identidad; importador/revisor para decidir la equivalencia | ServicioCorrespondenciasTerceros y su repositorio insertan. MigradorERP/ContaSol proporcionan procedencia y criterio. No es una autorización para fusionar por coincidencia de NIF. |
| DatosContacto / catálogo de tipos | DAT-TERCEROS como destino funcional acordado | Mantenimiento destino pendiente. La tabla existente en ProcontaEstructura es referencia; ninguna operación actual de esta API la modifica. |
| Facturas e instantáneas fiscales | FacturasVenta | Sus repositorios crean/materializan datos fiscales; DAT-TERCEROS solo aporta identidad y aloja su vista. El cambio de ficha no debe modificar una factura emitida. |
| FacturasCompra | FacturasCompra | El módulo propietario debe proporcionar vista, navegación y acciones; integración pendiente. |
| Contratos y programaciones | Contratos / Facturación propietaria | DAT-TERCEROS no los mantiene; se conservan sus referencias a IdCliente y se leen los datos comunes mediante los consumidores adaptados. |
| CuentasClientesEjercicio, CuentasProveedoresEjercicio, CuentasContables | Cuentas Contables / dominio contable | No se escriben desde ServicioTerceros. Asociación por función e IdSociedadEjercicio; pendiente cerrar la interfaz pública consumidora correspondiente. |
| Apuntes, saldos y vista de Mayor | Futuro módulo contable propietario | Terceros solo aloja el control; no consulta ni escribe esos datos. Integración contable pendiente. |
| Sociedad y ejercicio activos | Autoridad a cerrar con SEG-ACCESO; presentación/orquestación en UI-PRINCIPAL | La composición actual entrega el contexto a VistaFacturas y reconstruye los listados alojados al cambiarlo. DAT-TERCEROS no autoriza ni persiste una selección global. |
| ProcontaEstructura.accdb y otras bases origen | Sistemas ERP/Contabilidad de origen | Referencia consolidada de solo lectura por indicación del usuario. No se despliega este esquema sobre ella. |

CFG-MANT puede aportar mecanismos genéricos de presentación; la autorización,
validación y auditoría de una edición de estos datos deben respetar al dueño
funcional. No está implementado ni acordado un mecanismo que permita a CFG-MANT
escribir por SQL y conservar automáticamente HistorialTerceros. Es un punto de
integración abierto, no una cesión de propiedad. CFG-GLOBAL puede proporcionar
configuración/rutas según su contrato; DAT-TERCEROS recibe la ruta, no define
su ubicación local ni convierte esa ruta en parámetro de negocio.

## 5. Dependencias y puntos de integración

| Dependencia | Punto actual | Integración por cerrar |
| --- | --- | --- |
| Persistencia Access | FabricaConexionesAccess, ACE/OleDb y parámetros posicionales; .NET 10 x64 | Validación completa de versión del esquema y gestión del despliegue. |
| ActualizadorEsquema | Cuatro DDL enlazados en §1 y contrato de adopción | Incorporar/verificar el manifiesto de maestra destino; los scripts no migran por sí solos una base existente. No se comprueba en esta revisión el estado de su rama. |
| MigradorERP / ContaSol | Mapas de origen y claves funcionales estables | Reconciliación e importación completas. En esta rama, el importador ContaSol antiguo rechaza el esquema Terceros antes de escribir; véase [ImportadorContaSolAccess](../src/ProConta.Facturacion.Persistencia.Access/ImportacionContaSol/ImportadorContaSolAccess.vb). |
| SEG-ACCESO | Callback `obtenerOperador`; actualmente [composición con Windows:dominio\usuario](../src/ProConta.Facturacion.App/ComposicionNucleoFacturacion.vb) | Operador autenticado, permisos y contexto autorizado; sin afirmar acuerdo sobre interfaces aún no integradas. No se consulta el instante de autenticación ni se detecta una sesión cerrada. |
| UI-PRINCIPAL | Construcción de VistaTerceros, fábrica de ventas, navegación y aviso de cambio de contexto | Autoridad de Seguridad separada de presentación/orquestación. Hoy la ruta/identidad Windows permiten operar sin una sesión de ProConta integrada; debe revisarse al conectar SEG-ACCESO. |
| UI-BASE | Controles Krypton y servicio central de mensajes existentes | Adaptación a contratos visuales compartidos cuando estén definidos; esta ficha no aprueba propuestas visuales de otros hilos. |
| FacturasVenta / documentos / correo | Instancia independiente de VistaFacturas y servicios habituales | Verificación conjunta de permisos y regresión funcional en la aplicación integrada. |
| FacturasCompra | Fábrica opcional `Func(Of Long, Control)` | Listado real con filtro fijo, menú, editor y refresco definido por su módulo. |
| Cuentas Contables | IdCliente/IdProveedor + IdSociedadEjercicio | Servicio de cuentas por función/ejercicio; no colocar una cuenta única en Terceros. |
| Futuro Mayor contable | Fábrica opcional de control y SolicitudMayorTercero (§3.12) | Acordar y aportar la vista real, resolución de cuentas y permisos. No se ha creado ese hilo ni una implementación de Mayor en DAT-TERCEROS. |
| CFG-MANT / CFG-GLOBAL | Sin integración específica comprobada | Evitar escrituras que eludan auditoría y separar provisión de ruta de propiedad de parámetros funcionales. |

## 6. Ejemplo mínimo de consumo real

Ejemplo con interfaces existentes, para una maestra destino de prueba ya
preparada. `rutaMaestra` e `idTercero` son entradas del llamante. Es lectura;
este documento no ejecuta el ejemplo ni autoriza usar una base real.

```vb
Imports ProConta.Facturacion.Aplicacion.Terceros
Imports ProConta.Facturacion.Persistencia.Access
Imports ProConta.Facturacion.Persistencia.Access.Terceros

Dim fabrica As New FabricaConexionesAccess(rutaMaestra)
Dim repositorio As New RepositorioTercerosAccess(
    fabrica, Function() "Proceso:consulta-de-ejemplo")
Dim servicio As New ServicioTerceros(repositorio)

Dim tercero As Tercero = servicio.Obtener(idTercero)
If tercero IsNot Nothing Then
    Dim cliente As DatosFuncionCliente = servicio.ObtenerCliente(tercero.IdTercero)
    Dim cambios As IReadOnlyList(Of CambioTercero) = servicio.ListarHistorial(tercero.IdTercero)
End If
```

El identificador de proceso del ejemplo demuestra la firma del callback, no
reemplaza una sesión autenticada. Para un guardado interactivo deberá
proporcionarse el operador real y verificarse el permiso antes de invocar la
API. Un ejemplo ejecutable de altas y cambios sobre Access temporal está en
[RepositorioTercerosAccessTests](../tests/ProConta.Facturacion.Tests/RepositorioTercerosAccessTests.vb).

## 7. Evidencia disponible y límites de integración

Verificación del incremento que culminó en `01a3dc9`, realizada el 30/09/2026.
La última edición de código posterior a ejecutar las pruebas añadió únicamente
comentarios XML. Los registros indicados son artefactos locales ignorados:

| Evidencia | Resultado y alcance |
| --- | --- |
| Compilación completa `dotnet build ProConta.Facturacion.sln --property:Platform=x64 --no-restore -v:q` | 0 errores, 30 advertencias. El registro local ignorado `terceros-build.log` conserva este resultado. No equivale a validación visual. |
| Pruebas relacionadas de consulta, búsqueda e integración temporal de Terceros | 15 correctas, 0 errores, 0 omitidas; filtro debajo. Resultado en `terceros-test.log`. |
| [ConservaUnTerceroConTresFuncionesSinFusionarPorNif](../tests/ProConta.Facturacion.Tests/RepositorioTercerosAccessTests.vb) | DDL sobre base temporal, tres funciones, NIF repetido, correspondencias, domicilios, relaciones, ENTIDAD, historial por campo, cambio de operador, NULL/texto largo, ausencia de entradas sin cambio y reversión por fallo de auditoría. Es una prueba de integración con múltiples comprobaciones, no una prueba por cada afirmación. |
| [PerfilesConservanAsignacionesAuditoriaYAtomicidad](../tests/ProConta.Facturacion.Tests/RepositorioTercerosAccessTests.vb) | DDL en una maestra temporal nueva; duplicados, retirada y reasignación con la misma clave, desactivación sin pérdida, nombre histórico, edición de catálogo, operador obligatorio y reversión de lote/catalogación por fallo de auditoría. |
| [MayorRecibeClavesYDescartaVistaAlCambiarContexto](../tests/ProConta.Facturacion.Tests/RepositorioTercerosAccessTests.vb) | Control sustituto del Mayor en STA, sin ventanas visibles: carga diferida, claves funcionales, cambio de contexto/tercero y liberación. No prueba una consulta contable real. |
| [FichaClienteConservaLimiteTrasLimpiarFiltrosYMostrarFacturas](../tests/ProConta.Facturacion.Tests/ServicioConsultaFacturacionTests.vb) | Verifica los predicados entregados al repositorio y la independencia de filtros. No ejecuta menús, impresión, correo ni navegación visual. |
| Pruebas anteriores de facturas manuales | Tres bloqueadas al preparar su base por una migración `20260822_003_MOTOR_SEGURIDAD` desconocida para esta rama. No constituyen tres fallos demostrados de la lógica de Terceros ni una validación satisfactoria de facturación completa. |

Filtro de la ejecución de 15 pruebas, con `dotnet test`, Platform=x64,
`--no-build --no-restore` sobre el proyecto ProConta.Facturacion.Tests:

```text
(FullyQualifiedName~ServicioConsultaFacturacionTests&TestCategory!=IntegracionLocal)|FullyQualifiedName~RepositorioTercerosAccessTests|FullyQualifiedName~ServicioBusquedaClientesTests
```

Se revisó la maqueta HTML en navegador; no se ha realizado una prueba manual
del arrastre WinForms ni de su disposición con diferentes escalados. No se ha
acreditado aquí integración en la rama principal, despliegue del
manifiesto completo, migración de una base real, menú general de compras,
Mayor operativo, sesión/permisos de Seguridad, auditoría de escritores externos ni pruebas de
concurrencia de todo el flujo. La documentación no elimina esos límites.

## 8. Decisiones abiertas, solapamientos y próximos pasos

1. **Transacciones de identidad y funciones.** La regla 1 de [Reglas de aplicación](TERCEROS_CONTRATO.md#reglas-de-aplicación) describe el alta conjunta en una transacción. El código actual confirma cada llamada por separado. Debe cerrarse la unidad de trabajo de la fachada; esta ficha registra la discrepancia y no inventa una garantía de atomicidad global.
2. **Autorización y sesión.** Conectar el operador y permisos reales de SEG-ACCESO. El callback solo valida un texto; no distingue usuario autenticado, sesión expirada o cambio autorizado de empresa. Definir con UI-PRINCIPAL quién suministra y quién valida cada contexto, sin convertir la UI en autoridad de permisos.
3. **Mantenimiento genérico.** Acordar con CFG-MANT una ruta que invoque la validación/escritura del dueño funcional. La edición SQL directa eludiría este historial. No existe todavía consenso técnico documentado sobre esa integración.
4. **Fachada e identidad.** Materializar el diseño de clases acordado y completar nombre/apellidos/denominación. Mantener la posibilidad de varias funciones simultáneas; aclarar que tipo de persona y función comercial son ejes distintos.
5. **Contrato SQL y DatosContacto.** Publicar las proyecciones y su versionado, incorporar la colección y flags acordados y la selección de email único por uso, y ampliar la auditoría. Mantener una fila por función en las consultas generales.
6. **Relaciones y domicilios.** Decidir APIs de lectura, edición y cierre de vigencia que faltan, sin declarar mantenimiento completo a partir de las altas existentes.
7. **Compras, cuentas y Mayor.** Completar la fábrica de listado de compras con su propietario y cerrar el consumo de cuentas por ejercicio. Acordar el contrato definitivo del control de Mayor cuando exista su módulo. La última nota del DDL de identidad describe validaciones contables objetivo; no hay que atribuir su implementación a ServicioTerceros.
8. **Correspondencias y tiempos.** Resolver concurrencia/idempotencia entre importadores, política de revisión de mapas, trazabilidad del revisor y diferencia entre FechaDecision local y FechaHora UTC. Hoy no se audita MapasOrigenTerceros en HistorialTerceros.
9. **Adopción y verificación.** Validar con ActualizadorEsquema el contrato completo, no solo la existencia de Terceros; con los importadores, la carga y conservación de referencias/instantáneas; después verificar comportamiento conjunto y UI. Las bases consolidadas de referencia permanecen fuera del destino de escritura.

DAT-TERCEROS deberá actualizar esta ficha cuando cambie su API, datos propios,
dependencias o evidencia de integración. Las decisiones de UI que el usuario
continúe definiendo no bloquean esta primera publicación. La normalización
progresiva sigue abierta.
