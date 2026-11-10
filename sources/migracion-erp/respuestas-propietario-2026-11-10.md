# Respuestas del propietario sobre el mapa de tablas de la migración del ERP

Copia literal de los mensajes de Ronald en el hilo «Analizar la migración del ERP» (2026-11-10, hora UTC), sin cambios. Los números 1 a 25 son los bloques del «Mapa de tablas» de `docs/implantacion/migracion-erp.md`.

## Mensaje de 2026-11-10 12:22

Ok, ya tenemos un C:\docs\codigo\ProContaVF\ProConta\ProcontaEstructura.accdb con datos actualizados a fecha de hoy.

Te contesto al documento de migración:

Criterios generales para la migración: 
- En una tabla con más de tres registros, no se migrarán ni tendrán en cuenta campos que tengan el mismo valor en todos los registros (eso incluye valor NULL).
- En caso de fallar una integridad referencial de una clave foránea, se nulifica 

Sobre el mapa de tablas:
1. Ok. De la tabla Sociedades, solo migramos Activa=TRUE y los campos IdSociedad, codigo, Ejercicio, Nombre, CIF 

2. Ok. Solo los campos Codigo, Descripcion, Domiciliable

3. Para simplificarlo, he formateado los datos de dirección de los clientes (los que tienen dirección) y guardado como c:\docs\codigo\procontavf\Direcciones.xlsx y c:\docs\codigo\procontavf\Direcciones.csv. De esa forma, podemos descartar migración de Calles, Provincias, Poblaciones y Paises

4. Ok. Campos a migrar de Personas. IdPersona, Nombre, Apellido1, Apellido2, (la dirección se obtiene en el fichero del punto 3), Telefono, email, DNI, Nacimiento, EsEntidad (es la que nos discrimina si es una persona jurídica), Notas y Mas Notas se concatenan en un solo campo Notas, Alta (fecha en la que se ha creado la ficha), IdPersonaTutor (código de persona a la que esta ficha está vinculada, la vinculación es bidireccional), Activo, IdAgrupacion (para saber su subcuenta contable a partir de Agrupaciones), FormaPagoDefecto, RazonSocial, TelefonoMovil. 
La tabla DatosContacto contiene teléfonos y emails alternativos y normalmente también aparecen Personas.Telefono, Personas.Telefono2, Personas.Telefono3, Personas.email y Personas.TelefonoMovil (información redundante). El primer paso es migrar primero Personas.Telefono, Personas.TelefonoMovil y Personas.email y luego agregar los teléfonos/emails de DatosContacto, si es un teléfono o email repetido (Personas.Telefono, Personas.TelefonoMovil o Personas.email) no creamos un nuevo registro en ProContaVF, pero sí actualizamos sus valores a partir de DatosContacto.Descripcion y DatosContacto.Nombre (sobreescribiendo si el nuevo valor no es null).
De la tabla DatosContacto, migramos si Contacto<>"" y migramos los campos Orden, Contacto, Descripcion, TipoContacto (=4 teléfono móvil, =5 email, otros valores-> teléfono fijo), Nombre. IdContacto es la clave primaria, pero no es necesario migrarla al nuevo modelo.

5. Departamentos forma más bien parte de la parte de contabilidad, más que el ERP, tiene correspondencia con las tablas F_DEP (solo el primer dígito del departamento) y F_CDP, que es prácticamente equivalente a Departamentos, solo que F_CDP incluye también 4XX que son inversiones financieras (ETFs). Departamentos es una 1:N con Agrupaciones (Agrupaciones.Departamento). En el ERP no se asigna el Departamento si no el IdAgrupacion.

6. No hace falta migrar RegistroCambioCuentasContables. CuentasContables es un subconjunto de F_MAE (la tabla de cuentas contables de ContaSol). Al igual que Departamentos, CuentasContables tiene una 1:N con Agrupaciones (Agrupaciones.CuentaContable). No se asigna CuentaContable directamente si no a través de IdAgrupacion. Mejor migrar F_MAE directamente que es la tabla completa de cuentas contables. Agrupaciones nos sirve para saber la cuenta contable de un cliente, un concepto, etc.

7. ConceptosCaja tiene una jerarquía por encima de TiposConceptosCaja, que no estaba en ProcontaEstructura, la acabo de añadir. Solo se importaran ConceptosCaja WHERE conceptoscaja.activo=true y solo TiposConceptosCaja con algún concepto activo. De TiposConceptosCaja, solo los campos Codigo y Descripcion. De la tabla ConceptosCaja, los campos Id, Tipo, Descripcion, Importe, IdAgrupacion (solo para saber su departamento y cuentacontable). Lo más probable es que acabe eliminando la redundancia de ConceptosCaja (se crea un concepto para cada inmueble) ya que con ProContaVF se asignará la subcuentacontable y departamento en la propia factura programada, por lo que con un concepto 'Alquiler mensual piso' podremos reemplazar unos 20 conceptos (todos los de Tipo=124 y CodigoTipoIVA=2 excepto Id 4729 y 4728), 'Alquiler mensual local' reemplazará 3 conceptos (todos los de Tipo=124 y CodigoTipoIVA=6), 'Alquiler piso turístico' 2 conceptos (Id 4729 y 4728), 'Retención IRP' reemplaza a los 4 con Tipo=125, 'Residuos comerciales' a los 2 con Tipo=126.
Hacer todo esto, implica que se perderá la integridad referencial con LineasFacturaVenta.IdConcepto y Pagos.CodigoConceptoCaja. Nulifica el valor de estos campos, no me preocupa perder la referencia al ConceptosCaja original

8. FechasFacturas es una tabla temporal, no hay que migrarla. EstadosFacturacion tampoco hace falta, ProContaVF lo gestiona de otra forma más elegante ni es relevante esta información sobre las facturas históricas. De FacturasVenta, necesitaremos los campos Id (es el número de factura), IdCliente (si no existe registro equivalente en Personas, se nulifica), NIF, NombreCliente, Fecha, Descripcion, Notas, ImporteTotal es un campo calculado que probablemente tenga inconsistencias en facturas antiguas, por lo que mejor no importarlo y recalcularlos con las nuevas facturas (lo mismo pasa con ImporteTotal, TotalBaseImponible, totalIVA), NotasInternas, IdBeneficiario (IdCliente es el 'pagador' de la factura, IdBeneficiario es quien percibe el servicio. Si no hay registro en Personas, se nulifica)
De la tabla LineasFacturaVenta, migramos los campos: Id (número de factura), NumLinea, IdConcepto, Descripcion, Cantidad, Precio (es el precio base del concepto, IVA incluído), TipoIVA, Fecha (fecha en la que se ha entregado el servicio/producto), DescripcionCompleta, DescuentoPorcentual,DescuentoAbsoluto 
Escabecera se usa para agrupar varias líneas es una línea sin importe (visualmente, se muestra con fondo gris para crear apartados visualmente) y se puede combinar con otras línea con el flag 'EsDescuento=true' con descuento porcentual (que tampoco es un producto/servicio). En estos casos, el descuento se aplica a todas las líneas entre la cabecera y la línea de descuento. No hace falta que ProcontaVF tenga esta funcionalidad. De hecho, a nivel de migración, se puede hacer una migración plana de LineasFacturaVenta, si un registro tiene EsDescuento=true y Descuentoporcentual<>0, simplemente se migra con DescuentoPorcentual=0 ya que DescuentoAbsoluto ya contiene el valor correcto. Las líneas que tengan Escabecera se migran con IdConcepto=NULL, manteniendo NumLinea y descripción y omitiendo el resto de campos (no tiene importes ni ninguna otra información útil)

9. Pagos: Es una tabla bastante compleja, realmente correspondería a vencimientos. No la migramos y si al final incluímos tesorería, lo haríamos con una tabla nueva y limpia.

10. Fraccionamiento: Al no tener vencimientos, no tiene sentido importarla. Para Fraccionamientos con un solo registro, nos indica el día de pago, pero ya actualizaré manualmente esta información en las facturas programadas. 

11. FacturasProgramadas: Sí, se migran con un criterio análogo al de FacturasVenta pero incorporando los campos Referencia1 (es la fecha en la que hay que incrementar el IPC en la factura programada), FechaPrimerVencimiento (primera fecha en la que se generó una FacturaVenta a partir de esta programada), Fecha (corresponde a la siguient fecha en la que hay que generar la facturaventa de esa factura programada), FechaInicio y FechaFin corresponde al la ventana de tiempo en la que se consumirá el servicio (habitualmente, el mes al que corresponde el pago del alquiler o el año al que corresponde el pago de impuestos de basuras), NumeroRepeticiones es el número de facturas que hemos de generar a partir de esa factura programada (=0 quiere decir infinitas repeticiones), FacturasGeneradas indica la cantidad de facturas que hemos generado con esa factura programada, PeriodicidadMeses indica cada cuantos meses hay que volver a generar una nueva factura, al generar una facturaventa, facturaprogramada.fecha =facturaprogramada.fecha + PeriodicidadMeses, Facturaprogramada.fechainicio=Facturaprogramada.fechainicio + PeriodicidadMeses (y lo mismo para fechafin). Solo migramos Activa=true. 
DescripcionFactura es un campo con tokens (%FECHAINICIO% y %FECHAFIN%) que es el que se asignará a la nueva FacturaVenta.
IdUltimaFactura apunta  la última factura generada por esta factura programada, pero creo que el nuevo modelo de facturas programadas ya tiene otro sistema de trazabilidad, por lo que no hace falta migrarlo (no tendremos trazabilidad de las Facturas programadas importadas, pero no pasa nada). El resto de cambos exclusivos de esta tabla no se migran (back*, etc.)

12. ReservasPisosTuristicos: No se importa. Crearemos un nuevo modelo (VentasExternas?) y empezaremos desde cero, pero eso cae fuera del ámbito del importador.

13. RemesasSEPA: No se importa. Está ligado a vencimientos y tesorería y a un posible nuevo módulo para generar remesas SEPA. Cae fuera del ámbito del importador.

14. Compras: Es una tabla no normalizada (ImporteBI0, ImporteBI1..., etc.) que hay que normalizar (entiendo que mediante DesgloseIVASoportado). 
(los campos que no menciono se migran y doy por supuesto que son auto-explicativos)
Exportada indica si ya se ha generado el asiento. 
Archivo indica la ruta al PDF, pero es la ruta local del servidor de producción, para poder acceder al mismo archivo en el ordenador de desarrollo, hay que reemplazar 'D:\SynologyBox' por 'D:\SynologyBox\SynologyDrive'. 
DIAAPU + FECAPU + ASIAPU es la clave foranea a la tabla F_APU[EJERCICIO] que es la que contiene los apuntes contables. 
ArchivoOriginal no se migra. 
FechaHoraExportación es el momento en el que se generó el asiento
FechaHoraRegistro es el momento en el que se creo el registro en esta tabla
Duplicada no se migra
Carpeta y NombreFichero son campos calculados a partir de la ruta
TipoOperacion -> 1-Nacional, 2-Intracomunitaria, 3-ExtraComunitaria
TipoProducto -> 1-Servicio, 2-Bien	
IVASuplido, IdRegistroFacturaImportacionSuplido e IdRegistroFacturaTransporte no se migran
TextoAsiento es un renderizado del asiento generado. No hace falta migrarlo.
ImporteDolares es el importe original de la factura en dólares. El nuevo modelo contempla más divisas. Trasladar este valor al nuevo modelo.
TipoRetencion -> 1-Servicios, 5-Arrendamientos
IdRegistroIVA -> Clave foránea a F_IVS (libro de IVA soportado)
LineaN43 -> Es una clave foranea a LineasN43 e indica el movimiento contable de pago de esta factura
Suplido es el importe de suplido de esa factura
ASIAPURectificativa no se migra
IdRegistroIVARectificativa no se migra

15. TiposOperacionCompra es una clasificación alternativa de TipoOperacion (creo que por compatibilidad con ContaSol). No se migra.
PalabrasClaveSubDepartamento y PalabrasClaveTipoGasto las tiene ya modelizadas el nodo de facturas de compra, por lo que hay que trasladarlas a su modelo de datos

16. No migramos F_APUMANUAL F_ANULACION_ASIENTOS

17. No migramos F_CLIAGRUPADOS

18. CuentaBalanceIVA no se migra. 
IAE no se migra
Te adjunto el texto que indiqué a 'Construir facturas de compra' y su respuesta:

Mi texto:
Sobre el punto 2, el esquema actual de departamentos no es suficientemente granular como para diferenciar, por ejemplo, alquiler de pisos turísticos, viviendas y locales (ambos tienen el departamento 3).
Actualmente, estas actividades están reflejadas en la tabla 'CategoriasIVA' y la n:m que relaciona CategoriasIVA con Departamentos/F_CDP es CategoriasIVAPorDepartamento. CategoriasIVA tiene un campo importante que es 'Deducible' (IVA soportado deducible): 0-No deducible, 1-Por prorrata, 2-100% deducible.
El nombre 'CategoriasIVA' se puede cambiar por 'Actividades' u otro que sea semánticamente más correcto (y en consecuencia, el nombre de la tabla n:m con Departamentos)
Otra opción que quizá sería más limpia y elegante sería ampliar la granularidad de Departamentos para que la relación con actividades sea 1:1. Eso nos quita la tabla n:m de encima y permite fusionar Actividades/Departamentos. Afectará a la migración de datos que tendrá que reconvertir códigos de departamento y centros de coste a la nueva numeración.

Respuesta:
Miro primero cómo están CategoriasIVA y su relación con los departamentos en la copia, y te propongo una de las dos opciones.
Te propongo una variante de tu segunda opción. Nuestros centros de coste ya son un árbol con los niveles que hagan falta, así que no hace falta tabla n:m ni renumerar:
Actividades: tabla nueva por sociedad, con código, nombre, «Deducible» (0 no deducible, 1 por prorrata, 2 100 % deducible) y la serie de la 472 (4725, 4726…).
Cada nodo del árbol de centros puede tener su actividad, y la heredan los que cuelgan de él. Por ejemplo, bajo el departamento 3 se crean los nodos «Alquiler turístico», «Viviendas» y «Locales», cada uno con su actividad, y los centros de cada piso o local cuelgan del que le toca.
Al exportar una compra, se busca la actividad de su centro:
100 % deducible: el IVA va a la 472 de esa actividad, con la terminación del tipo.
No deducible: el IVA se suma al gasto, y en el libro de IVA consta como no deducible.
Por prorrata: el IVA va a la 472 de la actividad; la regularización con la 4729 la dejaría para el cierre del año.
Sin actividad: como ahora, a la 4720.
En la migración, CategoriasIVA pasa a Actividades. Asignar a cada centro su nodo de actividad se hace a mano una sola vez (o con una tabla de equivalencias). Esto añade la tabla Actividades y una columna en los centros de coste.
¿Cómo relacionamos actividades y centros de coste? -> Actividad en el árbol, heredada

19. No migramos ninguna de estas tablas

20. No se migra N43BBVA ni N43LineasBBVA ni N43PayPal ni N43Tarjetas
N43Lotes: Solo campos IdLote, Fichero (aunque no está accesible con esa ruta en la máquina de desarrollo), Fecha y Hora
N43Cabeceras: No se migra FechaInicio, FechaFin (están formateados como fecha en FechaInicioReal y FechaFinReal) ni TotalImporteDebe y TotalImporteHaber (están formateados como currency en TotalImporteDebeEuros y TotalImporteHaberEuros). Tampoco migramos TotalImporteDebeCalculado ni TotalImporteHaberCalculado
SaldoInicial y SaldoFinal son valores en bruto del fichero N43. Hay que convertirlos en currency (dividirlos por 100)

21. N43Esquema es una tabla que creé para poder interpretar los campos de un fichero N43. Entiendo que tu ya tienes tu propio esquema definido, por lo que esta tabla no haría falta migrarla.

EntidadesBancarias solo es una tabla con los diferentes bancos existentes, no tengo claro que esté actualizada. Se utiliza para mostrar el nombre del banco en el momento de introducir un IBAN. Si tienes otro sistema para obtener el nombre del banco a través del IBAN, esta tabla ya no sería necesaria.

N43EntidadesBancarias son las diferentes cuentas bancarias de origen que nos podemos encontrar al cargar un fichero N43. Dos de estas cuentas (La Caixa EUR REMESAS y La Caixa EUR RECIBOS DEVUELTOS) son ficticias y se utilizan para crear remesas N43 también ficticias para explotar una línea N43 en diversas líneas. Sus campos son:
- CuentaContable: A qué cuenta (habitualmente una 572) cargaremos el movimiento bancario
- Cuenta555Temporal: En el caso de querer exportar el movimiento bancario a la contabilidad, si no se ha definido la contrapartida (por ejemplo, una cuenta de gasto o de un proveedor), 'aparcaremos' el asiento en una cuenta 555 hasta que se defina su destino definitivo. Cada entidad bancaria tiene su propia cuenta 555. 
- CuentaGastosDivisa es la cuenta de gastos bancarios (comisiones, manteniento, etc.) por defecto de esa entidad, ya no se utiliza por lo que no hace falta migrarlo.
- Cuenta555Split: Es una cuenta 'puente' que se usa al dividir una línea N43 en varias líneas. La línea original se contabiliza como 572 a 555Split, las nuevas líneas se contabilizan como 555Split a Gastos/Proveedores/etc. De esa forma, 555Split siempre queda saldada.

N43FormatoFicherosTarjetas: Es el esquema para interpretar movimientos de tarjetas de crédito, pero ya está obsoleto. No se migra

N43PalabrasClave: Esta tabla permite automatizar la asignación automática de la contrapartida a los movimientos bancarios. Se basa en buscar el contenido del campo 'PalabraClave' en la descripción completa de la línea N43 (que contactena todas las descripciones de esa línea), en el caso de hacer match, se asigna el valor de Cuentacontable a la línea N43 y también el IdPalabraClave para tener trazabilidad y poder modificar masivamente palabras clave asignadas.
Token, Marca, Multiple, IdPersona, Importe, CuentaContableAlternativa y MascaraNumeroFactura no se migran
Especial solo tiene dos valores: 5 (indica que esa línea N43 es una remesa de recibos, luego lo utilizaremos para 'explotar' una remesa de recibos en recibos indiduales) y 0/NULL que no tienen efecto
IdLineaReferenciaSimilitud apunta a una línea N43 y se usa en la consulta para encontrar líneas N43 con una descripción parecida a otra ya contabilizada y así poderla asignar a la misma subcuenta contable. Si vas a construir un sistema diferente (y probablemente, mejor) para esta funcionalidad, este campo no será necesario.
PalabraClave2: Segunda palabra clave opcional para hacer un AND con la primera, para poder asignar palabras clave por dos textos separados entre sí.

DatosBancariosGestion: Son las dos cuentas bancarias que se usan para emitir remesas de recibos SEPA. Queda fuera de esta migración y pendiente de si incorporamos remesas SEPA.

22. F_NOM es el resultado de importación de nóminas desde un fichero excel. Para cada trabajador/mes debería haber un registro + dos registros adicionales en caso de tener pagas extra. La mayoría de los campos simplemente son una traslación 1:1 de las cabeceras de los ficheros excel, sin semántica alguna. Muchos de estos campos están siempre a NULL ya que aparecieron al migrar otros formatos de excel de nóminas. No se migran. 
- CuentaContable es la cuenta 465 de ese empleado. 
- ASIAPU: Es la clave foránea al Asiento generado, para poder deshacer o rehacer los asientos.
- Departamento: No se migra
- DNI es el DNI del trabajador (cuando aparece en el Excel de nóminas)
- PagaExtra no se migra
- TipoPaga: EXTRA para indicar que es una paga EXTRA, cualquier otro valor sería paga normal.

F_NOMAlias: No se migra

AliasCamposF_NOM: Convierte cabeceras del fichero Excel de nóminas en campos de F_NOM. Es un mapeo que permite importar diferentes formatos de ficheros Excel haciendo una traslación de campos de origen a campos de destino. La idea es que al leer un Excel, si no se encuentra la equivalencia de una cabecera, se pide al operador que seleccione un campo de destino de la tabla F_NOM (que podría ser 'NINGUNO' si no es un campo que queramos importar)

CamposF_NOM: Es una ayuda para que al preguntarle al operador el campo de destino de una cabecera (para crear un registro en AliasCamposF_NOM), se le de información descriptiva de cada campo de destino.

PlantillasApuntes: Permite generar los apuntes de nómina a partir del contenido de los campos de F_NOM. Con este nombre de tabla, realmente debería haber sido una tabla más genérica para contener plantillas de apuntes para diferentes sociedades, no solo la de nóminas. 
La función CTrabajadores.GenerarApuntes es la que construye una SQL dinámica para construir el asiento.

23. Activos: La tabla 'Inmuebles' (que pasará a ser 'Activos') tiene una tabla 1:n PlantillasInmuebles con cuentas contables XXXXX que sirve para generar automáticamente las subcuentas necesarias para cada activo. Por ejemplo, la cuenta 62550 (seguros) generará una subcuenta 62550XX para cada activo de los tipos 1 a 5.
Esto es muy cómodo ya que un activo necesita más de 20 subcuentas para poder contabilizar y crearlas a mano implica trabajo y, además, no genera las subcuentas con descripción uniforme.
La tabla Inmuebles tiene un Departamento/SubDepartamento
La tabla CuentasInmuebles es una materialización de las cuentas generadas para cada PlantillasInmuebles, de esa forma podemos, por ejemplo, actualizar masivamente la descripción de la subcuenta (modificando solo la plantilla) y, sobre todo, saber en qué subcuenta de un inmueble en concreto hemos de contabilizar, por ejemplo, su amortización.

24. No hace falta migrar nada de seguridad. Se crea un modelo de seguridad 'from scratch'

25. No migramos nada de Documentos. El gestor documental se diseña 'from scratch'. La idea es tener un gestor de todos los posibles documentos que se guarden en ProConta, que tendrá sub-tipos de documento (factura compra, factura venta, contrato alquiler, documentación personal como copia del DNI o nóminas, etc.) de forma que podamos consultar un documento-factura desde la ficha del tercero y desde la propia factura.

Tablas sin dueño:

Paises: No se migra

PaisesMonedas: No se migra

CodigosPais: Es una tabla con los diferentes códigos normalizados de cada país. Se usa para normalizar el páis de una factura de compra y saber su prefijo VIES (el prefijo de su CIF). Es una tabla útil, pero si tienes otro sistema para asignar la factura de compra de un país , esta tabla ya no sería necesaria

PrefijosVIES no da para cada país los dos primeros dígitos de un CIF intracomunitario y así poder identificarlo. Igual que la tabla anterior, si ya lo tienes gestionado, no hace falta migrar esta tabla

Monedas y PaisesMonedas: Todas las monedas del mundo, con su abreviatura y descripción. Para cada país, se indica cual es su moneda. Se utiliza para saber la moneda por defecto de una factura de compra de otro país, pero si esto ya lo tienes resuelto, estas tablas ya no tienen uso

CambioEuroDolar y CambioEuroLibra: Es un histórico del tipo de cambio entré dólar/libra y euro, para poder convertir una factura de compra a euros. Si ya tienes un sistema para consultar el tipo de cambio en una fecha determinada, estas tablas ya no tienen uso, y casi mejor, ya que requieren un daemon que las actualice diariamente.

InspeccionIVA* (tanto tablas como consultas) es un automatismo que monté para rellenar la información que nos solicitaron al hacer una inspección de IVA. No son fácilmente trasladable al nuevo modelo datos por lo que descartamos. Si vuelve a venir una inspección, ya te pediré tu ayuda ;-)

Mayores y cálculos derivados: No se migran, el motor de informes se encargará de estas extracciones de datos

Copias: No se migran

Auxiliares: No se migran. La única que tiene algo de sentido es 'Validaciones', que contiene una serie de consultas para detectar inconsistencias en los datos (por ejemplo, que el asiento contable al que apunta una línea N43 exista). Entiendo que difícilmente vamos a tener inconsistencias con el nuevo desarrollo. Si aparece alguna, ya crearemos un nodo de validaciones (como un test de pruebas) que haga diversas consultas cruzadas para asegurar la consistencia de los datos

Sobre la migración de Contasol: Quizá sí sería importante implementar primero la importación de ContaSol (importar diferentes ejercicios) antes que la del ERP ya que el ERP apunta y usa información de Contasol (Contasol no usa nada del ERP), por lo que sin la información de ContaSol la migración puede quedar inconsistente. 
La importación del ERP será 'one-shot' (una vez importado y pasado a producción, ya no se volverá a usar el módulo de importación del ERP).
Sin embargo, la importación de ContaSol sí será reutilizable para otros usuarios que quieran usar ProContaVF y migrar los datos de su contabilidad.

## Mensaje de 2026-11-10 13:06 (respuesta a los puntos A a G3)

Punto A: La factura que recibió el cliente se basa en LineasFactura, lo otro son campos calculados, que, además, incorporé hace dos o tres años. De todas formas, lo más sencillo es cotejar si la suma de líneas cuadra con el total de la factura. En según de lo que salga, decidimos.

Punto B: a) Pasar remesas a creative.

C.  Sí, vamos a por importador de ContaSol y luego reactivamos este

D. a)

E. Ok, correcto

F. Ok

G1. De F_NOM migramos todos los campos que tienen información, otro tema es a qué campo mapeamos esta migración (supongo que hay una nueva tabla 'Nominas' que tiene sus propios campos definidos).
Ok a tu recomendación

G2. Sí, se migran como activos. Sí a los tres

G3. Ok

Los puntos A a G3 a los que responde están en `sources/migracion-erp/puntos-a-confirmar-2026-11-10.md`.
