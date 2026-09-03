using LabInventario.Models;

namespace LabInventario.Services
{
    /// <summary>
    /// Guarda el rol con el que se inició sesión en la aplicación actual.
    /// Es deliberadamente simple (aplicación de escritorio de un solo
    /// puesto, no multiusuario en red): no hay tokens ni expiración, solo
    /// un valor en memoria que las pantallas consultan para decidir qué
    /// mostrar y qué permitir.
    /// </summary>
    public static class SesionActual
    {
        public static Rol Rol { get; set; } = Rol.Usuario;

        public static bool EsAdministrador => Rol == Rol.Administrador;
    }
}
