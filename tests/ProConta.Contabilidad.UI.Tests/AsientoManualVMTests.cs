using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Plantillas;
using ProConta.Contabilidad.UI.Asientos;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.UI.Tests;

/// <summary>Pantalla de asiento manual sin BD (contabilidad/pantallas, PR 2 de contabilidad/plantillas-asiento).</summary>
public class AsientoManualVMTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 1, 2026);
    private static readonly IReadOnlyList<PlantillaAsiento> Catalogo =
        PlantillasJson.LeerCatalogo(SemillaPlantillasAsiento.Json).Select((p, i) => p with { IdPlantilla = i + 1 }).ToList();

    /// <summary>Fuente en memoria: cuentas del ejercicio, trimestre activo 2, un asiento manual (1) y uno automático (2); anota lo que se grabaría.</summary>
    private sealed class Fuente : IFuenteAsientoManual
    {
        public readonly List<(PropuestaPlantilla Propuesta, string Motivo)> Ejecutadas = [];
        public readonly List<(IReadOnlyList<Asiento> Asientos, string Motivo)> Nuevos = [];
        public readonly List<(Asiento Asiento, string Motivo)> Modificados = [];
        public int Escrituras => Ejecutadas.Count + Nuevos.Count + Modificados.Count;
        public int? Trimestre { get; init; } = 2;

        public ContextoSociedadEjercicio Contexto => Ctx;

        public Task<IReadOnlyList<PlantillaAsiento>> PlantillasAsync(OrigenPlantilla? origen = null) =>
            Task.FromResult<IReadOnlyList<PlantillaAsiento>>(Catalogo.Where(p => origen is null || p.Origen == origen).ToList()); // también la de sistema: la pantalla la quita

        public Task<EntornoSociedad> EntornoAsync(PlantillaAsiento? plantilla = null) => Task.FromResult(new EntornoSociedad(Ctx, "1130000", "5700000", Trimestre,
            new Dictionary<string, string>
            {
                ["6220001"] = "Reparaciones", ["6290001"] = "Otros servicios", ["5720001"] = "Banco", ["4000001"] = "Proveedor sintético", ["5700000"] = "Caja",
                ["1130000"] = "Reservas voluntarias", ["5510001"] = "Socio sintético", ["4720021"] = "IVA soportado",
            },
            plantilla?.Nombre == "Pago por un socio" ? new Dictionary<string, string> { ["CuentaSocio"] = "5510001" } : null));

        public Task<IReadOnlyList<CentroCoste>> CentrosAsync() =>
            Task.FromResult<IReadOnlyList<CentroCoste>>([new CentroCoste(3, "T01", null, "3", "Coches", true, true, 0, "3"), new CentroCoste(4, "T01", null, "4", "Inactivo", true, false, 0, "4")]);

        public Task<Resultado<OrigenApunte>> OrigenApunteAsync(int idApunte)
        {
            var a = Asiento(1, new DateTime(2026, 2, 10), "Reparación sintética", ("6220001", 242m, 0m), ("5720001", 0m, 242m));
            return Task.FromResult(Resultado<OrigenApunte>.Correcto(new OrigenApunte(a, 1)));
        }

        public Task<Asiento?> AsientoAsync(int idAsiento) => Task.FromResult<Asiento?>(idAsiento switch
        {
            1 => Asiento(1, new DateTime(2026, 5, 3), "Gasto sintético", ("6290001", 50m, 0m), ("5720001", 0m, 50m)),
            2 => Automatico(),
            _ => null,
        });

        public Task<Resultado<IReadOnlyList<int>>> EjecutarPlantillaAsync(PropuestaPlantilla propuesta, string motivo)
        {
            Ejecutadas.Add((propuesta, motivo));
            return Task.FromResult(Resultado<IReadOnlyList<int>>.Correcto(propuesta.Asientos.Select((_, i) => 100 + i).ToList()));
        }

        public Task<Resultado<IReadOnlyList<int>>> GuardarNuevosAsync(IReadOnlyList<Asiento> asientos, string motivo)
        {
            Nuevos.Add((asientos, motivo));
            return Task.FromResult(Resultado<IReadOnlyList<int>>.Correcto(asientos.Select((_, i) => 200 + i).ToList()));
        }

        public Task<Resultado<Asiento>> ModificarAsync(Asiento asiento, string motivo)
        {
            Modificados.Add((asiento, motivo));
            return Task.FromResult(Resultado<Asiento>.Correcto(asiento));
        }

        private static Asiento Asiento(int id, DateTime fecha, string descripcion, params (string Cuenta, decimal Debe, decimal Haber)[] apuntes)
        {
            var a = new Asiento(Ctx, fecha, descripcion);
            typeof(Asiento).GetProperty(nameof(Contabilidad.Asientos.Asiento.IdAsiento))!.SetValue(a, id);
            var orden = 0;
            foreach (var (c, d, h) in apuntes)
            {
                var x = d != 0 ? Apunte.AlDebe(c, d, descripcion) : Apunte.AlHaber(c, h, descripcion);
                typeof(Apunte).GetProperty(nameof(Apunte.Orden))!.SetValue(x, ++orden);
                typeof(Apunte).GetProperty(nameof(Apunte.IdApunte))!.SetValue(x, id * 10 + orden);
                a.Apuntes.Add(x);
            }
            return a;
        }

        private static Asiento Automatico()
        {
            var a = Asiento(2, new DateTime(2026, 5, 2), "Factura de compra sintética", ("6220001", 100m, 0m), ("4000001", 0m, 100m));
            typeof(Asiento).GetProperty(nameof(Contabilidad.Asientos.Asiento.Lote))!.SetValue(a, new IdentidadLote("compras/factura-compra", "EXPORTAR", "FC-1"));
            return a;
        }
    }

    private sealed class Dialogos : IDialogosAsiento
    {
        public (string Codigo, string Nombre)? Cuenta { get; set; }
        public readonly List<string> Errores = [];
        public (string Codigo, string Nombre)? ElegirCuenta(string? texto, IReadOnlyList<string> grupos, string titulo) => Cuenta;
        public void Error(string titulo, string mensaje) => Errores.Add(mensaje);
    }

    private static (AsientoManualVM Vm, Fuente Fuente, Dialogos Dialogos) Crear(Fuente? fuente = null)
    {
        var f = fuente ?? new Fuente();
        var d = new Dialogos();
        return (new AsientoManualVM(f, d), f, d);
    }

    private static void Linea(LineaAsientoVM l, string cuenta, string debe = "", string haber = "")
    {
        l.Cuenta = cuenta;
        if (debe.Length > 0) l.Debe = debe;
        if (haber.Length > 0) l.Haber = haber;
    }

    [Fact]
    public async Task Arranca_en_Sin_plantilla_y_no_ofrece_la_de_sistema()
    {
        var (vm, _, _) = Crear();
        await vm.IniciarNuevoAsync();
        Assert.Equal("Sin plantilla", vm.Plantilla!.Texto);
        Assert.Equal(["Sin plantilla", "Anular asiento", "Anular y rehacer con otra cuenta", "Pasar saldo a otra cuenta", "Ajustar saldo inicial contra la 113",
            "Pago por tesorería", "Pago por un socio"], vm.Plantillas.Select(p => p.Texto));
        Assert.DoesNotContain(vm.Plantillas, p => p.Plantilla?.Tipo == TipoPlantilla.Sistema);
        Assert.False(vm.TieneCambios);
        Assert.Single(vm.Bloques);
        Assert.Equal(["Sin centro", "3 · Coches"], vm.Centros.Select(c => c.Texto)); // solo centros activos e imputables
    }

    [Fact]
    public async Task Guardar_solo_con_el_asiento_cuadrado_y_su_ayuda_dice_por_cuanto()
    {
        var (vm, fuente, _) = Crear();
        await vm.IniciarNuevoAsync();
        var b = vm.Bloques[0];
        b.Descripcion = "Gasto de prueba";
        Linea(b.Lineas[0], "6290001", debe: "100");
        Linea(b.Lineas[1], "5720001", haber: "90");
        b.Lineas[1].Concepto = "Reparación sintética";
        b.Lineas[1].Documento = "F-9";
        vm.AnadirLinea(b);
        Assert.Equal(("Reparación sintética", "F-9"), (b.Lineas[2].Concepto, b.Lineas[2].Documento)); // la línea nueva copia concepto y documento de la anterior
        Assert.False(vm.PuedeGuardar);
        Assert.Contains("10,00", vm.AyudaGuardar);
        Assert.Contains("Haber", vm.AyudaGuardar);
        Assert.StartsWith("Descuadre: 10,00", b.TextoDescuadre);
        Assert.EndsWith("(falta Haber)", b.TextoDescuadre);

        vm.CuadrarCon(b.Lineas[1]);
        Assert.Equal(100m, b.Lineas[1].ValorHaber);
        Assert.True(b.Cuadrado);
        Assert.True(vm.PuedeGuardar, vm.AyudaGuardar);
        Assert.True(vm.TieneCambios);

        Assert.True(await vm.GuardarAsync());
        var (asientos, motivo) = Assert.Single(fuente.Nuevos);
        var a = Assert.Single(asientos);
        Assert.Equal([("6290001", 100m, 0m), ("5720001", 0m, 100m)], a.Apuntes.Select(x => (x.CodigoCuenta, x.Debe.Valor, x.Haber.Valor))); // la línea vacía no se graba
        Assert.Equal("Alta manual", motivo);
        Assert.Null(a.IdPlantilla);
    }

    [Fact]
    public async Task Invertir_duplicar_borrar_e_importes_negativos_o_ilegibles()
    {
        var (vm, _, _) = Crear();
        await vm.IniciarNuevoAsync();
        var b = vm.Bloques[0];
        var l = b.Lineas[0];
        Linea(l, "6290001", debe: "121,50");
        l.Haber = "10";
        Assert.Equal((0m, 10m), (l.ValorDebe, l.ValorHaber)); // un importe en un lado borra el otro
        l.Debe = "-30";
        Assert.Equal((0m, 30m), (l.ValorDebe, l.ValorHaber)); // en negativo va al lado contrario
        vm.Invertir(l);
        Assert.Equal((30m, 0m), (l.ValorDebe, l.ValorHaber));
        vm.Duplicar(l);
        Assert.Equal(3, b.Lineas.Count);
        Assert.Equal(("6290001", 30m), (b.Lineas[1].Cuenta, b.Lineas[1].ValorDebe));
        vm.Borrar(b.Lineas[1]);
        Assert.Equal(2, b.Lineas.Count);
        b.Lineas[1].Haber = "veinte";
        Assert.False(b.Lineas[1].ImporteValido);
        Assert.False(vm.PuedeGuardar);
        b.Lineas[1].Cuenta = "629.1";
        Assert.Equal("6290001", b.Lineas[1].Cuenta); // el punto se completa
        b.Lineas[1].Cuenta = "6999999";
        Assert.False(b.Lineas[1].CuentaValida);
    }

    [Fact]
    public async Task Nuevo_desde_el_Mayor_lleva_la_cuenta_y_el_importe_al_lado_contrario()
    {
        var (vm, _, _) = Crear();
        await vm.IniciarNuevoAsync("4000001", -121m); // la selección tiene saldo acreedor de 121 €
        var l = vm.Bloques[0].Lineas[0];
        Assert.Equal(("4000001", 121m, 0m), (l.Cuenta, l.ValorDebe, l.ValorHaber));
        Assert.Equal(2, vm.Bloques[0].Lineas.Count);
    }

    [Fact]
    public async Task Sin_apunte_las_plantillas_de_correccion_se_ven_con_su_ejemplo_pero_no_se_aplican()
    {
        var (vm, fuente, _) = Crear();
        await vm.IniciarNuevoAsync();
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Anular asiento");
        await Task.Yield();
        Assert.True(vm.HayPlantilla);
        Assert.False(string.IsNullOrWhiteSpace(vm.DescripcionPlantilla));
        var previo = Assert.Single(vm.VistaPrevia);
        Assert.Contains("ejemplo", vm.TextoVistaPrevia);
        Assert.Equal(["6220001", "5720001"], previo.Apuntes.Select(a => a.Cuenta));
        Assert.False(vm.PuedeAplicar);
        Assert.Contains("Asistente de corrección", vm.AyudaAplicar);
        Assert.False(vm.TieneCambios); // elegir plantilla no es un cambio
        Assert.Equal(0, fuente.Escrituras);
    }

    [Fact]
    public async Task Correccion_anular_y_rehacer_propone_dos_asientos_y_se_guardan_juntos_con_el_origen()
    {
        var (vm, fuente, _) = Crear();
        Assert.True((await vm.IniciarCorreccionAsync(11)).EsCorrecto);
        Assert.Equal(ModoAsientoManual.Correccion, vm.Modo);
        Assert.Equal(6, vm.Plantillas.Count - 1); // solo las de origen Apunte
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Anular y rehacer con otra cuenta");
        await Task.Yield();
        Assert.False(vm.PuedeAplicar); // falta la nueva cuenta
        Assert.Contains("Nueva cuenta", vm.AyudaAplicar);
        vm.Datos.Single().Valor = "6290001";
        Assert.Equal("Otros servicios", vm.Datos.Single().NombreCuenta);
        Assert.Equal(2, vm.VistaPrevia.Count);
        Assert.True(vm.PuedeAplicar, vm.AyudaAplicar);

        vm.Aplicar();
        Assert.Equal(2, vm.Bloques.Count);
        Assert.All(vm.Bloques, b => Assert.True(b.Cuadrado));
        Assert.Equal("Asiento 1 de 2", vm.Bloques[0].Titulo);
        Assert.Equal("01/04/2026", vm.Bloques[0].Fecha); // primer día del trimestre activo
        Assert.Equal([("6290001", 242m, 0m), ("5720001", 0m, 242m)], vm.Bloques[1].Lineas.Select(l => (l.Cuenta, l.ValorDebe, l.ValorHaber)));
        Assert.False(vm.PuedeGuardar);
        Assert.Contains("motivo", vm.AyudaGuardar);

        vm.Motivo = "La reparación era de otro servicio";
        vm.Bloques[1].Lineas[0].Concepto = "Rehecho a mano"; // la propuesta se puede cambiar antes de guardar
        Assert.True(await vm.GuardarAsync());
        var (propuesta, motivo) = Assert.Single(fuente.Ejecutadas);
        Assert.Equal("La reparación era de otro servicio", motivo);
        Assert.Equal((1, 11), (propuesta.IdAsientoOrigen, propuesta.IdApunteOrigen));
        Assert.Equal(2, propuesta.Asientos.Count);
        Assert.Equal("Rehecho a mano", propuesta.Asientos[1].Apuntes[0].Concepto);
        Assert.Equal("Anular y rehacer con otra cuenta", propuesta.Plantilla.Nombre);
    }

    [Fact]
    public async Task Correccion_pago_por_tesoreria_propone_el_motivo_y_pago_por_socio_la_ultima_551()
    {
        var (vm, _, _) = Crear();
        await vm.IniciarCorreccionAsync(12);
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Pago por tesorería");
        await Task.Yield();
        Assert.Equal("Pasamos pago a caja/tesorería", vm.Motivo);
        Assert.Equal("242,00", vm.Datos.Single().Valor);
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Pago por un socio");
        await Task.Yield();
        Assert.Equal("5510001", vm.Datos.Single(d => d.Dato.Nombre == "CuentaSocio").Valor);
    }

    [Fact]
    public async Task Cancelar_no_escribe_nada()
    {
        var (vm, fuente, _) = Crear();
        var cancelado = false;
        vm.Cancelado += () => cancelado = true;
        await vm.IniciarCorreccionAsync(11);
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Anular asiento");
        await Task.Yield();
        vm.Aplicar();
        vm.Motivo = "No lo voy a guardar";
        vm.CancelarCommand.Execute(null);
        vm.Descartar();
        Assert.True(cancelado);
        Assert.Equal(0, fuente.Escrituras);
        Assert.False(vm.TieneCambios);
    }

    [Fact]
    public async Task Un_asiento_automatico_se_abre_en_solo_lectura_y_dice_desde_donde_se_corrige()
    {
        var (vm, fuente, _) = Crear();
        Assert.True((await vm.IniciarModificarAsync(2)).EsCorrecto);
        Assert.True(vm.SoloLectura);
        Assert.Contains("Compras › Facturas recibidas", vm.AvisoSoloLectura);
        Assert.False(vm.PuedeGuardar);
        Assert.False(await vm.GuardarAsync());
        Assert.Equal(0, fuente.Escrituras);
    }

    [Fact]
    public async Task Modificar_exige_motivo_y_guarda_por_el_motor_con_el_mismo_asiento()
    {
        var (vm, fuente, _) = Crear();
        await vm.IniciarModificarAsync(1);
        Assert.Equal("Asiento 1", vm.Titulo);
        var b = vm.Bloques[0];
        Assert.Equal(["6290001", "5720001"], b.Lineas.Select(l => l.Cuenta));
        b.Lineas[0].Debe = "60";
        vm.CuadrarCon(b.Lineas[1]);
        Assert.False(vm.PuedeGuardar);
        vm.Motivo = "Importe corregido";
        Assert.True(await vm.GuardarAsync());
        var (a, motivo) = Assert.Single(fuente.Modificados);
        Assert.Equal((1, "Importe corregido", 60m), (a.IdAsiento, motivo, a.TotalDebe.Valor));
    }

    [Fact]
    public async Task La_fecha_nunca_es_anterior_al_trimestre_activo()
    {
        var (vm, _, _) = Crear();
        await vm.IniciarNuevoAsync();
        Assert.Equal(new DateTime(2026, 4, 1), vm.FechaPropuesta(new DateTime(2026, 2, 1)));
        Assert.Equal(new DateTime(2026, 12, 31), vm.FechaPropuesta(new DateTime(2027, 3, 5)));
        var b = vm.Bloques[0];
        b.Fecha = "15/03/2026";
        Assert.False(b.FechaValida);
        b.Descripcion = "Prueba";
        Linea(b.Lineas[0], "6290001", debe: "1");
        vm.CuadrarCon(b.Lineas[1]);
        b.Lineas[1].Cuenta = "5720001";
        Assert.Contains("trimestre cerrado", vm.AyudaGuardar);
        b.Fecha = "02/04/2026";
        Assert.True(vm.PuedeGuardar, vm.AyudaGuardar);
    }

    [Fact]
    public async Task El_buscador_pone_la_cuenta_y_una_subcuenta_nueva_ya_vale()
    {
        var (vm, _, dialogos) = Crear();
        await vm.IniciarNuevoAsync();
        var l = vm.Bloques[0].Lineas[0];
        dialogos.Cuenta = ("6290009", "Subcuenta creada en el buscador");
        vm.BuscarCuentaLinea(l);
        Assert.Equal(("6290009", "Subcuenta creada en el buscador", true), (l.Cuenta, l.NombreCuenta, l.CuentaValida));
    }
}
