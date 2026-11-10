using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Texto clave de compras de una empresa (tabla TextosClaveCompra, compras/pantallas 2026-11-06 17:02): si aparece en el
/// contenido o en la ruta del PDF, la factura toma su cuenta de gasto, su centro de coste o los dos. La cuenta se guarda
/// por su identidad contable y se muestra con el código del ejercicio de trabajo; <c>CuentaEnEjercicio</c> = false si no
/// existe en él (no se asigna).
/// </summary>
public sealed record TextoClaveCompra(int IdTextoClave, string Texto, int? IdIdentidadContable, string? Cuenta, string? NombreCuenta, bool CuentaEnEjercicio,
    int? IdCentroCoste, string? NombreCentro);

/// <summary>
/// Lo que los textos clave asignan a una factura y los avisos de empate. Con empate no se asigna esa parte ni se toma la
/// del proveedor: la decide el operador.
/// </summary>
public sealed record AsignacionTextosClave(string? Cuenta, int? IdCentroCoste, bool EmpateCuenta, bool EmpateCentro, IReadOnlyList<string> Avisos);

/// <summary>
/// Reglas de los textos clave (compras/pantallas 2026-11-06 17:02). Se buscan sin distinguir mayúsculas ni acentos en el
/// contenido del PDF y en su ruta completa. Si coinciden varios, gana el más largo; si empatan en longitud y asignan cosas
/// distintas, no se asigna nada y se avisa. La cuenta y el centro se eligen por separado: un texto solo cuenta para lo
/// que asigna.
/// </summary>
public static class ReglasTextosClave
{
    public const int LongitudMaxima = 100;

    public static AsignacionTextosClave Elegir(IEnumerable<TextoClaveCompra> textos, string? contenido, string? ruta)
    {
        var enContenido = Plano(contenido);
        var enRuta = Plano(ruta);
        var coinciden = textos.Where(t => Plano(t.Texto) is { Length: > 0 } p && (enContenido.Contains(p, StringComparison.Ordinal) || enRuta.Contains(p, StringComparison.Ordinal)))
            .ToList();
        var avisos = new List<string>();
        var (cuenta, empateCuenta) = Ganador(coinciden.Where(t => t.CuentaEnEjercicio && t.Cuenta is not null), t => t.Cuenta, "la cuenta de gasto", avisos);
        var (centro, empateCentro) = Ganador(coinciden.Where(t => t.IdCentroCoste is not null), t => t.IdCentroCoste, "el centro de coste", avisos);
        return new AsignacionTextosClave(cuenta, centro, empateCuenta, empateCentro, avisos);
    }

    private static (T? Valor, bool Empate) Ganador<T>(IEnumerable<TextoClaveCompra> candidatos, Func<TextoClaveCompra, T?> valor, string que, List<string> avisos)
    {
        var lista = candidatos.ToList();
        if (lista.Count == 0) return (default, false);
        var largo = lista.Max(t => Plano(t.Texto).Length);
        var primeros = lista.Where(t => Plano(t.Texto).Length == largo).ToList();
        if (primeros.Select(valor).Distinct().Count() == 1) return (valor(primeros[0]), false);
        avisos.Add($"Los textos clave {string.Join(" y ", primeros.Select(t => $"«{t.Texto}»"))} asignan {que} distinto y son igual de largos: no he asignado {que}.");
        return (default, true);
    }

    /// <summary>Minúsculas, sin acentos y con los espacios seguidos reducidos a uno.</summary>
    public static string Plano(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        var espacio = false;
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { espacio = true; continue; }
            if (espacio && sb.Length > 0) sb.Append(' ');
            espacio = false;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}

/// <summary>Textos clave de compras por empresa. Es la única vía de escritura de TextosClaveCompra.</summary>
public sealed class CatalogoTextosClaveCompra(IBaseDatos bd)
{
    public Task<IReadOnlyList<TextoClaveCompra>> ListarAsync(ContextoSociedadEjercicio trabajo) =>
        bd.EjecutarAsync(s => ListarAsync(s, trabajo));

    /// <summary>Textos de la empresa con la cuenta en el ejercicio de trabajo (o la última que tuvo) y el nombre del centro.</summary>
    internal static async Task<IReadOnlyList<TextoClaveCompra>> ListarAsync(ISesionDatos s, ContextoSociedadEjercicio trabajo)
    {
        // Sin parámetros en un ON (fallo de Access): se cruza en memoria.
        var filas = await s.ConsultarAsync("SELECT IdTextoClave, Texto, IdIdentidadContable, IdCentroCoste FROM TextosClaveCompra WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdTextoClave"), Texto: f.Texto("Texto")?.Trim() ?? "", Identidad: f.EnteroNulo("IdIdentidadContable"), Centro: f.EnteroNulo("IdCentroCoste")),
            trabajo.IdSociedad);
        if (filas.Count == 0) return [];
        var cuentas = (await s.ConsultarAsync("SELECT IdIdentidadContable, Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Codigo: f.Texto("Codigo")?.Trim(), Nombre: f.Texto("Nombre")), trabajo.IdSociedadEjercicio))
            .Where(c => c.Identidad is not null).GroupBy(c => c.Identidad!.Value).ToDictionary(g => g.Key, g => g.First());
        var centros = (await s.ConsultarAsync("SELECT IdCentroCoste, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Nombre: f.Texto("Nombre")), trabajo.IdSociedad)).ToDictionary(c => c.Id, c => c.Nombre);
        var r = new List<TextoClaveCompra>(filas.Count);
        foreach (var f in filas)
        {
            string? codigo = null, nombre = null;
            var enEjercicio = false;
            if (f.Identidad is int ident)
            {
                if (cuentas.TryGetValue(ident, out var c)) (codigo, nombre, enEjercicio) = (c.Codigo, c.Nombre, true);
                else
                {
                    var ultima = await s.ConsultarPrimeroAsync("SELECT TOP 1 Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable = ? ORDER BY IdSociedadEjercicio DESC",
                        x => (x.Texto("Codigo")?.Trim(), x.Texto("Nombre")), ident);
                    (codigo, nombre) = ultima;
                }
            }
            r.Add(new TextoClaveCompra(f.Id, f.Texto, f.Identidad, codigo, nombre, enEjercicio, f.Centro,
                f.Centro is int ce && centros.TryGetValue(ce, out var nc) ? nc : null));
        }
        return r.OrderBy(t => t.Texto, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Da de alta o cambia un texto clave de la empresa. <paramref name="cuenta"/>: código de una subcuenta del ejercicio de
    /// trabajo que admita apuntes; vacía, sin cuenta. Hace falta la cuenta, el centro o los dos. Dar de alta un texto que ya
    /// existe en la empresa (sin distinguir mayúsculas ni acentos) no lo repite: le añade lo que se asigna ahora.
    /// </summary>
    public Task<Resultado<int>> GuardarAsync(ContextoSociedadEjercicio trabajo, int? idTextoClave, string texto, string? cuenta, int? idCentroCoste) =>
        bd.EnTransaccionAsync(async s =>
        {
            Resultado<int> Fallo(string codigo, string mensaje, string? accion = null) =>
                Resultado<int>.Fallo(new Error(codigo, mensaje, $"Texto clave «{texto}»", null, accion));
            texto = (texto ?? "").Trim();
            if (texto.Length == 0) return Fallo("TextosClave.SinTexto", "Escribe el texto clave.", "Por ejemplo, una palabra del concepto de la factura o el nombre de una carpeta.");
            if (texto.Length > ReglasTextosClave.LongitudMaxima)
                return Fallo("TextosClave.Largo", $"El texto clave admite hasta {ReglasTextosClave.LongitudMaxima} caracteres y tiene {texto.Length}.");
            var codigo = (cuenta ?? "").Trim();
            if (codigo.Length == 0 && idCentroCoste is null)
                return Fallo("TextosClave.SinAsignacion", "Un texto clave tiene que asignar una cuenta de gasto, un centro de coste o los dos.");
            int? identidad = null;
            if (codigo.Length > 0)
            {
                var c = await s.ConsultarPrimeroAsync("SELECT IdIdentidadContable, EsImputable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                    f => (Id: f.EnteroNulo("IdIdentidadContable"), Imputable: f.Logico("EsImputable")), trabajo.IdSociedadEjercicio, codigo);
                if (c.Id is not int ident)
                    return Fallo("TextosClave.CuentaNoExiste", $"No existe la cuenta {codigo} en el ejercicio {trabajo.Ejercicio}.", "Revisa el código o da de alta la subcuenta.");
                if (!c.Imputable) return Fallo("TextosClave.CuentaNoImputable", $"La cuenta {codigo} no admite apuntes.", "Elige una subcuenta, por ejemplo 6290001.");
                identidad = ident;
            }
            if (idCentroCoste is int ce)
            {
                var centro = await s.ConsultarPrimeroAsync("SELECT IdSociedad, EstaActivo, EsImputable FROM CentrosCoste WHERE IdCentroCoste = ?",
                    f => (Sociedad: f.Texto("IdSociedad")?.Trim(), Activo: f.Logico("EstaActivo"), Imputable: f.Logico("EsImputable")), ce);
                if (centro.Sociedad != trabajo.IdSociedad || !centro.Activo || !centro.Imputable)
                    return Fallo("TextosClave.CentroNoValido", "El centro de coste no es de la empresa, no está activo o no admite apuntes.", "Elige otro centro de coste.");
            }
            var mismo = (await s.ConsultarAsync("SELECT IdTextoClave, Texto FROM TextosClaveCompra WHERE IdSociedad = ?",
                    f => (Id: f.Entero("IdTextoClave"), Texto: f.Texto("Texto")?.Trim() ?? ""), trabajo.IdSociedad))
                .Where(t => ReglasTextosClave.Plano(t.Texto) == ReglasTextosClave.Plano(texto)).Select(t => (int?)t.Id).FirstOrDefault();
            if (idTextoClave is int id)
            {
                if (mismo is int otro && otro != id) return Fallo("TextosClave.Repetido", $"Ya hay otro texto clave «{texto}» en la empresa.", "Cambia ese en lugar de repetirlo.");
                var filas = await s.EjecutarAsync("UPDATE TextosClaveCompra SET Texto = ?, IdIdentidadContable = ?, IdCentroCoste = ? WHERE IdTextoClave = ? AND IdSociedad = ?",
                    texto, identidad, idCentroCoste, id, trabajo.IdSociedad);
                return filas == 0 ? Fallo("TextosClave.NoExiste", "El texto clave ya no existe.", "Actualiza la lista.") : Resultado<int>.Correcto(id);
            }
            if (mismo is int existente)
            {
                // Dar de alta un texto que ya existe le añade lo que se asigna ahora y conserva lo demás.
                var antes = await s.ConsultarPrimeroAsync("SELECT IdIdentidadContable, IdCentroCoste FROM TextosClaveCompra WHERE IdTextoClave = ?",
                    f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Centro: f.EnteroNulo("IdCentroCoste")), existente);
                await s.EjecutarAsync("UPDATE TextosClaveCompra SET Texto = ?, IdIdentidadContable = ?, IdCentroCoste = ? WHERE IdTextoClave = ?",
                    texto, identidad ?? antes.Identidad, idCentroCoste ?? antes.Centro, existente);
                return Resultado<int>.Correcto(existente);
            }
            await s.EjecutarAsync("INSERT INTO TextosClaveCompra (IdSociedad, Texto, IdIdentidadContable, IdCentroCoste) VALUES (?, ?, ?, ?)",
                trabajo.IdSociedad, texto, identidad, idCentroCoste);
            return Resultado<int>.Correcto(await s.UltimoIdAsync());
        });

    /// <summary>Centros de coste activos e imputables de la empresa, con «código · nombre», en el orden de la jerarquía.</summary>
    public Task<IReadOnlyList<(int Id, string Texto)>> CentrosAsync(string idSociedad) =>
        bd.EjecutarAsync<IReadOnlyList<(int Id, string Texto)>>(async s => (await s.ConsultarAsync(
                "SELECT IdCentroCoste, Codigo, Nombre, RutaJerarquica, EstaActivo, EsImputable FROM CentrosCoste WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdCentroCoste"), Texto: $"{f.Texto("Codigo")?.Trim()} · {f.Texto("Nombre")?.Trim()}", Ruta: f.Texto("RutaJerarquica") ?? "",
                    Valido: f.Logico("EstaActivo") && f.Logico("EsImputable")), idSociedad))
            .Where(c => c.Valido).OrderBy(c => c.Ruta, StringComparer.Ordinal).Select(c => (c.Id, c.Texto)).ToList());

    public Task<Resultado> EliminarAsync(ContextoSociedadEjercicio trabajo, int idTextoClave) =>
        bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM TextosClaveCompra WHERE IdTextoClave = ? AND IdSociedad = ?", idTextoClave, trabajo.IdSociedad);
            return Resultado.Correcto();
        });
}
