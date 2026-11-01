---
title: Acceso y sesión
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
- 2026-11-01 09:30 · Flujo de la pantalla de acceso: el bloque «Dónde quieres trabajar» (sociedad y ejercicio) está oculto hasta que el operador y la contraseña son correctos. Se comprueban al salir de la contraseña (Tab), con Intro en la contraseña o con el botón, que se llama «Entrar» desde el principio. Si son correctos, aparecen la sociedad y el ejercicio con la última elección del operador propuesta y el foco en «Entrar»; Intro (o clic) entra. Un operador sin contraseña elegido de la lista entra al elegirlo (`AutenticarSinContrasenaAsync`, que no registra ningún intento fallido si el operador tiene contraseña). No se comprueba a cada pulsación. Si falla, mensaje genérico y el bloque sigue oculto.
- 2026-11-01 09:30 · Parámetro general de seguridad «Mostrar la lista de operadores en el acceso», activado por defecto, guardado en negativo como Configuracion.OcultarListaOperadoresAcceso (migración 20261101_0925_LISTA_OPERADORES_ACCESO, esquema versión 7): con la lista, un desplegable con los operadores activos; sin ella, se escribe el nombre. En los dos casos, cada puesto recuerda el último operador que entró (configuración local) y lo propone.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-01, 02, 05, 06: cuatro fases, preferencias de empresa/ejercicio, sesión sin credenciales, hash PBKDF2
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Seguridad/docs/: contrato público e integración del acceso anteriores
- 2026-10-28 12:28 · draft: Implementar la autenticación en cuatro fases y la sesión validada.
- 2026-10-29 17:45 · From sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 1, 6.4: el acceso antiguo obligaba a identificarse sin poder cancelar; el nuevo permite salir de la identificación.
- 2026-10-31 17:53 · Derived from architecture: la lógica del acceso en cuatro fases con sesión validada (FechaHoraAutenticacion) y hash PBKDF2 de la biblioteca estándar, sin pantalla.
- 2026-10-31 19:02 · 3. No, de momento podemos trabajar sin contraseñas
- 2026-10-31 19:02 · answer: ¿obligar a los operadores sin contraseña a definirla? → no, de momento se trabaja sin contraseñas
- 2026-11-01 09:12 · En la primera fase del login, una de dos, o no se muestra 'DÓNDE QUIERES TRABAJAR' (ya que está disabled y no aporta uso alguno) o pasa a enabled en el momento que la combinación usr/pwd es correcta (se haría una validación a cada change de alguno de estos dos campos) sin tener que darle a Siguiente. Esta segunda opción ahorra un click aunque no tengo claro si a nivel de seguridad es una vulnerabilidad grave o no. En caso de aplicar esta segunda opción, faltaría remarcar mejor que la sociedad y ejercicio están disabled (o directamente, dejarlos invisible hasta que pasan automáticamente a visible si usr/pwd ok). La idea es que un operador no logeado no debe saber qué sociedades existen y, obviamente, uno logeado solo verá las que tiene acceso.
- 2026-11-01 09:12 · Operador con desplegable. Supongo que no es lo ideal a nivel de seguridad, pero ahorra tener que escribir cada vez el nombre de usuario. A malas, un parámetro general del módulo de seguridad para que sea desplegable o no.
- 2026-11-01 09:18 · Todo ok, lo único es que, en ese caso, mantendremos el botón Siguiente (pero con el texto 'Entrar') para que un nuevo operador sepa cómo provocar un lost focus. Si todo va bien, al hacer TAB en contraseña, de repente aparece sociedad/ejercicio con el valor anterior por defecto y foco en el botón Entrar. Un intro entra.

## Hand-over
- 2026-11-01 09:30 · Hecho: servicio de acceso y pruebas (primer administrador, mensajes iguales ante cualquier fallo y auditoría sin secretos, operador sin contraseña, cuatro fases con ámbito configurado y sin configurar, grupo sin filas, ejercicio cerrado, preferencias, revalidación de sesión y cierre, lista de operadores y parámetro, autenticación al elegir un operador sin contraseña). Pendiente: cambio de contexto sin volver a autenticar (lo usará el contenedor); mantenimiento del parámetro de la lista desde la pantalla de configuración.
