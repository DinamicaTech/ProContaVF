<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · implantacion

## FasesImportacionContaSol

- Dueño funcional: `implantacion/importador-contasol`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdFaseImportacionContaSol | COUNTER | no |  |  |
| IdImportacionContaSol | LONG | no |  |  |
| CodigoFase | TEXT(50) | no |  |  |
| Estado | TEXT(30) | no |  |  |
| FechaHoraInicio | DATETIME | no |  |  |
| FechaHoraFin | DATETIME | sí |  |  |
| FilasLeidas | LONG | no |  |  |
| FilasCreadas | LONG | no |  |  |
| FilasOmitidas | LONG | no |  |  |

Índices:
- FK_Fases_Importacion: IdImportacionContaSol
- PK_FasesImportacionContaSol (clave primaria): IdFaseImportacionContaSol
- UX_FasesContaSol_Codigo (único): IdImportacionContaSol, CodigoFase

Relaciones:
- FK_Fases_Importacion: (IdImportacionContaSol) → ImportacionesContaSol (IdImportacionContaSol); borrado NO ACTION, actualización NO ACTION

## ImportacionesContaSol

- Dueño funcional: `implantacion/importador-contasol`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdImportacionContaSol | COUNTER | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |
| RutaOrigen | TEXT(255) | no |  |  |
| HuellaOrigen | TEXT(64) | no |  |  |
| VersionImportador | TEXT(30) | no |  |  |
| FechaHoraInicio | DATETIME | no |  |  |
| FechaHoraFin | DATETIME | sí |  |  |
| Operador | TEXT(100) | sí |  |  |
| Estado | TEXT(30) | no |  |  |
| Resumen | LONGTEXT | sí |  |  |

Índices:
- FK_Importaciones_Ejercicio: IdSociedadEjercicio
- FK_Importaciones_Sociedad: IdSociedad
- PK_ImportacionesContaSol (clave primaria): IdImportacionContaSol

Relaciones:
- FK_Importaciones_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_Importaciones_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## IncidenciasImportacionContaSol

- Dueño funcional: `implantacion/importador-contasol`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdIncidenciaImportacionContaSol | COUNTER | no |  |  |
| IdImportacionContaSol | LONG | no |  |  |
| Fase | TEXT(50) | sí |  |  |
| TablaOrigen | TEXT(100) | sí |  |  |
| ClaveOrigen | TEXT(150) | sí |  |  |
| CampoOrigen | TEXT(100) | sí |  |  |
| Codigo | TEXT(50) | no |  |  |
| Severidad | TEXT(20) | no |  |  |
| Explicacion | LONGTEXT | no |  |  |
| ValorOriginal | LONGTEXT | sí |  |  |
| Estado | TEXT(20) | no |  |  |

Índices:
- FK_Incidencias_Importacion: IdImportacionContaSol
- PK_IncidenciasContaSol (clave primaria): IdIncidenciaImportacionContaSol

Relaciones:
- FK_Incidencias_Importacion: (IdImportacionContaSol) → ImportacionesContaSol (IdImportacionContaSol); borrado NO ACTION, actualización NO ACTION

## MapasIdentidadContaSol

- Dueño funcional: `implantacion/importador-contasol`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdMapaIdentidadContaSol | COUNTER | no |  |  |
| IdImportacionContaSol | LONG | no |  |  |
| TipoEntidad | TEXT(50) | no |  |  |
| ClaveOrigen | TEXT(150) | no |  |  |
| IdDestino | LONG | no |  |  |

Índices:
- FK_Mapas_Importacion: IdImportacionContaSol
- PK_MapasIdentidadContaSol (clave primaria): IdMapaIdentidadContaSol
- UX_MapasContaSol_EntidadClave (único): IdImportacionContaSol, TipoEntidad, ClaveOrigen

Relaciones:
- FK_Mapas_Importacion: (IdImportacionContaSol) → ImportacionesContaSol (IdImportacionContaSol); borrado NO ACTION, actualización NO ACTION

