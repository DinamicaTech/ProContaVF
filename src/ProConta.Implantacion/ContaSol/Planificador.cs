using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Importes;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.ContaSol;

/// <summary>Severidad de una incidencia (protocolo de importación): una bloqueante impide dar por buena la importación.</summary>
public enum Severidad { Informativa, Advertencia, Bloqueante }

/// <summary>Incidencia de la importación, con la tabla, la clave y el campo de origen y una explicación para el operador.</summary>
public sealed record Incidencia(string Fase, string Tabla, string? Clave, string? Campo, string Codigo, Severidad Severidad, string Explicacion, string? ValorOriginal = null);

public sealed record CentroPlan(string Clave, string? ClavePadre, string Codigo, string Nombre, string? Descripcion);

public sealed record CuentaPlan(string Codigo, string Nombre, string? Descripcion, bool EsImputable, string? ClaveCentro);

public sealed record ApuntePlan(string Cuenta, decimal Debe, decimal Haber, string? Descripcion, string? Documento, string? ClaveCentro);

/// <summary>Asiento de ContaSol ya transformado. <see cref="Clave"/> es «diario:número» (DIAAPU:ASIAPU), la clave de su mapa.</summary>
public sealed record AsientoPlan(string Clave, int Numero, DateTime Fecha, TipoAsiento Tipo, string Descripcion, string? Notas, IReadOnlyList<ApuntePlan> Apuntes)
{
    public decimal TotalDebe => Apuntes.Sum(a => a.Debe);
    public decimal TotalHaber => Apuntes.Sum(a => a.Haber);
}

public sealed record TramoPlan(string Calificacion, decimal Base, decimal Tipo, decimal Cuota, decimal Autorrepercutida, decimal PorcentajeDeducible, decimal CuotaDeducible,
    decimal TipoRecargo, decimal CuotaRecargo);

/// <summary>Registro del libro de IVA ya transformado. <see cref="Clave"/> es CODIVS o CODIVR.</summary>
public sealed record RegistroIvaPlan(
    bool Soportado,
    string Clave,
    string? ClaveAsiento,
    DateTime FechaExpedicion,
    DateTime FechaOperacion,
    DateTime FechaRegistro,
    int Trimestre,
    string? NumeroFactura,
    string? Nombre,
    string? Nif,
    string Pais,
    int TipoOperacionContaSol,
    string ClaveRegimen,
    string CuentaTercero,
    decimal Total,
    decimal Base,
    decimal Cuota,
    decimal Autorrepercutida,
    decimal Deducible,
    decimal Recargo,
    bool Incluido347,
    string? Descripcion,
    IReadOnlyList<TramoPlan> Tramos);

/// <summary>Cliente o proveedor de ContaSol con actividad en el ejercicio, que pasa a tercero con su rol y su cuenta.</summary>
public sealed record TerceroPlan(TerceroContaSol Origen, string Nif, ClaseCuentaRol Clase);

/// <summary>Saldo de una cuenta que no coincide con el de F_SAL.</summary>
public sealed record DiferenciaSaldo(string Cuenta, decimal Calculado, decimal Control);

/// <summary>Todo lo que se importará de un ejercicio, sin tocar ninguna BD.</summary>
public sealed record PlanEjercicio(
    OrigenContaSol Origen,
    IReadOnlyList<CentroPlan> Centros,
    IReadOnlyList<CuentaPlan> Cuentas,
    IReadOnlyList<AsientoPlan> Asientos,
    IReadOnlyList<RegistroIvaPlan> Registros,
    IReadOnlyList<TerceroPlan> Terceros,
    IReadOnlyList<Incidencia> Incidencias)
{
    public EmpresaContaSol Empresa => Origen.Empresa;
    public int Ejercicio => Origen.Empresa.Ejercicio;
    public int Apuntes => Asientos.Sum(a => a.Apuntes.Count);
    public bool TieneBloqueantes => Incidencias.Any(i => i.Severidad == Severidad.Bloqueante);
}

/// <summary>
/// Transforma un ejercicio leído de ContaSol en lo que se importará (fase de inspección y simulación del protocolo): no
/// abre ninguna BD, así que se prueba sin Access. Reglas acordadas con el propietario el 2026-11-10:
/// importe = IMEAPU, en positivo al lado contrario si es negativo; las líneas a cero no se importan; F_OAS da las notas del
/// asiento; un registro de IVA se enlaza con su asiento por CIVAPU y TIVAPU; los clientes y proveedores solo pasan a
/// terceros si su subcuenta tiene apuntes en el ejercicio (sin contar apertura ni cierre) y el ejercicio es de los últimos
/// cinco.
/// </summary>
public static class PlanificadorContaSol
{
    public const string FaseInspeccion = "inspeccion";
    public const int LongitudDescripcion = 255;

    /// <param name="primerEjercicioConTerceros">Primer ejercicio cuyos clientes y proveedores pasan a terceros (los últimos cinco).</param>
    public static PlanEjercicio Planificar(OrigenContaSol origen, int primerEjercicioConTerceros)
    {
        var incidencias = new List<Incidencia>();
        void Anotar(string tabla, string? clave, string? campo, string codigo, Severidad sev, string texto, string? valor = null) =>
            incidencias.Add(new Incidencia(FaseInspeccion, tabla, clave, campo, codigo, sev, texto, valor));
        var ejercicio = origen.Empresa.Ejercicio;

        // ---------- IVA del 0 %: terminación 000 y sin las antiguas del 16 % (organizacion/sociedades-ejercicios 2026-11-13 20:55) ----------
        var (renombrado, incidenciasIvaCero) = IvaCeroContaSol.Aplicar(origen);
        origen = renombrado;
        incidencias.AddRange(incidenciasIvaCero);

        // ---------- centros de coste: F_DEP (raíces) y F_CDP (hijos) ----------
        var centros = new Dictionary<string, CentroPlan>(StringComparer.Ordinal);
        foreach (var d in origen.Departamentos.Where(d => d.Codigo != 0).OrderBy(d => d.Codigo))
            centros.TryAdd(Clave(d.Codigo), new CentroPlan(Clave(d.Codigo), null, d.Codigo.ToString(CultureInfo.InvariantCulture), Recortar(Nombre(d.Nombre, $"Departamento {d.Codigo}"), 150), d.Observaciones));
        foreach (var c in origen.Subdepartamentos.Where(c => c.Codigo != 0).OrderBy(c => c.Departamento).ThenBy(c => c.Codigo))
        {
            if (!centros.ContainsKey(Clave(c.Departamento)))
            {
                Anotar("F_CDP", Clave(c.Departamento, c.Codigo), "DEPCDP", "ContaSol.DepartamentoDesconocido", Severidad.Advertencia,
                    $"El subdepartamento {c.Codigo} es del departamento {c.Departamento}, que no está en F_DEP: lo creo con el nombre «Departamento {c.Departamento}».");
                centros[Clave(c.Departamento)] = new CentroPlan(Clave(c.Departamento), null, c.Departamento.ToString(CultureInfo.InvariantCulture), $"Departamento {c.Departamento}", null);
            }
            centros.TryAdd(Clave(c.Departamento, c.Codigo), new CentroPlan(Clave(c.Departamento, c.Codigo), Clave(c.Departamento), c.Codigo.ToString(CultureInfo.InvariantCulture),
                Recortar(Nombre(c.Nombre, $"Subdepartamento {c.Codigo}"), 150), c.Observaciones));
        }
        string? Centro(int dep, int sub, string tabla, string? clave)
        {
            if (dep == 0 && sub == 0) return null;
            var k = sub == 0 ? Clave(dep) : Clave(dep, sub);
            if (centros.ContainsKey(k)) return k;
            Anotar(tabla, clave, "DEP/SUB", "ContaSol.CentroDesconocido", Severidad.Advertencia,
                $"El centro de coste {k} no está en F_DEP ni F_CDP: lo creo para no perder la imputación.", k);
            if (!centros.ContainsKey(Clave(dep)))
                centros[Clave(dep)] = new CentroPlan(Clave(dep), null, dep.ToString(CultureInfo.InvariantCulture), $"Departamento {dep}", null);
            if (sub != 0) centros[k] = new CentroPlan(k, Clave(dep), sub.ToString(CultureInfo.InvariantCulture), $"Subdepartamento {sub}", null);
            return k;
        }

        // ---------- plan contable: F_MAE ----------
        var cuentas = new Dictionary<string, CuentaContaSol>(StringComparer.Ordinal);
        foreach (var c in origen.Cuentas)
        {
            var codigo = (c.Codigo ?? "").Trim();
            if (codigo.Length == 0)
            {
                Anotar("F_MAE", null, "CODMAE", "ContaSol.CuentaSinCodigo", Severidad.Informativa, $"Hay una cuenta sin código («{c.Nombre}»): no se importa.", c.Nombre);
                continue;
            }
            if (!codigo.All(char.IsAsciiDigit) || codigo.Length > 30)
            {
                Anotar("F_MAE", codigo, "CODMAE", "ContaSol.CuentaCodigoNoValido", Severidad.Bloqueante,
                    $"El código de cuenta «{codigo}» no es válido: solo puede tener cifras (hasta 30). Corrígelo en ContaSol y vuelve a importar.", codigo);
                continue;
            }
            if (!cuentas.TryAdd(codigo, c with { Codigo = codigo }))
                Anotar("F_MAE", codigo, "CODMAE", "ContaSol.CuentaRepetida", Severidad.Advertencia, $"La cuenta {codigo} está dos veces en F_MAE: me quedo con la primera.");
        }

        // ---------- asientos: F_APU agrupado por diario y número, con F_OAS ----------
        var textos = origen.Textos.GroupBy(t => Clave(t.Diario, t.Asiento, ':'))
            .ToDictionary(g => g.Key, g => string.Join(Environment.NewLine, g.Select(t => t.Texto.Trim()).Where(t => t.Length > 0)), StringComparer.Ordinal);
        var usadas = new HashSet<string>(StringComparer.Ordinal);
        var asientos = new List<AsientoPlan>();
        var actividad = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grupo in origen.Apuntes.GroupBy(a => (a.Diario, a.Asiento)).OrderBy(g => g.Key.Diario).ThenBy(g => g.Key.Asiento))
        {
            var clave = Clave(grupo.Key.Diario, grupo.Key.Asiento, ':');
            var lineas = grupo.OrderBy(a => a.Orden).ToList();
            var fechas = lineas.Select(a => a.Fecha).Distinct().ToList();
            var fecha = fechas.Max();
            if (fecha.Year != ejercicio)
            {
                Anotar("F_APU", clave, "FECAPU", "ContaSol.FechaFueraDeEjercicio", Severidad.Bloqueante,
                    $"El asiento {grupo.Key.Asiento} tiene fecha {(fecha == DateTime.MinValue ? "vacía" : fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))}, que no es del ejercicio {ejercicio}: no se importa. Corrige la fecha en ContaSol y vuelve a importar.");
                continue;
            }
            if (fechas.Count > 1)
                Anotar("F_APU", clave, "FECAPU", "ContaSol.VariasFechas", Severidad.Advertencia,
                    $"Las líneas del asiento {grupo.Key.Asiento} tienen fechas distintas: lo importo con la más reciente, {fecha:dd/MM/yyyy}.");

            var apuntes = new List<ApuntePlan>();
            var valido = true;
            foreach (var a in lineas)
            {
                var cuenta = a.Cuenta.Trim();
                if (cuenta.Length == 0)
                {
                    Anotar("F_APU", $"{clave}:{a.Orden}", "CUEAPU", "ContaSol.ApunteSinCuenta", Severidad.Bloqueante,
                        $"La línea {a.Orden} del asiento {grupo.Key.Asiento} no tiene cuenta: el asiento no se importa. Corrígelo en ContaSol.");
                    valido = false;
                    continue;
                }
                var importe = Importe.RedondearValor(a.Importe);
                if (importe == 0m)
                {
                    Anotar("F_APU", $"{clave}:{a.Orden}", "IMEAPU", "ContaSol.ApunteACero", Severidad.Informativa,
                        $"La línea {a.Orden} del asiento {grupo.Key.Asiento} ({cuenta}) tiene importe cero: no se importa.");
                    continue;
                }
                var alDebe = importe > 0m ? a.AlDebe : !a.AlDebe;
                if (importe < 0m)
                    Anotar("F_APU", $"{clave}:{a.Orden}", "IMEAPU", "ContaSol.ImporteNegativo", Severidad.Informativa,
                        $"La línea {a.Orden} del asiento {grupo.Key.Asiento} ({cuenta}) tiene un importe negativo: la paso en positivo al {(alDebe ? "Debe" : "Haber")}.",
                        a.Importe.ToString(CultureInfo.InvariantCulture));
                usadas.Add(cuenta);
                apuntes.Add(new ApuntePlan(cuenta, alDebe ? Math.Abs(importe) : 0m, alDebe ? 0m : Math.Abs(importe), Vacio(Recortar(a.Concepto, 255)),
                    Vacio(Recortar(a.Documento, 100)), Centro(a.Departamento, a.Subdepartamento, "F_APU", $"{clave}:{a.Orden}")));
            }
            if (!valido) continue;
            var descripcion = Recortar(lineas.Select(a => a.Concepto?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? $"Asiento {grupo.Key.Asiento} de ContaSol", LongitudDescripcion)!;
            var plan = new AsientoPlan(clave, grupo.Key.Asiento, fecha, TipoDe(grupo.Key.Asiento, descripcion), descripcion, textos.GetValueOrDefault(clave) is { Length: > 0 } n ? n : null, apuntes);
            if (apuntes.Count < 2 || plan.TotalDebe != plan.TotalHaber)
            {
                Anotar("F_APU", clave, "IMEAPU", "ContaSol.AsientoDescuadrado", Severidad.Bloqueante,
                    $"El asiento {grupo.Key.Asiento} no cuadra en euros (Debe {plan.TotalDebe:0.00}, Haber {plan.TotalHaber:0.00}, {apuntes.Count} líneas con importe): no se importa. Corrígelo en ContaSol.");
                continue;
            }
            if (plan.Tipo is not (TipoAsiento.Apertura or TipoAsiento.Cierre))
                foreach (var a in apuntes) actividad.Add(a.Cuenta);
            asientos.Add(plan);
        }
        var conAsiento = asientos.Select(a => a.Clave).ToHashSet(StringComparer.Ordinal);
        foreach (var texto in textos.Keys.Where(k => !conAsiento.Contains(k)).Take(50))
            Anotar("F_OAS", texto, "NASOAS", "ContaSol.TextoSinAsiento", Severidad.Informativa, $"F_OAS tiene un texto para el asiento {texto}, que no existe en F_APU: no se importa.");

        // Cuentas con apuntes que no están en F_MAE: se crean para no perder el apunte.
        foreach (var codigo in usadas.Where(c => !cuentas.ContainsKey(c)).Order(StringComparer.Ordinal))
        {
            Anotar("F_APU", codigo, "CUEAPU", "ContaSol.CuentaNoEnPlan", Severidad.Advertencia,
                $"La cuenta {codigo} tiene apuntes pero no está en el plan contable (F_MAE) de {ejercicio}: la creo con un nombre provisional.");
            cuentas[codigo] = new CuentaContaSol(codigo, $"Cuenta {codigo} (no estaba en el plan de ContaSol)", null, 0, 0);
        }
        var codigos = cuentas.Keys.Order(StringComparer.Ordinal).ToList();
        var planCuentas = codigos.Select(c =>
        {
            var o = cuentas[c];
            var tieneHijas = codigos.Any(h => h.Length > c.Length && h.StartsWith(c, StringComparison.Ordinal));
            return new CuentaPlan(c, Recortar(Nombre(o.Nombre, $"Cuenta {c}"), 150)!, Vacio(Recortar(o.Descripcion, 255)), !tieneHijas || usadas.Contains(c),
                Centro(o.Departamento, o.Subdepartamento, "F_MAE", c));
        }).ToList();

        // ---------- libros de IVA: F_IVS y F_IVR ----------
        var enlaces = origen.Apuntes.Where(a => a.CodigoIva != 0 && a.TipoIva is "S" or "R")
            .GroupBy(a => (Soportado: a.TipoIva == "S", a.CodigoIva))
            .ToDictionary(g => g.Key, g => g.Select(a => Clave(a.Diario, a.Asiento, ':')).Distinct().ToList());
        var fechaAsiento = asientos.ToDictionary(a => a.Clave, a => a.Fecha, StringComparer.Ordinal);
        var registros = new List<RegistroIvaPlan>();
        foreach (var r in origen.RegistrosIva.OrderBy(r => r.Soportado ? 0 : 1).ThenBy(r => r.Codigo))
        {
            var tabla = r.Soportado ? "F_IVS" : "F_IVR";
            var clave = r.Codigo.ToString(CultureInfo.InvariantCulture);
            string? claveAsiento = null;
            if (enlaces.TryGetValue((r.Soportado, r.Codigo), out var candidatos))
            {
                claveAsiento = candidatos[0];
                if (candidatos.Count > 1)
                    Anotar(tabla, clave, "CIVAPU", "ContaSol.IvaVariosAsientos", Severidad.Advertencia,
                        $"El registro de IVA {clave} aparece en varios asientos ({string.Join(", ", candidatos)}): lo enlazo con el primero.");
                if (!fechaAsiento.ContainsKey(claveAsiento))
                {
                    Anotar(tabla, clave, "CIVAPU", "ContaSol.IvaAsientoNoImportado", Severidad.Advertencia,
                        $"El asiento {claveAsiento} del registro de IVA {clave} no se importa (ver su incidencia): el registro queda sin asiento.");
                    claveAsiento = null;
                }
            }
            else
                Anotar(tabla, clave, "CIVAPU", "ContaSol.IvaSinAsiento", Severidad.Advertencia,
                    $"Ningún asiento de F_APU apunta al registro de IVA {clave}: lo importo sin asiento.");

            // Fecha de registro: la de F_IVS/F_IVR; si no es del ejercicio, la de la factura; si tampoco, la de su asiento.
            var fechaRegistro = r.FechaRegistro.Year == ejercicio ? r.FechaRegistro
                : r.FechaFactura is { } ff && ff.Year == ejercicio ? ff
                : claveAsiento is not null ? fechaAsiento[claveAsiento] : DateTime.MinValue;
            if (fechaRegistro == DateTime.MinValue)
            {
                Anotar(tabla, clave, "FEC", "ContaSol.IvaFueraDeEjercicio", Severidad.Bloqueante,
                    $"El registro {clave} del libro de IVA {(r.Soportado ? "soportado" : "repercutido")} no tiene fecha del ejercicio {ejercicio} ni asiento: no se importa. Corrige la fecha en ContaSol.",
                    r.FechaRegistro == DateTime.MinValue ? null : r.FechaRegistro.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                continue;
            }
            if (r.FechaRegistro.Year != ejercicio)
                Anotar(tabla, clave, "FEC", "ContaSol.IvaFechaCorregida", Severidad.Advertencia,
                    $"El registro {clave} del libro de IVA tiene fecha {(r.FechaRegistro == DateTime.MinValue ? "vacía" : r.FechaRegistro.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))}, que no es de {ejercicio}: lo registro el {fechaRegistro:dd/MM/yyyy} (fecha de la factura o de su asiento).");

            var tramos = new List<TramoPlan>();
            var intracomunitaria = r.Soportado && r.TipoOperacion == 1;
            if (r.BaseExenta != 0m)
                tramos.Add(new TramoPlan(r.Soportado ? "N1" : "E1", r.BaseExenta, 0m, 0m, 0m, r.PorcentajeDeducible, 0m, 0m, 0m));
            foreach (var t in r.Tramos.Where(t => t.Base != 0m || t.Cuota != 0m))
            {
                var deducible = r.Soportado ? Importe.RedondearValor(t.Cuota * r.PorcentajeDeducible / 100m) : 0m;
                tramos.Add(new TramoPlan(t.Tipo == 0m && t.Cuota == 0m ? (r.Soportado ? "N1" : "E1") : intracomunitaria ? "S2" : "S1", t.Base, t.Tipo, t.Cuota,
                    intracomunitaria ? t.Cuota : 0m, r.PorcentajeDeducible, deducible, t.TipoRecargo, t.CuotaRecargo));
            }
            if (tramos.Count == 0)
                Anotar(tabla, clave, "BA0..BA3", "ContaSol.IvaSinBases", Severidad.Advertencia, $"El registro de IVA {clave} no tiene ninguna base: lo importo con total {r.Total:0.00} y sin desglose.");
            var nif = Vacio(r.Nif?.Trim().ToUpperInvariant());
            var pais = nif is { Length: > 2 } && char.IsAsciiLetter(nif[0]) && char.IsAsciiLetter(nif[1]) && r.TipoOperacion != 0 && !nif.StartsWith("ES", StringComparison.Ordinal)
                ? nif[..2] : "ES";
            var fechaExpedicion = r.FechaFactura ?? fechaRegistro;
            registros.Add(new RegistroIvaPlan(r.Soportado, clave, claveAsiento, fechaExpedicion, r.FechaOperacion ?? fechaExpedicion, fechaRegistro,
                (fechaRegistro.Month - 1) / 3 + 1, Vacio(Recortar(r.NumeroFactura, 100)), Vacio(Recortar(r.Nombre, 150)), Recortar(nif, 30), pais, r.TipoOperacion,
                r.Soportado ? r.TipoOperacion switch { 1 => "09", 2 => "13", _ => "01" } : "01", r.Cuenta.Trim(),
                r.Total, tramos.Sum(t => t.Base), tramos.Sum(t => t.Cuota), tramos.Sum(t => t.Autorrepercutida), tramos.Sum(t => t.CuotaDeducible), tramos.Sum(t => t.CuotaRecargo),
                r.Incluido347, Vacio(r.Observaciones), tramos));
        }

        // ---------- clientes y proveedores con actividad ----------
        var terceros = new List<TerceroPlan>();
        if (ejercicio >= primerEjercicioConTerceros)
            foreach (var t in origen.Terceros.Where(t => t.Codigo.Trim().Length > 0).OrderBy(t => t.EsCliente ? 0 : 1).ThenBy(t => t.Codigo, StringComparer.Ordinal))
            {
                var codigo = t.Codigo.Trim();
                if (!actividad.Contains(codigo)) continue;
                if (!cuentas.ContainsKey(codigo))
                {
                    Anotar(t.EsCliente ? "F_CLI" : "F_PRO", codigo, "COD", "ContaSol.TerceroSinCuenta", Severidad.Advertencia,
                        $"El {(t.EsCliente ? "cliente" : "proveedor")} {codigo} no tiene su subcuenta en el plan contable: no lo doy de alta como tercero.");
                    continue;
                }
                terceros.Add(new TerceroPlan(t with { Codigo = codigo }, NormalizarNif(t.Nif), ClaseDe(t.EsCliente, codigo)));
            }

        // ---------- control: saldo anual por cuenta contra F_SAL ----------
        foreach (var d in CompararSaldos(SaldosDe(asientos), origen.SaldosControl))
            Anotar("F_SAL", d.Cuenta, "ED/EH", "ContaSol.SaldoDistintoEnOrigen", Severidad.Advertencia,
                $"En ContaSol, el saldo de la cuenta {d.Cuenta} según sus apuntes ({d.Calculado:0.00}) no coincide con el de F_SAL ({d.Control:0.00}). Es una diferencia del propio ContaSol; la importación sigue los apuntes.");

        return new PlanEjercicio(origen, [.. centros.Values], planCuentas, asientos, registros, terceros, incidencias);
    }

    /// <summary>Saldo (Debe − Haber) de cada cuenta en los asientos.</summary>
    public static Dictionary<string, decimal> SaldosDe(IEnumerable<AsientoPlan> asientos)
    {
        var saldos = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var a in asientos.SelectMany(x => x.Apuntes))
            saldos[a.Cuenta] = saldos.GetValueOrDefault(a.Cuenta) + a.Debe - a.Haber;
        return saldos;
    }

    /// <summary>
    /// Cuentas cuyo saldo anual no coincide con el de control (F_SAL). Se comparan las cuentas de último nivel: F_SAL también
    /// acumula las cuentas de grupo, que no tienen apuntes.
    /// </summary>
    public static IReadOnlyList<DiferenciaSaldo> CompararSaldos(IReadOnlyDictionary<string, decimal> calculado, IReadOnlyDictionary<string, decimal> control)
    {
        var codigos = calculado.Keys.Concat(control.Keys).Distinct(StringComparer.Ordinal).ToList();
        var hojas = codigos.Where(c => !codigos.Any(o => o.Length > c.Length && o.StartsWith(c, StringComparison.Ordinal)) || calculado.ContainsKey(c));
        return hojas
            .Select(c => new DiferenciaSaldo(c, calculado.GetValueOrDefault(c), control.GetValueOrDefault(c)))
            .Where(d => d.Calculado != d.Control)
            .OrderBy(d => d.Cuenta, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Clase de la cuenta del rol: cliente (F_CLI), acreedor (410, 411…) o proveedor (400, 401…).</summary>
    public static ClaseCuentaRol ClaseDe(bool esCliente, string codigo) =>
        esCliente ? ClaseCuentaRol.Cliente : codigo.StartsWith("41", StringComparison.Ordinal) ? ClaseCuentaRol.Acreedor : ClaseCuentaRol.Proveedor;

    /// <summary>Tipo del asiento: apertura (el 1 o «Asiento de apertura»), cierre (el 20000 o «Cierre del ejercicio») y regularización («Regularización de cuentas»).</summary>
    public static TipoAsiento TipoDe(int numero, string descripcion)
    {
        var texto = descripcion.ToUpperInvariant();
        if (numero == 20000 || texto.Contains("CIERRE DEL EJERCICIO", StringComparison.Ordinal)) return TipoAsiento.Cierre;
        if ((numero == 1 && texto.Contains("APERTURA", StringComparison.Ordinal)) || texto.StartsWith("ASIENTO DE APERTURA", StringComparison.Ordinal)) return TipoAsiento.Apertura;
        if (texto.StartsWith("REGULARIZACIÓN DE CUENTAS", StringComparison.Ordinal) || texto.StartsWith("REGULARIZACION DE CUENTAS", StringComparison.Ordinal)) return TipoAsiento.Regularizacion;
        return TipoAsiento.Ordinario;
    }

    /// <summary>NIF sin espacios, guiones ni puntos, en mayúsculas (vacío si no hay).</summary>
    public static string NormalizarNif(string? nif) =>
        new string((nif ?? "").Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '.').ToArray()).ToUpperInvariant();

    /// <summary>
    /// Dos NIF del mismo titular: iguales, o uno vacío, o uno es el otro con la letra de control añadida o con prefijo de país
    /// (una corrección, no un cambio de titular).
    /// </summary>
    public static bool MismoTitular(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0 || a == b) return true;
        static string Base(string n) => n.Length > 2 && n.StartsWith("ES", StringComparison.Ordinal) ? n[2..] : n;
        var (x, y) = (Base(a), Base(b));
        if (x == y) return true;
        var (corto, largo) = x.Length <= y.Length ? (x, y) : (y, x);
        return largo.Length == corto.Length + 1 && (largo.StartsWith(corto, StringComparison.Ordinal) || largo.EndsWith(corto, StringComparison.Ordinal));
    }

    /// <summary>
    /// Datos de contacto de la ficha de un tercero de ContaSol (terceros/contactos): cada email del campo de email (separados por
    /// «;», «,» o espacios; con «@» que no esté al principio ni al final; sin repetir, sin distinguir mayúsculas) y el teléfono si
    /// tiene 6 cifras o más. Son las mismas reglas que la migración 20261101_1300_CONTACTO_FICHA_ANTIGUA, para que un tercero
    /// importado ya cumpla su comprobación y no deje la BD como «migración parcial».
    /// </summary>
    public static IReadOnlyList<(TipoDatoContacto Tipo, string Valor)> ContactosDeFicha(string? email, string? telefono)
    {
        var datos = new List<(TipoDatoContacto, string)>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in (email ?? "").Replace(";", " ").Replace(",", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var v = e.Trim();
            var arroba = v.IndexOf('@');
            if (arroba <= 0 || arroba == v.Length - 1 || v.Length > 254 || !vistos.Add(v.ToLowerInvariant())) continue;
            datos.Add((TipoDatoContacto.Email, v));
        }
        var tel = telefono?.Trim();
        if (tel is { Length: > 0 and <= 254 } && tel.Count(char.IsAsciiDigit) >= 6) datos.Add((TipoDatoContacto.Telefono, tel));
        return datos;
    }

    private static string Clave(int dep) => dep.ToString(CultureInfo.InvariantCulture);
    private static string Clave(int a, int b, char separador = '/') => $"{a.ToString(CultureInfo.InvariantCulture)}{separador}{b.ToString(CultureInfo.InvariantCulture)}";
    private static string Nombre(string? nombre, string predeterminado) => string.IsNullOrWhiteSpace(nombre) ? predeterminado : nombre.Trim();
    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(texto))]
    internal static string? Recortar(string? texto, int maximo) => texto is null ? null : texto.Trim() is var t && t.Length <= maximo ? t : t[..maximo];
}
