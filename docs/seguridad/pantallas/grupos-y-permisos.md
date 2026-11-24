---
title: Grupos de operadores y sus permisos
status: draft
depends_on: [seguridad/pantallas, seguridad/permisos]
---
## Summary
Pendiente: pantalla para mantener los grupos de operadores y lo que puede hacer cada uno (sociedades y acciones). Hoy los grupos solo se marcan en el rol «Operador ProConta» de la ficha del tercero.
**Aceptación:** un administrador crea un grupo, le asigna sociedades y acciones, y un operador de ese grupo solo ve esas sociedades y solo puede esas acciones.

## Decisions
- 2026-11-24 11:20 · Creado como borrador desde seguridad/pantallas (operadores como rol de terceros), con el OK del propietario a dejarlo para más adelante.

## Requirements
- 2026-11-24 10:07 · 3. Sí, el mantenimiento de permisos por grupo lo podemos añadir a posteriori
- 2026-11-24 11:20 · draft: Mantenimiento de grupos de operadores y de sus permisos: crear, renombrar y desactivar grupos y asignarles las sociedades (AccesoSociedadesGrupo) y las acciones (PermisosAccionesGrupo, p. ej. SEGURIDAD.OPERADORES), con la pantalla y los permisos que acuerde el propietario.
