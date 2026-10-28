# Encaje de ProConta en la maestra 0.23

Fecha de la captura: 01/10/2026. La base de origen es una copia de `C:\Docs\VeriFactu\ProConta.accdb`, con SHA-256 `27B201CAEE2B0C344AC5EB5F8AAB45037C47BB047B1291F9CE26881BA03B0804`. Los originales `ProConta.accdb` y `ProcontaVF.accdb` conservaron esa misma huella tras el ensayo.

## Alcance aplicado

El manifiesto `database/migrations/20261001_023_supermodelo_proconta.json` amplía la maestra vacía a 130 tablas locales y 23 versiones de esquema. Incorpora `DatosContacto`, `EstadoContratoTerceros`, `Perfiles` y `PerfilesTerceros`, además de los campos locales operativos ausentes, sus relaciones y sus índices donde ACE lo permite. `Terceros.TipoPersona` admite `NULL`; `Nombre` y `RazonSocial` son `TEXT(200)` y `Email` es `TEXT(150)`.

La comparación considera las 106 tablas locales operativas y catálogos de ProConta. Excluye las tablas de respaldo `CuentasContablesLegacy` y `FacturasProgramadasBak` y los vínculos externos `F_DEP`, `F_CDP`, `Inmuebles` y `FormasPago`, conforme a la decisión del usuario. Los campos heredados añadidos a tablas compartidas son opcionales en la maestra para conservar las escrituras del modelo nuevo.

La maestra generada está en `work/databases/ProConta.Maestra.Supermodelo.0.23.accdb`, SHA-256 `4F95E298BA3B7A4C301CE24A3D150076E1A55D336E2AC01ACA4D64399387EB57`. Su diagnóstico reconoce los 23 manifiestos y no produce hallazgos.

## Reducción de longitudes sobre copia

`database/migrations/20261001_024_reducir_longitudes_proconta.ps1` comprueba que ningún valor supera el límite de la maestra, crea un destino nuevo y reduce 20 campos de texto en esa copia. Incluye `Terceros.Email` de 254 a 150, los códigos de cuenta a 10 y `IdSociedad` a 3 donde faltaba. Además reduce los dos `FormaPagoDefecto` de `LONG` a `BYTE`, previa comprobación de su rango. No reduce `Terceros.Nombre` ni `RazonSocial`, porque la maestra ahora admite sus 200 caracteres.

La copia resultante está en `work/databases/ProConta.Reducida.20261001.v2.accdb`, SHA-256 `76DE365EDF5EDA587DC56D03981E5C1049E6C11CF8D29B22D64692CBB783BAE7`. Conserva las 108 tablas locales de la fuente y sus recuentos de filas. Los originales siguen intactos. El hilo `DAT-TERCEROS · Modelo y gestión de terceros` adaptó su contrato, DDL y persistencia a `Email` de 150 caracteres y verificó 14 pruebas; esa rama aún debe integrarse en el consumidor instalado antes de sustituir la base operativa.

## Diferencias físicas restantes

`tools/ActualizadorEsquema.Inspeccion/CompararSubconjuntoProConta.ps1` encuentra una diferencia al comparar la copia reducida con la maestra:

| Objeto | ProConta | Maestra | Evaluación |
| --- | --- | --- | --- |
| Índice `Sociedades(CIF)` | Presente | Ausente | ACE rechaza añadirlo: la tabla ya alcanza su límite contando las 29 relaciones entrantes y sus tres índices propios. `CIF` es un campo heredado opcional en la maestra. |

Por tanto, los datos y columnas locales operativas de ProConta caben en la maestra. **El subconjunto físico estricto aún tiene la excepción del índice legado `CIF`**. No se ha debilitado ninguna relación de la maestra para forzarlo. El corte de la copia reducida debe esperar a la integración del contrato de Terceros en el consumidor instalado.

## Verificación

- Compilación de `ProConta.Facturacion.sln`: correcta, 0 errores.
- Pruebas de creación y diagnóstico de maestra: 5 correctas.
- Diagnóstico de la maestra 0.23: 130 tablas, 23 versiones reconocidas, ningún hallazgo.
- Reducción sobre copia: 20 textos y 2 numéricos, sin valores fuera de rango; 108 recuentos de filas iguales a los de la captura.
