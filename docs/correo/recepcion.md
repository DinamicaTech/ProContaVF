---
title: Recepción de correo
status: draft
depends_on: [correo/envio, compras/reconocimiento, norma43, nominas]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-11-10 19:40 · Desarrollar envío y recepción de correo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GT4mx65rhF9fy8aRE77SaGH
---
## Summary
Mail Dispatcher: mira el buzón POP3 de la cuenta de cada ejercicio mientras la aplicación está abierta, guarda cada mensaje (.eml y adjuntos con su huella), atiende solo a los remitentes autorizados, clasifica los adjuntos (facturas de compra, Norma 43, nóminas) y los pasa a sus motores, responde al remitente con lo hecho y borra del servidor lo terminado, con hitos persistidos para no duplicar. Código: `ServicioRecepcionCorreo`, `ClientePop3MailKit` y `LectorCorreo` (src/ProConta.Correo/Recepcion.cs), `ProcesadorFacturasCompraCorreo` (src/ProConta.Compras/Facturas/FacturasRecibidasCorreo.cs), pantalla `CorreoRecibidoVM` y migración `20261111_1100_CORREO_RECEPCION`.
**Aceptación:** la de spec § 23.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 16:54 · Protocolo de recepción: POP3 con UIDL; la idempotencia se basa en cuenta + UIDL (resuelve RF-DIS-02).
- 2026-11-10 19:40 · Alcance (Q1): se construye todo el circuito. Las facturas de compra se procesan ya; los adjuntos de Norma 43 y nóminas y los comandos de informe quedan guardados con la incidencia «motor pendiente» y se conectan cuando existan norma43, nominas e informes.
- 2026-11-10 19:40 · Contexto (Q6): el buzón es la cuenta de correo de una sociedad y un ejercicio (correo/envio 2026-11-10 19:40), así que el correo recibido pertenece a ese ejercicio sin deducirlo de la fecha ni del asunto.
- 2026-11-10 19:40 · Factura recibida (Q2): se guarda en la carpeta del proveedor para el escaneo por checksum; compras/reconocimiento la procesa al momento y, si todos los campos alcanzan al menos un 95 % de confianza, la registra y la contabiliza. Siempre se responde al remitente: si se ha procesado, los campos reconocidos y, si se ha generado, el asiento.
- 2026-11-10 19:40 · Borrado en el servidor: el mensaje se borra del buzón POP3 una vez descargado y procesado, con todos sus pasos completados (y la respuesta aceptada); los que quedan con incidencia no se borran.
- 2026-11-10 19:40 · Solo se atiende a los remitentes autorizados del buzón, con permisos separados para importar documentos y para pedir informes. La recepción la procesa la aplicación mientras está abierta (correo 2026-11-10 19:40).
- 2026-11-11 11:00 · Identidad e hitos: cada mensaje es cuenta + UIDL (MensajesRecibidosCorreo, único); se descarga y se guarda en `<carpeta de documentos>\<ejercicio>\Correo\Recibido\<aaaamm>\<id>\` antes de procesarlo; cada adjunto guarda su estado y lo que generó (factura de compra, asiento); la respuesta (una por mensaje) y el borrado del servidor son hitos aparte. Un sondeo repetido o un corte a medias continúa sin duplicar factura, asiento ni respuesta.
- 2026-11-11 11:00 · Remitentes: solo se procesa el correo de los remitentes autorizados y activos del ejercicio (Parámetros y tablas › Remitentes autorizados), con permisos separados para enviar documentos a importar y para pedir informes; sin remitentes, no se procesa nada. Lo de un remitente no autorizado se guarda sin procesar ni responder y se conserva en el servidor; tras autorizarlo, «Volver a procesar». Las respuestas automáticas, los avisos del servidor de correo y el correo de la propia cuenta se ignoran.
- 2026-11-11 11:00 · Adjuntos: PDF → factura de compra (compras/reconocimiento); fichero Norma 43 (registros de 80 caracteres que empiezan por 11) y Excel de nóminas → «en espera» hasta que existan norma43 y nominas; sin documentos, el asunto se toma como comando de informe y queda «en espera» hasta informes/reporting. Lo «en espera» o con incidencia se conserva en el servidor; un mensaje se borra solo cuando está procesado entero y su respuesta ha sido aceptada.
- 2026-11-11 11:00 · Hecho (entrega 2): esquema v34, recepción POP3 cifrada, procesado, respuesta y borrado, pantalla «Correo recibido» (menú Correo) con «Mirar el buzón ahora», «Volver a procesar» y el mensaje original y sus adjuntos, y el mantenimiento de remitentes. Pruebas: 9 contra Access (factura procesada, respondida y borrada; sondeos repetidos sin duplicar; no autorizado; Norma 43 y nóminas en espera; fallo y nuevo proceso; respuesta incierta que impide borrar; respuestas automáticas; corte antes de responder; comando en espera). Pendiente de validar con la cuenta real de pruebas.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 23 RF-DIS-01..07: dispatcher, protocolo, flujo de factura recibida, hitos, comandos
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/MailDispatcher/: documentos y código anteriores
- 2026-10-28 12:28 · draft: Implementar la recepción y el procesamiento de correo.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: RF-DIS-02 ¿protocolo? (a) POP3 con UIDL, como la implementación posterior ✅ (b) IMAP (c) ambos
- 2026-10-31 16:54 · 7. a
- 2026-10-31 16:54 · answer: RF-DIS-02 protocolo → (a) POP3 con UIDL, como la implementación posterior
- 2026-10-29 17:45 · Derived from nominas: aceptar el fichero Excel de nóminas enviado al correo de ProContaVF y pasarlo a nominas para que lo parsee, importe y genere los asientos.
- 2026-10-31 16:54 · Derived from compras/reconocimiento: guardar cada factura de compra recibida por correo en `<raíz>\<ejercicio>\Proveedores\<carpeta del proveedor>`, creando la carpeta del proveedor (nombre comercial) si no existe, para que la incorpore el escaneo por checksum.
- 2026-11-10 19:26 · answer: Q2 factura recibida → A, pero la idea es que una vez copiada la factura a la raiz, el importador de facturas de compre la procese y si consigue una fiabilidad del 95% en todos los campos de importación, la importe y contabilice. En cualquier caso, se responderá al remitente indicando si se ha procesado la factura, los campos reconocidos y, en caso de haberse generado, el asiento contable
- 2026-11-10 19:26 · Ok a remitentes autorizados
- 2026-11-10 19:26 · En la cuenta POP3, borrado del servidor una vez descargado y procesado el correo.
