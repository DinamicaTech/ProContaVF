namespace ProConta.UI.Base.Tablas;

/// <summary>Columna tal como la define la pantalla: clave estable, título, ancho de fábrica y si viene oculta.</summary>
public sealed record DefinicionColumna(string Clave, string Titulo, bool OcultaDeFabrica = false, bool SiempreVisible = false);

/// <summary>Lo que el operador ha cambiado de las columnas de una tabla. Solo guarda lo que difiere de fábrica.</summary>
public sealed class ConfiguracionColumnas
{
    /// <summary>Orden de las columnas por clave (null: el de fábrica).</summary>
    public List<string>? Orden { get; set; }

    /// <summary>Columnas mostradas u ocultadas por el operador (las que no están, como de fábrica).</summary>
    public Dictionary<string, bool>? Visibles { get; set; }

    /// <summary>Anchos en píxeles por clave (las que no están, de fábrica).</summary>
    public Dictionary<string, double>? Anchos { get; set; }

    /// <summary>True si no hay nada cambiado respecto a fábrica.</summary>
    public bool EsDeFabrica => (Orden is null || Orden.Count == 0) && (Visibles is null || Visibles.Count == 0) && (Anchos is null || Anchos.Count == 0);
}

/// <summary>Una columna ya resuelta: posición, visibilidad y ancho guardado.</summary>
public sealed record ColumnaResuelta(DefinicionColumna Definicion, int Posicion, bool Visible, double? Ancho);

/// <summary>
/// Reglas del criterio global de columnas (docs/plataforma/ui-base.md, 2026-10-31 09:58), sin dependencias de WPF:
/// orden, visibles y anchos por operador; mover, ocultar y restablecer.
/// </summary>
public static class DisposicionColumnas
{
    /// <summary>Mínimo de columnas visibles que deja ocultar.</summary>
    public const int MinimoVisibles = 3;

    /// <summary>Aplica la configuración a las definiciones: columnas nuevas o desconocidas se tratan como de fábrica.</summary>
    public static IReadOnlyList<ColumnaResuelta> Resolver(IReadOnlyList<DefinicionColumna> definiciones, ConfiguracionColumnas? cfg)
    {
        var porClave = definiciones.ToDictionary(d => d.Clave, StringComparer.Ordinal);
        var orden = new List<string>();
        if (cfg?.Orden is { } o) orden.AddRange(o.Where(porClave.ContainsKey).Distinct());
        // Las columnas que no estaban en el orden guardado se insertan tras su vecina de fábrica.
        for (var i = 0; i < definiciones.Count; i++)
        {
            var k = definiciones[i].Clave;
            if (orden.Contains(k)) continue;
            var previa = i > 0 ? orden.IndexOf(definiciones[i - 1].Clave) : -1;
            orden.Insert(previa + 1, k);
        }
        return orden.Select((k, i) =>
        {
            var d = porClave[k];
            return new ColumnaResuelta(d, i, EsVisible(d, cfg), cfg?.Anchos is { } a && a.TryGetValue(k, out var w) && w > 0 ? w : null);
        }).ToList();
    }

    /// <summary>Visibilidad de una columna según la configuración.</summary>
    public static bool EsVisible(DefinicionColumna d, ConfiguracionColumnas? cfg)
    {
        if (d.SiempreVisible) return true;
        return cfg?.Visibles is { } v && v.TryGetValue(d.Clave, out var on) ? on : !d.OcultaDeFabrica;
    }

    /// <summary>Mueve una columna a la posición indicada (sobre el orden completo) y devuelve la nueva configuración.</summary>
    public static ConfiguracionColumnas Mover(IReadOnlyList<DefinicionColumna> definiciones, ConfiguracionColumnas? cfg, string clave, int destino)
    {
        var orden = Resolver(definiciones, cfg).Select(c => c.Definicion.Clave).ToList();
        var i = orden.IndexOf(clave);
        if (i < 0) return Copiar(cfg);
        orden.RemoveAt(i);
        orden.Insert(Math.Clamp(destino, 0, orden.Count), clave);
        var r = Copiar(cfg);
        r.Orden = orden.SequenceEqual(definiciones.Select(d => d.Clave)) ? null : orden;
        return r;
    }

    /// <summary>Mueve una columna junto a su vecina visible a la izquierda (-1) o a la derecha (+1); null si no puede.</summary>
    public static ConfiguracionColumnas? MoverJuntoAVisible(IReadOnlyList<DefinicionColumna> definiciones, ConfiguracionColumnas? cfg, string clave, int sentido)
    {
        var res = Resolver(definiciones, cfg);
        var visibles = res.Where(c => c.Visible).ToList();
        var i = visibles.FindIndex(c => c.Definicion.Clave == clave);
        var j = i + Math.Sign(sentido);
        if (i < 0 || j < 0 || j >= visibles.Count) return null;
        return Mover(definiciones, cfg, clave, visibles[j].Posicion);
    }

    /// <summary>Muestra u oculta una columna; devuelve null si no se puede (siempre visible o mínimo de visibles).</summary>
    public static ConfiguracionColumnas? CambiarVisible(IReadOnlyList<DefinicionColumna> definiciones, ConfiguracionColumnas? cfg, string clave, bool visible)
    {
        var d = definiciones.FirstOrDefault(x => x.Clave == clave);
        if (d is null) return null;
        if (!visible && (d.SiempreVisible || Resolver(definiciones, cfg).Count(c => c.Visible) <= MinimoVisibles)) return null;
        var r = Copiar(cfg);
        r.Visibles ??= new Dictionary<string, bool>();
        if (visible == !d.OcultaDeFabrica) r.Visibles.Remove(clave); else r.Visibles[clave] = visible;
        if (r.Visibles.Count == 0) r.Visibles = null;
        return r;
    }

    /// <summary>Guarda el ancho de una columna.</summary>
    public static ConfiguracionColumnas CambiarAncho(ConfiguracionColumnas? cfg, string clave, double? ancho)
    {
        var r = Copiar(cfg);
        r.Anchos ??= new Dictionary<string, double>();
        if (ancho is > 0) r.Anchos[clave] = Math.Round(ancho.Value); else r.Anchos.Remove(clave);
        if (r.Anchos.Count == 0) r.Anchos = null;
        return r;
    }

    private static ConfiguracionColumnas Copiar(ConfiguracionColumnas? c) => new()
    {
        Orden = c?.Orden is null ? null : new List<string>(c.Orden),
        Visibles = c?.Visibles is null ? null : new Dictionary<string, bool>(c.Visibles),
        Anchos = c?.Anchos is null ? null : new Dictionary<string, double>(c.Anchos)
    };
}
