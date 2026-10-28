# Mensajes y comunicacion con el usuario

## Objetivo

Los mensajes de ProConta deben ayudar al usuario a comprender una situacion y a
continuar trabajando. No son solamente notificaciones tecnicas: forman parte de
la personalidad y de la relacion cotidiana con la aplicacion.

La voz de ProConta sera:

- Cercana y conversacional.
- En primera persona cuando habla la aplicacion.
- Dirigida al usuario de tu.
- Clara sobre datos, consecuencias y acciones.
- Ligeramente irreverente y con humor cuando el contexto lo permita.
- Respetuosa y profesional en cuestiones fiscales, contables o irreversibles.

## Regla principal

Ningun mensaje debe dejar al usuario en un callejon sin salida.

Se evitaran mensajes como:

```text
No he podido guardar la factura.
Ha ocurrido un error.
Operacion no valida.
Proceso cancelado.
```

Estas frases comunican el fracaso, pero no permiten comprenderlo ni resolverlo.

## Estructura recomendada

Cuando la informacion este disponible, un mensaje debe responder a estas
preguntas:

1. ¿Que estaba intentando hacer ProConta?
2. ¿Sobre que registro o dato?
3. ¿Por que no puede continuar?
4. ¿Que consecuencia tiene?
5. ¿Que puede hacer ahora el usuario?
6. ¿Puede hacerlo ProConta directamente?

No todos los mensajes necesitan seis parrafos. La estructura se adaptara a la
complejidad de la situacion.

## Ejemplo de validacion asistida

Evitar:

```text
No he podido guardar la factura.
```

Utilizar:

```text
A ver, si no me pones el cliente en la factura, no la puedo guardar, que luego
Hacienda se enfada.

¿Quieres que te abra el selector de clientes para buscar uno que te caiga bien?
```

Acciones:

```text
[Buscar cliente] [Volver a la factura]
```

El mensaje identifica el dato ausente, explica por que resulta necesario y
ofrece resolver el problema sin obligar al usuario a buscar manualmente la
pantalla adecuada.

## Ejemplo de archivo bloqueado

Evitar:

```text
No se ha podido generar el PDF.
```

Utilizar:

```text
La factura 2026-0042 esta abierta en otro programa y no puedo reemplazar su PDF.

Cierra el documento y, cuando lo tengas, puedo intentarlo otra vez.
```

Acciones:

```text
[Reintentar] [Abrir carpeta] [Cancelar]
```

## Ejemplo de configuracion incompleta

Evitar:

```text
Error de configuracion SMTP.
```

Utilizar:

```text
La empresa 002 no tiene configurada una cuenta de correo activa, asi que no se a
quien pedirle que envie la factura 2026-0042.

¿Quieres que abra ahora la configuracion de correo de esta empresa?
```

Acciones:

```text
[Configurar correo] [Ahora no]
```

## Ejemplo de error tecnico no diagnosticado

Cuando no se conozca la causa exacta, no se inventara una explicacion.

```text
No he conseguido enviar la factura 2026-0042 a VeriFactu. Los datos de la
factura han pasado la validacion, pero la comunicacion se ha interrumpido antes
de recibir una respuesta de la AEAT.

La factura sigue emitida y pendiente de envio; no hace falta que la vuelvas a
crear. Puedo reintentarlo ahora o dejarla en pendientes.
```

Acciones:

```text
[Reintentar] [Ver pendientes] [Cerrar]
```

El detalle tecnico se guardara en el log y podra mostrarse mediante una opcion
como `Ver detalles` o `Copiar informacion para soporte`.

## Confirmaciones

Una confirmacion debe explicar:

- La accion exacta.
- Los registros afectados.
- Las consecuencias relevantes.
- Si puede deshacerse.
- La accion recomendada cuando exista.

Evitar botones ambiguos:

```text
[Si] [No] [Cancelar]
```

Preferir:

```text
[Generar rectificativa] [Volver a la factura]
[Eliminar borrador] [Conservarlo]
[Reintentar envio] [Dejar pendiente]
```

Las acciones destructivas o fiscales no se seleccionaran como boton
predeterminado.

## Titulos

Los titulos seran breves, funcionales y descriptivos:

```text
Cliente pendiente
Factura ya emitida
PDF abierto
Envio pendiente de VeriFactu
Cambio de cuenta contable
Eliminacion de borrador
```

El cuerpo puede ser mas conversacional. Esta combinacion facilita reconocer
rapidamente la situacion sin perder la personalidad de ProConta.

## Humor

El humor se utilizara para acompañar el mensaje, no para sustituir informacion.

Es adecuado cuando:

- Se solicita completar un dato.
- La operacion es reversible.
- El usuario puede resolver facilmente la situacion.
- Se confirma un resultado satisfactorio.

Se moderara cuando:

- Exista riesgo de perdida de datos.
- Se modifique una factura emitida mediante rectificacion.
- Haya consecuencias fiscales o contables.
- Se produzca un error que requiera soporte.
- El usuario pueda interpretar que la aplicacion resta importancia al problema.

## Mensajes de exito

Seran breves y no interrumpiran innecesariamente el trabajo:

```text
Apa, factura emitida y enviada a VeriFactu.
Todo cuadrado. El lote contable ya esta generado.
Venga, PDF regenerado y listo para enviar.
```

Cuando no sea necesaria una decision, se preferiran notificaciones no modales o
actualizaciones visuales de estado frente a un dialogo que obligue a pulsar
`Aceptar`.

## Validacion en formularios

No todas las validaciones necesitan un dialogo. Para errores sencillos se
preferira:

- Marcar el control afectado.
- Mostrar el motivo junto al campo.
- Situar el foco en el dato que debe corregirse.
- Ofrecer selectores o valores validos cuando sea posible.

El dialogo se reserva para situaciones que requieran una decision, afecten a
varios campos o necesiten explicar una consecuencia.

## Informacion tecnica y soporte

Nunca se mostraran como unico mensaje:

- `Exception.Message`.
- Trazas de pila.
- SQL.
- Nombres internos de clases.
- Rutas o codigos que no ayuden al usuario.

Cada incidencia tecnica tendra, cuando sea posible, un identificador de
correlacion. El dialogo podra ofrecer:

```text
[Copiar informacion para soporte]
[Ver detalles]
[Abrir registro]
```

El texto principal seguira siendo comprensible sin abrir esos detalles.

## Servicio central de mensajes

Los formularios no llamaran directamente a `MessageBox.Show`. La presentacion
se centralizara mediante una abstraccion similar a:

```vbnet
Public Interface IServicioMensajes
    Sub MostrarInformacion(mensaje As MensajeUsuario)
    Sub MostrarAdvertencia(mensaje As MensajeUsuario)
    Sub MostrarError(mensaje As MensajeUsuario)
    Function SolicitarDecision(mensaje As MensajeDecision) As DecisionUsuario
End Interface
```

Los mensajes con acciones podran asociar una decision a cada boton. La capa de
aplicacion devolvera informacion estructurada; la interfaz decidira como
presentarla y que pantalla abrir.

## Lista de comprobacion

Antes de incorporar un mensaje, comprobar:

- [ ] ¿Identifica que operacion y que registro estan afectados?
- [ ] ¿Explica la causa conocida sin inventarla?
- [ ] ¿Indica que puede hacer el usuario?
- [ ] ¿ProConta puede ofrecer esa accion directamente?
- [ ] ¿Los botones describen acciones reales?
- [ ] ¿El detalle tecnico se registra por separado?
- [ ] ¿El humor ayuda sin ocultar informacion?
- [ ] ¿El mensaje necesita ser modal?
