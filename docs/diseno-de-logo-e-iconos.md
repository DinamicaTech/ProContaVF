---
title: Diseño de logo e iconos
depends_on: [plataforma/contenedor]
status: draft
threads:
  - 2026-11-17 10:29 · Diseñar el icono de ProContaVF | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GHucmYMs2JCKwKZFq7Pfzkf
---
## Summary
Identidad visual de ProContaVF: el icono del programa (ejecutable, barra de tareas y ventanas) y, más adelante, el logotipo para documentos y pantallas.
Icono elegido: una cuña naranja (facturación) encajada en una V blanca (contabilidad y la V de VeriFactu), separadas por un hueco de grosor constante, inscritas en una pastilla azul marino con las esquinas redondeadas. Fuente en `sources/identidad/procontavf-icono.svg`; icono de Windows en `src/ProConta.App/Recursos/ProContaVF.ico`.
**Aceptación:** ProContaVF.exe se ve con el icono en el Explorador, la barra de tareas y la ventana principal, y se reconoce a 16 px en barra clara y oscura.

## Decisions
- 2026-11-17 11:22 · Icono del programa: pastilla de 240 × 240 (sobre 256) con esquinas redondeadas de radio 52 y degradado azul marino (#1E3156 → #14213A). Dentro, un triángulo invertido inscrito en la pastilla: sus dos esquinas superiores caen sobre la curva de las esquinas redondeadas y su punta toca el borde inferior, sin que nada quede cortado. El triángulo se parte en una franja superior con una cuña central naranja (#F4A259) y una V blanca que la abraza; el hueco entre ambas tiene el mismo grosor en todo el contorno (el 4,8 % del ancho del triángulo). Proporciones respecto al triángulo: franja hasta el 16,5 % de su altura, cuña hasta el 54,1 % y medio ancho de la cuña en la base el 20,2 % de su ancho.
- 2026-11-17 11:22 · El .ico lleva los tamaños 16, 20, 24, 32, 40, 48, 64, 128 y 256 px renderizados uno a uno desde el SVG (no reescalados) y se asigna como ApplicationIcon de ProConta.App; las ventanas toman el icono del ejecutable.
- 2026-11-17 11:22 · Se descartan, por este orden, P con sello (parece un letrero de parking), cuenta en T (demasiado sutil), factura con gráfico (parece un módulo de reporting), libro mayor, ticket con euro, balanza, formas abstractas (ciclo, arco, esquina doblada), dos piezas que encajan (eslabones, puzle, ganchos, arcos enganchados, círculo en onda, órbita). Bocetos en project-files `diseno-icono/`.

## Requirements
- 2026-11-17 10:29 · Ahora que ya estamos cerquita de pasar a producción, ¿Me puedes hacer una propuesta visual de un icono para ProContaVF? (tres propuesta diferentes para poder escoger)
- 2026-11-17 10:33 · Puedes diseñar tres opciones más ?
- 2026-11-17 10:45 · Puedes intentar generar una imagen menos clásica ? Puede ser totalmente abstracta o insinuar algo pero sin que se vea tan marcadamente una calculadora o un gráfico
- 2026-11-17 10:49 · Algo que muestre como dos piezas que encajen o interactúen entre ellas ? (facturacion <-> contabilidad).
- 2026-11-17 10:53 · Otra propuesta ?
- 2026-11-17 10:56 · Y algo como esto, adaptado a la paleta de ProcontaVF?
- 2026-11-17 11:00 · Sería la primera opción, pero manteniendo la equidistancia entre las dos formas.
- 2026-11-17 11:12 · si hacemos que la figura interior llegue hasta el borde de la pastilla hacemos que la pastilla se integre dentro del logo y este queda más visible.
- 2026-11-17 11:12 · (sin las esquinas naranjas fuera de la pastilla)
- 2026-11-17 11:17 · Puedes mejorar un detalle ? El icono interior algo más pequeño de forma que no quede cortado, digamos que el pixel más extremo es el que coincide con el contorno de la pastilla y aún ha de ser visible, justo al lado de ese pixel extremo ya estaría la zona fuera de la pastilla.
- 2026-11-17 11:22 · Ok, ya lo tenemos. Adelante.
