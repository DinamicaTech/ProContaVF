# Seguridad de operadores

El manifiesto `20260929_010_SEGURIDAD` crea `OperadoresSeguridad`, `GruposOperadores`, `OperadoresGrupos`, `RestriccionesReportsGrupo` y `AuditoriaSeguridad`. Adopta el contrato final del módulo Seguridad: `OperadoresSeguridad.Nombre` es único y no se conserva el antiguo `Codigo` redundante. Los campos binarios de contraseña mantienen los tamaños 32 y 16 del motor existente.

Solo se siembran los grupos técnicos `ADMINISTRADORES` y `OPERADORES`. No se crean operadores, contraseñas, pertenencias, restricciones de report ni eventos de auditoría. Las pertenencias y restricciones tienen FKs físicas; la auditoría conserva `CodigoOperador` como texto histórico, sin forzar una relación que impediría conservar eventos de operadores eliminados o renombrados. Los permisos concretos seguirán siendo responsabilidad del módulo Seguridad.

La muestra `work/databases/ProConta.Maestra.Seguridad.accdb` tiene diez versiones, 70 tablas y huella del último manifiesto `D7F948B836743516A89D1FD83EA905A8E0DB71D121B1103D0BB90258B3E5D987`. La base original `ProcontaVF.accdb` no se modifica.
