using Microsoft.Data.Sqlite;
using LabInventario.Models;

namespace LabInventario.Data
{
    // Acceso a datos para la tabla `prestamos`, incluida la consulta combinada para reportes.
    public class PrestamoRepository
    {
        private readonly DatabaseManager _db = DatabaseManager.Instancia;

        public int Crear(int alumnoId, int materialId, int cantidad, DateTime fechaSalida)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                INSERT INTO prestamos (AlumnoId, MaterialId, Cantidad, FechaSalida, Estado)
                VALUES ($alumnoId, $materialId, $cantidad, $fecha, 'Activo');
                SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$alumnoId", alumnoId);
            comando.Parameters.AddWithValue("$materialId", materialId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$fecha", fechaSalida.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        public void MarcarDevuelto(int idPrestamo, DateTime fechaRegreso)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText =
                "UPDATE prestamos SET FechaRegreso = $fecha, Estado = 'Devuelto' WHERE Id = $id";
            comando.Parameters.AddWithValue("$fecha", fechaRegreso.ToString("yyyy-MM-dd HH:mm:ss"));
            comando.Parameters.AddWithValue("$id", idPrestamo);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Reduce la cantidad de un préstamo SIN cerrarlo (devolución
        /// parcial): el registro sigue "Activo", con su misma fecha de
        /// salida original, pero con menos unidades pendientes.
        /// </summary>
        public void ActualizarCantidad(int idPrestamo, int nuevaCantidad)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "UPDATE prestamos SET Cantidad = $cantidad WHERE Id = $id";
            comando.Parameters.AddWithValue("$cantidad", nuevaCantidad);
            comando.Parameters.AddWithValue("$id", idPrestamo);
            comando.ExecuteNonQuery();
        }

        public void Eliminar(int idPrestamo)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "DELETE FROM prestamos WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", idPrestamo);
            comando.ExecuteNonQuery();
        }

        public Prestamo? ObtenerPorId(int idPrestamo)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT * FROM prestamos WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", idPrestamo);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        // Localiza el préstamo activo más reciente de un alumno para un material dado.
        public Prestamo? BuscarActivo(int alumnoId, int materialId)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                SELECT * FROM prestamos
                WHERE AlumnoId = $alumnoId AND MaterialId = $materialId AND Estado = 'Activo'
                ORDER BY FechaSalida DESC LIMIT 1";
            comando.Parameters.AddWithValue("$alumnoId", alumnoId);
            comando.Parameters.AddWithValue("$materialId", materialId);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        /// <summary>
        /// Todos los préstamos activos de un alumno para un material,
        /// ordenados del más antiguo al más reciente (orden FIFO: el que
        /// salió primero es el primero en saldarse al devolver).
        /// </summary>
        public List<Prestamo> ListarActivosPorAlumnoYMaterial(int alumnoId, int materialId)
        {
            var resultado = new List<Prestamo>();
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                SELECT * FROM prestamos
                WHERE AlumnoId = $alumnoId AND MaterialId = $materialId AND Estado = 'Activo'
                ORDER BY FechaSalida ASC";
            comando.Parameters.AddWithValue("$alumnoId", alumnoId);
            comando.Parameters.AddWithValue("$materialId", materialId);
            using var lector = comando.ExecuteReader();
            while (lector.Read())
                resultado.Add(Mapear(lector));
            return resultado;
        }

        // Devuelve filas ya combinadas (JOIN) con el nombre del alumno y del
        // material, listas para mostrarse directamente en el DataGrid.
        public List<PrestamoDetalle> ListarDetallado(string filtro = "", bool soloActivos = false)
        {
            var resultado = new List<PrestamoDetalle>();
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();

            var condiciones = new List<string>();
            if (soloActivos)
                condiciones.Add("p.Estado = 'Activo'");

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                condiciones.Add("(a.Nombre LIKE $f OR a.NumeroCuenta LIKE $f OR m.Nombre LIKE $f OR m.CodigoBarras LIKE $f)");
                comando.Parameters.AddWithValue("$f", $"%{filtro}%");
            }

            var clausulaWhere = condiciones.Count > 0 ? "WHERE " + string.Join(" AND ", condiciones) : "";

            comando.CommandText = $@"
                SELECT p.Id, p.AlumnoId, p.MaterialId,
                       a.Nombre AS AlumnoNombre, a.NumeroCuenta,
                       m.Nombre AS MaterialNombre, m.CodigoBarras,
                       p.Cantidad, p.FechaSalida, p.FechaRegreso, p.Estado
                FROM prestamos p
                JOIN alumnos a ON p.AlumnoId = a.Id
                JOIN materiales m ON p.MaterialId = m.Id
                {clausulaWhere}
                ORDER BY p.FechaSalida DESC";

            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                resultado.Add(new PrestamoDetalle
                {
                    Id = lector.GetInt32(lector.GetOrdinal("Id")),
                    AlumnoId = lector.GetInt32(lector.GetOrdinal("AlumnoId")),
                    MaterialId = lector.GetInt32(lector.GetOrdinal("MaterialId")),
                    AlumnoNombre = lector.GetString(lector.GetOrdinal("AlumnoNombre")),
                    NumeroCuenta = lector.GetString(lector.GetOrdinal("NumeroCuenta")),
                    MaterialNombre = lector.GetString(lector.GetOrdinal("MaterialNombre")),
                    CodigoBarras = lector.GetString(lector.GetOrdinal("CodigoBarras")),
                    Cantidad = lector.GetInt32(lector.GetOrdinal("Cantidad")),
                    FechaSalida = DateTime.Parse(lector.GetString(lector.GetOrdinal("FechaSalida"))),
                    FechaRegreso = lector.IsDBNull(lector.GetOrdinal("FechaRegreso"))
                        ? null
                        : DateTime.Parse(lector.GetString(lector.GetOrdinal("FechaRegreso"))),
                    Estado = lector.GetString(lector.GetOrdinal("Estado")),
                });
            }
            return resultado;
        }

        private static Prestamo Mapear(SqliteDataReader lector) => new()
        {
            Id = lector.GetInt32(lector.GetOrdinal("Id")),
            AlumnoId = lector.GetInt32(lector.GetOrdinal("AlumnoId")),
            MaterialId = lector.GetInt32(lector.GetOrdinal("MaterialId")),
            Cantidad = lector.GetInt32(lector.GetOrdinal("Cantidad")),
            FechaSalida = DateTime.Parse(lector.GetString(lector.GetOrdinal("FechaSalida"))),
            FechaRegreso = lector.IsDBNull(lector.GetOrdinal("FechaRegreso"))
                ? null
                : DateTime.Parse(lector.GetString(lector.GetOrdinal("FechaRegreso"))),
            Estado = lector.GetString(lector.GetOrdinal("Estado")) == "Activo"
                ? EstadoPrestamo.Activo
                : EstadoPrestamo.Devuelto,
        };

        /// <summary>
        /// Borra los préstamos ya devueltos con más de <paramref name="dias"/>
        /// días de antigüedad (contados desde su FechaRegreso). Se llama
        /// automáticamente al entrar a la pestaña de Historial y al iniciar
        /// sesión, así el historial de devoluciones queda disponible un
        /// tiempo prudente para revisión (por ejemplo, si algo se regresó
        /// dañado y hay que saber quién y cuándo) sin acumularse para
        /// siempre.
        /// </summary>
        public int EliminarDevueltosAntiguos(int dias = 7)
        {
            var limite = DateTime.Now.AddDays(-dias);
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                DELETE FROM prestamos
                WHERE Estado = 'Devuelto'
                  AND FechaRegreso IS NOT NULL
                  AND FechaRegreso < $limite";
            comando.Parameters.AddWithValue("$limite", limite.ToString("yyyy-MM-dd HH:mm:ss"));
            return comando.ExecuteNonQuery();
        }
    }
}
