---
title: Contabilización de compras
status: draft
depends_on: [compras/factura-compra, contabilidad/asientos, contabilidad/cuenta-contable]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-29 18:54 · Validar prototipos de pantallas principales | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGPu88XoVeXWyHsuxbrF2kK
---
## Summary
Genera los asientos de las facturas de compra con el motor de contabilidad/asientos.
**Aceptación:** compra registrada genera su asiento cuadrado; regenerar no duplica.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-29 17:45 · Referencia funcional: el asiento y el libro de IVA que genera hoy «Exportar factura» de `frmEntradaFacturas`, con transitario, deshacer, rehacer y rectificativa, en `sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md` § 3-4, y sus defectos (§ 7), que no se reproducen. Donde el análisis y las decisiones siguientes de este nodo difieren (descuadres, fecha del asiento, rectificativas, importaciones, moneda), prevalecen las decisiones.
- 2026-10-29 18:14 · [replaced by 2026-10-29 21:05] Un descuadre por redondeo de hasta 0,05 € (propuesta pendiente de validar) se ajusta con un apunte en la cuenta de diferencias de redondeo de la sociedad (6690000 en el prototipo); un descuadre mayor bloquea la contabilización. Nunca se graba un asiento descuadrado.
- 2026-10-29 18:14 · [replaced by 2026-10-30 09:58] La factura rectificativa se contabiliza en el trimestre de IVA activo: su fecha contable es la mayor entre la fecha de la factura original y el primer día del trimestre activo.
- 2026-10-29 18:14 · La autoliquidación del IVA (inversión del sujeto pasivo e intracomunitarias) usa el tipo real de la factura, no un 21 % fijo. Las cuentas de IVA, retención, suplidos y redondeo son parámetros de la sociedad.
- 2026-10-29 18:53 · [replaced by 2026-10-30 09:58] Propuesta pendiente de validar: la fecha contable de una factura es la mayor entre su fecha y el primer día del trimestre de IVA activo (sustituye la regla antigua del 2 de enero para facturas del ejercicio anterior); una factura de un ejercicio posterior al de trabajo se bloquea.
- 2026-10-29 21:05 · Nunca se graba un asiento descuadrado. Un descuadre de hasta 0,03 € se ajusta solo con un apunte en la cuenta de diferencias de redondeo de la sociedad (6690000 en el prototipo). Si es mayor, no se deja al operador sin salida: se indica cuánto falta y qué cuota de IVA no corresponde a su base, y puede recalcular las cuotas desde las bases o contabilizar con un ajuste manual a esa misma cuenta escribiendo un motivo, que va en el concepto del apunte y queda en el registro de la factura.
- 2026-10-30 09:58 · El asiento se fecha siempre el día en que se genera (hoy), también el rectificativo; la fecha de la factura se conserva tal cual y es básicamente informativa. El asiento va al ejercicio de esa fecha: si el ejercicio de trabajo es otro, o la factura tiene fecha posterior a hoy, no se deja contabilizar. Rehacer una contabilización permite mantener la fecha del asiento o pasarla a hoy. La pantalla deja de usar el trimestre de IVA activo para fechar.
- 2026-10-31 07:46 · Importación: la factura lleva un solo importe y se desglosa siempre al tipo de IVA vigente (21 % por defecto), lleve o no IVA desglosado: el gasto es el importe / (1 + tipo) y el resto va a la cuenta de IVA soportado en importaciones de la sociedad (4724001), sin cuenta 477. Intracomunitarias con inversión del sujeto pasivo: doble apunte 472/477 al tipo vigente sobre la base, con las cuentas parametrizadas por sociedad. Moneda: los importes se guardan en la moneda de la factura y se pasan a euros con el cambio oficial del día de la factura (o del último día hábil anterior, hasta 7 días); sin cambio no se deja contabilizar. La contabilidad y el libro de IVA van siempre en euros.
- 2026-10-31 07:48 · Cada factura de importación se contabiliza sola, con su IVA de importación al 21 % a la 4724001 (decisión de 07:46): no hay asiento conjunto de la mercancía con la factura del transitario ni se descuenta el IVA del DUA. El DUA es una factura independiente del transitario o agente de aduanas, que se contabiliza como una factura nacional, con lo que paga por cuenta nuestra como suplido (5550010, sin IVA).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 14 RF-COM-07: contabilización de compra, contrato incompleto
- 2026-10-28 13:14 · Q5) b) La lógica de contabilización está dentro del propio nodo (FacturasCompra, FacturasVenta, etc.). Ya tenemos un motor para gestionar Asientos, por lo que mejor conectar este motor directamente cada nodo que ha de generar asientos
- 2026-10-28 12:28 · draft: Contabilizar las facturas de compra registradas mediante el motor de contabilidad/asientos.
- 2026-10-28 12:28 · question: P-08 ¿de dónde sale la cuenta de gasto? (a) la del proveedor, con la última usada como propuesta revisable ✅ (b) reglas por concepto
- 2026-10-29 18:14 · * A veces un asiento queda descuadrado por tema de redondeos (principalmente con apuntes de IVA), si es el caso, hay que ajustarlo con un apunte de diferencias de redondeo o similar
- 2026-10-29 18:14 · * Ok, rectificativas al trimestre activo
- 2026-10-29 21:05 · 1. Bueno, realmente la fecha de la factura se conserva sea la que sea. La fecha contable corresponderá al día en el que se contabiliza. Suponiendo que por fecha contable entendamos asiento.Fecha
- 2026-10-29 21:05 · 2. Creo que lo podemos bajar a 0,03€, sería muy raro un descuadre mayor. Otro tema es qué hacer si se da el caso de que el descuadre sea superior para no llevar al operador a un camino sin salida.
- 2026-10-30 09:58 · 1. En el caso de contabilizar el 1/1/2027 una factura con FechaFactura=31/12/2026, asientos.fecha=1/1/2027. Vaya, la fecha del asiento siempre será 'hoy' (el día en que se genera). Realmente, FechaFactura es básicamente informativo, diría que el uso contable es marginal, no sé si en prorratas de amortizaciones o algo así puede ser útil.
- 2026-10-31 07:30 · En la maqueta no veo como probarlo, en el caso de Importación en el Proconta actual, hago desaparecer todo el panel de IVAs dejando solo un importe ya que las facturas de importación (EEUU, China, etc.) vienen sin IVA, o en caso de venir, no es deducible por lo que no tiene sentido desglosable (luego está el tema de que la auto-repercusión del IVA).
- 2026-10-31 07:30 · Y en el caso de facturas de importación, se puede indicar la moneda y hago conversión a euros aplicando el tipo de cambio de la moneda en la fecha de la factura. En la contabilidad siempre se trabaja en euros.
- 2026-10-31 07:37 · No, la auto-repercusión del IVA es que hay que hay que hacer un apunte en una 472 aplicando el 21% de IVA (bueno, el % vigente).
- 2026-10-31 07:41 · 2. Siempre se aplica el 21% en importaciones, lleve o no lleve IVA desglosado
- 2026-10-31 07:48 · El DUA será una factura independiente suministrada por el carrier o agente de aduanas, por lo que no la tenemos en cuenta.
- 2026-10-29 17:45 · Derived from compras/pantallas: generar el asiento contable (con su libro de IVA, analítica y notas) de cada factura revisada, y deshacerlo o rehacerlo, como hace hoy frmEntradaFacturas (sources/proconta-antiguo/compras/ANALISIS-FUNCIONAL.md § 3-4).
