using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>
/// «Pago de dividendos…» de Líneas N43 (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55) contra una copia de pruebas con datos inventados:
/// un cargo a un socio va a su 526; un cargo de 100.000 € a dos socios al 50 % se desglosa en dos líneas de 50.000 € con su 526, y «Deshacer
/// explosión» lo deja como estaba.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class PagoDividendosN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];
    private int[] _socios = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioRemesasN43 Remesas => new(Bd);
    private ConsultaLineasN43 Consulta => new(Bd);

    private async Task<LineaN43> LineaAsync(int id) => Assert.Single(await Consulta.LineasAsync("T01", null, [id]));

    /// <summary>
    /// Sociedad T01 con su banco y la cuenta ficticia de remesas, dos socios al 50 % con sus 526 y un tercero sin 526. El extracto trae un cargo de
    /// 100.000 € (a los dos), otro de 30.000 € (a uno) y un abono.
    /// </summary>
    private async Task<int[]> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco uno ficticio"), ("5550025", "Remesas ficticias"), ("5260001", "Socia Uno"), ("5260002", "Socio Dos"),
                     ("4751011", "Socia Uno"), ("4751012", "Socio Dos") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        _socios = await Bd.EjecutarAsync(async s =>
        {
            foreach (var (cuenta, nombre, contable) in new[] { ("0012345678", "Banco uno ficticio", "5720001"), (ServicioRemesasN43.CuentaRemesas, "Banco uno REMESAS", "5550025") })
            {
                await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    "T01", "0049", "1500", cuenta, "EUR", nombre, true);
                var id = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                    id, _identidades[contable], new DateTime(2000, 1, 1));
            }
            var socios = new List<int>();
            foreach (var (nombre, c526, c4751) in new[] { ("Socia Uno", "5260001", "4751011"), ("Socio Dos", "5260002", "4751012") })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Socios (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", t, true, new DateTime(2020, 1, 1));
                var socio = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO SociosSociedad (IdSocio, IdSociedad, Participacion, IdIdentidadDividendos, IdIdentidadRetencion, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                    socio, "T01", 50m, _identidades[c526], _identidades[c4751], true);
                socios.Add(socio);
            }
            return socios.ToArray();
        });
        var movs = new[]
        {
            new Mov(new DateTime(2026, 7, 10), 100000m, true, "TRANSFERENCIA DIVIDENDOS SOCIOS"),
            new Mov(new DateTime(2026, 7, 11), 30000m, true, "TRANSFERENCIA A SOCIA UNO"),
            new Mov(new DateTime(2026, 7, 12), 500m, false, "ABONO CUALQUIERA"),
        };
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 200000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    [Fact]
    public async Task Un_cargo_a_dos_socios_al_50_se_desglosa_en_dos_lineas_con_su_526_y_se_deshace()
    {
        var ids = await PrepararAsync();
        var p = await Remesas.ProponerPagoDividendosAsync("T01", ids[0]);
        Assert.NotNull(p);
        Assert.Null(p.Bloqueo);
        Assert.Equal([("Socia Uno", 50000m, "5260001"), ("Socio Dos", 50000m, "5260002")], p.Socios.Select(s => (s.Nombre, s.ImportePropuesto, s.Cuenta526)));

        // Que no cuadre no se graba.
        var mal = await Remesas.PagarDividendosAsync("T01", ids[0], [new(_socios[0], 50000m), new(_socios[1], 40000m)], _actor);
        Assert.Equal("Dividendos.NoCuadra", mal.Error?.Codigo);

        var r = await Remesas.PagarDividendosAsync("T01", ids[0], p.Socios.Select(s => new PagoSocio(s.IdSocio, s.ImportePropuesto)).ToList(), _actor);
        Assert.True(r.Hecho, r.Error?.TextoOperador());
        Assert.Equal(2, r.Recibos.Count);
        var hijas = (await Consulta.LineasAsync("T01", null, r.Recibos)).OrderBy(l => l.Id).ToList();
        Assert.All(hijas, h => Assert.Equal((ids[0], "D", "5550025", 50000m), (h.IdLineaOrigen!.Value, h.Sentido, h.CuentaOrigen, h.Importe)));
        Assert.Equal(["5260001", "5260002"], hijas.Select(h => h.CuentaDestino));
        Assert.Equal(["Pago de dividendos Socia Uno", "Pago de dividendos Socio Dos"], hijas.Select(h => h.Concepto));
        var origen = await LineaAsync(ids[0]);
        Assert.True(origen.Explotada);
        Assert.Equal("5550025", origen.CuentaDestino);
        Assert.Equal("DIVIDENDOS", await Bd.EjecutarAsync(s => s.EscalarAsync(
            "SELECT lo.Formato FROM ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria WHERE e.IdLineaExtractoOrigen = ?", ids[0])));
        // Ya desglosado no se vuelve a pagar.
        Assert.Equal("Dividendos.YaExplotada", (await Remesas.ProponerPagoDividendosAsync("T01", ids[0]))!.Bloqueo?.Codigo);
        Assert.NotNull(AccionesLineasN43.Evaluar([origen]).PagoDividendos);
        Assert.Null(AccionesLineasN43.Evaluar([origen]).DeshacerExplosion);

        // Deshacer la explosión lo deja como estaba: sin desglose y sin cuenta, y se puede volver a pagar.
        var u = await Remesas.DeshacerAsync("T01", ids[0], _actor);
        Assert.True(u.Hecho, u.Error?.TextoOperador());
        var deshecha = await LineaAsync(ids[0]);
        Assert.False(deshecha.Explotada);
        Assert.Empty(deshecha.Destinos);
        Assert.Empty(await Consulta.LineasAsync("T01", null, u.Recibos));
        Assert.Null((await Remesas.ProponerPagoDividendosAsync("T01", ids[0]))!.Bloqueo);
    }

    [Fact]
    public async Task Un_cargo_a_un_socio_va_a_su_526_y_la_contabilidad_cuadra()
    {
        var ids = await PrepararAsync();
        var r = await Remesas.PagarDividendosAsync("T01", ids[1], [new(_socios[0], 30000m)], _actor);
        Assert.True(r.Hecho, r.Error?.TextoOperador());
        Assert.Empty(r.Recibos);
        var l = await LineaAsync(ids[1]);
        Assert.False(l.Explotada);
        var d = Assert.Single(l.Destinos);
        Assert.Equal(("5260001", ServicioRemesasN43.OrigenDividendos), (d.Cuenta, d.Origen));

        // Contabilizar: la 526 al Debe por 30.000 y el banco al Haber; debe = haber.
        var conciliacion = new ServicioConciliacionN43(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd));
        var c = await conciliacion.ContabilizarLineasAsync("T01", [ids[1]], _actor);
        Assert.Empty(c.Rechazadas);
        const string saldo = "SELECT SUM(IIF(ImporteDebe IS NULL, 0, ImporteDebe)) - SUM(IIF(ImporteHaber IS NULL, 0, ImporteHaber)) FROM Apuntes";
        Assert.Equal(30000m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(saldo + " WHERE CuentaContable = ?", "5260001"), CultureInfo.InvariantCulture)));
        Assert.Equal(0m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(saldo), CultureInfo.InvariantCulture)));
        // Contabilizada ya no admite el pago.
        Assert.Equal("Dividendos.Contabilizada", (await Remesas.ProponerPagoDividendosAsync("T01", ids[1]))!.Bloqueo?.Codigo);
    }

    [Fact]
    public async Task Un_abono_o_una_sociedad_sin_socios_no_admiten_el_pago()
    {
        var ids = await PrepararAsync();
        Assert.Equal("Dividendos.NoEsCargo", (await Remesas.ProponerPagoDividendosAsync("T01", ids[2]))!.Bloqueo?.Codigo);
        Assert.NotNull(AccionesLineasN43.Evaluar([await LineaAsync(ids[2])]).PagoDividendos);
        Assert.Null(AccionesLineasN43.Evaluar([await LineaAsync(ids[0])]).PagoDividendos);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociosSociedad SET Activo = False"));
        Assert.Equal("Dividendos.SinSocios", (await Remesas.ProponerPagoDividendosAsync("T01", ids[0]))!.Bloqueo?.Codigo);
    }
}
