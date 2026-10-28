# ActualizadorEsquema: creación inicial de la maestra

La propuesta de partida está en `C:\Docs\VeriFactu\ProConta.Facturacion\docs\actualizador-esquema\INVENTARIO_Y_PROPUESTA.md`. El creador materializa veintidós migraciones en una **base maestra nueva de submodelos**. Aún faltan pruebas de consumo y transición de datos antes de usarla como base operativa compartida.

## Crear una base maestra parcial

Desde este worktree:

```powershell
dotnet run --project tools/ProConta.ActualizadorEsquema.Cli -- crear C:/ruta/nueva/ProConta.Maestra.accdb
```

El directorio debe existir y el archivo de destino no puede existir. El creador genera un ACCDB temporal en ese mismo directorio, aplica los veintidós manifiestos incorporados en el ejecutable, comprueba sus tablas y catálogos, y publica el archivo mediante un movimiento sin sobrescritura. Los recursos reproducibles están en `database/migrations/20260928_001_base_tecnica.json` a `20260930_022_preferencias_acceso.json`. Cada huella SHA-256 queda en su fila de `VersionesEsquema`; los IDs no simulan migraciones históricas.

La versión técnica `0.1.0` contiene `VersionesEsquema`, `LogMigracionesEsquema` y `TiposDeIVA`, con valores iniciales 0, 4, 10 y 21. La versión `0.2.0` añade `FormasPago`, `Sociedades`, `SociedadesEjercicios`, `TiposActivo`, `Activos` y `ActivosInmuebles`. Solo se siembran los cuatro tipos técnicos de activo; sociedades, ejercicios, formas de pago y activos están vacíos. `IdSociedad` tiene tres caracteres en sus tres apariciones; las cuentas de configuración tienen un máximo de diez. El NIF es único y los ejercicios conservan su razón social. Se comprobaron las restricciones de NIF y las claves externas de ejercicio e inmueble con ACE.

La versión `0.3.0` añade planes, tipos e identidades contables; cuentas y reservas de códigos; asientos, apuntes y auditorías. Siembra siete tipos técnicos de cuenta y el plan base PGC Pymes con sus siete grupos, pero ninguna identidad, cuenta, sociedad, asiento ni apunte real. `CuentasPlanContable.Codigo`, `CuentasContables.Codigo`, `CodigosContablesReservados.Codigo`, `TiposCuentaContable.Prefijo` y `Apuntes.CuentaContable` son `TEXT(10)`. La [nota del bloque contable](CONTABILIDAD_INICIAL.md) delimita las relaciones que se incorporarán al crear los módulos dependientes.

Las versiones `0.4.0` y `0.5.0` añaden centros de coste, configuración contable y el [núcleo de facturación](CENTROS_Y_FACTURACION.md), incluidas las FKs a sociedad, ejercicio, serie, cuenta, activo inmueble y forma de pago. Se siembran únicamente estados y tipos fiscales técnicos. VeriFactu, correo, Nóminas y seguridad se incorporan en las versiones posteriores, sin credenciales ni operadores reales. Los ejemplares anteriores se conservan en `work/databases/ProConta.Maestra.Tecnica.accdb`, `work/databases/ProConta.Maestra.Nucleo.accdb` y `work/databases/ProConta.Maestra.Contabilidad.accdb`; los ACCDB están excluidos de Git. El destino existente se rechaza antes de abrirlo.

Se creó y verificó `work/databases/ProConta.Maestra.Facturacion.accdb` con la huella del quinto manifiesto `22013B01BBE4C8D23654B939438C698ADE5E06173982D1B0AE7AE8229072F2D8`. Este archivo es la muestra de la versión `0.5.0`, sin datos empresariales, y no sustituye `ProcontaVF.accdb`. Tras cerrar la base abierta, se conservó la muestra previa como `ProConta.Maestra.Facturacion.Anterior.accdb`. El [diagnóstico de la suite completa](DIAGNOSTICO_SUITE_20260928.md) distingue los fallos históricos de la prueba del nuevo creador.

La versión `0.6.0` incorpora el [bloque inicial de compras](FACTURAS_COMPRA.md), sin datos ni configuración de reconocimiento precargados. Su muestra es `work/databases/ProConta.Maestra.Compras.accdb`, con seis filas de historial y huella final `9592F1D91C4E263095B36B5349FC96A44F8333689A0BD4D6E59A70D5BA8FDFEA`. La muestra anterior de facturación se conserva para comparar versiones.

La versión `0.7.0` añade [banca y Norma43](BANCA_NORMA43.md). Su muestra es `work/databases/ProConta.Maestra.Banca.accdb`, con siete filas de historial y huella final `947F25D565A63A0D45FB958B78F7C50B2C46CCFF29377FE8BFE9021EBB5F7B2F`. Las muestras anteriores se conservan para comparar versiones.

La versión `0.8.0` añade el [núcleo de integración ContaSol](CONTASOL_NUCLEO.md), incluidos los cuatro tipos técnicos de operación IVA usados por el importador. La muestra histórica `work/databases/ProConta.Maestra.ContaSol.accdb` precede a esta corrección del manifiesto y no representa la versión vigente.

La versión `0.9.0` incorpora los [libros y desgloses de IVA](CONTASOL_IVA.md). Su muestra es `work/databases/ProConta.Maestra.ContaSolIVA.accdb`, con nueve filas de historial y huella final `A16F7CF8F86085C86529AB1BABDA871F41F0B0D75A75D14E84EE8DEA1C607409`.

La versión `0.10.0` incorpora [seguridad de operadores](SEGURIDAD.md). Su muestra es `work/databases/ProConta.Maestra.Seguridad.accdb`, con diez filas de historial, 70 tablas y huella final `D7F948B836743516A89D1FD83EA905A8E0DB71D121B1103D0BB90258B3E5D987`.

Las versiones `0.11.0` a `0.18.0` completaron los submodelos inventariados de la muestra anterior `work/databases/ProConta.Maestra.Submodelos.Validada.accdb`: 18 filas, 117 tablas y SHA-256 `E735E92FEA8BC085AFEE6F383E68FCAA84F1A4E11BE7F5CD7ACBC2D1AD291A38`. La versión `0.19.0` incorporó el [contrato de Terceros](INTEGRACION_TERCEROS.md) a una maestra creada desde cero. Su muestra histórica es `work/databases/ProConta.Maestra.Terceros.0.19.accdb`, con 19 filas, 122 tablas y SHA-256 `5DE850FF84E4B870204C26F6F7414A7BE683DEA0A79496225D434EBEA076B2D3`. El diagnóstico comprueba tablas, columnas, tipos ACE, longitudes de texto y binario, índices y nombres de FKs. Las propiedades completas de las relaciones y los atributos que ACE no distingue por código de tipo, como `COUNTER` frente a `LONG`, todavía no forman parte de esta comparación.

La versión `0.20.0` añade `HistorialTerceros` para registrar por campo las escrituras de identidad y funciones, con el operador y un identificador común por guardado. `TipoPersona` admite también `ENTIDAD` para colectivos sin personalidad jurídica. Su muestra histórica es `work/databases/ProConta.Maestra.Terceros.0.20.accdb`, con 20 versiones, 123 tablas y SHA-256 `5751AA9CB9975F2C946C67D050519168788B4A1E823AE76E145974F6CD0E5E97`. Su diagnóstico no presenta hallazgos. La muestra 0.19 permanece como antecedente.

Las versiones `0.21.0` y `0.22.0` añaden [acceso por sociedad y preferencias de entrada](../ACCESO_SOCIEDADES_GRUPO.md). La muestra vigente `work/databases/ProConta.Maestra.Acceso.0.22.accdb` tiene 22 versiones, 126 tablas y SHA-256 `D2EF6BA1322020B123E271280635A248C800DD568488E456C4D183EE47DD8A42`; el diagnóstico no encuentra hallazgos. La BD de pruebas recibió únicamente estas tres tablas mediante la aplicación mínima documentada, con backup y sin reinterpretar su historial anterior.

## Base de pruebas con datos

`C:\Docs\VeriFactu\ProcontaVF.accdb` recibió únicamente las tres tablas de acceso descritas arriba; no se ha convertido al modelo global de la maestra. Su historial tiene identificadores y huellas incompatibles entre ramas. La migración automática actual no puede convertirla de forma segura a la nueva línea de versiones: marcarla como `BASE_TECNICA` fingiría una creación que no ocurrió. La transición deberá leer y planificar sobre una copia aislada, reconciliar los contratos y registrar explícitamente cada transformación de datos y su verificación. Se ha ensayado el importador ContaSol sobre una maestra descartable y una copia de `ProcontaEstructura.accdb`: carga centros, cuentas, asientos, bancos y libros IVA, y una segunda ejecución reutiliza el resultado sin duplicarlo. MigradorERP aún no dispone de un ejecutor integral para el resto de datos.

La captura de pruebas inspeccionada revela además 15 facturas de compra con referencia anual sin ejercicio, dos contratos cuya relación declarada apunta a `Inmuebles` del ERP pero cuyos IDs no existen en las dos copias ERP inspeccionadas, y usos del código de forma de pago `1` ausente de esos catálogos ERP. Esos datos impiden una conversión directa con FKs estrictas. El 28-09-2026 se verificó de nuevo que `ProcontaVF.accdb` seguía sin archivo de bloqueo y conservaba exactamente la huella SHA-256 de la captura (`4A99674A9FC6DC4C9BD7D34ED4CB53DD0BE67A2DB25EFA9B868D29C1D7DF25A6`); las consultas se hicieron exclusivamente sobre copias. El ERP original cambió de huella durante la revisión, de modo que sus datos se contrastarán otra vez en el corte. Se recomienda preparar la regeneración desde la maestra y los dos importadores, conservando una copia de pruebas para seleccionar y trasladar por separado cualquier configuración útil. Antes del corte se repetirá el diagnóstico por si cambia algún archivo; no se borran ni reescriben originales.

El usuario identificó `003` como sociedad de pruebas y autorizó su supresión en el destino. Se omitirán también sus ejercicios 2024/2025 y su configuración contable, sin tocar el ACCDB original. La sociedad `002` conserva los nombres históricos de sus ejercicios y su NIF estable. En Compras, tres referencias huérfanas señalan `002`/`2021` y tienen un ejercicio candidato; otras doce señalan la sociedad inexistente `999`. El perfil de importación deberá diagnosticar estos casos antes de cargar facturas de compra.

## Siguientes incrementos

El [contrato compartido del incremento 1](CONTRATOS_INCREMENTO_1.md) recoge las decisiones de identidad, la longitud máxima de 10 caracteres para cuentas contables y la sustitución de los cuatro vínculos heredados.

1. Contrastar los consumidores de cada rama con el esquema físico 0.22, incluida nulabilidad, tamaños, índices, relaciones y campos añadidos en ramas recientes. Corregir mediante un nuevo manifiesto cualquier diferencia comprobada.
2. Completar la validación estructural del diagnóstico y las pruebas de consumo de cada módulo sobre una maestra nueva.
3. Ejecutar el [plan de transición de pruebas](PLAN_TRANSICION_BASE_PRUEBAS.md) sobre copias: reconciliar datos heredados e importadores, sin marcar la base antigua como si hubiera recibido los manifiestos nuevos.
4. Incorporar coordinación de mantenimiento entre equipos, copia verificada, diario externo y recuperación por pasos; conectar MigradorERP, ContaSol y los demás ejecutables al contrato común antes de usar la maestra para operaciones de negocio.
