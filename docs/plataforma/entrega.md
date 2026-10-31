---
title: Entrega y calidad
status: draft
depends_on: [architecture]
threads:
  - 2026-10-28 12:30 · Carga de requisitos | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GRNMH95h9tnQp4dmEPHmw1w
  - 2026-10-31 08:38 · Estrategia de desarrollo | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
  - 2026-10-31 16:58 · Cerrar preguntas pendientes del proyecto | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5G7UyrP5eDJV4Ds6CCBozdbu
  - 2026-10-31 18:02 · Desarrollos base | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GPQZwFm28U4vq3sQdopN45C
---
## Summary
Solución .NET, compilación x64, pruebas automáticas ejecutadas en local (sin BD por cada hilo antes de su PR; contra Access en el equipo del propietario antes de validar), versionado de entregas visible (InformationalVersion), esquema de pruebas separado de producción y copias de seguridad coherentes de BD y documentos.
En la raíz del repositorio: `compilar.cmd` compila `ProContaVF.slnx` en Release x64; `probar.cmd` compila y ejecuta todas las pruebas, y `probar.cmd sinbd` solo las que no tocan Access (las pruebas contra Access llevan `[Trait("Categoria","Access")]`). `Directory.Build.targets` pone a cada binario la versión `ProContaVF-<rama>.<número de commits>+<sha corto>`.
**Aceptación:** un comando local compila la solución x64 y ejecuta las pruebas, con y sin BD de pruebas; la entrega muestra su versión.

## Decisions
- 2026-10-28 13:30 · Creado en la carga inicial de requisitos.
- 2026-10-31 08:37 · Pruebas en local, sin compilación en GitHub (ver architecture 2026-10-31 08:37): cada hilo ejecuta las pruebas sin BD antes de abrir su PR y las pruebas contra Access se ejecutan en el equipo del propietario, contra la BD de pruebas, antes de pedir la validación. Sustituye la parte "ejecución de pruebas en cada PR" de su línea draft. Es el primer incremento del esqueleto técnico.
- 2026-10-31 16:54 · Instalación en el Windows Server: carpeta versionada copiada con un script (con respaldo previo y actualización del esquema) y ACE x64 instalado aparte una sola vez; sin instalador MSI (resuelve P-14).
2026-10-31 18:02 · Comandos de entrega en la raíz: `compilar.cmd` (`dotnet build ProContaVF.slnx -c Release -p:Platform=x64`) y `probar.cmd` (compila y ejecuta todas las pruebas; con el argumento `sinbd` excluye las de la categoría `Access`).
2026-10-31 18:02 · InformationalVersion calculada desde git en cada compilación (`Directory.Build.targets`): `ProContaVF-<rama>.<número de commits>+<sha corto>`, con «/» de la rama cambiada por «-»; sin git queda `ProContaVF-local`. Cumple el formato `<producto>-<rama>.<secuencia>` de RF-TEC-06 y añade el commit.
2026-10-31 18:02 · Las pruebas que tocan Access llevan `[Trait("Categoria","Access")]` y trabajan siempre sobre una copia de la BD en una carpeta temporal, nunca sobre el original.

## Requirements
- 2026-10-28 12:28 · From sources/01_ESPECIFICACION_FUNCIONAL.docx § 27 RF-TEC-04..07: pruebas sin efectos externos, backups con ensayo de restauración, entregas versionadas, compilación completa
- 2026-10-28 12:28 · draft: Preparar la solución, la compilación x64, la ejecución de pruebas en cada PR y el versionado de entregas.
- 2026-10-28 12:28 · [answered 2026-10-31 16:54] question: P-14 ¿cómo se instala en el Windows Server? (a) carpeta copiada con un script y ACE x64 instalado aparte ✅ (b) instalador MSI
- 2026-10-31 16:54 · 28. a
- 2026-10-31 16:54 · answer: P-14 instalación → (a) carpeta copiada con un script y ACE x64 instalado aparte
- 2026-10-31 08:37 · Derived from architecture: la solución, la compilación x64 y la ejecución local de las pruebas (sin BD y contra la BD de pruebas) forman el primer incremento del esqueleto técnico; no se configura compilación en GitHub.
- 2026-10-31 17:53 · Derived from architecture: los comandos locales de compilación x64 y de pruebas (con y sin Access) y la versión de entrega calculada desde git.

## Hand-over
- 2026-10-31 18:02 · Hecho: `compilar.cmd`, `probar.cmd [sinbd]` y la versión desde git en todos los binarios. Pendiente: mostrar la versión en el contenedor (plataforma/contenedor), el script de instalación en el Windows Server con respaldo previo (P-14) y el ensayo de restauración de copias (RF-TEC-05).
