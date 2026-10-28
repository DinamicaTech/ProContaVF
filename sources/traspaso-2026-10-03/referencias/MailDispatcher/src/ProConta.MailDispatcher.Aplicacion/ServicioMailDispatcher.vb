Public NotInheritable Class ServicioMailDispatcher
    Private ReadOnly _resolutor As ResolutorBuzon
    Private ReadOnly _cadena As CadenaIdentificadoresAdjunto
    Private ReadOnly _procesadores As IReadOnlyDictionary(Of TipoAdjuntoDispatcher, IProcesadorAdjunto)
    Private ReadOnly _registro As IRegistroProcesamientos
    Private ReadOnly _comandos As RegistroComandos
    Private ReadOnly _autorizador As IAutorizadorComando
    Private ReadOnly _ejecutor As IEjecutorComando

    ''' <summary>Compone el flujo sin acoplar transporte, motores ni almacenamiento.</summary>
    ''' <param name="resolutor">Resolución por destinatario.</param><param name="cadena">Clasificación encadenada.</param><param name="procesadores">Adaptadores de motores.</param><param name="registro">Reserva idempotente.</param><param name="comandos">Catálogo extensible.</param><param name="autorizador">Permisos.</param><param name="ejecutor">Delegación de informes.</param>
    ''' <remarks>No devuelve un valor directo ni abre conexiones.</remarks>
    Public Sub New(resolutor As ResolutorBuzon, cadena As CadenaIdentificadoresAdjunto, procesadores As IEnumerable(Of IProcesadorAdjunto), registro As IRegistroProcesamientos, comandos As RegistroComandos, autorizador As IAutorizadorComando, ejecutor As IEjecutorComando)
        _resolutor = resolutor
        _cadena = cadena
        _procesadores = procesadores.ToDictionary(Function(item) item.Tipo)
        _registro = registro
        _comandos = comandos
        _autorizador = autorizador
        _ejecutor = ejecutor
    End Sub

    ''' <summary>Procesa un mensaje aislando cada adjunto y haciendo seguros los reintentos.</summary>
    ''' <param name="mensaje">Mensaje ya leído por un adaptador de transporte.</param>
    ''' <returns>Respuesta que el transporte podrá enviar.</returns>
    Public Function Procesar(mensaje As MensajeEntrante) As ResultadoMensaje
        Dim clave = IdentidadProcesamiento.Calcular(mensaje)
        If Not _registro.IntentarIniciar(clave) Then Return New ResultadoMensaje With {.Estado = EstadoProcesamientoMensaje.Completado, .EsReintento = True}
        Try
            Dim contexto = _resolutor.Resolver(mensaje.Destinatarios)
            Dim resultado As ResultadoMensaje
            If mensaje.Adjuntos.Count = 0 Then
                resultado = ProcesarComando(mensaje, contexto)
            Else
                resultado = ProcesarAdjuntos(mensaje, contexto)
            End If
            _registro.Completar(clave, resultado.Estado, resultado.CuerpoHtml)
            Return resultado
        Catch ex As Exception
            _registro.Completar(clave, EstadoProcesamientoMensaje.Fallido, ex.Message)
            Throw
        End Try
    End Function

    ''' <summary>Procesa ficheros independientemente para conservar resultados parciales.</summary>
    ''' <param name="mensaje">Mensaje origen.</param><param name="contexto">Contexto anual resuelto.</param>
    ''' <returns>Resumen de todos los adjuntos.</returns>
    Private Function ProcesarAdjuntos(mensaje As MensajeEntrante, contexto As ContextoMensaje) As ResultadoMensaje
        Dim resultados As New List(Of ResultadoAdjunto)()
        For Each adjunto In mensaje.Adjuntos
            Dim tipo = _cadena.Identificar(adjunto)
            If tipo = TipoAdjuntoDispatcher.NoReconocido OrElse Not _procesadores.ContainsKey(tipo) Then
                resultados.Add(New ResultadoAdjunto With {.NombreOriginal = adjunto.Nombre, .Tipo = tipo, .Correcto = False, .Resumen = "No reconozco este fichero. Comprueba que sea Norma 43, una nómina o una factura de compra admitida."})
                Continue For
            End If
            Try
                resultados.Add(_procesadores(tipo).Procesar(adjunto, mensaje, contexto))
            Catch ex As Exception
                resultados.Add(New ResultadoAdjunto With {.NombreOriginal = adjunto.Nombre, .Tipo = tipo, .Correcto = False, .Resumen = $"No pude procesar '{adjunto.Nombre}': {ex.Message} Revisa el fichero y vuelve a enviarlo; los demás adjuntos sí se han procesado."})
            End Try
        Next
        Dim estado = If(resultados.All(Function(item) item.Correcto), EstadoProcesamientoMensaje.Completado, EstadoProcesamientoMensaje.CompletadoConIncidencias)
        Return New ResultadoMensaje With {.Estado = estado, .AsuntoRespuesta = $"Resultado: {mensaje.Asunto}", .CuerpoHtml = String.Join("<br>", resultados.Select(Function(item) Net.WebUtility.HtmlEncode(item.Resumen))), .Adjuntos = resultados}
    End Function

    ''' <summary>Analiza, autoriza y delega un comando o produce ayuda viva.</summary>
    ''' <param name="mensaje">Mensaje sin adjuntos.</param><param name="contexto">Contexto anual resuelto.</param>
    ''' <returns>Respuesta lista para transporte.</returns>
    Private Function ProcesarComando(mensaje As MensajeEntrante, contexto As ContextoMensaje) As ResultadoMensaje
        Dim solicitud = _comandos.Analizar(mensaje.Asunto)
        If solicitud Is Nothing Then Return New ResultadoMensaje With {.Estado = EstadoProcesamientoMensaje.Completado, .AsuntoRespuesta = "Ayuda de ProConta", .CuerpoHtml = _comandos.GenerarAyuda(mensaje.Remitente, contexto.IdSociedadEjercicio, _autorizador)}
        If Not _autorizador.EstaAutorizado(mensaje.Remitente, solicitud.Definicion.Permiso, contexto.IdSociedadEjercicio) Then Return New ResultadoMensaje With {.Estado = EstadoProcesamientoMensaje.CompletadoConIncidencias, .AsuntoRespuesta = "Permiso necesario", .CuerpoHtml = "No tienes permiso para ejecutar esta solicitud en la sociedad y ejercicio de la dirección receptora. Pide al administrador que revise tu acceso y vuelve a intentarlo."}
        Dim ejecutado = _ejecutor.Ejecutar(solicitud, contexto)
        Return New ResultadoMensaje With {.Estado = EstadoProcesamientoMensaje.Completado, .AsuntoRespuesta = $"Resultado: {solicitud.Definicion.Nombre}", .CuerpoHtml = ejecutado.CuerpoHtml}
    End Function
End Class
