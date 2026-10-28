# Terceros: contrato de identidad y adopción

## Alcance y estado

Este contrato define el destino para una **maestra nueva**. El DDL está en
`database/migrations/20260929_001_terceros_maestra_nueva.sql`. ActualizadorEsquema
debe incorporar ese DDL a su manifiesto de creación y verificarlo sobre una base
vacía. No se ejecuta sobre la maestra 0.18 ni transforma automáticamente una
base que ya contiene clientes, proveedores o facturas.

## Identidad y funciones

- `Terceros.IdTercero` es la identidad estable de una persona u organización.
  Nombre, razón social, identificación fiscal y medios de contacto y domicilio
  principal viven aquí. `NIF` es un dato revisable, **no** una clave única.
- `Clientes`, `Proveedores` y `Contactos` son funciones opcionales, cada una con
  `IdTercero` obligatorio y único. Una identidad puede tener las tres funciones.
  Se mantienen `IdCliente` e `IdProveedor` para que contratos, facturas,
  programaciones, IVA y compras conserven sus referencias funcionales.
- `RelacionesContactos` enlaza el tercero que ejerce la función de contacto con
  la organización representada. Sus fechas permiten conservar relaciones
  históricas. Ni el vínculo ni la función crean otra identidad.
- `DomiciliosTercero` permite direcciones adicionales con una finalidad
  explícita. El domicilio principal permanece en `Terceros` para la ficha común.
- La cuenta contable **no** está en `Terceros`. Se resuelve por función y
  `IdSociedadEjercicio` mediante `CuentasClientesEjercicio` o
  `CuentasProveedoresEjercicio`. Una misma identidad puede tener cuentas
  diferentes como cliente y proveedor, incluso en el mismo ejercicio.

## Reglas de aplicación

1. Se crea primero el tercero y después las funciones necesarias en la misma
   transacción. El alta de una función existente devuelve su identificador;
   nunca crea un segundo tercero por cambio de función.
2. Buscar un NIF propone candidatos, pero no fusiona. La persona que importa
   coteja nombre, procedencia y domicilio y registra su decisión. Dos registros
   con igual NIF pueden permanecer separados.
3. Un contacto puede ser persona física y cliente o proveedor. La relación
   empresa-contacto es muchos a muchos y no exige que la empresa sea cliente.
4. Las pantallas parten de la ficha de Terceros. Las pestañas `Cliente`,
   `Proveedor` y `Contacto` aparecen según las funciones existentes; activar
   una función añade su fila sin copiar datos comunes.
5. Una factura emitida conserva `CIF`, `Nombre`, `RazonSocial`, país y restantes
   datos fiscales materializados. El contrato añade a `Facturas` dirección,
   código postal, población, provincia y correo de cliente, mediante
   `database/migrations/20260929_002_facturas_instantanea_terceros.sql`, para
   que el PDF no consulte la ficha viva después de la emisión. Solo un borrador puede tomar datos actuales
   del tercero al elegir un cliente. Los documentos históricos y las
   rectificativas leen su instantánea, nunca vuelven a materializarla desde
   la ficha viva.

## Correspondencias de importación

`MapasOrigenTerceros` registra `OrigenSistema`, `ContextoOrigen`, `TipoEntidad`,
`ClaveOrigen` e `IdTercero`. `ContextoOrigen` identifica de forma estable la base
ERP o la sociedad y ejercicio ContaSol; evita confundir códigos iguales de
distintos orígenes. `HuellaOrigen` es el SHA-256 hexadecimal de esa tupla,
serializada con longitud explícita de cada componente en UTF-8; tiene índice
único. Al encontrar una huella existente, el importador compara los cuatro
componentes antes de reutilizar el mapa. Esto evita superar el límite de
longitud de índice compuesto de ACE. Las filas `Personas` se
registran como `ERP/PERSONA`; `DatosContacto` se traduce a medios de contacto o
relaciones explícitas tras revisar su semántica; domicilios a principal o
adicionales. `F_CLI/CODCLI` y `F_PRO/CODPRO` se registran por ejercicio como
`CONTASOL/CLIENTE` y `CONTASOL/PROVEEDOR`. Además, el mapa de cada importación
ContaSol conserva el `IdCliente` o `IdProveedor` funcional para resolver IVA y
asientos. Ambos mapas deben apuntar al mismo `IdTercero` cuando se ha confirmado
que se trata de la misma persona.

El importador actual usa `SELECT TOP 1 ... WHERE NIF=?` para `F_CLI` y `F_PRO`.
Esa búsqueda debe eliminarse antes de importar al contrato nuevo. El flujo es:
mapa de origen existente; candidato por NIF y nombre para revisión; decisión
registrada; alta de identidad si no hay equivalencia; alta de función; cuenta
por ejercicio. Una discrepancia se anota como incidencia y no se resuelve con
una fusión automática.

## Adopción de datos existentes

Para una maestra con datos se requiere una migración **separada y explícita**:
respaldo, ausencia de `.laccdb`, inventario de FKs y duplicados, propuesta de
equivalencias, revisión humana, carga en copia, comparación de recuentos y
verificación de instantáneas fiscales. Solo entonces se reemplazan las tablas
de funciones y sus consultas. Las bases originales permanecen intactas.

## Consumidores que deben cambiar con la adopción

- `RepositorioClientesAccess`, búsqueda de clientes, `VistaClientes` y
  `RepositorioFacturasManualesAccess.CargarCliente`: leer datos comunes mediante
  `Clientes INNER JOIN Terceros`.
- Consulta de facturación, contratos, programaciones, avisos por correo y
  documentos: mantener `IdCliente`, unir con `Terceros` cuando se requiera un
  dato vivo y preferir la instantánea en documentos emitidos.
- Compras, proveedores, IVA soportado y cuentas por ejercicio: mantener
  `IdProveedor`, unir con `Terceros` para datos vivos.
- Importador ContaSol y MigradorERP: sustituir la fusión por NIF por mapas y
  reconciliación; conservar códigos originales por contexto.

La adopción se considera verificada cuando los índices y FKs existen, un
tercero puede tener simultáneamente tres funciones, dos terceros pueden
compartir NIF, las cuentas cliente/proveedor son independientes y editar una
ficha no cambia los datos de una factura emitida.

## Estado del incremento implementado

La rama `feature/terceros` incluye API de identidades, funciones, domicilios y
mapas de origen; repositorios ACE parametrizados; ficha Krypton; y rutas de
lectura compatibles para selección de clientes, contratos, correo, alquiler y
facturas programadas. Las facturas nuevas del esquema de Terceros congelan el
domicilio al reservar número o generar la factura y los documentos toman su
dirección de la cabecera fiscal.

El importador ContaSol antiguo permanece disponible únicamente sobre el
esquema antiguo. Si detecta `Terceros`, se detiene **antes de escribir** porque
su conciliación por NIF sigue siendo insegura. La importación ERP y la carga
ContaSol al contrato nuevo requieren adoptar `MapasOrigenTerceros` y completar
la revisión de identidades en sus respectivos módulos. ActualizadorEsquema
debe incorporar ambos scripts a la maestra nueva; hasta entonces la BD de
trabajo vigente permanece en el contrato anterior.

## Historial y facturas desde la ficha (30/09/2026)

`HistorialTerceros` registra una fila por campo cambiado: IdTercero,
fecha/hora UTC, operador, entidad, identificador del registro, operación,
campo, valor antiguo y valor nuevo. Un IdOperacion agrupa los campos de cada
registro guardado. Los valores son MEMO y conservan NULL. La presentación
convierte la fecha a hora local. La migración reproducible es
`20260930_001_historial_terceros.sql`; no modifica las bases de referencia.

El repositorio captura los valores anteriores desde Access y registra las
diferencias en la misma transacción que la escritura. Un fallo de auditoría
revierte el cambio; guardar sin cambios no crea entradas. Se registran las
altas y modificaciones de Terceros, Clientes, Proveedores y Contactos, las
altas de domicilios y las relaciones contacto-organización (visibles desde
ambas identidades). La pantalla Historial es de solo lectura y se actualiza
al entrar. La auditoría cubre las operaciones del repositorio; los consumidores
no deben escribir estas tablas mediante SQL propio.

El constructor del repositorio exige un `Func(Of String)` que identifica al
operador en cada guardado. La composición de esta rama, que todavía no integra
sesiones de Seguridad, proporciona `Windows:dominio\usuario`. Al integrar
Seguridad debe pasarse su identidad autenticada; un proceso automatizado debe
proporcionar su identificador explícito. No se acepta un operador vacío.

`VistaTerceros` admite dos fábricas de controles independientes: una recibe
IdCliente y otra IdProveedor. Cada módulo suministra su propio listado con
menú, acciones, permisos y navegación. Terceros únicamente aloja y libera el
control. Las pestañas se crean según las funciones presentes y los listados
se cargan al seleccionarlas.

FacturasVenta reutiliza `VistaFacturas`, incluidas impresión, PDF, envío,
selección múltiple y doble clic al editor habitual. Su servicio de consulta es
independiente y mantiene un IdCliente fijo fuera de los filtros eliminables.
Quitar filtros, Escape y los filtros de resultados de operaciones no eliminan
ese límite. Una factura nueva creada desde esta pestaña recibe el cliente de
la ficha. Se conserva el contexto sociedad/ejercicio activo, visible en el
listado; al cambiarlo se reconstruyen los listados alojados.

FacturasCompra tiene preparado el punto de integración, pero el módulo actual
solo proporciona la pantalla de reconocimiento, no un listado general de
facturas de proveedor con menú y editor. Hasta incorporar esa vista, la pestaña
indica expresamente que el listado no está disponible. No hay una segunda
implementación de las acciones de compras dentro de Terceros.

## Decisiones de modelo posteriores al primer incremento

- `TipoPersona` admite FISICA, JURIDICA y ENTIDAD (sin personalidad jurídica).
  Las dos últimas pueden ser organizaciones representadas por contactos.
- `ProcontaEstructura.accdb` se utiliza exclusivamente como referencia, sin
  modificarla. El modelo destino reutilizará el concepto `DatosContacto`.
- Los usos de DatosContacto serán los flags independientes EnvioFacturas y
  EnviosAdministrativos. La selección de emails por uso empleará todos los marcados y activos;
  si no hay ninguno, utilizará la única dirección activa distinta, si existe.
  Esta regla no modifica los flags. Si hay varias sin selección explícita,
  registra una incidencia sin detener el resto del proceso.
- La normalización de DatosContacto, las consultas públicas qClientes,
  qProveedores y qContactos y la fachada de clases acordada siguen pendientes
  de implementación. El historial deberá ampliarse al mantenimiento de
  DatosContacto cuando se incorpore al repositorio.

### Validación funcional de contactos e identificación (01/10/2026)

Reglas confirmadas por el usuario, pendientes de implementación:

- Un tercero admite varios teléfonos y emails en DatosContacto, con Tipo y los
  flags independientes EnvioFacturas y EnviosAdministrativos.
- Para la finalidad solicitada se envía a **todos los emails activos marcados**.
- Si no hay ninguno marcado para esa finalidad y existe una única dirección de
  email activa, se utiliza como si tuviese el flag marcado. Es el modo básico;
  no actualiza los flags en la base de datos.
- Si existen varias direcciones activas y ninguna marcada para la finalidad,
  se registra una incidencia y se continúa el proceso. La falta de destinatario
  inequívoco no aborta, por ejemplo, la generación de una factura periódica;
  no se elige una dirección arbitrariamente.
- Los datos de contacto desactivados se excluyen de las comunicaciones
  automáticas, tanto de la selección por flag como del cómputo del email único.
  Se tratan como inexistentes a estos efectos.
- Persona física: **Nombre obligatorio**; primer y segundo apellido opcionales.
  Queda descartada la propuesta anterior de exigir el primer apellido.
- Persona jurídica y entidad sin personalidad jurídica: **Razón social
  obligatoria**. Esta es la etiqueta funcional del campo, en lugar de
  «Denominación». Nombre comercial opcional.

La implementación actual aún conserva teléfono/email únicos y exige Nombre
para los tres tipos. Falta incorporar la colección, mantenimiento y auditoría
de DatosContacto, la selección de destinatarios y sus consumidores, los campos
separados de apellidos y nombre comercial y las validaciones/presentación por
tipo. No se considera implementado por documentar estas reglas. Roles/Perfiles
está resuelto como nomenclatura; el guardado conjunto sigue pendiente de la
decisión que está preparando el usuario.

## Perfiles y alojamiento del Mayor (30/09/2026)

### Clasificación propia de Terceros

`Perfiles` y `PerfilesTerceros` pertenecen íntegramente a DAT-TERCEROS. Su DDL
para la maestra destino es `20260930_002_perfiles_terceros.sql`; requiere el
contrato de identidad y el historial. No es una migración de datos de las bases
consolidadas ni se aplica sobre `ProcontaEstructura.accdb`.

| Tabla | Campos y reglas |
| --- | --- |
| Perfiles | IdPerfil autonumérico; Nombre obligatorio y único, hasta 100 caracteres; Descripcion MEMO opcional; Activo obligatorio. |
| PerfilesTerceros | IdPerfilTercero autonumérico; FKs IdTercero e IdPerfil, pareja única; Activo; FechaAsignacion UTC obligatoria y FechaRetirada UTC opcional. Índice adicional por IdPerfil. |

La relación conserva su clave al retirarse y reasignarse. Activo en la relación
indica asignación vigente; Activo en el catálogo indica disponibilidad para
nuevas asignaciones. Desactivar el catálogo conserva las asignaciones previas.
Reasignar actualiza FechaAsignacion y limpia FechaRetirada; los ciclos previos
quedan en HistorialTerceros. No hay borrado físico ni altas de ejemplo.

`ServicioTerceros` ofrece ListarPerfiles, GuardarPerfil, ListarPerfilesTercero,
AsignarPerfiles y QuitarPerfiles. La lectura por tercero incluye perfiles
asignados aunque estén desactivados en el catálogo. Para JOINs de lectura,
filtrar PerfilesTerceros.Activo=True; añadir Perfiles.Activo=True excluiría
asignaciones conservadas. Las escrituras deben pasar por el servicio.

Cada lote de asignación/retirada valida todas las claves y se confirma junto
con su auditoría, o se revierte entero. Repetir el estado actual no añade
historial. El historial incluye el campo derivado «Perfil asignado», con el
nombre en el momento del movimiento, además de los campos persistidos. Editar
el catálogo registra sus diferencias en todos los terceros que lo tienen
asignado. Un perfil sin titulares no tiene una ficha de tercero donde anotar
sus cambios: no se ofrece auditoría global del catálogo en este incremento.
IdOperacion sigue agrupando los campos de cada registro, no el lote entero.

La pestaña tiene filtros en las dos listas, selección múltiple, arrastre
interno, doble clic, botones y teclado. Cada movimiento se guarda de inmediato
y así se indica en pantalla. Guardar identidad conserva su alcance propio.
Las clasificaciones son globales en la maestra, independientes de sociedad,
ejercicio, funciones Cliente/Proveedor/Contacto y permisos.

### Integración pendiente del Mayor

Contabilidad aparece para cualquier tercero guardado, también sin funciones.
`VistaTerceros` admite una fábrica opcional
`Func(Of SolicitudMayorTercero, Control)` y un proveedor del contexto vigente
`Func(Of ContextoSociedadEjercicio)`. La solicitud inmutable contiene
IdTercero, IdCliente e IdProveedor opcionales, e IdSociedadEjercicio. La fábrica
se invoca al entrar en la pestaña y debe devolver una instancia independiente
cuya liberación corresponde al anfitrión.

La composición todavía no suministra la fábrica: se muestra un estado
informativo. Al cambiar sociedad/ejercicio, ActualizarContextoFacturas libera
también el Mayor anterior; cambiar de tercero o cerrar la ficha lo libera
igualmente. El siguiente control recibe las nuevas claves.

La futura implementación contable resolverá cuentas por contexto, autorizaciones,
apuntes, filtros, saldos, refresco, menú, impresión y navegación. El anfitrión no
consultará apuntes ni calculará el Mayor a partir de facturas. La solicitud es
un punto de extensión de esta rama, pendiente de acuerdo con el módulo futuro;
no define un contrato contable común ni acredita integración operativa.

Revisión visual y sugerencias: [maqueta de Terceros](REVISION_MAQUETA_TERCEROS.md).

## Requisitos de la siguiente maqueta (01/10/2026)

Fuente: [requisitos registrados en Facturación](../../ProConta.Facturacion/docs/REQUISITOS_TERCEROS_20261001.md).
Se incorporan como requisitos pendientes de implementación. La nueva maqueta
está en preparación; no se han revisado HTML anteriores para esta actualización.
La revisión del 30/09 queda como antecedente, no como aprobación del diseño nuevo.
Esta anotación no modifica código, DDL, bases ni el orden del roadmap.

### Domicilio y dimensiones

- Dimensionar campos según contenido esperado, conservando la capacidad de
  almacenamiento y contemplando valores largos y escalado de Windows.
- Orden visual y de teclado: **País → Código postal → Provincia → Población →
  Vía y número**. Provincias por país y poblaciones por provincia; resolver el
  código postal dentro del país, sin imponer formato español a otros países.
- Autoseleccionar y omitir la parada de Provincia solo cuando haya una única
  coincidencia. Si también hay una única población, pasar a Vía y número.
  Con ambigüedad, permitir elegir; sin coincidencias, conservar el recorrido
  permitido por el catálogo sin inventar relaciones.
- Mostrar los valores automáticos y permitir corregirlos. Revalidar dependencias
  al cambiar país, CP o provincia. Saltar al confirmar el dato, nunca al cargar
  una ficha existente.

Contraste en esta rama: el DTO Tercero y el DDL de identidad ya tienen Pais,
CodigoPostal, Provincia, Poblacion y Direccion como textos; DomiciliosTercero
también. No hay campos separados de vía/número en ese contrato. Quedan por
identificar catálogo y API de resolución, y concretar la representación del
último editor. No se propone un cambio físico por deducción.

### Roles y cuentas

La denominación de interfaz pasa a **Roles**, sustituyendo la propuesta anterior
«Funciones». No renombra las clases/tablas actuales ni alude a permisos de
operadores o a las clasificaciones de Perfiles.

| Rol | Presentación requerida, por sociedad/ejercicio |
| --- | --- |
| Cliente | Una cuenta de cliente, debajo de Forma de pago. |
| Proveedor | Dos posibles cuentas diferenciadas: Proveedor y Acreedor, debajo de Forma de pago. No son alternativas excluyentes ni un tercer rol. |
| Contacto | Sin cuenta propia; conserva las de otros roles que pueda tener la misma identidad. |

Los ejemplos funcionales de suministro para la actividad y para funcionamiento
ordinario no autorizan clasificar compras ni asignar cuentas automáticamente.
Se mantiene la resolución por rol y contexto mediante el dominio contable,
sin cuenta global en Terceros. Quedan abiertas obligatoriedad, validaciones,
representación física de Proveedor/Acreedor y API de lectura/edición.

Contraste: DatosFuncionCliente/Proveedor/Contacto no contienen cuentas ni forma
de pago en ServicioTerceros. El importador existente referencia
CuentasClientesEjercicio y CuentasProveedoresEjercicio con IdSociedadEjercicio,
IdCuentaContable y EsPredeterminada. Esa evidencia no acredita la representación
de la pareja Proveedor/Acreedor ni resuelve las decisiones pendientes.

### Ventas: programaciones y emitidas

Arriba se alojará el listado de programaciones/periódicas del cliente; abajo,
sus facturas emitidas. Seleccionar una programación filtrará el segundo listado
por su origen verificable. Sin selección se mostrarán las emitidas del cliente,
con acción para quitar el filtro e indicación visible del filtro activo. Cambiar
cliente o contexto eliminará selecciones incompatibles.

Terceros aloja ambos controles. Programación y Facturación conservan consultas,
relaciones, acciones y aplicación de permisos/contexto. No se inferirá el origen
por importes, fechas o textos, ni se duplicará el motor de generación.

Contraste: CreadorFacturasProgramadasAccess inserta en Facturas
IdFacturaProgramada, IdLoteFacturaProgramada y FechaProgramadaOrigen; consulta
IdFacturaProgramada/FechaProgramadaOrigen para evitar duplicar un período.
RepositorioConsultaFacturacionAccess ya consulta FacturasProgramadas. Debe
cerrarse con Facturación el contrato consumidor de origen, cobertura de datos
importados y filtro de emitidas antes de integrar los dos listados; la relación
encontrada no acredita ese contrato ni su despliegue en todas las bases.

### Notas antes de Historial

La pestaña **Notas** irá inmediatamente antes de Historial y contendrá dos
editores multilínea independientes: **Notas** y **Notas internas**, ambos de
texto largo Memo/Long Text.

Contraste: Terceros.Notas ya es LONGTEXT y se expone como Tercero.Notas; los
campos NotasCliente, NotasProveedor y NotasContacto también son LONGTEXT,
propios de cada rol. No existe NotasInternas en el DTO/DDL de Terceros revisado.
Las NotasInternas de Factura y FacturaProgramada pertenecen a otros registros y
no sustituyen las de la ficha. No se fusionarán las notas de rol por deducción.

Quedan abiertos el alcance global o por contexto, visibilidad, usos externos y
permisos de los dos campos. Sus nombres no autorizan publicación en documentos,
correos o exportaciones. El historial actual cubre cambios de Terceros.Notas por
el repositorio; la incorporación del segundo campo y su autorización deben
verificarse al implementar. Cualquier DDL resultante se acordará con
ActualizadorEsquema y tendrá script reproducible.

La propuesta de guardado conjunto de la conversación sigue pendiente de
confirmación; estos requisitos no la dan por aprobada. La implementación actual
conserva guardados separados. La siguiente maqueta permitirá contrastar foco,
estados, filtros y disposición antes de programar la ficha WPF; la rama actual
sigue usando WinForms/Krypton.
