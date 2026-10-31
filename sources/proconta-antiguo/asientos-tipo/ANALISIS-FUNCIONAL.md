# Asientos tipo de ProConta antiguo: análisis funcional

Fuente: la exportación de `frmModificarAsiento` y de los módulos que usa (`CApunte`, `CCuentacContable`, `CAsiento.RectificaAsiento`, `IntegracionContaSol`). La exportación en bruto (definición del formulario y código) solo está en el clon local del propietario y no se sube al repositorio. Este documento describe **qué hace la pantalla y qué asientos generan sus opciones**, no cómo está programado. Donde algo depende de un formulario que no se ha exportado (`frmAsiento`, `frmSeleccionarCuenta`, `frmAsientosManuales`), se indica.

## 0. Contexto

- La ventana se llama "Modificación de un asiento". Es un **asistente de corrección**: enseña un apunte concreto y deja elegir qué **asiento tipo de corrección** se quiere generar a partir de él (anular, mover a otra cuenta, regularizar un saldo inicial, pasar a tesorería…).
- **No graba nada por sí misma.** Al pulsar Ejecutar devuelve la elección (tipo, cuenta nueva, concepto nuevo, importe, nota) a la pantalla que la abrió, y es esa pantalla la que genera el asiento con la rutina común de rectificación (`CAsiento.RectificaAsiento`, §4).
- Tablas implicadas, todas a través de los módulos: `F_APU<año>` (apuntes; se lee el del ejercicio del apunte y se escribe en el del ejercicio activo), `F_OAS` (nota del asiento), `F_MAE` (plan de cuentas), `F_IVS`/`F_IVR` (libros de IVA, solo al anular facturas), `F_APUManual` (historial de apuntes manuales), `F_DIA` (contador de asientos) y la configuración de la sociedad (`Cuenta113ParaCuadres`, `Cuenta570Tesoreria`, ejercicio y trimestre activos).
- **Quién la abre** (siempre como ventana modal, nunca desde el menú principal):
  - El **Mayor** (`frmMayor`, botón "Nuevo asiento semi-manual"): le pasa asiento, ejercicio, cuenta, importe del apunte y el importe pendiente de la selección. Ver el análisis del Mayor, §3 "Crear asientos".
  - Los **Asientos manuales** (`frmAsientosManuales`, no exportado): le pasa los datos ya preparados (asiento, cuenta origen y destino, importe, ejercicio, notas, tipo y si se permite modificar) y la pantalla marca de entrada el botón del tipo recibido.

## 1. Zona de datos del apunte

Arriba se muestran, solo lectura: **Ejercicio, IdAsiento, Orden, Fecha, Importe, Debe/Haber, Cuenta** (código y descripción) y **Concepto origen**.

- El apunte se localiza por asiento, cuenta e importe del ejercicio indicado. La coincidencia de importe es aproximada: basta con que la parte entera coincida con ±1 € (la misma tolerancia que en el Mayor).
- **Clic en el concepto**: pide un concepto nuevo para el apunte original ("un concepto más útil"). Se muestra en cursiva hasta ejecutar. Al ejecutar, el concepto del apunte original se sustituye por el nuevo **sea cual sea el tipo elegido**.
- Debajo de los botones aparece el recordatorio "Recuerda que puedes modificar el concepto del apunte original" en cuanto se elige una opción.
- **Ver asiento**: abre el asiento completo en `frmAsiento` (modal).

## 2. Opciones (botones de alternar)

Son siete botones grandes; al elegir uno se desmarcan los demás y aparece un **ejemplo del asiento** que se va a generar (con cuentas y conceptos de muestra). Según la opción aparecen además los campos **Nueva cuenta** (con lupa que abre `frmSeleccionarCuenta`) e **Importe**.

Nota importante sobre los nombres: el texto del botón y el tipo interno que devuelve no coinciden en dos casos ("Nuevo apunte con otra cuenta" devuelve el tipo *contra-apunte* y "Contra-apunte" devuelve el tipo *cambio de cuenta*). La tabla usa el texto del botón y lo que realmente se genera.

| Botón | Pide | Asiento que se genera (detalle en §4) |
|---|---|---|
| **Modificar apunte** — "permite modificar la cuenta de este apunte (solo ejercicio activo)" | Nueva cuenta | Ningún asiento nuevo: **cambia la cuenta del apunte original**. Solo está habilitado si el apunte es del ejercicio activo y la sociedad está activa. |
| **Anular apunte** — "genera un asiento con los mismos apuntes pero debe/haber intercambiado" | Importe (se muestra, ver §6) | **Asiento inverso** del original. Si es una factura con varias líneas, invierte todas y genera el **registro de IVA inverso**. |
| **Nuevo apunte con otra cuenta** — "se puede combinar con la opción anterior" | Nueva cuenta | **Dos asientos**: el inverso del original y uno nuevo igual al original pero con la cuenta cambiada por la nueva. Al elegirlo se marca también "Anular apunte". Útil para corregir un asiento de un ejercicio anterior sin tocarlo. |
| **Contra-apunte** — "genera un asiento con este apunte invertido y un nuevo apunte con otra cuenta; útil para pasar un saldo de una cuenta a otra" | Nueva cuenta e importe | **Un asiento de dos líneas**: la cuenta del apunte en el lado contrario al original y la nueva cuenta en el mismo lado que el original, por el importe indicado. |
| **Ajuste saldo inicial a reservas voluntarias (113)** — "no sabemos por qué hay un saldo inicial, pero estamos seguros de que debería ser 0" | Importe | Asiento de dos líneas: la cuenta contra la **cuenta 113 de cuadres** de la sociedad. |
| **Apunte a tesorería** — "pago desde tesorería (caja) a/desde esta cuenta" | Importe; propone la nota "Pasamos pago a caja/tesorería" | Asiento de dos líneas: la cuenta en el lado contrario contra la **cuenta de tesorería (570) de la sociedad**. |
| **Apunte a deuda con socios (5510XXX)** — "pago de una factura de la empresa con dinero de un socio" | Importe; propone la nota "Pasamos pago deuda con socios" | Asiento de dos líneas: la cuenta en el lado contrario contra la cuenta fija **5510001**. |

### Petición del importe

Cuando la opción necesita importe, se pide en un cuadro de texto con la explicación: "si el saldo de la contabilidad es mayor del real (valor positivo), el importe en positivo (irá al HABER); si no, en negativo (irá al DEBE)". El valor propuesto es:
- en tesorería y socios, el importe del apunte en positivo;
- en el resto, el importe del apunte con signo positivo si el apunte está en el Debe y negativo si está en el Haber (es decir, lo que haría falta para dejarlo a cero).

En la opción de ajuste de saldo inicial, el ejemplo se rehace con el importe tecleado y la cuenta 113 real de la sociedad.

## 3. Ejecutar y Salir

- **Motivo de este cambio** (nota): es **obligatorio**. El campo trae un texto inicial que empieza por `**` y que se borra al entrar; si se deja así o vacío, Ejecutar muestra "Venga, primero tienes que documentar un poco este cambio..." y no sigue.
- **Ejecutar**: solo está habilitado si se ha elegido una opción y si la pantalla que la abrió permite modificar. Devuelve tipo, nueva cuenta, nuevo concepto, nuevo importe, importe pendiente y nota, y cierra.
- **Salir** ("Uf, qué miedito, sácame de aquí"): cierra sin devolver nada; no se genera ningún asiento.

## 4. Asientos que genera la rutina común de rectificación

Lo hace la pantalla que abrió el asistente (el Mayor o los Asientos manuales) con los datos devueltos. Reglas comunes:

- **Fecha**: 1 de enero del ejercicio activo. Si el asiento original es una factura (tiene IVA soportado o repercutido) y la opción es anular o "nuevo apunte con otra cuenta", se usa el **primer día del trimestre activo**, para no tocar trimestres de IVA ya presentados. Si el apunte original es posterior a esa fecha, se usa la del original.
- **Dónde se graba**: los asientos nuevos van siempre al **ejercicio activo**, aunque el apunte corregido sea de otro año. El número es el siguiente libre del diario (se saltan el 10000 y el 20000, reservados a regularización y cierre).
- Todos los apuntes nuevos se graban **punteados**. Desde el Mayor, además, se puntean el apunte original y su contrapartida.
- El **motivo** se guarda como nota del asiento nuevo y en el **historial de apuntes manuales** (`F_APUManual`), con referencia al asiento original y a su ejercicio.
- Si el asistente devolvió un concepto nuevo, se cambia el concepto del apunte original antes de generar nada.
- La **contrapartida** del apunte (necesaria para anular y para "nuevo apunte con otra cuenta") es otra línea del mismo asiento con un importe parecido (misma parte entera ±1 €) y cuenta distinta.

| Opción | Líneas que se crean |
|---|---|
| **Modificar apunte** | Ninguna. El apunte original pasa a la cuenta nueva. Historial: "Modificación subcuenta apunte (A por B): motivo". |
| **Anular apunte** (asiento normal) | Asiento nuevo con el apunte y su contrapartida, mismos importes y **Debe/Haber intercambiados**, concepto "Apunte inverso: …" y el mismo documento. Historial: "Asiento Inverso: motivo". |
| **Anular apunte** (factura sin contrapartida de importe parecido) | Copia **todas las líneas** del asiento original con Debe/Haber intercambiados. Para el registro de IVA de la factura genera un **registro de IVA inverso** (mismos datos con bases, cuotas y total en negativo y la nota "Registro inverso.") en el ejercicio activo, y lo enlaza a las líneas nuevas. |
| **Nuevo apunte con otra cuenta** | Primero el asiento inverso anterior (solo de las dos líneas). Después un segundo asiento igual al original pero con la **cuenta nueva** en lugar de la del apunte: cuenta nueva en el lado original y contrapartida en su lado original, concepto "Contra-apunte: …". Historial: "Contra-asiento: motivo". |
| **Contra-apunte** | Un asiento de dos líneas por el importe indicado: cuenta del apunte en el lado **contrario** al original y cuenta nueva en el **mismo** lado que el original, concepto "Cambio cuenta: …". Historial: "Cambio de cuenta (A por B): motivo". |
| **Ajuste saldo inicial** | Cuenta del apunte por el importe absoluto, al **Haber si el importe es positivo** y al Debe si es negativo, concepto "Regularización saldo inicial"; y la cuenta 113 de cuadres en el lado contrario, concepto "Regularización saldo inicial cliente <cuenta>". Si la sociedad no tiene configurada la cuenta 113, avisa y no genera nada. |
| **Apunte a tesorería** | Cuenta del apunte en el lado contrario al original, concepto "Pago por tesorería …"; cuenta 570 de la sociedad en el lado original. Mismo documento que el original. |
| **Apunte a deuda con socios** | Cuenta del apunte en el lado contrario al original, concepto "Pago por tesorería …"; cuenta **5510001** en el lado original, concepto "Pagado por socio …". Mismo documento que el original. |

Si algo falla (no encuentra el apunte o su contrapartida, no puede leer o generar el registro de IVA), la pantalla que llamó muestra "Todo mal: " con el motivo.

## 5. Reglas de negocio implícitas

1. **Corregir sin borrar**: ninguna opción borra apuntes. Los errores se corrigen con asientos nuevos (inverso, cambio de cuenta, regularización), salvo "Modificar apunte", que cambia la cuenta en el sitio y solo se permite en el ejercicio activo.
2. **Toda corrección queda documentada**: la nota es obligatoria y se guarda en el asiento y en el historial de apuntes manuales, con el asiento y el ejercicio de origen.
3. **Fechas que protegen el IVA presentado**: las anulaciones de facturas se fechan en el trimestre activo; el resto, el 1 de enero del ejercicio activo; nunca antes del apunte original.
4. **Al anular una factura se anula también su IVA** con un registro de IVA en negativo, para que el libro de IVA del trimestre actual recoja la corrección.
5. **Cuentas que deben venir de la configuración**: cuenta 113 de cuadres y cuenta 570 de tesorería (por sociedad); la 5510001 de socios está fija en el código y debería ser configurable (y probablemente una por socio).
6. **Signo del importe en los ajustes**: positivo = la contabilidad tiene más saldo del real y la cuenta se abona; negativo = se carga.
7. **Los apuntes generados por el sistema nacen punteados.**

## 6. Defectos detectados (no reproducir)

1. **Nombres cruzados**: el botón "Nuevo apunte con otra cuenta" genera lo que internamente se llama contra-apunte, y el botón "Contra-apunte" genera un cambio de cuenta. En el sistema nuevo cada opción debe tener un único nombre.
2. **Preselección incompleta** al venir de Asientos manuales: los tipos tesorería y deuda con socios no marcan ningún botón (y el tipo "inverso" está repetido en la lista).
3. **Tesorería y socios**: el asiento se graba con un número tomado en el momento de crear la primera línea; funciona, pero el historial se guarda con el tipo "inverso" en lugar de su propio tipo, sin lado Debe/Haber y con el importe original en vez del nuevo.
4. **Etiqueta de ejemplo de tesorería**: una vez elegida esa opción, su ejemplo ya no se oculta al cambiar a otra.
5. **"Anular apunte" pide un importe que no se usa**: el inverso siempre usa los importes del original.
6. **Cuenta visible**: la condición que decide si se pide cuenta nueva repite un tipo y no tiene en cuenta otros; funciona por casualidad.
7. **Localización por importe aproximado** (±1 € en la parte entera): si el asiento tiene dos líneas parecidas de la misma cuenta, puede corregir la que no es. Lo mismo para la contrapartida.
8. **Factura cuya base coincide con el total** (por ejemplo, exenta): como encuentra una "contrapartida" de importe parecido, la anula como asiento de dos líneas y **no invierte el IVA**.
9. **Ajuste de saldo inicial sin cuenta 113**: avisa, pero la pantalla que llama muestra además "Todo mal" sin motivo.
10. **Apuntes de otro ejercicio**: el asiento nuevo va al ejercicio activo, pero la nota del asiento (`F_OAS`) se guarda sin año, y la búsqueda del registro de IVA original usa el año del apunte. Si el ejercicio activo no es el siguiente, las referencias pueden no cuadrar.
11. **"Nuevo apunte con otra cuenta" sobre un asiento manual ya existente** (cuando se reescribe un asiento manual): el código se detiene ("cómo modificamos un multi-asiento manual") en vez de resolverlo.
12. **Al grabar cualquier apunte se pierde el identificador de pago**: el mismo campo se usa para el subdepartamento y el último valor pisa al primero.
