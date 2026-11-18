using ProConta.Conector;
using ProConta.Datos.Persistencia;
using ProConta.Plataforma.Configuracion;

namespace ProConta.Conector.Tests;

/// <summary>
/// Errores inesperados de las herramientas del conector (plataforma/registro-errores): se guardan en el registro con la sociedad
/// que pidió la herramienta y Claude recibe qué ha fallado y dónde está el fichero. Sin BD: una BD falsa que siempre falla.
/// </summary>
public sealed class ErroresInesperadosConectorTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "registro-conector-" + Guid.NewGuid().ToString("N"));

    private sealed class BdQueFalla : IBaseDatos
    {
        public string Ruta => @"C:\Datos\falsa.accdb";
        public Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) =>
            throw new InvalidCastException("La columna Saldo no es un número");
        public Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) =>
            throw new InvalidCastException("La columna Saldo no es un número");
    }

    private (HerramientasConector Herramientas, RegistroErrores Registro) Crear(bool provocar = false)
    {
        var registro = new RegistroErrores(() => new ContextoError("Conector de Claude Desktop", "v-prueba", "Pruebas", @"C:\Datos\falsa.accdb", "Ronald"), _carpeta);
        var servicio = new ServicioConector(new BdQueFalla(), "Ronald") { Registro = registro, ProvocarError = provocar };
        return (new HerramientasConector(servicio), registro);
    }

    [Fact]
    public async Task Un_fallo_inesperado_queda_en_el_registro_con_la_sociedad_y_Claude_recibe_la_ruta()
    {
        var (h, registro) = Crear();

        var respuesta = await h.MayorCuenta("01", 2026, "4300001");

        var ruta = registro.RutaActual;
        Assert.StartsWith("No se ha hecho nada: la herramienta mayor_cuenta ha fallado", respuesta);
        Assert.Contains("InvalidCastException: La columna Saldo no es un número", respuesta);
        Assert.Contains(ruta, respuesta);
        var texto = File.ReadAllText(ruta);
        Assert.Contains("Conector de Claude Desktop · error inesperado", texto);
        Assert.Contains("Versión: v-prueba", texto);
        Assert.Contains("Sociedad: 01 · ejercicio 2026", texto);
        Assert.Contains("Dónde: Herramienta mayor_cuenta (cuenta 4300001)", texto);
        Assert.Contains("Detalle técnico:", texto);
    }

    [Fact]
    public async Task Provocar_error_hace_fallar_la_herramienta_sociedades()
    {
        var (h, registro) = Crear(provocar: true);

        var respuesta = await h.Sociedades();

        Assert.Contains("--provocar-error", respuesta);
        Assert.Contains(registro.RutaActual, respuesta);
        Assert.Contains("Dónde: Herramienta sociedades", File.ReadAllText(registro.RutaActual));
    }

    [Fact]
    public void Si_no_se_pudo_guardar_lo_dice_con_la_ruta_prevista()
    {
        var texto = HerramientasConector.TextoErrorInesperado("ver_asiento", new InvalidOperationException("x"), null, @"C:\errores\errores-2026-11.log");
        Assert.Contains(@"No se ha podido guardar el detalle en el registro de errores (C:\errores\errores-2026-11.log)", texto);
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }
}
