---
title: Operadores y permisos
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 18:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-14 20:06 · Contraseña opcional sin complejidad | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GAnsNpR97upaGB6RX2PKgeg
  - 2026-11-18 15:10 · Completar los modelos tributarios y su pantalla | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWW9gQjRoqJLGy11fuziWXQ
  - 2026-11-18 17:30 · Diseñar el gestor documental | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GWo8SybrmKimtCP8FKpt7zw
---
## Summary
Operadores, grupos (N:M) y permisos por ámbito: ámbito sin configurar permite a todo operador válido; ámbito configurado aplica sus asignaciones. Los servicios comprueban la autorización, no solo la pantalla. Código (src/ProConta.Seguridad/Permisos): `PoliticaAmbito` (regla común), `AutorizadorSociedades` (ámbito AccesoSociedadesGrupo), `ServicioOperadores` (buscar operador, grupos activos, primer administrador, contraseña) y `HashContrasena` (PBKDF2-HMAC-SHA256). La contraseña de los operadores es opcional y sin requisitos de longitud ni complejidad. Acciones con permiso propio: FACTURAS.*, MANTENIMIENTO.EDITAR, TRIMESTRE.CAMBIAR, PROCESOS.LIQUIDACION_IVA, PROCESOS.DIVIDENDOS, PROCESOS.IMPUESTO_SOCIEDADES, PROCESOS.CIERRE_EJERCICIO y PROCESOS.FACTURAS_PENDIENTES. Cada operador es el rol «Operador ProConta» de un tercero persona física (nombre y correo del tercero); darlo, quitarlo y cambiar sus grupos lo pueden solo los administradores (acción SEGURIDAD.OPERADORES).
**Aceptación:** grupo sin filas no hereda acceso en un ámbito configurado; tabla ausente no equivale a ámbito sin configurar.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Si no hay ningún operador, un asistente crea el primer administrador; la maestra no trae operadores por defecto (P-03).
- 2026-10-31 16:54 · Bloqueo por intentos, expiración de contraseña y segundo factor quedan fuera del primer incremento (P-03).
- 2026-10-31 18:55 · `PoliticaAmbito.Permite` aplica la regla común a cualquier ámbito: sin asignaciones en todo el ámbito, permiso para todos; con alguna, solo los grupos activos del operador que tienen la asignación. Si la tabla del ámbito falta o no se puede leer, la excepción de datos se propaga (nunca se trata como ámbito vacío). Primer ámbito materializado: acceso a sociedades (AccesoSociedadesGrupo).
- 2026-10-31 18:55 · [replaced by 2026-11-14 20:06] Primer administrador: si no hay ningún operador, `CrearPrimerAdministradorAsync` crea uno con contraseña (mínimo 8 caracteres) y lo pone en el grupo ADMINISTRADORES (lo crea si falta), en una sola transacción y con auditoría; si ya hay operadores, se niega.
- 2026-10-31 18:55 · Contraseñas: PBKDF2-HMAC-SHA256 con salt de 16 bytes, hash de 32 y 210.000 iteraciones, comparación en tiempo constante; mismo formato que el programa anterior, así que las credenciales guardadas siguen valiendo.
- 2026-10-31 18:55 · [replaced by seguridad/acceso 2026-11-24 11:20] Un nombre de operador que aparece dos veces (sin distinguir mayúsculas) no identifica a nadie: no se puede entrar con él hasta que se corrija.
- 2026-10-31 19:02 · [replaced by 2026-11-14 20:06] No se obliga a los operadores a tener contraseña; el primer administrador sí la define (mínimo 8 caracteres) y cualquier operador puede tenerla.
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-01 18:15 · Migración `20261101_1633_PERMISOS_ACCIONES`: tabla PermisosAccionesGrupo (CodigoAccion, IdGrupoOperadores, única por los dos). `AutorizadorAccionesAccess` aplica la regla común: una acción sin filas la puede cualquier operador; con alguna, solo los grupos activos del operador que la tienen. Acciones de hoy: FACTURAS.EDITAR, FACTURAS.VALIDAR, FACTURAS.RECTIFICAR y MANTENIMIENTO.EDITAR. Aún no hay pantalla para asignarlas.
- 2026-11-14 20:06 · Criterio de contraseñas (de momento, hasta afinarlo en seguridad/permisos/politica-contrasenas): la contraseña de un operador no es obligatoria y no tiene requisitos de longitud ni de complejidad («a» es válida). Vacía significa sin contraseña: el operador entra con la contraseña vacía. Primer administrador: si no hay ningún operador, `CrearPrimerAdministradorAsync` lo crea con contraseña o sin ella y lo pone en el grupo ADMINISTRADORES (lo crea si falta), en una sola transacción y con auditoría; si ya hay operadores, se niega. `EstablecerContrasenaAsync` con la contraseña vacía le quita la contraseña al operador (auditoría CONTRASENA_QUITADA). Sigue valiendo que un operador con contraseña nunca entra con la contraseña vacía (seguridad/acceso).
- 2026-11-16 10:20 · Acción nueva «PROCESOS.LIQUIDACION_IVA» (liquidar el IVA de un trimestre y deshacerlo, contabilidad/procesos/liquidacion-iva): mientras no tenga grupos asignados, la tiene quien puede cambiar el trimestre activo (TRIMESTRE.CAMBIAR), porque liquidar cierra el trimestre. La comprueba el servicio, no solo la pantalla.
- 2026-11-16 18:40 · Acción nueva «PROCESOS.DIVIDENDOS» (repartir dividendos, corregir y deshacer un reparto, contabilidad/procesos/aplicacion-resultado): mientras no tenga grupos asignados, la tiene quien puede liquidar el IVA (PROCESOS.LIQUIDACION_IVA). La comprueba `ServicioDividendos`, no solo la pantalla. El pago de dividendos de Líneas N43 no lleva permiso propio: solo asigna cuentas a líneas, como explotar una remesa.
- 2026-11-16 19:25 · Validado por el propietario lo derivado de contabilidad/procesos/aplicacion-resultado (PR #95).
- 2026-11-17 17:05 · Cumplida la línea «Derived from contabilidad/procesos/facturas-pendientes-recibir» del 2026-11-17 16:40: acción PROCESOS.FACTURAS_PENDIENTES (`Acciones.FacturasPendientes`), «Contabilizar las facturas pendientes de recibir»; la comprueba el servicio al confirmar y al deshacer. Mientras no tenga grupos, la tienen quienes pueden liquidar el IVA (`Acciones.Respaldo`), como la del cierre del ejercicio.

- 2026-11-17 18:12 · Acción PROCESOS.AMORTIZACION («Amortizar los activos»: confirmar y deshacer la amortización de un periodo); mientras no tenga grupos, la tienen quienes pueden liquidar el IVA (contabilidad/procesos/amortizacion-de-activos 2026-11-17 18:12).
- 2026-11-17 19:15 · Acción PROCESOS.CENTIMOS_RESIDUALES («Saldar los céntimos residuales»: confirmar y deshacer sus asientos del 31/12); mientras no tenga grupos, la tienen quienes pueden liquidar el IVA (contabilidad/procesos/centimos-residuales 2026-11-17 19:15).
- 2026-11-18 15:10 · Acciones nuevas: MODELOS.VER (ver Contabilidad › Modelos tributarios, borradores, ficheros, justificantes y la consulta a la AEAT; sin grupos, cualquier operador) y MODELOS.PRESENTAR (presentar modelos y complementarias a la AEAT; sin grupos, quienes pueden liquidar el IVA).
- 2026-11-24 11:20 · Operadores como rol de terceros (seguridad/pantallas 2026-11-24 11:20): un operador es el rol «Operador ProConta» de un tercero persona física (OperadoresSeguridad.IdTercero, esquema 82); `ServicioOperadores` calcula el nombre de acceso, lista los grupos, da y quita el rol (`GuardarRolAsync`, también solo para comprobar) y crea el primer administrador con su tercero. Acción nueva SEGURIDAD.OPERADORES (`Acciones.Operadores`): dar y quitar el rol, sus grupos y la contraseña de otro; mientras no tenga grupos asignados la tienen solo los miembros activos de ADMINISTRADORES (no cualquier operador), y la comprueban el servicio y el autorizador. Nadie se quita a sí mismo el rol ni el grupo ADMINISTRADORES y no se puede dejar sin ningún administrador activo. Primer administrador: crea también su tercero (nombre y apellidos).

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
- 2026-11-01 17:20 · Derived from facturacion/factura-venta: las acciones de validar, rectificar y eliminar borradores se autorizan en el servicio con la regla común.
- 2026-11-14 20:04 · La contraseña no es obligatoria
- 2026-11-14 20:04 · Tampoco es preciso complejidad de contraseña, 'a' sería una contraseña válida
- 2026-11-16 10:20 · Derived from contabilidad/procesos/liquidacion-iva: permiso propio para liquidar el IVA y deshacer la liquidación.
- 2026-11-16 17:55 · Derived from contabilidad/procesos/aplicacion-resultado: nueva acción PROCESOS.DIVIDENDOS, «Repartir dividendos».
- 2026-11-17 11:35 · Derived from contabilidad/procesos/impuesto-sociedades: nueva acción PROCESOS.IMPUESTO_SOCIEDADES, «Contabilizar el Impuesto sobre Sociedades» (confirmar y deshacer); mientras no tenga grupos, la tienen quienes pueden liquidar el IVA.
- 2026-11-17 16:40 · Derived from contabilidad/procesos/facturas-pendientes-recibir: nueva acción PROCESOS.FACTURAS_PENDIENTES, «Contabilizar las facturas pendientes de recibir» (confirmar y deshacer); mientras no tenga grupos, la tienen quienes pueden liquidar el IVA.
- 2026-11-17 18:50 · Derived from activos: una sola acción nueva para ver y modificar los activos (pantalla Datos maestros › Activos).
- 2026-11-17 19:05 · Derived from contabilidad/procesos/centimos-residuales: nueva acción PROCESOS.CENTIMOS_RESIDUALES, «Saldar los céntimos residuales», que mientras no tenga grupos sigue a la de liquidar el IVA.
- 2026-11-18 15:10 · Derived from contabilidad/modelos-tributarios: un permiso para ver la pantalla de modelos tributarios y otro propio para presentar.
- 2026-11-18 16:28 · Derived from documentos: nueva acción DOCUMENTOS.EDITAR, «Documentos» (añadir, archivar, vincular, versionar y eliminar documentos); sin grupos asignados la tiene cualquier operador, sin respaldo.
- 2026-11-24 11:20 · Derived from seguridad/pantallas: acción SEGURIDAD.OPERADORES (sin grupos, solo administradores) y reglas del rol de operador (no quitarse a sí mismo, no quedarse sin administradores).

## Hand-over
- 2026-10-31 19:10 · Hecho: política común, ámbito de sociedades, operadores (búsqueda, grupos activos, primer administrador, cambio de contraseña) y pruebas. Pendiente: mantenimiento completo de operadores y grupos (seguridad/pantallas); ámbitos de reports (las RestriccionesReportsGrupo antiguas son de denegación y necesitan adaptarse, RF-SEG-07) y de procesos que generan asientos o borran datos (requisito 2026-10-29 17:45, sin tabla todavía); regeneración de contraseña temporal por correo.
- 2026-11-07 13:23 · Derived from compras/pantallas: nueva acción TRIMESTRE.CAMBIAR, «Cambiar el trimestre activo». Mientras no tenga ningún grupo asignado en PermisosAccionesGrupo, la tienen quienes pueden FACTURAS.EDITAR (respaldo en el autorizador, sin copiar datos); en cuanto se le asigna un grupo, va por su cuenta. El campo del trimestre activo en Parámetros › Ejercicios pasa a solo lectura.
- 2026-11-14 20:06 · Hecho: criterio de contraseña opcional sin longitud ni complejidad en el servicio de operadores y en el asistente del primer administrador, con pruebas. Pendiente: la política definitiva, en el borrador seguridad/permisos/politica-contrasenas.
- 2026-11-17 17:59 · Derived from contabilidad/procesos/amortizacion-de-activos: nueva acción PROCESOS.AMORTIZACION, «Amortizar los activos» (confirmar y deshacer la amortización), que mientras no tenga grupos sigue a la de liquidar el IVA.
