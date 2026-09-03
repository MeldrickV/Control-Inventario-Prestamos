using Microsoft.Data.Sqlite;
using LabInventario.Models;

namespace LabInventario.Data
{
    // Acceso a datos para la tabla `materiales`.</summary>
    public class MaterialRepository
    {
        private readonly DatabaseManager _db = DatabaseManager.Instancia;

        public int Crear(string codigoBarras, string nombre, int cantidadTotal)
        {
            // Al crear un material nuevo, la cantidad disponible arranca igual a la total.
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                INSERT INTO materiales (CodigoBarras, Nombre, CantidadTotal, CantidadDisponible)
                VALUES ($codigo, $nombre, $total, $total);
                SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$codigo", codigoBarras);
            comando.Parameters.AddWithValue("$nombre", nombre);
            comando.Parameters.AddWithValue("$total", cantidadTotal);
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        public void Actualizar(int id, string codigoBarras, string nombre, int cantidadTotal, int cantidadDisponible)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                UPDATE materiales
                SET CodigoBarras = $codigo, Nombre = $nombre,
                    CantidadTotal = $total, CantidadDisponible = $disponible
                WHERE Id = $id";
            comando.Parameters.AddWithValue("$codigo", codigoBarras);
            comando.Parameters.AddWithValue("$nombre", nombre);
            comando.Parameters.AddWithValue("$total", cantidadTotal);
            comando.Parameters.AddWithValue("$disponible", cantidadDisponible);
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        public void Eliminar(int id)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "DELETE FROM materiales WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        public Material? ObtenerPorCodigo(string codigoBarras)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT * FROM materiales WHERE CodigoBarras = $codigo";
            comando.Parameters.AddWithValue("$codigo", codigoBarras);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        public Material? ObtenerPorId(int id)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT * FROM materiales WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", id);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        public List<Material> Listar(string filtro = "")
        {
            var resultado = new List<Material>();
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                comando.CommandText =
                    "SELECT * FROM materiales WHERE Nombre LIKE $f OR CodigoBarras LIKE $f ORDER BY Nombre";
                comando.Parameters.AddWithValue("$f", $"%{filtro}%");
            }
            else
            {
                comando.CommandText = "SELECT * FROM materiales ORDER BY Nombre";
            }

            using var lector = comando.ExecuteReader();
            while (lector.Read())
                resultado.Add(Mapear(lector));
            return resultado;
        }

        /// <summary>
        /// Suma `delta` a la cantidad disponible.
        /// delta negativo = sale material en préstamo; delta positivo = se devuelve al stock.
        /// </summary>
        public void AjustarDisponible(int id, int delta)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText =
                "UPDATE materiales SET CantidadDisponible = CantidadDisponible + $delta WHERE Id = $id";
            comando.Parameters.AddWithValue("$delta", delta);
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        public bool ExisteCodigo(string codigoBarras, int? excluirId = null)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();

            comando.CommandText = excluirId.HasValue
                ? "SELECT 1 FROM materiales WHERE CodigoBarras = $codigo AND Id != $id"
                : "SELECT 1 FROM materiales WHERE CodigoBarras = $codigo";

            comando.Parameters.AddWithValue("$codigo", codigoBarras);
            if (excluirId.HasValue)
                comando.Parameters.AddWithValue("$id", excluirId.Value);

            using var lector = comando.ExecuteReader();
            return lector.Read();
        }

        private static Material Mapear(SqliteDataReader lector) => new()
        {
            Id = lector.GetInt32(lector.GetOrdinal("Id")),
            CodigoBarras = lector.GetString(lector.GetOrdinal("CodigoBarras")),
            Nombre = lector.GetString(lector.GetOrdinal("Nombre")),
            CantidadTotal = lector.GetInt32(lector.GetOrdinal("CantidadTotal")),
            CantidadDisponible = lector.GetInt32(lector.GetOrdinal("CantidadDisponible")),
        };
    }
}
