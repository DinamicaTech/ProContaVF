# Contenedor WPF: primer incremento

Autorizado por el usuario el 30/09/2026 tras validar el login real .5. Referencia: https://www.dinamica.tech/Contenedor/new_proconta-contenedor . Entrega .6 implementada y comprobada; pendiente de validación interactiva del usuario.

## Alcance

- Cabecera con operador, sociedad y ejercicio reales; controles de ventana integrados.
- Navegación lateral plegable, pestañas con identidad estable, activación sin duplicados, listado de abiertas y cierre individual.
- Inicio, accesos a pantallas disponibles, recientes de esta sesión, búsqueda «Ir a» de pantallas/acciones y ampliación del área de trabajo.
- Cambio de entorno autorizado mediante SEG-ACCESO, cambio de operador y cierre de sesión. La selección cancelada conserva contexto y pantallas actuales.
- No mostrar datos de una sociedad bajo la cabecera de otra: resolver cambios pendientes antes de sustituir contexto y cerrar vistas anteriores.
- Contrato de pantalla para declarar pendientes y guardar/descartar/cancelar. La pantalla funcional guarda sus datos; un error o cancelación impide completar la transición. No se promete atomicidad de guardados entre módulos.
- Ejercicios cerrados accesibles. Mostrar «Cerrado», sin imponer «solo consulta» a toda la aplicación; restricciones en cada módulo. Dato ausente: estado sin informar.
- Los módulos aún no integrados se identifican como no disponibles. No se implementan reglas de Terceros, facturas o contabilidad para simular funcionalidad.

## Responsabilidades y protección del trabajo

- UI-PRINCIPAL: consumidor, ciclo de ventanas, sesión, navegación, integración y pruebas; `tools/ProConta.Acceso.Prototipo`, `tools/ProConta.Acceso.Comprobacion` y documentación de integración.
- UI-BASE: recursos compartidos `tools/ProConta.UI.Base`, con diccionario de contenedor y contrato de claves. No cambia recursos globales que afecten al login aprobado.
- Auditor: revisión cruzada, seguimiento de contratos, conservación de cambios ajenos y consolidación selectiva en Git al acabar ambos trabajos.
- SEG-ACCESO sigue siendo autoridad de autenticación y autorización. No se crean permisos ni roles ficticios desde la interfaz.
- Se conserva el worktree central y la aplicación anterior. No DDL ni pruebas de escritura sobre la BD original; usar copias privadas ignoradas.

## Criterios de entrega

Versión posterior a .5 (primera prevista .6), Debug/x64, ruta permanente, metadatos verificados y versión visible en el contenedor. Compilar solución y consumidor, ejecutar regresión login y pruebas de pestañas, pendientes, cambio de contexto y cierre de sesión. Documentar límites y comandos en `docs/INTEGRACION_CONTENEDOR_WPF.md`. Validación visual manual del usuario tras entrega.

## Cierre técnico

- Entrega `1.21.0-integracion-proconta-20260822.6`, Debug/x64. Binario: `C:/Docs/VeriFactu/ProConta.Facturacion/tools/ProConta.Acceso.Prototipo/bin/x64/Debug/net10.0-windows/win-x64/ProConta.Acceso.Prototipo.exe`.
- UI-BASE: diccionario con 22 claves explícitas, carga y convivencia con login comprobadas.
- UI-PRINCIPAL: 57 comprobaciones superadas, salida 0; regresión ACE sobre copia privada. Solución completa sin errores, dos advertencias preexistentes WindowsBase/WebView2.
- Auditor revisó código de transición y los renders sintéticos a 1380×860 y 1000×640. Se corrigieron limpieza fallida después de confirmar entorno, indicador de navegación, fondo, identificación del buscador e iconos del lateral compacto.
- Consolidación selectiva en Git de fuentes y documentación de acceso/contenedor; se preservan cambios ajenos. No se incluyen bases, binarios ni imágenes temporales.
- Ningún módulo de negocio conectado todavía: el contrato de pendientes se ha probado con dobles. No hay transacción distribuida de guardados. Prueba nativa de ventanas/maximizado/DPI pendiente del usuario.
