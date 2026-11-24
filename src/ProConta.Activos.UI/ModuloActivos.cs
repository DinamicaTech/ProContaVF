using ProConta.Activos.Gestion;
using ProConta.Activos.Subcuentas;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Activos.UI;

/// <summary>Contexto de la pantalla de activos y lo que abre fuera de ella (lo conecta el contenedor).</summary>
public sealed record ContextoActivos(ServicioActivos Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Action<string> Avisar)
{
    /// <summary>Abre el Mayor de unas subcuentas en unos ejercicios (vacío: el de trabajo).</summary>
    public Action<IReadOnlyList<string>, IReadOnlyList<int>>? AbrirMayor { get; init; }

    /// <summary>Abre Contabilidad › Amortización de activos.</summary>
    public Action? AbrirAmortizacion { get; init; }

    /// <summary>Abre una factura de compra en Compras › Facturas recibidas (apartado Facturas de la ficha).</summary>
    public Action<int>? AbrirFacturaCompra { get; init; }

    /// <summary>Abre una factura de venta o un borrador (apartado Ventas de la ficha).</summary>
    public Action<int>? AbrirFacturaVenta { get; init; }

    /// <summary>Abre una factura programada (apartado Ventas de la ficha).</summary>
    public Action<int>? AbrirProgramada { get; init; }

    /// <summary>Panel del gestor documental para un activo (su Id y su nombre): apartado Documentos de la ficha (documentos 2026-11-18).</summary>
    public Func<int, string, object>? CrearPanelDocumentos { get; init; }

    /// <summary>Cotizaciones por ISIN y precio en euros de los activos financieros (activos/activos-financieros); null sin conexión configurada.</summary>
    public Financieros.ICotizaciones? Cotizaciones { get; init; }

    /// <summary>Cartera financiera con sus cotizaciones al día, para la pantalla de análisis (activos/activos-financieros); null si no está disponible.</summary>
    public ServicioCarteraFinanciera? Cartera { get; init; }
}

/// <summary>Apartado de la ficha.</summary>
/// <remarks>El índice de cada valor es el de su pestaña en la ficha: Operaciones (activos financieros) va al final.</remarks>
public enum ApartadoFicha { Datos, CuentaResultados, Facturas, Ventas, Subcuentas, Amortizacion, Documentos, Operaciones }

/// <summary>Diálogos de la pantalla de activos (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosActivos
{
    /// <summary>Devuelve el índice de la opción elegida.</summary>
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    void Error(string titulo, string mensaje);

    /// <summary>Elige un centro de coste de la lista; null si se cancela.</summary>
    CentroParaActivo? ElegirCentro(string titulo, string explicacion, IReadOnlyList<CentroParaActivo> centros, int? actual, bool soloImputables);

    /// <summary>Pide dónde guardar un Excel; null si se cancela.</summary>
    string? GuardarExcel(string nombrePropuesto);

    /// <summary>Muestra el alta paso a paso; true si se ha creado el activo (el VM guarda su Id).</summary>
    bool Alta(AltaActivoVM alta);

    /// <summary>Abre un fichero con el programa del sistema.</summary>
    void AbrirFichero(string ruta);

    /// <summary>Abre una dirección web en el navegador predeterminado.</summary>
    void AbrirEnNavegador(string url);

    /// <summary>Muestra «Nueva compra…» de un activo financiero; true si se ha guardado.</summary>
    bool NuevaCompra(NuevaCompraVM compra);

    /// <summary>Pide un importe en euros con una explicación; null si se cancela.</summary>
    decimal? PedirImporte(string titulo, string explicacion, decimal actual);
}

/// <summary>
/// Activos dentro del contenedor (activos 2026-11-17 18:50): una pestaña con el listado y una por cada ficha abierta, como mucho cinco (como en
/// Terceros: al abrir la sexta se cierra la abierta hace más tiempo sin cambios).
/// </summary>
public sealed class ModuloActivos(ContextoActivos contexto, IAnfitrionPestanas anfitrion, IDialogosActivos dialogos)
{
    public const int MaximoFichas = 5;
    private readonly List<FichaActivoVM> _fichas = [];
    private ListadoActivosVM? _listado;
    private CarteraFinancieraVM? _cartera;
    private long _orden;

    internal ContextoActivos Contexto => contexto;
    internal IDialogosActivos Dialogos => dialogos;

    /// <summary>Abre (o activa) el listado; con <paramref name="idCentro"/>, filtrado por ese centro de coste.</summary>
    public void AbrirListado(int? idCentro = null, string? textoCentro = null)
    {
        if (_listado is null)
        {
            _listado = new ListadoActivosVM(this);
            anfitrion.Abrir(_listado);
        }
        else anfitrion.Activar(_listado);
        _listado.FiltrarCentro(idCentro, textoCentro);
        _ = _listado.CargarAsync();
    }

    internal void ListadoCerrado() => _listado = null;

    /// <summary>
    /// Abre (o activa) el análisis de los activos financieros (activos/activos-financieros, maqueta v4): enseña lo guardado y después actualiza las
    /// cotizaciones.
    /// </summary>
    public void AbrirCartera()
    {
        if (_cartera is null)
        {
            _cartera = new CarteraFinancieraVM(this);
            anfitrion.Abrir(_cartera);
            _ = _cartera.CargarAsync();
        }
        else anfitrion.Activar(_cartera);
    }

    internal void CarteraCerrada() => _cartera = null;

    /// <summary>Abre (o activa) la ficha de un activo en un apartado.</summary>
    public void AbrirFicha(int idActivo, ApartadoFicha apartado = ApartadoFicha.Datos)
    {
        if (_fichas.FirstOrDefault(f => f.IdActivo == idActivo) is { } abierta)
        {
            abierta.Apartado = apartado;
            anfitrion.Activar(abierta);
            return;
        }
        FichaActivoVM? sustituida = null;
        if (_fichas.Count >= MaximoFichas)
        {
            sustituida = _fichas.Where(f => !f.TieneCambios).OrderBy(f => f.OrdenApertura).FirstOrDefault();
            if (sustituida is null)
            {
                dialogos.Preguntar("Cinco fichas con cambios",
                    $"Ya tienes {MaximoFichas} fichas de activos abiertas y todas tienen cambios sin guardar, así que no abro otra para no perder nada. " +
                    "Guarda o cancela los cambios de alguna y vuelve a intentarlo.", _fichas.Select(f => f.Titulo).ToList(),
                    new OpcionMensaje("Entendido", "Cierra este aviso", Principal: true));
                return;
            }
            _fichas.Remove(sustituida);
        }
        var ficha = new FichaActivoVM(this, idActivo, ++_orden) { Apartado = apartado };
        _fichas.Add(ficha);
        anfitrion.Abrir(ficha, sustituida);
        if (sustituida is not null) contexto.Avisar($"He cerrado la ficha de {sustituida.Titulo}, la abierta hace más tiempo sin cambios, para no pasar de {MaximoFichas} fichas.");
        _ = ficha.CargarAsync();
    }

    internal void FichaCerrada(FichaActivoVM ficha) => _fichas.Remove(ficha);

    /// <summary>Lo llama una ficha al guardar: el listado vuelve a leer sus cifras.</summary>
    internal void Cambiado()
    {
        if (_listado is not null) _ = _listado.CargarAsync();
        _cartera?.Cambiado();
    }

    /// <summary>«Nuevo activo…»: el alta paso a paso; al crear, se generan sus subcuentas con propuesta y se abre la ficha.</summary>
    public async Task NuevoAsync()
    {
        if (!await contexto.Servicio.PuedeAsync()) { SinPermiso(); return; }
        var alta = new AltaActivoVM(contexto, dialogos);
        await alta.CargarAsync();
        if (!dialogos.Alta(alta) || alta.IdCreado is not int id) return;
        contexto.Avisar($"Activo {alta.Codigo} creado.");
        if (!alta.EsAjeno) await GenerarSubcuentasAsync([id], $"Subcuentas de {alta.Codigo}", ofrecerSiNoHay: false);
        Cambiado();
        AbrirFicha(id);
    }

    /// <summary>
    /// Apertura directa por centro de coste (desde el Impuesto sobre Sociedades): con un activo, su ficha en Cuenta de resultados; con varios, el
    /// listado filtrado; con ninguno, un aviso que lo explica y ofrece abrir el listado.
    /// </summary>
    public async Task AbrirDeCentroAsync(string textoCentro)
    {
        if (!await contexto.Servicio.PuedeAsync()) { SinPermiso(); return; }
        ActivosDeCentro r;
        try { r = await contexto.Servicio.ActivosDeCentroAsync(contexto.Trabajo, textoCentro); }
        catch (Exception ex) { dialogos.Error("No he podido buscar el activo", $"{ex.Message} Vuelve a intentarlo o abre Datos maestros › Activos."); return; }
        switch (r.Activos.Count)
        {
            case 1:
                AbrirFicha(r.Activos[0].IdActivo, ApartadoFicha.CuentaResultados);
                break;
            case > 1:
                AbrirListado(r.IdCentroCoste, r.TextoCentro);
                contexto.Avisar($"El centro «{r.TextoCentro}» tiene {r.Activos.Count} activos: elige uno en el listado.");
                break;
            default:
                var motivo = r.IdCentroCoste is null
                    ? $"No encuentro el centro de coste «{textoCentro}» en esta sociedad."
                    : $"Ningún activo en uso tiene el centro de coste «{r.TextoCentro}» (puede que sea un centro de gastos comunes o que su activo esté de baja).";
                if (dialogos.Preguntar("Sin activo para este centro", motivo + " Para verlo con su cuenta de explotación, asígnale el centro en la ficha del activo.", null,
                        new OpcionMensaje("Abrir el listado de activos", "Abre Datos maestros › Activos para buscarlo", Principal: true),
                        new OpcionMensaje("Cerrar", "No abre nada", Cancelar: true)) == 0)
                    AbrirListado();
                break;
        }
    }

    /// <summary>
    /// «Generar las que falten…» para unos activos: enseña la propuesta (crear, dar de alta en el ejercicio o renombrar) y, si se confirma, la aplica.
    /// Devuelve true si ha cambiado algo.
    /// </summary>
    internal async Task<bool> GenerarSubcuentasAsync(IReadOnlyCollection<int> ids, string titulo, bool ofrecerSiNoHay = true)
    {
        IReadOnlyList<OperacionSubcuenta> propuesta;
        try { propuesta = await contexto.Servicio.ProponerSubcuentasAsync(contexto.Trabajo, ids); }
        catch (Exception ex) { dialogos.Error("No he podido preparar la propuesta de subcuentas", $"{ex.Message} Pulsa Actualizar (F5) y vuelve a intentarlo."); return false; }
        if (propuesta.Count == 0)
        {
            if (ofrecerSiNoHay)
                dialogos.Preguntar(titulo, "No falta ninguna subcuenta: todas las líneas de su plantilla ya tienen la suya en el ejercicio de trabajo, con el nombre que toca.", null,
                    new OpcionMensaje("Entendido", "Cierra este aviso", Principal: true));
            return false;
        }
        var muyDistintos = propuesta.Count(o => o.MuyDistinto);
        var mensaje = $"Se van a hacer {propuesta.Count} cambios en las subcuentas del ejercicio {contexto.Trabajo.Ejercicio}, según la plantilla de su tipo de activo." +
                      (muyDistintos > 0 ? $" {muyDistintos} renombres cambian mucho el nombre (marcados con ⚠): revísalos antes de confirmar." : "");
        if (dialogos.Preguntar(titulo, mensaje, propuesta.Select(o => $"{o.Activo.Nombre}: {o.Texto}").ToList(),
                new OpcionMensaje("Generar", "Crea, da de alta o renombra estas subcuentas", Principal: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0)
            return false;
        try
        {
            var r = await contexto.Servicio.GenerarSubcuentasAsync(contexto.Trabajo, ids);
            if (!r.EsCorrecto) { dialogos.Error("No se han generado las subcuentas", string.Join(Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return false; }
            var g = r.Valor;
            contexto.Avisar($"Subcuentas: {g.Creadas} creadas, {g.DadasDeAlta} dadas de alta en el ejercicio y {g.Renombradas} renombradas.");
            if (g.Errores.Count > 0)
                dialogos.Error("Algunas subcuentas no se han podido generar", "El resto sí se ha hecho. Corrige lo que se indica y vuelve a pulsar «Generar las que falten…»." +
                    Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, g.Errores));
            return true;
        }
        catch (Exception ex)
        {
            dialogos.Error("No se han generado las subcuentas", $"{ex.Message} Lo hecho hasta el fallo se conserva; pulsa «Generar las que falten…» otra vez para terminar.");
            return true;
        }
    }

    /// <summary>
    /// «Nueva compra…» de un activo financiero (activos/activos-financieros 2026-11-23 20:32): fecha y participaciones, con el precio del día; al guardar
    /// se refrescan el listado y su ficha si está abierta.
    /// </summary>
    internal Task NuevaCompraAsync(int idActivo, string codigo) => OperacionAsync(idActivo, codigo, esVenta: false);

    /// <summary>«Vender…» de un activo financiero (2026-11-24 09:14): participaciones, fecha e importe total opcional, con el precio del día.</summary>
    internal Task VenderAsync(int idActivo, string codigo) => OperacionAsync(idActivo, codigo, esVenta: true);

    private async Task OperacionAsync(int idActivo, string codigo, bool esVenta)
    {
        if (!await contexto.Servicio.PuedeAsync()) { SinPermiso(); return; }
        Financieros.InversionDeActivo? inversion;
        try { inversion = await contexto.Servicio.InversionAsync(idActivo); }
        catch (Exception ex) { dialogos.Error("No he podido leer el activo", $"{ex.Message} Pulsa Actualizar (F5) y vuelve a intentarlo."); return; }
        if (inversion is null)
        {
            dialogos.Error("No es un activo financiero", $"El activo {codigo} no tiene ISIN, así que no se le pueden anotar {(esVenta ? "ventas" : "compras")} de participaciones. " +
                (esVenta ? "La venta de los demás activos aún no está disponible." : "Las compras de los demás activos entran por su factura de compra."));
            return;
        }
        if (esVenta && inversion.Participaciones <= 0m)
        {
            dialogos.Error("No le quedan participaciones", $"El activo {codigo} no tiene participaciones que vender.");
            return;
        }
        var vm = new NuevaCompraVM(contexto, idActivo, codigo, inversion, esVenta);
        _ = vm.CargarAsync();
        if (!dialogos.NuevaCompra(vm)) return;
        contexto.Avisar(esVenta ? $"Venta anotada en {codigo}." : $"Compra anotada en {codigo}.");
        Cambiado();
        if (_fichas.FirstOrDefault(f => f.IdActivo == idActivo) is { } ficha) _ = ficha.RecargarTrasCompraAsync();
    }

    internal void SinPermiso() => dialogos.Error("Sin permiso para los activos",
        "No tienes permiso para ver ni modificar los activos: la acción «Activos» tiene grupos asignados (o, sin grupos, la de editar Parámetros y tablas) y el tuyo no está entre ellos. " +
        "Pídeselo a quien administra los permisos.");
}
