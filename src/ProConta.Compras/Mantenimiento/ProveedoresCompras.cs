using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Mantenimiento;

/// <summary>Mantenimientos de compras para la pantalla genérica (plataforma/mantenimiento).</summary>
public static class ProveedoresCompras
{
    public static IReadOnlyList<IProveedorMantenimiento> Todos(IBaseDatos bd) => [new ProveedorTextosClaveCompra(new CatalogoTextosClaveCompra(bd))];
}

/// <summary>
/// Textos clave de compras de la empresa de trabajo (TextosClaveCompra, compras/pantallas 2026-11-06 17:02): el texto que se
/// busca en el contenido y la ruta de los PDF y la cuenta de gasto y el centro de coste que asigna.
/// </summary>
public sealed class ProveedorTextosClaveCompra(CatalogoTextosClaveCompra catalogo) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _centros = [];

    public override string Clave => "textosclavecompra";
    public override string Titulo => "Textos clave de compras";
    public override string Categoria => "Catálogos";
    public override string Finalidad => "Textos que, si aparecen en el contenido o la ruta de un PDF de compra, le asignan una cuenta de gasto, un centro de coste o los dos.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override string NombreRegistro => "texto clave";
    public override IReadOnlyList<string> Sinonimos => ["texto clave", "palabra clave", "cuenta de gasto", "centro de coste", "departamento", "compras"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Texto", "Texto clave") { Obligatorio = true, Longitud = ReglasTextosClave.LongitudMaxima, Ancho = "l",
            Ayuda = "Se busca sin distinguir mayúsculas ni acentos. Si coinciden varios textos, gana el más largo; si empatan, no se asigna nada y la factura avisa." },
        new("Cuenta", "Cuenta de gasto", TipoCampo.Cuenta) { Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "gasto", "6"],
            Ayuda = "Subcuenta del ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código. Vacía, el texto no asigna cuenta." },
        new("NombreCuenta", "Nombre de la cuenta") { SoloLectura = true, Ancho = "l" },
        new("IdCentroCoste", "Centro de coste", TipoCampo.Lista) { Opciones = _centros, Ancho = "l", Ayuda = "Vacío, el texto no asigna centro de coste." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Texto", "Texto clave"), new("Cuenta", "Cuenta", 90), new("NombreCuenta", "Nombre de la cuenta"), new("Centro", "Centro de coste")];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo texto clave" : r.Texto("Texto") ?? "Texto clave sin texto";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        _centros = [new OpcionCampo(null, "Sin centro de coste"), .. (await catalogo.CentrosAsync(c.Trabajo.IdSociedad)).Select(x => new OpcionCampo(x.Id, x.Texto))];
        return (await catalogo.ListarAsync(c.Trabajo)).Select(AsRegistro).ToList();
    }

    private static RegistroMantenimiento AsRegistro(TextoClaveCompra x) => new(x.IdTextoClave, new Dictionary<string, object?>
    {
        ["Texto"] = x.Texto, ["Cuenta"] = x.Cuenta,
        ["NombreCuenta"] = x.Cuenta is null ? null : x.CuentaEnEjercicio ? x.NombreCuenta : $"{x.NombreCuenta} (no existe en este ejercicio)",
        ["IdCentroCoste"] = x.IdCentroCoste, ["Centro"] = x.NombreCentro,
    });

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        int? id = r.Clave is null ? null : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture);
        var g = await catalogo.GuardarAsync(c.Trabajo, id, r.Texto("Texto") ?? "", r.Texto("Cuenta"), r.Entero("IdCentroCoste"));
        if (!g.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(g.Errores);
        var guardado = (await catalogo.ListarAsync(c.Trabajo)).First(x => x.IdTextoClave == g.Valor);
        return Resultado<RegistroMantenimiento>.Correcto(AsRegistro(guardado));
    }

    public override Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c) =>
        catalogo.EliminarAsync(c.Trabajo, Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture));
}
