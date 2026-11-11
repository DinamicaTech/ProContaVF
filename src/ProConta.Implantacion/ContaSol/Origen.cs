namespace ProConta.Implantacion.ContaSol;

// Datos leídos de una BD de ContaSol (un ejercicio de una empresa). Solo los campos que se importan o que sirven para
// comprobar: los campos con el mismo valor en todas las filas no se leen (criterio del propietario, 2026-11-10).

/// <summary>F_EMP: empresa y ejercicio de la BD.</summary>
public sealed record EmpresaContaSol(string Codigo, string Nif, string Nombre, int Ejercicio);

/// <summary>F_DEP: departamento (raíz de los centros de coste).</summary>
public sealed record DepartamentoContaSol(int Codigo, string Nombre, string? Observaciones);

/// <summary>F_CDP: subdepartamento (centro de coste hijo de su departamento).</summary>
public sealed record SubdepartamentoContaSol(int Departamento, int Codigo, string Nombre, string? Observaciones);

/// <summary>F_MAE: cuenta del plan contable del ejercicio.</summary>
public sealed record CuentaContaSol(string Codigo, string Nombre, string? Descripcion, int Departamento, int Subdepartamento);

/// <summary>F_APU: una línea de asiento. <see cref="Importe"/> es IMEAPU (euros); IMPAPU no es fiable (en 2018 son pesetas).</summary>
public sealed record ApunteContaSol(
    int Diario,
    int Asiento,
    int Orden,
    DateTime Fecha,
    string Cuenta,
    decimal Importe,
    bool AlDebe,
    string? Concepto,
    string? Documento,
    int Departamento,
    int Subdepartamento,
    string? TipoIva,
    int CodigoIva);

/// <summary>F_OAS: texto (observaciones) de un asiento.</summary>
public sealed record TextoAsientoContaSol(int Diario, int Asiento, string Texto);

/// <summary>Un tramo de IVA de un registro del libro (BA1..3, PI1..3, CI1..3 y, en repercutido, PR/CR).</summary>
public sealed record TramoIvaContaSol(decimal Base, decimal Tipo, decimal Cuota, decimal TipoRecargo = 0m, decimal CuotaRecargo = 0m);

/// <summary>F_IVS (soportado) o F_IVR (repercutido): un registro del libro de IVA.</summary>
public sealed record RegistroIvaContaSol(
    bool Soportado,
    int Codigo,
    DateTime FechaRegistro,
    DateTime? FechaFactura,
    DateTime? FechaOperacion,
    string Cuenta,
    string? NumeroFactura,
    string? Nombre,
    string? Nif,
    decimal BaseExenta,
    IReadOnlyList<TramoIvaContaSol> Tramos,
    decimal Total,
    int TipoOperacion,
    bool Incluido347,
    decimal PorcentajeDeducible,
    string? Observaciones);

/// <summary>F_CLI o F_PRO: ficha de cliente o proveedor. El código es la propia subcuenta (p. ej. 4300007).</summary>
public sealed record TerceroContaSol(
    bool EsCliente,
    string Codigo,
    string Nombre,
    string? NombreComercial,
    string? Nif,
    string? Direccion,
    string? CodigoPostal,
    string? Poblacion,
    string? Provincia,
    string? Pais,
    string? Telefono,
    string? Email);

/// <summary>Un ejercicio completo leído de ContaSol.</summary>
public sealed record OrigenContaSol(
    string Ruta,
    string Huella,
    EmpresaContaSol Empresa,
    IReadOnlyList<DepartamentoContaSol> Departamentos,
    IReadOnlyList<SubdepartamentoContaSol> Subdepartamentos,
    IReadOnlyList<CuentaContaSol> Cuentas,
    IReadOnlyList<ApunteContaSol> Apuntes,
    IReadOnlyList<TextoAsientoContaSol> Textos,
    IReadOnlyList<RegistroIvaContaSol> RegistrosIva,
    IReadOnlyList<TerceroContaSol> Terceros,
    IReadOnlyDictionary<string, decimal> SaldosControl);
