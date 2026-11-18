using System.Windows;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.UI;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

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

    /// <summary>Subcuentas (imputables) del plan del ejercicio, para el nombre junto al código y para «Auto» (plataforma/mantenimiento 2026-11-18).</summary>
    public async Task<IReadOnlyList<CuentaPlan>> SubcuentasAsync(ContextoMantenimiento contexto) =>
        [.. (await cuentas.ListarAsync(contexto.Trabajo)).Where(c => c.EsImputable || c.Codigo.Trim().Length >= 7).Select(c => new CuentaPlan(c.Codigo.Trim(), c.Nombre.Trim()))];

    /// <summary>Alta de la subcuenta que propone «Auto», con la cuenta padre que le toca por su código.</summary>
    public async Task<Resultado> CrearSubcuentaAsync(ContextoMantenimiento contexto, string codigo, string nombre)
    {
        var r = await cuentas.AltaSubcuentaAsync(contexto.Trabajo, codigo, nombre);
        return r.EsCorrecto ? Resultado.Correcto() : Resultado.Fallo(r.Error!);
    }
}
