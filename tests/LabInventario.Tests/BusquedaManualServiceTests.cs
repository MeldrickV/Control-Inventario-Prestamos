using LabInventario.Services;

namespace LabInventario.Tests
{
    public class BusquedaManualServiceTests : BaseDePruebas
    {
        private BusquedaManualService Buscar() => new(Db);

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Buscar_Vacio_DevuelveVacio(string texto)
        {
            CrearAlumnoYMaterial();

            Assert.Empty(Buscar().Buscar(texto));
        }

        [Fact]
        public void Buscar_PorNombreAlumno_Encuentra()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar("Sof");

            var alumno = Assert.Single(resultados);
            Assert.Equal(CuentaAlumno, alumno.Alumno!.NumeroCuenta);
            Assert.Null(alumno.Material);
        }

        [Fact]
        public void Buscar_PorCuenta_Encuentra()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar("202310");

            Assert.Contains(resultados, r => r.Alumno?.NumeroCuenta == CuentaAlumno);
        }

        [Fact]
        public void Buscar_PorNombreMaterial_Encuentra()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar("multim");

            var material = Assert.Single(resultados);
            Assert.Equal(CodigoMaterial, material.Material!.CodigoBarras);
        }

        [Fact]
        public void Buscar_PorCodigoBarras_Encuentra()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar(CodigoMaterial);

            Assert.Contains(resultados, r => r.Material?.CodigoBarras == CodigoMaterial);
        }

        [Fact]
        public void Buscar_SoloAlumnos_ExcluyeMateriales()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar(CodigoMaterial, incluirAlumnos: true, incluirMateriales: false);

            Assert.Empty(resultados);
        }

        [Fact]
        public void Buscar_SoloMateriales_ExcluyeAlumnos()
        {
            CrearAlumnoYMaterial();

            var resultados = Buscar().Buscar(CuentaAlumno, incluirAlumnos: false, incluirMateriales: true);

            Assert.Empty(resultados);
        }

        [Fact]
        public void Buscar_RespetaLimite()
        {
            for (var i = 1; i <= 5; i++)
                Alumnos.Crear($"Alumno Prueba {i}", $"9000000{i}");

            var resultados = Buscar().Buscar("Prueba", limite: 2);

            Assert.Equal(2, resultados.Count);
        }

        [Fact]
        public void Buscar_SinCoincidencias_DevuelveVacio()
        {
            CrearAlumnoYMaterial();

            Assert.Empty(Buscar().Buscar("zzz-sin-existir"));
        }
    }
}
