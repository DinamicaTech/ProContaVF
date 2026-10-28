# Motor Gráficos

## Decisión inicial

Se adopta ScottPlot 5 como motor inicial detrás de contratos propios de ProConta.
La decisión no forma parte del dominio: ProConta.Graficos no referencia ScottPlot
y puede sobrevivir a una sustitución.

La prueba se realiza con ScottPlot y ScottPlot.WPF 5.1.59 desde VB.NET,
net10.0-windows y x64. El paquete WPF publica destino nativo
net10.0-windows7.0. Su núcleo permite renderizar PNG sin interfaz, mientras el
control WPF aporta zoom, desplazamiento y coordenadas del puntero. Sus patrones,
series temporales y gráficos OHLC/candlestick cubren la evolución prevista.

LiveCharts2 2.x es una alternativa válida cuando animaciones, MVVM y una
presentación más ornamental pesen más. También ofrece render Skia sin UI, pero su
guía WPF pide fijar una versión mínima de Windows para evitar una resolución
inadecuada de SkiaSharp. OxyPlot 2.2 es estable, sencillo y exportable, aunque su
adaptador WPF/Skia tiene menor cadencia y una experiencia interactiva menos directa
para este spike.

Fuentes oficiales consultadas:

- https://scottplot.net/quickstart/wpf/
- https://scottplot.net/faq/version-5.0/
- https://scottplot.net/cookbook/5/Finance/
- https://www.nuget.org/packages/ScottPlot.WPF/5.1.59
- https://livecharts.dev/docs/wpf/2.0.0/overview.installation
- https://livecharts.dev/docs/wpf/2.0.0/samples.general.chartToImage
- https://www.nuget.org/packages/OxyPlot.SkiaSharp.Wpf/2.2.0

## Separación mínima

- ProConta.Graficos: contratos neutrales y cálculo puro. Distingue comparación
  normalizada de mercado de resultado real de cartera.
- ProConta.Graficos.ScottPlot: única traducción a tipos ScottPlot y render PNG
  sin UI para Reporting.
- ProConta.Graficos.Wpf: control interactivo reutilizable. LEOC puede envolverlo
  o distribuirlo sin conocer el motor.
- ProConta.Graficos.Demo: ejecutable aislado con datos sintéticos, sin Access ni
  servicios.

El cálculo de cartera —cantidades, flujos, comisiones, dividendos y conversión de
divisa— pertenece al consumidor financiero. El contrato recibe el resultado ya
calculado y lo etiqueta como ResultadoCartera; nunca lo normaliza como precio.

Los huecos se conservan y cortan el trazo. La segmentación duplica el punto anterior
al cambio de exposición. Un reductor mínimo/máximo prepara series intradía futuras
sin acoplar el contrato a una técnica concreta de renderizado.

## Estado del spike

La demo ofrece periodos, modos financieros, tramo previo atenuado y punteado,
benchmark, leyenda activable, zoom, desplazamiento, restablecimiento, lectura
sincronizada por fecha y un tooltip flotante que identifica por color la serie más
cercana y muestra fecha, valor exacto y variación porcentual. También permite
exportación PNG/CSV. Los eventos se muestran como hitos
verticales, no como marcadores permanentes de cada dato diario.

La integración posterior puede enriquecer los filtros actuales de selección única
hasta multiselección y automatizar la traducción del tema JSON de LEOC a TemaGrafico.
