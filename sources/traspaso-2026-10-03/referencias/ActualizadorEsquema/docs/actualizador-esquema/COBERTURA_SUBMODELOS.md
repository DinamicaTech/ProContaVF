# Cobertura de submodelos en la maestra 0.22

La muestra vigente `work/databases/ProConta.Maestra.Acceso.0.22.accdb` se creó desde cero con veintidós manifiestos y contiene 126 tablas locales. El diagnóstico reconoce las veintidós huellas y no informa diferencias de tablas, columnas, tipos ACE, longitudes de texto, índices ni nombres de claves externas. Solo hay filas en catálogos técnicos: tipos de IVA, activo, cuenta, operación ContaSol, factura y correo; estados de factura; plan y modelos PGC Pymes 2008; grupos de seguridad; alias de configuración; y trece plantillas de cuentas de activo. No hay sociedades, ejercicios, terceros, historial, asignaciones de acceso, preferencias, empleados, inversiones, buzones, certificados ni secretos reales.

| Submodelo | Manifiestos | Alcance |
|---|---|---|
| Identidad y contabilidad | 001–004 | Sociedades `TEXT(3)`, NIF único, razón social histórica, cuentas `TEXT(10)`, asientos, auditoría y centros. |
| Facturación y compras | 005–006 | Contratos, conceptos permanentes, programación permanente, facturas anuales y reconocimiento de compras. |
| Terceros | 005–006 y 019–020 | Identidad común; roles simultáneos Cliente, Proveedor y Contacto; domicilios, relaciones y mapas de origen; instantánea fiscal de facturas e historial de cambios por campo. |
| Norma43 y ContaSol | 007–009 | Banca, extractos, importaciones ContaSol, periodos y libros de IVA. |
| Seguridad | 010, 018 y 021–022 | Operadores, grupos, auditoría, restricciones de reports y procesos contables, acceso por sociedad y preferencias de entrada. |
| Correo | 011 y 017 | GestorCorreo, Mail Dispatcher y tablas de facturación todavía consumidas por el código heredado. |
| GestorContable y Nóminas | 012–013 | Fiscalidad anual, empleados permanentes, nóminas e importaciones por ejercicio. |
| Reporting | 014 | Modelos y reglas PGC Pymes, con semilla normativa del CSV incorporado. |
| Activos | 002 y 015 | Inmuebles, inversiones, cotizaciones, cuentas, amortización e importación. |
| VeriFactu | 016 | Configuración por sociedad, lotes anuales, envíos de factura y respuestas. |
| Soporte transversal | 018 | Configuración global, alias, conversaciones, revisiones de asientos y presentaciones tributarias. |

La comparación estática con los `CREATE TABLE` encontrados en las ramas deja fuera tres tablas del **motor ContaSol antiguo**: `TiposAsiento`, `CompatibilidadesFusionCuenta` y `FusionesIdentidadesContables`. El núcleo vigente representa el tipo de asiento como `Asientos.TipoAsiento`, y las fusiones de identidades en `FusionesCuentasContables`. No se ha duplicado ese modelo obsoleto ni se ha incorporado `CuentasContablesLegacy` o `FacturasProgramadasBak`, que son tablas de conservación de datos. La coincidencia de nombres de tablas tampoco acredita por sí sola que todos los consumidores funcionen con los campos transformados; esa validación continúa.

`Clientes` y `Proveedores` conservan sus claves funcionales y enlazan por `IdTercero` único a los datos comunes. Sus cuentas contables se asignan en `CuentasClientesEjercicio` y `CuentasProveedoresEjercicio`. El importador ContaSol registra correspondencias independientes por rol y contexto, sin fusionar por NIF. La muestra 0.18 se conserva como antecedente; sus huellas 005 y 006 difieren de esta versión.

El diagnóstico histórico de 29-09-2026 se hizo exclusivamente sobre copia: 95 tablas locales, 24 tablas del nuevo contrato ausentes y dos tablas adicionales de conservación. Ninguna de las dieciocho huellas entonces previstas figuraba en su historial. Su SHA-256 era `4A99674A9FC6DC4C9BD7D34ED4CB53DD0BE67A2DB25EFA9B868D29C1D7DF25A6`. Posteriormente recibió solo las tres tablas de [acceso real](../ACCESO_SOCIEDADES_GRUPO.md), con backup; no se le aplicaron los manifiestos de creación completa. La transición de datos se planificará y comprobará aparte sobre copias.
