---
title: Acceso y sesión
status: draft
depends_on: [seguridad/permisos, organizacion/sociedades-ejercicios, datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 18:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Autenticación en cuatro fases (usuario, contraseña, empresa, ejercicio) y sesión validada con FechaHoraAutenticacion; solo muestra las empresas autorizadas tras autenticar y revalida la sesión en cada operación. Código: `ServicioAcceso` y `SesionAcceso` (src/ProConta.Seguridad/Acceso), sin pantalla (la pantalla es de plataforma/contenedor y seguridad/pantallas).
**Aceptación:** la de spec § 5.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 18:55 · Fases: `AutenticarAsync(usuario, contraseña)` (fases 1 y 2), `ListarSociedadesAsync` (fase 3, solo las autorizadas), `ListarEjerciciosAsync` (fase 4) y `CompletarAsync`, que valida empresa y ejercicio, guarda las preferencias (PreferenciasAccesoOperador y PreferenciasAccesoEjercicio) y devuelve el contexto validado. Se propone la última empresa y el último ejercicio elegidos si siguen siendo accesibles; si no, la única empresa o el ejercicio activo más reciente.
- 2026-10-31 18:55 · Los fallos de autenticación dan siempre el mismo mensaje (usuario inexistente, contraseña errónea u operador inactivo); la auditoría registra el motivo sin la contraseña. Una empresa inexistente y una no autorizada dan el mismo error, para no descubrir empresas.
- 2026-10-31 18:55 · Sesión: identidad sin credenciales, FechaHoraAutenticacion con desfase horario y un token aleatorio que solo conoce el servicio que la emitió; `ValidarAsync` y `ValidarContextoAsync` comprueban en cada operación que la sesión no se ha cerrado, que el operador sigue activo y que la empresa sigue autorizada. Cerrar la sesión invalida el token. Un ejercicio cerrado permite entrar y el contexto lo indica.
- 2026-10-31 18:55 · [replaced by 2026-10-31 19:02] Compatibilidad: los operadores de la maestra que no tienen contraseña (los dos activos) entran con la contraseña vacía, como en el programa anterior, y la sesión lo marca (`SinContrasena`) para que el contenedor proponga definir una. Un operador con contraseña nunca entra con la contraseña vacía.
- 2026-10-31 19:02 · De momento se trabaja sin contraseñas: un operador sin contraseña entra con la contraseña vacía, como en el programa anterior, sin que se le pida definir una al entrar; la sesión conserva internamente la marca `SinContrasena`. Un operador con contraseña nunca entra con la contraseña vacía.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-01, 02, 05, 06: cuatro fases, preferencias de empresa/ejercicio, sesión sin credenciales, hash PBKDF2
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Seguridad/docs/: contrato público e integración del acceso anteriores
- 2026-10-28 12:28 · draft: Implementar la autenticación en cuatro fases y la sesión validada.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 1, 6.4: el acceso antiguo obligaba a identificarse sin poder cancelar; el nuevo permite salir de la identificación.
- 2026-10-31 17:53 · Derived from architecture: la lógica del acceso en cuatro fases con sesión validada (FechaHoraAutenticacion) y hash PBKDF2 de la biblioteca estándar, sin pantalla.
- 2026-10-31 19:02 · 3. No, de momento podemos trabajar sin contraseñas
- 2026-10-31 19:02 · answer: ¿obligar a los operadores sin contraseña a definirla? → no, de momento se trabaja sin contraseñas

## Hand-over
- 2026-10-31 19:10 · Hecho: servicio de acceso y pruebas (primer administrador, mensajes iguales ante cualquier fallo y auditoría sin secretos, operador sin contraseña, cuatro fases con ámbito configurado y sin configurar, grupo sin filas, ejercicio cerrado, preferencias, revalidación de sesión y cierre). Pendiente: cambio de contexto sin volver a autenticar (lo usará el contenedor); la pantalla de acceso (paso del contenedor).
