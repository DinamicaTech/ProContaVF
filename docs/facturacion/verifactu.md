---
title: VeriFactu
status: draft
depends_on: [facturacion/factura-venta, plataforma/configuracion, architecture]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-11-01 16:20 · Analizar el VeriFactu anterior | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GEyEz6FipiZodYmTieuk1Rs
---
## Summary
Intercambio fiscal con la AEAT encapsulado detrás de una interfaz propia (.NET nativo, sin COM) sobre la biblioteca VeriFactu de Irene Solutions (AGPL-3.0, la misma que ya funcionó en la versión anterior): traducción de la factura, envío, cinco resultados de un intento, reenvío técnico de las incertidumbres, consulta de registros propios antes de volver a borrador, y PDF/QR tras la aceptación.
El análisis funcional de la implementación anterior y la lista de lo que necesita el corte (certificado, entorno de pruebas de la AEAT y casos) están en "Análisis de la implementación anterior" y "Lo que necesita el corte".
**Aceptación:** la de spec § 11 (pérdida de respuesta no duplica emisión; consulta fallida no desbloquea edición; rechazo explícito informa corrección; recuperación PDF no cambia número ni cadena; entorno de pruebas imposible de confundir con producción) más la batería de casos de "Lo que necesita el corte" aceptada en el entorno de pruebas de la AEAT.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-01 16:20 · Se usa la biblioteca VeriFactu de Irene Solutions (mdiago/VeriFactu, AGPL-3.0) detrás de una interfaz propia, como en la versión anterior; no se reimplementa el protocolo de la AEAT. Requiere que ProContaVF se publique con licencia AGPL-3.0 (architecture 2026-11-01 16:20). La biblioteca se fija a una revisión concreta y solo se actualiza de forma explícita, revisando cambios y pasando todas las pruebas.
- 2026-11-01 16:20 · Se mantienen del diseño anterior, salvo lo que corrige "Lo que necesita el corte": los cinco resultados de un intento (ACEPTADO, ACEPTADO_DUPLICADO, RECHAZADO, ERROR_PREVIO_ENVIO, COMUNICACION_DUDOSA); tras una comunicación dudosa se reenvía el mismo registro (misma huella, encadenamiento y fecha de generación), nunca un alta nueva; un duplicado solo es éxito si el registro previo está Correcta o AceptadaConErrores; sin anulación registral (la anulación económica es una rectificativa 1:1); el QR se genera con la URL de validación y solo tras la aceptación; un fallo de PDF o correo no revierte la aceptación ni reemite.
- 2026-11-01 16:20 · El corte de VeriFactu corrige los siete puntos débiles de la implementación anterior (ver "Puntos débiles detectados"), como describe "Lo que necesita el corte": contraseña del certificado desprotegida antes de usarla, reenvío técnico que recupera el XML de la BD, bloqueo de envío entre procesos, versión y algoritmo de huella leídos de la configuración y entorno que decide los endpoints, rectificativas con la factura rectificada informada, validación de NIF en AEAT y VIES activada, y ningún dato del certificado en scripts versionados.
- 2026-11-01 16:28 · Certificado: uno por sociedad, instalado una sola vez en el almacén del equipo del servidor (`LocalMachine\My`), con permiso de lectura de su clave privada para el grupo de Windows de los usuarios de ProContaVF; la BD solo guarda su huella o serie. Si la biblioteca solo busca en el almacén del usuario, el adaptador carga el certificado del almacén del equipo y se lo entrega; el corte lo comprueba con "Probar configuración" desde dos usuarios distintos. Renovarlo es instalar el nuevo en el mismo almacén y cambiar la huella en la configuración de la sociedad.
- 2026-11-01 16:28 · El primer corte de VeriFactu emite solo para la sociedad 002 (Dinámica), en el entorno de pruebas de la AEAT; las demás sociedades se activan después, una a una.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 11 RF-VF-01..05: integración, traducción, estados, recuperación, PDF/QR
- 2026-10-28 12:28 · From ProConta/VeriFactu-main (disco del propietario): biblioteca VeriFactu .NET de referencia
- 2026-10-28 12:28 · draft: Integrar VeriFactu .NET detrás de una interfaz propia, con pruebas que no emiten realmente.
- 2026-11-01 15:52 · Análisis en paralelo de Verifactu anterior
- 2026-11-01 16:20 · Ok a todo, incluyendo resolver las 'incorreciones' del código anterior
- 2026-11-01 16:08 · Derived from architecture: integrar la biblioteca VeriFactu existente (AGPL-3.0) en lugar de desarrollar el protocolo.
- 2026-11-01 16:20 · [answered 2026-11-01 16:28] question: ¿Dónde se instala el certificado en el servidor? La biblioteca lo buscaba en el almacén del usuario (`CurrentUser\My`). (a) en el almacén de cada usuario de Windows que use ProContaVF (2-3 usuarios), como en la versión anterior — recomendado, no cambia nada de la biblioteca; (b) un .pfx en una carpeta protegida del servidor con su contraseña protegida, más cómodo de renovar pero con el secreto en disco; (c) en el almacén del equipo (`LocalMachine\My`), solo si se confirma que la biblioteca lo admite.
- 2026-11-01 16:20 · [answered 2026-11-01 16:28] question: ¿Qué sociedades emiten con VeriFactu en el primer corte? (a) solo la sociedad que ya se probó (002, Dinámica) — recomendado; (b) todas las que facturan hoy en el ERP.
- 2026-11-01 16:28 · El certificado mejor en el equipo ya que varios usuarios diferentes lo van a necesitar y no quiero tener que gestionar ese mantenimiento
- 2026-11-01 16:28 · Corte en Sociedad 002, correcto
- 2026-11-01 16:28 · answer: dónde se instala el certificado → en el almacén del equipo (`LocalMachine\My`), para que lo usen todos los usuarios sin mantenerlo por usuario
- 2026-11-01 16:28 · answer: sociedades del primer corte → solo la 002 (Dinámica)

## Análisis de la implementación anterior
Fuentes: `sources/traspaso-2026-10-03/referencias/Facturacion/docs/DECISIONES.md` (2026-08-09 a 2026-08-13), `MODELO_FACTURA.md`, `ARQUITECTURA.md`, `MENSAJES.md`, la migración `referencias/ActualizadorEsquema/database/migrations/20260929_016_verifactu.json` y, en el disco del propietario (fuera de Git), `C:\Docs\Codigo\ProContaVF\ProConta\ProConta.VeriFactu` (adaptador VB.NET, sus pruebas y su `docs/DECISIONES.md`) y `ProConta\VeriFactu-main`. Revisado en solo lectura el 2026-11-01.

### Biblioteca
- mdiago/VeriFactu (Irene Solutions SL), versión 1.0.65, consumida como submódulo Git `externals/VeriFactu` en el commit `f290377a1057a7b1d368716702af94c7b2fea383` mediante ProjectReference a `NetCore/VeriFactu.Net.Core.csproj` (destinos net7.0, net8.0 y netcoreapp3.1; funciona desde net10.0). `ProConta/VeriFactu-main` es una copia suelta del mismo código más los artefactos COM; no la usaba el adaptador.
- Licencia: AGPL-3.0 con una cláusula 7(a) que exige licencia comercial para uso comercial sin publicar el código propio. Al publicar ProContaVF como AGPL no hace falta esa licencia.
- Única dependencia NuGet: System.Security.Cryptography.Xml (la versión anterior la forzaba a una versión corregida por vulnerabilidades).
- La biblioteca calcula la huella SHA-256 y el encadenamiento, firma y envía por SOAP con certificado de cliente, guarda la cadena por emisor y ofrece alta (`InvoiceEntry`), reenvío técnico (`InvoiceRetrySend`), consulta (`InvoiceQuery.GetSales`), anulación (`InvoiceCancellation`), subsanación (`InvoiceFix`), cola por lotes (`InvoiceQueue`) y URL de validación (`GetUrlValidate`).
- Su configuración es global y estática (`Settings.Current`): un solo envío a la vez por proceso.

### Identidad del sistema informático
Productor Dinámica Ingeniería Suministros y Servicios, S.L. (NIF B62344502); sistema ProConta, identificador PC, versión 1.0, instalación 1; tipo de uso solo VeriFactu S, multi-OT S, indicador múltiples OT S (DECISIONES 2026-08-10).

### Configuración por sociedad
Tabla `VeriFactuConfiguracion`, una fila por `IdSociedad` (ya existe en la maestra; dueño de la tabla: este nodo): Activo, Entorno, medio del certificado (huella, serie o ruta + contraseña protegida), versión, endpoints de envío y de validación, algoritmo y codificación de la huella, omitir validación de NIF en AEAT y VIES, log, y la identidad del sistema informático. El adaptador la volcaba en memoria en cada intento; si `Activo` es falso, no envía. Validación previa: productor, sistema, instalación, los dos endpoints y al menos un medio de certificado.

### Traducción de la factura
- Cabecera: número, fecha de factura y de operación, NIF y nombre del emisor, tipo de factura (F1, F2, R1–R5) y tipo de rectificativa (S/I), identificación, país y nombre del cliente, descripción de la operación.
- Desglose: las líneas no viajan como líneas; se agrupan por impuesto, clave de régimen, calificación, causa de exención ("NA" si vacía), tipo de IVA y tipo de recargo, sumando base, cuota y recargo. Los suplidos quedan fuera del desglose y del importe total fiscal.
- Retención: fuera del desglose; el importe total enviado es Σ(base + IVA + recargo) − retención.
- Importes redondeados por línea a dos decimales, alejando de cero las mitades, antes de agregar (igual en pantalla, PDF, Access y VeriFactu).

### Ciclo de un intento
1. La factura de venta revisa sus reglas (`Revisar`); con error no numera ni envía.
2. En una transacción corta: número, fecha definitiva y paso a `PendienteValidar`. Un fallo posterior deja la factura pendiente y recuperable.
3. Intento en VeriFactu, registrado en `VeriFactuEnvios` (número de intento, entorno, estado, XML de la solicitud, huella, huella anterior, fecha-hora-huso de generación, endpoint, URL de validación) y su respuesta en `VeriFactuRespuestas` (código, estado, CSV, error, duplicado, estado del duplicado, tiempo de espera, SOAP completo).
4. Clasificación: ACEPTADO si el estado es Correcto o AceptadoConErrores; ACEPTADO_DUPLICADO si hay registro duplicado Correcta o AceptadaConErrores; RECHAZADO cualquier otro estado (también un duplicado Anulada o desconocido); ERROR_PREVIO_ENVIO cualquier fallo antes de transmitir (configuración, validación, XML); COMUNICACION_DUDOSA un fallo de transmisión sin respuesta.
5. Aceptada: la factura pasa a `Validada` con fecha-hora de emisión y CSV en el historial; una rectificativa pasa su original de `RectificacionPendiente` a `Rectificada`.
6. Siguiente intento: tras COMUNICACION_DUDOSA se reenvía el XML original con `InvoiceRetrySend` (misma huella, cadena y fecha); tras RECHAZADO o ERROR_PREVIO_ENVIO se genera un alta nueva.
- Un único validador (`ValidadorFacturasVeriFactu`) sirve a todos los consumidores: emisión manual, programadas, PDF y correo. En un lote, el rechazo de una factura no detiene las demás.

### Volver a borrador una factura pendiente
Antes de abrir a edición una `PendienteValidar` se consultan los registros propios de la AEAT del periodo (fecha de operación o, si falta, de factura) por NIF emisor, número y fecha: encontrada → `Validada` y se ofrece rectificar; sin respuesta → sigue pendiente y se propone reintentar; rechazada → vuelve a Borrador con el motivo; no encontrada → vuelve a Borrador solo si han pasado al menos 5 minutos desde el último intento. Ratificado en facturacion/factura-venta 2026-10-31 16:54.

### PDF y QR
El QR se genera (QRCoder, corrección M) con la URL de validación de la AEAT y solo tras la aceptación; sin aceptación se usa la plantilla proforma, sin número ni QR y con aviso de que no tiene validez fiscal. Factura validada sin PDF y PDF sin correo son estados normales y recuperables (nodos documentos y correo).

### Pruebas que existían
Sin llamadas reales a la AEAT: mapeo de importes y de la retención; lectura de un duplicado aceptado en un SOAP de ejemplo; clasificación reenvío técnico frente a rechazo; los cuatro desenlaces de la consulta antes de volver a borrador (con consultor simulado); orquestación por lotes (aislar rechazos, PDF que valida antes, correo solo de lo que falta); rectificativas; PDF con QR y proforma; y una comprobación local de que la configuración de pruebas carga y el certificado está en el almacén con su clave privada.
Pruebas manuales en el entorno de pruebas de la AEAT: `TEST-2026-0001` (1,21 €) aceptada con CSV, PDF con QR y correo; después 20 altas con PDF y correo y 5 rectificativas.

### Puntos débiles detectados
1. La contraseña del certificado protegida se pasaba tal cual a la biblioteca, sin desprotegerla: con un .pfx no habría funcionado. Solo funcionó porque las pruebas usaban el almacén de Windows.
2. El reenvío técnico relee el XML original de una carpeta junto al ejecutable; si cambia la carpeta o se pierde, el reenvío de una incertidumbre falla. El XML ya se guardaba en `VeriFactuEnvios.XMLSolicitud`, pero no se usaba para recuperarlo.
3. La cadena de huellas vive en `%ProgramData%\VeriFactu` de la máquina; con un único servidor es correcto, pero no se puede mover ni repartir entre equipos, y la exclusión mutua era solo dentro del proceso (varias sesiones de Escritorio remoto son varios procesos).
4. Versión, algoritmo y codificación de la huella no se leían de la tabla sino de los valores por defecto de la biblioteca; el campo `Entorno` era informativo y el entorno real lo decidían los endpoints guardados.
5. Las rectificativas no informaban las facturas rectificadas (`FacturasRectificadas`) ni, en las de sustitución, el importe rectificado; F3 no se contempla.
6. Validación de NIF en AEAT y VIES desactivada en la configuración de pruebas.
7. Un script de datos de pruebas guardaba la huella y la serie del certificado en claro; no es la clave privada, pero no se trae al repositorio.

## Lo que necesita el corte
Para el corte "facturación con VeriFactu de pruebas y PDF" (architecture 2026-10-31 08:37, paso 3). Cada punto dice cómo se comprueba.

### Certificado
- Un certificado electrónico de representante de persona jurídica (o de sello) de cada sociedad que emita, válido para el entorno de pruebas de la AEAT (el de producción sirve también en pruebas). Lo aporta el propietario; nunca entra en el repositorio, la BD ni los logs.
- Instalado en el almacén del equipo del servidor (`LocalMachine\My`), con su clave privada legible por los usuarios de ProContaVF (decisión 2026-11-01 16:28). La BD solo guarda cómo encontrarlo (huella o serie). Si se usa un .pfx, su contraseña se guarda protegida con DPAPI y se desprotege justo antes de usarla (corrige el punto débil 1).
- Comprobación: una función "Probar configuración" por sociedad que dice qué certificado ha encontrado, su titular, su caducidad y si tiene clave privada, sin enviar nada; un certificado caducado o que caduca en menos de 30 días se avisa con la acción a seguir.

### Entorno de pruebas de la AEAT
- Endpoints de pruebas (preproducción): envío `https://prewww1.aeat.es/wlpl/TIKE-CONT/ws/SistemaFacturacion/VerifactuSOAP` y validación QR `https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR`. Producción: `https://www1.agenciatributaria.gob.es/...` y `https://www2.agenciatributaria.gob.es/...`. Se toman de las constantes de la biblioteca a partir del campo `Entorno` (PRUEBAS o PRODUCCION): no se escriben a mano, de modo que el entorno no puede contradecir a los endpoints (corrige el punto débil 4).
- Imposible de confundir con producción: la sociedad muestra su entorno en la barra de estado y en la ficha de la factura; los PDF de pruebas llevan una marca "PRUEBAS — sin validez fiscal"; pasar una sociedad a PRODUCCION es una acción de administrador con confirmación que nombra la sociedad y avisa de que es irreversible para sus facturas; la BD de pruebas (copia sin datos, datos/esquema 2026-10-31 18:40) solo admite PRUEBAS.
- Salida a Internet del servidor hacia esos hosts por HTTPS con certificado de cliente.
- La cadena de huellas en `%ProgramData%\VeriFactu` del servidor, incluida en la copia de seguridad junto a la BD; la cadena de pruebas y la de producción no se mezclan (la biblioteca guarda una por emisor; se comprueba al pasar a producción).
- Un único envío a la vez entre todas las sesiones del servidor (bloqueo entre procesos, no solo dentro del proceso; corrige el punto débil 3).
- El reenvío técnico recupera el XML original desde `VeriFactuEnvios.XMLSolicitud` si falta en disco (corrige el punto débil 2).
- Versión, algoritmo y codificación de la huella leídos de la configuración de la sociedad (corrige el punto débil 4).
- Configuración de la sociedad de pruebas cargada a mano por el propietario (o por un asistente) en la BD de pruebas, nunca con un script versionado que lleve datos del certificado.

### Casos
Pruebas automáticas (sin red, con la AEAT simulada) y, después, la misma batería contra el entorno de pruebas de la AEAT, en el equipo del propietario. Los casos salen del anexo A04 (`sources/traspaso-2026-10-03/03_ANEXOS_PENDIENTES.md`) y de las pruebas anteriores:
1. Factura ordinaria F1 con un tipo de IVA → ACEPTADO, CSV, huella encadenada con la anterior, PDF con QR que abre la URL de validación.
2. Factura con varios tipos de IVA, recargo de equivalencia y retención → desglose agrupado, retención fuera del desglose y restada del total (confirmar este criterio con la documentación de la AEAT).
3. Factura con suplidos → fuera del desglose y del total fiscal, dentro del total de la factura.
4. Factura con descuentos por porcentaje e importe fijo → importes iguales en pantalla, PDF, BD y XML.
5. Factura a cero → advertencia confirmable y envío aceptado.
6. Exenta y no sujeta, con su causa o calificación → aceptada; tipo impositivo en una exenta → error antes de enviar.
7. Simplificada F2 → aceptada; su rectificativa es R5.
8. Rectificativa 1:1 R4 por diferencias → aceptada con la factura rectificada informada; la original pasa a Rectificada; borrar el borrador devuelve la original a Validada (corrige el punto débil 5).
9. Rechazo de la AEAT (por ejemplo, NIF de cliente inexistente con la validación de NIF activada) → RECHAZADO, motivo en el mensaje al operador, la factura sigue pendiente y se puede corregir tras la consulta.
10. Error previo al envío (configuración incompleta, certificado no encontrado) → ERROR_PREVIO_ENVIO, nada transmitido, mensaje con la acción a seguir.
11. Comunicación dudosa (cortar la red durante el envío) → COMUNICACION_DUDOSA; el reintento reenvía el mismo registro y queda ACEPTADO o ACEPTADO_DUPLICADO sin un alta nueva ni otra huella.
12. Duplicado de un registro aceptado → éxito; duplicado de uno anulado o desconocido → sigue pendiente.
13. Volver a borrador una pendiente: encontrada, sin respuesta, rechazada y no encontrada antes y después de 5 minutos.
14. Dos sesiones validando a la vez facturas de la misma serie → números consecutivos sin huecos y cadena sin bifurcar.
15. Programadas en lote con validación automática → un rechazo no detiene el lote.
16. Fallo de PDF o de correo tras la aceptación → la factura sigue Validada; regenerar el PDF no cambia número, huella ni QR.
17. Sociedad en PRUEBAS → marca de pruebas en pantalla y PDF; pasar a PRODUCCION exige la confirmación de administrador.
- Terminado cuando: las pruebas automáticas pasan sin red, el hilo del corte ha ejecutado la batería 1–17 contra el entorno de pruebas de la AEAT con la sociedad 002 y ha anotado sus CSV en el informe de validación, y el propietario ha comprobado en el portal de la AEAT al menos los casos 1, 8 y 11 (lo único que se le pide).

## Hand-over
- 2026-11-01 16:20 · Hecho: análisis funcional de la implementación anterior y lista de lo que necesita el corte. Pendiente: el desarrollo (draft de este nodo), que se hará después del ente FacturaVenta y con las preguntas abiertas cerradas.
