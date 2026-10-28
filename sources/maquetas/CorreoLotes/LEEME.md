# Seguimiento de lotes de correo · maqueta

Fichero: `seguimiento-lotes-correo.html` (autocontenido, sin backend ni correo real). La franja rayada superior son controles de la maqueta, no forman parte de la pantalla.

## Cómo probarla
- **Escenario:** Lote A (120, en curso), Lote B (12, finalizado), Lote B con avisos de entrega del proveedor, Lote C (en preparación, vacío).
- **Motor simulado:** arranca detenido, para que los contadores coincidan con el enunciado (72 / 30 / 3 / 8 / 2 / 5). «Un paso» avanza un ciclo: termina un mensaje en proceso y toma el siguiente pendiente. Hay tres velocidades (5 s, 2,5 s y 1,2 s).
- **Capacidades del motor:** «Comprobar inciertos», «Pausa» e «Incidencias corregidas» simulan contratos que aún no existen.
- **Ancho:** 1024, 1280, 1440 o completo. Por debajo de 1250 px, el detalle se superpone a la tabla.
- **Restablecer escenarios:** recarga los tres lotes con sus datos iniciales.

## Teclado
↑ ↓ RePág AvPág Inicio Fin mueven la fila activa. Mayús amplía la selección. Espacio marca o desmarca. Intro abre el detalle; Esc lo cierra o quita la selección. Ctrl+A selecciona los mensajes **visibles** y avisa de ello. Ctrl+F lleva a la búsqueda. Con el ratón: clic activa la fila, Ctrl+clic o la casilla marcan, Mayús+clic marca un rango y doble clic abre el detalle.

## Decisiones de diseño
1. **La tabla es el centro.** Cabecera de una línea y resumen en fichas compactas. Cada ficha es a la vez contador y filtro, y se pueden combinar. No hay gráficos decorativos.
2. **Estados con icono y texto.** «Aceptado por el servicio» usa un avión de papel azul, no un visto verde. El verde solo aparece en «Entregado», y únicamente si el proveedor envía avisos de entrega (escenario B con avisos). «Rebotado» es un aviso posterior a la aceptación, no un estado nuevo del envío.
3. **La barra de progreso dice qué cuenta:** mensajes con resultado registrado frente al total. Muestra por separado aceptados, fallidos, inciertos (rayado), cancelados y en proceso. Al terminar con incidencias, el lote dice «Finalizado con incidencias» y el texto se muestra en ocre con ⚠, nunca como un 100 % verde.
4. **Total frente a filtrados.** Al filtrar se ve «Mostrando N de 120 mensajes del lote». La ficha «Total» no cambia.
5. **Actualizaciones sin mover filas.** Las filas se actualizan en su sitio, sin perder selección ni desplazamiento. Si una fila deja de cumplir el filtro, se atenúa con la marca «ya no coincide». Si el orden depende de un dato que cambia, aparece el aviso «Actualizar vista». Los reenvíos se añaden al final.
6. **Tres alcances, separados a la vista:**
   - **Este mensaje:** en el detalle.
   - **Selección:** en la barra de la tabla, con el número de mensajes afectados en cada botón.
   - **Todo el lote:** en el menú «Acciones del lote», que incluye los mensajes no visibles.

   La casilla de la cabecera marca solo los visibles y lo dice, con la opción de seleccionar los del lote completo. Si la selección incluye mensajes ocultos por el filtro, se indica.
7. **Confirmación de acciones colectivas.** Indica el alcance, los mensajes afectados (con su número) y los excluidos, agrupados por motivo. Al reintentar, los que volverán a fallar previsiblemente (dirección rechazada; cuenta o adjunto sin corregir) quedan fuera por defecto, con la opción «Incluirlos igualmente».
8. **Reintentar y Reenviar son acciones distintas.**
   - **Reintentar** solo actúa sobre fallidos. Añade un intento al historial y conserva el error anterior.
   - **Reenviar** solo actúa sobre aceptados. Crea un mensaje nuevo, vinculado al original («Reenvío de nº X» / «reenviado como nº Y»), y exige marcar «Entiendo que se enviará de nuevo».
   - Nunca se reintenta automáticamente un aceptado.
9. **Incierto: comprobar antes de repetir.**
   - Si el motor puede comprobarlo, el mensaje pasa a Aceptado, o a Fallido («No consta») y entonces puede reintentarse.
   - Si no puede, se explica por qué y se ofrece registrar el resultado de una verificación manual. También se puede repetir el envío, aceptando expresamente el riesgo de que llegue duplicado.
10. **Cancelar.** Solo actúa sobre los pendientes. Indica cuántos están en proceso y no pueden detenerse. Si uno pasa a proceso durante la operación, se avisa de que no se ha cancelado.
11. **Sin dobles acciones.** Mientras hay una operación en curso, las acciones se desactivan y las filas afectadas muestran «⏳». El botón del diálogo se desactiva al pulsarlo.
12. **El detalle es de consulta.**
    - Contenido marcado como «solo lectura», con la nota «tal como se generó el…».
    - Para, CC y CCO completos, Message-Id y adjuntos con vista previa. Un adjunto no disponible se marca como tal.
    - Enlace para abrir el origen o la cuenta en Gestor Correo.
    - Historial que combina eventos, intentos y avisos del proveedor.
    - Explicación del error en lenguaje claro («Qué hacer») y detalle técnico desplegable y copiable.
13. **Cerrar no es cancelar.** La cabecera lo recuerda, con un texto adaptado al estado del lote. Pausar/Reanudar lleva un borde discontinuo y la etiqueta «Propuesta».

## Capacidades que requieren contrato con el motor
| Capacidad | Qué necesita la pantalla |
|---|---|
| **Estados y eventos** | Estado por mensaje (pendiente, en proceso, aceptado, fallido, incierto, cancelado), intentos con hora de inicio y fin, código y texto saneado, y notificación de cambios (eventos o sondeo) con identificador estable. |
| **Pausa / reanudación** *(propuesta)* | Dejar de tomar pendientes de un lote sin interrumpir los que están en proceso. Estado «pausado» persistente aunque se cierre la vista o se reinicie el proceso. Reanudar respetando el orden y sin duplicar envíos. |
| **Cancelación** | Cancelar mensajes pendientes de forma atómica: respuesta por mensaje («cancelado» o «ya en proceso»), sin condiciones de carrera con el despachador. Registro de quién canceló y cuándo. |
| **Reintentos** | Volver a encolar un fallido como nuevo intento del mismo mensaje (mismo Message-Id o uno derivado, según política), conservando los intentos anteriores. Clasificación del error: transitorio, permanente o que requiere corrección. Política explícita de reintento automático solo para transitorios y nunca para aceptados. Idempotencia ante peticiones repetidas. |
| **Resultado incierto** | Detectar la conexión cortada después de DATA y antes de la respuesta. Consultar al proveedor (registro por Message-Id, carpeta Enviados por IMAP o API del proveedor) si existe ese medio. Si no, permitir registrar una resolución manual con su autor. |
| **Reenvío** | Crear un mensaje nuevo a partir del contenido histórico (no de la plantilla actual), vinculado al original en ambos sentidos. |
| **Notificaciones de entrega** | Recibir DSN o webhooks del proveedor (entregado o rebotado, con fecha y código) y asociarlos al mensaje por Message-Id. Solo se muestran si la cuenta los ofrece. |
| **Contenido histórico y adjuntos** | Conservar el cuerpo, los destinatarios y las referencias a los adjuntos tal como se enviaron, y poder recuperarlos para la vista previa. |
| **Diagnóstico** | Detalle técnico saneado (sin credenciales) apto para copiar y enviar a soporte. |

## Pendiente de decidir
- Si el reintento conserva el Message-Id o genera uno nuevo.
- Política de reintentos automáticos para errores transitorios (número de intentos y espera entre ellos).
- Si «Reenviar» puede usar destinatarios actualizados o siempre los originales (en la maqueta, siempre los originales).
- Fuente real de la comprobación de inciertos con cada proveedor.
