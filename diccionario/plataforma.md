<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · plataforma

## AliasCamposConfiguracion

- Dueño funcional: `plataforma/mantenimiento`

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAlias | COUNTER | no |  |  |
| Alias | TEXT(120) | no |  |  |
| GrupoFuncional | TEXT(50) | no |  |  |
| Campo | TEXT(100) | sí |  |  |

Índices:
- IX_AliasCamposConfiguracion_Alias: Alias
- PK_AliasCamposConfiguracion (clave primaria): IdAlias

## Configuracion

- Dueño funcional: `plataforma/mantenimiento`
- ConfiguraciÃ³n general de la aplicaciÃ³n. Debe contener una Ãºnica fila con IdConfiguracion=1.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracion | BYTE | no |  | Identificador fijo de la configuraciÃ³n general. Ãšnico valor permitido: 1. |
| RutaDatosVeriFactu | TEXT(255) | sí |  | Carpeta raÃ­z de datos VeriFactu. De ella se derivan Inbox, Outbox, Blockchains, Invoices y Log. |
| SistemaInformaticoNombreRazon | TEXT(120) | sí |  | Nombre o razÃ³n social del productor del sistema informÃ¡tico de facturaciÃ³n. MÃ¡ximo 120 caracteres. |
| SistemaInformaticoNIF | TEXT(9) | sí |  | NIF del productor del sistema informÃ¡tico de facturaciÃ³n. MÃ¡ximo 9 caracteres. |
| SistemaInformaticoNombre | TEXT(30) | sí |  | Nombre dado por el productor al sistema informÃ¡tico de facturaciÃ³n. MÃ¡ximo 30 caracteres. |
| SistemaId | TEXT(2) | sí |  | CÃ³digo identificador del sistema informÃ¡tico de facturaciÃ³n. MÃ¡ximo 2 caracteres. |
| SistemaInformaticoVersion | TEXT(50) | sí |  | VersiÃ³n instalada del sistema informÃ¡tico de facturaciÃ³n. MÃ¡ximo 50 caracteres. |
| SistemaInformaticoNumeroInstalacion | TEXT(100) | sí |  | Identificador estable y Ãºnico de esta instalaciÃ³n del sistema. No debe regenerarse ni reutilizarse en otra instalaciÃ³n. |
| SistemaInformaticoTipoUsoPosibleSoloVerifactu | TEXT(1) | sí |  | S=el sistema solo puede funcionar como VeriFactu; N=admite tambiÃ©n modalidad no VeriFactu. |
| SistemaInformaticoTipoUsoPosibleMultiOT | TEXT(1) | sí |  | S=el sistema permite gestionar varios obligados tributarios; N=solo permite uno. |
| FechaModificacion | DATETIME | sí |  | Fecha y hora de la Ãºltima modificaciÃ³n de la configuraciÃ³n general. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizÃ³ la Ãºltima modificaciÃ³n. |
| EndpointIPC | TEXT(255) | sí |  | Endpoint oficial del INE usado para consultar el IPC interanual. |
| EndpointIRAV | TEXT(255) | sí |  | Endpoint oficial del INE usado para consultar el IRAV. |
| EmailIncidencias | TEXT(255) | sí |  | DirecciÃ³n que recibe resÃºmenes de incidencias de procesos desacoplados. Si estÃ¡ vacÃ­a, no se intentan enviar avisos automÃ¡ticos. |

Índices:
- PK_Configuracion (clave primaria): IdConfiguracion

