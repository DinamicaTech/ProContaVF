namespace ProConta.Documentos;

/// <summary>Estados de un documento (Documentos.Estado).</summary>
public static class EstadosDocumento
{
    /// <summary>Añadido y pendiente de revisar y archivar: está en la bandeja «Por clasificar».</summary>
    public const string PorClasificar = "POR_CLASIFICAR";
    /// <summary>En su carpeta definitiva.</summary>
    public const string Archivado = "ARCHIVADO";
}

/// <summary>Tipos de entidad a los que se vincula un documento (DocumentosVinculos.TipoEntidad).</summary>
public static class TiposEntidad
{
    public const string Empresa = "EMPRESA";
    public const string Ejercicio = "EJERCICIO";
    public const string Modelo = "MODELO";
    public const string Tercero = "TERCERO";
    public const string Activo = "ACTIVO";
    public const string FacturaVenta = "FACTURA_VENTA";
    public const string FacturaCompra = "FACTURA_COMPRA";
    public const string Asiento = "ASIENTO";
    public const string ImportacionNomina = "NOMINA";
    public const string CorreoEnviado = "CORREO_ENVIADO";
    public const string CorreoRecibido = "CORREO_RECIBIDO";

    /// <summary>Nombre corto que se ve en las etiquetas de los vínculos.</summary>
    public static string Nombre(string tipo) => tipo switch
    {
        Empresa => "Empresa",
        Ejercicio => "Ejercicio",
        Modelo => "Modelo",
        Tercero => "Tercero",
        Activo => "Activo",
        FacturaVenta => "Factura emitida",
        FacturaCompra => "Factura recibida",
        Asiento => "Asiento",
        ImportacionNomina => "Nóminas",
        CorreoEnviado => "Correo enviado",
        CorreoRecibido => "Correo recibido",
        _ => tipo,
    };

    /// <summary>Nombre del grupo en el buscador de «Vincular a…».</summary>
    public static string Grupo(string tipo) => tipo switch
    {
        Empresa => "Empresa",
        Ejercicio => "Ejercicios",
        Modelo => "Modelos tributarios",
        Tercero => "Terceros",
        Activo => "Activos",
        FacturaVenta => "Facturas emitidas",
        FacturaCompra => "Facturas recibidas",
        Asiento => "Asientos",
        _ => Nombre(tipo),
    };

    /// <summary>Orden de los grupos en el buscador.</summary>
    public static int Orden(string tipo) => tipo switch
    {
        Modelo => 1, Ejercicio => 2, Empresa => 3, Tercero => 4, Activo => 5, FacturaVenta => 6, FacturaCompra => 7, Asiento => 8, _ => 9,
    };
}

/// <summary>Orígenes de un documento (Documentos.Origen): quién lo produjo.</summary>
public static class OrigenesDocumento
{
    public const string Manual = "MANUAL";
}

/// <summary>
/// Vínculo de un documento con un registro: el tipo de entidad y su Id (<see cref="IdEntidad"/>) o su clave (<see cref="Clave"/>: «303|2026|2T» en
/// un modelo, el código de la sociedad en la empresa). <see cref="Etiqueta"/> es el texto que se enseña («303 · 2T 2026»).
/// </summary>
public sealed record Vinculo(string TipoEntidad, int? IdEntidad, string? Clave, string Etiqueta)
{
    /// <summary>Mismo registro (sin mirar la etiqueta).</summary>
    public bool MismoDestino(Vinculo otro) =>
        string.Equals(TipoEntidad, otro.TipoEntidad, StringComparison.OrdinalIgnoreCase) && IdEntidad == otro.IdEntidad &&
        string.Equals(Clave ?? "", otro.Clave ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Texto para buscar y para el detalle: «Modelo 303 · 2T 2026».</summary>
    public string Texto => $"{TiposEntidad.Nombre(TipoEntidad)} {Etiqueta}";

    public static Vinculo Modelo(string modelo, int ejercicio, string periodo) =>
        new(TiposEntidad.Modelo, null, ClaveModelo(modelo, ejercicio, periodo), EtiquetaModelo(modelo, ejercicio, periodo));

    public static string ClaveModelo(string modelo, int ejercicio, string periodo) => $"{modelo}|{ejercicio}|{periodo.ToUpperInvariant()}";

    /// <summary>«303 · 2T 2026», «390 · 2025» (anual), «111 · enero 2026» (mensual).</summary>
    public static string EtiquetaModelo(string modelo, int ejercicio, string periodo) => PeriodoTexto(periodo) is { Length: > 0 } p
        ? $"{modelo} · {p} {ejercicio}"
        : $"{modelo} · {ejercicio}";

    /// <summary>Texto del periodo de un modelo: 1T…4T tal cual, 1P…3P tal cual, 01…12 el mes y 0A (anual) vacío.</summary>
    public static string PeriodoTexto(string periodo)
    {
        var p = (periodo ?? "").Trim().ToUpperInvariant();
        if (p is "0A" or "") return "";
        if (p.Length == 2 && int.TryParse(p, out var mes) && mes is >= 1 and <= 12)
            return System.Globalization.CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(mes);
        return p;
    }
}

/// <summary>Tipo de documento (TiposDocumento).</summary>
public sealed record TipoDocumento(string Codigo, string Nombre, string Carpeta, int Orden, bool PorAnio, bool Manual);

/// <summary>Una versión de un documento: su fichero en disco.</summary>
public sealed record VersionDocumento(int IdVersionDocumento, int NumeroVersion, string Ruta, string RutaCompleta, string? NombreOriginal, long TamanoBytes,
    string Huella, DateTime FechaAlta, string? Operador, string? Motivo);

/// <summary>Grado de confianza de lo que el programa propone al leer un fichero.</summary>
public enum Confianza
{
    /// <summary>Nada reconocido: el operador completa el tipo, la fecha y el vínculo.</summary>
    Ninguna,
    /// <summary>Algo reconocido por el texto, para revisar.</summary>
    Media,
    /// <summary>Reconocido por un dato que lo identifica (número de justificante, NIF de un tercero).</summary>
    Alta,
    /// <summary>Es una factura de compra: se registra en Compras, no aquí.</summary>
    FacturaCompra,
    /// <summary>El mismo contenido ya está en el gestor.</summary>
    Duplicado,
}

/// <summary>Lo que se propone para un fichero al añadirlo: tipo, fecha, título y vínculos, con su confianza y el motivo.</summary>
public sealed record Propuesta(string? Tipo, DateTime? Fecha, string Titulo, IReadOnlyList<Vinculo> Vinculos, Confianza Confianza, string Motivo)
{
    /// <summary>Texto guardado en Documentos.Propuesta: «ALTA|motivo».</summary>
    public string Serializar() => $"{Confianza.ToString().ToUpperInvariant()}|{Motivo}";

    public static (Confianza Confianza, string Motivo) Leer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (Confianza.Ninguna, "");
        var i = texto.IndexOf('|');
        var codigo = i < 0 ? texto : texto[..i];
        var motivo = i < 0 ? "" : texto[(i + 1)..];
        return (Enum.TryParse<Confianza>(codigo, true, out var c) ? c : Confianza.Ninguna, motivo);
    }
}

/// <summary>Un documento con su versión vigente y sus vínculos, como se ve en las listas.</summary>
public sealed record DocumentoListado(
    int IdDocumento,
    string IdSociedad,
    string? Tipo,
    string? NombreTipo,
    string Titulo,
    DateTime? FechaDocumento,
    int? Ejercicio,
    string Estado,
    string Origen,
    string? Notas,
    Confianza Confianza,
    string Motivo,
    DateTime FechaAlta,
    VersionDocumento? Vigente,
    int NumeroVersiones,
    IReadOnlyList<Vinculo> Vinculos)
{
    public bool PorClasificar => Estado == EstadosDocumento.PorClasificar;

    /// <summary>Se puede archivar: tiene tipo, título, fecha y algún vínculo, y no es una factura de compra.</summary>
    public bool Listo => Tipo is { Length: > 0 } && Titulo.Trim().Length > 0 && FechaDocumento is not null && Vinculos.Count > 0 && Confianza != Confianza.FacturaCompra;
}

/// <summary>Un registro al que se puede vincular un documento, como lo devuelve el buscador de «Vincular a…».</summary>
public sealed record DestinoVinculo(Vinculo Vinculo, string Detalle)
{
    public string Grupo => TiposEntidad.Grupo(Vinculo.TipoEntidad);
    public int OrdenGrupo => TiposEntidad.Orden(Vinculo.TipoEntidad);
}

/// <summary>
/// Lo que pide un módulo productor al guardar un documento: el productor decide el nombre del fichero y la subcarpeta (con su semántica: número
/// de factura y cliente, modelo y periodo…), el gestor solo evita sobrescribir, limpia el nombre y registra la huella, la ruta y los vínculos
/// (propietario 2026-11-18 16:27).
/// </summary>
/// <param name="Subcarpeta">Relativa a la carpeta de documentos de la empresa (por ejemplo «2026\Modelos»), o una ruta completa fuera de ella.</param>
/// <param name="NombreFichero">Nombre propuesto, con su extensión.</param>
/// <param name="NuevaVersionDe">Documento del que esto es una versión nueva (por ejemplo, el PDF regenerado de una factura); null: documento nuevo.</param>
public sealed record SolicitudDocumento(
    string IdSociedad,
    string Tipo,
    string Titulo,
    string Subcarpeta,
    string NombreFichero,
    DateTime? FechaDocumento,
    int? Ejercicio,
    string Origen,
    IReadOnlyList<Vinculo> Vinculos,
    int? NuevaVersionDe = null,
    string? Motivo = null);

/// <summary>Resultado de guardar o registrar un documento.</summary>
public sealed record DocumentoGuardado(int IdDocumento, int IdVersionDocumento, int NumeroVersion, string Ruta, string RutaCompleta, bool YaExistia);

/// <summary>Resultado de añadir ficheros a la bandeja.</summary>
public sealed record ResultadoAnadir(IReadOnlyList<int> Anadidos, IReadOnlyList<string> Repetidos, IReadOnlyList<string> Errores);

/// <summary>Resultado de archivar documentos de la bandeja.</summary>
public sealed record ResultadoArchivar(int Archivados, IReadOnlyList<string> NoListos, IReadOnlyList<string> Errores);
