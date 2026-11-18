using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

public class ReglasCuentaTests
{
    private static CuentaContable Cuenta(bool activa = true, bool bloqueada = false, bool imputable = true) =>
        new(1, 1, 1, null, "5720001", "Banco sintético", activa, bloqueada, imputable, null, 0m, 0m, 0);

    [Fact]
    public void SoloLasCuentasActivasNoBloqueadasEImputablesAdmitenApuntes()
    {
        Assert.True(ReglasCuenta.AdmiteApuntes(Cuenta()).EsCorrecto);
        Assert.Contains("inactiva", ReglasCuenta.AdmiteApuntes(Cuenta(activa: false)).Error!.Causa);
        Assert.Contains("bloqueada", ReglasCuenta.AdmiteApuntes(Cuenta(bloqueada: true)).Error!.Causa);
        Assert.Contains("subcuenta", ReglasCuenta.AdmiteApuntes(Cuenta(imputable: false)).Error!.AccionCorrectora);
    }
}

[Trait("Categoria", "Access")]
public sealed class CuentasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _c2025 = new("T01", 9001, 2025);
    private readonly ContextoSociedadEjercicio _c2026 = new("T01", 9002, 2026);

    public void Dispose() => _copia.Dispose();

    private async Task PrepararAsync()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await DatosSinteticos.SociedadAsync(bd, "T02", "Otra sociedad sintética", "Q1234567D");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(bd, 9002, "T01", 2026);
    }

    [Fact]
    public async Task CodigoIgualEnEjerciciosDistintosNoMezclaCuentas()
    {
        await PrepararAsync();
        var catalogo = new CatalogoCuentasAccess(_copia.Bd());
        var padre = await catalogo.AltaAsync(_c2025, new AltaCuenta("572", "Bancos", EsImputable: false));
        Assert.True(padre.EsCorrecto, padre.Error?.ToString());
        var banco = await catalogo.AltaAsync(_c2025, new AltaCuenta("5720001", "Banco sintético", padre.Valor.IdCuentaContable));
        Assert.True(banco.EsCorrecto, banco.Error?.ToString());

        Assert.Equal("Cuentas.Repetida", (await catalogo.AltaAsync(_c2025, new AltaCuenta("5720001", "Otra"))).Error!.Codigo);
        // El código ya está reservado por otra identidad de la sociedad: en 2026 no se reutiliza para una cuenta nueva.
        Assert.Equal("Cuentas.CodigoReservado", (await catalogo.AltaAsync(_c2026, new AltaCuenta("5720001", "Otra cosa"))).Error!.Codigo);
        Assert.Equal("Cuentas.PadreNoValido", (await catalogo.AltaAsync(_c2026, new AltaCuenta("5720002", "Banco 2", padre.Valor.IdCuentaContable))).Error!.Codigo);

        Assert.Equal(banco.Valor.IdCuentaContable, (await catalogo.ResolverAsync(_c2025, " 5720001 ")).Valor.IdCuentaContable);
        Assert.Equal("Cuentas.NoExiste", (await catalogo.ResolverAsync(_c2026, "5720001")).Error!.Codigo);
        Assert.Equal(["572", "5720001"], (await catalogo.ListarAsync(_c2025, "572")).Select(c => c.Codigo));
        Assert.Equal("Cuentas.CodigoNoValido", (await catalogo.AltaAsync(_c2025, new AltaCuenta("57A", "x"))).Error!.Codigo);
    }

    [Fact]
    public async Task AltaOTraerTraeLaCuentaDeOtroEjercicioSiSuCodigoEstaReservado()
    {
        // «Auto» de Ejercicios (ensayo del 2026-11-18): la 4009000 de 2025 no se podía dar de alta otra vez en 2026.
        await PrepararAsync();
        var catalogo = new CatalogoCuentasAccess(_copia.Bd());
        var proveedores = await catalogo.AltaAsync(_c2025, new AltaCuenta("400", "Proveedores", EsImputable: false));
        Assert.True((await catalogo.AltaEnEjercicioAsync(_c2026, proveedores.Valor.IdIdentidadContable)).EsCorrecto);
        var en2025 = await catalogo.AltaSubcuentaAsync(_c2025, "4009000", "Proveedores, facturas pendientes de recibir");
        Assert.Equal("Cuentas.CodigoReservado", (await catalogo.AltaSubcuentaAsync(_c2026, "4009000", "Otro nombre")).Error!.Codigo);
        var traida = await catalogo.AltaOTraerSubcuentaAsync(_c2026, "4009000", "Otro nombre");
        Assert.True(traida.EsCorrecto, traida.Error?.ToString());
        Assert.Equal((en2025.Valor.IdIdentidadContable, "Proveedores, facturas pendientes de recibir"), (traida.Valor.IdIdentidadContable, traida.Valor.Nombre));
        Assert.Equal((await catalogo.ResolverAsync(_c2026, "400")).Valor.IdCuentaContable, (await catalogo.ResolverAsync(_c2026, "4009000")).Valor.IdCuentaPadre);
        // Un código libre se da de alta con el nombre propuesto.
        Assert.Equal("Nueva", (await catalogo.AltaOTraerSubcuentaAsync(_c2026, "4009001", "Nueva")).Valor.Nombre);
    }

    [Fact]
    public async Task LaSubcuentaGuardaSuDescripcionYElSiguienteCodigoSaltaLosReservados()
    {
        await PrepararAsync();
        var catalogo = new CatalogoCuentasAccess(_copia.Bd());
        await catalogo.AltaAsync(_c2025, new AltaCuenta("629", "Otros servicios", EsImputable: false, Descripcion: "Gastos diversos"));
        var agua = await catalogo.AltaAsync(_c2025, new AltaCuenta("6290001", "Suministros: agua", Descripcion: "  Agua de la oficina  "));
        Assert.True(agua.EsCorrecto, agua.Error?.ToString());
        Assert.Equal("Agua de la oficina", (await catalogo.ResolverAsync(_c2025, "6290001")).Valor.Descripcion);
        Assert.Null((await catalogo.AltaAsync(_c2025, new AltaCuenta("6290002", "Sin descripción", Descripcion: " "))).Valor.Descripcion);
        Assert.Equal("Cuentas.DescripcionNoValida", (await catalogo.AltaAsync(_c2025, new AltaCuenta("6290003", "Larga", Descripcion: new string('x', 256)))).Error!.Codigo);
        Assert.Equal("6290003", await catalogo.SiguienteSubcuentaAsync(_c2025, "629"));
        // En 2026 los códigos de 2025 siguen reservados.
        Assert.Equal("6290003", await catalogo.SiguienteSubcuentaAsync(_c2026, "629"));
        // La copia a otro ejercicio conserva la descripción.
        Assert.Equal("Agua de la oficina", (await catalogo.AltaEnEjercicioAsync(_c2026, agua.Valor.IdIdentidadContable)).Valor.Descripcion);
    }

    [Fact]
    public async Task RecalcularSaldos_muestra_la_diferencia_y_deja_la_cache_igual_a_los_apuntes()
    {
        await PrepararAsync();
        var bd = _copia.Bd();
        var catalogo = new CatalogoCuentasAccess(bd);
        var banco = (await catalogo.AltaAsync(_c2025, new AltaCuenta("5720001", "Banco sintético"))).Valor;
        await catalogo.AltaAsync(_c2025, new AltaCuenta("7000001", "Ventas sintéticas"));
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE CuentasContables SET SaldoInicial = ?, SaldoActual = ?, NumeroApuntes = ? WHERE IdCuentaContable = ?", 100m, 999m, 99, banco.IdCuentaContable);
            await s.EjecutarAsync(
                "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, new DateTime(2025, 3, 1), "Asiento sintético", (byte)1, (byte)1, "prueba", DateTime.Now, 150m, 150m, 1);
            var asiento = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                asiento, (short)1, "5720001", 150m, null, (byte)0, false);
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                asiento, (short)2, "7000001", null, 150m, (byte)0, false);
            return true;
        });

        // contabilidad/procesos/recalcular-saldos: la caché de la 5720001 está descuadrada a mano (999 con 99 apuntes); la 7000001 no tiene
        // caché; la 6290099 tiene apuntes pero no está en el plan del ejercicio.
        await bd.EjecutarAsync(async s =>
        {
            var asiento = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdAsiento) FROM Asientos"), System.Globalization.CultureInfo.InvariantCulture);
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                asiento, (short)3, "6290099", 10m, null, (byte)0, false);
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                asiento, (short)4, "5720001", null, 10m, (byte)0, false);
            return true;
        });
        var servicio = new ProConta.Contabilidad.Saldos.ServicioRecalcularSaldos(bd, new ProConta.Contabilidad.Asientos.MotorAsientosAccess(bd));
        var antes = await servicio.CompararAsync(_c2025);
        Assert.Equal(["5720001", "7000001"], antes.Diferencias.Select(d => d.Codigo));
        var banco2025 = antes.Diferencias[0];
        Assert.Equal((999m, 240m, 99, 2, 759m), (banco2025.SaldoGuardado, banco2025.SaldoApuntes, banco2025.ApuntesGuardados, banco2025.ApuntesReales, banco2025.Diferencia));
        Assert.Equal(["6290099"], antes.SinAlta.Select(c => c.Codigo));
        Assert.Equal(10m, antes.SinAlta[0].Saldo);
        // Comparar no graba nada.
        Assert.Equal(999m, (await catalogo.ResolverAsync(_c2025, "5720001")).Valor.SaldoActual);

        var despues = await servicio.RecalcularAsync(_c2025);
        Assert.True(despues.Cuadra);
        Assert.Equal(["6290099"], despues.SinAlta.Select(c => c.Codigo));
        var reconstruida = (await catalogo.ResolverAsync(_c2025, "5720001")).Valor;
        Assert.Equal(240m, reconstruida.SaldoActual);
        Assert.Equal(2, reconstruida.NumeroApuntes);
        Assert.Equal(-150m, (await catalogo.ResolverAsync(_c2025, "7000001")).Valor.SaldoActual);
    }

    [Fact]
    public async Task CentrosDeCosteJerarquicosPorSociedad()
    {
        await PrepararAsync();
        var catalogo = new CatalogoCentrosCosteAccess(_copia.Bd());
        var raiz = await catalogo.AltaAsync("T01", "EMP", "Empresa", esImputable: false);
        Assert.True(raiz.EsCorrecto, raiz.Error?.ToString());
        var hijo = await catalogo.AltaAsync("T01", "GEN", "Gastos generales", raiz.Valor.IdCentroCoste);
        Assert.True(hijo.EsCorrecto, hijo.Error?.ToString());
        Assert.Equal("EMP/GEN", hijo.Valor.RutaJerarquica);
        Assert.Equal(2, hijo.Valor.Nivel);

        Assert.Equal("CentrosCoste.Repetido", (await catalogo.AltaAsync("T01", "GEN", "Repetido", raiz.Valor.IdCentroCoste)).Error!.Codigo);
        Assert.Equal("CentrosCoste.PadreNoValido", (await catalogo.AltaAsync("T02", "X", "Otro", raiz.Valor.IdCentroCoste)).Error!.Codigo);
        Assert.Equal(["EMP", "EMP/GEN"], (await catalogo.ListarAsync("T01")).Select(c => c.RutaJerarquica));

        Assert.True((await catalogo.ComprobarImputableAsync("T01", hijo.Valor.IdCentroCoste)).EsCorrecto);
        Assert.Contains("no es imputable", (await catalogo.ComprobarImputableAsync("T01", raiz.Valor.IdCentroCoste)).Error!.Causa);
        Assert.Contains("no existe", (await catalogo.ComprobarImputableAsync("T02", hijo.Valor.IdCentroCoste)).Error!.Causa);
    }
}
