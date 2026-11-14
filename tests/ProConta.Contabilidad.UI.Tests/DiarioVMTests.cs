using System.IO;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.UI.Diario;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.UI.Tests;

/// <summary>Pantalla Diario sin BD (contabilidad/diario): vistas, filtros, selección, ficha, acciones del menú y exportación.</summary>
public class DiarioVMTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly EjercicioDiario E2025 = new(9000, 2025, false, null);
    private static readonly EjercicioDiario E2026 = new(9001, 2026, false, 4);

    /// <summary>Fuente en memoria: 2025 con un asiento y 2026 con una venta de febrero (con libro de IVA), su cobro por banco y dos manuales.</summary>
    private sealed class Fuente : IFuenteDiario
    {
        public int Cargas { get; private set; }
        public bool Falla { get; set; }
        public List<AsientoDiario> Asientos { get; } = Crear();

        public static List<AsientoDiario> Crear()
        {
            var id = 100;
            AsientoDiario A(int n, EjercicioDiario e, DateTime fecha, string descripcion, string? motor, bool iva, params (string Cuenta, decimal Debe, decimal Haber, string? Doc)[] lineas)
            {
                var a = new AsientoDiario
                {
                    IdAsiento = n, Ejercicio = e, Fecha = fecha, Descripcion = descripcion, EsAutomatico = motor is not null, MotorPropietario = motor, ClaveEjecucion = motor is null ? null : "55",
                    ConLibroIva = iva, Version = 3,
                };
                var orden = 0;
                return a.Con(lineas.Select(l => new ApunteDiario { IdApunte = ++id, Orden = ++orden, Cuenta = l.Cuenta, NombreCuenta = "Cuenta " + l.Cuenta, Debe = l.Debe, Haber = l.Haber, Documento = l.Doc }).ToArray());
            }
            return
            [
                A(1, E2025, new DateTime(2025, 12, 20), "Gasto de 2025", null, false, ("6290001", 50m, 0m, null), ("5720001", 0m, 50m, null)),
                A(10, E2026, new DateTime(2026, 2, 3), "Factura F26-0001", "facturacion/factura-venta", true, ("4300001", 121m, 0m, "F26-0001"), ("7050001", 0m, 100m, "F26-0001"), ("4770021", 0m, 21m, "F26-0001")),
                A(11, E2026, new DateTime(2026, 2, 9), "Cobro F26-0001", "norma43/conciliacion", false, ("5720001", 121m, 0m, null), ("4300001", 0m, 121m, "F26-0001")),
                A(14, E2026, new DateTime(2026, 11, 5), "Ingreso sin identificar", null, false, ("5720002", 350m, 0m, null), ("5550001", 0m, 350m, null)),
                A(15, E2026, new DateTime(2026, 11, 6), "Seguro", null, false, ("6250001", 80m, 0m, "POL-1"), ("5720002", 0m, 80m, "POL-1")),
            ];
        }

        public Task<IReadOnlyList<EjercicioDiario>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<EjercicioDiario>>([E2025, E2026]);

        public Task<DatosDiario> CargarAsync(string idSociedad, IReadOnlyList<EjercicioDiario> ejercicios, CancellationToken cancelacion = default)
        {
            Cargas++;
            if (Falla) throw new InvalidOperationException("fichero bloqueado");
            return Task.FromResult(new DatosDiario(ejercicios, Asientos.ToList()));
        }
    }

    /// <summary>Motor que anota lo que se le pide y responde lo que se le diga; lo que el Diario no usa lanza.</summary>
    private sealed class Motor : IMotorAsientos
    {
        public readonly List<string> Llamadas = [];
        public Error? Error { get; set; }
        public bool Conflicto { get; set; }
        public CambioDatosApunte? UltimoCambio { get; private set; }
        public IReadOnlyList<AsientoBorrado> Borrados { get; set; } = [];
        private Task<Resultado<Guid>> Hecho(string llamada)
        {
            Llamadas.Add(llamada);
            return Task.FromResult(Error is { } e ? Resultado<Guid>.Fallo(e) : Resultado<Guid>.Correcto(Guid.NewGuid()));
        }
        private static T No<T>() => throw new NotSupportedException("El Diario no debe usar esta operación.");

        public Task<Resultado<Guid>> PuntearApuntesAsync(IReadOnlyCollection<int> idsApunte, bool punteado, Actor actor, string? motivo = null) => Hecho($"puntear {string.Join(",", idsApunte)} {punteado}");
        public Task<Resultado<Guid>> AsignarNumeroDocumentoAsync(IReadOnlyCollection<int> idsApunte, string? numero, Actor actor, string motivo) => Hecho($"documento {string.Join(",", idsApunte)} «{numero}» ({motivo})");
        public Task<Resultado<Guid>> CambiarConceptoApuntesAsync(IReadOnlyCollection<int> idsApunte, string? concepto, Actor actor, string motivo) => Hecho($"concepto {string.Join(",", idsApunte)} «{concepto}» ({motivo})");
        public Task<Resultado<Guid>> CambiarCuentaApuntesAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsApunte, string nuevaCuenta, Actor actor, string motivo) =>
            Hecho($"cuenta {contexto.Ejercicio} {string.Join(",", idsApunte)} {nuevaCuenta} ({motivo})");
        public Task<Resultado<Guid>> CambiarDatosApuntesAsync(IReadOnlyCollection<int> idsApunte, CambioDatosApunte cambio, Actor actor, string? motivo = null)
        {
            UltimoCambio = cambio;
            return Hecho($"datos {string.Join(",", idsApunte)} ({motivo})");
        }
        public Task<Resultado> EliminarManualAsync(int idAsiento, int versionLeida, Actor actor, string motivo)
        {
            Llamadas.Add($"eliminar {idAsiento} v{versionLeida} ({motivo})");
            if (Conflicto) throw new ConflictoConcurrenciaException(new Error("Datos.ConflictoVersion", "Otra persona ha modificado este asiento.", $"Asiento {idAsiento}", null, "Vuelve a cargarlo."));
            return Task.FromResult(Error is { } e ? Resultado.Fallo(e) : Resultado.Correcto());
        }
        public Task<IReadOnlyList<AsientoBorrado>> AsientosBorradosAsync(IReadOnlyCollection<int> idsSociedadEjercicio)
        {
            Llamadas.Add($"borrados {string.Join(",", idsSociedadEjercicio)}");
            return Task.FromResult(Borrados);
        }
        public Task<IReadOnlyList<CambioApunte>> RegistroCambiosAsync(int idAsiento) => Task.FromResult<IReadOnlyList<CambioApunte>>(
        [
            new CambioApunte(Guid.Empty, new DateTime(2026, 11, 5, 10, 0, 0), "ronald", "Alta manual", idAsiento, null, 1, "ALTA", "Apunte", null, "5720002 D 350"),
        ]);

        public Task<Asiento?> ObtenerAsync(int idAsiento) => No<Task<Asiento?>>();
        public Task<Resultado<Asiento>> GuardarManualAsync(Asiento asiento, Actor actor, string? motivo = null) => No<Task<Resultado<Asiento>>>();
        public Task<Resultado<IReadOnlyList<int>>> GuardarManualesAsync(IReadOnlyList<Asiento> asientos, Actor actor, string motivo, OpcionesAltaManual? opciones = null) => No<Task<Resultado<IReadOnlyList<int>>>>();
        public Task<Resultado<Asiento>> CorregirAutomaticoAsync(Asiento asiento, Actor actor, string motivo) => No<Task<Resultado<Asiento>>>();
        public Task<bool> TieneLibroIvaAsync(int idAsiento) => No<Task<bool>>();
        public Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
            TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null) => No<Task<Resultado<IReadOnlyList<int>>>>();
        public Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
            OpcionesLote? opciones = null) => No<Task<Resultado<int>>>();
        public Task RecalcularSaldosAsync(ContextoSociedadEjercicio contexto) => No<Task>();
        public Task<Resultado<ResultadoCargaLibro>> CargarLibroExternoAsync(ContextoSociedadEjercicio contexto, string motorPropietario, string codigoProceso,
            IReadOnlyList<AsientoExterno> asientos, Actor actor, string motivo, Func<ISesionDatos, IReadOnlyList<int>, Task>? antesDeBorrar = null,
            CancellationToken cancelacion = default) => No<Task<Resultado<ResultadoCargaLibro>>>();
    }

    private sealed class Centros : ICatalogoCentrosCoste
    {
        public Task<IReadOnlyList<CentroCoste>> ListarAsync(string idSociedad) => Task.FromResult<IReadOnlyList<CentroCoste>>(
            [new CentroCoste(3, "T01", null, "APT", "Apartamentos", true, true, 0, "APT"), new CentroCoste(4, "T01", null, "OLD", "Inactivo", true, false, 0, "OLD")]);
        public Task<CentroCoste?> ObtenerAsync(int idCentroCoste) => throw new NotSupportedException();
        public Task<Resultado<CentroCoste>> AltaAsync(string idSociedad, string codigo, string nombre, int? idPadre = null, bool esImputable = true) => throw new NotSupportedException();
        public Task<Resultado> ComprobarImputableAsync(string idSociedad, int idCentroCoste) => throw new NotSupportedException();
    }

    private sealed class Dialogos : IDialogosDiario
    {
        public readonly List<string> Errores = [];
        public (string Valor, string Motivo)? Texto { get; set; }
        public string? Motivo { get; set; } = "Motivo de prueba";
        public (string Codigo, string Nombre)? Cuenta { get; set; }
        public int? IdCentro { get; set; }
        public bool CancelaCentro { get; set; }
        public string? MotivoBorrado { get; set; } = "Grabado dos veces";
        public FormatoExportacionDiario? Formato { get; set; }
        public string? Ruta { get; set; }
        public string? Copiado { get; private set; }
        public int VecesBorrados { get; private set; }
        public IReadOnlyList<DiferenciaApertura>? Diferencias { get; private set; }
        public readonly List<string> Pedidos = [];

        public void Error(string titulo, string mensaje) => Errores.Add($"{titulo}: {mensaje}");
        public (string Valor, string Motivo)? PedirTexto(string titulo, string explicacion, string etiqueta, string? inicial, int longitud, bool pideMotivo, bool valorObligatorio)
        {
            Pedidos.Add($"{titulo} [{inicial}]");
            return Texto;
        }
        public string? PedirMotivo(string titulo, string explicacion) { Pedidos.Add(titulo); return Motivo; }
        public (string Codigo, string Nombre)? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto) { Pedidos.Add($"cuenta {ejercicio.Ejercicio}"); return Cuenta; }
        public (CentroDiario Centro, string Motivo)? ElegirCentro(IReadOnlyList<CentroDiario> centros, string explicacion) =>
            CancelaCentro ? null : (centros.First(c => c.Id == IdCentro), "Centro de prueba");
        public string? ConfirmarBorrado(IReadOnlyList<AsientoDiario> asientos) { Pedidos.Add($"borrar {string.Join(",", asientos.Select(a => a.IdAsiento))}"); return MotivoBorrado; }
        public void VerBorrados(IReadOnlyList<AsientoBorrado> borrados, Func<AsientoBorrado, ApunteBorrado, string> nombreCuenta, string ejercicios) => VecesBorrados++;
        public FormatoExportacionDiario? ElegirExportacion(string explicacion, bool hayPdf) => Formato;
        public string? PedirRuta(string titulo, string nombrePropuesto, string filtro) => Ruta;
        public void VerDiferenciasApertura(IReadOnlyList<DiferenciaApertura> diferencias, int anterior, int trabajo) => Diferencias = diferencias;
        public void Copiar(string texto) => Copiado = texto;
    }

    private sealed record Montaje(DiarioVM Vm, Fuente Fuente, Motor Motor, Dialogos Dialogos, List<string> Avisos);

    private static async Task<Montaje> CrearAsync(Action<Fuente>? preparar = null)
    {
        var fuente = new Fuente();
        preparar?.Invoke(fuente);
        var motor = new Motor();
        var dialogos = new Dialogos();
        var avisos = new List<string>();
        var vm = new DiarioVM(new ContextoDiario(fuente, motor, new Centros(), Ctx, "Sociedad sintética", new Actor("ronald"), avisos.Add), dialogos);
        await vm.IniciarAsync();
        return new Montaje(vm, fuente, motor, dialogos, avisos);
    }

    private static string Euros(decimal importe) => ProConta.UI.Base.Formato.Formatos.Importe(importe);
    private static FilaDiarioVM Fila(DiarioVM vm, int asiento, int orden) => vm.Filas.Single(f => f.Apunte is { } a && a.Asiento.IdAsiento == asiento && a.Orden == orden);
    private static FilaDiarioVM Cabecera(DiarioVM vm, int asiento) => vm.Filas.Single(f => f.EsCabecera && f.Asiento!.IdAsiento == asiento);
    private static void Seleccionar(DiarioVM vm, params FilaDiarioVM[] filas) => vm.CambiarSeleccion(filas);

    [Fact]
    public async Task Abre_con_el_ejercicio_de_trabajo_por_asiento_y_cuadrado()
    {
        var (vm, fuente, _, _, _) = await CrearAsync();
        Assert.Equal(1, fuente.Cargas);
        Assert.True(vm.PorAsiento);
        Assert.True(vm.EnEjercicio);
        Assert.Equal(("01/01/2026", "31/12/2026"), (vm.DesdeTexto, vm.HastaTexto));
        // Cuatro asientos de 2026: su cabecera y sus apuntes; el de 2025 no entra hasta cambiar el periodo.
        Assert.Equal(4, vm.Filas.Count(f => f.EsCabecera));
        Assert.Equal(9, vm.Filas.Count(f => f.EsApunte));
        Assert.Equal($"4 asientos · 9 apuntes · Debe {Euros(672m)} · Haber {Euros(672m)}", vm.Resumen);
        Assert.Equal("Cuadrado", vm.Cuadre);
        Assert.Equal(ProConta.UI.Base.Controles.EstadoVista.Contenido, vm.Estado);
        // La cabecera de la factura dice que su trimestre está cerrado; sus apuntes no repiten número ni fecha.
        Assert.Contains("trimestre cerrado", Cabecera(vm, 10).Concepto);
        Assert.Equal(("10", "03/02/2026", "Ventas"), (Cabecera(vm, 10).NumeroAsiento, Cabecera(vm, 10).Fecha, Cabecera(vm, 10).Origen));
        Assert.Equal(("", "", "121,00"), (Fila(vm, 10, 1).NumeroAsiento, Fila(vm, 10, 1).Fecha, Fila(vm, 10, 1).Debe));

        vm.PeriodoCommand.Execute(DiarioVM.PeriodoAnterior);
        Assert.True(vm.EnAnterior);
        Assert.Equal([1], vm.Filas.Where(f => f.EsCabecera).Select(f => f.Asiento!.IdAsiento));
        vm.PeriodoCommand.Execute("1");
        Assert.Equal(("01/01/2026", "31/03/2026"), (vm.DesdeTexto, vm.HastaTexto));
        Assert.Equal([10, 11], vm.Filas.Where(f => f.EsCabecera).Select(f => f.Asiento!.IdAsiento));
    }

    [Fact]
    public async Task Las_dos_vistas_ensenan_lo_mismo_de_dos_maneras_y_el_filtro_resalta_o_deja_solo_lo_que_cumple()
    {
        var (vm, _, _, _, _) = await CrearAsync();
        vm.Cuenta = "430";
        // Por asiento: los dos asientos completos, con los apuntes de la 430 resaltados y el resto apagado.
        Assert.Equal(2, vm.Filas.Count(f => f.EsCabecera));
        Assert.Equal(2, vm.Filas.Count(f => f.Coincide));
        Assert.Equal(3, vm.Filas.Count(f => f.Apagada));
        Assert.Contains("1 de 3 cumplen el filtro", Cabecera(vm, 10).Concepto);
        Assert.Single(vm.Fichas);
        Assert.StartsWith("Cuenta: 430", vm.Fichas[0].Texto);

        vm.VistaCommand.Execute("lista");
        Assert.True(vm.EnLista);
        Assert.All(vm.Filas, f => Assert.True(f.EsApunte));
        Assert.Equal(["10", "11"], vm.Filas.Select(f => f.NumeroAsiento));        // en lista cada fila lleva su número
        Assert.EndsWith($"Saldo {Euros(0m)}", vm.Resumen);
        Assert.False(vm.HayCuadre);

        vm.Fichas[0].Quitar.Execute(null);
        Assert.Equal("", vm.Cuenta);
        Assert.Equal(9, vm.Filas.Count);

        vm.TextoFiltro = "zzz";
        Assert.Equal(ProConta.UI.Base.Controles.EstadoVista.Vacio, vm.Estado);
        Assert.Equal("Quitar los filtros", vm.TextoAccionEstado);
        Assert.Contains("entre el 01/01/2026 y el 31/12/2026", vm.MensajeEstado);
        vm.QuitarFiltrosCommand.Execute(null);
        Assert.Equal(9, vm.Filas.Count);
    }

    [Fact]
    public async Task Plegar_totales_por_mes_y_fechas_escritas()
    {
        var (vm, _, _, _, _) = await CrearAsync();
        vm.AlternarPlegadoCommand.Execute(Cabecera(vm, 10));
        Assert.True(Cabecera(vm, 10).Plegado);
        Assert.DoesNotContain(vm.Filas, f => f.Apunte?.Asiento.IdAsiento == 10);
        vm.PlegarTodoCommand.Execute(null);
        Assert.All(vm.Filas, f => Assert.True(f.EsCabecera));
        vm.DesplegarTodoCommand.Execute(null);
        Assert.Equal(9, vm.Filas.Count(f => f.EsApunte));

        vm.TotalesPorMes = true;
        var meses = vm.Filas.Where(f => f.EsTotalMes).ToList();
        Assert.Equal(["Total de febrero de 2026 · 2 asientos", "Total de noviembre de 2026 · 2 asientos"], meses.Select(m => m.Concepto));
        Assert.Equal(("242,00", "242,00"), (meses[0].Debe, meses[0].Haber));
        Assert.True(vm.Filas[^1].EsTotalMes);

        vm.HastaTexto = "31/02/2026";
        Assert.Contains("no válida", vm.ErrorFechas);
        vm.HastaTexto = "28/2/26";
        Assert.Null(vm.ErrorFechas);
        Assert.Equal("", vm.Periodo);
        Assert.Equal(2, vm.Filas.Count(f => f.EsCabecera));
        vm.DesdeTexto = "01/03/2026";
        Assert.Contains("posterior", vm.ErrorFechas);
    }

    [Fact]
    public async Task La_seleccion_suma_y_la_ficha_ensena_el_asiento_o_la_suma_por_cuenta()
    {
        var (vm, _, _, _, _) = await CrearAsync();
        Assert.True(vm.FichaVacia);
        Seleccionar(vm, Fila(vm, 14, 1));
        Assert.True(vm.FichaAsiento);
        Assert.Equal(("Asiento 14", "Manual"), (vm.TituloFicha, vm.OrigenFicha));
        Assert.Null(vm.InfoFicha);
        Assert.Equal([true, false], vm.ApuntesFicha.Select(l => l.Seleccionada));
        Assert.Contains("Debe 350,00 = Haber 350,00", vm.TotalesFicha);
        Assert.True(vm.HayEdicion);
        Assert.Equal($"Selección: 1 apunte de 1 asiento · Debe {Euros(350m)} · Haber {Euros(0m)} · Diferencia {Euros(350m)}", vm.ResumenSeleccion);
        await Task.Yield();
        Assert.Equal("Alta del asiento", Assert.Single(vm.Historial).Texto);

        // La cabecera de un asiento selecciona todos sus apuntes; con apuntes de varios asientos, la suma por cuenta.
        Seleccionar(vm, Cabecera(vm, 10), Fila(vm, 11, 2));
        Assert.True(vm.FichaVarios);
        Assert.Equal(4, vm.Seleccion.Count);
        Assert.Equal("4 apuntes de 2 asientos", vm.TituloFicha);
        var suma430 = vm.SumasFicha.Single(s => s.Cuenta == "4300001");
        Assert.Equal((121m, 121m, 2), (suma430.ValorDebe, suma430.ValorHaber, suma430.Apuntes));
        Assert.False(vm.HayEdicion);
        Assert.Equal("4 apuntes de 2 asientos", vm.TituloMenu);

        // Un automático avisa de qué se cambia aquí y qué no.
        Seleccionar(vm, Fila(vm, 10, 3));
        Assert.Contains("libro de IVA", vm.InfoFicha);
        Assert.Contains(vm.DatosFicha, d => d.Etiqueta == "Bloqueo");
    }

    [Fact]
    public async Task El_menu_se_activa_segun_la_seleccion_y_dice_por_que_no()
    {
        var (vm, _, _, _, _) = await CrearAsync();
        var abiertos = new List<(int Asiento, bool Modificar)>();
        vm.AbrirAsiento = (a, m) => abiertos.Add((a.IdAsiento, m));
        vm.AsistenteCorreccion = _ => { };
        vm.UsarComoBase = _ => { };
        vm.AbrirMayor = (_, _) => { };
        Assert.Equal("Selecciona algún apunte", vm.AccionPuntear.Motivo);

        // Apunte de banco de un automático: no cambia de cuenta ni se borra; sí se puntea, se abre y se modifica su asiento.
        Seleccionar(vm, Fila(vm, 11, 1));
        Assert.Equal("Apunte 1 del asiento 11 · 5720001", vm.TituloMenu);
        Assert.True(vm.AccionPuntear.Habilitada && vm.AccionDocumento.Habilitada && vm.AccionConcepto.Habilitada && vm.AccionCentro.Habilitada);
        Assert.Contains("572", vm.AccionCuenta.Motivo);
        Assert.Contains("desde su origen", vm.AccionEliminar.Motivo);
        Assert.Equal("Abrir el asiento 11", vm.AccionAbrir.Texto);
        Assert.True(vm.AccionModificar.Habilitada);
        Assert.Equal("Ir a la contrapartida (4300001)", vm.AccionContrapartida.Texto);
        vm.AbrirFila(Fila(vm, 11, 1));
        vm.Abrir(modificar: true);
        Assert.Equal([(11, false), (11, true)], abiertos);

        // Línea de IVA de una factura en un trimestre cerrado.
        Seleccionar(vm, Fila(vm, 10, 3));
        Assert.Contains("Trimestre cerrado", vm.AccionCuenta.Motivo);
        Assert.True(vm.AccionConcepto.Habilitada);                           // los textos no tienen efecto fiscal

        // Manual: se elimina; con dos asientos, el texto lo dice y las acciones de un solo asiento se apagan.
        Seleccionar(vm, Fila(vm, 14, 2));
        Assert.Equal("Eliminar el asiento 14…", vm.AccionEliminar.Texto);
        Assert.True(vm.AccionEliminar.Habilitada && vm.AccionCuenta.Habilitada);
        Assert.Equal("Asiento manual, sin origen", vm.AccionOrigen.Motivo);
        Seleccionar(vm, Cabecera(vm, 14), Cabecera(vm, 15));
        Assert.Equal("Eliminar los 2 asientos…", vm.AccionEliminar.Texto);
        Assert.Equal("Solo con apuntes de un asiento", vm.AccionAbrir.Motivo);
        Assert.Equal("Los apuntes son de cuentas distintas", vm.AccionCuenta.Motivo);
        Assert.Equal("Ver el Mayor de las 3 cuentas", vm.AccionMayor.Texto);

        // El asistente de corrección crea asientos en el ejercicio de trabajo: un apunte de 2025 no vale.
        vm.PeriodoCommand.Execute(DiarioVM.PeriodoAnterior);
        Seleccionar(vm, Fila(vm, 1, 1));
        Assert.Contains("2025", vm.AccionCorreccion.Motivo);
    }

    [Fact]
    public async Task Puntear_no_vuelve_a_leer_y_el_numero_de_factura_sigue_la_regla_del_Mayor()
    {
        var (vm, fuente, motor, dialogos, _) = await CrearAsync();
        Seleccionar(vm, Fila(vm, 14, 1), Fila(vm, 14, 2));
        Assert.Equal("Puntear", vm.AccionPuntear.Texto);
        await vm.PuntearAsync();
        Assert.Equal(1, fuente.Cargas);                                      // F4 no relee la BD
        Assert.Equal(["✓", "✓"], new[] { Fila(vm, 14, 1).Punteado, Fila(vm, 14, 2).Punteado });
        Assert.Equal("Quitar el punteo", vm.AccionPuntear.Texto);
        Assert.EndsWith("True", motor.Llamadas[^1]);
        await vm.PuntearAsync();
        Assert.Equal("", Fila(vm, 14, 1).Punteado);

        // Solo un apunte de la selección tiene número: pasa a los demás sin preguntar.
        Seleccionar(vm, Fila(vm, 11, 1), Fila(vm, 11, 2));
        await vm.AsignarDocumentoAsync();
        Assert.Empty(dialogos.Pedidos);
        Assert.Contains("«F26-0001» (Mismo número que el apunte 2 del asiento 11)", motor.Llamadas[^1]);
        Assert.Equal(2, fuente.Cargas);

        // Ninguno tiene número: se pide; cancelar no escribe nada.
        Seleccionar(vm, Fila(vm, 14, 1));
        var antes = motor.Llamadas.Count;
        await vm.AsignarDocumentoAsync();
        Assert.Equal(antes, motor.Llamadas.Count);
        dialogos.Texto = ("ING-7", "Identificado");
        await vm.AsignarDocumentoAsync();
        Assert.Contains("«ING-7» (Identificado)", motor.Llamadas[^1]);
    }

    [Fact]
    public async Task La_ficha_guarda_solo_lo_que_cambia_y_pide_motivo_salvo_para_el_punteo()
    {
        var (vm, fuente, motor, dialogos, _) = await CrearAsync();
        Seleccionar(vm, Fila(vm, 15, 1));
        Assert.Equal(("Seguro", "POL-1", "", false), (vm.EdConcepto, vm.EdDocumento, vm.EdVencimiento, vm.EdPunteado));
        Assert.Equal(["Sin centro", "APT · Apartamentos"], vm.CentrosFicha.Select(c => c.Texto));   // solo activos e imputables
        Assert.False(vm.FichaCambiada);
        Assert.False(vm.TieneCambios);

        vm.EdPunteado = true;
        Assert.True(vm.PuedeGuardarFicha);
        Assert.True(await vm.GuardarFichaAsync());
        Assert.Empty(dialogos.Pedidos);                                       // solo punteo: sin motivo
        Assert.True(motor.UltimoCambio is { SoloPunteo: true, Punteado: true });

        Seleccionar(vm, Fila(vm, 15, 1));
        vm.EdConcepto = "Seguro de RC 2026";
        vm.EdVencimiento = "31/13/2026";
        Assert.True(vm.FichaCambiada);
        Assert.False(vm.PuedeGuardarFicha);
        Assert.Contains("vencimiento", vm.AyudaGuardarFicha);
        vm.EdVencimiento = "30/11/2026";
        vm.EdCentro = vm.CentrosFicha[1];
        Assert.Equal(["Cambios sin guardar en el apunte 1 del asiento 15"], vm.DescribirCambios());

        dialogos.Motivo = null;                                               // cancelar el motivo no guarda y conserva lo escrito
        var escrituras = motor.Llamadas.Count;
        Assert.False(await vm.GuardarFichaAsync());
        Assert.Equal(escrituras, motor.Llamadas.Count);
        Assert.Equal("Seguro de RC 2026", vm.EdConcepto);

        dialogos.Motivo = "Completar la ficha";
        Assert.True(await vm.GuardarFichaAsync());
        var cambio = motor.UltimoCambio!;
        Assert.Equal((true, "Seguro de RC 2026", false, true, new DateTime(2026, 11, 30), true, 3, null),
            (cambio.CambiaConcepto, cambio.Concepto, cambio.CambiaDocumento, cambio.CambiaVencimiento, cambio.Vencimiento!.Value, cambio.CambiaCentro, cambio.IdCentroCoste!.Value, cambio.Punteado));
        Assert.Contains("(Completar la ficha)", motor.Llamadas[^1]);
        Assert.True(fuente.Cargas >= 3);

        // Cancelar devuelve la ficha a lo grabado.
        Seleccionar(vm, Fila(vm, 15, 2));
        vm.EdDocumento = "OTRO";
        vm.CancelarFichaCommand.Execute(null);
        Assert.Equal("POL-1", vm.EdDocumento);
        Assert.False(vm.TieneCambios);
    }

    [Fact]
    public async Task Cambiar_subcuenta_concepto_y_centro_pasan_por_el_motor_con_su_motivo()
    {
        var (vm, _, motor, dialogos, avisos) = await CrearAsync();
        Seleccionar(vm, Fila(vm, 14, 2));
        dialogos.Cuenta = ("5550001", "La misma");
        vm.AccionCuenta.Comando.Execute(null);
        await Task.Yield();
        Assert.Empty(motor.Llamadas);
        Assert.Contains(avisos, a => a.Contains("misma subcuenta"));

        dialogos.Cuenta = ("4300001", "Cliente sintético");
        vm.AccionCuenta.Comando.Execute(null);
        await Task.Yield();
        Assert.Contains("cuenta 2026", motor.Llamadas[^1]);
        Assert.Contains("4300001 (Motivo de prueba)", motor.Llamadas[^1]);

        Seleccionar(vm, Fila(vm, 10, 2), Fila(vm, 11, 1));                    // dos apuntes de asientos automáticos
        dialogos.Texto = ("Alquiler de febrero", "Concepto más claro");
        vm.AccionConcepto.Comando.Execute(null);
        await Task.Yield();
        Assert.StartsWith("concepto ", motor.Llamadas[^1]);
        Assert.Contains("«Alquiler de febrero» (Concepto más claro)", motor.Llamadas[^1]);

        Seleccionar(vm, Fila(vm, 10, 2));
        dialogos.IdCentro = 3;
        vm.AccionCentro.Comando.Execute(null);
        await Task.Yield();
        Assert.True(motor.UltimoCambio is { CambiaCentro: true, IdCentroCoste: 3, SoloPunteo: false });

        // Un error del motor se enseña entero y no se da por hecho.
        motor.Error = new Error("Asientos.TrimestreCerrado", "El trimestre está cerrado.", "Asiento 10", "Lleva la 477.", "Corrígelo con un contra-asiento.");
        Seleccionar(vm, Fila(vm, 10, 2));
        dialogos.Texto = ("Otro", "x");
        vm.AccionConcepto.Comando.Execute(null);
        await Task.Yield();
        Assert.Contains("Qué hacer: Corrígelo con un contra-asiento.", Assert.Single(dialogos.Errores));
    }

    [Fact]
    public async Task Eliminar_pide_el_motivo_borra_cada_asiento_con_su_version_y_cuenta_lo_que_falla()
    {
        var (vm, fuente, motor, dialogos, avisos) = await CrearAsync();
        Seleccionar(vm, Cabecera(vm, 14), Cabecera(vm, 15));
        dialogos.MotivoBorrado = null;                                        // cancelar no borra
        vm.AccionEliminar.Comando.Execute(null);
        await Task.Yield();
        Assert.Empty(motor.Llamadas);

        dialogos.MotivoBorrado = "Grabados dos veces";
        fuente.Asientos.RemoveAll(a => a.IdAsiento is 14 or 15);              // lo que devolverá la recarga
        vm.AccionEliminar.Comando.Execute(null);
        await Task.Yield();
        Assert.Equal(["eliminar 14 v3 (Grabados dos veces)", "eliminar 15 v3 (Grabados dos veces)"], motor.Llamadas);
        Assert.Contains(avisos, a => a.StartsWith("2 asientos eliminados"));
        Assert.DoesNotContain(vm.Filas, f => f.Asiento?.IdAsiento == 14);
        Assert.True(vm.FichaVacia);

        // Otra persona lo ha cambiado: se dice cuál y por qué, y no se da por borrado.
        var (vm2, _, motor2, dialogos2, _) = await CrearAsync();
        motor2.Conflicto = true;
        Seleccionar(vm2, Cabecera(vm2, 14));
        vm2.AccionEliminar.Comando.Execute(null);
        await Task.Yield();
        Assert.Contains("Asiento 14: Otra persona ha modificado este asiento.", Assert.Single(dialogos2.Errores));
        Assert.StartsWith("No he eliminado ningún asiento", dialogos2.Errores[0]);
    }

    [Fact]
    public async Task Copiar_filtrar_desde_el_menu_borrados_y_error_de_lectura()
    {
        var (vm, fuente, motor, dialogos, _) = await CrearAsync();
        Seleccionar(vm, Fila(vm, 10, 1), Fila(vm, 10, 2));
        vm.AccionCopiar.Comando.Execute(null);
        var lineas = dialogos.Copiado!.Split(Environment.NewLine);
        Assert.Equal(3, lineas.Length);
        Assert.StartsWith("Asiento\tId apunte\tFecha\tOrigen\tSubcuenta", lineas[0]);
        Assert.Contains("\t4300001\t", lineas[1]);
        Assert.Contains("\t121,00\t", lineas[1]);

        Seleccionar(vm, Fila(vm, 10, 1));
        vm.AccionFiltrarDocumento.Comando.Execute(null);
        Assert.Equal("F26-0001", vm.TextoFiltro);
        vm.QuitarFiltrosCommand.Execute(null);
        Seleccionar(vm, Fila(vm, 15, 1));
        vm.AccionBuscarImporte.Comando.Execute(null);
        Assert.Equal("80,00", vm.TextoFiltro);
        Assert.Equal([15], vm.Filas.Where(f => f.EsCabecera).Select(f => f.Asiento!.IdAsiento));
        vm.QuitarFiltrosCommand.Execute(null);
        Seleccionar(vm, Fila(vm, 15, 1));
        vm.AccionFiltrarCuenta.Comando.Execute(null);
        Assert.Equal("6250001", vm.Cuenta);

        vm.BorradosCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(1, dialogos.VecesBorrados);
        Assert.Equal("borrados 9001,9000", motor.Llamadas[^1]);

        fuente.Falla = true;
        await vm.CargarAsync();
        Assert.Equal(ProConta.UI.Base.Controles.EstadoVista.Error, vm.Estado);
        Assert.Contains("fichero bloqueado", vm.MensajeEstado);
        Assert.Contains("F5", vm.MensajeEstado);
    }

    [Fact]
    public async Task Avisa_de_la_apertura_desactualizada_y_ensena_las_cuentas()
    {
        var (vm, _, _, dialogos, _) = await CrearAsync(f =>
        {
            // Apertura de 2026 con el banco en 100 cuando 2025 lo dejó en −50 (y el resultado sin regularizar).
            var apertura = new AsientoDiario { IdAsiento = 2, Ejercicio = E2026, Fecha = new DateTime(2026, 1, 1), Descripcion = "Apertura", TipoAsiento = 1 }.Con(
                new ApunteDiario { IdApunte = 900, Orden = 1, Cuenta = "5720001", NombreCuenta = "Banco", Debe = 100m },
                new ApunteDiario { IdApunte = 901, Orden = 2, Cuenta = "1290001", NombreCuenta = "Resultado", Haber = 100m });
            f.Asientos.Add(apertura);
        });
        Assert.True(vm.HayAvisoApertura);
        Assert.Contains("La apertura de 2026 no coincide con el saldo con que acabó 2025 en 2 cuentas", vm.AvisoApertura);
        vm.VerDiferenciasCommand.Execute(null);
        Assert.Equal(["129", "5720001"], dialogos.Diferencias!.Select(d => d.Cuenta));
        Assert.False((await CrearAsync()).Vm.HayAvisoApertura);
    }

    [Fact]
    public async Task Exporta_lo_filtrado_a_PDF_y_a_Excel_con_las_columnas_visibles()
    {
        var (vm, _, _, dialogos, avisos) = await CrearAsync();
        var carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas", $"diario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        try
        {
            string? html = null;
            var abiertos = new List<string>();
            vm.HtmlAPdf = h => { html = h; return [0x25, 0x50, 0x44, 0x46]; };
            vm.AbrirFichero = abiertos.Add;
            vm.ColumnasVisibles = () => [ColumnasDiario.Asiento, ColumnasDiario.Cuenta, ColumnasDiario.Concepto, ColumnasDiario.Debe, ColumnasDiario.Haber];
            vm.Cuenta = "572";

            dialogos.Formato = null;                                          // cancelar no genera nada
            await vm.ExportarAsync();
            Assert.Empty(abiertos);

            dialogos.Formato = FormatoExportacionDiario.Pdf;
            dialogos.Ruta = Path.Combine(carpeta, "diario.pdf");
            await vm.ExportarAsync();
            Assert.Equal([0x25, 0x50, 0x44, 0x46], File.ReadAllBytes(dialogos.Ruta));
            Assert.Contains("Cuenta: 572", html);
            Assert.Contains("Total · 3 asientos · 6 apuntes", html);          // por asiento: los asientos completos
            Assert.DoesNotContain("Documento", html);

            vm.VistaCommand.Execute("lista");
            dialogos.Formato = FormatoExportacionDiario.Excel;
            dialogos.Ruta = Path.Combine(carpeta, "diario.xlsx");
            await vm.ExportarAsync();
            using (var libro = new ClosedXML.Excel.XLWorkbook(dialogos.Ruta))
            {
                var hoja = libro.Worksheet("Diario");
                Assert.StartsWith("Diario · Sociedad sintética · 2026", hoja.Cell(1, 1).GetString());
                Assert.Equal(["Asiento", "Subcuenta", "Concepto", "Debe", "Haber"], Enumerable.Range(1, 5).Select(c => hoja.Cell(3, c).GetString()));
                Assert.Equal(11, hoja.Cell(4, 1).GetValue<int>());             // lista: solo los tres apuntes de la 572
                Assert.Equal(121m, hoja.Cell(4, 4).GetValue<decimal>());
                Assert.True(hoja.Cell(7, 1).IsEmpty());
                Assert.Equal(("Totales", 471m, 80m), (hoja.Cell(8, 1).GetString(), hoja.Cell(8, 4).GetValue<decimal>(), hoja.Cell(8, 5).GetValue<decimal>()));
            }
            Assert.Equal(2, abiertos.Count);
            Assert.Contains(avisos, a => a.StartsWith("Diario exportado a diario.xlsx (3 apuntes)"));
        }
        finally { Directory.Delete(carpeta, recursive: true); }
    }
}
