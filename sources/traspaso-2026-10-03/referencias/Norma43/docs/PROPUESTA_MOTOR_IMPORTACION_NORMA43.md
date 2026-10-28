# Propuesta del motor de importación Norma 43

## Estado

La propuesta técnica original ya está materializada en el motor actual. Este
documento conserva el punto de entrada histórico sin duplicar una especificación
que podría quedar desactualizada.

La arquitectura implantada, sus límites, el contrato de reversión y el flujo de
interfaz se describen en [MOTOR_NORMA43.md](MOTOR_NORMA43.md). La caracterización
verificada frente al comportamiento heredado se recoge en
[RESULTADO_COMPARACION_NORMA43_20260821.md](RESULTADO_COMPARACION_NORMA43_20260821.md).

## Decisiones vigentes

- El análisis del fichero es puro y está separado de Access y de la interfaz.
- La persistencia guarda cada lote de forma transaccional y evita duplicados por
  sociedad y huella SHA-256.
- Una reversión es lógica y auditable: no elimina el fichero, sus extractos ni
  sus movimientos.
- No se permite revertir un lote de otra sociedad ni uno con imputaciones activas
  o contabilizaciones confirmadas.
- Una nueva importación del mismo fichero restaura el lote revertido existente;
  no crea una segunda copia incompatible con la restricción de unicidad.
- La pantalla operativa nueva es WPF y mantiene un origen LEOC versionado para
  continuar la migración visual de forma gradual.

## Evolución

Cualquier cambio funcional futuro debe actualizar primero la documentación del
motor y sus pruebas de caracterización. Los cambios de esquema se incorporarán
mediante una migración nueva; las migraciones publicadas no se reescriben.
