# Auditoría para la transición operativa de ProcontaVF

Fecha de corte: 01/10/2026. Este documento examina una **copia privada** de
`C:\Docs\VeriFactu\ProcontaVF.accdb`; no autoriza ni ejecuta una migración en
esa base ni en `C:\Docs\VeriFactu\ProConta.accdb`. La captura
`work/databases/ProcontaVF.Auditoria.20261001.accdb` tenía SHA-256
`B5A25ECB183193F86EA9A7C28B1FEF41BC313CCC69D61BE4E4DD0772A302B992`.
El hash del original antes y después de obtenerla coincidió con el de la
captura. Los recuentos de este informe solo describen ese corte.

## Resultado de la revisión

| Área | Estado de la copia | Acción antes del cambio real |
| --- | --- | --- |
| Acceso y organización | Existen `OperadoresSeguridad` (3), `GruposOperadores` (2), `OperadoresGrupos` (3), `AccesoSociedadesGrupo` (0), `PreferenciasAccesoOperador` (1) y `PreferenciasAccesoEjercicio` (1). | No repetir las tres migraciones de acceso ni sembrar permisos. Mantener las preferencias existentes. La política del grupo sin asignaciones sigue en Seguridad. |
| Sociedad y ejercicio | `Sociedades` tiene 3 filas; `SociedadesEjercicios`, 13. `IdSociedad` es `TEXT(3)`. Falta `EjercicioCerrado`. | `EjercicioCerrado` no es requisito de acceso: el adaptador devuelve estado desconocido. Su migración pertenece a Asientos y requiere decidir el valor histórico antes de aplicarla. `Activa` no equivale a ejercicio abierto. |
| Norma 43, consulta | Hay 12 lotes, 12 extractos y 691 líneas. Las seis tablas y todos los campos de `EsquemaModulos.Exigir("N43")` están presentes. | No hay DDL necesario para la lectura integrada actual. Validar servicio, contexto y permisos en UI/Norma43. |
| Norma 43, reversión | Faltan `FechaHoraReversion`, `OperadorReversion` y `MotivoReversion` en `LotesImportacionBancaria`. | Migración separada de Norma43 si se habilita la reversión. No confundir esta capacidad con el requisito de lectura. Evitar el identificador de versión conflictivo de las ramas hasta fijar catálogo/huella. |
| Terceros | `Terceros`, `Contactos`, domicilios, datos de contacto, perfiles, historial y mapas de origen no existen. `Clientes` (212) y `Proveedores` (394) contienen nombre, NIF y dirección propios del modelo heredado. | Exige conversión de datos y adaptación de consumidores. Los manifiestos de creación de maestra 005–006/019–020 no pueden aplicarse directamente porque `Clientes` y `Proveedores` ya existen con otra forma. |

El catálogo heredado de `ActualizadorEsquemaAccess.CatalogoPredeterminado()`
declara 13 migraciones, mientras `VersionesEsquema` de la captura registra 19,
incluidas Seguridad, Activos y Mail Dispatcher. Tampoco corresponde al historial
de 22 manifiestos de creación nueva. **No ejecutar ninguno de los dos catálogos
completos sobre esta base.** Primero se requiere un plan de actualización
incremental que reconozca el historial instalado sin reescribir sus huellas.

## Dependencias de Terceros y ensayo

El ensayo reproducible está en
`tools/ActualizadorEsquema.Inspeccion/EnsayarTercerosSobreCopia.ps1`. Copia la
captura a otro `.accdb`, crea solo `MapaTercerosEnsayo` y asigna una identidad
provisional distinta a cada `IdCliente` y `IdProveedor`. Conservó las 606
correspondencias, con índice único por tipo e ID de origen; la captura de
entrada mantuvo su hash. El ensayo **no** crea el modelo final ni valida que los
servicios existentes puedan escribir en él.

Desde la raíz de este repositorio, con ACE instalado, se reproduce en una
ruta de ensayo nueva así:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/ActualizadorEsquema.Inspeccion/EnsayarTercerosSobreCopia.ps1 `
  -RutaCopiaOrigen work/databases/ProcontaVF.Auditoria.20261001.accdb `
  -RutaEnsayo work/databases/ProcontaVF.Ensayo.Terceros.20261001.accdb
```

El destino del ensayo debe no existir; el script rechaza sobreescrituras.

Los NIF vacíos son 58 en clientes y 1 en proveedores. Hay seis repeticiones de
NIF no vacío dentro de Clientes y ningún duplicado dentro de Proveedores. Cuatro
NIF aparecen en ambos roles. Estas cifras no acreditan equivalencia: no se
fusionará automáticamente por NIF, por nombre ni por coincidencia numérica de
`IdCliente` e `IdProveedor`. La reconciliación se registrará explícitamente en
`MapasOrigenTerceros` cuando se aprueben los pares concretos.

Las FKs físicas de la captura son:

| Destino funcional | Tablas que lo referencian |
| --- | --- |
| `Clientes.IdCliente` | `Contratos` (2), `CuentasClientesEjercicio` (73), `Facturas` (2118), `FacturasProgramadas` (37), `IVARepercutido` (162). |
| `Proveedores.IdProveedor` | `CuentasProveedoresEjercicio` (412), `IVASoportado` (159). |

Esos recuentos son filas totales de las tablas dependientes, no el número de
referencias no nulas. La migración debe mantener `IdCliente` y `IdProveedor`,
añadirles `IdTercero` obligatorio y único tras poblarlos, y pasar los campos
comunes a `Terceros` sin romper las referencias. Las facturas emitidas conservan
su instantánea fiscal; la ficha viva no debe reescribirlas. Las cuentas
contables se asocian al papel y al ejercicio, no a la identidad común.

## Secuencia propuesta para una futura aplicación

1. Congelar el corte y verificar exclusividad, copia de seguridad, hash e
   historial de versiones. Preparar un catálogo de **evolución** específico
   para el esquema instalado; nunca reiniciar `VersionesEsquema`.
2. En copia privada, crear `Terceros` y las correspondencias de origen; migrar
   los campos comunes de Clientes/Proveedores, conservar IDs funcionales y
   registrar incidencias de NIF y de datos incompatibles. Comparar recuentos,
   FKs y hashes de facturas/contabilidad antes y después.
3. Añadir los submodelos aprobados (`Contactos`, domicilios, relaciones,
   historial, perfiles y datos de contacto) según el contrato vigente. Adaptar
   consultas y escrituras de Facturación, Compras e importadores a `JOIN
   Terceros`; después retirar las columnas duplicadas con un paso separado y
   reversible en la copia de trabajo.
4. Ejecutar pruebas de acceso, consulta N43 y ficha de Terceros con contexto
   autenticado; comprobar cierre de ejercicio, permisos y guardado. Solo tras
   superar esos controles se podrá proponer la actualización del original y
   su clonación al nombre de trabajo `ProConta.accdb`.

## Decisiones pendientes y límites

- **Aplicación inmediata:** no hay migración adicional imprescindible para
  abrir el acceso ya desplegado ni para la consulta actual de N43. Esta copia
  ya contiene los objetos físicos de ambos. Para Terceros sí hay bloqueo real:
  aplicar solo `CREATE TABLE Terceros` activaría lectores que detectan su
  existencia, mientras `Clientes` y `Proveedores` conservarían su forma
  antigua. No existe aún una actualización coherente, probada y lista para
  aplicar al original.
- **Equivalencias de identidad:** las cuatro coincidencias de NIF entre roles
  requieren revisión. Se puede conservar de momento cada origen como tercero
  distinto; unificar pares concretos necesita una decisión explícita y un mapa
  auditable. Los seis NIF repetidos dentro de Clientes merecen la misma revisión.
- **Ficha de Terceros:** el contrato nuevo de `ProConta.Terceros` incluye
  `20260930_002_perfiles_terceros.sql` y existe una propuesta
  `20261001_001_ficha_terceros.sql` aún sin consolidar en ese hilo. El chequeo
  central de lectura exige los campos de ficha, perfiles y datos de contacto,
  pero verificar su presencia no prueba el contrato de guardado. Incorporar
  las adiciones solo cuando su propietario cierre DDL y consumidores. En el
  estado actual, `Clientes.Nombre` y `Proveedores.Nombre` siguen obligatorios,
  pero el repositorio nuevo de altas escribe solo los campos del rol; una alta
  fallaría. El DDL de ficha propuesto también actualiza `Activo` y
  `EsPrincipal` de domicilios existentes: se debe revisar su efecto sobre
  datos reales antes de ejecutarlo.
- **Clasificación de identidad:** `EsEntidad` heredado no distingue por sí
  solo persona física, jurídica y entidad, ni permite separar con certeza
  nombre y apellidos. Los valores dudosos necesitan regla funcional o
  incidencia de revisión; no inferirlos durante el corte.
- **Permisos de Terceros:** Seguridad no tiene todavía autorización por
  operación para `Leer`, `Editar`, `LeerNotasInternas` o
  `EditarNotasInternas`. No crear una tabla de permisos por deducción; la ficha
  no será operativa solo por aplicar DDL.
- **Configuración de ruta:** CFG-GLOBAL prepara la selección explícita de
  `ProConta.accdb`; confirmar su integración antes del cambio de nombre.
- **Reversión Norma43 y ejercicio cerrado:** capacidades independientes de
  esta primera integración de lectura. Requieren sus propias decisiones de
  historial, valores iniciales y pruebas.

Este alcance excluye correo, gestión documental, facturación nueva y la
conversión integral de los demás submodelos de la maestra. La base maestra
nueva sigue siendo la referencia de diseño; este plan cubre la transición
operativa mínima de la base existente.
