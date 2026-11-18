---
title: Proceso de paso a producción
depends_on: [plataforma/entrega, plataforma/configuracion, datos/esquema, implantacion/importador-contasol, implantacion/migracion-erp, contabilidad/modelos-tributarios]
status: draft
threads:
  - 2026-11-16 09:45 · Proceso de paso a producción | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G6wk4co8EFiPSGMv2WZnjZp
---
## Summary
Organiza el arranque en producción de la versión mínima viable en el equipo del propietario (aplicación en `C:\Docs\ProConta`, BD en `C:\Docs\ProConta\datos`), antes del 1/12/2026: las tareas en orden (congelar la versión, instalar ejecutables y librerías, crear la BD desde la maestra, lanzar las importaciones de ContaSol y del ERP, revisar la configuración) y las validaciones de importación y de operativa antes del corte; después del arranque, los datos que hay que rellenar antes de presentar cada modelo tributario.
No construye nada propio: usa el script de instalación de plataforma/entrega, el ActualizadorEsquema de datos/esquema, el importador de ContaSol y el MigradorERP. Es un borrador hasta que el propietario lo active, cuando acabe los últimos desarrollos.
**Aceptación:** cada paso del plan (abajo) está marcado como hecho con su comprobación, sin incidencias bloqueantes abiertas, y el propietario factura y contabiliza en producción.

## Decisions
- 2026-11-16 09:45 · Creado como hijo de implantacion. El padre sigue en `docs/implantacion.md`, sin moverlo a `docs/implantacion/README.md`, como el resto de nodos con hijos (acordado con el propietario).
- 2026-11-16 09:45 · Reparto: el procedimiento y sus listas de comprobación viven aquí; la copia de ejecutables la hace el script de instalación de plataforma/entrega (línea «Derived from» allí); la BD y el esquema, datos/esquema (`copia-estructura` y `actualizar`), sin cambios; las importaciones, implantacion/importador-contasol e implantacion/migracion-erp tal como están; sus pendientes se revisan en el paso D.
- 2026-11-18 16:21 · La lista «Modelos tributarios · Qué rellenar en producción antes de presentar» (datos-a-rellenar-en-produccion.md, del hilo de modelos tributarios, PR #123, esquema 73) se integra en el plan y no como md aparte, porque en docs cada md es un nodo: la corrección del NIF y los códigos postales de los socios va en el ERP antes de migrar (paso D); el resto es el paso J, después del arranque y antes de presentar cada modelo (acordado con el propietario).

## Plan (borrador, a revisar al activar el nodo)
Cada paso se marca con la fecha en que se hace y cómo se comprobó.

**A. Congelar la versión**
- [ ] Decidir qué entra en el arranque: liquidación de IVA (esquema 56), conciliación N43 (PR #56), conector de Claude Desktop; lo que no entre queda para después.
- [ ] Fusionar en main todo lo que entra y hacer los cambios de esquema antes del arranque.
- [ ] Ejecutar `probar.cmd` (con y sin BD de pruebas) sobre main y compilar con `compilar.cmd`.

**B. Instalar ejecutables y librerías**
- [ ] ACE x64 instalado en el equipo.
- [ ] Copiar la versión compilada a `C:\Docs\ProConta` con el script de plataforma/entrega (respaldo previo de la versión anterior si la hay).
- [ ] Acceso directo o `ejecutar.cmd` con `--bd "C:\Docs\ProConta\datos\<BD>.accdb"`; la versión de main se ve en la barra de estado.

**C. Base de datos maestra**
- [ ] Crear la BD de producción en `C:\Docs\ProConta\datos` a partir de la maestra (`ActualizadorEsquema copia-estructura`) y llevarla a la última versión (`actualizar`); `diagnosticar` sin errores.
- [ ] Primer administrador (operador Ronald; contraseña opcional).

**D. Lanzar importaciones**
- [ ] Importador de ContaSol, ejercicio a ejercicio (simulación primero), y revisión de sus incidencias.
- [ ] Antes de migrar, en el ERP: corregir el NIF del socio que pasó de NIE a NIF y añadir el código postal de los dos socios (lo piden los modelos tributarios, paso J).
- [ ] Regenerar ProcontaEstructura y ejecutar MigradorERP; revisión de sus incidencias (la actividad General queda en prorrata).
- [ ] Decidir cuáles de los pendientes conocidos de la migración bloquean el arranque: IdCentroCoste de Conceptos y LineasFactura; conceptos que solo difieren por departamento; TipoProducto por defecto del proveedor; F_MAE.NEXMAE → descripción de la cuenta; palabras clave → textos clave; CategoriasIVA → Actividades; 233 ventas 2018–2023 sin enlace con su asiento; 23 compras sin PDF; código de tipo de operación cruzado entre ContaSol y Compras.

**E. Revisar la configuración**
- [ ] Sociedades y ejercicios; Empresas › Carpeta de documentos y Ejercicios › Carpeta de facturas de compra (dentro de C:\Docs, para las copias).
- [ ] Credenciales de AWS Textract por sociedad.
- [ ] Correo: envío y recepción POP3.
- [ ] Integraciones › Avaibook · Conexión: volver a poner el token.
- [ ] VeriFactu desactivado.
- [ ] Catálogos › Cambios de deducción de las actividades: turístico 100 % deducible desde el 01/12/2026, antes de contabilizar compras de diciembre.

**F. Validar la importación**
- [ ] Sumas y saldos por ejercicio iguales a ContaSol.
- [ ] Facturas de venta por serie sin huecos y totales por ejercicio iguales al ERP.
- [ ] Libros de IVA repercutido y soportado por trimestre iguales a ContaSol.
- [ ] Terceros, activos, programadas y reservas migrados.
- [ ] Inicio › Salud de la contabilidad sin avisos inesperados.

**G. Validar la operativa**
- [ ] Importar reservas de Avaibook y «Facturar todo» un periodo; la factura se valida y contabiliza.
- [ ] Generar las facturas programadas de un mes.
- [ ] Registrar una compra desde su PDF (reconocimiento) y contabilizarla.
- [ ] Importar un fichero Norma 43.
- [ ] Pedir un informe por correo y recibirlo.

**H. Corte**
- [ ] Copia de seguridad de la BD y los documentos antes de empezar a trabajar en producción.
- [ ] ERP en solo lectura; plan de vuelta atrás (qué hacer si algo falla en los primeros días).

**I. Después del arranque**
- [ ] 1/12/2026: IVA de venta del turístico al 10 % y, después, generar las programadas de diciembre.

**J. Datos para los modelos tributarios**
Lista del hilo de modelos tributarios (pantalla fusionada en la PR #123, esquema 73). Va después del arranque, antes de presentar cada modelo: el 390 y los informativos 190, 180 y 347 en enero de 2027, el 202 en abril y el 200 en julio. Todo se rellena en Configuración › Parámetros y tablas (salvo donde se indica). Si falta un dato obligatorio, el modelo no se genera y dice qué ficha completar.

*Para todos los modelos*
- [ ] Empresas: teléfono y CNAE.

*390*
- [ ] Actividades: clave del 390, epígrafe IAE y CNAE de cada actividad. En 2025 la gestoría declaró A01 (epígrafe 8612, locales) y A03 (epígrafe 845). Revisarlo cuando la actividad General esté bien migrada.
- [ ] Cargos de la empresa: representante legal, con fecha del poder y notaría.

*190*
- [ ] Datos de los empleados (190), por cada empleado (clave A): año de nacimiento, situación familiar (y NIF del cónyuge si es la situación 2), discapacidad, contrato y movilidad geográfica.
- [ ] Familiares de los empleados (190): sus hijos y ascendientes.
- [ ] El administrador: marcado con el rol Empleado en su ficha de tercero, con su subclave (la gestoría usa la 04).
- [ ] Código postal de todos los perceptores. Si falta, sale la provincia «00».
- [ ] Corregir los NIF de profesionales que no son españoles válidos (hay uno sin letra).

*180*
- [ ] Cada factura de alquiler con retención lleva el centro de coste del inmueble.
- [ ] El inmueble alquilado está dado de alta en Activos (como ajeno si no es de la empresa), con su situación, referencia catastral y dirección (Activos › Inmuebles para los modelos).

*347*
- [ ] Situación, referencia catastral y dirección de cada local: Palafox 8, Montflorit local y Eramprunya local.
- [ ] Revisar los 37 terceros sin NIF español, por si alguno lo es.

*202*
- [ ] Empresas: CNAE.

*200*
- [ ] Ejercicios › Impuesto sobre Sociedades: plantilla media fija y no fija, y opción del 0,7 %.
- [ ] Cargos de la empresa: administradores, secretario y titulares reales (con fecha de nacimiento, nacionalidad y país de residencia), y representante legal con su poder.
- [ ] Terceros: código postal de los socios y administradores.
- [ ] Pantalla del Impuesto sobre Sociedades: revisar la «Línea del 200» de cada ajuste.
- [ ] Aplicación del resultado grabada, si hay dividendos.

## Requirements
- 2026-11-16 09:40 · Este nodo organizará las diferente tareas y validaciones que vamos a hacer para pasar a producción (copia de ejecutables y librerías a la carpeta de producción, base de datos maestra, lanzar importaciones, revisar parámetros de configuración, validaciones de importación y operativa, etc.)
- 2026-11-16 09:45 · Cuando haya acabado los últimos desarrollos, activaré este nodo para pasar a producción.
- 2026-11-16 09:45 · draft: Ejecutar el plan de paso a producción de este nodo, paso a paso y con sus validaciones, cuando el propietario lo active tras acabar los últimos desarrollos.
- 2026-11-16 14:05 · Derived from plataforma/entorno-pruebas: después de las importaciones (paso D), crear la copia de pruebas con «Actualizar la copia de pruebas…», y validar la operativa (paso G) primero en pruebas.
- 2026-11-18 16:20 · Añade este md dentro del proceso de paso a producción
