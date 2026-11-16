<!-- Generado por ProConta.Datos.Esquema.Consola (comando «diccionario») desde la maestra. No editar a mano: volver a generarlo. -->

# Diccionario de datos · nominas

## AliasCamposNomina

- Dueño funcional: `nominas`
- Qué campo de la nómina es cada cabecera del Excel de la gestoría, por sociedad (se aprende la primera vez).

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdAliasCampoNomina | COUNTER | no |  | Identificador del alias. |
| IdSociedad | TEXT(3) | no |  | Sociedad del alias. |
| Cabecera | TEXT(100) | no |  | Cabecera del Excel normalizada (mayúsculas, sin acentos); «Columna N» si está vacía. |
| Campo | TEXT(50) | sí |  | Campo de Nominas al que corresponde; vacío = la columna se ignora. |

Índices:
- FK_AliasCamposNomina_Sociedad: IdSociedad
- PK_AliasCamposNomina (clave primaria): IdAliasCampoNomina
- UX_AliasCamposNomina_SociedadCabecera (único): IdSociedad, Cabecera

Relaciones:
- FK_AliasCamposNomina_Sociedad: (IdSociedad) → Sociedades (IdSociedad); borrado NO ACTION, actualización NO ACTION

## ImportacionesNomina

- Dueño funcional: `nominas`
- Cada fichero de nóminas importado (resumen de la gestoría), con su periodo, tipo de paga y estado.

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdImportacionNomina | COUNTER | no |  | Identificador de la importación. |
| IdSociedadEjercicio | LONG | no |  | Sociedad y ejercicio de las nóminas. |
| NombreFichero | TEXT(255) | no |  | Nombre del fichero Excel. |
| Huella | TEXT(64) | no |  | SHA-256 del contenido: el mismo fichero no se importa dos veces. |
| Origen | TEXT(20) | no |  | FICHERO (desde la pantalla), CORREO (adjunto recibido) o MIGRACION (histórico de ProConta antiguo). |
| IdAdjuntoRecibido | LONG | sí |  | Adjunto del correo del que viene, si llegó por correo. |
| CodigoEmpresaGestoria | TEXT(20) | sí |  | Código de la empresa en la gestoría («Empresa: 1804-…»). |
| NifEmpresa | TEXT(20) | sí |  | NIF de la empresa que trae el fichero; tiene que ser el de la sociedad. |
| TextoPaga | TEXT(100) | sí |  | Texto de la paga en el fichero («PAGA MENSUAL + FINIQUITO», «PAGA EXTRA»…). |
| TipoPaga | TEXT(10) | no |  | MENSUAL o EXTRA. |
| PeriodoDesde | DATETIME | sí |  | Primer día del periodo («DEL …»). |
| PeriodoHasta | DATETIME | sí |  | Último día del periodo («AL …»). |
| FechaNomina | DATETIME | no |  | Fecha de la nómina: la fecha de cobro si la trae, si no el último día del periodo. |
| Estado | TEXT(20) | no |  | PENDIENTE (lista para contabilizar), INCIDENCIAS (algo por resolver), CONTABILIZADA, REEMPLAZADA (otra versión la sustituye) o DESHECHA. |
| IdImportacionSustituida | LONG | sí |  | Versión anterior del mismo periodo y paga a la que sustituye. |
| Incidencias | LONGTEXT | sí |  | Lo que falta por resolver o las advertencias de la lectura, una por línea. |
| FechaHoraImportacion | DATETIME | no |  | Cuándo se importó. |
| Operador | TEXT(100) | sí |  | Quién la importó (o el proceso de correo). |
| FechaHoraEstado | DATETIME | sí |  | Cuándo cambió de estado por última vez. |
| OperadorEstado | TEXT(100) | sí |  | Quién cambió el estado. |
| MotivoEstado | LONGTEXT | sí |  | Motivo del último cambio de estado (deshacer, reemplazar). |

Índices:
- FK_ImportacionesNomina_Adjunto: IdAdjuntoRecibido
- FK_ImportacionesNomina_Ejercicio: IdSociedadEjercicio
- FK_ImportacionesNomina_Sustituida: IdImportacionSustituida
- IX_ImportacionesNomina_Periodo: IdSociedadEjercicio, PeriodoDesde, TipoPaga
- PK_ImportacionesNomina (clave primaria): IdImportacionNomina
- UX_ImportacionesNomina_Huella (único): IdSociedadEjercicio, Huella

Relaciones:
- FK_ImportacionesNomina_Adjunto: (IdAdjuntoRecibido) → AdjuntosRecibidosCorreo (IdAdjuntoRecibido); borrado NO ACTION, actualización NO ACTION
- FK_ImportacionesNomina_Ejercicio: (IdSociedadEjercicio) → SociedadesEjercicios (IdSociedadEjercicio); borrado NO ACTION, actualización NO ACTION
- FK_ImportacionesNomina_Sustituida: (IdImportacionSustituida) → ImportacionesNomina (IdImportacionNomina); borrado NO ACTION, actualización NO ACTION

## Nominas

- Dueño funcional: `nominas`
- Una nómina: la fila de un trabajador en un fichero importado, con los importes que usa la plantilla «Nómina».

| Campo | Tipo | Nulo | Por defecto | Descripción |
|---|---|---|---|---|
| IdNomina | COUNTER | no |  | Identificador de la nómina. |
| IdImportacionNomina | LONG | no |  | Importación de la que viene. |
| Ordinal | LONG | no |  | Orden de la fila dentro del fichero (1, 2…). |
| FilaOrigen | LONG | sí |  | Fila del Excel. |
| IdEmpleado | LONG | sí |  | Empleado reconocido; vacío mientras el trabajador está sin resolver. |
| CodigoTrabajador | TEXT(30) | sí |  | Código del trabajador en la gestoría, tal como viene. |
| NombreOrigen | TEXT(150) | no |  | Nombre del trabajador tal como viene. |
| NifOrigen | TEXT(20) | sí |  | NIF del trabajador si el fichero lo trae. |
| TipoPaga | TEXT(10) | no |  | MENSUAL o EXTRA. |
| FechaNomina | DATETIME | no |  | Fecha de la nómina. |
| Bruto | CURRENCY | sí |  | Total devengado (bruto). |
| DtoReEsp | CURRENCY | sí |  | Descuento por retribución en especie (llega en negativo). |
| InRetEsp | CURRENCY | sí |  | Ingreso a cuenta por retribución en especie. |
| SSEmp | CURRENCY | sí |  | Seguridad Social a cargo de la empresa. |
| TC1 | CURRENCY | sí |  | Total de la liquidación a la Seguridad Social (empresa + cuotas obreras). |
| Autonomo | CURRENCY | sí |  | Cuota de autónomo pagada como retribución en especie. |
| IRPF111 | CURRENCY | sí |  | Retención de IRPF (modelo 111). |
| Neto | CURRENCY | sí |  | Líquido a percibir. |
| AnticOtro | CURRENCY | sí |  | Anticipos y otras deducciones. |
| RetEmbarg | CURRENCY | sí |  | Retenciones por embargo (llegan en negativo). |
| FaltaPrea | CURRENCY | sí |  | Falta de preaviso. |
| CostEmp | CURRENCY | sí |  | Coste para la empresa. |
| CostPPExt | CURRENCY | sí |  | Coste con la prorrata de pagas extra. |
| DtoVac | CURRENCY | sí |  | Descuento por vacaciones. |
| DifExtra | CURRENCY | sí |  | Diferencia de paga extra. |
| BFormCon | CURRENCY | sí |  | Bonificación por formación continua. |
| HEFestiv | CURRENCY | sí |  | Horas extra festivas. |
| RegBases | CURRENCY | sí |  | Regularización de bases. |
| EnfermedadAccidente | CURRENCY | sí |  | Enfermedad o accidente (incapacidad temporal). |
| CompensBonif | CURRENCY | sí |  | Compensación de bonificaciones. |
| BonifFundae | CURRENCY | sí |  | Bonificación FUNDAE (formación). |
| TotalCosteSS | CURRENCY | sí |  | Total coste de Seguridad Social. |
| TotalFLC_FPHIB | CURRENCY | sí |  | Total FLC / FPHIB. |
| ProrrataPagasExtra | CURRENCY | sí |  | Prorrata de pagas extra. |
| CuotasObreras | CURRENCY | sí |  | Cuotas obreras (Seguridad Social a cargo del trabajador). |
| IdAsiento | LONG | sí |  | Asiento de la nómina una vez contabilizada. |

Índices:
- FK_Nominas_Asiento: IdAsiento
- FK_Nominas_Empleado: IdEmpleado
- FK_Nominas_Importacion: IdImportacionNomina
- IX_Nominas_Asiento: IdAsiento
- IX_Nominas_Empleado: IdEmpleado
- PK_Nominas (clave primaria): IdNomina
- UX_Nominas_ImportacionOrdinal (único): IdImportacionNomina, Ordinal

Relaciones:
- FK_Nominas_Asiento: (IdAsiento) → Asientos (IdAsiento); borrado NO ACTION, actualización NO ACTION
- FK_Nominas_Empleado: (IdEmpleado) → Empleados (IdEmpleado); borrado NO ACTION, actualización NO ACTION
- FK_Nominas_Importacion: (IdImportacionNomina) → ImportacionesNomina (IdImportacionNomina); borrado NO ACTION, actualización NO ACTION

