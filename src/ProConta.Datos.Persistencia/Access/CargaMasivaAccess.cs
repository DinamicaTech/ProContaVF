using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProConta.Datos.Persistencia.Access;

/// <summary>
/// Carga masiva en Access (datos/persistencia 2026-11-11 14:00): inserta miles de filas con un Recordset de DAO (AddNew/Update)
/// en una transacción por tanda, sin una sentencia SQL por fila. Mil filas cuestan lo que unas pocas sentencias de OLE DB.
/// DAO se usa solo aquí: en un hilo STA propio (cada llamada COM entre apartamentos costaría tanto como una sentencia), con su
/// propia apertura de la BD en modo compartido, y cada tanda se confirma forzando la escritura en disco para que las conexiones
/// OLE DB que se abran después la vean. Los ficheros de texto con INSERT … SELECT no sirven: el ACE de Office bloquea las tablas
/// remotas en las consultas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CargaMasivaAccess : IDisposable
{
    /// <summary>Filas por transacción.</summary>
    public const int FilasPorTanda = 5000;

    private const int DbOpenTable = 1;
    private const int DbAppendOnly = 8;
    private const int DbForceOSFlush = 1;
    private const int DbMaxLocksPerFile = 62;

    private readonly BlockingCollection<Action> _cola = [];
    private readonly Thread _hilo;
    private dynamic? _motor;
    private dynamic? _bd;

    public CargaMasivaAccess(string rutaBd)
    {
        _hilo = new Thread(Bucle) { IsBackground = true, Name = "Carga masiva DAO" };
        _hilo.SetApartmentState(ApartmentState.STA);
        _hilo.Start();
        EnHilo<bool>(() =>
        {
            var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120")
                ?? throw new InvalidOperationException("No encuentro DAO (DAO.DBEngine.120) del motor de Access instalado en este equipo.");
            _motor = Activator.CreateInstance(tipo)!;
            // El límite de bloqueos por defecto (9.500) no deja confirmar tandas grandes.
            _motor.SetOption(DbMaxLocksPerFile, 1_000_000);
            _bd = _motor.OpenDatabase(rutaBd, false, false);
            return true;
        }).GetAwaiter().GetResult();
    }

    private void Bucle()
    {
        foreach (var accion in _cola.GetConsumingEnumerable()) accion();
    }

    private Task<T> EnHilo<T>(Func<T> trabajo)
    {
        var tarea = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cola.Add(() =>
        {
            try { tarea.SetResult(trabajo()); }
            catch (Exception ex) { tarea.SetException(ex); }
        });
        return tarea.Task;
    }

    /// <summary>
    /// Añade las filas a la tabla (un valor por campo, en el orden de <paramref name="campos"/>; null es NULL), en tandas de
    /// <paramref name="filasPorTanda"/> con una transacción cada una. Con <paramref name="campoId"/> devuelve el autonumérico de
    /// cada fila, en el mismo orden; sin él, una lista vacía.
    /// </summary>
    public Task<IReadOnlyList<int>> InsertarAsync(string tabla, IReadOnlyList<string> campos, IEnumerable<object?[]> filas, string? campoId = null,
        int filasPorTanda = FilasPorTanda, CancellationToken cancelacion = default) =>
        EnHilo<IReadOnlyList<int>>(() =>
        {
            var ids = new List<int>();
            dynamic espacio = _motor!.Workspaces[0];
            foreach (var tanda in filas.Chunk(filasPorTanda))
            {
                cancelacion.ThrowIfCancellationRequested();
                espacio.BeginTrans();
                dynamic? rs = null;
                try
                {
                    rs = _bd!.OpenRecordset(tabla, DbOpenTable, DbAppendOnly);
                    dynamic todos = rs.Fields;
                    var campo = campos.Select(c => (object)todos[c]).ToArray();
                    dynamic? id = campoId is null ? null : todos[campoId];
                    foreach (var fila in tanda)
                    {
                        if (fila.Length != campos.Count) throw new ArgumentException($"La fila tiene {fila.Length} valores y la carga de {tabla} {campos.Count} campos.", nameof(filas));
                        rs.AddNew();
                        for (var i = 0; i < fila.Length; i++) ((dynamic)campo[i]).Value = fila[i] ?? DBNull.Value;
                        if (id is not null) ids.Add(Convert.ToInt32(id.Value));
                        rs.Update();
                    }
                    rs.Close();
                    rs = null;
                    espacio.CommitTrans(DbForceOSFlush);
                }
                catch
                {
                    try { rs?.Close(); } catch (COMException) { }
                    espacio.Rollback();
                    throw;
                }
            }
            return ids;
        });

    public void Dispose()
    {
        try
        {
            EnHilo<bool>(() =>
            {
                try { _bd?.Close(); } catch (COMException) { }
                if (_bd is not null) Marshal.FinalReleaseComObject(_bd);
                if (_motor is not null) Marshal.FinalReleaseComObject(_motor);
                _bd = null;
                _motor = null;
                return true;
            }).GetAwaiter().GetResult();
        }
        finally
        {
            _cola.CompleteAdding();
            _hilo.Join();
            _cola.Dispose();
        }
    }
}
