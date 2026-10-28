# Entrega selectiva Contacto / Terceros para .10

Fecha: 2026-10-01. Raíz: `C:\Docs\VeriFactu\ProConta.Terceros`.

Actualización posterior: la agrupación libre ya tiene implementación y delta probado en `docs/CONTACTOS_LIBRES_TERCEROS_20261001.md`. Su paquete se superpone a este; los 13 archivos congelados y hashes de esta primera entrega no se alteran. El apartado de dependencia al final describe el alcance de este primer paquete, no el estado de la entrega adicional.

## Paquete y compatibilidad

Paquete congelado: `integracion/entregas/contacto-10/`.
Manifiesto exacto de 13 archivos, SHA-256, destino sugerido y hash del archivo previo cuando existe: `docs/ENTREGA_CONTACTO_UI_10_20261001.sha256.json`.

Solo se entregan doce fuentes WPF y el adaptador de composición. No contiene proyectos completos, aplicación, persistencia, SQL, ContratoFinal.json ni migraciones. No incorporar los archivos de Email150 de esta rama ni el esquema 0.23. Conservar los proyectos y referencias ya adaptados por UI-PRINCIPAL.

Se ha construido y probado la combinación con una copia privada de las dependencias vigentes de `C:\Docs\VeriFactu\ProConta.Facturacion\tools\ProConta.Acceso.Prototipo\Dependencias`. Su manifiesto exige **TERCEROS_VF_1, Terceros.Email254**. Los hashes de las dependencias leídas constan en el manifiesto como evidencia, no como archivos para copiar. La UI no cambia longitudes ni validaciones de aplicación.

No se ha modificado el contenedor central, publicado un EXE ni abierto/modificado bases originales. La integración final y su prueba en el shell corresponden a UI-PRINCIPAL.

## Archivos de producción

En `Wpf/`, copiar al proyecto WPF de Terceros del contenedor:

- `EditorDatosContacto.xaml` y `.xaml.vb`: etiqueta 170 DIP, email 320 DIP, teléfono 170 DIP; distribución adaptable, valores completos en tooltip y edición con Inicio/Fin. Panel efectivo visible, recalculado tras actualizar cada enlace del borrador, sin consultas Access por pulsación. Reutiliza ResolverEmails y sus reglas de finalidad, alternativa única e inactivos.
- `VistaFichaTercero.Secciones.vb` y `VistaFichaTercero.vb`: relación junto a sus acciones, sin altura fija vacía; solicitud de compositor y aviso de borradores temporales; contexto repetido trasladado a tooltip.
- `VistaFichaTercero.Relaciones.vb`: cancelación sin cambios, candidatos clasificados, validación de identidad/rol activo al elegir; baja local de nuevas relaciones y cierre confirmado de las guardadas, conservando clave e historial.
- `VistaListadoTerceros.vb`: selección reutilizando el listado, buscador y filtros. Mantiene carga asíncrona y espera de todas las operaciones pendientes antes de liberar la vista.
- `SelectorTerceros.xaml` y `.xaml.vb`: modal con Owner, selección por Intro/doble clic y cancelación; espera el cierre de lecturas activas.
- `ConfirmacionRelacion.xaml` y `.xaml.vb`: confirmación explícita del cierre de vigencia.
- `NombreVinculadoConverter.vb`: nombres legibles de identidades enlazadas.
- `SolicitudCompositorTercero.vb`: evento de composición individual hacia el anfitrión.

En `Integracion/`, incorporar `CompositorCorreoTerceros.vb` al proyecto anfitrión que referencia INF-COR. No incorporarlo a dominio/aplicación ni al proyecto WPF de Terceros.

El selector no genera Contactos. La ausencia de candidatos IdContacto se explica por la migración de Clientes/Proveedores: esta no crea roles Contacto. Para vincular una persona existente debe estar clasificada y tener ese rol activo guardado; se ofrece ayuda funcional cuando se abre el selector.

## Firmas y conexión del compositor

API de Terceros:

```vb
Event VistaFichaTercero.CompositorSolicitado As EventHandler(Of SolicitudCompositorTercero)
Event VistaFichaTercero.Cerrada As EventHandler
Sub VistaFichaTercero.InformarBorradoresCorreo(cantidad As Integer)
```

`SolicitudCompositorTercero` publica Destinatario As String, IdTercero As Long, IdDatoContacto As Long, Contexto As ContextoTerceros, Propietario As Window y Atendida As Boolean. IDs cero representan datos aún no guardados.

Adaptador del anfitrión:

```vb
New ProConta.Terceros.Integracion.CompositorCorreoTerceros(
    vista As VistaFichaTercero,
    compositor As IServicioCompositorCorreoWpf,
    preparar As Func(Of SolicitudCompositorTercero, SolicitudComposicionCorreo))
```

Crear una instancia por ficha y conservarla durante su vida. Se desconecta y vacía al evento Cerrada o Dispose. El delegado preparar debe revalidar permisos, resolver la cuenta/remitente reales y completar ContextoOrigenCorreo: sociedad/ejercicio, origen, descripción y estado actual de cambios pendientes. El adaptador rechaza contexto sin ejercicio o distinto del de la ficha. No inventa cuentas ni concede permisos. Si el shell carece de una cuenta válida debe producir un error funcional corregible desde su configuración.

Referencia canónica INF-COR: `C:\Docs\VeriFactu\ProConta.GestorCorreo\docs\INTEGRACION_COMPOSITOR_WPF.md`, commit publicado `378b88c`. Usar sus proyectos/contrato, no duplicar clases del compositor. Se compiló contra:

```vb
IServicioCompositorCorreoWpf.MostrarDialogo(
    propietario As Window, solicitud As SolicitudComposicionCorreo
) As ResultadoCompositorCorreo
FabricaSolicitudesCompositorCorreo.Reabrir(
    borrador As BorradorComposicionCorreo,
    nombreRemitente As String, permiteSolicitarEnvio As Boolean
) As SolicitudComposicionCorreo
```

La ventana propietaria se obtiene de la ficha alojada mediante Window.GetWindow. En el shell debe existir; el compositor canónico la exige. El doble de pruebas acepta Nothing porque el render aislado no crea un shell.

Siempre `PermiteSolicitarEnvio=False`, `PermiteConservarBorrador=True`. Un borrador por dirección seleccionada, comparación sin distinguir mayúsculas. Conservar devuelve la instantánea a memoria; pulsar otra vez la misma dirección la reabre. Cancelar conserva la versión anterior. Cerrar la ficha elimina todas las instantáneas. El pie lo advierte: «borrador(es) de correo en memoria; se pierden al cerrar la ficha». Guardar ficha no persiste correo. No hay SMTP, Dispatcher, almacenamiento durable ni confirmación de envío.

## Pruebas y límites de la evidencia

- Build solución completa x64: **0 errores, 33 advertencias**, log `tmp/contacto-10-solucion.log`.
- Pruebas del módulo contra las fuentes de esta rama y proyectos canónicos INF-COR: **17 correctas, 2 omitidas**; las omitidas requieren una copia real explícita y no se ejecutaron. Log `tmp/contacto-10-test.log`; TRX `tmp/contacto-10-test/contacto-10.trx`.
- Build de los doce archivos UI superpuestos a copia privada del snapshot del contenedor: **0 errores, 0 advertencias**, log `tmp/contacto-10-compat-build.log`.
- Pruebas funcionales con aplicación/persistencia del snapshot Email254 y adaptador INF-COR: **11 correctas, 1 benchmark omitido**, log `tmp/contacto-10-compat-test.log`; TRX `tmp/contacto-10-compat-test/compat.trx`. Harness local `tmp/contacto-10-compat/Compat.vbproj`. Access sintético se creó con Email254; no se reutilizó una base real.
- Cubierto: actualización de texto/activo y resolver efectivo, ancho relativo y tooltip, dirección inválida deshabilita composición, cancelar elección, alta/baja local, cancelación/cierre confirmado, auditoría FechaFin, filtro de candidato y alta oculta, reapertura/cancelación del borrador, rechazo de contexto nulo/distinto y desconexión al cerrar. También regresión de guardado, perfiles, concurrencia y esperas asíncronas del listado.
- Fuentes nuevas de prueba: `tests/ProConta.Facturacion.Tests/ContactoVisualTercerosTests.vb` e `integracion/Pruebas/CompositorCorreoTercerosTests.vb`. Para el segundo, el proyecto de pruebas admite `-p:RutaGestorCorreo=C:/Docs/VeriFactu/ProConta.GestorCorreo`.
- Renders sintéticos revisados en `tmp/contacto-10/`: `contacto-amplio.png`, `contacto-compacto.png`, `contacto-150dpi.png` (144 DPI / escala 150%), `relaciones-vacias.png`, `relaciones-pobladas.png`, `selector.png`. Campos editables mantienen parada de teclado. El render a 144 DPI no equivale a una prueba interactiva moviendo ventanas entre monitores; modalidad, foco final y teclado en el shell integrado siguen como comprobación de integración.
- `git diff --check` sin errores de espacios.

## Dependencia pendiente: contacto libre agrupado

**No está implementado en este paquete.** El modelo actual DatosContacto almacena datos individuales y no permite agrupar de forma estable nombre, departamento, emails y teléfonos sin crear una identidad/rol adicional. Etiqueta no debe emplearse como clave de agrupación.

La implementación requiere acordar/desplegar una entidad de agrupación dependiente de Terceros (1:N), con nombre y departamento, y una referencia opcional desde DatosContacto (grupo 1:N datos). Los registros actuales quedarían sin grupo y conservarían sus finalidades/actividad. La referencia debe garantizar el mismo IdTercero; persistencia y auditoría han de guardarse en la transacción de ficha. No implica crear un Tercero ni Contacto ficticio. ActualizadorEsquema deberá publicar la migración compatible antes de activar esa funcionalidad.

Este paquete cierra el subconjunto visual y el adaptador probado. No acredita agrupación libre, almacenamiento durable de correo ni integración final del compositor en el EXE central.
