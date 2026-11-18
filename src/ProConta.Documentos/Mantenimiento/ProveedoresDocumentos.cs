using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;

namespace ProConta.Documentos.Mantenimiento;

/// <summary>
/// Tipos de documento en Parámetros y tablas › Catálogos (propietario 2026-11-18 18:06): los que trae el programa más los que añada el operador
/// (DNI, poderes…), con la subcarpeta donde se archivan los que se suben a mano.
/// </summary>
public static class ProveedoresDocumentos
{
    public static IReadOnlyList<IProveedorMantenimiento> Todos(IBaseDatos bd) => [TiposDocumento(bd)];

    public static ProveedorTablaAccess TiposDocumento(IBaseDatos bd) => new(bd, "tiposdocumento", "Tipos de documento", "Catálogos",
        "Tipos de documento del gestor documental: su nombre, la subcarpeta de «Documentos» donde se archivan los que se suben a mano y si van por año.",
        AmbitoMantenimiento.Compartido, "tipo de documento", "TiposDocumento", "Codigo", ClaveNueva.Escrita,
        [
            new("Codigo", "Código", TipoCampo.Codigo) { Obligatorio = true, SoloAlta = true, Longitud = 20, Ancho = "s",
                Ayuda = "Código corto en mayúsculas y sin espacios (por ejemplo DNI o PODER). Se fija al crear el tipo." },
            new("Nombre", "Nombre") { Obligatorio = true, Longitud = 60, Ancho = "m", Ayuda = "Lo que se ve en el desplegable de tipo de la pantalla Documentos." },
            new("Carpeta", "Subcarpeta") { Obligatorio = true, Longitud = 60, Ancho = "m",
                Ayuda = "Subcarpeta de «Documentos» (dentro de la carpeta de documentos de la empresa) donde se archivan los de este tipo, por ejemplo «Empresa» o «Personal»." },
            new("PorAnio", "Por año", TipoCampo.Logico) { Ayuda = "Marcado: se archivan en Documentos › año › subcarpeta (modelos, notificaciones). Sin marcar: en una subcarpeta común a todos los años (escrituras, DNI)." },
            new("Orden", "Orden", TipoCampo.Entero) { Obligatorio = true, Minimo = 0, Maximo = 999, Ancho = "xs", Ayuda = "Posición en el desplegable: los de número más bajo salen antes." },
            new("Manual", "Se puede elegir al subir", TipoCampo.Logico) { Ayuda = "Sin marcar, solo lo ponen los módulos del programa (facturas, adjuntos de correo…)." },
            new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Sin marcar no se ofrece para documentos nuevos; los que ya lo tienen lo conservan." },
        ],
        [new("Nombre", "Nombre"), new("Carpeta", "Subcarpeta", 140), new("PorAnio", "Por año", 70), new("Manual", "Al subir", 70), new("Activo", "Activo", 60), new("Orden", "Orden", 60, Derecha: true)])
    {
        Orden = "Orden, Nombre",
        Alias = ["documentos", "tipos de documento", "dni", "escritura", "gestor documental"],
        Usos = [new UsoRegistro("Documentos", "Tipo", "documentos")],
        Titular = r => r.EsNuevo ? "Nuevo tipo de documento" : r.Texto("Nombre") ?? "",
    };
}
