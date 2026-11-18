# Contraste del modelo 200 de 2025: programa y gestoría

Generado el 2026-11-18 con la copia de pruebas `ProConta-modelos-200.accdb` (desde `ProConta-modelos-01`, con los parámetros del IS
de 2025, los socios de `ProConta-modelos-193` y los datos fiscales de la Sede de 2025) y comparado con el 200 de 2025 que presentó
la gestoría (24/07/2026). Nodo: `docs/contabilidad/modelos-tributarios/modelo-200.md`.

Lo que se pasó al cálculo del Impuesto sobre Sociedades, igual que en la aceptación de `contabilidad/procesos/impuesto-sociedades`:
el ajuste del art. 21 de 51.727,50 € (disminución permanente), la bonificación de 2.148,29 € escrita (la propuesta del programa es
1.429,32 €), los datos fiscales importados y, en la 4735079, lo contabilizado (1.668,33 €).

**Resultado:** de las 113 casillas con importe del 200 de la gestoría (páginas 3 a 8, 12 a 14 bis, 20, 20 bis, 20 quater, 26 y la de
devolución), el programa da el mismo importe en la misma casilla en 108 (95,6 %). Las 5 restantes son 2 de criterio de clasificación
y 3 del historial del límite de gastos financieros que el programa no lleva. Cabecera, claves y página 2 coinciden (ver al final).

## Casillas iguales (108)

| Página | Casillas |
|---|---|
| 3 Activo (I) | 00101 5.898.391,51 · 00111 30.013,22 · 00115 5.831.092,29 · 00126 37.286,00 · 00136 1.128.237,64 |
| 4 Activo (II) | 00149 35.826,94 · 00150 y 00152 28.608,69 · 00159 7.218,25 · 00168 385.800,97 · 00169 336.687,16 · 00177 706.609,73 · 00180 7.026.629,15 |
| 5 PN y pasivo (I) | 00185 y 00186 6.977.668,81 · 00187 y 00188 20.000,00 · 00191 y 00193 6.852.824,33 · 00199 104.844,48 · 00210, 00216 y 00222 42.518,41 |
| 6 PN y pasivo (II) | 00228, 00239 y 00249 6.441,93 · 00252 7.026.629,15 |
| 7 PyG (I) | 00255 375.716,29 · 00260 −4.652,24 · 00261 y 00760 −419,80 · 00263 −4.232,44 · 00265 3.148,96 · 00266 y 00268 2.148,96 · 00269 1.000,00 · 00270 −69.089,21 · 00271 −66.532,17 · 00274 −9,25 · 00277 −2.547,79 · 00279 −219.289,85 · 00280 −159.772,57 · 00253 −30.969,49 · 00254 −128.803,08 · 00281 −37.517,28 · 00282 −22.000,00 · 00284 −69.639,09 · 00295 −183,44 · 00296 16.011,42 |
| 8 PyG (II) | 00297, 00298 y 00300 2.454,35 · 00305 y 00307 −100,99 · 00309 11.648,62 · 00313 y 00319 86.417,55 · 00321 89.410,03 · 00323 −2.992,48 · 00324 100.419,53 · 00325 116.430,95 · 00326 −11.586,47 · 00327 y 00500 104.844,48 |
| 12 Liquidación (I) | 00500 104.844,48 · 00301 11.586,47 · 00501 116.430,95 |
| 13 Liquidación (II) | 02183 y 00418 51.727,50 |
| 14 Liquidación (III) | 00550, 00552 y 01330 64.703,45 · 00562 13.734,76 · 00576 2.148,29 · 00582 11.586,47 |
| 14 bis Liquidación (IV) | 00592 11.586,47 · 01785 2.655,25 · 01787 9.753,08 · 01766 12.408,33 · 00599 −821,86 · 00603 y 00605 2.530,54 · 00611, 01586 y 00621 −5.882,94 |
| 20 Gastos financieros | 01245, 01246 y 01248 100,99 · 01249 25.695,15 · 01250 16.011,42 · 01251 −69.639,09 · 02369 1.000.000,00 · 01256, 03585 y 01214 100,99 |
| 20 bis | 02302 y 00418 51.727,50 |
| 20 quater Aplicación del resultado | 00650, 00653, 00654, 01522 y 00666 104.844,48 |
| 26 quinquies y septies | 02986 51.727,50 · 02302 51.727,50 |
| Ingreso o devolución | 00552 64.703,45 · 00562 13.734,76 · 01586 y 00621 −5.882,94 |

En las páginas 8 y 20 el PDF de la gestoría no deja ver en qué fila va cada importe (la extracción del texto descoloca las columnas):
se han tomado las filas en el orden del PDF, y ServaliDos acepta las del programa calculando las mismas (por ejemplo, 02369 = 1.000.000).

## Diferencias (5)

| Casilla | Gestoría | Programa | Por qué |
|---|---|---|---|
| 00127 / 00133 | 37.286,00 en «Instrumentos de patrimonio» | 37.286,00 en «Resto» de inversiones financieras a largo plazo | Son las fianzas depositadas en el INCASOL (2650001…2650027). Por el PGC (260 y 265, fianzas y depósitos constituidos) no son instrumentos de patrimonio: el programa las pone en «Resto». Mismo total (00126). **Criterio a validar por el propietario.** |
| 00171 / 00175 | 49.113,81 en «Valores representativos de deuda» | 49.113,81 en «Resto» de inversiones financieras a corto plazo | Es la 5510001 «Deudas a corto plazo con socios» con saldo deudor: un crédito a socios, no valores. El programa la pone en «Resto» del modelo PYMES. Mismo total (00168). **Criterio a validar por el propietario.** |
| 01398 | 57.302,27 | — | Límite del beneficio operativo de 2023 pendiente de aplicar: historial de años anteriores que el programa no lleva. |
| 02447 / 03588, 03590, 00538, 00546 | 25.695,15 en la fila de 2025 y totales con el de 2023 (82.997,42) | 25.594,16 (límite de 2025 menos los 100,99 de gastos financieros) en la fila de 2025 y en los totales | ServaliDos calcula estas casillas y rechaza otros importes; el programa genera lo que la AEAT calcula con los datos del año. La diferencia es el historial de 2023. |

## Cabecera, claves y página 2

- Iguales: ejercicio 2025, periodo del 1/1 al 31/12, CNAE 6820, claves 00038 (arrendamiento de viviendas), 00073 (0,7 % para fines
  sociales, dato del operador), 00088 (tipo reducido por cifra de negocios inferior a 1 M€), cifra de negocios inferior a 20 M€, balance
  y PyG en el modelo PYMES, personal fijo 1,00, socios al 50 % con 10.000,00 de nominal cada uno, tipo de declaración D (devolución).
- No están en el programa (sin cambio de esquema, propuestas para el esquema 72): administradores, titulares reales con su fecha de
  nacimiento, secretario y representantes legales con su poder; el CNAE, la plantilla media y la opción del 0,7 %. En la prueba se
  pasaron a mano.
- Datos de las fichas: los socios no tienen código postal y sus NIF están sin letra; uno de ellos no coincide con el NIE que usa la
  gestoría. El programa añade la letra de control, pero la ficha hay que corregirla.
