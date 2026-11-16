using System.Windows;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.UI;
using ProConta.Mantenimiento;

namespace ProConta.App;

/// <summary>
/// Buscador de cuentas de la pantalla de Parámetros (plataforma/mantenimiento 2026-11-12): el mismo diálogo que la cuenta
/// de gasto de compras (contabilidad/cuenta-contable), con el filtro de grupos del campo y «Crear subcuenta».
/// </summary>
internal sealed class SelectorCuentasMantenimiento(ICatalogoCuentas cuentas, ICatalogoCentrosCoste centrosCoste, Func<Window?> ventana) : ISelectorCuentas
{
    public Task<string?> ElegirAsync(CampoMantenimiento campo, string? texto, ContextoMantenimiento contexto)
    {
        var buscador = new BuscadorCuentasVM(cuentas, contexto.Trabajo, campo.GruposCuenta, texto, centrosCoste, campo.SoloSubcuentas);
        return Task.FromResult(DialogoCuentas.Elegir(ventana(), buscador, campo.Etiqueta)?.Codigo);
    }
}
