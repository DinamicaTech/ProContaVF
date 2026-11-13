using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.Tests;

/// <summary>Pantalla de líneas N43 (norma43/pantallas) sin base de datos: filtros, reglas de las acciones y selección por Id.</summary>
public sealed class LineasN43Tests
{
    private static LineaN43 L(int id, decimal importe, bool cargo = true, int banco = 1, string concepto = "RECIBO LUZ FICTICIA", int? asiento = null, bool propio = true,
        bool cuenta = false, int ejercicio = 2026, bool cerrado = false, DateTime? fecha = null, string? tipo = "Recibo") =>
        new(id, 1, banco, banco == 1 ? "Banco uno" : "Banco dos", banco == 1 ? "0049 1500 0012345678" : "2100 0001 0099999999", banco == 1 ? "0049" : "2100",
            fecha ?? new DateTime(ejercicio, 3, id), fecha ?? new DateTime(ejercicio, 3, id), ejercicio, cerrado, cargo ? "D" : "H", importe, "02", concepto, "0000012345", null, null,
            banco == 1 ? "5720001" : "5720002", banco == 1 ? "Banco uno" : "Banco dos",
            cuenta || asiento is not null ? [new DestinoLineaN43(10, "6280001", "Suministros ficticios", cargo ? importe : -importe, "MANUAL", null, null)] : [],
            asiento, propio, tipo);

    // ---------- filtros ----------

    [Fact]
    public void Los_filtros_se_combinan_en_Y_y_cada_ficha_se_quita_por_separado()
    {
        var lineas = new[]
        {
            L(1, 120.50m, concepto: "RECIBO LUZ ELÉCTRICA FICTICIA"),
            L(2, 120.50m, cargo: false, banco: 2, concepto: "TRANSFERENCIA DE CLIENTE", cuenta: true),
            L(3, 99m, concepto: "RECIBO AGUA", asiento: 50),
        };
        var f = new FiltroLineasN43(FiltroEstadoN43.Pendientes, Importe: -120.5m);
        Assert.Equal([1, 2], lineas.Where(f.Cumple).Select(l => l.Id));
        // Texto sin distinguir acentos ni mayúsculas, todas las palabras.
        Assert.Equal([1], lineas.Where((f with { Texto = "electrica luz" }).Cumple).Select(l => l.Id));
        Assert.Equal([2], lineas.Where((f with { IdCuentaBancaria = 2 }).Cumple).Select(l => l.Id));
        Assert.Equal([3], lineas.Where(new FiltroLineasN43(FiltroEstadoN43.Contabilizadas).Cumple).Select(l => l.Id));
        Assert.Equal([2], lineas.Where(new FiltroLineasN43(FiltroEstadoN43.ConCuenta).Cumple).Select(l => l.Id));
        Assert.Equal([2, 3], lineas.Where(new FiltroLineasN43(Desde: new DateTime(2026, 3, 2), Hasta: new DateTime(2026, 3, 3)).Cumple).Select(l => l.Id));
        // El texto también busca en las cuentas de destino.
        Assert.Equal([2, 3], lineas.Where(new FiltroLineasN43(Texto: "suministros").Cumple).Select(l => l.Id));

        var todas = new FiltroLineasN43(FiltroEstadoN43.SinCuenta, 1, "luz", 10m, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(["estado", "banco", "texto", "importe", "desde", "hasta"], todas.Fichas(_ => "Banco uno").Select(x => x.Clave));
        Assert.Equal("Importe: 10,00 €", todas.Fichas().Single(x => x.Clave == "importe").Texto);
        var sinTexto = todas.Sin("texto");
        Assert.Null(sinTexto.Texto);
        Assert.Equal(todas with { Texto = null }, sinTexto);
        Assert.Empty(FiltroLineasN43.Ninguno.Fichas());
    }

    // ---------- acciones ----------

    [Fact]
    public void Sin_seleccion_todo_se_desactiva_salvo_reasignar_las_pendientes()
    {
        var a = AccionesLineasN43.Evaluar([]);
        Assert.NotNull(a.AsignarCuenta);
        Assert.NotNull(a.Contabilizar);
        Assert.NotNull(a.Deshacer);
        Assert.NotNull(a.Traspaso);
        Assert.Null(a.Reasignar);
        Assert.Contains("pendientes", a.TextoReasignar);
    }

    [Fact]
    public void Las_acciones_dicen_por_que_no_se_pueden_hacer()
    {
        var sinCuenta = AccionesLineasN43.Evaluar([L(1, 10m)]);
        Assert.Null(sinCuenta.AsignarCuenta);
        Assert.Contains("Asignar cuenta", sinCuenta.Contabilizar);
        Assert.Contains("no está contabilizada", sinCuenta.Deshacer);
        Assert.Null(sinCuenta.PalabrasClave);

        var conCuenta = AccionesLineasN43.Evaluar([L(1, 10m, cuenta: true), L(2, 5m)]);
        Assert.Null(conCuenta.Contabilizar);
        Assert.Equal("Contabilizar (1)", conCuenta.TextoContabilizar);
        Assert.Equal([1], conCuenta.AContabilizar);
        Assert.NotNull(conCuenta.PalabrasClave); // solo con una línea

        var propia = AccionesLineasN43.Evaluar([L(1, 10m, asiento: 7)]);
        Assert.Equal("Recontabilizar", propia.TextoContabilizar);
        Assert.Null(propia.Deshacer);
        Assert.Null(propia.ActualizarTextos);
        Assert.NotNull(propia.Reasignar); // contabilizada: no se reasigna

        var erp = AccionesLineasN43.Evaluar([L(1, 10m, asiento: 8, propio: false)]);
        Assert.Contains("ERP", erp.AsignarCuenta);
        Assert.NotNull(erp.Contabilizar);
        Assert.Contains("ERP", erp.Deshacer);

        var cerrada = AccionesLineasN43.Evaluar([L(1, 10m, cuenta: true, ejercicio: 2024, cerrado: true)]);
        Assert.Contains("2024 está cerrado", cerrada.AsignarCuenta);
        Assert.Contains("2024 está cerrado", cerrada.Contabilizar);
        Assert.Null(cerrada.VerAsiento); // consultar sí
    }

    [Fact]
    public void El_traspaso_pide_un_cargo_y_un_abono_del_mismo_importe_en_bancos_distintos_sin_contabilizar()
    {
        Assert.Contains("exactamente dos", AccionesLineasN43.Evaluar([L(1, 10m)]).Traspaso);
        Assert.Contains("cargos", AccionesLineasN43.Evaluar([L(1, 10m), L(2, 10m, banco: 2)]).Traspaso);
        Assert.Contains("no coinciden", AccionesLineasN43.Evaluar([L(1, 10m), L(2, 11m, cargo: false, banco: 2)]).Traspaso);
        Assert.Contains("misma cuenta bancaria", AccionesLineasN43.Evaluar([L(1, 10m), L(2, 10m, cargo: false)]).Traspaso);
        Assert.Contains("contabilizada", AccionesLineasN43.Evaluar([L(1, 10m, asiento: 3), L(2, 10m, cargo: false, banco: 2)]).Traspaso);
        Assert.Null(AccionesLineasN43.Evaluar([L(1, 10m), L(2, 10m, cargo: false, banco: 2)]).Traspaso);
    }

    // ---------- pantalla ----------

    private sealed class SinBd : IBaseDatos
    {
        public string Ruta => "sin-bd";
        public Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin BD en esta prueba");
        public Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin BD en esta prueba");
    }

    private sealed class SinDialogos : IDialogosLineasN43
    {
        public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) => opciones.Length - 1;
        public string? PedirMotivo(string titulo, string explicacion, string textoBoton) => null;
        public CuentaElegidaN43? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto) => null;
        public void VerPropuesta(PropuestaLineaN43 propuesta) { }
        public void PalabrasClave(PalabrasClaveN43VM vm) { }
    }

    private static LineasN43VM Pantalla(params LineaN43[] lineas)
    {
        var bd = new SinBd();
        var vm = new LineasN43VM(new ConsultaLineasN43(bd), new ServicioLineasN43(bd), new ServicioConciliacionN43(bd), new AsignadorCuentasN43(bd), new SinDialogos(),
            new ContextoLineasN43(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"), _ => { }));
        vm.Mostrar(lineas, [new EjercicioLineasN43(9001, 2026, false), new EjercicioLineasN43(9000, 2025, true)]);
        return vm;
    }

    [Fact]
    public void La_seleccion_se_guarda_por_Id_al_filtrar_y_al_volver_a_leer_las_lineas()
    {
        var vm = Pantalla(L(1, 10m, concepto: "RECIBO LUZ"), L(2, 10m, cargo: false, banco: 2, concepto: "TRASPASO RECIBIDO", tipo: "Transferencia"), L(3, 30m, concepto: "RECIBO AGUA"));
        Assert.Equal(3, vm.Filas.Count);
        Assert.Equal("Ejercicio 2026", vm.TextoEjercicios);
        vm.CambiarSeleccion([vm.Filas[0], vm.Filas[1]]);
        Assert.True(vm.AccionTraspaso.Habilitada, vm.AccionTraspaso.Ayuda);

        // Filtrar oculta la 2: la selección la recuerda, pero las acciones actúan solo sobre la visible.
        vm.TextoFiltro = "recibo";
        Assert.Equal([1, 3], vm.Filas.Select(f => f.Id));
        Assert.Equal([1], vm.Seleccion.Select(f => f.Id));
        Assert.Equal(new HashSet<int> { 1, 2 }, vm.IdsSeleccionados.ToHashSet());
        Assert.Contains("seleccionada oculta", vm.Resumen);
        Assert.False(vm.AccionTraspaso.Habilitada);
        Assert.Contains("exactamente dos", vm.AccionTraspaso.Ayuda);
        var ficha = Assert.Single(vm.Fichas);
        Assert.Equal("texto", ficha.Clave);

        // La vista, al restaurar la selección tras filtrar, no la cambia.
        vm.Restaurando = true;
        vm.CambiarSeleccion([]);
        vm.Restaurando = false;
        Assert.Equal([1], vm.Seleccion.Select(f => f.Id));

        // Quitar la ficha vuelve a mostrar la 2, seleccionada.
        ficha.Quitar.Execute(null);
        Assert.Empty(vm.Fichas);
        Assert.Equal([1, 2], vm.Seleccion.Select(f => f.Id));

        // Volver a leer (p. ej. tras contabilizar) conserva la misma fila con los datos nuevos y la selección.
        var fila1 = vm.Filas[0];
        vm.Mostrar([L(1, 10m, concepto: "RECIBO LUZ", asiento: 77), L(2, 10m, cargo: false, banco: 2), L(3, 30m)]);
        Assert.Same(fila1, vm.Filas[0]);
        Assert.Equal("77", fila1.Asiento);
        Assert.Equal([1, 2], vm.Seleccion.Select(f => f.Id));
        // Una línea que desaparece (lote revertido) sale de la selección.
        vm.Mostrar([L(1, 10m, asiento: 77), L(3, 30m)]);
        Assert.Equal([1], vm.IdsSeleccionados);
    }

    [Fact]
    public void Las_acciones_de_la_pantalla_siguen_a_la_seleccion_y_los_filtros_de_estado_y_banco()
    {
        var vm = Pantalla(L(1, 10m, cuenta: true), L(2, 20m, asiento: 5), L(3, 30m, banco: 2));
        Assert.Equal(3, vm.Bancos.Count); // «Todos» y los dos bancos
        vm.BancoFiltro = vm.Bancos.Single(b => b.Valor == 2);
        Assert.Equal([3], vm.Filas.Select(f => f.Id));
        Assert.StartsWith("Banco: ", Assert.Single(vm.Fichas).Texto);
        vm.QuitarFiltros();
        vm.EstadoFiltro = vm.Estados.Single(e => e.Valor == FiltroEstadoN43.Pendientes);
        Assert.Equal([1, 3], vm.Filas.Select(f => f.Id));
        vm.QuitarFiltros();

        Assert.False(vm.AccionAsignar.Habilitada);
        Assert.True(vm.AccionReasignar.Habilitada);
        vm.CambiarSeleccion([vm.Filas[0], vm.Filas[1]]);
        Assert.True(vm.AccionContabilizar.Habilitada);
        Assert.Equal("Contabilizar (2)", vm.AccionContabilizar.Texto);
        Assert.Equal("Deshacer el asiento…", vm.AccionDeshacer.Texto);
        Assert.True(vm.AccionDeshacer.Habilitada);
        Assert.False(vm.AccionPalabras.Habilitada);
        vm.CambiarSeleccion([vm.Filas[2]]);
        Assert.False(vm.AccionContabilizar.Habilitada);
        Assert.Contains("no tiene cuenta", vm.AccionContabilizar.Ayuda);
        Assert.Equal("Ver el asiento propuesto", vm.AccionVerAsiento.Texto);
    }

    [Fact]
    public void Las_fechas_e_importes_mal_escritos_no_filtran_y_lo_dicen()
    {
        var vm = Pantalla(L(1, 10m), L(2, 20m));
        vm.DesdeTexto = "32/13/2026";
        Assert.NotNull(vm.ErrorFechas);
        Assert.Equal(2, vm.Filas.Count);
        vm.DesdeTexto = "2/3/2026";
        Assert.Null(vm.ErrorFechas);
        Assert.Equal([2], vm.Filas.Select(f => f.Id));
        vm.ImporteFiltro = "abc";
        Assert.True(vm.ImporteNoValido);
        Assert.Equal([2], vm.Filas.Select(f => f.Id));
        vm.ImporteFiltro = "20,00 €";
        Assert.False(vm.ImporteNoValido);
        Assert.Equal(2, vm.Fichas.Count);
    }

    [Fact]
    public void Una_palabra_clave_tiene_que_encajar_con_la_linea_y_llevar_cuenta()
    {
        var linea = L(1, 10m, concepto: "RECIBO ENDESA ENERGIA REF 99");
        Assert.Contains("Falta el texto", ServicioLineasN43.ValidarPalabra(linea, new(null, null, null, " ", null, 5, null))!.Mensaje);
        Assert.Contains("Falta la cuenta", ServicioLineasN43.ValidarPalabra(linea, new(null, null, null, "ENDESA", null, null, null))!.Mensaje);
        Assert.Null(ServicioLineasN43.ValidarPalabra(linea, new(null, null, null, "ENDESA", null, null, ReglasPalabrasClave.EspecialRemesa)));
        Assert.Contains("no encaja", ServicioLineasN43.ValidarPalabra(linea, new(null, null, null, "IBERDROLA", null, 5, null))!.Mensaje);
        // Cargo: el sentido de la palabra clave es el de la 572 (H); D no encajaría.
        Assert.Null(ServicioLineasN43.ValidarPalabra(linea, new(null, "0049", "H", "ENDESA", "REF 99", 5, null)));
        Assert.NotNull(ServicioLineasN43.ValidarPalabra(linea, new(null, "0049", "D", "ENDESA", null, 5, null)));
        Assert.NotNull(ServicioLineasN43.ValidarPalabra(linea, new(null, "2100", null, "ENDESA", null, 5, null)));
    }

    [Fact]
    public void El_dialogo_de_palabras_clave_solo_acepta_trozos_literales_del_concepto()
    {
        var bd = new SinBd();
        var vm = new PalabrasClaveN43VM(new ServicioLineasN43(bd), new SinDialogos(), "T01", L(1, 10m, concepto: "RECIBO ENDESA ENERGIA"), new ContextoSociedadEjercicio("T01", 9001, 2026));
        Assert.False(vm.UsarSeleccion("", segunda: false));
        Assert.NotNull(vm.Error);
        Assert.False(vm.UsarSeleccion("IBERDROLA", segunda: false));
        Assert.True(vm.UsarSeleccion(" ENDESA ", segunda: false));
        Assert.Equal("ENDESA", vm.Palabra);
        Assert.Null(vm.Error);
        Assert.True(vm.UsarSeleccion("ENERGIA", segunda: true));
        var e = vm.Edicion;
        Assert.Equal(("0049", "H", "ENDESA", "ENERGIA"), (e.Entidad, e.Sentido, e.PalabraClave, e.PalabraClave2));
        vm.SoloBanco = false;
        vm.SoloSentido = false;
        Assert.Equal((null, null), (vm.Edicion.Entidad, vm.Edicion.Sentido));
    }
}
