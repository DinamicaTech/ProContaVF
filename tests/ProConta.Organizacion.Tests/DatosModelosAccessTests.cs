using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Mantenimiento;
using ProConta.Pruebas.Access;

namespace ProConta.Organizacion.Tests;

/// <summary>
/// Mantenimientos del esquema 72 para los modelos (contabilidad/modelos-tributarios) contra una copia de pruebas: cargos de la empresa,
/// datos de los empleados para el 190 y sus familiares. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class DatosModelosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2025), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    /// <summary>Un tercero sintético empleado de la sociedad: su IdTercero y su IdEmpleado.</summary>
    private async Task<(int Tercero, int Empleado)> EmpleadoAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        return await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, NIF, CodigoPostal, Activo) VALUES (?, ?, ?, ?, ?)", "FISICA", "Persona sintética", "00000000T", "08001", true);
            var tercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Empleados (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", tercero, true, new DateTime(2025, 1, 1));
            var empleado = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO EmpleadosSociedad (IdEmpleado, IdSociedad, CodigoTrabajador, Activo) VALUES (?, ?, ?, ?)", empleado, "T01", "1", true);
            return (tercero, empleado);
        });
    }

    [Fact]
    public async Task LosCargosPidenLoQueElDoscientosNecesita()
    {
        var (tercero, _) = await EmpleadoAsync();
        var p = new ProveedorCargosSociedad(Bd);
        await p.ListarAsync(_ctx);                                                                    // carga la lista de terceros
        var r = await p.NuevoAsync(_ctx);
        r["IdTercero"] = tercero;
        r["Cargo"] = "TITULAR_REAL";
        r["Activo"] = true;
        Assert.Contains(p.Validar(r), e => e.Campo == "FechaNacimiento");
        r["FechaNacimiento"] = new DateTime(1970, 1, 1);
        r["Nacionalidad"] = "ESP";
        Assert.Contains(p.Validar(r), e => e.Campo == "Nacionalidad");
        r["Nacionalidad"] = "ES";
        Assert.Empty(p.Validar(r));
        var g = await p.GuardarAsync(r, _ctx);
        Assert.True(g.EsCorrecto, g.Error?.ToString());

        var rep = await p.NuevoAsync(_ctx);
        rep["IdTercero"] = tercero;
        rep["Cargo"] = "REPRESENTANTE";
        rep["Activo"] = true;
        Assert.Contains(p.Validar(rep), e => e.Campo == "FechaPoder");
        Assert.Single(await p.ListarAsync(_ctx));
    }

    [Fact]
    public async Task LosDatosDelEmpleadoYSusFamiliaresSeGuardanEnSuFicha()
    {
        var (_, empleado) = await EmpleadoAsync();
        var p = new ProveedorEmpleados190(Bd);
        var r = Assert.Single(await p.ListarAsync(_ctx));
        Assert.Equal("00000000T", r.Texto("Nif"));
        r["AnioNacimiento"] = 1980;
        r["SituacionFamiliar"] = (byte)3;
        r["NifConyuge"] = "00000001R";
        Assert.Contains(p.Validar(r), e => e.Campo == "NifConyuge");                                 // el NIF del cónyuge solo con la situación 2
        r["SituacionFamiliar"] = (byte)2;
        Assert.Empty(p.Validar(r));
        Assert.True((await p.GuardarAsync(r, _ctx)).EsCorrecto);
        var fila = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT AnioNacimiento, SituacionFamiliar, NifConyuge FROM Empleados WHERE IdEmpleado = ?",
            f => (Convert.ToInt32(f.Valor("AnioNacimiento"), CultureInfo.InvariantCulture), Convert.ToInt32(f.Valor("SituacionFamiliar"), CultureInfo.InvariantCulture), f.Texto("NifConyuge")),
            empleado));
        Assert.Equal((1980, 2, "00000001R"), fila);

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE EmpleadosSociedad SET Administrador = True WHERE IdEmpleado = ?", empleado));
        var admin = Assert.Single(await p.ListarAsync(_ctx));
        Assert.Contains("subclave de administrador", admin.Texto("Falta"));
        admin["SubclaveAdministrador190"] = "04";
        Assert.True((await p.GuardarAsync(admin, _ctx)).EsCorrecto);
        Assert.Equal("04", await Bd.EjecutarAsync(async s => (await s.EscalarAsync("SELECT SubclaveAdministrador190 FROM EmpleadosSociedad WHERE IdEmpleado = ?", empleado)) as string));

        var fam = new ProveedorFamiliaresEmpleado(Bd);
        await fam.ListarAsync(_ctx);
        var h = await fam.NuevoAsync(_ctx);
        h["IdEmpleado"] = empleado;
        h["Parentesco"] = "D";
        h["AnioNacimiento"] = 2024;
        h["PorEntero"] = true;
        h["Activo"] = true;
        Assert.Empty(fam.Validar(h));
        Assert.True((await fam.GuardarAsync(h, _ctx)).EsCorrecto);
        Assert.Single(await fam.ListarAsync(_ctx));
    }
}
