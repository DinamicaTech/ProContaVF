# Menú principal de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmMain` ("-ProConta-"), de su clase propia `CAsientoManual` y de las consultas que usan sus botones (balances, diario, IVA, amortizaciones), más los módulos comunes ya exportados (`CAsiento`, `CCuentacContable`, `CFacturaVenta`, `CCliente`, `Utiles`, `IntegracionContaSol`). La exportación en bruto solo está en el clon local del propietario y no se sube al repositorio. Este documento describe **qué hace cada botón y, en los procesos que generan asientos, el algoritmo exacto**, no cómo está programado. Los formularios que solo se abren desde aquí y no se han exportado (`frmLogin`, `frmSeleccionaSociedad`, `frmSubCuentas`, `frmAsientos`, `frmAsientosManuales`, `frmCambioCuenta`, `frmExportacionFacturas`, `frmFacturasPendientes`, `frmN43CuentaPalabrasClave`, `frmSQL2VBA`, `frmIN`, `frmAsiento`) se indican.

## 0. Contexto

- `frmMain` es el **menú principal**: 38 botones sueltos, sin pestañas. La base no tiene formulario de inicio: el menú se abre a mano.
- Hace tres cosas:
  1. **abre las pantallas de trabajo**;
  2. **exporta informes a Excel**;
  3. **ejecuta directamente procesos contables** de cierre, regularización e IVA, que escriben asientos sin pasar por otra pantalla.
- Todos los procesos trabajan sobre la **sociedad y el ejercicio activos**: `F_APU` (apuntes), `F_SAL` (saldos por periodo), `F_MAE` (plan de cuentas), `F_OAS` (nota del asiento), `F_IVS`/`F_IVR` (libros de IVA). La apertura lee además `F_SAL<año anterior>`.
- **Registro de asientos generados**: varios procesos dejan constancia en `F_APUManual` (historial de apuntes manuales): asiento, fecha, cuenta origen y destino, lado, importe, notas, operador, referencia (campo y valor), ejercicio y tipo.
- **Saldos por periodo** (`F_SAL`): para cada cuenta, en sus niveles de 3, 4, 5 y 7 dígitos, el Debe y el Haber de 15 periodos:
  - periodo 0 = asiento 1 (apertura);
  - periodos 1-12 = meses, sin contar los asientos 1, 10000 y 20000;
  - periodo 13 = asiento 10000 (regularización);
  - periodo 14 = asiento 20000 (cierre).

  El **saldo "corriente"** que usan muchos procesos (consulta `F_Saldo`) es Debe − Haber de los periodos 0 a 12: incluye la apertura y excluye la regularización y el cierre.
- **Números de asiento reservados**: 1 (apertura), 10000 (regularización) y 20000 (cierre). El contador normal se los salta.

## 1. Entrada y permisos

- **Al abrir** pide identificarse (`frmLogin`) y no deja seguir mientras no haya operador; no hay forma de cancelar.
- Es **superusuario** el operador con permisos mayores que 1. Solo el superusuario tiene habilitados:
  - Cambiar de empresa;
  - Exportar facturas venta;
  - N43-Importar;
  - Exportar nóminas;
  - Asientos;
  - Cambiar subcuenta.

  Todo lo demás, **incluidos todos los procesos que generan asientos y el reseteo de la demo**, está disponible para cualquier operador.
- **Ctrl+F5 pulsado tres veces** (y a partir de ahí, cada vez) cambia el modo superusuario sin pedir nada.
- El botón de empresa muestra "Cambiar de empresa" y la sociedad activa.
- **Resetear demo** solo es visible en la sociedad de demostración (Id 22).
- **LOGIN** vuelve a pedir el operador y recalcula los permisos.
- **Salir**: el superusuario solo cierra el menú; el resto cierra la aplicación.

## 2. Botones

Tipos: **pantalla** (abre otra pantalla), **Excel** (exporta a Excel), **proceso** (genera o modifica asientos o datos contables directamente), **utilidad** (otras cosas). "Todos" = cualquier operador.

| # | Botón | Tipo | Quién | Qué hace |
|---|---|---|---|---|
| 1 | Cambiar de empresa | pantalla | Superusuario | Abre el selector de sociedad y refresca el menú. |
| 2 | Entrar facturas compra | pantalla | Todos | Entrada de facturas de proveedor (ver análisis de compras). |
| 3 | Exportar facturas venta | pantalla | Superusuario | Exportación de facturas de venta a contabilidad (`frmExportacionFacturas`, no exportado). |
| 4 | Mayor | pantalla | Todos | Libro mayor (ver análisis del Mayor). |
| 5 | N43-Movimientos bancarios | pantalla | Todos | Líneas de extractos bancarios Norma 43. |
| 6 | N43-Asignar cuenta a palabras clave | pantalla | Todos | Palabras clave de N43 → cuenta. |
| 7 | N43-Importar fichero N43/Tarjetas/Paypal | pantalla | Superusuario | Importación de extractos. |
| 8 | Exportar nóminas | pantalla | Superusuario | Nóminas (ver análisis de nóminas). |
| 9 | Salir | utilidad | Todos | §1. |
| 10 | SubCuentas Contable | pantalla | Todos | Alta y edición de subcuentas (modal). |
| 11 | Asientos manuales | pantalla | Todos | Historial y alta de asientos manuales. |
| 12 | Asientos | pantalla | Superusuario | Buscador de asientos. |
| 13 | LOGIN | utilidad | Todos | Cambiar de operador. |
| 14 | Cambiar subcuenta | pantalla | Superusuario | Sustituir una cuenta por otra en la contabilidad (modal). |
| 15 | REGENERA SALDOS CONTASOL | proceso | Todos | §3.12. |
| 16 | CONTASOL | utilidad | Todos | Lanza el programa de contabilidad externo ContaSol (acceso directo en la carpeta del programa), una sola vez por sesión. |
| 17 | Facturar Pistos turísticos (sic) | proceso | Todos | §3.11. Crea facturas de venta, no asientos. |
| 18 | Generar amortizaciones | proceso | Todos | §3.1. |
| 19 | Generar subcuentas inmuebles | proceso | Todos | §3.2. Crea cuentas, no asientos. |
| 20 | SQL 2 VBA | utilidad | Todos | Herramienta de desarrollo. |
| 21 | IN () | utilidad | Todos | Herramienta de desarrollo. |
| 22 | Balance PyG | Excel | Todos | §4. |
| 23 | Balance Situación | Excel | Todos | §4. |
| 24 | Salda cuentas IVA (trimestral) | proceso | Todos | §3.3. |
| 25 | Mata centimillos | proceso | Todos | §3.5. |
| 26 | Genera facturas pendientes recibir | proceso | Todos | §3.6. |
| 27 | Reparto dividendos | proceso | Todos | §3.7. |
| 28 | Liquidación de IVA (trimestral) | proceso | Todos | §3.4. |
| 29 | Reinicia cuadre Mayor | proceso | Todos | §3.13. Modifica el estado de cuadre, no asientos. |
| 30 | Listado Mayor | Excel | Todos | §4. |
| 31 | Listado Diario | Excel | Todos | §4. |
| 32 | Ratios | Excel | Todos | §4. |
| 33 | Asiento de apertura | proceso | Todos | §3.8. |
| 34 | Resetea datos de DEMO | proceso | Todos (solo visible en la demo) | §3.14. Borra datos. |
| 35 | Balance PyG Detallado | Excel | Todos | §4. |
| 36 | Asiento de cierre | proceso | Todos | §3.9. |
| 37 | Facturas de Compra Pendientes | pantalla | Todos | Lista de facturas de compra pendientes (no exportada). |
| 38 | Liquida ETFs | proceso | Todos | §3.10. |

Casi todos los procesos piden confirmación al empezar y, al terminar, abren los asientos generados en `frmAsiento`.

## 3. Procesos que generan asientos o datos

Reglas comunes:
- los asientos se graban en el ejercicio activo, con el siguiente número libre (salvo apertura y cierre);
- todos los apuntes nacen **punteados**;
- el texto de "notas" se guarda como nota del asiento.

### 3.1 Generar amortizaciones

**Lee**: regenera los saldos (§3.12) y recorre la consulta `CalculoAmortizaciones`. Cada inmueble (`Inmuebles`) tiene tres subcuentas asignadas por plantilla (`CuentasInmuebles` + `PlantillasInmuebles`):
- la del **activo** (221…);
- la de **amortización acumulada** (282…);
- la de **gasto** (682…).

Entran los inmuebles comprados en el ejercicio activo o antes cuyo **pendiente de amortizar** (saldo del activo + saldo de la 282, que es acreedor) sea positivo.

**Calcula**:
- Valor amortizable = saldo corriente de la cuenta del activo.
- Amortización = valor amortizable × **% de amortización** del inmueble. Si eso supera lo pendiente, se amortiza solo lo pendiente y se marca como **última amortización**.
- **Prorrateo del primer año** = (31/12 del ejercicio − fecha de compra) / 365, con un máximo de 1. Si es menor que 1, la amortización se multiplica por él.
- No usa el **porcentaje de construcción** del inmueble (separación suelo/construcción) ni el coste de compra: da por hecho que la cuenta del activo solo recoge la parte amortizable.

**Asiento** (uno por inmueble), con fecha **31/12** del ejercicio:

| Cuenta | Lado | Importe |
|---|---|---|
| Amortización acumulada (282…) | Haber | Amortización |
| Gasto de amortización (682…) | Debe | Amortización |

- Concepto: "Amortización <inmueble> <año>".
- Nota: "Amortización X% de <valor>", con "prorrateado a N% por primer año" o "Última amortización, amortizamos remanente" cuando corresponde.
- Se registra en el historial con referencia al inmueble.
- Al final dice cuántos asientos y el importe total, y los abre.

**Idempotencia**: **no la tiene**. Ejecutarlo dos veces el mismo año amortiza dos veces, salvo que la segunda vez lo pendiente ya sea 0.

### 3.2 Generar subcuentas de inmuebles

Para cada inmueble y cada línea de `PlantillasInmuebles` de su tipo (avisa si un tipo, salvo el 9, no tiene plantilla):

- **Si el inmueble aún no tiene cuenta para esa plantilla**:
  - toma la subcuenta más alta del plan que empiece por el prefijo de la plantilla y le suma 1 (si no hay ninguna, el sufijo es "01");
  - **crea la subcuenta** con:
    - nombre = nombre de la plantilla, con `[token]` sustituido por el nombre del inmueble;
    - descripción = la de la plantilla;
    - departamento y subdepartamento del inmueble;
  - la anota en `CuentasInmuebles`.
- **Si ya la tiene**:
  - actualiza el nombre y la descripción de la subcuenta según la plantilla;
  - si el nombre nuevo se parece menos de un 65 % al actual, el programa se para;
  - si la cuenta ha desaparecido del plan, avisa y se para.

Al final dice cuántas ha creado. No genera asientos. Es idempotente: una segunda ejecución no crea nada nuevo.

### 3.3 Salda cuentas IVA (trimestral)

**Pide** el trimestre (1-4; propone el activo).

**Lee**: para cada subcuenta **472…** y **477…**, el saldo de todos los apuntes del ejercicio activo con fecha hasta el final de ese trimestre (Debe − Haber). Es **acumulado desde enero**.

**Asiento** (uno por subcuenta con saldo distinto de 0), con fecha el **último día del trimestre**:

| Cuenta | Lado | Importe |
|---|---|---|
| La 472/477 | Haber si su saldo es deudor (soportado); Debe si es acreedor (repercutido) | Saldo absoluto |
| **6341000** | El contrario | Saldo absoluto |

- Concepto: "Regularización IVA Nº trimestre <nombre de la cuenta>".
- Nota: "Saldamos la cuenta de IVA de forma trimestral. Trimestres posteriores acumulan saldo de anteriores".
- Se registra en el historial.

**Idempotencia**: en la práctica sí. Después de saldar, el saldo acumulado al final del trimestre es 0 y una segunda ejecución no genera nada, salvo que entren apuntes nuevos con fecha de ese trimestre.

Efecto: deja las 472/477 a cero y lleva el neto a la 6341000. La **liquidación** (§3.4) lleva después ese neto de la 6341000 a Hacienda.

### 3.4 Liquidación de IVA (trimestral) con prorrata

**Pide**:
1. el trimestre (1-4);
2. la **prorrata**:
   - trimestres 1-3: la del año anterior, guardada en la sociedad (si es 0, avisa y sale);
   - 4.º trimestre: la calcula como total facturado de las facturas emitidas que tienen cuota en el primer tipo de IVA (total con IVA del libro de IVA repercutido) ÷ suma de importes de los apuntes de las cuentas 70…;
   - se puede corregir a mano y **hay que escribirla con "%"**, o no sigue.
3. si **regenerar la tabla de cálculo del IVA** (`BalancesIVACalculado`).

**Tabla de cálculo del IVA**: una fila por línea de base de cada registro de IVA enlazado con un apunte, con:
- trimestre, soportado o repercutido, tipo de IVA (con marca "ISP"), base, cuota y total (en negativo para el soportado);
- cuenta, factura y NIF;
- tipo de operación (nacional, intracomunitaria o importación, sacado de la factura de compra);
- **centro de coste**: la categoría de IVA del subdepartamento del apunte;
- tipo de deducción de esa categoría: no deducible, 100 % deducible o prorrata.

**Calcula** para el trimestre (no acumulado):
- **Repercutido** = IVA repercutido de los centros de coste **"T-INNOVA"** y **"Alquiler Locales"** + IVA soportado con inversión del sujeto pasivo (todos los centros) + IVA soportado intracomunitario. Estos dos últimos son la autorepercusión.
- **Soportado deducible** = IVA soportado **nacional** de los centros "T-INNOVA" y "Alquiler Locales".
- **Soportado con prorrata** = IVA soportado nacional del centro **"General"** + el de inversión del sujeto pasivo del centro "General".
- **Resultado** = repercutido − deducible − soportado con prorrata × prorrata.

Enseña el desglose y **deja cambiar el importe**. Positivo = a ingresar.

**Asiento** (fecha el último día del trimestre):

| Resultado | Líneas |
|---|---|
| A ingresar (> 0) | **6341000** Debe / **4750000** (HP acreedora por IVA) Haber |
| A devolver o compensar (< 0) | **4700000** (HP deudora por IVA) Debe / **6341000** Haber |

- Concepto: "Liquidación IVA N.º trimestre".
- La nota es el desglose del cálculo.
- Se registra en el historial.

**Idempotencia**: **no**. El propio mensaje avisa de que no comprueba si el trimestre ya se liquidó; repetirlo duplica la liquidación.

Nota: la parte de IVA soportado con prorrata que no se deduce **se queda en la 6341000** (como gasto), porque el saldo previo (§3.3) llevó todo el soportado a esa cuenta y la liquidación solo saca la parte deducible.

### 3.5 Mata centimillos

**Lee** el saldo corriente actual de todas las subcuentas de 7 dígitos, **sin regenerar saldos antes**. Coge las que tienen saldo distinto de 0 y entre −0,07 € y +0,07 €, salvo la propia 6690000.

**Asiento** (uno por subcuenta), con fecha **31/12**:

| Cuenta | Lado | Importe |
|---|---|---|
| La subcuenta | El contrario a su saldo (Haber si es deudor) | Saldo absoluto |
| **6690000** (diferencias por redondeo) | El otro | Saldo absoluto |

Concepto "Cuadre decimales por redondeo"; nota "Enviamos diferencias por redondeo a una 669". Sin historial.

**Idempotencia**: depende de que los saldos estén al día. Como no los regenera, ejecutarlo dos veces sin regenerar en medio **duplica** los asientos.

### 3.6 Genera facturas pendientes de recibir

**Lee**: regenera saldos. Coge las subcuentas de proveedores y acreedores (**40…, 41…**) de 7 dígitos con **saldo deudor** (se ha pagado más de lo facturado) que estén **marcadas como cuadradas** en el cuadre del Mayor.

**Calcula**: base supuesta = saldo / **1,21** (supone que todo lleva un 21 % de IVA).

**Dos asientos por cuenta**, con fecha **31/12**:

| Asiento | Debe | Haber | Nota |
|---|---|---|---|
| 1 | **4109000** | La cuenta del proveedor | "Pasamos una hipotética base imponible de 21 % a gastos" |
| 2 | **6299000** (otros servicios) | **4109000** | "Imputamos a una cuenta de gastos genérica; cuando entre la factura ya la derivamos a la que toca" |

Concepto: "Facturas pendientes de recibir en <año siguiente> - <cuenta> <nombre>". Sin historial.

Efecto neto: la 4109000 queda a 0 (entra y sale); el proveedor se abona por la base supuesta y se carga un gasto genérico. La **parte de IVA supuesta (21 %) se queda en el saldo del proveedor**.

**Idempotencia**: **no**. Como el proveedor sigue con saldo deudor (el 21 % que no se ha movido) y sigue marcado como cuadrado, una segunda ejecución vuelve a generar asientos sobre ese resto.

### 3.7 Reparto de dividendos

**Pide** el importe bruto total (IRPF incluido) y la fecha. Si la fecha no es válida, sale.

**Calcula**: retención = bruto × **19 %**. Se reparte **a partes iguales entre dos socios fijos**.

**Asiento** (uno), en la fecha indicada:

| Cuenta | Lado | Importe |
|---|---|---|
| **1130000** (reservas voluntarias) | Debe | Bruto |
| **5260001** (dividendo a pagar, socio 1) | Haber | Bruto / 2 |
| **5260002** (dividendo a pagar, socio 2) | Haber | Bruto / 2 |
| **4751011** (retención socio 1) | Haber | Retención / 2 |
| **5260001** | Debe | Retención / 2 |
| **4751012** (retención socio 2) | Haber | Retención / 2 |
| **5260002** | Debe | Retención / 2 |

- Concepto: "Reparto de dividendos <fecha>", con el nombre de cada socio en sus líneas.
- Nota: "Repartimos X euros entre los socios, de los cuales hay que restar el IRPF".
- Se registra en el historial.

**Idempotencia**: no; cada ejecución es un reparto.

### 3.8 Asiento de apertura

1. Regenera los saldos del **ejercicio anterior**.
2. Si ya existe el asiento **1** en el ejercicio activo, pide confirmación y **borra sus apuntes**.
3. Lee de los saldos del ejercicio anterior las subcuentas de 7 dígitos de los **grupos 1 a 5**, con saldo = periodos 0 a 13 (incluye la regularización 10000 y excluye el cierre 20000).
4. **Asiento 1**, con fecha **1/1** del ejercicio activo: cada subcuenta con saldo distinto de 0, al **Debe si su saldo era deudor** y al **Haber si era acreedor**. Concepto "Asiento de apertura <nombre de la cuenta>"; nota "Saldo inicial <año>".
   - Si una cuenta ya no existe en el plan y empieza por **260**, prueba con la misma terminación en **265**. Si tampoco existe, el programa se para.
5. Comprueba que el total cuadre. Si no, avisa del descuadre ("algo anda mal con los saldos del ejercicio anterior").
6. Abre el asiento 1.

**Idempotencia**: sí, porque borra y regenera. Pero el descuadre es esperable (ver §6).

### 3.9 Asiento de cierre

1. Regenera los saldos.
2. Si existe el asiento **20000**, pide confirmación y lo borra.
3. Lee las subcuentas de 7 dígitos de los **grupos 1 a 5**, con saldo de los periodos 0 a 13.
4. Crea, con fecha **31/12**, un asiento con cada subcuenta en el lado contrario a su saldo (Haber si es deudor), para dejarla a 0. Concepto "Asiento de cierre <nombre>"; nota "Asiento de cierre <año>". Misma regla 260 → 265.
5. Comprueba el cuadre y abre el asiento 20000.

**Atención**: el asiento se graba con el **número 1** (el de la apertura), no con el 20000 (ver §6). Tampoco hay ningún proceso que genere la **regularización** (asiento 10000, grupos 6 y 7 contra la 129).

### 3.10 Liquida ETFs

**Lee**: los "inmuebles" de **tipo 6** (inversiones en fondos cotizados), con tres cuentas de plantilla:
- la de la **inversión** (plantilla 1);
- la de **beneficios** (grupo 7, plantilla 7);
- la de **pérdidas** (grupo 6, plantilla 8).

Coge los que tienen el saldo de la cuenta de inversión **por debajo de 20.000 € en valor absoluto**: se da por hecho que esos se han vendido.

**Asiento** (uno por inversión con saldo distinto de 0), con fecha **31/12**:

| Saldo de la inversión | Líneas |
|---|---|
| Deudor (> 0): **pérdida** | Inversión Haber / cuenta de pérdidas Debe, por el saldo |
| Acreedor (< 0): **beneficio** | Inversión Debe / cuenta de beneficios Haber, por el saldo |

Concepto "Pérdida/Beneficio en inversión ETF <cuenta>"; nota "Ajuste pérdida/beneficio por ETF <año>". Sin historial. Al final da el número de asientos y el balance total.

**Idempotencia**: sí en la práctica, porque la cuenta queda a 0. Pero usa los saldos tal como estén, sin regenerarlos.

### 3.11 Facturar pisos turísticos

No genera asientos: crea **facturas de venta** (`FacturasVenta` + `LineasFacturaVenta`) a partir de las reservas de alquiler turístico (`ReservasPisosTuristicos`).

1. Calcula el año y el mes de cada reserva sin facturar a partir de su fecha de inicio.
2. Coge las reservas sin factura y las ordena por plataforma de origen, año, mes, alojamiento y fecha.
3. Busca:
   - el **piso** (alojamiento) en `DatosContacto`. Si falta alguno, copia su nombre al portapapeles, avisa y sale;
   - la **plataforma** (origen) en `Personas`. Si falta, pide el dato y el programa se para.
4. Solo factura reservas que empiezan **antes del mes en curso**.
5. Crea **una factura por plataforma + mes + alojamiento**:
   - número = el último + 1;
   - fecha = **hoy**;
   - concepto "Reserva <origen> <MES> - <nombre>";
   - el concepto de facturación sale del tercero del piso. Si no tiene, el programa se para.
6. Añade **una línea por reserva**:
   - descripción con origen, fecha, periodo y huésped, y el código de reserva;
   - cantidad = noches (mínimo 1);
   - precio = importe neto / noches;
   - coste = (comisión + limpieza) / noches;
   - comisión;
   - **IVA 0**.

   Recalcula los totales de la factura y marca la reserva como facturada.

**Idempotencia**: sí, porque las reservas facturadas ya no vuelven a entrar.

### 3.12 Regenera saldos

Borra los saldos por periodo del ejercicio y los **recalcula desde los apuntes** (niveles de 3, 4, 5 y 7 dígitos; periodos de §0). Dice cuántos segundos ha tardado. Lo llaman también amortizaciones, apertura, cierre, facturas pendientes de recibir y reinicio del cuadre. Es idempotente.

### 3.13 Reinicia cuadre del Mayor

Pensado para el **cambio de ejercicio**:
1. Opcionalmente (segunda pregunta) marca **todas** las cuentas del cuadre del Mayor como **no cuadradas** y añade una línea "-------" al principio de sus comentarios, como separador del ejercicio anterior.
2. Regenera saldos.
3. Marca como **cuadradas** las subcuentas de 7 dígitos con saldo corriente de ±0,05 € o menos (las crea en el cuadre si no estaban). No guarda fecha ni operador.
4. Dice cuántas ha marcado.

Es idempotente.

### 3.14 Resetea datos de DEMO

Solo funciona si la sociedad activa es la de demostración (Id 22, código 003, ejercicio 2025). Tras un aviso ("no hay Ctrl+Z que valga"):
- **vacía por completo** la tabla de apuntes, los dos libros de IVA, el historial de apuntes manuales y las notas de asiento;
- borra las nóminas de la sociedad 22;
- borra las facturas de compra de la demo sin fecha y desmarca como no exportadas las demás;
- borra las cabeceras N43 de la demo de 2025;
- desmarca la exportación contable de **todas** las facturas de venta exportadas desde 2025;
- desvincula del N43 los recibos y remesas desde 2025;
- borra los lotes de importación N43 desde 2025.

## 4. Exportaciones a Excel

Todas abren un **libro nuevo** de Excel, salvo Ratios, que usa una plantilla. Cada informe ocupa una hoja con el nombre de su tabla, cabeceras en la primera fila y una fila por registro. Si la consulta no devuelve nada: "No datos, no excel". Cada vez se anotan las columnas del informe en una tabla de formato de exportación. Todas trabajan sobre los apuntes del **ejercicio activo**.

| Botón | Hoja | Contenido |
|---|---|---|
| **Balance PyG** | DatosPyG | Un registro por fecha, asiento y subcuenta de **pérdidas y ganancias**. Cada cuenta de 3 dígitos se clasifica según un esquema configurable: tipo, apartado, título y subtítulo. Lleva la cuenta y la subcuenta, el importe con signo (Haber − Debe por el signo del esquema), el concepto, el centro de coste y el subdepartamento. Excluye los asientos 10000 y 20000 y los conceptos de cierre. Pensado como origen de una tabla dinámica. |
| **Balance Situación** | DatosSituacion | Igual, para las cuentas de **balance** (activo, pasivo, patrimonio), con el signo del grupo del esquema y además el departamento. |
| **Balance PyG Detallado** | DatosPyGDetallado | Para cada línea de PyG, las **contrapartidas** del mismo asiento según parejas fijas, para ver quién está detrás de cada gasto o ingreso. Por ejemplo: compras 600 → proveedor 40/41 o socios 55; servicios 62 → acreedores 4 o banco 572; sueldos 6400000 → 465/476/4751/755; ventas 7 → clientes 43; amortización 680 → 28. |
| **Listado Diario** | DatosDiario | Todos los apuntes de subcuentas de 7 dígitos: diario, fecha, asiento, orden, subcuenta y nombre, concepto, documento, Debe, Haber, saldo (siempre 0) y nota del asiento (255 caracteres). Ordenado por fecha y asiento. |
| **Listado Mayor** | DatosMayor | La consulta del mayor ya descrita en el análisis del Mayor. |
| **Ratios** | DatosPyG y DatosSituacion | Abre la plantilla **`d:\deporwin\indicadores.xlsx`**, vacía sus tablas "DatosPyG" y "DatosSituacion" y las rellena con los dos balances. La plantilla calcula los ratios. |

## 5. Reglas de negocio implícitas

1. **Calendario contable por números de asiento**: 1 = apertura (1/1), 10000 = regularización, 20000 = cierre (31/12). Los saldos por periodo se organizan así y el contador normal se los salta.
2. **Ciclo de IVA trimestral en dos pasos**:
   1. saldar las 472/477 contra la 6341000 a fin de trimestre;
   2. liquidar el neto de la 6341000 contra 4750000 (a ingresar) o 4700000 (a compensar o devolver).

   La parte no deducible por prorrata se queda en la 6341000 como gasto.
3. **Prorrata**:
   - en los trimestres 1-3, la definitiva del año anterior (guardada en la sociedad);
   - en el 4.º, la del año calculada con la facturación;
   - el IVA soportado se clasifica como 100 % deducible, con prorrata o no deducible según el **centro de coste** del subdepartamento.
4. **Cierre de ejercicio**:
   - amortizaciones a 31/12 prorrateadas el primer año;
   - céntimos residuales a la 6690000;
   - facturas pendientes de recibir de proveedores cuadrados con saldo deudor;
   - liquidación de ETFs vendidos;
   - asiento de cierre y apertura de los grupos 1-5.

   Las amortizaciones, los céntimos y las facturas pendientes deberían hacerse **antes** del cierre.
5. **Inmuebles e inversiones como fichas con plantillas de cuentas**: cada tipo de ficha (inmueble, ETF…) define qué subcuentas necesita (activo, amortización acumulada, gasto, beneficios, pérdidas). El sistema las crea con el nombre de la ficha.
6. **Cuentas a parametrizar** (hoy fijas en el código):
   - 6341000 (IVA a liquidar o IVA no deducible);
   - 4700000 / 4750000 (HP deudora / acreedora por IVA);
   - 6690000 (redondeos);
   - 4109000 y 6299000 (facturas pendientes de recibir);
   - 1130000 (reservas), 5260001/5260002 y 4751011/4751012 (dividendos y retenciones por socio);
   - la equivalencia 260 → 265 en apertura y cierre.
7. **Parámetros fijos a parametrizar**:
   - 21 % de IVA supuesto en facturas pendientes;
   - 19 % de retención de dividendos y reparto 50/50 entre dos socios;
   - 20.000 € como umbral de ETF vendido;
   - ±0,07 € para céntimos y ±0,05 € para cuentas cuadradas;
   - días 31/12 y 1/1;
   - los nombres de centro de coste "T-INNOVA", "Alquiler Locales" y "General";
   - la ruta de la plantilla de ratios;
   - la sociedad de demo (Id 22, código 003, 2025).
8. **Facturación de alquiler turístico**: una factura por plataforma, mes y piso, una línea por reserva, sin IVA, y solo de meses ya cerrados.

## 6. Defectos detectados (no reproducir)

1. **El asiento de cierre se graba como asiento 1**: el proceso borra el 20000 pero crea las líneas con el número de la apertura. Se mezclan con el asiento de apertura (mismo número y órdenes) y se pisa su nota. Después enseña un asiento 20000 que no existe. Su mensaje final es el de la apertura ("flamante saldo inicial").
2. **No existe la regularización** (asiento 10000, grupos 6 y 7 contra la 129). Por eso la apertura y el cierre, que solo miran los grupos 1-5, **descuadran por el resultado del ejercicio**, y el aviso de descuadre sale siempre que hay beneficio o pérdida.
3. **Procesos contables sin restricción**: cualquier operador puede lanzar amortizaciones, IVA, cierre, apertura, dividendos y el reseteo de la demo. Y **Ctrl+F5 tres veces da superusuario sin contraseña**.
4. **Login sin salida**: la ventana de identificación se repite sin fin si no se entra.
5. **Procesos no idempotentes sin aviso ni control**: amortizaciones, liquidación de IVA, facturas pendientes de recibir (que además se realimenta con el 21 % que deja en el proveedor), mata centimillos (no regenera saldos antes) y dividendos. Ninguno comprueba si ya se hizo.
6. **Cálculo de la prorrata del 4.º trimestre**:
   - el numerador es el total **con IVA** de las facturas con cuota en el primer tipo;
   - el denominador suma los importes de las 70… **sin distinguir Debe y Haber** (los abonos suman en vez de restar).

   La prorrata legal es operaciones con derecho a deducción / total de operaciones, sin IVA.
7. **Liquidación de IVA ligada a nombres de centro de coste fijos**: el IVA repercutido de cualquier otro centro, o de apuntes sin subdepartamento, **no entra** en la liquidación. El IVA soportado intracomunitario entra en el repercutido (autorepercusión) pero **no se deduce** en ninguna parte.
8. **Prorrata obligatoriamente con "%"**: si se escribe sin el símbolo, el proceso se cancela.
9. **Paradas en seco** (el programa se detiene) en vez de errores controlados:
   - cuentas que no existen (apertura, cierre, ETF, subcuentas de inmuebles);
   - personas o conceptos que faltan en la facturación de pisos;
   - nombres de subcuenta que cambian mucho;
   - consultas de IVA vacías.
10. **Facturas pendientes de recibir**: contabilidad dudosa. Pasa por la 4109000 en dos asientos que se anulan entre sí, deja el IVA supuesto en el proveedor y no revierte nada al año siguiente.
11. **Liquida ETFs** da por vendida cualquier inversión de menos de 20.000 €, aunque siga en cartera. Tampoco regenera saldos antes.
12. **Amortizaciones**:
    - la condición de cuenta de activo incluye un patrón con una errata ("como 218*") que nunca coincide, así que las instalaciones 218 no se amortizan;
    - el prorrateo usa 365 días fijos.
13. **Facturación de pisos turísticos**:
    - la factura se emite con fecha de **hoy**, no del mes facturado;
    - se crea con el piso como cliente; el cliente y el beneficiario correctos se asignan después, pero **no se guardan**;
    - el IVA está fijo a 0;
    - el propio código deja anotado que habría que facturar a la plataforma y no al piso.
14. **Reseteo de la demo con alcance excesivo**: vacía tablas enteras sin filtrar por sociedad, y desmarca facturas de venta, recibos, remesas y lotes N43 de **todas** las sociedades desde 2025.
15. **Historial con tipos equivocados**: las amortizaciones se registran con el tipo 6 y el IVA y los dividendos con el 7, números que en el historial significan "cambio de cuenta" y "puntear".
16. **Subcuentas de inmuebles**: con un prefijo de 4 dígitos y sin subcuentas previas, el sufijo "01" crea una cuenta de 6 dígitos.
17. **Mezcla de periodos en el saldo**: apertura y cierre incluyen el periodo 13 (regularización), pero el saldo corriente de los demás procesos no. Si algún día existe la regularización, unos procesos la verán y otros no.
18. **Ruta fija** de la plantilla de ratios en una unidad local (`d:\deporwin\`).
19. **Botón con errata** ("Pistos") y dos herramientas de desarrollo visibles en el menú (SQL 2 VBA, IN ()).
20. **El listado diario** muestra una columna de saldo siempre a 0.
