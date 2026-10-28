# Corrección acotada del listado de Terceros — 01/10/2026

## Entrega para UI-PRINCIPAL

Copiar los **cuatro archivos de producción completos** listados abajo desde este worktree a sus ubicaciones equivalentes del contenedor. Los hashes exactos están en `LISTADO_TERCEROS_RENDIMIENTO_20261001.sha256.json`. El adaptador implementa el IPantallaConOperacion existente; no redefine contratos, esquema ni reglas de permisos. No se ha editado el repositorio central ni generado un EXE para el usuario.

- `src/ProConta.Facturacion.Persistencia.Access/Terceros/RepositorioTercerosAccess.Ficha.vb`
  - SHA-256: `29222BD14A65A42FF95897B11567D7E604D7C9FB54F89C7BEB147DE9564535AA`
- `src/ProConta.Facturacion.Aplicacion/Terceros/FichaTercero.vb`
  - SHA-256: `770B59AB45685A11CB7ADD132B95498E401FF8CBFF4784167D524942136D78E7`
- `src/ProConta.Terceros.Wpf/VistaListadoTerceros.vb`
  - SHA-256: `9EEFAE06618D18372E119C47F7060549FC39AF31DD75B12AA87D0C2400F83EE6`
- `integracion/TercerosWpf/PantallaTercerosWpf.vb`
  - SHA-256: `ED88BFB4D4625AAD83A867902202DE929225AD39DD7FC619186E5A6FB0E4ADC3`

## Cambios

- `ListarFichas` ejecuta siete consultas de datos por conjuntos, además de la validación de esquema existente. No llama a LeerFicha por fila y no carga relaciones, historial, notas internas, datos de rol ajenos al resumen ni huellas de concurrencia.
- Mantiene ORDER BY Nombre,IdTercero, nombres de persona/organización y clasificación pendiente, roles activos, perfiles asignados activos y exactamente el texto de búsqueda anterior. Incluye datos inactivos en la búsqueda igual que antes. Conserva los respaldos escalares de contactos y dirección cuando corresponden, sin inventar país para NULL.
- `ServicioFichaTercero.CargarListadoAsync` autoriza Leer antes de consultar, ejecuta el acceso a datos fuera del hilo UI y revalida Leer antes de entregar. La consulta ACE iniciada termina y libera su conexión; cancelar descarta la entrega.
- El constructor y Refrescar de VistaListadoTerceros inician la carga asíncrona. CargaActual permite observar su finalización sin esperar sincrónicamente en UI. Conserva los filtros y la selección por identidad; una nueva recarga cancela la entrega anterior. Dispose cancela y los resultados tardíos no se aplican a una vista cerrada/contexto retirado. TieneCambiosPendientes continúa False.

## Pruebas y medición

- Copia privada de `C:\Docs\VeriFactu\ProConta.accdb`, verificada por hash antes/después de copiar. SHA-256 de entrada: `27B201CAEE2B0C344AC5EB5F8AAB45037C47BB047B1291F9CE26881BA03B0804`.
- 606 filas. Cinco lecturas completas mediante ServicioFichaTercero.Listar: **163, 180, 192, 170 y 158 ms** (media 172,6 ms). Referencia comunicada por integración antes del cambio: 8966 ms. Los tiempos dependen del equipo/caché; no son un SLA.
- Comparación contra cada ficha completa: **606/606 equivalentes** en nombre, tipo, NIF, población, texto íntegro de búsqueda, roles, perfiles y orden. No se imprimieron datos personales.
- Prueba sintética adicional cubre NULL, nombre de pila distinto del nombre usado para ordenar, organización, identidad pendiente/inactiva, contactos inactivos, dirección principal inactiva, respaldos escalares y exclusión de notas internas.
- Prueba STA con consultas retenidas: el constructor devuelve con la consulta pendiente, la segunda recarga termina antes que la primera y no es sobrescrita, cerrar impide entregar datos y retirar autorización antes de terminar deja el listado vacío con aviso. Los filtros cambiados durante la carga se conservan.
- **12 pruebas del módulo correctas**, incluida regresión de ficha, guardado, perfiles, auditoría y filtros. El benchmark de copia real también es correcto.
- Solución completa x64: 0 errores, 31 advertencias (WindowsBase/MSTest; una sugerencia de estilo de aserción en el test nuevo). Adaptador contra el contrato central: 0 errores y 0 advertencias. Diff sin errores de espacios.
- Evidencias locales: `tmp/listado-tests.log`, `tmp/listado-benchmark.log`, `tmp/listado-solucion.log`, `tmp/listado-adaptador.log`.

Las autorizaciones simuladas quedan solo en el harness. Las bases originales no se abrieron mediante ACE ni se modificaron; la medición utilizó exclusivamente la copia privada. No se amplía la habilitación de otros módulos.

## Ajuste final: espera de todas las cargas antes del cierre

Esta revisión sustituye los hashes publicados para VistaListadoTerceros y añade el adaptador a la entrega. Los otros dos archivos de producción conservan sus hashes y la medición anterior sigue correspondiendo al mismo código de consulta.

- VistaListadoTerceros registra todas las tareas iniciadas. `EsperarOperacionAsync` espera el conjunto activo y vuelve a comprobarlo después de cada espera. Las consultas sustituidas/canceladas siguen incluidas hasta que su tarea termina realmente; no se usa solo CargaActual.
- Dispose cancela la entrega, pero no borra el registro de tareas ni libera el pool. El coordinador puede esperar antes o después de Dispose mientras mantenga activo el dispatcher.
- PantallaTercerosWpf implementa `IPantallaConOperacion.EsperarOperacionAsync(CancellationToken)` del contrato central y delega al listado. No se añade otro protocolo ni se modifica el contenedor.
- La espera es asíncrona. Cancelar su token cancela la espera del coordinador, no una consulta ACE en ejecución.
- Prueba específica con dos consultas solapadas: cerrar y liberar la consulta más nueva no completa la espera; liberar la anterior cancelada sí la completa. Se prueba también que no se aplica contenido tras Dispose.
- Las dos pruebas de cierre/recargas y autorización pasan. Solución x64: 0 errores (31 advertencias WindowsBase/MSTest). Adaptador con contrato central: 0 errores y 0 advertencias. Logs `tmp/listado-cierre-tests.log`, `tmp/listado-cierre-solucion.log` y `tmp/listado-cierre-adaptador.log`.

Actualización posterior de Email: incorporar también los archivos descritos en EMAIL_TERCEROS_150_20261001.md. Los hashes anteriores de la persistencia quedan sustituidos por los aquí actualizados.
