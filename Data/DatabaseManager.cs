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
    /// por tener acceso a la carpeta de datos de la aplicación, y (2) copiar
    /// esa carpeta a otra computadora y abrirla ahí, incluso con el mismo
    /// programa.
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

        /// <summary>
        /// Crea una instancia que trabaja sobre la carpeta de datos dada, o
        /// sobre la carpeta predeterminada de la aplicación si no se pasa
        /// una. El constructor es público para permitir apuntar a una base
        /// temporal en pruebas (<c>new DatabaseManager(rutaTemp)</c>); la
        /// aplicación en sí siempre usa la <see cref="Instancia"/> singleton.
        /// </summary>
        public DatabaseManager(string? rutaBase = null)
        {
            var carpetaDatos = rutaBase ?? ObtenerCarpetaDatosPredeterminada();
            Directory.CreateDirectory(carpetaDatos);
            DbPath = Path.Combine(carpetaDatos, "laboratorio.db");
            CrearEsquema();
        }

        /// <summary>
        /// Carpeta donde se guardan el archivo .db y el respaldo de ID de
        /// máquina.
        ///
        /// Elección: si ya existe una carpeta portable "data" con contenido
        /// junto al ejecutable, se sigue usando (continuidad para
        /// instalaciones previas); si no existe, los datos viven en la
        /// carpeta de datos del usuario del sistema operativo (ver
        /// <see cref="ObtenerCarpetaDatosUsuario"/>): no requiere permisos de
        /// administración para escribir, se mantiene al actualizar o
        /// reemplazar el programa, y es la ruta que Avalonia y las
        /// plataformas recomiendan para datos de aplicación.
        /// </summary>
        private static string ObtenerCarpetaDatosPredeterminada()
        {
            var carpetaPortable = Path.Combine(AppContext.BaseDirectory, "data");
            if (Directory.Exists(carpetaPortable) && Directory.EnumerateFileSystemEntries(carpetaPortable).Any())
                return carpetaPortable;

            return ObtenerCarpetaDatosUsuario();
        }

        /// <summary>
        /// Carpeta de datos específica del usuario según el sistema
        /// operativo. En Windows es %LOCALAPPDATA%\LabInventario; en Linux
        /// $XDG_DATA_HOME/LabInventario (o ~/.local/share/LabInventario) y
        /// en macOS ~/Library/Application Support/LabInventario.
        /// </summary>
        private static string ObtenerCarpetaDatosUsuario()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var raiz = string.IsNullOrWhiteSpace(home) ? "/" : home;
                return Path.Combine(raiz, "Library", "Application Support", "LabInventario");
            }

            // Windows (%LOCALAPPDATA%) y Linux ($XDG_DATA_HOME o ~/.local/share).
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(localAppData, "LabInventario");

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(xdg, "LabInventario");

            return Path.Combine(Path.GetTempPath(), "LabInventario");
        }

        /// <summary>
        /// Ejecuta <paramref name="cuerpo"/> dentro de UNA transacción:
        /// abre una conexión, inicia BEGIN, invoca el cuerpo pasándole la
        /// conexión abierta, y hace COMMIT al terminar. Si el cuerpo lanza
        /// cualquier excepción, se hace ROLLBACK (nada de lo escrito se
        /// conserva) y la excepción se re-lanza.
        /// </summary>
        /// <remarks>
        /// Los repositorios deben recibir esta conexión por parámetro para
        /// que sus escrituras queden dentro de la transacción (ver los
        /// overloads con <c>SqliteConnection?</c> de cada repositorio).
        /// </remarks>
        public void EjecutarTransaccion(Action<SqliteConnection> cuerpo)
        {
            using var conexion = ObtenerConexion();
            using var transaccion = conexion.BeginTransaction();
            try
            {
                cuerpo(conexion);
                transaccion.Commit();
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
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
            // Misma carpeta que el .db (ver ObtenerCarpetaDatosPredeterminada):
            // si los datos son portables, el ID viaja con ellos; si viven en
            // la carpeta del usuario, el ID queda ahí también.
            var carpetaDatos = ObtenerCarpetaDatosPredeterminada();
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
                    AlumnoId      INTEGER NULL,
                    MaterialId    INTEGER NULL,
                    -- Foto de los datos al momento del préstamo: el historial
                    -- conserva el texto aunque después se borre el alumno o
                    -- el material del catálogo (ver MigrarPrestamosBorradoFlexible).
                    AlumnoNombre   TEXT NOT NULL DEFAULT '',
                    NumeroCuenta   TEXT NOT NULL DEFAULT '',
                    MaterialNombre TEXT NOT NULL DEFAULT '',
                    CodigoBarras   TEXT NOT NULL DEFAULT '',
                    Cantidad      INTEGER NOT NULL,
                    FechaSalida   TEXT NOT NULL,
                    FechaRegreso  TEXT NULL,
                    Estado        TEXT NOT NULL DEFAULT 'Activo',
                    -- Cantidad de cable complementario prestado TEMPORALMENTE
                    -- con el material (0 = no lleva). Los cables no tienen
                    -- stock ni inventario: es solo un registro informativo
                    -- del historial (ver Services/DetectorComplementos.cs).
                    CablesExtra   INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (AlumnoId) REFERENCES alumnos(Id) ON DELETE SET NULL,
                    FOREIGN KEY (MaterialId) REFERENCES materiales(Id) ON DELETE SET NULL
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

            // Migración suave para bases ya existentes: `CREATE TABLE IF NOT
            // EXISTS` no modifica las tablas creadas en versiones previas,
            // así que las columnas nuevas se agregan aquí solo si faltan.
            // Se consulta PRAGMA table_info para decidir (SQLite no soporta
            // "ALTER TABLE ... ADD COLUMN IF NOT EXISTS").
            foreach (var columna in ColumnasMigracionPrestamos)
            {
                if (!ColumnaExiste(conexion, "prestamos", columna))
                {
                    using var alter = conexion.CreateCommand();
                    alter.CommandText = $"ALTER TABLE prestamos ADD COLUMN {columna} INTEGER NOT NULL DEFAULT 0";
                    alter.ExecuteNonQuery();
                }
            }

            MigrarPrestamosBorradoFlexible(conexion);
        }

        /// <summary>
        /// Migra `prestamos` al borrado flexible: columnas de foto
        /// (nombres/cuenta/código al momento del préstamo, rellenadas desde
        /// las tablas vivas) y claves foráneas con `ON DELETE SET NULL` para
        /// que borrar un alumno o material con historial sea posible sin
        /// perder el texto del historial. SQLite no permite alterar FK con
        /// ALTER TABLE, así que si las FK aún son restrictivas se reconstruye
        /// la tabla (copia exacta + DROP + RENAME + índices).
        /// </summary>
        private void MigrarPrestamosBorradoFlexible(SqliteConnection conexion)
        {
            foreach (var columna in new[] { "AlumnoNombre", "NumeroCuenta", "MaterialNombre", "CodigoBarras" })
            {
                if (!ColumnaExiste(conexion, "prestamos", columna))
                {
                    using var alter = conexion.CreateCommand();
                    alter.CommandText = $"ALTER TABLE prestamos ADD COLUMN {columna} TEXT NOT NULL DEFAULT ''";
                    alter.ExecuteNonQuery();
                }
            }

            using (var rellenar = conexion.CreateCommand())
            {
                rellenar.CommandText = @"
                    UPDATE prestamos
                    SET AlumnoNombre = COALESCE((SELECT Nombre FROM alumnos WHERE Id = prestamos.AlumnoId), AlumnoNombre),
                        NumeroCuenta = COALESCE((SELECT NumeroCuenta FROM alumnos WHERE Id = prestamos.AlumnoId), NumeroCuenta),
                        MaterialNombre = COALESCE((SELECT Nombre FROM materiales WHERE Id = prestamos.MaterialId), MaterialNombre),
                        CodigoBarras = COALESCE((SELECT CodigoBarras FROM materiales WHERE Id = prestamos.MaterialId), CodigoBarras)
                    WHERE AlumnoNombre = '' OR NumeroCuenta = '' OR MaterialNombre = '' OR CodigoBarras = '';";
                rellenar.ExecuteNonQuery();
            }

            if (PrestamosPermiteBorrado(conexion)) return;

            using var migracion = conexion.CreateCommand();
            migracion.CommandText = @"
                CREATE TABLE prestamos_nueva (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    AlumnoId       INTEGER NULL,
                    MaterialId     INTEGER NULL,
                    AlumnoNombre   TEXT NOT NULL DEFAULT '',
                    NumeroCuenta   TEXT NOT NULL DEFAULT '',
                    MaterialNombre TEXT NOT NULL DEFAULT '',
                    CodigoBarras   TEXT NOT NULL DEFAULT '',
                    Cantidad       INTEGER NOT NULL,
                    FechaSalida    TEXT NOT NULL,
                    FechaRegreso   TEXT NULL,
                    Estado         TEXT NOT NULL DEFAULT 'Activo',
                    CablesExtra    INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (AlumnoId) REFERENCES alumnos(Id) ON DELETE SET NULL,
                    FOREIGN KEY (MaterialId) REFERENCES materiales(Id) ON DELETE SET NULL
                );
                INSERT INTO prestamos_nueva
                    (Id, AlumnoId, MaterialId, AlumnoNombre, NumeroCuenta, MaterialNombre, CodigoBarras,
                     Cantidad, FechaSalida, FechaRegreso, Estado, CablesExtra)
                SELECT Id, AlumnoId, MaterialId, AlumnoNombre, NumeroCuenta, MaterialNombre, CodigoBarras,
                       Cantidad, FechaSalida, FechaRegreso, Estado, CablesExtra
                FROM prestamos;
                DROP TABLE prestamos;
                ALTER TABLE prestamos_nueva RENAME TO prestamos;
                CREATE INDEX IF NOT EXISTS idx_prestamos_estado   ON prestamos(Estado);
                CREATE INDEX IF NOT EXISTS idx_prestamos_alumno   ON prestamos(AlumnoId);
                CREATE INDEX IF NOT EXISTS idx_prestamos_material ON prestamos(MaterialId);";
            migracion.ExecuteNonQuery();
        }

        /// <summary>
        /// True si ambas FK de `prestamos` ya son `ON DELETE SET NULL`.
        /// </summary>
        private static bool PrestamosPermiteBorrado(SqliteConnection conexion)
        {
            using var comando = conexion.CreateCommand();
            comando.CommandText = "PRAGMA foreign_key_list(\"prestamos\")";
            using var lector = comando.ExecuteReader();
            var revisadas = 0;
            var indiceAccion = lector.GetOrdinal("on_delete");
            while (lector.Read())
            {
                revisadas++;
                if (!string.Equals(lector.GetString(indiceAccion), "SET NULL", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return revisadas == 2;
        }

        // Columnas nuevas que se añaden a instalaciones anteriores (una por
        // versión de esquema). Se recorren en orden en CrearEsquema.
        private static readonly string[] ColumnasMigracionPrestamos =
        {
            "CablesExtra",
        };

        private static bool ColumnaExiste(SqliteConnection conexion, string tabla, string columna)
        {
            using var comando = conexion.CreateCommand();
            comando.CommandText = $"PRAGMA table_info(\"{tabla}\")";
            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                if (string.Equals(lector.GetString(1), columna, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
