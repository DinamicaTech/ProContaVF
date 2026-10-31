namespace ProConta.Datos.Persistencia;

/// <summary>Estructura física leída de la BD; la usan el ActualizadorEsquema y el diccionario de datos.</summary>
public sealed record EsquemaFisico(IReadOnlyList<TablaFisica> Tablas)
{
    public TablaFisica? Tabla(string nombre) =>
        Tablas.FirstOrDefault(t => string.Equals(t.Nombre, nombre, StringComparison.OrdinalIgnoreCase));

    public bool ExisteTabla(string nombre) => Tabla(nombre) is not null;

    public bool ExisteColumna(string tabla, string columna) => Tabla(tabla)?.Columna(columna) is not null;

    public bool ExisteIndice(string tabla, string indice) =>
        Tabla(tabla)?.Indices.Any(i => string.Equals(i.Nombre, indice, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <param name="EsVinculada">true si es una tabla vinculada a otro fichero.</param>
public sealed record TablaFisica(
    string Nombre,
    bool EsVinculada,
    string? Descripcion,
    IReadOnlyList<ColumnaFisica> Columnas,
    IReadOnlyList<IndiceFisico> Indices,
    IReadOnlyList<RelacionFisica> Relaciones)
{
    public ColumnaFisica? Columna(string nombre) =>
        Columnas.FirstOrDefault(c => string.Equals(c.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
}

/// <param name="Tipo">Tipo Access legible (TEXT(100), LONG, CURRENCY…).</param>
public sealed record ColumnaFisica(string Nombre, int Posicion, string Tipo, int? Longitud, bool AdmiteNulos, string? PorDefecto, string? Descripcion);

public sealed record IndiceFisico(string Nombre, bool EsClavePrimaria, bool EsUnico, IReadOnlyList<string> Columnas);

/// <summary>Relación (clave ajena) de esta tabla hacia otra.</summary>
public sealed record RelacionFisica(string Nombre, IReadOnlyList<string> Columnas, string TablaReferenciada, IReadOnlyList<string> ColumnasReferenciadas, string ReglaBorrado, string ReglaActualizacion);
