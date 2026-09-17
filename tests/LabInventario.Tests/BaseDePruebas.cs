using LabInventario.Data;
using LabInventario.Services;

namespace LabInventario.Tests
{
    /// <summary>
    /// Base para las pruebas que tocan la base de datos: crea una base
    /// SQLite TEMPORAL por prueba (carpeta única en el directorio temporal
    /// del sistema) y expone los repositorios y el servicio apuntando a
    /// ella. xUnit descarta la instancia de la clase de prueba después de
    /// cada método, así que <see cref="Dispose"/> limpia la carpeta y cada
    /// prueba arranca desde cero.
    /// </summary>
    public abstract class BaseDePruebas : IDisposable
    {
        protected const string CuentaAlumno = "20231001";
        protected const string NombreAlumno = "Ana Sofía Ramírez";
        protected const string CodigoMaterial = "7501234560012";
        protected const string NombreMaterial = "Multímetro digital";
        protected const int CantidadTotalMaterial = 10;

        private readonly string _carpetaTemporal;

        protected DatabaseManager Db { get; }
        protected AlumnoRepository Alumnos { get; }
        protected MaterialRepository Materiales { get; }
        protected PrestamoRepository Prestamos { get; }
        protected PrestamoService Servicio { get; }

        protected BaseDePruebas()
        {
            _carpetaTemporal = Path.Combine(Path.GetTempPath(), "LabInventarioTests", Guid.NewGuid().ToString("N"));
            Db = new DatabaseManager(_carpetaTemporal);
            Alumnos = new AlumnoRepository(Db);
            Materiales = new MaterialRepository(Db);
            Prestamos = new PrestamoRepository(Db);
            Servicio = new PrestamoService(Db);
        }

        /// <summary>Inserta el alumno y el material de ejemplo en la base temporal.</summary>
        protected void CrearAlumnoYMaterial()
        {
            CrearAlumno();
            CrearMaterial();
        }

        protected void CrearAlumno() => Alumnos.Crear(NombreAlumno, CuentaAlumno);

        protected void CrearMaterial() => Materiales.Crear(CodigoMaterial, NombreMaterial, CantidadTotalMaterial);

        private static DateTime D(int dia, int hora) => new(2026, 9, Math.Clamp(dia, 1, 30), hora, 0, 0);

        /// <summary>Fecha determinista (2026-09-{dia} {hora}:00) para préstamos verificables.</summary>
        protected static DateTime FechaPrueba(int dia, int hora = 8) => D(dia, hora);

        public virtual void Dispose()
        {
            if (Directory.Exists(_carpetaTemporal))
            {
                // Reintentar eliminar la carpeta con pequeños delays para liberar locks de SQLite
                const int maxRetries = 5;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        Directory.Delete(_carpetaTemporal, recursive: true);
                        return;
                    }
                    catch (IOException) when (retry < maxRetries - 1)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    catch
                    {
                        // Si falla en el último intento, ignorar silenciosamente
                    }
                }
            }
        }
    }
}
