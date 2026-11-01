namespace ProConta.Terceros.UI.Logica;

/// <summary>Ficha abierta en una pestaña: su clave (IdTercero, o negativa si es nueva), cuándo se abrió y si tiene cambios.</summary>
public sealed record FichaAbierta(int Clave, long OrdenApertura, bool TieneCambios);

public enum AccionApertura
{
    /// <summary>Ya estaba abierta: se activa su pestaña.</summary>
    Activar,
    /// <summary>Hay sitio: se abre una pestaña nueva.</summary>
    Abrir,
    /// <summary>Se sustituye, en su misma posición, la ficha abierta hace más tiempo sin cambios pendientes.</summary>
    Sustituir,
    /// <summary>Las cinco tienen cambios: se avisa y no se abre.</summary>
    Rechazar
}

public sealed record DecisionApertura(AccionApertura Accion, int? ClaveSustituida = null);

/// <summary>
/// Límite de fichas abiertas (maqueta del contenedor, escenario terceros-5): como máximo cinco; al abrir la sexta se
/// sustituye la abierta hace más tiempo que no tenga cambios pendientes y, si todas los tienen, no se abre.
/// </summary>
public static class GestorFichas
{
    public const int Maximo = 5;

    /// <param name="clave">IdTercero de la ficha que se quiere abrir, o null si es una ficha nueva.</param>
    public static DecisionApertura Decidir(IReadOnlyCollection<FichaAbierta> abiertas, int? clave)
    {
        if (clave is int id && abiertas.Any(f => f.Clave == id)) return new(AccionApertura.Activar, id);
        if (abiertas.Count < Maximo) return new(AccionApertura.Abrir);
        var candidata = abiertas.Where(f => !f.TieneCambios).OrderBy(f => f.OrdenApertura).FirstOrDefault();
        return candidata is null ? new(AccionApertura.Rechazar) : new(AccionApertura.Sustituir, candidata.Clave);
    }
}
