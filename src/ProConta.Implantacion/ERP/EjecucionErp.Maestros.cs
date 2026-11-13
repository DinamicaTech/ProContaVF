using System.Globalization;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Identificacion;
using ProConta.Terceros;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.ERP;

// Fase 1: sociedades y ejercicios, formas de pago, terceros (Personas, DatosContacto, Direcciones.csv), rol de cliente con su
// cuenta (vía Agrupaciones) y la cuenta de retención de cada cliente (M-11).
internal sealed partial class EjecucionErp
{
    private const string Fase1 = "1 maestros";
    private readonly HashSet<int> _clientesPersona = [];

    private async Task MaestrosAsync()
    {
        await SociedadesAsync();
        FormasPago();
        await TercerosAsync();
        await RetencionesClienteAsync();
    }

    /// <summary>Años con datos del ERP (ventas, compras y Norma 43).</summary>
    private IEnumerable<int> AniosConDatos() =>
        o.Tabla("FacturasVenta").Select(f => f.F("Fecha")?.Year)
            .Concat(o.Tabla("FacturasCompra").Where(f => f.T("Sociedad") == Sociedad).Select(f => f.E("Ejercicio")))
            .OfType<int>().Where(a => a is > 1990 and < 2100).Distinct().Order();

    private async Task SociedadesAsync()
    {
        var est = Est(Fase1, "Sociedades");
        var sociedades = o.Tabla("Sociedades");
        // El emisor es siempre la empresa (organizacion/sociedades-ejercicios 2026-11-13 20:55): el CIF del ERP se compara con el suyo.
        var cifSociedad = (await ConsultarAsync("SELECT CIF FROM Sociedades WHERE IdSociedad = ?", f => f.Texto("CIF"), Sociedad)).FirstOrDefault();
        est.Leidas = sociedades.Count;
        foreach (var s in sociedades)
        {
            var clave = Txt(s.E("IdSociedad"));
            if (!s.B("Activa")) { Excluir(Fase1, "Sociedades", clave, $"Sociedad {s.T("Codigo")} {s.T("Ejercicio")} ({s.T("Nombre")}) inactiva en el ERP: no se migra.", Severidad.Informativa); continue; }
            if (s.T("Codigo") != Sociedad) { Excluir(Fase1, "Sociedades", clave, $"La sociedad {s.T("Codigo")} no es la que se migra ({Sociedad})."); continue; }
            var anio = s.E("Ejercicio") ?? 0;
            if (_ejercicios.TryGetValue(anio, out var e))
            {
                est.Cargadas++;
                Mapear(TiposErp.Ejercicio, clave, e.Id);
                var cif = Nif.Normalizar(s.T("CIF"));
                if (cif.Length > 0 && cifSociedad is { } c && Nif.Normalizar(c) != cif)
                    Anotar(Fase1, "Sociedades", clave, "CIF", "Erp.CifDistinto", Severidad.Advertencia, $"La empresa tiene el CIF {c} en ProContaVF y el ejercicio {anio} tiene {cif} en el ERP.");
            }
            else Excluir(Fase1, "Sociedades", clave, $"El ejercicio {anio} activo en el ERP no está en ProContaVF (no se importó de ContaSol).");
        }
        // Un ejercicio por año con datos (P-13): los anteriores a ContaSol se crean cerrados, solo para colgar las facturas.
        foreach (var anio in AniosConDatos().Where(a => !_ejercicios.ContainsKey(a)))
        {
            var erp = sociedades.FirstOrDefault(x => x.T("Codigo") == Sociedad && x.E("Ejercicio") == anio);
            var nombre = erp?.T("Nombre") ?? $"{_ejercicios.Values.OrderByDescending(v => v.Id).Select(v => v.Nombre).FirstOrDefault()?.Split(' ')[0] ?? Sociedad} {anio}";
            var id = await InsertarAsync("INSERT INTO SociedadesEjercicios (IdSociedad, Ejercicio, Nombre, Activa, EjercicioCerrado, CarpetaFacturasCompra) VALUES (?, ?, ?, ?, ?, ?)",
                Sociedad, anio.ToString(CultureInfo.InvariantCulture), ReglasErp.Recortar(nombre, 255), true, true,
                Path.Combine(anio.ToString(CultureInfo.InvariantCulture), "Proveedores"));
            Anotar(Fase1, "Sociedades", anio.ToString(CultureInfo.InvariantCulture), "Ejercicio", "Erp.EjercicioCreado", Severidad.Informativa,
                $"He dado de alta el ejercicio {anio} (cerrado) para las facturas del ERP de ese año, que ContaSol no tiene.");
        }
        await CargarEjerciciosAsync();
        await ConfiguracionEjerciciosAsync();
        await RazonesSocialesAsync();
        await TiposIvaHistoricosAsync();
    }

    /// <summary>
    /// Razón social de la empresa por año para el emisor de las facturas migradas (organizacion/sociedades-ejercicios 2026-11-13
    /// 21:55): el ERP solo tiene etiquetas por año («DINAMICA 2023») y la razón social de hoy (F_EMP), así que sale del Nombre de
    /// los ejercicios importados de ContaSol, que es su F_EMP.DENEMP. La de los años sin ContaSol (2008–2017) es la del ejercicio
    /// de ContaSol más cercano. La del más reciente pasa a ser el Nombre de la empresa, que es el emisor de las facturas nuevas.
    /// </summary>
    private async Task RazonesSocialesAsync()
    {
        _razonesSociales.Clear();
        foreach (var (anio, nombre) in await ConsultarAsync(
                     "SELECT se.Ejercicio, se.Nombre FROM SociedadesEjercicios AS se WHERE se.IdSociedad = ? AND se.Nombre IS NOT NULL " +
                     "AND EXISTS (SELECT 1 FROM ImportacionesContaSol AS i WHERE i.IdSociedadEjercicio = se.IdSociedadEjercicio)",
                     f => (Anio: int.TryParse(f.Texto("Ejercicio"), out var a) ? a : 0, Nombre: (f.Texto("Nombre") ?? "").Trim()), Sociedad))
            if (anio > 0 && nombre.Length > 0) _razonesSociales[anio] = nombre;
        if (_razonesSociales.Count == 0)
        {
            Anotar(Fase1, "Sociedades", Sociedad, "Nombre", "Erp.SinRazonSocialAnual", Severidad.Advertencia,
                "No hay ejercicios importados de ContaSol con su razón social: las facturas migradas llevan como emisor el nombre de la empresa.");
            return;
        }
        var reciente = _razonesSociales.MaxBy(r => r.Key);
        var actual = (await ConsultarAsync("SELECT Nombre FROM Sociedades WHERE IdSociedad = ?", f => f.Texto("Nombre"), Sociedad)).FirstOrDefault()?.Trim();
        if (!string.Equals(actual, reciente.Value, StringComparison.Ordinal))
        {
            await EjecutarAsync("UPDATE Sociedades SET Nombre = ? WHERE IdSociedad = ?", ReglasErp.Recortar(reciente.Value, 255), Sociedad);
            Anotar(Fase1, "Sociedades", Sociedad, "Nombre", "Erp.RazonSocialEmpresa", Severidad.Informativa,
                $"La empresa se llamaba «{actual}» en ProContaVF; le pongo «{reciente.Value}», la razón social de {reciente.Key} en ContaSol, que es la que sale como emisor en las facturas nuevas.", actual);
        }
    }

    /// <summary>Los tipos de IVA del 16 % y del 18 % ya no se aplican: se dejan inactivos en TiposDeIVA, sin borrarlos (2026-11-13 22:45).</summary>
    private async Task TiposIvaHistoricosAsync()
    {
        var n = await EjecutarAsync("UPDATE TiposDeIVA SET Activo = False WHERE Activo = True AND (Valor = 16 OR Valor = 18)");
        if (n > 0)
            Anotar(Fase1, "TiposDeIVA", null, "Activo", "Erp.TiposIvaHistoricos", Severidad.Informativa,
                $"He dejado inactivos {n} tipos de IVA históricos (16 % y 18 %): no se ofrecen en las facturas, pero se conservan.");
    }

    /// <summary>
    /// Configuración que queda en cada ejercicio (organizacion/sociedades-ejercicios 2026-11-13 20:55), leída de la tabla Sociedades
    /// del ERP (una fila por empresa y ejercicio). El ejercicio que no está en el ERP (2008–2017) copia la del más cercano que la
    /// tenga. Solo se escriben los valores que el ERP tiene: un campo vacío en el origen no borra el de ProContaVF, así que repetir
    /// la migración deja lo mismo.
    /// </summary>
    private async Task ConfiguracionEjerciciosAsync()
    {
        var est = Est(Fase1, "Sociedades (configuración del ejercicio)");
        var origen = o.Tabla("Sociedades").Where(s => s.B("Activa") && s.T("Codigo") == Sociedad && s.E("Ejercicio") is not null)
            .GroupBy(s => s.E("Ejercicio")!.Value).ToDictionary(g => g.Key, g => g.First());
        est.Leidas = origen.Count;
        foreach (var (anio, ej) in _ejercicios.OrderBy(e => e.Key))
        {
            if (ReglasErp.ConfiguracionDeEjercicio(origen, anio) is not { } fuente) continue;
            var valores = ReglasErp.ValoresConfiguracionEjercicio(fuente.Fila);
            if (valores.Count == 0) continue;
            await EjecutarAsync($"UPDATE SociedadesEjercicios SET {string.Join(", ", valores.Select(v => $"[{v.Campo}] = ?"))} WHERE IdSociedadEjercicio = ?",
                [.. valores.Select(v => v.Valor), ej.Id]);
            est.Cargadas++;
            if (fuente.Anio != anio)
                Anotar(Fase1, "Sociedades", Txt(anio), "Ejercicio", "Erp.ConfiguracionCopiada", Severidad.Informativa,
                    $"El ejercicio {anio} no está en el ERP: le copio la configuración (cuentas, forma de pago, trimestre activo y prorrata) del {fuente.Anio}.");
        }
        // IVA de importación (propietario 2026-11-13 23:23): solo el ejercicio en curso (el más reciente) trabaja con la 4724021; los
        // anteriores se quedan como vienen (vacíos) y el histórico de ContaSol sigue en la 4724001. No pisa una cuenta ya puesta.
        if (_ejercicios.Count > 0 && _ejercicios.MaxBy(e => e.Key) is var (ultimo, ejUltimo)
            && await EjecutarAsync("UPDATE SociedadesEjercicios SET CuentaIVAImportacion = ? WHERE IdSociedadEjercicio = ? AND (CuentaIVAImportacion IS NULL OR CuentaIVAImportacion = '')",
                ReglasErp.CuentaIvaImportacionEjercicioEnCurso, ejUltimo.Id) > 0)
            Anotar(Fase1, "Sociedades", Txt(ultimo), "CuentaIVAImportacion", "Erp.CuentaIvaImportacion", Severidad.Informativa,
                $"El ejercicio {ultimo} usa la {ReglasErp.CuentaIvaImportacionEjercicioEnCurso} como cuenta de IVA de importación; los anteriores se quedan sin ella, como en el ERP.");
    }

    private void FormasPago()
    {
        var est = Est(Fase1, "FormasPago");
        var destino = ConsultarAsync("SELECT Id, Descripcion FROM FormasPago", f => (Id: f.Entero("Id"), Descripcion: f.Texto("Descripcion") ?? "")).GetAwaiter().GetResult()
            .ToDictionary(x => x.Id, x => x.Descripcion);
        foreach (var f in o.Tabla("FormasPago"))
        {
            est.Leidas++;
            var codigo = f.E("Codigo") ?? -1;
            if (destino.TryGetValue(codigo, out var d))
            {
                est.Cargadas++;
                if (!string.Equals(d.Trim(), f.T("Descripcion"), StringComparison.OrdinalIgnoreCase))
                    Anotar(Fase1, "FormasPago", Txt(codigo), "Descripcion", "Erp.FormaPagoDistinta", Severidad.Informativa, $"La forma de pago {codigo} se llama «{d}» en ProContaVF y «{f.T("Descripcion")}» en el ERP.");
            }
            else Excluir(Fase1, "FormasPago", Txt(codigo), $"La forma de pago {codigo} «{f.T("Descripcion")}» del ERP no está en ProContaVF (el esquema v2 las copió): revísala en Parámetros y tablas.");
        }
    }

    /// <summary>Subcuenta 43… de la persona a través de su agrupación.</summary>
    private string? Cuenta430(Fila persona) =>
        persona.E("IdAgrupacion") is int a && _agrupaciones.TryGetValue(a, out var g) && g.T("CuentaContable") is { } c && c.StartsWith("43", StringComparison.Ordinal) ? c : null;

    private async Task TercerosAsync()
    {
        var est = Est(Fase1, "Personas");
        var estContactos = Est(Fase1, "DatosContacto");
        var estDirecciones = Est(Fase1, "Direcciones.csv");
        var usadas = o.Tabla("FacturasVenta").Select(f => f.E("IdCliente")).Concat(o.Tabla("FacturasProgramadas").Where(f => f.B("Activa")).Select(f => f.E("IdCliente")))
            .OfType<int>().ToHashSet();
        var contactos = o.Tabla("DatosContacto").GroupBy(c => c.E("IdPersona") ?? -1).ToDictionary(g => g.Key, g => g.OrderBy(c => c.E("Orden") ?? 0).ToList());
        var direcciones = o.Direcciones.GroupBy(d => d.IdPersona).ToDictionary(g => g.Key, g => g.First());
        estContactos.Leidas = o.Tabla("DatosContacto").Count;
        estDirecciones.Leidas = o.Direcciones.Count;
        foreach (var d in o.Direcciones.Where(d => !_personas.ContainsKey(d.IdPersona)))
            Excluir(Fase1, "Direcciones.csv", Txt(d.IdPersona), $"La dirección de la persona {d.IdPersona} no tiene persona en el ERP.");

        // Terceros de ContaSol por subcuenta de cliente (el último ejercicio que la tenga).
        var terceroDeCliente = (await ConsultarAsync("SELECT IdCliente, IdTercero FROM Clientes", f => (Id: f.Entero("IdCliente"), Tercero: f.Entero("IdTercero"))))
            .ToDictionary(x => x.Id, x => x.Tercero);
        var existentes = (await ConsultarAsync("SELECT IdTercero FROM Terceros", f => f.Entero("IdTercero"))).ToHashSet();
        var reclamados = new HashSet<int>();

        foreach (var p in _personas.Values.OrderBy(p => p.E("IdPersona")))
        {
            cancelacion.ThrowIfCancellationRequested();
            est.Leidas++;
            var id = p.E("IdPersona")!.Value;
            var clave = Txt(id);
            if (id == 0) { Excluir(Fase1, "Personas", clave, "La persona 0 («USUARIO DESCONOCIDO») es un comodín del ERP: no se migra.", Severidad.Informativa); continue; }
            if (ReglasErp.EsInmueblePersona(id))
            {
                Excluir(Fase1, "Personas", clave, $"La persona {id} «{p.T("Nombre")}» es un inmueble que el ERP guardaba como persona: no es un tercero (va como activo).", Severidad.Informativa);
                continue;
            }
            var cuenta = Cuenta430(p);
            if (p.E("IdAgrupacion") is int ag && !_agrupaciones.ContainsKey(ag))
                ClaveNula(Fase1, "Personas", clave, "IdAgrupacion", ag, $"La agrupación {ag} de la persona {id} no existe: queda sin subcuenta.");

            int? idTercero = Anterior(TiposErp.Persona, clave) is int previo && existentes.Contains(previo) ? previo : null;
            var deContaSol = false;
            if (idTercero is null && cuenta is not null)
                foreach (var anio in _ejercicios.Keys.OrderDescending())
                    if (MapaCs(anio, TiposMapa.Cliente).TryGetValue(cuenta, out var idCliente) && terceroDeCliente.TryGetValue(idCliente, out var t))
                    {
                        if (reclamados.Contains(t))
                            Anotar(Fase1, "Personas", clave, "IdAgrupacion", "Erp.SubcuentaCompartida", Severidad.Advertencia,
                                $"La subcuenta {cuenta} de la persona {id} ya es de otra persona del ERP: le creo un tercero propio.");
                        else { idTercero = t; deContaSol = true; }
                        break;
                    }
            if (idTercero is int r) reclamados.Add(r);

            var tercero = idTercero is int it ? await m.Terceros.ObtenerAsync(it) : null;
            var nuevo = tercero is null;
            tercero ??= new Tercero();
            RellenarTercero(tercero, p, nuevo, deContaSol);
            var (anadidos, omitidos) = RellenarContactos(tercero, p, contactos.GetValueOrDefault(id) ?? []);
            estContactos.Cargadas += anadidos;
            foreach (var (valor, motivo) in omitidos) Excluir(Fase1, "DatosContacto", $"{id}:{valor}", motivo);
            if (direcciones.TryGetValue(id, out var dir))
            {
                tercero.Direccion = ReglasErp.Recortar(dir.Direccion, 255);
                tercero.CodigoPostal = ReglasErp.Recortar(dir.CodigoPostal, 10);
                tercero.Poblacion = ReglasErp.Recortar(dir.Poblacion, 100);
                tercero.Provincia = ReglasErp.Recortar(dir.Provincia, 100);
                tercero.Pais = dir.Pais?.ToUpperInvariant() is "ESPAÑA" or "ESPANA" or "SPAIN" ? "ES" : tercero.Pais;
                estDirecciones.Cargadas++;
            }
            var esCliente = cuenta is not null || usadas.Contains(id);
            if (esCliente && tercero.Roles.Cliente is null) tercero.Roles.Cliente = new RolCliente { Activo = true };

            var guardado = await m.Terceros.GuardarAsync(tercero, actor);
            if (!guardado.EsCorrecto)
            {
                Excluir(Fase1, "Personas", clave, $"No he podido guardar el tercero de la persona {id} {p.T("Nombre")}: {guardado.Error!.TextoOperador()}", Severidad.Bloqueante);
                continue;
            }
            idTercero = guardado.Valor.IdTercero;
            existentes.Add(idTercero.Value);
            if (nuevo && p.F("Alta") is DateTime alta)
                await EjecutarAsync("UPDATE Terceros SET FechaAlta = ? WHERE IdTercero = ?", alta, idTercero.Value);
            est.Cargadas++;
            Mapear(TiposErp.Persona, clave, idTercero.Value);
            if (deContaSol)
                Anotar(Fase1, "Personas", clave, null, "Erp.TerceroDeContaSol", Severidad.Informativa,
                    $"La persona {id} es el cliente {cuenta} ya importado de ContaSol: completo su ficha (tercero {idTercero}).");

            if (!esCliente) continue;
            var rol = await m.Roles.AsegurarClienteAsync(idTercero.Value);
            if (!rol.EsCorrecto) { Anotar(Fase1, "Personas", clave, null, "Erp.RolNoCreado", Severidad.Advertencia, rol.Error!.TextoOperador()); continue; }
            Mapear(TiposErp.Cliente, clave, rol.Valor);
            _clientesPersona.Add(id);
            if (cuenta is null) continue;
            if (Identidad(cuenta) is not int identidad)
            {
                ClaveNula(Fase1, "Personas", clave, "IdAgrupacion", cuenta, $"La subcuenta {cuenta} de la persona {id} no está en el plan contable importado de ContaSol: el cliente queda sin cuenta.");
                continue;
            }
            var otro = (await ConsultarAsync("SELECT IdCliente FROM CuentasClientesSociedad WHERE IdSociedad = ? AND Clase = ? AND IdIdentidadContable = ? AND IdCliente <> ?",
                f => f.Entero("IdCliente"), Sociedad, "CLIENTE", identidad, rol.Valor)).FirstOrDefault();
            if (otro != 0)
            {
                Anotar(Fase1, "Personas", clave, "IdAgrupacion", "Erp.CuentaDeOtroCliente", Severidad.Advertencia,
                    $"La subcuenta {cuenta} ya es del cliente {otro}: no se la asigno también al cliente de la persona {id}.");
                continue;
            }
            var asignada = await m.Roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, rol.Valor, Sociedad, identidad);
            if (!asignada.EsCorrecto) Anotar(Fase1, "Personas", clave, null, "Erp.CuentaNoAsignada", Severidad.Advertencia, asignada.Error!.TextoOperador());
        }
        await TutoresAsync();
    }

    private void RellenarTercero(Tercero t, Fila p, bool nuevo, bool deContaSol)
    {
        var id = p.E("IdPersona")!.Value;
        var entidad = p.B("EsEntidad");
        var nombre = ReglasErp.Recortar(p.T("Nombre"), 200);
        var razon = ReglasErp.Recortar(p.T("RazonSocial"), 200);
        if (entidad)
        {
            t.TipoPersona = TipoPersona.Juridica;
            t.RazonSocial = razon ?? nombre ?? $"Persona {id}";
            t.NombreComercial = nombre is not null && !string.Equals(nombre, t.RazonSocial, StringComparison.OrdinalIgnoreCase) ? nombre : t.NombreComercial;
            t.NombrePersona = null; t.PrimerApellido = null; t.SegundoApellido = null;
        }
        else
        {
            t.TipoPersona = TipoPersona.Fisica;
            t.NombrePersona = nombre ?? razon ?? $"Persona {id}";
            t.PrimerApellido = ReglasErp.Recortar(p.T("Apellido1"), 100);
            t.SegundoApellido = ReglasErp.Recortar(p.T("Apellido2"), 100);
            t.RazonSocial = null;
        }
        var nif = Nif.Normalizar(p.T("DNI"));
        if (nif.Length > 0 && nif.Length <= 20)
        {
            t.Nif = nif;
            var extranjero = nif.Length > 2 && char.IsAsciiLetter(nif[0]) && char.IsAsciiLetter(nif[1]) && !Nif.EsValido(nif);
            t.TipoIdentificacion = Nif.EsValido(nif) ? null : extranjero ? "VAT" : "OTRO";
            if (!Nif.EsValido(nif))
                Anotar(Fase1, "Personas", Txt(id), "DNI", "Erp.NifNoValido", Severidad.Informativa, $"El DNI «{nif}» de la persona {id} no es un NIF español válido: lo guardo como «{t.TipoIdentificacion}».");
        }
        if (p.T("Telefono") is { } tel) t.Telefono = ReglasErp.Recortar(tel, 50);
        else if (p.T("TelefonoMovil") is { } movil) t.Telefono = ReglasErp.Recortar(movil, 50);
        if (p.T("Email") is { } email && EmailValido(email)) t.Email = email;
        t.Activo = p.B("Activo");
        var origen = $"Migrado del ERP: persona {id}.";
        var notas = ReglasErp.UnirNotas(p.T("Notas"), p.T("MasNotas"));
        var deContaSolAntes = t.Notas?.Split(Environment.NewLine)[0] is { } primera && primera.StartsWith("Importado de ContaSol", StringComparison.Ordinal) ? primera : null;
        t.Notas = ReglasErp.UnirNotas(nuevo ? null : deContaSolAntes, notas, origen);
        var internas = new List<string?>();
        if (p.F("Nacimiento") is DateTime n) internas.Add($"Fecha de nacimiento (ERP): {n:dd/MM/yyyy}.");
        if (p.E("FormaPagoDefecto") is int fp and > 0)
        {
            var forma = o.Tabla("FormasPago").FirstOrDefault(f => f.E("Codigo") == fp)?.T("Descripcion");
            internas.Add($"Forma de pago por defecto en el ERP: {fp} {forma}.");
        }
        t.NotasInternas = ReglasErp.UnirNotas([.. internas]);
    }

    private static bool EmailValido(string v) =>
        v.Length <= 254 && v.Contains('@') && !v.StartsWith('@') && !v.EndsWith('@') && !v.Contains(' ');

    /// <summary>
    /// Primero Telefono, TelefonoMovil y email de Personas; después DatosContacto con contacto, sin duplicados: el que repite uno
    /// existente solo actualiza su etiqueta (Descripcion) y su persona de contacto (Nombre) cuando no son nulos.
    /// </summary>
    private (int Anadidos, List<(string Valor, string Motivo)> Omitidos) RellenarContactos(Tercero t, Fila p, IReadOnlyList<Fila> datos)
    {
        var omitidos = new List<(string, string)>();
        var anadidos = 0;
        DatoContacto? Buscar(string valor) => t.DatosContacto.FirstOrDefault(d => string.Equals(Normal(d.Valor), Normal(valor), StringComparison.OrdinalIgnoreCase));
        void Anadir(TipoDatoContacto tipo, string? etiqueta, string valor, string? persona, bool cuenta)
        {
            valor = valor.Trim();
            if (tipo == TipoDatoContacto.Email && !EmailValido(valor)) { omitidos.Add((valor, $"«{valor}» no es un email válido: no lo migro.")); return; }
            if (valor.Length > 254) { omitidos.Add((valor, "Dato de contacto de más de 254 caracteres.")); return; }
            var existente = Buscar(valor);
            ContactoLibre? grupo = null;
            if (persona is { Length: > 0 })
            {
                grupo = t.ContactosLibres.FirstOrDefault(g => string.Equals(g.Nombre, persona, StringComparison.OrdinalIgnoreCase));
                if (grupo is null) t.ContactosLibres.Add(grupo = new ContactoLibre { Nombre = ReglasErp.Recortar(persona, 100)! });
            }
            if (existente is not null)
            {
                if (etiqueta is not null) existente.Etiqueta = ReglasErp.Recortar(etiqueta, 50);
                if (grupo is not null) existente.ContactoLibre = grupo;
                return;
            }
            t.DatosContacto.Add(new DatoContacto { Tipo = tipo, Etiqueta = ReglasErp.Recortar(etiqueta, 50), Valor = valor, ContactoLibre = grupo, EnvioFacturas = tipo == TipoDatoContacto.Email });
            if (cuenta) anadidos++;
        }
        if (p.T("Telefono") is { } tel) Anadir(TipoDatoContacto.Telefono, "Fijo", tel, null, false);
        if (p.T("TelefonoMovil") is { } mov) Anadir(TipoDatoContacto.Telefono, "Móvil", mov, null, false);
        if (p.T("Email") is { } em) Anadir(TipoDatoContacto.Email, "Email", em, null, false);
        foreach (var d in datos)
        {
            if (d.T("Contacto") is not { } valor) { omitidos.Add(($"orden {d.E("Orden")}", "Dato de contacto vacío en el ERP: no se migra.")); continue; }
            var (tipo, etiqueta) = ReglasErp.TipoContacto(d.E("TipoContacto"), d.T("Descripcion"));
            Anadir(tipo, d.T("Descripcion") is null && Buscar(valor) is not null ? null : etiqueta, valor, d.T("Nombre"), true);
        }
        return (anadidos, omitidos);
    }

    private static string Normal(string v) => new(v.Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray());

    /// <summary>IdPersonaTutor: vínculo entre las dos fichas (relación «Tutor (ERP)»), visible desde las dos.</summary>
    private async Task TutoresAsync()
    {
        foreach (var p in _personas.Values)
        {
            var id = p.E("IdPersona")!.Value;
            if (p.E("IdPersonaTutor") is not int tutor || tutor == 0 || tutor == id || Mapa(TiposErp.Persona, Txt(id)) is not int idTercero) continue;
            if (ReglasErp.EsInmueblePersona(tutor))
            {
                Anotar(Fase1, "Personas", Txt(id), "IdPersonaTutor", "Erp.TutorInmueble", Severidad.Informativa, $"El tutor de la persona {id} es el inmueble {tutor}, que no se migra como tercero: queda sin ese vínculo.", Txt(tutor));
                continue;
            }
            if (Mapa(TiposErp.Persona, Txt(tutor)) is not int idTutor)
            {
                ClaveNula(Fase1, "Personas", Txt(id), "IdPersonaTutor", tutor, $"El tutor {tutor} de la persona {id} no existe en el ERP: la ficha queda sin ese vínculo.");
                continue;
            }
            var t = await m.Terceros.ObtenerAsync(idTercero);
            if (t is null || t.Relaciones.Any(r => r.IdTerceroPersona == idTutor && r.IdTerceroOrganizacion == idTercero)) continue;
            t.Relaciones.Add(new RelacionContacto { IdTerceroPersona = idTutor, IdTerceroOrganizacion = idTercero, TipoRelacion = "Tutor (ERP)", Observaciones = $"IdPersonaTutor del ERP ({tutor} de {id})." });
            var r = await m.Terceros.GuardarAsync(t, actor);
            if (!r.EsCorrecto) Anotar(Fase1, "Personas", Txt(id), "IdPersonaTutor", "Erp.TutorNoGuardado", Severidad.Advertencia, r.Error!.TextoOperador());
        }
    }

    /// <summary>
    /// Cuenta de retención de cada cliente (M-11): la 473 del concepto de retención de sus facturas y programadas. Si un cliente
    /// tiene dos distintas, queda como incidencia y no se elige ninguna.
    /// </summary>
    private async Task RetencionesClienteAsync()
    {
        var porFactura = o.Tabla("FacturasVenta").Where(f => f.E("Id") is not null).ToDictionary(f => f.E("Id")!.Value, f => f.E("IdCliente"));
        var porProgramada = o.Tabla("FacturasProgramadas").Where(f => f.E("Id") is not null && f.B("Activa")).ToDictionary(f => f.E("Id")!.Value, f => f.E("IdCliente"));
        var cuentas = new Dictionary<int, HashSet<string>>();
        void Sumar(IEnumerable<Fila> lineas, Dictionary<int, int?> cabeceras)
        {
            foreach (var l in lineas)
                if (l.E("IdConcepto") is int c && CuentaConcepto(c) is { } cuenta && ReglasErp.EsCuentaRetencion(cuenta)
                    && cabeceras.TryGetValue(l.E("Id") ?? -1, out var cli) && cli is int persona)
                    (cuentas.TryGetValue(persona, out var set) ? set : cuentas[persona] = []).Add(cuenta);
        }
        Sumar(o.Tabla("LineasFacturaVenta"), porFactura);
        Sumar(o.Tabla("LineasFacturaProgramada"), porProgramada);
        foreach (var (persona, set) in cuentas)
        {
            if (Mapa(TiposErp.Cliente, Txt(persona)) is not int idCliente) continue;
            if (set.Count > 1)
            {
                Anotar(Fase1, "ConceptosCaja", Txt(persona), "IdAgrupacion", "Erp.RetencionAmbigua", Severidad.Advertencia,
                    $"El cliente de la persona {persona} tiene retenciones a {string.Join(" y ", set.Order())}: no le asigno ninguna cuenta de retención; elígela en su ficha.");
                continue;
            }
            var cuenta = set.First();
            if (Identidad(cuenta) is not int identidad)
            {
                ClaveNula(Fase1, "ConceptosCaja", Txt(persona), "IdAgrupacion", cuenta, $"La cuenta de retención {cuenta} no está en el plan contable importado: el cliente queda sin ella.");
                continue;
            }
            var r = await m.Roles.AsignarCuentaAsync(ClaseCuentaRol.Retencion, idCliente, Sociedad, identidad);
            if (r.EsCorrecto) Est(Fase1, "Retenciones de cliente (M-11)").Cargadas++;
            else Anotar(Fase1, "ConceptosCaja", Txt(persona), null, "Erp.RetencionNoAsignada", Severidad.Advertencia, r.Error!.TextoOperador());
        }
    }

    /// <summary>Cuenta de un concepto del ERP a través de su agrupación.</summary>
    private string? CuentaConcepto(int idConcepto) =>
        _conceptosErp.TryGetValue(idConcepto, out var c) && c.E("IdAgrupacion") is int a && _agrupaciones.TryGetValue(a, out var g) ? g.T("CuentaContable") : null;

    private string? DepartamentoConcepto(int idConcepto) =>
        _conceptosErp.TryGetValue(idConcepto, out var c) && c.E("IdAgrupacion") is int a && _agrupaciones.TryGetValue(a, out var g) ? g.T("Departamento") : null;
}
