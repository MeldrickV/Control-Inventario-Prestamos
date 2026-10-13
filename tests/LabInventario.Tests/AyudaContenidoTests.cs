using LabInventario.Services;

namespace LabInventario.Tests
{
    /// <summary>
    /// El manual integrado se filtra por rol: un Usuario nunca ve las
    /// secciones de administrador (importar, exportar, nube). Pruebas puras,
    /// sin base de datos.
    /// </summary>
    public class AyudaContenidoTests
    {
        [Fact]
        public void Usuario_NoVeSeccionesDeAdministrador()
        {
            var secciones = AyudaContenido.Para(esAdministrador: false);
            Assert.NotEmpty(secciones);
            Assert.All(secciones, s => Assert.False(s.SoloAdmin));
            Assert.DoesNotContain(secciones, s => s.Id == AyudaContenido.IdImportar);
            Assert.DoesNotContain(secciones, s => s.Id == AyudaContenido.IdExportar);
            Assert.DoesNotContain(secciones, s => s.Id == AyudaContenido.IdGoogle);
            // Lo esencial de operación sí se ve como usuario.
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdSalida);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdDevolucion);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdHistorial);
        }

        [Fact]
        public void Administrador_VeTodasLasSecciones()
        {
            var secciones = AyudaContenido.Para(esAdministrador: true);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdImportar);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdExportar);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdGoogle);
            Assert.Contains(secciones, s => s.Id == AyudaContenido.IdPrimerosPasos);
        }

        [Fact]
        public void Secciones_TienenIdsUnicosYContenidoMinimo()
        {
            var secciones = AyudaContenido.Para(esAdministrador: true).ToList();
            var ids = secciones.Select(s => s.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.All(secciones, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Titulo));
                Assert.False(string.IsNullOrWhiteSpace(s.Resumen));
                Assert.True(s.Pasos.Length >= 3);
            });
        }

        [Fact]
        public void SeccionGoogle_MencionaClavesDelProceso()
        {
            var google = AyudaContenido.PorId(AyudaContenido.IdGoogle);
            Assert.NotNull(google);
            var texto = string.Join("\n", google!.Pasos);
            Assert.Contains("/exec", texto);
            Assert.Contains("CLAVE", texto);
            Assert.Contains("Probar conexión", texto);
            Assert.Contains("Copiar script", google.Resumen + texto);
        }

        [Fact]
        public void PorId_RegresaNullConIdDesconocido()
        {
            Assert.Null(AyudaContenido.PorId("no-existe"));
        }
    }
}
