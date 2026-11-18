namespace ProConta.App;

/// <summary>Lo que la ventana principal aporta al registro de errores (plataforma/registro-errores): sociedad, ejercicio y pantalla activa.</summary>
public partial class VentanaPrincipal
{
    /// <summary>«01 · RAZÓN SOCIAL · ejercicio 2026».</summary>
    internal string SociedadParaRegistro => $"{_ejercicioTrabajo.IdSociedad} · {_razonSocial} · ejercicio {_ejercicioTrabajo.Ejercicio}";

    /// <summary>Título de la pestaña activa y cuántas hay abiertas, o null si todavía no hay ninguna.</summary>
    internal string? PantallaParaRegistro =>
        _area?.Activa is { } activa ? $"{activa.Titulo} ({_area.Lista.Count} {(_area.Lista.Count == 1 ? "pestaña abierta" : "pestañas abiertas")})" : null;

    private PestanaRegistroErrores? _registroErrores;

    /// <summary>Configuración › «Registro de errores»: abre la pestaña o, si ya está abierta, la activa y vuelve a leer los ficheros.</summary>
    private void AbrirRegistroErrores()
    {
        if (_registroErrores is not null && _area.Pestanas.Contains(_registroErrores))
        {
            _area.Activar(_registroErrores);
            _registroErrores.Cargar();
            return;
        }
        _registroErrores = new PestanaRegistroErrores(ErroresInesperados.Registro, Avisar);
        _area.Abrir(_registroErrores);
    }
}
