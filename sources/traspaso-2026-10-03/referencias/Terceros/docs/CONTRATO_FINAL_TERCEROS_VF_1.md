# Contrato físico final TERCEROS_VF_1

Publicado para Actualizador el 1 de octubre de 2026. El DDL completo y ejecutable sobre una **base vacía** está en `database/migrations/20261001_002_contrato_final_terceros.sql`. No se debe aplicar encima de VF: Actualizador realiza el traslado incremental, preserva claves y referencias y elimina columnas comunes tras verificarlas.

## Correspondencia y ausencia de datos

- Una identidad por cada Cliente y por cada Proveedor (UNION ALL lógico). Nunca fusionar por NIF ni por igualdad completa de valores.
- Se conservan IdCliente e IdProveedor, con IdTercero obligatorio y único por tabla de rol. Una misma identidad puede adquirir más roles posteriormente por acción explícita.
- Nombre, RazonSocial, NIF, Telefono, Email, Direccion, CP/CodigoPostal, Poblacion, Provincia y Pais pasan a Terceros. En origen CP se convierte en CodigoPostal. Estos campos comunes se retiran de Clientes/Proveedores.
- Clientes.Notas y Clientes.FechaAlta conservan sus nombres físicos, valor original y nulabilidad. FechaAlta sigue siendo un dato del rol; no se copia a la fecha global del tercero. Clientes.IdContrato y CuentaContable permanecen en el rol. No tocar cuentas por ejercicio ni referencias entrantes.
- Proveedores.Activo permanece en el rol. No se deduce de él la actividad de la identidad común. Proveedores.NotasProveedor y los campos de rol sin origen admiten NULL.
- TipoPersona admite NULL para clasificación pendiente. No asignar FISICA/JURIDICA/ENTIDAD por NIF o nombre. Nombre se conserva íntegro; NombrePersona, apellidos, tipo de identificación y nombre comercial quedan NULL si no hay origen explícito.
- Terceros.FechaAlta admite NULL: no inventar una fecha global a partir de la fecha de migración o del rol.
- Terceros.Activo es un estado operativo nuevo, obligatorio. Inicializar True para que la identidad migrada esté disponible, sin atribuirle significado histórico. Clientes.Activo, ausente en origen, se inicializa True como estado operativo del rol; conservar el valor original de Proveedores.Activo. Las lecturas del módulo incluyen también los inactivos.
- Conservar NULL/vacío y valores completos durante el traslado; no truncar ni reformatear. La lectura representa los textos NULL como cadena vacía en el DTO. Un guardado explícito aplica la normalización habitual del formulario, no la migración.
- Las colecciones pueden estar vacías. La ficha presenta una dirección escalar como domicilio GENERAL y los contactos escalares como borradores generales; cargar nunca los escribe. Si Actualizador materializa colecciones, conservar valores, usar GENERAL sin inferir finalidad fiscal y mantener los flags de comunicaciones sin asignación deducida.

## Activación y verificación

Crear EstadoContratoTerceros con el resto del DDL. Publicar **al final**, después de verificar todos los traslados, retirada de columnas, índices y referencias, una fila:

- Contrato: `TERCEROS_VF_1`.
- Estado: `ACTIVO`.
- IdPlan: GUID textual de 36 caracteres del plan aplicado.
- HuellaPlan: SHA-256 hexadecimal de 64 caracteres del plan.
- FechaActivacion: instante real de la activación.

El DDL no inserta esa fila. `EsquemaTercerosAccess.RevisarEstructura` permite verificar antes de activar, sin escribir. `EstaDisponible` devuelve False solo en bases antiguas sin Terceros ni marcador; rechaza estructuras parciales o no activadas. Verifica campos, tipos, capacidad mínima, nulabilidad, índices por sus nombres/columnas/unicidad, relaciones y ausencia de columnas comunes en roles. El marcador certifica las postcondiciones de **datos**, responsabilidad de Actualizador; no sustituye esa auditoría.

Usar los nombres de índices del DDL. Los índices de NIF y Nombre **no son únicos**. No añadir restricciones de unicidad que fusionen o impidan duplicados legítimos. Los campos autonuméricos del DDL siguen siendo autonuméricos en tablas nuevas; en roles existentes conservar generación de claves compatible con sus IDs originales.

## Interfaz y límites del corte

Listado y ficha leen las identidades sin clasificar y muestran Pendiente con el nombre original. Para guardar es obligatorio seleccionar un tipo y completar nombre (persona física) o razón social (organización). No se exigen apellidos. Elegir persona física en una ficha pendiente exige informar el nombre expresamente; no se separa el texto original automáticamente. Razón social: 200; nombre de pila: 150; nombre visible compuesto: 200; cada apellido: 100; Terceros.Email: 150. DatosContacto.Valor mantiene 254; una dirección que no cabe en el escalar permanece completa en DatosContacto y su proyección escalar queda NULL.

La habilitación cubre Terceros/listado/ficha en el nuevo EXE del roadmap, con autorización real del contenedor. Las pruebas pueden simular autorización únicamente en el harness. No habilita módulos antiguos por el hecho de migrar:

- Mantenimiento legacy de clientes (`Clientes/RepositorioClientesAccess.vb`): SELECT/INSERT/UPDATE de columnas retiradas.
- Importación ContaSol (`ImportacionContaSol/ImportadorContaSolAccess.vb`): inserciones legacy y búsqueda por NIF; exige adaptación propia. Ya rechaza el esquema nuevo en esta rama.
- Facturación manual y programada, consultas de facturación, contratos e incrementos: tienen algunas proyecciones de lectura adaptadas, pero eso **no certifica** su flujo completo ni los habilita en este corte.
- Correo de facturas/avisos: las lecturas escalares no implementan por sí solas la resolución de todos los contactos y finalidades del nuevo módulo. No activar por esta migración.
- FacturasCompra, importadores restantes y demás pantallas externas: pendientes de certificar por sus responsables; bloquear su acceso desde el prototipo hasta esa validación.

No hay doble escritura de campos comunes ni compatibilidad de escritura con el EXE antiguo. No se modifican datos de facturas emitidas. VF es una base de pruebas. ProcontaEstructura es referencia de lectura.

## Campos, capacidad y nulabilidad

Tipos: LONG = entero Access de 32 bits; BIT = booleano; DATETIME = fecha/hora; MEMO = texto largo. Las claves Id nuevas son AUTOINCREMENT/COUNTER tal como indica el DDL. “Sí” en NULL permite ausencia; la obligatoriedad funcional al editar puede ser más estricta.

### Terceros

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdTercero | LONG | No |
| TipoPersona | TEXT(10) | Sí |
| Nombre | TEXT(200) | No |
| RazonSocial | TEXT(200) | Sí |
| NIF | TEXT(30) | Sí |
| Telefono | TEXT(50) | Sí |
| Email | TEXT(150) | Sí |
| Direccion | TEXT(255) | Sí |
| CodigoPostal | TEXT(20) | Sí |
| Poblacion | TEXT(100) | Sí |
| Provincia | TEXT(100) | Sí |
| Pais | TEXT(3) | Sí |
| Notas | MEMO | Sí |
| Activo | BIT | No |
| FechaAlta | DATETIME | Sí |
| NombrePersona | TEXT(150) | Sí |
| PrimerApellido | TEXT(100) | Sí |
| SegundoApellido | TEXT(100) | Sí |
| NombreComercial | TEXT(150) | Sí |
| TipoIdentificacion | TEXT(30) | Sí |
| NotasInternas | MEMO | Sí |

### Clientes

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdCliente | LONG | No |
| IdTercero | LONG | No |
| IdContrato | LONG | Sí |
| CuentaContable | TEXT(15) | Sí |
| Notas | MEMO | Sí |
| FechaAlta | DATETIME | Sí |
| Activo | BIT | No |

### Proveedores

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdProveedor | LONG | No |
| IdTercero | LONG | No |
| NotasProveedor | MEMO | Sí |
| Activo | BIT | No |

### Contactos

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdContacto | LONG | No |
| IdTercero | LONG | No |
| Cargo | TEXT(100) | Sí |
| Departamento | TEXT(100) | Sí |
| NotasContacto | MEMO | Sí |
| Activo | BIT | No |

### RelacionesContactos

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdRelacionContacto | LONG | No |
| IdContacto | LONG | No |
| IdTerceroOrganizacion | LONG | No |
| TipoRelacion | TEXT(40) | No |
| FechaInicio | DATETIME | Sí |
| FechaFin | DATETIME | Sí |
| Observaciones | MEMO | Sí |

### DomiciliosTercero

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdDomicilioTercero | LONG | No |
| IdTercero | LONG | No |
| TipoDomicilio | TEXT(30) | No |
| Direccion | TEXT(255) | No |
| CodigoPostal | TEXT(20) | Sí |
| Poblacion | TEXT(100) | Sí |
| Provincia | TEXT(100) | Sí |
| Pais | TEXT(3) | Sí |
| EsPrincipal | BIT | No |
| Activo | BIT | No |
| FechaInicio | DATETIME | Sí |
| FechaFin | DATETIME | Sí |

### MapasOrigenTerceros

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdMapaOrigenTercero | LONG | No |
| OrigenSistema | TEXT(20) | No |
| ContextoOrigen | TEXT(150) | No |
| TipoEntidad | TEXT(40) | No |
| ClaveOrigen | TEXT(150) | No |
| HuellaOrigen | TEXT(64) | No |
| IdTercero | LONG | No |
| FechaDecision | DATETIME | No |
| CriterioDecision | TEXT(100) | No |
| Observaciones | MEMO | Sí |

### HistorialTerceros

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdHistorialTercero | LONG | No |
| IdTercero | LONG | No |
| IdOperacion | TEXT(36) | No |
| FechaHora | DATETIME | No |
| Operador | TEXT(255) | No |
| Entidad | TEXT(50) | No |
| IdRegistro | LONG | No |
| Operacion | TEXT(15) | No |
| Campo | TEXT(64) | No |
| ValorAntiguo | MEMO | Sí |
| ValorNuevo | MEMO | Sí |

### Perfiles

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdPerfil | LONG | No |
| Nombre | TEXT(100) | No |
| Descripcion | MEMO | Sí |
| Activo | BIT | No |

### PerfilesTerceros

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdPerfilTercero | LONG | No |
| IdTercero | LONG | No |
| IdPerfil | LONG | No |
| Activo | BIT | No |
| FechaAsignacion | DATETIME | No |
| FechaRetirada | DATETIME | Sí |

### DatosContacto

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| IdDatoContacto | LONG | No |
| IdTercero | LONG | No |
| Tipo | TEXT(20) | No |
| Etiqueta | TEXT(100) | Sí |
| Valor | TEXT(254) | No |
| Activo | BIT | No |
| EnvioFacturas | BIT | No |
| EnviosAdministrativos | BIT | No |

### EstadoContratoTerceros

| Campo | Tipo/capacidad | NULL |
|---|---|---|
| Contrato | TEXT(40) | No |
| Estado | TEXT(15) | No |
| IdPlan | TEXT(36) | No |
| HuellaPlan | TEXT(64) | No |
| FechaActivacion | DATETIME | Sí |

## Evidencia de verificación (01/10/2026)

- Solución completa x64: 0 errores; 30 advertencias preexistentes de WindowsBase/MSTest.
- Adaptador de integración, contra contrato del contenedor central: 0 errores y 0 advertencias; sin modificar el contenedor.
- 22 pruebas relacionadas correctas. Los fixtures crean el DDL final 002, sin campos comunes en roles. Cubren conservación de IDs, mismo NIF en identidades distintas, 200 caracteres de razón social, email escalar de 150, contacto de 254 sin truncamiento, fecha global NULL, fecha/nota original de Clientes, auditoría, transacciones y rechazo de cortes incompletos.
- Prueba WPF de clasificación pendiente correcta: conserva el nombre original y seleccionar FISICA no lo interpreta como nombre de pila ni escribe datos.
- Diagnóstico sobre copia privada del ensayo `ProcontaVF.MigracionTerceros.ContratoFinal.20261001.accdb`: 606/606 fichas leídas, 606 pendientes de clasificación. Este diagnóstico preliminar queda complementado por la aceptación final documentada en `ACEPTACION_FINAL_TERCEROS_20261001.md`.
- Originales VF, ProConta y ProcontaEstructura sin modificaciones por esta rama. No hay entrega de EXE independiente.

El harness de aceptación es `ContratoFinalLeeTodasLasFichasDeCopiaMigrada` en `tests/ProConta.Facturacion.Tests/ContratoFinalTercerosTests.vb`. Requiere una copia privada bajo `tmp` del worktree y la variable `TERCEROS_COPIA_MIGRADA` con su ruta absoluta. Por defecto solo consulta; TERCEROS_PROBAR_GUARDADO=1 prueba también el guardado en una segunda copia desechable. La autorización simulada permanece dentro del test. Se ejecuta con `dotnet test`, plataforma x64 y filtro por ese método, sin `--no-build` para evitar binarios obsoletos.

## Ajuste de Email a 150

Por decisión posterior del usuario, Terceros.Email requiere capacidad mínima 150 en el contrato y el DDL lo crea como TEXT(150). El servicio de escritura escalar rechaza más de 150. La proyección de ficha solo copia un email único si cabe completo; DatosContacto conserva su límite 254. Actualizador realiza la reducción física y comprueba que no haya truncamientos. Adaptación verificada con 14 pruebas del módulo sobre bases sintéticas nuevas de Email TEXT(150), incluida detección de capacidad insuficiente a 149. No se modificaron bases originales.
