using LabInventario.Services;

namespace LabInventario.Tests
{
    public class AuthServiceTests : BaseDePruebas
    {
        [Fact]
        public void PrimeraVez_SiembraLaPasswordPorDefecto()
        {
            var auth = new AuthService(Db);

            Assert.True(auth.ValidarPasswordAdmin("admin123"));
            Assert.True(auth.UsaPasswordPorDefecto());
        }

        [Fact]
        public void ValidarPasswordAdmin_PasswordEquivocada_Falso()
        {
            var auth = new AuthService(Db);

            Assert.False(auth.ValidarPasswordAdmin("admin124"));
            Assert.False(auth.ValidarPasswordAdmin("Administrador123"));
        }

        [Fact]
        public void EstablecerPasswordAdmin_CambiaYDejaDeUsarLaPorDefecto()
        {
            var auth = new AuthService(Db);
            auth.EstablecerPasswordAdmin("Clave.Nueva-2026");

            Assert.True(auth.ValidarPasswordAdmin("Clave.Nueva-2026"));
            Assert.False(auth.ValidarPasswordAdmin("admin123"));
            Assert.False(auth.UsaPasswordPorDefecto());
        }
    }
}