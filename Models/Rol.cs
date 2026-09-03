namespace LabInventario.Models
{
    /// <summary>
    /// Roles del sistema.
    /// - Administrador: único que puede crear/editar/eliminar en las bases
    ///   de datos de alumnos y materiales (información delicada), además de
    ///   poder importar datos masivamente y cambiar la contraseña.
    /// - Usuario: solo puede registrar salidas y entradas de material
    ///   (operación diaria del laboratorio) y consultar el historial.
    /// </summary>
    public enum Rol
    {
        Administrador,
        Usuario
    }
}
