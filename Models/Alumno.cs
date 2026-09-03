namespace LabInventario.Models
{
    // Representa una fila de la tabla `alumnos`.
    public class Alumno
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string NumeroCuenta { get; set; } = string.Empty;

        public override string ToString() => $"{Nombre} ({NumeroCuenta})";
    }
}
