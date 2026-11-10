# ProConta · Editor del borrador de factura (prototipo)

`proconta-borrador.html` se abre directamente en el navegador; no usa servidor y los cambios se quedan en memoria. Es la pestaña que abren «Nueva factura» y «Editar borrador» de la pantalla de Facturas emitidas (`sources/maquetas/Facturas/`), con sus mismos tokens.

La barra gris con borde amarillo es solo de demostración: escenarios (borrador vacío, con líneas, cliente sin cuenta de retención, total cero, rectificativo R4 y factura validada) y operador completo o de consulta.

Qué enseña: cliente con sus cuentas de cliente y de retención por sociedad (y «Asignar…» cuando faltan), datos de la factura, líneas con cálculo en vivo (descuento % y luego fijo, redondeo por línea, suplidos sin IVA, total con IVA que recalcula la base), totales con desglose de IVA, vista previa del asiento, historial, errores en el campo y en el resumen, diálogo de validación con el aviso de importe cero, factura validada «sin VeriFactu» en solo lectura (solo notas internas) y rectificar.
