using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Seguridad;

namespace ProConta.Correo;

/// <summary>Lectura de las cuentas de correo (CuentasCorreo), con la contraseña descifrada solo en memoria.</summary>
public sealed class RepositorioCuentasCorreo(IBaseDatos bd)
{
    internal const string Columnas = "IdCuentaCorreo, IdSociedadEjercicio, Activa, Direccion, NombreVisible, Usuario, SecretoProtegido, ResponderA, ServidorSmtp, PuertoSmtp, " +
        "SeguridadSmtp, ServidorPop3, PuertoPop3, SeguridadPop3, EliminarTrasProcesar, IntervaloRecepcionMinutos, TiempoEsperaSegundos";

    /// <summary>La cuenta del ejercicio (activa o no); null si no tiene.</summary>
    public Task<CuentaCorreo?> ObtenerPorEjercicioAsync(int idSociedadEjercicio) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM CuentasCorreo WHERE IdSociedadEjercicio = ?", Mapear, idSociedadEjercicio));

    public Task<CuentaCorreo?> ObtenerAsync(int idCuentaCorreo) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM CuentasCorreo WHERE IdCuentaCorreo = ?", Mapear, idCuentaCorreo));

    internal static CuentaCorreo Mapear(IFilaDatos f) => new()
    {
        IdCuentaCorreo = f.Entero("IdCuentaCorreo"),
        IdSociedadEjercicio = f.Entero("IdSociedadEjercicio"),
        Activa = f.Logico("Activa"),
        Direccion = f.Texto("Direccion") ?? "",
        NombreVisible = f.Texto("NombreVisible"),
        Usuario = f.Texto("Usuario"),
        Contrasena = SecretoProtegido.Desproteger(f.Texto("SecretoProtegido")),
        ResponderA = f.Texto("ResponderA"),
        ServidorSmtp = f.Texto("ServidorSmtp"),
        PuertoSmtp = f.Entero("PuertoSmtp"),
        SeguridadSmtp = Seguridad(f.Texto("SeguridadSmtp")),
        ServidorPop3 = f.Texto("ServidorPop3"),
        PuertoPop3 = f.Entero("PuertoPop3"),
        SeguridadPop3 = Seguridad(f.Texto("SeguridadPop3")),
        EliminarTrasProcesar = f.Logico("EliminarTrasProcesar"),
        IntervaloRecepcionMinutos = f.Entero("IntervaloRecepcionMinutos"),
        TiempoEsperaSegundos = f.Entero("TiempoEsperaSegundos"),
    };

    internal static SeguridadConexion Seguridad(string? codigo) =>
        string.Equals(codigo, "TLS", StringComparison.OrdinalIgnoreCase) ? SeguridadConexion.TlsImplicito : SeguridadConexion.StartTls;

    internal static string Codigo(SeguridadConexion s) => s == SeguridadConexion.TlsImplicito ? "TLS" : "STARTTLS";
}
