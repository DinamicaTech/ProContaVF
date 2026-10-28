# Entrega visual selectiva de Líneas bancarias

Fecha: 01/10/2026. Rama fuente: `codex/norma43`.

Esta entrega normaliza exclusivamente el contenido WPF de TES-N43 frente a la
maqueta de referencia. No modifica el contenedor central, no aplica migraciones,
no abre Access y no habilita importación, exportación ni reversión en el host.

## Contrato de integración

- Destino estable: `lineas`.
- Vista alojable: `ContenidoLineasBancariasWpf`, un `UserControl` sin ventana,
  navegación, título, selector de contexto, origen de datos ni versión propios.
- El contenedor mantiene la propiedad de título, contexto, modo/base y estado
  global mediante `IPantallaWpf` e `IDetalleEstadoPantalla`.
- No cambia ninguna interfaz pública ni se añade dependencia de ejecución.
- El adaptador central puede seguir localizando el botón `Revertir lote` y los
  enlaces `OperadorReversion`/`MotivoReversion` dentro del detalle ampliado para
  mantenerlos deshabilitados hasta disponer de autorización contractual.

## Snapshot selectivo para UI-PRINCIPAL

Solo han cambiado estos tres archivos respecto al manifiesto central `.9`:

| Origen | Destino central | SHA-256 |
| --- | --- | --- |
| `src/ProConta.Facturacion.UI.Wpf/ContenidoLineasBancariasWpf.xaml` | `ProConta.Norma43.Wpf/ContenidoLineasBancariasWpf.xaml` | `66368C53D275C8675EA7EFEF801992E6E54AE88B6B00F89933B2750ACCEF08AD` |
| `src/ProConta.Facturacion.UI.Wpf/ContenidoLineasBancariasWpf.xaml.vb` | `ProConta.Norma43.Wpf/ContenidoLineasBancariasWpf.xaml.vb` | `C3C5DCB7FC4DFAA38F7707BFC259B662D45913B34C2A219F4780FC1E8ED9B705` |
| `src/ProConta.Facturacion.UI.Wpf/VistaLineasBancariasWpfViewModel.Maqueta.vb` | `ProConta.Norma43.Wpf/VistaLineasBancariasWpfViewModel.Maqueta.vb` | `C3B0CAA850561F1EBD52A9CE8DE214761A3EEE19E138A356ED7964B7BFC678B5` |

Los siguientes orígenes consumidos por el snapshot no han cambiado frente al
manifiesto central y no necesitan copiarse otra vez:

| Origen | SHA-256 |
| --- | --- |
| `src/ProConta.Facturacion.Aplicacion/Banca/ConsultaLineasExtractoBancario.vb` | `E2D368ADDFF0FD4CF328768759CE7FA00BE9195D6421DD02284DDDD1454BF742` |
| `src/ProConta.Facturacion.Persistencia.Access/Banca/RepositorioConsultaLineasExtractoBancarioAccess.vb` | `CF9D4BA9CB22A5615ACC61EC2E8CF71CF15790C2CCD9AB5B3319E47A9E1FA7B1` |
| `src/ProConta.Facturacion.UI.Wpf/VistaLineasBancariasWpfViewModel.vb` | `4C2E4BD0C217CD1793193AFE2ED55A3647908677C9F55A1C07F7E8F75FAC224E` |

No se debe traer la rama completa ni copiar el renderizador al proyecto central.

## Resultado visual y comportamiento

- desaparecen la cabecera y las bandas técnicas duplicadas dentro del módulo;
- tabla como zona dominante, con selección explícita, estados contractuales,
  desplazamiento horizontal propio y densidades 26/22;
- buscador, filtros, columnas y refresco en una banda compacta;
- concepto y acciones se mantienen accesibles sin desplazar la tabla;
- Escape limpia la selección e Intro sobre la tabla amplía o reduce el detalle;
- acciones no integradas permanecen deshabilitadas o publican una explicación,
  sin simular escrituras ni interpretar `555`, `SEPA` o `Split` como contratos.

## Evidencia reproducible

El proyecto `tools/ProConta.Facturacion.RenderizarLineasBancarias` usa 36 líneas
sintéticas y no implementa ninguna escritura. Genera:

| Tamaño | PNG | SHA-256 |
| --- | --- | --- |
| 1180 × 720 | `artifacts/lineas-bancarias/lineas-bancarias-1180x720.png` | `7F658AC88E3E71C2CDDEFAAF8BC13F9E3A223CB1354611AE757D4F97CB03BA03` |
| 850 × 640 | `artifacts/lineas-bancarias/lineas-bancarias-850x640.png` | `8466B5A16384402E729DF45AF75B661397D64D90FCA52B856EB6CBF07BB86AA0` |

Comando reproducible:

```powershell
dotnet run --project tools/ProConta.Facturacion.RenderizarLineasBancarias/ProConta.Facturacion.RenderizarLineasBancarias.vbproj --configuration Debug -p:Platform=x64
```

Los PNG son evidencia local y no forman parte del snapshot de fuentes.
