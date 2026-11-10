# Migración del ERP · tus respuestas del 10/11 y lo que hay que confirmar

Recojo en el nodo `implantacion/migracion-erp` todas tus respuestas tal como las escribiste: los criterios generales, los puntos 1 a 25 del mapa, las tablas sin dueño y el orden con ContaSol. Las que dejan una tabla fuera responden también a las preguntas M-01 a M-10 (te las dejo resueltas debajo).

Antes de escribirlas, hay siete puntos que conviene confirmar o cerrar. Cuatro cambian algo que ya estaba decidido y tres son huecos pequeños.

## Lo que cambia decisiones anteriores

**A. Importes de las facturas emitidas (punto 8).** Propones no importar ImporteTotal, TotalBaseImponible ni TotalIVA y recalcularlos. El problema es que una factura emitida no puede cambiar de importe: es la que recibió el cliente y la que está declarada en el IVA. Por eso RF-ERP-04 dice «preservar número, importe e instantánea fiscal». Si el recálculo diese otro total en las facturas antiguas con inconsistencias, ProContaVF mostraría una factura distinta de la emitida.
- (a) **Recomendada:** importar los totales del ERP tal como están, recalcular solo como control y registrar como incidencia cada factura donde no coincidan. Tú decides después qué hacer con ellas.
- (b) Recalcular y no conservar los totales del ERP.

**B. Pagos y remesas SEPA fuera (puntos 9 y 13).** El 01/11 decidimos importar Pagos (solo desde 2022) y las remesas SEPA, y creé el nodo `facturacion/remesas-sepa` para ello. Ahora ninguno de los dos se importa. Marco como sustituidas esas decisiones, aquí y en la arquitectura. ¿Qué hago con el nodo?
- (a) **Recomendada:** borrarlo y devolver las remesas SEPA a la idea `creative-lab/tesoreria`, que ya habla de cobros, vencimientos y remesas.
- (b) Dejarlo como borrador, sin importación, para un futuro módulo de remesas.

**C. ContaSol antes que el ERP.** De acuerdo: el ERP apunta a datos de ContaSol (cuentas F_MAE, centros F_CDP/F_DEP, asientos F_APU, libro F_IVS) y ContaSol no usa nada del ERP. Pongo en la migración del ERP una dependencia del importador de ContaSol: no se ejecuta sin ContaSol cargado y enlaza por sus mapas de identidad. También dejo la línea correspondiente en `implantacion/importador-contasol`. ¿Confirmas que el importador de ContaSol es lo siguiente que se construye?

**D. Ruta de los PDF de compras (punto 14).** Propones cambiar `D:\SynologyBox` por `D:\SynologyBox\SynologyDrive` para que el PDF se vea en desarrollo. Si ese cambio se escribe en la BD, en producción la ruta quedaría mal.
- (a) **Recomendada:** guardar la ruta relativa a una carpeta raíz configurable, como ya hace el gestor documental. Producción y desarrollo solo cambian esa raíz.
- (b) Hacer el reemplazo en la importación, solo en los ensayos de desarrollo.

## Criterios generales: dos matices

**E. Campos con el mismo valor en todos los registros.** Lo aplico solo a los campos que no nombras. Los que listas explícitamente se migran siempre. Por ejemplo, si todas las facturas son de la misma sociedad, la sociedad no se puede perder.

**F. Clave foránea que no se encuentra.** Se pone a nulo, como dices, pero cada caso queda registrado como incidencia con la clave original. Así nada se pierde en silencio.

## Huecos pequeños

**G1. Nóminas (punto 22).** De F_NOM migramos CuentaContable, ASIAPU, DNI y TipoPaga. ¿Qué más? Supongo que el periodo, el empleado y los importes que usa PlantillasApuntes. ¿Y AliasCamposF_NOM, CamposF_NOM y PlantillasApuntes se migran?
- **Mi recomendación:** AliasCamposF_NOM sí, porque ahorra volver a mapear las cabeceras. CamposF_NOM no, porque el nodo de nóminas describe sus propios campos. PlantillasApuntes se pasa al motor común de plantillas de asiento.

**G2. Activos (punto 23).** ¿Se migran Inmuebles (como Activos), PlantillasInmuebles y CuentasInmuebles, con el Departamento/SubDepartamento pasado a su centro de coste?
- **Mi recomendación:** sí a los tres.

**G3. Países, monedas, cambios, VIES y EntidadesBancarias.** Dices que no hacen falta si ya está resuelto. En compras, la moneda y el cambio del día ya están en la factura y el VIES se consulta por internet. Lo confirmo con el hilo de compras y, si está cubierto, no se migran. EntidadesBancarias tampoco: el banco sale del IBAN con el catálogo de Norma 43.

## Preguntas M-01 a M-10 que quedan resueltas con tus respuestas

- **M-01** (F_CDP/F_DEP): las carga el importador de ContaSol. El ERP solo enlaza a través de Agrupaciones.
- **M-02** (F_APUMANUAL, F_Anulacion_Asientos, F_CLIAGRUPADOS): no se migran.
- **M-03** (nóminas): queda abierta solo en lo que pregunto en G1.
- **M-04** (fraccionamiento): no se migra.
- **M-05** (_ControlAcceso, _ConverOperador): no se migran. La seguridad se hace desde cero.
- **M-06** (países, monedas, cambios): ver G3.
- **M-07** (Inspección de IVA): no se migra.
- **M-08** (mayores y cálculos derivados): no se migran.
- **M-09** (copias): no se migran.
- **M-10** (auxiliares): no se migran, tampoco Validaciones.

Puedes responder con algo como «A a, B a, C sí, D a, E ok, F ok, G1 ok, G2 ok» o con lo que quieras cambiar.
