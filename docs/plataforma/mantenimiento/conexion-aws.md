---
title: Conexión de AWS en Integraciones
status: draft
depends_on: [compras/reconocimiento, datos/esquema]
threads:
  - 2026-11-24 10:17 · Valorar la integración con WhatsApp | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GJXpa4WaUhKrEnujzqPQJ6G
  - 2026-11-24 10:30 · Mover la conexión de AWS a Integraciones | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GGodXXpQvNaBaoCVe38d4dT
---
## Summary
La conexión de AWS de cada sociedad (región, clave de acceso y clave secreta cifrada) está en Parámetros y tablas › Integraciones › «AWS · Conexión» (tabla ConexionesAws), según la regla «Integraciones», y no en la configuración del reconocimiento de compras. La usan el OCR de Textract y la usará, si se hace, el intermediario de WhatsApp (creative-lab/integracion-con-whatsapp). La pantalla explica, al hacer clic en las etiquetas de las claves, cómo crear en la consola de AWS un usuario con solo el permiso de Textract y su clave de acceso. Hecho y pendiente de que el propietario lo pruebe.
**Aceptación:** las claves de AWS se ven y se cambian solo en Integraciones › «AWS · Conexión»; las claves ya guardadas siguen funcionando sin volver a escribirlas; «Probar el OCR» y el reconocimiento con Textract funcionan igual que antes.

## Decisions
- 2026-11-24 10:17 · Creado como borrador desde el debate de creative-lab/integracion-con-whatsapp, a petición del propietario.
- 2026-11-24 10:30 · «AWS · Conexión» en Integraciones (ProveedorConexionAws, clave «conexionaws»), por sociedad: región, clave de acceso, clave secreta (se escribe sin verse, se guarda cifrada con SecretoProtegido como antes, nunca se vuelve a mostrar y, vacía, se conserva) y «Clave secreta guardada». Acciones «Probar la conexión» (la página de prueba de Textract de compras/reconocimiento 2026-11-07 18:27) y «Quitar las claves». Las etiquetas de región y claves abren la explicación «cómo conseguir las claves»: usuario de IAM sin acceso a la consola, política con solo textract:DetectDocumentText (Textract no se «activa»: basta el permiso), clave de acceso «Aplicación que se ejecuta fuera de AWS» (la secreta solo se ve al crearla), región (eu-west-1 o eu-central-1) y coste por página. Sin claves, cada equipo sigue usando su perfil de AWS «proconta-textract».
- 2026-11-24 10:30 · Esquema 82 (`MigracionesPlataforma.ConexionAws`, 20261124_1300_CONEXION_AWS, orden 261124130): tabla ConexionesAws (IdSociedad, Region, ClaveAcceso, SecretoProtegido), dueña plataforma/mantenimiento/conexion-aws; la migración copia las claves guardadas en ConfiguracionReconocimientoFacturasCompra (el secreto ya cifrado, tal cual) y quita RegionAWS, ClaveAccesoAWS y SecretoAWSProtegido, a petición del propietario. El código de la conexión vive en el proyecto de compras porque su única usuaria hoy es Textract; si se hace el intermediario de WhatsApp, se lleva a un proyecto común.

## Requirements
- 2026-11-24 10:16 · Quizá AWS debería estar en el menú de integraciones ?
- 2026-11-24 10:17 · draft: Mover la conexión de AWS de cada sociedad (región, clave de acceso y secreto cifrado), que hoy está en la configuración del reconocimiento de facturas de compra, a Parámetros y tablas › Integraciones › «AWS · Conexión», para que la compartan Textract y, si se hace, el intermediario de WhatsApp.
- 2026-11-24 10:20 · En la pantalla de AWS, explicar cómo se ha de obtener el token de desarrollador y qué funcionalidad hay que activar y dónde encontrarla.
- 2026-11-24 10:23 · Si, eliminamos columnas antiguas, es 'garbage'
