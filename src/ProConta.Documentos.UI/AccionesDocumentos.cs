using ProConta.UI.Base.Controles;

namespace ProConta.Documentos.UI;

/// <summary>Acciones sobre documentos que comparten la pantalla y el panel de las fichas: abrir, carpeta, vincular, desvincular, versiones y eliminar.</summary>
internal sealed class AccionesDocumentos(ModuloDocumentos modulo)
{
    private ContextoDocumentos Ctx => modulo.Contexto;
    private IDialogosDocumentos Dlg => modulo.Dialogos;

    public void Abrir(FilaDocumentoVM? fila)
    {
        if (fila?.Ruta is not { } ruta) return;
        if (!File.Exists(ruta)) { NoEsta(fila, ruta); return; }
        Dlg.AbrirFichero(ruta);
    }

    public void Carpeta(FilaDocumentoVM? fila)
    {
        if (fila?.Ruta is not { } ruta) return;
        if (!File.Exists(ruta)) { NoEsta(fila, ruta); return; }
        Dlg.MostrarEnCarpeta(ruta);
    }

    public void CopiarRuta(FilaDocumentoVM? fila)
    {
        if (fila?.Ruta is not { } ruta) return;
        if (Dlg.Copiar(ruta)) Ctx.Avisar($"Ruta copiada: {ruta}");
        else Dlg.Error("No se ha copiado", $"No he podido usar el portapapeles. La ruta es {ruta}");
    }

    /// <summary>
    /// «Clasificar y archivar…» (propietario 2026-11-18 18:06): pide tipo, fecha y título de un documento pendiente, los guarda y lo archiva en su
    /// carpeta. Devuelve true si ha quedado archivado.
    /// </summary>
    public async Task<bool> ClasificarAsync(FilaDocumentoVM fila, object? origen)
    {
        if (!fila.Datos.PorClasificar) return false;
        if (!await Ctx.Servicio.PuedeAsync()) { modulo.SinPermiso(); return false; }
        var tipos = (await Ctx.Servicio.TiposAsync()).Where(t => t.Manual).ToList();
        var explicacion = fila.Datos.Vinculos.Count == 0
            ? "Elige el tipo y la fecha del documento. Después, vincúlalo a algo con «Vincular a…» para poder archivarlo."
            : $"Elige el tipo y la fecha: el documento se archiva en su carpeta y sigue vinculado a {string.Join(", ", fila.Datos.Vinculos.Select(v => v.Etiqueta))}.";
        if (Dlg.Clasificar(tipos, fila.Tipo, fila.Fecha, fila.Datos.Titulo, explicacion) is not { } c) return false;
        var r = await Ctx.Servicio.ActualizarAsync(fila.Id, c.Tipo, c.Titulo, c.Fecha, fila.Datos.Notas);
        if (!r.EsCorrecto) { Dlg.Error("No se ha guardado", r.Error!.TextoOperador()); return false; }
        var a = await Ctx.Servicio.ArchivarAsync([fila.Id]);
        if (!a.EsCorrecto) { Dlg.Error("No se ha archivado", a.Error!.TextoOperador()); return false; }
        var problemas = a.Valor.NoListos.Concat(a.Valor.Errores).ToList();
        if (await Ctx.Servicio.LeerAsync(fila.Id) is { } d) fila.Actualizar(d);
        modulo.Cambiado(origen);
        if (a.Valor.Archivados == 0)
        {
            Dlg.Error("Guardado, pero no archivado", "He guardado el tipo, la fecha y el título, pero el documento sigue en «Por clasificar»:", problemas);
            return false;
        }
        Ctx.Avisar($"«{c.Titulo}» clasificado y archivado en {fila.Datos.Vigente?.Ruta}.");
        return true;
    }

    private void NoEsta(FilaDocumentoVM fila, string ruta) => Dlg.Error("No encuentro el fichero",
        $"El fichero de «{fila.Datos.Titulo}» no está en {ruta}. Puede que alguien lo haya movido o borrado fuera del programa, o que la carpeta de documentos no esté " +
        "accesible desde este equipo. Comprueba la carpeta en Parámetros y tablas › Empresas; si lo encuentras, guárdalo como versión nueva con «Nueva versión…».");

    /// <summary>
    /// «Vincular a…»: con un documento, lo elegido sustituye a sus vínculos (se ven los actuales y se pueden quitar); con varios, se añade a todos.
    /// Devuelve true si ha cambiado algo.
    /// </summary>
    public async Task<bool> VincularAsync(IReadOnlyList<FilaDocumentoVM> filas, object? origen)
    {
        if (filas.Count == 0) return false;
        if (!await Ctx.Servicio.PuedeAsync()) { modulo.SinPermiso(); return false; }
        var uno = filas.Count == 1;
        var vm = new VincularVM(Ctx.Servicio, Ctx.IdSociedad,
            uno ? $"Vincular «{filas[0].Datos.Titulo}»" : $"Vincular {filas.Count} documentos",
            uno ? "Escribe lo que buscas («303 2t 2026», «prado», «2025», «local»…) y marca uno o varios. Lo que quites de arriba se desvincula."
                : "Escribe lo que buscas y marca uno o varios: se añaden a todos los documentos marcados, sin quitarles los que ya tienen.",
            uno ? filas[0].Datos.Vinculos : [], reemplaza: uno);
        _ = vm.BuscarAsync();
        if (!Dlg.Vincular(vm)) return false;
        var r = await Ctx.Servicio.VincularAsync(filas.Select(f => f.Id).ToList(), vm.Vinculos, reemplazar: uno);
        if (!r.EsCorrecto) { Dlg.Error("No se han guardado los vínculos", string.Join(Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return false; }
        foreach (var f in filas)
            if (await Ctx.Servicio.LeerAsync(f.Id) is { } d) f.Actualizar(d);
        Ctx.Avisar(uno ? $"Vínculos de «{filas[0].Datos.Titulo}» guardados." : $"Vínculos añadidos a {filas.Count} documentos.");
        modulo.Cambiado(origen);
        return true;
    }

    public async Task<bool> DesvincularAsync(FilaDocumentoVM fila, Vinculo vinculo, object? origen)
    {
        if (!await Ctx.Servicio.PuedeAsync()) { modulo.SinPermiso(); return false; }
        var r = await Ctx.Servicio.DesvincularAsync(fila.Id, vinculo);
        if (!r.EsCorrecto) { Dlg.Error("No se ha desvinculado", r.Error!.TextoOperador()); return false; }
        if (await Ctx.Servicio.LeerAsync(fila.Id) is { } d) fila.Actualizar(d);
        Ctx.Avisar($"«{fila.Datos.Titulo}» desvinculado de {vinculo.Etiqueta}. El fichero sigue en la carpeta de documentos.");
        modulo.Cambiado(origen);
        return true;
    }

    public async Task<bool> NuevaVersionAsync(FilaDocumentoVM fila, object? origen)
    {
        if (!await Ctx.Servicio.PuedeAsync()) { modulo.SinPermiso(); return false; }
        var fichero = Dlg.ElegirFicheros($"Nueva versión de «{fila.Datos.Titulo}»", varios: false);
        if (fichero is not [var ruta]) return false;
        var motivo = Dlg.PedirTexto("Nueva versión", $"Motivo de la versión {fila.NumeroVersiones + 1} (la versión {fila.NumeroVersiones} se conserva intacta: lo enviado con ella sigue apuntando a ella).",
            "Versión firmada");
        if (motivo is null) return false;
        var r = await Ctx.Servicio.NuevaVersionAsync(fila.Id, ruta, motivo);
        if (!r.EsCorrecto) { Dlg.Error("No se ha guardado la versión", r.Error!.TextoOperador()); return false; }
        if (await Ctx.Servicio.LeerAsync(fila.Id) is { } d) fila.Actualizar(d);
        Ctx.Avisar($"Versión {r.Valor.NumeroVersion} de «{fila.Datos.Titulo}» guardada.");
        modulo.Cambiado(origen);
        return true;
    }

    /// <summary>Elimina documentos (de la bandeja, o archivados sin vínculos) tras confirmarlo; sus ficheros van a la papelera de Windows.</summary>
    public async Task<bool> EliminarAsync(IReadOnlyList<FilaDocumentoVM> filas, object? origen)
    {
        if (filas.Count == 0) return false;
        if (!await Ctx.Servicio.PuedeAsync()) { modulo.SinPermiso(); return false; }
        var bandeja = filas.All(f => f.Datos.PorClasificar);
        var mensaje = bandeja
            ? $"Se quita{(filas.Count == 1 ? "" : "n")} de «Por clasificar» y su copia va a la papelera de Windows. El fichero original, si no elegiste moverlo, sigue donde estaba."
            : "Se elimina del gestor y su fichero va a la papelera de Windows (se puede recuperar de ahí). Solo se pueden eliminar los que no tienen vínculos.";
        if (Dlg.Preguntar(bandeja ? "Quitar de la bandeja" : "Eliminar documentos", mensaje, filas.Select(f => f.Datos.Titulo).ToList(),
                new OpcionMensaje(bandeja ? "Quitar" : "Eliminar", bandeja ? "Los quita de la bandeja" : "Los elimina y manda sus ficheros a la papelera", Principal: true, Peligro: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true)) != 0)
            return false;
        var r = await Ctx.Servicio.EliminarAsync(filas.Select(f => f.Id).ToList());
        if (!r.EsCorrecto) Dlg.Error("No se han eliminado todos", r.Error!.TextoOperador());
        else Ctx.Avisar(ModuloDocumentos.Plural(r.Valor, "documento eliminado", "documentos eliminados") + ". Sus ficheros están en la papelera de Windows.");
        modulo.Cambiado(origen);
        return true;
    }
}
