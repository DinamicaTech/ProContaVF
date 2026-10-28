# Integración WPF del compositor individual

## Estado funcional

INF-COR publica un compositor WPF neutral para que DAT-Terceros y otros módulos
preparen un único email sin duplicar la pantalla. El incremento actual:

- abre una ventana modal con `Owner` WPF obligatorio, redimensionable y no
  `TopMost`;
- precarga destinatarios, remitente, contexto, asunto, cuerpo y adjuntos;
- mantiene visibles los cambios pendientes de la ficha como aviso funcional
  compacto, sin bandas técnicas;
- valida cada dirección y el asunto antes de devolver una solicitud de envío;
- devuelve cancelación, un borrador o una solicitud de envío;
- no consulta Access, no persiste borradores y no invoca SMTP ni Mail Dispatcher.

`PermiteSolicitarEnvio` vale `False` de forma predeterminada. En ese modo el
botón **Solicitar envío** está deshabilitado y la ventana explica que el shell
todavía no tiene transporte conectado. Habilitarlo solo permite devolver
`SolicitarEnvio`: no acredita encolado, aceptación ni entrega y no autoriza al
shell a mostrar éxito antes del resultado del motor propietario.

## Contrato neutral

DAT-Terceros expone o construye `SolicitudComposicionCorreo` con el contexto y
la dirección elegida. No referencia WPF. UI-PRINCIPAL referencia
`ProConta.GestorCorreo.UI.Wpf`, crea `ServicioCompositorCorreoWpf` y aporta su
ventana como propietaria.

```vb
Dim solicitud As New SolicitudComposicionCorreo With {
    .Contexto = New ContextoOrigenCorreo With {
        .ModuloOrigen = "DAT-TERCEROS",
        .TipoOrigen = "Tercero",
        .IdOrigen = tercero.IdTercero.ToString(CultureInfo.InvariantCulture),
        .DescripcionOrigen = $"Tercero · {tercero.NombreVisible}",
        .IdSociedad = contexto.IdSociedad,
        .IdSociedadEjercicio = contexto.IdSociedadEjercicio,
        .NombreSociedad = contexto.NombreSociedad,
        .TieneCambiosPendientes = ficha.TieneCambiosPendientes,
        .DescripcionCambiosPendientes = ficha.DescribirCambiosPendientes()
    },
    .Remitente = cuenta.DireccionCorreo,
    .NombreRemitente = cuenta.NombreRemitente,
    .Destinatarios = {direccionSeleccionada},
    .PermiteConservarBorrador = True,
    .PermiteSolicitarEnvio = False
}

Dim servicio As IServicioCompositorCorreoWpf = New ServicioCompositorCorreoWpf()
Dim resultado = servicio.MostrarDialogo(ventanaShell, solicitud)

Select Case resultado.Accion
    Case AccionCompositorCorreo.Cancelar
        Return
    Case AccionCompositorCorreo.ConservarBorrador
        ficha.ConservarBorradorCorreo(resultado.Borrador)
    Case AccionCompositorCorreo.SolicitarEnvio
        ' Solo alcanzable si el shell habilitó expresamente el trámite.
        coordinadorCorreo.Tramitar(resultado.Borrador)
End Select
```

El ejemplo expresa responsabilidades; `ConservarBorradorCorreo` y
`coordinadorCorreo.Tramitar` no son APIs entregadas por este incremento.

## Cancelación y borradores

- Abrir la ventana no produce efectos externos.
- Cancelar o cerrar con la X devuelve `Cancelar` y ningún borrador.
- **Conservar borrador** devuelve `BorradorComposicionCorreo` al invocador. No
  significa «guardado en disco» ni «guardado en Access».
- `FabricaSolicitudesCompositorCorreo.Reabrir` reconstruye una solicitud con el
  contenido devuelto. El shell debe conservar esa instantánea mientras quiera
  reabrirla.
- No existe todavía un puerto o repositorio canónico durable de borradores. Si
  el shell solo lo conserva en memoria, el contenido se pierde al cerrar la
  aplicación. No debe prometer recuperación entre sesiones.
- Una ficha de Terceros con cambios pendientes mantiene su propia gestión. El
  borrador de correo no guarda ni descarta la ficha.

## Propiedad y modalidad

`IServicioCompositorCorreoWpf.MostrarDialogo` exige una `Window` propietaria. La
implementación asigna `Owner` antes de llamar a `ShowDialog`; el shell conserva
el foco y no recibe una pestaña child. El contrato UI acepta `Window` porque
pertenece a la capa WPF, no al dominio ni a la aplicación neutral.

## Fuentes canónicas para integrar

Los hashes se calculan sobre los archivos versionados tras la verificación
final. UI-PRINCIPAL debe integrar por referencia de proyecto o por el commit,
no copiar clases sueltas.

| Archivo | SHA-256 |
| --- | --- |
| `src/ProConta.GestorCorreo.Aplicacion/ComposicionCorreo.vb` | `96C107AFB27873B47B2A67380D51648B4EA5B8C671E5113F32E169DC733D2C76` |
| `src/ProConta.GestorCorreo.UI.Wpf/ProConta.GestorCorreo.UI.Wpf.vbproj` | `A0446817A9C0B4E25B39315CA1283095FC5C00E7AD31251AB8F3143EE8A59A96` |
| `src/ProConta.GestorCorreo.UI.Wpf/VistaCompositorCorreoWpf.xaml` | `9A1311C0985DC2A0784BEF9B0484A2B51654C9DEC5BE8B452292AEBEE424B376` |
| `src/ProConta.GestorCorreo.UI.Wpf/VistaCompositorCorreoWpf.xaml.vb` | `32A1120CE5637D2C1740476887C2FC3F0AF5DD0937E8CE420E1ABC99043C48B0` |
| `src/ProConta.GestorCorreo.UI.Wpf/VistaCompositorCorreoWpfViewModel.vb` | `B16BC3CB335A7A4868D250FE55A36B86D4AE9D6E3D65972CB2247ED44451F01E` |
| `src/ProConta.GestorCorreo.UI.Wpf/ServicioCompositorCorreoWpf.vb` | `36BA8ECCD3B7E1F6915055E95E0BD4998EA52C838BBA1F7ACC45D92154D80EF9` |

Pruebas: `tests/ProConta.GestorCorreo.Tests/CompositorCorreoWpfTests.vb`.
Renderizador reproducible: `tools/render-compositor-correo.ps1`.

Renders revisados:

- `docs/renders/compositor-terceros.png` — 920 × 720;
- `docs/renders/compositor-terceros-estrecho.png` — 720 × 560.

## Limitaciones pendientes

- Persistencia durable y recuperación entre sesiones de borradores.
- Adaptador del shell hacia la futura coordinación saliente de INF-DIS.
- Selección real de cuentas remitentes dentro del contexto autorizado.
- Plantillas, previsualización HTML y adquisición documental de adjuntos.
- Resultado SMTP con certeza, destinatarios parciales e incertidumbre.
- Integración efectiva del botón de cada email en DAT-Terceros y composición en
  la versión `.10` de UI-PRINCIPAL.

Estas limitaciones no se sustituyen con estados simulados ni mensajes de éxito.
