using ProConta.Compras.Mantenimiento;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Compras.Tests;

/// <summary>
/// Catálogos › Cambios de deducción de las actividades y su lectura (contabilidad/procesos/cambio-deduccion-actividad) contra una
/// copia con datos sintéticos: alta, fecha repetida, calendario en la ficha de la actividad y deducción por fecha al leer los centros.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CambiosDeduccionAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task<int> TuristicoAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa) VALUES (?, ?, ?, ?, ?, ?)",
            "T01", "ALQTUR", "Alquiler turístico", (byte)0, "4728", true));
        var id = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MAX(IdActividad) FROM Actividades")));
        var centros = new CatalogoCentrosCosteAccess(Bd);
        var centro = (await centros.AltaAsync("T01", "3", "Alquileres")).Valor.IdCentroCoste;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ?", id, centro));
        return id;
    }

    [Fact]
    public async Task Se_da_de_alta_el_cambio_y_la_actividad_deduce_segun_la_fecha()
    {
        var id = await TuristicoAsync();
        var proveedor = new ProveedorCambiosDeduccion(Bd);
        var nuevo = await proveedor.NuevoAsync(_ctx);
        Assert.Equal((byte)2, nuevo["Deducible"]);
        nuevo["IdActividad"] = id;
        nuevo["FechaDesde"] = new DateTime(2026, 12, 1);
        var g = await proveedor.GuardarAsync(nuevo, _ctx);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal("ALQTUR · Alquiler turístico", g.Valor.Texto("Actividad"));
        Assert.Equal("01/12/2026", g.Valor.Texto("Desde"));
        Assert.Equal("100 % deducible", g.Valor.Texto("Deduccion"));

        // Otro cambio de la misma actividad y el mismo día no se admite.
        var repetido = await proveedor.NuevoAsync(_ctx);
        repetido["IdActividad"] = id;
        repetido["FechaDesde"] = new DateTime(2026, 12, 1);
        var r = await proveedor.GuardarAsync(repetido, _ctx);
        Assert.False(r.EsCorrecto);
        Assert.Equal("CambiosDeduccion.Repetido", r.Error!.Codigo);

        // La ficha de la actividad enseña el calendario.
        var actividad = (await new ProveedorActividades(Bd).ListarAsync(_ctx)).Single();
        Assert.Equal("No deducible hasta el 30/11/2026 · 100 % deducible desde el 01/12/2026", actividad.Texto("Calendario"));

        // Quien lee la actividad del centro la ve con su cambio.
        var leida = (await new LectorActividadesIva(Bd).PorCentroAsync("T01")).Values.Single();
        Assert.Equal(0, leida.DeducibleEn(new DateTime(2026, 11, 30)));
        Assert.Equal(2, leida.DeducibleEn(new DateTime(2026, 12, 1)));

        // Borrar el cambio devuelve la actividad a su deducción de siempre.
        Assert.True((await proveedor.EliminarAsync(g.Valor, _ctx)).EsCorrecto);
        Assert.Empty(await proveedor.ListarAsync(_ctx));
        Assert.Equal(0, (await new LectorActividadesIva(Bd).PorCentroAsync("T01")).Values.Single().DeducibleEn(new DateTime(2026, 12, 1)));
    }
}
