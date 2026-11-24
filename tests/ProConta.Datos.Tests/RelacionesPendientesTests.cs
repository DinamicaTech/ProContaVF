using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Nucleo.Auditoria;
using ProConta.Pruebas.Access;
using static ProConta.Datos.Esquema.Migraciones.MigracionesIntegridad;

namespace ProConta.Datos.Tests;

/// <summary>Relaciones que faltaban (datos/esquema 2026-11-24, esquema 83).</summary>
public class RelacionesPendientesTests
{
    [Fact]
    public void UnPasoPorRelacionConNombresUnicos()
    {
        Assert.Equal(Relaciones.Count, RelacionesPendientes.Pasos.Count);
        Assert.Equal(Relaciones.Count, Relaciones.Select(r => r.Nombre.ToUpperInvariant()).Distinct().Count());
        Assert.Contains(Relaciones, r => r is { Tabla: "OperacionesInversion", Columna: "IdActivo", TablaPadre: "Activos" });
        Assert.Contains(Relaciones, r => r is { Tabla: "ActivosInversiones", Columna: "IdActivo", TablaPadre: "Activos" });
        Assert.Contains(Relaciones, r => r is { Tabla: "ActivosInversiones", Columna: "IdInstrumentoFinanciero", TablaPadre: "InstrumentosFinancieros" });
        Assert.Contains(Relaciones, r => r is { Tabla: "CotizacionesInstrumentoFinanciero", Columna: "IdInstrumentoFinanciero", TablaPadre: "InstrumentosFinancieros" });
    }

    [Fact]
    public void NingunaApuntaALasTablasQueYaEstanEnElMaximoDeIndices()
    {
        string[] llenas = ["Sociedades", "Facturas", "SociedadesEjercicios"];
        Assert.DoesNotContain(Relaciones, r => llenas.Contains(r.Tabla) || llenas.Contains(r.TablaPadre));
    }

    [Fact]
    public void LosAsientosDeLosProcesosNoImpidenBorrarElAsiento()
    {
        // El motor borra el asiento antes de que el proceso limpie su fila (TrabajoLote.Despues): impedirlo rompería deshacer y regenerar.
        Assert.All(Relaciones.Where(r => r.TablaPadre == "Asientos"), r => Assert.NotEqual(AlBorrar.Impedir, r.AlBorrar));
        Assert.EndsWith("ON DELETE SET NULL", Relaciones.Single(r => r.Nombre == "FK_LiquidacionesIva_Asiento").Sql);
        Assert.EndsWith("ON DELETE CASCADE", Relaciones.Single(r => r.Nombre == "FK_ContabilizacionesLinea_Asiento").Sql);
    }
}

[Trait("Categoria", "Access")]
public sealed class RelacionesPendientesAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task LaBdDePruebasTieneTodasLasRelacionesConSuReglaDeBorrado()
    {
        var esquema = await _copia.Bd().EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        foreach (var r in Relaciones)
        {
            var fisica = esquema.Tabla(r.Tabla)!.Relaciones.SingleOrDefault(x => x.Nombre == r.Nombre);
            Assert.True(fisica is not null, $"Falta {r.Nombre}");
            Assert.Equal(r.TablaPadre, fisica!.TablaReferenciada, ignoreCase: true);
            Assert.Equal([r.Columna], fisica.Columnas);
            Assert.Equal(r.AlBorrar switch { AlBorrar.Vaciar => "SET NULL", AlBorrar.Cascada => "CASCADE", _ => "NO ACTION" }, fisica.ReglaBorrado);
        }
    }

    [Fact]
    public async Task ConHuerfanosNoCreaNingunaYLosLista()
    {
        var bd = _copia.Bd();
        var r = Relaciones.Single(x => x.Nombre == "FK_MensajesConversacionIA_Conversacion");
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync($"ALTER TABLE [{r.Tabla}] DROP CONSTRAINT [{r.Nombre}]");
            await s.EjecutarAsync("INSERT INTO MensajesConversacionIA (IdConversacion, OrdenMensaje, Rol, Contenido, FechaHora) VALUES (?, ?, ?, ?, ?)",
                987654, 1, (byte)1, "Mensaje sintético", new DateTime(2026, 11, 24));
            return true;
        });

        var error = await bd.EjecutarAsync(s => ComprobarHuerfanosAsync(s, ContextoMigracion.Vacio));
        Assert.NotNull(error);
        Assert.Equal("Esquema.RelacionesConHuerfanos", error!.Codigo);
        Assert.Contains("MensajesConversacionIA.IdConversacion → ConversacionesIA: 1 fila(s) con 987654", error.RegistroAfectado);
        Assert.Contains("No he cambiado nada", error.Mensaje);

        // Sin el huérfano, la comprobación pasa y el paso vuelve a crear la relación.
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM MensajesConversacionIA WHERE IdConversacion = 987654");
            Assert.Null(await ComprobarHuerfanosAsync(s, ContextoMigracion.Vacio));
            var paso = RelacionesPendientes.Pasos[Relaciones.ToList().IndexOf(r)];
            Assert.False(await paso.YaAplicadoAsync(s, s.LeerEsquema()));
            await paso.AplicarAsync(s, ContextoMigracion.Vacio);
            Assert.True(await paso.YaAplicadoAsync(s, s.LeerEsquema()));
            return true;
        });
    }
}
