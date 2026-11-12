using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Plantillas;

/// <summary>
/// Motor de plantillas sin BD (contabilidad/plantillas-asiento 2026-11-12 11:15, «Motor»): a partir de una plantilla, su origen,
/// sus datos de entrada y las cuentas de la sociedad propone los asientos que generaría, sin grabar nada. Lo usan la vista previa
/// (con el ejemplo de la plantilla) y <see cref="ServicioPlantillasAsiento"/> antes de ejecutar.
/// </summary>
public static class MotorPlantillas
{
    /// <summary>Nombres de los campos y cuentas que entienden las fórmulas; un dato de entrada no puede llamarse así.</summary>
    public static readonly IReadOnlySet<string> NombresReservados = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Cuenta", "NombreCuenta", "Lado", "Importe", "Debe", "Haber", "Concepto", "Documento", "Fecha", "Centro", "EsApunteOrigen",
        "DescripcionAsiento", "CuentaCuadres", "CuentaTesoreria", "Ejercicio", "Clave", "Y", "O", "No", "Verdadero", "Falso",
    };

    private const int LongitudConcepto = 255;

    /// <summary>
    /// Comprueba que la plantilla está bien definida: fórmulas con sintaxis correcta, lados válidos, datos con nombres propios y
    /// líneas «por cada apunte» solo con origen Apunte. Las del catálogo inicial lo cumplen (pruebas).
    /// </summary>
    public static Resultado Validar(PlantillaAsiento p)
    {
        var errores = new List<Error>();
        foreach (var d in p.Datos)
        {
            if (NombresReservados.Contains(d.Nombre) || d.Nombre.Contains('.') || d.Nombre.Any(char.IsWhiteSpace))
                errores.Add(new Error("Plantillas.DatoNoValido", $"El dato «{d.Nombre}» de la plantilla «{p.Nombre}» no puede llamarse así.", p.ToString(),
                    "Su nombre coincide con un campo del origen o tiene espacios o puntos.", "Ponle otro nombre, sin espacios (p. ej. NuevaCuenta)."));
            if (Formula.Validar(d.ValorPropuesto) is { } e) errores.Add(ErrorDeFormula(p, $"dato {d.Nombre}", e));
        }
        foreach (var l in p.Lineas)
        {
            var donde = $"asiento {l.NumeroAsiento}, línea {l.Orden}";
            foreach (var f in new[] { l.Cuenta, l.Importe, l.Concepto, l.Centro, l.Condicion, EsLadoFijo(l.Lado) ? null : l.Lado })
                if (Formula.Validar(f) is { } e) errores.Add(ErrorDeFormula(p, donde, e));
            if (l.PorCadaApunte && p.Origen != OrigenPlantilla.Apunte)
                errores.Add(new Error("Plantillas.LineaSinApunte", $"La línea {l.Orden} del asiento {l.NumeroAsiento} se repite por cada apunte, pero la plantilla «{p.Nombre}» no parte de un apunte.",
                    p.ToString(), "Solo con origen Apunte hay un asiento de origen cuyos apuntes recorrer.", "Cambia el origen a Apunte o quita «por cada apunte» de la línea."));
        }
        if (p.Tipo == TipoPlantilla.Sistema && string.IsNullOrWhiteSpace(p.ClaveProceso))
            errores.Add(new Error("Plantillas.SistemaSinProceso", $"La plantilla de sistema «{p.Nombre}» no dice qué proceso la ejecuta.", p.ToString(),
                "Una plantilla de sistema solo la ejecuta su proceso.", "Indica la clave del proceso (p. ej. nominas)."));
        return errores.Count == 0 ? Resultado.Correcto() : Resultado.Fallo(errores);
    }

    /// <summary>Valores que se proponen para los datos de entrada (su fórmula de valor propuesto con el origen), sin pedir nada.</summary>
    public static IReadOnlyDictionary<string, string?> ValoresPropuestos(PlantillaAsiento p, OrigenEjecucion origen, EntornoSociedad entorno)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var campos = CamposOrigen(origen, entorno, apunteFila: null);
        foreach (var d in p.Datos.OrderBy(d => d.Orden))
        {
            try
            {
                var v = Formula.Evaluar(d.ValorPropuesto, Entorno(campos, valores, entorno, d.Nombre, p));
                valores[d.Nombre] = v.EsVacio ? null : Normalizar(d, v);
            }
            catch (ErrorFormula)
            {
                valores[d.Nombre] = null;
            }
        }
        return valores;
    }

    /// <summary>
    /// Propone los asientos de la plantilla sin grabar nada. <paramref name="datos"/> son los valores tecleados (los que falten se
    /// toman de su valor propuesto); <paramref name="fecha"/>, si se indica, sustituye a la de la regla de la plantilla.
    /// </summary>
    public static Resultado<PropuestaPlantilla> Proponer(PlantillaAsiento p, OrigenEjecucion origen, IReadOnlyDictionary<string, string?>? datos,
        EntornoSociedad entorno, DateTime? fecha = null)
    {
        var comprobacion = ComprobarOrigen(p, origen);
        if (comprobacion is not null) return Resultado<PropuestaPlantilla>.Fallo(comprobacion);

        var leidos = LeerDatos(p, origen, datos, entorno);
        if (!leidos.EsCorrecto) return Resultado<PropuestaPlantilla>.Fallo(leidos.Errores);
        var valores = leidos.Valor;

        // Contextos de evaluación: un registro por nómina en un proceso; uno solo en el resto.
        var registros = origen is OrigenProceso proceso
            ? proceso.Registros.Select(r => (Clave: r.Clave, Fecha: (DateTime?)r.Fecha, Campos: CamposRegistro(r, entorno))).ToList()
            : [(Clave: "", Fecha: FechaOrigen(origen), Campos: CamposOrigen(origen, entorno, null))];
        if (registros.Count == 0)
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.SinRegistros", $"No hay nada que contabilizar con la plantilla «{p.Nombre}».", p.ToString(),
                "El proceso no ha pasado ningún registro.", "Comprueba que hay registros pendientes de contabilizar en el periodo."));

        var grupos = new List<(int Numero, DateTime? Fecha, List<ApuntePropuesto> Apuntes)>();
        try
        {
            foreach (var registro in registros)
            {
                foreach (var numero in p.Lineas.Select(l => l.NumeroAsiento).Distinct().Order())
                {
                    var apuntes = new List<ApuntePropuesto>();
                    foreach (var linea in p.Lineas.Where(l => l.NumeroAsiento == numero).OrderBy(l => l.Orden))
                    {
                        var error = GenerarLinea(p, linea, origen, registro.Campos, valores, entorno, apuntes);
                        if (error is not null) return Resultado<PropuestaPlantilla>.Fallo(error);
                    }
                    if (p.AgruparPorCuenta && grupos.FirstOrDefault(g => g.Numero == numero) is { Apuntes: not null } existente)
                        existente.Apuntes.AddRange(apuntes);
                    else
                        grupos.Add((numero, registro.Fecha, apuntes));
                }
            }
        }
        catch (ErrorFormula e)
        {
            return Resultado<PropuestaPlantilla>.Fallo(ErrorDeFormula(p, "fórmula", e));
        }

        var inicio = InicioPeriodo(entorno);
        var asientos = new List<AsientoPropuesto>();
        foreach (var g in grupos)
        {
            var apuntes = p.AgruparPorCuenta ? Agrupar(g.Apuntes) : g.Apuntes;
            if (apuntes.Count == 0) continue;
            var f = fecha?.Date ?? (p.ReglaFecha == ReglaFecha.FechaOrigen
                ? g.Fecha?.Date ?? inicio
                : g.Fecha is { } o && o.Date > inicio ? o.Date : inicio);
            var descripcion = apuntes.Select(a => a.Concepto).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? p.Nombre;
            asientos.Add(new AsientoPropuesto(g.Numero, f, Recortar(descripcion, ReglasAsiento.LongitudDescripcion), apuntes));
        }

        if (asientos.Count == 0)
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.SinApuntes", $"La plantilla «{p.Nombre}» no genera ningún apunte con estos datos.", p.ToString(),
                "Todos los importes salen a 0 o no se cumple ninguna condición de sus líneas.", "Indica un importe distinto de 0."));
        foreach (var a in asientos.Where(a => !a.Cuadrado))
            return Resultado<PropuestaPlantilla>.Fallo(new Error("Plantillas.Descuadrada", $"La plantilla «{p.Nombre}» genera un asiento que no cuadra.", p.ToString(),
                $"El asiento {a.NumeroAsiento} tiene Debe {a.TotalDebe:0.00} y Haber {a.TotalHaber:0.00}.",
                "Revisa las fórmulas de importe de la plantilla (mantenimiento de plantillas) o corrige los apuntes en la rejilla antes de guardar."));

        var (idAsiento, idApunte) = origen is OrigenApunte oa ? (oa.Asiento.IdAsiento, oa.Apunte.IdApunte) : (0, 0);
        return Resultado<PropuestaPlantilla>.Correcto(new PropuestaPlantilla(p, entorno.Contexto, asientos, valores,
            idAsiento == 0 ? null : idAsiento, idApunte == 0 ? null : idApunte));
    }

    /// <summary>Primer día del trimestre activo del ejercicio; el 1 de enero si no hay trimestre activo.</summary>
    public static DateTime InicioPeriodo(EntornoSociedad entorno) =>
        TrimestreContable.Inicio(entorno.Contexto.Ejercicio, entorno.TrimestreActivo is >= 1 and <= 4 ? entorno.TrimestreActivo.Value : 1);

    /// <summary>Reglas que no dependen de los datos: plantilla activa, origen adecuado y asientos que no se anulan ni rehacen con plantilla.</summary>
    public static Error? ComprobarOrigen(PlantillaAsiento p, OrigenEjecucion origen)
    {
        if (!p.Activa)
            return new Error("Plantillas.Inactiva", $"La plantilla «{p.Nombre}» está desactivada.", p.ToString(), "Se desactivó en el mantenimiento de plantillas.",
                "Elige otra plantilla o vuelve a activarla en el mantenimiento de plantillas.");
        switch (p.Origen)
        {
            case OrigenPlantilla.Apunte when origen is not OrigenApunte:
                return new Error("Plantillas.FaltaApunte", $"La plantilla «{p.Nombre}» parte de un apunte.", p.ToString(), "Corrige o reparte un apunte que ya existe.",
                    "Ábrela desde el Mayor: elige el apunte y usa «Asistente de corrección…».");
            case OrigenPlantilla.Proceso when origen is not OrigenProceso op || !string.Equals(op.ClaveProceso, p.ClaveProceso, StringComparison.OrdinalIgnoreCase):
                return new Error("Plantillas.OtroProceso", $"La plantilla «{p.Nombre}» es del proceso «{p.ClaveProceso}».", p.ToString(),
                    "Solo la ejecuta ese proceso con sus registros.", "Contabiliza desde ese proceso.");
        }
        if (p.ReproduceElAsiento && origen is OrigenApunte apunte)
        {
            if (apunte.TieneLibroIva)
                return new Error("Plantillas.AsientoDeFactura", "Este asiento es de una factura (tiene libro de IVA) y no se anula ni se rehace con una plantilla.",
                    apunte.Asiento.ToString(), "Anularlo aquí dejaría el libro de IVA sin corregir.",
                    "Corrígelo desde la factura: en compras, deshaz su exportación contable; en ventas, emite una factura rectificativa.");
            if (apunte.Asiento.EsAutomatico)
                return new Error("Plantillas.AsientoAutomatico", "Este asiento es automático y no se anula ni se rehace con una plantilla.",
                    apunte.Asiento.ToString(), $"Lo generó {apunte.Asiento.Lote} y solo ese proceso lo regenera.",
                    "Corrige el documento de origen (la factura, la nómina…) y vuelve a generar su asiento.");
        }
        return null;
    }

    private static Resultado<IReadOnlyDictionary<string, string?>> LeerDatos(PlantillaAsiento p, OrigenEjecucion origen, IReadOnlyDictionary<string, string?>? datos,
        EntornoSociedad entorno)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var campos = CamposOrigen(origen, entorno, null);
        var errores = new List<Error>();
        foreach (var d in p.Datos.OrderBy(d => d.Orden))
        {
            string? texto = null;
            if (datos is not null && datos.TryGetValue(d.Nombre, out var tecleado) && !string.IsNullOrWhiteSpace(tecleado)) texto = tecleado.Trim();
            else
            {
                try
                {
                    var v = Formula.Evaluar(d.ValorPropuesto, Entorno(campos, valores, entorno, d.Nombre, p));
                    texto = v.EsVacio ? null : Normalizar(d, v);
                }
                catch (ErrorFormula e)
                {
                    errores.Add(ErrorDeFormula(p, $"dato {d.Nombre}", e));
                    continue;
                }
            }
            if (texto is null)
            {
                if (d.Obligatorio)
                    errores.Add(new Error("Plantillas.FaltaDato", $"Falta «{d.Etiqueta}».", p.ToString(), d.Ayuda ?? "La plantilla lo necesita para proponer el asiento.",
                        d.Tipo == TipoDato.Cuenta && d.FiltroCuenta is { Length: > 0 } filtro
                            ? $"Elige una subcuenta {filtro} con el buscador; si no existe, créala en Cuentas contables."
                            : $"Escribe {d.Etiqueta.ToLowerInvariant()}."));
                valores[d.Nombre] = null;
                continue;
            }
            switch (d.Tipo)
            {
                case TipoDato.Importe when !ValorFormula.TryNumero(texto, out _):
                    errores.Add(new Error("Plantillas.ImporteNoValido", $"«{texto}» no es un importe válido para «{d.Etiqueta}».", p.ToString(), "Debe ser un número.",
                        "Escribe el importe con coma decimal, por ejemplo 121,50 (en negativo para el lado contrario)."));
                    break;
                case TipoDato.Fecha when !DateTime.TryParse(texto, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.None, out _):
                    errores.Add(new Error("Plantillas.FechaNoValida", $"«{texto}» no es una fecha válida para «{d.Etiqueta}».", p.ToString(), "Debe ser una fecha.",
                        "Escribe la fecha como dd/mm/aaaa."));
                    break;
                case TipoDato.Cuenta:
                    if (d.FiltroCuenta is { Length: > 0 } prefijo && !texto.StartsWith(prefijo, StringComparison.Ordinal))
                        errores.Add(new Error("Plantillas.CuentaFueraDeFiltro", $"La cuenta {texto} no vale para «{d.Etiqueta}».", p.ToString(),
                            $"Esta plantilla solo admite subcuentas {prefijo}.", $"Elige una subcuenta {prefijo} con el buscador."));
                    else if (entorno.Cuentas is { } cuentas && !cuentas.ContainsKey(texto))
                        errores.Add(new Error("Plantillas.CuentaNoExiste", $"No existe la cuenta {texto} en el ejercicio {entorno.Contexto.Ejercicio}.", p.ToString(),
                            $"La has indicado en «{d.Etiqueta}».", "Elígela con el buscador o da de alta la cuenta en Cuentas contables."));
                    break;
            }
            valores[d.Nombre] = d.Tipo == TipoDato.Importe && ValorFormula.TryNumero(texto, out var n) ? n.ToString(CultureInfo.InvariantCulture) : texto;
        }
        return errores.Count == 0
            ? Resultado<IReadOnlyDictionary<string, string?>>.Correcto(valores)
            : Resultado<IReadOnlyDictionary<string, string?>>.Fallo(errores);
    }

    private static string Normalizar(DatoEntrada d, ValorFormula v) =>
        d.Tipo == TipoDato.Importe ? Math.Round(v.ComoNumero(d.ValorPropuesto ?? ""), 2, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) : v.ComoTexto().Trim();

    /// <summary>Genera los apuntes de una línea (uno, o uno por apunte del asiento de origen) y los añade a <paramref name="apuntes"/>.</summary>
    private static Error? GenerarLinea(PlantillaAsiento p, LineaPlantilla linea, OrigenEjecucion origen, IReadOnlyDictionary<string, ValorFormula> camposRegistro,
        IReadOnlyDictionary<string, string?> valores, EntornoSociedad entorno, List<ApuntePropuesto> apuntes)
    {
        List<(Apunte? Apunte, IReadOnlyDictionary<string, ValorFormula> Campos)> filas = linea.PorCadaApunte && origen is OrigenApunte oa
            ? oa.Asiento.Apuntes.OrderBy(a => a.Orden).Select(a => ((Apunte?)a, (IReadOnlyDictionary<string, ValorFormula>)CamposOrigen(origen, entorno, a))).ToList()
            : [(origen is OrigenApunte o ? o.Apunte : null, camposRegistro)];
        var donde = $"asiento {linea.NumeroAsiento}, línea {linea.Orden}";
        foreach (var fila in filas)
        {
            var entornoLinea = Entorno(fila.Campos, valores, entorno, null, p);
            if (!string.IsNullOrWhiteSpace(linea.Condicion) && !Formula.Evaluar(linea.Condicion, entornoLinea).ComoLogico(linea.Condicion)) continue;

            var importe = Math.Round(Formula.Evaluar(linea.Importe, entornoLinea).ComoNumero(linea.Importe), 2, MidpointRounding.AwayFromZero);
            if (importe == 0m) continue;

            var cuenta = Formula.Evaluar(linea.Cuenta, entornoLinea).ComoTexto().Trim();
            if (cuenta.Length == 0) return CuentaVacia(p, linea, donde);
            string? nombreNueva = null;
            if (cuenta.EndsWith('?'))
            {
                var baseCuenta = apuntes.FirstOrDefault()?.Cuenta ?? (fila.Campos.TryGetValue("Cuenta", out var c) ? c.ComoTexto() : "");
                cuenta = CuentasPlantilla.Completar(cuenta, baseCuenta);
                if (entorno.Cuentas is { } existentes && !existentes.ContainsKey(cuenta))
                    nombreNueva = Recortar(existentes.TryGetValue(baseCuenta, out var nombreBase) ? nombreBase : $"Subcuenta {cuenta}", 150);
            }
            else if (entorno.Cuentas is { } existentes && !existentes.ContainsKey(cuenta))
                return new Error("Plantillas.CuentaNoExiste", $"No existe la cuenta {cuenta} en el ejercicio {entorno.Contexto.Ejercicio}.", $"{p}, {donde}",
                    "La pide esta línea de la plantilla.", $"Da de alta la cuenta {cuenta} en Cuentas contables, o corrige la plantilla.");

            var lado = Lado(linea.Lado, entornoLinea, fila.Apunte);
            if (lado is null)
                return new Error("Plantillas.LadoNoValido", $"No sé a qué lado va la línea {linea.Orden} del asiento {linea.NumeroAsiento} de «{p.Nombre}».", $"{p}, {donde}",
                    $"El lado «{linea.Lado}» no es D, H, SIGNO, MISMO ni CONTRARIO (MISMO y CONTRARIO necesitan un apunte de origen).",
                    "Corrige el lado de la línea en el mantenimiento de plantillas.");
            var alDebe = lado.Value;
            if (importe < 0m) { alDebe = !alDebe; importe = -importe; }

            var concepto = Formula.Evaluar(linea.Concepto, entornoLinea).ComoTexto().Trim();
            var centroValor = Formula.Evaluar(linea.Centro, entornoLinea);
            int? centro = centroValor.EsVacio ? null : (int)centroValor.ComoNumero(linea.Centro ?? "");
            apuntes.Add(new ApuntePropuesto(cuenta, alDebe, importe, concepto.Length == 0 ? null : Recortar(concepto, LongitudConcepto),
                fila.Apunte?.NumeroDocumento, centro is 0 ? null : centro, nombreNueva));
        }
        return null;
    }

    private static Error CuentaVacia(PlantillaAsiento p, LineaPlantilla linea, string donde)
    {
        if (linea.Cuenta.Contains("CuentaCuadres", StringComparison.OrdinalIgnoreCase))
            return new Error("Plantillas.SinCuentaCuadres", "La empresa no tiene configurada la cuenta 113 para cuadres.", $"{p}, {donde}",
                $"La plantilla «{p.Nombre}» la usa como contrapartida.", "Configúrala en Parámetros › Empresas › Cuentas › «Cuadres (113)» y vuelve a intentarlo.");
        if (linea.Cuenta.Contains("CuentaTesoreria", StringComparison.OrdinalIgnoreCase))
            return new Error("Plantillas.SinCuentaTesoreria", "La empresa no tiene configurada la cuenta 570 de tesorería.", $"{p}, {donde}",
                $"La plantilla «{p.Nombre}» paga contra ella.", "Configúrala en Parámetros › Empresas › Cuentas › «Tesorería (570)» y vuelve a intentarlo.");
        return new Error("Plantillas.CuentaVacia", $"La línea {linea.Orden} del asiento {linea.NumeroAsiento} de «{p.Nombre}» no da ninguna cuenta.", $"{p}, {donde}",
            $"Su fórmula de cuenta «{linea.Cuenta}» sale vacía con estos datos.", "Rellena el dato de cuenta que pide la plantilla, o corrige su fórmula de cuenta.");
    }

    /// <summary>true = Debe. MISMO y CONTRARIO son relativos al apunte del que sale la línea.</summary>
    private static bool? Lado(string lado, IEntornoFormula entorno, Apunte? apunte)
    {
        var texto = EsLadoFijo(lado) ? lado.Trim() : Formula.Evaluar(lado, entorno).ComoTexto().Trim();
        var origenAlDebe = apunte is null ? (bool?)null : !apunte.Debe.EsCero;
        return texto.ToUpperInvariant() switch
        {
            "D" or "DEBE" => true,
            "H" or "HABER" or "SIGNO" => false,
            "MISMO" => origenAlDebe,
            "CONTRARIO" => origenAlDebe is null ? null : !origenAlDebe,
            _ => null,
        };
    }

    private static bool EsLadoFijo(string? lado) =>
        lado?.Trim().ToUpperInvariant() is "D" or "H" or "DEBE" or "HABER" or "SIGNO" or "MISMO" or "CONTRARIO";

    /// <summary>Suma las líneas de la misma cuenta y centro (y concepto del primero); el saldo decide el lado.</summary>
    private static List<ApuntePropuesto> Agrupar(List<ApuntePropuesto> apuntes) =>
        apuntes.GroupBy(a => (a.Cuenta, a.IdCentroCoste))
            .Select(g =>
            {
                var saldo = g.Sum(a => a.Debe - a.Haber);
                var primero = g.First();
                return primero with { AlDebe = saldo >= 0m, Importe = Math.Abs(saldo), Documento = g.Select(a => a.Documento).Distinct().Count() == 1 ? primero.Documento : null };
            })
            .Where(a => a.Importe != 0m)
            .ToList();

    private static DateTime? FechaOrigen(OrigenEjecucion origen) => origen is OrigenApunte oa ? oa.Asiento.FechaContable : null;

    /// <summary>Campos del origen: los del apunte <paramref name="apunteFila"/> (o del de origen) y, con «Origen.», siempre los del de origen.</summary>
    private static Dictionary<string, ValorFormula> CamposOrigen(OrigenEjecucion origen, EntornoSociedad entorno, Apunte? apunteFila)
    {
        var campos = new Dictionary<string, ValorFormula>(StringComparer.OrdinalIgnoreCase);
        if (origen is not OrigenApunte oa) return campos;
        var deOrigen = oa.Apunte;
        Rellenar(campos, "", apunteFila ?? deOrigen, oa.Asiento, entorno);
        Rellenar(campos, "Origen.", deOrigen, oa.Asiento, entorno);
        campos["EsApunteOrigen"] = ValorFormula.Logico((apunteFila ?? deOrigen).Orden == deOrigen.Orden);
        return campos;
    }

    private static void Rellenar(Dictionary<string, ValorFormula> campos, string prefijo, Apunte a, Asiento asiento, EntornoSociedad entorno)
    {
        var alDebe = !a.Debe.EsCero;
        campos[prefijo + "Cuenta"] = ValorFormula.Texto(a.CodigoCuenta);
        campos[prefijo + "NombreCuenta"] = ValorFormula.Texto(entorno.Cuentas?.GetValueOrDefault(a.CodigoCuenta) ?? "");
        campos[prefijo + "Lado"] = ValorFormula.Texto(alDebe ? "D" : "H");
        campos[prefijo + "Importe"] = ValorFormula.Numero(alDebe ? a.Debe.Valor : a.Haber.Valor);
        campos[prefijo + "Debe"] = ValorFormula.Numero(a.Debe.Valor);
        campos[prefijo + "Haber"] = ValorFormula.Numero(a.Haber.Valor);
        campos[prefijo + "Concepto"] = ValorFormula.Texto(string.IsNullOrWhiteSpace(a.Descripcion) ? asiento.Descripcion : a.Descripcion);
        campos[prefijo + "Documento"] = ValorFormula.Texto(a.NumeroDocumento ?? "");
        campos[prefijo + "Fecha"] = ValorFormula.Texto(asiento.FechaContable.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        campos[prefijo + "Centro"] = a.IdCentroCoste is int c ? ValorFormula.Numero(c) : ValorFormula.Vacio;
        campos[prefijo + "DescripcionAsiento"] = ValorFormula.Texto(asiento.Descripcion);
    }

    private static Dictionary<string, ValorFormula> CamposRegistro(RegistroProceso r, EntornoSociedad entorno)
    {
        var campos = new Dictionary<string, ValorFormula>(StringComparer.OrdinalIgnoreCase)
        {
            ["Clave"] = ValorFormula.Texto(r.Clave),
            ["Fecha"] = ValorFormula.Texto(r.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
        };
        foreach (var (nombre, valor) in r.Campos)
            campos[nombre] = valor switch
            {
                null => ValorFormula.Vacio,
                decimal d => ValorFormula.Numero(d),
                int i => ValorFormula.Numero(i),
                long l => ValorFormula.Numero(l),
                double x => ValorFormula.Numero((decimal)x),
                bool b => ValorFormula.Logico(b),
                DateTime f => ValorFormula.Texto(f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                _ => ValorFormula.Texto(Convert.ToString(valor, CultureInfo.InvariantCulture)),
            };
        return campos;
    }

    private static IEntornoFormula Entorno(IReadOnlyDictionary<string, ValorFormula> campos, IReadOnlyDictionary<string, string?> valores, EntornoSociedad entorno,
        string? datoPropuesto, PlantillaAsiento p)
    {
        var importes = p.Datos.Where(d => d.Tipo == TipoDato.Importe).Select(d => d.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var todo = new Dictionary<string, ValorFormula>(campos, StringComparer.OrdinalIgnoreCase)
        {
            ["CuentaCuadres"] = ValorFormula.Texto(entorno.CuentaCuadres?.Trim() ?? ""),
            ["CuentaTesoreria"] = ValorFormula.Texto(entorno.CuentaTesoreria?.Trim() ?? ""),
            ["Ejercicio"] = ValorFormula.Numero(entorno.Contexto.Ejercicio),
        };
        foreach (var (nombre, valor) in valores)
            todo[nombre] = valor is null ? ValorFormula.Vacio
                : importes.Contains(nombre) && ValorFormula.TryNumero(valor, out var n) ? ValorFormula.Numero(n) : ValorFormula.Texto(valor);
        return new EntornoDiccionario(todo, () => datoPropuesto is not null && entorno.UltimasCuentas?.GetValueOrDefault(datoPropuesto) is { } c ? c : null);
    }

    private static Error ErrorDeFormula(PlantillaAsiento p, string donde, ErrorFormula e) =>
        new("Plantillas.FormulaNoValida", $"La plantilla «{p.Nombre}» tiene una fórmula que no puedo calcular ({donde}).", p.ToString(),
            $"«{e.Formula}»: {e.Causa}", e.Accion + " Se corrige en el mantenimiento de plantillas.");

    private static string Recortar(string texto, int longitud) => texto.Length <= longitud ? texto : texto[..longitud];
}
