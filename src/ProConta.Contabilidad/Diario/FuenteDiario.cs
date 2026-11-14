using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.Diario;

/// <summary>De dónde lee el Diario: los ejercicios de la sociedad y todos los asientos contabilizados de unos ejercicios.</summary>
public interface IFuenteDiario
{
    Task<IReadOnlyList<EjercicioDiario>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default);

    Task<DatosDiario> CargarAsync(string idSociedad, IReadOnlyList<EjercicioDiario> ejercicios, CancellationToken cancelacion = default);
}

/// <summary>
/// El Diario sobre la BD (contabilidad/diario): Asientos y Apuntes, el libro de IVA (para saber si el asiento es de una factura y su fecha),
/// los nombres de las cuentas, los centros de coste y las plantillas. Cada carga hace un número fijo de consultas, sea cual sea el número
/// de asientos; filtrar, ordenar y sumar se hace después en memoria (<see cref="CalculadoraDiario"/>).
/// </summary>
public sealed class FuenteDiarioAccess(IBaseDatos bd) : IFuenteDiario
{
    public Task<IReadOnlyList<EjercicioDiario>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync(
                "SELECT IdSociedadEjercicio, Ejercicio, EjercicioCerrado, TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: (f.Texto("Ejercicio") ?? "").Trim(), Cerrado: !f.EsNulo("EjercicioCerrado") && f.Logico("EjercicioCerrado"),
                    Trimestre: f.EsNulo("TrimestreActivo") ? (int?)null : Convert.ToInt32(f.Valor("TrimestreActivo"), CultureInfo.InvariantCulture)),
                idSociedad);
            return (IReadOnlyList<EjercicioDiario>)filas
                .Where(x => int.TryParse(x.Anio, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .Select(x => new EjercicioDiario(x.Id, int.Parse(x.Anio, CultureInfo.InvariantCulture), x.Cerrado, x.Trimestre))
                .OrderBy(e => e.Ejercicio).ToList();
        }, cancelacion);

    public Task<DatosDiario> CargarAsync(string idSociedad, IReadOnlyList<EjercicioDiario> ejercicios, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            if (ejercicios.Count == 0) return DatosDiario.Vacio;
            var en = string.Join(", ", ejercicios.Select(e => e.IdSociedadEjercicio.ToString(CultureInfo.InvariantCulture)));
            var porId = ejercicios.ToDictionary(e => e.IdSociedadEjercicio);

            // Libro de IVA por asiento (repercutido y soportado): si lo tiene, el asiento es de una factura; su fecha es la del documento.
            var fechasFactura = new Dictionary<int, DateTime>();
            foreach (var tabla in new[] { "IVARepercutido", "IVASoportado" })
                foreach (var (id, fecha) in await s.ConsultarAsync(
                             $"SELECT r.IdAsiento, MIN(r.FechaExpedicion) AS F FROM {tabla} AS r WHERE r.IdSociedadEjercicio IN ({en}) AND r.IdAsiento IS NOT NULL GROUP BY r.IdAsiento",
                             f => (f.Entero("IdAsiento"), f.FechaNula("F"))))
                    if (fecha is { } d && (!fechasFactura.TryGetValue(id, out var otra) || d < otra)) fechasFactura[id] = d.Date;

            var plantillas = (await s.ConsultarAsync("SELECT IdPlantillaAsiento, Nombre FROM PlantillasAsiento", f => (Id: f.Entero("IdPlantillaAsiento"), Nombre: f.Texto("Nombre") ?? "")))
                .ToDictionary(x => x.Id, x => x.Nombre);
            var centros = (await s.ConsultarAsync("SELECT IdCentroCoste, Codigo, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
                    f => (Id: f.Entero("IdCentroCoste"), Texto: $"{(f.Texto("Codigo") ?? "").Trim()} · {(f.Texto("Nombre") ?? "").Trim()}"), idSociedad))
                .ToDictionary(x => x.Id, x => x.Texto);
            var nombres = new Dictionary<(int, string), string>();
            foreach (var (ej, codigo, nombre) in await s.ConsultarAsync(
                         $"SELECT IdSociedadEjercicio, Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio IN ({en})",
                         f => (f.Entero("IdSociedadEjercicio"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim())))
                nombres[(ej, codigo)] = nombre;

            var asientos = (await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.IdSociedadEjercicio, a.FechaContable, a.Descripcion, a.TipoAsiento, a.Notas, a.EsAutomatico, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion, " +
                "a.CorregidoAMano, a.OperadorCreacion, a.FechaHoraCreacion, a.OperadorModificacion, a.FechaHoraModificacion, a.Version, a.IdPlantillaAsiento " +
                $"FROM Asientos AS a WHERE a.IdSociedadEjercicio IN ({en}) AND a.EstadoAsiento = 1",
                f =>
                {
                    var id = f.Entero("IdAsiento");
                    return new AsientoDiario
                    {
                        IdAsiento = id,
                        Ejercicio = porId[f.Entero("IdSociedadEjercicio")],
                        Fecha = f.Fecha("FechaContable").Date,
                        Descripcion = (f.Texto("Descripcion") ?? "").Trim(),
                        TipoAsiento = Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture),
                        Notas = f.Texto("Notas")?.Trim() is { Length: > 0 } n ? n : null,
                        EsAutomatico = !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico"),
                        MotorPropietario = f.Texto("MotorPropietario"),
                        CodigoProceso = f.Texto("CodigoProceso"),
                        ClaveEjecucion = f.Texto("ClaveEjecucion"),
                        CorregidoAMano = !f.EsNulo("CorregidoAMano") && f.Logico("CorregidoAMano"),
                        ConLibroIva = fechasFactura.ContainsKey(id),
                        FechaDocumento = fechasFactura.TryGetValue(id, out var fd) ? fd : null,
                        OperadorCreacion = f.Texto("OperadorCreacion"),
                        GrabadoEl = f.FechaNula("FechaHoraCreacion"),
                        OperadorModificacion = f.Texto("OperadorModificacion"),
                        ModificadoEl = f.FechaNula("FechaHoraModificacion"),
                        Version = f.Entero("Version"),
                        Plantilla = f.EnteroNulo("IdPlantillaAsiento") is int p && plantillas.TryGetValue(p, out var nombre) ? nombre : null,
                    };
                })).ToDictionary(a => a.IdAsiento);

            var apuntes = await s.ConsultarAsync(
                "SELECT p.IdApunte, p.IdAsiento, p.Orden, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion, p.NumeroDocumento, p.FechaVencimiento, p.IdCentroCoste, p.Punteado " +
                $"FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio IN ({en}) AND a.EstadoAsiento = 1",
                f => (IdAsiento: f.Entero("IdAsiento"), Apunte: new ApunteDiario
                {
                    IdApunte = f.Entero("IdApunte"),
                    Orden = f.EsNulo("Orden") ? 0 : Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture),
                    Cuenta = (f.Texto("CuentaContable") ?? "").Trim(),
                    Debe = f.DecimalNulo("ImporteDebe") ?? 0m,
                    Haber = f.DecimalNulo("ImporteHaber") ?? 0m,
                    ConceptoPropio = f.Texto("Descripcion")?.Trim() is { Length: > 0 } d ? d : null,
                    Documento = f.Texto("NumeroDocumento")?.Trim() is { Length: > 0 } doc ? doc : null,
                    Vencimiento = f.FechaNula("FechaVencimiento")?.Date,
                    IdCentroCoste = f.EnteroNulo("IdCentroCoste"),
                    Punteado = !f.EsNulo("Punteado") && f.Logico("Punteado"),
                }));
            foreach (var (idAsiento, apunte) in apuntes.OrderBy(x => x.Apunte.Orden).ThenBy(x => x.Apunte.IdApunte))
            {
                if (!asientos.TryGetValue(idAsiento, out var asiento)) continue;
                asiento.Apuntes.Add(Completar(apunte, asiento, nombres, centros));
            }
            return new DatosDiario(ejercicios, asientos.Values.OrderBy(a => a.Fecha).ThenBy(a => a.IdAsiento).ToList());
        }, cancelacion);

    private static ApunteDiario Completar(ApunteDiario a, AsientoDiario asiento, Dictionary<(int, string), string> nombres, Dictionary<int, string> centros) => new()
    {
        IdApunte = a.IdApunte, Orden = a.Orden, Cuenta = a.Cuenta, Debe = a.Debe, Haber = a.Haber, ConceptoPropio = a.ConceptoPropio, Documento = a.Documento,
        Vencimiento = a.Vencimiento, IdCentroCoste = a.IdCentroCoste, Punteado = a.Punteado, Asiento = asiento,
        NombreCuenta = nombres.GetValueOrDefault((asiento.Ejercicio.IdSociedadEjercicio, a.Cuenta), ""),
        CentroCoste = a.IdCentroCoste is int c ? centros.GetValueOrDefault(c, $"Centro {c}") : "",
    };
}
