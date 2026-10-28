Imports System.Net
Imports System.Text

Public NotInheritable Class ResolutorBuzon
    Private ReadOnly _repositorio As IRepositorioConfiguracionesBuzon

    ''' <summary>Prepara la resolución estricta del contexto anual mediante destinatarios.</summary>
    ''' <param name="repositorio">Origen de configuraciones activas.</param>
    ''' <remarks>No devuelve un valor directo ni abre conexiones.</remarks>
    Public Sub New(repositorio As IRepositorioConfiguracionesBuzon)
        If repositorio Is Nothing Then Throw New ArgumentNullException(NameOf(repositorio))
        _repositorio = repositorio
    End Sub

    ''' <summary>Resuelve una única sociedad y ejercicio sin consultar el asunto.</summary>
    ''' <param name="destinatarios">Direcciones receptoras del mensaje.</param>
    ''' <returns>Contexto único.</returns>
    Public Function Resolver(destinatarios As IEnumerable(Of String)) As ContextoMensaje
        Dim candidatas = DestinatariosNormalizados(destinatarios).SelectMany(Function(direccion) _repositorio.BuscarActivasPorDireccion(direccion)).GroupBy(Function(item) item.IdConfiguracion).Select(Function(grupo) grupo.First()).ToArray()
        If candidatas.Length = 0 Then Throw New InvalidOperationException("No hay una cuenta activa asociada a los destinatarios. Revisa la dirección receptora en Configuración > Motor Mail Dispatcher.")
        If candidatas.Length > 1 OrElse candidatas.Select(Function(item) item.IdSociedadEjercicio).Distinct().Count() > 1 Then Throw New InvalidOperationException("Los destinatarios resuelven más de una sociedad o ejercicio. Corrige las direcciones duplicadas o ambiguas en Configuración > Motor Mail Dispatcher.")
        Return New ContextoMensaje With {.IdSociedadEjercicio = candidatas(0).IdSociedadEjercicio, .Configuracion = candidatas(0)}
    End Function

    ''' <summary>Normaliza y deduplica direcciones receptoras.</summary>
    ''' <param name="destinatarios">Colección opcional.</param>
    ''' <returns>Direcciones no vacías en minúsculas.</returns>
    Private Shared Function DestinatariosNormalizados(destinatarios As IEnumerable(Of String)) As IReadOnlyList(Of String)
        Return If(destinatarios, Array.Empty(Of String)()).Select(Function(valor) If(valor, String.Empty).Trim().ToLowerInvariant()).Where(Function(valor) valor.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    End Function
End Class

Public NotInheritable Class CadenaIdentificadoresAdjunto
    Private ReadOnly _identificadores As IReadOnlyList(Of IIdentificadorAdjunto)

    ''' <summary>Fija el orden fiscal de identificación: Norma 43, Nóminas y Facturas de compra.</summary>
    ''' <param name="identificadores">Adaptadores de motores propietarios.</param>
    ''' <remarks>No devuelve un valor directo.</remarks>
    Public Sub New(identificadores As IEnumerable(Of IIdentificadorAdjunto))
        Dim porTipo = If(identificadores, Array.Empty(Of IIdentificadorAdjunto)()).ToDictionary(Function(item) item.Tipo)
        _identificadores = {TipoAdjuntoDispatcher.Norma43, TipoAdjuntoDispatcher.Nomina, TipoAdjuntoDispatcher.FacturaCompra}.Where(Function(tipo) porTipo.ContainsKey(tipo)).Select(Function(tipo) porTipo(tipo)).ToArray()
    End Sub

    ''' <summary>Consulta cada contrato sin conocer sus reglas internas.</summary>
    ''' <param name="adjunto">Fichero recibido.</param>
    ''' <returns>Primer tipo reconocido o NoReconocido.</returns>
    Public Function Identificar(adjunto As AdjuntoEntrante) As TipoAdjuntoDispatcher
        For Each identificador In _identificadores
            If identificador.IdentificarFichero(adjunto) Then Return identificador.Tipo
        Next
        Return TipoAdjuntoDispatcher.NoReconocido
    End Function
End Class

Public NotInheritable Class RegistroComandos
    Private ReadOnly _comandos As IReadOnlyList(Of DefinicionComando)

    ''' <summary>Crea el catálogo extensible y comprueba aliases no ambiguos.</summary>
    ''' <param name="comandos">Definiciones aportadas por módulos.</param>
    ''' <remarks>No devuelve un valor directo.</remarks>
    Public Sub New(comandos As IEnumerable(Of DefinicionComando))
        _comandos = If(comandos, Array.Empty(Of DefinicionComando)()).ToArray()
        Dim repetido = _comandos.SelectMany(Function(item) item.Aliases.Append(item.Nombre)).GroupBy(Function(item) item.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(Function(grupo) grupo.Count() > 1)
        If repetido IsNot Nothing Then Throw New ArgumentException($"El alias de comando '{repetido.Key}' es ambiguo.", NameOf(comandos))
    End Sub

    ''' <summary>Proporciona el catálogo inicial acordado.</summary>
    ''' <returns>Registro listo para analizar y generar ayuda.</returns>
    Public Shared Function CrearPredeterminado() As RegistroComandos
        Return New RegistroComandos({
            Crear("BALANCE SITUACION", {"BALANCE DE SITUACION"}, "BALANCE SITUACION [PDF]", {"BALANCE SITUACION PDF"}, {"PDF"}, "PDF", "reporting.balance"),
            Crear("PYG", {"P&G", "PERDIDAS Y GANANCIAS", "BALANCE RESULTADOS"}, "PYG [PDF]", {"P&G PDF"}, {"PDF"}, "PDF", "reporting.pyg"),
            Crear("MODELO", {"180", "347", "200"}, "<modelo> <periodo> [EXCEL|XLS|XLSX]", {"347 2026 XLSX"}, {"EXCEL", "XLS", "XLSX"}, "XLSX", "modelos.exportar"),
            Crear("MAYOR", {}, "MAYOR <subcuenta|nombre> [PDF|EXCEL|XLS]", {"MAYOR 4300001 PDF"}, {"PDF", "EXCEL", "XLS"}, "EXCEL", "reporting.mayor"),
            Crear("DIARIO", {}, "DIARIO [PDF|EXCEL]", {"DIARIO", "DIARIO PDF"}, {"PDF", "EXCEL"}, "EXCEL", "reporting.diario"),
            Crear("RETENCIONES", {}, "RETENCIONES <mes|trimestre> [PDF|EXCEL]", {"RETENCIONES T1 EXCEL"}, {"PDF", "EXCEL"}, "EXCEL", "reporting.retenciones"),
            Crear("INDICADORES", {}, "INDICADORES [PDF|EXCEL]", {"INDICADORES PDF"}, {"PDF", "EXCEL"}, "EXCEL", "reporting.indicadores")})
    End Function

    ''' <summary>Analiza el prefijo más largo y separa formato y argumentos.</summary>
    ''' <param name="asunto">Asunto que contiene únicamente la solicitud.</param>
    ''' <returns>Solicitud o Nothing cuando no coincide.</returns>
    Public Function Analizar(asunto As String) As SolicitudComando
        Dim texto = Normalizar(asunto)
        For Each par In _comandos.SelectMany(Function(comando) comando.Aliases.Append(comando.Nombre).Select(Function(aliasTexto) (Comando:=comando, AliasTexto:=Normalizar(aliasTexto)))).OrderByDescending(Function(item) item.AliasTexto.Length)
            If texto.Equals(par.AliasTexto, StringComparison.OrdinalIgnoreCase) OrElse texto.StartsWith(par.AliasTexto & " ", StringComparison.OrdinalIgnoreCase) Then
                Dim resto = texto.Substring(par.AliasTexto.Length).Trim()
                Dim formato = par.Comando.FormatoPredeterminado
                For Each candidato In par.Comando.Formatos.OrderByDescending(Function(item) item.Length)
                    If resto.EndsWith(" " & candidato, StringComparison.OrdinalIgnoreCase) OrElse resto.Equals(candidato, StringComparison.OrdinalIgnoreCase) Then
                        formato = candidato
                        resto = resto.Substring(0, resto.Length - candidato.Length).Trim()
                        Exit For
                    End If
                Next
                Return New SolicitudComando With {.Definicion = par.Comando, .Argumentos = resto, .Formato = formato}
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>Genera ayuda directamente desde comandos autorizados.</summary>
    ''' <param name="remitente">Operador que recibirá la ayuda.</param>
    ''' <param name="idSociedadEjercicio">Contexto anual resuelto.</param>
    ''' <param name="autorizador">Servicio de permisos.</param>
    ''' <returns>HTML seguro y sincronizado con el registro.</returns>
    Public Function GenerarAyuda(remitente As String, idSociedadEjercicio As Long, autorizador As IAutorizadorComando) As String
        Dim sb As New StringBuilder("<h2>¿Qué necesitas de ProConta?</h2><p>Escribe uno de estos comandos en el asunto:</p><ul>")
        For Each comando In _comandos.Where(Function(item) autorizador.EstaAutorizado(remitente, item.Permiso, idSociedadEjercicio))
            sb.Append("<li><strong>").Append(WebUtility.HtmlEncode(comando.Sintaxis)).Append("</strong>")
            If comando.Aliases.Count > 0 Then sb.Append("<br>También puedes usar: ").Append(WebUtility.HtmlEncode(String.Join(", ", comando.Aliases)))
            sb.Append("<br>Ejemplo: ").Append(WebUtility.HtmlEncode(comando.Ejemplos.FirstOrDefault())).Append("<br>Formatos: ").Append(WebUtility.HtmlEncode(String.Join(", ", comando.Formatos))).Append("</li>")
        Next
        Return sb.Append("</ul>").ToString()
    End Function

    ''' <summary>Crea una definición compacta del catálogo incorporado.</summary>
    ''' <param name="nombre">Nombre canónico.</param><param name="aliases">Alternativas.</param><param name="sintaxis">Sintaxis visible.</param><param name="ejemplos">Ejemplos.</param><param name="formatos">Formatos.</param><param name="predeterminado">Formato por omisión.</param><param name="permiso">Permiso requerido.</param>
    ''' <returns>Definición inmutable por convención.</returns>
    Private Shared Function Crear(nombre As String, aliases As IEnumerable(Of String), sintaxis As String, ejemplos As IEnumerable(Of String), formatos As IEnumerable(Of String), predeterminado As String, permiso As String) As DefinicionComando
        Return New DefinicionComando With {.Nombre = nombre, .Aliases = aliases.ToArray(), .Sintaxis = sintaxis, .Ejemplos = ejemplos.ToArray(), .Formatos = formatos.ToArray(), .FormatoPredeterminado = predeterminado, .Permiso = permiso}
    End Function

    ''' <summary>Normaliza espacios y acentos para comparar comandos humanos.</summary>
    ''' <param name="texto">Texto opcional.</param>
    ''' <returns>Texto canónico.</returns>
    Private Shared Function Normalizar(texto As String) As String
        Dim valor = If(texto, String.Empty).Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD)
        valor = New String(valor.Where(Function(caracter) Globalization.CharUnicodeInfo.GetUnicodeCategory(caracter) <> Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC)
        Return String.Join(" ", valor.Split({" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries))
    End Function
End Class

Public NotInheritable Class PoliticaMensajesAutomaticos
    ''' <summary>Detecta mensajes que no deben provocar una respuesta automática.</summary>
    ''' <param name="mensaje">Mensaje recibido.</param>
    ''' <param name="direccionesPropias">Direcciones controladas por ProConta.</param>
    ''' <returns>True para mensajes propios, rebotes, auto-respuestas o listas.</returns>
    Public Shared Function DebeIgnorar(mensaje As MensajeEntrante, direccionesPropias As IEnumerable(Of String)) As Boolean
        Dim remitente = If(mensaje.Remitente, String.Empty).Trim()
        If If(direccionesPropias, Array.Empty(Of String)()).Any(Function(item) String.Equals(If(item, String.Empty).Trim(), remitente, StringComparison.OrdinalIgnoreCase)) Then Return True
        Dim valor As String = Nothing
        If mensaje.Cabeceras.TryGetValue("Auto-Submitted", valor) AndAlso Not String.Equals(valor, "no", StringComparison.OrdinalIgnoreCase) Then Return True
        If mensaje.Cabeceras.ContainsKey("X-Auto-Response-Suppress") OrElse mensaje.Cabeceras.ContainsKey("List-Id") Then Return True
        Return remitente.Contains("mailer-daemon", StringComparison.OrdinalIgnoreCase) OrElse remitente.Contains("postmaster", StringComparison.OrdinalIgnoreCase)
    End Function
End Class
