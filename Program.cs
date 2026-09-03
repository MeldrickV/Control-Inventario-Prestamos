using Avalonia;

namespace LabInventario
{
    internal static class Program
    {
        // El punto de entrada NO debe usar tipos de Avalonia directamente en su
        // firma (recomendación oficial), para que el diseñador / previewer
        // puedan cargar el ensamblado sin inicializar la app completa.
        [STAThread]
        public static void Main(string[] args)
        {
            // Con Microsoft.Data.Sqlite.Core hay que registrar manualmente el
            // motor nativo. Aquí se registra el de SQLCipher (cifrado), no el
            // de SQLite normal, para que la base quede cifrada en disco.
            SQLitePCL.Batteries_V2.Init();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
