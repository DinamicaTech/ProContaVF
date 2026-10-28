# ProConta · Compositor de correo individual (maqueta)

Abre `proconta-correo.html` con doble clic en Chrome o Edge. Funciona sin servidor. Si hay conexión, carga las fuentes de Google Fonts; si no, usa Segoe UI. Los datos son ficticios, con dominios `example.invalid`.

**El envío es siempre simulado.** No se conecta con ningún servicio de correo. Cada resultado lleva la marca «SIMULADO».

## Barra de demostración
La **barra gris con borde amarillo** no forma parte del producto. Permite cambiar:
- el escenario (A, B, C o D);
- el resultado del envío: aceptado, rechazo confirmado, cuenta no disponible o resultado incierto;
- lo que devuelve «Comprobar» tras un resultado incierto;
- con o sin borradores (propuesta);
- el tamaño de la ventana: amplia (1080 × 740), compacta (720 × 580) o maximizada;
- la sociedad activa, para simular un cambio hecho desde otra ventana de ProConta.

Parámetros de URL: `?escenario=A|B|C|D`, `?ventana=wide|compact|max` y `?borradores=1`.

## Escenarios
- **A. Factura F2026-0123 · Talleres Demo.** Todo está precargado: remitente, destinatario, asunto, texto de la plantilla y PDF de 148 KB.
- **B. Documentos · Servicios Norte.** Para y CC rellenos. El contrato (2,4 MB) viene precargado y el justificante (320 KB) se añadió a mano, así que la ventana ya empieza con cambios.
- **C. Tercero · Talleres Demo.** Solo el destinatario. El asunto y el cuerpo están vacíos y no hay adjuntos.
- **D. Incidencias.** Incluye:
  - cuenta de envío caducada;
  - dirección mal formada (`…@example`), con propuesta de corrección;
  - dominio con errata (`.invlid`): formato correcto pero sin comprobar;
  - destinatario repetido en Para y CC;
  - un adjunto ausente y otro sin permiso de lectura;
  - asunto y nombres largos, 7 destinatarios y 5 adjuntos.

  Por defecto, el envío acaba en **rechazo confirmado**. Cambia el resultado en la barra para ver el incierto.

## Comportamiento
- **Ventana.** Es modal respecto al origen, que aparece detrás atenuado y bloqueado. Se mueve por el título y se redimensiona desde la esquina. Doble clic en el título maximiza. Por debajo de 900 px de ancho pasa a disposición compacta. Al cerrar, el foco vuelve al botón que la abrió. No es TopMost; eso es comportamiento de WPF y aquí solo se representa.
- **Remitente.** Lista cerrada con las cuentas autorizadas de la empresa de origen. Las cuentas no disponibles aparecen deshabilitadas con el motivo. Si cambia la sociedad activa, un aviso lo indica y el remitente no cambia.
- **Destinatarios.** Se buscan contactos por nombre, dirección u organización. Intro, «;» o «,» confirman una dirección, y pegar varias direcciones las separa. Cada destinatario es una ficha que puedes revisar (descripción emergente), editar (clic, Intro o F2) y quitar (× o Supr). Con ← y → te mueves entre ellos.
  - **Mal formada** (roja): bloquea el envío. El aviso indica qué falla y ofrece corregir, quitar o usar un contacto parecido.
  - **Sin comprobar** (borde discontinuo): no bloquea. El formato es correcto, pero no se sabe si el buzón existe. Si el dominio se parece a uno conocido, se propone corregirlo.
  - **Repetida** (ámbar): bloquea el envío. Se resuelve con un clic: «Dejar solo en Para/CC/CCO».
- **CC y CCO.** Se muestran bajo demanda. Solo se pueden ocultar si están vacíos.
- **Validación.** Se valida junto a cada campo. Al pulsar Enviar, el pie resume lo que falta, con enlaces que llevan el foco a cada punto.
- **Vista previa.** Ocupa un panel de la misma ventana (o toda el área en compacto). Muestra los CCO solo como recuento y también previsualiza los adjuntos.
- **Cerrar con cambios.** Aparece una barra en la propia ventana, sin un segundo modal: Seguir editando / Guardar borrador (solo con borradores) / Descartar. Esc equivale a Cancelar.

### Estados del envío
1. **Enviando.** Muestra los pasos. El botón se desactiva al primer clic y la edición se bloquea.
2. **Aceptado por el servicio de correo.** El aviso explica que eso no confirma la entrega al buzón. Cerrar la ventana no cancela el envío, y queda registrado en la actividad del origen.
3. **Rechazo confirmado** (buzón inexistente o cuenta no disponible). No se ha enviado nada y todo lo redactado se conserva. Se indican la causa y la acción, y después se puede volver a enviar sin riesgo de duplicado.
4. **Resultado incierto.** La edición y el envío quedan bloqueados. Solo se ofrece «Comprobar si se envió» o «Cerrar y comprobar más tarde». Si la comprobación lo encuentra, pasa a aceptado. Si no consta, se habilita «Enviar de nuevo». Nunca hay un reintento ciego.

### Teclado
- Ctrl+Intro: Enviar. Esc: Cancelar.
- Ctrl+Mayús+C / B: mostrar CC / CCO. Ctrl+Mayús+P: vista previa.
- Ctrl+B / I / K: negrita / cursiva / enlace. F1: ayuda.
- Tab recorre la ventana sin salir de ella. El foco es visible en naranja.

## Supuestos funcionales (para revisar)
1. **Borradores (propuesta).** Se guardan en la empresa de origen y quedan enlazados al registro de origen (factura, documento o tercero), desde cuya actividad de correo se reabren. Los adjuntos de ProConta se guardan como referencia, no como copia. Al reabrir se comprueba otra vez que existen. Está por decidir si hay autoguardado y cuánto se conservan.
2. **Editor.** Solo negrita, cursiva, listas, enlaces y quitar formato.
   - No hay subrayado, para no confundirlo con un enlace.
   - Lo que se pega llega sin formato externo.
   - Los enlaces deben ser `https://…` o `mailto:`. Si se escribe `www.…`, se completa con `https://`.
   - Los datos que una plantilla no puede rellenar desde el origen se marcan entre corchetes (`[importe]`) y bloquean el envío hasta completarlos.
3. **Plantillas.** Sustituyen el asunto y el cuerpo, pero no los destinatarios ni los adjuntos. Si el texto se ha modificado desde la precarga o desde la última plantilla, se pide confirmación en una barra de la misma ventana.
4. **Límites de adjuntos.** **No hay ningún límite definido.** La maqueta solo muestra el tamaño total y no aplica topes. El límite real depende de la cuenta o del proveedor de correo y está por confirmar. Si se decide fijar uno, debería ser configurable por cuenta y no estar fijado en el código.
5. **Quitar un adjunto** solo lo quita del mensaje. Se puede deshacer y el documento original nunca se modifica. «Abrir» usa la aplicación predeterminada de Windows; «Ver» abre la previsualización en el panel.
6. **Un rechazo de destinatario rechaza el mensaje completo** (nadie lo recibe). Si el servicio real admite rechazos parciales, el estado debería decir a quién se entregó y a quién no.
7. **Seguimiento de devoluciones** (avisos de no entrega que llegan después): propuesta, fuera de esta maqueta.
8. Una **dirección sin comprobar** no bloquea el envío. Una **mal formada** o **repetida** sí.
