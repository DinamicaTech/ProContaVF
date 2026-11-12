using System.Globalization;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Mantenimiento;

/// <summary>
/// Ámbito de los datos de un proveedor (plataforma/mantenimiento 2026-10-31 16:54): lo declara cada proveedor y el
/// servicio lo aplica con la empresa y el ejercicio de trabajo que fija el contenedor.
/// </summary>
public enum AmbitoMantenimiento
{
    /// <summary>Configuración local de este puesto.</summary>
    Equipo,
    /// <summary>En la base de datos, común a todas las empresas.</summary>
    Compartido,
    /// <summary>Cada registro es de una empresa: se ven los de la empresa de trabajo.</summary>
    Sociedad,
    /// <summary>Cada registro es de un ejercicio: se ven los del ejercicio de trabajo.</summary>
    Ejercicio
}

/// <summary>Un registro único (parámetros) o una colección 1:N (lista y editor).</summary>
public enum Cardinalidad { Unico, Lista }

/// <summary>Editor que usa la pantalla para un campo.</summary>
public enum TipoCampo
{
    Texto,
    TextoLargo,
    /// <summary>Texto corto en letra monoespaciada (códigos, prefijos, NIF).</summary>
    Codigo,
    Entero,
    Decimal,
    Porcentaje,
    Importe,
    Logico,
    /// <summary>Lista cerrada de <see cref="CampoMantenimiento.Opciones"/>.</summary>
    Lista,
    /// <summary>
    /// Lista abierta: se elige una de las <see cref="CampoMantenimiento.Opciones"/> o se escribe un texto nuevo. El valor es el de la
    /// opción cuyo texto coincide con lo escrito (sin distinguir mayúsculas) o, si ninguna coincide, el texto escrito, y el
    /// proveedor decide qué hacer con él (plataforma/mantenimiento 2026-11-12 11:45).
    /// </summary>
    ListaAbierta,
    /// <summary>Código de cuenta contable (se comprueba en el ejercicio de trabajo si el proveedor lo pide).</summary>
    Cuenta,
    Ruta,
    Email,
    Fecha,
    /// <summary>
    /// Clave o contraseña: se escribe sin verse y nunca se muestra lo guardado; vacío, se conserva lo que hubiera.
    /// </summary>
    Secreto,
    /// <summary>
    /// Clave de un registro de otro mantenimiento 1:N (<see cref="CampoMantenimiento.Referencia"/>): no se escribe, se elige
    /// en el selector de esa tabla, desde el que también se da de alta (plataforma/mantenimiento 2026-11-12).
    /// </summary>
    Referencia
}

public sealed record OpcionCampo(object? Valor, string Texto);

public sealed record GrupoCampos(string Clave, string Titulo);

/// <summary>Metadatos de un campo: cómo se edita, se valida y dónde se coloca.</summary>
public sealed record CampoMantenimiento(string Clave, string Etiqueta, TipoCampo Tipo = TipoCampo.Texto)
{
    public string? Grupo { get; init; }
    public string? Ayuda { get; init; }
    public bool Obligatorio { get; init; }
    public int? Longitud { get; init; }
    public bool SoloLectura { get; init; }
    /// <summary>Solo se escribe al crear el registro (claves escritas por el operador).</summary>
    public bool SoloAlta { get; init; }
    /// <summary>Significado aún por confirmar: la pantalla lo marca para que no se tome como definitivo.</summary>
    public bool Pendiente { get; init; }
    public decimal? Minimo { get; init; }
    public decimal? Maximo { get; init; }
    public IReadOnlyList<OpcionCampo>? Opciones { get; init; }
    /// <summary>Ancho orientativo del editor: xs, s, m, l o f (toda la fila).</summary>
    public string Ancho { get; init; } = "m";
    /// <summary>Otras palabras con las que se encuentra en la búsqueda (Ctrl+K).</summary>
    public IReadOnlyList<string> Sinonimos { get; init; } = [];
    /// <summary>
    /// Solo en <see cref="TipoCampo.Cuenta"/>: grupos o prefijos (472, 57, 6…) a los que se limita el buscador de cuentas al
    /// abrirlo; el operador puede quitar el filtro. Vacío: todo el plan.
    /// </summary>
    public IReadOnlyList<string> GruposCuenta { get; init; } = [];
    /// <summary>Solo en <see cref="TipoCampo.Cuenta"/>: false si el campo admite cuentas de cualquier nivel (por ejemplo, un prefijo).</summary>
    public bool SoloSubcuentas { get; init; } = true;
    /// <summary>Solo en <see cref="TipoCampo.Referencia"/>: clave del mantenimiento 1:N al que apunta (por ejemplo «formaspago»).</summary>
    public string? Referencia { get; init; }
}

/// <summary>Columna de la lista de registros.</summary>
public sealed record ColumnaLista(string Clave, string Titulo, double Ancho = 0, bool Derecha = false);

/// <summary>
/// Acción propia de un proveedor sobre el registro activo (Duplicar, Ver uso…). Con <paramref name="ConPropuesta"/>, la pantalla
/// pide antes la propuesta (<see cref="IProveedorMantenimiento.ProponerAsync"/>), la muestra y solo ejecuta si el operador confirma.
/// </summary>
public sealed record AccionMantenimiento(string Clave, string Texto, string Ayuda, bool Modifica = true, bool ConPropuesta = false);

/// <summary>Resultado de una acción: un mensaje, el registro que hay que abrir (por ejemplo la copia) y un detalle opcional.</summary>
public sealed record ResultadoAccion(string Mensaje, object? ClaveAbrir = null, IReadOnlyList<string>? Detalle = null)
{
    /// <summary>En una propuesta: no hay nada que hacer, así que la pantalla solo informa y no pide confirmación.</summary>
    public bool NadaQueHacer { get; init; }
}

public sealed record ErrorCampo(string? Campo, string Mensaje);

/// <summary>Contexto de trabajo que fija el contenedor.</summary>
public sealed record ContextoMantenimiento(ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor);

/// <summary>
/// Buscador de cuentas para los campos <see cref="TipoCampo.Cuenta"/> (plataforma/mantenimiento 2026-11-12): lo pone el
/// contenedor con el plan de cuentas del ejercicio de trabajo. Con él, la cuenta solo se elige en el buscador; sin él, se escribe.
/// </summary>
public interface ISelectorCuentas
{
    /// <summary>Abre el buscador (con «Crear subcuenta») y devuelve el código elegido o creado; null si se cancela.</summary>
    Task<string?> ElegirAsync(CampoMantenimiento campo, string? texto, ContextoMantenimiento contexto);
}

/// <summary>
/// Registro que edita la pantalla: valores por clave de campo, más lo que el proveedor bloquea y por qué (por ejemplo,
/// el prefijo de una serie con facturas).
/// </summary>
public sealed class RegistroMantenimiento
{
    public RegistroMantenimiento(object? clave, IDictionary<string, object?>? valores = null)
    {
        Clave = clave;
        if (valores is not null) foreach (var (k, v) in valores) Valores[k] = v;
    }

    /// <summary>Clave del registro en su tabla; null en uno nuevo sin guardar.</summary>
    public object? Clave { get; set; }
    public bool EsNuevo => Clave is null;
    public Dictionary<string, object?> Valores { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Campos que no se pueden cambiar en este registro, con el motivo.</summary>
    public Dictionary<string, string> Bloqueos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Si no es null, el registro entero es de solo consulta por este motivo.</summary>
    public string? MotivoSoloLectura { get; set; }

    public object? this[string campo]
    {
        get => Valores.TryGetValue(campo, out var v) ? v : null;
        set => Valores[campo] = value;
    }

    public string? Texto(string campo) => this[campo] switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var o => o.ToString()
    };

    public decimal? Decimal(string campo) => this[campo] switch
    {
        null => null,
        decimal d => d,
        double d => Math.Round((decimal)d, 4),
        float f => Math.Round((decimal)f, 4),
        IConvertible c when c is not string => Convert.ToDecimal(c, CultureInfo.InvariantCulture),
        string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out var d) => d,
        _ => null
    };

    public int? Entero(string campo) => Decimal(campo) is decimal d ? (int)d : null;

    public bool Logico(string campo) => this[campo] switch
    {
        bool b => b,
        null => false,
        var o => Convert.ToBoolean(o, CultureInfo.InvariantCulture)
    };

    public RegistroMantenimiento Copia()
    {
        var c = new RegistroMantenimiento(Clave, Valores) { MotivoSoloLectura = MotivoSoloLectura };
        foreach (var (k, v) in Bloqueos) c.Bloqueos[k] = v;
        return c;
    }
}

/// <summary>
/// Proveedor de un mantenimiento (RF-MAN-01): lo publica el área dueña de los datos y la pantalla genérica lo muestra.
/// El proveedor nunca cede la propiedad de su tabla: las reglas con invariantes van en su servicio tipado.
/// </summary>
public interface IProveedorMantenimiento
{
    string Clave { get; }
    string Titulo { get; }
    /// <summary>Categoría de la navegación: General, Empresas y ejercicios, Facturación, Integraciones o Catálogos.</summary>
    string Categoria { get; }
    string Finalidad { get; }
    AmbitoMantenimiento Ambito { get; }
    Cardinalidad Cardinalidad { get; }
    bool PermiteAlta { get; }
    bool PermiteBaja { get; }
    /// <summary>Si no es null, el mantenimiento es de solo consulta para todos por este motivo.</summary>
    string? MotivoSoloConsulta { get; }
    /// <summary>Nombre corto del registro («serie», «concepto»).</summary>
    string NombreRegistro { get; }
    IReadOnlyList<string> Sinonimos { get; }
    IReadOnlyList<GrupoCampos> Grupos { get; }
    IReadOnlyList<CampoMantenimiento> Campos { get; }
    IReadOnlyList<ColumnaLista> Columnas { get; }
    IReadOnlyList<AccionMantenimiento> Acciones { get; }

    Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto);
    Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto);
    /// <summary>Errores por campo sin BD: obligatorios, longitudes, rangos y reglas propias.</summary>
    IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento registro);
    Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto);
    Task<Resultado> EliminarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto);
    Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento contexto);

    /// <summary>Qué hará una acción con propuesta, sin cambiar nada: el mensaje y el detalle que confirma el operador.</summary>
    Task<Resultado<ResultadoAccion>> ProponerAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
        Task.FromResult(Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"¿Ejecuto «{Acciones.FirstOrDefault(a => a.Clave == accion)?.Texto ?? accion}»?")));

    string TituloRegistro(RegistroMantenimiento registro);
    string? SubtituloRegistro(RegistroMantenimiento registro);
}

/// <summary>Valores por defecto y validación genérica (obligatorio, longitud, número, rango, email).</summary>
public abstract class ProveedorBase : IProveedorMantenimiento
{
    public abstract string Clave { get; }
    public abstract string Titulo { get; }
    public abstract string Categoria { get; }
    public abstract string Finalidad { get; }
    public abstract AmbitoMantenimiento Ambito { get; }
    public virtual Cardinalidad Cardinalidad => Cardinalidad.Lista;
    public virtual bool PermiteAlta => MotivoSoloConsulta is null;
    public virtual bool PermiteBaja => MotivoSoloConsulta is null;
    public virtual string? MotivoSoloConsulta => null;
    public abstract string NombreRegistro { get; }
    public virtual IReadOnlyList<string> Sinonimos => [];
    public virtual IReadOnlyList<GrupoCampos> Grupos => [];
    public abstract IReadOnlyList<CampoMantenimiento> Campos { get; }
    public abstract IReadOnlyList<ColumnaLista> Columnas { get; }
    public virtual IReadOnlyList<AccionMantenimiento> Acciones => [];

    public abstract Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto);

    public virtual Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto) =>
        Task.FromResult(new RegistroMantenimiento(null, Campos.ToDictionary(c => c.Clave, c => c.Tipo == TipoCampo.Logico ? (object?)false : null)));

    public virtual IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r) => ValidacionGenerica.Validar(Campos, r);

    public abstract Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto);

    public virtual Task<Resultado> EliminarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
        Task.FromResult(Resultado.Fallo(new Error("Mantenimiento.SinBaja", $"Este mantenimiento no permite borrar un {NombreRegistro}.")));

    public virtual Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
        Task.FromResult(Resultado<ResultadoAccion>.Fallo(new Error("Mantenimiento.AccionDesconocida", $"La acción «{accion}» no existe en {Titulo}.")));

    public virtual Task<Resultado<ResultadoAccion>> ProponerAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
        Task.FromResult(Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"¿Ejecuto «{Acciones.FirstOrDefault(a => a.Clave == accion)?.Texto ?? accion}»?")));

    public virtual string TituloRegistro(RegistroMantenimiento r) =>
        r.EsNuevo ? $"Nuevo {NombreRegistro}" : r.Texto(Columnas.FirstOrDefault()?.Clave ?? "") ?? $"{NombreRegistro} {r.Clave}";

    public virtual string? SubtituloRegistro(RegistroMantenimiento r) => null;
}

public static class ValidacionGenerica
{
    public static IReadOnlyList<ErrorCampo> Validar(IEnumerable<CampoMantenimiento> campos, RegistroMantenimiento r)
    {
        var errores = new List<ErrorCampo>();
        foreach (var c in campos.Where(c => !c.SoloLectura))
        {
            var valor = r[c.Clave];
            var texto = valor as string;
            var vacio = valor is null || (texto is not null && string.IsNullOrWhiteSpace(texto));
            if (c.Obligatorio && vacio && c.Tipo != TipoCampo.Logico)
            {
                errores.Add(new(c.Clave, $"Falta {Minuscula(c.Etiqueta)}."));
                continue;
            }
            if (vacio) continue;
            if (texto is not null && c.Longitud is int l && texto.Trim().Length > l)
                errores.Add(new(c.Clave, $"{c.Etiqueta}: admite hasta {l} caracteres y tiene {texto.Trim().Length}."));
            if (c.Tipo is TipoCampo.Entero or TipoCampo.Decimal or TipoCampo.Porcentaje or TipoCampo.Importe)
            {
                var n = r.Decimal(c.Clave);
                if (n is null) errores.Add(new(c.Clave, $"{c.Etiqueta}: escribe un número."));
                else if (c.Tipo == TipoCampo.Entero && n != Math.Truncate(n.Value)) errores.Add(new(c.Clave, $"{c.Etiqueta}: debe ser un número entero."));
                else if (c.Minimo is decimal min && n < min) errores.Add(new(c.Clave, $"{c.Etiqueta}: debe ser {min.ToString(CultureInfo.CurrentCulture)} o más."));
                else if (c.Maximo is decimal max && n > max) errores.Add(new(c.Clave, $"{c.Etiqueta}: debe ser {max.ToString(CultureInfo.CurrentCulture)} o menos."));
                else if (c.Tipo == TipoCampo.Porcentaje && (n < 0m || n > 100m)) errores.Add(new(c.Clave, $"{c.Etiqueta}: debe estar entre 0 y 100."));
            }
            if (c.Tipo == TipoCampo.Email && texto is not null && !EsEmail(texto))
                errores.Add(new(c.Clave, $"{c.Etiqueta}: no parece una dirección de correo (falta la @ o el dominio)."));
            if (c.Tipo == TipoCampo.Cuenta && texto is not null && !texto.Trim().All(char.IsAsciiDigit))
                errores.Add(new(c.Clave, $"{c.Etiqueta}: un código de cuenta solo lleva cifras."));
        }
        return errores;
    }

    private static bool EsEmail(string t)
    {
        var s = t.Trim();
        var arroba = s.IndexOf('@');
        return arroba > 0 && arroba == s.LastIndexOf('@') && s.IndexOf('.', arroba) > arroba + 1 && !s.EndsWith('.') && !s.Contains(' ');
    }

    private static string Minuscula(string s) => s.Length == 0 ? s : char.ToLower(s[0], CultureInfo.CurrentCulture) + s[1..];
}
