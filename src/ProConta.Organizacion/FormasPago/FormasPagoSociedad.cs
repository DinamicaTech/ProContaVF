using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.FormasPago;

/// <summary>
/// Forma de pago vista desde una empresa: la subcuenta que usa (tabla FormasPagoSociedad, organizacion/formas-pago
/// 2026-11-06). Se guarda la identidad contable y se muestra el código del ejercicio de trabajo; si la cuenta no existe en
/// ese ejercicio, el último código que tuvo y <c>CuentaEnEjercicio</c> = false. También la cuenta bancaria de la empresa en la
/// que se cobra (facturacion/factura-venta 2026-11-13 12:13), cuyo IBAN sale en las facturas.
/// </summary>
public sealed record FormaPagoSociedad(byte IdFormaPago, string Descripcion, int? IdIdentidadContable, string? Cuenta, string? NombreCuenta, bool CuentaEnEjercicio,
    int? IdCuentaBancaria = null, string? CuentaBancaria = null);

/// <summary>Cuenta bancaria de una empresa (CuentasBancarias, dueño norma43) tal como se elige y se muestra: nombre e IBAN.</summary>
public sealed record CuentaBancariaEmpresa(int IdCuentaBancaria, string? Nombre, string? Iban, bool Activa)
{
    /// <summary>«Nombre · IBAN agrupado de cuatro en cuatro», o lo que haya de los dos.</summary>
    public string Texto => string.Join(" · ", new[] { Nombre?.Trim(), Ibanes.Agrupar(Iban) }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } t ? t : $"Cuenta {IdCuentaBancaria}";
}

/// <summary>Subcuentas de las formas de pago por empresa. Es la única vía de escritura de FormasPagoSociedad.</summary>
public sealed class CatalogoFormasPagoSociedad(IBaseDatos bd)
{
    /// <summary>Todas las formas de pago, con la subcuenta que tienen en la empresa de trabajo (o sin ella).</summary>
    public Task<IReadOnlyList<FormaPagoSociedad>> ListarAsync(ContextoSociedadEjercicio trabajo) =>
        bd.EjecutarAsync<IReadOnlyList<FormaPagoSociedad>>(async s =>
        {
            var formas = await s.ConsultarAsync("SELECT Id, Descripcion FROM FormasPago ORDER BY Descripcion",
                f => ((byte)f.Entero("Id"), f.Texto("Descripcion") ?? ""));
            // Sin parámetros en un ON (fallo de Access): las tres consultas se cruzan en memoria.
            var asignadas = (await s.ConsultarAsync("SELECT IdFormaPago, IdIdentidadContable, IdCuentaBancaria FROM FormasPagoSociedad WHERE IdSociedad = ?",
                f => ((byte)f.Entero("IdFormaPago"), f.EnteroNulo("IdIdentidadContable"), f.EnteroNulo("IdCuentaBancaria")), trabajo.IdSociedad))
                .ToDictionary(x => x.Item1, x => (Identidad: x.Item2, Banco: x.Item3));
            var bancos = (await CuentasBancariasEnSesionAsync(s, trabajo.IdSociedad)).ToDictionary(b => b.IdCuentaBancaria);
            var r = new List<FormaPagoSociedad>(formas.Count);
            foreach (var (id, descripcion) in formas)
            {
                var fila = asignadas.GetValueOrDefault(id);
                var banco = fila.Banco is int b ? bancos.GetValueOrDefault(b)?.Texto ?? $"Cuenta bancaria {b} (ya no existe)" : null;
                if (fila.Identidad is not int ident)
                {
                    r.Add(new(id, descripcion, null, null, null, false, fila.Banco, banco));
                    continue;
                }
                var enEjercicio = await s.ConsultarPrimeroAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
                    f => (f.Texto("Codigo"), f.Texto("Nombre")), trabajo.IdSociedadEjercicio, ident);
                if (enEjercicio.Item1 is not null) { r.Add(new(id, descripcion, ident, enEjercicio.Item1, enEjercicio.Item2, true, fila.Banco, banco)); continue; }
                var ultima = await s.ConsultarPrimeroAsync("SELECT TOP 1 Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable = ? ORDER BY IdSociedadEjercicio DESC",
                    f => (f.Texto("Codigo"), f.Texto("Nombre")), ident);
                r.Add(new(id, descripcion, ident, ultima.Item1, ultima.Item2, false, fila.Banco, banco));
            }
            return r;
        });

    /// <summary>
    /// Asigna a la forma de pago la subcuenta <paramref name="codigo"/> del ejercicio de trabajo; vacío quita la asignación.
    /// La subcuenta tiene que existir en el ejercicio y admitir apuntes.
    /// </summary>
    public Task<Resultado> AsignarAsync(byte idFormaPago, string? codigo, ContextoSociedadEjercicio trabajo) =>
        bd.EnTransaccionAsync(async s =>
        {
            var texto = (codigo ?? "").Trim();
            if (texto.Length == 0)
            {
                await s.EjecutarAsync("UPDATE FormasPagoSociedad SET IdIdentidadContable = NULL WHERE IdSociedad = ? AND IdFormaPago = ?", trabajo.IdSociedad, (int)idFormaPago);
                await BorrarVaciaAsync(s, idFormaPago, trabajo.IdSociedad);
                return Resultado.Correcto();
            }
            var cuenta = await s.ConsultarPrimeroAsync("SELECT IdIdentidadContable, EsImputable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                f => (Id: (int?)f.Entero("IdIdentidadContable"), Imputable: f.Logico("EsImputable")), trabajo.IdSociedadEjercicio, texto);
            if (cuenta.Id is not int identidad)
                return Resultado.Fallo(new Error("FormasPago.CuentaNoExiste", $"No existe la cuenta {texto} en el ejercicio {trabajo.Ejercicio}.",
                    $"Cuenta {texto}", "La subcuenta de la forma de pago tiene que existir en el ejercicio de trabajo.",
                    "Revisa el código o da de alta la subcuenta en el plan de cuentas."));
            if (!cuenta.Imputable)
                return Resultado.Fallo(new Error("FormasPago.CuentaNoImputable", $"La cuenta {texto} no admite apuntes.",
                    $"Cuenta {texto}", "Es una cuenta de agrupación.", "Elige una subcuenta, por ejemplo 5720001."));
            var filas = await s.EjecutarAsync("UPDATE FormasPagoSociedad SET IdIdentidadContable = ? WHERE IdSociedad = ? AND IdFormaPago = ?",
                identidad, trabajo.IdSociedad, (int)idFormaPago);
            if (filas == 0)
                await s.EjecutarAsync("INSERT INTO FormasPagoSociedad (IdFormaPago, IdSociedad, IdIdentidadContable) VALUES (?, ?, ?)",
                    (int)idFormaPago, trabajo.IdSociedad, identidad);
            return Resultado.Correcto();
        });

    /// <summary>Cuentas bancarias de la empresa, activas primero y por nombre.</summary>
    public Task<IReadOnlyList<CuentaBancariaEmpresa>> CuentasBancariasAsync(string idSociedad) =>
        bd.EjecutarAsync(s => CuentasBancariasEnSesionAsync(s, idSociedad));

    private static async Task<IReadOnlyList<CuentaBancariaEmpresa>> CuentasBancariasEnSesionAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT IdCuentaBancaria, Nombre, Iban, Activa FROM CuentasBancarias WHERE IdSociedad = ?",
            f => new CuentaBancariaEmpresa(f.Entero("IdCuentaBancaria"), f.Texto("Nombre"), f.Texto("Iban"), f.Logico("Activa")), idSociedad))
        .OrderByDescending(c => c.Activa).ThenBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// Asigna a la forma de pago la cuenta bancaria de la empresa en la que se cobra (facturacion/factura-venta 2026-11-13 12:13);
    /// null la quita. La cuenta tiene que ser de la empresa de trabajo.
    /// </summary>
    public Task<Resultado> AsignarCuentaBancariaAsync(byte idFormaPago, int? idCuentaBancaria, ContextoSociedadEjercicio trabajo) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (idCuentaBancaria is int idBanco)
            {
                var deLaEmpresa = await s.EscalarAsync("SELECT IdSociedad FROM CuentasBancarias WHERE IdCuentaBancaria = ?", idBanco) as string;
                if (!string.Equals(deLaEmpresa, trabajo.IdSociedad, StringComparison.OrdinalIgnoreCase))
                    return Resultado.Fallo(new Error("FormasPago.CuentaBancariaAjena", $"La cuenta bancaria {idBanco} no es de la empresa {trabajo.IdSociedad}.",
                        $"Cuenta bancaria {idBanco}", "Cada forma de pago solo puede cobrarse en una cuenta de la empresa de trabajo.",
                        "Elige una de las cuentas de la lista; las cuentas se dan de alta en Bancos (Norma 43)."));
            }
            var filas = await s.EjecutarAsync("UPDATE FormasPagoSociedad SET IdCuentaBancaria = ? WHERE IdSociedad = ? AND IdFormaPago = ?",
                idCuentaBancaria, trabajo.IdSociedad, (int)idFormaPago);
            if (filas == 0 && idCuentaBancaria is not null)
                await s.EjecutarAsync("INSERT INTO FormasPagoSociedad (IdFormaPago, IdSociedad, IdCuentaBancaria) VALUES (?, ?, ?)",
                    (int)idFormaPago, trabajo.IdSociedad, idCuentaBancaria);
            await BorrarVaciaAsync(s, idFormaPago, trabajo.IdSociedad);
            return Resultado.Correcto();
        });

    /// <summary>Una fila sin subcuenta ni cuenta bancaria no aporta nada: se borra.</summary>
    private static Task BorrarVaciaAsync(ISesionDatos s, byte idFormaPago, string idSociedad) =>
        s.EjecutarAsync("DELETE FROM FormasPagoSociedad WHERE IdSociedad = ? AND IdFormaPago = ? AND IdIdentidadContable IS NULL AND IdCuentaBancaria IS NULL",
            idSociedad, (int)idFormaPago);
}
