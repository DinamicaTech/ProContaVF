namespace ProConta.Nominas.Lectura;

/// <summary>Tipo de paga de la importación: la mensual (con o sin finiquito) o una paga extra.</summary>
public enum TipoPaga { Mensual, Extra }

/// <summary>Incidencia de la lectura, con la fila del Excel (1 = primera) si es de una fila.</summary>
public sealed record IncidenciaNominas(string Texto, int? Fila = null)
{
    public override string ToString() => Fila is int f ? $"Fila {f}: {Texto}" : Texto;
}

/// <summary>Columna del Excel sin alias: hay que decir a qué campo corresponde (o ignorarla) antes de importar.</summary>
/// <param name="Cabecera">Texto de la cabecera, o «Columna N» si está vacía y la columna tiene datos.</param>
/// <param name="Ejemplo">Primer valor de la columna, para reconocerla.</param>
public sealed record CabeceraSinAlias(string Cabecera, int Columna, string? Ejemplo);

/// <summary>Una fila del Excel: un trabajador.</summary>
/// <param name="Fila">Número de fila en el Excel (1 = primera).</param>
/// <param name="Importes">Importes por campo (solo los que trae el fichero; vacío = 0).</param>
public sealed record FilaNomina(int Fila, int Ordinal, string? CodigoTrabajador, string Nombre, string? Nif, TipoPaga TipoPaga, DateTime FechaNomina,
    IReadOnlyDictionary<string, decimal> Importes)
{
    public decimal this[string campo] => Importes.GetValueOrDefault(campo);

    /// <summary>Cuotas obreras: la columna si la trae; si no, TC1 − SS de la empresa.</summary>
    public decimal CuotasObreras => Importes.TryGetValue("CuotasObreras", out var c) ? c : this["TC1"] - this["SSEmp"];

    /// <summary>
    /// Neto calculado: bruto + descuentos en especie y embargos (llegan en negativo) + ingreso a cuenta en especie − autónomo
    /// − IRPF − cuotas obreras − anticipos − falta de preaviso. Debe dar el neto del fichero.
    /// </summary>
    public decimal NetoCalculado => this["Bruto"] + this["DtoReEsp"] + this["InRetEsp"] + this["RetEmbarg"] - this["Autonomo"] - this["IRPF111"]
        - CuotasObreras - Math.Abs(this["AnticOtro"]) - Math.Abs(this["FaltaPrea"]);

    public decimal DescuadreNeto => NetoCalculado - this["Neto"];
}

/// <summary>Resultado de leer el Excel de nóminas de la gestoría.</summary>
public sealed record FicheroNominas(
    string? CodigoEmpresa, string? NombreEmpresa, string? NifEmpresa, string? TextoPaga, TipoPaga TipoPaga,
    DateTime? PeriodoDesde, DateTime? PeriodoHasta, DateTime? FechaNomina,
    IReadOnlyList<FilaNomina> Filas, IReadOnlyDictionary<int, string> Mapeo, IReadOnlyList<CabeceraSinAlias> SinAlias,
    IReadOnlyList<IncidenciaNominas> Errores, IReadOnlyList<IncidenciaNominas> Advertencias, bool TieneTotal)
{
    /// <summary>Se puede importar: sin errores y sin cabeceras por decidir.</summary>
    public bool EsValido => Errores.Count == 0 && SinAlias.Count == 0 && Filas.Count > 0;

    /// <summary>Mes (día 1) que cubre la nómina.</summary>
    public DateTime? Mes => (PeriodoDesde ?? FechaNomina) is DateTime f ? new DateTime(f.Year, f.Month, 1) : null;

    public decimal Total(string campo) => Filas.Sum(f => f[campo]);
}
