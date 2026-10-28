# Libros de IVA de ContaSol

El manifiesto `20260929_009_CONTASOL_IVA` crea `IVASoportado`, `DesglosesIVASoportado`, `IVARepercutido` y `DesglosesIVARepercutido`. Su estructura conserva los campos que escribe el importador ContaSol: cabecera fiscal, totales, periodo, tipo de operación y varios tramos de IVA. Las cuatro tablas de negocio arrancan vacías.

Cada cabecera pertenece a `SociedadesEjercicios` y tiene FKs a `PeriodosIVA`, `TiposOperacion` y, si existen, al tercero y al asiento. Cada desglose pertenece a una cabecera, tiene orden único en ella y puede apuntar a cuentas contables del ejercicio. La FK simple asegura la existencia de cada referencia; el importador debe comprobar antes de escribir que periodo, asiento y cuentas pertenecen al ejercicio de la cabecera, y que el tercero corresponde a la sociedad. Esas condiciones cruzadas no se garantizan con las FKs simples de Access.

`FacturasCompra.IdRegistroIVA` aún no se relaciona físicamente con `IVASoportado`: el vínculo exacto de los registros heredados y la regla de conciliación factura-libro necesitan verificarse en el perfil de importación. `TiposOperacion` contiene únicamente los cuatro códigos técnicos que usa el importador. La muestra histórica `work/databases/ProConta.Maestra.ContaSolIVA.accdb` contiene nueve versiones y 65 tablas, sin datos empresariales; precede a la corrección de la semilla técnica.
