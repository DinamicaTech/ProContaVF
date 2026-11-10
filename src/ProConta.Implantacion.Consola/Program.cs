using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Auditoria;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

// Importador de ContaSol (docs/implantacion/importador-contasol.md).
//   ImportadorContaSol <bd de ProContaVF> <bd de ContaSol>... [--simular] [--informe <fichero md>]

Console.OutputEncoding = Encoding.UTF8;
var simular = args.Contains("--simular", StringComparer.OrdinalIgnoreCase);
var iInforme = Array.FindIndex(args, a => a.Equals("--informe", StringComparison.OrdinalIgnoreCase));
var informe = iInforme >= 0 && iInforme + 1 < args.Length ? args[iInforme + 1] : null;
var posicionales = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (iInforme < 0 || i != iInforme + 1)).ToList();
if (posicionales.Count < 2)
{
    Console.WriteLine("""
        Uso:
          ImportadorContaSol <bd de ProContaVF> <bd de ContaSol>... [--simular] [--informe <fichero md>]
              Importa cada BD de ContaSol (una por ejercicio) en la BD de ProContaVF, en orden de ejercicio.
              --simular: solo lee y comprueba, sin escribir.  --informe: escribe el resumen y las incidencias en un md.
        """);
    return 2;
}

IBaseDatos bd = new BaseDatosAccess(posicionales[0]);
var cuentas = new CatalogoCuentasAccess(bd);
var importador = new ImportadorContaSol(bd, new MotorAsientosAccess(bd), new CatalogoCentrosCosteAccess(bd), new RepositorioTercerosAccess(bd), new ServicioRoles(bd, cuentas));
var ultimo = "";
var progreso = new Progress<ProgresoImportacion>(p =>
{
    var linea = $"{p.Ejercicio} · {p.Fase}";
    if (linea != ultimo || p.Hechos == p.Total) Console.WriteLine($"{DateTime.Now:HH:mm:ss} {p.Texto}");
    ultimo = linea;
});
using var cancelar = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelar.Cancel(); };

var inicio = DateTime.Now;
var planes = await importador.AnalizarAsync(posicionales.Skip(1), progreso, cancelar.Token);
if (!planes.EsCorrecto)
{
    foreach (var e in planes.Errores) Console.Error.WriteLine(e.TextoOperador());
    BaseDatosAccess.TerminarProceso(1);
    return 1;
}
var md = new StringBuilder($"# Importación de ContaSol ({(simular ? "simulación" : "importación")}, {inicio:yyyy-MM-dd HH:mm})\n\n");
IReadOnlyList<(int Ejercicio, string Estado, string Texto, IReadOnlyList<Incidencia> Incidencias)> filas;
if (simular)
    filas = planes.Valor.Select(p => (p.Ejercicio, "SIMULADO",
        $"{p.Ejercicio}: {p.Cuentas.Count} cuentas, {p.Asientos.Count} asientos, {p.Apuntes} apuntes, {p.Registros.Count} registros de IVA, {p.Terceros.Count} terceros.",
        p.Incidencias)).ToList();
else
    filas = (await importador.ImportarAsync(planes.Valor, new Actor(Environment.UserName), progreso, cancelar.Token))
        .Select(r => (r.Ejercicio, r.Estado, r.Texto, r.Incidencias)).ToList();

foreach (var f in filas)
{
    var porCodigo = f.Incidencias.GroupBy(i => (i.Severidad, i.Codigo)).OrderByDescending(g => g.Key.Severidad).ThenBy(g => g.Key.Codigo);
    Console.WriteLine($"{f.Estado} · {f.Texto}");
    md.Append($"## {f.Ejercicio} · {f.Estado}\n\n{f.Texto}\n\n| Gravedad | Código | Número |\n|---|---|---|\n");
    foreach (var g in porCodigo)
    {
        Console.WriteLine($"    {g.Key.Severidad,-11} {g.Key.Codigo,-40} {g.Count(),6}");
        md.Append($"| {g.Key.Severidad} | {g.Key.Codigo} | {g.Count()} |\n");
    }
    md.Append("\n### Bloqueantes y advertencias\n\n");
    foreach (var i in f.Incidencias.Where(i => i.Severidad != Severidad.Informativa).OrderByDescending(i => i.Severidad).Take(300))
        md.Append($"- {i.Severidad} · {i.Tabla} {i.Clave} · {i.Explicacion.Replace('\n', ' ')}\n");
    md.Append('\n');
}
Console.WriteLine($"Tiempo: {DateTime.Now - inicio:hh\\:mm\\:ss}");
if (informe is not null) await File.WriteAllTextAsync(informe, md.ToString());
var codigo = filas.Any(f => f.Estado is EstadosImportacion.Fallida) ? 1 : 0;
BaseDatosAccess.TerminarProceso(codigo);
return codigo;
