# Estado del reconocimiento de facturas de compra

Última actualización: 19/08/2026.

Este documento es el punto de coordinación con el resto del desarrollo de ProConta. El motor se integra en la misma solución `ProConta.Facturacion.sln`; `ProcontaEstructura.accdb` se usa únicamente como referencia legacy y los datos operativos se leen de `ProcontaVF.accdb`.

## Implementado

- Motor local de extracción y reconocimiento con candidatos, fuentes, confianza y advertencias.
- Extracción posicional de PDF nativo y conector opcional AWS Textract para OCR/segunda fuente.
- Conversión histórica de divisas mediante Frankfurter.
- Protección DPAPI de secretos AWS.
- Servicio sustituible de consulta de datos empresariales por NIF. El proveedor principal es el servicio oficial `VNifV2` de la AEAT, que reutiliza el certificado configurado para VeriFactu y devuelve razón social, NIF actual y estado censal. El antiguo conector Axesor queda como fuente opcional porque Incapsula bloquea actualmente las consultas automatizadas.
- Persistencia prevista en `FacturasCompra`, incluido `LOGImportacion`.
- API `ServicioProcesamientoFacturaCompra.ProcesarFacturaAsync` para el flujo definitivo con inserción.
- API no destructiva `ReconocerFacturaAsync` para probar facturas que ya existen en Access.
- Pantalla `Prueba facturas compra` accesible desde la cinta de Facturación.

## Pantalla de prueba

- Filtra `FacturasCompra` por el ejercicio activo.
- Muestra solamente la última carpeta y el nombre del PDF.
- Al seleccionar una fila procesa el documento y presenta NIF, fecha, número, BI, IVA, total, confianza, comparación con la BD y log.
- Muestra el PDF a la derecha mediante WebView2.
- Incluye columnas fiscales 0 %, 4 %, 5 %, 10 % y 21 %. Al editar BI, IVA o total se recalculan las otras dos magnitudes.
- La selección no inserta ni actualiza registros.

## Límites de este subproyecto

La revisión asistida definitiva, la generación de asientos y la recepción por correo permanecen como capas posteriores. Todas consumirán el resultado `CFacturaCompra` y el contenido de `FacturasCompra`.

## Cambios de base de datos previstos

La migración `database/migrations/20260819_001_reconocimiento_facturas_compra.sql` documenta los campos de trazabilidad y la configuración del reconocimiento. Antes de activar inserciones automáticas debe integrarse en el migrador versionado general y validarse sobre una copia de `ProcontaVF.accdb`.
