# SEG-ACCESO · Integración mínima de permisos para CFG-MANT

Fecha: 01/10/2026. Esta entrega define contrato y composición; no añade DDL ni modifica bases Access.

## API consumible

- `ServicioAccesoReal.RevalidarSesion(sesion)` comprueba en cada operación que el token fue emitido por el servicio, sigue registrado y su operador continúa activo.
- `ServicioPermisosAmbito.EstaAutorizada(sesion, ambito, recurso, operacion)` normaliza identificadores y consulta `IFuentePermisosAmbito` después de revalidar la sesión.
- `EstadoPermisoAmbito` diferencia `SinConfiguracion`, `Permitido` y `Denegado`.
- `FuentePermisosAmbitoNoConfigurado` declara explícitamente un ámbito completo sin configuración. No consulta tablas y no debe utilizarse para ocultar errores de una fuente persistente.
- `ComposicionNucleoFacturacion.CrearPermisosConfiguracionSinConfigurar(acceso)` es la composición autorizada para la entrega actual, donde CFG-MANT todavía no tiene mecanismo persistente de permisos.

`SinConfiguracion` y `Permitido` autorizan. `Denegado` rechaza. Excepciones de esquema, lectura o estados desconocidos se propagan y nunca se transforman en permiso.

## Adaptación al puerto de CFG-MANT

El adaptador de UI-PRINCIPAL implementa `IAutorizacionConfiguracion` conservando la `SesionAcceso` emitida por el mismo `ServicioAccesoReal`:

| Método CFG-MANT | Ámbito | Recurso | Operación |
|---|---|---|---|
| `PuedeAbrirPantalla` | `CFG-MANT` | `PANTALLA` | `ABRIR` |
| `PuedeUsarProveedor` | `CFG-MANT` | identificador estable del proveedor | `USAR` |
| `PuedeEjecutarOperacion` | `CFG-MANT` | identificador estable del proveedor | nombre de `OperacionConfiguracion`: `LEER`, `CARGAROPCIONES`, `CREAR`, `MODIFICAR` o `ELIMINAR` |

El contexto visual transportado por CFG-MANT no concede permisos. El adaptador debe comprobar que `IdOperador` coincide con `sesion.Operador.IdOperador`; la autoridad SEG revalida además la sesión en cada llamada. Los proveedores Access genéricos se consideran compartidos: no se anuncia aislamiento por sociedad o ejercicio que el repositorio no implementa.

## Estado de configuración y evolución

En esta entrega no existe tabla esperada para CFG-MANT: se ha decidido explícitamente que su ámbito está sin configurar y, conforme a la política común, todo operador autenticado válido queda autorizado. Esto es distinto de que una futura tabla requerida falte o falle; en ese caso la fuente persistente deberá lanzar y la pantalla no ejecutará la operación.

Cuando se acuerde configuración por grupos, SEG-ACCESO deberá aportar otra implementación de `IFuentePermisosAmbito` y ActualizadorEsquema su delta reproducible. Ese trabajo futuro debe determinar el vacío sobre todo el ámbito `CFG-MANT`; dentro de un ámbito configurado, la falta de asignación del operador o de sus grupos devuelve `Denegado`.

No se requiere delta de esquema para integrar la entrega actual.

## Límite especial de Terceros

La autorización completa de Terceros en la entrega de pruebas sobre `ProConta.accdb` sigue siendo una habilitación artificial y limitada acordada por el usuario. No usa `FuentePermisosAmbitoNoConfigurado` como si fuera el contrato persistente de DAT-TERCEROS y no acredita configuración por grupos.
## Subconjunto exacto para integración

| SHA256 | Fuente |
|---|---|
| `016F1EA5A868D256324495EDCB3287A34E082606AADB8D6A44DD6177CD0BB360` | `src/ProConta.Facturacion.Aplicacion/Seguridad/ContratosPermisosAmbito.vb` |
| `5080714973AA8DF9A3638344E89F0F23701ADD6A21CA6E84536919B8847477C8` | `src/ProConta.Facturacion.Aplicacion/Seguridad/ServicioPermisosAmbito.vb` |
| `C56BF7B31B2FF27AC583D67494DCEA50155A5BD3251519CC2D414E520B175825` | `src/ProConta.Facturacion.Aplicacion/Seguridad/ServicioAccesoReal.vb` |
| `15887FE90EFA25415F1BC914CD339B1538ADFABBB8C479647A6F6DD5E1ADA8A5` | `src/ProConta.Facturacion.Aplicacion/Seguridad/ContratosAccesoReal.vb` |
| `5FC34B81BE289A2B9A93C5FDD829FA341E3DCAB2185CC833E4F2D9A754B3FEA5` | `src/ProConta.Facturacion.App/ComposicionNucleoFacturacion.vb` |

Los dos primeros archivos y la nueva función `RevalidarSesion` constituyen el contrato funcional mínimo. `ContratosAccesoReal.vb` y el resto de `ServicioAccesoReal` ya forman parte del snapshot SEG previo. La función de composición es una comodidad para la aplicación central; UI-PRINCIPAL puede construir el mismo servicio con `New ServicioPermisosAmbito(acceso, New FuentePermisosAmbitoNoConfigurado())` sin copiar otras composiciones.

## Evidencia

- 23 pruebas focales de `ServicioAccesoRealTests` y `ServicioSeguridadTests`: superadas.
- Solución completa `Debug/x64`: 0 errores; advertencias existentes de auditoría NuGet sin red, XAML LEOC versionado y analizadores.
- No se abrió ni modificó una base Access original.