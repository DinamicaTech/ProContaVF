# Recepción de facturas de proveedor por correo

## Objetivo

ProConta recibe inicialmente por POP3 mensajes enviados por un operador interno con una
factura de proveedor en PDF. El proceso deberá:

1. conservar localmente el mensaje y el PDF;
2. crear o localizar el proveedor;
3. registrar la factura recibida;
4. contabilizarla;
5. enviar al operador una confirmación con el asiento creado;
6. eliminar del servidor el mensaje ya procesado.

La recepción utiliza MailKit/MimeKit a través de Gestor Correo, pero no comparte conexiones ni estado
con el transporte SMTP saliente.

## Identidad e idempotencia

La identidad técnica no es `Message-ID`, porque puede faltar o repetirse. En el transporte POP3 inicial la clave única es:

```text
IdCuentaCorreo + UIDL
```

Antes de ejecutar acciones funcionales se insertará una recepción con esa clave.
Un segundo sondeo encontrará el registro existente y reanudará el paso pendiente
en vez de crear otra factura o asiento.

El mensaje MIME original se guardará como `.eml` en el repositorio documental y
se calculará su hash. Así podrá eliminarse del servidor sin perder evidencia ni
depender del tamaño limitado de una base Access.

## Estados mínimos

```text
Detectado
  -> Descargado
  -> Validado
  -> FacturaRegistrada
  -> Contabilizado
  -> ConfirmacionPendiente
  -> Confirmado
  -> EliminacionPendiente
  -> EliminadoServidor
```

Un error funcional o técnico conserva el último estado correcto y su detalle.
El operador podrá corregir los datos o reintentar desde ese punto.

## Regla de eliminación

El mensaje solo podrá marcarse para eliminación mediante POP3 y confirmar esa
eliminación al cerrar correctamente la sesión cuando se cumplan conjuntamente
estas condiciones:

- el `.eml` y el PDF están almacenados localmente y tienen hash;
- la factura de proveedor está registrada;
- el asiento contable está confirmado;
- el correo de confirmación ha sido aceptado por SMTP;
- la recepción está marcada como `EliminacionPendiente` en Access.

La eliminación del servidor es el último paso y nunca forma parte de la
transacción que crea la factura y el asiento. Si ProConta se interrumpe después
de contabilizar o confirmar, el siguiente sondeo reconoce la misma identidad y
completa únicamente los pasos pendientes.

Si el servidor confirma la eliminación pero la aplicación se interrumpe antes
de registrarla, la ausencia posterior del UIDL se resolverá como eliminado siempre
que todos los pasos anteriores consten como correctos.

## Mensajes que no deben eliminarse

Permanecerán en el servidor y quedarán señalados como incidencia los mensajes:

- sin PDF;
- con más adjuntos de los admitidos por el proceso;
- cuyo PDF no sea válido o exceda el tamaño configurado;
- enviados por un remitente no autorizado;
- cuyo proveedor o contabilización requieran intervención;
- cuyo correo de confirmación no se haya podido entregar.

## Primera implementación prevista

- Una cuenta o buzón dedicado por sociedad y ejercicio.
- Sondeo POP3 periódico, sin conexión permanente.
- Enumeración y descarga por UIDL.
- Lista configurable de remitentes internos autorizados.
- Un PDF principal por mensaje en la primera versión.
- Reintentos explícitos e idempotentes por estado.
- IMAP, sincronización de carpetas, lectura general de correo y funciones propias de
  un cliente como EudoraMail quedan fuera del alcance.
