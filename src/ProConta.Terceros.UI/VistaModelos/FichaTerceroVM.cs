using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Geografia;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.Terceros.UI.Logica;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>
/// Ficha del tercero en su pestaña (terceros/pantallas): cabecera con identidad, estado e insignias de roles; pestañas
/// Identidad, Contacto, Roles, Perfiles, Ventas, Compras, Contabilidad, Notas, Documentos e Historial; barra al pie con
/// Guardar, Descartar y los cambios pendientes. Todo se guarda junto, en una transacción, con el repositorio del ente.
/// </summary>
public sealed class FichaTerceroVM : Observable, IPestanaTrabajo, INotifyDataErrorInfo
{
    private readonly ModuloTerceros? _modulo;
    private Tercero _original;
    private object? _vista;
    private IReadOnlyList<ErrorFicha> _errores = [];
    private readonly HashSet<(object, string)> _tocados = new(new ComparadorTocado());
    private bool _intentoGuardar;
    private bool _cargando;
    private IReadOnlyList<Perfil> _catalogoPerfiles = [];

    internal FichaTerceroVM(ServiciosTerceros servicios, ModuloTerceros? modulo, IDialogos dialogos, int clave, long orden)
    {
        Servicios = servicios;
        _modulo = modulo;
        Dialogos = dialogos;
        Clave = clave;
        OrdenApertura = orden;
        _original = T = new Tercero();
        CuentaCliente = new CuentaRolVM(this, ClaseCuentaRol.Cliente);
        CuentaProveedor = new CuentaRolVM(this, ClaseCuentaRol.Proveedor);
        CuentaAcreedor = new CuentaRolVM(this, ClaseCuentaRol.Acreedor);
        CuentaRetencion = new CuentaRolVM(this, ClaseCuentaRol.Retencion);

        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => !Guardando && Estado == EstadoVista.Contenido);
        DescartarCommand = new ComandoDelegado(PreguntarDescartar, () => TieneCambios && !Guardando);
        VerCambiosCommand = new ComandoDelegado(() => VerCambios = !VerCambios, () => TieneCambios);
        IrARolCommand = new ComandoDelegado(p => { PestanaActiva = (int)PestanaFicha.Roles; RolEnfocado?.Invoke(this, p as string ?? ""); });
        IrAErrorCommand = new ComandoDelegado(p => { if (p is ErrorResumen e) IrAError(e.Error); });
        ElegirTipoCommand = new ComandoDelegado(p => TipoPersonaTexto = p as string ?? "");
        AnadirDomicilioCommand = new ComandoDelegado(AnadirDomicilio);
        AnadirEmailCommand = new ComandoDelegado(() => AnadirDato(TipoDatoContacto.Email));
        EnviarEmailCommand = new ComandoDelegado(p => { if (p is DatoContactoVM d) _ = EnviarEmailAsync(d); },
            p => _modulo?.EnviarEmail is not null && p is DatoContactoVM { EsEmail: true } d && d.Valor.Trim() is { Length: > 2 } v && v.Contains('@') && !v.Contains(' '));
        AnadirTelefonoCommand = new ComandoDelegado(() => AnadirDato(TipoDatoContacto.Telefono));
        PasarEmailPrincipalCommand = new ComandoDelegado(PasarEmailPrincipal, () => MostrarEmailAntiguo);
        PasarTelefonoPrincipalCommand = new ComandoDelegado(PasarTelefonoPrincipal, () => MostrarTelefonoAntiguo && TelefonoAntiguoValido);
        AnadirGrupoCommand = new ComandoDelegado(AnadirGrupo);
        VincularPersonaCommand = new ComandoDelegado(() => Vincular(comoOrganizacion: true));
        VincularOrganizacionCommand = new ComandoDelegado(() => Vincular(comoOrganizacion: false));
        ActivarRolCommand = new ComandoDelegado(p => CambiarRol(p as string, true));
        DesactivarRolCommand = new ComandoDelegado(p => CambiarRol(p as string, false));
        AsignarPerfilesCommand = new ComandoDelegado(p => Asignar(Lista(p)));
        RetirarPerfilesCommand = new ComandoDelegado(p => Retirar(Lista(p)));
        NuevoPerfilCommand = new ComandoDelegado(() => _ = NuevoPerfilAsync());
        RecargarCommand = new ComandoDelegado(() => _ = CargarAsync(T.IdTercero));
        RecargarHistorialCommand = new ComandoDelegado(() => _ = CargarHistorialAsync());
    }

    internal ServiciosTerceros Servicios { get; }
    internal IDialogos Dialogos { get; }

    /// <summary>Tercero que se edita (copia de trabajo).</summary>
    internal Tercero T { get; private set; }

    /// <summary>IdTercero, o un número negativo mientras la ficha es nueva.</summary>
    public int Clave { get; private set; }
    public long OrdenApertura { get; }
    internal CatalogoGeografico? Geografia { get; private set; }
    internal string NombreSociedad => Servicios.NombreSociedad;
    internal int Ejercicio => Servicios.Contexto.Ejercicio;

    // ---------- carga ----------
    private EstadoVista _estado = EstadoVista.Cargando;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    private string _tituloEstado = "Abriendo la ficha…", _mensajeEstado = "";
    public string TituloEstado { get => _tituloEstado; private set => Fijar(ref _tituloEstado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }

    /// <summary>Carga una ficha guardada.</summary>
    internal async Task CargarAsync(int idTercero)
    {
        Estado = EstadoVista.Cargando;
        TituloEstado = "Abriendo la ficha…";
        try
        {
            Geografia ??= await Servicios.Geografia();
            _catalogoPerfiles = await Servicios.Perfiles.ListarAsync();
            var t = await Servicios.Repositorio.ObtenerAsync(idTercero);
            if (t is null)
            {
                Estado = EstadoVista.Error;
                TituloEstado = "Este tercero ya no existe";
                MensajeEstado = $"No encuentro el tercero {idTercero}: puede que lo hayan borrado. Cierra esta pestaña y búscalo de nuevo en Terceros.";
                return;
            }
            t.CuentasRoles = await Servicios.Roles.IdentidadesAsync(idTercero, Servicios.Contexto.IdSociedad);
            await MostrarAsync(t);
        }
        catch (ExcepcionDatos ex)
        {
            Estado = EstadoVista.Error;
            TituloEstado = "No he podido abrir la ficha";
            MensajeEstado = ex.Error.TextoOperador();
        }
    }

    /// <summary>Prepara una ficha nueva, con el rol ya activado si se pide (botón «Nuevo» del buscador).</summary>
    internal async Task NuevaAsync(string? rol)
    {
        Geografia ??= await Servicios.Geografia();
        _catalogoPerfiles = await Servicios.Perfiles.ListarAsync();
        var t = new Tercero
        {
            TipoPersona = rol == "contacto" ? TipoPersona.Fisica : TipoPersona.Juridica,
            Pais = Paises.Espana,
            CuentasRoles = new CuentasRolesFicha(Servicios.Contexto.IdSociedad),
        };
        t.TipoIdentificacion = Identificaciones.Propuesto(t.TipoPersona);
        _rolNuevo = rol;
        _original = t.Clonar();
        switch (rol)
        {
            case "cliente": t.Roles.Cliente = new RolCliente(); break;
            case "proveedor": t.Roles.Proveedor = new RolProveedor(); break;
            case "contacto": t.Roles.Contacto = new RolContacto(); break;
        }
        T = t;
        Reconstruir();
        await ResolverCuentasAsync();
        Estado = EstadoVista.Contenido;
    }

    private string? _rolNuevo;

    private async Task MostrarAsync(Tercero t)
    {
        _cargando = true;
        _original = t;
        T = t.Clonar();
        Clave = t.IdTercero;
        _tocados.Clear();
        _intentoGuardar = false;
        Reconstruir();
        await ResolverCuentasAsync();
        _cargando = false;
        Modificado();
        Estado = EstadoVista.Contenido;
        _ = ActualizarNominasAsync();
        if (_historialCargado) await CargarHistorialAsync();
    }

    private async Task ResolverCuentasAsync()
    {
        var c = T.EsNuevo ? new CuentasRolesTercero(null, null, null) : await Servicios.Roles.CuentasAsync(T.IdTercero, Servicios.Contexto);
        // La ficha puede tener una identidad aún sin guardar (recién elegida): se resuelve igual.
        async Task<CuentaRol?> De(ClaseCuentaRol clase) =>
            T.CuentasRoles?.De(clase) is int id ? c.De(clase) is { } r && r.IdIdentidadContable == id ? r : await Servicios.Roles.ResolverAsync(clase, id, Servicios.Contexto) : null;
        CuentaCliente.Cargar(await De(ClaseCuentaRol.Cliente));
        CuentaProveedor.Cargar(await De(ClaseCuentaRol.Proveedor));
        CuentaAcreedor.Cargar(await De(ClaseCuentaRol.Acreedor));
        CuentaRetencion.Cargar(await De(ClaseCuentaRol.Retencion));
        ActualizarMayor();
    }

    // ---------- pestaña Contabilidad: el Mayor embebido (contabilidad/mayor) ----------
    private object? _mayor;
    private string? _cuentasMayor;

    /// <summary>Mayor de las cuentas del tercero (cliente, proveedor y acreedor): el mismo control que Contabilidad › Mayor, sin recientes.</summary>
    public object? MayorContabilidad => _mayor;
    public bool HayMayor => _mayor is not null;
    public string TituloSinMayor => _modulo?.MayorEmbebido is null ? "El Mayor no está disponible aquí" : "Este tercero no tiene cuentas contables";
    public string MensajeSinMayor => _modulo?.MayorEmbebido is null
        ? "Abre la ficha desde el programa para ver el Mayor de sus cuentas."
        : "Asígnale su cuenta de cliente, proveedor o acreedor en la pestaña Roles y guarda la ficha: aquí verás su mayor, todas juntas o una a una.";

    /// <summary>Crea (o rehace si cambian sus cuentas) el Mayor embebido; sus datos se leen al abrir la pestaña.</summary>
    private void ActualizarMayor()
    {
        if (_modulo?.MayorEmbebido is not { } crear) return;
        var codigos = new[] { CuentaCliente, CuentaProveedor, CuentaAcreedor }.Select(c => c.Codigo).Where(c => c.Length > 0).Distinct().ToList();
        var clave = string.Join("|", codigos);
        if (clave == _cuentasMayor) return;
        _cuentasMayor = clave;
        _mayor = codigos.Count == 0 ? null : crear(codigos);
        Avisar(nameof(MayorContabilidad), nameof(HayMayor), nameof(TituloSinMayor), nameof(MensajeSinMayor));
    }

    // ---------- pestaña Nóminas (nominas/pantallas): solo si el tercero es empleado de la sociedad activa ----------
    private object? _nominas;

    /// <summary>Contenido de la pestaña Nóminas (lo crea el módulo de nóminas); null si el tercero no es empleado de la sociedad.</summary>
    public object? NominasEmpleado => _nominas;
    public bool HayNominas => _nominas is not null;

    private async Task ActualizarNominasAsync()
    {
        if (_modulo?.NominasEmbebidas is not { } crear || T.EsNuevo) return;
        var id = T.IdTercero;
        object? vista;
        try { vista = await crear(id); }
        catch { vista = null; }
        if (T.IdTercero != id) return;
        _nominas = vista;
        Avisar(nameof(NominasEmpleado), nameof(HayNominas));
        if (_nominas is null && PestanaActiva == (int)PestanaFicha.Nominas) PestanaActiva = (int)PestanaFicha.Identidad;
    }

    /// <summary>Rehace las partes editables a partir de la copia de trabajo.</summary>
    private void Reconstruir()
    {
        Domicilios.Clear();
        Domicilios.Add(new DomicilioVM(this, T, null));
        foreach (var d in T.Domicilios) Domicilios.Add(new DomicilioVM(this, T, d));
        Grupos.Clear();
        foreach (var g in T.ContactosLibres) Grupos.Add(new GrupoContactoVM(this, g));
        RefrescarOpcionesGrupo();
        Emails.Clear();
        Telefonos.Clear();
        foreach (var d in T.DatosContacto) (d.Tipo == TipoDatoContacto.Email ? Emails : Telefonos).Add(new DatoContactoVM(this, d));
        Relaciones.Clear();
        foreach (var r in T.Relaciones) Relaciones.Add(new RelacionVM(this, r));
        RefrescarPerfiles();
        Avisar(string.Empty);
        Modificado();
    }

    // ---------- IPestanaTrabajo ----------
    public string Titulo
    {
        get
        {
            if (T.EsNuevo && string.IsNullOrWhiteSpace(T.NombreCalculado()))
                return _rolNuevo switch { "cliente" => "Nuevo cliente", "proveedor" => "Nuevo proveedor", "contacto" => "Nuevo contacto", _ => "Nuevo tercero" };
            if (T.TipoPersona == TipoPersona.Fisica)
                return string.Join(' ', new[] { T.NombrePersona, T.PrimerApellido }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
            var n = T.NombreCalculado();
            return string.IsNullOrWhiteSpace(n) ? $"Tercero {T.IdTercero}" : n;
        }
    }

    public string Ayuda => $"Ficha de {NombreCabecera}" + (TieneCambios ? $" · {TextoCambios}" : "") + (GuardadoFallido ? " · el último intento de guardar falló" : "");
    public string Icono => "user";
    public object Vista => _vista ??= new Vistas.VistaFicha { DataContext = this };

    private bool _guardadoFallido;
    public bool GuardadoFallido { get => _guardadoFallido; private set { if (Fijar(ref _guardadoFallido, value)) Avisar(nameof(Ayuda)); } }

    private IReadOnlyList<CambioFicha> _cambios = [];
    public IReadOnlyList<CambioFicha> Cambios { get => _cambios; private set => Fijar(ref _cambios, value); }
    public bool TieneCambios => _cambios.Count > 0;
    public string TextoCambios => Formatos.Plural(_cambios.Count, "cambio sin guardar", "cambios sin guardar");
    private bool _verCambios;
    public bool VerCambios { get => _verCambios; set => Fijar(ref _verCambios, value); }

    public IReadOnlyList<string> DescribirCambios() => _cambios.Select(c => c.ToString()).ToList();

    public void AlCerrar() => _modulo?.FichaCerrada(this);

    /// <summary>Descarta los cambios y vuelve a la ficha tal como se leyó.</summary>
    public void Descartar()
    {
        T = _original.Clonar();
        _tocados.Clear();
        _intentoGuardar = false;
        GuardadoFallido = false;
        Resumen.Clear();
        Avisar(nameof(HayResumen));
        Reconstruir();
        _ = ResolverCuentasAsync();
    }

    private void PreguntarDescartar()
    {
        var opcion = Dialogos.Preguntar("Descartar los cambios",
            $"Vuelvo a dejar la ficha de {NombreCabecera} como estaba al abrirla y pierdes {TextoCambios.Replace(" sin guardar", "")}. No se puede deshacer.",
            DescribirCambios(),
            new OpcionMensaje("Seguir editando", "Conserva los cambios en la ficha", Principal: true, Cancelar: true),
            new OpcionMensaje("Descartar los cambios", "Pierde los cambios pendientes de esta ficha", Peligro: true));
        if (opcion == 1) Descartar();
    }

    // ---------- cabecera ----------
    public string NombreCabecera => T.EsNuevo && string.IsNullOrWhiteSpace(T.NombreCalculado()) ? Titulo : T.NombreCalculado() is { Length: > 0 } n ? n : T.Nombre;
    public string InicialesCabecera => Iniciales(NombreCabecera);
    public string TipoTexto => TiposPersona.Texto(T.TipoPersona);
    public bool Inactivo => !T.Activo;

    public string LineaDatos => string.Join(" · ", new[]
    {
        T.EsNuevo ? "Sin guardar" : $"IdTercero {T.IdTercero}",
        string.IsNullOrWhiteSpace(T.Nif) ? null : $"{Identificaciones.Etiqueta(T.TipoIdentificacion)} {T.Nif.Trim()}",
        string.Equals(T.NombreComercial?.Trim(), NombreCabecera.Trim(), StringComparison.OrdinalIgnoreCase) ? null : T.NombreComercial?.Trim(),
        T.Poblacion?.Trim(),
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public IReadOnlyList<InsigniaRol> Insignias => new[]
    {
        T.Roles.EsCliente ? new InsigniaRol("cliente", "Cliente", T.Roles.Cliente!.IdCliente?.ToString() ?? "nuevo", "Rol Cliente activo. Pulsa para ir a él en la pestaña Roles") : null,
        T.Roles.EsProveedor ? new InsigniaRol("proveedor", "Proveedor", T.Roles.Proveedor!.IdProveedor?.ToString() ?? "nuevo", "Rol Proveedor activo. Pulsa para ir a él en la pestaña Roles") : null,
        T.Roles.EsContacto ? new InsigniaRol("contacto", "Contacto", T.Roles.Contacto!.IdContacto?.ToString() ?? "nuevo", "Rol Contacto activo. Pulsa para ir a él en la pestaña Roles") : null,
    }.OfType<InsigniaRol>().ToList();

    /// <summary>Se ha pulsado una insignia: la vista lleva el foco al rol.</summary>
    public event EventHandler<string>? RolEnfocado;

    // ---------- pestañas ----------
    private int _pestana;
    public int PestanaActiva
    {
        get => _pestana;
        set
        {
            if (!Fijar(ref _pestana, value)) return;
            if (value == (int)PestanaFicha.Historial && !_historialCargado) _ = CargarHistorialAsync();
        }
    }

    // ---------- identidad ----------
    public string TipoPersonaTexto
    {
        get => T.TipoPersona switch { TipoPersona.Fisica => "fisica", TipoPersona.Juridica => "juridica", TipoPersona.EntidadSinPersonalidad => "entidad", _ => "" };
        set
        {
            TipoPersona? nuevo = value switch { "fisica" => TipoPersona.Fisica, "juridica" => TipoPersona.Juridica, "entidad" => TipoPersona.EntidadSinPersonalidad, _ => T.TipoPersona };
            if (nuevo == T.TipoPersona) return;
            // Una ficha antigua sin clasificar conserva su nombre en el campo que corresponde al tipo elegido.
            if (T.TipoPersona is null && !string.IsNullOrWhiteSpace(T.Nombre))
            {
                if (nuevo == TipoPersona.Fisica && string.IsNullOrWhiteSpace(T.NombrePersona)) T.NombrePersona = T.Nombre.Trim();
                if (nuevo is TipoPersona.Juridica or TipoPersona.EntidadSinPersonalidad && string.IsNullOrWhiteSpace(T.RazonSocial)) T.RazonSocial = T.Nombre.Trim();
            }
            T.TipoPersona = nuevo;
            Avisar(nameof(NombrePersona), nameof(RazonSocial));
            Avisar(nameof(TipoPersonaTexto), nameof(EsFisica), nameof(EsOrganizacion), nameof(SinClasificar), nameof(EsParticular), nameof(EsEmpresa), nameof(EsEntidad));
            CambiadoAqui(nameof(TipoPersona));
        }
    }

    public bool EsParticular { get => T.TipoPersona == TipoPersona.Fisica; set { if (value) TipoPersonaTexto = "fisica"; } }
    public bool EsEmpresa { get => T.TipoPersona == TipoPersona.Juridica; set { if (value) TipoPersonaTexto = "juridica"; } }
    public bool EsEntidad { get => T.TipoPersona == TipoPersona.EntidadSinPersonalidad; set { if (value) TipoPersonaTexto = "entidad"; } }
    public bool EsFisica => T.TipoPersona == TipoPersona.Fisica;
    public bool EsOrganizacion => T.TipoPersona is TipoPersona.Juridica or TipoPersona.EntidadSinPersonalidad;
    public bool SinClasificar => T.TipoPersona is null;
    public string? NombreAntiguo { get => T.Nombre; set => Texto(T.Nombre, value, v => T.Nombre = v ?? "", nameof(NombreAntiguo)); }

    public IReadOnlyList<Opcion> TiposIdentificacion => Identificaciones.Lista.Any(o => o.Valor == (T.TipoIdentificacion ?? "").Trim().ToUpperInvariant()) || string.IsNullOrWhiteSpace(T.TipoIdentificacion)
        ? Identificaciones.Lista : [.. Identificaciones.Lista, new Opcion(T.TipoIdentificacion!.Trim(), T.TipoIdentificacion.Trim())];

    public string? TipoIdentificacion
    {
        get => string.IsNullOrWhiteSpace(T.TipoIdentificacion) ? "NIF" : T.TipoIdentificacion.Trim().ToUpperInvariant() is var v && Identificaciones.Lista.Any(o => o.Valor == v) ? v : T.TipoIdentificacion.Trim();
        set
        {
            if (value == TipoIdentificacion) return;
            T.TipoIdentificacion = value;
            Avisar(nameof(TipoIdentificacion), nameof(EtiquetaIdentificacion), nameof(AyudaIdentificacion));
            CambiadoAqui(nameof(TipoIdentificacion));
        }
    }

    public string EtiquetaIdentificacion => Identificaciones.Etiqueta(TipoIdentificacion);
    public string AyudaIdentificacion => EtiquetaIdentificacion is "NIF" or "CIF" or "NIE"
        ? $"{EtiquetaIdentificacion} español: compruebo la letra o el dígito de control al guardar si lo cambias. Puede repetirse en varios terceros"
        : $"{EtiquetaIdentificacion}: se guarda tal cual, sin comprobar el formato";

    public string? Nif { get => T.Nif; set => Texto(T.Nif, value, v => T.Nif = v, nameof(Nif)); }
    public string? NombrePersona { get => T.NombrePersona; set => Texto(T.NombrePersona, value, v => T.NombrePersona = v, nameof(NombrePersona)); }
    public string? PrimerApellido { get => T.PrimerApellido; set => Texto(T.PrimerApellido, value, v => T.PrimerApellido = v, nameof(PrimerApellido)); }
    public string? SegundoApellido { get => T.SegundoApellido; set => Texto(T.SegundoApellido, value, v => T.SegundoApellido = v, nameof(SegundoApellido)); }
    public string? RazonSocial { get => T.RazonSocial; set => Texto(T.RazonSocial, value, v => T.RazonSocial = v, nameof(RazonSocial)); }
    public string? NombreComercial { get => T.NombreComercial; set => Texto(T.NombreComercial, value, v => T.NombreComercial = v, nameof(NombreComercial)); }
    public bool Activo { get => T.Activo; set { if (value == T.Activo) return; T.Activo = value; Avisar(nameof(Activo), nameof(Inactivo)); CambiadoAqui(nameof(Activo)); } }

    public ObservableCollection<DomicilioVM> Domicilios { get; } = [];
    public ICommand AnadirDomicilioCommand { get; }

    private void AnadirDomicilio()
    {
        var d = new Domicilio(0, T.Domicilios.Count == 0 ? "ENVIO" : "OTRO", "", null, null, null, Paises.Espana);
        T.Domicilios.Add(d);
        Domicilios.Add(new DomicilioVM(this, T, d));
        Modificado();
    }

    internal void QuitarDomicilio(DomicilioVM vm)
    {
        if (vm.Adicional is { } d) T.Domicilios.Remove(d);
        Domicilios.Remove(vm);
        Modificado();
    }

    /// <summary>Intercambia el domicilio principal (columnas de Terceros) con un adicional.</summary>
    internal void MarcarPrincipal(DomicilioVM vm)
    {
        if (vm.Adicional is not { } d) return;
        var i = T.Domicilios.IndexOf(d);
        var antiguo = d with { Pais = T.Pais, CodigoPostal = T.CodigoPostal, Provincia = T.Provincia, Poblacion = T.Poblacion, Direccion = T.Direccion ?? "" };
        (T.Pais, T.CodigoPostal, T.Provincia, T.Poblacion, T.Direccion) = (d.Pais, d.CodigoPostal, d.Provincia, d.Poblacion, d.Direccion);
        T.Domicilios[i] = antiguo;
        Reconstruir();
        AvisoEstado($"El domicilio «{TiposDomicilio.Texto(d.TipoDomicilio)}» es ahora el principal; el anterior queda como adicional. Guarda la ficha para conservarlo.");
    }

    /// <summary>Aviso de que los cambios de identidad o domicilio no alteran facturas ya emitidas (solo si hay alguno pendiente).</summary>
    public bool MostrarNotaFacturas => _cambios.Any(c => c.Pestana == PestanaFicha.Identidad);

    public string NotaFacturas => "Los cambios de identidad o domicilio no alteran las facturas ya emitidas o registradas: cada una conserva los datos con que se emitió.";

    // ---------- contacto ----------
    public string? TelefonoPrincipal { get => T.Telefono; set => Texto(T.Telefono, value, v => T.Telefono = v, nameof(TelefonoPrincipal)); }
    public string? EmailPrincipal { get => T.Email; set => Texto(T.Email, value, v => T.Email = v, nameof(EmailPrincipal)); }
    public ObservableCollection<DatoContactoVM> Emails { get; } = [];
    public ObservableCollection<DatoContactoVM> Telefonos { get; } = [];
    public ObservableCollection<GrupoContactoVM> Grupos { get; } = [];

    /// <summary>Opciones de «Persona» de cada email o teléfono: General (null) y las personas de contacto.</summary>
    public ObservableCollection<OpcionGrupo> OpcionesGrupo { get; } = [];

    /// <summary>Quién recibe cada finalidad sin estar marcado y los avisos de las columnas (RF-TER-09).</summary>
    internal EstadoDestinatarios EstadoDestinatarios { get; private set; } = DestinatariosFicha.Calcular([]);
    public string? AvisoFacturas => EstadoDestinatarios.AvisoFacturas;
    public string? AvisoAdministrativos => EstadoDestinatarios.AvisoAdministrativos;

    /// <summary>El email antiguo de la ficha solo se ve si aún no está en la lista de emails.</summary>
    public bool MostrarEmailAntiguo => !string.IsNullOrWhiteSpace(T.Email) &&
        !T.DatosContacto.Any(d => d.Tipo == TipoDatoContacto.Email && string.Equals(d.Valor.Trim(), T.Email.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>El teléfono antiguo solo se ve si sus cifras no están ya en la lista de teléfonos.</summary>
    public bool MostrarTelefonoAntiguo => Cifras(T.Telefono) is { Length: > 0 } c &&
        !T.DatosContacto.Any(d => d.Tipo == TipoDatoContacto.Telefono && Cifras(d.Valor) == c);

    public bool MostrarFichaAntigua => MostrarEmailAntiguo || MostrarTelefonoAntiguo;

    public bool HayGrupos => Grupos.Count > 0;
    public bool HayRelaciones => Relaciones.Count > 0;

    /// <summary>El valor antiguo parece un teléfono (al menos 6 cifras); si no, se muestra pero no se ofrece pasarlo a la lista.</summary>
    public bool TelefonoAntiguoValido => Cifras(T.Telefono).Length >= 6;

    private static string Cifras(string? texto) => new((texto ?? "").Where(char.IsAsciiDigit).ToArray());

    public ICommand AnadirEmailCommand { get; }
    public ICommand AnadirTelefonoCommand { get; }
    public ICommand PasarEmailPrincipalCommand { get; }
    public ICommand PasarTelefonoPrincipalCommand { get; }
    public ICommand AnadirGrupoCommand { get; }

    /// <summary>Se acaba de añadir una fila: la vista pone el cursor en ella.</summary>
    public event EventHandler<object>? FilaAnadida;

    private void AnadirDato(TipoDatoContacto tipo)
    {
        var d = new DatoContacto { Tipo = tipo, Activo = true };
        T.DatosContacto.Add(d);
        var vm = new DatoContactoVM(this, d);
        (tipo == TipoDatoContacto.Email ? Emails : Telefonos).Add(vm);
        Modificado();
        FilaAnadida?.Invoke(this, vm);
    }

    private void PasarEmailPrincipal()
    {
        if (string.IsNullOrWhiteSpace(T.Email)) return;
        if (T.DatosContacto.Any(d => d.Tipo == TipoDatoContacto.Email && string.Equals(d.Valor.Trim(), T.Email.Trim(), StringComparison.OrdinalIgnoreCase))) return;
        var d = new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = T.Email.Trim(), Etiqueta = "Principal", Activo = true };
        T.DatosContacto.Add(d);
        Emails.Add(new DatoContactoVM(this, d));
        Modificado();
    }

    private void PasarTelefonoPrincipal()
    {
        if (!MostrarTelefonoAntiguo) return;
        var d = new DatoContacto { Tipo = TipoDatoContacto.Telefono, Valor = T.Telefono!.Trim(), Etiqueta = "Principal", Activo = true };
        T.DatosContacto.Add(d);
        Telefonos.Add(new DatoContactoVM(this, d));
        Modificado();
    }

    internal void QuitarDato(DatoContactoVM vm)
    {
        T.DatosContacto.Remove(vm.Dato);
        Emails.Remove(vm);
        Telefonos.Remove(vm);
        Modificado();
    }

    private void AnadirGrupo()
    {
        var g = new ContactoLibre();
        T.ContactosLibres.Add(g);
        var vm = new GrupoContactoVM(this, g);
        Grupos.Add(vm);
        RefrescarOpcionesGrupo();
        Modificado();
        FilaAnadida?.Invoke(this, vm);
    }

    internal void QuitarGrupo(GrupoContactoVM vm)
    {
        foreach (var d in T.DatosContacto.Where(d => ReferenceEquals(d.ContactoLibre, vm.Grupo))) d.ContactoLibre = null;
        T.ContactosLibres.Remove(vm.Grupo);
        Grupos.Remove(vm);
        RefrescarOpcionesGrupo();
        Modificado();
    }

    internal void GrupoRenombrado()
    {
        foreach (var d in Emails.Concat(Telefonos)) d.RefrescarGrupo();
    }

    private void RefrescarOpcionesGrupo()
    {
        OpcionesGrupo.Clear();
        OpcionesGrupo.Add(OpcionGrupo.General);
        foreach (var g in Grupos) OpcionesGrupo.Add(new OpcionGrupo(g));
        foreach (var d in Emails.Concat(Telefonos)) d.RefrescarGrupo();
    }

    public string AyudaEnviarEmail => _modulo?.EnviarEmail is null
        ? "Enviar un email no está disponible en esta pantalla."
        : "Abre el compositor de correo con esta dirección en «Para», con la cuenta de correo del ejercicio de trabajo";

    /// <summary>Botón «Enviar email» de una dirección: abre el compositor común (correo/pantallas 2026-11-10 19:40).</summary>
    public System.Windows.Input.ICommand EnviarEmailCommand { get; }

    private async Task EnviarEmailAsync(DatoContactoVM d)
    {
        if (_modulo?.EnviarEmail is not { } enviar) return;
        try
        {
            await enviar(d.Valor.Trim(), Clave, Titulo);
        }
        catch (Exception ex)
        {
            _modulo.Avisar($"No he podido abrir el compositor de correo: {ex.Message}");
        }
    }

    // ---------- relaciones ----------
    public ObservableCollection<RelacionVM> Relaciones { get; } = [];
    public ICommand VincularPersonaCommand { get; }
    public ICommand VincularOrganizacionCommand { get; }

    private void Vincular(bool comoOrganizacion)
    {
        var elegido = Dialogos.ElegirTercero(
            comoOrganizacion ? "Vincular una persona de contacto" : "Vincular a una organización",
            comoOrganizacion
                ? $"Elige la persona que es contacto de {NombreCabecera}. Si aún no tiene el rol Contacto, se lo doy al guardar."
                : $"Elige la organización de la que {NombreCabecera} es contacto. Al guardar, esta ficha recibe el rol Contacto si no lo tiene.",
            new FiltroTerceros(Tipo: comoOrganizacion ? TipoPersona.Fisica : null));
        if (elegido is null) return;
        if (elegido.IdTercero == T.IdTercero && !T.EsNuevo)
        {
            MostrarError("No puedo vincular el tercero consigo mismo", new Error("Relaciones.ConsigoMismo", "Has elegido este mismo tercero.", null,
                "Una relación une una persona con una organización distinta.", "Elige otro tercero."));
            return;
        }
        var r = comoOrganizacion
            ? new RelacionContacto { IdTerceroPersona = elegido.IdTercero, NombrePersona = elegido.Nombre, IdTerceroOrganizacion = T.IdTercero, NombreOrganizacion = NombreCabecera, FechaInicio = DateTime.Today }
            : new RelacionContacto { IdTerceroPersona = T.IdTercero, NombrePersona = NombreCabecera, IdTerceroOrganizacion = elegido.IdTercero, NombreOrganizacion = elegido.Nombre, FechaInicio = DateTime.Today };
        T.Relaciones.Add(r);
        var vm = new RelacionVM(this, r);
        Relaciones.Add(vm);
        Modificado();
        FilaAnadida?.Invoke(this, vm);
    }

    internal void QuitarRelacion(RelacionVM vm)
    {
        if (!vm.EsNueva) return;
        T.Relaciones.Remove(vm.Relacion);
        Relaciones.Remove(vm);
        Modificado();
    }

    internal void AbrirOtraFicha(int idTercero)
    {
        if (idTercero > 0) _modulo?.AbrirFicha(idTercero);
    }

    // ---------- roles ----------
    public bool ClienteActivo => T.Roles.EsCliente;
    public bool ProveedorActivo => T.Roles.EsProveedor;
    public bool ContactoActivo => T.Roles.EsContacto;
    public string IdCliente => T.Roles.Cliente?.IdCliente?.ToString() ?? (ClienteActivo ? "al guardar" : "");
    public string IdProveedor => T.Roles.Proveedor?.IdProveedor?.ToString() ?? (ProveedorActivo ? "al guardar" : "");
    public string IdContacto => T.Roles.Contacto?.IdContacto?.ToString() ?? (ContactoActivo ? "al guardar" : "");
    public string EstadoCliente => EstadoRol(T.Roles.Cliente?.IdCliente, ClienteActivo);
    public string EstadoProveedor => EstadoRol(T.Roles.Proveedor?.IdProveedor, ProveedorActivo);
    public string EstadoContacto => EstadoRol(T.Roles.Contacto?.IdContacto, ContactoActivo);

    private static string EstadoRol(int? id, bool activo) => activo ? "" : id is null ? "Rol no activo." : $"Rol desactivado (conserva su Id {id}).";

    public string? NotasCliente { get => T.Roles.Cliente?.Notas; set { if (T.Roles.Cliente is { } c && value != c.Notas) { c.Notas = value; Avisar(); CambiadoAqui(nameof(NotasCliente)); } } }
    public string? NotasProveedor { get => T.Roles.Proveedor?.Notas; set { if (T.Roles.Proveedor is { } p && value != p.Notas) { p.Notas = value; Avisar(); CambiadoAqui(nameof(NotasProveedor)); } } }
    public string? Cargo { get => T.Roles.Contacto?.Cargo; set { if (T.Roles.Contacto is { } c && value != c.Cargo) { c.Cargo = value; Avisar(); CambiadoAqui(nameof(Cargo)); } } }
    public string? Departamento { get => T.Roles.Contacto?.Departamento; set { if (T.Roles.Contacto is { } c && value != c.Departamento) { c.Departamento = value; Avisar(); CambiadoAqui(nameof(Departamento)); } } }
    public string? NotasContacto { get => T.Roles.Contacto?.Notas; set { if (T.Roles.Contacto is { } c && value != c.Notas) { c.Notas = value; Avisar(); CambiadoAqui(nameof(NotasContacto)); } } }

    public CuentaRolVM CuentaCliente { get; }
    public CuentaRolVM CuentaProveedor { get; }
    public CuentaRolVM CuentaAcreedor { get; }
    public CuentaRolVM CuentaRetencion { get; }

    public string TextoCuentas => $"Las cuentas valen para todos los ejercicios de {NombreSociedad}: en cada ejercicio se usa la misma cuenta, aunque cambie su código. Se ven las del ejercicio {Ejercicio}.";

    public ICommand ActivarRolCommand { get; }
    public ICommand DesactivarRolCommand { get; }

    private void CambiarRol(string? rol, bool activo)
    {
        switch (rol)
        {
            case "cliente":
                if (T.Roles.Cliente is null) { if (activo) T.Roles.Cliente = new RolCliente(); }
                else T.Roles.Cliente.Activo = activo;
                break;
            case "proveedor":
                if (T.Roles.Proveedor is null) { if (activo) T.Roles.Proveedor = new RolProveedor(); }
                else T.Roles.Proveedor.Activo = activo;
                break;
            case "contacto":
                if (T.Roles.Contacto is null) { if (activo) T.Roles.Contacto = new RolContacto(); }
                else T.Roles.Contacto.Activo = activo;
                break;
            default: return;
        }
        AvisarRoles();
        CambiadoAqui("Rol");
    }

    private void AvisarRoles() => Avisar(nameof(ClienteActivo), nameof(ProveedorActivo), nameof(ContactoActivo), nameof(IdCliente), nameof(IdProveedor), nameof(IdContacto),
        nameof(EstadoCliente), nameof(EstadoProveedor), nameof(EstadoContacto), nameof(NotasCliente), nameof(NotasProveedor), nameof(Cargo), nameof(Departamento),
        nameof(NotasContacto), nameof(Insignias));

    internal void FijarCuenta(ClaseCuentaRol clase, int? identidad)
    {
        T.CuentasRoles ??= new CuentasRolesFicha(Servicios.Contexto.IdSociedad);
        T.CuentasRoles.Fijar(clase, identidad);
    }

    internal string NombreParaCuenta() => string.IsNullOrWhiteSpace(T.NombreCalculado()) ? NombreCabecera : T.NombreCalculado();

    // ---------- perfiles ----------
    public ObservableCollection<PerfilItemVM> PerfilesDisponibles { get; } = [];
    public ObservableCollection<PerfilItemVM> PerfilesAsignados { get; } = [];
    private string _filtroPerfiles = "";
    public string FiltroPerfiles { get => _filtroPerfiles; set { if (Fijar(ref _filtroPerfiles, value)) RefrescarPerfiles(); } }
    public int NumeroPerfiles => PerfilesAsignados.Count;
    public bool SinPerfilesAsignados => PerfilesAsignados.Count == 0;
    public ICommand AsignarPerfilesCommand { get; }
    public ICommand RetirarPerfilesCommand { get; }
    public ICommand NuevoPerfilCommand { get; }

    private void RefrescarPerfiles()
    {
        var asignados = T.Perfiles.Where(a => a.Activo).Select(a => a.IdPerfil).ToHashSet();
        var f = IndiceTerceros.Normalizar(_filtroPerfiles);
        PerfilesAsignados.Clear();
        foreach (var a in T.Perfiles.Where(a => a.Activo).OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase))
            PerfilesAsignados.Add(new PerfilItemVM(a.IdPerfil, a.Nombre, a.Descripcion, a.PerfilActivo));
        PerfilesDisponibles.Clear();
        foreach (var p in _catalogoPerfiles.Where(p => !asignados.Contains(p.IdPerfil) && (f.Length == 0 || IndiceTerceros.Normalizar(p.Nombre + " " + p.Descripcion).Contains(f)))
                     .OrderByDescending(p => p.Activo).ThenBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase))
            PerfilesDisponibles.Add(new PerfilItemVM(p.IdPerfil, p.Nombre, p.Descripcion, p.Activo));
        Avisar(nameof(NumeroPerfiles), nameof(SinPerfilesAsignados), nameof(TextoSinPerfiles), nameof(Cabeceras));
    }

    public string TextoSinPerfiles => _catalogoPerfiles.Count == 0
        ? "El catálogo de perfiles está vacío. Crea el primero con «Nuevo perfil»."
        : "Este tercero no tiene perfiles. Arrastra uno desde la izquierda, haz doble clic o usa la flecha.";

    private static IEnumerable<PerfilItemVM> Lista(object? p) => p switch
    {
        PerfilItemVM uno => [uno],
        IList lista => lista.OfType<PerfilItemVM>().ToList(),
        _ => []
    };

    /// <summary>Asigna perfiles (los retirados del catálogo no admiten asignaciones nuevas y se explica).</summary>
    public void Asignar(IEnumerable<PerfilItemVM> perfiles)
    {
        var lista = perfiles.ToList();
        var retirados = lista.Where(p => !p.PerfilActivo && T.Perfiles.All(a => a.IdPerfil != p.IdPerfil)).ToList();
        foreach (var p in lista.Except(retirados))
            T.AsignarPerfil(_catalogoPerfiles.FirstOrDefault(c => c.IdPerfil == p.IdPerfil) ?? new Perfil(p.IdPerfil, p.Nombre, p.Descripcion, p.PerfilActivo));
        if (retirados.Count > 0)
            AvisoEstado($"No asigno {string.Join(", ", retirados.Select(r => $"«{r.Nombre}»"))}: está retirado del catálogo y no admite asignaciones nuevas.");
        RefrescarPerfiles();
        CambiadoAqui("Perfiles");
    }

    public void Retirar(IEnumerable<PerfilItemVM> perfiles)
    {
        foreach (var p in perfiles.ToList()) T.RetirarPerfil(p.IdPerfil);
        RefrescarPerfiles();
        CambiadoAqui("Perfiles");
    }

    private async Task NuevoPerfilAsync()
    {
        if (Dialogos.PedirPerfilNuevo(string.IsNullOrWhiteSpace(_filtroPerfiles) ? null : _filtroPerfiles.Trim()) is not { } datos) return;
        var r = await Servicios.Perfiles.AltaAsync(datos.Nombre, datos.Descripcion);
        if (!r.EsCorrecto)
        {
            MostrarError("No he podido crear el perfil", r.Error!);
            return;
        }
        _catalogoPerfiles = [.. _catalogoPerfiles, r.Valor];
        _filtroPerfiles = "";
        Avisar(nameof(FiltroPerfiles));
        Asignar([new PerfilItemVM(r.Valor.IdPerfil, r.Valor.Nombre, r.Valor.Descripcion, true)]);
        AvisoEstado($"Hecho: he creado el perfil «{r.Valor.Nombre}» en el catálogo y lo he asignado a esta ficha (se guarda con la ficha).");
    }

    // ---------- notas ----------
    public string? Notas { get => T.Notas; set => Texto(T.Notas, value, v => T.Notas = v, nameof(Notas)); }
    public string? NotasInternas { get => T.NotasInternas; set => Texto(T.NotasInternas, value, v => T.NotasInternas = v, nameof(NotasInternas)); }

    // ---------- historial ----------
    public ObservableCollection<MovimientoVM> Movimientos { get; } = [];
    private bool _historialCargado;
    private EstadoVista _estadoHistorial = EstadoVista.Cargando;
    public EstadoVista EstadoHistorial { get => _estadoHistorial; private set => Fijar(ref _estadoHistorial, value); }
    private string _mensajeHistorial = "";
    public string MensajeHistorial { get => _mensajeHistorial; private set => Fijar(ref _mensajeHistorial, value); }
    public ICommand RecargarHistorialCommand { get; }

    private async Task CargarHistorialAsync()
    {
        _historialCargado = true;
        if (T.EsNuevo)
        {
            EstadoHistorial = EstadoVista.Vacio;
            MensajeHistorial = "La ficha aún no se ha guardado: su historial empieza al guardarla.";
            return;
        }
        EstadoHistorial = EstadoVista.Cargando;
        try
        {
            var lista = await Servicios.Repositorio.HistorialAsync(T.IdTercero);
            Movimientos.Clear();
            foreach (var m in lista) Movimientos.Add(new MovimientoVM(m));
            EstadoHistorial = lista.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
            Avisar(nameof(Cabeceras));
            MensajeHistorial = "Esta ficha no tiene cambios registrados desde que se migró. Cada guardado deja aquí una línea por dato cambiado.";
        }
        catch (ExcepcionDatos ex)
        {
            EstadoHistorial = EstadoVista.Error;
            MensajeHistorial = ex.Error.TextoOperador();
        }
    }

    // ---------- cambios y errores ----------
    /// <summary>Algo ha cambiado en la copia de trabajo: recalcula cambios, errores, destinatarios y cabecera.</summary>
    internal void Modificado()
    {
        if (_cargando) return;
        Cambios = CambiosFicha.Comparar(_original, T);
        if (!TieneCambios) VerCambios = false;
        EstadoDestinatarios = DestinatariosFicha.Calcular(T.DatosContacto);
        foreach (var e in Emails) e.RefrescarImplicitos();
        RecalcularErrores();
        Avisar(nameof(AvisoFacturas), nameof(AvisoAdministrativos), nameof(MostrarEmailAntiguo), nameof(MostrarTelefonoAntiguo), nameof(TelefonoAntiguoValido), nameof(MostrarFichaAntigua), nameof(HayGrupos), nameof(HayRelaciones), nameof(MostrarNotaFacturas));
        Avisar(nameof(TieneCambios), nameof(TextoCambios), nameof(Marcas), nameof(Titulo), nameof(Ayuda), nameof(NombreCabecera), nameof(InicialesCabecera), nameof(TipoTexto),
            nameof(LineaDatos), nameof(Insignias));
    }

    private void CambiadoAqui(string campo)
    {
        Tocar(T, campo);
        Modificado();
    }

    private void Texto(string? actual, string? nuevo, Action<string?> fijar, string propiedad)
    {
        if (string.Equals(actual ?? "", nuevo ?? "", StringComparison.Ordinal)) return;
        fijar(nuevo);
        Avisar(propiedad);
        CambiadoAqui(propiedad);
    }

    internal void Tocar(object modelo, string campo) => _tocados.Add((modelo, campo));

    private IEnumerable<ParteFicha> Partes() =>
        Domicilios.Cast<ParteFicha>().Concat(Emails).Concat(Telefonos).Concat(Grupos).Concat(Relaciones).Concat([CuentaCliente, CuentaRetencion, CuentaProveedor, CuentaAcreedor]);

    private void RecalcularErrores()
    {
        _errores = ValidacionFicha.Revisar(T, Domicilios).Concat(Partes().SelectMany(p => p.ErroresPropios())).Concat(_erroresGuardado).ToList();
        foreach (var p in Partes()) p.RefrescarErrores();
        foreach (var c in new[] { "TipoPersona", nameof(NombrePersona), nameof(RazonSocial), nameof(Nif), nameof(NombreAntiguo), nameof(EmailPrincipal) })
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(c));
        if (_intentoGuardar) RellenarResumen();
        Avisar(nameof(Marcas), nameof(Cabeceras));
    }

    private static readonly string[] _nombresPestanas = ["Identidad", "Contacto", "Roles", "Perfiles", "Ventas", "Compras", "Nóminas", "Contabilidad", "Notas", "Documentos", "Historial"];

    /// <summary>Título, número (perfiles, movimientos) y marca de cada pestaña.</summary>
    public IReadOnlyList<CabeceraPestana> Cabeceras
    {
        get
        {
            var marcas = Marcas;
            return _nombresPestanas.Select((n, i) => new CabeceraPestana(n,
                i == (int)PestanaFicha.Perfiles && PerfilesAsignados.Count > 0 ? PerfilesAsignados.Count.ToString() :
                i == (int)PestanaFicha.Historial && Movimientos.Count > 0 ? Movimientos.Count.ToString() : null, marcas[i])).ToList();
        }
    }

    /// <summary>Marca de cada pestaña, en el orden de <see cref="PestanaFicha"/>: punto si tiene cambios, triángulo si tiene errores.</summary>
    public IReadOnlyList<MarcaPestana> Marcas => Enum.GetValues<PestanaFicha>()
        .Select(p => new MarcaPestana(_cambios.Any(c => c.Pestana == p), _intentoGuardar && _errores.Any(e => e.Pestana == p))).ToList();

    /// <summary>Mensaje de error de un campo si debe verse (el campo se ha tocado o ya se ha intentado guardar).</summary>
    internal string? ErrorVisible(object modelo, string campo)
    {
        var e = _errores.FirstOrDefault(x => ReferenceEquals(x.Objeto, modelo) && x.Campo == campo);
        return e is not null && (_intentoGuardar || _tocados.Contains((modelo, campo))) ? e.Mensaje : null;
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => _errores.Any(e => ReferenceEquals(e.Objeto, T));
    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && ErrorVisible(T, propertyName switch { nameof(EmailPrincipal) => "Email", nameof(NombreAntiguo) => "Nombre", _ => propertyName }) is { } e ? new[] { e } : Array.Empty<string>();

    public ObservableCollection<ErrorResumen> Resumen { get; } = [];
    public bool HayResumen => Resumen.Count > 0;
    public string TituloResumen => Resumen.Count == 1 ? "No se ha podido guardar: corrige este dato." : $"No se ha podido guardar: corrige estos {Resumen.Count} datos.";
    public ICommand IrAErrorCommand { get; }

    private void RellenarResumen()
    {
        Resumen.Clear();
        foreach (var e in _errores.OrderBy(e => e.Pestana))
            Resumen.Add(new ErrorResumen(e, $"{NombrePestana(e.Pestana)} · {e.Mensaje}"));
        Avisar(nameof(HayResumen), nameof(TituloResumen));
    }

    /// <summary>La vista lleva el cursor al campo del error (pestaña, objeto y campo).</summary>
    public event EventHandler<ErrorFicha>? NavegarAError;

    private void IrAError(ErrorFicha e)
    {
        PestanaActiva = (int)e.Pestana;
        NavegarAError?.Invoke(this, e);
    }

    private static string NombrePestana(PestanaFicha p) => p switch
    {
        PestanaFicha.Identidad => "Identidad",
        PestanaFicha.Contacto => "Contacto",
        PestanaFicha.Roles => "Roles",
        PestanaFicha.Perfiles => "Perfiles",
        PestanaFicha.Notas => "Notas",
        _ => p.ToString()
    };

    // ---------- guardar ----------
    private bool _guardando;
    public bool Guardando { get => _guardando; private set => Fijar(ref _guardando, value); }
    public ICommand GuardarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand VerCambiosCommand { get; }
    public ICommand IrARolCommand { get; }
    public ICommand ElegirTipoCommand { get; }
    public ICommand RecargarCommand { get; }

    private IReadOnlyList<ErrorFicha> _erroresGuardado = [];

    public async Task<bool> GuardarAsync()
    {
        if (Guardando) return false;
        _intentoGuardar = true;
        _erroresGuardado = [];
        // Confirma las cuentas escritas y aún sin comprobar (el foco puede seguir en el campo).
        foreach (var c in new[] { CuentaCliente, CuentaRetencion, CuentaProveedor, CuentaAcreedor }) await c.ConfirmarAsync();
        RecalcularErrores();
        if (_errores.Count > 0)
        {
            GuardadoFallido = true;
            Avisar(nameof(HayResumen));
            return false;
        }

        Guardando = true;
        try
        {
            var copia = T.Clonar();
            var r = await Task.Run(() => Servicios.Repositorio.GuardarAsync(copia, Servicios.Actor));
            if (!r.EsCorrecto)
            {
                _erroresGuardado = r.Errores.Select(ErrorDeGuardado).ToList();
                RecalcularErrores();
                GuardadoFallido = true;
                return false;
            }
            var nombre = r.Valor.Nombre;
            _rolNuevo = null;
            await MostrarAsync(r.Valor);
            GuardadoFallido = false;
            Resumen.Clear();
            Avisar(nameof(HayResumen));
            _modulo?.FichaGuardada(this);
            AvisoEstado($"Hecho: he guardado la ficha de {nombre}.");
            return true;
        }
        catch (ConflictoConcurrenciaException ex)
        {
            GuardadoFallido = true;
            var opcion = Dialogos.Preguntar("Ficha modificada por otra persona",
                $"No he guardado tus cambios en {NombreCabecera} porque otra persona ha cambiado la ficha mientras la editabas. " +
                "Si recargo la ficha verás sus cambios, pero perderás los tuyos; puedes anotarlos antes con «Ver cuáles».",
                DescribirCambios(),
                new OpcionMensaje("Seguir editando", "No recarga: puedes copiar tus cambios antes", Principal: true, Cancelar: true),
                new OpcionMensaje("Recargar la ficha", "Pierde tus cambios y muestra la ficha tal como está guardada ahora", Peligro: true));
            _ = ex;
            if (opcion == 1) await CargarAsync(T.IdTercero);
            return false;
        }
        catch (ExcepcionDatos ex)
        {
            GuardadoFallido = true;
            var opcion = Dialogos.Preguntar("No he podido guardar la ficha",
                $"No he podido guardar la ficha de {NombreCabecera}: {ex.Error.Mensaje} Tus cambios siguen en la ficha; no se ha guardado nada a medias.",
                new[] { ex.Error.Causa, ex.Error.AccionCorrectora }.OfType<string>().ToList(),
                new OpcionMensaje("Reintentar", "Vuelve a intentar guardar ahora", Principal: true),
                new OpcionMensaje("Seguir editando", "Deja los cambios en la ficha para guardarlos más tarde", Cancelar: true));
            Guardando = false;
            if (opcion == 0) return await GuardarAsync();
            return false;
        }
        finally
        {
            Guardando = false;
        }
    }

    /// <summary>Lleva un error del ente al campo que lo corrige (o al resumen, si no tiene campo).</summary>
    private ErrorFicha ErrorDeGuardado(Error e)
    {
        var texto = e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "");
        return e.Codigo switch
        {
            "Tercero.SinTipo" => new(PestanaFicha.Identidad, T, "TipoPersona", texto),
            "Tercero.SinNombre" => new(PestanaFicha.Identidad, T, T.TipoPersona == TipoPersona.Fisica ? nameof(NombrePersona) : "Nombre", texto),
            "Tercero.SinRazonSocial" => new(PestanaFicha.Identidad, T, nameof(RazonSocial), texto),
            "Tercero.EmailLargo" => new(PestanaFicha.Contacto, T, "Email", texto),
            var c when c.StartsWith("NIF.", StringComparison.Ordinal) => new(PestanaFicha.Identidad, T, nameof(Nif), texto),
            var c when c.StartsWith("Contactos.", StringComparison.Ordinal) || c.StartsWith("Relaciones.", StringComparison.Ordinal) => new(PestanaFicha.Contacto, new object(), "", texto),
            var c when c.StartsWith("Perfiles.", StringComparison.Ordinal) => new(PestanaFicha.Perfiles, new object(), "", texto),
            var c when c.StartsWith("Roles.", StringComparison.Ordinal) || c.StartsWith("Cuentas.", StringComparison.Ordinal) => new(PestanaFicha.Roles, new object(), "", texto),
            _ => new(PestanaFicha.Identidad, new object(), "", texto),
        };
    }

    // ---------- avisos y errores puntuales ----------
    internal void AvisoEstado(string texto) => _modulo?.Avisar(texto);

    internal void MostrarError(string titulo, Error error) =>
        Dialogos.Preguntar(titulo, error.Mensaje, new[] { error.Causa, error.AccionCorrectora }.OfType<string>().ToList(),
            new OpcionMensaje("Volver a la ficha", "Cierra este aviso", Principal: true, Cancelar: true));

    public static string Iniciales(string nombre)
    {
        var partes = (nombre ?? "").Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 1 && char.IsLetter(p[0]) && !EsParticula(p)).Take(2).ToList();
        return partes.Count == 0 ? "?" : string.Concat(partes.Select(p => char.ToUpperInvariant(p[0])));
    }

    private static bool EsParticula(string p) => p.ToLowerInvariant() is "de" or "del" or "la" or "las" or "los" or "y" or "sl" or "sa" or "slu" or "cb" or "sll";

    private sealed class ComparadorTocado : IEqualityComparer<(object, string)>
    {
        public bool Equals((object, string) x, (object, string) y) => ReferenceEquals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        public int GetHashCode((object, string) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1), obj.Item2);
    }
}
