# Diagnóstico de la suite completa

La solución x64 compila. La prueba `CreaMaestraParcialYRechazaSobrescritura` pasa sobre un ACCDB nuevo. La suite completa devuelve **164 correctas y 13 fallidas, de 177**. Se compararon los nombres de las 13 pruebas fallidas con el TRX anterior a los manifiestos 004 y 005: los conjuntos son idénticos, sin fallos nuevos ni resueltos. Ninguna de esas pruebas llama al creador nuevo.

| Grupo | Pruebas | Causa observada |
|---|---:|---|
| Historial ajeno al catálogo legacy | 10 | La copia del `ProcontaVF.accdb` de pruebas contiene `20260822_003_MOTOR_SEGURIDAD`, pero `ActualizadorEsquemaAccess.CatalogoPredeterminado()` de este worktree no lo enumera. La comprobación de seguridad rechaza abrirla. No es un fallo del manifiesto nuevo. |
| Reinicio artificial del historial | 2 | `ActualizadorEsquemaAccessTests` copia la base instalada y elimina `VersionesEsquema` y `LogMigracionesEsquema`, dejando tablas y relaciones. Al volver a aplicar `MigracionBalancesPgcPymes2008`, su `CrearRelaciones` intenta crear de nuevo `FK_NodosBalance_Modelo` y ACE la rechaza. La prueba presupone una base física anterior a esas relaciones y además espera exactamente 13 migraciones, mientras la captura instalada tiene 19 registros. |
| Archivo sintético Norma43 | 1 | `InterpretaConceptosYTotales` escribe cinco registros antes del cierre `88` pero declara `000006`. El analizador informa correctamente esa discrepancia. |

Estas pruebas deben estabilizarse en sus respectivos contratos: usar una fixture versionada para las pruebas del catálogo legacy, o convertirlas en pruebas explícitas de diagnóstico de esquema desconocido; hacer que la prueba de reanudación parta de un estado físico coherente; y corregir el recuento del cierre Norma43 de la fixture. Cambiar el nuevo manifiesto para aceptar silenciosamente la base de pruebas actual ocultaría las colisiones del historial y no resolvería estas pruebas. La evaluación de transición de datos seguirá sobre copias separadas.
