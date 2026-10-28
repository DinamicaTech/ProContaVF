# Facturas WPF (beta)

Este vertical mantiene `VistaFacturas` WinForms intacta y abre una ventana WPF paralela. WPF reutiliza `ServicioConsultaFacturacion` mediante un adaptador; no contiene SQL, conexiones ACE/OLE DB ni reglas fiscales.

## LEOC y Krypton

`src/ProConta.Facturacion.UI.Wpf/VistaFacturasWpf.leoc` es la fuente editable LEOC 0.2 del layout y puede abrirse directamente en LEOC Designer. `VistaFacturasWpf.bindings.json` conserva aparte el contrato con el ViewModel y `VistaFacturasWpf.generated.xaml` es el artefacto generado y versionado. `VistaFacturasWpf.xaml`, su clase parcial, los converters y el ViewModel son el envoltorio estable: no se regeneran y mantienen los recursos semánticos de estado, selección, ordenación y doble clic.

Las rutas de desarrollo se resuelven mediante las variables de usuario `LEOC_ROOT` y `PROCONTA_LEOC_THEME`; ProConta no contiene referencias binarias ni de ejecución a LEOC. El comando explícito desde `C:\Docs\VeriFactu\ProConta.WpfFacturas` es:

```powershell
dotnet msbuild .\src\ProConta.Facturacion.UI.Wpf\ProConta.Facturacion.UI.Wpf.vbproj `
  -target:RegenerarVistaFacturasWpf `
  -property:LEOC_ROOT="$([Environment]::GetEnvironmentVariable('LEOC_ROOT','User'))" `
  -property:PROCONTA_LEOC_THEME="$([Environment]::GetEnvironmentVariable('PROCONTA_LEOC_THEME','User'))"
```

Antes de `MarkupCompilePass1`, MSBuild regenera incrementalmente si cambió el `.leoc`, el manifiesto o el tema y las variables están disponibles. Si LEOC no está disponible, la compilación continúa con el último XAML versionado y muestra un aviso; el target explícito falla con un diagnóstico accionable. Los cambios del `.leoc` o del tema requieren regenerar y recompilar: nunca alteran un ejecutable que ya se hubiera construido.

El contrato estable de `x:Name`, bindings y comandos está en el manifiesto y en `ContratoVistaFacturasWpf.vb`. Superficie, color primario, campos, tipografía y métricas proceden del tema indicado por `PROCONTA_LEOC_THEME`; solo la paleta funcional de estados de factura permanece en el host estable.

Krypton es la biblioteca homogénea de las pantallas WinForms de ProConta. No ofrece controles WPF nativos; por eso esta pantalla usa controles WPF puros y tokens visuales derivados del tema LEOC, sin mezclar tecnologías dentro de la vista.

## Rendimiento y evolución

La cuadrícula virtualiza filas y columnas, recicla contenedores, fija filas a 30 px y evita plantillas por celda. La carga es cancelable y se ejecuta fuera del despachador; el pie muestra tiempos de consulta, materialización y presentación.

El contrato `ConsultaFacturacion` ya mantiene filtros, orden y límite fuera de WPF. Para 20–30.000 filas, el siguiente vertical debe extender ese contrato con cursor/página y recuento, y resolverlos en `RepositorioConsultaFacturacionAccess`; el ViewModel solo sustituirá la instantánea o pedirá la página siguiente. No se debe introducir filtrado masivo en memoria ni acceso directo a Access desde WPF.
