---
title: Envío de correo
status: draft
depends_on: [datos/esquema, datos/persistencia, plataforma/configuracion, documentos, organizacion/sociedades-ejercicios]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-10 19:40 · Desarrollar envío y recepción de correo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GT4mx65rhF9fy8aRE77SaGH
---
## Summary
Cuentas de correo por sociedad y ejercicio, transporte SMTP con evidencia por destinatario y cola durable con intentos, reintentos, lotes y estados que distinguen aceptación, aceptación en parte, fallo e incertidumbre. La procesa la aplicación mientras está abierta, y los adjuntos enviados se conservan como copia inmutable.
**Aceptación:** la de spec § 22.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Reintentos automáticos solo ante fallo confirmado; ante incertidumbre nunca se reintenta ni se reenvía automáticamente (resuelve P-11).
- 2026-11-10 19:40 · Cuentas (Q6): una cuenta de correo por sociedad y ejercicio (CuentasCorreo, única por IdSociedadEjercicio), con la dirección que lleva implícito el ejercicio (por ejemplo dinamica2026@proconta.es); la misma cuenta envía (SMTP) y recibe (POP3). Se mantiene en Configuración › Parámetros y tablas, con «Probar envío» y «Probar recepción». La conexión exige cifrado: STARTTLS o TLS implícito; nunca en claro.
- 2026-11-10 19:40 · Contraseñas (Q4): en la propia BD, cifradas con la clave del programa (el mismo cifrado que las claves de AWS de compras), para que funcionen aunque cambie el equipo; nunca vuelven a la pantalla, solo se sustituyen. El propietario acepta que quien tenga la BD y el programa pueda descifrarlas.
- 2026-11-10 19:40 · Aceptación (Q7): un mensaje está «Aceptado» cuando nuestro servidor SMTP lo acepta; la entrega en el buzón del destinatario y los rebotes no se siguen en esta versión. Resultado por destinatario: aceptado o rechazado por el servidor; si se acepta alguno y se rechaza otro, «Aceptado en parte», nunca fallo total. «Incierto» solo cuando la conexión se corta después de enviar el contenido y antes de la respuesta del servidor: no se reintenta solo; el operador lo da por enviado o lo vuelve a enviar aceptando el riesgo de duplicado, con su nombre y motivo.
- 2026-11-10 19:40 · Reintentos (completa la decisión 2026-10-31 16:54, que sigue en vigor): solo ante fallo confirmado y transitorio (respuesta 4xx del servidor o fallo de conexión antes de enviar el contenido), hasta cuatro reintentos a los 5 minutos, 30 minutos, 2 horas y 16 horas, conservando el Message-ID; un rechazo definitivo (5xx) no se reintenta solo. El operador puede reintentar un fallido a mano en cualquier momento.
- 2026-11-10 19:40 · Reenvío: crea un mensaje nuevo, enlazado al original, con su contenido, destinatarios y adjuntos tal como se enviaron, y pide confirmación expresa.
- 2026-11-10 19:40 · Cola durable: cada mensaje se guarda con su Message-ID, destinatarios, contenido y adjuntos antes de conectar con el servidor; la aplicación procesa la cola mientras está abierta (ver correo 2026-11-10 19:40) y, con varias sesiones, una sola la procesa gracias a un turno con caducidad en la BD. Los procesos de más de un correo crean un lote sin abrir ninguna ventana por mensaje y muestran un resumen al final.
- 2026-11-10 19:40 · Adjuntos (Q5): cada adjunto enviado se guarda como copia inmutable con su huella SHA-256 en la carpeta de documentos de la empresa (`<RutaDocumentos>\<ejercicio>\Correo\<mensaje>\`), y la BD guarda su ruta relativa; pasará al motor documental cuando exista. Nunca se modifica el documento original.
- 2026-11-10 19:40 · Desvío de pruebas: parámetro general «Desviar todo el correo a» (Configuracion.CorreoDesviarA); con valor, todo mensaje sale solo a esa dirección, con los destinatarios reales anotados en el propio mensaje y en el registro, y las pantallas lo avisan. En la BD de pruebas se pone a la dirección del propietario.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 22 RF-COR-04..07: cuentas y secretos, coordinador saliente, reintento/reenvío, no duplicar transporte
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/GestorCorreo/docs/: contratos anteriores
- 2026-10-28 12:28 · draft: Implementar el envío de correo y los lotes con seguimiento.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-11 ¿política de reintentos automáticos? (a) solo ante fallo confirmado, nunca ante incertidumbre ✅ (b) ninguno automático
- 2026-10-31 16:54 · 6. a
- 2026-10-31 16:54 · answer: P-11 reintentos automáticos → (a) solo ante fallo confirmado, nunca ante incertidumbre
- 2026-11-10 19:26 · answer: Q4 contraseñas → B, en la propia BD para que siga funcionando aunque se cambie de ordenador
- 2026-11-10 19:26 · answer: Q5 adjuntos enviados → A. De hecho, hay que poder consultar los emails enviados y poder acceder a los adjuntos si los hubiesen
- 2026-11-10 19:26 · answer: Q6 cuentas → B, no siempre la fecha de la factura indica el ejercicio y, además, para solicitar, por ejemplo, un balance, hay que poder indicar de alguna forma el ejercicio, por lo que, una de dos, o la cuenta lo lleva implícito (Dinamica2026@procontavf.com) o se explicita en el subject o boy, pero corremos el riesgo de que no se indique el ejercicio y estemos en una zona ambigua a la hora de procesar el correo.
- 2026-11-10 19:26 · answer: Q7 incierto → con el protocolo SMTP sabemos si el correo a sido aceptado por el servidor de correo destinatario, con eso ya tendríamos suficiente, no ?
- 2026-11-10 19:26 · answer: Q10 pruebas → dinamica2026@proconta.es, pop3 server: email.dinamica.tech port 110, smtp server: email.dinamica.tech port 587. Todos los correos redirigidos a <correo del propietario>
- 2026-11-10 19:26 · Reintentos: ok, pero añade un cuarto intento a las 16 horas
- 2026-11-10 19:26 · Reenvios: Ok
