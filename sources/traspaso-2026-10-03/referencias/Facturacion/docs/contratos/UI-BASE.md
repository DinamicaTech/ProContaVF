# UI-BASE — Responsabilidades y contrato público

## Criterio transversal de dimensiones — 01/10/2026

**Acuerdo del usuario, pendiente de aplicación y verificación en las nuevas pantallas.** Procedencia: [requisitos de Terceros del 01/10/2026](../REQUISITOS_TERCEROS_20261001.md), apartado «Diseño y entrada de domicilio».

- Dimensionar cada campo proporcionalmente al contenido y formato esperado. Evitar anchos desproporcionados y no igualar todos los controles al ancho máximo disponible. La alineación del formulario no exige que todos los campos tengan la misma anchura.
- Considerar la etiqueta, la tipografía, los elementos internos del control y el escalado DPI de Windows al distribuir el espacio. El tamaño debe permitir leer y manejar el dato, sin recortar etiquetas ni superponer controles al aumentar el escalado.
- Separar anchura visible y capacidad del dato: no reducir almacenamiento, validación de longitud ni `MaxLength` para ajustar la apariencia. Los límites funcionales siguen perteneciendo al contrato de cada dato.
- Contemplar valores válidos más largos que el habitual, incluidos formatos internacionales. Deben poder introducirse, consultarse y corregirse completos mediante la adaptación de espacio o el desplazamiento/multilínea adecuado al tipo de campo, sin truncar ni perder su contenido.
- Contrastar las futuras propuestas con valores habituales y largos y con escalado de Windows; no fijar ahora una tabla universal de anchuras ni dar por verificada su aplicación en las pantallas existentes.

UI-BASE conserva este criterio común; cada pantalla lo concreta según sus datos y distribución. En la revisión posterior del 01/10/2026 el usuario aporta capturas comparativas y pide corregir su aplicación en Terceros, N43 y Configuración. Las acciones de listas deben quedar próximas a su contenido, sin grandes espacios vacíos; las bandas técnicas repetidas se sustituyen por información discreta de estado y detalle accesible. Ver el apartado de revisión WPF de los requisitos enlazados. La implementación y verificación de cada pantalla siguen pendientes hasta su entrega integrada.

## Superficie actual — actualización del 30/09/2026

**Existe una biblioteca WPF independiente implementada:** [ProConta.UI.Base.vbproj](../../tools/ProConta.UI.Base/ProConta.UI.Base.vbproj), ubicada provisionalmente en `tools/ProConta.UI.Base` por autorización del usuario, sin worktree propio. VB.NET, .NET 10 Windows, x64; rama anfitriona `codex/integracion-proconta-20260822`, HEAD base `773d3e491687ffe72a3cd889566968e56461acf0`, archivos sin commit en esta entrega.

Contrato de consumo vigente: [README de UI-BASE](../../tools/ProConta.UI.Base/README.md), que enumera claves, tipos, URI, ejemplos y evidencia. No se duplica aquí el catálogo. Capacidades implementadas:

- Recursos de login: `/ProConta.UI.Base;component/Recursos/Acceso.xaml`, que importa `PaletaAcceso.xaml`. Extracción de estilos del prototipo aprobado .4; el usuario ha validado posteriormente el login .5. Esta actualización no modifica esos recursos.
- Recursos del contenedor: `/ProConta.UI.Base;component/Recursos/Contenedor.xaml`. Diccionario autónomo de claves explícitas `Contenedor.*`: cabecera/lateral navy, acento naranja, fondo cálido, pestañas, navegación y tarjetas Inicio. Referencia publicada revisada en navegador según los [acuerdos del contenedor](../ACUERDOS_CONTENEDOR_WPF.md). No aplica estilos globales ni importa el diccionario de acceso.

Interfaz pública: recursos WPF/BAML y estilos; no hay métodos VB de inicialización. Entradas: clave, contenido del control y estados visuales (`IsChecked`, `IsSelected`, foco, habilitación). Salidas/efectos: propiedades y plantillas en memoria. Contexto requerido: consumidor WPF con referencia al ensamblado y diccionario importado. Errores de URI/clave/tipo se propagan por WPF; no hay fallback común. No requiere permisos funcionales ni escribe tablas, archivos de preferencias o sesiones. Selección visual no concede autorización.

UI-PRINCIPAL integra el layout y comportamiento, cierre de pestañas, ToolTips, comandos, sesión y contexto. UI-BASE no implementa la maqueta como contenedor funcional ni transfiere autoridad desde SEG-ACCESO. Compilación independiente del incremento: 0 errores/0 advertencias; comprobación de carga y estados documentada en README. La verificación de recursos no certifica el contenedor integrado ni sustituye la validación visual del usuario.

**Lectura del inventario siguiente:** los apartados 1–8 conservan el diagnóstico inicial previo a la implementación de esta biblioteca, salvo donde se indica estado actualizado. Las referencias al vertical Facturas WPF y mensajes WinForms son antecedentes evaluables, no la superficie actual del login/contenedor. La normalización completa, mensajes WPF y validaciones comunes siguen pendientes.

## 1. Identidad y procedencia

- Clave: **UI-BASE**. Responsable funcional: hilo **UI-BASE · Base visual y componentes WPF**, `01a0f1d3-5060-74c2-8945-260a39b48c6b`.
- Fecha de comprobación: **30/09/2026**, Europe/Madrid.
- Alcance de esta entrega: documentación inicial autorizada para la normalización progresiva del primer roadmap. No acredita normalización terminada ni entrega de una biblioteca común.
- Carpeta del hilo: `C:/Docs/VeriFactu`. Sin rama ni worktree propios asignados.
- Ubicación documental provisional autorizada: `C:/Docs/VeriFactu/ProConta.Facturacion/docs/contratos/UI-BASE.md`.
- Repositorio anfitrión comprobado: `C:/Docs/VeriFactu/ProConta.Facturacion`; rama `codex/integracion-proconta-20260822`; HEAD `773d3e491687ffe72a3cd889566968e56461acf0`. Este commit identifica la base consultada, no contiene esta ficha nueva.
- Referencia WPF comprobada: `C:/Docs/VeriFactu/ProConta.WpfFacturas`; rama `codex/wpf-facturas`; HEAD `203d912dff107f746e3dd2fa243c95217d22ac33`; `git status --short` sin cambios en el momento de consulta.

El anfitrión ya tenía cambios ajenos en `AGENTS.md` y archivos sin seguimiento en documentación, reservas, pruebas y herramientas de esquema. Se preservan. Se consultaron estado, historial reciente y diff de instrucciones; la excepción `safe.directory` se aplicó solo a los comandos de lectura, sin cambiar la configuración global de Git.

Referencias de gobierno: [mapa](../MAPA_PROYECTO.md), [registro de hilos](../REGISTRO_HILOS.json), [instrucciones](../../AGENTS.md), [convenciones](../CONVENCIONES.md) y [guía de mensajes](../MENSAJES.md). Las instrucciones conservan WinForms/Krypton como tecnología histórica; la autorización explícita del usuario para la aplicación nueva WPF prevalece en este encargo. Se mantienen VB.NET, .NET 10, x64 y separación de presentación, dominio, servicios y persistencia.

## 2. Responsabilidad y exclusiones

UI-BASE responde por recursos, estilos, tipografía, controles reutilizables, presentación común de mensajes y validaciones, accesibilidad y ToolTips. Antes de sustituir componentes se evaluará lo existente en WPF/LEOC. Los consumidores previstos son UI-PRINCIPAL y las pantallas funcionales.

Quedan fuera la composición, arranque y navegación de la aplicación; las reglas de negocio y sus validaciones; autenticación, autorización y ciclo de sesión; selección efectiva del contexto funcional; repositorios, SQL, migraciones y propiedad de tablas. Mostrar una confirmación no autoriza una operación ni garantiza que se haya ejecutado o revertido.

El hilo **Diseñar pantallas WPF estandarizadas**, `01a0f182-bf1a-7640-bf05-d928401dd316`, aporta propuestas visuales. Esas propuestas no son por sí mismas interfaces implementadas, recursos aceptados ni decisiones de integración. UI-BASE evaluará su materialización técnica sin duplicar ese trabajo. No se ha auditado su contenido en esta entrega.

## 3. Capacidades y superficie de consumo

**Inventario histórico previo a la biblioteca de login/contenedor descrita al inicio.** «Implementada» en los apartados siguientes significa existencia comprobada en el componente y rama indicados; no atribuye su propiedad a UI-BASE ni acredita integración en la nueva aplicación.

### 3.1 Recursos, estilos y tipografía

- Estado: **implementada en el vertical Facturas WPF; propuesta su reutilización común**.
- Contrato existente: [WPF_FACTURAS.md](../../../ProConta.WpfFacturas/docs/WPF_FACTURAS.md). Fuente: [VistaFacturasWpf.generated.xaml](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/VistaFacturasWpf.generated.xaml), recursos locales del `UserControl`. Ejemplos reales: `LeocSurfaceBrush`, `LeocPrimaryBrush`, `LeocTextBrush`, `LeocLabelStyle`, `LeocTitleStyle` y `LeocTextBoxStyle`; tipografía generada `Segoe UI`.
- Interfaz/método: consumo declarativo WPF mediante `StaticResource`; no hay método de carga común de UI-BASE comprobado. Los recursos actuales no constituyen todavía un diccionario global compartido.
- Entradas/salidas: claves y estados visuales del control → pinceles, tamaños, márgenes y estilos resueltos en su ámbito de recursos.
- Errores: una clave ausente o un tipo incompatible puede impedir cargar o aplicar el XAML; no se ha identificado un traductor común de estos fallos a mensajes de usuario.
- Permisos: ninguno funcional; el estilo no decide acceso. Efectos: presentación en memoria. Contexto: árbol visual y recursos WPF correspondientes; sin sociedad, ejercicio ni base requeridos para resolver un estilo.
- La semántica de los estados de factura permanece en Facturación. El host [VistaFacturasWpf.xaml](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/VistaFacturasWpf.xaml) contiene recursos `Factura.Estado.*` y `EstiloFilaFacturacion`; su generalización no se presume aprobada.

### 3.2 Generación LEOC y fachada estable

- Estado: **implementada para Facturas WPF; pendiente decidir su alcance común**.
- Interfaz real de construcción: target `RegenerarVistaFacturasWpf` del [proyecto WPF](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/ProConta.Facturacion.UI.Wpf.vbproj). Entradas: `.leoc`, manifiesto `.bindings.json`, tema indicado por `PROCONTA_LEOC_THEME` y compilador localizado mediante `LEOC_ROOT`. Salida: `VistaFacturasWpf.generated.xaml`.
- Errores: el target explícito diagnostica variables/rutas ausentes; la construcción ordinaria tiene un aviso y utiliza el XAML versionado cuando LEOC no está disponible. No se ha ejecutado generación en esta entrega ni comprobado la instalación externa.
- Efectos: la regeneración escribe XAML; exige acceso a sus entradas y permiso de escritura sobre la salida de desarrollo, no permisos de negocio. No es una dependencia de ejecución declarada por este proyecto.
- Interfaz de ejecución: clase pública `VistaFacturasWpfLayout`, constructor `New()`, propiedades de solo lectura `CuadriculaFacturas As DataGrid` y `BotonLimpiarFiltros As Button`, en [VistaFacturasWpfLayout.vb](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/VistaFacturasWpfLayout.vb). Sin entradas en el constructor; materializa controles, sin escribir datos. Requiere contexto WPF. Las propiedades usan `FindName` y `DirectCast`: no aportan validación explícita ante nombres ausentes/tipos incompatibles.
- El catálogo público `ContratoVistaFacturasWpf` y el manifiesto conservan nombres/bindings del vertical; [contrato existente](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/ContratoVistaFacturasWpf.vb). No es un contrato de controles genéricos ni transfiere `IConsultaFacturasWpf` a UI-BASE.

### 3.3 Comandos de presentación

- Estado: **implementada en Facturas WPF; propuesta su evaluación para reutilización**.
- Clase real: `ProConta.Facturacion.UI.Wpf.ComandoDelegado`, implementa `System.Windows.Input.ICommand`; código en [Comandos.vb](../../../ProConta.WpfFacturas/src/ProConta.Facturacion.UI.Wpf/Comandos.vb).
- Métodos reales: `New(ejecutar As Action(Of Object), Optional puedeEjecutar As Func(Of Object, Boolean) = Nothing)`, `CanExecute(parameter As Object) As Boolean`, `Execute(parameter As Object)` y `NotificarDisponibilidad()`; evento `CanExecuteChanged`.
- Entradas/salidas: acción obligatoria, predicado opcional y parámetro que puede ser `Nothing`; `CanExecute` devuelve el predicado o `True` si no existe; `Execute` no devuelve valor; la notificación genera el evento.
- Errores: constructor rechaza acción nula mediante `ArgumentNullException`; las excepciones de los delegados no se capturan aquí.
- Permisos: no consulta seguridad. **`Execute` no comprueba `CanExecute`**; el consumidor y el servicio funcional deben aplicar las reglas que correspondan. No usar deshabilitación visual como barrera de autorización.
- Efectos/contexto: ejecuta el delegado y publica un evento; cualquier escritura procede del delegado, no de esta clase. No cambia automáticamente al hilo de UI ni implementa cancelación o ejecución asíncrona.

### 3.4 Mensajes y confirmaciones

- Estado: **implementada una referencia WinForms; propuesta la capacidad común WPF; pendiente su API**.
- Referencia real: clase `Friend ServicioMensajesAplicacion`, en [ServicioMensajesAplicacion.vb](../../src/ProConta.Facturacion.App/Mensajes/ServicioMensajesAplicacion.vb), basada en Krypton y `IWin32Window`. No es API pública exportable de UI-BASE.
- Métodos examinados: `MostrarErrorContexto(propietario As IWin32Window, excepcion As Exception)` y `ConfirmarBorradoConfiguracion(propietario As IWin32Window, grupo As String) As Boolean`, ambos `Friend Shared`.
- Entradas/salidas: propietario y excepción → diálogo sin retorno; propietario y nombre de catálogo → `True` solo si el diálogo devuelve `Yes`.
- Errores: el código examinado accede a `excepcion.Message` sin validación explícita de nulo ni traducción común; incorpora detalle técnico en el cuerpo. Esto no acredita cumplimiento completo de la [guía vigente](../MENSAJES.md).
- Permisos/efectos/contexto: no verifica permisos ni borra datos; abre un diálogo modal en entorno WinForms. Una confirmación positiva sigue necesitando autorización y validación del servicio funcional.
- Contrato WPF propuesto, sin nombres de interfaz inventados: recibir operación/registro, explicación funcional, acciones posibles y detalle técnico separado; devolver la acción elegida. El módulo funcional aporta hechos, consecuencias y acciones válidas. UI-BASE presenta; no afirma ausencia de cambios parciales sin evidencia del servicio.

### 3.5 Presentación de validaciones

- Estado: **propuesta; API y adaptación común pendientes**.
- Entradas propuestas: incidencias producidas por el módulo funcional, identificación del campo/registro, severidad y acciones disponibles. Salida propuesta: errores visibles y navegación al campo afectado, sin reinterpretar reglas fiscales o contables.
- Interfaz/método/código común: no identificados en esta revisión. Errores y cancelación: pendientes de contrato; no inventar una validación satisfactoria si falla su obtención.
- Permisos: los mantiene el servicio funcional. Efectos previstos: foco y estado visual, sin escrituras. Contexto: formulario activo y resultado de validación correspondiente a sus datos actuales.

### 3.6 Accesibilidad y ToolTips

- Estado: **pendiente de auditoría y contrato común**. Existen estados visuales de foco/deshabilitado en los estilos examinados, insuficientes para certificar accesibilidad del conjunto.
- Interfaz/método común: no identificado. Entradas previstas: propósito, formato esperado, acciones, consecuencias y estado de cada control; salidas previstas: ayuda contextual y metadatos accesibles coherentes.
- Debe cumplirse la obligación vigente de ToolTips descriptivos y actualizados. Quedan por verificar navegación por teclado, orden y visibilidad del foco, nombres accesibles, escalado y contraste, incluidas ayudas para controles deshabilitados.
- Errores: política ante ayuda ausente o desactualizada pendiente. Permisos: no autoriza ni expone datos restringidos por sí misma. Efectos previstos: solo presentación; contexto requerido: control y estado funcional aportado por el consumidor.

## 4. Datos, propiedad y escritura

| Datos | Dueño funcional / escritor | Papel de UI-BASE |
|---|---|---|
| Recursos, estilos y contratos visuales comunes futuros | UI-BASE cuando se acuerde su ubicación; herramientas de generación o desarrollo | Mantener presentación y compatibilidad; sin tablas propias identificadas |
| `.leoc`, manifiesto y XAML del vertical existente | Vertical Facturas WPF; regeneración LEOC escribe el artefacto | Evaluar reutilización; esta ficha no transfiere su propiedad |
| Facturas, terceros y catálogos | Sus módulos funcionales y servicios/repositorios autorizados | Recibir proyecciones para presentar; no escribir sus tablas |
| Configuración de ubicación local | CFG-GLOBAL según su contrato | Consumir únicamente lo necesario a través del anfitrión; no resolver conexiones |
| Parámetros editados por mantenimientos | Dueño funcional de cada parámetro; CFG-MANT presenta/coordina conforme a su contrato | Aportar controles; mantenimiento genérico no equivale a propiedad de negocio |
| Sesión, permisos y contexto autorizado | SEG-ACCESO y contratos funcionales aplicables; composición a cargo de UI-PRINCIPAL | Presentar el estado recibido; no autenticar, conceder permisos ni elegir contexto |
| Mensajes y errores | Módulo originador aporta hechos y causa; infraestructura de diagnóstico por concretar | Presentación y acciones explícitas; política de almacenamiento/log pendiente |

No se declara ninguna tabla Access propia ni acceso directo a tablas consumidas. Cultura, formatos y preferencias visuales persistidas todavía no tienen contrato común verificado; no se crea un almacén alternativo.

## 5. Dependencias e integración

- UI-PRINCIPAL compone la aplicación, establece propietarios de ventanas y conecta servicios/contexto; UI-BASE proporciona infraestructura visual. Los módulos son pares; Orquestador mantiene la coordinación operativa.
- SEG-ACCESO conserva autoridad sobre sesión/permisos. UI-BASE no deduce instante de autenticación, zona horaria o validez de sesión a partir de cuándo se abrió una ventana. El comportamiento sin sesión y al cambiar empresa depende del contrato de SEG-ACCESO y de su consumo por UI-PRINCIPAL; queda pendiente contrastarlo, sin presuponer consenso.
- Las pantallas funcionales suministran ViewModels, validaciones y comandos de negocio. Deshabilitar/ocultar controles refleja decisiones recibidas, nunca sustituye la comprobación funcional.
- WPF/LEOC existente es la primera referencia técnica: [contrato del vertical](../../../ProConta.WpfFacturas/docs/WPF_FACTURAS.md). Otras implementaciones WPF, como DetalleFactura y Configuración, están identificadas en el registro pero no auditadas en profundidad en esta entrega.
- No se acuerdan aún nombre de ensamblado común, referencias de proyecto, claves globales ni política de versiones. Deben evitarse dependencias de la futura base visual hacia módulos funcionales.

## 6. Ejemplo mínimo de consumo

Ejemplo ilustrativo con **API real existente** de la rama WPF indicada, no una nueva API de UI-BASE. Requiere referenciar ese proyecto; los delegados locales solo cambian una variable para evitar atribuir operaciones de negocio al comando.

```vb
Dim textoFiltro As String = "pendiente"
Dim limpiar As New ProConta.Facturacion.UI.Wpf.ComandoDelegado(
    Sub(parametro) textoFiltro = String.Empty,
    Function(parametro) textoFiltro.Length > 0)

If limpiar.CanExecute(Nothing) Then
    limpiar.Execute(Nothing)
    limpiar.NotificarDisponibilidad()
End If
```

Estado actualizado: los ejemplos reales de importación de recursos UI-BASE están en su [README](../../tools/ProConta.UI.Base/README.md). La integración futura de mensajes/validaciones seguirá obteniendo permisos y validaciones de los servicios funcionales; el anfitrión aporta contexto y UI-BASE presenta el resultado.

## 7. Evidencia y límites de verificación

- Esta entrega verifica por lectura las ramas/HEAD, el código y los enlaces usados, y revisa la nueva ficha. No ejecuta aplicación, generación LEOC, compilación, pruebas ni operaciones sobre bases.
- Hay código de pruebas en [ColoresEstadoFacturacionWpfTests.vb](../../../ProConta.WpfFacturas/tests/ProConta.Facturacion.Tests/ColoresEstadoFacturacionWpfTests.vb) y [VistaFacturasWpfViewModelTests.vb](../../../ProConta.WpfFacturas/tests/ProConta.Facturacion.Tests/VistaFacturasWpfViewModelTests.vb). Se localizaron comprobaciones de recursos de tema, ventana/layout y ViewModel. **Su existencia no demuestra ejecución satisfactoria**: no se ha verificado un resultado previo de ejecución en esta revisión.
- El proyecto WPF declara `net10.0-windows` y `UseWPF`; su `Directory.Build.props` declara x64 y opciones VB estrictas. Esto verifica configuración fuente, no un binario.
- Código implementado en `codex/wpf-facturas` no implica integración en `codex/integracion-proconta-20260822`, ni en la nueva aplicación WPF. No se ha comprobado una integración de UI-BASE con sus futuros consumidores.
- Sin certificación visual, accesibilidad, rendimiento del componente común ni compatibilidad completa de LEOC. No se ha auditado el repositorio externo LEOC.

## 8. Decisiones abiertas y próximos pasos

1. Inventariar recursos/controles reutilizables de los demás verticales WPF y contrastarlos con LEOC antes de proponer extracciones o sustituciones.
2. Acordar con el usuario las propuestas visuales aceptadas; mantener separadas propuesta, implementación y validación.
3. Concretar la biblioteca común y su contrato de recursos, mensajes, validaciones, accesibilidad y ToolTips, sin trasladar reglas de negocio.
4. Contrastar con UI-PRINCIPAL y SEG-ACCESO los límites de sesión/contexto, cambios de empresa y presentación de permisos. Registrar discrepancias explícitas cuando se identifiquen.
5. Resolver cómo adaptar la referencia WinForms de mensajes a WPF y cómo separar detalle técnico, hechos confirmados y acciones específicas. La implementación histórica no se considera ya conforme por estar centralizada.
6. Elegir un primer consumidor y pruebas de integración cuando se autorice implementación; la emisión VeriFactu sigue siendo prioridad del roadmap.

Esta ficha se actualizará cuando cambie el contrato público. La publicación inicial no cierra la normalización ni bloquea la definición posterior de requisitos/UI con el usuario. En este encargo se modifica únicamente esta ficha provisional; no se actualizan los índices centrales ni se envían mensajes a otros hilos.
