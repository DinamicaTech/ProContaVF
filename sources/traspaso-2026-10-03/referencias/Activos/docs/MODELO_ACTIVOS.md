# Modelo de activos

## Decisión principal

`InstrumentosFinancieros` describe una ficha compartida (nombre, ISIN, ticker,
moneda y URL). `Activos` representa una posición propiedad de una sociedad. Una
posición admite cualquier número de compras, ventas, traspasos y ajustes en
`OperacionesInversion`; por eso varias compras del mismo fondo no crean activos
duplicados. Solo se abre otra posición si cambia la sociedad o se decide separar
su custodia o clasificación.

Los diez fondos del universo propuesto se crean como instrumentos candidatos.
No crean posiciones ni operaciones. Las seis posiciones vigentes se leen del
rango `Cartera!A97:Q102` del libro cerrado y se asignan a la sociedad `002` al
ejecutar el importador.

## Importes materializados

`CosteAdquisicionActual`, `AmortizacionAcumulada`, `DeterioroAcumulado`,
`ValorContable`, `ValorActual` y `ResultadoNoRealizado` viven en `Activos` para
que Access y reporting consulten rápido. Son cachés de solo lectura funcional:

- el coste se reconstruye desde operaciones o movimientos capitalizables;
- la amortización oficial procede del saldo de la identidad contable vinculada
  al rol `AMORTIZACION_ACUMULADA`;
- el deterioro procede de contabilidad;
- el valor actual procede de la última cotización disponible.

`FechaHoraUltimoCalculo` y `EstadoCalculo` permiten detectar datos pendientes o
desactualizados. No existe un segundo importe de amortización editable.

## Cuentas automáticas

`PlantillasCuentasActivo` define líneas por tipo y `RolCuenta` semántico.
`CuentasActivo` enlaza cada línea con una `IdentidadContable` permanente; la
cuenta de cada ejercicio se resuelve mediante el maestro contable. Al guardar un
activo se completan todas las líneas activas. Al añadir una línea, el mismo
provisionador puede recorrer todos los activos vigentes del tipo. El proceso es
idempotente por el índice único `(IdActivo, IdPlantillaCuentaActivo)` y deja el
activo en `IncidenciaCuentas` si una cuenta requiere reintento.

## Migración e importación

La migración `20260903_001_ACTIVOS` crea y documenta el esquema tras la copia de
seguridad y comprobación de acceso exclusivo del actualizador común. El
importador legacy excluye `Tipo=6`, `InmueblesRecibosSEPA` e
`InmueblesTitularesSEPA`. Los tipos legacy 7 y 9 se conservan como inmuebles sin
inventar un subtipo. Las asociaciones útiles de `CuentasInmuebles` se traducen a
roles semánticos y se enlazan con la identidad contable del último ejercicio de
la sociedad; si falta una cuenta, se informa y no se crea un duplicado a ciegas.
El libro y la base antigua siguen siendo orígenes externos; no se versionan
datos financieros reales.

## Asistente de compra

El mantenimiento abre un asistente que consulta por ISIN el último valor diario
publicado en la ficha de Renta 4. Siempre muestra la fecha y el origen: un valor
liquidativo diario no se presenta como si fuera una cotización intradía. Para
instrumentos no denominados en euros, el cambio se obtiene con la convención
"unidades de moneda nativa por euro" y se muestran ambos importes.
Los instrumentos históricos cuya ficha de Investing no permite una consulta
directa utilizan como respaldo su última cotización importada, rotulada como
histórico local y con su fecha visible.

La propuesta aplica `Floor(importe máximo / precio)` y, por tanto, nunca rebasa
el presupuesto con participaciones enteras. El usuario puede cambiar la cantidad,
pero la validación mantiene el mismo límite. Al registrar, la operación queda
marcada como provisional: si ya hay una posición abierta para la misma sociedad
e instrumento, acumula participaciones y coste; si no, crea el activo y completa
su plantilla de cuentas. La ficha permite confirmar después el cargo real de
Renta 4 y repercute solo la diferencia en `CosteAdquisicionActual` y
`ValorContable`, manteniendo coherentes los campos materializados.

## Capa gráfica de inversiones

La visualización se diseñará como una capacidad transversal reutilizable de
ProConta y no como comportamiento incrustado en Activos. El motor y su spike se
desarrollarán en `C:\Docs\VeriFactu\ProConta.Graficos`, rama `codex/graficos`.
El motor inicial propuesto para el piloto es ScottPlot 5 mediante
`ScottPlot.WPF`, aislado en el adaptador de ese módulo. Activos no añadirá el
paquete ScottPlot ni referenciará sus tipos, controles o enumeraciones desde
Dominio, Aplicación o UI.

El futuro contrato compartido se apoyará, como mínimo, en estos conceptos:

- `PuntoTemporal`: instante, valor y estado de calidad o ausencia del dato.
- `SerieTemporalGrafico`: identidad, título, unidad, puntos, estilo semántico y
  agrupaciones de exposición.
- `EventoGrafico`: compra, venta, dividendo u otro flujo relevante, con fecha,
  importe, símbolo semántico y texto explicativo.
- `OpcionesGrafico`: periodo, normalización, benchmark, filtros, visibilidad del
  histórico previo y opciones de interacción.

El adaptador WPF de Motor Gráficos traducirá estos contratos al motor elegido y obtendrá colores,
tipografía, fondos, rejilla, grosores y patrones del tema común/LEOC. Ninguna
lectura dependerá exclusivamente del color: patrón, grosor, opacidad y símbolos
aportarán redundancia visual y accesibilidad. Las vistas consumirán un control
propio de ProConta; no crearán trazas ScottPlot directamente ni expondrán el
motor desde su `code-behind`. Hasta que exista ese contrato compartido, Activos
se limitará a definir consultas de Aplicación que devuelvan históricos,
operaciones, flujos, divisas y periodos de exposición mediante modelos propios
sin dependencias de presentación. Su UI solo consumirá el control transversal
cuando Motor Gráficos publique una interfaz estable.

### Exposición de la cartera

La fecha de la primera compra no será un simple atributo visual. La aplicación
derivará periodos de exposición desde `OperacionesInversion`: antes de entrar o
entre una venta total y una reentrada, la serie se representará punteada, fina y
atenuada; mientras exista posición, será continua y de mayor grosor. Los dos
segmentos compartirán el punto frontera para evitar saltos visuales. Las compras,
ventas, dividendos y ajustes se marcarán mediante símbolos discretos con ayuda
contextual, sin colocar un marcador permanente en cada observación diaria.

### Lecturas que no deben mezclarse

La gráfica ofrecerá dos modos conceptualmente separados:

1. **Evolución comparativa del mercado**: precios o valores liquidativos
   normalizados a una base común para comparar instrumentos y benchmark.
2. **Resultado real de la cartera**: cantidades poseídas, entradas y salidas,
   comisiones, dividendos, fiscalidad configurada y conversión de divisa.

La primera lectura responde a cómo evolucionó el mercado; la segunda, a cuánto
ganó o perdió realmente la sociedad. La interfaz indicará siempre el modo activo
y las unidades utilizadas.

### Interacción prevista

El control común contemplará periodos `1M`, `3M`, `YTD`, `1A`, `Máx.` y rango
personalizado; filtros por instrumento, cuenta y tipo; mostrar u ocultar el
periodo previo a la inversión; normalización común y benchmark; leyenda que
permita activar series; zoom y desplazamiento horizontal; restablecimiento de
vista; cursor sincronizado; y exportación de imagen y datos.

El modelo tolerará huecos de calendario y valores ausentes sin interpolarlos a
ciegas. También reservará una estrategia de reducción de puntos para futuras
series intradía, aunque no se aplicará al histórico diario actual.

### Spike visual previo

Antes de integrar datos reales, la tarea Motor Gráficos realizará un spike
desechable y acotado con series sintéticas. Debe validar:

- entre tres y seis instrumentos simultáneos, con al menos uno en otra divisa;
- transiciones punteado/continuo, incluida una venta total y reentrada;
- eventos de compra, venta y dividendo con ayuda contextual;
- selector de periodo, leyenda interactiva, zoom, desplazamiento y cursor
  sincronizado;
- ambos modos de lectura, benchmark, huecos de calendario y exportación;
- contraste, legibilidad y coherencia con el tema ProConta a escalados de
  Windows del 100 %, 125 % y 150 %.

El spike no accederá a Access ni a servicios externos y no condicionará el
modelo de Activos con tipos ScottPlot. Su resultado será una decisión
documentada de continuar con el adaptador propuesto, ajustar sus límites o
sustituir el motor. La integración posterior se coordinará entre
`codex/activos` y `codex/graficos`: primero se estabiliza el contrato compartido,
después Activos implementa sus consultas y finalmente la UI enlaza el control.
