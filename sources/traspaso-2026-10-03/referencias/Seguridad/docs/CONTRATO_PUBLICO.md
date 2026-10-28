# SEG-ACCESO · Contrato público de autenticación, permisos y entorno

Estado: ficha inicial verificable; no implica normalización terminada. Fecha: 01/10/2026.

## 1. Identidad

| Campo | Valor comprobado |
|---|---|
| Clave | `SEG-ACCESO` |
| Hilo | `01a07b3f-cb00-7651-9d36-3bab014f82bb` · SEG-ACCESO · Autenticación, permisos y entorno |
| Responsable | Identidad de operadores, autenticación local, grupos, permisos y auditoría de seguridad |
| Ruta / rama | `C:/Docs/VeriFactu/ProConta.Seguridad` · `codex/seguridad` |
| Commit | `b0face7ca1e95b0fa58ae74d275839dfdba9f865` · Implementa el contrato de acceso real |
| Gobierno | [Mapa](../../ProConta.Facturacion/docs/MAPA_PROYECTO.md) y [registro](../../ProConta.Facturacion/docs/REGISTRO_HILOS.json) |

La implementación está en esta rama; no se acredita aquí su integración en la rama principal.

## 2. Responsabilidad y exclusiones

SEG-ACCESO es autoridad sobre operadores locales, credenciales, grupos y pertenencias, política de autorización, contraseñas y auditoría de seguridad.

No es autoridad sobre presentación y navegación, contexto visual actual, terceros/empleados, sociedades/ejercicios, transporte de correo ni catálogos de reports y procesos. UI-PRINCIPAL presenta y orquesta, pero no decide identidad o permisos. ActualizadorEsquema materializa el DDL sin adquirir la propiedad funcional. CFG-MANT no adquiere tablas por mostrarlas.

No existe consenso implementado que atribuya a Seguridad el contexto de sociedad/ejercicio; actualmente lo recibe de la aplicación principal solo para elegir la cuenta remitente.

## 3. Capacidades ofrecidas

### 3.1 Autenticación local — implementada en la rama

- Contrato: `ServicioSeguridad.IniciarSesion(idOperador, password) As SesionOperador` en [ServicioSeguridad.vb](../src/ProConta.Facturacion.Aplicacion/Seguridad/ServicioSeguridad.vb).
- Entrada/salida: ID y contraseña en memoria; devuelve `ResultadoLogin` y, si procede, `Operador`, definidos en [ModeloSeguridad.vb](../src/ProConta.Facturacion.Dominio/Seguridad/ModeloSeguridad.vb).
- Errores: inexistente, inactivo o contraseña incorrecta, sin revelar en la UI qué credencial falló.
- Permiso/contexto: no requiere permiso previo ni sociedad/ejercicio; sí una base compatible.
- Efectos: audita éxito o fallo sin contraseña. Usa PBKDF2-HMAC-SHA256 y comparación en tiempo constante.

No implementa bloqueo por intentos, segundo factor, expiración ni autenticación remota.

### 3.2 Sesión actual, instante y contexto — implementada en la rama, pendiente de integración WPF

`ServicioAccesoReal.Autenticar` emite una `SesionAcceso` en memoria con token aleatorio, `IdentidadOperadorAcceso` sin hashes y `FechaHoraAutenticacion As DateTimeOffset`. Sus constructores no son públicos para consumidores y el servicio revalida que el token y la instancia proceden de él, además de que el operador siga activo.

- `CerrarSesion` invalida el token.
- `ListarSociedades` revalida sesión y grupos.
- `ListarEjercicios` incluye ejercicios activos e inactivos; `EstaCerrado As Boolean?` diferencia abierto, cerrado y desconocido cuando una maestra antigua aún carece del campo canónico.
- `CompletarAcceso` emite `ContextoAccesoValidado` y solo entonces guarda últimas selecciones.
- `Revalidar` comprueba sesión, autorización y relaciones sin reescribir preferencias.
- Cambiar sociedad/ejercicio exige completar un contexto nuevo; no modifica ni reabre el ejercicio.

El contrato consumible y físico está en [INTEGRACION_ACCESO_REAL.md](INTEGRACION_ACCESO_REAL.md). UI-PRINCIPAL presenta y orquesta este servicio, pero no se convierte en autoridad de identidad o permisos.
### 3.3 Operadores y contraseñas — implementada en la rama

- Métodos: `HayOperadores`, `ListarOperadores`, `GuardarOperador`, `AsignarPassword`.
- Entradas/salidas: ficha, actor y contraseña cuando corresponde; existencia, lista o ID.
- Errores: nombre obligatorio/duplicado; no hay borrado público.
- Permisos: aún no comprueba que el actor sea administrador.
- Efectos/contexto: escribe operador, asigna `OPERADORES` al alta y audita; ámbito global.

Persiste hash, salt, iteraciones y temporalidad, nunca contraseña en claro.

### 3.4 Grupos y pertenencias — implementada en la rama

- Métodos: `ListarGrupos`, `GuardarGrupo`, `ObtenerGruposOperador`, `EstablecerGruposOperador`.
- Entradas/salidas: grupo, operador, IDs y actor; catálogo, pertenencias o ID.
- Errores: nombre vacío, duplicidad o referencia inexistente.
- Permisos: falta autorización administrativa autoaplicada.
- Efectos/contexto: persiste grupos y sustituye pertenencias transaccionalmente; ámbito global.

`ADMINISTRADORES` y `OPERADORES` son grupos iniciales; su nombre no es por sí solo una comprobación.

### 3.5 Política común para ámbitos configurables — acordada

La política de permisos es uniforme y se evalúa de forma independiente dentro de cada ámbito funcional:

- Si no existe ninguna configuración de permisos en todo el ámbito, todos los operadores autenticados y todavía válidos tienen permiso.
- En cuanto existe al menos una configuración dentro del ámbito, deja de aplicarse el acceso general y se resuelve la autorización mediante las asignaciones del ámbito.
- Dentro de un ámbito ya configurado, que un grupo no tenga una asignación no equivale a que el ámbito esté vacío y no le concede acceso implícito.
- Una tabla requerida ausente, un error de lectura o cualquier incompatibilidad técnica detienen la autorización; nunca se interpretan como ausencia de configuración.
- Esta regla no sustituye la revalidación de sesión ni las restricciones de sociedad, ejercicio o contexto aplicables a la operación.

«Ámbito» significa un conjunto funcional de permisos con configuración propia, por ejemplo acceso a sociedades, operaciones de Terceros, reports o procesos contables. Vacío se determina sobre el ámbito completo, no sobre las filas de un operador o de uno de sus grupos.

Este acuerdo reemplaza la recomendación anterior de denegación por defecto para ámbitos todavía sin configurar. No cambia automáticamente las implementaciones históricas basadas en restricciones: cada adaptador debe documentar cómo materializa esta política y señalar cualquier divergencia hasta su adaptación.

### 3.6 Reports — implementada en la rama

- Interfaz: `IAutorizadorReports.PuedeAcceder(idOperador, codigoReport)` en [ContratosSeguridad.vb](../src/ProConta.Facturacion.Aplicacion/Seguridad/ContratosSeguridad.vb).
- Mantenimiento: `ObtenerRestriccionesReport`, `EstablecerRestriccionReport`.
- Entrada/salida: operador/grupo y código de Reporting; permitido o restricciones.
- Política: permitido por defecto; una restricción en cualquier grupo deniega.
- Errores/efectos: valida grupo/código; mantener crea/elimina excepción y audita.
- Contexto: global, sin catálogo local ni sociedad/ejercicio.

### 3.7 Procesos contables — implementada en la rama

- Interfaz: `IAutorizadorProcesosContables.PuedeEjecutar(idOperador, codigoProceso)`.
- Mantenimiento: `ObtenerRestriccionesProcesosContables`, `EstablecerRestriccionProcesoContable`.
- Contrato: política equivalente a reports; códigos de GestorContable normalizados en mayúsculas invariantes.
- Contexto: global por grupo, sin catálogo físico ni ejercicio.

Contrato físico enlazado: [migración 003](../database/migrations/20260907_003_restricciones_procesos_contables.sql).

### 3.8 Contraseña temporal por correo — implementada en la rama

- Método: `RegenerarPasswordAsync`; puerto `IEnviadorPasswordTemporal.EnviarAsync`; adaptador [EnviadorPasswordTemporalSmtp.vb](../src/ProConta.Facturacion.Integraciones/Correo/EnviadorPasswordTemporalSmtp.vb).
- Entrada/salida: operador guardado, activo, con email y actor; tarea tras aceptación del transporte.
- Errores: operador, correo, contexto o cuenta inválidos; fallo de transporte.
- Permisos: falta autorización administrativa formal.
- Efectos: guarda hash temporal y envía el secreto; ante error restaura la credencial anterior y audita sin secreto.
- Contexto: identidad global; cuenta remitente seleccionada por `IdSociedadEjercicio`.

Cuentas, secretos y transporte pertenecen a [Gestor Correo](../../ProConta.GestorCorreo/docs/GESTOR_CORREO.md); Seguridad solo consume ese contrato.

### 3.9 Consulta de auditoría — propuesta

La escritura existe en `IRepositorioSeguridad.RegistrarAuditoria`; no hay consulta pública, paginación, retención ni exportación. Una futura UI debe consumir un contrato de SEG-ACCESO, no leer la tabla.

## 4. Tablas y datos

| Tabla/dato | Propiedad y escritura |
|---|---|
| `OperadoresSeguridad` | SEG-ACCESO; `RepositorioSeguridadAccess` |
| `GruposOperadores` | SEG-ACCESO; repositorio y migración inicial |
| `OperadoresGrupos` | SEG-ACCESO; repositorio |
| `RestriccionesReportsGrupo` | SEG-ACCESO; Reporting publica códigos |
| `RestriccionesProcesosContablesGrupo` | SEG-ACCESO; GestorContable publica códigos |
| `AuditoriaSeguridad` | SEG-ACCESO; repositorio |
| `CuentasCorreo` y asociaciones | INF-COR; Seguridad consulta |
| `SociedadesEjercicios` | Propietario organizativo pendiente; Seguridad no escribe |

Migraciones: [motor](../database/migrations/20260822_003_motor_seguridad.sql), [identidad/grupos](../database/migrations/20260822_004_identidad_grupos_seguridad.sql) y [procesos](../database/migrations/20260907_003_restricciones_procesos_contables.sql). ActualizadorEsquema las aplica; SEG-ACCESO conserva la semántica.

## 5. Dependencias e integración

- **UI-PRINCIPAL:** presenta/orquesta acceso y contexto; consumirá la futura sesión.
- **UI-BASE:** componentes visuales, no política.
- **CFG-MANT:** mantenimiento mediante servicios, sin propiedad de tablas.
- **Configuración organizativa:** aporta sociedad/ejercicio; propietario pendiente.
- **INF-COR:** cuenta, secreto y transporte.
- **INF-REP:** publica códigos y consume `IAutorizadorReports`.
- **CON-GES:** publica códigos y consume `IAutorizadorProcesosContables`.
- **INF-ESQ:** aplica migraciones, no decide política.
- **DAT-TERCEROS:** publica sus operaciones; SEG-ACCESO debe aportar la autorización permanente. La entrega limitada de pruebas sobre ProConta.accdb está autorizada para lectura, edición, notas internas y perfiles, pero esa habilitación de prueba no sustituye el futuro contrato persistente por grupos.
- **Access:** [RepositorioSeguridadAccess.vb](../src/ProConta.Facturacion.Persistencia.Access/Seguridad/RepositorioSeguridadAccess.vb).

## 6. Ejemplo mínimo

```vb
Dim autorizado = autorizadorProcesos.PuedeEjecutar(idOperador, "IVA.LIQUIDAR")
If Not autorizado Then
    ' La presentación informa y no ejecuta el proceso.
End If
```

Propuesta aún inexistente:

```vb
' PROPUESTO: nombres y forma final pendientes.
Dim sesion = proveedorSesion.ObtenerActual()
If sesion Is Nothing Then Await navegador.SolicitarAutenticacionAsync()
```

UI-PRINCIPAL no reconstruirá permisos desde nombres ni leerá tablas directamente.

## 7. Evidencia y límites

El commit contiene [ServicioSeguridadTests.vb](../tests/ProConta.Facturacion.Tests/Seguridad/ServicioSeguridadTests.vb), [EnviadorPasswordTemporalSmtpTests.vb](../tests/ProConta.Facturacion.Tests/Seguridad/EnviadorPasswordTemporalSmtpTests.vb), [MigracionSeguridadAccessTests.vb](../tests/ProConta.Facturacion.Tests/Seguridad/MigracionSeguridadAccessTests.vb) y [CatalogoMigracionesTests.vb](../tests/ProConta.Facturacion.Tests/CatalogoMigracionesTests.vb). Cubren autenticación, restricciones, pertenencias, regeneración/restauración, correo e idempotencia/identidad de migraciones.

En la entrega del commit se registraron compilación completa sin errores y pruebas focales superadas. Esta ficha no las repite ni certifica integración. La rama incorpora Gestor Correo por merge; no consta aquí su integración en principal ni en la futura WPF.

## 8. Decisiones abiertas y próximos pasos

1. Ratificar duración/expiración de la sesión en memoria; identidad, instante con offset, cierre y revalidación ya están implementados en la rama.
2. Acordar arranque sin operadores; el flujo heredado usa Windows, pero `ServicioAccesoReal` no emite una sesión sin autenticar.
3. Ratificar que cambiar empresa/ejercicio conserva la sesión pero exige autorización y contexto nuevos, como implementa provisionalmente el servicio.
4. Materializar la política común por ámbito en los autorizadores que aún carecen de configuración persistente y registrar las divergencias de los adaptadores históricos.
5. Añadir permiso administrativo real a mantenimientos sensibles.
6. Definir consulta, retención y zona horaria de auditoría.
7. Definir códigos, persistencia y mantenimiento por grupos para Terceros/empleados sin fusionar identidades. La regla de ámbito vacío ya está acordada y no es un punto pendiente.
8. Acordar con UI-PRINCIPAL el puerto y eventos de sesión; registrar desacuerdos.
9. Mantener esta ficha al cambiar el contrato; solo el responsable autorizado modifica el mapa central.
