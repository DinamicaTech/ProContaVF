using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Activos.Subcuentas;

/// <summary>Activo que puede recibir subcuentas (de la sociedad de trabajo y sin fecha de baja).</summary>
public sealed record ActivoParaSubcuentas(int IdActivo, int IdTipoActivo, string Nombre, int? IdCentroCoste, bool EsAjeno = false);

/// <summary>Línea de plantilla de un tipo de activo con el nombre y la descripción de su tipo de subcuenta.</summary>
public sealed record LineaParaSubcuentas(int IdPlantilla, int IdTipoActivo, string Prefijo, int Orden, string NombreTipoSubcuenta, string? Descripcion, bool EstaActiva = true);

/// <summary>Subcuenta ya generada de un activo y línea, con su cuenta en el ejercicio de trabajo si la tiene.</summary>
public sealed record SubcuentaActual(int IdActivo, int IdPlantilla, int IdIdentidadContable, int? IdCuentaContable, string? Codigo, string? Nombre);

public enum AccionSubcuenta
{
    /// <summary>El activo no tiene subcuenta en esa línea: se crea con el siguiente código libre tras el prefijo.</summary>
    Crear,
    /// <summary>La subcuenta existe en otro ejercicio pero no en el de trabajo: se da de alta con su código.</summary>
    AltaEnEjercicio,
    /// <summary>La subcuenta existe con otro nombre: se renombra.</summary>
    Renombrar
}

/// <summary>Lo que hará «Generar subcuentas» con un activo y una línea de su plantilla.</summary>
public sealed record OperacionSubcuenta(AccionSubcuenta Accion, ActivoParaSubcuentas Activo, LineaParaSubcuentas Linea, string NombreNuevo, SubcuentaActual? Actual)
{
    /// <summary>Renombre con un parecido menor del 65 %: se marca en la propuesta para revisarlo (activos 2026-11-12 11:45, B).</summary>
    public bool MuyDistinto => Accion == AccionSubcuenta.Renombrar && ReglasSubcuentaActivo.MuyDistinto(Actual?.Nombre, NombreNuevo);

    public string Texto => Accion switch
    {
        AccionSubcuenta.Crear => $"Crea una {Linea.Prefijo}… «{NombreNuevo}»",
        AccionSubcuenta.AltaEnEjercicio => $"Da de alta en el ejercicio la {Actual?.Codigo ?? "subcuenta"} «{NombreNuevo}»",
        _ => $"{(MuyDistinto ? "⚠ Muy distinto: " : "")}renombra la {Actual?.Codigo} «{Actual?.Nombre}» → «{NombreNuevo}»",
    };
}

/// <summary>Resultado de la generación: lo hecho y lo que no se ha podido hacer, con el motivo.</summary>
public sealed record ResultadoGeneracionSubcuentas(int Creadas, int DadasDeAlta, int Renombradas, IReadOnlyList<string> Errores);

/// <summary>Subcuenta de una línea de plantilla que nunca ha tenido apuntes (se puede borrar, activos 2026-11-12 11:45, C).</summary>
public sealed record SubcuentaSinApuntes(int IdSubcuentaActivo, int IdIdentidadContable, string Activo, string? Codigo, string? Nombre);

/// <summary>
/// «Generar subcuentas» (activos 2026-11-12 11:45): para cada activo no ajeno de los tipos pedidos y cada línea activa de su
/// plantilla, en el ejercicio de trabajo, crea la subcuenta que falta, da de alta la que solo existe en otro ejercicio y
/// renombra la que tiene otro nombre. Primero se propone (sin cambiar nada) y después se ejecuta; lanzarlo dos veces seguidas
/// no hace nada la segunda.
/// </summary>
public sealed class GeneradorSubcuentasActivo(IBaseDatos bd, ICatalogoCuentas cuentas)
{
    /// <summary>Qué hará con los activos de esos tipos (puro, sin BD).</summary>
    public static IReadOnlyList<OperacionSubcuenta> Planificar(IEnumerable<ActivoParaSubcuentas> activos, IEnumerable<LineaParaSubcuentas> lineas, IEnumerable<SubcuentaActual> actuales)
    {
        var porTipo = lineas.Where(l => l.EstaActiva).GroupBy(l => l.IdTipoActivo)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Orden).ThenBy(l => l.IdPlantilla).ToList());
        var actual = actuales.GroupBy(a => (a.IdActivo, a.IdPlantilla)).ToDictionary(g => g.Key, g => g.First());
        var operaciones = new List<OperacionSubcuenta>();
        foreach (var activo in activos.Where(a => !a.EsAjeno).OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.IdActivo))
        {
            if (!porTipo.TryGetValue(activo.IdTipoActivo, out var deTipo)) continue;
            foreach (var linea in deTipo)
            {
                var nombre = ReglasSubcuentaActivo.Nombre(linea.NombreTipoSubcuenta, activo.Nombre);
                if (!actual.TryGetValue((activo.IdActivo, linea.IdPlantilla), out var a))
                    operaciones.Add(new(AccionSubcuenta.Crear, activo, linea, nombre, null));
                else if (a.IdCuentaContable is null)
                    operaciones.Add(new(AccionSubcuenta.AltaEnEjercicio, activo, linea, nombre, a));
                else if (!string.Equals((a.Nombre ?? "").Trim(), nombre, StringComparison.Ordinal))
                    operaciones.Add(new(AccionSubcuenta.Renombrar, activo, linea, nombre, a));
            }
        }
        return operaciones;
    }

    /// <summary>Propuesta para los activos de esos tipos en la sociedad y el ejercicio de trabajo.</summary>
    public async Task<IReadOnlyList<OperacionSubcuenta>> ProponerAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsTipoActivo)
    {
        if (idsTipoActivo.Count == 0) return [];
        var (activos, lineas, actuales) = await LeerAsync(contexto, idsTipoActivo);
        return Planificar(activos, lineas, actuales);
    }

    /// <summary>Vuelve a calcular la propuesta y la aplica; cada subcuenta va en su propia transacción.</summary>
    public async Task<ResultadoGeneracionSubcuentas> GenerarAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsTipoActivo)
    {
        var operaciones = await ProponerAsync(contexto, idsTipoActivo);
        int creadas = 0, altas = 0, renombradas = 0;
        var errores = new List<string>();
        foreach (var op in operaciones)
        {
            var donde = $"{op.Activo.Nombre} · {op.NombreNuevo}";
            switch (op.Accion)
            {
                case AccionSubcuenta.Crear:
                    var codigo = await cuentas.SiguienteSubcuentaAsync(contexto, op.Linea.Prefijo);
                    if (codigo is null) { errores.Add($"{donde}: no quedan códigos libres tras el prefijo {op.Linea.Prefijo}."); break; }
                    var alta = await cuentas.AltaSubcuentaAsync(contexto, new AltaCuenta(codigo, op.NombreNuevo, IdCentroCostePredeterminado: op.Activo.IdCentroCoste,
                        Descripcion: op.Linea.Descripcion));
                    if (!alta.EsCorrecto) { errores.Add($"{donde}: {alta.Error!.TextoOperador()}"); break; }
                    try
                    {
                        await bd.EjecutarAsync(s => s.EjecutarAsync(
                            "INSERT INTO SubcuentasActivo (IdActivo, IdPlantillaSubcuentaActivo, IdIdentidadContable, FechaHoraCreacion) VALUES (?, ?, ?, ?)",
                            op.Activo.IdActivo, op.Linea.IdPlantilla, alta.Valor.IdIdentidadContable, DateTime.Now));
                        creadas++;
                    }
                    catch (ExcepcionDatos e)
                    {
                        // Sin el enlace con el activo, la cuenta recién creada sobraría: se quita para no dejar residuos.
                        await cuentas.EliminarSinApuntesAsync(contexto, alta.Valor.IdIdentidadContable);
                        errores.Add($"{donde}: {e.Error.TextoOperador()}");
                    }
                    break;
                case AccionSubcuenta.AltaEnEjercicio:
                    var enEjercicio = await cuentas.AltaEnEjercicioAsync(contexto, op.Actual!.IdIdentidadContable, nombre: op.NombreNuevo);
                    if (enEjercicio.EsCorrecto) altas++;
                    else errores.Add($"{donde}: {enEjercicio.Error!.TextoOperador()}");
                    break;
                case AccionSubcuenta.Renombrar:
                    var renombre = await cuentas.CambiarNombreAsync(op.Actual!.IdCuentaContable!.Value, op.NombreNuevo);
                    if (renombre.EsCorrecto) renombradas++;
                    else errores.Add($"{donde}: {renombre.Error!.TextoOperador()}");
                    break;
            }
        }
        return new ResultadoGeneracionSubcuentas(creadas, altas, renombradas, errores);
    }

    /// <summary>Subcuentas de una línea de plantilla, de los activos de la sociedad de trabajo, que nunca han tenido apuntes.</summary>
    public Task<IReadOnlyList<SubcuentaSinApuntes>> SinApuntesAsync(ContextoSociedadEjercicio contexto, int idPlantilla) =>
        bd.EjecutarAsync<IReadOnlyList<SubcuentaSinApuntes>>(async s =>
        {
            var subcuentas = await s.ConsultarAsync(
                "SELECT sa.IdSubcuentaActivo, sa.IdIdentidadContable, a.Nombre FROM SubcuentasActivo AS sa INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo " +
                "WHERE sa.IdPlantillaSubcuentaActivo = ? AND a.IdSociedad = ? ORDER BY a.Nombre",
                f => (Id: f.Entero("IdSubcuentaActivo"), Identidad: f.Entero("IdIdentidadContable"), Activo: f.Texto("Nombre")?.Trim() ?? ""), idPlantilla, contexto.IdSociedad);
            var sinApuntes = new List<SubcuentaSinApuntes>();
            foreach (var sub in subcuentas)
            {
                var cuentasIdentidad = await s.ConsultarAsync(
                    "SELECT c.Codigo, c.Nombre, c.SaldoInicial FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                    "WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC",
                    f => (Codigo: f.Texto("Codigo")?.Trim(), Nombre: f.Texto("Nombre")?.Trim(), Inicial: f.Decimal("SaldoInicial")), sub.Identidad);
                if (cuentasIdentidad.Any(c => c.Inicial != 0m)) continue;
                var apuntes = Convert.ToInt32(await s.EscalarAsync(
                    "SELECT COUNT(*) FROM (Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento) INNER JOIN CuentasContables AS c " +
                    "ON (a.IdSociedadEjercicio = c.IdSociedadEjercicio AND ap.CuentaContable = c.Codigo) WHERE c.IdIdentidadContable = ?", sub.Identidad), CultureInfo.InvariantCulture);
                if (apuntes > 0) continue;
                var ultima = cuentasIdentidad.FirstOrDefault();
                sinApuntes.Add(new SubcuentaSinApuntes(sub.Id, sub.Identidad, sub.Activo, ultima.Codigo, ultima.Nombre));
            }
            return sinApuntes;
        });

    /// <summary>Borra las subcuentas sin apuntes de una línea: la cuenta en todos los ejercicios, su código reservado y su enlace con el activo.</summary>
    public async Task<(int Borradas, IReadOnlyList<string> Errores)> BorrarSinApuntesAsync(ContextoSociedadEjercicio contexto, int idPlantilla)
    {
        var borradas = 0;
        var errores = new List<string>();
        foreach (var sub in await SinApuntesAsync(contexto, idPlantilla))
        {
            var r = await cuentas.EliminarSinApuntesAsync(contexto, sub.IdIdentidadContable,
                s => s.EjecutarAsync("DELETE FROM SubcuentasActivo WHERE IdSubcuentaActivo = ?", sub.IdSubcuentaActivo));
            if (r.EsCorrecto) borradas++;
            else errores.Add($"{sub.Activo} · {sub.Codigo} {sub.Nombre}: {r.Error!.TextoOperador()}");
        }
        return (borradas, errores);
    }

    private Task<(IReadOnlyList<ActivoParaSubcuentas>, IReadOnlyList<LineaParaSubcuentas>, IReadOnlyList<SubcuentaActual>)> LeerAsync(
        ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsTipoActivo) =>
        bd.EjecutarAsync<(IReadOnlyList<ActivoParaSubcuentas>, IReadOnlyList<LineaParaSubcuentas>, IReadOnlyList<SubcuentaActual>)>(async s =>
        {
            var marcas = string.Join(", ", idsTipoActivo.Select(_ => "?"));
            object?[] tipos = [.. idsTipoActivo.Cast<object?>()];
            // Los activos de baja ya no reciben subcuentas; los ajenos se descartan al planificar.
            var activos = await s.ConsultarAsync(
                $"SELECT IdActivo, IdTipoActivo, Nombre, IdCentroCoste, EsAjeno FROM Activos WHERE IdSociedad = ? AND FechaBaja IS NULL AND IdTipoActivo IN ({marcas})",
                f => new ActivoParaSubcuentas(f.Entero("IdActivo"), f.Entero("IdTipoActivo"), f.Texto("Nombre")?.Trim() ?? "", f.EnteroNulo("IdCentroCoste"), f.Logico("EsAjeno")),
                [contexto.IdSociedad, .. tipos]);
            var lineas = await s.ConsultarAsync(
                "SELECT p.IdPlantillaSubcuentaActivo, p.IdTipoActivo, p.Prefijo, p.Orden, p.EstaActiva, t.Nombre, t.Descripcion FROM PlantillasSubcuentasActivo AS p " +
                $"INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo WHERE p.IdTipoActivo IN ({marcas})",
                f => new LineaParaSubcuentas(f.Entero("IdPlantillaSubcuentaActivo"), f.Entero("IdTipoActivo"), f.Texto("Prefijo")?.Trim() ?? "", f.Entero("Orden"),
                    f.Texto("Nombre")?.Trim() ?? "", f.Texto("Descripcion")?.Trim(), f.Logico("EstaActiva")),
                tipos);
            var enlaces = await s.ConsultarAsync(
                "SELECT sa.IdActivo, sa.IdPlantillaSubcuentaActivo, sa.IdIdentidadContable FROM SubcuentasActivo AS sa INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo " +
                $"WHERE a.IdSociedad = ? AND a.IdTipoActivo IN ({marcas})",
                f => (Activo: f.Entero("IdActivo"), Plantilla: f.Entero("IdPlantillaSubcuentaActivo"), Identidad: f.Entero("IdIdentidadContable")),
                [contexto.IdSociedad, .. tipos]);
            var enEjercicio = (await s.ConsultarAsync(
                    "SELECT c.IdIdentidadContable, c.IdCuentaContable, c.Codigo, c.Nombre FROM (CuentasContables AS c INNER JOIN SubcuentasActivo AS sa " +
                    "ON c.IdIdentidadContable = sa.IdIdentidadContable) INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo WHERE c.IdSociedadEjercicio = ? AND a.IdSociedad = ?",
                    f => (Identidad: f.Entero("IdIdentidadContable"), Id: f.Entero("IdCuentaContable"), Codigo: f.Texto("Codigo")?.Trim(), Nombre: f.Texto("Nombre")?.Trim()),
                    contexto.IdSociedadEjercicio, contexto.IdSociedad))
                .GroupBy(c => c.Identidad).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First());
            // Sin cuenta en el ejercicio, el código que se muestra es el de su último ejercicio (el que conservará al darla de alta).
            var ultimos = new Dictionary<int, string?>();
            foreach (var e in enlaces.Where(e => !enEjercicio.ContainsKey(e.Identidad)))
                ultimos[e.Identidad] = await s.ConsultarPrimeroAsync(
                    "SELECT c.Codigo FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                    "WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC", f => f.Texto("Codigo")?.Trim(), e.Identidad);
            var actuales = enlaces.Select(e => enEjercicio.TryGetValue(e.Identidad, out var c)
                ? new SubcuentaActual(e.Activo, e.Plantilla, e.Identidad, c.Id, c.Codigo, c.Nombre)
                : new SubcuentaActual(e.Activo, e.Plantilla, e.Identidad, null, ultimos.GetValueOrDefault(e.Identidad), null)).ToList();
            return (activos, lineas, actuales);
        });
}
