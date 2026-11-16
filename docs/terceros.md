---
title: Terceros
depends_on: []
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
---
## Summary
Personas y organizaciones con las que se trabaja, con una identidad transversal y roles (cliente, proveedor, contacto). Su ficha, ya construida en WPF con su buscador, aloja componentes de otras áreas (mayor, facturas, compras, documentos), que de momento son huecos explícitos.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-11-15 14:48 · Cumplida la línea «Derived from pantalla-de-inicio» del 2026-11-15 11:57: la salud de Inicio comprueba que los clientes y proveedores cuya subcuenta tiene apuntes en el ejercicio tienen NIF en su ficha (pantalla-de-inicio 2026-11-15 14:48).

## Requirements
- 2026-10-28 12:28 · Y, por supuesto, hay que encapsular los diferentes 'entes' que aparecen en el proyecto (CuentaContable, Tercero -> Cliente/Proveedor/Asociacion/Particular..., FacturaCompra, FacturaVenta, etc.).
- 2026-10-28 12:28 · From sources/traspaso-2026-10-03/referencias/Terceros/docs/: contratos y entregas anteriores de Terceros
- 2026-11-15 11:57 · Derived from pantalla-de-inicio: comprobación de que todos los clientes y proveedores con movimientos tienen NIF. Fase 3.
