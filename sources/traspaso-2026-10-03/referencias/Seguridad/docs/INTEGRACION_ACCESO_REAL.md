# Integración del acceso real

Fecha: 01/10/2026. Rama `codex/seguridad`, base contrastada en copia privada de `C:/Docs/VeriFactu/ProcontaVF.accdb`. No se abrió ni modificó la original con ACE.

## API provisional consumible

La composición pública es `ComposicionNucleoFacturacion.CrearServicioAccesoReal(rutaBaseDatos)` y devuelve `ServicioAccesoReal`.

Secuencia obligatoria:

1. `ListarOperadores()` devuelve `IdentidadOperadorAcceso`, nunca `Operador`, hashes ni salts.
2. `Autenticar(idOperador, password)` devuelve `ResultadoAutenticacionAcceso`; este contiene `SesionAcceso` solo con credenciales válidas. La contraseña no se conserva.
3. `ListarSociedades(sesion)` revalida sesión, operador activo, grupos activos y asignaciones.
4. `ListarEjercicios(sesion, idSociedad)` incluye abiertos y cerrados sin cambiar su estado.
5. `CompletarAcceso(sesion, idSociedad, idSociedadEjercicio)` revalida relaciones, guarda preferencias y emite `ContextoAccesoValidado`.
6. `Revalidar(contexto)` vuelve a comprobar sesión, operador, sociedad y ejercicio. `CerrarSesion` invalida el token en memoria.

La sesión incluye `FechaHoraAutenticacion As DateTimeOffset`; su constructor es `Friend` y el servicio comprueba que el token y la instancia fueron emitidos por él. El contexto no se puede crear desde un DTO arbitrario.

El puerto `ICatalogoOrganizativoAcceso` separa la lectura de sociedades/ejercicios de Seguridad. SEG-ACCESO no mantiene ni reabre ejercicios.

## Política grupo-sociedad

- La ausencia o error de `AccesoSociedadesGrupo` detiene el flujo como incompatibilidad de esquema.
- Tabla globalmente vacía: acceso a todas las sociedades para todo operador autenticado y todavía válido, aunque no tenga grupos activos.
- Con alguna fila global: unión de las sociedades asignadas a los grupos activos del operador.
- Un grupo nuevo sin filas no concede acceso cuando la tabla ya tiene alguna asignación.
- Eliminar la última asignación cambia el sistema a modo global; el futuro mantenimiento deberá exigir confirmación explícita. Esta rama no implementa CRUD.

La implementación actual de `RepositorioAutorizacionSociedadesAccess` todavía deniega al operador sin grupos antes de comprobar si el ámbito está globalmente vacío. Esta divergencia queda registrada para una adaptación funcional posterior; este cambio documental no altera código ni bases.

## Aplicación a ámbitos nuevos

La misma semántica rige cada ámbito de permisos nuevo: ausencia total de configuración concede acceso; una vez configurado, se aplican exclusivamente sus asignaciones. La ausencia de fila para un grupo dentro de un ámbito configurado no concede permiso. Un fallo técnico o contrato físico ausente nunca equivale a ámbito vacío.

Para Terceros siguen pendientes el contrato persistente, los códigos estables y el mantenimiento por grupos. La autorización explícita para que UI-PRINCIPAL habilite lectura, edición, notas internas y perfiles en la entrega de pruebas limitada a `ProConta.accdb` es una decisión de esa entrega y no una configuración permanente de SEG-ACCESO.

## Contrato físico requerido por ActualizadorEsquema
Tipos comprobados en la base de pruebas:

- `GruposOperadores.IdGrupoOperadores`: Access `LONG`, PK.
- `OperadoresSeguridad.IdOperador`: Access `LONG` autonumérico, PK.
- `Sociedades.IdSociedad`: `TEXT(3)`, PK física denominada actualmente `PK_Empresas`.
- `SociedadesEjercicios.IdSociedadEjercicio`: `LONG`, PK.
- `SociedadesEjercicios.IdSociedad`: `TEXT(3)` nullable en la base actual.
- `SociedadesEjercicios.Ejercicio`: `TEXT(4)` nullable.
- `SociedadesEjercicios.Activa`: `YESNO NOT NULL`; es vigencia organizativa y **no** significa abierto/cerrado.
- `SociedadesEjercicios.EjercicioCerrado`: ausente en la copia actual. Cuando existe es `YESNO NOT NULL`; cuando falta el adaptador devuelve `EstaCerrado = Nothing` (desconocido), permite seleccionar y no inventa el estado.

Referencia futura ajena a la migración mínima de acceso:

```sql
ALTER TABLE SociedadesEjercicios ADD COLUMN EjercicioCerrado YESNO NOT NULL;
```

El origen canónico ya identificado es `ProConta.Asientos/database/migrations/20260907_002_asientos_automaticos.sql`. Esa migración pertenece a Asientos y no es requisito de la entrega mínima de SEG-ACCESO. ActualizadorEsquema decidirá cuándo aplicarla y su valor inicial; SEG-ACCESO no presume que `Activa` sea equivalente.

Objetos requeridos para el acceso real:

```sql
CREATE TABLE AccesoSociedadesGrupo (
    IdGrupoOperadores LONG NOT NULL,
    IdSociedad TEXT(3) NOT NULL,
    CONSTRAINT PK_AccesoSociedadesGrupo PRIMARY KEY (IdGrupoOperadores, IdSociedad),
    CONSTRAINT FK_AccesoSociedadesGrupo_Grupo FOREIGN KEY (IdGrupoOperadores) REFERENCES GruposOperadores (IdGrupoOperadores),
    CONSTRAINT FK_AccesoSociedadesGrupo_Sociedad FOREIGN KEY (IdSociedad) REFERENCES Sociedades (IdSociedad)
);
CREATE INDEX IX_AccesoSociedadesGrupo_Sociedad ON AccesoSociedadesGrupo (IdSociedad);

CREATE TABLE PreferenciasAccesoOperador (
    IdOperador LONG NOT NULL,
    IdSociedad TEXT(3) NOT NULL,
    CONSTRAINT PK_PreferenciasAccesoOperador PRIMARY KEY (IdOperador),
    CONSTRAINT FK_PreferenciasAccesoOperador_Operador FOREIGN KEY (IdOperador) REFERENCES OperadoresSeguridad (IdOperador),
    CONSTRAINT FK_PreferenciasAccesoOperador_Sociedad FOREIGN KEY (IdSociedad) REFERENCES Sociedades (IdSociedad)
);

CREATE TABLE PreferenciasAccesoEjercicio (
    IdOperador LONG NOT NULL,
    IdSociedad TEXT(3) NOT NULL,
    IdSociedadEjercicio LONG NOT NULL,
    CONSTRAINT PK_PreferenciasAccesoEjercicio PRIMARY KEY (IdOperador, IdSociedad),
    CONSTRAINT FK_PreferenciasAccesoEjercicio_Operador FOREIGN KEY (IdOperador) REFERENCES OperadoresSeguridad (IdOperador),
    CONSTRAINT FK_PreferenciasAccesoEjercicio_Sociedad FOREIGN KEY (IdSociedad) REFERENCES Sociedades (IdSociedad),
    CONSTRAINT FK_PreferenciasAccesoEjercicio_Ejercicio FOREIGN KEY (IdSociedadEjercicio) REFERENCES SociedadesEjercicios (IdSociedadEjercicio)
);
```

ActualizadorEsquema debe crear estos objetos con backup y exclusividad. SEG-ACCESO no añade DDL ni siembra operadores, grupos o asignaciones. La integridad de que `IdSociedadEjercicio` pertenece a `IdSociedad` se revalida en el servicio, porque una FK simple no expresa esa relación compuesta en el esquema heredado.

## Preferencias

Solo `CompletarAcceso` escribe preferencias, dentro de una transacción. Consultarlas no concede permisos: una sociedad recordada que deje de estar autorizada no aparecerá y no podrá completar el acceso.

## Integración visual

UI-PRINCIPAL debe adaptar exclusivamente el prototipo WPF aprobado en `tools/ProConta.Acceso.Prototipo`, reemplazando su modelo ficticio por esta API. SEG-ACCESO no copia ni modifica ese prototipo y no posee la presentación, el arrastre, el botón Salir ni la navegación.
