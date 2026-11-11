using System.Text;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia.Access;
using ProConta.Facturacion.Conceptos;
using ProConta.Implantacion.ContaSol;
using ProConta.Implantacion.ERP;
using ProConta.Nucleo.Auditoria;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

// Migración del ERP (docs/implantacion/migracion-erp.md).
//   MigradorERP <bd destino> <bd ERP> --direcciones <csv> [--simular] [--informe <md>] [--raiz <carpeta raíz de documentos>]

Console.OutputEncoding = Encoding.UTF8;
string? Opcion(string nombre)
{
    var i = Array.FindIndex(args, a => a.Equals(nombre, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var conValor = new[] { "--direcciones", "--informe", "--raiz" };
var posicionales = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !conValor.Contains(args[i - 1], StringComparer.OrdinalIgnoreCase))).ToList();
var simular = args.Contains("--simular", StringComparer.OrdinalIgnoreCase);
var informe = Opcion("--informe");
var direcciones = Opcion("--direcciones");
var raiz = Opcion("--raiz") ?? @"D:\SynologyBox";
if (posicionales.Count < 2 || direcciones is null)
{
    Console.WriteLine("""
        Uso:
          MigradorERP <bd destino> <bd ERP> --direcciones <csv> [--simular] [--informe <md>] [--raiz <carpeta>]
              Migra el ERP (ProcontaEstructura.accdb, solo lectura) a la BD de ProContaVF, que ya debe tener ContaSol importado.
              --simular: lee y comprueba todo sin escribir en el destino (trabaja sobre una copia temporal que después borra).
              --informe: escribe el informe de la ejecución en un md.  --raiz: raíz de las rutas de los PDF (D:\SynologyBox).
        """);
    return 2;
}
var destino = Path.GetFullPath(posicionales[0]);
var inicio = DateTime.Now;
var progreso = new Progress<string>(Console.WriteLine);
Console.WriteLine($"{DateTime.Now:HH:mm:ss} Leyendo el ERP…");
var leido = await new LectorErp().LeerAsync(posicionales[1], direcciones);
if (!leido.EsCorrecto)
{
    foreach (var e in leido.Errores) Console.Error.WriteLine(e.TextoOperador());
    BaseDatosAccess.TerminarProceso(1);
    return 1;
}
Console.WriteLine($"{DateTime.Now:HH:mm:ss} ERP leído: {string.Join(", ", leido.Valor.Tablas.Select(t => $"{t.Key} {t.Value.Count}"))}.");

var trabajo = destino;
if (simular)
{
    trabajo = Path.Combine(Path.GetTempPath(), $"MigradorERP-simulacion-{Guid.NewGuid():N}.accdb");
    File.Copy(destino, trabajo);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss} Simulación: trabajo sobre una copia temporal; {destino} no se modifica.");
}
ResultadoMigracionErp resultado;
try
{
    var bd = new BaseDatosAccess(trabajo);
    var cuentas = new CatalogoCuentasAccess(bd);
    var migrador = new MigradorErp(bd, new RepositorioTercerosAccess(bd), new ServicioRoles(bd, cuentas), cuentas, new CatalogoConceptos(bd, cuentas));
    resultado = await migrador.MigrarAsync(leido.Valor, new OpcionesMigracionErp(RaizDocumentos: raiz), new Actor("MigradorERP", true), progreso);
}
finally
{
    if (simular) try { File.Delete(trabajo); } catch (IOException) { }
}

var md = InformeErp.Generar(resultado, leido.Valor, destino, simular);
foreach (var t in resultado.Tablas) Console.WriteLine($"  {t.Fase,-14} {t.Tabla,-50} leídas {t.Leidas,6}  cargadas {t.Cargadas,6}  excluidas {t.Excluidas,5}");
foreach (var g in resultado.Incidencias.GroupBy(i => (i.Severidad, i.Codigo)).OrderByDescending(g => g.Key.Severidad).ThenBy(g => g.Key.Codigo))
    Console.WriteLine($"    {g.Key.Severidad,-11} {g.Key.Codigo,-40} {g.Count(),6}");
foreach (var i in resultado.Incidencias.Where(i => i.Severidad == Severidad.Bloqueante).Take(20)) Console.WriteLine($"  BLOQUEANTE {i.Fase} {i.Tabla} {i.Clave}: {i.Explicacion}");
Console.WriteLine($"{resultado.Estado} · Tiempo: {DateTime.Now - inicio:hh\\:mm\\:ss}");
if (informe is not null) await File.WriteAllTextAsync(informe, md, new UTF8Encoding(true));
var codigo = resultado.Estado is EstadosImportacion.Fallida ? 1 : 0;
BaseDatosAccess.TerminarProceso(codigo);
return codigo;
