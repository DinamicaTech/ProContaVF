<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · inicio

## HistoricoSalud

- Dueño funcional: `pantalla-de-inicio`
- Recuento diario de la salud de la contabilidad de Inicio: una fila por día y elemento (la última revisión del día sustituye). Sirve para la tendencia «desde el lunes».

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdHistoricoSalud | COUNTER | no |  | Identificador de la fila. |
| IdSociedad | TEXT(3) | no |  | Sociedad revisada. |
| Ejercicio | SHORT | no |  | Ejercicio de trabajo con el que se hizo la revisión. |
| Fecha | DATETIME | no |  | Día de la revisión (sin hora). |
| ClaveRevision | TEXT(60) | no |  | Revisión que encontró el elemento (por ejemplo, bancos-sin-asiento). |
| ClaveElemento | TEXT(120) | no |  | Clave del elemento: la revisión con su detalle (por ejemplo, el banco o el trimestre). |
| Elementos | LONG | no |  | Número de cosas que cuenta el elemento ese día (movimientos, facturas, subcuentas…); 1 si no cuenta nada. |
| Importe | CURRENCY | sí |  | Importe asociado al elemento, si lo tiene. |
| FechaHoraRegistro | DATETIME | no |  | Cuándo se guardó la fila (la última revisión del día). |

Índices:
- PK_HistoricoSalud (clave primaria): IdHistoricoSalud
- UX_HistoricoSalud_Dia (único): IdSociedad, Ejercicio, Fecha, ClaveElemento

