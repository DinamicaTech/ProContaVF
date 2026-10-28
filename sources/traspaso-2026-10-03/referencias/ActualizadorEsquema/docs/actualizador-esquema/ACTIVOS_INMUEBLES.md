# Diseño del detalle `ActivosInmuebles`

Estado: diseño incorporado al manifiesto ejecutable `20260928_002_nucleo_operativo.json` de la base maestra nueva. Se basa en los metadatos DAO de una **copia** de `ProcontaEstructura.accdb` (SHA-256 `56293465EC601762C7C5B04A1035F88A53286E8EFE42791EF18EFD68F1F09E99`), en el importador actual de Activos y en las consultas legacy `CalculoAmortizaciones` y `CuadreInmuebles`. Una segunda copia del ERP enlazado (SHA-256 `D3E457E9E3FD0E0BC68709E0803513E363DB5B9A896DEE64951D86534D6AE662`) confirmó que la estructura de `Inmuebles`, `TiposInmueble` y `FormasPago` no había cambiado. Durante esta revisión el archivo ERP original cambió otra vez de huella (`A03499D43C31B8A78EA3E22275D575CC879FB97812A850E51C80CF5BC0ABE352` a las 11:55 UTC del 28-09-2026); por ello las comprobaciones de datos se atribuyen solo a las copias identificadas y se repetirán en el corte. Las copias temporales se retiraron; no se modificó la base original. Los inventarios de propiedades quedan en `work/inmuebles-propiedades*.json`, fuera de Git.

## Límite del agregado

`Activos` conserva la identidad, sociedad, tipo, adquisición, factura de compra, dimensión analítica y resumen contable comunes. `ActivosInmuebles` será un detalle opcional uno a uno cuya PK y FK es `IdActivo LONG`. Solo existirá para un activo de tipo `INMUEBLE`. Reproduce la pauta de `ActivosInversiones`, pero contiene la ficha física, catastral y los datos de origen necesarios para conciliar la importación. Un cambio de tipo que saque el activo de `INMUEBLE` debe resolver antes su detalle y sus contratos; la aplicación no debe dejar un detalle huérfano o incompatible.

`TiposActivo` ya contiene `INMUEBLE`, `VEHICULO`, `TECNICO` e `INVERSION`. Los tipos legacy 3 (`Coches`) y 4 (`Instalaciones/equipamiento`) pertenecen a los tipos de activo comunes `VEHICULO` y `TECNICO`, respectivamente: **no** reciben una fila de `ActivosInmuebles`. El tipo 6, descrito como fondos por el importador actual, queda fuera de este detalle y necesita una ruta de importación separada. Los tipos 1 (`Vivienda`), 2 (`Local`) y 5 (`Parking`) sí son inmuebles. El catálogo `TiposInmueble` del ERP describe los tipos 1 a 6; los tipos 7 y 9 que el importador actual trata como inmuebles conservarán su código sin asignarles una clase inventada. Cualquier otro código exige diagnóstico antes de importar.

Perfil agregado de la copia del ERP: tipos 1/2/3/4/5/6/7/9 con 18/3/3/2/1/18/2/1 filas respectivamente. Las tres filas de los tipos 7 y 9 contienen campos catastrales y de alquiler informados, pero el catálogo no explica su significado. Los 25 registros clasificados provisionalmente como inmuebles (1, 2, 5, 7 y 9) tienen `PorcentajeInmueble` entre 0,1894 y 0,6431, `PorcentajeAmortizacion` entre 0,02 y 0,04 y referencias catastrales de hasta 21 caracteres. Estas medidas solo verifican compatibilidad de la captura; se repetirán sobre una copia vigente antes de cargar datos.

## Columnas propuestas

| Columna | Access | Semántica y restricción |
|---|---|---|
| `IdActivo` | `LONG NOT NULL` | PK y FK a `Activos.IdActivo`; una ficha por activo inmueble. |
| `ClaseInmueble` | `TEXT(30)` | `VIVIENDA`, `LOCAL` o `APARCAMIENTO` cuando el tipo 1, 2 o 5 lo justifique; nula para 7/9 hasta clasificarlos. Para altas nuevas, la aplicación requiere una clase válida. |
| `TipoInmuebleOrigen` | `BYTE` | Código legacy conservado literalmente; nulo en altas nuevas. No sirve como catálogo del producto. |
| `ReferenciaCatastral` | `TEXT(30)` | Cadena del origen sin normalización destructiva; índice de búsqueda no único. |
| `ValorCatastralSuelo` | `CURRENCY` | Valor declarado para el suelo. |
| `ValorCatastralInmueble` | `CURRENCY` | Valor declarado para el inmueble. No se presume que equivalga exactamente a «construcción» sin contrastar la fuente. |
| `ValorCatastralTotal` | `CURRENCY` | Valor total declarado; se guarda incluso si no coincide con la suma de los componentes, registrando incidencia. |
| `ProporcionValorInmueble` | `DECIMAL(9,8)` | Fracción de coste imputable al inmueble frente al terreno, en el intervalo 0–1. No expresa porcentaje de titularidad. |
| `ValorCompraNeto` | `CURRENCY` | Importe cobrado por el vendedor, distinto de gastos de compra. |
| `GastosCompra` | `CURRENCY` | Notario, ITP y otros gastos incluidos en `CosteCompra` legacy. |
| `PorcentajeAmortizacionAnualOrigen` | `DECIMAL(9,8)` | Tasa legacy como fracción (por ejemplo, 0,02 para 2 %). Es evidencia para crear o contrastar `PlanesAmortizacionActivo`, no una segunda fuente oficial de amortización. |
| `ImporteAmortizadoOrigen` | `CURRENCY` | Saldo histórico leído del ERP para conciliación; el valor oficial sigue procediendo de contabilidad y del plan aplicable. |
| `PrecioAlquilerOrigen` | `CURRENCY` | Importe que constaba en la ficha del ERP. No se usa para facturar si existe `Contratos.ImporteMensual`. |
| `FechaInicioAlquilerOrigen` | `DATETIME` | Fecha que constaba en la ficha del ERP; no basta para crear un contrato. |
| `TributoMetropolitanoUltimoImporteOrigen` | `CURRENCY` | Último importe conocido; el origen no indica ejercicio, por lo que no se asigna uno artificial. |

Los campos de origen aparecen con `Required=False` en DAO, salvo la implicación de la PK `Id`; el importador debe tratar los valores no informados como `NULL`. Los porcentajes y los importes se prevalidarán sobre la copia antes de imponer límites o generar un plan; jamás se recortarán ni se sustituirá `NULL` por cero. Se deben documentar en DAO las unidades, el origen y la distinción entre dato vigente y dato importado. `ReferenciaCatastral` no es única: varias posiciones o sociedades pueden referirse al mismo inmueble.

### DDL objetivo para el manifiesto inicial

```sql
CREATE TABLE ActivosInmuebles (
    IdActivo LONG CONSTRAINT PK_ActivosInmuebles PRIMARY KEY,
    ClaseInmueble TEXT(30),
    TipoInmuebleOrigen BYTE,
    ReferenciaCatastral TEXT(30),
    ValorCatastralSuelo CURRENCY,
    ValorCatastralInmueble CURRENCY,
    ValorCatastralTotal CURRENCY,
    ProporcionValorInmueble DECIMAL(9,8),
    ValorCompraNeto CURRENCY,
    GastosCompra CURRENCY,
    PorcentajeAmortizacionAnualOrigen DECIMAL(9,8),
    ImporteAmortizadoOrigen CURRENCY,
    PrecioAlquilerOrigen CURRENCY,
    FechaInicioAlquilerOrigen DATETIME,
    TributoMetropolitanoUltimoImporteOrigen CURRENCY,
    CONSTRAINT FK_ActivosInmuebles_Activo FOREIGN KEY (IdActivo)
        REFERENCES Activos (IdActivo)
);

CREATE INDEX IX_ActivosInmuebles_ReferenciaCatastral
    ON ActivosInmuebles (ReferenciaCatastral);
```

La relación debe comprobarse físicamente tras crearla con ACE. La regla «el activo asociado es de tipo `INMUEBLE`» se valida en el servicio y en el importador dentro de la misma transacción, ya que la FK por sí sola no verifica `IdTipoActivo`. No se añade una restricción de unicidad sobre `ReferenciaCatastral`.

Prueba de sintaxis: ACE/OLE DB aplicó las tres sentencias anteriores sobre una copia temporal de la maestra técnica. Una segunda sonda en .NET 10 x64 extrajo el bloque SQL de este documento, lo aplicó a otra copia y consultó los 15 campos de la tabla; terminó con código 0. Ambas copias y la sonda temporal se retiraron. El DDL está integrado en el creador; la prueba automatizada verifica que ACE rechaza una ficha sin activo padre.

## Mapeo completo de las 21 columnas de `Inmuebles`

| Origen | Destino o tratamiento |
|---|---|
| `Id` | `Activos.ReferenciaMigracion = INMUEBLES:<Id>` y correspondencia de importación `Id` → `IdActivo`; no se reutiliza como PK nueva. |
| `Tipo` | `Activos.IdTipoActivo` según la tabla de tipos anterior; para inmuebles también `ActivosInmuebles.TipoInmuebleOrigen` y `ClaseInmueble` cuando se conoce. |
| `Nombre` | `Activos.Nombre`. |
| `Alias` | `Activos.AliasActivo`. |
| `RefCatastral` | `ActivosInmuebles.ReferenciaCatastral`. |
| `FechaCompra` | `Activos.FechaAdquisicion`; `FechaAlta` no debe hacerse pasar por fecha de compra si se desconoce. |
| `ValorCompra` | `ActivosInmuebles.ValorCompraNeto`; contribuye al coste de adquisición común. |
| `CosteCompra` | `ActivosInmuebles.GastosCompra`; contribuye al coste de adquisición común. |
| `ValorCatastroSuelo`, `ValorCatastroInmueble`, `ValorCatastroTotal` | Sus tres columnas homónimas de detalle. |
| `PorcentajeInmueble` | `ActivosInmuebles.ProporcionValorInmueble`. Las consultas legacy calculan `CosteCompra * PorcentajeInmueble` y `CosteCompra * (1 - PorcentajeInmueble)` para edificio y terreno. |
| `ImporteAmortizado` | `ActivosInmuebles.ImporteAmortizadoOrigen`; se concilia con `Activos.AmortizacionAcumulada` y cuentas oficiales. |
| `PorcentajeAmortizacion` | `ActivosInmuebles.PorcentajeAmortizacionAnualOrigen`; proponer plan solo tras verificar base amortizable, fechas y cuentas. |
| `PrecioAlquiler`, `FechaInicioAlquiler` | Campos `*Origen` del detalle. El contrato operativo debe migrarse de `Contratos` con su titular, vigencia e importe; no se fabrica a partir de estas dos columnas. |
| `IdFacturaCompra` | `Activos.IdFacturaCompra` tras correspondencia de facturas. Si no hay equivalencia, incidencia y referencia pendiente. |
| `Uno` | No se incorpora: la consulta legacy lo iguala a `Sistema.UnUno`, una constante técnica para unir la configuración. |
| `Departamento`, `SubDepartamento` | `Activos.IdDepartamento` e `IdSubDepartamento` mediante correspondencias globales; no copiar IDs sin mapear. |
| `TributoMetropolitano` | `ActivosInmuebles.TributoMetropolitanoUltimoImporteOrigen`. |

El importe total de adquisición común se calcula como `ValorCompra + CosteCompra` **solo cuando ambos significados y valores estén confirmados**; si uno es nulo, no se interpreta automáticamente como cero. El importador actual sí suma ambos y asigna el resultado a `Activos.CosteAdquisicionActual`: habrá que contrastar y corregir esta regla durante la migración integral. Para coches y equipamiento, conservar el desglose de compra requerirá una solución común o detalles propios fuera de este submodelo.

## Relaciones de alquiler y contabilidad

La captura de pruebas contiene **una relación Access declarada** de `Contratos.IdInmueble` a la tabla vinculada `Inmuebles.Id` del ERP. Sin embargo, sus dos contratos no encuentran ese ID ni en la copia de inventario ni en la segunda copia del ERP; ambos números sí coinciden con un `Activos.IdActivo` de tipo `INMUEBLE` y de la misma sociedad. Esto apunta a que los contratos de prueba se crearon usando IDs de Activos mientras la relación declarada seguía apuntando al origen antiguo. La coincidencia numérica sola no autoriza un remapeo automático: la transición debe clasificar cada contrato, comprobar su activo y dejar incidencia si falta o es ambiguo. En la base maestra nueva `Contratos.IdInmueble` debe referenciar `ActivosInmuebles.IdActivo`, que comparte la PK del activo. Para contratos que provengan del ERP antiguo, el importador utilizará la correspondencia `Inmuebles.Id` → `Activos.IdActivo`. No se deduce un arrendatario ni un período contractual de `PrecioAlquiler` y `FechaInicioAlquiler`. Los importes históricos de renta pertenecen a los contratos y sus revisiones, no a un único campo de la ficha del inmueble.

`CuentasInmuebles` y `PlantillasInmuebles` se trasladan al modelo `CuentasActivo` y `PlantillasCuentasActivo` mediante roles semánticos. `PorcentajeInmueble` y los importes de catastro pueden ayudar a revisar la separación edificio/terreno, pero no generan asientos ni saldos contables por sí solos. `InmueblesRecibosSEPA` e `InmueblesTitularesSEPA` son conjuntos distintos sin clave de inmueble declarada en el inventario; este detalle no absorbe sus datos ni los descarta automáticamente.

## Comprobaciones previas a la carga

1. Repetir el perfil agregado sobre una copia actual: tipos presentes, nulos, longitud de referencia, tasas fuera de 0–1, importes negativos y discrepancias de total catastral. Mantener los tipos 7/9 sin clase funcional hasta resolver su significado.
2. Verificar la correspondencia unívoca de `Inmuebles.Id` con `Activos.ReferenciaMigracion` e impedir que una reimportación omita cambios del origen silenciosamente.
3. Mapear facturas, departamentos, subdepartamentos, contratos y cuentas antes de activar relaciones; informar huérfanos con el identificador de origen. Distinguir contratos de prueba que ya usan `Activos.IdActivo` de contratos ERP que usan `Inmuebles.Id`.
4. Crear y actualizar `Activos` y `ActivosInmuebles` en una transacción; validar fila de detalle, PK/FK, clase y tipo, y comparar recuentos por tipo.
5. Conciliar el coste y la amortización con contabilidad sin convertir la instantánea legacy en saldo oficial.

El manifiesto físico ya incluye la ficha vacía. La carga desde Inmuebles y los cambios del importador de Activos quedan pendientes de integrar con el esquema completo; ninguna base original se ha modificado.
