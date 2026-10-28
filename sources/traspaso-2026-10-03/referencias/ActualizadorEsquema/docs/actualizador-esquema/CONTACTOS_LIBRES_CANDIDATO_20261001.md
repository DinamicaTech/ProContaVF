# Candidato de ProConta para contactos libres

**Estado posterior:** el usuario autorizó más tarde actualizar `C:\Docs\VeriFactu\ProConta.accdb`; el corte real y sus hashes figuran en `CORTE_CONTACTOS_LIBRES_PROCONTA_20261001.md`. Lo que sigue documenta el candidato preparado antes de ese corte.

Preparado el 01/10/2026 para la integración del EXE `.10`. Este corte conserva el contrato `TERCEROS_VF_1` instalado con `Terceros.Email TEXT(254)`. Es independiente de la maestra 0.23 y de la copia experimental con `Email(150)`. No se ha aplicado a `C:\Docs\VeriFactu\ProConta.accdb` ni a `C:\Docs\VeriFactu\ProcontaVF.accdb`.

## Procedencia

| Objeto | Ruta | SHA-256 |
| --- | --- | --- |
| Origen cerrado, antes y después de copiar | `C:\Docs\VeriFactu\ProConta.accdb` | `8A23330EBAFB1DE9ADEB2BA6D358C2B40243C0579736658DA1B0E556DA492539` |
| SQL003 recibido de DAT-TERCEROS | `database/migrations/20261001_025_contactos_libres_terceros.sql` | `DB4AD9D645FC6AEEC51930484DD5590C6DD5A900CD435B4D0E4B371216A2DF9F` |
| Candidato para integración | `work/databases/ProConta.ContactosLibres.Candidato.20261001.final.accdb` | `E9E44FF399EC9BD38D65391D1876BCAB2CDC8D8E4821C407F8959331DF12CD9D` |

El paquete de origen es `C:\Docs\VeriFactu\ProConta.Terceros\integracion\entregas\contactos-libres-10\`, manifiesto `docs/CONTACTOS_LIBRES_TERCEROS_20261001.sha256.json`. El SQL copiado conserva exactamente su SHA-256.

## Contrato de actualización

`database/migrations/20261001_025_contactos_libres_terceros.ps1` exige hash inicial, ausencia de `.laccdb`, apertura exclusiva, `TERCEROS_VF_1` activo, `Terceros.Email TEXT(254)` y extensión ausente. Crea un backup fuera del directorio de la base antes de las cinco sentencias del SQL003. Si detecta instalación parcial sin versión registrada, rechaza el cambio antes del backup y del DDL; ACE no garantiza rollback de DDL. Registra `20261001_002_CONTACTOS_LIBRES_TERCEROS` con orden `261001002` y la huella del SQL en `VersionesEsquema`, así como inicio y fin en `LogMigracionesEsquema`.

Comprueba la tabla, cuatro campos, tres índices, dos FKs, nulabilidad y tamaños. Documenta en DAO la tabla, campos e índices nuevos. Exige tabla de grupos vacía y `IdContactoLibre=NULL` en los datos previos. Compara hashes de contenido de Terceros y de todos los datos de contacto previos, además de los recuentos de Clientes, Proveedores y Contactos. Una segunda ejecución comprueba estructura y huella y devuelve `YA_INSTALADA` sin cambios.

Ensayo de la captura: 20→21 versiones; 606 Terceros y 2 DatosContacto preservados; ningún grupo creado automáticamente. La ejecución parcial ensayada (solo `CREATE TABLE`) se rechazó antes del backup y del DDL adicional. El EXE integrado debe usar este candidato separado para sus pruebas de login y agrupación; no debe sustituir todavía la ruta operativa autorizada.

## Aplicación posterior a un destino autorizado

1. Cerrar Access y el EXE; comprobar que no exista `.laccdb`. Crear primero una copia privada del destino y verificar su SHA-256 antes y después de copiar.
2. Ejecutar sobre la **copia** desde este worktree, usando su hash exacto y un directorio de respaldo distinto del de la copia:

   ```powershell
   ./database/migrations/20261001_025_contactos_libres_terceros.ps1 `
     -RutaBase work/databases/ProConta.ContactosLibres.CopiaNueva.accdb `
     -HashEsperado <SHA256_DE_LA_COPIA> `
     -DirectorioBackups work/backups
   ```

3. Verificar salida `CONTACTOS_LIBRES_ACTIVO`, un único registro nuevo en ambos historiales, `Email(254)`, grupos vacíos y datos previos conservados. Probar el EXE completo con esa copia.
4. Si se interrumpe tras crear el respaldo, **no reejecutar** sobre la base parcial: restaurar el archivo de backup bajo exclusividad y repetir desde el original cerrado. Antes de cualquier corte operativo, repetir captura y validación de hash porque el destino puede haber cambiado.

El manifiesto separado `database/migrations/20261001_024_contactos_libres_maestra.json` incorpora el mismo submodelo a una **maestra nueva 0.24**, posterior al contrato `Email(150)` de la maestra 0.23. Está en `work/databases/ProConta.Maestra.ContactosLibres.0.24.accdb`, SHA-256 `A86FA74B1BDB26B88B1A9F258B8FC3D16F52A8E086CA9C114C4697A18E03048D`; su diagnóstico reconoce 131 tablas y 24 versiones sin hallazgos. No forma parte del candidato del EXE `.10`, que conserva `Email(254)`. Un corte posterior deberá resolver ambos contratos expresamente.
