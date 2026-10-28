# Gestor Correo

## Responsabilidad

Gestor Correo es propietario de la definición y validación de cuentas, las credenciales protegidas, los presets de proveedor y el transporte POP3/SMTP. Su servicio de aplicación no depende de WPF y puede componerse posteriormente en un proceso headless.

Mail Dispatcher conserva la interpretación de comandos, la autorización, la clasificación de adjuntos, la idempotencia funcional y la generación de respuestas. Sus adaptadores de integración delegan las conexiones reales en Gestor Correo.

## Compatibilidad con Mail Dispatcher

`ServicioRecepcionPop3` consume directamente `IClientePop3`, `IClienteSmtp`,
`CuentaCorreo` e `IAlmacenCredencialesCorreo`. Gestor Correo es, por tanto, el
único propietario de la configuración y del transporte POP3/SMTP. El Dispatcher
solo mantiene `IConversorMimeDispatcher` para transformar el MIME neutral en su
modelo funcional y `OpcionesRecepcionDispatcher` para sus límites y política de
eliminación. Las credenciales POP3 y SMTP se resuelven por separado cuando la
cuenta no las comparte.

## Secretos

Access guarda exclusivamente referencias con el formato `proconta-correo:v1:<guid>`. El contenido se cifra mediante DPAPI `CurrentUser`, con entropía propia de Gestor Correo, en la carpeta `GestorCorreo\credenciales` situada bajo el directorio global resuelto por ProConta. Las credenciales no deben aparecer en Access, JSON, logs, mensajes de diagnóstico o Git.

## Proveedores y OAuth

Los códigos de proveedor son formato persistente estable. Gmail usa POP3 con TLS implícito en el puerto 995 y SMTP con STARTTLS obligatorio en el 587. El MVP admite contraseña de aplicación. El contrato acepta OAuth 2.0/XOAUTH2, pero la experiencia de autorización interactiva de Gmail queda condicionada a registrar el cliente OAuth de ProConta y completar las verificaciones exigidas por Google.

## Procedencia selectiva

La separación entre dirección de correo y usuario de autenticación, el catálogo estable de proveedores, el plegado conceptual de opciones avanzadas y la presentación separada de diagnósticos se han adaptado selectivamente de QuickMail/EudoraMail, commit de referencia `735197132111c7a895d5228c4450add5b1d6412c`.

No existe referencia de proyecto, enlace, modelo global, `App.xaml`, `MainViewModel`, persistencia SQLite ni base de datos compartida con esa aplicación.

## Persistencia

- `CuentasCorreo`: identidad y configuración neutral POP3/SMTP.
- `CuentasCorreoSociedadesEjercicios`: asignación de una cuenta por sociedad y ejercicio.
- `DiagnosticosCuentasCorreo`: resultados saneados sin detalle técnico sensible.
- `ProcesamientosMailDispatcher` y `AdjuntosMailDispatcher`: continúan siendo propiedad de Mail Dispatcher.

La migración `20260907_001_GESTOR_CORREO` copia las configuraciones históricas del Dispatcher sin modificar la migración ya publicada.

## Correo saliente de Facturación

El envío interactivo y programado de facturas, los avisos de incrementos de
alquiler y las notificaciones de incidencias entregan su contenido mediante
`TransporteCorreoGestorCorreo`. Facturación continúa preparando destinatarios,
plantillas, adjuntos, cabeceras e historial; Gestor Correo resuelve la cuenta
asociada a `IdSociedadEjercicio`, obtiene la credencial efímera desde DPAPI y es
el único componente que crea el cliente SMTP de MailKit.

La migración `20260907_002_CORREO_SALIENTE_GESTOR` conserva servidor, puerto,
TLS, usuario, remitente y asociación de las configuraciones históricas, pero no
lee ni copia `PasswordProtegida`. Las cuentas migradas quedan en borrador hasta
que el usuario introduzca una nueva credencial, que se almacenará fuera de
Access. `ConfiguracionCorreo` permanece como origen de plantillas y preferencias
funcionales de copia, no como configuración del transporte.

## Compositor y seguimiento de lotes

La revisión de capacidades, el contrato visual preliminar y la frontera con la
coordinación saliente de Mail Dispatcher están publicados en
[CONTRATO_COMPOSITOR_Y_LOTES_CORREO.md](CONTRATO_COMPOSITOR_Y_LOTES_CORREO.md).
El documento prepara el desarrollo, pero no aprueba todavía migraciones ni las
reglas funcionales que las maquetas presentan como propuestas.

Los requisitos acordados el 1 de octubre de 2026 establecen un registro común
consultable de correo entrante y saliente, disponible desde menú y reutilizable
como resumen modal filtrado de un proceso. Todo proceso con más de un email
omite el compositor individual. INF-COR posee la interfaz y consulta; INF-DIS,
el procesamiento. FAC-LIS consume ambos contratos sin duplicar el motor.

El compositor WPF invocable desde Terceros, su API neutral y sus límites están
documentados en
[INTEGRACION_COMPOSITOR_WPF.md](INTEGRACION_COMPOSITOR_WPF.md). Devuelve la
decisión al shell y no activa SMTP ni Dispatcher por sí mismo.
