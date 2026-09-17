using LabInventario.Data;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class DetectorPatronesTests : BaseDePruebas
    {
        [Fact]
        public void TipoProbable_NumeroCuentaTipico_DiceAlumno()
        {
            var detector = new DetectorPatrones(Db);

            Assert.Equal(TipoCodigo.Alumno, detector.TipoProbable("20231001"));
        }

        [Theory]
        [InlineData("X-123")]
        [InlineData("7501234560012")]
        [InlineData("codigo-inventario-1")]
        public void TipoProbable_QueNoCalzaConElPatron_DiceMaterial(string codigo)
        {
            var detector = new DetectorPatrones(Db);

            Assert.Equal(TipoCodigo.Material, detector.TipoProbable(codigo));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void TipoProbable_VacioODespuesEspacios_DiceDesconocido(string codigo)
        {
            var detector = new DetectorPatrones(Db);

            Assert.Equal(TipoCodigo.Desconocido, detector.TipoProbable(codigo));
        }

        [Fact]
        public void EstablecerPatron_Personalizado_SeAplicaYDuraGuardado()
        {
            var detector = new DetectorPatrones(Db);
            detector.EstablecerPatron(@"^ABC-\d{3}$");

            Assert.Equal(@"^ABC-\d{3}$", detector.ObtenerPatron());
            Assert.Equal(TipoCodigo.Alumno, detector.TipoProbable("ABC-123"));
            Assert.Equal(TipoCodigo.Material, detector.TipoProbable("ABC-9999"));
        }

        [Fact]
        public void EstablecerPatron_ExpresionInvalida_LanzaYSinGuardar()
        {
            var detector = new DetectorPatrones(Db);

            Assert.ThrowsAny<ArgumentException>(() => detector.EstablecerPatron("["));

            Assert.Equal(DetectorPatrones.PatronPorDefecto, detector.ObtenerPatron());
        }

        [Fact]
        public void TipoProbable_PatronGuardadoCorrupto_DiceDesconocido()
        {
            // Simula un patrón inválido ya guardado por un fallo/hackeo:
            // el detector no debe tumbar el escaneo, solo declararse agnóstico.
            var config = new ConfiguracionRepository(Db);
            config.Establecer("PatronCuentaAlumno", "[");

            var detector = new DetectorPatrones(Db);
            Assert.Equal(TipoCodigo.Desconocido, detector.TipoProbable("20231001"));
        }
    }
}