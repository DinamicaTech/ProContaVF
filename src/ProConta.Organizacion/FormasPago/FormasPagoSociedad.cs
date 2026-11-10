using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.FormasPago;

/// <summary>
/// Forma de pago vista desde una empresa: la subcuenta que usa (tabla FormasPagoSociedad, organizacion/formas-pago
/// 2026-11-06). Se guarda la identidad contable y se muestra el código del ejercicio de trabajo; si la cuenta no existe en
/// ese ejercicio, el último código que tuvo y <c>CuentaEnEjercicio</c> = false.
/// </summary>
public sealed record FormaPagoSociedad(byte IdFormaPago, string Descripcion, int? IdIdentidadContable, string? Cuenta, string? NombreCuenta, bool CuentaEnEjercicio);

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
            var asignadas = (await s.ConsultarAsync("SELECT IdFormaPago, IdIdentidadContable FROM FormasPagoSociedad WHERE IdSociedad = ?",
                f => ((byte)f.Entero("IdFormaPago"), f.EnteroNulo("IdIdentidadContable")), trabajo.IdSociedad))
                .ToDictionary(x => x.Item1, x => x.Item2);
            var r = new List<FormaPagoSociedad>(formas.Count);
            foreach (var (id, descripcion) in formas)
            {
                if (!asignadas.TryGetValue(id, out var identidad) || identidad is not int ident)
                {
                    r.Add(new(id, descripcion, null, null, null, false));
                    continue;
                }
                var enEjercicio = await s.ConsultarPrimeroAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
                    f => (f.Texto("Codigo"), f.Texto("Nombre")), trabajo.IdSociedadEjercicio, ident);
                if (enEjercicio.Item1 is not null) { r.Add(new(id, descripcion, ident, enEjercicio.Item1, enEjercicio.Item2, true)); continue; }
                var ultima = await s.ConsultarPrimeroAsync("SELECT TOP 1 Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable = ? ORDER BY IdSociedadEjercicio DESC",
                    f => (f.Texto("Codigo"), f.Texto("Nombre")), ident);
                r.Add(new(id, descripcion, ident, ultima.Item1, ultima.Item2, false));
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
                await s.EjecutarAsync("DELETE FROM FormasPagoSociedad WHERE IdSociedad = ? AND IdFormaPago = ?", trabajo.IdSociedad, (int)idFormaPago);
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
}
