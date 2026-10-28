# Directorio de contratos públicos de ProConta

Actualización: 30/09/2026. Primera publicación solicitada a los seis propietarios; pendiente de revisión cruzada por el Auditor. No certifica integración ni cierre funcional.

## Incrementos verificados

- Login WPF real .5: integrado y validado por el usuario. SEG-ACCESO publica la API vigente en [INTEGRACION_ACCESO_REAL.md](C:/Docs/VeriFactu/ProConta.Seguridad/docs/INTEGRACION_ACCESO_REAL.md), commit `b0face7`; la sesión proporciona `FechaHoraAutenticacion` como `DateTimeOffset`. Evidencia conjunta en [ACUERDOS_ACCESO_REAL.md](ACUERDOS_ACCESO_REAL.md).
- UI-BASE: biblioteca independiente disponible en `tools/ProConta.UI.Base`, con diccionarios separados `Recursos/Acceso.xaml` y `Recursos/Contenedor.xaml`. Claves y consumo en su [README](../tools/ProConta.UI.Base/README.md).
- Contenedor WPF: implementación autorizada en curso según [ACUERDOS_CONTENEDOR_WPF.md](ACUERDOS_CONTENEDOR_WPF.md). La aceptación del login no implica aceptación del contenedor ni de módulos de negocio aún no integrados.

## Ampliación del 01/10/2026

- DOC-GESTOR · Gestión documental: [contrato inicial](contratos/DOC-GESTOR.md).
  Hilo `01a0f6ee-afa9-7101-b599-97fa421a3359`; sin implementación acreditada.
- INF-COR · Gestión y seguimiento del correo:
  [compositor y lotes](C:/Docs/VeriFactu/ProConta.GestorCorreo/docs/CONTRATO_COMPOSITOR_Y_LOTES_CORREO.md)
  y [decisiones del usuario](ACUERDOS_CORREO_20261001.md).
- Los títulos normalizados de los hilos afectados hoy se sincronizan en
  [REGISTRO_HILOS.json](REGISTRO_HILOS.json). El prefijo identifica al módulo,
  no determina jerarquía ni certifica capacidades implementadas.

## Consulta

Buscar la capacidad en la ficha del módulo, verificar su estado y seguir el enlace al contrato/código. Una propuesta no es una API disponible. Los identificadores de hilos, ramas y rutas están en [REGISTRO_HILOS.json](REGISTRO_HILOS.json).

| Módulo | Responsabilidad de referencia | Ficha solicitada |
|---|---|---|
| UI-BASE | Recursos visuales y componentes comunes | [Contrato](C:/Docs/VeriFactu/ProConta.Facturacion/docs/contratos/UI-BASE.md) |
| CFG-GLOBAL | Ubicación de datos y configuración global | [Contrato](C:/Docs/VeriFactu/ProConta.ConfiguracionGlobal/docs/CONTRATO_PUBLICO.md) |
| SEG-ACCESO | Autenticación, permisos y sesión | [Contrato](C:/Docs/VeriFactu/ProConta.Seguridad/docs/CONTRATO_PUBLICO.md) |
| UI-PRINCIPAL | Composición, navegación y coordinación del entorno | [Contrato](C:/Docs/VeriFactu/ProConta.Facturacion/docs/contratos/UI-PRINCIPAL.md) |
| DAT-TERCEROS | Identidad y roles de terceros | [Contrato](C:/Docs/VeriFactu/ProConta.Terceros/docs/CONTRATO_PUBLICO.md) |
| CFG-MANT | Proveedores de mantenimiento genérico | [Contrato](C:/Docs/VeriFactu/ProConta.WpfConfiguracion/docs/CONTRATO_PUBLICO.md) |

Los enlaces son destinos encargados; pueden no existir mientras los propietarios preparan la primera publicación. UI-BASE y UI-PRINCIPAL publican provisionalmente aquí porque no tienen worktree propio asignado.

## Criterios de normalización progresiva

1. Identidad estable: prefijo, hilo vigente, rama y carpeta verificadas; referencias históricas diferenciadas.
2. Límite funcional: responsabilidad, exclusiones y decisiones abiertas. No cerrar requisitos que el usuario todavía está definiendo.
3. Contrato consumible: capacidades, interfaces reales, semántica, permisos, errores, efectos y ejemplos; estado por capacidad.
4. Propiedad de datos: propietario funcional por tabla, escritores y lectores; mostrar una tabla en CFG-MANT no transfiere su propiedad.
5. Integración: dependencias, composición y ciclo de vida de sesión/contexto; cambios de contrato con consumidores afectados y compatibilidad explícita.
6. Calidad comprobable: pruebas existentes, fecha/commit de evidencia, pendiente de integración y aceptación funcional/UI separadas.
7. Persistencia e implantación: contrato con ActualizadorEsquema y, cuando proceda, reglas de migración de datos para MigradorERP/ContaSol. Marcar no aplicable cuando corresponda.
8. Documentación coherente: instrucciones antiguas WinForms frente a WPF autorizado, rutas y contratos duplicados deben señalarse y corregirse en el alcance del propietario.

Estados separados: nombre normalizado, ficha publicada, revisión cruzada realizada, implementación verificada, integrada y aceptación del usuario. No reducirlos a un único «terminado».

## Cruces prioritarios a resolver

- SEG-ACCESO / UI-PRINCIPAL: autoridad de Seguridad sobre sesión y entorno frente a orquestación/presentación. API de login y fecha de autenticación ya integrada en .5; transición de entorno desde el contenedor en implementación.
- CFG-GLOBAL / CFG-MANT: resolución de datos y almacenamiento local frente a edición a través de proveedores.
- UI-BASE / hilo de diseño: componentes implementados frente a propuestas visuales.
- DAT-TERCEROS / CFG-MANT: reglas de negocio y escritura autorizada frente a presentación genérica.
- Todos / ActualizadorEsquema: versión compatible requerida sin DDL independiente en pantallas.

El inventario físico de tablas y el catálogo de métodos se consolidarán a partir de las fichas verificadas; no se asignan dueños definitivos por deducción del nombre de tabla. Auditor mantiene este índice y revisa cruces; cada módulo mantiene su ficha; Orquestador coordina la integración.
