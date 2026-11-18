using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Mantenimiento;

namespace ProConta.Contabilidad.Cierre;

/// <summary>
/// «Auto» sin pantalla (organizacion/sociedades-ejercicios 2026-11-18): asigna las cuentas vacías de un ejercicio con la misma regla que el
/// botón de Parámetros y tablas › Ejercicios (la habitual, creándola si no existe, o la única que encaja) y deja vacías las dudosas.
/// Lo usa «Crear el ejercicio siguiente» para que el ejercicio nuevo nazca con todas sus cuentas asignadas.
/// </summary>
public sealed class AsignacionCuentasEjercicio(IBaseDatos bd, ICatalogoCuentas cuentas)
{
    /// <summary>Asigna las cuentas vacías y devuelve qué ha hecho, una línea por cuenta («IVA · A ingresar → 4750000»).</summary>
    public async Task<IReadOnlyList<string>> AsignarAsync(ContextoSociedadEjercicio ctx)
    {
        var columnas = await bd.EjecutarAsync(s => Task.FromResult(CuentasEjercicio.Todas.Where(c => s.LeerEsquema().ExisteColumna("SociedadesEjercicios", c.Columna)).ToList()));
        if (columnas.Count == 0) return [];
        var registro = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT {string.Join(", ", columnas.Select(c => c.Columna))} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => new RegistroMantenimiento(ctx.IdSociedadEjercicio, columnas.ToDictionary(c => c.Columna, c => (object?)f.Texto(c.Columna))), ctx.IdSociedadEjercicio));
        if (registro is null) return [];
        var plan = (await cuentas.ListarAsync(ctx)).Where(c => c.EsImputable || c.Codigo.Trim().Length >= 7).Select(c => new CuentaPlan(c.Codigo.Trim(), c.Nombre.Trim())).ToList();
        var propuesta = AsignacionAutomatica.Proponer(columnas.Select(CuentasEjercicio.Campo), registro, plan);
        var hecho = new List<string>();
        foreach (var a in propuesta.Asignadas)
        {
            if (a.Crear && !(await cuentas.AltaOTraerSubcuentaAsync(ctx, a.Codigo, a.Nombre)).EsCorrecto) continue;
            if (a.YaPuesta) { hecho.Add($"{CuentasEjercicio.De(a.Campo.Clave).TituloPestana} · {CuentasEjercicio.De(a.Campo.Clave).Etiqueta}: {a.Codigo} dada de alta"); continue; }
            await bd.EjecutarAsync(s => s.EjecutarAsync($"UPDATE SociedadesEjercicios SET {a.Campo.Clave} = ? WHERE IdSociedadEjercicio = ?", a.Codigo, ctx.IdSociedadEjercicio));
            var c = CuentasEjercicio.De(a.Campo.Clave);
            hecho.Add(string.Create(CultureInfo.InvariantCulture, $"{c.TituloPestana} · {c.Etiqueta} → {a.Codigo}{(a.Crear ? " (creada)" : "")}"));
        }
        return hecho;
    }
}
