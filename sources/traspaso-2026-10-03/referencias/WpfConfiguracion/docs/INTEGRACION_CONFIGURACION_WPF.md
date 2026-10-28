# Integración de CFG-MANT WPF

Entrega compatible: rama `codex/wpf-configuracion`, commit `688b4102dcacbbcff30c1f5a3b691560351f913f`.

## Subconjunto mínimo compilable

El contenido reutilizable no necesita el proyecto WinForms, `VistaConfiguracionWpf.xaml` ni regenerar LEOC. El proyecto WPF compila el XAML versionado cuando `LEOC_ROOT` y `PROCONTA_LEOC_THEME` no están definidos.

| SHA256 | Fuente |
|---|---|
| `246E487FC9FEF7544F08B61864607D5B3BC9A68394324D8938DF2C681618C9DC` | `src/ProConta.Facturacion.Aplicacion/Configuracion/ContratosConfiguracion.vb` |
| `B31F6547058EB1698A32D81F2B3D14CC004DC6756A5E6BC55B890FC7DD85B016` | `src/ProConta.Facturacion.Aplicacion/Configuracion/ProveedoresConfiguracion.vb` |
| `35F2C9F4954711520D760E406328DCEEC36142A39CF2BF7F292CBEEB56D53B85` | `src/ProConta.Facturacion.Aplicacion/Configuracion/ServicioConfiguracion.vb` |
| `ECF1069518387C7376731135A4EF2D1B37880A9272390339F05E230D10F249B8` | `src/ProConta.Facturacion.Persistencia.Access/Configuracion/RepositorioConfiguracionAccess.vb` |
| `B16C6C46014D45F54B34C7F87BA2BC500E7112B3DAD0CCA2AA9A7A16AFCD2E5A` | `src/ProConta.Facturacion.UI.Wpf/ContenidoConfiguracionWpf.xaml` |
| `F47576DCD1AFE779FA3CF7874EC917A9FAAC8B31161A50328753FFFB5FD445D2` | `src/ProConta.Facturacion.UI.Wpf/ContenidoConfiguracionWpf.xaml.vb` |
| `6CAD4B071C3A1E504E1F8E87FB8F71740937A36D7ACA3E4EC3A31D7A54B20693` | `src/ProConta.Facturacion.UI.Wpf/VistaConfiguracionWpfViewModel.vb` |
| `35428A5ABE396091E02CD5D8809649ADC95EB237EFAB6ABD25841C3119991CF7` | `src/ProConta.Facturacion.UI.Wpf/SelectorPlantillaCampoConfiguracion.vb` |
| `76F9A920E4879804F943CAC9D4EC77102109BD174885039090241D861175F5E0` | `src/ProConta.Facturacion.UI.Wpf/ContratoAlojamientoConfiguracionWpf.vb` |
| `E8C15EE6758942DFAF8819582FA6ADE674014184F668C64FCAAFEF4024278FFF` | `src/ProConta.Facturacion.UI.Wpf/Comandos.vb` |
| `D0663A2C45FF2D94A0CE478FA18599CFED652D071213818AC34C35288930DD22` | `src/ProConta.Facturacion.UI.Wpf/InvertidorBooleano.vb` |

Referencias: UI WPF → Aplicación; Persistencia Access → Aplicación y la fábrica Access existente. Este subconjunto no referencia `ProConta.Facturacion.App` ni WinForms.

## Composición real y fábrica

CFG-MANT no crea tablas ni altera esquemas. La composición existente es:

```vb
Dim repositorio As IRepositorioConfiguracion = New RepositorioConfiguracionAccess(fabricaConexiones)
Dim servicio As New ServicioConfiguracion(repositorio)
Dim registro = servicio.CrearRegistroProveedores()
Dim preparacion = Await PantallaConfiguracionWpf.PrepararAsync(registro, contexto, autorizacion, cancelacion)
Dim pantalla = PantallaConfiguracionWpf.Crear(preparacion) ' En el Dispatcher WPF del contenedor.
host.Content = pantalla.Vista
```

`PrepararAsync` filtra por `IAutorizacionConfiguracion`, exige contexto si un proveedor autorizado depende de sociedad o ejercicio y carga el modelo sin controles WPF. `Crear` materializa el `UserControl` en el Dispatcher propietario. Los proveedores reales proceden de `ServicioConfiguracion.CrearRegistroProveedores()` y de los campos devueltos por `IRepositorioConfiguracion.DescribirCampos()`. `RepositorioConfiguracionAccess` opera sobre la base recibida mediante `FabricaConexionesAccess`; no contiene DDL.

El repositorio Access genérico no filtra por sociedad ni ejercicio. Por seguridad, sus proveedores se publican como `Compartido` y `DependeDelContexto=False`. No deben anunciarse como contextuales hasta que un proveedor tipado propietario aplique y pruebe el filtro real en cada lectura, escritura y catálogo de referencia.

CFG-GLOBAL no forma parte del subconjunto. `ProveedorUbicacionDatosLocal` permanece en la aplicación actual y usa almacenamiento local; su propietario puede registrarlo adicionalmente sin materializarlo en Access.

## Contexto, permisos y revalidación

### Bloqueo de activación SEG

CFG-MANT no contiene actualmente un contrato canónico acordado de códigos SEG para abrir la pantalla, acceder a cada proveedor o ejecutar cada operación. Los permisos existentes de Reports y ProcesosContables no son equivalentes y no deben reutilizarse ni inferirse. Por tanto, aunque el wrapper de revalidación está implementado, Configuración real debe permanecer fuera del catálogo central hasta que el propietario de Seguridad publique el mapeo y un adaptador implemente `IAutorizacionConfiguracion`. La ausencia de ese adaptador es denegación, nunca permiso vacío. Este bloqueo tampoco debe sortearse ampliando modos de pruebas de terceros.

- `ContextoAlojamientoConfiguracion` transporta `IdSociedad`, `IdEjercicio` e `IdOperador`; no concede autoridad.
- `IAutorizacionConfiguracion` es obligatorio: no existe permiso permisivo por defecto.
- Se comprueba `PuedeAbrirPantalla` y `PuedeUsarProveedor` para cada identificador estable.
- `ProveedorConfiguracionAutorizado` revalida `Leer`, `CargarOpciones`, `Crear`, `Modificar` o `Eliminar` justo antes de delegar. Así también quedan protegidos los comandos locales del modelo.
- El integrador debe implementar `PuedeEjecutarOperacion`; la ausencia de adaptador no equivale a permiso.
- Para cambiar sociedad/ejercicio: consultar `TieneCambiosPendientes`, esperar `EsperarTareasAsync` y decidir centralmente guardar, descartar o cancelar.
- `DependeDelContexto` refleja los proveedores autorizados: `Sociedad` y `Ejercicio` dependen; `Compartido` y `GlobalAplicacion` no.

## Límites de edición y tareas

- Se respetan `EsEditable`, validadores, `RegistroUnico`/`Coleccion` y capacidades declaradas de alta/eliminación.
- Los secretos no se revelan ni editan con el renderizador genérico.
- Reglas fiscales, efectos laterales y permisos por operación pertenecen al proveedor tipado.
- La carga inicial es asíncrona. Guardar/eliminar siguen el contrato síncrono actual del proveedor; `GuardarAsync` no convierte esa E/S en cancelable una vez iniciada.
- `EsperarTareasAsync` coordina cierres; actualmente no existe una cola de operaciones de proveedor en segundo plano.
- El host conecta su servicio central mediante `IPantallaConfiguracionWpf.ConfirmarEliminacion`. Un registro persistido no se elimina si falta el callback; cancelar, revocar o fallar conserva la fila. El host conserva el Owner del diálogo.
- No importar `VistaConfiguracionWpf.xaml`: es el host de comparación de la migración.

## Verificación

- Solución completa `Debug`, `x64`: 0 errores con XAML LEOC versionado.
- `ConfiguracionWpfTests`: 19/19 superadas.
- Evidencia WPF real con tres filas y texto, entero, decimal, booleano, fecha, referencia de dos opciones, memo, ruta y secreto: `docs/renders/configuracion-amplia-1380x860.png` y `docs/renders/configuracion-estrecha-775x520.png`.
- La tabla usa padding, selección naranja, elipsis y ToolTip del valor completo; ofrece desplazamiento horizontal cuando las columnas no caben.
- No se abrió el ejecutable ni se modificó una base Access original.
