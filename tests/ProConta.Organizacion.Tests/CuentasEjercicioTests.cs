using ProConta.Mantenimiento;
using ProConta.Organizacion.Mantenimiento;

namespace ProConta.Organizacion.Tests;

/// <summary>
/// Botón «Auto» y cuentas del ejercicio (plataforma/mantenimiento y organizacion/sociedades-ejercicios 2026-11-18): la habitual se propone y
/// se crea si falta; sin habitual, solo la única que encaja; nunca se cambia una cuenta puesta.
/// </summary>
public class CuentasEjercicioTests
{
    private static readonly IReadOnlyList<CampoMantenimiento> Campos = [.. CuentasEjercicio.Todas.Select(CuentasEjercicio.Campo)];
    private static CampoMantenimiento C(string clave) => Campos.Single(c => c.Clave == clave);

    private static readonly IReadOnlyList<CuentaPlan> Plan =
    [
        new("4750000", "Hacienda Pública, acreedora por IVA"),
        new("4751000", "H.P. acreedora por retenciones practicadas"),
        new("4751001", "H.P. acreedora por retenciones de alquileres"),
        new("4751002", "H.P. acreedora por retenciones profesionales"),
        new("4751003", "H.P. acreedora por retenciones de servicios de agentes"),
        new("4720121", "H.P. IVA soportado intracomunitario bienes"),
        new("4720221", "H.P. IVA soportado intracomunitario servicios"),
        new("4724010", "H.P. IVA soportado importaciones 10 %"),
        new("4724021", "H.P. IVA soportado importaciones 21 %"),
        new("5550002", "Suplidos con IVA"),
        new("5550000", "Partidas pendientes de aplicación"),
    ];

    private static RegistroMantenimiento Vacio(params (string Clave, string Valor)[] puestos)
    {
        var r = new RegistroMantenimiento(1, Campos.ToDictionary(c => c.Clave, _ => (object?)null));
        foreach (var (k, v) in puestos) r[k] = v;
        return r;
    }

    [Fact]
    public void LaHabitualSeAsignaYSeCreaSiNoExiste()
    {
        var p = AsignacionAutomatica.Proponer([C("CuentaHaciendaAcreedoraIva"), C("CuentaHaciendaDeudoraIva")], Vacio(), Plan);
        var existente = p.Asignadas.Single(a => a.Campo.Clave == "CuentaHaciendaAcreedoraIva");
        Assert.Equal(("4750000", false, "Hacienda Pública, acreedora por IVA"), (existente.Codigo, existente.Crear, existente.Nombre));
        var nueva = p.Asignadas.Single(a => a.Campo.Clave == "CuentaHaciendaDeudoraIva");
        Assert.Equal(("4700000", true, "Hacienda Pública, deudora por IVA"), (nueva.Codigo, nueva.Crear, nueva.Nombre));
        Assert.Empty(p.SinCandidato);
    }

    [Fact]
    public void SinHabitualSoloAsignaLaUnicaQueEncaja()
    {
        var p = AsignacionAutomatica.Proponer(
            [C("CuentaSuplido"), C("CuentaRetencionAlquileres"), C("CuentaRetencionServicios"), C("CuentaIVAImportacion"), C("CuentaIVAIntracomunitarioSoportadoBienes"),
             C("CuentaIVAInversionSujetoPasivoSoportado")],
            Vacio(), Plan);
        Assert.Equal("5550002", p.Asignadas.Single(a => a.Campo.Clave == "CuentaSuplido").Codigo);
        Assert.Equal("4751001", p.Asignadas.Single(a => a.Campo.Clave == "CuentaRetencionAlquileres").Codigo);
        Assert.Equal("4720121", p.Asignadas.Single(a => a.Campo.Clave == "CuentaIVAIntracomunitarioSoportadoBienes").Codigo);
        Assert.All(p.Asignadas, a => Assert.False(a.Crear));
        // Dos candidatas (profesionales y servicios de agentes; importaciones al 10 y al 21 %): se quedan vacías con el motivo.
        Assert.Contains("4751002, 4751003", p.SinCandidato.Single(d => d.Campo.Clave == "CuentaRetencionServicios").Motivo);
        Assert.Contains("4724010, 4724021", p.SinCandidato.Single(d => d.Campo.Clave == "CuentaIVAImportacion").Motivo);
        // Ninguna: también vacía.
        Assert.Contains("ninguna", p.SinCandidato.Single(d => d.Campo.Clave == "CuentaIVAInversionSujetoPasivoSoportado").Motivo);
    }

    [Fact]
    public void NuncaCambiaUnaCuentaPuesta()
    {
        var p = AsignacionAutomatica.Proponer(Campos, Vacio(("CuentaHaciendaAcreedoraIva", "4750001"), ("CuentaSuplido", "5550000")), Plan);
        Assert.DoesNotContain(p.Asignadas, a => a.Campo.Clave is "CuentaHaciendaAcreedoraIva" or "CuentaSuplido");
        Assert.DoesNotContain(p.SinCandidato, a => a.Campo.Clave is "CuentaHaciendaAcreedoraIva" or "CuentaSuplido");
    }

    [Fact]
    public void ConTodasPuestasNoHayNadaQueHacer() =>
        Assert.True(AsignacionAutomatica.Proponer(Campos, Vacio([.. Campos.Select(c => (c.Clave, "1000000"))]), Plan).NadaQueHacer);

    [Fact]
    public void LasPalabrasNoDistinguenTildesNiMayusculas()
    {
        var campo = C("CuentaIVAImportacion");
        Assert.True(AsignacionAutomatica.Encaja(campo, "H.P. IVA SOPORTADO IMPORTACIÓN"));
        Assert.False(AsignacionAutomatica.Encaja(campo, "H.P. IVA soportado 21 %"));
    }

    [Fact]
    public void CadaCuentaTieneSuPestanaSuExplicacionYSuReglaDeAuto()
    {
        var pestanas = CuentasEjercicio.Pestanas.Select(p => p.Clave).ToHashSet();
        Assert.Equal(["ident", "compras", "iva", "is", "fin"], CuentasEjercicio.Pestanas.Select(p => p.Clave));
        Assert.Equal(CuentasEjercicio.Todas.Count, CuentasEjercicio.Todas.Select(c => c.Columna).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var c in Campos)
        {
            Assert.Contains(c.Grupo!, pestanas);
            Assert.False(string.IsNullOrWhiteSpace(c.Subgrupo), c.Clave);
            Assert.True(c.TieneAsignacionAutomatica, c.Clave);
            Assert.NotEmpty(c.GruposCuenta);
            Assert.Equal(5, c.Explicacion.Count);
            Assert.All(c.Explicacion, a => Assert.False(string.IsNullOrWhiteSpace(a.Texto), $"{c.Clave} · {a.Titulo}"));
            if (c.CuentaHabitual is string h)
            {
                Assert.Equal(7, h.Length);
                Assert.True(AsignacionAutomatica.EnGrupos(c, h), $"{c.Clave}: la habitual {h} está fuera de sus grupos");
                Assert.False(string.IsNullOrWhiteSpace(c.NombreCuentaHabitual));
            }
        }
    }

    [Fact]
    public void LaFichaDeEjerciciosVaEnPestanasYSinCuentaPorDefectoImplicita()
    {
        var ejercicios = ProveedoresOrganizacion.Ejercicios(null!);
        Assert.Equal(CuentasEjercicio.Pestanas, ejercicios.Grupos);
        Assert.All(ejercicios.Campos, c => Assert.Contains(ejercicios.Grupos, g => g.Clave == c.Grupo));
        // Ninguna ayuda promete ya una cuenta «Vacía: …» (propietario 2026-11-18 05:10: siempre materializadas).
        Assert.All(ejercicios.Campos.Where(c => c.Tipo == TipoCampo.Cuenta), c => Assert.DoesNotContain("Vacía:", c.Ayuda ?? ""));
        Assert.All(CuentasEjercicio.Todas, c => Assert.Contains(ejercicios.Campos, x => x.Clave == c.Columna));
    }

    [Fact]
    public void ElAvisoDeCuentaQueFaltaDiceDondeAsignarla()
    {
        var e = CuentasEjercicio.Faltan("2026", ["CuentaHaciendaAcreedoraIva", "CuentaResultadoEjercicio"]);
        Assert.Contains("«A ingresar» (IVA)", e.Mensaje);
        Assert.Contains("«Cuenta de resultados» (Fin de ejercicio)", e.Mensaje);
        Assert.Contains("Parámetros y tablas › Ejercicios", e.AccionCorrectora);
        Assert.Contains("«Auto»", e.AccionCorrectora);
    }
}
