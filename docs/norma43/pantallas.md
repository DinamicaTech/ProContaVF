---
title: Pantalla de líneas bancarias
status: draft
depends_on: [norma43, plataforma/contenedor]
threads:
  - Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Tabla densa de líneas bancarias con filtros, detalle y acciones (también las de conciliación).
**Aceptación:** búsqueda y orden mantienen la selección por ID; las acciones indican su alcance real.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 14:40 · Referencia funcional: la pantalla "Gestión de Lineas N43" de ProConta antiguo (`frmLineasN43` y su ficha `frmLineaN43`), descrita en `sources/proconta-antiguo/norma43/ANALISIS-FUNCIONAL.md`, incluida su lista de defectos, que no se reproducen. La maqueta autónoma anterior no se ha encontrado (la carpeta de maquetas está vacía); el prototipo se propondrá a partir de este análisis.
- 2026-10-29 15:47 · Propuesto el prototipo de la pestaña «Líneas N43» dentro del contenedor (`sources/maquetas/Contenedor/`, `?escenario=n43`), pendiente de validación: filtros combinados en Y con selector de estado y fichas de filtro que se quitan una a una, filtro por remesa y diálogo «Remesas…» invocable desde la pantalla (requisito de las 15:32), menú contextual según la selección sea simple o múltiple, detalle lateral, explotar remesa con vista previa, dividir en fracciones, palabras clave y diálogo de subcuentas parametrizadas por sociedad y ejercicio (solo consulta aquí). Los atajos son provisionales hasta acordar el nodo de atajos. En el lienzo de diseño, tablero «Líneas bancarias Norma 43 · 2560 × 1440».

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 19 RF-N43-03, 04: pantalla fiel a maqueta, selección explícita
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Norma43/docs/ENTREGA_VISUAL_LINEAS_BANCARIAS_20261001.md: entrega visual anterior
- 2026-10-28 12:28 · draft: Construir la pantalla de líneas bancarias según la maqueta.
- 2026-10-28 12:28 · question: La maqueta autónoma no está en el paquete (C:\Docs\VeriFactu\Maquetas\LineasBancarias\lineas-bancarias-proconta.html): (a) la copias a ProConta y la subo a sources/maquetas ✅
- 2026-10-29 10:20 · Derived from plataforma/ui-base: validar con el propietario el prototipo HTML final de las líneas bancarias Norma 43 (lienzo del contenedor, 2050 × 1110) antes de construir su UI WPF.
- 2026-10-29 14:12 · Y si revisar primero el formulario frmLineasN43 con su código igual que has hecho con el Mayor vitaminado ?
- 2026-10-29 15:23 · El código es funcional en otro formulario (frmN43Importar) y mi idea era añadir esta opción en la pantalla de líneas para explotar solo la línea seleccionada, pero me quedé a medias.
- 2026-10-29 15:23 · De momento podemos obviar esta funcionalidad, aunque el código de esta sí es funcional.
- 2026-10-29 15:23 · Quizá le podemos dar una vuelta y rediseñar el sistema de filtros desde cero, lo que hay ahora tampoco es maravilloso.
- 2026-10-29 15:23 · Todas las subcuentas (por ejemplo, 5729999) tendrán que ser parametrizadas por Sociedad y Ejercicio, aunque, por defecto, tenga este valor
- 2026-10-29 15:23 · La idea siempre es trasladar a menú contextual todas las funcionalidades que tengan sentido, algunas serán aplicables solo a selección simple, otras a selección múltiple (traspaso entre bancos, por ejemplo) y la mayoría a selección simple y múltiple.
- 2026-10-29 15:32 · Quizá podemos hacer que el formulario remesas N43 se pueda invocar directamente desde la pantalla de líneas N43 y poder filtrar las líneas N43 de cualquier remesa.
- 2026-10-29 16:37 · El desplegable de remesas que indique la subcuenta (así se verá el nombre del banco o banco ficticio para líneas N43 generadas), el rango de fechas de la remesa, el número de recibos dentro de la remesa y el importe con formato currency
- 2026-10-29 16:37 · A la hora de crear una palabra clave, la funcionalidad actual es que se selecciona una parte de la descripción de la línea N43 que es la que se convertirá en criterio para la asignación de la palabra clave. El formulario popup está bien, pero faltaría que apareciese el concepto de la línea N43 (que puede ocupar unos 200 carácteres) para que el usuario pueda seleccionar un trozo de ese texto, no tiene mucho sentido entrarlo a mano ya que a de corresponder literalmente a un subconjunto del concepto de la línea N43. Si ya existe una o más palabras N43 que hagan 'match' con la línea seleccionada, permitir editarlas, borrarlas y/o crear una nueva:
- 2026-10-29 16:37 · Importes de la grid en formato currency. En un principio, las cuentas bancarias serán siempre en euros o se habrán convertido a euros, trabajar en contabilidad con dos monedas a la vez puede ser complicado.
- 2026-10-29 16:37 · Falta una columna con la subcuenta de destino y otra con la descripción de la subcuenta de destino.
- 2026-10-29 16:37 · Doble click en una línea que muestre el asiento contable, excepto si el doble click es en una subcuenta (ya sea la de origen o la destino), en cuyo caso se abrirá la pestaña de Mayor vitaminado
- 2026-10-29 16:37 · Igual que en la pantalla anterior, el 'Ampliar área de trabajo' un slider entre los menús y el formulario
- 2026-10-29 16:37 · Falta el selector del ejercicio, que puede ser selección múltiple
- 2026-10-29 16:37 · El formulario de remesas con formato currency. Siempre aplicar currency a importes.
- 2026-10-29 16:37 · Sí, la configuración de subcuentas desde la configuración global, cuando esté, podremos añadir un botón para que desde el popup de subcuentas se pueda saltar directamente a la configuración de subcuentas.
