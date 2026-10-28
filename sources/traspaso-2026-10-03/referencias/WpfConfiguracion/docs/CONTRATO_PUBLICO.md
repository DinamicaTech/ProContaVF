# Contrato público — CFG-MANT

## 1. Identidad y trazabilidad

| Dato | Valor verificado |
|---|---|
| Clave | `CFG-MANT` |
| Módulo | Mantenimiento y shell WPF de Configuración |
| Hilo responsable | `01a02903-4be4-74f2-afdf-a8292b08b40b` |
| Responsable funcional | Equipo de Configuración WPF de ProConta |
| Worktree | `C:\Docs\VeriFactu\ProConta.WpfConfiguracion` |
| Rama | `codex/wpf-configuracion` |
| Commit inspeccionado | `adf0d882db3fabd1ce9775d86a1caf3f9c72d1c7` |
| Fecha de verificación | 2026-09-30 |

Esta ficha describe el estado de la rama del módulo. Su presencia no implica que el commit esté integrado en la rama común de entrega.

## 2. Responsabilidad y exclusiones

CFG-MANT aporta la infraestructura extensible de mantenimiento de configuración: registro y búsqueda de proveedores, metadatos de grupos y campos, shell WPF dinámico, edición de registros únicos y colecciones 1:n, validación, seguimiento de cambios y operaciones de carga, guardado y eliminación.

No es propietario funcional de las tablas que presenta. La aparición de una tabla en el catálogo significa que existe un mantenimiento genérico, no que Configuración defina sus reglas de negocio. Tampoco sustituye servicios tipados cuando una configuración requiere efectos laterales, autorizaciones de negocio o invariantes propias.

Quedan fuera de este contrato la emisión de facturas, VeriFactu, seguridad de sesión y la propiedad de maestros contables. La pantalla WinForms anterior se conserva durante la migración.

## 3. Capacidades públicas

### Implementadas

- `RegistroProveedoresConfiguracion.Registrar`, `ObtenerTodos`, `Obtener` y `Buscar`: registra proveedores por identificador estable, rechaza duplicados sin distinguir mayúsculas, ordena y filtra por texto y visibilidad.
- `IProveedorConfiguracion`: expone `Definicion`, `Cargar`, `CargarOpciones`, `Validar`, `Guardar` y `Eliminar`.
- `DefinicionProveedorConfiguracion`: publica categoría, título, descripción, orden, cardinalidad, campos, permisos y editor preferido.
- `ServicioConfiguracion`: compone proveedores estándar, busca grupos y delega carga, opciones, validación, guardado y eliminación.
- `IRepositorioConfiguracion` y `RepositorioConfiguracionAccess`: describen esquema, cargan registros/opciones/alias y ejecutan altas, modificaciones y bajas parametrizadas sobre Access.
- `ProveedorUbicacionDatosLocal`: publica `LOCAL.UBICACION_DATOS` como registro único almacenado localmente, sin materializarlo en Access.
- El shell WPF selecciona plantilla por tipo de campo y admite texto, memo, entero, decimal, booleano, fecha/hora y referencia; `TipoEditorConfiguracion` deja puntos explícitos para ruta, secreto y editores futuros.

Código contractual: `Aplicación/Configuracion/ContratosConfiguracion.vb`, `Aplicación/Configuracion/ProveedoresConfiguracion.vb`, `Aplicación/Configuracion/ServicioConfiguracion.vb`, `Persistencia.Access/Configuracion/RepositorioConfiguracionAccess.vb` y `UI WPF/Configuracion/ProveedorUbicacionDatosLocal.vb`.

Entradas: identificador de proveedor, texto de búsqueda, diccionarios de valores, clave de registro y contexto de opciones. Salidas: definiciones, registros, opciones, resultados de validación y persistencia. Los errores incluyen proveedor inexistente o duplicado, validación fallida, eliminación no admitida, clave/concurrencia no válida y errores de almacenamiento. Access usa consultas parametrizadas y transacción en guardado; OLE DB enlaza los parámetros por posición.

Los permisos `PuedeLeer`, `PuedeCrear`, `PuedeModificar` y `PuedeEliminar` son metadatos consumidos por el registro y la UI. En el estado inspeccionado no están conectados a una autoridad externa de sesión; no deben interpretarse como una frontera de seguridad.

### Propuestas, no contrato implementado

- Proveedores tipados de cada módulo para configuraciones con reglas o efectos laterales.
- Editores especializados adicionales, como secreto o selectores complejos, sobre los puntos de extensión existentes.
- Integración de los permisos declarativos con el subsistema que finalmente sea propietario de identidad, roles y sesión.

### Pendientes

- Asignar formalmente propietario funcional a cada maestro mostrado.
- Sustituir donde corresponda el mantenimiento genérico por servicios tipados del módulo propietario.
- Definir el contexto compartido de empresa, ejercicio, usuario y permisos sin acoplarlo al shell.

## 4. Datos mostrados, consumidos y propiedad

| Identificador | Origen actual | Cardinalidad | Relación de CFG-MANT con el dato |
|---|---|---:|---|
| `GENERAL` | `Configuracion` | Registro único | Mantenimiento genérico; propiedad funcional por asignar |
| `CORREO` | `ConfiguracionCorreo` | Colección | Mantenimiento genérico; propiedad funcional por asignar |
| `VERIFACTU` | `VeriFactuConfiguracion` | Colección | Mantenimiento genérico; la regla de negocio corresponde al módulo VeriFactu/facturación |
| `SOCIEDADES` | `Sociedades` | Colección | Maestro mostrado; CFG-MANT no reclama propiedad funcional |
| `EJERCICIOS` | `SociedadesEjercicios` | Colección | Maestro mostrado; CFG-MANT no reclama propiedad funcional |
| `SERIES` | `SeriesFacturacion` | Colección | Maestro mostrado; reglas de facturación fuera de CFG-MANT |
| `FORMAS_PAGO` | `FormasPago` | Colección | Maestro mostrado; propiedad funcional por asignar |
| `TIPOS_IVA` | `TiposDeIVA` | Colección | Maestro mostrado; reglas fiscales fuera de CFG-MANT |
| `TIPOS_FACTURA` | `TiposFactura` | Colección | Maestro mostrado; reglas de facturación fuera de CFG-MANT |
| `TIPOS_CONCEPTO` | `TiposConcepto` | Colección | Maestro mostrado; propiedad funcional por asignar |
| `ESTADOS_FACTURA` | `EstadosFactura` | Colección | Maestro mostrado; reglas de ciclo de factura fuera de CFG-MANT |
| `LOCAL.UBICACION_DATOS` | Archivo local `proconta.config.json` | Registro único | Proveedor local diferenciado; no se persiste en Access |

El repositorio genérico puede escribir las tablas catalogadas, pero esa capacidad técnica no transfiere propiedad funcional. El módulo propietario debe aportar un proveedor/servicio tipado cuando existan invariantes, efectos laterales o permisos específicos.

Referencias consumidas actualmente: sociedad para correo, VeriFactu y ejercicios; sociedad-ejercicio para series. Las opciones se obtienen mediante el repositorio, sin pasar controles WPF al dominio o a persistencia.

## 5. Dependencias e integración

- Access mediante ACE/OLE DB para catálogos simples.
- Contratos de Aplicación para aislar shell, proveedores y persistencia.
- Infraestructura WPF/LEOC común y tema seleccionado por `PROCONTA_LEOC_THEME`; LEOC describe el shell estable y las plantillas materializan el cuerpo dinámico.
- `ResolutorUbicacionDatos` para la configuración local global.
- Los módulos consumidores registran proveedores; no modifican directamente el shell.

La distinción acordada es obligatoria: la ubicación global de datos es configuración local del puesto y se almacena fuera de Access; los parámetros funcionales pertenecen a sus módulos y pueden residir en Access o en servicios tipados según su contrato.

## 6. Ejemplo mínimo de consumo

```vb
Dim registro As New RegistroProveedoresConfiguracion()
registro.Registrar(miProveedor)

Dim visibles = registro.Buscar("correo")
Dim proveedor = registro.Obtener("CORREO")
Dim filas = proveedor.Cargar()
Dim fila = filas(0)
Dim errores = proveedor.Validar(fila)

If errores.Count = 0 Then
    fila = proveedor.Guardar(fila)
End If
```

El consumidor debe proporcionar un `IProveedorConfiguracion` con identificador estable y decidir si delega en `IRepositorioConfiguracion` o en un servicio tipado propio.

## 7. Pruebas, evidencia y límites

La rama contiene pruebas de registro y unicidad, orden y búsqueda, selección de plantilla por tipo, carga WPF en STA, estado modificado, cancelar/guardar, CRUD 1:n y permisos en `ProConta.Facturacion.Tests/ConfiguracionWpfTests.vb`. `ServicioConfiguracionTests.vb` cubre alias, descripción de campos y lectura de esquema sobre una copia de base de prueba.

Evidencia histórica de esta rama: compilación `Debug/x64` satisfactoria y 9/9 pruebas específicas de Configuración superadas tras corregir la carga de recursos WPF. La suite completa registró fallos ajenos a este módulo en migración de balances y Norma 43. Esta ficha no presenta esa evidencia histórica como una nueva ejecución ni como integración en la rama común.

Límites conocidos: la generación LEOC externa falló en la verificación anterior y se empleó el XAML versionado; los permisos siguen siendo declarativos; el patrón de clave `Max + 1` del repositorio genérico no es apropiado para concurrencia elevada; no se modificaron bases originales.

## 8. Decisiones abiertas, solapamientos y siguientes pasos

- Acordar qué módulo es propietario funcional y escritor autorizado de cada maestro. CFG-MANT solo ofrece mantenimiento genérico.
- Resolver con Seguridad el contrato de identidad/roles/permisos. La UI puede ocultar o deshabilitar acciones, pero eso no sustituye autorización en la capa propietaria.
- Resolver con UI-BASE qué parte es infraestructura común ejecutable (tema, mensajes, hosting WPF/WinForms, LEOC) y qué parte son propuestas visuales. CFG-MANT consume la infraestructura; no reclama su propiedad.
- Resolver el contexto empresa/ejercicio entre módulos. No hay consenso documentado suficiente para inventar una autoridad o precedencia.
- Incorporar proveedores tipados para reglas de facturación, fiscalidad, correo o VeriFactu antes de considerar el catálogo genérico como API definitiva de esos dominios.
- Revalidar compilación completa y pruebas en la rama de integración cuando se consuma este contrato; una rama funcional compilada no equivale a integración verificada.

Esta ficha debe actualizarse cuando cambien los contratos públicos, la propiedad de datos, los permisos efectivos o la evidencia de integración.
