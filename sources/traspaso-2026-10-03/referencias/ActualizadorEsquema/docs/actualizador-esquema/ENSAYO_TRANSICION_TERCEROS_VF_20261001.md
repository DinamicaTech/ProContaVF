# Ensayo de transición incremental de Terceros en VF

Fecha: 01/10/2026. Alcance: `Clientes` y `Proveedores` de la base VF, sin ERP.
Ejecutor: `database/migrations/20261001_001_terceros_vf_incremental.ps1`.
Contrato físico congelado: `database/migrations/20261001_001_terceros_vf_contrato_final.json`,
SHA-256 `D2E999DD2D18C94EE968E4F1033455C6DA2C9FFF8F1EF1AF7C576540F01697E7`.
El JSON procede del contrato `TERCEROS_VF_1` publicado por DAT-TERCEROS.

## Evidencia de copia privada

- Origen de ensayo: `work/databases/ProcontaVF.Auditoria.20261001.accdb`, SHA-256
  `B5A25ECB183193F86EA9A7C28B1FEF41BC313CCC69D61BE4E4DD0772A302B992`.
- Copia migrada **para prueba funcional de DAT-TERCEROS**:
  `C:\Docs\VeriFactu\ProConta.ActualizadorEsquema\work\databases\ProcontaVF.MigracionTerceros.Final20261001.accdb`,
  SHA-256 `42151353CC259E7F7859F01A493F1D279A281115D8A78728098C2994A18B8B82`.
- Respaldo previo exclusivo:
  `work/backups/ProcontaVF.MigracionTerceros.Final20261001.Terceros.20261001-173307.B5A25ECB1831.accdb`.
  Tiene el hash del origen.
- Resultado: proceso terminó con código 0 en Windows PowerShell 5.1 x64;
  212 clientes + 394 proveedores → 606 terceros y 606 mapas de origen,
  sin fusionar registros. `VersionesEsquema` pasó de 19 a 20 sin reiniciar el
  historial. `HashMigracion` corresponde al PS1, no al archivo Access.
- Contrato `TERCEROS_VF_1` publicado **al final** en `EstadoContratoTerceros`,
  `IdPlan=3d80d18b-ba2e-4af2-bfd1-98f9a9b0e6a7`,
  `HuellaPlan=C2383D506FF5FDC44B885EE2D63CD9FEA33C7D7F26063D607499677AF5B27ACE`.
- Antes del DDL se cotejaron las huellas lógicas de **todas** las tablas y el
  esquema con el preflight, después de copiar desde un `FileStream` exclusivo.
  Tras el traslado se verificó cada campo común antes de suprimirlo, la
  totalidad de los mapas, FKs, nulabilidad e índices; facturas, asientos,
  apuntes y asociaciones conservaron recuento y huella lógica. También se
  compararon los atributos exclusivos de Clientes y Proveedores.
- La validación física contra el JSON del contrato pasó antes y después de
  añadir descripciones DAO a tablas, campos e índices nuevos. La descripción
  de `TipoPersona=NULL` conserva correctamente la `ó` de «clasificación» en
  DAO (comprobado por caracteres, sin secuencia de texto mal decodificada).
- Durante este ensayo privado, el original `C:\Docs\VeriFactu\ProcontaVF.accdb`
  conservó el hash inicial y no existía aún el clon `ProConta.accdb` del corte.
- Una segunda ejecución sobre la copia ya migrada terminó con código 1 antes
  de crear otro respaldo o escribir datos; el hash de la copia siguió siendo
  `42151353CC259E7F7859F01A493F1D279A281115D8A78728098C2994A18B8B82`.

La clasificación histórica permanece NULL. `Terceros.Activo` y
`Clientes.Activo` nacen True como estado operativo nuevo; el valor original de
`Proveedores.Activo` y `Clientes.Notas`/`FechaAlta` permanece en su rol.
Las colecciones de domicilios y datos de contacto se crean vacías; los datos
escalares siguen en `Terceros`. La migración física no certifica la autorización
de la interfaz ni los consumidores legacy.

## Aceptación del consumidor antes del corte

DAT-TERCEROS verificó la copia privada final anterior: contrato completo y
activo, listado de 606 identidades y carga individual de **606/606 fichas**.
Las 606 quedaron pendientes de clasificación y conservaron el nombre original.
La escritura sin clasificar fue rechazada según contrato; en una segunda copia
desechable, una clasificación explícita y un cambio de nota interna se guardaron
y recargaron correctamente. El ensayo no alteró la copia de lectura. Resultado
del harness x64: 1 prueba correcta, 0 fallos. Evidencia de su propietario:
`C:\Docs\VeriFactu\ProConta.Terceros\docs\ACEPTACION_FINAL_TERCEROS_20261001.md`.

## Aplicación autorizada por GOV-AUD

Tras la aceptación, GOV-AUD ejecutó el script confirmado en Git `d393d25`,
SHA-256 `D7B46085D8D7B941481768E93B14E2D56289629FDDA7881DE0F1F8E3CF91696E`,
sobre `C:\Docs\VeriFactu\ProcontaVF.accdb`. Terminó con código 0: 212 clientes,
394 proveedores, 606 terceros, `VersionesEsquema` 19→20 y contrato
`TERCEROS_VF_1` ACTIVO. `IdPlan` del corte:
`e0d57df3-8e2b-4a7f-98e9-be7680d49f6f`. La huella del plan fue
`C2383D506FF5FDC44B885EE2D63CD9FEA33C7D7F26063D607499677AF5B27ACE`.

La copia bit a bit `C:\Docs\VeriFactu\ProConta.accdb` se creó bajo exclusividad.
Ambas bases tenían SHA-256
`27B201CAEE2B0C344AC5EB5F8AAB45037C47BB047B1291F9CE26881BA03B0804`
al corte, valor que se volvió a comprobar por lectura. El respaldo original
`C:\Docs\ProConta.Backups\ProcontaVF.Terceros.20261001-173759.B5A25ECB1831.accdb`
conserva SHA-256
`B5A25ECB183193F86EA9A7C28B1FEF41BC313CCC69D61BE4E4DD0772A302B992`.
Registro estructurado de GOV-AUD:
`C:\Docs\ProConta.Backups\ActualizacionTercerosVF-20261001-173815.json`.

El ejecutable .7 no es compatible con el esquema normalizado y **no debe
utilizarse contra ninguna de estas dos bases**. Este corte no entrega un
ejecutable nuevo; el acceso real y los consumidores pendientes requieren su
propia integración y autorización.

## Reproducción en otra copia

Crear primero una copia de la captura en una ruta nueva. Desde la raíz del
worktree:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File database/migrations/20261001_001_terceros_vf_incremental.ps1 `
  -RutaBase work/databases/MI_COPIA_NUEVA.accdb `
  -HashEsperado B5A25ECB183193F86EA9A7C28B1FEF41BC313CCC69D61BE4E4DD0772A302B992 `
  -DirectorioBackups work/backups
```

El comando mostrado es solo para una nueva copia del estado anterior al corte.
El ejecutor rechazará su reejecución sobre las bases ya actualizadas.

Si falla una vez iniciado el DDL, no se reejecuta sobre el estado parcial. Bajo
la misma parada exclusiva, se conserva el fichero parcial para diagnóstico y
se restaura el `.accdb` indicado en `Backup` a la ruta de destino; se verifica
antes el SHA-256 del respaldo frente a `HashInicial`. El ejecutor rehúsa
sobrescribir un historial aplicado o una estructura parcial.
