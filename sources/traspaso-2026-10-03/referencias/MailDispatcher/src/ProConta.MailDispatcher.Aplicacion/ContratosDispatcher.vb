Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Enum TipoAdjuntoDispatcher
    NoReconocido
    Norma43
    Nomina
    FacturaCompra
End Enum

Public Enum EstadoProcesamientoMensaje
    Recibido
    EnProceso
    Completado
    CompletadoConIncidencias
    Fallido
    Ignorado
End Enum

Public NotInheritable Class ConfiguracionBuzon
    Public Property IdConfiguracion As Long
    Public Property IdSociedadEjercicio As Long
    Public Property EstaActivo As Boolean
    Public Property DireccionReceptora As String = String.Empty
    Public Property NombreVisible As String = String.Empty
    Public Property ServidorPop3 As String = String.Empty
    Public Property PuertoPop3 As Integer = 995
    Public Property TlsPop3 As String = "TlsImplicito"
    Public Property UsuarioPop3 As String = String.Empty
    Public Property ServidorSmtp As String = String.Empty
    Public Property PuertoSmtp As Integer = 587
    Public Property TlsSmtp As String = "StartTls"
    Public Property AutenticarSmtp As Boolean = True
    Public Property UsuarioSmtp As String = String.Empty
    Public Property ReferenciaSecreto As String = String.Empty
    Public Property DireccionRemitente As String = String.Empty
    Public Property DireccionRespuesta As String = String.Empty
    Public Property IntervaloMinutos As Integer = 5
    Public Property EliminarTrasProcesar As Boolean
    Public Property TiempoEsperaSegundos As Integer = 30
    Public Property TamanoMaximoMensajeBytes As Long = 26214400
    Public Property NumeroMaximoAdjuntos As Integer = 20
    Public Property TamanoMaximoAdjuntoBytes As Long = 10485760
End Class

Public NotInheritable Class OpcionesRecepcionDispatcher
    Public Property EliminarTrasProcesar As Boolean
    Public Property TamanoMaximoMensajeBytes As Long = 26214400
    Public Property NumeroMaximoAdjuntos As Integer = 20
    Public Property TamanoMaximoAdjuntoBytes As Long = 10485760
End Class

Public Interface IConversorMimeDispatcher
    ''' <summary>Convierte el MIME original en el mensaje funcional que procesa el Dispatcher.</summary>
    ''' <param name="contenidoMime">Contenido MIME completo; no puede ser <c>Nothing</c>.</param>
    ''' <returns>Mensaje desconectado con cabeceras y adjuntos materializados.</returns>
    Function Convertir(contenidoMime As Byte()) As MensajeEntrante
End Interface

Public Interface IRegistroEntregaPop3
    ''' <summary>Comprueba si un UIDL ya terminó completamente.</summary>
    ''' <param name="idConfiguracion">Cuenta.</param><param name="uidl">Identidad remota.</param>
    ''' <returns>True si no debe descargarse ni responderse otra vez.</returns>
    Function EstaCompletado(idConfiguracion As Long, uidl As String) As Boolean
    ''' <summary>Persiste UIDL e identidades de respaldo antes de ejecutar efectos.</summary>
    ''' <param name="idConfiguracion">Cuenta.</param><param name="uidl">Identidad POP3 primaria.</param><param name="claveRespaldo">Message-Id y hash.</param>
    ''' <remarks>No devuelve valor directo; reserva o actualiza el estado recibido.</remarks>
    Sub RegistrarRecibido(idConfiguracion As Long, uidl As String, claveRespaldo As String)
    ''' <summary>Registra una incidencia transitoria o permanente sin secretos.</summary>
    ''' <param name="idConfiguracion">Cuenta.</param><param name="uidl">Identidad remota.</param><param name="esTransitorio">Permite reintento.</param><param name="detalle">Explicación saneada.</param>
    ''' <remarks>No devuelve valor directo; actualiza trazabilidad local.</remarks>
    Sub RegistrarError(idConfiguracion As Long, uidl As String, esTransitorio As Boolean, detalle As String)
    ''' <summary>Confirma que proceso y respuesta SMTP finalizaron.</summary>
    ''' <param name="idConfiguracion">Cuenta.</param><param name="uidl">Identidad remota.</param>
    ''' <remarks>No devuelve valor directo; marca el éxito local.</remarks>
    Sub Confirmar(idConfiguracion As Long, uidl As String)
End Interface

Public NotInheritable Class MensajeEntrante
    Public Property MessageId As String = String.Empty
    Public Property Remitente As String = String.Empty
    Public Property Destinatarios As IReadOnlyList(Of String) = Array.Empty(Of String)()
    Public Property Asunto As String = String.Empty
    Public Property Cabeceras As IReadOnlyDictionary(Of String, String) = New Dictionary(Of String, String)()
    Public Property Adjuntos As IReadOnlyList(Of AdjuntoEntrante) = Array.Empty(Of AdjuntoEntrante)()
End Class

Public NotInheritable Class AdjuntoEntrante
    Public Property Nombre As String = String.Empty
    Public Property Contenido As Byte() = Array.Empty(Of Byte)()
End Class

Public NotInheritable Class ContextoMensaje
    Public Property IdSociedadEjercicio As Long
    Public Property Configuracion As ConfiguracionBuzon = New ConfiguracionBuzon()
End Class

Public NotInheritable Class ResultadoAdjunto
    Public Property NombreOriginal As String = String.Empty
    Public Property Tipo As TipoAdjuntoDispatcher
    Public Property Correcto As Boolean
    Public Property Resumen As String = String.Empty
    Public Property RutaMaterializada As String = String.Empty
End Class

Public NotInheritable Class ResultadoMensaje
    Public Property Estado As EstadoProcesamientoMensaje
    Public Property EsReintento As Boolean
    Public Property AsuntoRespuesta As String = String.Empty
    Public Property CuerpoHtml As String = String.Empty
    Public Property Adjuntos As IReadOnlyList(Of ResultadoAdjunto) = Array.Empty(Of ResultadoAdjunto)()
End Class

Public Interface IRepositorioConfiguracionesBuzon
    ''' <summary>Obtiene las cuentas activas que reciben una dirección concreta.</summary>
    ''' <param name="direccion">Dirección normalizada del sobre o cabecera de destino.</param>
    ''' <returns>Configuraciones activas candidatas.</returns>
    Function BuscarActivasPorDireccion(direccion As String) As IReadOnlyList(Of ConfiguracionBuzon)
End Interface

Public Interface IIdentificadorAdjunto
    ReadOnly Property Tipo As TipoAdjuntoDispatcher
    ''' <summary>Determina si el motor propietario reconoce un fichero.</summary>
    ''' <param name="adjunto">Nombre y contenido desconectados.</param>
    ''' <returns>True únicamente cuando el motor puede procesarlo.</returns>
    Function IdentificarFichero(adjunto As AdjuntoEntrante) As Boolean
End Interface

Public Interface IProcesadorAdjunto
    ReadOnly Property Tipo As TipoAdjuntoDispatcher
    ''' <summary>Delega el procesamiento y materialización en el adaptador del motor propietario.</summary>
    ''' <param name="adjunto">Fichero aislado que se procesa.</param>
    ''' <param name="mensaje">Mensaje de origen.</param>
    ''' <param name="contexto">Sociedad y ejercicio resueltos por destinatario.</param>
    ''' <returns>Resultado independiente del fichero.</returns>
    Function Procesar(adjunto As AdjuntoEntrante, mensaje As MensajeEntrante, contexto As ContextoMensaje) As ResultadoAdjunto
End Interface

Public Interface IRegistroProcesamientos
    ''' <summary>Intenta reservar de forma atómica la identidad de un mensaje.</summary>
    ''' <param name="clave">Message-Id y hash estable calculados por el servicio.</param>
    ''' <returns>True si este intento adquirió el procesamiento.</returns>
    Function IntentarIniciar(clave As String) As Boolean
    ''' <summary>Registra el estado terminal y su resumen trazable.</summary>
    ''' <param name="clave">Identidad previamente reservada.</param>
    ''' <param name="estado">Estado final.</param>
    ''' <param name="detalle">Resumen sin secretos.</param>
    ''' <remarks>No devuelve un valor directo; actualiza la traza persistente.</remarks>
    Sub Completar(clave As String, estado As EstadoProcesamientoMensaje, detalle As String)
End Interface

Public Interface IAutorizadorComando
    ''' <summary>Comprueba si el remitente puede ejecutar un comando en el contexto resuelto.</summary>
    ''' <param name="remitente">Dirección del operador.</param>
    ''' <param name="permiso">Permiso estable requerido.</param>
    ''' <param name="idSociedadEjercicio">Contexto anual inequívoco.</param>
    ''' <returns>True si debe mostrarse y ejecutarse.</returns>
    Function EstaAutorizado(remitente As String, permiso As String, idSociedadEjercicio As Long) As Boolean
End Interface

Public Interface IEjecutorComando
    ''' <summary>Ejecuta una solicitud mediante Reporting, Modelos u otro motor propietario.</summary>
    ''' <param name="solicitud">Comando ya analizado y autorizado.</param>
    ''' <param name="contexto">Sociedad y ejercicio resueltos.</param>
    ''' <returns>Respuesta desacoplada del transporte.</returns>
    Function Ejecutar(solicitud As SolicitudComando, contexto As ContextoMensaje) As ResultadoComando
End Interface

Public NotInheritable Class SolicitudComando
    Public Property Definicion As DefinicionComando
    Public Property Argumentos As String = String.Empty
    Public Property Formato As String = String.Empty
End Class

Public NotInheritable Class ResultadoComando
    Public Property CuerpoHtml As String = String.Empty
    Public Property NombreAdjunto As String = String.Empty
    Public Property ContenidoAdjunto As Byte() = Array.Empty(Of Byte)()
End Class

Public NotInheritable Class DefinicionComando
    Public Property Nombre As String = String.Empty
    Public Property Aliases As IReadOnlyList(Of String) = Array.Empty(Of String)()
    Public Property Sintaxis As String = String.Empty
    Public Property Ejemplos As IReadOnlyList(Of String) = Array.Empty(Of String)()
    Public Property Formatos As IReadOnlyList(Of String) = Array.Empty(Of String)()
    Public Property FormatoPredeterminado As String = String.Empty
    Public Property Permiso As String = String.Empty
End Class

Public Module IdentidadProcesamiento
    ''' <summary>Calcula una clave idempotente estable sin almacenar el contenido del correo.</summary>
    ''' <param name="mensaje">Mensaje recibido.</param>
    ''' <returns>Message-Id normalizado más SHA-256 del contenido relevante.</returns>
    Public Function Calcular(mensaje As MensajeEntrante) As String
        ArgumentNullException.ThrowIfNull(mensaje)
        Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Agregar(hash, mensaje.Remitente)
            Agregar(hash, mensaje.Asunto)
            For Each destinatario In mensaje.Destinatarios.OrderBy(Function(valor) valor, StringComparer.OrdinalIgnoreCase)
                Agregar(hash, destinatario)
            Next
            For Each adjunto In mensaje.Adjuntos
                Agregar(hash, adjunto.Nombre)
                hash.AppendData(adjunto.Contenido)
            Next
            Return $"{mensaje.MessageId.Trim().ToLowerInvariant()}|{Convert.ToHexString(hash.GetHashAndReset())}"
        End Using
    End Function

    ''' <summary>Añade texto UTF-8 con separador inequívoco al hash incremental.</summary>
    ''' <param name="hash">Acumulador criptográfico.</param>
    ''' <param name="valor">Texto opcional.</param>
    ''' <remarks>No devuelve un valor directo; modifica el acumulador.</remarks>
    Private Sub Agregar(hash As IncrementalHash, valor As String)
        hash.AppendData(Encoding.UTF8.GetBytes(If(valor, String.Empty)))
        hash.AppendData({CByte(0)})
    End Sub
End Module
