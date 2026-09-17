using Microsoft.Data.Sqlite;

namespace LabInventario.Data
{
    /// <summary>
    /// Acceso a datos para la tabla `configuracion`: pares clave/valor de
    /// uso general (contraseña de administrador, patrones de detección de
    /// escaneo, etc.). Evita tener que crear una tabla nueva cada vez que
    /// se necesita guardar un ajuste sencillo.
    /// </summary>
    public class ConfiguracionRepository
    {
        private readonly DatabaseManager _db;

        /// <summary>
        /// Usa la base de datos real (<see cref="DatabaseManager.Instancia"/>)
        /// por defecto; recibir un <see cref="DatabaseManager"/> permite
        /// apuntar a una base temporal en las pruebas.
        /// </summary>
        public ConfiguracionRepository(DatabaseManager? db = null) => _db = db ?? DatabaseManager.Instancia;

        public string? Obtener(string clave)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT Valor FROM configuracion WHERE Clave = $clave";
            comando.Parameters.AddWithValue("$clave", clave);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? lector.GetString(0) : null;
        }

        public void Establecer(string clave, string valor)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                INSERT INTO configuracion (Clave, Valor) VALUES ($clave, $valor)
                ON CONFLICT(Clave) DO UPDATE SET Valor = excluded.Valor;";
            comando.Parameters.AddWithValue("$clave", clave);
            comando.Parameters.AddWithValue("$valor", valor);
            comando.ExecuteNonQuery();
        }
    }
}
