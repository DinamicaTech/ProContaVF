Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports ProConta.GestorCorreo.Aplicacion

Public NotInheritable Class ServicioRecepcionPop3
    Private ReadOnly _pop3 As IClientePop3
    Private ReadOnly _smtp As IClienteSmtp
    Private ReadOnly _credenciales As IAlmacenCredencialesCorreo
    Private ReadOnly _conversorMime As IConversorMimeDispatcher
    Private ReadOnly _registro As IRegistroEntregaPop3
    Private ReadOnly _dispatcher As ServicioMailDispatcher

    ''' <summary>Coordina los contratos neutrales de correo con el procesamiento del Dispatcher.</summary>
    ''' <param name="pop3">Cliente POP3 de Gestor Correo.</param><param name="smtp">Cliente SMTP de Gestor Correo.</param><param name="credenciales">Almacén seguro de Gestor Correo.</param><param name="conversorMime">Conversor del contenido MIME al modelo funcional.</param><param name="registro">Trazabilidad UIDL.</param><param name="dispatcher">Motor de mensajes.</param>
    ''' <remarks>No devuelve valor directo ni conecta durante la construcción.</remarks>
    Public Sub New(pop3 As IClientePop3, smtp As IClienteSmtp, credenciales As IAlmacenCredencialesCorreo, conversorMime As IConversorMimeDispatcher, registro As IRegistroEntregaPop3, dispatcher As ServicioMailDispatcher)
        _pop3 = pop3
        _smtp = smtp
        _credenciales = credenciales
        _conversorMime = conversorMime
        _registro = registro
        _dispatcher = dispatcher
    End Sub

    ''' <summary>Procesa todos los mensajes visibles de una cuenta sin bloquear los siguientes.</summary>
    ''' <param name="cuenta">Cuenta activa administrada por Gestor Correo.</param><param name="opciones">Límites y política propios del Dispatcher.</param><param name="cancelacion">Cancelación externa.</param>
    ''' <remarks>No devuelve valor directo; puede descargar, responder, trazar y eliminar mensajes confirmados.</remarks>
    Public Async Function EjecutarAsync(cuenta As CuentaCorreo, opciones As OpcionesRecepcionDispatcher, cancelacion As CancellationToken) As Task
        Validar(cuenta, opciones)
        Dim secretoPop3 = ObtenerCredencial(cuenta.ReferenciaCredencialPop3, "recepción POP3")
        Dim referenciaSmtp = If(cuenta.ComparteCredencialSmtp, cuenta.ReferenciaCredencialPop3, cuenta.ReferenciaCredencialSmtp)
        Dim secretoSmtp = ObtenerCredencial(referenciaSmtp, "envío SMTP")
        Using limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion)
            limite.CancelAfter(TimeSpan.FromSeconds(cuenta.TiempoEsperaSegundos))
            Dim mensajes = Await _pop3.ListarAsync(cuenta, secretoPop3, limite.Token)
            For Each remoto In mensajes
                cancelacion.ThrowIfCancellationRequested()
                If _registro.EstaCompletado(cuenta.IdCuentaCorreo, remoto.Uidl) Then Continue For
                Try
                    If remoto.TamanoBytes > opciones.TamanoMaximoMensajeBytes Then Throw New InvalidDataException($"El mensaje UIDL '{remoto.Uidl}' supera el límite de {opciones.TamanoMaximoMensajeBytes} bytes.")
                    Dim contenidoMime = Await _pop3.RecuperarMimeAsync(cuenta, secretoPop3, remoto, limite.Token)
                    Dim mensaje = _conversorMime.Convertir(contenidoMime)
                    ValidarLimites(mensaje, opciones)
                    _registro.RegistrarRecibido(cuenta.IdCuentaCorreo, remoto.Uidl, IdentidadProcesamiento.Calcular(mensaje))
                    If PoliticaMensajesAutomaticos.DebeIgnorar(mensaje, {cuenta.DireccionCorreo, cuenta.DireccionRespuesta}) Then
                        _registro.Confirmar(cuenta.IdCuentaCorreo, remoto.Uidl)
                        If opciones.EliminarTrasProcesar Then Await _pop3.EliminarAsync(cuenta, secretoPop3, remoto, limite.Token)
                        Continue For
                    End If
                    Dim resultado = _dispatcher.Procesar(mensaje)
                    Dim respuesta As New MensajeCorreoSaliente With {.Remitente = cuenta.DireccionCorreo, .NombreRemitente = cuenta.NombreVisible, .Destinatarios = {mensaje.Remitente}, .DireccionRespuesta = cuenta.DireccionRespuesta, .Asunto = resultado.AsuntoRespuesta, .CuerpoHtml = resultado.CuerpoHtml}
                    Await _smtp.EnviarAsync(cuenta, secretoSmtp, respuesta, limite.Token)
                    _registro.Confirmar(cuenta.IdCuentaCorreo, remoto.Uidl)
                    If opciones.EliminarTrasProcesar Then Await _pop3.EliminarAsync(cuenta, secretoPop3, remoto, limite.Token)
                Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                    _registro.RegistrarError(cuenta.IdCuentaCorreo, remoto.Uidl, EsTransitorio(ex), LimpiarError(ex.Message))
                End Try
            Next
        End Using
    End Function

    ''' <summary>Obtiene una credencial sin exponer su referencia en errores.</summary>
    ''' <param name="referencia">Referencia opaca administrada por Gestor Correo.</param><param name="uso">Uso funcional de la credencial.</param>
    ''' <returns>Secreto efímero recuperado del almacén seguro.</returns>
    Private Function ObtenerCredencial(referencia As String, uso As String) As String
        Dim secreto = _credenciales.Obtener(referencia)
        If String.IsNullOrEmpty(secreto) Then Throw New InvalidOperationException($"No encuentro la credencial de {uso}. Revisa la cuenta de correo y vuelve a intentarlo.")
        Return secreto
    End Function

    ''' <summary>Comprueba la cuenta neutral y los límites propios de recepción.</summary>
    ''' <param name="cuenta">Cuenta de Gestor Correo.</param><param name="opciones">Política del Dispatcher.</param>
    ''' <remarks>No devuelve un valor directo ni modifica datos.</remarks>
    Private Shared Sub Validar(cuenta As CuentaCorreo, opciones As OpcionesRecepcionDispatcher)
        ArgumentNullException.ThrowIfNull(cuenta)
        ArgumentNullException.ThrowIfNull(opciones)
        If cuenta.Estado <> EstadoCuentaCorreo.Activa Then Throw New InvalidOperationException("La cuenta de correo no está activa. Actívala en Configuración y vuelve a intentarlo.")
        Dim validacion = ValidadorCuentaCorreo.Validar(cuenta)
        If Not validacion.EsValida Then Throw New InvalidOperationException(String.Join(" ", validacion.Errores))
        If opciones.TamanoMaximoMensajeBytes < 1 OrElse opciones.NumeroMaximoAdjuntos < 0 OrElse opciones.TamanoMaximoAdjuntoBytes < 1 Then Throw New InvalidOperationException("Revisa los límites de recepción del Dispatcher: los tamaños deben ser positivos y el número de adjuntos no puede ser negativo.")
    End Sub

    ''' <summary>Aplica límites después de convertir el MIME completo.</summary>
    ''' <param name="mensaje">Mensaje materializado.</param><param name="opciones">Límites de recepción.</param>
    ''' <remarks>No devuelve un valor directo.</remarks>
    Private Shared Sub ValidarLimites(mensaje As MensajeEntrante, opciones As OpcionesRecepcionDispatcher)
        If mensaje.Adjuntos.Count > opciones.NumeroMaximoAdjuntos Then Throw New InvalidDataException($"El mensaje contiene {mensaje.Adjuntos.Count} adjuntos y el máximo permitido es {opciones.NumeroMaximoAdjuntos}.")
        If mensaje.Adjuntos.Any(Function(item) item.Contenido.LongLength > opciones.TamanoMaximoAdjuntoBytes) Then Throw New InvalidDataException($"Un adjunto supera el límite de {opciones.TamanoMaximoAdjuntoBytes} bytes.")
    End Sub

    ''' <summary>Clasifica fallos de red como reintentables.</summary>
    ''' <param name="ex">Error capturado.</param>
    ''' <returns>True para timeout, E/S o socket.</returns>
    Private Shared Function EsTransitorio(ex As Exception) As Boolean
        Return TypeOf ex Is TimeoutException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is Net.Sockets.SocketException
    End Function

    ''' <summary>Evita que detalles sensibles o excesivos lleguen a la traza.</summary>
    ''' <param name="detalle">Mensaje técnico.</param>
    ''' <returns>Texto limitado sin saltos.</returns>
    Private Shared Function LimpiarError(detalle As String) As String
        Dim limpio = If(detalle, "Error sin detalle").Replace(ControlChars.Cr, " ").Replace(ControlChars.Lf, " ")
        Return limpio.Substring(0, Math.Min(limpio.Length, 500))
    End Function
End Class
