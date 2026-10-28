# UI-PRINCIPAL — responsabilidades y contrato público

## Incremento vigente .7 — 01/10/2026

Composición conjunta con vistas Terceros/N43, consulta N43 real contextual y demostración sintética explícita. CFG-MANT excluido. Ficha Terceros real bloqueada por permisos no contratados y esquema ausente en la base actual. `RegistrarFicha` registra identidades dinámicas, `IPantallaConOperacion` espera lecturas antes de retirar contexto. 82 comprobaciones y metadatos .7 verificados. [Manifiesto vigente](../ENTREGA_CONTENEDOR_TERCEROS_N43_20261001.md). Los apartados siguientes describen la base .6, cuyo catálogo sin módulos queda sustituido por esta composición.

## 1. Estado y alcance de la base .6

Implementado en `tools/ProConta.Acceso.Prototipo`: acceso real y contenedor nativo WPF .6, VB.NET/.NET 10/x64. Rama `codex/integracion-proconta-20260822`, base `773d3e491687ffe72a3cd889566968e56461acf0`, cambios locales pendientes de consolidación por Auditor. Versión `1.21.0-integracion-proconta-20260822.6`. La autorización específica WPF rige este incremento; no transforma las pantallas WinForms existentes.

Contrato detallado, comandos, evidencia y límites: [INTEGRACION_CONTENEDOR_WPF.md](../INTEGRACION_CONTENEDOR_WPF.md). Procedencia CFG/SEG y acceso: [INTEGRACION_LOGIN_WPF.md](../INTEGRACION_LOGIN_WPF.md).

## 2. Responsabilidades y exclusiones

UI-PRINCIPAL compone el acceso, presenta contexto validado, coordina pestañas, navegación, selección provisional y resolución de ediciones antes de cambiar/cerrar. No autentica por reglas propias, no concede permisos, no reabre ejercicios, no ejecuta DDL y no guarda negocio de otros módulos. Los destinos aún sin integración muestran No disponible. No hay CRUD ni datos ficticios presentados como reales.

## 3. API pública vigente

| Tipo | API implementada / resultado |
|---|---|
| `ComposicionAcceso` | `CrearRecorrido(args)` real por defecto, demo explícita; `CrearServicio(args)` consume CFG y SEG |
| `RecorridoAccesoReal` | Secuencia de acceso; `CrearSesionTrabajo()` transfiere el mismo servicio/token/contexto completado |
| `SesionTrabajoReal : ISesionTrabajo` | `Contexto`, revalidar, listar sociedades/ejercicios, cambiar con `CompletarAcceso`, cerrar mediante Seguridad; consultas no persisten preferencias |
| `ContextoTrabajo` | Instantánea inmutable para UI, con identificadores, nombres, estado contable nullable y autenticación `DateTimeOffset` |
| `GestorContenedor` | Abrir/reactivar sin duplicar, cerrar pestaña, buscar destinos, cambiar contexto, cerrar sesión; colecciones de pantallas/recientes y exclusión de operaciones |
| `DestinoPantalla` | Clave, título, grupo, disponibilidad, fábrica y autorización explícita de apertura |
| `IPantallaTrabajo` / `IPantallaWpf` | Estado de edición, guardar, descartar, liberar; dependencia del contexto y vista WPF reutilizable |
| `VentanaContenedor` / `SelectorEntorno` | Presentación WPF; selector retorna IDs provisionales, nunca los convierte por sí mismo en contexto autorizado |

Las firmas y efectos se documentan en [ContratoContenedor.vb](../../tools/ProConta.Acceso.Prototipo/ContratoContenedor.vb), [GestorContenedor.vb](../../tools/ProConta.Acceso.Prototipo/GestorContenedor.vb) y [SesionTrabajoReal.vb](../../tools/ProConta.Acceso.Prototipo/SesionTrabajoReal.vb). El estado visual no es un token de autorización.

## 4. Datos propios y consumidos

No hay tablas propias. Pestañas, recientes y expansión de navegación viven en memoria. CFG-GLOBAL resuelve ubicación; SEG-ACCESO posee autenticación, sesión, autorización, auditoría y preferencias. El catálogo organizativo se consulta mediante el puerto de Seguridad; su propietario funcional sigue pendiente de ratificación. Cada módulo conserva la propiedad de sus datos y ediciones. ActualizadorEsquema conserva las migraciones.

## 5. Dependencias y puntos de integración

CFG y el subconjunto SEG están incorporados como fuentes con hashes/procedencia, sin modificar sus reglas, según el documento .5. UI-BASE aporta `Acceso.xaml` y `Contenedor.xaml` mediante referencia de proyecto. La nueva ventana importa los recursos del contenedor sin alterar los de login. Los módulos futuros registrarán fábrica y autorización, implementarán `IPantallaWpf` y declararán dependencia de contexto. No está integrada todavía ninguna pantalla de DAT-TERCEROS, CFG-MANT ni facturación.

## 6. Secuencia mínima de consumo

Autenticar y completar selección con Seguridad; transferir `ISesionTrabajo`; construir `GestorContenedor(sesion, catalogo)` y `VentanaContenedor(gestor)`. Para registrar una pantalla: crear `DestinoPantalla` con clave única, fábrica y predicado de permiso real. El módulo guarda mediante sus servicios y comunica el resultado a través de `TieneCambiosPendientes`/`GuardarAsync`.

Cambiar entorno: consultar destino provisional, resolver todas las decisiones, guardar lo solicitado, confirmar con Seguridad, retirar vistas anteriores y vaciar recientes. Cancelación/fallo conserva contexto; guardados ya completados no se revierten. Descartes se ejecutan tras confirmar. Cerrar sesión invalida la autoridad y devuelve un login limpio. Ejercicio cerrado permanece accesible y cerrado; estado desconocido se rotula sin inferencias.

## 7. Verificación y límites

Proyecto propio y pruebas: compilan sin advertencias/errores. Solución con VeriFactu restaurado: cero errores y dos advertencias previas WindowsBase/WebView2. Pruebas de transiciones, cancelaciones, guardado fallido/completado, permisos, limpieza y login pasan. ACE se verificó exclusivamente contra la copia privada; no se abrió la base original. PNG sintéticos a 1380×860 y 1000×640 revisados sin mostrar ventanas.

Falta validación manual del ciclo real visible, controles nativos de ventana, varios monitores/DPI y ediciones de módulos reales. Los dobles de pruebas acreditan el coordinador, no los guardados de futuros módulos. La cancelación no interrumpe forzosamente una llamada ACE nativa iniciada.

## 8. Decisiones abiertas y próximos pasos

Integrar CFG-MANT y el resto de módulos por el contrato anterior, con permisos específicos y pruebas de pendientes reales. Ratificar fuera de UI-PRINCIPAL la política SEG de operadores sin grupos activos y la propiedad organizativa. Completar validación manual del usuario. El incremento de contenedor está implementado; la integración funcional del conjunto de ProConta sigue abierta.
