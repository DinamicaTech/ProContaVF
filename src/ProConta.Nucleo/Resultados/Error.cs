namespace ProConta.Nucleo.Resultados;

/// <summary>
/// Error recuperable que se muestra al operador (RF-UI-06): dice qué registro está afectado,
/// por qué ha pasado y qué puede hacer para corregirlo. El detalle técnico va aparte y
/// nunca contiene secretos.
/// </summary>
/// <param name="Codigo">Código estable del error, para pruebas y registros (p. ej. "NIF.DigitoControl").</param>
/// <param name="Mensaje">Frase en primera persona para el operador.</param>
/// <param name="RegistroAfectado">Registro afectado (p. ej. "Tercero 000123"), si lo hay.</param>
/// <param name="Causa">Causa conocida del error.</param>
/// <param name="AccionCorrectora">Acción concreta que corrige el error.</param>
/// <param name="DetalleTecnico">Detalle técnico copiable, sin secretos.</param>
public sealed record Error(
    string Codigo,
    string Mensaje,
    string? RegistroAfectado = null,
    string? Causa = null,
    string? AccionCorrectora = null,
    string? DetalleTecnico = null)
{
    /// <summary>Texto completo para el operador: mensaje, registro, causa y acción.</summary>
    public string TextoOperador()
    {
        var partes = new List<string> { Mensaje };
        if (!string.IsNullOrWhiteSpace(RegistroAfectado)) partes.Add($"Registro: {RegistroAfectado}.");
        if (!string.IsNullOrWhiteSpace(Causa)) partes.Add($"Causa: {Causa}");
        if (!string.IsNullOrWhiteSpace(AccionCorrectora)) partes.Add($"Qué hacer: {AccionCorrectora}");
        return string.Join(" ", partes);
    }

    public override string ToString() => $"[{Codigo}] {TextoOperador()}";
}
