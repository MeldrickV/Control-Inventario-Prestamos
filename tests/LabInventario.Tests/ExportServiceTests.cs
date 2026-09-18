using LabInventario.Services;

namespace LabInventario.Tests
{
    public class ExportServiceTests : BaseDePruebas
    {
        private readonly ExportService _exportador;
        private readonly string _carpeta;

        public ExportServiceTests()
        {
            _exportador = new ExportService(Db);
            _carpeta = Path.Combine(Path.GetTempPath(), "LabInventarioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_carpeta);
        }

        private string CrearRuta(string nombre) => Path.Combine(_carpeta, nombre);

        [Fact]
        public void ExportarAlumnosCsv_EscribeEncabezadoYFilas()
        {
            CrearAlumno();
            Alumnos.Crear("Martínez, Juan \"El Chino\"", "20232001");
            var ruta = CrearRuta("alumnos.csv");

            _exportador.ExportarAlumnosCsv(ruta);

            var contenido = File.ReadAllText(ruta);
            Assert.Contains("Id,Nombre,NumeroCuenta", contenido);
            Assert.Contains("\"Martínez, Juan \"\"El Chino\"\"\",20232001", contenido);
        }

        [Fact]
        public void CsvExportado_RoundTrip_SeReimportaSinPerderDatos()
        {
            Alumnos.Crear("Martínez, Juan \"El Chino\"", "20232001");
            Alumnos.Crear("Vásquez, María", "20231099");
            var ruta = CrearRuta("alumnos.csv");

            _exportador.ExportarAlumnosCsv(ruta);
            var datos = new ImportService().LeerArchivo(ruta, esAlumnos: true);

            Assert.Equal(new[] { "Id", "Nombre", "NumeroCuenta" }, datos.Headers);
            var filaComilla = Assert.Single(datos.Filas, f => f[2] == "20232001");
            Assert.Equal("Martínez, Juan \"El Chino\"", filaComilla[1]);
            Assert.Equal("Vásquez, María", Assert.Single(datos.Filas, f => f[2] == "20231099")[1]);
        }

        [Fact]
        public void ExportarHistorialCsv_ReflejaLosPrestamos()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));
            var ruta = CrearRuta("historial.csv");

            _exportador.ExportarHistorialCsv(ruta);

            var datos = new ImportService().LeerArchivo(ruta, esAlumnos: false);
            var fila = Assert.Single(datos.Filas, f => f[2] == CuentaAlumno);
            Assert.Equal(CodigoMaterial, fila[4]);
            Assert.Equal("2", fila[5]);
            Assert.Equal("", fila[6]);
            Assert.Equal("Activo", fila[9]);
        }

        [Fact]
        public void ExportarHistorialCsv_ReflejaCablesExtra()
        {
            CrearAlumno();
            Materiales.Crear("8500000000012", "Osciloscopio digital", 5);
            Servicio.RegistrarSalida(CuentaAlumno, "8500000000012", 1, FechaPrueba(2), cablesExtra: 2);
            var ruta = CrearRuta("historial-extra.csv");

            _exportador.ExportarHistorialCsv(ruta);

            var datos = new ImportService().LeerArchivo(ruta, esAlumnos: false);
            var fila = Assert.Single(datos.Filas, f => f[2] == CuentaAlumno);
            Assert.Equal("8500000000012", fila[4]);
            Assert.Equal("1", fila[5]);
            Assert.Equal("Puntas de osciloscopio: 2", fila[6]);
        }

        [Fact]
        public void ExportarBaseDatosCifrada_CopiaElArchivoTalCual()
        {
            var ruta = CrearRuta("respaldo.db");

            _exportador.ExportarBaseDatosCifrada(ruta);

            Assert.True(File.Exists(ruta));
            Assert.Equal(new FileInfo(Db.DbPath).Length, new FileInfo(ruta).Length);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Directory.Exists(_carpeta))
                Directory.Delete(_carpeta, recursive: true);
        }
    }
}