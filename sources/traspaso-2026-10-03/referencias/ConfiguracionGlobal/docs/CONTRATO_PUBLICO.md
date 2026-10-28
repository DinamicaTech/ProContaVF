# CFG-GLOBAL — Ficha de responsabilidades y contrato público

## 1. Identidad

| Campo | Valor comprobado |
|---|---|
| Clave estable | `CFG-GLOBAL` |
| Título del hilo | `CFG-GLOBAL · Configuración global de datos` |
| Id del hilo | `01a07b3f-f0f5-75e2-988d-51543b318131` |
| Responsable funcional | Hilo CFG-GLOBAL; coordinación del Orquestador y límites globales del Auditor |
| Worktree | `C:\Docs\VeriFactu\ProConta.ConfiguracionGlobal` |
| Rama | `codex/configuracion-global` |
| Commit base del contrato | `482538167213e98e33a457ad1c4342cadc148e04` |
| Fecha | 01/10/2026 |
| Estado Git inicial | Limpio; Git necesitó `safe.directory` efímero por el cambio de identidad del usuario Windows |

Esta ficha concreta la entrada de CFG-GLOBAL en el [mapa central](C:/Docs/VeriFactu/ProConta.Facturacion/docs/MAPA_PROYECTO.md). El contrato detallado y la precedencia permanecen en [CONFIGURACION_DATOS.md](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/docs/CONFIGURACION_DATOS.md); no se duplican aquí.

## 2. Responsabilidad y exclusiones

CFG-GLOBAL resuelve una única ubicación local por proceso y puesto: el directorio absoluto compartido y la ruta del archivo Access fijado por la composición (`ProcontaVF.accdb` de forma predeterminada). Normaliza rutas, comprueba carpeta y fichero, verifica lectura/escritura, devuelve errores estructurados y conserva atómicamente la última selección válida.

Quedan fuera:

- Parámetros funcionales de sociedades, ejercicios, facturación, contabilidad, correo, seguridad u otros módulos.
- Propiedad de tablas Access: localizar la base no convierte a CFG-GLOBAL en dueño de su contenido.
- Mantenimiento genérico de catálogos y auxiliares, asignado a `CFG-MANT`.
- Autenticación, permisos y sesión, asignados a `SEG-ACCESO`.
- Composición y navegación de la nueva aplicación, asignadas a `UI-PRINCIPAL`.
- Esquema físico y migraciones, responsabilidad de `INF-ESQ` y los dueños funcionales.
- Crear una carpeta de datos o base ante una ruta errónea.

Puede crear `%LocalAppData%\ProConta` para la preferencia del puesto. Eso no crea ni modifica datos de negocio.

## 3. Capacidades ofrecidas

### 3.1 Ubicación resuelta — implementada en la rama

- **Interfaz real:** `IUbicacionDatosAplicacion`.
- **Miembros:** `DirectorioDatos` y `RutaBaseDatos`, de solo lectura.
- **Código:** [UbicacionDatosAplicacion.vb](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/src/ProConta.Configuracion/UbicacionDatosAplicacion.vb).
- **Entrada:** ninguna; representa el resultado validado.
- **Salida:** rutas absolutas coherentes.
- **Errores/permisos/efectos:** ninguno al leer las propiedades.
- **Contexto:** ubicación validada o doble de prueba.

### 3.2 Resolución por precedencia — implementada en la rama

- **Método:** `ResolutorUbicacionDatos.Resolver(args As String()) As ResultadoUbicacionDatos`.
- **Entradas:** argumentos, `PROCONTA_DIRECTORIO_DATOS`, JSON local, histórico y de última ubicación. Véase la [precedencia](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/docs/CONFIGURACION_DATOS.md#precedencia).
- **Salida:** `ResultadoUbicacionDatos` con `Ubicacion`, `CodigoError`, `Detalle`, `Origen` y `Resuelto`.
- **Errores:** `NoConfigurada`, `ConfiguracionInvalida`, `CarpetaInexistente`, `PermisosInsuficientes` y `FicheroAusente`.
- **Permisos:** enumerar directorio y abrir brevemente la base para lectura/escritura.
- **Efectos:** lectura de entorno, JSON y metadatos; no escribe ni abre UI.
- **Contexto:** ninguno de sesión, empresa o ejercicio; apto para procesos desatendidos.

Una fuente configurada pero inválida produce su error; no se oculta usando otra de menor prioridad.

El último parámetro opcional del constructor, `nombreBaseDatosEsperado`, permite fijar otro nombre `.accdb` sin romper consumidores existentes. `NombreBaseDatosEsperado` expone el valor efectivo. Dos argumentos explícitos simultáneos deben señalar la misma ubicación.

### 3.3 Validación de selección — implementada en la rama

- **Método:** `ValidarSeleccion(directorio As String) As ResultadoUbicacionDatos`.
- **Entrada/salida:** ruta absoluta; ubicación con origen `SeleccionInteractiva` o error.
- **Errores/permisos:** los de resolución del directorio.
- **Efectos:** comprobaciones, sin persistencia.
- **Contexto:** ninguno de negocio o sesión.

### 3.4 Última ubicación — implementada en la rama

- **Método:** `GuardarUltimaUbicacionValida(ubicacion As IUbicacionDatosAplicacion)`.
- **Entrada:** ubicación no nula, revalidada.
- **Salida:** ninguna.
- **Errores:** `ArgumentNullException`, `ArgumentException`, E/S y permisos.
- **Permisos:** escritura en `%LocalAppData%\ProConta`.
- **Efectos:** sustituye atómicamente `ultima-ubicacion-datos.json`; solo guarda `DirectorioDatos`.
- **Contexto:** puesto Windows; no requiere sesión.

### 3.5 Abstracción del sistema — implementada para composición y pruebas

- **Interfaz/implementación:** `ISistemaUbicacionDatos` / `SistemaUbicacionDatos`.
- **Operaciones:** variable, existencia, acceso, lectura y escritura atómica.
- **Finalidad:** pruebas deterministas y una sola implementación del entorno.
- **Exclusión:** no es una API general de sistema de archivos.

### 3.6 Asistente WPF — implementado en la rama, integración no certificada

- **Clase:** `AsistenteUbicacionDatosWpf`.
- **Código:** [proyecto WPF](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/src/ProConta.Configuracion.UI.Wpf) y `AsistenteUbicacionDatosWpf.leoc`.
- **Entrada/salida:** resolutor y fallo previo; `UbicacionSeleccionada` al aceptar.
- **Errores:** se presentan con acción correctiva.
- **Permisos/efectos:** usa validación y guardado; no modifica Access.
- **Contexto:** proceso interactivo STA; `Ctrl+Mayús+D` intenta abrir el LEOC.
- **Límite:** `UI-PRINCIPAL` presenta y orquesta, pero no se apropia de la resolución.

### 3.7 Parámetros funcionales genéricos — no ofrecido

Corresponden a `CFG-MANT` y a los módulos propietarios. CFG-GLOBAL no publica bolsa de claves, repositorio de parámetros de negocio ni acceso genérico a Access.

## 4. Datos propios y consumidos

| Dato | Uso | Dueño funcional | Quién escribe | Observación |
|---|---|---|---|---|
| `%LocalAppData%\ProConta\proconta.config.json` | Configuración consumida | CFG-GLOBAL para `DirectorioDatos` | Provisión administrativa; `Resolver` no escribe | Admite temporalmente `BaseDatos`. |
| JSON junto al ejecutable | Compatibilidad consumida | CFG-GLOBAL durante transición | Instalación/administración | Fuente histórica. |
| `ultima-ubicacion-datos.json` | Preferencia propia | CFG-GLOBAL | `GuardarUltimaUbicacionValida` | Atómico; solo ruta. |
| `PROCONTA_DIRECTORIO_DATOS` | Entrada consumida | Despliegue/administración | Proceso/SO | CFG-GLOBAL valida. |
| Archivo `.accdb` esperado | Recurso consumido | Dueños de tablas; `INF-ESQ` para estructura | CFG-GLOBAL no escribe | `ProcontaVF.accdb` por defecto; la composición puede fijar `ProConta.accdb`. Solo existencia y apertura breve. |
| Parámetros funcionales Access | Fuera de alcance | Módulo correspondiente; CFG-MANT puede presentarlos | Servicio/repositorio propietario | Mostrar no transfiere propiedad. |

CFG-GLOBAL no posee tablas de negocio ni migraciones Access.

## 5. Dependencias e integración

- **UI-PRINCIPAL:** resuelve al arrancar, decide contexto interactivo y entrega una instancia. Presenta/orquesta; no redefine reglas.
- **SEG-ACCESO:** consume ubicación para repositorios. CFG-GLOBAL no conoce operador, permisos, empresa ni ejercicio.
- **CFG-MANT:** puede consumir `RutaBaseDatos`; definiciones, permisos y escrituras son ajenos.
- **INF-ESQ:** consume `RutaBaseDatos` y decide compatibilidad estructural; CFG-GLOBAL no migra.
- **Procesos desatendidos:** consumen el núcleo sin WPF; propagan error y código de salida.
- **WinForms existente:** esta rama consume desde `Program.vb` y `ConfiguracionInicio.vb`; no prueba integración en la rama principal.

## 6. Ejemplo mínimo

Consumo real reducido:

```vbnet
Dim resolutor As New ResolutorUbicacionDatos()
Dim resultado = resolutor.Resolver(args)
If Not resultado.Resuelto Then
    Console.Error.WriteLine(resultado.Detalle)
    Environment.ExitCode = 2
    Return
End If
Dim ubicacion As IUbicacionDatosAplicacion = resultado.Ubicacion
```

Integración exacta para el ejecutable que debe usar la copia
`C:\Docs\VeriFactu\ProConta.accdb`:

```vbnet
Dim resolutor As New ResolutorUbicacionDatos(
    nombreBaseDatosEsperado:="ProConta.accdb")
Dim resultado = resolutor.Resolver({
    "--base-datos",
    "C:\Docs\VeriFactu\ProConta.accdb"
})
```

Esta composición no busca, copia ni sustituye la base. Si `ProConta.accdb`
falta, devuelve `FicheroAusente` aunque `ProcontaVF.accdb` exista en la misma
carpeta. La persistencia guarda el directorio y el nombre sigue fijado por la
composición.

Pseudocódigo propuesto para módulos no adaptados:

```vbnet
Public Sub New(ubicacion As IUbicacionDatosAplicacion)
    _directorioDatos = ubicacion.DirectorioDatos
    _rutaBaseDatos = ubicacion.RutaBaseDatos
End Sub
```

Ningún consumidor debe releer argumentos, entorno o JSON para crear otra fuente de verdad.

## 7. Evidencia y límites

Evidencia registrada en la rama:

- `101daca`: resolución, errores, persistencia y pruebas.
- `a3d7bb5`: asistente WPF/LEOC e integración en el arranque existente.
- Solución completa Debug/x64: 0 errores.
- `ResolutorUbicacionDatosTests`: 18/18 superadas, incluidas selección explícita, coexistencia, ausencia, contradicción y persistencia.
- VeriFactu inicializado y restaurado en aquella verificación.
- Árbol limpio al comenzar esta ficha.

Límites:

- La extensión del nombre esperado se compiló con la solución completa en Debug/x64.
- Los commits están en `codex/configuracion-global`; no se afirma integración en la rama principal.
- No se ha verificado aún el consumo común de los seis módulos.
- El asistente no normaliza por sí solo el nuevo arranque.
- Permanecen avisos preexistentes de VeriFactu y MSTest.

## 8. Decisiones abiertas y próximos pasos

1. **Composición:** UI-PRINCIPAL debe conservar una instancia del contrato sin mover reglas a UI.
2. **Asistente:** decidir si permanece aquí o se presenta mediante UI-BASE; mover presentación no mueve contrato.
3. **Compatibilidad:** fijar retirada de `--base-datos`, JSON `BaseDatos` y JSON junto al ejecutable.
4. **Administración:** decidir si el JSON local sigue desplegado externamente o necesita escritura explícita.
5. **Permisos:** confirmar si algún consumidor debe soportar solo lectura; hoy se exige lectura/escritura.
6. **INF-ESQ:** diferenciar «ruta válida» de «esquema compatible».
7. **CFG-MANT:** los catálogos y parámetros permanecen allí y en sus dueños; el nombre «configuración» no amplía CFG-GLOBAL.
8. **Normalización:** adaptar consumidores uno a uno y registrar evidencia. Esta ficha inicia, no cierra, la normalización.

Actualizar esta ficha cuando cambien el contrato, precedencia, persistencia o fronteras con UI-PRINCIPAL, CFG-MANT, SEG-ACCESO o INF-ESQ.
