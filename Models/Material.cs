namespace LabInventario.Models
{
    // Representa una fila de la tabla `materiales`. 
    public class Material
    {
        public int Id { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int CantidadTotal { get; set; }
        public int CantidadDisponible { get; set; }

        public override string ToString() => $"{Nombre} [{CodigoBarras}]";
    }
}
