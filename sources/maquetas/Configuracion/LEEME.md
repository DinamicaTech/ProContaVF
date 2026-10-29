# ProConta · Configuración (maqueta)

`proconta-configuracion.html` se abre directamente en el navegador: no usa backend, `fetch` ni servicios externos. Los datos del anexo están embebidos y todos los cambios se quedan en memoria. «Restablecer» vuelve al estado inicial.

## Qué contiene

- 12 mantenimientos (11 tablas del anexo y la ubicación local de datos) generados por un único motor de metadatos: catálogo de mantenimientos → campos → renderizador común → copia en memoria → validar → guardar.
- Composición según el contenido: formulario directo (hasta un grupo), secciones plegables (varios grupos) o pestañas internas (más de 30 campos). Lista y editor lado a lado o lista arriba, con divisor redimensionable.
- Barra de demostración separada del producto con contexto de la empresa y el ejercicio, 10 escenarios, tamaño de ventana (1440×900 y 1024×768, descontando el marco del contenedor) y escala (100/125/150 %).
- «Notas de entrega» dentro del HTML: sistema reutilizable, escenarios, hechos del anexo frente a propuestas, cobertura, banco de editores y extensiones futuras.

## Atajos

Ctrl+K busca un ajuste o mantenimiento. Ctrl+G (o Ctrl+S) guarda. Con el foco en la lista, las flechas cambian de registro e Intro pasa al editor. Con el foco en el divisor, las flechas lo mueven.

## Escenarios añadidos a los 8 pedidos

«Sin permiso de acceso» y «Error al guardar». Representan la falta de permiso y el guardado fallido que conserva los valores.

## Revisión de validación 1 (29/10/2026)

Se aplican los criterios ya validados en Terceros y Facturas:

- Todos los controles tienen un tooltip descriptivo. Los campos muestran su etiqueta y su ayuda, o el mensaje del error si lo tienen.
- Los errores ya no se escriben debajo del campo: el campo se marca con borde rojo e icono, y el mensaje va en su tooltip y en el resumen superior del editor.
- Sin palabras enteras en mayúsculas: las categorías de la navegación y los rótulos de la búsqueda van en minúscula inicial.
- A 2560 × 1440 la lista se ensancha hasta mostrar todas sus columnas (como máximo el 62 % del ancho) mientras no se mueva el divisor a mano.
- La barra de demostración añade el tamaño de ventana 2560 × 1440, que es la resolución de referencia.
