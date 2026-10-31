using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Diccionario;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;

// ActualizadorEsquema de ProContaVF (docs/datos/esquema.md).
//   ActualizadorEsquema diagnosticar <bd>
//   ActualizadorEsquema actualizar <bd> <carpeta de copias>
//   ActualizadorEsquema diccionario <bd> <carpeta de salida>
//   ActualizadorEsquema copia-estructura <bd origen> <bd destino>

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("""
        Uso:
          ActualizadorEsquema diagnosticar <bd>
              Informa de la versión del esquema y de los cambios pendientes, sin modificar nada.
          ActualizadorEsquema actualizar <bd> <carpeta de copias>
              Hace una copia de seguridad y aplica los cambios pendientes (todos deben cerrar ProContaVF).
          ActualizadorEsquema diccionario <bd> <carpeta de salida>
              Genera el diccionario de datos (un md por área) leyendo una copia temporal de la BD.
          ActualizadorEsquema copia-estructura <bd origen> <bd destino>
              Crea una BD con la misma estructura y sin datos de negocio (para pruebas o maestra vacía).
        """);
    return 2;
}

var codigo = await EjecutarAsync(args);
BaseDatosAccess.TerminarProceso(codigo);
return codigo;

static async Task<int> EjecutarAsync(string[] args)
{
try
{
    switch (args[0].ToLowerInvariant())
    {
        case "diagnosticar":
        {
            var diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(new BaseDatosAccess(args[1], ModoApertura.SoloLectura));
            Console.WriteLine(diagnostico.Resumen());
            return diagnostico.EsCompatible ? 0 : 1;
        }
        case "actualizar" when args.Length >= 3:
        {
            var actualizador = new ActualizadorEsquema(new Actor(Environment.UserName));
            var resultado = await actualizador.ActualizarAsync(args[1], args[2]);
            if (!resultado.EsCorrecto)
            {
                foreach (var error in resultado.Errores) Console.Error.WriteLine(error.TextoOperador());
                return 1;
            }
            var informe = resultado.Valor;
            Console.WriteLine(informe.Aplicadas.Count == 0
                ? $"La BD ya estaba actualizada (versión {informe.VersionFinal}). No he cambiado nada."
                : $"Actualizada de la versión {informe.VersionInicial} a la {informe.VersionFinal}: {string.Join(", ", informe.Aplicadas)}. Copia previa: {informe.CopiaSeguridad}");
            return 0;
        }
        case "diccionario" when args.Length >= 3:
        {
            // Se lee siempre una copia temporal: el original no se abre.
            var temporal = Path.Combine(Path.GetTempPath(), $"ProContaVF-diccionario-{Guid.NewGuid():N}.accdb");
            File.Copy(args[1], temporal);
            try
            {
                var huella = ActualizadorEsquema.Sha256(temporal);
                var escritos = await GeneradorDiccionario.GenerarAsync(new BaseDatosAccess(temporal, ModoApertura.SoloLectura), args[2], huella);
                Console.WriteLine($"Diccionario generado en {Path.GetFullPath(args[2])}: {escritos.Count} ficheros.");
            }
            finally
            {
                File.Delete(temporal);
            }
            return 0;
        }
        case "copia-estructura" when args.Length >= 3:
            await CopiaEstructura.CrearAsync(args[1], args[2]);
            Console.WriteLine($"Copia sin datos de negocio creada en {Path.GetFullPath(args[2])}.");
            return 0;
        default:
            Console.Error.WriteLine("Comando o argumentos no reconocidos. Ejecuta el programa sin argumentos para ver el uso.");
            return 2;
    }
}
catch (ExcepcionDatos ex)
{
    Console.Error.WriteLine(ex.Error.TextoOperador());
    return 1;
}
catch (IOException ex)
{
    Console.Error.WriteLine($"No he podido acceder a un fichero: {ex.Message}");
    return 1;
}
}
