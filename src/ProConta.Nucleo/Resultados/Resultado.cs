namespace ProConta.Nucleo.Resultados;

/// <summary>Resultado de una operación que puede fallar de forma recuperable, sin valor.</summary>
public class Resultado
{
    private static readonly Resultado _correcto = new(Array.Empty<Error>());

    protected Resultado(IReadOnlyList<Error> errores) => Errores = errores;

    /// <summary>Errores de la operación; vacío si ha ido bien.</summary>
    public IReadOnlyList<Error> Errores { get; }

    public bool EsCorrecto => Errores.Count == 0;

    /// <summary>Primer error, o null si la operación ha ido bien.</summary>
    public Error? Error => Errores.Count > 0 ? Errores[0] : null;

    public static Resultado Correcto() => _correcto;

    public static Resultado Fallo(Error error) => new(new[] { error });

    public static Resultado Fallo(IEnumerable<Error> errores)
    {
        var lista = errores.ToArray();
        if (lista.Length == 0) throw new ArgumentException("Un fallo necesita al menos un error.", nameof(errores));
        return new Resultado(lista);
    }

    /// <summary>Combina varios resultados: correcto si todos lo son; si no, reúne todos sus errores.</summary>
    public static Resultado Combinar(params Resultado[] resultados)
    {
        var errores = resultados.SelectMany(r => r.Errores).ToArray();
        return errores.Length == 0 ? _correcto : new Resultado(errores);
    }
}

/// <summary>Resultado de una operación que devuelve un valor o errores recuperables.</summary>
public sealed class Resultado<T> : Resultado
{
    private readonly T? _valor;

    private Resultado(T? valor, IReadOnlyList<Error> errores) : base(errores) => _valor = valor;

    /// <summary>Valor de la operación. Lanza si la operación ha fallado.</summary>
    public T Valor => EsCorrecto
        ? _valor!
        : throw new InvalidOperationException($"No hay valor: la operación falló. {Error}");

    public static Resultado<T> Correcto(T valor) => new(valor, Array.Empty<Error>());

    public static new Resultado<T> Fallo(Error error) => new(default, new[] { error });

    public static new Resultado<T> Fallo(IEnumerable<Error> errores)
    {
        var lista = errores.ToArray();
        if (lista.Length == 0) throw new ArgumentException("Un fallo necesita al menos un error.", nameof(errores));
        return new Resultado<T>(default, lista);
    }

    /// <summary>Transforma el valor si la operación ha ido bien; si no, propaga los errores.</summary>
    public Resultado<TOtro> Map<TOtro>(Func<T, TOtro> transformar) =>
        EsCorrecto ? Resultado<TOtro>.Correcto(transformar(_valor!)) : Resultado<TOtro>.Fallo(Errores);
}
