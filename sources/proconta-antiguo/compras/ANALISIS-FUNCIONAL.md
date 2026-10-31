# Entrada de facturas de proveedor de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmEntradaFacturas` ("Facturas Proveedores"), de sus módulos propios (`CArchivos`, `CSubDepartamento`, `Portapapeles`) y de los comunes ya exportados (`CFacturaCompra`, `CAsiento`, `CIVASoportado`, `CProveedor`, `CCuentacContable`, `CSociedad`, `IntegracionContaSol`). La exportación en bruto solo está en el clon local del propietario y no se sube al repositorio. Este documento describe **qué hace la pantalla**, no cómo está programada. Donde algo depende de un formulario que no se ha exportado (`frmAltaProveedor`, `frmSelectorCuenta`, `frmSelectorSubDepartamento`, `frmSeleccionaSociedad`, `frmAsiento`), se indica.

## 0. Contexto

- Es la pantalla de **registro de facturas de proveedor a partir de sus PDF**. Se abre desde el menú principal ("Entrar facturas compra") y desde la integración con ContaSol. Siempre ocupa toda la ventana.
- Flujo de trabajo:
  1. Los PDF se dejan en la carpeta de facturas de la sociedad.
  2. La pantalla da de alta un registro por PDF.
  3. El usuario abre cada uno, ve el PDF a la derecha y rellena o revisa los datos en el centro. Puede copiarlos del PDF o dejar que los extraiga solo.
  4. **Exportar** genera el asiento y el registro del libro de IVA soportado.
- Tablas:
  - `FacturasCompra`: una fila por PDF, con los datos de la factura, el estado de exportación, el asiento y el registro de IVA.
  - `F_PRO` (proveedores) y `F_MAE` (plan de cuentas).
  - `F_APU` (apuntes) y `F_IVS` (libro de IVA soportado), los dos del ejercicio activo.
  - `F_BAN` (formas de pago y bancos), `CodigosPais`, `CambioEuroDolar` (cambio diario).
  - `PalabrasClaveTipoGasto` y `PalabrasClaveSubDepartamento`: asignación automática por palabras.
  - `F_DEP` y `F_CDP` (departamentos y subdepartamentos o centros de coste), `Frases` (frases que se enseñan al guardar) y `Sistema`.
- **Al abrir**:
  - Comprueba el trimestre de la sociedad: si ya estamos en el trimestre siguiente al activo (con 10 días de margen en abril, julio y octubre), propone pasar al siguiente. Avisa de que, a partir de ahí, las facturas del trimestre anterior se contabilizarán con fecha del actual.
  - Pide la sociedad (`frmSeleccionaSociedad`). La cabecera muestra la sociedad, el trimestre activo (por ejemplo "abril-junio") y el ejercicio, en **verde** si la fecha de hoy cae en el trimestre activo y en **rojo** si no. Doble clic ahí cambia de sociedad.
  - Carga las dos listas.

## 1. Zona izquierda: pestañas de facturas

### 1.1 Pendientes

- Lista las facturas **no exportadas** de la sociedad, ordenadas por ruta del fichero.
- Columnas: ID, Fecha, NIF, Gasto (cuenta), Dep (subdepartamento) y Fichero (ruta relativa a la carpeta de la sociedad).
- Cada fichero lleva delante "+" si la factura ya tiene datos (proveedor, número, fecha y base distinta de 0) y "−" si no.
- El rótulo dice "Pendientes (N)". Admite selección múltiple.
- Botones de la pestaña:
  - **Recargar lista**: vuelve a leer la tabla.
  - **Recargar ficheros**: vuelve a recorrer la carpeta, incluidas las subcarpetas. Para cada PDF:
    - si ya está registrado con esa ruta, no hace nada;
    - si no, calcula su **huella MD5**; si coincide con un registro existente (el fichero se ha movido o renombrado), le actualiza la ruta;
    - si no coincide con ninguno, da de alta un registro nuevo con la ruta, la huella, la carpeta, el nombre del fichero, la sociedad y el ejercicio.
  - **Eliminar** (oculto normalmente): para cada seleccionada pregunta si borrar también el PDF. Se niega si la factura está exportada y pide confirmación si ya tiene importes. Borra el registro y, si se pidió, el fichero (antes comprueba que no esté abierto).
  - **Buscar pendientes**: pide un criterio:
    - `P` pendientes de rellenar (total 0);
    - `C` completadas;
    - `T` todas;
    - `f+texto` por nombre de fichero;
    - `K` sin centro de coste.

    La ayuda anuncia también "solo con suplido" e "importación", que en esta pestaña no hacen nada.

### 1.2 Exportadas

- Por defecto, las facturas **exportadas de la sociedad cuyo asiento es del ejercicio activo**, ordenadas por fecha de exportación (la más reciente arriba).
- Columnas: registro, fecha, NIF, número de factura, fichero, base, IVA, % de retención, fecha contable, tipo de operación (Interior/Import/Intra), inversión del sujeto pasivo (Sí/No) y ruta completa.
- **Clic en la cabecera** de una columna ordena por ella; un segundo clic invierte el orden.
- **Doble clic** recarga la lista.
- Botones de la pestaña: **Recargar**, **Buscar registro**, **Filtrar por proveedor**, **Deshacer exportación**, **Rehacer exportación** y sus dos variantes, y **Rectificativa** (§4).
- **Buscar registro** pide un criterio:

  | Criterio | Resultado |
  |---|---|
  | número | ese registro (y lo abre) |
  | `a+N` | la factura del asiento N |
  | `i+N` | las facturas cuyo asiento usa el registro de IVA N |
  | `R` | la factura actual y sus relacionadas (transporte / suministro) |
  | `S` | las que tienen IVA suplido en el libro de IVA |
  | `I` / `E` / `N` | intracomunitarias / de importación / nacionales |
  | `O` | con retención |
  | `J` | con inversión del sujeto pasivo |
  | `K` | sin centro de coste (sin cuenta de gasto, con la genérica 6000000 o sin subdepartamento) |
  | `Q+condición` | condición libre escrita a mano sobre la tabla de facturas |
  | `f+texto` | por nombre de fichero |

  Salvo el número de registro y `R`, los filtros mezclan exportadas y no exportadas de cualquier ejercicio.
- **Filtrar por proveedor**: abre la ficha de proveedores (`frmAltaProveedor`) para elegir uno y muestra todas sus facturas.
- **Zoom**: oculta el visor de PDF y ensancha la lista. Un segundo clic la devuelve a su tamaño.
- **Exportar** (de la lista): copia la lista al portapapeles para pegarla en Excel.

Al hacer clic en una factura de cualquiera de las dos listas, se abre (§1.3).

### 1.3 Abrir una factura

1. Lee el registro.
2. Si el PDF **ya no existe** en su ruta:
   - sin importes, ofrece borrar el registro;
   - con importes y sin exportar, ofrece borrarlo igualmente;
   - exportada, solo avisa ("un expediente X").
3. **Proveedor**:
   - si la factura no tiene proveedor, lo intenta deducir de la **carpeta**: si todas las facturas ya registradas de esa carpeta son del mismo proveedor, propone ese;
   - si ya tiene proveedor, lo carga.
4. **Extracción automática** (§2.4):
   - si la factura no tiene importes todavía, la lanza entera;
   - si ya los tiene pero le falta el subdepartamento, intenta solo el subdepartamento y guarda si lo encuentra.
5. Pinta los datos, enseña el PDF en el visor, prepara la lista de posibles facturas relacionadas y pone el foco en el NIF.

## 2. Zona central: datos de la factura

### 2.1 Campos

| Campo | Comportamiento |
|---|---|
| **NIF** (desplegable de proveedores con NIF) | Al pegar, quita guiones, espacios y puntos. Con Intro o Tab busca el proveedor (§2.3). Doble clic: busca la empresa por NIF en internet y ofrece darla de alta o actualizarla. Si se hace clic con el campo vacío, toma el NIF del portapapeles. |
| **Razón social** / **nombre comercial** | Solo lectura (cuenta del proveedor + nombre). |
| **País** | Al salir, guarda el país en el proveedor. Si no es España, oculta todos los tipos de IVA salvo el 0 %, la retención, los totales de base e IVA y el suplido. Al buscar el NIF, si el país está vacío se deduce: NIF de 9 caracteres → España; si no, por el prefijo de dos letras. |
| **VIES** | §2.5. |
| **Fecha factura** | Si se hace clic con la fecha de hoy, toma la fecha del portapapeles. Recalcula el vencimiento. |
| **Número factura** | Si se hace clic con el campo vacío, toma el portapapeles; con doble clic lo sustituye. Se limpia de barras, guiones, puntos y similares. Al cambiarlo, el concepto se actualiza solo si seguía siendo el propuesto. |
| **Concepto** | Propuesto: concepto por defecto del proveedor + número de factura. |
| **Tipo gasto** | Cuenta de gasto (6xx). Al lado se ve su descripción o "(¿?)". Propuesta: la de la factura o, si no tiene, la cuenta de gasto por defecto del proveedor. Doble clic: vuelve a buscarla por palabras clave (§2.4) y guarda. Ctrl+F5 bloquea o desbloquea el campo. |
| **Subdepartamento** (centro de coste) | Al lado, "departamento (subdepartamento)". Doble clic: vuelve a buscarlo por palabras clave y guarda. |
| **Importes** | Ver §2.2. |
| **IVA suplido** | IVA de aduanas pagado por un transitario en una importación. Si es distinto de 0, aparece el desplegable de **factura relacionada**. |
| **Factura relacionada** | Enlaza la factura del transportista con la del proveedor de la mercancía (importación con IVA suplido). Si la factura tiene IVA suplido, ofrece facturas de importación; si es de importación, facturas con IVA suplido. Solo salen las no exportadas o las ya enlazadas con esta. El botón de al lado salta a la factura enlazada. |
| **Aplicar retención** | Casilla. Muestra el % (por defecto **15**) y el tipo: "Servicios remunerados" o "Arrendamientos". Al cambiarlos, se guardan como valores por defecto del proveedor. |
| **Tipo operación** | Nacional / Importación / Intracomunitario. Al cambiarlo, se guarda en el proveedor. |
| **Tipo producto** | Servicios / Bienes. Al cambiarlo, se guarda en el proveedor. |
| **Inversión del sujeto pasivo** | Solo se ve si la factura tiene base pero IVA 0. Se marca sola si la operación es de importación. |
| **Forma de pago** | Banco o forma de pago (`F_BAN`) y su cuenta. Si el proveedor no tenía forma de pago, la guarda como suya; si tenía otra, pregunta si cambiarla. |
| **Vencimiento** | Calculado con los días o el día de vencimiento del proveedor. |
| **Notas factura** | Texto libre. |
| **Código de asiento / Fecha contable / Asiento rectificativo** | Solo si la factura está exportada (o rectificada). Doble clic en el código abre el asiento. |
| **USD** | Si la factura se convirtió de dólares, muestra el importe original. |
| **LOG** | Mensajes, con el más reciente arriba. Al abrir una factura muestra el texto del último asiento generado. |

### 2.2 Importes

Hay una **matriz de 5 columnas** con los tipos de IVA de la sociedad: exento (0 %), superreducido (4 %), reducido (10 %), normal (21 %) y 5 %. Cada tipo tiene su cuenta de IVA soportado (por ejemplo 4720001, 4720004, 4720010 y 4720021), que se configura en la sociedad. Cada columna tiene **base, IVA y total**:

- Al teclear la **base**, se calculan el IVA (base × tipo) y el total.
- Al teclear el **IVA**, se calcula la base (IVA / tipo) y el total.
- Al teclear el **total**, se calculan la base (total / (1 + tipo)) y el IVA.

Además están el **IVA suplido** y el **suplido** (gasto anticipado por el proveedor, sin IVA).

Totales:
- **Retención** = −(base total − base exenta) × % de retención.
- **Total** = base + retención + IVA + IVA suplido + suplido.

Comprobaciones automáticas al cambiar importes:
- Si hay IVA, el tipo de operación pasa a **Nacional**.
- Si el IVA es 0 y la operación era nacional, se deduce del país y del NIF (intracomunitaria o importación).
- Un proveedor "nacional" con NIF que empieza por N o Y (extranjero con NIF español) se trata como **importación**.
- Para proveedores extranjeros se ponen a 0 las bases con IVA distinto de 0.

### 2.3 Búsqueda y alta del proveedor por NIF

Se lanza al introducir el NIF (a mano o por captura).
- Si lo que se teclea acaba en el NIF del proveedor actual (se le ha añadido un prefijo de país), actualiza el NIF del proveedor directamente.
- Si no encuentra ningún proveedor con ese NIF, pregunta "¿vamos a buscar mejor, y si no, creamos un nuevo proveedor?":
  1. primero busca la empresa por NIF en internet y ofrece darla de alta;
  2. si no la encuentra, abre la ficha de proveedores (`frmAltaProveedor`) con el NIF, para buscarlo por nombre o crearlo a mano. Al volver, recarga la factura con ese proveedor.
- Si lo encuentra, carga sus valores por defecto:
  - tipo de operación y de producto;
  - retención;
  - cuenta de gasto;
  - forma de pago;
  - concepto;
  - país.

  Después pasa el foco a la fecha.
- **Lupa (F3)** y **Editar proveedor**: abren la ficha de proveedores para buscar por nombre, crear o editar. Al volver se valida el NIF.

### 2.4 Extracción automática del PDF

Convierte el PDF a texto con una utilidad externa y busca en ese texto:

1. **Subdepartamento**: la primera palabra clave de `PalabrasClaveSubDepartamento` que aparezca en el texto o en la ruta del fichero.
2. **Cuenta de gasto**: la primera palabra clave de `PalabrasClaveTipoGasto` que aparezca. Si no hay ninguna, la cuenta por defecto del proveedor.
3. **Proveedor** (si el NIF de la factura no aparece en el texto):
   - primero, cualquier NIF de proveedor que aparezca, ignorando el de la propia sociedad;
   - después, el nombre de un proveedor de más de 6 letras, salvo "Registro Mercantil…";
   - por último, el proveedor habitual de la carpeta.

   Comprueba también que el NIF de la sociedad aparezca en la factura; si no aparece, avisa ("no encuentro nuestro NIF en la factura").
4. **Importes** (solo nacionales): busca **parejas de números** del texto que encajen con un 21 % de IVA (base y total, base e IVA, o total e IVA, con 2 céntimos de margen) y se queda con la mayor. Hace una segunda pasada uniendo números partidos por un espacio. Si no encuentra ninguna, toma como total el **número con decimales que más se repite** y lo pone como base exenta.
5. **Fecha**: la primera fecha válida (en formato numérico o con el mes en letra) que no sea posterior a hoy + 15 días. Descarta las que van precedidas de "vto", "venc" o "giro". Se da por buena si va precedida de "fecha", "emisión" o "factura" (y de un par de textos fijos de un proveedor concreto).
6. **Número de factura**:
   - por **parecido con los números de facturas anteriores del mismo proveedor**: misma longitud o un carácter más, terminado en cifra, y cadenas parecidas;
   - si no, la palabra que sigue a "factura" (más de 5 caracteres y al menos 3 cifras);
   - si no, una palabra que empiece por "INV" (formato de Amazon).
7. **Inversión del sujeto pasivo**: si el texto contiene "inversión", "sujeto" y "pasivo" y la factura no tiene IVA.

Hay **reglas a medida para algunos proveedores**, identificados por su NIF: una plataforma de reservas, una de alquiler vacacional, la compañía de agua, una eléctrica y una de telefonía. Leen el total, las comisiones o el desglose (base reducida + canon exento, por ejemplo) de posiciones concretas de sus facturas.

Con mayúsculas + clic, o doble clic, en la ruta del fichero se abre el texto extraído en el Bloc de notas.

### 2.5 VIES

Consulta el NIF en el servicio VIES de la Comisión Europea. Si el NIF no lleva prefijo de país, usa el del proveedor; si tampoco hay, trata el NIF como español.
- **Proveedor español**: pregunta antes de consultar. El botón queda **verde** si el NIF es válido, **amarillo** si ha habido error de conexión y **azul** si no es válido.
- **Proveedor extranjero**:
  - válido → botón verde y operación **intracomunitaria**;
  - no válido → botón azul y operación de **importación**;
  - error de conexión → botón amarillo, sin cambiar la operación.

### 2.6 Conversión de moneda ($)

Solo se ve si la operación del proveedor es de importación.
- Convierte todos los importes de **dólares a euros** con el cambio de `CambioEuroDolar` **del día de la fecha de factura**. Si es la fecha de hoy, pregunta antes. Si no hay cambio para esa fecha, no hace nada.
- Guarda el importe original en dólares (se ve la etiqueta "USD …").
- Si se vuelve a pulsar, recupera el importe en dólares.
- Si se teclea otra base exenta, se olvida el importe en dólares.

### 2.7 Captura del portapapeles ("Capturando")

Pensada para rellenar la factura copiando del PDF sin teclear.
- El botón **Capturando / Sin captura** (o **F10** en el NIF o en el número de factura) activa o desactiva una lectura del portapapeles cada 0,3 segundos. Al activarla vacía el portapapeles.
- El campo que "toca" se marca en **verde**, en este orden: **NIF → fecha → número de factura → importe**. Hacer clic en un campo también lo marca.
- Cada vez que cambia el portapapeles (menos de 50 caracteres):
  - si se espera NIF o número y lo copiado es una **fecha**, va a la fecha;
  - se espera **NIF**: si tiene entre 7 y 14 caracteres, quita el prefijo "ES", espacios, guiones y puntos, busca el proveedor (§2.3) y pasa a la fecha;
  - se espera **fecha**: la limpia y, si es válida, la pone y pasa al número;
  - se espera **número**: si el campo está vacío y tiene entre 3 y 39 caracteres, lo pone y pasa al importe;
  - se espera **importe**: si es numérico (admite punto o coma decimal), lo pone como base y **apaga la captura**. La columna de IVA que recibe la base es la que el **proveedor usó en su última factura** (si usó varias, va pasando de una a otra en el orden exento, 4 %, 10 %, 21 %, 5 %; sin historial, la del 21 %).

### 2.8 Botones de cuenta de gasto y subdepartamento

- **Buscar tipo gasto**: abre el selector de cuentas de gasto (filtrado por 600). Después:
  - si el proveedor no tenía cuenta de gasto, la guarda como suya;
  - si tenía la genérica **6000000**, pregunta si ponerle la nueva y si **actualizar todos sus asientos anteriores**. Esto reemplaza la cuenta en sus asientos y en sus facturas de la sociedad;
  - en los demás casos, ofrece crear una **palabra clave** para que la extracción asigne esa cuenta sola.

  Si la factura tiene datos, la guarda.
- **Buscar sub-departamento**: abre el selector de subdepartamentos, guarda el elegido en la factura y ofrece crear una **palabra clave** para él.

## 3. Guardar y exportar

### 3.1 Guardar

Guarda todos los datos de la factura sin contabilizarla:
- proveedor, número, fecha, concepto, cuenta de gasto, forma de pago, vencimiento;
- importes por tipo, tipos de IVA, suplidos, retención;
- tipo de operación y de producto, inversión del sujeto pasivo, notas;
- departamento, subdepartamento, NIF, carpeta y nombre del fichero.

Si la factura enlaza con la del suministro (importación), también marca en esa factura el enlace inverso. La primera vez que una factura queda completa, la valida (§3.3) y enseña una frase de `Frases` en el LOG.

### 3.2 Exportar factura (F12)

Con varias pendientes seleccionadas, pregunta: **Sí** las exporta todas en cadena, **No** solo la actual. Para cada factura:

1. **Validación** (§3.3). Si hay avisos, se pueden ignorar.
2. Si la fecha es **hoy**, pide confirmación.
3. Si la base es **0**, pide confirmación.
4. Si la fecha es de un **año anterior** al ejercicio activo, pide confirmación. Si es de un **año posterior**, no deja exportar.
5. Exige el **número de factura**.
6. Si es la factura de **transporte** de una importación, en su lugar se exporta la factura del **suministro** enlazada, que lleva las dos (§3.5).
7. Si la factura ya está **exportada**, se niega. Si encuentra un asiento con la misma cuenta del proveedor y el mismo número de factura en la misma fecha o en el mismo año, avisa con el detalle del asiento y deja seguir "bajo tu responsabilidad".
8. Genera el **registro de IVA soportado** y el **asiento** (§3.4).
9. Marca la factura como exportada, con la fecha y hora de exportación, el asiento, la fecha contable y el texto del asiento.
10. Pone el **departamento y subdepartamento** de la factura en todos los apuntes de su asiento, salvo en la sociedad 001, que no usa departamentos. De paso completa el de cualquier apunte sin subdepartamento con el de su cuenta en el plan.
11. Comprueba que el libro de IVA no tenga **duplicados nuevos** (mismo NIF y número de factura). Si aparece uno, copia la consulta al portapapeles y avisa.
12. Limpia el formulario y recarga las listas.

### 3.3 Validación

- **Factura sin IVA de un proveedor nacional** (o sin país) que tiene **otras facturas del ejercicio con IVA**: aviso.
- **Posible duplicada**: otra factura del mismo proveedor y fecha con el mismo total (±0,02 €). Si además tiene el mismo número, el aviso es "ya has contabilizado esta factura"; si no, "mismo proveedor, fecha e importe, pero distinto número".

### 3.4 Asiento y libro de IVA que genera

**Fecha contable**:
- es la fecha de la factura;
- si la factura es de otro año, se usa el **2 de enero** del ejercicio activo;
- al rehacer "con fecha del trimestre actual", si la factura es de un trimestre anterior al activo, se usa el **primer día del trimestre activo**;
- al rehacer, se mantiene la fecha original; al rehacer con fecha determinada, la pedida (tiene que ser del ejercicio activo).

**Requisitos**: cuenta del proveedor y cuenta de gasto; la cuenta de la forma de pago solo si la factura se marcara como pagada, opción que está oculta.

**Registro de IVA soportado** (`F_IVS`):
- fecha, proveedor (cuenta, nombre, NIF), número de factura, tipo de operación;
- hasta tres bases con su tipo y cuota, más la base exenta;
- IVA suplido, retención (tipo, %, base = base total, importe);
- 100 % deducible, inclusión en el 347 (solo nacionales sin retención) y el concepto como nota.

Particularidades:
- **Importación**: lo que va al registro es la base al 21 %. Si hay IVA suplido, se calcula a partir de él.
- **Intracomunitaria**: base al 21 %.
- **Inversión del sujeto pasivo nacional**: la base exenta se registra como base al 21 %.

**Líneas del asiento** (concepto de la factura y número de factura como documento; la primera línea enlaza con el PDF y con el registro de IVA):

| Caso | Línea | Lado | Importe |
|---|---|---|---|
| Siempre | Cuenta del proveedor (40/41) | Haber | Total de la factura (con retención restada y suplidos sumados). En la rectificativa, en negativo. |
| Con retención | Cuenta de gasto | Debe | Base total (una sola línea) |
| | Retención: cuenta de **servicios** o de **alquileres** de la sociedad (4751…) | Haber | Importe de la retención |
| Sin retención, nacional o intracomunitaria | Cuenta de gasto, **una línea por tipo de IVA** con base | Debe | Base de ese tipo |
| Importación sin enlace de suministro, con inversión del sujeto pasivo | Cuenta de gasto | Debe | Base total |
| Importación sin enlace de suministro, sin inversión del sujeto pasivo | Cuenta de gasto | Debe | Total / 1,21 |
| | **4724001** (IVA importación) | Debe | Total × 0,21 / 1,21 |
| Nacional con IVA | Cuenta de IVA soportado de cada tipo (472…) | Debe | IVA de ese tipo |
| Nacional sin IVA con inversión del sujeto pasivo, e importación con inversión | Cuenta de IVA soportado por inversión del sujeto pasivo (sociedad) | Debe | Base × 21 % |
| | Cuenta de IVA repercutido por inversión del sujeto pasivo (sociedad) | Haber | Base × 21 % |
| Intracomunitaria | IVA intracomunitario **soportado** (cuenta de la sociedad según bienes o servicios) | Debe | Base × 21 % |
| | IVA intracomunitario **repercutido** (ídem) | Haber | Base × 21 % |
| IVA suplido | Cuenta de IVA suplido de la sociedad | Debe | IVA suplido |
| Suplido | Cuenta de suplidos de la sociedad | Debe | Suplido |
| Descuadre entre 0,01 y 0,05 € | **6690000** "Ajuste redondeo" | El que cuadre | Diferencia |

Si el descuadre es mayor de 0,05 €, avisa, pero **graba el asiento igualmente**.

### 3.5 Importación con transitario (factura relacionada)

Cuando la factura del proveedor de la mercancía tiene enlazada la del **transportista**, se genera **un único asiento** con las dos:
- las líneas de la mercancía (§3.4);
- **más** el transportista al Haber por el total de su factura, con su propio registro de IVA;
- su flete en la cuenta de gasto del transporte, una línea por tipo de IVA;
- el IVA nacional del flete;
- el **IVA de importación** que pagó el transitario (IVA suplido) al Debe de la **4724001**.

La factura del transporte queda marcada como exportada con el mismo asiento. Hay un ajuste previsto por diferencia entre el IVA de aduana y el 21 % de la mercancía, pero está desactivado.

## 4. Deshacer, rehacer y rectificar (pestaña Exportadas)

- **Deshacer exportación**: tras confirmar, borra los apuntes del asiento de la factura y su registro de IVA.
  - Se niega si el asiento quedaría descuadrado sin forzar; aquí se fuerza.
  - Borra también el asiento rectificativo, si existe, y la nota del asiento.
  - Deja la factura como no exportada.
  - Si la factura forma parte de un par transporte/suministro, deshace las dos, porque comparten asiento.
- **Rehacer exportación** (con la misma fecha), **… con fecha del trimestre actual** y **… con una fecha determinada** (se pide la fecha):
  - explican que se **mantiene el número de asiento**, se borran apuntes y libro de IVA y se vuelven a generar, y piden confirmación;
  - se aplican a todas las exportadas seleccionadas, de una en una, con el progreso en el LOG;
  - **no se permiten** en facturas con transporte o suministro enlazados: hay que deshacer y volver a exportar.
- **Rectificativa** (el botón también se rotula "Rehacer exportación"):
  - para una factura ya exportada que no se debió exportar (por ejemplo, duplicada), genera un **asiento nuevo igual al original con todos los importes en negativo**, y un registro de IVA también en negativo;
  - el concepto del proveedor lleva delante "(Rectificativa)";
  - guarda el asiento rectificativo en la factura;
  - si ya estaba rectificada, se niega.

## 5. Otros botones y teclas

- **Auto**: tras confirmar ("¿me sujetas el cubata?"), recorre las facturas del ejercicio y sociedad sin base y sin exportar, abre cada una (sin cargar el PDF), lanza la extracción automática y **guarda** lo que encuentre. Al final pide revisarlas.
- **Abrir PDF**: abre el PDF con su programa asociado.
- **F3**: buscar proveedor. **F10**: activar o desactivar la captura. **F12**: exportar (desde el NIF o el número de factura). **Ctrl+F5**: bloquear o desbloquear la cuenta de gasto.
- **Al cerrar**: libera la vigilancia del portapapeles.

## 6. Reglas de negocio implícitas

1. **Un PDF = una factura**. La huella MD5 identifica el fichero aunque se mueva o se renombre. La **carpeta** del PDF sugiere el proveedor.
2. **El proveedor aporta los valores por defecto**: tipo de operación y de producto, retención (% y tipo), cuenta de gasto, forma de pago, concepto, país y vencimiento. Cambiarlos en una factura los guarda en el proveedor, a veces sin preguntar.
3. **La cuenta de gasto genérica es la 6000000**. Las palabras clave asignan cuenta de gasto y centro de coste automáticamente a partir del texto del PDF o de su ruta.
4. **Tipo de operación**:
   - con IVA es nacional;
   - sin IVA y con NIF válido en el VIES es intracomunitaria;
   - sin IVA y no válido es importación;
   - NIF que empieza por N o Y es importación.
5. **Fecha contable y trimestres de IVA**:
   - una factura de otro año entra el 2 de enero del ejercicio activo;
   - una de un trimestre ya cerrado puede reexportarse al primer día del trimestre activo;
   - el trimestre activo avanza cuando ha pasado el trimestre (con 10 días de margen).
6. **Retención**: por defecto del 15 %, de servicios profesionales o de arrendamientos (cada tipo con su cuenta de la sociedad). Se calcula sobre la base no exenta.
7. **Autorepercusión**: en intracomunitarias e inversión del sujeto pasivo se registra el IVA al 21 % en soportado y repercutido a la vez. **El 21 % está fijo**, sea cual sea el tipo real.
8. **Importación**: sin transitario, se supone que el total incluye un 21 % de IVA de importación (4724001). Con transitario, el IVA de aduana suplido va a la 4724001 en el asiento conjunto.
9. **Duplicados**:
   - por proveedor + número + fecha o año en la contabilidad;
   - por proveedor + fecha + total en las facturas registradas;
   - por NIF + número en el libro de IVA.
10. **Departamentos**: los apuntes heredan el subdepartamento de la factura, o el de su cuenta. La sociedad 001 no usa departamentos.
11. **Cuentas a parametrizar** (hoy fijas en el código): 4724001 (IVA importación), 6690000 (redondeos), 6000000 (gasto genérico). Las de IVA por tipo, retenciones, inversión del sujeto pasivo, intracomunitarias y suplidos ya vienen de la sociedad.
12. **Tolerancias**:
    - 0,02 € para duplicados y para reconocer importes al extraer;
    - 0,05 € para cuadrar con línea de redondeo.

## 7. Defectos detectados (no reproducir)

1. **Asiento descuadrado grabado**: si el descuadre pasa de 0,05 € solo avisa y lo graba igual.
2. **Paradas del programa en producción**:
   - en facturas con IVA suplido o suplido, el proceso se para en seco antes de crear la línea ("pensemos un poco bien esto del suplido");
   - también se para ante algunas anomalías (registro de IVA duplicado, borrado que no borra, apuntes que no se han borrado al rehacer);
   - y en la regla de telefonía, que tiene una parada para revisarla.
3. **El número del registro de IVA no se guarda en la factura**: al guardar la exportación se reasigna a sí mismo y no toma el nuevo. Al rehacer se crea un registro nuevo en lugar de reutilizar el anterior.
4. **Deshacer exportación usa la factura cargada**, no la seleccionada en la lista: si no coinciden, deshace otra.
5. **Deshacer no limpia** el número de asiento ni el del rectificativo: la factura desexportada sigue diciendo que está "rectificada" y el botón de rectificativa se niega.
6. **Rectificativa con la fecha de la factura original**, que puede caer en un trimestre de IVA ya presentado (no usa el trimestre activo). Además, la factura sigue marcada como exportada sin indicar que está anulada.
7. **21 % fijo** en intracomunitarias, inversión del sujeto pasivo e importación, aunque el tipo real sea otro (10 %, 4 %…).
8. **Importación sin transitario**: separa un 21 % "dentro" del total aunque la factura extranjera no lleve IVA. Lo que se debe al proveedor es el total, pero el gasto queda minorado.
9. **Base de retención distinta** en el asiento (sin la exenta) y en el libro de IVA (base total).
10. **"Duplicado por importe"** en la contabilidad está calculado pero desactivado. La validación de duplicados no filtra por sociedad.
11. **Buscar pendientes** anuncia filtros ("con suplido", "importación") que no hacen nada. "Buscar registro" ofrece `i+` a medias, porque busca por asiento y no abre la factura.
12. **Botones con el mismo rótulo**: la rectificativa se llama "Rehacer exportación", igual que el de rehacer.
13. **Filtro libre `Q+`**: permite escribir una condición directamente contra la tabla. No debe existir en el sistema nuevo.
14. **Valores del proveedor que se guardan sin preguntar**: tipo de operación, tipo de producto, país, retención y la primera forma de pago.
15. **Reglas de extracción por NIF fijo** de proveedores concretos, y textos de un proveedor concreto en la detección de la fecha. En el sistema nuevo deberían ser plantillas configurables por proveedor.
16. **Validación de IVA 0** limitada al ejercicio y sociedad seleccionados, y la de duplicados a la misma fecha exacta: una factura duplicada con otra fecha no se detecta.
17. **"Factura ya exportada"**: si se intenta exportar una ya exportada, ofrece moverla a duplicados, pero esa opción está a medio hacer y el programa se detiene.
18. **Las rutas de las listas** se recortan con una longitud fija (31 caracteres) en la consulta guardada, mientras el código usa la longitud real de la carpeta de la sociedad.
19. **Eliminar** sale de toda la selección al primer "Cancelar" o al primer aviso.
20. **Captura del portapapeles**: puede rellenar un campo con cualquier cosa copiada en otra aplicación mientras está activa.
