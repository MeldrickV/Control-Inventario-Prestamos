using System.Runtime.CompilerServices;

namespace LabInventario.Tests
{
    /// <summary>
    /// Habilita el motor nativo de SQLite (SQLCipher) una sola vez por
    /// proceso de prueba, igual que hace la aplicación en Program.cs.
    /// Sin esto, Microsoft.Data.Sqlite no encuentra el proveedor nativo.
    /// </summary>
    public static class InicializacionSqlite
    {
        [ModuleInitializer]
        public static void Inicializar() => SQLitePCL.Batteries_V2.Init();
    }
}