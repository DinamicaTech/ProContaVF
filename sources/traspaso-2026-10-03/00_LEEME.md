# Paquete de continuidad de ProConta

Versión 1.0 · 3 de octubre de 2026

## Contenido y orden de lectura

1. **01_ESPECIFICACION_FUNCIONAL.docx**: documento para revisión humana, con índice y navegación por títulos.
2. **01_ESPECIFICACION_FUNCIONAL.md**: el mismo contenido funcional en texto portable para el nuevo modelo.
3. **02_PROMPT_INICIAL.txt**: encargo listo para copiar al proyecto nuevo.
4. **03_ANEXOS_PENDIENTES.md**: qué debe adjuntarse y cuándo es necesario.
5. **04_FUENTES.md** y **MANIFIESTO.json**: inventario y huellas de las fuentes incluidas.
6. **referencias/**: copia de contratos, decisiones y modelos; no exige acceso a los worktrees originales.
7. **proconta.accdb**: Base de datos maestra con el modelo de datos más actualizado
8. **procontaestructura.accdb**: Base de datos legacy que es la fusión de la BD del ERP actual y el de contabilidad. Como referencia únicamente.

Las referencias conservan su texto y, por ello, pueden contener rutas antiguas, enlaces relativos fuera del paquete, estados superados y propuestas. Para localizar una referencia usa el índice de este paquete. La especificación principal resuelve las discrepancias conocidas y marca las restantes. No ejecutar automáticamente los ejemplos de comandos o migraciones de las fuentes.

El paquete no incluye BD, HTML, certificados, contraseñas, PDFs reales, binarios de ProConta ni repositorios completos. La BD maestra y las maquetas las aportará el usuario, según su encargo. Su ausencia aquí es deliberada y está indicada en el inventario de anexos.

Este documento reúne el alcance conocido y conserva los detalles disponibles. No equivale a una especificación aprobada de las áreas que todavía no se habían definido: tesorería completa, asistente IA, determinadas políticas de correo/documentos y varios modelos fiscales permanecen abiertos. Las decisiones del usuario en el nuevo proyecto pueden resolverlos sin tener que reconstruir el historial.

## Qué conservar del proyecto anterior

Conservar comportamiento de negocio, invariantes, identidades, mapas de importación y ejemplos con resultados contrastados. Utilizar el código anterior solo como consulta selectiva ante una ambigüedad. No copiar automáticamente sus pantallas, archivos de integración acumulativos o restricciones temporales del prototipo.

No se ha ejecutado ninguna actualización de esquema, envío, emisión ni cambio de código durante esta preparación documental. Los worktrees contienen cambios pendientes; las huellas de las fuentes describen el contenido copiado, no una versión reproducible de todo el software.

## Incorporación de anexos por el usuario

Añadir los ficheros a una carpeta externa **anexos_usuario/** junto al paquete, con un índice que indique versión, finalidad y si sus datos son ficticios o autorizados para pruebas. La maestra seleccionada debe llevar SHA-256 y versión. En las maquetas incluir CSS, JS, iconos, imágenes y fuentes o sus dependencias documentadas. Evitar reemplazar el ZIP original sin conservar su manifiesto.

Los anexos de pruebas de cada módulo pueden entregarse cuando comience ese módulo. Para iniciar no necesitas preparar todos los corpus históricos.
