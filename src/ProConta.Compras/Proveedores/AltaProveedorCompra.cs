using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Compras.Proveedores;

/// <summary>Lo que se sabe del proveedor al darlo de alta desde una factura de compra.</summary>
public sealed record DatosAltaProveedor(string Nif, string RazonSocial, string? NombreComercial = null, string? Direccion = null,
    string? Telefono = null, string? Email = null);

/// <summary>Proveedor dado de alta (o el tercero que ya existía con ese NIF, ahora proveedor) y su cuenta de acreedor.</summary>
public sealed record ProveedorCreado(int IdProveedor, int IdTercero, string Nif, string Nombre, string Cuenta, bool TerceroExistente);

/// <summary>Respuesta de la AEAT sobre un NIF. <c>Consultado</c> = false: no se pudo preguntar (sin certificado, sin conexión…), con el motivo.</summary>
public sealed record ResultadoNifAeat(bool Consultado, bool Identificado, string? RazonSocial, string? Motivo)
{
    public static ResultadoNifAeat SinConsultar(string motivo) => new(false, false, null, motivo);
}

/// <summary>Consulta de la razón social de un NIF español en la AEAT (servicio VNifV2) con el certificado de la empresa.</summary>
public interface IConsultaNifAeat
{
    Task<ResultadoNifAeat> ConsultarAsync(string idSociedad, string nif, string? nombre, CancellationToken cancelacion = default);
}

/// <summary>
/// Alta de proveedor desde la factura de compra (compras/pantallas 2026-11-06 12:10 y 13:16). La razón social se confirma
/// primero con la AEAT si la empresa tiene certificado; si no hay confirmación, la pantalla pide los datos. El proveedor
/// se crea como acreedor (cuenta 410, la siguiente libre del ejercicio de trabajo); la primera exportación de su factura
/// preguntará si es proveedor (400). Si ya hay un único tercero con ese NIF, se le da el rol en lugar de duplicarlo.
/// </summary>
public sealed class ServicioAltaProveedorCompra(IBaseDatos bd, IRepositorioTerceros terceros, ServicioRoles roles, ICatalogoCuentas cuentas,
    IConsultaNifAeat? aeat, Actor actor)
{
    public const string CuentaAcreedores = "410";
    public const string CuentaProveedores = "400";

    /// <summary>
    /// Alta automática al leer la factura (compras/pantallas 2026-11-07): con el NIF leído se consulta la razón social en
    /// la AEAT y, si esa razón social aparece en el texto del PDF, se da de alta como acreedor (410). Que el nombre esté en
    /// la factura es la salvaguarda contra un NIF mal leído. Devuelve lo que dijo la AEAT y el proveedor, si se creó.
    /// </summary>
    public async Task<(ResultadoNifAeat Aeat, ProveedorCreado? Creado, string? Error)> AltaAutomaticaAsync(ContextoSociedadEjercicio ctx, string nif, string? textoPdf,
        string? nombreLeido, CancellationToken cancelacion = default)
    {
        var aeat = await ConsultarAsync(ctx, nif, nombreLeido, cancelacion);
        if (!aeat.Identificado || string.IsNullOrWhiteSpace(aeat.RazonSocial) || !NombreEnTexto(aeat.RazonSocial, textoPdf)) return (aeat, null, null);
        var r = await AltaAsync(ctx, new DatosAltaProveedor(nif, aeat.RazonSocial.Trim()));
        return r.EsCorrecto ? (aeat, r.Valor, null) : (aeat, null, string.Join(" ", r.Errores.Select(e => e.Mensaje)));
    }

    private static readonly HashSet<string> FormasJuridicas = new(StringComparer.Ordinal)
    {
        "SL", "SLU", "SLL", "SLNE", "SA", "SAU", "SAL", "SC", "SCP", "SCOOP", "COOP", "CB", "SRL", "SOCIEDAD", "LIMITADA", "ANONIMA", "UNIPERSONAL",
        "LABORAL", "COOPERATIVA", "CIVIL", "PROFESIONAL", "COMUNIDAD", "BIENES", "DE", "Y", "EN"
    };

    /// <summary>
    /// ¿La razón social está en el texto? Sin mayúsculas, acentos ni puntuación, y sin la forma jurídica («S.L.», «SL»,
    /// «Sociedad Limitada»…): sus palabras significativas tienen que aparecer seguidas en el texto.
    /// </summary>
    public static bool NombreEnTexto(string razonSocial, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var nombre = Palabras(razonSocial).Where(p => !FormasJuridicas.Contains(p)).ToList();
        if (nombre.Count == 0 || string.Concat(nombre).Length < 4) return false;
        var buscado = " " + string.Join(" ", nombre) + " ";
        var enTexto = " " + string.Join(" ", Palabras(texto).Where(p => !FormasJuridicas.Contains(p))) + " ";
        return enTexto.Contains(buscado, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Palabras(string texto)
    {
        var d = texto.Normalize(System.Text.NormalizationForm.FormD);
        var limpio = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark || ch == '.') continue;
            limpio.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return limpio.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Pone el nombre comercial del tercero del proveedor (compras/pantallas 2026-11-07 20:10): el alta automática solo
    /// trae la razón social, y muchas veces el proveedor se conoce por su nombre comercial. Vacío, lo quita.
    /// </summary>
    public async Task<Resultado<bool>> NombreComercialAsync(int idTercero, string? nombreComercial)
    {
        var t = await terceros.ObtenerAsync(idTercero);
        if (t is null) return Resultado<bool>.Fallo(new Error("Proveedores.SinTercero", "No encuentro la ficha del proveedor.", null, null, "Vuelve a abrir la factura."));
        var nombre = string.IsNullOrWhiteSpace(nombreComercial) ? null : nombreComercial.Trim();
        if (nombre is { Length: > 150 }) return Resultado<bool>.Fallo(new Error("Proveedores.NombreComercialLargo", "El nombre comercial no puede pasar de 150 caracteres.", null, null, "Acórtalo."));
        if (t.NombreComercial == nombre) return Resultado<bool>.Correcto(false);
        t.NombreComercial = nombre;
        var r = await terceros.GuardarAsync(t, actor);
        return r.EsCorrecto ? Resultado<bool>.Correcto(true) : Resultado<bool>.Fallo(r.Errores);
    }

    /// <summary>NIF de la factura comprobado en la AEAT; sin certificado o sin servicio, sin consultar.</summary>
    public async Task<ResultadoNifAeat> ConsultarAsync(ContextoSociedadEjercicio ctx, string nif, string? nombre, CancellationToken cancelacion = default)
    {
        var n = Nif.Validar(nif);
        if (!n.EsCorrecto) return ResultadoNifAeat.SinConsultar("La AEAT solo comprueba NIF españoles.");
        if (aeat is null) return ResultadoNifAeat.SinConsultar("La consulta a la AEAT no está disponible.");
        try
        {
            return await aeat.ConsultarAsync(ctx.IdSociedad, n.Valor.Valor, nombre, cancelacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelacion.IsCancellationRequested)
        {
            return ResultadoNifAeat.SinConsultar($"No he podido consultar la AEAT: {ex.Message}");
        }
    }

    /// <summary>
    /// Da de alta el proveedor. <paramref name="comoProveedor"/>: con su cuenta de proveedor (400) en lugar de la de
    /// acreedor (410), cuando el operador ya lo ha decidido al guardar (compras/pantallas 2026-11-07).
    /// </summary>
    public async Task<Resultado<ProveedorCreado>> AltaAsync(ContextoSociedadEjercicio ctx, DatosAltaProveedor datos, bool comoProveedor = false)
    {
        Resultado<ProveedorCreado> Fallo(string codigo, string mensaje, string? accion = null) =>
            Resultado<ProveedorCreado>.Fallo(new Error(codigo, mensaje, $"Proveedor {datos.Nif}", null, accion));
        var nif = Nif.Normalizar(datos.Nif);
        var razon = (datos.RazonSocial ?? "").Trim();
        if (nif.Length == 0) return Fallo("AltaProveedor.SinNif", "Falta el NIF del proveedor.", "Escríbelo en la ficha de la factura.");
        if (razon.Length == 0) return Fallo("AltaProveedor.SinRazonSocial", "Falta la razón social del proveedor.", "Escríbela tal como figura en la factura.");
        var validado = Nif.Validar(nif);
        var extranjero = !validado.EsCorrecto && nif.Length > 2 && char.IsAsciiLetter(nif[0]) && char.IsAsciiLetter(nif[1]) && !nif.StartsWith("ES", StringComparison.Ordinal);
        if (!validado.EsCorrecto && !extranjero) return Resultado<ProveedorCreado>.Fallo(validado.Errores);

        // Un tercero que ya tiene ese NIF no se duplica: se le da el rol de proveedor.
        var mismos = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdTercero, NIF FROM Terceros", f => (Id: f.Entero("IdTercero"), Nif: f.Texto("NIF"))));
        var conNif = mismos.Where(t => Nif.Normalizar(t.Nif) == nif).Select(t => t.Id).Distinct().ToList();
        if (conNif.Count > 1)
            return Fallo("AltaProveedor.NifRepetido", $"Hay {conNif.Count} terceros con el NIF {nif}.", "Elige cuál es el proveedor en Terceros y dale allí el rol.");

        int idTercero;
        var existente = conNif.Count == 1;
        string nombre;
        if (existente)
        {
            idTercero = conNif[0];
            var rol = await roles.AsegurarProveedorAsync(idTercero);
            if (!rol.EsCorrecto) return Resultado<ProveedorCreado>.Fallo(rol.Errores);
            nombre = (await terceros.ObtenerAsync(idTercero))?.Nombre ?? razon;
        }
        else
        {
            var fisica = validado.EsCorrecto && validado.Valor.EsPersonaFisica;
            var t = new Tercero
            {
                TipoPersona = fisica ? TipoPersona.Fisica : TipoPersona.Juridica,
                // De una persona física solo se sabe el nombre completo de la factura: va entero en el nombre.
                NombrePersona = fisica ? razon : null,
                RazonSocial = fisica ? null : razon,
                NombreComercial = Vacio(datos.NombreComercial),
                Nif = nif,
                TipoIdentificacion = extranjero ? "VAT" : null,
                Pais = extranjero ? nif[..2] : null,
                Direccion = Vacio(datos.Direccion),
                Telefono = Vacio(datos.Telefono),
                Email = Vacio(datos.Email),
            };
            t.Roles.Proveedor = new RolProveedor { Activo = true };
            var guardado = await terceros.GuardarAsync(t, actor);
            if (!guardado.EsCorrecto) return Resultado<ProveedorCreado>.Fallo(guardado.Errores);
            idTercero = guardado.Valor.IdTercero;
            nombre = guardado.Valor.Nombre;
        }

        var idProveedor = (await roles.ObtenerAsync(idTercero)).IdProveedor!.Value;
        var actuales = await roles.CuentasAsync(idTercero, ctx);
        // Si ya tiene cuenta de acreedor o de proveedor en la empresa, se respeta.
        if ((actuales.Acreedor ?? actuales.Proveedor) is { ExisteEnEjercicio: true } ya)
            return Resultado<ProveedorCreado>.Correcto(new ProveedorCreado(idProveedor, idTercero, nif, nombre, ya.Codigo, existente));

        var cuenta = await CuentaAcreedorAsync(ctx, nombre, comoProveedor ? CuentaProveedores : CuentaAcreedores);
        if (!cuenta.EsCorrecto) return Resultado<ProveedorCreado>.Fallo(cuenta.Errores);
        var asignada = await roles.AsignarCuentaAsync(comoProveedor ? ClaseCuentaRol.Proveedor : ClaseCuentaRol.Acreedor, idProveedor, ctx.IdSociedad, cuenta.Valor.IdIdentidadContable);
        if (!asignada.EsCorrecto) return Resultado<ProveedorCreado>.Fallo(asignada.Errores);
        return Resultado<ProveedorCreado>.Correcto(new ProveedorCreado(idProveedor, idTercero, nif, nombre, cuenta.Valor.Codigo, existente));
    }

    /// <summary>La siguiente subcuenta libre de la 410 (o la 400) en el ejercicio, con el nombre del proveedor, colgando de ella si existe.</summary>
    private async Task<Resultado<CuentaContable>> CuentaAcreedorAsync(ContextoSociedadEjercicio ctx, string nombre, string raiz)
    {
        var codigo = await cuentas.SiguienteSubcuentaAsync(ctx, raiz);
        if (codigo is null)
            return Resultado<CuentaContable>.Fallo(new Error("AltaProveedor.SinCodigos", $"No quedan códigos libres en la cuenta {raiz}.", nombre, null,
                "Asígnale la cuenta a mano en la ficha del tercero."));
        var padre = await cuentas.ResolverAsync(ctx, raiz);
        var texto = nombre.Length > ReglasCuenta.LongitudMaximaNombre ? nombre[..ReglasCuenta.LongitudMaximaNombre] : nombre;
        return await cuentas.AltaAsync(ctx, new AltaCuenta(codigo, texto, padre.EsCorrecto ? padre.Valor.IdCuentaContable : null));
    }

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
