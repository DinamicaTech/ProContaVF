---
title: Arquitectura
depends_on: [plataforma/entrega]
threads:
  - 2026-10-28 12:30 · Nuevo proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 08:38 · Estrategia de desarrollo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Decisiones técnicas que condicionan todo el árbol: aplicación de escritorio Windows en WPF y C# (.NET 10, x64) sobre una base Access (accdb) detrás de una capa de persistencia propia, instalada en un único Windows Server al que acceden 2-3 usuarios por Escritorio remoto.
La solución es nueva, en este repositorio; el proyecto antiguo (VB.NET) solo se consulta.
La estructura de la BD tiene un único dueño (`datos/esquema`), que publica el diccionario de datos; cada ente es un nodo de su área funcional que publica su contrato.
El código se construye de abajo arriba: esqueleto técnico, motores de los entes sin pantallas en orden de dependencias y después los cortes por área con sus pantallas, de uno en uno; las pruebas se ejecutan en local.

## Decisions
- 2026-10-28 12:45 · Plataforma: aplicación de escritorio Windows con interfaz WPF, escrita en C# sobre .NET 10 LTS y compilada solo para x64. Motivo: el propietario prefiere C# por sus ventajas sobre VB.NET. Sustituye la parte de lenguaje de RF-GEN-01 y las convenciones VB de RF-TEC-02 (sources/01_ESPECIFICACION_FUNCIONAL.docx); el resto de esos requisitos sigue en vigor: identificadores propios en español, PascalCase público, `_camelCase` en campos privados, camelCase en locales, sufijo Async, y en C# `Nullable` activado en lugar de Option Strict/Explicit/Infer.
- 2026-10-28 12:45 · El código antiguo en VB.NET (carpeta `ProConta/`, fuera de Git) es solo referencia: sus algoritmos y casos de prueba se traducen a C#, nunca se copian sus pantallas ni sus mecanismos provisionales de integración.
- 2026-10-28 12:45 · Persistencia: Microsoft Access (accdb) mediante ACE/OLE DB x64, con consultas parametrizadas (parámetros por posición) y transacciones cortas. Motivo: el propietario necesita un SGBD rápido y ágil contra el que trabajar directamente; el volumen de datos no justifica otro motor y no habrá concurrencia en LAN. Todo acceso a Access pasa por la capa de persistencia común (`datos/persistencia`): ningún módulo abre conexiones por su cuenta, y el dominio no conoce OLE DB, de modo que un cambio de motor futuro quedaría localizado.
- 2026-10-28 12:45 · Instalación: un único puesto, un Windows Server, al que acceden 2-3 usuarios por Escritorio remoto. La BD y la raíz documental están en un disco local del servidor; varias sesiones pueden abrir a la vez el mismo accdb, por lo que se mantienen transacciones cortas, concurrencia optimista en fichas y numeración protegida por transacción. La configuración local de cada puesto se guarda en el perfil del usuario de Windows.
- 2026-10-28 12:45 · Código: solución nueva en este repositorio (`src/` y `tests/` junto a `docs/`), con un proyecto por área funcional y contratos estables entre ellos (RF-TEC-08); la interfaz, la aplicación, el dominio, la persistencia y las integraciones van separados (RF-TEC-01).
- 2026-10-28 12:45 · Estructura de la BD: el nodo `datos/esquema` (ActualizadorEsquema) es el único que escribe DDL, versiona la maestra y actualiza instalaciones en producción, y publica el diccionario de datos (tablas, campos, tipos, longitudes, índices y relaciones) que leen los módulos. Cada área es dueña del modelo lógico de sus tablas y pide cualquier cambio de estructura a `datos/esquema` con una línea "Derived from" en sus Requirements; ningún otro nodo modifica la estructura.
- 2026-10-28 12:45 · Entes: el árbol se organiza por áreas funcionales; cada ente (Tercero, CuentaContable, FacturaVenta, FacturaCompra…) es un nodo hijo del área que lo posee, publica su contrato y es la única vía de escritura de sus tablas (RF-ARQ-01); las demás áreas solo lo usan a través de ese contrato. Los tipos comunes (NIF, importes y redondeo, contexto sociedad/ejercicio, errores) viven en el nodo `nucleo`.
- 2026-10-28 12:45 · Organización: sociedades, ejercicios, formas de pago y geografía forman el nodo `organizacion`, transversal a todo el proyecto (resuelve P-02 / RF-ARQ-04).
- 2026-10-28 12:45 · Tesorería: de momento no se trabaja. Norma 43 es un nodo propio; cobros, pagos, remesas y vencimientos quedan como idea en el laboratorio creativo.
- 2026-10-28 12:45 · Fuentes: el paquete de traspaso del 03/10/2026 (documentos y referencias, sin bases de datos) se conserva en `sources/traspaso-2026-10-03/`.
- 2026-10-31 08:37 · Estrategia de desarrollo del código, una vez cerradas las preguntas pendientes de cada nodo:
  1. Esqueleto técnico: solución `src/`+`tests/`, `nucleo`, `datos/persistencia`, `datos/esquema` (versión 1 de la BD), `plataforma/configuracion`, `plataforma/ui-base`, `plataforma/contenedor` y `seguridad/acceso`. Terminado cuando la aplicación vacía abre en el servidor con el usuario del propietario y muestra su versión.
  2. Motores sin pantallas, de abajo arriba según los `depends_on` de los nodos: primero los entes que no dependen de otros y después los que se apoyan en ellos (orientativo: organización → Tercero y CuentaContable → centros de coste → motor de asientos → FacturaVenta y FacturaCompra con su contabilización). Cada motor incluye su contrato, su cambio de esquema, su persistencia y sus pruebas, y cubre solo lo que sus consumidores ya tienen especificado; lo demás queda como borrador.
  3. Cortes por área con sus pantallas y flujos sobre esos motores, en el orden del recorrido crítico (spec § 30): terceros, facturación con VeriFactu de pruebas y PDF, programadas y correo, migración del ERP; después compras, contabilidad, N43, nóminas, mayor e informes. Cada pantalla se compara con su maqueta.
  - Unidad de trabajo: un nodo por hilo y una PR por incremento. Antes de empezar se cierran sus preguntas pendientes; al terminar, el propietario valida una entrega ejecutable con una lista corta de comprobaciones y no revisa código.
  - Clases: una clase por ente (agregado), no por tabla; un ente incluye sus tablas 1:N propias y se guarda en una transacción (p. ej. FacturaVenta con líneas y desgloses de IVA). Los catálogos simples no necesitan una clase propia: se publican con un proveedor declarativo (columnas y validaciones) para `plataforma/mantenimiento`, que nunca escribe directamente en la BD sino a través del dueño de la tabla.
  - Paralelismo: un único hilo de código a la vez; en paralelo solo hilos que no tocan código (análisis, documentación).
  - Pruebas: en local, sin compilación en GitHub. Cada hilo ejecuta las pruebas sin BD antes de abrir su PR y las pruebas contra Access en el equipo del propietario, contra la BD de pruebas, antes de pedir la validación. Se podrá añadir la ejecución en GitHub más adelante sin rehacer nada.

## Requirements
- 2026-10-28 12:28 · es importante que haya un nodo especializado en mantener la estructura de la base de datos para poder actualizar el modelo cuando se actualice una instalación en producción, por lo tanto, cualquier nodo que quiera modificar la estructura de la Base de Datos ha de comunicarlo o pasar a través del nodo de mantenimiento de estructura de BD.
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:39 · A1) C# y accdb.
- 2026-10-28 12:39 · A2) Un solo puesto (Windows Server) al que accederán 2-3 usuarios utilizando Remote Desktop
- 2026-10-28 12:39 · A3) Solución nueva
- 2026-10-28 12:39 · A6) a (son nodos tranversales a todo el proyecto)
- 2026-10-28 12:39 · A7) b (de momento no trabajaremos tesorería)
- 2026-10-28 12:39 · A8) a
- 2026-10-31 08:23 · Lo que no tengo claro es cual es la mejor estrategia para ir desarrollando todo el código. ¿Qué propones?
- 2026-10-31 08:31 · Antes de los cortes verticales, no se podrían avanzar las clases núcleo (por ejemplo, el motor de asientos, motor de terceros, etc.) transversales ?
- 2026-10-31 08:31 · P1. Hace falta compilarlo en Github ? Quizá es más sencillo compilar y probar solo en local contra la BD de pruebas
- 2026-10-31 08:36 · Ok, entiendo que se puede organizar la implementación de los 'entes' haciendo un drill-up, es decir, implementando los más periféricos que no dependan de otros y a partir de ahí ir construyendo los de arriba, así no habría que dejar tanto código pendiente de otros contratos
