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
- 2026-11-24 12:15 · Activado por el propietario. Claude tiene acceso total a `C:\Docs\ProConta` y subcarpetas. El ERP y ContaSol no tienen cambios desde hace una semana: sus copias en la carpeta del repositorio son el origen de la importación. Los pendientes conocidos de la migración del ERP (paso D) no bloquean el arranque y se revisan después.
- 2026-11-24 13:05 · Origen de la importación: ContaSol 0022018…0022026 (los 0032018/0032021 eran copias corregidas de un ensayo y el propietario los borró; la corrección de la 4770000 se hizo de nuevo en los originales) y el ERP `ProcontaEstructurav2.accdb`, con los maestros saneados por el propietario: no se arrastran clientes ni conceptos históricos que ya no son de la actividad, así que las ventas y líneas antiguas que los usaban quedan sin cliente o sin concepto (aceptado por el propietario 2026-11-24 12:52).
- 2026-11-24 13:05 · La migración 20261117_2035_ACTIVO_LINEAS_VENTA (esquema 71) se aplica en la BD vacía, antes de cargar el ERP, y después el diagnóstico la da por parcial («MigracionParcial»). En producción se rellenó a mano con `RellenoActivoLineas.AplicarAsync` (1.706 líneas de factura y 25 de programada), tras copiar la BD en `datos\implantacion`. Pendiente para una BD nueva: que la migración del ERP haga ese relleno al terminar (implantacion/migracion-erp).

## Plan (borrador, a revisar al activar el nodo)
Cada paso se marca con la fecha en que se hace y cómo se comprobó.

**A. Congelar la versión**
- [x] Decidir qué entra en el arranque: liquidación de IVA (esquema 56), conciliación N43 (PR #56), conector de Claude Desktop; lo que no entre queda para después. — 2026-11-24: entra todo lo fusionado; main en el esquema 82, sin PR abiertas.
- [x] Fusionar en main todo lo que entra y hacer los cambios de esquema antes del arranque. — 2026-11-24: hecho por el propietario.
- [x] Ejecutar `probar.cmd` (con y sin BD de pruebas) sobre main y compilar con `compilar.cmd`. — 2026-11-24: compila; en paralelo fallaron 5 pruebas contra Access por bloqueos («Demasiados usuarios activos»), que pasan repetidas una a una; `ProgramadasAccessTests.DosLotesALaVezNoDuplicanFacturas` (tres lotes a la vez) solo falla con la máquina cargada: no afecta con un usuario.

**B. Instalar ejecutables y librerías**
- [x] ACE x64 instalado en el equipo. — 2026-11-24: ya estaba (lo usan las pruebas y las herramientas).
- [x] Copiar la versión compilada a `C:\Docs\ProConta` con `instalar.cmd` de plataforma/entrega (respaldo previo de la versión anterior en `versiones`). — 2026-11-24 12:30: instalada `ProContaVF-main.1281+313e820` (PR #154); antes se cerraron los dos procesos del conector de Claude que Claude Desktop tenía abiertos desde `C:\Docs\ProConta\Conector` (bloqueaban la copia).
- [ ] Acceso directo o `ejecutar.cmd` con `--bd "C:\Docs\ProConta\datos\<BD>.accdb"`; la versión de main se ve en la barra de estado.

**C. Base de datos maestra**
- [x] Crear la BD de producción en `C:\Docs\ProConta\datos` a partir de la maestra (`ActualizadorEsquema copia-estructura`) y llevarla a la última versión (`actualizar`); `diagnosticar` sin errores. — 2026-11-24: `C:\Docs\ProConta\datos\ProConta.accdb` desde la copia sin datos de la maestra (versión 1, `copias-limpieza-04`), actualizada al esquema 82 con `--origen-formas-pago` del ERP; diagnóstico «Actualizada» tras rellenar el activo de las líneas (ver Decisions 2026-11-24 13:05). Log e informes en `datos\implantacion`.
- [ ] Primer administrador (operador Ronald; contraseña opcional).

**D. Lanzar importaciones**
- [x] Importador de ContaSol, ejercicio a ejercicio (simulación primero), y revisión de sus incidencias. — 2026-11-24: 0022018…0022026, los 9 ejercicios «Completada» sin bloqueantes (antes se movieron en los originales los apuntes de la 4770000 a la 4770021 de 2018 y 2021, con permiso del propietario; copia previa en `datos\implantacion\respaldo-contasol`).
- [ ] Antes de migrar, en el ERP: corregir el NIF del socio que pasó de NIE a NIF y añadir el código postal de los dos socios (lo piden los modelos tributarios, paso J).
- [x] Regenerar ProcontaEstructura y ejecutar MigradorERP; revisión de sus incidencias (la actividad General queda en prorrata). — 2026-11-24: desde `ProcontaEstructurav2.accdb` (maestros saneados por el propietario), «Completada» sin bloqueantes; 394 ventas sin cliente y 5.822 líneas sin concepto, aceptado por el propietario.
- [x] Decidir cuáles de los pendientes conocidos de la migración bloquean el arranque (2026-11-24: ninguno, se revisan después): IdCentroCoste de Conceptos y LineasFactura; conceptos que solo difieren por departamento; TipoProducto por defecto del proveedor; F_MAE.NEXMAE → descripción de la cuenta; palabras clave → textos clave; CategoriasIVA → Actividades; 233 ventas 2018–2023 sin enlace con su asiento; 23 compras sin PDF; código de tipo de operación cruzado entre ContaSol y Compras.

**E. Revisar la configuración**
- [ ] Sociedades y ejercicios; Empresas › Carpeta de documentos y Ejercicios › Carpeta de facturas de compra (dentro de C:\Docs, para las copias).
- [ ] Credenciales de AWS Textract por sociedad.
- [ ] Correo: envío y recepción POP3.
- [ ] Integraciones › Avaibook · Conexión: volver a poner el token.
- [ ] VeriFactu desactivado.
- [ ] Catálogos › Cambios de deducción de las actividades: turístico 100 % deducible desde el 01/12/2026, antes de contabilizar compras de diciembre.

**F. Validar la importación**
- [x] Sumas y saldos por ejercicio iguales a ContaSol. — 2026-11-24: 2018–2026, 0 cuentas con saldo distinto (`datos\implantacion\validacion-saldos.md`).
- [x] Facturas de venta por serie sin huecos y totales por ejercicio iguales al ERP. — 2026-11-24: totales por año iguales al ERP (informe de la migración); serie 2026 con 280 facturas hasta la 83280 (siguiente 83281) y la rectificativa «R» vacía.
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
- [ ] Pasar los apuntes de la 4724001 a la 4724021 (IVA de importación al 21 %, la cuenta del ejercicio 2026 según el propietario, 2026-11-24 13:25), revisando antes cuántos hay por ejercicio y que no se descuadren los libros de IVA.

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
- 2026-11-24 12:01 · Venga, arrancamos el proceso de paso a producción
- 2026-11-24 12:15 · Tienes acceso total a c:\docs\ProConta y subcarpetas
- 2026-11-24 12:15 · answer: pendientes de la migración del ERP → no bloquean el arranque; se revisan después
- 2026-11-24 13:25 · Bueno, la correcta es 4724021 ya que el IVA de importación es 21%.
- 2026-11-24 13:25 · Prefiero cambiar luego todas los apuntes con 4724001 a 4724021
- 2026-11-24 12:15 · answer: corte del ERP y ContaSol → sin cambios desde hace una semana; las copias de la carpeta del repositorio son las más actuales

## Hand-over
- 2026-11-24 13:05 · Hechos A, B, C y D; F parcial (saldos y series). Siguiente: el propietario hace E en la aplicación (operador, carpetas, Avaibook, AWS, correo, VeriFactu, cambio de deducción, copia de pruebas, conector de Claude) y revisa Inicio › Salud; después G en la copia de pruebas y H.
