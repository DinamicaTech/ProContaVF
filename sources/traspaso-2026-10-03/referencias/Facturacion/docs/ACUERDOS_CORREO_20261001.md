# Correo: registro e interfaz de procesos

Decisiones explícitas del usuario, 01/10/2026.

- Registro consultable de correos salientes y entrantes, accesible por opción
  de menú. INF-COR aporta la interfaz; reutiliza los registros y servicios
  de los motores propietarios. No crear un historial paralelo por módulo.
- Un proceso automático, como generación de facturas programadas, puede
  mostrar al terminar una ventana de resumen/seguimiento modal respecto
  al origen, independiente de las pestañas child del contenedor. Debe filtrar
  el proceso/lote concreto y reutilizar el componente de consulta del registro.
- Todo proceso que implique más de un email omite el compositor individual.
  El contenido/destinatarios se preparan mediante contratos del proceso;
  los errores se registran y gestionan sin abrir un editor por destinatario.
- El compositor individual sigue siendo modal respecto al origen.
- Finalización del proceso de facturación, encolado, aceptación del correo y
  entrega al destinatario son hitos distintos. No presentar uno como prueba
  de los demás. Cerrar el seguimiento no cancela por sí mismo los envíos.

Estado: requisitos acordados, no funciones completas acreditadas. La revisión
de INF-COR (commit 3bd357a) identifica transporte SMTP existente e historial
básico, pero no una cola saliente durable ni todo el seguimiento propuesto.
Contrato detallado:
[Compositor y lotes](C:/Docs/VeriFactu/ProConta.GestorCorreo/docs/CONTRATO_COMPOSITOR_Y_LOTES_CORREO.md).

Entrantes y salientes comparten navegación y componentes de consulta, pero
conservan estados/operaciones propios; no aplicar Reenviar/Reintentar a un
correo entrante por reutilizar una tabla visual.

Borradores, pausa, política Message-ID, resolución manual de inciertos y
notificaciones de entrega siguen sujetos a revisión. Las notas de Claude
son propuestas, no requisitos obligatorios ni autorización para enviar correo.

Documentos/adjuntos: propietario DOC-GESTOR, hilo
`01a0f6ee-afa9-7101-b599-97fa421a3359`,
[contrato](contratos/DOC-GESTOR.md). La conservación de la versión exacta del
adjunto enviado debe acordarse entre ambos contratos; no basta apuntar a un
archivo cuyo contenido pueda cambiar.
