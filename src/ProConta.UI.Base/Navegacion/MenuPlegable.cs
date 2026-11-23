using ProConta.UI.Base.Modelos;

namespace ProConta.UI.Base.Navegacion;

/// <summary>
/// Opción de un menú lateral en lista plana con niveles (0 = área, 1 = pantalla del área, 2 = opción de un submenú)
/// que puede formar parte de un grupo plegable (plataforma/contenedor 2026-11-23).
/// </summary>
public abstract class OpcionPlegable : ModeloVista
{
    private bool _expandida;
    private bool _visible = true;

    /// <summary>0 = área, 1 = pantalla del área (o submenú), 2 = opción de un submenú.</summary>
    public abstract int Nivel { get; }

    /// <summary>Texto de ayuda de la opción (tooltip).</summary>
    public abstract string Ayuda { get; }

    /// <summary>Grupo plegable al que pertenece (null si su grupo no se pliega o es un área).</summary>
    public OpcionPlegable? Grupo { get; internal set; }

    /// <summary>Es el título de un grupo con más opciones que el umbral: el clic lo despliega o lo pliega.</summary>
    public bool EsPlegable { get; internal set; }

    public bool Expandida
    {
        get => _expandida;
        internal set { if (Fijar(ref _expandida, value)) Avisar(nameof(Flecha), nameof(AyudaPlegable), nameof(TextoAyuda)); }
    }

    /// <summary>Se ve en el menú: todos los grupos que la contienen están desplegados.</summary>
    public bool Visible
    {
        get => _visible;
        internal set => Fijar(ref _visible, value);
    }

    /// <summary>Flecha a la derecha del título de un grupo plegable (▸ plegado, ▾ desplegado); vacía en los demás.</summary>
    public string Flecha => !EsPlegable ? "" : Expandida ? "▾" : "▸";

    /// <summary>Texto que el tooltip añade a un grupo plegable.</summary>
    public string AyudaPlegable => !EsPlegable ? "" : Expandida ? "Clic para plegar el grupo" : "Clic para desplegar el grupo";

    /// <summary>Tooltip completo: la ayuda y, en un grupo plegable, qué hace el clic.</summary>
    public string TextoAyuda => EsPlegable ? $"{Ayuda}. {AyudaPlegable}" : Ayuda;
}

/// <summary>
/// Pliega los grupos del menú lateral que tienen más de <see cref="Umbral"/> opciones: empiezan plegados y se
/// despliegan con un clic en su título. Cada grupo se despliega por su cuenta y el estado no se guarda entre sesiones.
/// </summary>
public static class MenuPlegable
{
    /// <summary>Un grupo con más opciones directas que este número se muestra plegado.</summary>
    public const int Umbral = 3;

    /// <summary>Marca los grupos plegables, los deja plegados y calcula qué opciones se ven.</summary>
    public static void Organizar(IReadOnlyList<OpcionPlegable> opciones)
    {
        for (var i = 0; i < opciones.Count; i++)
        {
            var titulo = opciones[i];
            titulo.Grupo = null;
            var hijas = Hijas(opciones, i).ToList();
            titulo.EsPlegable = hijas.Count > Umbral;
            titulo.Expandida = false;
        }
        for (var i = 0; i < opciones.Count; i++)
        {
            if (!opciones[i].EsPlegable) continue;
            for (var j = i + 1; j < opciones.Count && opciones[j].Nivel > opciones[i].Nivel; j++)
                if (opciones[j].Grupo is null || opciones[j].Grupo!.Nivel < opciones[i].Nivel) opciones[j].Grupo = opciones[i];
        }
        RecalcularVisibles(opciones);
    }

    /// <summary>Despliega o pliega un grupo plegable; las demás opciones no cambian.</summary>
    public static void Alternar(IReadOnlyList<OpcionPlegable> opciones, OpcionPlegable grupo)
    {
        if (!grupo.EsPlegable) return;
        grupo.Expandida = !grupo.Expandida;
        RecalcularVisibles(opciones);
    }

    private static IEnumerable<OpcionPlegable> Hijas(IReadOnlyList<OpcionPlegable> opciones, int indice)
    {
        var nivel = opciones[indice].Nivel;
        for (var j = indice + 1; j < opciones.Count && opciones[j].Nivel > nivel; j++)
            if (opciones[j].Nivel == nivel + 1) yield return opciones[j];
    }

    private static void RecalcularVisibles(IReadOnlyList<OpcionPlegable> opciones)
    {
        foreach (var o in opciones)
        {
            var visible = true;
            for (var g = o.Grupo; g is not null && visible; g = g.Grupo) visible = g.Expandida;
            o.Visible = visible;
        }
    }
}
