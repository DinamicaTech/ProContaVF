<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · datos

## LogMigracionesEsquema

- Dueño funcional: `datos/esquema`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdEjecucion | COUNTER | no |  |  |
| IdMigracion | TEXT(100) | no |  |  |
| FechaHoraInicio | DATETIME | no |  |  |
| FechaHoraFin | DATETIME | sí |  |  |
| Estado | TEXT(20) | no |  |  |
| PasoActual | TEXT(255) | sí |  |  |
| DescripcionError | LONGTEXT | sí |  |  |
| DetalleTecnico | LONGTEXT | sí |  |  |
| VersionAplicacion | TEXT(100) | sí |  |  |
| Equipo | TEXT(100) | sí |  |  |
| Operador | TEXT(100) | sí |  |  |
| CopiaSeguridad | TEXT(255) | sí |  |  |

Índices:
- IX_LogMigracionesEsquema_Migracion: IdMigracion
- PK_LogMigracionesEsquema (clave primaria): IdEjecucion

## VersionesEsquema

- Dueño funcional: `datos/esquema`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdMigracion | TEXT(100) | no |  |  |
| OrdenMigracion | LONG | no |  |  |
| Descripcion | TEXT(255) | no |  |  |
| FechaHoraAplicacion | DATETIME | no |  |  |
| VersionAplicacion | TEXT(100) | sí |  |  |
| HashMigracion | TEXT(64) | sí |  |  |
| DuracionMilisegundos | LONG | sí |  |  |
| CopiaSeguridad | TEXT(255) | sí |  |  |

Índices:
- PK_VersionesEsquema (clave primaria): IdMigracion
- UX_VersionesEsquema_Orden (único): OrdenMigracion

