# ProConta.UI.Base

Biblioteca de estilos y componentes WPF compartidos de ProContaVF. Sus decisiones están en `docs/plataforma/ui-base.md`; esta página explica cómo usarla.

## Uso

En `App.xaml` de la aplicación:

```xml
<ResourceDictionary Source="/ProConta.UI.Base;component/Themes/ProConta.xaml" />
```

En cada ventana: `Style="{StaticResource Ventana.ProConta}"`. Espacio de nombres XAML: `xmlns:pc="urn:proconta-ui"`.

Al iniciar sesión, la aplicación fija las preferencias del operador:

```csharp
PreferenciasOperador.Actual = new PreferenciasJson(operador);
```

## Contenido

| Pieza | Claves o tipos |
|---|---|
| Tokens | `Color.*`, `Pincel.*`, `Fuente.Texto/Mono/Titulo`, `Letra.*`, `Radio.*`, `Alto.*`, `Sombra.*` (`Themes/Tokens.xaml`) |
| Iconos | `Icono.<nombre>` (geometrías 24×24 de las maquetas) con los estilos `Icono.Trazo`, `Icono.Trazo12/14/18/32`; en botones, `pc:Iconos.Icono` |
| Botones | implícito, `Boton.Primario`, `Boton.Peligro`, `Boton.Pequeno`, `Boton.Fantasma`, `Boton.Icono`, `Boton.Enlace`, `Boton.Aspa` |
| Campos | implícito `TextBox` (error: borde rojo, icono y mensaje en el tooltip), `Campo.Importe`, `Campo.Mono`, `Campo.Multilinea`, `ComboBox`, `CheckBox`, `RadioButton` |
| Selector segmentado | `Border Style="{StaticResource Segmentado}"` con `RadioButton Style="{StaticResource Segmento}"` |
| Textos | `Texto.TituloPantalla`, `Texto.Titulo`, `Texto.Seccion`, `Texto.Apagado`, `Texto.Mono`, `Texto.Importe`, `Texto.Saldo`, `Texto.Celda` (inicio con «…» y texto completo en el tooltip) |
| Contenedores | `Tarjeta`, `Barra.Pie`, `Barra.Seleccion`, `Separador` (Thumb), `GridSplitter`, `TabControl`, `ContextMenu`, `ToolTip`, barras de desplazamiento finas |
| Tabla | `pc:TablaProConta`: criterio global de columnas (ordenar, mover, ocultar, redimensionar, restablecer, exportar a Excel) guardado por operador con `ClavePreferencias`. Por columna: `pc:TablaProConta.Clave`, `OcultaDeFabrica`, `SiempreVisible`, `RutaExportar` |
| Componentes | `pc:PanelPlegable`, `pc:VistaEstado` (vacío, cargando, error, sin permiso), `pc:Etiqueta` (filtro), `pc:Nota`, `pc:DialogoProConta` |
| Formatos | `Formatos.Importe/Saldo/Fecha/Plural`, `ReglaSaldo` (color del saldo por grupo del PGC), conversores `ImporteConverter`, `FechaConverter`, `SaldoPincelConverter` |
| Preferencias | `IPreferenciasOperador`, `PreferenciasJson` (fichero local por operador; más adelante, base de datos) |

## Muestra

`src/ProConta.UI.Muestra` enseña todos los componentes. Para compararla con las maquetas:

```
ProConta.UI.Muestra.exe --captura muestra-2560.png 2560 1440 0
```

El último número es la pestaña (0: tabla y panel; 1: controles).

## Licencias de terceros

- ClosedXML (MIT), para exportar a Excel.
- IBM Plex Sans, IBM Plex Mono y Bricolage Grotesque (SIL Open Font License 1.1): `Fonts/OFL-*.txt`.
