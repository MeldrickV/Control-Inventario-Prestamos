using System.Globalization;
using Microsoft.Data.Sqlite;
using LabInventario.Models;

namespace LabInventario.Data
{
    // Acceso a datos para la tabla `prestamos`, incluida la consulta combinada para reportes.
    public class PrestamoRepository
    {
        private readonly DatabaseManager _db;

        /// <summary>
        /// Usa la base de datos real (<see cref="DatabaseManager.Instancia"/>)
        /// por defecto; recibir un <see cref="DatabaseManager"/> permite
        /// apuntar a una base temporal en las pruebas.
        /// </summary>
        public PrestamoRepository(DatabaseManager? db = null) => _db = db ?? DatabaseManager.Instancia;

        public int Crear(int alumnoId, int materialId, int cantidad, DateTime fechaSalida, SqliteConnection? conexion = null, int cablesExtra = 0)
        {
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
            comando.CommandText = @"
                INSERT INTO prestamos (AlumnoId, MaterialId, Cantidad, FechaSalida, Estado, CablesExtra)
                VALUES ($alumnoId, $materialId, $cantidad, $fecha, 'Activo', $cablesExtra);
                SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$alumnoId", alumnoId);
            comando.Parameters.AddWithValue("$materialId", materialId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$fecha", fechaSalida.ToString("yyyy-MM-dd HH:mm:ss"));
            comando.Parameters.AddWithValue("$cablesExtra", cablesExtra);
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        public void MarcarDevuelto(int idPrestamo, DateTime fechaRegreso, SqliteConnection? conexion = null)
        {
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
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
        public void ActualizarCantidad(int idPrestamo, int nuevaCantidad, SqliteConnection? conexion = null)
        {
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
            comando.CommandText = "UPDATE prestamos SET Cantidad = $cantidad WHERE Id = $id";
            comando.Parameters.AddWithValue("$cantidad", nuevaCantidad);
            comando.Parameters.AddWithValue("$id", idPrestamo);
            comando.ExecuteNonQuery();
        }

        /// <summary>
        /// Crea directamente un préstamo ya cerrado (Estado 'Devuelto').
        /// Se usa para dejar un registro NUEVO y visible en el historial
        /// cuando una devolución es parcial: el préstamo original que
        /// sigue activo conserva su cantidad restante y su fecha de
        /// salida sin tocar (ver <see cref="ActualizarCantidad"/>), y este
        /// método agrega, aparte, una fila cerrada con la cantidad que sí
        /// se devolvió, la FechaSalida original (de dónde salió) y la
        /// FechaRegreso de este momento — así la devolución parcial deja
        /// rastro en vez de perderse dentro del préstamo activo restante.
        /// </summary>
        public int CrearDevuelto(int alumnoId, int materialId, int cantidad, DateTime fechaSalida, DateTime fechaRegreso, SqliteConnection? conexion = null)
        {
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
            comando.CommandText = @"
                INSERT INTO prestamos (AlumnoId, MaterialId, Cantidad, FechaSalida, FechaRegreso, Estado)
                VALUES ($alumnoId, $materialId, $cantidad, $fechaSalida, $fechaRegreso, 'Devuelto');
                SELECT last_insert_rowid();";
            comando.Parameters.AddWithValue("$alumnoId", alumnoId);
            comando.Parameters.AddWithValue("$materialId", materialId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$fechaSalida", fechaSalida.ToString("yyyy-MM-dd HH:mm:ss"));
            comando.Parameters.AddWithValue("$fechaRegreso", fechaRegreso.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        /// <summary>
        /// Indica si un alumno tiene algún préstamo registrado (activo o ya
        /// devuelto). Se usa antes de eliminar: la FK de `prestamos` impide
        /// borrar quien tenga historial, así que la interfaz debe impedirlo
        /// con un mensaje claro.
        /// </summary>
        public bool TienePrestamosDeAlumno(int alumnoId)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT 1 FROM prestamos WHERE AlumnoId = $id LIMIT 1";
            comando.Parameters.AddWithValue("$id", alumnoId);
            using var lector = comando.ExecuteReader();
            return lector.Read();
        }

        /// <summary>Igual que <see cref="TienePrestamosDeAlumno"/>, pero para materiales.</summary>
        public bool TienePrestamosDeMaterial(int materialId)
        {
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT 1 FROM prestamos WHERE MaterialId = $id LIMIT 1";
            comando.Parameters.AddWithValue("$id", materialId);
            using var lector = comando.ExecuteReader();
            return lector.Read();
        }

        public Prestamo? ObtenerPorId(int idPrestamo, SqliteConnection? conexion = null)
        {
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
            comando.CommandText = "SELECT * FROM prestamos WHERE Id = $id";
            comando.Parameters.AddWithValue("$id", idPrestamo);
            using var lector = comando.ExecuteReader();
            return lector.Read() ? Mapear(lector) : null;
        }

        /// <summary>
        /// Todos los préstamos activos de un alumno para un material,
        /// ordenados del más antiguo al más reciente (orden FIFO: el que
        /// salió primero es el primero en saldarse al devolver).
        /// </summary>
        public List<Prestamo> ListarActivosPorAlumnoYMaterial(int alumnoId, int materialId, SqliteConnection? conexion = null)
        {
            var resultado = new List<Prestamo>();
            using var conexionPropia = conexion is null ? _db.ObtenerConexion() : null;
            var con = conexion ?? conexionPropia!;
            using var comando = con.CreateCommand();
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
                       p.Cantidad, p.FechaSalida, p.FechaRegreso, p.Estado, p.CablesExtra
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
                    FechaSalida = ParsearFecha(lector.GetString(lector.GetOrdinal("FechaSalida"))),
                    FechaRegreso = lector.IsDBNull(lector.GetOrdinal("FechaRegreso"))
                        ? null
                        : ParsearFecha(lector.GetString(lector.GetOrdinal("FechaRegreso"))),
                    Estado = lector.GetString(lector.GetOrdinal("Estado")),
                    CablesExtra = lector.GetInt32(lector.GetOrdinal("CablesExtra")),
                });
            }
            return resultado;
        }

        /// <summary>
        /// Convierte una fecha guardada en formato fijo "yyyy-MM-dd HH:mm:ss"
        /// de vuelta a <see cref="DateTime"/>. Se usa cultura invariable: los
        /// datos se escriben siempre con ese formato y así se leen, sin que
        /// el ajuste regional de la máquina cambie el resultado del parseo.
        /// </summary>
        private static DateTime ParsearFecha(string texto) =>
            DateTime.ParseExact(texto, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static Prestamo Mapear(SqliteDataReader lector) => new()
        {
            Id = lector.GetInt32(lector.GetOrdinal("Id")),
            AlumnoId = lector.GetInt32(lector.GetOrdinal("AlumnoId")),
            MaterialId = lector.GetInt32(lector.GetOrdinal("MaterialId")),
            Cantidad = lector.GetInt32(lector.GetOrdinal("Cantidad")),
            FechaSalida = ParsearFecha(lector.GetString(lector.GetOrdinal("FechaSalida"))),
            FechaRegreso = lector.IsDBNull(lector.GetOrdinal("FechaRegreso"))
                ? null
                : ParsearFecha(lector.GetString(lector.GetOrdinal("FechaRegreso"))),
            Estado = lector.GetString(lector.GetOrdinal("Estado")) == "Activo"
                ? EstadoPrestamo.Activo
                : EstadoPrestamo.Devuelto,
            CablesExtra = lector.GetInt32(lector.GetOrdinal("CablesExtra")),
        };

        /// <summary>
        /// Cuenta los préstamos ya devueltos con más de <paramref name="dias"/>
        /// días de antigüedad (contados desde su fecha de regreso). Se usa
        /// antes de <see cref="EliminarDevueltosAntiguos"/> para que el
        /// administrador sepa cuántos registros se van a borrar.
        /// </summary>
        public int ContarDevueltosAntiguos(int dias)
        {
            var limite = DateTime.Now.AddDays(-dias);
            using var conexion = _db.ObtenerConexion();
            using var comando = conexion.CreateCommand();
            comando.CommandText = @"
                SELECT COUNT(*) FROM prestamos
                WHERE Estado = 'Devuelto'
                  AND FechaRegreso IS NOT NULL
                  AND FechaRegreso < $limite";
            comando.Parameters.AddWithValue("$limite", limite.ToString("yyyy-MM-dd HH:mm:ss"));
            return Convert.ToInt32((long)comando.ExecuteScalar()!);
        }

        /// <summary>
        /// Borra los préstamos ya devueltos con más de <paramref name="dias"/>
        /// días de antigüedad (contados desde su fecha de regreso). Este
        /// borrado es SOLO manual: la aplicación ya no purga el historial
        /// sola. Si el administrador quiere depurar registros antiguos lo
        /// hace explícitamente desde "Administración &gt; Limpiar historial
        /// antiguo...", que primero muestra cuántos se eliminarán (ver
        /// <see cref="ContarDevueltosAntiguos"/>) y pide confirmación.
        /// </summary>
        public int EliminarDevueltosAntiguos(int dias = 30)
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
