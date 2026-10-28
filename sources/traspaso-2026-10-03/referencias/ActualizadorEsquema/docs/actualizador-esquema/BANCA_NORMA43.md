# Banca y Norma43

El manifiesto `20260929_007_BANCA_NORMA43` crea diez tablas para entidades y cuentas bancarias, configuración contable, lotes de importación, extractos, movimientos, complementos, incidencias, imputaciones y contabilizaciones. Todas arrancan vacías. La muestra `work/databases/ProConta.Maestra.Banca.accdb` tiene siete versiones registradas y solo catálogos técnicos poblados.

`CuentasBancarias.IdSociedad TEXT(3)` identifica a la sociedad permanente. Su campo `Cuenta TEXT(10)` contiene los diez dígitos bancarios de la Norma43; no es un código de cuenta contable. La configuración usa `IdIdentidadContableBanco` y otras identidades contables estables, y se fecha mediante `FechaDesde` y `FechaHasta`. Al contabilizar se resuelve la cuenta concreta del ejercicio correspondiente. El servicio debe comprobar que cuenta bancaria, identidades, centro de coste y asiento pertenecen a la misma sociedad; las FKs simples no expresan toda esa igualdad.

`LotesImportacionBancaria` incorpora desde la creación `FechaHoraReversion`, `OperadorReversion` y `MotivoReversion`, que eran una migración posterior en el worktree de Norma43. El lote pertenece a una sociedad y el extracto guarda su intervalo de fechas, sin asignarle artificialmente un solo ejercicio. `ContabilizacionesLineaExtracto.IdAsiento` sí apunta al asiento anual real. Los índices únicos impiden repetir contenido importado, ordinal de extracto, movimiento o complemento en su ámbito.

El motor existente todavía consume la estructura heredada y no se ha conectado a esta maestra. La carga y la conciliación deberán validar el ejercicio del asiento y la cuenta contable resuelta en la misma operación. El manifiesto no importa extractos ni modifica las bases originales.
