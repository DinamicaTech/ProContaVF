# Anexos que completan el traspaso

## Imprescindibles para iniciar

**A01 · Maestra Access elegida.** Adjuntar accdb, versión, fecha y SHA-256, indicando si es estructura vacía, catálogos semilla o datos de pruebas. No adjuntar un laccdb. No basta con llamarla «Maestra»: el prototipo Email254 y la maestra 0.24 Email150 son contratos diferentes. La evidencia histórica de la candidata está en referencias de Actualizador; la selección definitiva corresponde al usuario. Si hay tablas vinculadas, listar sus orígenes y proporcionar copias/diccionario cuando sean necesarias; no suponer que el enlace viajará al otro equipo.

**A02 · Maquetas HTML completas.** Login (variante elegida 2 y refinamiento a cuatro fases), contenedor, Terceros, N43, Configuración, Facturas/programadas, compositor de correo y seguimiento de lotes. Incluir sus CSS/JS/imágenes/iconos/fuentes, resolución y escala objetivo; marcar versión elegida y elementos meramente simulados. Las notas de Claude son ideas de apoyo, no requisitos obligatorios. Es preferible adjuntar archivos estables a depender de URLs que puedan cambiar.

**A03 · Catálogos y diccionario físico legibles.** Si no están documentados en la maestra, anexar exportación de tablas/campos/tipos/longitudes/defaults/nulabilidad/PK/FK/índices/consultas y los catálogos semilla con su significado. Permite a un modelo sin ACE comprender la base. Los manifiestos de Actualizador incluidos son referencia histórica, no garantizan que coincidan con el archivo finalmente adjunto.

## Necesarios al abordar cada módulo

**A04 · Facturación y VeriFactu.** Ejemplos saneados con entrada y resultado esperado: factura ordinaria, cero, descuentos, suplidos, varios IVA/recargo/retención, rectificativa, duplicación, serie concurrente, factura rechazada y envío incierto. Adjuntar XML de prueba/QR/PDF esperado, contratos del adaptador y versión/licencia de la biblioteca elegida; sin certificados privados. Conservar los tests de cálculo y calendario como especificación ejecutable si se dispone de ellos.

**A05 · Programaciones y contratos.** Casos de períodos, días 28/29/30/31, año bisiesto, cadencias, IPC/IRAV, primera emisión tras incremento y lotes interrumpidos, con resultado esperado y reglas contractuales aplicables.

**A06 · Compras.** Corpus PDF nativo y escaneado representativo, conjunto de verdad corregido y TXT tabulado de resultados 2025/2026. Incluir IVA mixto/cero, retención, moneda extranjera, factura múltiple y discrepancias de proveedor; datos ficticios o expresamente autorizados. La especificación de reconocimiento ya va incluida; el corpus real no.

**A07 · Bancos.** Ficheros Norma 43 con controles, complementos, duplicados, errores y sus extractos/movimientos esperados. Incluir un volumen representativo para rendimiento y casos de contabilización/reversión. No hacen falta claves de banca ni acceso a cuentas reales.

**A08 · Nóminas.** Excel representativos de las variantes de cabecera/columnas, alias, empleados, plantillas y asientos esperados, incluyendo paga extra, ambigüedad y reversión. Usar datos ficticios o anonimizados consistentemente.

**A09 · Contabilidad.** Planes y cuentas por ejercicio, centros, asientos ejemplo, mayor y balances esperados; cierres/apertura/reapertura, prorrata, modelos tributarios y casos de períodos protegidos. Adjuntar fórmulas y reglas de cada modelo que se quiera desarrollar; una plantilla no basta para demostrar cobertura normativa.

**A10 · Migraciones.** Copias autorizadas del ERP y de ContaSol por versión/ejercicio, diccionario de vínculos, mapas de IDs y decisiones de conservación/descarte; balances/recuentos esperados. Necesarios cuando se implemente cada importador, no para dibujar el login. Documentos históricos del ERP quedan excluidos salvo cambio expreso de alcance.

**A11 · Informes y diseño.** Plantillas FRX/XLSX y salidas aprobadas, logotipo y recursos de marca, licencias de fuentes/iconos. No adjuntar solo capturas si se espera reutilizar plantillas editables.

**A12 · Correo y Documentos.** EML/MIME saneados de entrada, comandos admitidos y resultados, adjuntos de prueba, respuestas simuladas SMTP/POP3/IMAP y casos de incertidumbre. Definir límites, destinatarios autorizados, raíz documental, versiones y retención antes de activar recepción/envío. Credenciales nuevas se configuran en el entorno; no se trasladan en este paquete.

**A13 · Activos y gráficos.** Operaciones/cotizaciones con fechas, moneda y fuente, compras/ventas/reentradas, costes/dividendos/FX y resultados contables y gráficos esperados. Separar ejemplos de cartera de reglas universales.

## Opcionales útiles

**A14 · Vídeos breves de interacción.** Para búsquedas, teclado, doble clic, filtros y ventanas modales cuya interacción no se deduzca del HTML. Añadir texto con pasos y resultado esperado; no deben ser la única especificación.

**A15 · Archivo de evidencia antigua.** Código/tests selectivos o snapshot separado del repositorio para consultar reglas difíciles, sin imponer copiarlo. No es imprescindible entregar todo el código para iniciar; conservarlo permite resolver dudas que la documentación todavía deja abiertas. Ramas/commits por sí solos no incluyen cambios sin confirmar.

## Índice mínimo de anexos del usuario

Por cada archivo: identificador Axx, nombre relativo, versión/fecha, SHA-256, finalidad, fuente, propietario funcional, datos ficticios/autorizados, limitaciones conocidas y requisitos RF que ayuda a validar. Mantener ejemplos y resultado esperado juntos. Nunca sustituir un resultado esperado por el resultado del código nuevo sin revisión.
