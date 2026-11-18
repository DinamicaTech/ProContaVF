using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

/// <summary>Cambiar la subcuenta del tercero (terceros/roles/cambio-subcuenta): qué cambios se detectan y qué asientos no se pasan, sin BD.</summary>
public sealed class CambioSubcuentaTerceroTests
{
    private static Tercero ConCuentas(Action<CuentasRolesFicha> fijar, bool cliente = true, bool proveedor = true)
    {
        var t = new Tercero { IdTercero = 7 };
        if (cliente) { t.Roles.Cliente = new RolCliente(); }
        if (proveedor) { t.Roles.Proveedor = new RolProveedor(); }
        t.CuentasRoles = new CuentasRolesFicha("T01");
        fijar(t.CuentasRoles);
        return t;
    }

    [Fact]
    public void Solo_cuenta_como_cambio_pasar_de_una_subcuenta_a_otra_en_un_rol_activo()
    {
        var antes = ConCuentas(c => { c.Cliente = 10; c.Acreedor = 20; c.Empleado = 30; });
        antes.Roles.Cliente!.IdCliente = 5;
        antes.Roles.Proveedor!.IdProveedor = 6;
        var ahora = antes.Clonar();
        ahora.CuentasRoles!.Cliente = 11;
        ahora.CuentasRoles.Acreedor = null;   // quitar la cuenta no es un cambio
        ahora.CuentasRoles.Proveedor = 40;    // poner una nueva tampoco
        ahora.CuentasRoles.Empleado = 31;     // la 465 no se traslada
        var c = Assert.Single(CambioSubcuentaTercero.QueCambiar(antes, ahora));
        Assert.Equal(new SubcuentaCambiada(ClaseCuentaRol.Cliente, 5, "T01", 10, 11), c);

        // Con el rol quitado al guardar, el cambio no se traslada (lo trata el borrado de la subcuenta del rol quitado).
        ahora.Roles.Cliente!.Activo = false;
        Assert.Empty(CambioSubcuentaTercero.QueCambiar(antes, ahora));
        // Un tercero nuevo no tiene nada contabilizado.
        Assert.Empty(CambioSubcuentaTercero.QueCambiar(null, ahora));
    }

    [Fact]
    public void En_un_trimestre_cerrado_no_se_pasa_un_asiento_con_retencion_ni_a_otro_grupo_si_lleva_cuentas_fiscales()
    {
        Assert.Null(CambioSubcuentaTercero.BloqueoTrimestreCerrado(["4300001", "5720001"], "4300001", "4100001"));
        Assert.Null(CambioSubcuentaTercero.BloqueoTrimestreCerrado(["4300001", "4770001", "7050001"], "4300001", "4300002"));
        Assert.Contains("retención (4751)", CambioSubcuentaTercero.BloqueoTrimestreCerrado(["4300001", "4751001"], "4300001", "4300002"));
        Assert.Contains("no es del mismo grupo", CambioSubcuentaTercero.BloqueoTrimestreCerrado(["4000001", "4720001", "6280001"], "4000001", "4100001"));
    }
}
