using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.FormasPago;

/// <summary>Forma de pago, muy simplificada: código, descripción y si es domiciliación SEPA.</summary>
public sealed record FormaPago(byte Id, string Descripcion, bool Sepa);

/// <summary>
/// Catálogo de formas de pago. Es la única vía de escritura de la tabla FormasPago; lo usará
/// plataforma/mantenimiento. La baja está restringida por uso.
/// </summary>
public interface ICatalogoFormasPago
{
    Task<IReadOnlyList<FormaPago>> ListarAsync();
    Task<FormaPago?> ObtenerAsync(byte id);
    Task<Resultado<FormaPago>> AltaAsync(string descripcion, bool sepa);
    Task<Resultado> ModificarAsync(FormaPago formaPago);
    Task<Resultado> BajaAsync(byte id);
}

public sealed class CatalogoFormasPagoAccess(IBaseDatos bd) : ICatalogoFormasPago
{
    public const int LongitudDescripcion = 100;

    /// <summary>Columnas que guardan un código de forma de pago (diccionario de datos).</summary>
    private static readonly (string Tabla, string Columna, string Nombre)[] _usos =
    [
        ("Facturas", "FormaDePago", "facturas"),
        ("Contratos", "FormaDePago", "contratos"),
        ("Sociedades", "FormaPagoDefecto", "sociedades (forma de pago por defecto)"),
        ("SociedadesEjercicios", "FormaPagoDefecto", "ejercicios de sociedad (forma de pago por defecto)"),
    ];

    public Task<IReadOnlyList<FormaPago>> ListarAsync() =>
        bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Id, Descripcion, SEPA FROM FormasPago ORDER BY Descripcion", Mapear));

    public Task<FormaPago?> ObtenerAsync(byte id) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Id, Descripcion, SEPA FROM FormasPago WHERE Id = ?", Mapear, id));

    public Task<Resultado<FormaPago>> AltaAsync(string descripcion, bool sepa) =>
        bd.EnTransaccionAsync(async s =>
        {
            var texto = descripcion?.Trim() ?? "";
            var error = await ValidarAsync(s, texto, null);
            if (error is not null) return Resultado<FormaPago>.Fallo(error);
            var siguiente = await s.SiguienteNumeroAsync("FormasPago", "Id");
            if (siguiente > byte.MaxValue)
                return Resultado<FormaPago>.Fallo(new Error("FormasPago.SinCodigos", "No quedan códigos libres para otra forma de pago.",
                    null, "Los códigos van de 0 a 255 y el último ya está usado.", "Reutiliza o modifica una forma de pago que ya no se use."));
            var nueva = new FormaPago((byte)siguiente, texto, sepa);
            await s.EjecutarAsync("INSERT INTO FormasPago (Id, Descripcion, SEPA) VALUES (?, ?, ?)", nueva.Id, nueva.Descripcion, nueva.Sepa);
            return Resultado<FormaPago>.Correcto(nueva);
        });

    public Task<Resultado> ModificarAsync(FormaPago formaPago) =>
        bd.EnTransaccionAsync(async s =>
        {
            var texto = formaPago.Descripcion?.Trim() ?? "";
            var error = await ValidarAsync(s, texto, formaPago.Id);
            if (error is not null) return Resultado.Fallo(error);
            var filas = await s.EjecutarAsync("UPDATE FormasPago SET Descripcion = ?, SEPA = ? WHERE Id = ?", texto, formaPago.Sepa, formaPago.Id);
            return filas == 1 ? Resultado.Correcto() : Resultado.Fallo(NoExiste(formaPago.Id));
        });

    public Task<Resultado> BajaAsync(byte id) =>
        bd.EnTransaccionAsync(async s =>
        {
            var usos = new List<string>();
            foreach (var (tabla, columna, nombre) in _usos)
            {
                var n = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM [{tabla}] WHERE [{columna}] = ?", (int)id));
                if (n > 0) usos.Add($"{n} {nombre}");
            }
            if (usos.Count > 0)
                return Resultado.Fallo(new Error("FormasPago.EnUso", "No puedo dar de baja esta forma de pago porque se está usando.",
                    $"Forma de pago {id}", "La usan: " + string.Join(", ", usos) + ".",
                    "Cambia la forma de pago de esos registros o deja esta tal como está."));
            var filas = await s.EjecutarAsync("DELETE FROM FormasPago WHERE Id = ?", id);
            return filas == 1 ? Resultado.Correcto() : Resultado.Fallo(NoExiste(id));
        });

    private static async Task<Error?> ValidarAsync(ISesionDatos s, string descripcion, byte? id)
    {
        if (descripcion.Length == 0)
            return new Error("FormasPago.SinDescripcion", "Falta la descripción de la forma de pago.", null,
                "La descripción es obligatoria.", "Escribe cómo se llama la forma de pago, por ejemplo «Transferencia».");
        if (descripcion.Length > LongitudDescripcion)
            return new Error("FormasPago.DescripcionLarga", $"La descripción tiene más de {LongitudDescripcion} caracteres.", null,
                $"Tiene {descripcion.Length}.", "Acórtala.");
        // Las descripciones pueden repetirse (0 y 1 se llaman «Indefinida», decisión del propietario).
        await Task.CompletedTask;
        return null;
    }

    private static Error NoExiste(byte id) =>
        new("FormasPago.NoExiste", "La forma de pago ya no existe.", $"Forma de pago {id}",
            "Otra persona la ha borrado.", "Vuelve a cargar la lista.");

    private static FormaPago Mapear(IFilaDatos f) => new((byte)f.Entero("Id"), f.Texto("Descripcion") ?? "", f.Logico("SEPA"));
}
