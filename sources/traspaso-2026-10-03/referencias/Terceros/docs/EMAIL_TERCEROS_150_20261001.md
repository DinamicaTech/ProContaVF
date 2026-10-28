# Adaptación de Terceros.Email a TEXT(150)

Decisión comunicada por Actualizador: Email escalar de 150 en maestra y ProConta.

- DDL final y ContratoFinal.json ajustados a 150. El detector ya admite esa columna y sigue rechazando capacidad inferior (prueba con 149).
- ServicioTerceros rechaza escrituras escalares de más de 150 con un mensaje de validación antes de SQL.
- La proyección de GuardarFicha copia el email único solamente si cabe completo en 150. No se trunca: DatosContacto.Valor mantiene 254 y conserva el destinatario completo; la proyección escalar queda NULL cuando no cabe.
- 14 pruebas del módulo correctas sobre Access sintético con TEXT(150), incluida conservación/guardado de 150 y contacto de 254.
- Solución x64: 0 errores, 31 advertencias de WindowsBase/MSTest. Logs: tmp/email150-tests.log y tmp/email150-build.log.
- No se modificaron bases originales ni se ensayó la sustitución de la base operativa. Esa operación sigue a cargo de Actualizador. No se envió mensaje a otros hilos ni se generó EXE.

## Archivos finales

Copiar los archivos de producción y el manifiesto embebido al contenedor antes de recompilar. El DDL es referencia para base nueva, no un ALTER que se deba ejecutar sobre la base actual. Los hashes de esta revisión están también en EMAIL_TERCEROS_150_20261001.sha256.json.

- `database/migrations/20261001_002_contrato_final_terceros.sql`: `F978B56DC404B470CE39E67C26FBB27E6AE41F939D0FA107764CE8EA9C0B9FEB`
- `src/ProConta.Facturacion.Persistencia.Access/Terceros/ContratoFinal.json`: `EE8891096FE50CE6C712542AA8430112BABCEFF08AAF5C0A8B0F4E2DB8C97396`
- `src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioTercerosAccess.Ficha.vb`: `29222BD14A65A42FF95897B11567D7E604D7C9FB54F89C7BEB147DE9564535AA`
- `src/ProConta.Facturacion.Aplicacion/Terceros/ServicioTerceros.vb`: `29C250AEBC3DD33056FD36C9EC9B3E312B7EC1E6A1F94F838E062C66AA2FADEA`
- `tests/ProConta.Facturacion.Tests/ContratoFinalTercerosTests.vb`: `0F02ABE9F07121C75EF8E6BAC27232206B12C25BCB74DABCE1F52628738CF5B8`
