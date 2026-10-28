# Contactos libres agrupados: contrato mínimo

Implementado y probado: 2026-10-01. Extensión aditiva de TERCEROS_VF_1; la entrega conserva Email254, otros tamaños y versiones de esquema ajenas. No se ha aplicado sobre originales.

## Modelo y migración

- Terceros 1:N ContactosLibresTercero. Campos: IdContactoLibre autonumérico, IdTercero obligatorio, Nombre texto150 obligatorio, Departamento texto100 opcional.
- ContactosLibresTercero 1:N DatosContacto, mediante IdContactoLibre LONG nullable añadido a DatosContacto y clave externa simple al grupo. Un dato pertenece a un grupo como máximo; NULL significa dato general sin agrupar. El servicio y repositorio validan que el grupo sea del mismo titular al guardar y leer.
- La migración conserva todos los DatosContacto existentes con NULL. No deduce grupos por etiqueta ni crea Terceros, Clientes, Proveedores o Contactos. No fusiona ni duplica direcciones.
- Nombre identifica a la persona o al departamento de forma libre, sin identidad fiscal, apellidos obligatorios ni rol. Departamento es descriptivo. Se permiten nombres repetidos: la identidad del grupo es su clave.
- Activo y finalidades siguen perteneciendo a DatosContacto. ResolverEmails se ejecuta sobre todos los datos del tercero, agrupados y generales, sin cambiar las reglas acordadas.

## Mantenimiento

Alta y edición en la pestaña Contacto; datos generales y tarjetas por grupo. Cada tarjeta permite nombre, departamento, emails y teléfonos. Se pueden mover datos existentes entre grupos o devolverlos a General conservando su clave, flags e historial.

Un grupo nuevo puede retirarse del borrador; los datos se devuelven a General para no perderlos. Para un grupo ya guardado se ofrece desactivar sus datos, conservando la agrupación y auditoría. No hay borrado físico de grupos guardados.

Grupos y sus datos se guardan en la transacción de ficha con HistorialTerceros y control de concurrencia. Las claves negativas son temporales del borrador y se traducen a autonuméricos dentro de esa transacción; nunca se escriben como claves de Access.

## Compatibilidad y despliegue

Script reproducible: `database/migrations/20261001_003_contactos_libres_terceros.sql`. Debe ejecutarlo ActualizadorEsquema con copia de seguridad y exclusividad verificadas. No se ejecuta automáticamente al abrir fichas. No renombra ni modifica EstadoContratoTerceros ni aplica Email150/0.23.

Sin extensión: las fichas existentes siguen operando; la UI explica que la agrupación necesita actualización y no admite guardar grupos. Con extensión parcial o incompatible: se rechaza la operación para evitar pérdida de datos. Con extensión completa: se habilita el mantenimiento. El nuevo EXE debe incluir el delta de aplicación/persistencia/UI junto a la extensión para entregar esta función.

No falta una decisión funcional para este alcance. La aplicación a la base de destino y la integración del EXE pertenecen a sus módulos propietarios. Las fuentes, el delta y sus pruebas están disponibles para completar esas integraciones.

## Restricción comprobada de Access

La primera prueba con FK compuesta (IdTercero,IdContactoLibre) rechazó tanto la migración con datos generales existentes como guardar un dato general con IdContactoLibre NULL. ACE exige coincidencia de esa clave parcialmente nula. Por ello el delta final usa FK simple opcional; Access impide grupos inexistentes y la capa de ficha impide grupos de otro titular. No existe borrado en cascada. Escrituras SQL externas deben respetar el contrato de mantenimiento: una actualización directa que asigne un grupo de otro titular será rechazada al leer la ficha, pero la FK simple no la evita por sí sola.

## Entrega exacta para integración

Raíz absoluta: `C:\Docs\VeriFactu\ProConta.Terceros`.

- Paquete congelado: `integracion/entregas/contactos-libres-10/`.
- Manifiesto con nueve archivos, destino de integración y SHA-256: `docs/CONTACTOS_LIBRES_TERCEROS_20261001.sha256.json`.
- Aplicar sobre el paquete visual `integracion/entregas/contacto-10/` ya publicado. El nuevo delta sustituye sus tres archivos EditorDatosContacto.xaml, EditorDatosContacto.xaml.vb y VistaFichaTercero.Secciones.vb; añade VistaFichaTercero.ContactosLibres.vb, dos fuentes de aplicación y dos de persistencia. El noveno archivo es el SQL para ActualizadorEsquema.
- **Usar la variante de persistencia del paquete**, cuyo `RepositorioTercerosAccess.Ficha.vb` conserva la proyección Email <=254 del snapshot central. La fuente raíz de esa rama contiene una decisión independiente Email150 y no debe copiarse para esta entrega. El diff de la variante frente al snapshot central solo añade agrupación, además de diferencias de BOM/final de archivo.
- No copiar ServicioTerceros.vb, ContratoFinal.json, DDL002 ni proyectos completos desde la rama. El manifiesto base TERCEROS_VF_1/254 admite la extensión aditiva; no necesita ser sustituido.
- No hay nuevas dependencias de paquetes ni cambios en el adaptador INF-COR. Las acciones de correo de datos agrupados usan el mismo evento y compositor canónico.

### API añadida

`ContactoLibreTercero`: IdContactoLibre As Long, Nombre As String, Departamento As String.

`DatoContactoTercero.IdContactoLibre As Long?`: Nothing general; negativo solo mientras el grupo es nuevo en el borrador; positivo persistido.

`FichaTercero.ContactosLibres As ObservableCollection(Of ContactoLibreTercero)` y `PermiteContactosLibres As Boolean` (capacidad no serializada en la huella).

`IRepositorioContactosLibres.PermiteContactosLibres() As Boolean` es una interfaz adicional; no rompe implementaciones existentes de IRepositorioFichaTercero. RepositorioTercerosAccess la implementa. Cargar(0) consulta la capacidad; Cargar(id) recupera grupos y datos en la transacción. Guardar utiliza la capacidad real de la base y nunca confía en la bandera recibida.

ListarFichas añade una consulta por conjuntos cuando existe la extensión e incluye nombre/departamento en TextoBusqueda. No carga fichas completas por fila. ResolverEmails mantiene exactamente su implementación anterior y recibe la colección global de DatosContacto.

### Integración de esquema por ActualizadorEsquema

Incorporar exclusivamente las cinco sentencias CREATE/ALTER del SQL003 al catálogo de actualizaciones, con sus metadatos y comprobación de estructura antes de activar la función. Precondición: TERCEROS_VF_1 activo; ausencia de la extensión. El script no es idempotente: el actualizador debe reconocer el estado instalado y rechazar/reparar un corte parcial, no volver a ejecutarlo a ciegas. No altera el marcador de activación ni decide una versión global de esquema.

Postcondiciones: tabla de grupos vacía tras la migración, todas las referencias nuevas NULL, datos e identificadores anteriores intactos, PK/índices/FKs completos, Email254 conservado. El repositorio revisa campos, capacidades, nulabilidad, índices y FKs. Abrir una base parcialmente actualizada produce un error funcional; no ejecuta DDL.

Diccionario para las descripciones de Access: Nombre = nombre libre del contacto, obligatorio, hasta150; Departamento = descripción opcional, hasta100; IdContactoLibre en DatosContacto = grupo opcional del mismo tercero, NULL general; Activo/finalidades permanecen en cada dato, no se heredan de otro tercero. Sin borrado en cascada ni fusiones de identidades.

## Evidencias

- Solución completa x64: **0 errores, 36 advertencias** (WindowsBase y avisos de pruebas); `tmp/contactos-libres-solucion.log`.
- Pruebas del módulo: **20 correctas**, incluyendo tres nuevas pruebas de agrupación; las pruebas que requieren rutas de copia real se omiten en la ejecución general. Log/TRX: `tmp/contactos-libres-test.log`, `tmp/contactos-libres-test/grupos.trx`.
- Combinación con dependencias del snapshot del contenedor, Email254 y delta de agrupación: **14 correctas**, benchmark de copia omitido; `tmp/contactos-libres-compat-test.log`, `tmp/contactos-libres-compat-test/compat-grupos.trx`.
- Prueba adicional sobre copia privada de ProConta: **1 correcta**, 606 terceros previos; `tmp/contactos-libres-copia-test.log`, `tmp/contactos-libres-copia-test/copia-grupos.trx`. Compara hashes en memoria de todas las filas previas de Terceros, Clientes, Proveedores, Contactos y DatosContacto (excluyendo solo la nueva columna); compara también el listado completo antes/después. No imprime datos personales.
- SHA-256 del original antes/después de copiar y después de probar: `8A23330EBAFB1DE9ADEB2BA6D358C2B40243C0579736658DA1B0E556DA492539`. Origen leído solo como archivo para copia/hash; ACE y DAO trabajan sobre la copia `tmp/grupos-proconta-9d7fd1b898c046a4a0c9ec771f1ae3be.accdb`. Sin archivo de bloqueo al copiar. La copia se conserva ignorada por Git.
- En la copia se guarda una identidad sintética con un grupo y un email válido de 254 caracteres: tanto DatosContacto.Valor como Terceros.Email conservan el valor completo. La migración no crea roles ni terceros adicionales por agrupación.
- Pruebas sintéticas cubren auditoría, corrección del nombre, concurrencia, rollback del grupo si falla una operación posterior, movimiento conservando IdDatoContacto, finalidades globales, baja de datos, rechazo de claves ajenas, datos corruptos introducidos por SQL, instalación parcial y funcionamiento anterior sin extensión.
- UI: tarjetas con nombre/departamento/email/teléfono, alta/retirada de grupo nuevo y actualización reactiva del resumen global al editar un email agrupado. Renders revisados: `tmp/contactos-libres/grupos-amplio.png` (1180×1000) y `grupos-compacto.png` (860×1000); redistribución sin pérdida del texto editable.

## Estado para el EXE

La agrupación libre ya está desarrollada y probada; no queda como dependencia indefinida de diseño. Falta que ActualizadorEsquema incorpore/aplique este delta al destino autorizado y que UI-PRINCIPAL integre los archivos y pruebe el shell final. No se ha publicado un EXE ni aplicado la migración sobre originales desde DAT-Terceros.
