using System.Collections.Concurrent;
using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Inicio.Salud;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Conector;

/// <summary>
/// Lo que el conector de Claude Desktop puede hacer sobre la BD (plataforma/conector-claude): leer, proponer asientos manuales validándolos
/// con el motor sin grabar, y grabar una propuesta ya validada. No tiene reglas contables propias: todas son las del motor de asientos.
/// </summary>
public sealed class ServicioConector
{
    public const string MarcaAsistente = "Propuesto por el asistente IA (Claude)";
    public const int MaximoAsientos = 50;

    private readonly IBaseDatos _bd;
    private readonly string _operador;
    private readonly Func<DateTime> _hoy;
    private readonly ConcurrentDictionary<int, Propuesta> _propuestas = new();
    private int _ultimaPropuesta;

    /// <summary>Entorno de la BD (plataforma/entorno-pruebas): «Producción» o «Pruebas»; lo dice la herramienta «sociedades».</summary>
    public string Entorno { get; init; } = "Producción";

    public ServicioConector(IBaseDatos bd, string operador, Func<DateTime>? hoy = null)
    {
        _bd = bd;
        _operador = string.IsNullOrWhiteSpace(operador) ? Environment.UserName : operador.Trim();
        _hoy = hoy ?? (() => DateTime.Today);
    }

    /// <summary>Operador que queda en el registro de cambios: el de ProContaVF con la marca del asistente.</summary>
    public Actor Actor => new($"{_operador} · asistente IA");

    // ───────────── Lectura ─────────────

    public async Task<IReadOnlyList<SociedadConector>> SociedadesAsync()
    {
        var sociedades = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdSociedad, Nombre FROM Sociedades ORDER BY IdSociedad",
            f => (Id: (f.Texto("IdSociedad") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim())));
        var fuente = new FuenteDiarioAccess(_bd);
        var lista = new List<SociedadConector>();
        foreach (var (id, nombre) in sociedades)
        {
            var ejercicios = await fuente.EjerciciosAsync(id);
            lista.Add(new SociedadConector(id, nombre, ejercicios.Select(e => new EjercicioConector(e.Ejercicio, e.Cerrado, e.TrimestreActivo)).ToList()));
        }
        return lista;
    }

    public async Task<Resultado<IReadOnlyList<ResumenAsiento>>> BuscarAsientosAsync(string idSociedad, int ejercicio, int? numero = null, DateTime? desde = null,
        DateTime? hasta = null, string? cuenta = null, string? texto = null, bool soloDescuadrados = false, int maximo = MaximoAsientos)
    {
        var datos = await DiarioAsync(idSociedad, ejercicio);
        if (!datos.EsCorrecto) return Resultado<IReadOnlyList<ResumenAsiento>>.Fallo(datos.Errores);
        var plano = BuscadorCuentas.Plano(texto);
        var lista = datos.Valor.Asientos
            .Where(a => numero is null || a.IdAsiento == numero)
            .Where(a => desde is null || a.Fecha >= desde.Value.Date)
            .Where(a => hasta is null || a.Fecha <= hasta.Value.Date)
            .Where(a => string.IsNullOrWhiteSpace(cuenta) || a.Apuntes.Any(p => p.Cuenta.StartsWith(cuenta.Trim(), StringComparison.Ordinal)))
            .Where(a => string.IsNullOrWhiteSpace(plano) || BuscadorCuentas.Plano(a.Descripcion).Contains(plano, StringComparison.Ordinal)
                || a.Apuntes.Any(p => BuscadorCuentas.Plano(p.ConceptoPropio).Contains(plano, StringComparison.Ordinal) || BuscadorCuentas.Plano(p.Documento).Contains(plano, StringComparison.Ordinal)))
            .Where(a => !soloDescuadrados || a.Apuntes.Sum(p => p.Debe) != a.Apuntes.Sum(p => p.Haber))
            .Take(Math.Clamp(maximo, 1, 200))
            .Select(Resumen)
            .ToList();
        return Resultado<IReadOnlyList<ResumenAsiento>>.Correcto(lista);
    }

    public async Task<Resultado<DetalleAsiento>> VerAsientoAsync(int numero)
    {
        var asiento = await new MotorAsientosAccess(_bd).ObtenerAsync(numero);
        if (asiento is null) return Resultado<DetalleAsiento>.Fallo(NoExiste(numero));
        var conIva = await new MotorAsientosAccess(_bd).TieneLibroIvaAsync(numero);
        var nombres = await NombresCuentasAsync(asiento.Contexto.IdSociedadEjercicio);
        var relaciones = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdAsientoOrigen, IdAsientoDestino, TipoRelacion FROM RelacionesAsientos WHERE IdAsientoOrigen = ? OR IdAsientoDestino = ?",
            f => f.Entero("IdAsientoOrigen") == numero ? $"{f.Texto("TipoRelacion")?.Trim()} → asiento {f.Entero("IdAsientoDestino")}" : $"{f.Texto("TipoRelacion")?.Trim()} de asiento {f.Entero("IdAsientoOrigen")}",
            numero, numero));
        return Resultado<DetalleAsiento>.Correcto(new DetalleAsiento(
            asiento.IdAsiento, asiento.Contexto.IdSociedad, asiento.Contexto.Ejercicio, asiento.FechaContable, asiento.Descripcion, asiento.Tipo.ToString(), asiento.Notas,
            asiento.Version, asiento.EsAutomatico, asiento.Lote?.ToString(), asiento.CorregidoAMano, conIva,
            asiento.Apuntes.OrderBy(a => a.Orden).Select(a => new ApunteConector(a.CodigoCuenta, nombres.GetValueOrDefault(a.CodigoCuenta, ""), a.Debe.Valor, a.Haber.Valor,
                a.Descripcion, a.NumeroDocumento, a.Punteado)).ToList(),
            relaciones, asiento.TotalDebe.Valor, asiento.TotalHaber.Valor, ComoSeCorrige(asiento.EsAutomatico, conIva, EsImportado(asiento))));
    }

    public async Task<Resultado<IReadOnlyList<CuentaConector>>> BuscarCuentasAsync(string idSociedad, int ejercicio, string texto, int maximo = 50)
    {
        var ctx = await ContextoAsync(idSociedad, ejercicio);
        if (!ctx.EsCorrecto) return Resultado<IReadOnlyList<CuentaConector>>.Fallo(ctx.Errores);
        var cuentas = await new CatalogoCuentasAccess(_bd).ListarAsync(ctx.Valor);
        var lista = BuscadorCuentas.Filtrar(cuentas, texto, null).Take(Math.Clamp(maximo, 1, 200))
            .Select(c => new CuentaConector(c.Codigo, c.Nombre, c.EsImputable && BuscadorCuentas.EsSubcuenta(c.Codigo), c.EstaActiva && !c.EstaBloqueada, c.SaldoActual, c.NumeroApuntes))
            .ToList();
        return Resultado<IReadOnlyList<CuentaConector>>.Correcto(lista);
    }

    public async Task<Resultado<MayorConector>> MayorAsync(string idSociedad, int ejercicio, string cuenta, DateTime? desde = null, DateTime? hasta = null, int maximo = 200)
    {
        var ctx = await ContextoAsync(idSociedad, ejercicio);
        if (!ctx.EsCorrecto) return Resultado<MayorConector>.Fallo(ctx.Errores);
        var codigo = BuscadorCuentas.ExpandirPunto(cuenta) ?? cuenta.Trim();
        var origen = await new ServicioPlantillasAsiento(_bd).OrigenSubcuentaAsync(ctx.Valor, codigo);
        if (!origen.EsCorrecto) return Resultado<MayorConector>.Fallo(origen.Errores);
        var datos = await DiarioAsync(idSociedad, ejercicio);
        if (!datos.EsCorrecto) return Resultado<MayorConector>.Fallo(datos.Errores);
        var movimientos = datos.Valor.Asientos.SelectMany(a => a.Apuntes.Where(p => p.Cuenta == codigo).Select(p => (a, p)))
            .Where(x => desde is null || x.a.Fecha >= desde.Value.Date)
            .Where(x => hasta is null || x.a.Fecha <= hasta.Value.Date)
            .Take(Math.Clamp(maximo, 1, 1000))
            .Select(x => new MovimientoConector(x.a.IdAsiento, x.a.Fecha, x.p.ConceptoPropio ?? x.a.Descripcion, x.p.Documento, x.p.Debe, x.p.Haber, x.p.Punteado))
            .ToList();
        return Resultado<MayorConector>.Correcto(new MayorConector(origen.Valor.Cuenta, origen.Valor.Nombre, origen.Valor.SaldoApertura, origen.Valor.Saldo, movimientos));
    }

    public async Task<Resultado<IReadOnlyList<AvisoConector>>> AvisosSaludAsync(string idSociedad, int ejercicio)
    {
        try
        {
            var informe = await new ServicioSalud(new FuenteSaludAccess(_bd), hoy: _hoy).RevisarAsync(idSociedad, ejercicio);
            var avisos = informe.Elementos.Select(e => new AvisoConector(e.Clave, e.Gravedad.ToString(), e.Titulo, e.Explicacion, e.ComoSeResuelve, e.Plazo, e.Importe)).ToList();
            foreach (var r in informe.Revisiones.Where(r => r.Error is not null))
                avisos.Add(new AvisoConector(r.Clave, "NoRevisado", $"No se ha podido revisar «{r.Nombre}»", r.Error!, "Revísalo en la pantalla de inicio de ProContaVF.", null, null));
            return Resultado<IReadOnlyList<AvisoConector>>.Correcto(avisos);
        }
        catch (InvalidOperationException ex)
        {
            return Resultado<IReadOnlyList<AvisoConector>>.Fallo(new Error("Conector.SinEjercicio", ex.Message, idSociedad, null, "Consulta las sociedades y sus ejercicios con «sociedades»."));
        }
    }

    // ───────────── Propuesta y grabación ─────────────

    /// <summary>
    /// Valida unos asientos manuales nuevos con el motor, sin grabarlos, y los guarda como propuesta numerada. Con <see cref="PeticionPropuesta.AsientoOrigen"/>
    /// son una corrección: se relacionan con él (tipo Rectificación) y el origen no puede ser de una factura ni de un proceso automático.
    /// </summary>
    public async Task<Resultado<Propuesta>> ProponerAsync(PeticionPropuesta peticion)
    {
        var errores = new List<Error>();
        if (string.IsNullOrWhiteSpace(peticion.Motivo) || peticion.Motivo.Trim().Length < 5)
            errores.Add(new Error("Conector.SinMotivo", "Falta el motivo del asiento.", null, "Todo asiento del asistente queda documentado con su motivo en las notas y en el registro de cambios.",
                "Escribe en «motivo» por qué se hace, con las palabras del operador."));
        if (peticion.Asientos.Count == 0)
            errores.Add(new Error("Conector.SinAsientos", "La propuesta no tiene ningún asiento.", null, null, "Añade al menos un asiento con sus apuntes."));
        if (errores.Count > 0) return Resultado<Propuesta>.Fallo(errores);

        var ctx = await ContextoAsync(peticion.IdSociedad, peticion.Ejercicio);
        if (!ctx.EsCorrecto) return Resultado<Propuesta>.Fallo(ctx.Errores);

        Asiento? origen = null;
        if (peticion.AsientoOrigen is int idOrigen)
        {
            var motor = new MotorAsientosAccess(_bd);
            origen = await motor.ObtenerAsync(idOrigen);
            if (origen is null) return Resultado<Propuesta>.Fallo(NoExiste(idOrigen));
            if (origen.Contexto.IdSociedad != ctx.Valor.IdSociedad)
                return Resultado<Propuesta>.Fallo(new Error("Conector.OrigenOtraSociedad", "El asiento de origen es de otra sociedad.", origen.ToString(),
                    $"Es de {origen.Contexto.IdSociedad} y la propuesta es de {ctx.Valor.IdSociedad}.", "Propón la corrección en la sociedad del asiento de origen."));
            var conIva = await motor.TieneLibroIvaAsync(idOrigen);
            if (conIva || (origen.EsAutomatico && !EsImportado(origen)))
                return Resultado<Propuesta>.Fallo(new Error("Conector.OrigenNoManual", conIva ? "Este asiento es de una factura: no se corrige con asientos sueltos." : "Este asiento lo genera un proceso automático: no se corrige con asientos sueltos.",
                    origen.ToString(), conIva ? "Su IVA sale del libro de IVA de la factura y entra en el 303; un asiento suelto lo descuadraría." : $"Es del lote {origen.Lote}; si el proceso se regenera, la corrección quedaría duplicada.",
                    ComoSeCorrige(origen.EsAutomatico, conIva, EsImportado(origen))));
        }

        var asientos = new List<Asiento>();
        foreach (var (p, i) in peticion.Asientos.Select((p, i) => (p, i)))
        {
            if (p.Apuntes.Count < 2)
                errores.Add(new Error("Conector.PocosApuntes", $"El asiento {i + 1} de la propuesta tiene menos de dos apuntes.", p.Descripcion, null, "Cada asiento necesita al menos un apunte al Debe y otro al Haber."));
            foreach (var x in p.Apuntes.Where(x => (x.Debe ?? 0m) != 0m == ((x.Haber ?? 0m) != 0m) || (x.Debe ?? 0m) < 0m || (x.Haber ?? 0m) < 0m))
                errores.Add(new Error("Conector.ApunteNoValido", $"Un apunte de la cuenta {x.Cuenta} del asiento {i + 1} no es válido.", p.Descripcion,
                    "Cada apunte lleva un importe positivo al Debe o al Haber, nunca a los dos ni ninguno.", "Pon el importe solo en «debe» o solo en «haber»; para restar, usa el lado contrario."));
            var asiento = new Asiento(ctx.Valor, p.Fecha, p.Descripcion.Trim())
            {
                Notas = $"{peticion.Motivo.Trim()} · {MarcaAsistente}",
                Tipo = origen is null ? TipoAsiento.Ordinario : TipoAsiento.Rectificacion,
            };
            foreach (var x in p.Apuntes)
            {
                var cuenta = BuscadorCuentas.ExpandirPunto(x.Cuenta) ?? x.Cuenta.Trim();
                var apunte = (x.Debe ?? 0m) != 0m ? Apunte.AlDebe(cuenta, x.Debe!.Value, x.Concepto) : Apunte.AlHaber(cuenta, x.Haber ?? 0m, x.Concepto);
                apunte.NumeroDocumento = string.IsNullOrWhiteSpace(x.Documento) ? null : x.Documento.Trim();
                apunte.Punteado = origen is not null && p.Anula;
                asiento.Apuntes.Add(apunte);
            }
            asientos.Add(asiento);
        }
        if (errores.Count > 0) return Resultado<Propuesta>.Fallo(errores);

        // Validación completa con el motor en una transacción que siempre se revierte.
        var simulado = await new MotorAsientosAccess(new BaseDatosSimulacion(_bd)).GuardarManualesAsync(Copiar(asientos), Actor, peticion.Motivo.Trim(), Opciones(origen, peticion));
        if (!simulado.EsCorrecto) return Resultado<Propuesta>.Fallo(simulado.Errores);

        var nombres = await NombresCuentasAsync(ctx.Valor.IdSociedadEjercicio);
        var numero = Interlocked.Increment(ref _ultimaPropuesta);
        var propuesta = new Propuesta(numero, peticion, ctx.Valor, origen?.IdAsiento, origen?.Version,
            asientos.Select(a => new AsientoPropuesto(a.FechaContable, a.Descripcion, a.Tipo.ToString(),
                a.Apuntes.Select(x => new ApunteConector(x.CodigoCuenta, nombres.GetValueOrDefault(x.CodigoCuenta, ""), x.Debe.Valor, x.Haber.Valor, x.Descripcion, x.NumeroDocumento, x.Punteado)).ToList(),
                a.TotalDebe.Valor, a.TotalHaber.Valor)).ToList());
        _propuestas[numero] = propuesta;
        return Resultado<Propuesta>.Correcto(propuesta);
    }

    /// <summary>Graba una propuesta validada, tal como se validó, si el asiento de origen no ha cambiado desde entonces.</summary>
    public async Task<Resultado<IReadOnlyList<int>>> GrabarAsync(int numeroPropuesta)
    {
        if (!_propuestas.TryGetValue(numeroPropuesta, out var propuesta))
            return Resultado<IReadOnlyList<int>>.Fallo(new Error("Conector.PropuestaNoExiste", $"No existe la propuesta {numeroPropuesta}.", null,
                "Las propuestas solo duran mientras Claude Desktop tiene abierto el conector.", "Vuelve a proponer los asientos con «proponer_asientos»."));
        if (propuesta.Grabada is { } ya)
            return Resultado<IReadOnlyList<int>>.Fallo(new Error("Conector.PropuestaGrabada", $"La propuesta {numeroPropuesta} ya está grabada.", null,
                $"Se grabó como asiento(s) {string.Join(", ", ya)}.", "No hace falta grabarla otra vez."));
        Asiento? origen = null;
        if (propuesta.AsientoOrigen is int idOrigen)
        {
            origen = await new MotorAsientosAccess(_bd).ObtenerAsync(idOrigen);
            if (origen is null || origen.Version != propuesta.VersionOrigen)
                return Resultado<IReadOnlyList<int>>.Fallo(new Error("Conector.OrigenCambiado", $"El asiento {idOrigen} ha cambiado desde la propuesta {numeroPropuesta}.", null,
                    origen is null ? "Se ha borrado." : "Alguien lo ha modificado después de proponer la corrección.", "Vuelve a leer el asiento y propón la corrección de nuevo."));
        }
        var asientos = propuesta.Asientos.Select(p =>
        {
            var a = new Asiento(propuesta.Contexto, p.Fecha, p.Descripcion)
            {
                Notas = $"{propuesta.Peticion.Motivo.Trim()} · {MarcaAsistente}",
                Tipo = origen is null ? TipoAsiento.Ordinario : TipoAsiento.Rectificacion,
            };
            foreach (var x in p.Apuntes)
            {
                var apunte = x.Debe != 0m ? Apunte.AlDebe(x.Cuenta, x.Debe, x.Concepto) : Apunte.AlHaber(x.Cuenta, x.Haber, x.Concepto);
                apunte.NumeroDocumento = x.Documento;
                apunte.Punteado = x.Punteado;
                a.Apuntes.Add(apunte);
            }
            return a;
        }).ToList();
        var grabado = await new MotorAsientosAccess(_bd).GuardarManualesAsync(asientos, Actor, propuesta.Peticion.Motivo.Trim(), Opciones(origen, propuesta.Peticion));
        if (grabado.EsCorrecto) _propuestas[numeroPropuesta] = propuesta with { Grabada = grabado.Valor };
        return grabado;
    }

    // ───────────── Apoyo ─────────────

    private static OpcionesAltaManual? Opciones(Asiento? origen, PeticionPropuesta peticion) =>
        origen is null ? null : new OpcionesAltaManual(origen.IdAsiento,
            PuntearApuntes: peticion.Asientos.Any(a => a.Anula) ? origen.Apuntes.Select(a => a.IdApunte).ToList() : null);

    private static List<Asiento> Copiar(IEnumerable<Asiento> asientos) => asientos.Select(a =>
    {
        var c = new Asiento(a.Contexto, a.FechaContable, a.Descripcion) { Notas = a.Notas, Tipo = a.Tipo };
        foreach (var x in a.Apuntes)
        {
            var apunte = x.Debe.Valor != 0m ? Apunte.AlDebe(x.CodigoCuenta, x.Debe.Valor, x.Descripcion) : Apunte.AlHaber(x.CodigoCuenta, x.Haber.Valor, x.Descripcion);
            apunte.NumeroDocumento = x.NumeroDocumento;
            apunte.Punteado = x.Punteado;
            c.Apuntes.Add(apunte);
        }
        return c;
    }).ToList();

    /// <summary>
    /// Asiento copiado de ContaSol en la implantación (implantacion/importador-contasol): es de un lote, pero ese lote no se regenera en el día a día,
    /// así que un asiento importado sin libro de IVA se corrige con asientos manuales como uno manual.
    /// </summary>
    internal static bool EsImportado(Asiento a) => a.Lote?.MotorPropietario == OpcionesLote.MotorImportadorContaSol;

    internal static string ComoSeCorrige(bool automatico, bool conLibroIva, bool importado = false) => (conLibroIva, importado, automatico) switch
    {
        (true, true, _) => "Es un asiento importado de ContaSol con libro de IVA: su IVA no se cambia con asientos sueltos, porque el 303 y los libros saldrían descuadrados. " +
            "Las cuentas que no son de IVA se pueden cambiar con «Corregir a mano» en el Diario de ProContaVF; para el IVA, consulta antes con la gestoría.",
        (true, false, _) => "Es de una factura: corrige la factura (compras: en Compras › Facturas recibidas, se regenera su asiento; ventas: con una factura rectificativa en Facturación). " +
            "Para cambiar solo una cuenta que no sea de IVA, usa «Corregir a mano» en el Diario.",
        (false, true, _) or (false, false, false) => "Se corrige con un asiento que lo anula (las mismas cuentas con Debe y Haber cambiados, anula = true) y, si hace falta, otro con los importes correctos, los dos con asientoOrigen.",
        _ => "Lo genera un proceso automático: corrígelo en su proceso de origen (se regenera el asiento) o con «Corregir a mano» en el Diario de ProContaVF.",
    };

    private async Task<Resultado<ContextoSociedadEjercicio>> ContextoAsync(string idSociedad, int ejercicio)
    {
        var id = (idSociedad ?? "").Trim();
        var ejercicios = await new FuenteDiarioAccess(_bd).EjerciciosAsync(id);
        if (ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio) is { } e)
            return Resultado<ContextoSociedadEjercicio>.Correcto(new ContextoSociedadEjercicio(id, e.IdSociedadEjercicio, e.Ejercicio));
        return Resultado<ContextoSociedadEjercicio>.Fallo(new Error("Conector.SinEjercicio", $"La sociedad «{id}» no tiene el ejercicio {ejercicio}.", null,
            ejercicios.Count == 0 ? "No hay ninguna sociedad con ese código." : $"Sus ejercicios son {string.Join(", ", ejercicios.Select(x => x.Ejercicio))}.",
            "Consulta las sociedades y sus ejercicios con «sociedades»."));
    }

    private async Task<Resultado<DatosDiario>> DiarioAsync(string idSociedad, int ejercicio)
    {
        var fuente = new FuenteDiarioAccess(_bd);
        var ejercicios = await fuente.EjerciciosAsync((idSociedad ?? "").Trim());
        var e = ejercicios.Where(x => x.Ejercicio == ejercicio).ToList();
        if (e.Count == 0)
        {
            var ctx = await ContextoAsync(idSociedad ?? "", ejercicio);
            return Resultado<DatosDiario>.Fallo(ctx.Errores);
        }
        return Resultado<DatosDiario>.Correcto(await fuente.CargarAsync(idSociedad!.Trim(), e));
    }

    private Task<Dictionary<string, string>> NombresCuentasAsync(int idSociedadEjercicio) =>
        _bd.EjecutarAsync(async s => (await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (C: (f.Texto("Codigo") ?? "").Trim(), N: (f.Texto("Nombre") ?? "").Trim()), idSociedadEjercicio))
            .GroupBy(x => x.C).ToDictionary(g => g.Key, g => g.First().N));

    private static ResumenAsiento Resumen(AsientoDiario a) => new(a.IdAsiento, a.Fecha, a.Descripcion, a.Apuntes.Sum(p => p.Debe), a.Apuntes.Sum(p => p.Haber),
        a.EsAutomatico ? $"{a.MotorPropietario}/{a.CodigoProceso}" : "Manual", a.ConLibroIva, a.Apuntes.Count,
        string.Join(", ", a.Apuntes.Select(p => p.Cuenta).Distinct().Take(6)));

    private static Error NoExiste(int numero) => new("Conector.AsientoNoExiste", $"No existe el asiento {numero}.", null,
        "El número de asiento es el que se ve en el Diario (columna Nº).", "Búscalo con «buscar_asientos» por fecha, cuenta o texto.");
}

public sealed record EjercicioConector(int Ejercicio, bool Cerrado, int? TrimestreActivo);
public sealed record SociedadConector(string IdSociedad, string Nombre, IReadOnlyList<EjercicioConector> Ejercicios);
public sealed record ResumenAsiento(int Numero, DateTime Fecha, string Descripcion, decimal Debe, decimal Haber, string Origen, bool DeFactura, int Apuntes, string Cuentas);
public sealed record ApunteConector(string Cuenta, string NombreCuenta, decimal Debe, decimal Haber, string? Concepto, string? Documento, bool Punteado);
public sealed record DetalleAsiento(int Numero, string IdSociedad, int Ejercicio, DateTime Fecha, string Descripcion, string Tipo, string? Notas, int Version,
    bool Automatico, string? Lote, bool CorregidoAMano, bool DeFactura, IReadOnlyList<ApunteConector> Apuntes, IReadOnlyList<string> Relaciones,
    decimal TotalDebe, decimal TotalHaber, string ComoSeCorrige);
public sealed record CuentaConector(string Codigo, string Nombre, bool Imputable, bool Activa, decimal Saldo, int Apuntes);
public sealed record MovimientoConector(int Asiento, DateTime Fecha, string Concepto, string? Documento, decimal Debe, decimal Haber, bool Punteado);
public sealed record MayorConector(string Cuenta, string Nombre, decimal SaldoApertura, decimal SaldoActual, IReadOnlyList<MovimientoConector> Movimientos);
public sealed record AvisoConector(string Clave, string Gravedad, string Titulo, string Explicacion, string ComoSeResuelve, DateTime? Plazo, decimal? Importe);

/// <summary>Apunte de una petición de propuesta: un importe positivo al Debe o al Haber.</summary>
public sealed record ApuntePedido(string Cuenta, decimal? Debe, decimal? Haber, string? Concepto = null, string? Documento = null);

/// <summary>Asiento de una petición. <see cref="Anula"/>: es el que anula el de origen (sus apuntes y los del origen quedan punteados).</summary>
public sealed record AsientoPedido(DateTime Fecha, string Descripcion, IReadOnlyList<ApuntePedido> Apuntes, bool Anula = false);

public sealed record PeticionPropuesta(string IdSociedad, int Ejercicio, string Motivo, IReadOnlyList<AsientoPedido> Asientos, int? AsientoOrigen = null);

public sealed record AsientoPropuesto(DateTime Fecha, string Descripcion, string Tipo, IReadOnlyList<ApunteConector> Apuntes, decimal TotalDebe, decimal TotalHaber);

public sealed record Propuesta(int Numero, PeticionPropuesta Peticion, ContextoSociedadEjercicio Contexto, int? AsientoOrigen, int? VersionOrigen,
    IReadOnlyList<AsientoPropuesto> Asientos)
{
    public IReadOnlyList<int>? Grabada { get; init; }
}
