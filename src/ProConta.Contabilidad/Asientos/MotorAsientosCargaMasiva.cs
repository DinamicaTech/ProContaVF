using System.Globalization;
using System.Text;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Asientos;

/// <summary>Carga masiva de un libro externo (contabilidad/asientos 2026-11-11 13:00).</summary>
public sealed partial class MotorAsientosAccess
{
    /// <summary>Asientos por tanda al borrar (lista IN de la sentencia).</summary>
    private const int IdsPorBorrado = 200;

    private sealed record ApunteGrabado(int IdAsiento, int IdApunte, int Orden, string Cuenta, decimal Debe, decimal Haber, string? Descripcion, string? Documento, int? IdCentro);

    private sealed record AsientoGrabado(int IdAsiento, string Clave, DateTime Fecha, string Descripcion, int Tipo, string? Notas)
    {
        public List<ApunteGrabado> Apuntes { get; } = [];
    }

    public async Task<Resultado<ResultadoCargaLibro>> CargarLibroExternoAsync(ContextoSociedadEjercicio contexto, string motorPropietario, string codigoProceso,
        IReadOnlyList<AsientoExterno> asientos, Actor actor, string motivo, Func<ISesionDatos, IReadOnlyList<int>, Task>? antesDeBorrar = null,
        CancellationToken cancelacion = default)
    {
        var identidad = new IdentidadLote(motorPropietario, codigoProceso, "*");
        var error = ValidarLote(identidad, motivo) ?? ValidarExterno(identidad, externo: true);
        if (error is not null) return Resultado<ResultadoCargaLibro>.Fallo(error);
        var abierto = await bd.EjecutarAsync(s => ComprobarEjercicioAbiertoAsync(s, contexto, externo: true), cancelacion);
        if (!abierto.EsCorrecto) return Resultado<ResultadoCargaLibro>.Fallo(abierto.Errores);
        motivo = motivo.Trim();

        // 1) Validación en memoria: lo mismo que GuardarInterno, con las cuentas y los centros leídos de una vez.
        var cuentas = (await bd.EjecutarAsync(s => s.ConsultarAsync(
                "SELECT IdCuentaContable, IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado, " +
                "SaldoInicial, SaldoActual, NumeroApuntes, Descripcion FROM CuentasContables WHERE IdSociedadEjercicio = ?", CatalogoCuentasAccess.Mapear, contexto.IdSociedadEjercicio), cancelacion))
            .GroupBy(c => c.Codigo.Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var centros = (await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdCentroCoste, IdSociedad, EstaActivo, EsImputable FROM CentrosCoste",
                f => (Id: f.Entero("IdCentroCoste"), Sociedad: f.Texto("IdSociedad"), Activo: f.Logico("EstaActivo"), Imputable: f.Logico("EsImputable"))), cancelacion))
            .ToDictionary(c => c.Id);
        var rechazados = new Dictionary<string, IReadOnlyList<Error>>(StringComparer.Ordinal);
        var validos = new List<AsientoExterno>();
        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in asientos)
        {
            var asiento = x.Asiento;
            var errores = new List<Error>();
            if (!claves.Add(x.ClaveEjecucion))
                errores.Add(new Error("Asientos.ClaveRepetida", $"La clave {x.ClaveEjecucion} está repetida en la carga.", asiento.ToString(),
                    "Cada asiento del libro externo tiene su propia clave.", "Revisa el origen."));
            if (asiento.Contexto != contexto)
                errores.Add(new Error("Asientos.LoteOtroEjercicio", "Todos los asientos de la carga deben ser del mismo ejercicio.", asiento.ToString(),
                    "La carga es de un ejercicio.", "Carga cada ejercicio por separado."));
            asiento.Lote = new IdentidadLote(motorPropietario, codigoProceso, x.ClaveEjecucion);
            var reglas = ReglasAsiento.ValidarYCuadrar(asiento, _politica);
            errores.AddRange(reglas.Errores);
            foreach (var apunte in asiento.Apuntes)
            {
                var codigo = (apunte.CodigoCuenta ?? "").Trim();
                if (!cuentas.TryGetValue(codigo, out var cuenta)) { errores.Add(CuentaNoExiste(codigo, contexto) with { RegistroAfectado = $"{asiento}, apunte {apunte.Orden}" }); continue; }
                var admite = ReglasCuenta.AdmiteApuntes(cuenta);
                if (!admite.EsCorrecto) { errores.AddRange(admite.Errores.Select(e => e with { RegistroAfectado = $"{asiento}, apunte {apunte.Orden}" })); continue; }
                apunte.CodigoCuenta = cuenta.Codigo;
                if (apunte.IdCentroCoste is int centro && !(centros.TryGetValue(centro, out var c)
                        && string.Equals(c.Sociedad, contexto.IdSociedad, StringComparison.OrdinalIgnoreCase) && c.Activo && c.Imputable))
                    errores.Add(CentroNoValido(asiento, apunte));
            }
            if (errores.Count > 0) rechazados[x.ClaveEjecucion] = errores;
            else validos.Add(x);
        }

        // 2) Lo que ya tiene el ejercicio de este motor y proceso, en dos consultas.
        var grabados = await LeerGrabadosAsync(contexto, motorPropietario, codigoProceso, cancelacion);
        var porClave = grabados.Values.GroupBy(a => a.Clave, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var aGrabar = new List<AsientoExterno>();
        var aBorrar = new List<AsientoGrabado>();
        var borrados = new List<(string, int)>();
        foreach (var x in validos)
        {
            if (porClave.TryGetValue(x.ClaveEjecucion, out var previos))
            {
                if (previos.Count == 1 && Huella(previos[0]) == Huella(x.Asiento)) { ids[x.ClaveEjecucion] = previos[0].IdAsiento; continue; }
                aBorrar.AddRange(previos);
            }
            aGrabar.Add(x);
        }
        foreach (var (clave, previos) in porClave.Where(p => !claves.Contains(p.Key)))
        {
            aBorrar.AddRange(previos);
            borrados.AddRange(previos.Select(p => (clave, p.IdAsiento)));
        }

        var ahora = _reloj.Ahora;
        var operador = actor.ToString();
        var idLote = Guid.NewGuid().ToString();
        var cambios = false;
        try
        {
            if (aBorrar.Count == 0 && aGrabar.Count == 0)
                return Resultado<ResultadoCargaLibro>.Correcto(new ResultadoCargaLibro(ids, 0, validos.Count, borrados, rechazados));
            cambios = true;
            using var carga = new CargaMasivaAccess(bd.Ruta);

            // 3) Bajas: su registro de cambios y luego los asientos, por tandas.
            if (aBorrar.Count > 0)
            {
                await RegistrarCambiosAsync(carga, idLote, ahora, operador, motivo, aBorrar.SelectMany(a => a.Apuntes
                    .Select(x => new object?[] { a.IdAsiento, x.IdApunte, (short)x.Orden, "BAJA", "Apunte", ResumenGrabado(x), null })
                    .Append([a.IdAsiento, null, null, "BAJA", "Asiento", $"{a.Fecha:yyyy-MM-dd} {a.Descripcion} ({motorPropietario}/{codigoProceso}/{a.Clave})", null])), cancelacion);
                foreach (var tanda in aBorrar.Select(a => a.IdAsiento).Chunk(IdsPorBorrado))
                {
                    cancelacion.ThrowIfCancellationRequested();
                    var lista = string.Join(", ", tanda.Select(i => i.ToString(CultureInfo.InvariantCulture)));
                    await bd.EnTransaccionAsync(async s =>
                    {
                        if (antesDeBorrar is not null) await antesDeBorrar(s, tanda);
                        await s.EjecutarAsync($"DELETE FROM Apuntes WHERE IdAsiento IN ({lista})");
                        await s.EjecutarAsync($"DELETE FROM RevisionesAsientos WHERE IdAsiento IN ({lista})");
                        await s.EjecutarAsync($"DELETE FROM RelacionesAsientos WHERE IdAsientoOrigen IN ({lista}) OR IdAsientoDestino IN ({lista})");
                        await s.EjecutarAsync($"DELETE FROM Asientos WHERE IdAsiento IN ({lista})");
                        return true;
                    }, cancelacion);
                }
            }

            // 4) Altas: cabeceras (con su Id), apuntes, revisión y registro de cambios.
            if (aGrabar.Count > 0)
            {
                var nuevos = await carga.InsertarAsync("Asientos",
                    ["IdSociedadEjercicio", "FechaContable", "Descripcion", "TipoAsiento", "Notas", "EstadoAsiento", "OperadorCreacion", "FechaHoraCreacion",
                        "OperadorContabilizacion", "FechaHoraContabilizacion", "TotalDebe", "TotalHaber", "Version", "EsAutomatico", "MotorPropietario", "CodigoProceso", "ClaveEjecucion"],
                    aGrabar.Select(x => new object?[] { contexto.IdSociedadEjercicio, x.Asiento.FechaContable, x.Asiento.Descripcion.Trim(), (byte)x.Asiento.Tipo, x.Asiento.Notas,
                        (byte)EstadoAsiento.Contabilizado, operador, ahora, operador, ahora, x.Asiento.TotalDebe.Valor, x.Asiento.TotalHaber.Valor, 1, true, motorPropietario,
                        codigoProceso, x.ClaveEjecucion }), "IdAsiento", cancelacion: cancelacion);
                for (var i = 0; i < aGrabar.Count; i++)
                {
                    aGrabar[i].Asiento.IdAsiento = nuevos[i];
                    aGrabar[i].Asiento.Version = 1;
                    ids[aGrabar[i].ClaveEjecucion] = nuevos[i];
                }

                await carga.InsertarAsync("Apuntes",
                    ["IdAsiento", "Orden", "CuentaContable", "ImporteDebe", "ImporteHaber", "Descripcion", "NumeroDocumento", "IdCliente", "IdProveedor", "FechaVencimiento",
                        "IdCentroCoste", "TipoApunte", "GeneradoAutomaticamente", "MotivoGeneracion"],
                    aGrabar.SelectMany(x => x.Asiento.Apuntes.Select(a => new object?[] { x.Asiento.IdAsiento, (short)a.Orden, a.CodigoCuenta, a.Debe.EsCero ? null : a.Debe.Valor,
                        a.Haber.EsCero ? null : a.Haber.Valor, a.Descripcion, a.NumeroDocumento, a.IdCliente, a.IdProveedor, a.FechaVencimiento, a.IdCentroCoste, (byte)a.Tipo,
                        a.GeneradoAutomaticamente, a.MotivoGeneracion })), cancelacion: cancelacion);

                await carga.InsertarAsync("RevisionesAsientos",
                    ["IdAsiento", "NumeroRevision", "TipoOperacion", "Motivo", "Operador", "FechaHora", "ContenidoAnterior", "ContenidoPosterior"],
                    aGrabar.Select(x => new object?[] { x.Asiento.IdAsiento, 1, (byte)OperacionRevision.Lote, motivo, operador, ahora, null, Instantanea(x.Asiento) }),
                    cancelacion: cancelacion);

                await RegistrarCambiosAsync(carga, idLote, ahora, operador, motivo, aGrabar.SelectMany(x => x.Asiento.Apuntes
                    .Select(a => new object?[] { x.Asiento.IdAsiento, null, (short)a.Orden, "ALTA", "Apunte", null, Resumen(a) })), cancelacion);
            }
        }
        finally
        {
            // La caché de saldos se recalcula también si la carga se interrumpe a medias.
            if (cambios) await RecalcularSaldosAsync(contexto);
        }
        return Resultado<ResultadoCargaLibro>.Correcto(new ResultadoCargaLibro(ids, aGrabar.Count, validos.Count - aGrabar.Count, borrados, rechazados));
    }

    private static Task<IReadOnlyList<int>> RegistrarCambiosAsync(CargaMasivaAccess carga, string idLote, DateTime ahora, string operador, string motivo,
        IEnumerable<object?[]> filas, CancellationToken cancelacion) =>
        carga.InsertarAsync("RegistroCambiosApuntes",
            ["IdLote", "FechaHora", "Operador", "Motivo", "IdAsiento", "IdApunte", "Orden", "Operacion", "Campo", "ValorAntiguo", "ValorNuevo"],
            filas.Select(f => (object?[])[idLote, ahora, operador, motivo, .. f]), cancelacion: cancelacion);

    private async Task<Dictionary<int, AsientoGrabado>> LeerGrabadosAsync(ContextoSociedadEjercicio contexto, string motor, string proceso, CancellationToken cancelacion)
    {
        const string filtro = "a.EsAutomatico = True AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.IdSociedadEjercicio = ?";
        var asientos = (await bd.EjecutarAsync(s => s.ConsultarAsync(
                $"SELECT a.IdAsiento, a.ClaveEjecucion, a.FechaContable, a.Descripcion, a.TipoAsiento, a.Notas FROM Asientos AS a WHERE {filtro}",
                f => new AsientoGrabado(f.Entero("IdAsiento"), f.Texto("ClaveEjecucion") ?? "", f.Fecha("FechaContable"), f.Texto("Descripcion") ?? "", f.Entero("TipoAsiento"), f.Texto("Notas")),
                motor, proceso, contexto.IdSociedadEjercicio), cancelacion))
            .ToDictionary(a => a.IdAsiento);
        var apuntes = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT ap.IdAsiento, ap.IdApunte, ap.Orden, ap.CuentaContable, ap.ImporteDebe, ap.ImporteHaber, ap.Descripcion, ap.NumeroDocumento, ap.IdCentroCoste, ap.TipoApunte " +
            $"FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE {filtro} ORDER BY ap.IdAsiento, ap.Orden",
            f => (Tipo: (TipoApunte)f.Entero("TipoApunte"), Apunte: new ApunteGrabado(f.Entero("IdAsiento"), f.Entero("IdApunte"), f.Entero("Orden"), f.Texto("CuentaContable") ?? "",
                Importe.Redondear(f.DecimalNulo("ImporteDebe") ?? 0m).Valor, Importe.Redondear(f.DecimalNulo("ImporteHaber") ?? 0m).Valor, f.Texto("Descripcion"),
                f.Texto("NumeroDocumento"), f.EnteroNulo("IdCentroCoste"))),
            motor, proceso, contexto.IdSociedadEjercicio), cancelacion);
        foreach (var (tipo, apunte) in apuntes)
            if (tipo == TipoApunte.Ordinario && asientos.TryGetValue(apunte.IdAsiento, out var a)) a.Apuntes.Add(apunte);
        return asientos;
    }

    private static string ResumenGrabado(ApunteGrabado a) =>
        $"{a.Cuenta} D {a.Debe.ToString("0.00", CultureInfo.InvariantCulture)} H {a.Haber.ToString("0.00", CultureInfo.InvariantCulture)}" +
        (string.IsNullOrWhiteSpace(a.Descripcion) ? "" : $" «{a.Descripcion}»");

    /// <summary>Huella para saber si un asiento grabado es igual al que llega: fecha, descripción, tipo, notas y apuntes ordinarios.</summary>
    private static string Huella(DateTime fecha, string descripcion, int tipo, string? notas, IEnumerable<(string Cuenta, decimal Debe, decimal Haber, string? Descripcion, string? Documento, int? Centro)> apuntes)
    {
        var sb = new StringBuilder();
        sb.Append(fecha.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append('|').Append(descripcion.Trim()).Append('|').Append(tipo).Append('|').Append(notas?.Trim()).Append('\n');
        foreach (var x in apuntes)
            sb.Append(x.Cuenta.Trim()).Append('|').Append(x.Debe.ToString("0.00", CultureInfo.InvariantCulture)).Append('|').Append(x.Haber.ToString("0.00", CultureInfo.InvariantCulture))
                .Append('|').Append(x.Descripcion?.Trim()).Append('|').Append(x.Documento?.Trim()).Append('|').Append(x.Centro).Append('\n');
        return sb.ToString();
    }

    private static string Huella(AsientoGrabado a) =>
        Huella(a.Fecha, a.Descripcion, a.Tipo, a.Notas, a.Apuntes.Select(x => (x.Cuenta, x.Debe, x.Haber, x.Descripcion, x.Documento, x.IdCentro)));

    private static string Huella(Asiento a) =>
        Huella(a.FechaContable, a.Descripcion, (int)a.Tipo, a.Notas, a.Apuntes.Where(x => x.Tipo == TipoApunte.Ordinario)
            .Select(x => (x.CodigoCuenta, x.Debe.Valor, x.Haber.Valor, x.Descripcion, x.NumeroDocumento, x.IdCentroCoste)));
}
