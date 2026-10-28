# Primer acceso WPF con datos reales

Encargo autorizado el 30/09/2026. Estado: implementación integrada y comprobada; entrega .5 disponible para validación visual del usuario.

## Requisitos confirmados

- Referencia visual: tools/ProConta.Acceso.Prototipo, entrega .4. Ventana sin marco ni caption visible, arrastre en panel azul y zonas libres del derecho, Salir visible.
- Orden: operador, contraseña, empresa autorizada, ejercicio. No revelar empresas antes de autenticar.
- BD de pruebas: C:/Docs/VeriFactu/ProcontaVF.accdb. Conservar credenciales y datos existentes.
- Recordar empresa por operador y ejercicio por operador/empresa tras completar entrada. La preferencia no concede autorización.
- Ejercicios cerrados accesibles, conservando las protecciones de operaciones; entrar no reabre. Reapertura fuera del alcance de este login.
- AccesoSociedadesGrupo globalmente vacía: acceso a todas las sociedades para todos los grupos. Con cualquier fila: unión de concesiones de los grupos del operador. Grupo nuevo sin filas no aporta acceso.
- Ausencia de tabla o fallo de consulta no equivale a tabla vacía. Detener acceso con diagnóstico.
- Borrar la última concesión habilitaría acceso general: el futuro mantenimiento deberá confirmarlo explícitamente. No se implementa ese mantenimiento en esta entrega.

## Reparto de implementación

| Propietario | Salida | Zona de escritura |
|---|---|---|
| SEG-ACCESO | Servicios de acceso real, autorización y sesión; contrato INTEGRACION_ACCESO_REAL.md | ProConta.Seguridad |
| ActualizadorEsquema | DDL reproducible, prueba sobre copia, aplicación mínima con backup/exclusividad; ACCESO_SOCIEDADES_GRUPO.md | ProConta.ActualizadorEsquema y cambio autorizado de estructura en BD de pruebas |
| UI-BASE | Recursos WPF extraídos del prototipo | tools/ProConta.UI.Base |
| UI-PRINCIPAL | Arranque, login conectado y contenedor mínimo; INTEGRACION_LOGIN_WPF.md | tools/ProConta.Acceso.Prototipo y ProConta.Acceso.Comprobacion |
| Auditor | Revisión cruzada de contratos, evidencias y límites | Este documento y directorio central |

CFG-GLOBAL aporta resolución de ubicación existente. La consulta organizativa debe quedar encapsulada detrás de un puerto independiente; el propietario del mantenimiento de Sociedades/SociedadesEjercicios sigue pendiente y no se transfiere a Seguridad.

## Criterios de integración

- Conservar identidad de sociedad permanente y ejercicio; confirmar tipos del esquema instalado, que puede diferir de la maestra nueva.
- DTO de sesión y selección sin hashes, salts ni contraseña. Instante de autenticación explícito.
- Sin grupos activos: criterio técnico propuesto de denegación, pendiente de ratificación funcional; no asignar grupos automáticamente.
- Salir/cancelar invalida resultados tardíos; no abrir contenedor después de cerrar el login.
- Las pruebas sobre copias pueden crear datos sintéticos; no crear usuarios ni concesiones ficticias en la original.
- Una sola autoridad aplica DDL: ActualizadorEsquema. El usuario confirmó cierre de la BD; el archivo de bloqueo desapareció, pero debe comprobarse exclusividad real al ejecutar.
- Validar copia y backup antes del DDL. No recrear ni actualizar todos los submodelos de la original para incorporar una sola tabla.
- Entregar versión manual nueva posterior a .4 con ruta, metadatos, rama, commit y configuración. La demo debe ser explícita y no mezclarse con la autenticación real.

## Hallazgos de integración

- Las preferencias persistentes requieren `PreferenciasAccesoOperador` y `PreferenciasAccesoEjercicio`, además de `AccesoSociedadesGrupo`. El contrato físico se publica en `ProConta.Seguridad/docs/INTEGRACION_ACCESO_REAL.md`; las tres tablas se crean vacías.
- `Activa` no representa cierre contable. El campo canónico es `SociedadesEjercicios.EjercicioCerrado`, definido por Asientos en `database/migrations/20260907_002_asientos_automaticos.sql`. La copia de la BD de pruebas no lo contiene: el login expone estado desconocido y permite seleccionar, sin inventar apertura ni modificar ese estado.
- Revalidar sesión/contexto debe comprobar permisos sin guardar de nuevo preferencias; estas se guardan al completar la entrada.
- UI-BASE publica recursos consumibles en el ámbito de la ventana, sin estilos implícitos globales para otras pantallas.

## Evidencia cerrada

- ActualizadorEsquema: commit `b11e1c0`, rama `codex/actualizador-esquema`. Tres tablas aplicadas al original con ACE exclusivo, PK, índice y siete FKs verificados. Los registros de las cinco tablas existentes coinciden con el backup mediante comparación de contenido. Solución compilada sin errores.
- Backup previo verificado: `C:/Docs/VeriFactu/Backups/ProConta.ActualizadorEsquema/ProcontaVF.AccesoSociedadesGrupo.20260930-201536.d67b8f1eed2a46f8aad324075abb7ee6.accdb`. SHA-256: `7445C8B98660FCDDFD9DA5A64C064F3E5CD7217DE5B526770713676AD9B87965`.
- SEG-ACCESO: commit `b0face7ca1e95b0fa58ae74d275839dfdba9f865`, rama `codex/seguridad`. 21 pruebas relacionadas superadas y solución Debug/x64 sin errores. Contrato: `C:/Docs/VeriFactu/ProConta.Seguridad/docs/INTEGRACION_ACCESO_REAL.md`.
- Auditor contrastó por SHA-256 los once archivos de Seguridad incorporados en la integración WPF: sin divergencias frente a la fuente canónica al cierre de Seguridad.

## Entrega integrada

UI-PRINCIPAL entrega `1.21.0-integracion-proconta-20260822.5`, Debug/x64, en `C:/Docs/VeriFactu/ProConta.Facturacion/tools/ProConta.Acceso.Prototipo/bin/x64/Debug/net10.0-windows/win-x64/ProConta.Acceso.Prototipo.exe`. Auditor verificó metadatos del binario sin ejecutarlo. Rama `codex/integracion-proconta-20260822`, base `773d3e491687ffe72a3cd889566968e56461acf0`; integración local sin commit, conservando cambios ajenos.

34 comprobaciones superadas y comprobador terminado con código 0; compilación integrada sin errores ni advertencias. Solución completa sin errores, con dos advertencias preexistentes WindowsBase/WebView2. Prueba ACE realizada sobre copia privada. Detalles reproducibles en `docs/INTEGRACION_LOGIN_WPF.md`.

Acceso real por defecto; indicar `--directorio-datos C:/Docs/VeriFactu` para probar sobre la base autorizada. Demo únicamente con `--demo`. Contenedor mínimo dentro de la misma ventana y sesión vigente; aún no incluye operaciones de negocio. Pendientes: validación visual del usuario, ratificación funcional del operador sin grupos y futura integración de los módulos de negocio y su cierre contable.
