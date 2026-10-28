# Corte operativo de contactos libres en ProConta

El usuario eligió actualizar `C:\Docs\VeriFactu\ProConta.accdb` para las pruebas del EXE `.10` y confirmó que no la estaba usando. La autorización consta en el hilo `GOV-AUD · Auditoría y arquitectura global`, turno del 01/10/2026. El alcance fue **solo** SQL003 de contactos libres sobre el contrato instalado `TERCEROS_VF_1` con `Terceros.Email TEXT(254)`. No se aplicaron la maestra 0.24, `Email(150)` ni cambios a `ProcontaVF.accdb`.

## Ejecución

Antes de modificar el archivo se comprobó que no existía `ProConta.laccdb`, que era posible abrirlo con `FileShare.None`, y que la huella actual coincidía con la prevista. Se ejecutó `database/migrations/20261001_025_contactos_libres_terceros.ps1` con respaldo fuera del directorio de origen. El script rechazó un estado parcial, aplicó las cinco sentencias verificadas de SQL003, documentó el esquema, comprobó los datos previos y registró la migración.

| Evidencia | Resultado |
| --- | --- |
| SHA-256 de ProConta antes | `8A23330EBAFB1DE9ADEB2BA6D358C2B40243C0579736658DA1B0E556DA492539` |
| Respaldo | `C:\Docs\ProConta.Backups\ProConta.ContactosLibres.20261001190845.4e0809042d10434ab63ac2f93eab63c5.accdb` |
| SHA-256 del respaldo | `8A23330EBAFB1DE9ADEB2BA6D358C2B40243C0579736658DA1B0E556DA492539` |
| SHA-256 de ProConta después | `2E168686D3D407CC37C4B425E85BED33DC61294C6414A45A1D3CF75610AAB396` |
| SHA-256 de `ProcontaVF.accdb` | `27B201CAEE2B0C344AC5EB5F8AAB45037C47BB047B1291F9CE26881BA03B0804`, sin cambio |
| SHA-256 del SQL | `DB4AD9D645FC6AEEC51930484DD5590C6DD5A900CD435B4D0E4B371216A2DF9F` |
| Versión registrada | `20261001_002_CONTACTOS_LIBRES_TERCEROS`, orden `261001002` |

## Postcondiciones independientes

Se copió el archivo actualizado a `work/databases/ProConta.ContactosLibres.ValidacionCorte.20261001.accdb`. Los hashes de original antes y después de copiar y el de la copia fueron idénticos. Sobre esta copia, una segunda ejecución del actualizador devolvió `YA_INSTALADA` sin modificarla.

- `VersionesEsquema`: 20→21 registros.
- `LogMigracionesEsquema`: un registro `COMPLETADA` para la nueva migración.
- `ContactosLibresTercero`: 0 grupos; no se han inventado agrupaciones.
- `DatosContacto`: 2 filas previas; ninguna tiene `IdContactoLibre` asignado.
- `Terceros`: 606 filas preservadas.
- `Terceros.Email`: permanece `TEXT(254)`.
- La validación del script cubre columnas, tamaños, nulabilidad, índices, FKs, contenido previo de Terceros y DatosContacto, y recuentos de Clientes, Proveedores y Contactos.

La BD operativa actualizada queda lista para que UI-PRINCIPAL pruebe la compilación integrada. El integrador debe mantener su EXE `.10` en el contrato `Email(254)` y comprobar la ruta de datos configurada antes de entregarla. Una futura transición a la maestra `Email(150)` será otro corte.
