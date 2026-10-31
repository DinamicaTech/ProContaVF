using ProConta.Nucleo.Resultados;

namespace ProConta.Nucleo.Identificacion;

/// <summary>Clase de identificador fiscal español.</summary>
public enum TipoNif
{
    /// <summary>DNI de persona física española: 8 cifras y letra.</summary>
    Dni,
    /// <summary>NIE de extranjero: X, Y o Z, 7 cifras y letra.</summary>
    Nie,
    /// <summary>NIF especial de persona física (K, L, M), 7 cifras y letra.</summary>
    Especial,
    /// <summary>NIF de persona jurídica o entidad (antiguo CIF): letra, 7 cifras y control.</summary>
    Entidad
}

/// <summary>
/// NIF español validado (DNI, NIE, NIF especial o de entidad). Se crea solo mediante
/// <see cref="Validar"/>, que normaliza el texto (mayúsculas, sin espacios, guiones ni puntos,
/// sin el prefijo de país "ES") y comprueba el carácter de control.
/// </summary>
public sealed record Nif
{
    private const string LetrasDni = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const string LetrasControlEntidad = "JABCDEFGHI";
    private const string LetrasEntidad = "ABCDEFGHJNPQRSUVW";

    private Nif(string valor, TipoNif tipo)
    {
        Valor = valor;
        Tipo = tipo;
    }

    /// <summary>Texto normalizado, 9 caracteres.</summary>
    public string Valor { get; }

    public TipoNif Tipo { get; }

    public bool EsPersonaFisica => Tipo != TipoNif.Entidad;

    public override string ToString() => Valor;

    /// <summary>Normaliza el texto sin validar: mayúsculas, sin separadores ni prefijo "ES".</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var limpio = new string(texto.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.' && c != '/').ToArray())
            .ToUpperInvariant();
        if (limpio.Length == 11 && limpio.StartsWith("ES", StringComparison.Ordinal)) limpio = limpio[2..];
        return limpio;
    }

    public static bool EsValido(string? texto) => Validar(texto).EsCorrecto;

    /// <summary>Valida un NIF y devuelve su forma normalizada o un error explicado.</summary>
    public static Resultado<Nif> Validar(string? texto)
    {
        var nif = Normalizar(texto);
        if (nif.Length == 0)
            return Fallo("NIF.Vacio", "No has indicado el NIF.", texto, "El campo está vacío.", "Escribe el NIF completo, por ejemplo 12345678Z.");
        if (nif.Length != 9)
            return Fallo("NIF.Longitud", $"El NIF «{nif}» no tiene 9 caracteres.", texto,
                $"Tiene {nif.Length} caracteres; un NIF español tiene siempre 9.",
                "Revisa que no falte ni sobre ninguna cifra; si empieza por cero, incluye los ceros.");

        var primero = nif[0];
        var cuerpo = nif[1..8];
        var ultimo = nif[8];

        if (char.IsDigit(primero))
        {
            if (!nif[..8].All(char.IsDigit))
                return FormatoIncorrecto(nif, texto);
            var letra = LetrasDni[int.Parse(nif[..8]) % 23];
            return ultimo == letra
                ? Resultado<Nif>.Correcto(new Nif(nif, TipoNif.Dni))
                : ControlIncorrecto(nif, texto, letra.ToString());
        }

        if (!cuerpo.All(char.IsDigit))
            return FormatoIncorrecto(nif, texto);

        if (primero is 'X' or 'Y' or 'Z')
        {
            var numero = int.Parse((primero - 'X').ToString() + cuerpo);
            var letra = LetrasDni[numero % 23];
            return ultimo == letra
                ? Resultado<Nif>.Correcto(new Nif(nif, TipoNif.Nie))
                : ControlIncorrecto(nif, texto, letra.ToString());
        }

        if (primero is 'K' or 'L' or 'M')
        {
            var letra = LetrasDni[int.Parse(cuerpo) % 23];
            return ultimo == letra
                ? Resultado<Nif>.Correcto(new Nif(nif, TipoNif.Especial))
                : ControlIncorrecto(nif, texto, letra.ToString());
        }

        if (LetrasEntidad.Contains(primero))
        {
            var digito = DigitoControlEntidad(cuerpo);
            var letraControl = LetrasControlEntidad[digito];
            var digitoControl = (char)('0' + digito);
            // Entidades que usan siempre letra, siempre cifra o cualquiera de las dos.
            var valido = primero switch
            {
                'P' or 'Q' or 'R' or 'S' or 'N' or 'W' => ultimo == letraControl,
                'A' or 'B' or 'E' or 'H' => ultimo == digitoControl,
                _ => ultimo == letraControl || ultimo == digitoControl
            };
            var esperado = primero switch
            {
                'P' or 'Q' or 'R' or 'S' or 'N' or 'W' => letraControl.ToString(),
                'A' or 'B' or 'E' or 'H' => digitoControl.ToString(),
                _ => $"{digitoControl} o {letraControl}"
            };
            return valido
                ? Resultado<Nif>.Correcto(new Nif(nif, TipoNif.Entidad))
                : ControlIncorrecto(nif, texto, esperado);
        }

        return FormatoIncorrecto(nif, texto);
    }

    private static int DigitoControlEntidad(string sieteCifras)
    {
        var suma = 0;
        for (var i = 0; i < 7; i++)
        {
            var cifra = sieteCifras[i] - '0';
            if (i % 2 == 0)
            {
                var doble = cifra * 2;
                suma += doble / 10 + doble % 10;
            }
            else
            {
                suma += cifra;
            }
        }
        return (10 - suma % 10) % 10;
    }

    private static Resultado<Nif> FormatoIncorrecto(string nif, string? original) =>
        Fallo("NIF.Formato", $"El NIF «{nif}» no tiene un formato válido.", original,
            "Un NIF es un DNI (8 cifras y letra), un NIE (X, Y o Z, 7 cifras y letra) o el de una entidad (letra, 7 cifras y control).",
            "Comprueba el documento original y escríbelo sin espacios.");

    private static Resultado<Nif> ControlIncorrecto(string nif, string? original, string esperado) =>
        Fallo("NIF.Control", $"El carácter de control del NIF «{nif}» no es correcto.", original,
            $"Con esas cifras el carácter de control debería ser {esperado}.",
            "Revisa las cifras y el último carácter con el documento original.");

    private static Resultado<Nif> Fallo(string codigo, string mensaje, string? original, string causa, string accion) =>
        Resultado<Nif>.Fallo(new Error(codigo, mensaje, string.IsNullOrWhiteSpace(original) ? null : $"NIF «{original}»", causa, accion));
}
