using System.Globalization;
using System.Text;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Roles;

/// <summary>Empleado de una sociedad: su rol, su tercero, su código en la gestoría y su cuenta 465.</summary>
public sealed record EmpleadoSociedad(int IdEmpleado, int IdTercero, string Nombre, string? Nif, string IdSociedad, string? CodigoTrabajador,
    int? IdIdentidadContable, string? Cuenta);

/// <summary>Posible empleado para un trabajador del Excel que no se reconoce solo; nunca se asigna sin que el operador lo elija.</summary>
/// <param name="Motivo">Por qué se propone: «mismo NIF», «nombre parecido»…</param>
public sealed record CandidatoEmpleado(int IdTercero, int? IdEmpleado, string Nombre, string? Nif, string Motivo);

/// <summary>Resultado de buscar al trabajador: el empleado si se reconoce sin dudas, o los candidatos para elegir.</summary>
public sealed record ResolucionTrabajador(int? IdEmpleado, IReadOnlyList<CandidatoEmpleado> Candidatos)
{
    public bool Resuelto => IdEmpleado is not null;
}

/// <summary>
/// Rol Empleado (terceros/roles, derivado de nominas 2026-11-13): el tercero al que la empresa paga nóminas, con su código de
/// trabajador en la gestoría y su cuenta 465 en cada sociedad (igual que la cuenta de cliente o de proveedor). Al importar
/// nóminas, un trabajador se reconoce solo por su código en la sociedad o por el NIF de un tercero que ya es empleado de la
/// sociedad; si no, se proponen candidatos (mismo NIF, nombre parecido) y el operador elige o da de alta (RF-NOM-01: no se
/// equipara automáticamente un tercero cualquiera con un empleado).
/// </summary>
public sealed class ServicioEmpleados(IBaseDatos bd, ICatalogoCuentas cuentas, IRepositorioTerceros terceros, Actor actor)
{
    public const string CuentaRemuneraciones = "465";

    /// <summary>Busca al trabajador del Excel en la sociedad (en la sesión de la importación).</summary>
    public static async Task<ResolucionTrabajador> BuscarAsync(ISesionDatos s, string idSociedad, string? codigo, string? nif, string nombre)
    {
        if (!string.IsNullOrWhiteSpace(codigo))
        {
            var porCodigo = await s.ConsultarAsync("SELECT es.IdEmpleado FROM EmpleadosSociedad AS es INNER JOIN Empleados AS e ON es.IdEmpleado = e.IdEmpleado " +
                "WHERE es.IdSociedad = ? AND es.CodigoTrabajador = ? AND e.Activo = True", f => f.Entero("IdEmpleado"), idSociedad, codigo.Trim());
            if (porCodigo.Count == 1) return new(porCodigo[0], []);
        }
        var todos = await s.ConsultarAsync(
            "SELECT t.IdTercero, t.Nombre, t.NIF, e.IdEmpleado, es.IdSociedad FROM (Terceros AS t LEFT JOIN Empleados AS e ON t.IdTercero = e.IdTercero) " +
            "LEFT JOIN EmpleadosSociedad AS es ON (e.IdEmpleado = es.IdEmpleado AND es.IdSociedad = ?) WHERE t.Activo = True",
            f => (IdTercero: f.Entero("IdTercero"), Nombre: f.Texto("Nombre") ?? "", Nif: f.Texto("NIF"), IdEmpleado: f.EnteroNulo("IdEmpleado"),
                EnSociedad: f.Texto("IdSociedad") is not null), idSociedad);
        var nifNormal = Nif.Normalizar(nif ?? "");
        var candidatos = new List<CandidatoEmpleado>();
        if (nifNormal.Length > 0)
        {
            var mismos = todos.Where(t => Nif.Normalizar(t.Nif ?? "") == nifNormal).ToList();
            var empleadosAqui = mismos.Where(t => t.EnSociedad).Select(t => t.IdEmpleado!.Value).Distinct().ToList();
            if (empleadosAqui.Count == 1) return new(empleadosAqui[0], []);
            candidatos.AddRange(mismos.Select(t => new CandidatoEmpleado(t.IdTercero, t.IdEmpleado, t.Nombre, t.Nif, "mismo NIF")));
        }
        var buscadas = Palabras(nombre);
        if (buscadas.Count > 0)
            foreach (var t in todos.Where(t => candidatos.All(c => c.IdTercero != t.IdTercero)))
            {
                var comunes = Palabras(t.Nombre).Intersect(buscadas).Count();
                if (comunes >= Math.Min(2, buscadas.Count) && comunes * 2 >= buscadas.Count)
                    candidatos.Add(new CandidatoEmpleado(t.IdTercero, t.IdEmpleado, t.Nombre, t.Nif, t.EnSociedad ? "nombre parecido, ya es empleado" : "nombre parecido"));
            }
        return new(null, candidatos.OrderByDescending(c => c.Motivo == "mismo NIF").ThenBy(c => c.Nombre, StringComparer.CurrentCulture).Take(10).ToList());
    }

    /// <summary>Empleados de la sociedad, con su cuenta 465 en el ejercicio de trabajo.</summary>
    public async Task<IReadOnlyList<EmpleadoSociedad>> ListarAsync(ContextoSociedadEjercicio ctx)
    {
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT e.IdEmpleado, e.IdTercero, t.Nombre, t.NIF, es.CodigoTrabajador, es.IdIdentidadContable FROM (EmpleadosSociedad AS es " +
            "INNER JOIN Empleados AS e ON es.IdEmpleado = e.IdEmpleado) INNER JOIN Terceros AS t ON e.IdTercero = t.IdTercero WHERE es.IdSociedad = ? ORDER BY t.Nombre",
            f => (IdEmpleado: f.Entero("IdEmpleado"), IdTercero: f.Entero("IdTercero"), Nombre: f.Texto("Nombre") ?? "", Nif: f.Texto("NIF"),
                Codigo: f.Texto("CodigoTrabajador"), Identidad: f.EnteroNulo("IdIdentidadContable")), ctx.IdSociedad));
        var r = new List<EmpleadoSociedad>();
        foreach (var f in filas)
            r.Add(new(f.IdEmpleado, f.IdTercero, f.Nombre, f.Nif, ctx.IdSociedad, f.Codigo, f.Identidad,
                f.Identidad is int i ? (await cuentas.CuentaDeIdentidadAsync(ctx, i) ?? await cuentas.UltimaCuentaDeIdentidadAsync(i))?.Codigo : null));
        return r;
    }

    /// <summary>
    /// Da al tercero el rol Empleado en la sociedad con su código de trabajador (si lo trae el Excel) y, si aún no tiene cuenta
    /// 465 en la sociedad, le crea la siguiente libre con su nombre. Repetirlo no duplica nada.
    /// </summary>
    public async Task<Resultado<EmpleadoSociedad>> AsignarAsync(ContextoSociedadEjercicio ctx, int idTercero, string? codigoTrabajador)
    {
        var tercero = await terceros.ObtenerAsync(idTercero);
        if (tercero is null)
            return Resultado<EmpleadoSociedad>.Fallo(new Error("Empleados.SinTercero", "El tercero no existe.", $"Tercero {idTercero}", null,
                "Actualiza la lista de candidatos o da de alta al trabajador."));
        var codigo = string.IsNullOrWhiteSpace(codigoTrabajador) ? null : codigoTrabajador.Trim();
        var conflicto = codigo is null ? null : await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT t.Nombre FROM (EmpleadosSociedad AS es INNER JOIN Empleados AS e ON es.IdEmpleado = e.IdEmpleado) INNER JOIN Terceros AS t ON e.IdTercero = t.IdTercero " +
            "WHERE es.IdSociedad = ? AND es.CodigoTrabajador = ? AND e.IdTercero <> ?", f => f.Texto("Nombre"), ctx.IdSociedad, codigo, idTercero));
        if (conflicto is not null)
            return Resultado<EmpleadoSociedad>.Fallo(new Error("Empleados.CodigoRepetido", $"El código de trabajador {codigo} ya es de {conflicto} en esta sociedad.",
                $"Tercero {idTercero}", "Cada trabajador tiene un código propio en la gestoría.", $"Comprueba con la gestoría si {conflicto} y {tercero.Nombre} son la misma persona."));

        var (idEmpleado, identidad) = await bd.EnTransaccionAsync(async s =>
        {
            var id = await s.ConsultarPrimeroAsync("SELECT IdEmpleado FROM Empleados WHERE IdTercero = ?", f => (int?)f.Entero("IdEmpleado"), idTercero);
            if (id is int existente) await s.EjecutarAsync("UPDATE Empleados SET Activo = True WHERE IdEmpleado = ?", existente);
            else
            {
                await s.EjecutarAsync("INSERT INTO Empleados (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", idTercero, true, DateTime.Today);
                id = await s.UltimoIdAsync();
            }
            var fila = await s.ConsultarPrimeroAsync("SELECT IdEmpleadoSociedad, CodigoTrabajador, IdIdentidadContable FROM EmpleadosSociedad WHERE IdEmpleado = ? AND IdSociedad = ?",
                f => (Id: (int?)f.Entero("IdEmpleadoSociedad"), Codigo: f.Texto("CodigoTrabajador"), Identidad: f.EnteroNulo("IdIdentidadContable")), id, ctx.IdSociedad);
            if (fila.Id is int idFila)
                await s.EjecutarAsync("UPDATE EmpleadosSociedad SET CodigoTrabajador = ?, Activo = True WHERE IdEmpleadoSociedad = ?", codigo ?? fila.Codigo, idFila);
            else
                await s.EjecutarAsync("INSERT INTO EmpleadosSociedad (IdEmpleado, IdSociedad, CodigoTrabajador, Activo) VALUES (?, ?, ?, ?)", id, ctx.IdSociedad, codigo, true);
            return (id!.Value, fila.Identidad);
        });

        var cuenta = identidad is int i ? await cuentas.CuentaDeIdentidadAsync(ctx, i) ?? await cuentas.UltimaCuentaDeIdentidadAsync(i) : null;
        if (identidad is null)
        {
            var nueva = await Cuenta465Async(ctx, tercero.Nombre);
            if (!nueva.EsCorrecto) return Resultado<EmpleadoSociedad>.Fallo(nueva.Errores);
            cuenta = nueva.Valor;
            identidad = cuenta.IdIdentidadContable;
            await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE EmpleadosSociedad SET IdIdentidadContable = ? WHERE IdEmpleado = ? AND IdSociedad = ?",
                identidad, idEmpleado, ctx.IdSociedad));
        }
        return Resultado<EmpleadoSociedad>.Correcto(new(idEmpleado, idTercero, tercero.Nombre, tercero.Nif, ctx.IdSociedad, codigo, identidad, cuenta?.Codigo));
    }

    /// <summary>
    /// Da de alta al trabajador del Excel como tercero (persona física) con el rol Empleado, su código y una cuenta 465 nueva.
    /// El nombre «APELLIDOS, NOMBRE» de la gestoría se guarda como nombre y apellidos.
    /// </summary>
    public async Task<Resultado<EmpleadoSociedad>> AltaAsync(ContextoSociedadEjercicio ctx, string nombreOrigen, string? nif, string? codigoTrabajador)
    {
        var (nombre, apellido1, apellido2) = PartirNombre(nombreOrigen);
        if (nombre.Length == 0)
            return Resultado<EmpleadoSociedad>.Fallo(new Error("Empleados.SinNombre", "Falta el nombre del trabajador.", codigoTrabajador, null, "Revisa la fila del Excel."));
        var nifNormal = Nif.Normalizar(nif ?? "");
        var t = new Tercero
        {
            TipoPersona = TipoPersona.Fisica,
            NombrePersona = nombre,
            PrimerApellido = apellido1,
            SegundoApellido = apellido2,
            Nif = nifNormal.Length == 0 ? null : nifNormal,
        };
        var guardado = await terceros.GuardarAsync(t, actor);
        if (!guardado.EsCorrecto) return Resultado<EmpleadoSociedad>.Fallo(guardado.Errores);
        return await AsignarAsync(ctx, guardado.Valor.IdTercero, codigoTrabajador);
    }

    /// <summary>«NAVARRO GIL, LUCIA» → (Lucía…, Navarro, Gil); sin coma, todo el texto es el nombre.</summary>
    public static (string Nombre, string? Apellido1, string? Apellido2) PartirNombre(string? texto)
    {
        var t = (texto ?? "").Trim();
        var coma = t.IndexOf(',');
        if (coma < 0) return (Capitalizar(t), null, null);
        var apellidos = t[..coma].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nombre = Capitalizar(t[(coma + 1)..].Trim());
        return apellidos.Length switch
        {
            0 => (nombre, null, null),
            1 => (nombre, Capitalizar(apellidos[0]), null),
            2 => (nombre, Capitalizar(apellidos[0]), Capitalizar(apellidos[1])),
            // «VAN DER BERG LOPEZ»: el último es el segundo apellido.
            _ => (nombre, Capitalizar(string.Join(' ', apellidos[..^1])), Capitalizar(apellidos[^1])),
        };
    }

    private static string Capitalizar(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Length <= 1 ? p.ToUpperInvariant()
            : char.ToUpper(p[0], CultureInfo.GetCultureInfo("es-ES")) + p[1..].ToLower(CultureInfo.GetCultureInfo("es-ES"))));

    private async Task<Resultado<CuentaContable>> Cuenta465Async(ContextoSociedadEjercicio ctx, string nombre)
    {
        var codigo = await cuentas.SiguienteSubcuentaAsync(ctx, CuentaRemuneraciones);
        if (codigo is null)
            return Resultado<CuentaContable>.Fallo(new Error("Empleados.SinCodigos", $"No quedan códigos libres en la cuenta {CuentaRemuneraciones}.", nombre, null,
                "Asígnale la cuenta 465 a mano en la ficha del tercero (Roles › Empleado)."));
        var padre = await cuentas.ResolverAsync(ctx, CuentaRemuneraciones);
        var texto = nombre.Length > ReglasCuenta.LongitudMaximaNombre ? nombre[..ReglasCuenta.LongitudMaximaNombre] : nombre;
        return await cuentas.AltaAsync(ctx, new AltaCuenta(codigo, texto, padre.EsCorrecto ? padre.Valor.IdCuentaContable : null));
    }

    /// <summary>Palabras significativas de un nombre: sin acentos ni puntuación, en mayúsculas y de más de dos letras.</summary>
    internal static HashSet<string> Palabras(string? texto)
    {
        var d = (texto ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetter(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 2).ToHashSet();
    }
}
