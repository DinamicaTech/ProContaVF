# Interfaz de Nóminas con Krypton

Las pantallas de Nóminas, Empleados y el visor común de Asientos emplean controles
Krypton para todos los elementos visuales e interactivos que tienen equivalente en
Krypton Toolkit. La pantalla de Conceptos es la referencia de composición y estilo.

Se conservan los siguientes controles WinForms porque no existe un equivalente
Krypton independiente o porque forman parte del modelo nativo requerido por el
control Krypton:

- `UserControl`, como superficie hospedable de las vistas.
- `TableLayoutPanel` y `FlowLayoutPanel`, únicamente como motores de distribución.
- `DataGridViewTextBoxColumn`, `DataGridViewCheckBoxColumn` y
  `DataGridViewComboBoxColumn`, como columnas nativas de `KryptonDataGridView`.
- `OpenFileDialog`, como selector de archivos del sistema operativo.

Estos controles no dibujan una superficie de aplicación alternativa: los formularios,
grupos, botones, etiquetas, editores, listas y rejillas visibles usan Krypton y heredan
la paleta común de la aplicación.

## Ayuda contextual

Todos los controles de Nóminas y del visor común de Asientos reciben un `ToolTip`
descriptivo. La ayuda explica la finalidad del control, los formatos admitidos, la
acción disponible y sus consecuencias. En los controles que reflejan operaciones o
selecciones, el texto se actualiza durante la carga, ante estados vacíos o de error,
al validar un arrastre y al seleccionar un fichero o asiento.

## Ciclo contable de una importación

La vista de Nóminas ofrece las acciones `Contabilizar`, `Revertir contabilización`,
`Deshacer importación` y `Rehacer importación`. Su disponibilidad y ayuda cambian
con el estado del fichero seleccionado para impedir transiciones que no correspondan.

La contabilización crea cabeceras, apuntes, revisiones y vínculos con las nóminas en
una sola transacción. Si una plantilla, una cuenta o el cuadre no son válidos, no se
conserva ningún resultado parcial. La reversión comprueba que cada asiento siga
contabilizado, pertenezca al lote y conserve su versión original. Después aplica la
política configurada para la sociedad: crea un asiento inverso o elimina de forma
controlada únicamente asientos intactos y sin relaciones contables o fiscales.

Todas estas acciones conservan una operación de auditoría con el usuario, el motivo,
los estados anterior y posterior y los identificadores de los asientos afectados.
