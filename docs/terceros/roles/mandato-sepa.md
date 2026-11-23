---
title: Mandato SEPA del cliente
depends_on: [terceros/roles, informes/reporting, organizacion]
---
## Summary
Orden de domiciliación de adeudo directo SEPA Core que firma el cliente, en PDF de una página, desde el botón «Mandato SEPA…» junto a su IBAN en la pestaña Roles de la ficha (terceros/pantallas). Deudor: nombre, domicilio principal e IBAN de la ficha guardada; acreedor: la sociedad activa (nombre, NIF, domicilio y población fiscales); referencia de la orden: el código de cliente; pago periódico; sin SWIFT; fecha de emisión y localidad de la sociedad. El PDF se guarda en la carpeta temporal del equipo («ProContaVF\Mandatos SEPA») y se abre para imprimirlo. Código: `MandatoSepa`, `DatosMandatoSepa`, `AcreedorSepa` y `LectorAcreedorSepa` (src/ProConta.Terceros/Roles/MandatoSepa.cs), botón y flujo en `FichaTerceroVM.MandatoSepaCommand`.
**Aceptación:** en un cliente guardado con IBAN, «Mandato SEPA…» abre un PDF de una página con sus datos y los de la sociedad, el identificador de acreedor calculado, la casilla de pago periódico marcada y sin SWIFT; si a la sociedad le falta el nombre, el NIF o el domicilio o la población fiscales, un mensaje dice cuáles y dónde se completan.

## Decisions
- 2026-11-23 10:45 · Creado desde terceros/pantallas a petición del propietario.
- 2026-11-23 10:45 · Contenido (propietario 2026-11-23 10:28): sin SWIFT; referencia de la orden = código de cliente (IdCliente), a revisar cuando se envíen remesas al banco; identificador del acreedor SEPA (AT-02) calculado del NIF de la sociedad con el sufijo 000: ES + dígitos de control ISO 7064 MOD 97-10 + 000 + NIF; pago periódico marcado; localidad de firma = población fiscal de la sociedad; fecha = día de emisión. Texto propio inspirado en la plantilla del propietario, con la nota de derechos de reembolso del deudor (ocho semanas). La plantilla original no entra en el repositorio porque tiene datos reales.
- 2026-11-23 10:45 · Se genera con los datos guardados: con cambios pendientes o en una ficha nueva, el botón ofrece «Guardar y generar». HTML › PDF con el motor de informes (`ConversorHtmlPdf`, A4 con márgenes de 18 mm, marca «PRUEBAS» en la copia de pruebas). No se archiva en el gestor documental: el firmado se puede soltar en la pestaña Documentos del tercero.
- 2026-11-23 11:45 · Validado por el propietario (PR #133).

## Requirements
- 2026-11-23 10:23 · También necesito un botón para imprimir un mandato SEPA al lado del IBAN para que lo firme el cliente.
- 2026-11-23 10:23 · Te adjunto la plantilla actual que puedes modificar libremente, intentando que ocupe solo una página por simplicidad.
- 2026-11-23 10:23 · Los datos del deudor se obtienen de su ficha y los del acreedor de la sociedad.
- 2026-11-23 10:23 · La fecha es la de emisión del mandato.
- 2026-11-23 10:23 · Lo único es que tendrás que obtener el SWIFT de alguna forma ya que no se introduce en la ficha del tercero (tampoco es que haga falta). Si eso es complicado, emitimos el mandato sin swift, no hay problema.
- 2026-11-23 10:23 · El documento en PDF.
- 2026-11-23 10:28 · 1. Sin SWIFT
- 2026-11-23 10:28 · 2. Ok, el código de cliente. Aún no enviamos remesas al banco por lo que lo ajustaremos.
- 2026-11-23 10:28 · 3. Ok
- 2026-11-23 10:28 · 4. Ok a periódico. Localidad la de la sociedad.
- 2026-11-23 11:44 · Ok, está perfecto. Ya puedes fusionar.
