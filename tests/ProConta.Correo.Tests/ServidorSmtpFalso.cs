using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ProConta.Correo.Tests;

/// <summary>Qué hace el servidor falso al recibir el contenido (DATA).</summary>
public enum AlRecibirContenido { Aceptar, RechazarTemporal, RechazarDefinitivo, CortarConexion }

/// <summary>
/// Servidor SMTP mínimo en local para probar el transporte sin red: STARTTLS con un certificado de prueba, AUTH PLAIN/LOGIN,
/// rechazo de los destinatarios que contienen «rechazo» y el comportamiento elegido tras DATA. Guarda lo recibido.
/// </summary>
public sealed class ServidorSmtpFalso : IDisposable
{
    private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificado = CrearCertificado();
    private readonly CancellationTokenSource _fin = new();

    public ServidorSmtpFalso()
    {
        _escucha.Start();
        _ = Task.Run(AtenderAsync);
    }

    public int Puerto => ((IPEndPoint)_escucha.LocalEndpoint).Port;
    public AlRecibirContenido Comportamiento { get; set; } = AlRecibirContenido.Aceptar;
    public string ContrasenaValida { get; set; } = "clave-de-prueba";
    public bool OfrecerStartTls { get; set; } = true;
    public List<string> Recibidos { get; } = [];
    public List<string> RcptRecibidos { get; } = [];
    public int Conexiones { get; private set; }

    private async Task AtenderAsync()
    {
        while (!_fin.IsCancellationRequested)
        {
            TcpClient cliente;
            try { cliente = await _escucha.AcceptTcpClientAsync(_fin.Token); } catch { return; }
            Conexiones++;
            _ = Task.Run(() => SesionAsync(cliente));
        }
    }

    private async Task SesionAsync(TcpClient tcp)
    {
        using var _ = tcp;
        Stream flujo = tcp.GetStream();
        var lector = new StreamReader(flujo, Encoding.ASCII);
        var escritor = new StreamWriter(flujo, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        var cifrado = false;
        try
        {
            await escritor.WriteLineAsync("220 falso ESMTP");
            while (await lector.ReadLineAsync() is { } linea)
            {
                var orden = linea.ToUpperInvariant();
                if (orden.StartsWith("EHLO"))
                {
                    await escritor.WriteLineAsync("250-falso");
                    if (OfrecerStartTls && !cifrado) await escritor.WriteLineAsync("250-STARTTLS");
                    await escritor.WriteLineAsync("250 AUTH PLAIN LOGIN");
                }
                else if (orden == "STARTTLS")
                {
                    await escritor.WriteLineAsync("220 listo");
                    var ssl = new SslStream(flujo, false);
                    await ssl.AuthenticateAsServerAsync(_certificado);
                    flujo = ssl;
                    lector = new StreamReader(flujo, Encoding.ASCII);
                    escritor = new StreamWriter(flujo, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                    cifrado = true;
                }
                else if (orden.StartsWith("AUTH PLAIN"))
                {
                    var datos = linea.Length > 11 ? linea[11..] : await PedirAsync(lector, escritor, "334 ");
                    var partes = Encoding.UTF8.GetString(Convert.FromBase64String(datos)).Split('\0');
                    await escritor.WriteLineAsync(partes.Length == 3 && partes[2] == ContrasenaValida ? "235 ok" : "535 credenciales no validas");
                }
                else if (orden.StartsWith("AUTH LOGIN"))
                {
                    await PedirAsync(lector, escritor, "334 VXNlcm5hbWU6");
                    var clave = Encoding.UTF8.GetString(Convert.FromBase64String(await PedirAsync(lector, escritor, "334 UGFzc3dvcmQ6")));
                    await escritor.WriteLineAsync(clave == ContrasenaValida ? "235 ok" : "535 credenciales no validas");
                }
                else if (orden.StartsWith("MAIL FROM")) await escritor.WriteLineAsync("250 ok");
                else if (orden.StartsWith("RCPT TO"))
                {
                    lock (RcptRecibidos) RcptRecibidos.Add(linea);
                    await escritor.WriteLineAsync(orden.Contains("RECHAZO") ? "550 5.1.1 buzon inexistente" : "250 ok");
                }
                else if (orden == "DATA")
                {
                    await escritor.WriteLineAsync("354 adelante");
                    var contenido = new StringBuilder();
                    while (await lector.ReadLineAsync() is { } l && l != ".") contenido.AppendLine(l);
                    lock (Recibidos) Recibidos.Add(contenido.ToString());
                    switch (Comportamiento)
                    {
                        case AlRecibirContenido.Aceptar: await escritor.WriteLineAsync("250 2.0.0 en cola"); break;
                        case AlRecibirContenido.RechazarTemporal: await escritor.WriteLineAsync("451 4.3.0 intentalo luego"); break;
                        case AlRecibirContenido.RechazarDefinitivo: await escritor.WriteLineAsync("554 5.7.1 rechazado"); break;
                        default: return; // corta sin responder
                    }
                }
                else if (orden == "RSET" || orden == "NOOP") await escritor.WriteLineAsync("250 ok");
                else if (orden == "QUIT") { await escritor.WriteLineAsync("221 adios"); return; }
                else await escritor.WriteLineAsync("502 no implementado");
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task<string> PedirAsync(StreamReader lector, StreamWriter escritor, string invitacion)
    {
        await escritor.WriteLineAsync(invitacion);
        return await lector.ReadLineAsync() ?? "";
    }

    private static X509Certificate2 CrearCertificado()
    {
        using var rsa = RSA.Create(2048);
        var peticion = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = peticion.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    public void Dispose()
    {
        _fin.Cancel();
        _escucha.Stop();
        _certificado.Dispose();
    }
}
