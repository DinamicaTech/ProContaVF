using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Plantillas;

/// <summary>
/// Plantillas de asiento guardadas en la BD (contabilidad/plantillas-asiento 2026-11-12 11:15): las lista, propone sus asientos sin grabar
/// nada (también la vista previa a partir de su ejemplo) y los ejecuta con el motor de asientos, en una transacción, con motivo
/// obligatorio, un IdLote común, la relación con el asiento de origen y el punteo. Cancelar es no llamar a <see cref="EjecutarAsync"/>.
/// </summary>
public sealed class ServicioPlantillasAsiento(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas)
{
    private const string Columnas =
        "IdPlantillaAsiento, Nombre, Descripcion, Tipo, IdSociedad, Origen, ClaveProceso, ReglaFecha, AgruparPorCuenta, Activa, MotivoPropuesto, Ejemplo, Orden";

    public ServicioPlantillasAsiento(IBaseDatos bd) : this(bd, new MotorAsientosAccess(bd), new CatalogoCuentasAccess(bd)) { }

    /// <summary>
    /// Plantillas activas de la empresa (las suyas y las comunes), en su orden. Sin <paramref name="incluirSistema"/> solo las de usuario,
    /// que son las que se ofrecen a mano; con <paramref name="origen"/>, solo las de ese origen (p. ej. Apunte en el asistente del Mayor).
    /// </summary>
    public async Task<IReadOnlyList<PlantillaAsiento>> ListarAsync(string idSociedad, bool incluirSistema = false, OrigenPlantilla? origen = null)
    {
        var todas = await bd.EjecutarAsync(async s =>
        {
            var ids = await s.ConsultarAsync(
                "SELECT IdPlantillaAsiento FROM PlantillasAsiento WHERE Activa = True AND (IdSociedad IS NULL OR IdSociedad = ?) ORDER BY Orden, Nombre",
                f => f.Entero("IdPlantillaAsiento"), idSociedad);
            var lista = new List<PlantillaAsiento>();
            foreach (var id in ids) lista.Add((await CargarAsync(s, id))!);
            return lista;
        });
        return todas.Where(p => (incluirSistema || p.Tipo == TipoPlantilla.Usuario) && (origen is null || p.Origen == origen)).ToList();
    }

    public Task<PlantillaAsiento?> ObtenerAsync(int idPlantilla) => bd.EjecutarAsync(s => CargarAsync(s, idPlantilla));

    /// <summary>Plantilla de sistema de un proceso (p. ej. «nominas») para la empresa: la suya si tiene una, si no la común.</summary>
    public async Task<PlantillaAsiento?> DeProcesoAsync(string claveProceso, string idSociedad) =>
        (await ListarAsync(idSociedad, incluirSistema: true))
            .Where(p => p.Tipo == TipoPlantilla.Sistema && string.Equals(p.ClaveProceso, claveProceso, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.IdSociedad is null ? 1 : 0).FirstOrDefault();

    /// <summary>
    /// Cuentas de la empresa (113 de cuadres y 570 de tesorería, Parámetros › Empresas), trimestre activo del ejercicio, cuentas del ejercicio y,
    /// para <c>UltimaUsada()</c>, la última cuenta usada con la plantilla en cada dato de cuenta con filtro.
    /// </summary>
    public Task<EntornoSociedad> EntornoAsync(ContextoSociedadEjercicio contexto, PlantillaAsiento? plantilla = null) =>
        bd.EjecutarAsync(async s =>
        {
            var empresa = await s.ConsultarPrimeroAsync("SELECT Cuenta113ParaCuadres, Cuenta570Tesoreria FROM Sociedades WHERE IdSociedad = ?",
                f => (Cuadres: f.Texto("Cuenta113ParaCuadres"), Tesoreria: f.Texto("Cuenta570Tesoreria")), contexto.IdSociedad);
            var trimestre = await s.EscalarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", contexto.IdSociedadEjercicio);
            var lista = await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: f.Texto("Nombre") ?? ""), contexto.IdSociedadEjercicio);
            var codigos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (codigo, nombre) in lista) codigos.TryAdd(codigo, nombre);

            var ultimas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (plantilla is { IdPlantilla: > 0 })
                foreach (var d in plantilla.Datos.Where(d => d.Tipo == TipoDato.Cuenta && d.FiltroCuenta is { Length: > 0 }
                             && (d.ValorPropuesto ?? "").Contains("UltimaUsada", StringComparison.OrdinalIgnoreCase)))
                {
                    var usada = await s.ConsultarAsync(
                        "SELECT TOP 20 ap.CuentaContable FROM (Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento) " +
                        "INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                        "WHERE a.IdPlantillaAsiento = ? AND se.IdSociedad = ? AND ap.CuentaContable LIKE ? ORDER BY a.IdAsiento DESC, ap.Orden",
                        f => (f.Texto("CuentaContable") ?? "").Trim(), plantilla.IdPlantilla, contexto.IdSociedad, d.FiltroCuenta + "%");
                    if (usada.FirstOrDefault(codigos.ContainsKey) is { } c) ultimas[d.Nombre] = c;
                }

            return new EntornoSociedad(contexto, Limpio(empresa.Cuadres), Limpio(empresa.Tesoreria),
                trimestre is null or DBNull ? null : Convert.ToInt32(trimestre, CultureInfo.InvariantCulture), codigos, ultimas);
        });

    /// <summary>
    /// Origen a partir de un apunte del Mayor: su asiento completo y si tiene libro de IVA (es de una factura). Si el apunte ya no existe,
    /// el error dice cómo seguir.
    /// </summary>
    public async Task<Resultado<OrigenApunte>> OrigenApunteAsync(int idApunte)
    {
        var fila = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT IdAsiento, Orden FROM Apuntes WHERE IdApunte = ?",
            f => (IdAsiento: f.Entero("IdAsiento"), Orden: f.Entero("Orden")), idApunte));
        if (fila.IdAsiento == 0)
            return Resultado<OrigenApunte>.Fallo(new Error("Plantillas.ApunteNoEncontrado", $"El apunte {idApunte} ya no existe.", $"Apunte {idApunte}",
                "Se ha borrado, o su asiento se ha regenerado después de cargar el Mayor.", "Vuelve a cargar el Mayor y elige de nuevo el apunte."));
        var asiento = await motor.ObtenerAsync(fila.IdAsiento);
        if (asiento is null)
            return Resultado<OrigenApunte>.Fallo(new Error("Plantillas.ApunteNoEncontrado", $"El asiento {fila.IdAsiento} ya no existe.", $"Asiento {fila.IdAsiento}",
                "Otra persona lo ha borrado.", "Vuelve a cargar el Mayor y elige de nuevo el apunte."));
        var iva = await bd.EjecutarAsync(async s =>
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM IVASoportado WHERE IdAsiento = ?", asiento.IdAsiento), CultureInfo.InvariantCulture) +
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM IVARepercutido WHERE IdAsiento = ?", asiento.IdAsiento), CultureInfo.InvariantCulture));
        return Resultado<OrigenApunte>.Correcto(new OrigenApunte(asiento, fila.Orden, iva > 0));
    }

    /// <summary>Valores propuestos de los datos de entrada (para rellenar el formulario de la plantilla).</summary>
    public async Task<IReadOnlyDictionary<string, string?>> ValoresPropuestosAsync(ContextoSociedadEjercicio contexto, PlantillaAsiento plantilla, OrigenEjecucion origen) =>
        MotorPlantillas.ValoresPropuestos(plantilla, origen, await EntornoAsync(contexto, plantilla));

    /// <summary>Propone los asientos de la plantilla con los datos reales de la empresa, sin grabar nada.</summary>
    public async Task<Resultado<PropuestaPlantilla>> ProponerAsync(ContextoSociedadEjercicio contexto, PlantillaAsiento plantilla, OrigenEjecucion origen,
        IReadOnlyDictionary<string, string?>? datos, DateTime? fecha = null) =>
        MotorPlantillas.Proponer(plantilla, origen, datos, await EntornoAsync(contexto, plantilla), fecha);

    /// <summary>
    /// Vista previa de la plantilla a partir de su ejemplo ficticio (columna Ejemplo): los asientos que generaría, sin BD y sin texto
    /// escrito a mano. La plantilla «Nómina», sin líneas todavía, no tiene vista previa (lo dice el error).
    /// </summary>
    public static Resultado<PropuestaPlantilla> VistaPrevia(PlantillaAsiento plantilla)
    {
        if (string.IsNullOrWhiteSpace(plantilla.Ejemplo))
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.SinEjemplo", $"La plantilla «{plantilla.Nombre}» no tiene ejemplo para la vista previa.",
                plantilla.ToString(), "Su ejemplo está vacío.", "Añade un ejemplo en el mantenimiento de plantillas."));
        if (plantilla.Lineas.Count == 0)
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.SinLineas", $"La plantilla «{plantilla.Nombre}» todavía no tiene líneas.", plantilla.ToString(),
                plantilla.Tipo == TipoPlantilla.Sistema ? "Sus líneas las cargará su proceso (las de la nómina, desde PlantillasApuntes del ERP)." : "No se le ha añadido ninguna línea.",
                "Añade sus líneas en el mantenimiento de plantillas."));
        EjemploPlantilla ejemplo;
        try { ejemplo = PlantillasJson.LeerEjemplo(plantilla.Ejemplo); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.EjemploNoValido", $"El ejemplo de la plantilla «{plantilla.Nombre}» no se puede leer.",
                plantilla.ToString(), ex.Message, "Corrige el ejemplo en el mantenimiento de plantillas."));
        }
        var origen = ejemplo.Origen is OrigenProceso p && plantilla.ClaveProceso is { } clave ? p with { ClaveProceso = clave } : ejemplo.Origen;
        return MotorPlantillas.Proponer(plantilla, origen, ejemplo.Datos, ejemplo.Entorno);
    }

    /// <summary>
    /// Graba la propuesta (tal cual o corregida en la rejilla): crea las subcuentas «?» que falten y guarda todos sus asientos con el motor
    /// de asientos en una transacción, con el motivo como nota, un IdLote común, la plantilla usada, la relación con el asiento de origen y
    /// los apuntes nuevos y el de origen punteados. Una plantilla de sistema no se ejecuta a mano.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<int>>> EjecutarAsync(PropuestaPlantilla propuesta, string motivo, Actor actor)
    {
        var plantilla = propuesta.Plantilla;
        if (plantilla.IdPlantilla > 0 && await ObtenerAsync(plantilla.IdPlantilla) is { } guardada) plantilla = guardada;
        if (plantilla.Tipo == TipoPlantilla.Sistema)
            return Resultado<IReadOnlyList<int>>.Fallo(new Error("Plantillas.SistemaManual", $"La plantilla «{plantilla.Nombre}» es de sistema y no se ejecuta a mano.",
                plantilla.ToString(), $"Solo la ejecuta su proceso ({plantilla.ClaveProceso}).",
                plantilla.ClaveProceso == "nominas" ? "Contabiliza las nóminas desde Nóminas." : "Ejecútala desde su proceso."));
        if (string.IsNullOrWhiteSpace(motivo) || motivo.TrimStart().StartsWith("**", StringComparison.Ordinal))
            return Resultado<IReadOnlyList<int>>.Fallo(new Error("Plantillas.SinMotivo", "Falta el motivo de este cambio.", plantilla.ToString(),
                "Toda corrección queda documentada con su motivo en el asiento y en el registro de cambios.", "Escribe por qué haces este asiento."));
        if (propuesta.Asientos.Count == 0)
            return Resultado<IReadOnlyList<int>>.Fallo(new Error("Plantillas.SinApuntes", "No hay ningún asiento que grabar.", plantilla.ToString(), null,
                "Vuelve a proponer el asiento con la plantilla."));

        if (propuesta.IdApunteOrigen is int idApunte)
        {
            var origen = await OrigenApunteAsync(idApunte);
            if (!origen.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(origen.Errores);
            if (MotorPlantillas.ComprobarOrigen(plantilla, origen.Valor) is { } error) return Resultado<IReadOnlyList<int>>.Fallo(error);
        }

        foreach (var (codigo, nombre) in propuesta.CuentasNuevas)
        {
            if ((await cuentas.ResolverAsync(propuesta.Contexto, codigo)).EsCorrecto) continue;
            var alta = await cuentas.AltaAsync(propuesta.Contexto, new AltaCuenta(codigo, nombre));
            if (!alta.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(alta.Errores);
        }

        var deCorreccion = propuesta.IdApunteOrigen is not null;
        var asientos = propuesta.Asientos.Select(p =>
        {
            var a = new Asiento(propuesta.Contexto, p.Fecha, p.Descripcion)
            {
                Notas = motivo.Trim(),
                IdPlantilla = plantilla.IdPlantilla > 0 ? plantilla.IdPlantilla : null,
                Tipo = deCorreccion ? TipoAsiento.Rectificacion : TipoAsiento.Ordinario,
            };
            foreach (var x in p.Apuntes)
            {
                var apunte = x.AlDebe ? Apunte.AlDebe(x.Cuenta, x.Importe, x.Concepto) : Apunte.AlHaber(x.Cuenta, x.Importe, x.Concepto);
                apunte.NumeroDocumento = x.Documento;
                apunte.IdCentroCoste = x.IdCentroCoste;
                apunte.Punteado = deCorreccion;
                a.Apuntes.Add(apunte);
            }
            return a;
        }).ToList();

        return await motor.GuardarManualesAsync(asientos, actor, motivo.Trim(),
            new OpcionesAltaManual(propuesta.IdAsientoOrigen, PuntearApuntes: propuesta.IdApunteOrigen is int id ? [id] : null));
    }

    private static async Task<PlantillaAsiento?> CargarAsync(ISesionDatos s, int id)
    {
        var p = await s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM PlantillasAsiento WHERE IdPlantillaAsiento = ?", f => new PlantillaAsiento(
            f.Entero("IdPlantillaAsiento"), f.Texto("Nombre") ?? "", f.Texto("Descripcion") ?? "", PlantillasJson.Tipo(f.Texto("Tipo")), PlantillasJson.Origen(f.Texto("Origen")),
            [], [], f.Texto("IdSociedad"), f.Texto("ClaveProceso"), PlantillasJson.Regla(f.Texto("ReglaFecha")), f.Logico("AgruparPorCuenta"), f.Logico("Activa"),
            f.Texto("MotivoPropuesto"), f.Texto("Ejemplo"), f.Entero("Orden")), id);
        if (p is null) return null;
        var datos = await s.ConsultarAsync(
            "SELECT Orden, Nombre, Etiqueta, Tipo, ValorPropuesto, FiltroCuenta, Ayuda, Obligatorio FROM PlantillasAsientoDatos WHERE IdPlantillaAsiento = ? ORDER BY Orden",
            f => new DatoEntrada(f.Texto("Nombre") ?? "", f.Texto("Etiqueta") ?? "", PlantillasJson.TipoDeDato(f.Texto("Tipo")), f.Texto("ValorPropuesto"),
                f.Texto("FiltroCuenta"), f.Texto("Ayuda"), f.Logico("Obligatorio"), f.Entero("Orden")), id);
        var lineas = await s.ConsultarAsync(
            "SELECT NumeroAsiento, Orden, PorCadaApunte, FormulaCuenta, Lado, FormulaImporte, FormulaConcepto, FormulaCentro, Condicion FROM PlantillasAsientoLineas " +
            "WHERE IdPlantillaAsiento = ? ORDER BY NumeroAsiento, Orden",
            f => new LineaPlantilla(f.Entero("NumeroAsiento"), f.Entero("Orden"), f.Texto("FormulaCuenta") ?? "", f.Texto("Lado") ?? "", f.Texto("FormulaImporte") ?? "",
                f.Texto("FormulaConcepto"), f.Texto("FormulaCentro"), f.Texto("Condicion"), f.Logico("PorCadaApunte")), id);
        return p with { Datos = datos, Lineas = lineas };
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
