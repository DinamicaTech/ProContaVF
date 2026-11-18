using ProConta.Plataforma.Configuracion;

namespace ProConta.Plataforma.Configuracion.Tests;

/// <summary>Registro de errores inesperados (plataforma/registro-errores): qué se guarda, dónde y cuánto tiempo.</summary>
public sealed class RegistroErroresTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "registro-errores-" + Guid.NewGuid().ToString("N"));
    private DateTime _ahora = new(2026, 11, 18, 18, 40, 12);

    private RegistroErrores Nuevo(ContextoError? c = null) =>
        new(() => c ?? new ContextoError("ProContaVF", "ProContaVF-main.100+abc", "Producción", @"C:\Datos\ProConta.accdb", "Ronald",
            "01 · SOCIEDAD DE PRUEBA SL · ejercicio 2026", "Diario (3 pestañas abiertas)"), _carpeta, () => _ahora);

    private static Exception Lanzada()
    {
        try { throw new InvalidOperationException("Fallo de prueba", new FormatException("Formato raro")); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void Guarda_fecha_version_sociedad_pantalla_y_detalle_en_el_fichero_del_mes()
    {
        var ruta = Nuevo().Registrar(Lanzada(), "Pantalla (hilo de la interfaz)");

        Assert.Equal(Path.Combine(_carpeta, "errores-2026-11.log"), ruta);
        var texto = File.ReadAllText(ruta!);
        Assert.Contains("==== 2026-11-18 18:40:12 · ProContaVF · error inesperado", texto);
        Assert.Contains("Versión: ProContaVF-main.100+abc", texto);
        Assert.Contains("Entorno: Producción", texto);
        Assert.Contains(@"BD: C:\Datos\ProConta.accdb", texto);
        Assert.Contains("Operador: Ronald", texto);
        Assert.Contains("Sociedad: 01 · SOCIEDAD DE PRUEBA SL · ejercicio 2026", texto);
        Assert.Contains("Pantalla: Diario (3 pestañas abiertas)", texto);
        Assert.Contains("Dónde: Pantalla (hilo de la interfaz)", texto);
        Assert.Contains("Error: InvalidOperationException: Fallo de prueba", texto);
        // El detalle técnico completo: la pila y el error interno.
        Assert.Contains("Detalle técnico:", texto);
        Assert.Contains("FormatException: Formato raro", texto);
        Assert.Contains(nameof(Lanzada), texto);
    }

    [Fact]
    public void Los_errores_se_acumulan_y_cada_mes_va_a_su_fichero()
    {
        var registro = Nuevo();
        registro.Registrar(Lanzada(), "uno");
        registro.Registrar(Lanzada(), "dos");
        _ahora = new DateTime(2026, 12, 1, 9, 0, 0);
        var diciembre = registro.Registrar(Lanzada(), "tres");

        var noviembre = File.ReadAllText(Path.Combine(_carpeta, "errores-2026-11.log"));
        Assert.Equal(2, noviembre.Split("==== ").Length - 1);
        Assert.EndsWith("errores-2026-12.log", diciembre);
        Assert.Contains("Dónde: tres", File.ReadAllText(diciembre!));
    }

    [Fact]
    public void Lo_que_no_se_conoce_no_se_escribe_y_el_ajuste_cambia_la_sociedad()
    {
        var registro = Nuevo(new ContextoError("Conector de Claude Desktop", "v1"));
        var ruta = registro.Registrar(Lanzada(), "Herramienta mayor_cuenta", c => c with { Sociedad = "01 · ejercicio 2026" });

        var texto = File.ReadAllText(ruta!);
        Assert.Contains("· Conector de Claude Desktop · error inesperado", texto);
        Assert.Contains("Sociedad: 01 · ejercicio 2026", texto);
        Assert.DoesNotContain("Pantalla:", texto);
        Assert.DoesNotContain("Operador:", texto);
    }

    [Fact]
    public void Si_no_puede_escribir_devuelve_null_sin_lanzar()
    {
        // Un fichero en el sitio de la carpeta: no se puede crear la carpeta ni escribir en ella.
        Directory.CreateDirectory(Path.GetDirectoryName(_carpeta)!);
        File.WriteAllText(_carpeta, "ocupa el sitio de la carpeta");
        try
        {
            Assert.Null(Nuevo().Registrar(Lanzada(), "x"));
        }
        finally
        {
            File.Delete(_carpeta);
        }
    }

    [Fact]
    public void Si_el_contexto_falla_el_error_se_guarda_igualmente()
    {
        var registro = new RegistroErrores(() => throw new NullReferenceException(), _carpeta, () => _ahora);
        var ruta = registro.Registrar(Lanzada(), "x");
        Assert.Contains("no se pudo leer el contexto", File.ReadAllText(ruta!));
    }

    [Fact]
    public void Purgar_borra_los_de_mas_de_doce_meses_y_deja_el_resto()
    {
        Directory.CreateDirectory(_carpeta);
        foreach (var n in new[] { "errores-2025-11.log", "errores-2025-12.log", "errores-2026-11.log", "otro.log", "errores-raro.log" })
            File.WriteAllText(Path.Combine(_carpeta, n), "x");

        var borrados = Nuevo().Purgar();

        Assert.Equal(1, borrados);
        var quedan = Directory.GetFiles(_carpeta).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["errores-2025-12.log", "errores-2026-11.log", "errores-raro.log", "otro.log"], quedan);
    }

    [Fact]
    public void Purgar_sin_carpeta_no_hace_nada()
    {
        Assert.Equal(0, Nuevo().Purgar());
        Assert.False(Directory.Exists(_carpeta));
    }

    [Fact]
    public void Leer_devuelve_las_entradas_de_todos_los_meses_de_la_mas_reciente_a_la_mas_antigua()
    {
        var registro = Nuevo();
        _ahora = new DateTime(2026, 10, 3, 8, 0, 0);
        registro.Registrar(Lanzada(), "octubre");
        _ahora = new DateTime(2026, 11, 18, 18, 40, 12);
        registro.Registrar(Lanzada(), "noviembre 1");
        _ahora = new DateTime(2026, 11, 18, 19, 0, 0);
        var ruta = registro.Registrar(Lanzada(), "noviembre 2");
        File.AppendAllText(Path.Combine(_carpeta, "otro.log"), "==== 2026-11-30 00:00:00 · No es del registro");

        var entradas = registro.Leer(out var ilegibles);

        Assert.Equal(0, ilegibles);
        Assert.Equal(["noviembre 2", "noviembre 1", "octubre"], entradas.Select(e => e.Donde));
        var e = entradas[0];
        Assert.Equal(new DateTime(2026, 11, 18, 19, 0, 0), e.Fecha);
        Assert.Equal("ProContaVF", e.Programa);
        Assert.Equal("ProContaVF-main.100+abc", e.Version);
        Assert.Equal("01 · SOCIEDAD DE PRUEBA SL · ejercicio 2026", e.Sociedad);
        Assert.Equal("Diario (3 pestañas abiertas)", e.Pantalla);
        Assert.Equal("InvalidOperationException: Fallo de prueba", e.Error);
        Assert.Equal(ruta, e.Fichero);
        Assert.StartsWith("==== 2026-11-18 19:00:00", e.Texto);
        Assert.Contains("FormatException: Formato raro", e.Texto);
        Assert.DoesNotContain("noviembre 1", e.Texto);
    }

    [Fact]
    public void Leer_sin_carpeta_no_devuelve_nada()
    {
        Assert.Empty(Nuevo().Leer(out var ilegibles));
        Assert.Equal(0, ilegibles);
    }

    [Fact]
    public void Interpretar_ignora_texto_suelto_y_cabeceras_sin_fecha()
    {
        var texto = "basura inicial\n==== no es fecha · X\nError: a\n==== 2026-11-01 10:00:00 · Conector de Claude Desktop · error inesperado\nError: b\nDetalle técnico:\nError: no cuenta\n";
        var e = Assert.Single(RegistroErrores.Interpretar(texto, "f"));
        Assert.Equal("Conector de Claude Desktop", e.Programa);
        Assert.Equal("b", e.Error);
        Assert.Null(e.Sociedad);
    }

    [Fact]
    public void El_resumen_es_el_error_que_lo_causo()
    {
        var envuelto = new AggregateException(new System.Reflection.TargetInvocationException(new InvalidCastException("No es un número")));
        Assert.Equal("InvalidCastException: No es un número", RegistroErrores.Resumen(envuelto));
    }

    [Fact]
    public void La_carpeta_por_defecto_esta_junto_a_la_configuracion_local()
    {
        Assert.EndsWith(Path.Combine("ProContaVF", "errores"), RegistroErrores.CarpetaPorDefecto);
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }
}
