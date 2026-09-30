using System.Text.Json;
using LabInventario.Data;
using LabInventario.Dialogs;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class GoogleOAuthClientTests : BaseDePruebas
    {
        private readonly ManejadorFalso _manejador = new();

        private GoogleOAuthClient CrearCliente()
        {
            var config = new ConfiguracionRepository(Db);
            return new GoogleOAuthClient(Db, config, new HttpClient(_manejador));
        }

        private static HttpResponseMessage Json(object cuerpo) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo)),
        };

        private void ResponderTokens(string refreshNuevo = "refresh-1")
        {
            _manejador.Responder = req =>
            {
                var cuerpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (cuerpo.Contains("grant_type=refresh_token"))
                    return Json(new { access_token = "token-refrescado", expires_in = 3600, refresh_token = refreshNuevo });
                return Json(new { access_token = "token-inicial", expires_in = 0, refresh_token = "refresh-0" });
            };
        }

        [Fact]
        public async Task IntercambiarCodigo_GuardaRefreshToken()
        {
            ResponderTokens();
            var cliente = CrearCliente();

            await cliente.IntercambiarCodigoAsync("codigo-auth", "http://127.0.0.1:9999/");

            Assert.Single(_manejador.Peticiones, p => p.RequestUri!.ToString().Contains("oauth2.googleapis.com/token"));
            Assert.Equal("refresh-0", new ConfiguracionRepository(Db).Obtener(GoogleOAuthClient.ClaveRefreshToken));
        }

        [Fact]
        public async Task ObtenerToken_RefrescaCuandoExpiro_YRotaRefreshToken()
        {
            ResponderTokens(refreshNuevo: "refresh-1");
            var cliente = CrearCliente();
            await cliente.IntercambiarCodigoAsync("codigo-auth", "http://127.0.0.1:9999/"); // expires_in=0 → ya expirado

            var token = await cliente.ObtenerTokenAsync();

            Assert.Equal("token-refrescado", token);
            Assert.Equal("refresh-1", new ConfiguracionRepository(Db).Obtener(GoogleOAuthClient.ClaveRefreshToken));
        }

        [Fact]
        public async Task ObtenerToken_ReutilizaCacheSinPedirDeNuevo()
        {
            var llamadas = 0;
            _manejador.Responder = _ =>
            {
                llamadas++;
                return Json(new { access_token = "token-largo", expires_in = 3600 });
            };
            var config = new ConfiguracionRepository(Db);
            config.Establecer(GoogleOAuthClient.ClaveRefreshToken, "refresh-guardado");
            var cliente = new GoogleOAuthClient(Db, config, new HttpClient(_manejador));

            var primero = await cliente.ObtenerTokenAsync();
            var segundo = await cliente.ObtenerTokenAsync();

            Assert.Equal("token-largo", primero);
            Assert.Equal("token-largo", segundo);
            Assert.Equal(1, llamadas);
        }

        [Fact]
        public async Task ObtenerToken_SinSesion_LanzaClaro()
        {
            var cliente = CrearCliente();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.ObtenerTokenAsync());
            Assert.Contains("Conectar con Google", ex.Message);
        }

        [Fact]
        public void CrearSiConectado_SinRefresh_DevuelveNull()
        {
            Assert.Null(GoogleOAuthClient.CrearSiConectado(Db));
        }

        [Theory]
        [InlineData("http://127.0.0.1:8080/?code=4/ABC123&scope=x", "4/ABC123")]
        [InlineData("4/XYZ789", "4/XYZ789")]
        [InlineData("", null)]
        [InlineData("http://127.0.0.1:8080/?error=access_denied", null)]
        public void ExtraerCodigo_ParseaUrlOCodigoDirecto(string entrada, string? esperado)
        {
            Assert.Equal(esperado, CodigoAuthDialog.ExtraerCodigo(entrada));
        }
    }
}
