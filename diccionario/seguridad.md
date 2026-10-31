<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · seguridad

## AccesoSociedadesGrupo

- Dueño funcional: `seguridad/permisos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdGrupoOperadores | LONG | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |

Índices:
- FK_AccesoSociedadesGrupo_Grupo: IdGrupoOperadores
- FK_AccesoSociedadesGrupo_Sociedad: IdSociedad
- IX_AccesoSociedadesGrupo_Sociedad: IdSociedad
- PK_AccesoSociedadesGrupo (clave primaria): IdGrupoOperadores, IdSociedad

Relaciones:
- FK_AccesoSociedadesGrupo_Grupo: (IdGrupoOperadores) → GruposOperadores (IdGrupoOperadores); borrado NO ACTION, actualización NO ACTION
- FK_AccesoSociedadesGrupo_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## AuditoriaSeguridad

- Dueño funcional: `seguridad/acceso`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAuditoriaSeguridad | COUNTER | no |  |  |
| FechaHora | DATETIME | no |  |  |
| TipoEvento | TEXT(40) | no |  |  |
| CodigoOperador | TEXT(50) | sí |  |  |
| Actor | TEXT(50) | sí |  |  |
| Resultado | TEXT(30) | no |  |  |
| Detalle | TEXT(255) | sí |  |  |

Índices:
- IX_AuditoriaSeguridad_FechaHora: FechaHora
- PK_AuditoriaSeguridad (clave primaria): IdAuditoriaSeguridad

## GruposOperadores

- Dueño funcional: `seguridad/permisos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdGrupoOperadores | COUNTER | no |  |  |
| Codigo | TEXT(50) | no |  |  |
| Nombre | TEXT(100) | no |  |  |
| Descripcion | TEXT(255) | sí |  |  |
| Activo | YESNO | no |  |  |

Índices:
- PK_GruposOperadores (clave primaria): IdGrupoOperadores
- UX_GruposOperadores_Codigo (único): Codigo

## OperadoresGrupos

- Dueño funcional: `seguridad/permisos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdOperador | LONG | no |  |  |
| IdGrupoOperadores | LONG | no |  |  |

Índices:
- FK_OperadoresGrupos_Grupo: IdGrupoOperadores
- FK_OperadoresGrupos_Operador: IdOperador
- PK_OperadoresGrupos (clave primaria): IdOperador, IdGrupoOperadores

Relaciones:
- FK_OperadoresGrupos_Grupo: (IdGrupoOperadores) → GruposOperadores (IdGrupoOperadores); borrado NO ACTION, actualización NO ACTION
- FK_OperadoresGrupos_Operador: (IdOperador) → OperadoresSeguridad (IdOperador); borrado NO ACTION, actualización NO ACTION

## OperadoresSeguridad

- Dueño funcional: `seguridad/acceso`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdOperador | COUNTER | no |  |  |
| Nombre | TEXT(100) | no |  |  |
| Email | TEXT(254) | sí |  |  |
| Activo | YESNO | no |  |  |
| HashPassword | BINARY(32) | sí |  |  |
| SaltPassword | BINARY(16) | sí |  |  |
| IteracionesPassword | LONG | sí |  |  |
| PasswordTemporal | YESNO | no |  |  |
| FechaHoraPassword | DATETIME | sí |  |  |

Índices:
- PK_OperadoresSeguridad (clave primaria): IdOperador
- UX_OperadoresSeguridad_Nombre (único): Nombre

## PreferenciasAccesoEjercicio

- Dueño funcional: `seguridad/acceso`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdOperador | LONG | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |
| IdSociedadEjercicio | LONG | no |  |  |

Índices:
- FK_PreferenciasAccesoEjercicio_Ejercicio: IdSociedadEjercicio
- FK_PreferenciasAccesoEjercicio_Operador: IdOperador
- FK_PreferenciasAccesoEjercicio_Sociedad: IdSociedad
- PK_PreferenciasAccesoEjercicio (clave primaria): IdOperador, IdSociedad

Relaciones:
- FK_PreferenciasAccesoEjercicio_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_PreferenciasAccesoEjercicio_Operador: (IdOperador) → OperadoresSeguridad (IdOperador); borrado NO ACTION, actualización NO ACTION
- FK_PreferenciasAccesoEjercicio_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## PreferenciasAccesoOperador

- Dueño funcional: `seguridad/acceso`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdOperador | LONG | no |  |  |
| IdSociedad | TEXT(3) | no |  |  |

Índices:
- FK_PreferenciasAccesoOperador_Operador: IdOperador
- FK_PreferenciasAccesoOperador_Sociedad: IdSociedad
- PK_PreferenciasAccesoOperador (clave primaria): IdOperador

Relaciones:
- FK_PreferenciasAccesoOperador_Operador: (IdOperador) → OperadoresSeguridad (IdOperador); borrado NO ACTION, actualización NO ACTION
- FK_PreferenciasAccesoOperador_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## RestriccionesReportsGrupo

- Dueño funcional: `seguridad/permisos`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdGrupoOperadores | LONG | no |  |  |
| CodigoReport | TEXT(100) | no |  |  |

Índices:
- FK_RestriccionesReports_Grupo: IdGrupoOperadores
- PK_RestriccionesReportsGrupo (clave primaria): IdGrupoOperadores, CodigoReport

Relaciones:
- FK_RestriccionesReports_Grupo: (IdGrupoOperadores) → GruposOperadores (IdGrupoOperadores); borrado NO ACTION, actualización NO ACTION

