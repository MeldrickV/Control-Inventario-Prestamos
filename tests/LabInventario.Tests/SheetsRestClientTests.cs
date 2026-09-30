using System.Text.Json;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class SheetsRestClientTests
    {
        private readonly ManejadorFalso _manejador = new();
        private SheetsRestClient _cliente = null!;

        private void Configurar(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _manejador.Responder = responder;
            _cliente = new SheetsRestClient(new HttpClient(_manejador), _ => Task.FromResult("token-test"));
        }

        private static HttpResponseMessage Json(object cuerpo) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(cuerpo)),
        };

        [Fact]
        public async Task ObtenerValores_LeeRangoYParseaFilas()
        {
            Configurar(_ => Json(new { values = new[] { new[] { "Nombre", "NumeroCuenta" }, new[] { "Ana", "1845868-8" } } }));

            var rango = await _cliente.ObtenerValoresAsync("hoja1", "Alumnos!A:B");

            Assert.Equal(HttpMethod.Get, _manejador.Peticiones[0].Method);
            Assert.Contains("/values/" + Uri.EscapeDataString("Alumnos!A:B"), _manejador.Peticiones[0].RequestUri!.ToString());
            Assert.Equal("Bearer", _manejador.Peticiones[0].Headers.Authorization!.Scheme);
            Assert.Equal(2, rango.Valores.Count);
            Assert.Equal("1845868-8", rango.Valores[1][1]);
        }

        [Fact]
        public async Task ObtenerValores_SinValues_DevuelveVacio()
        {
            Configurar(_ => Json(new { range = "Alumnos!A:B" }));

            var rango = await _cliente.ObtenerValoresAsync("hoja1", "Alumnos!A:B");

            Assert.Empty(rango.Valores);
        }

        [Fact]
        public async Task AgregarFilas_UsaAppendYDevuelveConfirmadas()
        {
            Configurar(_ => Json(new { updates = new { updatedRows = 2 } }));

            var confirmadas = await _cliente.AgregarFilasAsync("hoja1", "Historial!A:K",
                new List<List<string>> { new() { "1" }, new() { "2" } });

            Assert.Equal(2, confirmadas);
            var url = _manejador.Peticiones[0].RequestUri!.ToString();
            Assert.Contains(":append", url);
            Assert.Contains("valueInputOption=USER_ENTERED", url);
        }

        [Fact]
        public async Task ActualizarValores_UsaPut()
        {
            Configurar(_ => Json(new { updatedCells = 2 }));

            await _cliente.ActualizarValoresAsync("hoja1", "Alumnos!A1",
                new List<List<string>> { new() { "Nombre", "NumeroCuenta" } });

            Assert.Equal(HttpMethod.Put, _manejador.Peticiones[0].Method);
        }

        [Fact]
        public async Task LimpiarRango_UsaClear()
        {
            Configurar(_ => Json(new { clearedRange = "Alumnos!A:B" }));

            await _cliente.LimpiarRangoAsync("hoja1", "Alumnos!A:B");

            Assert.Equal(HttpMethod.Post, _manejador.Peticiones[0].Method);
            Assert.Contains(":clear", _manejador.Peticiones[0].RequestUri!.ToString());
        }

        [Fact]
        public async Task CrearHojaCalculo_DevuelveId()
        {
            string? cuerpo = null;
            Configurar(req =>
            {
                cuerpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(new { spreadsheetId = "nueva-hoja-123" });
            });

            var id = await _cliente.CrearHojaCalculoAsync("LabInventario - LAB-1");

            Assert.Equal("nueva-hoja-123", id);
            Assert.Contains("LabInventario - LAB-1", cuerpo!);
        }

        [Fact]
        public async Task AsegurarPestanas_SoloCreaLasFaltantes()
        {
            string? cuerpoBatch = null;
            Configurar(req =>
            {
                if (req.Method == HttpMethod.Get)
                    return Json(new { sheets = new[] { new { properties = new { title = "Alumnos" } } } });
                cuerpoBatch = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(new { });
            });

            await _cliente.AsegurarPestanasAsync("hoja1", new[] { "Historial", "Alumnos", "Inventario" });

            Assert.Single(_manejador.Peticiones, p => p.Method == HttpMethod.Post);
            Assert.Contains("Historial", cuerpoBatch!);
            Assert.Contains("Inventario", cuerpoBatch!);
            Assert.DoesNotContain("\"title\":\"Alumnos\"", cuerpoBatch!);
        }

        [Fact]
        public async Task ErrorDeGoogle_LanzaHttpRequestException()
        {
            Configurar(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"message\":\"denegado\"}}"),
            });

            await Assert.ThrowsAsync<HttpRequestException>(() => _cliente.ObtenerValoresAsync("hoja1", "Alumnos!A:B"));
        }
    }
}
