---
title: Aviso de caducidad del certificado
depends_on: [contabilidad/modelos-tributarios/presentacion, pantalla-de-inicio]
threads:
  - 2026-11-23 14:10 · Mostrar la caducidad del certificado en Inicio | https://claude.ai/code/project/chan_01SQZ7PCqQx2V9FyGTmZCe5G?thread=cmsg_01SQZ7PCqQx2V9FyGTmZCe5GUTEwf49yaTSbe8rHbmhBWq
---
## Summary
Etiqueta fija en la cabecera de Inicio, junto a «Comparar con», con el nombre del certificado de la empresa y su fecha de caducidad: verde si faltan más de 60 días, amarillo entre 30 y 60 y rojo con menos de 30 o ya caducado. Pulsarla abre Configuración › Parámetros y tablas › «Certificado de la empresa»; el tooltip da el titular, el NIF y cómo renovarlo. Sustituye al aviso de salud que pedía el borrador.
Hecho y validado por el propietario (2026-11-23 14:17, PR #134): `src/ProConta.Inicio/Bloques/Certificado.cs` (`EstadoCertificado`), cargado con los bloques de la fase 4 (`ServicioBloques.CertificadoAsync`, `BloquesFase4VM.Certificado`) y dibujado en `PantallaInicio.xaml`. Sin cambios de esquema.
**Aceptación:** con un certificado que caduca dentro de 20 días la etiqueta sale en rojo con la fecha; a 45 días, en amarillo; a 90 días, en verde; pulsarla abre la pantalla del certificado.

## Decisions
- 2026-11-18 12:20 · Creado como borrador desde el hilo «Registrar las funcionalidades que faltan» (revisión de funcionalidades antes del arranque), con el visto bueno del propietario (2026-11-18 12:18).
- 2026-11-23 14:10 · En lugar de un aviso en la salud, una etiqueta fija en la cabecera de Inicio (propietario 2026-11-23 13:59, conforme con el planteamiento 14:01): «Certificado: <nombre> · caduca el dd/mm/aaaa (N días)», siempre visible (no se oculta en «Personalizar…»). Colores: verde con más de 60 días, amarillo entre 30 y 60 (los dos incluidos), rojo con menos de 30 o ya caducado; además del color lleva un símbolo (✓, !, ✕). Días = fecha de caducidad menos hoy, en días naturales. Nombre = CN del certificado. Clic: abre Configuración › Parámetros y tablas › «Certificado de la empresa» (clave «certificadoempresa»).
- 2026-11-23 14:10 · Casos límite: sin certificado elegido (VeriFactuConfiguracion sin huella ni número de serie), etiqueta gris «Sin certificado de la empresa»; con un certificado elegido que no está en el almacén de Windows del equipo ni del usuario, roja «Certificado no instalado en este equipo». Si no se puede leer, gris con la causa y «Actualizar» lo reintenta.
- 2026-11-23 14:10 · Se lee con los bloques de la fase 4 al abrir Inicio y con «Actualizar»: la huella de VeriFactuConfiguracion de la sociedad y el certificado con `CertificadosAeat.Buscar` (el mismo que usan la presentación a la AEAT y la consulta de NIF). Solo lee el almacén, sin usar la clave privada: no pide PIN.
- 2026-11-23 14:17 · Validado por el propietario («Ok, está perfecto»): deja de ser borrador.

## Requirements
- 2026-11-18 12:16 · Ok, puede añadirse a la pantalla de inicio como warning
- 2026-11-18 12:18 · draft: Avisar en la pantalla de Inicio, como advertencia, cuando el certificado digital de la sociedad caduque en menos de 30 días.
- 2026-11-23 13:59 · Mejor tener un widget fijo con el nombre del certificado y fecha de caducidad, en verde si > 60 dias, amarillo entre 30 y 60 y en rojo si <30
- 2026-11-23 14:17 · Ok, está perfecto. Ya puedes fusionar PR y cerrar hilo
