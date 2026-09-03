using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LabInventario.Data
{
    /// <summary>
    /// Punto único de acceso a la base de datos SQLite local.
    /// Es un Singleton: crea el archivo .db y el esquema una sola vez al
    /// arrancar la aplicación, y todos los repositorios piden conexiones
    /// a través de él (Microsoft.Data.Sqlite maneja bien conexiones cortas
    /// y frecuentes, así que no se mantiene una conexión abierta permanente).
    ///
    /// El archivo .db se cifra en disco con SQLCipher (AES-256) usando una
    /// clave derivada de un secreto embebido en el ejecutable combinado con
    /// el identificador de ESTA máquina (ver <see cref="ObtenerClaveCifrado"/>
    /// y <see cref="ObtenerIdMaquina"/>). Esto es DISTINTO del hash+salt de
    /// la contraseña de administrador: aquella es irreversible y solo sirve
    /// para verificar; esta clave es reversible porque la app necesita poder
    /// abrir el archivo en cada arranque.
    ///
    /// Importante ser realista sobre el alcance de esta protección: como la
    /// clave se puede recalcular con datos accesibles desde el propio
    /// programa, alguien que decompile el .exe con suficiente esfuerzo EN
    /// ESA MISMA MÁQUINA podría reconstruirla. Lo que sí logra es cerrar los
    /// dos vectores más comunes: (1) abrir laboratorio.db con herramientas
    /// genéricas (DB Browser for SQLite, un editor hexadecimal, etc.) solo
    /// por tener acceso a la carpeta del proyecto, y (2) copiar la carpeta
    /// "data" a otra computadora y abrirla ahí, incluso con el mismo .exe.
    /// </summary>
    public sealed class DatabaseManager
    {
        private static readonly Lazy<DatabaseManager> _instancia = new(() => new DatabaseManager());
        public static DatabaseManager Instancia => _instancia.Value;

        private static readonly string ClaveCifrado = ObtenerClaveCifrado();

        /// <summary>Clave de cifrado de esta instalación. Se expone de solo lectura para que otros
        /// servicios (p. ej. importar un .db cifrado hecho por esta misma app) puedan reutilizarla
        /// sin duplicar la lógica de derivación.</summary>
        public static string ClaveCifradoActual => ClaveCifrado;

        public string DbPath { get; }

        private DatabaseManager()
        {
            var carpetaDatos = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(carpetaDatos);
            DbPath = Path.Combine(carpetaDatos, "laboratorio.db");
            CrearEsquema();
        }

        /// <summary>Abre y devuelve una nueva conexión lista para usarse (cifrada, con foreign keys activas).</summary>
        public SqliteConnection ObtenerConexion()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DbPath,
                Password = ClaveCifrado // Microsoft.Data.Sqlite ejecuta "PRAGMA key" automáticamente al abrir.
            };

            var conexion = new SqliteConnection(builder.ConnectionString);
            conexion.Open();
            using var pragma = conexion.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
            return conexion;
        }

        /// <summary>
        /// Deriva la clave de cifrado de la base combinando un secreto fijo
        /// embebido en el binario con un identificador propio de ESTA
        /// máquina. Resultado: cada instalación tiene su propia clave real;
        /// si alguien copia la carpeta "data" a otra PC, ni siquiera con el
        /// mismo programa la puede abrir, porque el ID de máquina calculado
        /// ahí no coincide con el que se usó para cifrarla originalmente.
        /// </summary>
        private static string ObtenerClaveCifrado()
        {
            byte[] secretoBase =
            {
                0x4c, 0x61, 0x62, 0x2d, 0x49, 0x6e, 0x76, 0x2d,
                0x32, 0x30, 0x32, 0x36, 0x2d, 0x53, 0x51, 0x4c,
                0x43, 0x69, 0x70, 0x68, 0x65, 0x72, 0x2d, 0x6b
            };
            byte[] saltMaquina = Encoding.UTF8.GetBytes(ObtenerIdMaquina());

            var claveDerivada = Rfc2898DeriveBytes.Pbkdf2(
                secretoBase, saltMaquina, iterations: 50_000, HashAlgorithmName.SHA256, outputLength: 32);

            return Convert.ToBase64String(claveDerivada);
        }

        /// <summary>
        /// Identificador estable de la máquina actual, usando el mecanismo
        /// propio de cada sistema operativo. Si por alguna razón no se
        /// puede leer (permisos, entorno atípico), cae a un GUID propio
        /// generado una sola vez y guardado junto a la carpeta de datos
        /// (menos fuerte, porque viaja si alguien copia también ese
        /// archivo, pero evita que la app deje de funcionar).
        /// </summary>
        private static string ObtenerIdMaquina()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var valor = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
                    if (!string.IsNullOrWhiteSpace(valor)) return valor;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    foreach (var ruta in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                    {
                        if (File.Exists(ruta))
                        {
                            var valor = File.ReadAllText(ruta).Trim();
                            if (!string.IsNullOrWhiteSpace(valor)) return valor;
                        }
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var info = new ProcessStartInfo("ioreg", "-rd1 -c IOPlatformExpertDevice")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using var proceso = Process.Start(info);
                    var salida = proceso!.StandardOutput.ReadToEnd();
                    proceso.WaitForExit();
                    var coincidencia = Regex.Match(salida, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
                    if (coincidencia.Success) return coincidencia.Groups[1].Value;
                }
            }
            catch
            {
                // Si falla la detección (permisos, sandbox, contenedor, etc.),
                // se usa el respaldo de abajo en vez de tronar la app.
            }

            return ObtenerIdRespaldo();
        }

        private static string ObtenerIdRespaldo()
        {
            var carpetaDatos = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(carpetaDatos);
            var rutaId = Path.Combine(carpetaDatos, ".machine-id-respaldo");

            if (File.Exists(rutaId))
                return File.ReadAllText(rutaId).Trim();

            var nuevoId = Guid.NewGuid().ToString();
            File.WriteAllText(rutaId, nuevoId);
            return nuevoId;
        }

        private void CrearEsquema()
        {
            using var conexion = ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                CREATE TABLE IF NOT EXISTS alumnos (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nombre         TEXT NOT NULL,
                    NumeroCuenta   TEXT NOT NULL UNIQUE
                );

                CREATE TABLE IF NOT EXISTS materiales (
                    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    CodigoBarras        TEXT NOT NULL UNIQUE,
                    Nombre              TEXT NOT NULL,
                    CantidadTotal       INTEGER NOT NULL DEFAULT 0,
                    CantidadDisponible  INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS prestamos (
                    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    AlumnoId      INTEGER NOT NULL,
                    MaterialId    INTEGER NOT NULL,
                    Cantidad      INTEGER NOT NULL,
                    FechaSalida   TEXT NOT NULL,
                    FechaRegreso  TEXT NULL,
                    Estado        TEXT NOT NULL DEFAULT 'Activo',
                    FOREIGN KEY (AlumnoId) REFERENCES alumnos(Id),
                    FOREIGN KEY (MaterialId) REFERENCES materiales(Id)
                );

                CREATE INDEX IF NOT EXISTS idx_prestamos_estado   ON prestamos(Estado);
                CREATE INDEX IF NOT EXISTS idx_prestamos_alumno   ON prestamos(AlumnoId);
                CREATE INDEX IF NOT EXISTS idx_prestamos_material ON prestamos(MaterialId);

                -- Pares clave/valor de configuración general: contraseña de
                -- administrador (hash + salt) y patrón de detección de
                -- número de cuenta para el escaneo unificado.
                CREATE TABLE IF NOT EXISTS configuracion (
                    Clave  TEXT PRIMARY KEY,
                    Valor  TEXT NOT NULL
                );
            ";
            comando.ExecuteNonQuery();
        }
    }
}
