using System.Runtime.InteropServices;
using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Diccionario;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Nucleo.Auditoria;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

/// <summary>Catálogo de descripciones de tablas y campos (datos/esquema/descripciones-campos).</summary>
public class DescripcionesCamposTests
{
    [Fact]
    public void ElCatalogoSeLeeSinRepetidosNiTextosLargos()
    {
        var todas = CatalogoDescripciones.Todas;
        Assert.True(todas.Count > 1000, $"Solo hay {todas.Count} descripciones en el catálogo.");
        var repetidas = todas.GroupBy(d => $"{d.Tabla}.{d.Columna}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(repetidas.Count == 0, "Descripciones repetidas: " + string.Join(", ", repetidas));
        var largas = todas.Where(d => d.Texto.Length > 255).Select(d => $"{d.Tabla}.{d.Columna}").ToList();
        Assert.True(largas.Count == 0, "Access solo guarda 255 caracteres: " + string.Join(", ", largas));
        Assert.DoesNotContain(todas, d => d.Texto.Contains('Ã') || d.Texto.Contains('Â'));
    }

    [Fact]
    public void CadaTablaEstaEnElFicheroDeSuArea()
    {
        // Así el propietario revisa cada área en un solo fichero.
        var fuera = CatalogoDescripciones.Todas
            .Where(d => DuenosTablas.De(d.Tabla) is { } dueno && !string.Equals(dueno.Area, d.Area, StringComparison.OrdinalIgnoreCase))
            .Select(d => $"{d.Tabla} está en {d.Area}.txt y es de {DuenosTablas.De(d.Tabla)!.Area}").Distinct().ToList();
        Assert.True(fuera.Count == 0, string.Join("; ", fuera));
    }

    [Fact]
    public void LeeTablasCamposYDudosas()
    {
        var lista = CatalogoDescripciones.Leer("prueba", new StringReader("# comentario\n\nTabla: De la tabla.\n?Tabla.Campo: Dudosa: con dos puntos.\n"));
        Assert.Equal(2, lista.Count);
        Assert.Equal(new DescripcionCatalogo("prueba", "Tabla", null, "De la tabla.", false), lista[0]);
        Assert.Equal(new DescripcionCatalogo("prueba", "Tabla", "Campo", "Dudosa: con dos puntos.", true), lista[1]);
        Assert.Throws<FormatException>(() => CatalogoDescripciones.Leer("prueba", new StringReader("Tabla.Campo sin texto")));
    }
}

[Trait("Categoria", "Access")]
public sealed class DescripcionesCamposAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();

    public void Dispose() => _copia.Dispose();

    /// <summary>
    /// La BD de la versión del programa tiene descrita cada tabla y cada campo. Si falla, un campo nuevo ha llegado sin
    /// descripción: añádela a las Descripciones de su migración o, si es heredado, a Descripciones/&lt;área&gt;.txt.
    /// </summary>
    [Fact]
    public void TodasLasTablasYCamposTienenDescripcion()
    {
        var faltan = new List<string>();
        ConDao(_copia.Ruta, bd =>
        {
            foreach (dynamic t in bd.TableDefs)
            {
                string nombre = t.Name;
                if (nombre.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) || nombre.StartsWith('~') || !string.IsNullOrEmpty((string)t.Connect)) continue;
                if (string.IsNullOrWhiteSpace(EscritorDescripciones.Leer(t))) faltan.Add(nombre);
                foreach (dynamic f in t.Fields)
                    if (string.IsNullOrWhiteSpace(EscritorDescripciones.Leer(f))) faltan.Add($"{nombre}.{f.Name}");
            }
        });
        Assert.True(faltan.Count == 0,
            $"Hay {faltan.Count} tablas o campos sin descripción. Añádela a las Descripciones de su migración o a " +
            "src/ProConta.Datos.Esquema/Descripciones/<área>.txt: " + string.Join(", ", faltan));
    }

    [Fact]
    public async Task ElComandoDescripcionesRellenaLasQueFaltanConCopiaPrevia()
    {
        ConDao(_copia.Ruta, bd => bd.TableDefs["Asientos"].Fields["Notas"].Properties.Delete("Description"));
        var carpeta = BdPruebas.CarpetaTemporal();

        var resultado = await new ActualizadorEsquema(Actor.Proceso("pruebas")).CompletarDescripcionesAsync(_copia.Ruta, carpeta);

        Assert.True(resultado.EsCorrecto, resultado.Error?.ToString());
        Assert.Equal(1, resultado.Valor.Escritas);
        Assert.True(File.Exists(resultado.Valor.CopiaSeguridad));
        string? notas = null;
        ConDao(_copia.Ruta, bd => notas = EscritorDescripciones.Leer(bd.TableDefs["Asientos"].Fields["Notas"]));
        Assert.Equal(CatalogoDescripciones.Todas.Single(d => d is { Tabla: "Asientos", Columna: "Notas" }).Texto, notas);

        var otra = await new ActualizadorEsquema(Actor.Proceso("pruebas")).CompletarDescripcionesAsync(_copia.Ruta, carpeta);
        Assert.True(otra.EsCorrecto);
        Assert.Equal((0, (string?)null), otra.Valor);
    }

    private static void ConDao(string ruta, Action<dynamic> accion)
    {
        var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120")!;
        dynamic motor = Activator.CreateInstance(tipo)!;
        dynamic bd = motor.OpenDatabase(ruta, false, false);
        try { accion(bd); }
        finally
        {
            bd.Close();
            Marshal.FinalReleaseComObject(bd);
            Marshal.FinalReleaseComObject(motor);
        }
    }
}
