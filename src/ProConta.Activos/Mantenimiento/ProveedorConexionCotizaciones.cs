using ProConta.Activos.Financieros;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;
using ProConta.Nucleo.Seguridad;

namespace ProConta.Activos.Mantenimiento;

/// <summary>
/// Parámetros › Integraciones › «Cotizaciones · EODHD» (activos/activos-financieros 2026-11-23 20:11; regla «Integraciones»): la clave de EOD Historical
/// Data de la sociedad, cifrada, con la que se buscan los ISIN que Yahoo no encuentra y se pide la cotización del día.
/// </summary>
public sealed class ProveedorConexionCotizaciones(IBaseDatos bd, ClienteEodhd? eodhd) : ProveedorBase
{
    private const string Tabla = "ConexionesCotizaciones";

    /// <summary>ISIN con que se prueba la clave (iShares Core MSCI World, que EODHD tiene seguro).</summary>
    public const string IsinPrueba = "IE00B4L5Y983";

    public override string Clave => "conexioncotizaciones";
    public override string Titulo => "Cotizaciones · EODHD";
    public override string Categoria => "Integraciones";
    public override string Finalidad => "Clave de EOD Historical Data con que se buscan los ISIN que Yahoo Finance no encuentra y se pide la cotización del día de los activos financieros.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "conexión";
    public override IReadOnlyList<string> Sinonimos => ["eodhd", "cotizaciones", "cotizacion", "isin", "etf", "fondos", "api", "token", "bolsa"];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [new("probar", "Probar la clave", $"Busca en EODHD el ISIN {IsinPrueba} con la clave guardada (gasta una de las 20 consultas diarias del plan gratuito). Guarda antes los cambios.",
            Modifica: false)];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Activa", "Consultar EODHD", TipoCampo.Logico) { Ayuda = "Desactivada, las cotizaciones se piden solo a Yahoo Finance." },
        new("Clave", "Clave (API token)", TipoCampo.Secreto) { Longitud = 200, Ancho = "l", Sinonimos = ["token", "api key", "clave"],
            Ayuda = "El «API token» de eodhd.com › Settings (el plan gratuito vale). Se guarda cifrada y no se vuelve a mostrar. Déjalo vacío para conservar la guardada." },
        new("EstadoClave", "Clave guardada") { SoloLectura = true, Ancho = "m", Ayuda = "Dice si hay una clave guardada, sin mostrarla." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("EstadoClave", "Clave guardada")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Conexión con EODHD";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        [await LeerAsync(c.Trabajo.IdSociedad) ?? await NuevoAsync(c)];

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Activa"] = true, ["Clave"] = null, ["EstadoClave"] = "No" }));

    private Task<RegistroMantenimiento?> LeerAsync(string idSociedad) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT IdSociedad, Activa, SecretoProtegido FROM {Tabla} WHERE IdSociedad = ?", f =>
        {
            var secreto = f.Texto("SecretoProtegido");
            return new RegistroMantenimiento((f.Texto("IdSociedad") ?? "").Trim(), new Dictionary<string, object?>
            {
                ["Activa"] = f.Logico("Activa"), ["Clave"] = null,
                ["EstadoClave"] = string.IsNullOrWhiteSpace(secreto) ? "No" : SecretoProtegido.Desproteger(secreto) is null ? "No se puede leer: escríbela de nuevo" : "Sí",
            });
        }, idSociedad));

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var sociedad = c.Trabajo.IdSociedad;
        var clave = (r["Clave"] as string)?.Trim();
        await bd.EnTransaccionAsync(async s =>
        {
            var existe = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {Tabla} WHERE IdSociedad = ?", sociedad), System.Globalization.CultureInfo.InvariantCulture) > 0;
            if (!existe) await s.EjecutarAsync($"INSERT INTO {Tabla} (IdSociedad, Activa) VALUES (?, ?)", sociedad, r.Logico("Activa"));
            else await s.EjecutarAsync($"UPDATE {Tabla} SET Activa = ? WHERE IdSociedad = ?", r.Logico("Activa"), sociedad);
            // Con una clave nueva, sustituye a la guardada; vacía, se conserva.
            if (!string.IsNullOrEmpty(clave))
                await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoProtegido = ? WHERE IdSociedad = ?", SecretoProtegido.Proteger(clave), sociedad);
            return true;
        });
        return Resultado<RegistroMantenimiento>.Correcto((await LeerAsync(sociedad))!);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion != "probar" || eodhd is null) return await base.EjecutarAsync(accion, registro, c);
        var clave = await ClaveAsync(bd, c.Trabajo.IdSociedad, aunqueDesactivada: true);
        if (string.IsNullOrWhiteSpace(clave))
            return Resultado<ResultadoAccion>.Fallo(new Error("Eodhd.SinClave", "Aún no hay clave guardada (o la guardada no se puede leer en este equipo).", Titulo, null,
                "Pega el API token de eodhd.com › Settings, guarda y vuelve a probar."));
        var r = await eodhd.BuscarAsync(clave, IsinPrueba);
        if (!r.EsCorrecto) return Resultado<ResultadoAccion>.Fallo(r.Errores);
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(r.Valor.Count == 0
            ? $"EODHD acepta la clave, pero no encuentra el ISIN de prueba {IsinPrueba}. Puede que el plan no incluya la búsqueda: prueba a dar de alta un activo financiero."
            : $"Clave correcta: EODHD encuentra el ISIN de prueba {IsinPrueba} en {r.Valor.Count} {(r.Valor.Count == 1 ? "bolsa" : "bolsas")} ({r.Valor[0].Nombre})."));
    }

    /// <summary>La clave de EODHD de la sociedad, descifrada; null si no hay, no se puede leer o la conexión está desactivada.</summary>
    public static Task<string?> ClaveAsync(IBaseDatos bd, string idSociedad, bool aunqueDesactivada = false) =>
        bd.EjecutarAsync(async s =>
        {
            var fila = await s.ConsultarPrimeroAsync($"SELECT Activa, SecretoProtegido FROM {Tabla} WHERE IdSociedad = ?",
                f => (Activa: f.Logico("Activa"), Secreto: f.Texto("SecretoProtegido")), idSociedad);
            return fila.Secreto is null || (!fila.Activa && !aunqueDesactivada) ? null : SecretoProtegido.Desproteger(fila.Secreto);
        });
}
