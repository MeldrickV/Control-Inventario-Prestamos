namespace LabInventario.Services
{
    /// <summary>Qué catálogo toca el cambio recibido desde la hoja.</summary>
    public enum EntidadCambio { Alumno, Material }

    /// <summary>Qué pide la hoja hacer con el registro.</summary>
    public enum AccionCambio { Alta, Cambio, Baja }

    /// <summary>
    /// Un cambio del catálogo (alumnos o inventario) detectado al comparar
    /// la hoja de Google con la base local. La identidad del registro es
    /// su clave de negocio (<c>NumeroCuenta</c> o <c>CodigoBarras</c>),
    /// que son columnas UNIQUE en la base.
    /// </summary>
    public sealed class CambioSincronizacion
    {
        public EntidadCambio Entidad { get; set; }
        public AccionCambio Accion { get; set; }

        /// <summary>Clave de negocio: NumeroCuenta (alumno) o CodigoBarras (material).</summary>
        public string Clave { get; set; } = string.Empty;

        /// <summary>Nombre nuevo (alta o cambio).</summary>
        public string Nombre { get; set; } = string.Empty;

        /// <summary>Cantidad total nueva (solo materiales).</summary>
        public int CantidadTotal { get; set; }

        /// <summary>Línea legible con lo que se propone ("mensaje de lo que se está enviando").</summary>
        public string Detalle { get; set; } = string.Empty;

        public override string ToString() => Detalle;
    }
}
