---
title: Sociedades y ejercicios
depends_on: [datos/esquema, datos/persistencia]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 18:55 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-11-05 11:24 · Construir facturas de compra | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEbtcX2Z7baM2yhwHY2wJXj
---
## Summary
Sociedad permanente y su instancia por ejercicio (SociedadEjercicio), con consulta de ejercicios cerrados; la apertura y el cierre los hará contabilidad/procesos. Contrato `IServicioSociedades` (src/ProConta.Organizacion/Sociedades): listar sociedades y ejercicios, obtener el contexto validado (`ContextoSociedadEjercicio` de nucleo) y comprobar que un ejercicio admite cambios.
**Aceptación:** las demás áreas obtienen el contexto validado sin leer las tablas directamente.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 18:55 · Se usa la estructura actual de la maestra: Sociedades (IdSociedad TEXT(3), Nombre como razón social, CIF como NIF) y SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio TEXT(4), Nombre, Activa). Renombrar CIF/Nombre a NIF/RazonSocial como proponían los manifiestos antiguos queda fuera: no lo necesita ningún consumidor y obligaría a migrar el ERP y la facturación antigua.
- 2026-10-31 18:55 · Un ejercicio sin sociedad o sin año numérico no forma un contexto válido y no se lista.
- 2026-10-31 18:55 · Ejercicio cerrado: nueva columna SociedadesEjercicios.EjercicioCerrado (migración 20261031_1846_EJERCICIO_CERRADO, por defecto no). `ComprobarEjercicioAbiertoAsync` devuelve un error explicado si está cerrado; el acceso no lo impide (RF-SEG-02).
- 2026-11-01 09:31 · Validado por el propietario (desarrollos base).
- 2026-11-05 11:20 · La empresa guarda su domicilio fiscal (Sociedades.DireccionFiscal, CodigoPostalFiscal, PoblacionFiscal y ProvinciaFiscal), que `LectorConfiguracionEjercicio` da como emisor, y su carpeta de documentos (Sociedades.RutaDocumentos).
- 2026-11-06 13:42 · Catálogo TiposRetencion (versión 22), creado desde cero porque el ERP no tiene uno: porcentaje, descripción, clase (1 actividades y profesionales, modelo 111; 2 arrendamientos, modelo 115), vigente desde y hasta (los tipos cambian por ley: se da de alta el nuevo con su fecha en vez de cambiar el antiguo) y activo. Lectura con `CatalogoTiposRetencion.VigentesAsync(fecha)`; se escribe con el mantenimiento genérico «Tipos de retención» (Catálogos). Las facturas guardan el porcentaje y la clase, no el código del tipo. El campo de empresa «Tipo de retención» (código del ERP) sigue pendiente: no es este catálogo.
- 2026-11-07 00:19 · Derived from compras/factura-compra/contabilizacion: en Ejercicios, «Tipo de IVA general (%)» (vacío: el más alto de los tipos de IVA), «Cuenta de IVA de importación» y las cuentas de diferencias de redondeo de gastos y de ingresos (vacías: 6590000 y 7590000, compras/pantallas 2026-11-05 20:29). Las cuentas de IVA por tipo (CuentaTipoIVA0..4) son, en los datos del ERP, de IVA soportado (472); ventas las usa como repercutido: pendiente de decidir.
- 2026-11-07 00:38 · Derived from compras/pantallas: ServicioTrimestreActivo lee y cambia SociedadesEjercicios.TrimestreActivo (activar el siguiente, reactivar el anterior con las reglas de compras/pantallas 2026-11-06 12:10).
- 2026-11-07 13:00 · Cuentas de IVA del ejercicio (propietario 2026-11-07 11:59): CuentaTipoIVA0..4 son las de IVA soportado (472) que usa compras; ventas usa las nuevas CuentaIVARepercutido0..4 y, si están vacías, la 477 con la misma terminación (4720021 → 4770021). En la migración del ERP hay que rellenarlas.
- 2026-11-11 17:33 · Parámetros › Ejercicios muestra «Carpeta de facturas de compra» (compras/factura-compra 2026-11-11 17:33) y deja de mostrar «Base de datos de facturas de venta» y «Carpeta de facturas a exportar», que eran del ERP y no las usa nada; sus columnas siguen en la BD hasta terminar la migración del ERP.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 3 RF-ARQ-04: propietario de Sociedades/SociedadesEjercicios
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 5 RF-SEG-03: Sociedad y SociedadEjercicio son entidades distintas
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/ActualizadorEsquema/docs/actualizador-esquema/SOCIEDADES_Y_EJERCICIOS.md: modelo anterior
- 2026-10-28 12:28 · draft: Definir e implementar el contrato de sociedades y ejercicios.
- 2026-10-31 17:53 · Derived from architecture: el contrato de sociedades y ejercicios (listado, contexto validado y consulta de ejercicio cerrado) sobre las tablas actuales.
- 2026-11-01 17:20 · Derived from plataforma/mantenimiento: proveedores de Empresas y Ejercicios (con sus cuentas de IVA, suplidos y la configuración de facturación) para la pantalla genérica.
- 2026-11-05 11:20 · Derived from facturacion/factura-venta: domicilio fiscal y carpeta de documentos de la empresa.
- 2026-11-05 11:24 · Derived from compras: cada sociedad tiene el nombre de su carpeta raíz dentro de la carpeta de datos donde está la BD compartida; sus facturas de compra están en `<carpeta de datos>\<carpeta de la sociedad>\<ejercicio>\Proveedores\<carpeta del proveedor>`.
- 2026-11-06 13:36 · 3. Ok. No me suena que en el ERP haya una tabla de tipos de retención, por lo que podemos hacerlo desde cero.
- 2026-11-06 13:42 · Derived from compras: catálogo de tipos de retención vigentes con su descripción para la ficha de facturas recibidas.

## Hand-over
- 2026-10-31 18:55 · Hecho: contrato y servicio sobre Access, migración de EjercicioCerrado y pruebas (contexto válido e inválido, ejercicio cerrado). Pendiente: apertura de ejercicio nuevo y cierre/reapertura (contabilidad/procesos), unicidad (IdSociedad, Ejercicio) y unicidad del NIF de sociedad (la maestra tiene dos sociedades con el mismo NIF, una de pruebas), y la regla de propagación de la razón social a los ejercicios.
- 2026-11-11 17:33 · Derived from compras/factura-compra: campo «Carpeta de facturas de compra» en cada ejercicio, relativo a la carpeta de documentos de la empresa, por defecto «<año>\Proveedores».
