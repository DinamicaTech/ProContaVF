# Nóminas de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmNominas`, de la clase `CTrabajadores` y de las consultas `ResumenNominas` y del resumen de la lista, más los módulos comunes ya exportados (`CAsiento`, `CApunte`, `CCuentacContable`, `IntegracionContaSol`). La exportación en bruto solo está en el clon local del propietario y no se sube al repositorio. Este documento describe **qué hace la pantalla y qué asientos genera**, no cómo está programada. Donde algo depende de un formulario que no se ha exportado (`frmSeleccionarCampoF_NOM`, `frmAsiento`, `frmSeleccionarCuenta`), se indica.

## 0. Contexto

- La pantalla no tiene título propio; su etiqueta principal dice **"Resumen Nóminas"**. Se abre solo desde el menú principal, botón **"Exportar nóminas"**, que solo está habilitado para el superusuario.
- Sirve para dos cosas: **importar** las nóminas de un mes desde una hoja Excel que prepara la gestoría (una fila por trabajador) y **contabilizarlas** generando los asientos según una **plantilla de apuntes** configurable.
- Tablas:
  - `F_NOM`: una fila por nómina (sociedad, fecha, trabajador, código y DNI del trabajador, departamento, tipo de paga, ~25 importes, cuenta contable del trabajador, número de asiento generado y texto del asiento). Contiene datos personales.
  - `AliasCamposF_NOM`: por empresa, qué cabecera del Excel corresponde a qué campo de `F_NOM`.
  - `PlantillasApuntes`: la plantilla de contabilización (ver §4).
  - `F_MAE` (plan de cuentas, para las cuentas 465 de cada trabajador) y `F_APU` (apuntes, ejercicio activo).
  - `Sistema` (sociedad seleccionada).

## 1. Lista "Resumen Nóminas"

Muestra, para la **sociedad seleccionada**, una fila por **fecha de nómina y tipo de paga**: sociedad, fecha, paga extra (sí/no), suma del bruto, suma del neto, número de nóminas, cuántas están contabilizadas y cuántas pendientes. Una nómina cuenta como contabilizada si tiene número de asiento.

- Orden: por fecha y, dentro de la misma fecha, primero la paga extra.
- Selección múltiple (para contabilizar varios meses de golpe).
- **F10** copia la lista al portapapeles (función común de listas).

## 2. Importar

1. Muestra una explicación: hay que seleccionar en Excel todas las celdas del resumen, **incluidas las cabeceras**, copiarlas y aceptar.
2. Lee el portapapeles. Si no contiene tabuladores, avisa ("no hace pinta de ser celdas de una hoja Excel") y sale.
3. Enseña el contenido en una lista provisional, en el sitio de la lista de resumen, y pregunta si "hace pinta de ser datos de nóminas".
4. Pide **el mes**. Propone el siguiente al último mes importado de la sociedad (máximo 12). Si se deja vacío o 0, sale ("Tú no me dices mes, yo no importo").
5. Si el mes es **julio o diciembre**, pregunta si es **paga extra**.
6. **Fecha de la nómina**: día **15** si es paga extra y día **25** si no, del mes indicado y del **ejercicio activo** de la sociedad.
7. **Traducción de columnas**: cada cabecera del Excel se busca en los alias de la empresa (`AliasCamposF_NOM`). Una cabecera vacía se llama "Columna N". Si una cabecera no tiene alias:
   - pregunta si se quiere asignar a mano;
   - **Sí**: abre `frmSeleccionarCampoF_NOM` para elegir el campo de `F_NOM`;
   - **No**: la columna se ignora;
   - **Cancelar**, o no elegir campo: se sale sin importar.

   La respuesta (campo o "ignorar") se **guarda como alias** de la empresa y no se vuelve a preguntar.
8. Inserta una fila en `F_NOM` por cada fila no vacía del Excel, con la sociedad, la fecha y el valor de cada columna en su campo (las celdas vacías quedan nulas).
9. Si no ha importado ninguna, lo dice. Si ha importado N, refresca la lista y **ofrece contabilizarlas en el momento**; si se acepta, las contabiliza (§4) y abre los asientos resultantes (§6).

Ejemplos de cabeceras que trae la hoja de la gestoría (se usan como alias): TRABAJADOR, BRUTO, DTO.RE.ESP, IN.RET.ESP, S.S.EMP., TC1, AUTONOMO, IRPF 111, NETO, ANTIC/OTRO, RET EMBARG, FALTA PREA, COST EMP, COS+PP EXT, DTO VAC, DIF.EXTRA, B.FORM.CON, H.E.FESTIV, REG.BASES.

**No hay control de duplicados**: importar dos veces el mismo mes crea las nóminas dos veces.

### Campos de importe de `F_NOM`

Bruto, descuento por retención especial, ingreso a cuenta por retribución especial, Seguridad Social empresa, TC1, autónomo, IRPF (modelo 111), neto, anticipos y otros, retenciones por embargo, falta de preaviso, coste empresa, coste con prorrata de pagas extra, descuento por vacaciones, diferencia de paga extra, bonificación por formación continua, horas extra festivas, regularización de bases, enfermedad/accidente, compensación de bonificaciones, bonificación FUNDAE, total coste Seguridad Social, total FLC-FPHIB, prorrata de pagas extra y cuotas obreras. Además: paga extra (sí/no), departamento, DNI, tipo de paga, cuenta contable del trabajador, número de asiento y texto del asiento.

## 3. Botones

- **Importar**: §2.
- **Exportar** (contabilizar):
  - Con **una** fila seleccionada pregunta "¿exportamos las nóminas del mes de <fecha>?" y contabiliza esa fecha.
  - Con **varias**, pregunta "¿exportamos las nóminas seleccionadas?" y contabiliza cada fecha seleccionada.
  - El proceso está en §4.
- **Deshacer**: pregunta "¿quieres deshacer la exportación de las nóminas del mes de <fecha>?" y, si se confirma:
  - **borra de `F_APU` todos los apuntes** de los asientos que tengan las nóminas de **esa fecha exacta**;
  - deja esas nóminas con asiento 0 (pendientes).

  Las nóminas importadas no se borran: se pueden volver a contabilizar.
- **Refrescar**: vuelve a leer la lista y ajusta el ancho de las columnas.
- **Doble clic en una fila**: abre en `frmAsiento` todos los asientos de las nóminas de **ese mes** de la sociedad (§6).

## 4. Contabilización ("Exportar")

Para una fecha de nómina, en este orden:

### 4.1 Plantilla

Lee **todas** las líneas de `PlantillasApuntes` ordenadas por `Orden`. Si no hay ninguna, avisa ("no tengo configurada la plantilla") y sale. El indicador **"agrupar apuntes"** se toma de la **primera** línea de la plantilla.

### 4.2 Cuenta 465 de cada trabajador

1. Rellena la cuenta contable de las nóminas de ese mes que no la tienen buscando en el plan de cuentas una cuenta cuya **descripción auxiliar** sea exactamente el **nombre del trabajador** tal como viene en el Excel.
2. Propaga la cuenta entre nóminas del mismo trabajador de la misma sociedad: primero por código de trabajador y después por DNI.
3. Recorre las nóminas del mes de la sociedad y, para cada trabajador, comprueba que exista su cuenta:
   - la busca por nombre (descripción auxiliar) y, si no, por la cuenta guardada en la nómina;
   - si no la encuentra, pregunta "¿quieres buscarlo manualmente?":
     - **Sí**: abre el buscador de cuentas filtrado por **465**. La cuenta elegida guarda el nombre del trabajador como descripción auxiliar, para encontrarla la próxima vez. Si no se elige ninguna, pregunta si puede crearla él.
     - **No**: pregunta "¿me dejas crearla a mí?". Si se acepta, entra en **modo automático**: para el resto de trabajadores sin cuenta ya no pregunta y las crea directamente.
     - **Cancelar**: sale.
   - **Crear la cuenta**: siguiente subcuenta libre de la **465** (busca el primer hueco en la numeración de 7 dígitos), con el nombre del trabajador como nombre y como descripción auxiliar.

   Las cuentas creadas en este paso **no se escriben en la nómina**. La nómina solo la recibe en la siguiente contabilización, cuando se repite la búsqueda por nombre del punto 1 (ver §8).

### 4.3 Cálculo de las líneas

Cada línea de la plantilla es una **fórmula sobre los campos de la nómina**:

| Campo de la plantilla | Qué es |
|---|---|
| `Orden` | Orden de la línea dentro del asiento. **La línea de orden 0 es la del trabajador**: su cuenta se toma como "cuenta del trabajador" para las líneas siguientes. |
| `FormulaDescripcion` | Expresión que da el concepto del apunte (puede combinar texto y campos de la nómina, por ejemplo el nombre del trabajador). |
| `FormulaCuenta` | Expresión que da la cuenta. Puede ser una cuenta fija, un campo (por ejemplo la cuenta contable del trabajador) o un **prefijo terminado en `?`** (ver abajo). |
| `DH` | Lado del apunte: D o H. |
| `FormulaImporte` | Expresión que da el importe a partir de los campos de importe (por ejemplo Bruto, IRPF111, SSEmp, Neto, o sumas y restas de varios). |
| `Condicion` | Opcional. Si existe y no se cumple para esa nómina, el importe de la línea es 0. |
| `AgruparApuntes` | Sí/No; solo cuenta el de la primera línea (§4.1). |
| `IdSociedad`, `TipoApunte` | Existen en la tabla pero **no se usan** (ver §8). |

Para cada nómina del mes que tenga cuenta contable y que **aún no tenga asiento**:

- Se evalúa cada línea de la plantilla. Las líneas con **importe 0 no generan apunte**.
- **Cuentas con `?`**: si la cuenta calculada termina en `?`, se toma lo que va antes como prefijo y se completa con los **últimos dígitos de la cuenta del trabajador** (la de la línea de orden 0) hasta llegar a su longitud. Ejemplo: con prefijo `4760` y trabajador `4650012`, sale `4760012`. Si esa cuenta no existe, **se crea**: nombre = descripción auxiliar de la cuenta de grupo de 3 dígitos del prefijo + nombre del trabajador.
- Si una línea da cuenta vacía, avisa ("busca por ahí a ver si encuentras la cuenta de …") y el proceso se detiene.
- Cada apunte lleva:
  - la fecha de la nómina y el concepto calculado;
  - sin documento y sin nota de asiento;
  - marcado como punteado;
  - una marca de libro de IVA a 1 (ver §8).

### 4.4 Asiento por trabajador o asiento único

- **Sin agrupar**: un **asiento por trabajador** con sus líneas.
- **Agrupando**: **un solo asiento** para todas las nóminas del mes. Las líneas se **acumulan por cuenta**: si dos trabajadores usan la misma cuenta, sus importes se suman en una sola línea. Si la cuenta ya existía en el lado contrario, se resta. Las cuentas de cada trabajador (465) quedan como líneas separadas porque son distintas.
- **Cuadre**: después de cada trabajador, si el asiento descuadra en más de 1 €, muestra "Descuadre: X" y el proceso **se detiene**. Por debajo de 1 € se graba descuadrado.
- **Al grabar**: sin agrupar, se graba el asiento de cada trabajador y se apunta su número en la nómina. Agrupando, se apunta el número en cada nómina y el asiento único se graba al final. El campo "texto del asiento" de la nómina se deja vacío.
- Al terminar muestra "Exportación finalizada!" en la barra de estado.

### 4.5 Ejemplo de plantilla típica (ilustrativo)

Una plantilla habitual de nómina en el plan general sería:

| Orden | Cuenta | D/H | Importe |
|---|---|---|---|
| 0 | cuenta del trabajador (465…) | H | Neto |
| 1 | 640 sueldos y salarios | D | Bruto |
| 2 | 642 Seguridad Social a cargo de la empresa | D | SSEmp |
| 3 | 4751 HP acreedora por retenciones | H | IRPF111 |
| 4 | 476 organismos de la Seguridad Social (o `476?` por trabajador) | H | TC1 o SSEmp + cuotas obreras |

Los valores reales de la plantilla son datos de la base y no se han exportado: hay que pedírselos al propietario o leerlos de la tabla.

## 5. Deshacer

Ver §3. Borra los apuntes de los asientos de las nóminas de **esa fecha** y las deja pendientes. No borra la nota del asiento, si la hubiera, ni las cuentas 465 creadas.

## 6. Ver los asientos (doble clic y al importar)

Reúne los números de asiento de las nóminas del **mes** (sin mirar el año) de la sociedad activa y abre `frmAsiento` con todos ellos, identificados con el **año de la fecha elegida**. Las nóminas sin asiento aportan un asiento 0.

## 7. Reglas de negocio implícitas

1. **Fechas de nómina**: día 25 para la nómina ordinaria y día 15 para las pagas extra (que solo se ofrecen en julio y diciembre), siempre del ejercicio activo.
2. **Cada trabajador tiene su subcuenta 465** ("remuneraciones pendientes de pago"), que se localiza por el nombre exacto que trae la gestoría y, en su defecto, por código de trabajador o DNI. Se crea sola si hace falta.
3. **Las cuentas "por trabajador" se derivan de la 465**: una línea de plantilla con `?` crea o usa una subcuenta del grupo indicado con los mismos dígitos finales que la 465 del trabajador.
4. **La correspondencia entre columnas de la hoja y campos es por empresa** y se aprende la primera vez que aparece una cabecera.
5. **La contabilización es configurable** sin tocar código: qué cuentas, qué importes, con qué condición y en qué lado. La plantilla debería ser por sociedad y por tipo de paga (la tabla ya tiene esos campos).
6. **Una nómina con asiento no se vuelve a contabilizar**: hay que deshacer primero.
7. **Tolerancia de cuadre**: 1 €.

## 8. Defectos detectados (no reproducir)

1. **La plantilla no se filtra por sociedad ni por tipo**: se usan todas las líneas de `PlantillasApuntes`, aunque la tabla tiene esos campos. Con dos empresas con plantillas distintas, se mezclarían.
2. **Filtro por mes sin año ni sociedad** en varios pasos:
   - la asignación de cuenta por nombre afecta a nóminas de cualquier sociedad y año de ese mes;
   - la selección de nóminas a contabilizar **no filtra por sociedad**, así que contabiliza también las pendientes de ese mes de otras sociedades, y además cambia la sociedad activa a la de la primera nómina;
   - el doble clic muestra asientos de ese mes de cualquier año.
3. **Deshacer filtra solo por fecha**, no por sociedad: borraría apuntes de nóminas de otras sociedades con la misma fecha. Además borra de la tabla de apuntes del ejercicio activo, sea cual sea el año de la fecha.
4. **Cuentas recién creadas sin asignar**: la cuenta 465 que crea o elige el proceso no se guarda en la nómina. En la primera pasada esas nóminas pueden quedarse sin contabilizar (o con cuenta vacía) y hace falta exportar otra vez.
5. **Paradas en seco**: con cuenta vacía o descuadre de más de 1 €, el programa se detiene a mitad, con parte de los asientos grabados (sin agrupar) o ninguno (agrupando), y sin forma ordenada de seguir.
6. **Descuadres de menos de 1 € se graban** sin línea de ajuste.
7. **Marca de IVA a 1** en todos los apuntes de nómina, como si estuvieran ligados al registro de IVA número 1.
8. **El texto del asiento nunca se guarda** (el campo existe, pero el proceso lo deja vacío).
9. **Importación sin control**: no detecta un mes ya importado, ni valida que los importes sean números, ni que el bruto menos retenciones sea el neto.
10. **Agrupar depende del orden de la plantilla**: el indicador se toma solo de la primera línea.
11. **La línea de orden 0 debe existir y tener importe**: si no, las cuentas con `?` se completan con una cuenta de trabajador vacía o de la nómina anterior.
12. **Búsqueda del trabajador por nombre exacto**: un cambio de formato del nombre en la hoja de la gestoría crea una 465 nueva para el mismo trabajador.
