using System.Security.Cryptography;
using System.Text;

namespace ProConta.Nucleo.Seguridad;

/// <summary>
/// Cifrado de los secretos que se guardan en la BD (claves de AWS de compras/reconocimiento 2026-11-07 y contraseñas de las cuentas de correo, correo/envio 2026-11-10).
/// AES-GCM con una clave del programa, para que cualquier puesto con ProContaVF las lea sin configurar nada.
/// </summary>
/// <remarks>
/// Protege frente a quien abre la BD, no frente a quien tiene la BD y el programa: por eso las claves que se guardan
/// deben tener solo el permiso imprescindible (para Textract, DetectDocumentText).
/// </remarks>
public static class SecretoProtegido
{
    private const string Prefijo = "pcv1:";
    private const int Nonce = 12, Etiqueta = 16;
    private static readonly byte[] Clave = [0x8F, 0x51, 0xE7, 0x00, 0x25, 0x63, 0x50, 0x00, 0xF0, 0x5B, 0xFF, 0x7C, 0x1B, 0x55, 0xE3, 0x14, 0xB3, 0xB5, 0x82, 0x7B, 0xB7, 0xD8, 0xB3, 0xBA, 0x60, 0x0C, 0x3C, 0x73, 0x64, 0x9F, 0x04, 0x66];

    public static string Proteger(string secreto)
    {
        ArgumentException.ThrowIfNullOrEmpty(secreto);
        var claro = Encoding.UTF8.GetBytes(secreto);
        var salida = new byte[Nonce + Etiqueta + claro.Length];
        var nonce = salida.AsSpan(0, Nonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Clave, Etiqueta);
        aes.Encrypt(nonce, claro, salida.AsSpan(Nonce + Etiqueta), salida.AsSpan(Nonce, Etiqueta));
        return Prefijo + Convert.ToBase64String(salida);
    }

    /// <summary>El secreto en claro, o null si no hay o no se puede descifrar (otro formato o alterado).</summary>
    public static string? Desproteger(string? protegido)
    {
        if (string.IsNullOrWhiteSpace(protegido) || !protegido.StartsWith(Prefijo, StringComparison.Ordinal)) return null;
        try
        {
            var datos = Convert.FromBase64String(protegido[Prefijo.Length..]);
            if (datos.Length <= Nonce + Etiqueta) return null;
            var claro = new byte[datos.Length - Nonce - Etiqueta];
            using var aes = new AesGcm(Clave, Etiqueta);
            aes.Decrypt(datos.AsSpan(0, Nonce), datos.AsSpan(Nonce + Etiqueta), datos.AsSpan(Nonce, Etiqueta), claro);
            return Encoding.UTF8.GetString(claro);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
