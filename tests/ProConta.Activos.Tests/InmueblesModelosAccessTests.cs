using System.Globalization;
using ProConta.Activos.Mantenimiento;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Activos.Tests;

/// <summary>
/// «Inmuebles para los modelos (180 y 347)» contra una copia de pruebas (esquema 72): lista los inmuebles de la empresa con lo que les falta,
/// pide la referencia catastral con las situaciones 1 a 3 y crea o actualiza su fila de ActivosInmuebles. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class InmueblesModelosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2025), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task<int> ActivoAsync(string tipo, string nombre, bool ajeno) => await Bd.EjecutarAsync(async s =>
    {
        var idTipo = Convert.ToInt32(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = ?", tipo), CultureInfo.InvariantCulture);
        await s.EjecutarAsync(
            "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
            "ValorContable, EstadoCalculo, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", idTipo, nombre.ToUpperInvariant().Replace(' ', '-'), nombre, new DateTime(2025, 1, 1), (byte)1, !ajeno, 0m, 0m, 0m, 0m, (byte)0, ajeno, 0);
        return await s.UltimoIdAsync();
    });

    [Fact]
    public async Task CompletaLaSituacionYLaDireccionDeCadaInmueble()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        var piso = await ActivoAsync("VIVIENDA", "Piso sintético", ajeno: true);
        var p = new ProveedorInmueblesModelos(Bd);

        var r = Assert.Single(await p.ListarAsync(_ctx));
        Assert.Equal(piso, Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture));
        Assert.Contains("(ajeno)", r.Texto("Activo"));
        Assert.Equal("Falta situación, dirección, municipio y provincia", r.Texto("Falta"));

        r["SituacionInmueble"] = (byte)1;
        Assert.Contains(p.Validar(r), e => e.Campo == "ReferenciaCatastral");                     // con la situación 1 hace falta la referencia
        r["ReferenciaCatastral"] = "1111102df1711s0001xm";
        r["CodigoMunicipio"] = "8001";
        Assert.Contains(p.Validar(r), e => e.Campo == "CodigoMunicipio");
        r["CodigoMunicipio"] = "08001";
        r["CodigoProvincia"] = "08";
        r["TipoVia"] = "CALLE";
        r["NombreVia"] = "Sintética";
        r["NumeroCasa"] = "7";
        r["Municipio"] = "Municipio sintético";
        Assert.Empty(p.Validar(r));
        var g = await p.GuardarAsync(r, _ctx);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal("1111102DF1711S0001XM", g.Valor.Texto("ReferenciaCatastral"));
        Assert.Equal("SINTÉTICA", g.Valor.Texto("NombreVia"));
        Assert.True(string.IsNullOrEmpty(g.Valor.Texto("Falta")));

        g.Valor["SituacionInmueble"] = (byte)4;
        g.Valor["ReferenciaCatastral"] = null;
        Assert.Empty(p.Validar(g.Valor));                                                              // sin referencia es la situación 4
        Assert.True((await p.GuardarAsync(g.Valor, _ctx)).EsCorrecto);
        var fila = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT COUNT(*) AS n, MAX(SituacionInmueble) AS sit FROM ActivosInmuebles WHERE IdActivo = ?",
            f => (N: Convert.ToInt32(f.Valor("n"), CultureInfo.InvariantCulture), Sit: Convert.ToInt32(f.Valor("sit"), CultureInfo.InvariantCulture)), piso));
        Assert.Equal((1, 4), fila);                                                                     // la segunda vez actualiza, no duplica
    }
}
