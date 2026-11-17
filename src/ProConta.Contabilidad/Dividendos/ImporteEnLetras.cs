namespace ProConta.Contabilidad.Dividendos;

/// <summary>
/// Importes en letras, en castellano, para el acta del reparto de dividendos (contabilidad/procesos/aplicacion-resultado 2026-11-17):
/// 123.456,80 → «ciento veintitrés mil cuatrocientos cincuenta y seis euros con ochenta céntimos». Hasta 999.999.999.999,99.
/// </summary>
public static class ImporteEnLetras
{
    private static readonly string[] Unidades =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce", "quince",
        "dieciséis", "diecisiete", "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco",
        "veintiséis", "veintisiete", "veintiocho", "veintinueve",
    ];

    private static readonly string[] Decenas = ["", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"];

    private static readonly string[] Centenas =
        ["", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos"];

    /// <summary>Importe en euros con sus céntimos («… euros», «… euros con … céntimos»).</summary>
    public static string Euros(decimal importe)
    {
        var v = Math.Round(Math.Abs(importe), 2, MidpointRounding.AwayFromZero);
        var enteros = (long)Math.Truncate(v);
        var centimos = (int)((v - enteros) * 100m);
        var texto = $"{Apocopar(Numero(enteros))} {(enteros == 1 ? "euro" : "euros")}";
        if (centimos > 0) texto += $" con {Apocopar(Numero(centimos))} {(centimos == 1 ? "céntimo" : "céntimos")}";
        return importe < 0m ? "menos " + texto : texto;
    }

    /// <summary>Número entero en letras («uno», «veintiuno», «mil», «un millón»).</summary>
    public static string Numero(long n)
    {
        if (n < 0) return "menos " + Numero(-n);
        if (n < 1000) return Menor1000((int)n);
        if (n < 1_000_000)
        {
            var miles = (int)(n / 1000);
            var resto = (int)(n % 1000);
            var m = miles == 1 ? "mil" : $"{Apocopar(Menor1000(miles))} mil";
            return resto == 0 ? m : $"{m} {Menor1000(resto)}";
        }
        if (n < 1_000_000_000_000)
        {
            var millones = n / 1_000_000;
            var resto = n % 1_000_000;
            var m = millones == 1 ? "un millón" : $"{Apocopar(Numero(millones))} millones";
            return resto == 0 ? m : $"{m} {Numero(resto)}";
        }
        throw new ArgumentOutOfRangeException(nameof(n), "Importe demasiado grande para escribirlo en letras.");
    }

    private static string Menor1000(int n)
    {
        if (n < 30) return Unidades[n];
        if (n < 100) return n % 10 == 0 ? Decenas[n / 10] : $"{Decenas[n / 10]} y {Unidades[n % 10]}";
        if (n == 100) return "cien";
        var resto = n % 100;
        return resto == 0 ? Centenas[n / 100] : $"{Centenas[n / 100]} {Menor1000(resto)}";
    }

    /// <summary>«uno» delante de un sustantivo pasa a «un» («veintiún euros», «treinta y un mil»).</summary>
    private static string Apocopar(string texto) =>
        texto.EndsWith("veintiuno", StringComparison.Ordinal) ? texto[..^"veintiuno".Length] + "veintiún"
        : texto.EndsWith("uno", StringComparison.Ordinal) ? texto[..^1] : texto;
}
