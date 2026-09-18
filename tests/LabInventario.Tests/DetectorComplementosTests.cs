using LabInventario.Services;

namespace LabInventario.Tests
{
    public class DetectorComplementosTests
    {
        [Theory]
        [InlineData("Fuente de poder DC", TipoCable.Fuente)]
        [InlineData("FUENTE DE VOLTAJE variable", TipoCable.Fuente)]
        [InlineData("fuente de alimentación de bancada", TipoCable.Fuente)]
        [InlineData("Generador de señales", TipoCable.Generador)]
        [InlineData("GENERADOR DE FUNCIONES", TipoCable.Generador)]
        [InlineData("generador de audio", TipoCable.Generador)]
        [InlineData("Osciloscopio digital", TipoCable.Osciloscopio)]
        [InlineData("osciloscopio de 100 MHz", TipoCable.Osciloscopio)]
        [InlineData("OSCILOSCOPIO Tektronix TBS1052B", TipoCable.Osciloscopio)]
        [InlineData("Multímetro digital", TipoCable.Ninguno)]
        [InlineData("Protoboard grande", TipoCable.Ninguno)]
        [InlineData("Cables de prueba", TipoCable.Ninguno)]
        [InlineData("O1 TEKTRONIX TBS1052B-EDU C051040", TipoCable.Osciloscopio)]
        [InlineData("O10 TEKTRONIX TBS1052B-EDU C042068", TipoCable.Osciloscopio)]
        [InlineData("o-1041", TipoCable.Osciloscopio)]
        [InlineData("F7 LEYBOLD 521 546 14120191", TipoCable.Fuente)]
        [InlineData("F10 FUENTE DE PODER GW INSTEK/GPS-3303 GEP811196", TipoCable.Fuente)]
        [InlineData("G12 EQUIPO DE LABORATORIO INSTEK/SFG 1013 GCQ851443", TipoCable.Generador)]
        [InlineData("GX GENERADOR DE FUNCIONES SIN MARCA/G001026", TipoCable.Generador)]
        [InlineData("Ondulador de corriente", TipoCable.Ninguno)]
        [InlineData("Fusibles 5A", TipoCable.Ninguno)]
        public void Detectar_DetectaTipoSegunNombre(string nombre, TipoCable esperado) =>
            Assert.Equal(esperado, DetectorComplementos.Detectar(nombre));

        [Fact]
        public void Detectar_IgnoraAcentosYMayusculas()
        {
            Assert.Equal(TipoCable.Generador, DetectorComplementos.Detectar("GENERADOR de SEÑALES"));
            Assert.Equal(TipoCable.Fuente, DetectorComplementos.Detectar("FUENTE DE PODÉR con tílde"));
        }

        [Fact]
        public void Detectar_NombreVacio_DevuelveNinguno()
        {
            Assert.Equal(TipoCable.Ninguno, DetectorComplementos.Detectar(""));
            Assert.Equal(TipoCable.Ninguno, DetectorComplementos.Detectar("   "));
        }

        [Fact]
        public void TextoExtra_ConCables_DevuelveEtiquetaYCantidad()
        {
            Assert.Equal("Puntas de osciloscopio: 2", DetectorComplementos.TextoExtra("Osciloscopio Tektronix", 2));
            Assert.Equal("Cables de fuente de poder: 1", DetectorComplementos.TextoExtra("Fuente de poder DC", 1));
            Assert.Equal("Cables de generador: 3", DetectorComplementos.TextoExtra("Generador de funciones", 3));
        }

        [Fact]
        public void TextoExtra_SinCables_DevuelveVacio()
        {
            Assert.Equal("", DetectorComplementos.TextoExtra("Osciloscopio Tektronix", 0));
            Assert.Equal("", DetectorComplementos.TextoExtra("Multímetro digital", 2));
        }
    }
}