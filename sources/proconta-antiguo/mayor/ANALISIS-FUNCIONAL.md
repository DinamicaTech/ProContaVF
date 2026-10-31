# Mayor de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmMayor` (ProcontaEstructura.accdb), sus módulos y consultas; la exportación completa (formulario, 19 módulos, consultas y estructura de 41 tablas, con la credencial de uproc.io sustituida por un marcador) está en `sources/proconta-antiguo/mayor/` del clon local del propietario y no se ha subido. Este documento describe **qué hace la pantalla**, no cómo está programada. Donde algo depende de un formulario o función que no se ha exportado (`frmAsiento`, `frmModificarAsiento`, `frmCuadrarMayor`, `frmSeleccionarCuenta`, `frmLineasN43`, `HotKeyLista`…), se indica.

## 0. Contexto

- La pantalla se llama "Mayor" y se abre desde el menú principal. Muestra el **libro mayor de una o varias subcuentas** a partir de las tablas con formato ContaSol que guarda ProConta: `F_APU<año>` (apuntes), `F_OAS<año>` (notas del asiento), `F_MAE` (plan de cuentas), `F_SAL` (saldos por periodo), más las tablas propias `CuadreMayor`, `F_CLIAGRUPADOS`, `F_APUManual` (historial de apuntes manuales), `N43Lineas` y `SaldoClientesCalculado`.
- Cada ejercicio vive en su propia tabla (`F_APU2024`, `F_APU2025`…). Algunos cálculos usan la tabla sin año (`F_APU`, `F_SAL`), que corresponde al **ejercicio activo** de la sociedad.
- Es una herramienta de **revisión y cuadre de cuentas**: ver movimientos, puntear, asignar números de factura a cobros y pagos, corregir apuntes y dejar constancia de que la cuenta está cuadrada.
- Dos zonas: cabecera con filtros y opciones, y una tabla grande ("Mayor Contabilidad") con fila de totales y barra de botones debajo. A la izquierda, lista de cuentas recientes. Flechas a los lados para pasar a la cuenta anterior o siguiente.
- Quedan restos de una segunda tabla oculta ("Depor", el mayor del ERP DeporWin) para cuadrar clientes contra el ERP. Está desactivada (ver §6).

## 1. Cabecera: filtros y opciones

### 1.1 Selector de cuenta

Desplegable con seis columnas: **Cuenta, Descripción, Saldo neto** (sin apertura), **Saldo, Apuntes, Cuadrado** (Sí/No). Al elegir una cuenta:

1. Pasa a ser la cuenta actual y la vista se limita a ella.
2. Si tiene un **grupo** en `F_CLIAGRUPADOS` (cuenta base → relacionadas), la cuenta y sus relacionadas se añaden a la lista de cuentas recientes; al filtro solo se añade la elegida.
3. Se desmarca "Ver notas y cuenta destino".
4. Se recarga la tabla.

Con menos de 7 dígitos (cuenta de grupo), la vista muestra **todas las subcuentas que empiezan por ese código**.

Junto al desplegable:
- **Lupa**: abre el buscador de cuentas y carga la elegida.
- **`+`**: abre el buscador y **añade otra cuenta a la vista actual** (cliente y cuentas relacionadas juntos). Guarda la relación en `F_CLIAGRUPADOS`; cada cuenta solo puede pertenecer a un grupo (la base anterior se sustituye).
- **Rótulo**: códigos y descripciones de las cuentas de la vista, cortado a unos 200 caracteres.

### 1.2 Filtros de la lista de cuentas

Solo cambian qué cuentas aparecen en el desplegable, no los movimientos. Los saldos salen de `F_SAL` (ejercicio activo).

| Opción | Inicial | Qué hace |
|---|---|---|
| Solo subcuentas | Sí | Solo códigos de 7 dígitos. |
| Con saldo | No | \|saldo\| > 1 €, con apertura (periodos 0–12). |
| Sin saldo inicial | No | \|saldo sin apertura\| > 1 € (periodos 1–12). Tiene prioridad sobre "Con saldo". |
| Con movs | No | Pretende filtrar por movimientos; en la práctica, cuentas con fila en `F_SAL` (§6). |
| No cuadradas | No | Oculta las marcadas como cuadradas en `CuadreMayor`. |

### 1.3 Periodo: Desde / Hasta

- Desplegables de año (lista fija 2012–2026, 2025 por defecto). Si Desde > Hasta, el otro se ajusta. Cada cambio recarga.
- La tabla recorre los ejercicios en orden; entre años añade una fila en blanco y un separador `**** AÑO aaaa *****`.

### 1.4 Opciones de la vista de movimientos

| Opción | Qué hace |
|---|---|
| **Compacto** | Agrupa por **cuenta + número de factura** (sin factura, por asiento): primera fecha, primer asiento, primer concepto, suma Debe, suma Haber; ordenado por primera fecha. Factura y cobros en una sola línea. Sin apertura en años posteriores al primero; excluye el asiento 20000; no excluye los asientos de cierre por concepto. |
| **Muy compacto** | Como Compacto, pero **oculta los grupos que netean a cero** (\|Debe − Haber\| < 0,05): solo facturas con pendiente. |
| **Facturas pendientes** | Tras cargar, quita las facturas cuyo Debe − Haber suma cero en lo mostrado. `F2024001` y `2024001` cuentan como la misma. Las filas sin factura se mantienen. |
| **Ver notas y cuenta destino** | Cambia el origen de la columna Notas (§2). Cada cambio de cuenta la desmarca. |
| **Agrupar 700** | Sin compacto, agrupa por cuenta + fecha + factura (o asiento) + lado + tipo de IVA: una factura con varias líneas 700 sale en una línea por cuenta. En cuentas 43\*, en el Debe solo suman apuntes con IVA repercutido; un cargo sin IVA aparece como Haber negativo. |
| **Ver no punteados** | Solo apuntes no punteados. |

### 1.5 Reglas fijas de la tabla

- Se excluye el **asiento 20000** (cierre) y, en vista normal y "Agrupar 700", los conceptos "CIERRE DEL EJERCICIO" o "Asiento de cierre".
- Máximo **175 filas** en pantalla; todas se escriben en `Mayor.txt` (tabuladores) y, si hay más, ofrece abrirlo en el Bloc de notas para pegar en Excel.

## 2. Columnas

| Columna | Origen |
|---|---|
| CUEAPU | Subcuenta del apunte. |
| DESTINO | Contrapartida: primera línea del mismo asiento cuya cuenta no sea la del apunte. En asientos de varias líneas solo muestra una. |
| CDESTINO | Descripción de la cuenta destino. |
| FECAPU | Fecha (la primera del grupo si se agrupa). |
| ASIAPU | Asiento (el primero del grupo). |
| CONAPU | Concepto. |
| DOCAPU | Documento / número de factura. |
| Debe / Haber | Importe. La apertura (asiento 1) de años posteriores al primero, en blanco. |
| Saldo | Saldo acumulado en el orden mostrado, cruzando años; cada asiento 1 lo reinicia con el importe de la apertura. |
| PUNAPU | Punteado ("Sí" o vacío; dudoso, §6). |
| Notas | Sin "Ver notas": nota del asiento (`F_OAS`); si no hay y hay adjunto, "Factura <archivo>"; si no, descripción de la cuenta. Con "Ver notas": notas del historial de apuntes manuales o la nota del asiento contrapartida. |
| FechaCreacion | Fecha de creación del apunte. |

Fila **TOTAL** con Debe, Haber y saldo de lo mostrado, más los **comentarios de cuadre** de la cuenta (`CuadreMayor.Comentarios`).

**Fila de totales inferior**: "Selección" (Debe − Haber de las filas seleccionadas, en vivo) y "Saldo" = Pagos − Conta (Conta = saldo del TOTAL; Pagos = pendiente de cobro según el ERP en `SaldoClientesCalculado`). Fondo **verde si \|Pagos − Conta\| < 2 €**, rojo si no.

## 3. Botones

"Fila seleccionada" = de la tabla, con selección múltiple. El ejercicio de cada fila sale del año de su fecha.

### Búsqueda y filtro
- **Busca factura seleccionada**: con el número de la última fila seleccionada (con o sin "F"), muestra esos apuntes **en todas las cuentas** de los años elegidos. Dura una recarga.
- **Filtra factura seleccionada**: muestra solo los documentos de las filas seleccionadas, **dentro de las cuentas de la vista**. Dura una recarga.
- **Busca importe X en N43**: abre las líneas Norma 43 filtradas por el importe de la selección y la cuenta actual (con Mayúsculas, importe exacto).
- **Busca importe X en asientos**: abre el buscador de asientos por importe y rango de años.
- **Refresca**: recarga.

### Ver
- **Ver Asiento**: abre los asientos de las filas seleccionadas (modal).
- **Ver N43**: visible si el asiento tiene línea Norma 43 asociada; la abre.
- **Ver asiento manual**: visible si el asiento está en el historial de apuntes manuales; lo abre.
- **Ir a SubCuenta destino**: carga el mayor de la cuenta DESTINO de la primera fila seleccionada.

### Modificar apuntes (sin pedir contraseña; escriben directamente en los apuntes)
- **Asigna número factura** (Mayúsculas = sin preguntas):
  - Sin selección (automático): para cada factura de **importe único** en la vista, busca un movimiento del mismo importe (±0,02 €) sin factura y le pone el número. "He numerado N pagos de facturas".
  - Con selección: propone el número de la primera fila con factura numérica; vacío = propone borrar el número; confirma ("¿asignamos a estos N asientos…?"). Sobrescribe sin aviso.
  - Escribe el número en los apuntes de la cuenta actual del asiento cuyo importe coincide, y en la **línea N43** vinculada.
  - Encadenado: ofrece numerar automáticamente las siguientes parejas factura-pago del mismo importe.
- **Modificar concepto**: de una en una fila; deja línea en el historial ("Modificación concepto") sin el concepto anterior.
- **Puntear/despuntear**: cambia el punteo del apunte de cada fila seleccionada (solo la línea de la cuenta, sin historial). Probablemente siempre puntea (§6).
- **Cambiar SubCuenta Origen**: con confirmación, cambia la cuenta del apunte en las filas seleccionadas y lo anota en el historial.
- **Cambiar SubCuenta Destino**: igual con la contrapartida.

### Crear asientos
- **Nuevo asiento manual**: abre el editor de asientos en alta con la cuenta actual y la suma Debe − Haber de la selección.
- **Nuevo asiento semi-manual** (asistente): desde la fila actual (o el pendiente de la selección), se elige el tipo:

  | Tipo | Resultado |
  |---|---|
  | Regularización de saldo inicial | Dos líneas contra la cuenta 113 de cuadres de la sociedad. |
  | Inverso | Invierte apunte y contrapartida; si es factura, todas las líneas y el registro de IVA inverso. |
  | Contra-apunte | Inverso + asiento igual al original con la cuenta de destino. |
  | Modificación | Cambia la cuenta del apunte original. |
  | Concepto | Cambia el concepto. |
  | Cambio de cuenta | Pasa el importe de la cuenta origen a la de destino. |
  | Tesorería | Contra la cuenta 570 de la sociedad. |
  | Pagado por socio | Contra la cuenta fija 5510001. |

  Fecha: 1 de enero del ejercicio activo; si rectifica una factura, primer día del trimestre activo; si el original es posterior, la del original. Todo queda en el historial con su motivo.
- **Asiento inverso**: copia todas las líneas de cada asiento seleccionado intercambiando Debe y Haber, con fecha de hoy y nota "Asiento inverso de N del AAAA". Sin IVA ni historial.

### Cuadre
- **Marcar como cuadrado**: verde si todas las cuentas de la vista están cuadradas, amarillo si alguna tiene comentarios, rojo si no. Recorre las cuentas: saldo ≈ 0 sin comentario → propone "Cuenta contable con saldo 0"; si es 40\*/41\*, "Cuadrada por caja" y la marca. Guarda en `CuadreMayor` (cuenta, cuadrada, comentarios, fecha, operador).

### Navegación
- **Flechas ◀ ▶**: cuenta anterior o siguiente del desplegable, respetando sus filtros.
- **Cuentas recientes** (selección múltiple): clic = mayor combinado de las seleccionadas; doble clic o X = deja solo la actual; clic derecho = vacía; Supr = quita la línea.

## 4. Teclado y eventos

- Al abrir, la tabla queda vacía hasta elegir cuenta.
- Clic en fila: recalcula "Selección" y el importe de los botones de búsqueda; muestra u oculta "Ver N43" y "Ver asiento manual".
- Doble clic: en DESTINO abre el mayor de esa cuenta; en otra columna abre el asiento.
- **Supr en la tabla**: quita las filas seleccionadas **solo de la vista**, para ir tachando lo que cuadra.
- Otras teclas: función común `HotKeyLista` (no exportada).

## 5. Reglas de negocio implícitas

1. Asiento **1** = apertura (reinicia el saldo; no suma en años posteriores). Asiento **20000** = cierre, nunca se muestra.
2. Factura con o sin "F" = el mismo documento.
3. Cuadre = marca manual por cuenta con comentario, fecha y operador; ayuda visual Pagos (ERP) − saldo < 2 €; 40/41 con saldo cero = "cuadradas por caja".
4. Punteo por apunte, solo la línea de la cuenta revisada.
5. Grupos de cuentas (`F_CLIAGRUPADOS`) para el mayor combinado; una relacionada, una sola base.
6. Rectificar en lugar de borrar: inversos, contra-apuntes y cambios, con historial y motivo.
7. Fechas de corrección para no tocar trimestres de IVA presentados.
8. Cuentas fijas: 5510001 (socios), 7000000 (por defecto); 113 y 570 configuradas en la sociedad.
9. Tolerancias: ±0,02 € al asignar facturas, ±0,05 € para netear, 1 € para "con saldo", ±1 € para localizar el apunte que se modifica.
10. Los NIF fijos del código no intervienen en el Mayor (son del lector de facturas de compra).

## 6. Defectos detectados (no reproducir)

**Código muerto**: tabla "Depor" del ERP y todo lo suyo; eliminar asiento; corrección de saldo inicial antigua; `zBorraMayorDuplicados1/2` (ya no existen en la base).

**Fallos de comportamiento**:
1. Con más de 175 filas, TOTAL y "Conta" solo suman lo mostrado, sin avisar.
2. Columna Punteado y botón Puntear/despuntear probablemente leen mal el estado (comprobar en la aplicación).
3. "Con movs" no filtra por movimientos.
4. La flecha ▶ se para en la penúltima cuenta.
5. Supr en recientes no quita la cuenta del historial.
6. "Busca importe en asientos" ignora las Mayúsculas; ayudas de los botones copiadas mal.
7. Asiento inverso duplica si se seleccionan varias filas del mismo asiento; no invierte el IVA.
8. Asigna número factura en mayor combinado solo escribe en la cuenta actual.
9. Notas, Ver N43 y Ver asiento manual buscan por asiento sin año en algunos casos.
10. El saldo acumulado no se recalcula al quitar filas.
11. Las casillas Compacto / Muy compacto pueden no reflejar el modo real.
12. Compacto no excluye los asientos de cierre por concepto.
13. Busca/Filtra factura se pierde en la siguiente recarga sin indicarlo.
14. Cambiar SubCuenta Origen sin selección da error y cambia la cuenta actual sin avisar.
15. Marcar como cuadrado guarda aunque no cambie nada; una cuenta sin movimientos no cuenta como saldo 0.
16. Localizar el apunte por importe con ±1 € puede tocar la línea equivocada.
17. Saldos del desplegable y del cuadre usan solo el ejercicio activo.
18. Años fijos 2012–2026 en los desplegables.
19. `Mayor.txt` se reescribe en cada recarga.
20. El historial de cambios de concepto no guarda el concepto anterior ni el nuevo.
