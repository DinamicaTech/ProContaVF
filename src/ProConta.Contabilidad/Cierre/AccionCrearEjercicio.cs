using System.Globalization;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Cierre;

/// <summary>
/// Acción «Crear el ejercicio siguiente…» de Parámetros y tablas › Ejercicios (contabilidad/procesos/nuevo-ejercicio, propietario
/// 2026-11-17 15:38, punto 2: «En las dos»): sobre el ejercicio elegido, propone lo que se copia y crea el siguiente con
/// <see cref="ServicioNuevoEjercicio"/>, igual que el botón de Contabilidad › Cierre del ejercicio.
/// </summary>
public static class AccionCrearEjercicio
{
    public const string Clave = "crear-siguiente";

    /// <summary>Añade la acción al mantenimiento de ejercicios.</summary>
    public static void Conectar(ProveedorTablaAccess ejercicios, ServicioNuevoEjercicio servicio)
    {
        ejercicios.AccionesExtra = [.. ejercicios.AccionesExtra,
            new AccionMantenimiento(Clave, "Crear el ejercicio siguiente…",
                "Crea el ejercicio siguiente al elegido copiando sus parámetros, el plan de cuentas, las series de facturación y la configuración de correo, con el trimestre activo en el 1.º. Repetirlo no duplica nada.",
                Modifica: true, ConPropuesta: true)];
        ejercicios.ProponerAccion = async (_, r, c) =>
        {
            if (Contexto(r, c) is not { } ctx) return Sin(r);
            var p = await servicio.PrevisionAsync(ctx);
            return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(
                p.Existe ? $"El ejercicio {p.Ejercicio} ya existe: solo se añade lo que le falte (cuentas, series y correo)." : $"Se crea el ejercicio {p.Ejercicio} copiando el {ctx.Ejercicio}.",
                null, Detalle(p, ctx.Ejercicio)));
        };
        ejercicios.EjecutarAccion = async (_, r, c) =>
        {
            if (Contexto(r, c) is not { } ctx) return Sin(r);
            var hecho = await servicio.CrearSiguienteAsync(ctx);
            if (!hecho.EsCorrecto) return Resultado<ResultadoAccion>.Fallo(hecho.Errores);
            var x = hecho.Valor;
            return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(
                $"Ejercicio {x.Ejercicio.Ejercicio} {(x.Creado ? "creado" : "completado")}: {x.CuentasCopiadas} cuentas y {x.SeriesCopiadas} series copiadas" +
                (x.OtrasTablas.Count > 0 ? $", y {string.Join(", ", x.OtrasTablas)}." : "."),
                x.Ejercicio.IdSociedadEjercicio));
        };
    }

    /// <summary>Lo que se copia, para la confirmación (también la usa la pantalla del cierre).</summary>
    public static IReadOnlyList<string> Detalle(PrevisionNuevoEjercicio p, int ejercicio) =>
    [
        "Parámetros del ejercicio: cuentas, formas de pago, tipos, retenciones, cuenta de resultados e Impuesto sobre Sociedades.",
        $"Plan de cuentas: {p.Cuentas} cuentas, con las mismas identidades y sin saldos.",
        p.Series == 0 ? "Series de facturación: no hay ninguna."
            : $"Series de facturación: {p.Series} ({string.Join(", ", p.NombresSeries)}). Las que llevan el año en el prefijo empiezan en 1; las demás, en {ServicioNuevoEjercicio.PrimerNumero(p.Ejercicio)}.",
        p.Prorrata is decimal pr
            ? $"Prorrata provisional de {p.Ejercicio}: {pr.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"))} % ({(p.DefinitivaFijada ? "la definitiva" : "la provisional, porque la definitiva aún no está fijada")} de {ejercicio})."
            : "Prorrata: sin informar.",
        "Trimestre activo: 1.º. La configuración de correo, si el ejercicio nuevo no tiene.",
    ];

    private static ContextoSociedadEjercicio? Contexto(RegistroMantenimiento r, ContextoMantenimiento c) =>
        r.Clave is not null && int.TryParse(r.Texto("Ejercicio"), NumberStyles.None, CultureInfo.InvariantCulture, out var anio)
            ? new ContextoSociedadEjercicio(c.Trabajo.IdSociedad, Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture), anio)
            : null;

    private static Resultado<ResultadoAccion> Sin(RegistroMantenimiento r) =>
        Resultado<ResultadoAccion>.Fallo(new Error("NuevoEjercicio.SinEjercicio", "Guarda antes el ejercicio.", "Ejercicios", "La acción copia un ejercicio ya guardado.", "Guárdalo y vuelve a pulsar la acción."));
}
