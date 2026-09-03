namespace LabInventario.Services
{
    /// <summary>
    /// Excepción de negocio: su mensaje está redactado para mostrarse tal
    /// cual al usuario en un MessageBox, sin necesidad de traducirlo.
    /// </summary>
    public class PrestamoException : Exception
    {
        public PrestamoException(string mensaje) : base(mensaje) { }
    }
}
