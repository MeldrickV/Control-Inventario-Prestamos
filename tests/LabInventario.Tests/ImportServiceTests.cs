using LabInventario.Services;

namespace LabInventario.Tests
{
    /// <summary>
    /// Pruebas del lector de archivos de <see cref="ImportService"/> y de su
    /// heurística de mapeo de columnas. No tocan la base de datos.
    /// </summary>
    public class ImportServiceTests
    {
        private readonly ImportService _importador = new();

        private static ArchivoTemporal Escribir(string contenido, string extension) => new(contenido, extension);

        [Fact]
        public void LeerArchivo_CsvConComillasYComasIncrustadas_ParseaBien()
        {
            using var archivo = Escribir(
                "Nombre,No. Cuenta\n\"Pérez, Juan\",20231001\n\"Díaz \"\"El Profe\"\", María\",20231002",
                ".csv");

            var datos = _importador.LeerArchivo(archivo.Ruta);

            Assert.Equal(new[] { "Nombre", "No. Cuenta" }, datos.Headers);
            Assert.Equal(2, datos.Filas.Count);
            Assert.Equal(new List<string?> { "Pérez, Juan", "20231001" }, datos.Filas[0]);
            Assert.Equal(new List<string?> { "Díaz \"El Profe\", María", "20231002" }, datos.Filas[1]);
        }

        [Theory]
        [InlineData(";")]
        [InlineData("\t")]
        [InlineData("|")]
        public void LeerArchivo_DetectaFielElSeparadorDelArchivo(string separador)
        {
            using var archivo = Escribir(
                $"Nombre{separador}NumeroCuenta\nAna{separador}20231001\nLuis{separador}20231002",
                ".csv");

            var datos = _importador.LeerArchivo(archivo.Ruta);

            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, datos.Headers);
            Assert.Equal(new List<string?> { "Ana", "20231001" }, datos.Filas[0]);
            Assert.Equal(new List<string?> { "Luis", "20231002" }, datos.Filas[1]);
        }

        [Fact]
        public void LeerArchivo_SqlConMultiplesTuplas_ExtraeTodas()
        {
            using var archivo = Escribir(
                "CREATE TABLE alumnos (...) ;\n" +
                "INSERT INTO alumnos (Nombre, NumeroCuenta) VALUES " +
                "('Ana Ramírez', '20231001'), ('Luis Pérez', '20231002'), (NULL, '20231003');\n" +
                "INSERT INTO alumnos (Nombre, NumeroCuenta) VALUES ('Eva Torres', '20231004');",
                ".sql");

            var datos = _importador.LeerArchivo(archivo.Ruta);

            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, datos.Headers);
            Assert.Equal(4, datos.Filas.Count);
            Assert.Equal(new List<string?> { "Ana Ramírez", "20231001" }, datos.Filas[0]);
            Assert.Equal(new List<string?> { null, "20231003" }, datos.Filas[2]);
            Assert.Equal(new List<string?> { "Eva Torres", "20231004" }, datos.Filas[3]);
        }

        [Fact]
        public void LeerArchivo_ExtensionNoSoportada_Lanza()
        {
            using var archivo = Escribir("hola", ".dat");

            Assert.Throws<NotSupportedException>(() => _importador.LeerArchivo(archivo.Ruta));
        }

        [Fact]
        public void SugerirMapeo_Alumnos_ReconoceEncabezadosEnEspanol()
        {
            var headers = new List<string> { "No. Cuenta", "Nombre del Alumno", "Grupo", "Semestre" };

            var mapeo = _importador.SugerirMapeo(headers, ImportService.CamposAlumno);

            Assert.Equal(1, mapeo["Nombre"]);
            Assert.Equal(0, mapeo["NumeroCuenta"]);
        }

        [Fact]
        public void SugerirMapeo_Materiales_ReconoceCodigoYNombre()
        {
            var headers = new List<string> { "Código de barras", "Elemento", "Cantidad", "Ubicación" };

            var mapeo = _importador.SugerirMapeo(headers, ImportService.CamposMaterial);

            Assert.Equal(0, mapeo["CodigoBarras"]);
            Assert.Equal(1, mapeo["Nombre"]);
        }

        [Fact]
        public void SugerirMapeo_Materiales_ReconoceEncabezadosConAcentos()
        {
            // Los encabezados reales traen acentos; los patrones no. El mapeo
            // debe normalizarlos para no perder la importación por una tilde.
            var headers = new List<string> { "Código de barras", "Descripción", "Cantidad" };

            var mapeo = _importador.SugerirMapeo(headers, ImportService.CamposMaterial);

            Assert.Equal(0, mapeo["CodigoBarras"]);
            Assert.Equal(1, mapeo["Nombre"]); // "Descripción" responde al campo Nombre
        }

        [Fact]
        public void SugerirMapeo_CamposAusentesDevuelveNulo_SinReusarColumnas()
        {
            var headers = new List<string> { "Nombre", "Nombre" };

            var mapeo = _importador.SugerirMapeo(headers, ImportService.CamposAlumno);

            // El "Número de cuenta" no existe; aparte, no puede reutilizar la
            // única columna de nombre para los dos campos.
            Assert.Null(mapeo["NumeroCuenta"]);
            Assert.Equal(0, mapeo["Nombre"]);
        }

        /// <summary>Archivo temporal con limpieza automática del directorio que lo contiene.</summary>
        private sealed class ArchivoTemporal : IDisposable
        {
            public string Ruta { get; }

            public ArchivoTemporal(string contenido, string extension)
            {
                var direccion = Path.Combine(Path.GetTempPath(), "LabInventarioTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(direccion);
                Ruta = Path.Combine(direccion, "datos" + extension);
                File.WriteAllText(Ruta, contenido);
            }

            public void Dispose()
            {
                var direccion = Path.GetDirectoryName(Ruta);
                if (direccion is not null && Directory.Exists(direccion))
                    Directory.Delete(direccion, recursive: true);
            }
        }
    }
}