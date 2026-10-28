# Especificación autocontenida del reconocimiento de facturas de compra

## Propósito del documento

Este documento define todo lo necesario para reescribir desde cero el módulo de reconocimiento de facturas de compra de ProConta. Está dirigido a un desarrollador o a un modelo de programación que no dispone de esta conversación, del código legacy ni de documentación adicional del proyecto.

La implementación debe recibir un PDF de factura de compra, extraer y validar sus datos, devolver un resultado explicable con niveles de confianza y, cuando corresponda, insertar una fila en `FacturasCompra`. Debe funcionar tanto desde la entrada manual como desde una futura recepción por correo electrónico.

Las decisiones de este documento son normativas. Si el código, una migración antigua o el nombre histórico de un campo las contradicen, prevalece este documento.

## Resultado esperado

El punto de entrada conceptual será equivalente a:

```vbnet
Function ProcesarFacturaAsync(
    ficheroPdf As String,
    contexto As ContextoSociedadEjercicio,
    cancellationToken As CancellationToken
) As Task(Of CFacturaCompra)
```

También existirá una operación no destructiva para pruebas y revisión:

```vbnet
Function ReconocerFacturaAsync(
    ficheroPdf As String,
    contexto As ContextoSociedadEjercicio,
    cancellationToken As CancellationToken
) As Task(Of CFacturaCompra)
```

La primera reconoce, comprueba duplicados e inserta. La segunda no inserta ni actualiza datos.

El resultado debe incluir, como mínimo:

- sociedad y ejercicio contable de destino;
- proveedor identificado y cuenta contable, si se conocen;
- NIF leído, NIF finalmente asignado y razón social obtenida de la AEAT;
- fecha de factura;
- número de factura normalizado;
- divisa, tipo de cambio y fecha del cambio cuando no sea EUR;
- total, base imponible, cuota de IVA y desgloses por tipo;
- retención de IRPF, si existe;
- inversión del sujeto pasivo;
- hash MD5 del PDF;
- confianza individual de NIF, fecha, número e importes;
- confianza global;
- indicación de OCR requerido y revisión humana requerida;
- candidatos, fuentes, evidencias y advertencias en un log legible y serializable.

## Alcance y límites

Este módulo termina cuando ha producido un `CFacturaCompra` y, en el flujo definitivo, ha insertado `FacturasCompra`.

Quedan fuera de esta pieza:

- recepción y respuesta de correo electrónico;
- pantalla definitiva de revisión asistida;
- generación del asiento contable;
- exportación contable;
- gestión documental posterior;
- aprendizaje automático basado en las correcciones del operador.

Estas capas consumirán el resultado y el log de este módulo. Por ello, el motor no debe contener dependencias directas de formularios, correo ni creación de asientos.

## Plataforma y restricciones técnicas

- Aplicación de escritorio Windows.
- VB.NET sobre .NET 10 LTS.
- Compilación exclusivamente `x64`.
- Interfaz WinForms con controles Krypton.
- Base de datos Microsoft Access mediante ACE/OLE DB.
- `Option Explicit On`, `Option Strict On` y `Option Infer On`.
- Consultas parametrizadas. En OLE DB los parámetros se enlazan por posición, no por nombre.
- Arquitectura separada en dominio, aplicación, persistencia, integraciones e interfaz.
- No introducir reglas de reconocimiento en formularios ni en `Designer.vb`.
- Todas las funciones y procedimientos deben disponer de documentación XML completa.
- Las conexiones Access deben ser breves. La comprobación de duplicado y la inserción deben compartir una transacción.
- Cualquier cambio de esquema debe estar en una migración reproducible; no basta con modificar manualmente el `.accdb`.

## Fuentes de datos y bases de referencia

La base operativa es:

```text
C:\Docs\VeriFactu\ProcontaVF.accdb
```

`ProcontaEstructura.accdb` y las bases antiguas sirven solo como referencia legacy. Nunca deben convertirse en la base operativa de la nueva implementación.

La tabla `FacturasCompra` contiene facturas ya revisadas y es el conjunto de verdad disponible. La ruta original está en `FacturasCompra.ArchivoOriginal`; algunas versiones legacy también separan ejercicio, carpeta y fichero. Los datos históricos son muy útiles, pero no infalibles: durante las validaciones se localizaron errores manuales en total, base, IVA, NIF, fecha y número de factura. Una discrepancia con la base no demuestra por sí sola que el parser esté equivocado.

Los juegos principales de regresión son las facturas de 2025 y 2026 situadas bajo:

```text
D:\SynologyBox\SynologyDrive\<ejercicio>\Proveedores\<proveedor>\<fichero.pdf>
```

El repositorio debe tolerar rutas legacy que empiezan por `D:\SynologyBox\` y reconstruirlas bajo `D:\SynologyBox\SynologyDrive\` cuando proceda. Si no encuentra un PDF, el error debe mostrar la ruta completa intentada.

## Identidad de sociedad y ejercicio

Existen dos identificadores distintos y no son intercambiables:

```vbnet
Public NotInheritable Class ContextoSociedadEjercicio
    Public Property IdSociedad As String       ' TEXT(3), entidad jurídica permanente
    Public Property IdSociedadEjercicio As Long ' LONG, instancia anual
    Public Property Ejercicio As Integer
End Class
```

Decisiones obligatorias:

- `Sociedades.IdSociedad` es una clave textual de tres caracteres.
- `SociedadesEjercicios.IdSociedadEjercicio` es la clave numérica del ejercicio.
- El `FacturasCompra.IdSociedad LONG` legacy contiene realmente la antigua clave anual. Semánticamente equivale a `IdSociedadEjercicio`.
- La reimplementación debe usar `FacturasCompra.IdSociedadEjercicio LONG`, con relación a `SociedadesEjercicios.IdSociedadEjercicio`.
- Si es necesario mantener compatibilidad, se añadirá y rellenará `IdSociedadEjercicio` desde el campo legacy solo después de comprobar que la clave existe; el campo antiguo podrá conservarse temporalmente, pero no debe relacionarse con `Sociedades.IdSociedad`.
- La configuración del reconocedor pertenece a la sociedad permanente y debe usar `IdSociedad TEXT(3)`.
- El servicio recibirá ambos identificadores de forma explícita. Nunca deducirá el ejercicio desde la primera configuración activa.

## Arquitectura funcional

El flujo se divide en componentes sustituibles:

1. `IExtractorDocumentoFacturaCompra`: extrae texto y geometría del PDF nativo.
2. `IExtractorOcrFacturaCompra`: obtiene texto y geometría mediante OCR.
3. `MotorReconocimientoFacturaCompra`: interpreta un documento extraído sin acceder a archivos, red ni base de datos.
4. `IProveedorTiposCambioHistoricos`: obtiene cambios históricos de divisa.
5. `IConsultaDatosEmpresa`: consulta la razón social por NIF.
6. `IRepositorioProcesamientoFacturaCompra`: carga contexto e histórico, detecta duplicados e inserta.
7. `ServicioProcesamientoFacturaCompra`: orquesta fuentes, confianza, conversión, consulta AEAT, log y persistencia.

Todos los extractores deben producir un formato neutral:

```text
Documento
  Ruta
  Páginas[]
    Número
    Texto en orden de lectura
    Palabras[]
      Texto
      Página
      X, Y, ancho y alto normalizados
```

El motor nunca debe depender de objetos de PdfPig, AWS, WebView2, Access o WinForms.

## Extracción local de PDF

La primera fuente será un extractor local basado en PdfPig o una biblioteca equivalente que conserve posición, página y tamaño de cada palabra.

No se debe repetir la estrategia legacy de eliminar guiones, barras y puntos de todo el documento antes de interpretarlo. Deben mantenerse dos representaciones:

- texto original normalizado de forma conservadora, con líneas y símbolos;
- versiones compactadas específicas para comparar NIF, números de factura o importes.

La posición física es esencial para:

- asociar etiquetas con valores situados debajo o a la derecha;
- leer totales visuales aunque el orden interno del PDF sea extraño;
- detectar campos en laterales girados;
- evitar tomar importes de líneas de detalle como total;
- recomponer valores partidos en varios tokens.

Se considera que un PDF no contiene texto nativo útil cuando no alcanza aproximadamente diez caracteres alfanuméricos. En ese caso debe marcarse `RequiereOCR=True` y recurrirse a OCR si está habilitado.

## OCR y segunda fuente

El proveedor inicial será AWS Textract mediante `DetectDocumentText`. Cada página del PDF se enviará de forma independiente y se convertirá al mismo contrato neutral que la extracción local.

Textract se utilizará cuando se cumpla alguna de estas condiciones:

- el PDF no contiene texto útil;
- el NIF parece estar en una imagen o cabecera gráfica;
- el proveedor todavía no está consolidado con dos facturas aceptadas;
- la confianza local de importes, fecha o número queda por debajo del umbral;
- existe una disonancia que merece una segunda lectura.

No se debe llamar a Textract en todas las facturas de proveedores consolidados cuando la extracción local sea completa y coherente. El objetivo es combinar buen reconocimiento, bajo coste y explicabilidad.

El envío de estos PDFs a AWS está autorizado en el escenario actual. La integración debe seguir siendo configurable y desactivable por sociedad.

Las credenciales AWS se almacenarán en configuración. El identificador puede guardarse como texto; el secreto nunca se almacenará en claro. Se cifrará con DPAPI de Windows y solo se descifrará en memoria para crear el cliente. La región inicial es `eu-west-1`.

La selección entre fuentes seguirá estas reglas:

- si el documento local requiere OCR completo, Textract será la fuente principal;
- si ambas fuentes existen, se conservará la local salvo que Textract tenga más confianza y no exista discrepancia fiscal;
- una discrepancia superior al umbral entre total, base o cuota obliga a revisión;
- si local y OCR coinciden en total, fecha y número, la confianza global puede elevarse al menos a 0,97;
- el log conservará ambos candidatos aunque solo uno sea elegido.

Un modelo tipo Donut u otro transformer de documentos no es requisito de esta versión. Puede añadirse en el futuro detrás de otra interfaz de extractor o reconocedor, pero no debe sustituir el conjunto de regresión, las validaciones aritméticas ni la trazabilidad. Adoptarlo exigiría medirlo sobre las facturas reales, resolver ejecución local, consumo de GPU/CPU, idioma y ajuste al dominio español.

## Identificación del proveedor

La carpeta del PDF suele ser el nombre comercial del proveedor. Debe usarse como señal, no como verdad absoluta.

El contexto incluirá:

- alias o carpetas históricas;
- cuenta contable del proveedor;
- NIF del catálogo;
- hasta veinte números de factura recientes;
- número de facturas completas previamente aceptadas;
- NIF de las sociedades propias, que deben excluirse como posibles emisores.

Para obtener el NIF correcto del proveedor registrado, la relación legacy es:

```text
FacturasCompra.CuentaContableProveedor -> F_PRO.CODPRO -> F_PRO.CIFPRO
```

No debe usarse ciegamente un NIF incorrecto que figure en una fila histórica de `FacturasCompra`.

El alias de carpeta se comparará sin diacríticos, espacios ni puntuación. Solo se asignará proveedor cuando el resultado sea unívoco. La identificación por carpeta permite recuperar el NIF registrado si el documento no ofrece una lectura segura.

Una factura nueva de proveedor conocido gana confianza después de dos o más documentos aceptados sin corrección. El refuerzo crecerá con el histórico, pero tendrá un límite y nunca anulará una discrepancia fiscal o documental explícita.

## Reconocimiento del NIF

Reglas principales:

- excluir NIF de las sociedades receptoras;
- rechazar como NIF español un identificador puramente numérico;
- validar la letra o dígito de control cuando sea posible;
- aceptar un NIF español de nueve caracteres con formato válido;
- aceptar la variante europea `ES` seguida del identificador español; para sociedades suele tener once caracteres en total;
- tolerar separadores visuales como `ES-B82115577`, pero compactarlos al validar;
- priorizar etiquetas como `NIF`, `CIF`, `VAT` o `IVA` próximas a la razón social del emisor;
- evitar confundir número de factura, cliente, contrato u otros códigos con el NIF;
- limitar el análisis a la primera factura cuando el PDF contiene varias;
- si se lee un NIF fiable distinto del registrado, conservar el documental y emitir advertencia;
- si no se lee un NIF fiable pero el proveedor se identifica unívocamente, asignar el registrado con confianza inferior y dejar constancia;
- si el NIF parece estar en una imagen, marcar `RequiereOCRParaNif`.

No todos los documentos extranjeros contienen identificador fiscal. La ausencia de NIF en una factura de Estados Unidos, China o Dubái es una advertencia, no una razón para inventar un valor.

Solo se consultará la AEAT para candidatos españoles: `ES` más NIF español o nueve caracteres con primera letra y formato español válido. La fuente principal será el servicio oficial `VNifV2` de la AEAT, reutilizando el certificado ya configurado para VeriFactu. Para esta pieza solo es necesaria la razón social, aunque pueden conservarse NIF actual y estado censal. Axesor no es fuente principal.

## Número de factura

El número de factura es muy variable y no tiene relevancia contable directa, pero es esencial para detectar duplicados lógicos.

Orden de prioridad:

1. etiqueta explícita y asociación geométrica;
2. etiqueta explícita en texto lineal;
3. patrón histórico del proveedor;
4. nombre del fichero, si concuerda con el texto o el patrón histórico;
5. candidato genérico de baja confianza;
6. vacío y revisión manual si no hay evidencia suficiente.

Etiquetas relevantes, tolerando diacríticos y símbolos corruptos:

```text
Número de factura, Nº factura, N.° de factura, Factura nº,
FACTURA -, Invoice #, Invoice number, Invoice, Rechnung Nr.
```

El símbolo de número puede aparecer como cualquier carácter Unicode o carácter corrupto por encima de ASCII 127. El valor puede estar debajo de la etiqueta y partido en varios tokens, por ejemplo `25F 02355` o `C/26042692`.

La etiqueta inequívoca tiene prioridad sobre cualquier heurística histórica.

### Comparación con el histórico

Se consultarán números recientes del proveedor incluso si pertenecen a otro ejercicio. Para cada candidato se eliminarán separadores y se compararán:

- diferencia de longitud no superior a uno como regla ordinaria, con tolerancia especial para series que crecen durante el año;
- mismos primeros caracteres o prefijo alfabético;
- terminación numérica compatible;
- sufijo común, incluido el año cuando aparece al final;
- patrón de letras y dígitos;
- cantidad similar de letras;
- similitud con la función legacy `CadenasParecidas`.

Debe conservarse `NumeroFacturaEjemplo` en el resultado o log para explicar por qué se eligió o descartó un candidato.

Los candidatos que sean fechas, códigos postales, NIF, importes o números de cliente deben penalizarse o descartarse.

Al persistir, el número se guarda limpio: exclusivamente letras ASCII y dígitos. Se eliminan espacios, barras, guiones, puntos y demás símbolos. La lectura original y la evidencia se conservan en el log.

Si el documento parece escaneado, no hay etiqueta fiable ni histórico, es preferible dejar el número vacío antes que inventarlo.

Si un PDF contiene dos facturas, la política actual es procesar únicamente la primera y advertir que hay múltiples facturas. No se sumarán ambas salvo una regla documental explícita, como el listado mensual de Airbnb.

## Fecha de factura

La fecha debe elegirse por etiqueta y proximidad, no tomando la primera fecha del documento.

Prioridad de etiquetas:

```text
Fecha de factura, fecha factura, fecha de facturación,
fecha de emisión, ticket/fecha fra, invoice date, issue date.
```

Debe penalizarse `fecha de vencimiento`, fechas de consumo, periodos, albaranes y otras fechas secundarias.

Formatos obligatorios:

- `dd/MM/yyyy`, `dd-MM-yyyy`, `dd.MM.yyyy`;
- `dd/MM/yy`, interpretando el año en el siglo esperado;
- `yyyy-MM-dd`;
- `03-feb-2026` y abreviaturas equivalentes;
- `9 de enero de 2026`;
- meses en español y, cuando aparezcan, inglés.

El ejercicio activo y el año presente en la ruta son señales fuertes. Una fecha de 2025 en un PDF archivado como factura de 2026 debe quedar penalizada o marcada para revisión salvo evidencia inequívoca. El nombre del fichero puede ser apoyo, nunca superar una etiqueta documental clara.

## Importes, IVA y retenciones

El parser debe reconocer independientemente total, base, cuota y desglose. No debe asumir que toda factura tiene un único IVA del 21 %.

Tipos que deben soportarse en la interfaz y el modelo:

```text
0 %, 4 %, 5 %, 10 % y 21 %
```

También debe permitir varios tipos en una factura. Cada desglose contendrá tipo, base, cuota, confianza y evidencia.

### Formatos monetarios

Se deben aceptar:

- coma o punto decimal;
- punto, coma o espacio como separador de miles según contexto;
- entre cero y seis decimales en líneas y subtotales;
- importes enteros sin decimales;
- `€`, `EUR`, `EURO`, `$`, `USD`, `£` y `GBP`;
- símbolo monetario delante o detrás;
- signo delante del símbolo, delante del número o como sufijo;
- abonos y rectificativas con signo negativo.

Una fecha con puntos no debe interpretarse como importe. Los valores de consumo, kWh, teléfonos, códigos postales y números largos sin contexto monetario deben quedar excluidos.

### Selección y reconciliación

Prioridades:

1. desglose fiscal explícito y aritméticamente reconciliado;
2. etiquetas inequívocas de total, base y cuota;
3. asociación geométrica entre etiqueta y valor;
4. combinación de importes que satisface las ecuaciones fiscales;
5. reglas de familia documental;
6. heurística prudente del importe máximo solo cuando no haya mejores señales.

Ecuaciones de validación:

```text
base + cuota = total
suma(bases por tipo) = base total
suma(cuotas por tipo) = IVA total
cuota por tipo ≈ redondeo(base × tipo)
base + IVA - retención + suplidos/otros conceptos = total, si están explicitados
```

La tolerancia monetaria ordinaria es 0,02 EUR; en sumas de varios desgloses puede admitirse hasta 0,04 EUR si el redondeo lo justifica.

Una reconciliación completa de base, IVA y total debe elevar la confianza de importes aproximadamente a 0,97. Una reconciliación de solo dos magnitudes es útil, pero debe quedarse alrededor de 0,90 y conservar prudencia.

No corregir una factura documentalmente incoherente de forma silenciosa. Se conserva lo que consta en el documento y se añade advertencia, salvo que exista una regla contable explícita como VIES.

### IVA cero, exención e inversión del sujeto pasivo

- Si hay evidencia de operación sin IVA y el total está claro, se normalizará `CuotaIva=0`, no `NULL`, y `BaseImponible=Total` cuando proceda.
- Para validación histórica, `NULL` reconocido y cero esperado no deben considerarse descuadre si la factura es inequívocamente no sujeta o exenta; aun así, el resultado definitivo debe preferir cero explícito.
- La inversión del sujeto pasivo se detecta por texto equivalente a “inversión del sujeto pasivo” y cuota cero.
- En operaciones intracomunitarias, si la sociedad tiene VIES, se contabiliza `BaseImponible=Total` y `CuotaIva=0`, aunque el proveedor extranjero haya repercutido IVA. Debe emitirse una advertencia clara indicando que el IVA documental no se repercute.
- `SociedadTieneVies` es una configuración de la sociedad permanente.

### Retención de IRPF

Debe detectarse una etiqueta `IRPF` o `retención`, su porcentaje y su importe. Es un dato independiente del IVA y se conservará para el futuro módulo contable. Ejemplo conocido: facturas del Registro de la Propiedad con base, IVA y retención del 15 %.

## Divisas

Cuando el total está en USD o GBP:

- conservar divisa e importe original;
- obtener el cambio histórico a EUR en la fecha de factura;
- calcular el importe en EUR redondeado a dos decimales con `AwayFromZero`;
- conservar tipo y fecha efectiva del cambio;
- advertir si no se pudo convertir.

El proveedor inicial es Frankfurter, sin clave:

```text
https://api.frankfurter.dev/v2/rate/{ORIGEN}/EUR?date={yyyy-MM-dd}&providers=ECB
```

Si no hay dato para la fecha, por ejemplo durante un fin de semana, se buscará hacia atrás hasta siete días. Las respuestas se cachearán por fecha, origen y destino.

## Familias documentales conocidas

La implementación debe priorizar reglas genéricas y expresarlas como estrategias o perfiles comprobables. No debe convertirse en una cadena incontrolable de `If ruta contiene proveedor`. Sin embargo, el conjunto de regresión exige contemplar estas familias:

- **Aigües de Barcelona**: combina suministro al 10 % con canon sin IVA. El total visual de la primera página y la expresión de IVA sobre base sujeta son señales principales.
- **Airbnb**: algunos PDFs son listados mensuales, no una factura única. Se suman los importes de la columna pertinente y se usa el primer número como referencia.
- **Aluminios Casas**: base y otros importes pueden ser enteros sin símbolo monetario.
- **Amazon**: gran variedad de emisores y formatos. Priorizar `Número de la factura` o `Invoice #`, `Total pendiente`, tablas fiscales y primera factura del PDF. Algunos proveedores chinos usan punto decimal, símbolo prefijado e importes enteros. Algunos documentos contienen dos facturas: procesar la primera.
- **Bañón y Sánchez**: líneas con seis decimales, base con cuatro y total con dos; evitar confundir números de gran tamaño con importes.
- **Booking**: tratar `Comisión total` como operación sin IVA cuando corresponda.
- **Daspicuore** y documentos similares: la base puede etiquetarse `Total (Imp. excl.)`.
- **Duerto**: respetar cuota explícita `IVA 21 %` y total etiquetado.
- **eBay**: factura en GBP; punto decimal y conversión histórica.
- **Iberdrola**: diseños complejos; priorizar total visual y resumen fiscal, incluida factura negativa.
- **IONOS**: etiquetas `N.° de factura` y `Fecha de facturación`; el nombre del fichero también contiene ambos datos.
- **Lidl**: resumen final con varios tipos de IVA.
- **Mano a Mano**: puede usar `EURO` en lugar de símbolo; algunos portes no siguen la base ordinaria y requieren revisión.
- **Movistar**: puede combinar importes sujetos y exentos o fuera de base. No dividir el total por 1,21. El NIF puede estar girado 90 grados.
- **Naturgy**: no confundir consumo eléctrico con base; usar la serie final de base e IVA.
- **Naval Chicolino**, **Adeyco** y **ABC**: algunos documentos están escaneados. Si no hay evidencia suficiente, marcar OCR y dejar campos dudosos vacíos.
- **PCComponentes**, **LG** y **Pixar Printing**: pueden usar punto decimal; el total puede ser entero.
- **PDFEscape**: factura en USD sin identificador fiscal español.
- **Registro de la Propiedad**: incluye retención de IRPF.
- **Renta 4**: importes inferiores a un euro, a veces con un solo decimal y sin símbolo.
- **Repsol**: la línea `IVA (21 %) de <base> <cuota>` ofrece base y cuota; no confundir kWh con dinero.
- **Saltoki**: signo negativo como sufijo; número de factura próximo a la etiqueta `NUMERO` y normalmente al nombre del fichero.
- **SuperAceUPDF** y algunas facturas de Office: poco normalizadas, símbolo monetario delante, sin IVA ni NIF fiable.
- **The Option Networking**: etiquetas claras `BASE IMPONIBLE` y `FACTURA #`.
- **Tyco**: el NIF puede verse como `ES-B82115577` aunque la capa textual esté corrupta.
- **Vaillant Saunier Duval**: el NIF documental prevalece si es válido.
- **Varios Abono YouGet** y **KTuin**: facturas negativas; KTuin puede no desglosar base e IVA y debe marcarse para revisión.
- **Lavandería Bellamar** e **Isolana**: cabecera o NIF como bitmap, a veces girado; requieren OCR.
- **Arpa Motors**: puede proceder de Adobe Scan con OCR deficiente; usar etiquetas y segunda fuente.
- **Eminza**, **Lightroom**, **VRBO** y ciertas facturas de Amazon Francia: aplicar la política VIES configurada.

Estas reglas deben vivir en pruebas y perfiles separados. El nombre de carpeta es una señal para elegir perfil, pero cada perfil debe exigir evidencias documentales antes de modificar importes.

## Confianza y revisión

Cada campo tendrá confianza entre 0 y 1. La confianza global se ponderará inicialmente así:

```text
50 % importes
20 % fecha
20 % número de factura
10 % NIF
```

El histórico aceptado del proveedor puede añadir hasta 0,08. Dos facturas aceptadas sin corrección consolidan al proveedor y reducen la necesidad de segunda fuente, pero no deben transformar un campo ausente en un campo válido.

Umbrales iniciales configurables:

```text
Umbral de aceptación global: 0,90
Discrepancia monetaria entre fuentes: 0,02 EUR
```

Se requiere revisión cuando:

- falta total, fecha o número de factura;
- el PDF contiene varias facturas;
- hay discrepancia monetaria entre fuentes;
- el NIF leído y el registrado difieren;
- la fecha no concuerda razonablemente con el ejercicio;
- la confianza global no alcanza el umbral;
- se aplica una regla VIES;
- no se puede convertir la divisa;
- existe una incoherencia fiscal o documental.

En la interfaz, cada campo dudoso se resaltará individualmente. No basta con una única bandera global.

## Trazabilidad y log

`FacturasCompra.LOGImportacion` almacenará JSON versionado y suficientemente completo para mostrarlo al operador o incluirlo en una futura respuesta por correo.

Contenido mínimo:

```json
{
  "version": 1,
  "fechaProcesoUtc": "...",
  "idSociedad": "002",
  "idSociedadEjercicio": 24,
  "confianzaGlobal": 0.97,
  "requiereRevision": false,
  "fuenteElegida": "pdf-local",
  "razonOcr": "segunda_fuente_proveedor_no_consolidado",
  "facturasAceptadasProveedor": 4,
  "candidatoLocal": {},
  "candidatoOcr": {},
  "consultaAeat": {},
  "conversionDivisa": {},
  "advertencias": [],
  "evidencias": []
}
```

No almacenar secretos, certificado, contraseña AWS ni texto completo innecesario del PDF. Las evidencias deben ser fragmentos breves, página y coordenadas o etiqueta utilizada.

El concepto contable se compone así:

```text
[Nombre proveedor] Fact: [NúmeroFactura]
```

Si falta el nombre, usar `Fact: <número>`; si falta el número, conservar solo el nombre.

## Detección de duplicados

Se calculará MD5 sobre los bytes completos del PDF y se guardará en minúsculas como 32 caracteres hexadecimales. Debe ser compatible con el cálculo legacy.

Antes de insertar, dentro de la misma transacción, se comprobará si ya existe el mismo MD5. Si existe, se abortará con un error identificativo y accionable.

El número de factura permite una segunda detección lógica por proveedor, pero no sustituye al hash: un PDF puede cambiar sin cambiar la factura y un mismo documento puede llegar con distinto nombre.

## Esquema recomendado

### FacturasCompra

Además de los campos legacy que consuma la contabilidad, deben existir:

```text
IdRegistro                      AUTOINCREMENT / clave existente
IdSociedadEjercicio             LONG NOT NULL
CuentaContableProveedor         TEXT
Concepto                        TEXT
NumeroFactura                   TEXT
Fecha                           DATETIME
ArchivoOriginal                 TEXT
MD5                             TEXT(32)
NIF                             TEXT
NombreProveedorAeat             TEXT
ImporteBI                       CURRENCY
ImporteIVA                      CURRENCY
ImporteTotal                    CURRENCY
InversionSujetoPasivo           BIT
TipoRetencion                   DOUBLE
ImporteRetencion                CURRENCY
Divisa                          TEXT(3)
ImporteTotalDivisa              CURRENCY
TipoCambioAplicado              DOUBLE
FechaTipoCambio                 DATETIME
RequiereRevisionImportacion     BIT
ConfianzaImportacion            DOUBLE
LOGImportacion                  LONGTEXT
FechaHoraRegistro               DATETIME
```

Los desgloses de IVA deberían almacenarse en una tabla hija si van a ser consumidos por el futuro módulo contable. No serializarlos únicamente en el log:

```text
FacturasCompraDesglosesIva
  IdDesglose
  IdRegistroFacturaCompra
  TipoIva
  BaseImponible
  CuotaIva
  Confianza
  Evidencia
```

### ConfiguracionReconocimientoFacturasCompra

```text
IdConfiguracion                 AUTOINCREMENT
IdSociedad                      TEXT(3) NOT NULL
Activo                          BIT NOT NULL
SociedadTieneVies               BIT NOT NULL
UsarTextract                    BIT NOT NULL
RegionAWS                       TEXT(30) NOT NULL
ClaveAccesoAWS                  TEXT(128)
SecretoAWSProtegido             LONGTEXT
UmbralAceptacion                DOUBLE NOT NULL
UmbralDiscrepanciaImporte       CURRENCY NOT NULL
```

Debe existir un índice único por `IdSociedad` y una relación con `Sociedades(IdSociedad)`. La carga será `WHERE IdSociedad=?`; queda prohibido seleccionar simplemente la primera configuración activa.

## Persistencia y transacción

Secuencia de la operación definitiva:

1. validar ruta, extensión y legibilidad;
2. cargar contexto para la sociedad y ejercicio indicados;
3. extraer localmente;
4. interpretar candidato local;
5. decidir si se necesita OCR;
6. interpretar candidato OCR;
7. resolver discrepancias y elegir fuente;
8. convertir divisa si procede;
9. consultar AEAT si el NIF es español y fiable;
10. componer resultado y log;
11. calcular MD5;
12. abrir conexión y transacción;
13. comprobar duplicado;
14. insertar cabecera y desgloses;
15. confirmar transacción;
16. devolver el resultado con `IdRegistro`.

La cancelación antes del `Commit` no debe dejar ninguna fila parcial.

## Pantalla de pruebas

Debe existir una pantalla accesible desde la aplicación para probar el motor contra `FacturasCompra`.

Requisitos:

- listar todas las facturas del ejercicio activo;
- mostrar en la lista únicamente la última carpeta y el nombre del fichero;
- procesar de forma no destructiva la fila seleccionada;
- mostrar a la derecha una vista previa del PDF mediante WebView2 o visor equivalente;
- mostrar valor reconocido, valor guardado y si coinciden;
- mostrar NIF, razón social AEAT, fecha, número, BI, IVA, total y confianzas;
- resaltar campos que requieren revisión;
- incluir una cuadrícula con columnas 0 %, 4 %, 5 %, 10 % y 21 %, y filas Base imponible, IVA y Total;
- al editar cualquiera de las tres magnitudes de una columna, recalcular las otras dos;
- mostrar suplidos y retención cuando existan;
- usar un cuadro de texto multilínea para fuentes y avisos, con ajuste de línea y sin barra horizontal;
- al hacer doble clic en fuentes y avisos, transformar el JSON en una vista humana y copiable;
- al hacer doble clic en una fila, copiar al portapapeles la ruta completa del PDF;
- mostrar errores con ruta completa y detalle técnico copiable;
- cancelar el procesamiento anterior si el usuario selecciona rápidamente otra fila;
- nunca insertar ni actualizar desde esta pantalla de pruebas.

## Mensajes al operador

Los mensajes deben ser concretos y accionables. Ejemplo correcto:

```text
No encuentro el PDF de la factura 4536.
Ruta intentada: D:\SynologyBox\SynologyDrive\2026\Proveedores\Adeyco\factura.pdf
Comprueba que Synology Drive esté conectado o selecciona el archivo manualmente.
```

No usar mensajes genéricos como “No se ha podido analizar”. Las excepciones completas y SQL irán al log técnico, pero la interfaz debe explicar el registro afectado, la causa probable y el siguiente paso.

## Pruebas obligatorias

### Unitarias

- PDF sin texto marca OCR.
- PDF con texto nativo no marca OCR completo.
- importes europeos, anglosajones, enteros, negativos y símbolo prefijado;
- múltiples tipos de IVA;
- IVA cero explícito;
- retención de IRPF;
- inversión del sujeto pasivo;
- fecha con año de dos dígitos;
- fecha con punto, guion, mes abreviado y mes textual;
- prioridad de fecha de factura sobre vencimiento;
- etiqueta de número partida geométricamente;
- tolerancia a símbolo de número corrupto;
- comparación histórica equivalente a `CadenasParecidas`;
- limpieza alfanumérica del número persistido;
- exclusión del NIF propio;
- validación de NIF español y variante `ES`;
- proveedor unívoco como respaldo de NIF;
- discrepancia entre NIF documental y catálogo;
- primera factura en PDF múltiple;
- reconciliación aritmética eleva confianza;
- historial aceptado refuerza confianza con límite;
- VIES fuerza BI igual a total, IVA cero y warning;
- conversión USD y GBP con búsqueda hacia atrás;
- MD5 compatible y duplicado abortado.

### Integración

- extracción PdfPig de PDFs reales;
- Textract simulado y, en pruebas separadas habilitadas, llamada real;
- consulta AEAT simulada y prueba real controlada con certificado;
- Access sobre una copia temporal de `ProcontaVF.accdb`;
- inserción atómica de cabecera y desgloses;
- carga de configuración por sociedad;
- separación correcta entre `IdSociedad` e `IdSociedadEjercicio`;
- pantalla de pruebas sin escrituras.

### Regresión real

Ejecutar todos los documentos 2025 y 2026 y exportar siempre un TXT delimitado por tabuladores, no JSON como artefacto principal para el usuario. Columnas mínimas:

```text
IdRegistro, Ruta, Proveedor, RequiereOCR,
TotalBD, TotalReconocido, TotalCorrecto,
BaseBD, BaseReconocida, BaseCorrecta,
IvaBD, IvaReconocido, IvaCorrecto,
NifBD, NifReconocido, NifCorrecto,
FechaBD, FechaReconocida, FechaCorrecta,
NumeroBD, NumeroEjemplo, NumeroReconocido, NumeroCorrecto,
ConfianzaImportes, ConfianzaNif, ConfianzaFecha, ConfianzaNumero,
ConfianzaGlobal, RequiereRevision, Fuente, Advertencias
```

Las diferencias conocidas como errores de la base se documentarán como excepciones del conjunto de verdad; no deben deformar el parser para reproducir un dato histórico incorrecto.

## Criterios de aceptación

La reescritura se considera funcionalmente completa cuando:

- procesa de extremo a extremo PDFs nativos y escaneados;
- supera la regresión curada de 2025 y 2026 para total, base, IVA, NIF, fecha y número;
- todos los casos no resueltos quedan explícitamente marcados para revisión, sin valores inventados;
- ninguna factura se inserta dos veces con el mismo MD5;
- todas las decisiones se pueden explicar desde el log;
- configuración y credenciales están aisladas de la lógica;
- sociedad permanente y ejercicio contable están correctamente separados;
- la pantalla de pruebas no modifica la base;
- la solución completa compila en x64 y las pruebas pasan;
- no se incluyen credenciales, PDFs reales ni copias `.accdb` en el control de versiones.

## Orden recomendado de implementación

1. Definir contratos neutrales y modelo de resultado.
2. Corregir esquema e identidad sociedad/ejercicio mediante migración.
3. Implementar extractor PDF posicional.
4. Implementar normalización y parser genérico con pruebas unitarias.
5. Incorporar histórico de proveedor, número y NIF.
6. Implementar desgloses, retención, VIES y divisas.
7. Incorporar Textract y resolución de fuentes.
8. Incorporar AEAT.
9. Implementar persistencia, MD5, duplicados y log.
10. Implementar pantalla de pruebas.
11. Ejecutar regresión 2025 y 2026, corregir perfiles documentales y congelar resultados curados.

## Principios que no deben perderse en la reescritura

- La aritmética fiscal y la geometría del documento son más fiables que elegir el número más grande.
- Una etiqueta explícita vence a una heurística histórica.
- El histórico ayuda, pero nunca debe obligar a repetir un error previo.
- Es preferible devolver un campo vacío y solicitar revisión que inventar un dato.
- OCR es rescate y segunda fuente, no el lugar donde reside la lógica fiscal.
- Toda excepción de proveedor debe exigir evidencia y disponer de prueba de regresión.
- El resultado debe ser auditable por una persona que no conozca el código.
- El módulo debe poder ser invocado igual desde una pantalla manual y desde un futuro daemon de correo.
