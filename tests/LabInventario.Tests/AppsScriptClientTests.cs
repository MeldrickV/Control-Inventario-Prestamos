using System.Text.Json;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class AppsScriptClientTests
    {
        private const string Url = "https://script.google.com/macros/s/abc/exec";
        private const string Clave = "clave-test";

        private readonly ManejadorFalso _manejador = new();
        private AppsScriptClient _cliente = null!;

        private void Configurar(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _manejador.Responder = responder;
            _cliente = new AppsScriptClient(new HttpClient(_manejador), Url, Clave);
        }

        private static HttpResponseMessage Json(object cuerpo) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo)),
        };

        [Fact]
        public async Task ObtenerValores_HaceGetConClaveYParsea()
        {
            Configurar(req => req.Method == HttpMethod.Get
                ? Json(new { ok = true, valores = new[] { new[] { "Nombre", "NumeroCuenta" }, new[] { "Ana", "1845868-8" } } })
                : Json(new { ok = true }));

            var rango = await _cliente.ObtenerValoresAsync("principal", "Alumnos!A:B");

            var url = _manejador.Peticiones[0].RequestUri!.ToString();
            Assert.Contains("accion=leer", url);
            Assert.Contains("pestana=Alumnos", url);
            Assert.Contains("clave=" + Clave, url);
            Assert.Equal(2, rango.Valores.Count);
            Assert.Equal("1845868-8", rango.Valores[1][1]);
        }

        [Fact]
        public async Task ObtenerValores_TiendaCambios_MapeaPestana()
        {
            Configurar(_ => Json(new { ok = true, valores = new string[0][] }));

            await _cliente.ObtenerValoresAsync("cambios", "Alumnos!A:B");

            Assert.Contains("pestana=Cambios_Alumnos", _manejador.Peticiones[0].RequestUri!.ToString());
        }

        [Fact]
        public async Task AgregarFilas_HacePostYDevuelveConfirmadas()
        {
            string? cuerpo = null;
            Configurar(req =>
            {
                cuerpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(new { ok = true, confirmadas = 2 });
            });

            var confirmadas = await _cliente.AgregarFilasAsync("principal", "Historial!A:K",
                new List<List<string>> { new() { "1" }, new() { "2" } });

            Assert.Equal(2, confirmadas);
            Assert.Equal(HttpMethod.Post, _manejador.Peticiones[0].Method);
            Assert.Contains("\"accion\":\"agregar\"", cuerpo!);
            Assert.Contains("\"pestana\":\"Historial\"", cuerpo!);
            Assert.Contains(Clave, cuerpo!);
        }

        [Fact]
        public async Task ActualizarYLimpiar_UsanReescribir()
        {
            var acciones = new List<string>();
            Configurar(req =>
            {
                var cuerpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                acciones.Add(JsonDocument.Parse(cuerpo).RootElement.GetProperty("accion").GetString()!);
                return Json(new { ok = true });
            });

            await _cliente.ActualizarValoresAsync("principal", "Alumnos!A1",
                new List<List<string>> { new() { "Nombre", "NumeroCuenta" } });
            await _cliente.LimpiarRangoAsync("principal", "Alumnos!A:B");

            Assert.Equal(new[] { "reescribir", "reescribir" }, acciones.ToArray());
        }

        [Fact]
        public async Task SincronizarHistorial_EnviaEncabezadoYFilas_ParseaConteos()
        {
            string? cuerpo = null;
            Configurar(req =>
            {
                cuerpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(new { ok = true, agregadas = 2, actualizadas = 1 });
            });

            var (agregadas, actualizadas) = await _cliente.SincronizarHistorialAsync(
                "principal", "Historial", new[] { "PrestamoId", "Estado" },
                new List<List<string>> { new() { "1", "Devuelto" } });

            Assert.Equal(2, agregadas);
            Assert.Equal(1, actualizadas);
            Assert.Contains("\"accion\":\"sincronizarHistorial\"", cuerpo!);
            Assert.Contains("\"pestana\":\"Historial\"", cuerpo!);
            Assert.Contains("PrestamoId", cuerpo!);
        }

        [Fact]
        public async Task RespuestaOkFalse_LanzaConMensajeDelScript()
        {
            Configurar(_ => Json(new { ok = false, error = "No autorizado." }));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _cliente.ObtenerValoresAsync("principal", "Alumnos!A:B"));
            Assert.Contains("No autorizado", ex.Message);
        }

        [Fact]
        public async Task ErrorHttp_LanzaHttpRequestException()
        {
            Configurar(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            {
                Content = new StringContent("denegado"),
            });

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                _cliente.ObtenerValoresAsync("principal", "Alumnos!A:B"));
        }
    }
}
