using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Facturacion.Mantenimiento;

/// <summary>
/// Proveedores de facturación para la pantalla genérica: Series de facturación (por ejercicio, con su servicio),
/// Conceptos (por empresa, con Duplicar y Ver uso), Tipos de concepto y Tipos de IVA, y en solo consulta Tipos y
/// Estados de factura.
/// </summary>
public static class ProveedoresFacturacion
{
    public static IEnumerable<IProveedorMantenimiento> Todos(IBaseDatos bd, ICatalogoCuentas cuentas) =>
    [
        new ProveedorSeries(new ServicioSeries(bd)),
        new ProveedorConceptos(new CatalogoConceptos(bd, cuentas)),
        TiposConcepto(bd),
        TiposIva(bd),
        TiposFactura(bd),
        EstadosFactura(bd),
    ];

    private const string SoloConsultaFiscal = "Son códigos fiscales de la AEAT que usa el programa: se consultan, no se cambian.";

    public static ProveedorTablaAccess TiposConcepto(IBaseDatos bd) => new(bd, "tiposconcepto", "Tipos de concepto", "Catálogos",
        "Clasificación de los conceptos facturables (alquiler, suministros, servicios…).", AmbitoMantenimiento.Compartido, "tipo de concepto",
        "TiposConcepto", "IdTipoConcepto", ClaveNueva.Siguiente,
        [
            new("IdTipoConcepto", "Código", TipoCampo.Entero) { Ayuda = "Número que identifica el tipo de concepto. Se asigna al crearlo.", SoloLectura = true, Ancho = "xs" },
            new("Descripcion", "Descripción") { Ayuda = "Nombre del tipo de concepto (por ejemplo «Alquiler»), el que se elige en cada concepto.", Obligatorio = true, Longitud = 80, Ancho = "l" },
            new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Solo los activos se ofrecen al crear o cambiar conceptos." },
            new("Notas", "Notas", TipoCampo.TextoLargo) { Ayuda = "Comentarios internos sobre el tipo de concepto; no salen en ninguna factura.", Ancho = "f" },
        ],
        [new("IdTipoConcepto", "Código", 70, Derecha: true), new("Descripcion", "Descripción"), new("Activo", "Activo", 60)])
    {
        Orden = "Descripcion",
        Alias = ["clasificacion", "tipo"],
        Usos = [new("Conceptos", "IdTipoConcepto", "conceptos")],
        Titular = r => r.EsNuevo ? "Nuevo tipo de concepto" : r.Texto("Descripcion") ?? "Tipo sin descripción",
    };

    public static ProveedorTablaAccess TiposIva(IBaseDatos bd) => new(bd, "iva", "Tipos de IVA", "Catálogos",
        "Porcentajes de IVA que se ofrecen al facturar.", AmbitoMantenimiento.Compartido, "tipo de IVA",
        "TiposDeIVA", "Id", ClaveNueva.Autonumerica,
        [
            new("Valor", "Porcentaje", TipoCampo.Porcentaje) { Obligatorio = true, Ancho = "xs", Ayuda = "Por ejemplo 21 para el 21 %.", Sinonimos = ["porcentaje", "tipo"] },
            new("Descripcion", "Descripción") { Ayuda = "Nombre del tipo de IVA que se ve en los desplegables (por ejemplo «General»).", Obligatorio = true, Longitud = 100, Ancho = "l" },
            new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Solo los activos se ofrecen en conceptos y líneas." },
        ],
        [new("Valor", "%", 60, Derecha: true), new("Descripcion", "Descripción"), new("Activo", "Activo", 60)])
    {
        Orden = "Valor DESC",
        Alias = ["iva", "impuesto", "porcentaje"],
        Titular = r => r.EsNuevo ? "Nuevo tipo de IVA" : r.Texto("Descripcion") ?? $"{r.Decimal("Valor")} %",
        Subtitular = r => r.Decimal("Valor") is decimal v ? $"{v.ToString("0.##", CultureInfo.CurrentCulture)} %" : null,
        AntesDeGuardar = async (s, r, _) =>
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposDeIVA WHERE Valor = ? AND Id <> ?", (double)(r.Decimal("Valor") ?? 0m),
                r.Clave is null ? -1 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture) > 0
                ? new Error("TiposIva.Repetido", $"Ya existe el tipo de IVA del {r.Decimal("Valor"):0.##} %.", null, null, "Abre ese tipo en la lista.")
                : null,
    };

    public static ProveedorTablaAccess TiposFactura(IBaseDatos bd) => new(bd, "tiposfactura", "Tipos de factura", "Facturación",
        "Códigos de tipo de factura (F1…F3, R1…R5) y si son rectificativas.", AmbitoMantenimiento.Compartido, "tipo de factura",
        "TiposFactura", "TipoFactura", ClaveNueva.Escrita,
        [
            new("TipoFactura", "Código", TipoCampo.Codigo) { Ayuda = "Código de la AEAT del tipo de factura: F1 a F3 para las ordinarias, R1 a R5 para las rectificativas.", SoloAlta = true, Ancho = "xs" },
            new("Descripcion", "Descripción") { Ayuda = "Qué significa el código según la AEAT (factura completa, simplificada, rectificativa…).", Ancho = "f" },
            new("EsRectificativa", "Rectificativa", TipoCampo.Logico) { Ayuda = "Marcado en R1 a R5: el tipo corrige otra factura ya emitida." },
        ],
        [new("TipoFactura", "Código", 64), new("Descripcion", "Descripción"), new("EsRectificativa", "Rectif.", 60)])
    {
        Orden = "TipoFactura",
        SoloConsulta = SoloConsultaFiscal,
        Alias = ["f1", "f2", "r1", "r4", "r5", "rectificativa", "tipo factura"],
        Titular = r => $"{r.Texto("TipoFactura")} · {r.Texto("Descripcion")}",
    };

    public static ProveedorTablaAccess EstadosFactura(IBaseDatos bd) => new(bd, "estadosfactura", "Estados de factura", "Facturación",
        "Estados del ciclo de una factura: borrador, pendiente de validar, validada, rectificación pendiente y rectificada.", AmbitoMantenimiento.Compartido, "estado",
        "EstadosFactura", "IdEstado", ClaveNueva.Escrita,
        [
            new("IdEstado", "Código", TipoCampo.Entero) { Ayuda = "Número interno del estado de la factura.", SoloAlta = true, Ancho = "xs" },
            new("Nombre", "Nombre") { Ayuda = "Nombre del estado que se ve en la lista de facturas.", Ancho = "m" },
            new("Descripcion", "Descripción") { Ayuda = "Qué significa el estado y en qué momento del ciclo de la factura está.", Ancho = "f" },
            new("EsModificable", "Se puede modificar", TipoCampo.Logico) { Ayuda = "Marcado, una factura en este estado aún se puede editar; sin marcar, ya no se toca (solo se rectifica)." },
            new("EsTerminal", "Es final", TipoCampo.Logico) { Ayuda = "Marcado, el estado cierra el ciclo de la factura: no pasa a ningún otro." },
        ],
        [new("IdEstado", "Código", 64, Derecha: true), new("Nombre", "Nombre"), new("EsModificable", "Modif.", 60), new("EsTerminal", "Final", 60)])
    {
        Orden = "IdEstado",
        SoloConsulta = "Los estados los gobierna el programa (validar, rectificar): se consultan, no se cambian.",
        Alias = ["estado", "borrador", "validada", "rectificada"],
        Titular = r => r.Texto("Nombre") ?? $"Estado {r.Clave}",
    };
}

/// <summary>Series de facturación del ejercicio de trabajo, a través de <see cref="ServicioSeries"/>.</summary>
public sealed class ProveedorSeries(ServicioSeries servicio) : ProveedorBase
{
    public override string Clave => "series";
    public override string Titulo => "Series de facturación";
    public override string Categoria => "Facturación";
    public override string Finalidad => "Series, prefijos y rangos de numeración del ejercicio de trabajo.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Ejercicio;
    public override string NombreRegistro => "serie";
    public override IReadOnlyList<string> Sinonimos => ["serie", "prefijo", "numeracion", "rango", "contador"];
    public override IReadOnlyList<GrupoCampos> Grupos => [new("id", "Identificación"), new("num", "Numeración"), new("uso", "Uso")];
    public override IReadOnlyList<AccionMantenimiento> Acciones => [new("uso", "Ver uso", "Cuántas facturas y borradores tiene la serie", Modifica: false)];

    public override IReadOnlyList<CampoMantenimiento> Campos { get; } =
    [
        new("Descripcion", "Descripción") { Ayuda = "Nombre de la serie para reconocerla en las listas (por ejemplo «Facturas de alquiler»).", Grupo = "id", Obligatorio = true, Longitud = 100, Ancho = "l" },
        new("Prefijo", "Prefijo", TipoCampo.Codigo) { Grupo = "id", Obligatorio = true, Longitud = 20, Ancho = "s", Ayuda = "Va delante del número: F2026 da F20260001. No se cambia cuando la serie ya tiene facturas." },
        new("RangoDesde", "Desde el número", TipoCampo.Entero) { Ayuda = "Primer número que puede usar la serie. Cada factura validada toma el siguiente al último usado.", Grupo = "num", Obligatorio = true, Minimo = 1, Ancho = "s" },
        new("RangoHasta", "Hasta el número", TipoCampo.Entero) { Ayuda = "Último número que puede usar la serie. Al llegar a él, no se validan más facturas en ella.", Grupo = "num", Obligatorio = true, Minimo = 1, Ancho = "s" },
        new("UltimoNumero", "Último número emitido", TipoCampo.Entero) { Grupo = "num", SoloLectura = true, Ancho = "s", Ayuda = "Lo actualiza Facturación al numerar. Aquí solo se consulta." },
        new("EsRectificativa", "Serie de facturas rectificativas", TipoCampo.Logico) { Grupo = "uso", Ayuda = "Solo puede haber una por ejercicio." },
        new("EsPredeterminada", "Serie predeterminada del ejercicio", TipoCampo.Logico) { Grupo = "uso", Ayuda = "Se propone en las facturas nuevas. Marcarla quita la marca a la anterior." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas { get; } =
        [new("Prefijo", "Prefijo", 90), new("Descripcion", "Descripción"), new("UltimoNumero", "Último nº", 80, true), new("EsPredeterminada", "Predet.", 62), new("EsRectificativa", "Rectif.", 56)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nueva serie" : r.Texto("Descripcion") ?? "Serie sin descripción";
    public override string? SubtituloRegistro(RegistroMantenimiento r) => r.Texto("Prefijo") is string p ? $"Prefijo {p}" : null;

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        var r = new List<RegistroMantenimiento>();
        foreach (var s in await servicio.ListarAsync(c.Trabajo.IdSociedadEjercicio)) r.Add(await AsRegistro(s));
        return r;
    }

    private async Task<RegistroMantenimiento> AsRegistro(Serie s)
    {
        var r = new RegistroMantenimiento(s.IdSerie, new Dictionary<string, object?>
        {
            ["Descripcion"] = s.Descripcion, ["Prefijo"] = s.Prefijo.Trim(), ["RangoDesde"] = s.RangoDesde, ["RangoHasta"] = s.RangoHasta,
            ["UltimoNumero"] = s.UltimoNumero, ["EsRectificativa"] = s.EsRectificativa, ["EsPredeterminada"] = s.EsPredeterminada
        });
        var uso = await servicio.UsoAsync(s.IdSerie);
        if (uso.TieneFacturas)
        {
            var motivo = $"La serie ya tiene {uso.Numeradas + uso.Borradores} facturas: no se cambia.";
            r.Bloqueos["Prefijo"] = motivo;
            r.Bloqueos["EsRectificativa"] = motivo;
        }
        return r;
    }

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?>
        {
            ["Descripcion"] = $"Facturas {c.Trabajo.Ejercicio}", ["Prefijo"] = null, ["RangoDesde"] = 1, ["RangoHasta"] = 99999,
            ["UltimoNumero"] = null, ["EsRectificativa"] = false, ["EsPredeterminada"] = false
        }));

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var serie = new Serie(r.Clave is null ? 0 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture), c.Trabajo.IdSociedadEjercicio, r.Texto("Descripcion") ?? "",
            r.Texto("Prefijo") ?? "", r.Entero("RangoDesde") ?? 0, r.Entero("RangoHasta") ?? 0, r.Entero("UltimoNumero"), r.Logico("EsRectificativa"), r.Logico("EsPredeterminada"));
        var g = await servicio.GuardarAsync(serie);
        if (!g.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(g.Errores);
        var registro = await AsRegistro(g.Valor.Serie);
        if (g.Valor.Aviso is string aviso) registro["_Aviso"] = aviso;
        return Resultado<RegistroMantenimiento>.Correcto(registro);
    }

    public override Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c) => servicio.EliminarAsync(Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture));

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (accion != "uso" || r.Clave is null) return await base.EjecutarAsync(accion, r, c);
        var uso = await servicio.UsoAsync(Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture));
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(uso.TieneFacturas
            ? $"La serie {r.Texto("Prefijo")} tiene {uso.Numeradas} facturas numeradas y {uso.Borradores} borradores."
            : $"La serie {r.Texto("Prefijo")} todavía no tiene facturas: se puede cambiar o borrar."));
    }
}

/// <summary>Conceptos de la empresa de trabajo, a través de <see cref="CatalogoConceptos"/>, con Duplicar y Ver uso.</summary>
public sealed class ProveedorConceptos(CatalogoConceptos catalogo) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _tipos = [];

    public override string Clave => "conceptos";
    public override string Titulo => "Conceptos";
    public override string Categoria => "Facturación";
    public override string Finalidad => "Conceptos facturables de la empresa: nombre, precio, IVA y cuenta de venta que se proponen en las líneas.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override string NombreRegistro => "concepto";
    public override IReadOnlyList<string> Sinonimos => ["concepto", "producto", "servicio", "articulo", "alquiler", "cuenta de venta"];
    public override IReadOnlyList<GrupoCampos> Grupos => [new("con", "Concepto"), new("fac", "Valores para la factura"), new("con2", "Contabilidad")];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [new("duplicar", "Duplicar", "Crea una copia inactiva para ajustarla"), new("uso", "Ver uso", "En cuántas líneas de factura, contrato y programación se usa", Modifica: false)];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Nombre", "Nombre") { Grupo = "con", Obligatorio = true, Longitud = CatalogoConceptos.LongitudNombre, Ancho = "l", Ayuda = "Encabezado de la línea en la factura." },
        new("Descripcion", "Descripción", TipoCampo.TextoLargo) { Grupo = "con", Ancho = "f", Ayuda = "Texto que aparece debajo del nombre en la factura." },
        new("IdTipoConcepto", "Tipo de concepto", TipoCampo.Referencia) { Referencia = "tiposconcepto", Ayuda = "Clasificación del concepto (alquiler, suministros, servicios…). Los tipos se mantienen en «Tipos de concepto».", Grupo = "con", Opciones = _tipos, Ancho = "m" },
        new("Activo", "Activo", TipoCampo.Logico) { Grupo = "con", Ayuda = "Solo los activos se ofrecen al añadir líneas." },
        new("PrecioUnitario", "Precio unitario", TipoCampo.Importe) { Ayuda = "Precio sin IVA que se propone al añadir el concepto a una factura; se puede cambiar en cada línea.", Grupo = "fac", Ancho = "s" },
        new("TipoIVA", "IVA (%)", TipoCampo.Porcentaje) { Ayuda = "Porcentaje de IVA que se propone al añadir el concepto a una factura; se puede cambiar en cada línea.", Grupo = "fac", Ancho = "xs" },
        new("CuentaVenta", "Cuenta de venta", TipoCampo.Cuenta) { Grupo = "con2", Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "ingreso", "70"], GruposCuenta = ["7"],
            Ayuda = "Código en el ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código." },
        new("NombreCuentaVenta", "Nombre de la cuenta") { Ayuda = "Nombre de la cuenta de venta en el ejercicio de trabajo, para comprobar que es la buena. Se rellena solo.", Grupo = "con2", SoloLectura = true, Ancho = "l" },
        new("Notas", "Notas internas", TipoCampo.TextoLargo) { Ayuda = "Comentarios internos sobre el concepto; no salen en la factura.", Grupo = "con", Ancho = "f" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas =>
        [new("Nombre", "Nombre"), new("CuentaVenta", "Cuenta", 90), new("PrecioUnitario", "Precio", 90, true), new("TipoIVA", "IVA", 56, true), new("Activo", "Activo", 56)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo concepto" : r.Texto("Nombre") ?? "Concepto sin nombre";
    public override string? SubtituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? null : $"Concepto {r.Clave}";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        _tipos = [new OpcionCampo(null, "Sin tipo"), .. (await catalogo.TiposAsync()).Select(t => new OpcionCampo(t.IdTipoConcepto, t.Activo ? t.Descripcion : $"{t.Descripcion} (inactivo)"))];
        return (await catalogo.ListarAsync(c.Trabajo)).Select(AsRegistro).ToList();
    }

    private static RegistroMantenimiento AsRegistro(Concepto x)
    {
        var r = new RegistroMantenimiento(x.IdConcepto, new Dictionary<string, object?>
        {
            ["Nombre"] = x.Nombre, ["Descripcion"] = x.Descripcion, ["IdTipoConcepto"] = x.IdTipoConcepto, ["Activo"] = x.Activo,
            ["PrecioUnitario"] = x.PrecioUnitario, ["TipoIVA"] = x.TipoIva, ["CuentaVenta"] = x.CuentaVenta,
            ["NombreCuentaVenta"] = x.CuentaVenta is null ? null : x.CuentaEnEjercicio ? x.NombreCuentaVenta : $"{x.NombreCuentaVenta} (no existe en este ejercicio)",
            ["Notas"] = x.Notas, ["_CuentaOriginal"] = x.CuentaVenta, ["_IdIdentidadContable"] = x.IdIdentidadContable, ["_CuentaEnEjercicio"] = x.CuentaEnEjercicio,
            ["_IdDepartamento"] = x.IdDepartamento, ["_IdCentroCoste"] = x.IdCentroCoste
        });
        return r;
    }

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Activo"] = true, ["PrecioUnitario"] = 0m, ["TipoIVA"] = 21m }));

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var concepto = new Concepto(r.Clave is null ? 0 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture), c.Trabajo.IdSociedad, r.Entero("IdTipoConcepto"),
            r.Texto("Nombre") ?? "", r["Descripcion"] as string, r["_IdIdentidadContable"] as int?,
            r["_CuentaOriginal"] as string, null, r["_CuentaEnEjercicio"] is true,
            r["_IdDepartamento"] as int?, r["_IdCentroCoste"] as int?, r.Decimal("PrecioUnitario") ?? 0m, r.Decimal("TipoIVA"), r.Logico("Activo"), r["Notas"] as string);
        var g = await catalogo.GuardarAsync(concepto, r.Texto("CuentaVenta"), c.Trabajo);
        return g.EsCorrecto ? Resultado<RegistroMantenimiento>.Correcto(AsRegistro(g.Valor)) : Resultado<RegistroMantenimiento>.Fallo(g.Errores);
    }

    public override Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c) =>
        catalogo.EliminarAsync(Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture), c.Trabajo);

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (r.Clave is null) return Resultado<ResultadoAccion>.Fallo(new Error("Conceptos.SinGuardar", "Guarda antes el concepto."));
        var id = Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture);
        switch (accion)
        {
            case "duplicar":
                var d = await catalogo.DuplicarAsync(id, c.Trabajo);
                return d.EsCorrecto
                    ? Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"He creado «{d.Valor.Nombre}», inactivo. Revísalo y actívalo cuando esté listo.", d.Valor.IdConcepto))
                    : Resultado<ResultadoAccion>.Fallo(d.Errores);
            case "uso":
                var uso = await catalogo.UsoAsync(id);
                return Resultado<ResultadoAccion>.Correcto(uso.EnUso
                    ? new ResultadoAccion($"«{r.Texto("Nombre")}» se usa en {string.Join(", ", uso.Frases())}.", null,
                        uso.UltimasFacturas.Count > 0 ? ["Últimas facturas:", .. uso.UltimasFacturas] : null)
                    : new ResultadoAccion($"«{r.Texto("Nombre")}» no se ha usado nunca: se puede borrar."));
            default:
                return await base.EjecutarAsync(accion, r, c);
        }
    }
}
