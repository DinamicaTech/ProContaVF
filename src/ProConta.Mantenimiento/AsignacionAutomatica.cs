namespace ProConta.Mantenimiento;

/// <summary>
/// Una cuenta que «Auto» rellena: la subcuenta y si hay que darla de alta porque aún no está en el plan. <paramref name="YaPuesta"/>: el campo
/// ya tenía ese código pero la subcuenta no existe en el plan del ejercicio; «Auto» solo la da de alta, sin cambiar el código.
/// </summary>
public sealed record CuentaAsignada(CampoMantenimiento Campo, string Codigo, string Nombre, bool Crear, bool YaPuesta = false);

/// <summary>Una cuenta vacía que «Auto» deja como está porque no tiene un único candidato claro, con el motivo.</summary>
public sealed record CuentaSinCandidato(CampoMantenimiento Campo, string Motivo);

/// <summary>Lo que hará «Auto» con las cuentas vacías del registro y con las puestas que no existen en el plan.</summary>
public sealed record PropuestaAsignacion(IReadOnlyList<CuentaAsignada> Asignadas, IReadOnlyList<CuentaSinCandidato> SinCandidato)
{
    public bool NadaQueHacer => Asignadas.Count == 0 && SinCandidato.Count == 0;
}

/// <summary>
/// Botón «Auto» de la pantalla de mantenimiento (plataforma/mantenimiento 2026-11-18, propietario 2026-11-18 04:59): asigna las cuentas
/// vacías del registro y crea la subcuenta si hace falta, pero solo cuando hay un único candidato claro. Nunca cambia una cuenta ya puesta;
/// si la puesta no existe en el plan del ejercicio, la da de alta con el mismo código (cuentas siempre materializadas, propietario 2026-11-18 05:10).
/// <list type="bullet">
/// <item>Con cuenta habitual (<see cref="CampoMantenimiento.CuentaHabitual"/>): es el candidato; si no está en el plan, se crea con su nombre.</item>
/// <item>Sin ella: la única subcuenta de los grupos del campo cuyo nombre cumple <see cref="CampoMantenimiento.PalabrasCuenta"/>;
/// con ninguna o con varias, la cuenta se queda vacía y se dice por qué.</item>
/// </list>
/// </summary>
public static class AsignacionAutomatica
{
    public static PropuestaAsignacion Proponer(IEnumerable<CampoMantenimiento> campos, RegistroMantenimiento registro, IReadOnlyList<CuentaPlan> plan)
    {
        var asignadas = new List<CuentaAsignada>();
        var dudosas = new List<CuentaSinCandidato>();
        foreach (var c in campos.Where(c => c.TieneAsignacionAutomatica && !c.SoloLectura))
        {
            if (registro.Texto(c.Clave) is string puesta)
            {
                // Con el plan sin leer no se sabe si existe: no se propone nada.
                if (plan.Count > 0 && plan.All(p => p.Codigo != puesta.Trim()))
                    asignadas.Add(new CuentaAsignada(c, puesta.Trim(), puesta.Trim() == c.CuentaHabitual ? c.NombreCuentaHabitual ?? c.Etiqueta : c.Etiqueta, true, YaPuesta: true));
                continue;
            }
            if (c.CuentaHabitual is string habitual)
            {
                var existente = plan.FirstOrDefault(p => p.Codigo == habitual);
                asignadas.Add(existente is null
                    ? new CuentaAsignada(c, habitual, c.NombreCuentaHabitual ?? c.Etiqueta, true)
                    : new CuentaAsignada(c, habitual, existente.Nombre, false));
                continue;
            }
            var candidatas = Candidatas(c, plan);
            if (candidatas.Count == 1) asignadas.Add(new CuentaAsignada(c, candidatas[0].Codigo, candidatas[0].Nombre, false));
            else dudosas.Add(new CuentaSinCandidato(c, candidatas.Count == 0
                ? $"ninguna subcuenta de {Grupos(c)} encaja por su nombre: elígela o créala con la lupa."
                : $"encajan {candidatas.Count} subcuentas ({string.Join(", ", candidatas.Select(x => x.Codigo))}): elige la buena con la lupa."));
        }
        return new PropuestaAsignacion(asignadas, dudosas);
    }

    /// <summary>Subcuentas de los grupos del campo cuyo nombre cumple todas sus condiciones de palabras.</summary>
    public static IReadOnlyList<CuentaPlan> Candidatas(CampoMantenimiento campo, IReadOnlyList<CuentaPlan> plan) =>
        campo.PalabrasCuenta.Count == 0 ? []
            : plan.Where(p => EnGrupos(campo, p.Codigo) && Encaja(campo, p.Nombre)).OrderBy(p => p.Codigo, StringComparer.Ordinal).ToList();

    public static bool EnGrupos(CampoMantenimiento campo, string codigo) => campo.GruposCuenta.Count == 0 || campo.GruposCuenta.Any(g => codigo.StartsWith(g, StringComparison.Ordinal));

    public static bool Encaja(CampoMantenimiento campo, string nombre)
    {
        var plano = ServicioMantenimiento.Plano(nombre);
        return campo.PalabrasCuenta.Count > 0 && campo.PalabrasCuenta.All(alternativas => alternativas.Any(p => plano.Contains(ServicioMantenimiento.Plano(p), StringComparison.Ordinal)));
    }

    private static string Grupos(CampoMantenimiento c) => c.GruposCuenta.Count == 0 ? "el plan" : string.Join(", ", c.GruposCuenta);
}
