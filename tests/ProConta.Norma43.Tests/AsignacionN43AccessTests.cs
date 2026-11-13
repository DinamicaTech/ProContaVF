using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>Asignación de cuentas por palabras clave contra una copia de pruebas con datos inventados (norma43/asignacion-cuentas).</summary>
[Trait("Categoria", "Access")]
public sealed class AsignacionN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private static readonly DateTime Nov = new(2026, 11, 1);
    private int _luz, _cliente, _gimnasio;

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioImportacionN43 Servicio => new(Bd);
    private AsignadorCuentasN43 Asignador => new(Bd);

    private static Mov M(int dia, decimal importe, bool cargo, string concepto) => new(new DateTime(2026, 11, dia), importe, cargo, concepto);

    private static readonly Mov[] Semana1 = [M(2, 120.50m, true, "RECIBO LUZ FICTICIA"), M(3, 2500m, false, "TRANSFERENCIA CLIENTE INVENTADO"), M(4, 3.20m, true, "CAFETERIA")];
    private static readonly Mov[] Semana2 = [M(9, 60m, true, "CUOTA GIMNASIO FICTICIO"), M(10, 80.10m, true, "RECIBO LUZ FICTICIA")];

    private static byte[] Fichero(DateTime desde, DateTime hasta, decimal saldo, params Mov[] movs) =>
        new FicheroSintetico().Cuenta("0049", "1500", "0012345678", desde, hasta, saldo, "EMPRESA FICTICIA", movs).Bytes();

    private async Task EmpresaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Otra sociedad sintética", "B87654321");
        await DatosSinteticos.EjercicioAsync(Bd, 9101, "T02", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        async Task<int> Cuenta(string codigo, string nombre)
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            return c.Valor.IdIdentidadContable;
        }
        _luz = await Cuenta("6280001", "Suministro ficticio");
        _cliente = await Cuenta("4300001", "Cliente inventado");
        _gimnasio = await Cuenta("6290001", "Gimnasio ficticio");
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
            "T01", "0049", "1500", "0012345678", "EUR", "Banco ficticio", true));
    }

    // Sentido desde la 572: H para cargos, D para abonos.
    private Task<int> PalabraAsync(string palabra, string? sentido, int? cuenta, string? palabra2 = null, string? entidad = "0049", string sociedad = "T01", int? especial = null) =>
        Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                sociedad, entidad, sentido, palabra, palabra2, cuenta, especial, true);
            return await s.UltimoIdAsync();
        });

    private Task<IReadOnlyList<(string Concepto, int Cuenta, decimal Importe, string Origen, int? Palabra)>> ImputacionesAsync() =>
        Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT l.ConceptoCompleto, i.IdIdentidadContable, i.ImporteFirmado, i.Origen, i.IdPalabraClaveBancaria FROM ImputacionesLineaExtractoBancario AS i " +
            "INNER JOIN LineasExtractoBancario AS l ON i.IdLineaExtractoBancario = l.IdLineaExtractoBancario ORDER BY l.IdLineaExtractoBancario",
            f => (f.Texto("ConceptoCompleto") ?? "", f.Entero("IdIdentidadContable"), f.Decimal("ImporteFirmado"), f.Texto("Origen") ?? "", f.EnteroNulo("IdPalabraClaveBancaria"))));

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    [Fact]
    public async Task Al_importar_las_lineas_toman_la_cuenta_de_su_palabra_clave()
    {
        await EmpresaAsync();
        var luz = await PalabraAsync("luz ficticia", "H", _luz);
        var cliente = await PalabraAsync("cliente inventado", "D", _cliente);
        await PalabraAsync("cliente inventado", "D", _luz, sociedad: "T02");

        var r = await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);

        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        Assert.NotNull(r.Asignacion);
        Assert.Equal((3, 2, 1), (r.Asignacion!.Revisadas, r.Asignacion.Asignadas, r.Asignacion.SinPalabra));
        Assert.Contains("2 de 3", r.Explicacion);
        var imp = await ImputacionesAsync();
        Assert.Equal(2, imp.Count);
        Assert.Equal(("RECIBO LUZ FICTICIA", _luz, 120.50m, "PALABRA_CLAVE", (int?)luz), imp[0]);
        Assert.Equal(("TRANSFERENCIA CLIENTE INVENTADO", _cliente, -2500m, "PALABRA_CLAVE", (int?)cliente), imp[1]);
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE Rol = 'CONTRAPARTIDA' AND Estado = 'CONFIRMADA'"));
    }

    [Fact]
    public async Task Solo_trata_el_lote_importado_y_nunca_pisa_una_cuenta_puesta()
    {
        await EmpresaAsync();
        await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);
        // La luz de la semana 1 se pone a mano en otra cuenta; después nace la palabra clave.
        var lineaLuz = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MIN(IdLineaExtractoBancario) FROM LineasExtractoBancario")), CultureInfo.InvariantCulture);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado) VALUES (?, ?, ?, ?, ?, ?)",
            lineaLuz, _gimnasio, "CONTRAPARTIDA", 120.50m, "MANUAL", "CONFIRMADA"));
        await PalabraAsync("luz ficticia", "H", _luz);
        await PalabraAsync("gimnasio", "H", _gimnasio);

        var r = await Servicio.ImportarAsync(_ctx, Fichero(new(2026, 11, 8), new(2026, 11, 14), 3376.30m, Semana2), "semana2.n43", null, _actor);
        Assert.Equal((2, 2), (r.Asignacion!.Revisadas, r.Asignacion.Asignadas));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ?", lineaLuz));
        Assert.Equal(_gimnasio, await ContarAsync("SELECT IdIdentidadContable FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ?", lineaLuz));

        // La pasada de toda la sociedad solo encuentra la transferencia y la cafetería, que no tienen palabra; repetirla no cambia nada.
        var todo = await Asignador.AsignarAsync(_ctx);
        Assert.Equal((2, 0, 2), (todo.Revisadas, todo.Asignadas, todo.SinPalabra));
        var antes = await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario");
        await Asignador.AsignarAsync(_ctx);
        Assert.Equal(antes, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario"));
    }

    [Fact]
    public async Task Una_imputacion_descartada_deja_la_linea_libre_y_un_asiento_la_bloquea()
    {
        await EmpresaAsync();
        await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);
        var lineas = await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY IdLineaExtractoBancario", f => f.Entero("IdLineaExtractoBancario")));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado) VALUES (?, ?, ?, ?, ?, ?)",
            lineas[0], _gimnasio, "CONTRAPARTIDA", 120.50m, "MANUAL", "DESCARTADA"));
        var asiento = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, new DateTime(2026, 11, 3), "Cobro ficticio", (byte)1, (byte)1, "pruebas", DateTime.Now, 0m, 0m, 1);
            return await s.UltimoIdAsync();
        });
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ContabilizacionesLineaExtracto (IdLineaExtractoBancario, IdAsiento, Estado, VersionProceso, FechaHora, Operador) VALUES (?, ?, ?, ?, ?, ?)",
            lineas[1], asiento, "CONTABILIZADA", 1, DateTime.Now, "pruebas"));
        await PalabraAsync("luz ficticia", "H", _luz);
        await PalabraAsync("cliente inventado", "D", _cliente);

        var r = await Asignador.AsignarAsync(_ctx);

        Assert.Equal((2, 1), (r.Revisadas, r.Asignadas));
        Assert.Equal(_luz, await ContarAsync("SELECT IdIdentidadContable FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ? AND Estado = 'CONFIRMADA'", lineas[0]));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ?", lineas[1]));
    }

    [Fact]
    public async Task Ambiguas_remesas_palabras_sin_cuenta_y_lotes_revertidos_no_reciben_cuenta()
    {
        await EmpresaAsync();
        await PalabraAsync("luz", "H", _luz);
        await PalabraAsync("fic", "H", _gimnasio);
        await PalabraAsync("cliente inventado", "D", null, especial: ReglasPalabrasClave.EspecialRemesa);
        await PalabraAsync("cafeteria", "H", null);

        var r = await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);

        Assert.Equal((3, 0, 1, 1, 1), (r.Asignacion!.Revisadas, r.Asignacion.Asignadas, r.Asignacion.Ambiguas, r.Asignacion.Remesas, r.Asignacion.PalabraSinCuenta));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario"));

        Assert.True((await Servicio.RevertirAsync(_ctx, r.IdLote!.Value, _actor, "Prueba")).EsCorrecto);
        await PalabraAsync("cafeteria", "H", _gimnasio, palabra2: "cafeteria");
        Assert.Equal(0, (await Asignador.AsignarAsync(_ctx)).Revisadas);
        // Al reactivar el lote revertido, sus líneas pasan otra vez por el motor.
        var otra = await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Reactivable, otra.Veredicto);
        Assert.Equal(1, otra.Asignacion!.Asignadas);
        Assert.Equal(_gimnasio, await ContarAsync("SELECT IdIdentidadContable FROM ImputacionesLineaExtractoBancario"));
    }

    [Fact]
    public async Task Simular_no_escribe_y_puede_evaluar_tambien_las_lineas_con_cuenta()
    {
        await EmpresaAsync();
        await PalabraAsync("luz ficticia", "H", _luz);
        await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1), "semana1.n43", null, _actor);
        await PalabraAsync("cafeteria", "H", _gimnasio);

        var sim = await Asignador.SimularAsync(_ctx);
        Assert.Equal((2, 1), (sim.Revisadas, sim.Asignadas));
        var todas = await Asignador.SimularAsync(_ctx, incluirAsignadas: true);
        Assert.Equal((3, 2), (todas.Revisadas, todas.Asignadas));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario"));
    }
}
