---
title: Sociedades y ejercicios
status: draft
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 19:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Sociedad permanente y su instancia por ejercicio (SociedadEjercicio), con consulta de ejercicios cerrados; la apertura y el cierre los hará contabilidad/procesos. Contrato `IServicioSociedades` (src/ProConta.Organizacion/Sociedades): listar sociedades y ejercicios, obtener el contexto validado (`ContextoSociedadEjercicio` de nucleo) y comprobar que un ejercicio admite cambios.
**Aceptación:** las demás áreas obtienen el contexto validado sin leer las tablas directamente.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 19:55 · Se usa la estructura actual de la maestra: Sociedades (IdSociedad TEXT(3), Nombre como razón social, CIF como NIF) y SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio TEXT(4), Nombre, Activa). Renombrar CIF/Nombre a NIF/RazonSocial como proponían los manifiestos antiguos queda fuera: no lo necesita ningún consumidor y obligaría a migrar el ERP y la facturación antigua.
- 2026-10-31 19:55 · Un ejercicio sin sociedad o sin año numérico no forma un contexto válido y no se lista.
- 2026-10-31 19:55 · Ejercicio cerrado: nueva columna SociedadesEjercicios.EjercicioCerrado (migración 20261031_1946_EJERCICIO_CERRADO, por defecto no). `ComprobarEjercicioAbiertoAsync` devuelve un error explicado si está cerrado; el acceso no lo impide (RF-SEG-02).

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 3 RF-ARQ-04: propietario de Sociedades/SociedadesEjercicios
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-03: Sociedad y SociedadEjercicio son entidades distintas
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ActualizadorEsquema/docs/actualizador-esquema/SOCIEDADES_Y_EJERCICIOS.md: modelo anterior
- 2026-10-28 12:28 · draft: Definir e implementar el contrato de sociedades y ejercicios.
- 2026-10-31 17:53 · Derived from architecture: el contrato de sociedades y ejercicios (listado, contexto validado y consulta de ejercicio cerrado) sobre las tablas actuales.

## Hand-over
- 2026-10-31 19:55 · Hecho: contrato y servicio sobre Access, migración de EjercicioCerrado y pruebas (contexto válido e inválido, ejercicio cerrado). Pendiente: apertura de ejercicio nuevo y cierre/reapertura (contabilidad/procesos), unicidad (IdSociedad, Ejercicio) y unicidad del NIF de sociedad (la maestra tiene dos sociedades con el mismo NIF, una de pruebas), y la regla de propagación de la razón social a los ejercicios.
