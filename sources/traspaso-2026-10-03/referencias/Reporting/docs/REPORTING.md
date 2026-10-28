# Motor Reporting

## Alcance actual

Reporting separa cuatro conceptos: el tipo organiza el catálogo; el informe identifica el resultado funcional; el origen obtiene datos mediante un proveedor compilado; y la plantilla define su representación. La interfaz utiliza «plantilla» para las variantes visibles. El nombre interno `PerfilInforme` se conserva temporalmente como contrato de compatibilidad con los JSON existentes.

Las plantillas son versionadas e inmutables. Guardar crea un borrador; publicar o retirar crea otra versión y conserva el historial. Los orígenes SQL siguen el mismo criterio y solo una versión publicada puede seleccionarse desde un informe.

## Representaciones

- `HTML`: tabla desconectada.
- `PDF`: FastReport, reutilizando el cálculo existente.
- `XLSX_DATOS`: libro tabular editable con tipos, filtros, tabla, totales y cabecera inmovilizada.
- `XLSX_PRESENTACION`: genera primero los datos OOXML y los inserta en una plantilla XLSX publicada, actualizando rangos y cachés dinámicas. No utiliza Excel COM.
- `WPF_LEOC`: contrato gradual para diseños `.leoc`; todavía no sustituye las pantallas WinForms existentes.

## Diseño y publicación

El mantenimiento «Diseñar informes» permite administrar la jerarquía de tipos, informes y plantillas; los orígenes SQL se mantienen desde su editor especializado. Una plantilla puede guardarse como borrador, publicarse o retirarse sin sobrescribir versiones anteriores. `Ctrl+Shift+D` abre el archivo `.leoc` de la plantilla WPF seleccionada mediante la asociación configurada en Windows.

Los diseños, catálogos y configuraciones viven bajo `Datos\Reporting`. No contienen rutas absolutas ni reglas contables. Las bases Access originales y las migraciones publicadas no se modifican desde el diseñador.

## Pendientes funcionales

- Materializador WPF/LEOC para ejecución y preview dentro de ProConta.
- Comparación sistemática de PDF de balances con las salidas históricas.
- Selector interactivo común de parámetros.
- Pruebas de concurrencia y recuperación ante JSON corruptos o versiones futuras.
