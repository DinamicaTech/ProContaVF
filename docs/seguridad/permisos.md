---
title: Operadores y permisos
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 18:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Operadores, grupos (N:M) y permisos por ámbito: ámbito sin configurar permite a todo operador válido; ámbito configurado aplica sus asignaciones. Los servicios comprueban la autorización, no solo la pantalla. Código (src/ProConta.Seguridad/Permisos): `PoliticaAmbito` (regla común), `AutorizadorSociedades` (ámbito AccesoSociedadesGrupo), `ServicioOperadores` (buscar operador, grupos activos, primer administrador, contraseña) y `HashContrasena` (PBKDF2-HMAC-SHA256).
**Aceptación:** grupo sin filas no hereda acceso en un ámbito configurado; tabla ausente no equivale a ámbito sin configurar.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Si no hay ningún operador, un asistente crea el primer administrador; la maestra no trae operadores por defecto (P-03).
- 2026-10-31 16:54 · Bloqueo por intentos, expiración de contraseña y segundo factor quedan fuera del primer incremento (P-03).
- 2026-10-31 18:55 · `PoliticaAmbito.Permite` aplica la regla común a cualquier ámbito: sin asignaciones en todo el ámbito, permiso para todos; con alguna, solo los grupos activos del operador que tienen la asignación. Si la tabla del ámbito falta o no se puede leer, la excepción de datos se propaga (nunca se trata como ámbito vacío). Primer ámbito materializado: acceso a sociedades (AccesoSociedadesGrupo).
- 2026-10-31 18:55 · Primer administrador: si no hay ningún operador, `CrearPrimerAdministradorAsync` crea uno con contraseña (mínimo 8 caracteres) y lo pone en el grupo ADMINISTRADORES (lo crea si falta), en una sola transacción y con auditoría; si ya hay operadores, se niega.
- 2026-10-31 18:55 · Contraseñas: PBKDF2-HMAC-SHA256 con salt de 16 bytes, hash de 32 y 210.000 iteraciones, comparación en tiempo constante; mismo formato que el programa anterior, así que las credenciales guardadas siguen valiendo.
- 2026-10-31 18:55 · Un nombre de operador que aparece dos veces (sin distinguir mayúsculas) no identifica a nadie: no se puede entrar con él hasta que se corrija.
- 2026-10-31 19:02 · No se obliga a los operadores a tener contraseña; el primer administrador sí la define (mínimo 8 caracteres) y cualquier operador puede tenerla.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-03, 04, 07: operadores y grupos, política de ámbitos, puntos pendientes
- 2026-10-28 12:28 · draft: Implementar operadores, grupos y la política de permisos por ámbitos.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-03 ¿arranque sin operadores? (a) asistente que crea el primer administrador si no hay ninguno ✅ (b) operador por defecto en la maestra
- 2026-10-31 16:54 · 30. a
- 2026-10-31 16:54 · answer: arranque sin operadores → (a) asistente que crea el primer administrador si no hay ninguno
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-03 ¿bloqueo por intentos, expiración y segundo factor? (a) fuera del primer incremento ✅ (b) bloqueo por intentos ya
- 2026-10-31 16:54 · 31. a
- 2026-10-31 16:54 · answer: bloqueo, expiración y segundo factor → (a) fuera del primer incremento
- 2026-10-29 17:45 · Derived from (root): los procesos que generan asientos o borran datos (amortizaciones, IVA, apertura y cierre, dividendos, resetear la demo…) exigen un permiso propio, y ningún atajo de teclado da privilegios sin autenticarse (sources/proconta-antiguo/menu-principal/ANALISIS-FUNCIONAL.md § 1, 6.3).
- 2026-10-31 17:53 · Derived from architecture: la política de permisos por ámbitos, los operadores y grupos que necesita el acceso y el asistente del primer administrador (lógica, sin pantallas).
- 2026-10-31 19:02 · 3. No, de momento podemos trabajar sin contraseñas

## Hand-over
- 2026-10-31 19:10 · Hecho: política común, ámbito de sociedades, operadores (búsqueda, grupos activos, primer administrador, cambio de contraseña) y pruebas. Pendiente: mantenimiento completo de operadores y grupos (seguridad/pantallas); ámbitos de reports (las RestriccionesReportsGrupo antiguas son de denegación y necesitan adaptarse, RF-SEG-07) y de procesos que generan asientos o borran datos (requisito 2026-10-29 17:45, sin tabla todavía); regeneración de contraseña temporal por correo.
