---
title: Motor de gráficos
status: draft
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Motor de gráficos neutral para WPF e informes: el consumidor aporta series, el motor dibuja, sin interpolar huecos.
Primera versión en `src/ProConta.UI.Base/Graficos` (WPF propio, sin bibliotecas): barras agrupadas por mes con tramo rayado apilado (proyección), líneas (con área, tramo discontinuo y marca vertical), barra horizontal con marca de referencia, barra apilada y minigráfico, todos con tooltip al pasar el ratón. La geometría y las escalas están en clases puras, probadas sin WPF. La primera pantalla que lo usa es la de inicio (fase 1); queda la franja de rango de los ratios (fase 2).
**Aceptación:** los datos del gráfico coinciden con el origen; los huecos se ven.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-15 13:30 · Hecho (fase 1 de pantalla-de-inicio): motor en `src/ProConta.UI.Base/Graficos`, dibujado con DrawingContext, sin ningún paquete nuevo. Parte pura (sin WPF): `EscalasGrafico` (máximo «redondo» 1; 1,5; 2; 2,5; 5 o 10 por potencia de 10, con negativos y el cero en una división), `GeometriaGrafico` (bandas, barras del grupo, tramos sin interpolar huecos, barra apilada con un mínimo de 4 px por tramo) y `MinigraficoGeometria`. Controles: `GraficoBarras` (barras agrupadas; la proyección de una serie se apila rayada con el borde de su color y se rotula el mes más alto), `GraficoLineas` (líneas cortadas en los huecos, área opcional al 10 %, serie discontinua con el último punto hueco, marca vertical con rótulo), `BarraReferencia` (pista, barra y raya del comparado), `BarraApilada` (tooltip por tramo con importe y peso) y `Minigrafico`. Los datos van en `DatosGrafico` (categorías y series con null = hueco; tooltip por categoría opcional; sin él, el motor lista cada serie). Tooltip flotante propio que sigue al ratón. Paleta de series fija y en orden: #2a78d6, #eb6834, #1baf7a, #eda100, #e87ba4; a partir de la sexta serie, gris (el consumidor agrupa antes en «Otras»). Rejilla #ECE8DE y textos con IBM Plex Sans de ui-base. Pendiente: la franja de rango de los ratios (fase 2 de pantalla-de-inicio).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 24 RF-GRA-01, 02: motor neutral, huecos visibles
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Graficos/docs/MOTOR_GRAFICOS.md: motor anterior
- 2026-10-28 12:28 · draft: Implementar el motor de gráficos.
- 2026-11-15 11:57 · Derived from pantalla-de-inicio: motor de gráficos en WPF propio, sin biblioteca nueva, con los tipos de la maqueta `sources/maquetas/Inicio/proconta-inicio.html`: barras agrupadas por mes con tramo rayado para la proyección, líneas (con área, tramo discontinuo y marca de «hoy»), barras horizontales con marca de referencia, barra apilada de antigüedad, minigráficos y franja de rango de los ratios; todos con tooltip al pasar el ratón y sin interpolar huecos. Fase 1 de la pantalla de inicio.
