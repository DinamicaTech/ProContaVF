using System.Windows.Input;
using ProConta.Contabilidad.Cuentas;
using ProConta.Organizacion.Geografia;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.Terceros.UI.Logica;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>Adónde va el cursor después de escribir el código postal (maqueta: CP → provincia → población → vía).</summary>
public enum SiguienteFoco
{
    Ninguno,
    Poblacion,
    Via
}

/// <summary>
/// Domicilio de la ficha en una línea País › CP › Provincia › Población › Vía y número. El principal vive en las
/// columnas de Terceros; los adicionales, en DomiciliosTercero (con finalidad y activo).
/// </summary>
public sealed class DomicilioVM : ParteFicha, IDomicilioEditable
{
    private readonly Tercero _t;
    private Domicilio? _d;

    internal DomicilioVM(FichaTerceroVM ficha, Tercero t, Domicilio? adicional) : base(ficha)
    {
        _t = t;
        _d = adicional;
        QuitarCommand = new ComandoDelegado(() => Ficha.QuitarDomicilio(this), () => !EsPrincipal);
        MarcarPrincipalCommand = new ComandoDelegado(() => Ficha.MarcarPrincipal(this), () => !EsPrincipal);
        _poblaciones = CalcularPoblaciones();
    }

    public override object Modelo => this;
    protected override IEnumerable<string> CamposConError => [nameof(CodigoPostal), nameof(Direccion), nameof(TipoDomicilio)];

    public bool EsPrincipal => _d is null;
    internal Domicilio? Adicional => _d;
    public string Etiqueta => EsPrincipal ? "Principal" : TiposDomicilio.Texto(_d!.TipoDomicilio);

    public IReadOnlyList<Opcion> Tipos => TiposDomicilio.ListaCon(_d?.TipoDomicilio);

    public string? TipoDomicilio
    {
        get => _d?.TipoDomicilio;
        set { if (_d is null || value == _d.TipoDomicilio) return; _d = Reemplazar(_d with { TipoDomicilio = value ?? "" }); Avisar(nameof(TipoDomicilio), nameof(Etiqueta)); Cambiado(nameof(TipoDomicilio)); }
    }

    public bool Activo
    {
        get => _d?.Activo ?? true;
        set { if (_d is null || value == _d.Activo) return; _d = Reemplazar(_d with { Activo = value }); Avisar(); Cambiado(nameof(Activo)); }
    }

    public IReadOnlyList<Opcion> PaisesLista => Paises.ListaCon(Pais);

    public string? Pais
    {
        get => EsPrincipal ? _t.Pais : _d!.Pais;
        set
        {
            if (value == Pais) return;
            if (EsPrincipal) _t.Pais = value; else _d = Reemplazar(_d! with { Pais = value });
            Avisar(nameof(Pais), nameof(EsEspana), nameof(AyudaProvincia), nameof(Provincias));
            Cambiado(nameof(Pais));
        }
    }

    public bool EsEspana => Paises.EsEspana(Pais);

    public string? CodigoPostal
    {
        get => EsPrincipal ? _t.CodigoPostal : _d!.CodigoPostal;
        set
        {
            if (value == CodigoPostal) return;
            if (EsPrincipal) _t.CodigoPostal = value; else _d = Reemplazar(_d! with { CodigoPostal = value });
            Avisar();
            Cambiado(nameof(CodigoPostal));
        }
    }

    public string? Provincia
    {
        get => EsPrincipal ? _t.Provincia : _d!.Provincia;
        set
        {
            if (value == Provincia) return;
            if (EsPrincipal) _t.Provincia = value; else _d = Reemplazar(_d! with { Provincia = value });
            Avisar();
            Poblaciones = CalcularPoblaciones();
            Cambiado(nameof(Provincia));
        }
    }

    public string? Poblacion
    {
        get => EsPrincipal ? _t.Poblacion : _d!.Poblacion;
        set
        {
            if (value == Poblacion) return;
            if (EsPrincipal) _t.Poblacion = value; else _d = Reemplazar(_d! with { Poblacion = value });
            Avisar();
            Cambiado(nameof(Poblacion));
        }
    }

    public string? Direccion
    {
        get => EsPrincipal ? _t.Direccion : _d!.Direccion;
        set
        {
            if (value == Direccion) return;
            if (EsPrincipal) _t.Direccion = value; else _d = Reemplazar(_d! with { Direccion = value ?? "" });
            Avisar();
            Cambiado(nameof(Direccion));
        }
    }

    public IReadOnlyList<string> Provincias => Ficha.Geografia?.Provincias(EsEspana ? null : Pais) ?? [];

    private IReadOnlyList<string> _poblaciones;
    public IReadOnlyList<string> Poblaciones { get => _poblaciones; private set => Fijar(ref _poblaciones, value); }

    public string AyudaProvincia => EsEspana ? "Provincia: en España la fija el código postal; puedes elegir otra si hace falta" : "Región o provincia, escrita a mano fuera de España";

    public ICommand QuitarCommand { get; }
    public ICommand MarcarPrincipalCommand { get; }

    private IReadOnlyList<string> CalcularPoblaciones()
    {
        var geo = Ficha.Geografia;
        if (geo is null) return [];
        if (EsEspana && CodigoPostal is { Length: 5 } cp)
        {
            var r = geo.Resolver(cp);
            if (r.Poblaciones.Count > 0) return r.Poblaciones;
        }
        return string.IsNullOrWhiteSpace(Provincia) ? [] : geo.Poblaciones(Provincia);
    }

    /// <summary>
    /// El operador ha terminado de escribir el código postal (RF-TER-05): en España el CP fija la provincia; si solo
    /// corresponde a una población también la elige y el cursor pasa a la vía; si hay varias, pasa a la lista de
    /// poblaciones de ese CP. Sin correspondencia no se inventa nada. Al cargar una ficha nunca se llama.
    /// </summary>
    public SiguienteFoco ResolverCodigoPostal()
    {
        var geo = Ficha.Geografia;
        if (geo is null || !EsEspana || CodigoPostal?.Trim() is not { Length: 5 } cp || !cp.All(char.IsAsciiDigit)) return SiguienteFoco.Ninguno;
        var r = geo.Resolver(cp);
        if (r.SinCorrespondencia) return SiguienteFoco.Ninguno;
        if (r.ProvinciaUnica is { } provincia) Provincia = provincia;
        Poblaciones = r.Poblaciones.Count > 0 ? r.Poblaciones : CalcularPoblaciones();
        if (r.PoblacionUnica is { } poblacion)
        {
            Poblacion = poblacion;
            return SiguienteFoco.Via;
        }
        return r.Poblaciones.Count > 1 ? SiguienteFoco.Poblacion : SiguienteFoco.Ninguno;
    }

    private Domicilio Reemplazar(Domicilio nuevo)
    {
        var i = Ficha.T.Domicilios.IndexOf(_d!);
        if (i >= 0) Ficha.T.Domicilios[i] = nuevo;
        return nuevo;
    }
}

/// <summary>Email o teléfono de la lista, con su etiqueta, activo, finalidades y persona de contacto.</summary>
public sealed class DatoContactoVM : ParteFicha
{
    internal DatoContactoVM(FichaTerceroVM ficha, DatoContacto dato) : base(ficha)
    {
        Dato = dato;
        QuitarCommand = new ComandoDelegado(() => Ficha.QuitarDato(this));
    }

    internal DatoContacto Dato { get; }
    public override object Modelo => Dato;
    protected override IEnumerable<string> CamposConError => [nameof(Valor)];

    public bool EsEmail => Dato.Tipo == TipoDatoContacto.Email;
    public string? Etiqueta { get => Dato.Etiqueta; set { if (value == Dato.Etiqueta) return; Dato.Etiqueta = value; Avisar(); Cambiado(nameof(Etiqueta)); } }
    public string Valor { get => Dato.Valor; set { if (value == Dato.Valor) return; Dato.Valor = value ?? ""; Avisar(); Cambiado(nameof(Valor)); } }
    public bool Activo { get => Dato.Activo; set { if (value == Dato.Activo) return; Dato.Activo = value; Avisar(); Cambiado(nameof(Activo)); } }
    public bool EnvioFacturas { get => Dato.EnvioFacturas; set { if (value == Dato.EnvioFacturas) return; Dato.EnvioFacturas = value; Avisar(); Cambiado(nameof(EnvioFacturas)); } }
    public bool EnviosAdministrativos { get => Dato.EnviosAdministrativos; set { if (value == Dato.EnviosAdministrativos) return; Dato.EnviosAdministrativos = value; Avisar(); Cambiado(nameof(EnviosAdministrativos)); } }

    /// <summary>Persona de contacto a la que pertenece («General» si es de la organización).</summary>
    public OpcionGrupo? Grupo
    {
        get => Ficha.OpcionesGrupo.FirstOrDefault(o => ReferenceEquals(o.Grupo?.Grupo, Dato.ContactoLibre));
        set { if (value is null || ReferenceEquals(value.Grupo?.Grupo, Dato.ContactoLibre)) return; Dato.ContactoLibre = value.Grupo?.Grupo; Avisar(); Cambiado(nameof(Grupo)); }
    }

    internal void RefrescarGrupo() => Avisar(nameof(Grupo));

    /// <summary>Recibe esa finalidad sin estar marcado (es el único email activo): la casilla se ve atenuada como marcada.</summary>
    public bool FacturasImplicito => Ficha.EstadoDestinatarios.RecibeImplicito(Dato, FinalidadEnvio.Facturas);
    public bool AdministrativosImplicito => Ficha.EstadoDestinatarios.RecibeImplicito(Dato, FinalidadEnvio.Administrativos);
    public string AyudaFacturas => FacturasImplicito ? DestinatariosFicha.AyudaImplicita(FinalidadEnvio.Facturas) : "Recibe las facturas que se envían por email";
    public string AyudaAdministrativos => AdministrativosImplicito ? DestinatariosFicha.AyudaImplicita(FinalidadEnvio.Administrativos) : "Recibe las comunicaciones administrativas (recordatorios, certificados…)";

    internal void RefrescarImplicitos() => Avisar(nameof(FacturasImplicito), nameof(AdministrativosImplicito), nameof(AyudaFacturas), nameof(AyudaAdministrativos));

    public string AyudaValor => EsEmail
        ? "Dirección de email. Marca a la derecha si recibe facturas o comunicaciones administrativas"
        : "Número de teléfono, con prefijo si es del extranjero";

    public ICommand QuitarCommand { get; }
}

/// <summary>Persona de contacto libre (sin crear un tercero): nombre y departamento; sus emails y teléfonos se le asignan en la lista.</summary>
public sealed class GrupoContactoVM : ParteFicha
{
    internal GrupoContactoVM(FichaTerceroVM ficha, ContactoLibre grupo) : base(ficha)
    {
        Grupo = grupo;
        QuitarCommand = new ComandoDelegado(() => Ficha.QuitarGrupo(this));
    }

    internal ContactoLibre Grupo { get; }
    public override object Modelo => Grupo;
    protected override IEnumerable<string> CamposConError => [nameof(Nombre)];

    public string Nombre { get => Grupo.Nombre; set { if (value == Grupo.Nombre) return; Grupo.Nombre = value ?? ""; Avisar(); Ficha.GrupoRenombrado(); Cambiado(nameof(Nombre)); } }
    public string? Departamento { get => Grupo.Departamento; set { if (value == Grupo.Departamento) return; Grupo.Departamento = value; Avisar(); Cambiado(nameof(Departamento)); } }
    public ICommand QuitarCommand { get; }
    public override string ToString() => string.IsNullOrWhiteSpace(Nombre) ? "(sin nombre)" : Nombre;
}

/// <summary>
/// Relación persona-organización (RF-TER-11) vista desde esta ficha. «Quitar» solo existe mientras no se ha guardado;
/// una relación guardada no se borra: «Cerrar vigencia» le pone la fecha de fin de hoy.
/// </summary>
public sealed class RelacionVM : ParteFicha
{
    private string _textoDesde, _textoHasta;
    private string? _errorDesde, _errorHasta;

    internal RelacionVM(FichaTerceroVM ficha, RelacionContacto r) : base(ficha)
    {
        Relacion = r;
        _textoDesde = r.FechaInicio is { } i ? Formatos.Fecha(i) : "";
        _textoHasta = r.FechaFin is { } f ? Formatos.Fecha(f) : "";
        AccionCommand = new ComandoDelegado(Accion);
        AbrirOtroCommand = new ComandoDelegado(() => Ficha.AbrirOtraFicha(IdOtro), () => IdOtro > 0);
    }

    internal RelacionContacto Relacion { get; }
    public override object Modelo => Relacion;
    protected override IEnumerable<string> CamposConError => [nameof(TipoRelacion), nameof(TextoDesde), nameof(TextoHasta)];

    private bool SoyPersona => Relacion.IdTerceroPersona == 0 || Relacion.IdTerceroPersona == Ficha.T.IdTercero && Relacion.IdTerceroOrganizacion != Ficha.T.IdTercero;
    public string Papel => SoyPersona ? "Organización" : "Persona";
    public string NombreOtro => SoyPersona ? Relacion.NombreOrganizacion : Relacion.NombrePersona;
    public int IdOtro => SoyPersona ? Relacion.IdTerceroOrganizacion : Relacion.IdTerceroPersona;
    public string InicialesOtro => FichaTerceroVM.Iniciales(NombreOtro);
    public string AyudaOtro => SoyPersona
        ? $"Este tercero es «{Relacion.TipoRelacion}» de {Relacion.NombreOrganizacion}. Pulsa para abrir su ficha"
        : $"{Relacion.NombrePersona} es «{Relacion.TipoRelacion}» de este tercero. Pulsa para abrir su ficha";

    public IReadOnlyList<string> Tipos => RelacionContacto.TiposHabituales;

    public string TipoRelacion
    {
        get => Relacion.TipoRelacion;
        set { if (value == Relacion.TipoRelacion) return; Relacion.TipoRelacion = value ?? ""; Avisar(); Cambiado(nameof(TipoRelacion)); }
    }

    public string TextoDesde
    {
        get => _textoDesde;
        set
        {
            if (!Fijar(ref _textoDesde, value)) return;
            _errorDesde = ValidacionFicha.LeerFecha(value, out var f) ? null : $"«{value}» no es una fecha: escríbela como dd/mm/aaaa.";
            if (_errorDesde is null) Relacion.FechaInicio = f;
            Avisar(nameof(Estado), nameof(EsVigente));
            Cambiado(nameof(TextoDesde));
        }
    }

    public string TextoHasta
    {
        get => _textoHasta;
        set
        {
            if (!Fijar(ref _textoHasta, value)) return;
            _errorHasta = ValidacionFicha.LeerFecha(value, out var f) ? null : $"«{value}» no es una fecha: escríbela como dd/mm/aaaa.";
            if (_errorHasta is null) Relacion.FechaFin = f;
            Avisar(nameof(Estado), nameof(EsVigente), nameof(AccionTexto), nameof(AccionAyuda), nameof(HayAccion));
            Cambiado(nameof(TextoHasta));
        }
    }

    public string? Observaciones { get => Relacion.Observaciones; set { if (value == Relacion.Observaciones) return; Relacion.Observaciones = value; Avisar(); Cambiado(nameof(Observaciones)); } }

    public bool EsNueva => Relacion.EsNueva;
    public bool EsVigente => Relacion.EsVigente(DateTime.Today);
    public string Estado => EsNueva ? "Sin guardar" : EsVigente ? "Vigente" : "Finalizada";

    public bool HayAccion => EsNueva || EsVigente;
    public string AccionTexto => EsNueva ? "Quitar" : "Cerrar vigencia";
    public string AccionAyuda => EsNueva
        ? "Quita esta relación, que aún no se ha guardado"
        : "Las relaciones guardadas no se borran: les pongo la fecha de fin de hoy y quedan como históricas al guardar la ficha";
    public ICommand AccionCommand { get; }
    public ICommand AbrirOtroCommand { get; }

    private void Accion()
    {
        if (EsNueva) Ficha.QuitarRelacion(this);
        else TextoHasta = Formatos.Fecha(DateTime.Today);
    }

    internal override IEnumerable<Logica.ErrorFicha> ErroresPropios()
    {
        if (_errorDesde is not null) yield return new(PestanaFicha.Contacto, Relacion, nameof(TextoDesde), _errorDesde);
        if (_errorHasta is not null) yield return new(PestanaFicha.Contacto, Relacion, nameof(TextoHasta), _errorHasta);
    }
}

/// <summary>
/// Cuenta contable de un rol en la sociedad de trabajo (terceros/roles 2026-11-01 09:52): se escribe el código, se ve
/// su nombre para comprobar que es la correcta y, si no existe en el ejercicio, se explica y se ofrece crearla.
/// </summary>
public sealed class CuentaRolVM : ParteFicha
{
    private string _codigo = "";
    private string _nombre = "";
    private string? _aviso, _error, _accion;
    private Func<Task>? _hacerAccion;

    internal CuentaRolVM(FichaTerceroVM ficha, ClaseCuentaRol clase) : base(ficha)
    {
        Clase = clase;
        AccionCommand = new ComandoDelegado(() => { if (_hacerAccion is { } a) _ = a(); }, () => _hacerAccion is not null);
        ConfirmarCommand = new ComandoDelegado(() => _ = ConfirmarAsync());
    }

    public ClaseCuentaRol Clase { get; }
    public override object Modelo => this;
    protected override IEnumerable<string> CamposConError => [nameof(Codigo)];

    public string Titulo => Clase switch
    {
        ClaseCuentaRol.Cliente => "Cuenta contable",
        ClaseCuentaRol.Retencion => "Cuenta de retención",
        ClaseCuentaRol.Proveedor => "Cuenta de proveedor",
        _ => "Cuenta de acreedor"
    };
    public string AyudaCodigo => $"Código de la {ServicioRoles.Describir(Clase)} en {Ficha.NombreSociedad}. Vale para todos sus ejercicios; al salir del campo compruebo que existe en el ejercicio {Ficha.Ejercicio}";

    public string Codigo { get => _codigo; set => Fijar(ref _codigo, value?.Trim() ?? ""); }
    public string Nombre { get => _nombre; private set => Fijar(ref _nombre, value); }
    public string? Aviso { get => _aviso; private set { Fijar(ref _aviso, value); Avisar(nameof(HayAviso)); } }
    public bool HayAviso => _aviso is not null;
    public string? AccionTexto { get => _accion; private set { Fijar(ref _accion, value); Avisar(nameof(HayAccion)); } }
    public bool HayAccion => _accion is not null;
    public ICommand AccionCommand { get; }
    public ICommand ConfirmarCommand { get; }

    private string _codigoConfirmado = "";

    /// <summary>Muestra la cuenta asignada, resuelta en el ejercicio de trabajo.</summary>
    internal void Cargar(CuentaRol? cuenta)
    {
        _error = null;
        _hacerAccion = null;
        AccionTexto = null;
        if (cuenta is null)
        {
            _codigoConfirmado = Codigo = "";
            Nombre = "";
            Aviso = null;
        }
        else
        {
            _codigoConfirmado = Codigo = cuenta.Codigo;
            Nombre = cuenta.Nombre;
            if (cuenta.ExisteEnEjercicio) Aviso = null;
            else
            {
                Aviso = $"La cuenta {cuenta.Codigo} aún no existe en el ejercicio {Ficha.Ejercicio}.";
                AccionTexto = $"Crearla en {Ficha.Ejercicio}";
                _hacerAccion = () => CrearEnEjercicioAsync(cuenta);
            }
        }
        RefrescarErrores();
    }

    /// <summary>El operador ha escrito un código: si existe en el ejercicio se asigna; si no, se explica y se ofrece crearla.</summary>
    public async Task ConfirmarAsync()
    {
        if (Codigo == _codigoConfirmado && _error is null) return;
        _codigoConfirmado = Codigo;
        _hacerAccion = null;
        AccionTexto = null;
        Aviso = null;
        if (Codigo.Length == 0)
        {
            _error = null;
            Nombre = "";
            Ficha.FijarCuenta(Clase, null);
            Cambiado(nameof(Codigo));
            return;
        }
        var r = await Ficha.Servicios.Roles.BuscarPorCodigoAsync(Clase, Codigo, Ficha.Servicios.Contexto);
        if (r.EsCorrecto)
        {
            _error = null;
            Nombre = r.Valor.Nombre;
            Ficha.FijarCuenta(Clase, r.Valor.IdIdentidadContable);
        }
        else
        {
            var codigo = Codigo;
            _error = $"La cuenta {codigo} no existe en el ejercicio {Ficha.Ejercicio}. Revisa el código o créala con el nombre del tercero.";
            Nombre = "";
            AccionTexto = $"Crear la cuenta {codigo}";
            _hacerAccion = () => CrearNuevaAsync(codigo);
        }
        Cambiado(nameof(Codigo));
    }

    private async Task CrearNuevaAsync(string codigo)
    {
        var nombre = Recortar((Clase == ClaseCuentaRol.Retencion ? "Retención IRPF " : "") + Ficha.NombreParaCuenta(), ReglasCuenta.LongitudMaximaNombre);
        var opcion = Ficha.Dialogos.Preguntar($"Crear la cuenta {codigo}",
            $"Creo ahora la cuenta {codigo} «{nombre}» en el ejercicio {Ficha.Ejercicio} de {Ficha.NombreSociedad} y la asigno como {ServicioRoles.Describir(Clase)}. " +
            "La cuenta queda creada aunque después descartes los cambios de la ficha.", null,
            new OpcionMensaje("Crear la cuenta", $"Da de alta la cuenta {codigo} en el plan de cuentas del ejercicio {Ficha.Ejercicio}", Principal: true),
            new OpcionMensaje("Volver a la ficha", "No crea nada", Cancelar: true));
        if (opcion != 0) return;
        var r = await Ficha.Servicios.Cuentas.AltaAsync(Ficha.Servicios.Contexto, new AltaCuenta(codigo, nombre));
        if (!r.EsCorrecto)
        {
            Ficha.MostrarError("No he podido crear la cuenta", r.Error!);
            return;
        }
        Codigo = _codigoConfirmado = r.Valor.Codigo;
        Nombre = r.Valor.Nombre;
        _error = null;
        _hacerAccion = null;
        AccionTexto = null;
        Ficha.FijarCuenta(Clase, r.Valor.IdIdentidadContable);
        Ficha.AvisoEstado($"Hecho: he creado la cuenta {r.Valor.Codigo} «{r.Valor.Nombre}» en el ejercicio {Ficha.Ejercicio}.");
        Cambiado(nameof(Codigo));
    }

    private async Task CrearEnEjercicioAsync(CuentaRol cuenta)
    {
        var r = await Ficha.Servicios.Cuentas.AltaEnEjercicioAsync(Ficha.Servicios.Contexto, cuenta.IdIdentidadContable);
        if (!r.EsCorrecto)
        {
            Ficha.MostrarError("No he podido crear la cuenta en este ejercicio", r.Error!);
            return;
        }
        Cargar(new CuentaRol(Clase, cuenta.IdIdentidadContable, r.Valor, r.Valor));
        Ficha.AvisoEstado($"Hecho: la cuenta {r.Valor.Codigo} ya existe también en el ejercicio {Ficha.Ejercicio}.");
    }

    internal override IEnumerable<Logica.ErrorFicha> ErroresPropios()
    {
        if (_error is not null) yield return new(PestanaFicha.Roles, this, nameof(Codigo), _error);
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}

/// <summary>Perfil en las listas de disponibles o asignados.</summary>
public sealed class PerfilItemVM(int idPerfil, string nombre, string? descripcion, bool perfilActivo)
{
    public int IdPerfil { get; } = idPerfil;
    public string Nombre { get; } = nombre;
    public string? Descripcion { get; } = descripcion;
    public bool PerfilActivo { get; } = perfilActivo;
    public string Ayuda => PerfilActivo
        ? (string.IsNullOrWhiteSpace(Descripcion) ? Nombre : $"{Nombre}: {Descripcion}") + ". Doble clic, Intro o la flecha para moverlo a la otra lista"
        : $"{Nombre}: retirado del catálogo. Conserva las asignaciones que ya tenía, pero no admite nuevas";
}

/// <summary>Movimiento del historial con los textos de la tabla.</summary>
public sealed class MovimientoVM(MovimientoHistorial m)
{
    public DateTime FechaHora { get; } = m.FechaHora;
    public string Fecha => m.FechaHora.ToString("dd/MM/yyyy HH:mm", Formatos.Cultura);
    public string Operador { get; } = m.Operador;
    public string Seccion { get; } = m.Entidad switch
    {
        "Tercero" or "Terceros" => "Identidad",
        "DomiciliosTercero" => "Domicilios",
        "Clientes" => "Rol Cliente",
        "Proveedores" => "Rol Proveedor",
        "Contactos" => "Rol Contacto",
        "PerfilesTerceros" => "Perfiles",
        "RelacionesContactos" => "Relaciones",
        "DatosContacto" => "Contacto y domicilios",
        "Relacion" => "Relaciones",
        "Perfil" => "Perfiles",
        "Cliente" => "Rol Cliente",
        "Proveedor" => "Rol Proveedor",
        "Contacto" => "Rol Contacto",
        var e => e
    };
    public string Operacion { get; } = m.Operacion switch { "ALTA" => "Alta", "BAJA" => "Baja", "MODIFICACION" => "Cambio", var o => o };
    public string Campo { get; } = m.Campo;
    public string? Antes { get; } = m.ValorAntiguo;
    public string? Despues { get; } = m.ValorNuevo;
}

/// <summary>Insignia de un rol activo en la cabecera; al pulsarla se va a ese rol en la pestaña Roles.</summary>
public sealed record InsigniaRol(string Rol, string Texto, string Id, string Ayuda);

/// <summary>Línea del resumen de errores con enlace al campo.</summary>
public sealed record ErrorResumen(Logica.ErrorFicha Error, string Texto);

/// <summary>Marca de una pestaña de la ficha.</summary>
public sealed record MarcaPestana(bool Cambios, bool Error)
{
    public bool SoloCambios => Cambios && !Error;
}

/// <summary>Título de una pestaña de la ficha con su número (si lo tiene) y su marca.</summary>
public sealed record CabeceraPestana(string Texto, string? Numero, MarcaPestana Marca);

/// <summary>Opción de la columna «Persona» de un email o teléfono: «General» o una persona de contacto.</summary>
public sealed class OpcionGrupo(GrupoContactoVM? grupo)
{
    public static readonly OpcionGrupo General = new(null);
    public GrupoContactoVM? Grupo { get; } = grupo;
    public string Nombre => Grupo is null ? "General" : Grupo.ToString();
    public override string ToString() => Nombre;
}
