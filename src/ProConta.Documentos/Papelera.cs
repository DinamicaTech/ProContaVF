using System.Runtime.InteropServices;

namespace ProConta.Documentos;

/// <summary>Manda ficheros a la papelera de Windows (se pueden recuperar), sin preguntar ni enseñar ventanas.</summary>
public static class Papelera
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct op);

    /// <summary>Envía el fichero a la papelera; true si ya no está en su sitio.</summary>
    public static bool Enviar(string ruta)
    {
        if (!File.Exists(ruta)) return true;
        var op = new ShFileOpStruct
        {
            wFunc = FoDelete,
            pFrom = Path.GetFullPath(ruta) + "\0\0",
            fFlags = FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi,
        };
        return SHFileOperation(ref op) == 0 && !File.Exists(ruta);
    }
}
