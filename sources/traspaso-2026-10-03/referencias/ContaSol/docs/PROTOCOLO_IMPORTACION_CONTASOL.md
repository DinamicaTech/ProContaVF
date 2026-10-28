# Protocolo de importación de ContaSol

## Objetivo

ProConta ofrecerá una opción `Importar ContaSol` que trasladará a una base
ProConta todos los datos soportados de una empresa y sus ejercicios. No será un
script de puesta en marcha de una sola ocasión: será una capacidad mantenida,
versionada, observable y comprobable del producto.

Este protocolo es el contrato común entre el análisis del legado, el diseño del
modelo, el proyecto importador y sus pruebas. Cada tabla o regla nueva del
destino debe responder también cómo se obtiene, valida o inicializa al importar
ContaSol.

## Principios

1. La base ContaSol se abre exclusivamente para lectura.
2. La importación se ejecuta sobre una base ProConta con esquema compatible y
   copia de seguridad previa cuando contenga datos.
3. Toda transformación tiene una versión inmutable e identificable.
4. Repetir o reanudar una ejecución no duplica datos.
5. Los identificadores de ContaSol nunca se confunden con las identidades
   autonuméricas de ProConta; se conservan mediante mapas explícitos.
6. Ninguna incidencia se corrige silenciosamente salvo una normalización
   determinista documentada.
7. La unidad de confirmación será pequeña y recuperable, pero una fase no se
   declarará terminada hasta superar sus conciliaciones.
8. Una importación fallida conserva diagnóstico y punto de reanudación.
9. Los datos descartados y el motivo del descarte forman parte del contrato.
10. El resultado puede compararse con ContaSol por recuentos, totales y reglas
    contables, fiscales y referenciales.

## Separación respecto de las migraciones de esquema

`VersionesEsquema` registra cambios internos de una base ProConta. La
importación utiliza sus propias tablas de control y nunca marca versiones de
esquema. Antes de comenzar, el importador comprueba:

- versión mínima y máxima del esquema ProConta que admite;
- versión o perfil reconocido de la base ContaSol;
- sociedad, ejercicios y módulos presentes en el origen;
- ausencia de una ejecución incompatible sobre el mismo destino.

## Proyecto y capas

El importador se materializará como un módulo de Aplicación con adaptadores de
lectura y escritura separados:

- `ProConta.Facturacion.Aplicacion.ImportacionContaSol`: casos de uso,
  planificación, reglas de transformación y resultados;
- `ProConta.Facturacion.Integraciones.ContaSol`: lectura tipada de tablas y
  consultas del origen sin exponer `DataRow` fuera del adaptador;
- `ProConta.Facturacion.Persistencia.Access`: escritura mediante los mismos
  repositorios y transacciones que utiliza ProConta;
- interfaz WinForms y herramienta de consola de diagnóstico que consumen el
  mismo servicio de aplicación.

El importador no escribirá directamente tablas contables saltándose
`CAsiento`, `CApunte` o sus gestores. Podrá utilizar operaciones masivas
especializadas siempre que apliquen las mismas invariantes y produzcan la misma
auditoría.

## Tablas de control

### `ImportacionesContaSol`

- `IdImportacionContaSol`, `IdSociedad` y ruta o identidad segura del origen;
- huella del archivo origen, versión del importador y versión del esquema;
- fecha de inicio y fin, operador y equipo;
- modo (`Simulacion`, `Importacion`, `Reanudacion`);
- estado (`Preparada`, `EnCurso`, `ConIncidencias`, `Completada`, `Fallida`,
  `Cancelada`);
- fase actual, recuentos y resumen final.

La ruta ayuda al operador, pero la identidad efectiva será la huella y los
datos internos de la empresa; mover el archivo no debe aparentar un origen
nuevo.

### `FasesImportacionContaSol`

- importación, código y versión de la fase;
- estado, inicio, fin y último punto confirmado;
- filas leídas, creadas, actualizadas, omitidas y con incidencia;
- huella o resumen de resultados para detectar reanudaciones incompatibles.

### `MapasIdentidadContaSol`

- importación, tipo de entidad y ejercicio de origen cuando forme parte de su
  identidad;
- clave de origen normalizada y `IdDestino`;
- estado del mapa y versión de la regla que lo creó.

Será único por importación, tipo y clave de origen. Ejemplos: sociedad,
ejercicio, cuenta, cliente, proveedor, asiento, factura, registro de IVA,
movimiento N43 y centro de coste.

### `IncidenciasImportacionContaSol`

- importación, fase, tabla, clave y campo de origen;
- código estable, severidad y explicación para el operador;
- valor original, transformación propuesta y datos de contexto;
- estado (`Pendiente`, `Aceptada`, `Corregida`, `Omitida`) y resolución auditada.

Las severidades serán `Informativa`, `Advertencia` y `Bloqueante`. Una fase no
se confirma con incidencias bloqueantes pendientes.

## Fases y dependencias

El orden inicial será:

1. inspección, perfilado y simulación;
2. sociedad, ejercicios, períodos y configuración;
3. centros de coste y plan contable;
4. clientes, proveedores y sus cuentas;
5. facturas de venta y compra y documentos asociados;
6. asientos, apuntes, revisiones y referencias de origen;
7. IVA soportado, repercutido y retenciones;
8. cobros, pagos y vencimientos;
9. extractos N43, movimientos y conciliaciones;
10. inmuebles, contratos, nóminas, plantillas y módulos restantes;
11. reconstrucción de saldos derivados y conciliación global.

Una fase podrá subdividirse por ejercicio y lotes de tamaño limitado. Las
dependencias se expresarán en un catálogo del importador, no sólo mediante el
orden accidental del código.

## Idempotencia y reanudación

Cada registro importable tendrá una clave de origen estable. El importador
consultará primero `MapasIdentidadContaSol`; si ya existe:

- comprobará que el destino continúa presente y es compatible;
- no repetirá una inserción confirmada;
- aplicará una actualización sólo cuando el modo y la regla de esa entidad lo
  permitan;
- generará incidencia ante divergencias no reconciliables.

No se confiará únicamente en números de fila, orden de lectura ni
autonuméricos. Para asientos se utilizará al menos sociedad, ejercicio y
`ASIAPU`; `DIAAPU` se conservará en la clave de origen aunque no se materialice
en el nuevo modelo, para que la identidad sea inequívoca ante otras bases.

## Transacciones y cancelación

- El perfilado y la simulación no escriben datos de negocio.
- Cada lote se valida antes de abrir su transacción Access.
- Dentro de la transacción se escriben entidades, mapas, referencias y punto de
  control relacionados.
- La cancelación se atiende entre lotes y deja el último lote completo.
- Si ACE no permite una operación transaccional, se documentará su compensación
  y se verificará inmediatamente el resultado.

## Contrato de mapeo

Cada entidad contará con una ficha versionada que indique:

- tablas y campos de origen;
- entidad, tabla y campo de destino;
- normalización, conversión de tipos y valores predeterminados;
- claves y dependencias;
- valores descartados y justificación;
- incidencias posibles y su severidad;
- consultas de conciliación;
- ejemplos y pruebas de caracterización.

`PROPUESTA_RESTO_TABLAS_CONTASOL.md` contiene el primer inventario. Al
implementar cada fase, sus decisiones dejarán de ser texto genérico y pasarán a
reglas ejecutables y pruebas sin perder la ficha comprensible para soporte.

## Particularidades del núcleo contable

- `F_APU` se agrupa por identidad de asiento y se transforma mediante
  `CAsiento`; ningún grupo descuadrado se arregla sin aplicar la política
  explícita.
- Los descuadres dentro de la tolerancia configurada pueden producir un apunte
  técnico de redondeo y una incidencia informativa.
- Los valores de `DEPAPU` y `SUBAPU` crean la jerarquía de `CentrosCoste`; cada
  apunte conserva sólo `IdCentroCoste`.
- `F_OAS` y `F_APUMANUAL` aportan metadatos de cabecera, no un segundo libro.
- Los identificadores ContaSol se conservan en mapas y referencias de origen,
  no como claves primarias del nuevo modelo.
- Las presentaciones tributarias históricas se importan antes de habilitar
  modificaciones para que sus bloqueos protejan los períodos correspondientes.

## Validaciones y conciliación

Como mínimo, una importación completa comprobará:

- recuentos de origen, destino, omitidos e incidencias por entidad;
- todos los asientos cuadrados y todas sus cuentas y centros resueltos;
- Debe, Haber, saldos y mayor por cuenta, mes y ejercicio;
- bases y cuotas de IVA por tipo y período;
- facturas y registros fiscales sin huérfanos ni duplicados nuevos;
- saldos pendientes de clientes y proveedores;
- movimientos bancarios y conciliaciones;
- relaciones, documentos y mapas de identidad sin destinos ausentes;
- coincidencia de los totales de control definidos para cada módulo.

Una diferencia aceptada quedará identificada individualmente y explicará por
qué no debe coincidir. El estado `Completada` exige cero incidencias bloqueantes
y un informe final reproducible.

## Experiencia del operador

La opción `Importar ContaSol` seguirá este flujo:

1. seleccionar la base y analizarla sin modificar nada;
2. mostrar sociedades, ejercicios, módulos, volumen y compatibilidad;
3. elegir destino y alcance;
4. ejecutar una simulación con incidencias y conciliaciones previstas;
5. resolver o aceptar advertencias;
6. importar mostrando fase y progreso cancelable;
7. presentar el informe final y permitir abrir las incidencias;
8. reanudar una ejecución interrumpida desde su último punto confirmado.

No se ofrecerá como importación correcta una ejecución que sólo haya copiado
filas. El resultado deberá ser utilizable por los casos de uso normales de
ProConta y superar las mismas invariantes que los datos creados manualmente.

## Evolución conjunta

Toda propuesta o cambio de modelo debe incluir en su revisión:

1. efecto sobre bases ProConta existentes mediante migración de esquema;
2. efecto sobre una importación ContaSol nueva;
3. compatibilidad de importaciones incompletas que puedan reanudarse;
4. actualización de mapas, validaciones, conciliaciones y pruebas.

Una funcionalidad contable no se considerará completamente migrada hasta que
pueda crearse de forma nativa y también importarse desde su representación
ContaSol conocida.
