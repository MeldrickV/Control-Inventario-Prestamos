namespace LabInventario.Models
{
    public enum EstadoPrestamo
    {
        Activo,
        Devuelto
    }

    // Representa una fila de la tabla `prestamos`.
    public class Prestamo
    {
        public int Id { get; set; }
        public int AlumnoId { get; set; }
        public int MaterialId { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaSalida { get; set; }
        public DateTime? FechaRegreso { get; set; }
        public EstadoPrestamo Estado { get; set; }

        /// <summary>
        /// Cantidad de cable complementario prestado con este material
        /// (0 = no lleva). No es inventario: es solo un registro del
        /// historial (ver <see cref="LabInventario.Services.DetectorComplementos"/>).
        /// </summary>
        public int CablesExtra { get; set; }
    }

    // Proyección "aplanada" de un préstamo ya combinado (JOIN) con los datos
    // del alumno y del material. Se usa directamente para alimentar el
    // DataGridView de la pestaña de historial, sin más consultas.
    
    public class PrestamoDetalle
    {
        public int Id { get; set; }
        public int AlumnoId { get; set; }
        public int MaterialId { get; set; }
        public string AlumnoNombre { get; set; } = string.Empty;
        public string NumeroCuenta { get; set; } = string.Empty;
        public string MaterialNombre { get; set; } = string.Empty;
        public string CodigoBarras { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public DateTime FechaSalida { get; set; }
        public DateTime? FechaRegreso { get; set; }
        public string Estado { get; set; } = string.Empty;

        /// <summary>Cantidad de cable complementario prestado (0 = no lleva). Ver <see cref="Prestamo.CablesExtra"/>.</summary>
        public int CablesExtra { get; set; }
    }
}
