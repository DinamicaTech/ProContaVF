# Terceros: requisitos acordados el 01/10/2026

Origen: instrucciones del usuario y revisión posterior de la implementación WPF el 01/10/2026. Los apartados iniciales conservan los acuerdos funcionales; la revisión siguiente encarga corregir la UI. No constituye autorización para enviar correos durante pruebas ni para alterar bases originales sin el procedimiento de ActualizadorEsquema.

## Revisión de la entrega WPF — contacto, relaciones y coherencia visual

Estado: correcciones solicitadas, pendientes de implementación integrada y revisión visual. Referencia complementaria: `C:/docs/VeriFactu/Claude outputs/Contenedor/LEEME.md` y las tres capturas aportadas por el usuario. El LEEME es evidencia de diseño, no autorización automática de todas sus reglas funcionales; prevalecen los acuerdos del proyecto.

- Aplicar de verdad el criterio de anchos proporcionados: etiqueta corta, teléfono compacto, email más ancho, sin estirar los editores para rellenar toda la fila. Adaptar distribución a tamaño y DPI sin reducir longitud admitida ni perder acceso al valor completo.
- Mostrar siempre un panel compacto de destinatarios efectivos de facturas y comunicaciones administrativas, actualizado desde el borrador al cambiar direcciones, actividad o finalidades. Reutilizar el cálculo de dominio; no exigir pulsar «Ver destinatarios efectivos» ni consultar Access en cada tecla.
- Colocar las acciones de relaciones junto al listado, sin altura mínima vacía que las expulse al fondo. Mantener «+ Relación», «− Relación» y «Abrir ficha vinculada» en una misma fila cuando el espacio lo permita.
- Sustituir combos de personas/terceros por búsqueda modal que reutilice la búsqueda de Terceros con modo selección y filtro de candidatos válidos. Investigar el combo vacío; no crear roles automáticamente para rellenarlo. Cancelar no modifica el borrador.
- «− Relación» retira del borrador una relación nueva; para una relación guardada conserva el contrato de vigencia/historial, explicando la acción con ayuda y confirmación específica cuando proceda. No convertir un cambio de texto en borrado físico de relaciones históricas.
- Admitir datos de contacto libres, por ejemplo «Juan Díaz · Compras», email y teléfono, sin crear otro Tercero ni una relación artificial. DAT debe verificar si el modelo actual permite agrupar estos datos y documentar cualquier ampliación necesaria; no confundir una etiqueta libre con una persona vinculada.
- Añadir «Enviar email» junto a cada dirección válida. Abrir el compositor individual modal de INF-COR con destinatario precargado y propietario WPF; el clic no envía correo. Reutilizar su contrato y tratamiento de borradores, no crear otro compositor. No enviar mensajes reales durante verificaciones.
- Quitar las bandas técnicas repetidas del contenedor y del contenido. Conservar un indicador discreto de base/modo en la barra de estado y el detalle en ayuda o diagnóstico; los errores funcionales y cambios pendientes siguen visibles donde correspondan.
- Aplicar la misma revisión frente a su maqueta a TES-N43 y a CFG-MANT (pantalla Configuración). CFG-GLOBAL conserva la propiedad de parámetros globales; no duplicar un editor paralelo.
- Antes de integrar: comparar renders a dimensiones equivalentes con la maqueta, verificar teclado, ventanas estrechas y DPI, así como relación vacía/poblada, destinatarios sin configurar y valores largos. Un build correcto no acredita fidelidad visual.

## Diseño y entrada de domicilio

- Dimensionar campos según contenido y formato esperado, evitando anchos desproporcionados. Criterio transversal de UI-BASE: contemplar etiquetas, traducción visual al escalado de Windows y valores largos sin convertir todos los controles en campos del mismo ancho. No reducir capacidad de almacenamiento para ajustar la apariencia.
- Orden de entrada y de tabulación del domicilio: País → Código postal → Provincia → Población → Vía y número.
- Provincias filtradas por país; poblaciones filtradas por provincia. La resolución del código postal se realiza dentro del país seleccionado.
- Si el código postal identifica una sola provincia, seleccionarla y omitir su parada de teclado, pasando a Población. Si también identifica una sola población, seleccionarla y pasar directamente a Vía y número.
- Si existen varias coincidencias, no elegir arbitrariamente: permitir seleccionar el dato ambiguo. Sin coincidencias, no inventar una relación; mantener el recorrido de selección permitido por el catálogo.
- La selección automática debe quedar visible y ser corregible. Al cambiar país, código postal o provincia, revalidar las selecciones dependientes para no conservar una combinación incompatible. El salto corresponde a la introducción/confirmación del dato, no a la mera carga de una ficha existente.
- No asumir formato postal español para otros países. Catálogo y contrato concretos de resolución pendientes de identificar por DAT-TERCEROS.

## Roles y cuentas contables

- Renombrar la pestaña «Funciones» a «Roles». Es una denominación de interfaz: no implica renombrar tablas ni confundir roles de terceros con grupos/permisos de operadores o con perfiles de terceros.
- Cliente: una sola cuenta contable de cliente, situada debajo de su Forma de pago.
- Proveedor: puede disponer de dos cuentas diferenciadas, una como Proveedor y otra como Acreedor, situadas debajo de su Forma de pago. No tratarlas como una sola cuenta alternativa ni crear un tercer rol por deducción.
- Descripción funcional del usuario: Proveedor para suministros de la actividad (por ejemplo, reventa); Acreedor para suministros del funcionamiento ordinario (por ejemplo, tóner). Estos ejemplos no autorizan clasificar automáticamente compras ni asignar cuentas.
- Contacto: no tiene cuenta contable propia. Si el mismo tercero tiene también otros roles, esto no elimina las cuentas de Cliente/Proveedor.
- Conservar el contrato existente de resolución por rol y sociedad/ejercicio; no trasladar la cuenta a la identidad global `Terceros`. La cardinalidad indicada no debe interpretarse como una cuenta universal para todas las empresas y ejercicios.
- Pendiente de concretar con el módulo contable: obligatoriedad de cada cuenta, validación, representación física de la pareja Proveedor/Acreedor y API de lectura/edición. No imponer por esta especificación un plan de cuentas o numeración nuevos.

## Ventas

- Primer listado: facturas programadas/periódicas del cliente.
- Segundo listado: facturas emitidas del cliente.
- Seleccionar una programación filtra el segundo listado a facturas generadas a partir de esa programación, utilizando una relación de origen verificable. No inferir el vínculo por coincidencia de importes, fechas o textos.
- Sin selección de programación, mostrar las emitidas del cliente; permitir quitar el filtro y mostrar claramente cuándo está activo. Cambiar de cliente o contexto elimina una selección anterior incompatible.
- DAT-TERCEROS aloja ambos listados; los módulos de programación y facturación son propietarios de sus consultas, relaciones y acciones. Aplicar contexto y permisos del consumidor, sin duplicar motores de emisión o generación.

## Notas

- Añadir pestaña «Notas» inmediatamente antes de «Historial».
- Dos campos independientes de texto largo (Memo/Long Text en Access): «Notas» y «Notas internas»; controles multilínea adecuados.
- DAT-TERCEROS debe verificar primero los campos existentes y su alcance antes de proponer DDL. Cualquier estructura nueva se formaliza con ActualizadorEsquema, con script reproducible y sin cambios manuales aislados.
- El nombre «Notas» no implica publicar ese contenido en documentos o correos; «Notas internas» tampoco define por sí solo un permiso adicional. Queda por concretar visibilidad, usos externos y alcance global o por contexto; hasta entonces no añadir exportación o publicación automática.
- Revisar su incorporación al historial y a los permisos existentes al implementar; no afirmar que está resuelto sin verificar el contrato.

## Responsables y estado

DAT-TERCEROS incorpora estas reglas a su especificación y contrasta la siguiente maqueta. UI-BASE recoge el criterio de dimensiones. Contabilidad y Facturación aportarán los contratos consumidores necesarios; ActualizadorEsquema desplegará únicamente los cambios físicos que se concreten. UI-PRINCIPAL sigue alojando pantallas sin apropiarse de sus reglas.

No se cambia el orden del roadmap ni se inicia una implementación con esta anotación. La maqueta refinada permitirá contrastar distribución, foco, filtros y estados antes de programar la ficha WPF.
