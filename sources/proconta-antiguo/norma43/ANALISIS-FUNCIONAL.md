# Líneas N43 de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmLineasN43` y `frmLineaN43` (ProcontaEstructura.accdb), sus módulos (`C43`, `C43Linea`, `C43PalabraClave`, `C43Cabecera`, `CInmueble`, `CAsiento`) y consultas, más la estructura de 62 tablas. La exportación completa está en `sources/proconta-antiguo/norma43/` del clon local del propietario y no se ha subido; la credencial de eInforma y una posible contraseña de un comentario se sustituyeron por marcadores al exportar. Este documento describe **qué hace la pantalla**, no cómo está programada. Donde algo depende de un formulario que no se ha exportado (`frmSeleccionarCuenta`, `frmN43CuentaPalabrasClave`, `frmN43BusquedaPorImporte`, `frmMovimientosN43Similares`, `frmAsiento`, `frmSeleccionarInmueble`, `HotKeyLista`…), se indica.

## 0. Contexto

- La pantalla se llama "Gestión de Lineas N43" y se abre desde el menú principal ("N43-Movimientos bancarios") y desde el Mayor ("Busca importe … en N43" y "Ver N43", que la abren ya filtrada).
- Muestra los **movimientos bancarios** importados de ficheros Norma 43 (cuaderno 43 de la AEB) de la sociedad seleccionada y un ejercicio. Cada fichero crea una **cabecera** (`N43Cabeceras`: banco, oficina, cuenta, periodo, saldos) y sus **líneas** (`N43Lineas`: fecha, debe/haber, importe, concepto).
- Su trabajo es **imputar cada movimiento a una cuenta contable** y **generar el asiento** banco contra esa cuenta. La imputación se automatiza con **palabras clave**: un texto que aparece en el concepto del banco y apunta a una cuenta (por ejemplo, el nombre de un proveedor → su 400).
- La importación del fichero no está en esta pantalla (la hace `C43.ImportarC43`, llamada desde otro sitio; ver §8).
- Una sola zona: fila de filtros y búsqueda arriba, una lista grande de líneas en medio, una caja con el concepto completo de la línea seleccionada y dos filas de botones debajo.

## 1. Cabecera: filtros y búsqueda

### 1.1 Año

Desplegable con los años 2021 a 2026, fijos. Cambiarlo recarga la lista. La lista solo muestra líneas cuya cabecera es de ese ejercicio.

### 1.2 Casillas de filtro

Seis casillas; cada clic recarga la lista. **Se combinan con O**: una línea aparece si cumple cualquiera de las marcadas. Sin ninguna marcada se ven todas.

| Casilla | Muestra |
|---|---|
| Sin palabra clave | líneas sin palabra clave asignada |
| Sin cuenta contable | líneas sin cuenta contable de destino |
| Exportadas 555 | líneas exportadas a la cuenta puente 555 del banco porque no tenían cuenta |
| No exportadas | líneas sin asiento |
| SEPA no explotadas | líneas de remesas de recibos (palabra clave de tipo remesa) que aún no se han desglosado en recibos |
| Pendientes reexportar | líneas cuya cuenta de destino ya no coincide con la del apunte generado. **Sustituye a todas las demás**: si está marcada, las otras se ignoran |

Siempre se ocultan las líneas marcadas como "no exportar" (`NoExportar`), salvo en una búsqueda.

### 1.3 Búsqueda

Botón **Buscar** (pide el criterio en un cuadro de texto) y campo "Criterio búsqueda", que muestra el último. El criterio tiene prefijos:

| Escribir | Busca |
|---|---|
| texto | concepto que contiene el texto |
| `n+123` | número de línea |
| `i+150,25` | importe aproximado (±0,50 €, sin signo) |
| `e+150,25` | importe exacto (±0,02 €, sin signo) |
| `a+456` | número de asiento generado |
| `c+4000012` | cuenta de destino |
| `o+5720001` | cuenta del banco (origen) |
| `f+F2025` | número de factura que contiene el texto |

Con búsqueda, **las casillas se ignoran** y solo se aplica el año. La búsqueda dura hasta que se borra el criterio. Si la pantalla se abre desde el Mayor, el criterio llega ya escrito (`i+`, `e+` o `a+`) y el año pasa al ejercicio activo.

Junto a la búsqueda, **Refrescar** recarga la lista, y un rótulo muestra **el número de líneas y el filtro activo** ("317 Todos").

## 2. Lista de líneas

Selección múltiple (clic, Mayúsculas y Control). Clic en la cabecera de una columna: ordena por ella; otro clic invierte el orden. Por defecto se ordena por concepto, y el concepto es siempre el segundo criterio. Los anchos de columna se ajustan al contenido.

| Columna | Qué es |
|---|---|
| Entidad | código de la entidad bancaria y su nombre |
| IdRegistro | cabecera (fichero importado) a la que pertenece |
| NumLinea | número de la línea, único en toda la base |
| Fecha | fecha valor del movimiento |
| IdPalabraClave | palabra clave aplicada: vacía o 0 = ninguna; −1 = cuenta puesta a mano; otro número = palabra clave |
| CuentaContable | cuenta de destino |
| CCDestino | descripción de la cuenta de destino (50 caracteres) |
| DebeHaber | 1 = cargo (sale dinero del banco); 2 = abono (entra dinero) |
| ImporteEuros | importe, siempre positivo |
| CuentaOrigen | cuenta contable del banco |
| ASIAPU | asiento generado; vacío si no se ha exportado |
| NumeroFactura | factura asociada al movimiento |
| ConceptoCompleto | concepto del banco, todas las líneas de concepto juntas |

Al seleccionar una línea, la caja de debajo muestra su **concepto completo** y se activan o desactivan los botones según su estado (§4). Doble clic abre la ficha de la línea (§5).

## 3. Caja de concepto y alta de palabras clave

La caja muestra el concepto de la primera línea seleccionada. Al **seleccionar con el ratón un trozo del texto**, el botón de alta pasa a decir "Añadir «texto»". Es la forma de crear palabras clave: se marca el trozo que identifica al tercero (un nombre, una referencia de recibo) y se pulsa el botón (§4.1).

## 4. Botones

La mayoría actúan sobre las líneas seleccionadas.

### 4.1 Palabras clave

- **Añadir «texto»**: crea una palabra clave con el texto marcado para la sociedad, la entidad bancaria y el sentido (debe o haber) de la línea seleccionada.
  1. Si ya existe igual, avisa "Palabra duplicada".
  2. Si existe una **más larga** que contiene el texto, o una **más corta** contenida en él, pregunta si se sustituye por la nueva.
  3. Si la palabra clave no tiene cuenta, abre el selector de cuenta de palabras clave (no exportado). Si no se elige ninguna, pregunta si se quiere dar de alta sin cuenta; si no, la borra.
  4. Después **reasigna** las líneas con esa palabra (§6).
- **Asignar IdPalabraClave**: reasigna **todas** las palabras clave a todas las líneas (§6). Es el paso que se lanza después de importar.
- **Calcular similitudes**: recalcula el porcentaje de parecido de cada línea con la línea de referencia de su palabra clave (§6, paso 7).
- **Eliminar palabra clave**: borra la palabra clave de la línea seleccionada, previa confirmación. Todas sus líneas quedan sin palabra clave, y las no exportadas pierden también la cuenta. Solo se activa si la línea tiene palabra clave y no está exportada.

### 4.2 Asignar cuenta

- **Asignar cuenta manualmente** (o "Asignar cuenta palabra N" si la línea tiene palabra clave): abre el selector de cuentas, propuesto con la última cuenta usada, la de la línea o el texto filtrado. Para cada línea seleccionada:
  - si la línea tiene palabra clave **sin cuenta**, pone la cuenta a la palabra clave (y con ello a todas sus líneas) y termina;
  - si la palabra clave **ya tiene otra cuenta**, pregunta: **Sí** = cambiar la cuenta de la palabra clave y de todas sus líneas; **No** = cambiar solo esta línea y desvincularla de la palabra clave; **Cancelar** = nada;
  - si no, pone la cuenta a la línea y la marca como "puesta a mano" (−1).
  
  Si la línea ya estaba exportada, pregunta una vez si se reexportan; si se acepta, se reexportan las modificadas al final.
- **Enviar a cuenta de gasto (6XX)**: abre el selector filtrado por `6*` (o la última cuenta 6 usada), pone la cuenta a las líneas seleccionadas como "puesta a mano" y **las exporta sin preguntar**.
- **Desasignar cuenta contable**: quita la cuenta y la palabra clave de la primera línea seleccionada, previa confirmación. Si estaba exportada, borra antes su asiento. Solo se activa si la línea tiene cuenta.
- **Buscar similitud**: compara el concepto de la línea con todas las que tienen cuenta y, si alguna se parece al menos un 50 %, abre la lista de movimientos parecidos (no exportada) con la cuenta a la que se imputaron. Si se elige una, la asigna como en "Asignar cuenta manualmente".
- **Buscar por importe**: abre la búsqueda de facturas y pagos por importe (no exportada) con las líneas seleccionadas, en ventana no modal, para encontrar el cliente o proveedor de un cobro o pago.

### 4.3 Dividir y traspasar

- **SPLITEAR línea N43**: divide una línea en varias, para imputarla a más de una cuenta. Pide, una a una, el importe de cada fracción (absoluto o en %, propuesto con lo que queda) y su descripción, hasta cubrir el importe. Resultado:
  - una cabecera nueva "SPLIT Línea N43 n" con una línea por fracción, que va de la **cuenta 555 de split** del banco a la cuenta que se le asigne;
  - la línea original pasa a la 555 de split, queda "puesta a mano" y su concepto empieza por "(SPLIT)".
  
  Así el banco ve un único movimiento y la 555 queda a cero cuando se exportan todas.
- **SPLIT piso**: pide un inmueble y divide cada línea seleccionada en dos con su porcentaje: "Valor vivienda" a la cuenta del inmueble y "Valor terreno" a la del terreno. Sirve para la compra de un inmueble; el propietario la deja fuera por ahora (funciona).
- **Traspaso entre bancos**: exige seleccionar **exactamente dos** líneas con el mismo importe y sentidos contrarios (se activa solo con dos seleccionadas). Pone a las dos la cuenta puente fija `5729999` y las vincula entre sí, de modo que los dos asientos se compensan en esa cuenta.

### 4.4 Contabilidad

- **Exportar a contabilidad**: genera el asiento de las líneas seleccionadas (§7). Con una sola línea pregunta antes; si ya estaba exportada, pregunta si se reexporta. Con varias no pregunta. **Sin selección, exporta toda la lista.**
- **Borrar exportación contable**: tras confirmación, borra el asiento de las líneas seleccionadas exportadas y las deja como no exportadas. Solo se activa si la línea está exportada.
- **Exportar todo**: tras confirmación, exporta todas las líneas no exportadas (y las aparcadas en 555 que ya tienen cuenta) del ejercicio activo, con un contador de progreso. Para con el primer error.
- **Ver asiento/s**: abre los asientos de las líneas seleccionadas (formulario de asiento, no exportado).
- Después de cualquier exportación, los apuntes generados por recibos domiciliados toman como documento el de la línea.

### 4.5 Remesas y facturas

- **Explota Remesas**: muestra un aviso pendiente ("TODO") y desglosa las líneas de remesas de recibos SEPA en una línea por recibo (§6.2), incluidos los recibos devueltos. Según el propietario (29/10/2026), el desglose completo funciona en otro formulario (`frmN43Importar`, no exportado): busca las facturas con vencimiento próximo cuya suma cuadra con la línea, las marca como conciliadas, crea una remesa N43 ficticia de un banco 572 ficticio con una línea por factura y su titular, las lleva a la 430 de cada cliente y pasa la línea original de la 572 del banco a la 572 ficticia. Aquí quería ofrecerlo solo para la línea seleccionada y quedó a medias.
- **Asignar Numero Factura** (masivo): busca, para cada línea exportada sin factura, una factura de venta del ERP del mismo cliente con el mismo importe (±0,05 €) y fecha anterior, y si hay una sola, la anota en la línea y en el documento del apunte. En la práctica no se puede usar: el código se para en el depurador (§11). Según el propietario ya no tiene uso: lo sustituye la conciliación automática por importes, que busca facturas de compra y venta que cuadren por fecha e importe, asigna el número de factura y lleva la línea al proveedor o cliente.
- **Asigna #Factura**: pide un número de factura (propone el que ya tenga alguna de las líneas) y lo pone a todas las seleccionadas. No toca el asiento.

### 4.6 Salir

Cierra la pantalla.

## 5. Ficha de una línea ("Detalle línea N43")

Se abre con doble clic en la lista, desde "movimientos parecidos" y desde el Mayor, en ventana modal. Muestra todos los campos de la línea, el banco, la descripción de la cuenta y el sentido ("2-DEBE" / "1-HABER"). Dos acciones:

- **Guardar**: aparece al escribir en el concepto; guarda el concepto corregido.
- **Reasignar**: cambia la cuenta de la línea **y del apunte ya generado**, sin regenerar el asiento. Comprueba que la línea tenga asiento y que el apunte con esa cuenta exista.

## 6. Reasignación automática

### 6.1 Pasos

"Asignar IdPalabraClave", el alta de una palabra clave y su cambio de cuenta lanzan este proceso, para todas las palabras clave o solo para una:

1. **NIF de la palabra clave**: si su cuenta es de un proveedor o un cliente, copia su NIF.
2. **Palabra clave de cada línea**: una línea toma la palabra clave si coinciden la entidad bancaria y el sentido, y su concepto contiene el texto de la palabra clave (y el segundo texto, si lo tiene). Solo se aplica a líneas **desde el 1/12/2021**. Si varias encajan, se queda una cualquiera.
3. Asignación de cliente y factura por **máscara de número de factura** de la palabra clave: está desactivada por lenta.
4. **Línea de referencia** de la palabra clave: la primera línea que la usa, para calcular similitudes.
5. **PayPal**: los cargos de PayPal toman la palabra clave del correo del pagador según la tabla de transacciones PayPal.
6. **Cuenta del banco** de cada línea, según la tabla de entidades bancarias (las cuentas bancarias nuevas se dan de alta ahí sin cuenta).
7. **Cuenta de destino**: las líneas sin cuenta toman la de su palabra clave. **Nunca se sobrescribe** una cuenta ya puesta.
8. **Similitud**: porcentaje de parecido del concepto con el de la línea de referencia.

### 6.2 Remesas SEPA

Una remesa de recibos domiciliados llega al banco como una sola línea, con una palabra clave de tipo remesa. El desglose completo está en la pantalla de importación (`frmN43Importar`, botones "Explota Remesas Bancarias", "Explota Devoluciones Bancarias" y "Des-Explota Remesas Bancarias") y trata todas las remesas sin desglosar del ejercicio, o las de un fichero:

1. Para cada línea de remesa busca las **facturas de contrato** del mismo mes cuyos clientes tienen un esquema de cobro fraccionado con el día de cargo a **±3 días** de la fecha de la línea, agrupadas por esquema, y se queda con el grupo cuya suma más se acerca al importe.
2. Crea una cabecera N43 nueva ("Remesa …") de un banco ficticio, con **una línea por factura** con su titular, llevada a la 430 de cada cliente, y marca las facturas como cobradas.
3. La línea original queda "explotada" y pasa de la cuenta del banco a la cuenta del banco ficticio, que queda a cero cuando se exportan las nuevas.

Los recibos devueltos se tratan igual (varios recibos que suman el cargo, como mucho de 180 días antes). "Des-Explota" borra la cabecera ficticia, sus líneas y sus asientos, y libera los recibos. En `frmLineasN43` el botón quería hacer lo mismo solo con la línea seleccionada y quedó a medias.

## 7. Asiento que genera una línea

- **Dos apuntes**: la cuenta de destino y la cuenta del banco, por el importe de la línea. Un cargo (1) va al debe de la cuenta de destino y al haber del banco; un abono (2), al revés.
- **Sin cuenta de destino**, la línea se exporta a la **cuenta 555 temporal** de su banco y queda marcada "Exportada 555". Cuando más tarde recibe cuenta, "Exportar todo" la reexporta.
- **Conceptos**: "Pago desde / Ingreso en *banco*" y "Pago a / Pago de *destino*" ("Aparcamos en *555*" si va a la temporal). Los recibos devueltos usan "Recibo devuelto por…", "Desde…" y "Hacia…".
- **Documento**: el número de factura de la línea.
- **Fecha**: la de la línea; si es anterior al ejercicio activo, el **2 de enero** del ejercicio activo (el 1 se reserva para apertura y regularizaciones).
- **Reexportar** borra el asiento anterior y lo vuelve a crear **con el mismo número**.
- Faltar la cuenta del banco en la tabla de entidades bancarias es un error que detiene la exportación.
- Las líneas marcadas "no exportar" no se exportan, salvo que estén vinculadas a otra (traspaso), en cuyo caso se exporta la otra.

## 8. Importación del fichero (fuera de esta pantalla)

`C43.ImportarC43` lee el fichero Norma 43 línea a línea con el formato que describe la tabla `N43Esquema` (registros 11 cabecera, 22 movimiento, 23 conceptos adicionales, 33 final). Junta los conceptos en el concepto completo, convierte el importe a euros y la fecha valor a fecha, guarda el fichero y el ejercicio en la cabecera, crea un lote de importación y calcula los totales de control. No comprueba si el fichero ya se importó (hay un procedimiento aparte para borrar duplicados).

## 9. Teclado y eventos

- Clic en la cabecera de columna: ordenar (§2).
- Seleccionar texto en la caja de concepto: prepara el alta de palabra clave (§3).
- Doble clic en una línea: ficha (§5).
- Otras teclas de la lista: función común `HotKeyLista` (no exportada).
- Al recargar, la lista intenta quedarse en la misma posición (por número de fila, no por línea).
- Los procesos largos escriben su avance en la barra de estado ("Actualizando palabras clave", "Exportando n / m"…), que se borra a los dos segundos.

## 10. Reglas de negocio implícitas

1. Todo es por **sociedad seleccionada** y **ejercicio**: la línea pertenece al ejercicio de su cabecera.
2. Palabra clave = sociedad + entidad bancaria (4 dígitos) + sentido + texto contenido en el concepto (+ segundo texto opcional). En la palabra clave, D (debe del banco en contabilidad) corresponde a los abonos (2) y H a los cargos (1).
3. Estado de la imputación en `IdPalabraClave`: sin palabra / palabra clave / puesta a mano (−1). Lo puesto a mano no lo pisa la reasignación.
4. La cuenta de una palabra clave se hereda solo en las líneas sin cuenta; cambiarla arrastra las líneas que tenían la cuenta antigua o ninguna y, si se pide, los apuntes.
5. Cuentas puente por banco (tabla de entidades bancarias): **555 temporal** (exportar sin cuenta), **555 de split** (divisiones) y cuenta de gastos de divisa (prevista, no usada).
6. Cuenta fija **5729999** para traspasos entre bancos propios; cuentas **6*** como propuesta para gastos directos.
7. Tolerancias: ±0,50 € en búsqueda aproximada, ±0,02 € en búsqueda exacta, ±0,05 € al cuadrar una división y al asignar facturas en masa; 50 % de similitud para proponer parecidos.
8. Fecha mínima del 1/12/2021 para asignar palabras clave.
9. Una línea exportada conserva su número de asiento al reexportarse.

- En Access no había menú contextual: por eso hay tantos botones. La pantalla nueva llevará al menú contextual todo lo que tenga sentido, según la selección (una línea, varias o exactamente dos).

## 11. Defectos detectados (no reproducir)

**Código muerto**: búsqueda antigua de pagos y facturas por importe (sustituida por la ventana de búsqueda); fusión automática de traspasos (detrás de una salida); botón duplicado "Exportar todo" sin acción; asiento único para traspasos vinculados (condición siempre falsa); asignación por máscara de factura (desactivada).

**Fallos de comportamiento**:
1. Ayudas de los botones copiadas mal: "Importar N43" en Añadir palabra, Refrescar y Calcular similitudes; "No exportadas" con la ayuda de "Exportadas 555"; "Salir" dice "Deshacer este asiento manual".
2. En el código, las casillas se combinan con O, no con Y (el propietario las recordaba como Y; comprobar en la aplicación), y el rótulo dice "Sin palabra cuenta" y "SEPA exportadas" en lugar de lo marcado.
3. "Pendientes reexportar" anula las otras casillas sin indicarlo; la búsqueda también.
4. "Exportar a contabilidad" sin selección exporta toda la lista sin preguntar.
5. La comprobación de "fracción mayor que lo que queda" en SPLIT nunca salta; si las fracciones no cuadran, la división se para en el depurador.
6. Según una nota del propio código, la cuenta de banco de las líneas divididas puede acabar en la 572 en lugar de la 555 de split.
7. "Asignar cuenta manualmente" con varias líneas cuya palabra clave no tiene cuenta solo trata la primera y no ofrece reexportar.
8. "Desasignar cuenta contable" solo trata la primera línea seleccionada.
9. "Buscar similitud" pone a cero la similitud de **todas** las líneas de la base (todas las sociedades) y no se excluye a sí misma de forma fiable; "Calcular similitudes" para una sola palabra clave filtra mal y puede no recalcular nada.
10. "Asignar Numero Factura" masivo tiene paradas de depuración: no se puede usar.
11. "Explota Remesas" muestra un aviso "TODO" y procesa todas las remesas.
12. "Traspaso entre bancos" no comprueba que las cuentas sean de bancos distintos de la misma sociedad ni las fechas.
13. Si varias palabras clave encajan con un concepto, la que gana es arbitraria.
14. Años fijos 2021–2026 en el desplegable; fecha mínima fija del 1/12/2021.
15. La importación no detecta un fichero importado dos veces.
16. Al recargar, la posición se recupera por número de fila, así que tras filtrar u ordenar queda seleccionada otra línea.
17. Los textos de los conceptos del asiento parecen cruzados: "Pago desde *banco*" va en el apunte de destino y "Pago a *destino*" en el del banco (comprobar en la aplicación).
18. "Eliminar palabra clave" la borra sin dejar rastro ni posibilidad de deshacer.
19. Los mensajes son coloquiales y no siempre dicen qué ha pasado ("Something ha ido malamente…").
