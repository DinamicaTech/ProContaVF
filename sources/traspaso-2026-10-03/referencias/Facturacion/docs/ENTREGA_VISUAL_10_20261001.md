# Entrega WPF .10 — ProConta.accdb autorizada

Entrega completa de la revisión visual de Terceros, N43 y Configuración, contactos agrupados, compositor de correo e indicador discreto de base/modo. Sustituye al candidato previo de copia separada, que no se entregó para uso. El usuario eligió la segunda opción: actualizar `C:\Docs\VeriFactu\ProConta.accdb`. La decisión se recibió por GOV-AUD y no requiere otra aprobación.

## Abrir

- Lanzador: `C:\Docs\VeriFactu\ProConta.Facturacion\tools\ProConta.Acceso.Prototipo\bin\entrega10\x64\Debug\net10.0-windows\win-x64\Iniciar-ProConta.cmd`.
- EXE: `C:\Docs\VeriFactu\ProConta.Facturacion\tools\ProConta.Acceso.Prototipo\bin\entrega10\x64\Debug\net10.0-windows\win-x64\ProConta.Acceso.Prototipo.exe`.
- Caption: `ProConta · 1.21.0-integracion-proconta-20260822.10`.
- Rama `codex/integracion-proconta-20260822`, HEAD `35554e9f5d97163c303628ec795265af4dcbe333`, cambios locales sin commit; Debug, x64, .NET 10.

Conservar toda la carpeta de salida. El lanzador pasa `--base-datos C:\Docs\VeriFactu\ProConta.accdb --pruebas-terceros`. Guardar modifica esa base. La concesión de pruebas no acepta la antigua copia propuesta Entrega10 ni ProcontaVF.accdb. El doble clic directo en el EXE usa la misma base, pero sin conceder implícitamente el modo de edición de Terceros; utilizar el lanzador.

EXE SHA256: `388190E02970DE9615369236EECA565B5571E279CBB1CEBC825DFC4BD3D46B68`.

DLL principal SHA256: `B860809596EC245F141B153F90D34EAA1E86A82E7E52407D572F8E17BC4B19F9`. El apphost EXE conserva su huella entre candidatos de igual versión; comprobar también DLL y manifiesto de dependencias. Metadatos verificados sin ejecutar el programa únicamente para comprobar caption.

## Base actualizada

Actualizador aplicó exclusivamente el delta de contactos libres sobre el archivo vigente, con acceso exclusivo, respaldo previo y verificación independiente. Se conserva TERCEROS_VF_1 / Email(254), 606 terceros y 2 datos de contacto; no se inventaron grupos. VersionesEsquema: 20 → 21. No se aplicó la futura maestra 0.24 / Email(150).

- Base después del corte: SHA256 `2E168686D3D407CC37C4B425E85BED33DC61294C6414A45A1D3CF75610AAB396`.
- Backup: `C:\Docs\ProConta.Backups\ProConta.ContactosLibres.20261001190845.4e0809042d10434ab63ac2f93eab63c5.accdb`.
- SHA256 backup y estado anterior: `8A23330EBAFB1DE9ADEB2BA6D358C2B40243C0579736658DA1B0E556DA492539`.
- Informe propietario: `C:\Docs\VeriFactu\ProConta.ActualizadorEsquema\docs\actualizador-esquema\CORTE_CONTACTOS_LIBRES_PROCONTA_20261001.md`, commit `5b111d0`.

La validación del integrador utilizó una copia idéntica bajo `tmp/login-wpf/original-actualizada10` y copias derivadas. No añadió datos sintéticos a la base operativa. ProcontaVF.accdb permanece intacta; la entrega .9 se conserva.

## Cambios y límites

- Terceros: contacto con anchos proporcionados, relaciones y selector revisados, destinatarios efectivos y grupos libres con nombre/departamento y varios medios, guardado y recarga transaccionales.
- Configuración: once proveedores reales, carga inicial fuera del Dispatcher, paleta navy/naranja/cálida, controles armonizados, tabla con columnas proporcionadas y navegación legible. Disposición estrecha apilada y acceso al último campo mediante scroll. Confirmación central antes de eliminar, conservación al cancelar o fallar y descarte seguro tras revocación incluso con catálogos y altas nuevas.
- SEG: cada operación de Configuración revalida sesión/contexto; el ámbito CFG sin permisos configurados permite operar a sesiones válidas. Los mantenimientos son compartidos, sin filtro por sociedad/ejercicio.
- Correo: compositor canónico con borrador temporal por dirección hasta cerrar ficha; envío desconectado y remitente pendiente de conexión.
- N43: presentación revisada; conserva consulta, sin importación ni reversión.
- Base/modo: indicador compacto con detalle accesible, sin bandas técnicas permanentes. Diagnóstico de latencia en `%LOCALAPPDATA%\ProConta\Logs\latencia-wpf-10.log`; no se afirma corregir una latencia no reproducida.

CFG procede de `b3e1694` y `53df5cfd470596fc6047576d313e81f9b01570eb`. Ajuste mínimo del integrador: nombre contractual `PART_Popup` del ComboBox; origen, diferencia y hashes registrados en CFG-MANT.fuentes.json. El propietario quedó detenido por límite de uso antes de completar este ajuste; no se modificó su worktree. No se duplicó el diseño central. SEG delta `c86254f`, compositor `378b88c`.

## Comprobación

- Compilación del EXE y harness: cero errores y cero advertencias. Solución completa: cero errores y dos advertencias WindowsBase preexistentes.
- 135 garantías de integración: navegación, modal de eliminación, revocación, descarte con catálogos, selector F4/Escape y selección, compositor, grupos y límites del modo de pruebas.
- Copia de la base recién actualizada: login y ocho garantías de autoridad CFG; cinco garantías de guardar/reabrir grupos, sin migración adicional.
- 81 fuentes integradas verificadas por SHA256; manifiesto de salida en `ENTREGA_VISUAL_10_20261001.json`.
- Renders sintéticos revisados dentro del contenedor a 1380×860 y 1000×640, y contenido a 1155×720 / 775×520, incluidos campos inferiores. No se afirma réplica exacta de la maqueta ni validación real entre monitores DPI distintos.
- `git diff --check` correcto. Se preservaron cambios concurrentes; no se hizo commit de binarios, bases ni credenciales.

Evidencia interna: `tmp/entrega10-final.log`, `tmp/entrega10-final-build.log`, `tmp/entrega10-build-final.log`, `tmp/entrega10-solucion-final.log`, `tmp/entrega10-base-cfg.log`, `tmp/entrega10-base-grupos.log` y `tmp/modulos-wpf/configuracion-*.png`.

El test del selector materializa una vista y modelo independientes de los renders, espera el evento de carga mediante el Dispatcher y comprueba `IsLoaded` antes de generar F4/Escape. Antes de esa espera, WPF podía rechazar la apertura del popup todavía no cargado. Compartir el modelo con una segunda vista también interfería con la evidencia de la tabla; se aisló esa prueba. La ejecución final pasó sin activar esa ventana ni enviar teclas al escritorio. No se atribuyó ese fallo del harness al funcionamiento manual de la aplicación.
