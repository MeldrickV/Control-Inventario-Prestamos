using Microsoft.Data.Sqlite;
using LabInventario.Models;

namespace LabInventario.Data
{
    /// <summary>Acceso a datos para la tabla `alumnos`. Todo el SQL de alumnos vive aquí.</summary>
    public class AlumnoRepository
    {
        private readonly DatabaseManager _db = DatabaseManager.Instancia;

        public int Crear(string nombre, string numeroCuenta)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                INSERT INTO alumnos (Nombre, NumeroCuenta) VALUES ($nombre, $cuenta);
                SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$nombre", nombre);
            comando.Parameters.AddWithValue("$cuenta", numeroCuenta);
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        public void Actualizar(int id, string nombre, string numeroCuenta)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "UPDATE alumnos SET Nombre = $nombre, NumeroCuenta = $cuenta WHERE Id = $id";
            comando.Parameters.AddWithValue("$nombre", nombre);
            comando.Parameters.AddWithValue("$cuenta", numeroCuenta);
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        public void Eliminar(int id)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "DELETE FROM alumnos WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        public Alumno? ObtenerPorCuenta(string numeroCuenta)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT * FROM alumnos WHERE NumeroCuenta = $cuenta";
            comando.Parameters.AddWithValue("$cuenta", numeroCuenta);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        public Alumno? ObtenerPorId(int id)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT * FROM alumnos WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", id);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        public List<Alumno> Listar(string filtro = "")
        {
            var resultado = new List<Alumno>();
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                comando.CommandText =
                    "SELECT * FROM alumnos WHERE Nombre LIKE $f OR NumeroCuenta LIKE $f ORDER BY Nombre";
                comando.Parameters.AddWithValue("$f", $"%{filtro}%");
            }
            else
            {
                comando.CommandText = "SELECT * FROM alumnos ORDER BY Nombre";
            }

            using var lector = comando.ExecuteReader();
            while (lector.Read())
                resultado.Add(Mapear(lector));
            return resultado;
        }

        public bool ExisteNumeroCuenta(string numeroCuenta, int? excluirId = null)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();

            comando.CommandText = excluirId.HasValue
                ? "SELECT 1 FROM alumnos WHERE NumeroCuenta = $cuenta AND Id != $id"
                : "SELECT 1 FROM alumnos WHERE NumeroCuenta = $cuenta";

            comando.Parameters.AddWithValue("$cuenta", numeroCuenta);
            if (excluirId.HasValue)
                comando.Parameters.AddWithValue("$id", excluirId.Value);

            using var lector = comando.ExecuteReader();
            return lector.Read();
        }

        private static Alumno Mapear(SqliteDataReader lector) => new()
        {
            Id = lector.GetInt32(lector.GetOrdinal("Id")),
            Nombre = lector.GetString(lector.GetOrdinal("Nombre")),
            NumeroCuenta = lector.GetString(lector.GetOrdinal("NumeroCuenta")),
        };
    }
}
