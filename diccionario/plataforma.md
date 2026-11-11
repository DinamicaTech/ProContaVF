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
- Configuración general de la aplicación. Debe contener una única fila con IdConfiguracion=1.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdConfiguracion | BYTE | no |  | Identificador fijo de la configuración general. Único valor permitido: 1. |
| RutaDatosVeriFactu | TEXT(255) | sí |  | Carpeta raíz de datos VeriFactu. De ella se derivan Inbox, Outbox, Blockchains, Invoices y Log. |
| SistemaInformaticoNombreRazon | TEXT(120) | sí |  | Nombre o razón social del productor del sistema informático de facturación. Máximo 120 caracteres. |
| SistemaInformaticoNIF | TEXT(9) | sí |  | NIF del productor del sistema informático de facturación. Máximo 9 caracteres. |
| SistemaInformaticoNombre | TEXT(30) | sí |  | Nombre dado por el productor al sistema informático de facturación. Máximo 30 caracteres. |
| SistemaId | TEXT(2) | sí |  | Código identificador del sistema informático de facturación. Máximo 2 caracteres. |
| SistemaInformaticoVersion | TEXT(50) | sí |  | Versión instalada del sistema informático de facturación. Máximo 50 caracteres. |
| SistemaInformaticoNumeroInstalacion | TEXT(100) | sí |  | Identificador estable y único de esta instalación del sistema. No debe regenerarse ni reutilizarse en otra instalación. |
| SistemaInformaticoTipoUsoPosibleSoloVerifactu | TEXT(1) | sí |  | S=el sistema solo puede funcionar como VeriFactu; N=admite también modalidad no VeriFactu. |
| SistemaInformaticoTipoUsoPosibleMultiOT | TEXT(1) | sí |  | S=el sistema permite gestionar varios obligados tributarios; N=solo permite uno. |
| FechaModificacion | DATETIME | sí |  | Fecha y hora de la última modificación de la configuración general. |
| OperadorModificacion | TEXT(30) | sí |  | Usuario u operador que realizó la última modificación. |
| EndpointIPC | TEXT(255) | sí |  | Endpoint oficial del INE usado para consultar el IPC interanual. |
| EndpointIRAV | TEXT(255) | sí |  | Endpoint oficial del INE usado para consultar el IRAV. |
| EmailIncidencias | TEXT(255) | sí |  | Dirección que recibe resúmenes de incidencias de procesos desacoplados. Si está vacía, no se intentan enviar avisos automáticos. |
| OcultarListaOperadoresAcceso | YESNO | no | No |  |
| VeriFactuActivo | YESNO | no | No |  |
| CorreoDesviarA | TEXT(254) | sí |  |  |
| UmbralCuadreMayor | CURRENCY | sí |  |  |

Índices:
- PK_Configuracion (clave primaria): IdConfiguracion

