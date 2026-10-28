# Contrato de sociedades y ejercicios para la base maestra

Estado: identidad y distribución de campos incorporadas al manifiesto operativo `20260928_002_nucleo_operativo.json`. Basado en las propiedades DAO de una copia de `ProcontaVF.accdb` con SHA-256 `4A99674A9FC6DC4C9BD7D34ED4CB53DD0BE67A2DB25EFA9B868D29C1D7DF25A6`. El archivo original conservaba esa misma huella el 28-09-2026; todas las consultas se hicieron sobre copias. El inventario DAO completo está en `work/dao-propiedades.json`, fuera de Git.

## Identidad

| Tabla | Contrato nuevo |
|---|---|
| `Sociedades` | `IdSociedad TEXT(3)` PK física; `NIF TEXT(10)` obligatorio, único e inmutable; `RazonSocial TEXT(255)` obligatoria y vigente. |
| `SociedadesEjercicios` | `IdSociedadEjercicio LONG` PK; `IdSociedad TEXT(3)` obligatorio con FK a `Sociedades`; `Ejercicio TEXT(4)` obligatorio; `RazonSocial TEXT(255)` obligatoria e histórica. Índice único `(IdSociedad, Ejercicio)`. |

El esquema instalado llama `CIF` al identificador fiscal y `Nombre` a la denominación en ambas tablas. El modelo nuevo tendrá un único NIF en `Sociedades`; duplicarlo por ejercicio crearía dos fuentes para un dato que el usuario ha declarado inmutable. En cambio, duplica `RazonSocial`: al cambiarla se modifica la fila permanente, el ejercicio en curso y cualquier ejercicio posterior ya creado, sin reescribir ejercicios anteriores. Las facturas ya emitidas mantienen sus datos fiscales materializados y su inmutabilidad propia. Los consumidores que hoy leen `SociedadesEjercicios.CIF` o `.Nombre` deberán usar la FK al NIF permanente y `SociedadesEjercicios.RazonSocial`, respectivamente.

La tabla instalada tiene tres sociedades y trece ejercicios. No hay pares `(IdSociedad, Ejercicio)` duplicados. Sí hay **dos sociedades con el mismo CIF/NIF** (códigos `002` y `003`, nombres diferentes), mientras el índice instalado sobre `CIF` es no único. El usuario ha confirmado que `003` es una sociedad de pruebas y ha autorizado su supresión en el destino. En la copia inspeccionada sus ejercicios son 2024 y 2025; solo tiene una fila propia de `ConfiguracionesContables`, sin facturas, asientos, activos ni referencias de `FacturasCompra` a sus claves anuales. La carga empresarial de la maestra nueva excluirá `003`, sus dos ejercicios y esa configuración. Si se ensaya la actualización de una copia de pruebas, se podrá retirar allí tras verificar de nuevo sus dependencias. No se fusionará con `002`, no se alterará su NIF y la base de pruebas original conservará sus filas. La maestra nueva se crea vacía.

## Configuración heredada

Las dos tablas actuales duplican `FormaPagoDefecto`, `Usuario`, `RutaFacturasExportadas`, `RutaFacturasDuplicadas`, `TipoRetencion`, `PorcentajeRetencion`, `TipoIVA1`–`TipoIVA4`, `BDFacturasVenta`, `BDERPGestion`, `BDExportacion` y los campos `Cuenta*`. La duplicación de la razón social tiene una regla de actualización confirmada; **no se aplica esa propagación automáticamente a los otros campos**. Para el primer manifiesto se conservan en ambos ámbitos y se documenta la fila anual como configuración efectiva de su ejercicio. La fila permanente sirve de valores de partida para ejercicios futuros, sin modificar ejercicios históricos cuando se edite. Una posterior simplificación requerirá revisar cada consumidor y los datos existentes.

Los campos de código de cuenta de ambas tablas serán `TEXT(10)`, incluidos `CuentaSuplido` y `CuentaSuplidoIVA`, actualmente `TEXT(255)`. En la copia examinada ninguno supera diez caracteres; la transición exige repetir el preflight y rechazar valores que no quepan. `FormaPagoDefecto` pasará de `LONG` heredado a `BYTE` para concordar con `FormasPago.Codigo`; las referencias al código `1` de la base de pruebas carecen de fila correspondiente en el catálogo ERP copiado y requieren resolución antes de activar la FK.

`SociedadesEjercicios` añade frente a `Sociedades` `RutaFacturasAExportar`, `TrimestreActivo`, `Activa`, `NumIVSDuplicados` y `PorcentajeProrrataAñoAnterior`. El cambio coordinado de Asientos incorpora `EjercicioCerrado YESNO NOT NULL DEFAULT False`. Los valores de estos campos se preservan por ejercicio. Los campos de ruta `BDFacturasVenta`, `BDERPGestion` y `BDExportacion` son referencias de integración heredadas, no instrucciones para crear vínculos en la maestra nueva.

## Restricciones y transición

1. Crear las sociedades antes que los ejercicios; insertar filas reales solo por importador o configuración explícita, sin sociedad ficticia de arranque.
2. Normalizar el NIF para comprobar unicidad sin alterar silenciosamente el texto de origen; diagnosticar vacíos, duplicados y longitud mayor que diez. La regla de edición impide cambiarlo una vez creada la sociedad. El perfil de exclusión de `003` es específico de esta instalación y no una regla universal del actualizador.
3. Comprobar que cada ejercicio tiene sociedad y que `(IdSociedad, Ejercicio)` es único; no deducir el ejercicio a partir de `IdSociedad`.
4. Transferir `Nombre` a `RazonSocial` de la sociedad y del ejercicio sin sobrescribir el histórico. `CIF` anual se contrasta con `Sociedades.CIF`; cualquier discrepancia requiere incidencia, no se elige uno arbitrariamente.
5. Validar claves de formas de pago y cuentas antes de estrechar tipos o activar FKs; no truncar códigos ni crear filas de catálogo inventadas.
6. Documentar en DAO nulabilidad, defaults, índices, relaciones y significado de cada campo en el manifiesto físico. La captura tiene descripción en los 35 campos de `Sociedades` y en ninguno de los 42 de `SociedadesEjercicios`; esa ausencia se corregirá al crear la maestra.
