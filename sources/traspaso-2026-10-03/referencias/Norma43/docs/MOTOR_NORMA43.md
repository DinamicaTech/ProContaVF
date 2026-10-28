# Motor Norma 43

El subsistema separa interpretación, persistencia, consulta, clasificación y
contabilización. Un fichero válido se guarda en una transacción como lote,
extractos, movimientos, complementos e incidencias. La identidad idempotente es
IdSociedad + Formato + HashSha256Contenido.

## Reversión

Revertir no elimina filas. Cambia el lote a REVERTIDO y conserva fecha,
operador y motivo mediante la migración
20260907_001_REVERSION_LOTES_NORMA43. La operación se bloquea si:

- el lote no pertenece a la sociedad activa;
- existen imputaciones no descartadas;
- existe alguna contabilización confirmada.

Una nueva importación del mismo contenido restaura el lote existente, limpia la
auditoría de reversión y evita infringir su índice único.

## Interfaz

Tesorería → Líneas bancarias abre la vista WPF. El documento editable es
src/ProConta.Facturacion.UI.Wpf/VistaLineasBancariasWpf.leoc; Ctrl+Shift+D
lo abre con LEOC_EXE o con el Designer situado bajo LEOC_ROOT. El XAML
versionado permite compilar cuando LEOC no está instalado.

La reversión exige seleccionar una línea, identificar al operador y explicar el
motivo. Los estados indican la dependencia concreta y la acción necesaria.

### Adaptación visual de Líneas bancarias (2026-10-01)

La maqueta de Dinámica se ha utilizado como referencia de interacción para la
barra compacta de filtros, la tabla densa con selección múltiple, el concepto
desplegable y el menú de acciones agrupadas. La implementación reutilizable es
`ContenidoLineasBancariasWpf`; no crea una ventana, una barra lateral ni un
selector de sociedad o ejercicio. `VistaLineasBancariasWpf` se mantiene
únicamente como adaptador temporal para el host WinForms actual. El futuro
contenedor común podrá alojar el control en el destino estable `lineas`.

La pantalla diferencia expresamente la línea activa, las filas seleccionadas y
los resultados cargados. La consulta está limitada a 2.000 filas y avisa al
alcanzar ese número, por lo que ninguna acción puede interpretar las filas
cargadas como la totalidad sin un contrato adicional. El ejercicio visible
procede del contexto autorizado y no existe una opción local para eludirlo.

Las partes de la maqueta sin semántica confirmada no se han convertido en reglas
de negocio:

- `555`, `-1/0`, `SEPA`, `CCDestino`, `ASIAPU` y el desglose de movimientos no
  se interpretan como estados ni identificadores especiales;
- `IdPalabraClave` e `IdAsiento` mantienen su nulabilidad real;
- Debe y Haber usan exclusivamente el contrato existente `1` y `2`;
- no se ha inventado un ciclo de exportación o reexportación;
- asignación, contabilidad, desglose, remesas y vinculación de facturas se
  muestran como acciones pendientes y nunca simulan una escritura correcta.

Las operaciones conectadas son la consulta parametrizada, la búsqueda por
importe y la reversión auditable del lote. La cuenta bancaria de origen se
compone con los campos Entidad, Oficina y Cuenta ya disponibles en el extracto.

La revisión visual posterior elimina del contenido la cabecera y las bandas de
contexto que ya aporta el contenedor, fija las densidades 26/22 y deja tabla,
concepto y acciones como estructura dominante. Las fuentes exactas, huellas y
renders sintéticos se recogen en `ENTREGA_VISUAL_LINEAS_BANCARIAS_20261001.md`.

## Caracterización reproducible

La herramienta no abre Access ni copia datos reales. Sólo genera hashes y
recuentos. Se ejecuta con el proyecto
tools/ProConta.Facturacion.CompararNorma43 y los argumentos --directorio,
--salida y, opcionalmente, --esperado.

El código de salida es 2 cuando falta un fichero, aparece uno nuevo o cambia
cualquier hash o recuento. Nunca debe ejecutarse sobre una base Access original
ni versionarse el contenido de los ficheros bancarios.
