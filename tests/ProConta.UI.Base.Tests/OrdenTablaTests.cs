using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using ProConta.UI.Base.Tablas;

namespace ProConta.UI.Base.Tests;

/// <summary>La tabla común conserva la ordenación cuando la pantalla le cambia las filas (norma43/pantallas 2026-11-25 20:40).</summary>
public class OrdenTablaTests
{
    public sealed record Fila(int Id, decimal Importe);

    private static TablaProConta Tabla()
    {
        var t = new TablaProConta();
        t.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding(nameof(Fila.Id)), SortMemberPath = nameof(Fila.Id) });
        t.Columns.Add(new DataGridTextColumn { Header = "Importe", Binding = new Binding(nameof(Fila.Importe)), SortMemberPath = nameof(Fila.Importe) });
        return t;
    }

    private static List<Fila> Filas(params (int Id, decimal Importe)[] f) => f.Select(x => new Fila(x.Id, x.Importe)).ToList();

    [Fact]
    public void Al_cambiar_las_filas_se_conserva_la_columna_y_el_sentido_de_la_ordenacion() => EnSta(() =>
    {
        var t = Tabla();
        t.ItemsSource = Filas((1, 30m), (2, 10m), (3, 20m));
        t.Items.SortDescriptions.Add(new SortDescription(nameof(Fila.Importe), ListSortDirection.Descending));
        t.Columns[1].SortDirection = ListSortDirection.Descending;

        t.ItemsSource = Filas((1, 30m), (2, 50m), (3, 20m), (4, 5m));
        Assert.Equal([2, 1, 3, 4], t.Items.Cast<Fila>().Select(f => f.Id));
        Assert.Equal(ListSortDirection.Descending, t.Columns[1].SortDirection);
        Assert.Null(t.Columns[0].SortDirection);
    });

    [Fact]
    public void Sin_ordenacion_o_si_la_vista_nueva_trae_la_suya_no_se_toca() => EnSta(() =>
    {
        var t = Tabla();
        t.ItemsSource = Filas((1, 30m), (2, 10m));
        t.ItemsSource = Filas((2, 10m), (1, 30m));
        Assert.Equal([2, 1], t.Items.Cast<Fila>().Select(f => f.Id));
        Assert.Empty(t.Items.SortDescriptions);

        // Una pantalla que prepara su propia vista ordenada (como el Mayor) manda sobre la ordenación anterior.
        t.Items.SortDescriptions.Add(new SortDescription(nameof(Fila.Importe), ListSortDirection.Descending));
        var vista = new ListCollectionView(Filas((1, 30m), (2, 10m), (3, 20m)));
        vista.SortDescriptions.Add(new SortDescription(nameof(Fila.Id), ListSortDirection.Ascending));
        t.ItemsSource = vista;
        Assert.Equal([1, 2, 3], t.Items.Cast<Fila>().Select(f => f.Id));
        Assert.Equal(nameof(Fila.Id), Assert.Single(t.Items.SortDescriptions).PropertyName);
    });

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() => { try { accion(); } catch (Exception ex) { error = ex; } });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
