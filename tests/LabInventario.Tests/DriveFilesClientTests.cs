using System.Text.Json;
using ClosedXML.Excel;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class DriveFilesClientTests
    {
        private readonly ManejadorFalso _manejador = new();
        private DriveFilesClient _cliente = null!;

        private static byte[] XlsxCon(params (string pestana, string[][] filas)[] pestanas)
        {
            using var libro = new XLWorkbook();
            foreach (var (pestana, filas) in pestanas)
            {
                var hoja = libro.Worksheets.Add(pestana);
                for (var f = 0; f < filas.Length; f++)
                    for (var c = 0; c < filas[f].Length; c++)
                        hoja.Cell(f + 1, c + 1).Value = filas[f][c];
            }
            using var salida = new MemoryStream();
            libro.SaveAs(salida);
            return salida.ToArray();
        }

        private static List<List<string>> LeerXlsx(byte[] bytes, string pestana)
        {
            using var libro = new XLWorkbook(new MemoryStream(bytes));
            var hoja = libro.Worksheets.First(w => w.Name == pestana);
            return hoja.RowsUsed()
                .Select(f => Enumerable.Range(1, f.LastCellUsed()!.Address.ColumnNumber)
                    .Select(c => f.Cell(c).GetString()).ToList())
                .ToList();
        }

        private void Configurar(byte[] descarga, Func<HttpRequestMessage, HttpResponseMessage>? alEscribir = null)
        {
            _manejador.Responder = req =>
            {
                if (req.RequestUri!.ToString().Contains("alt=media"))
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(descarga) };
                return alEscribir is not null ? alEscribir(req) : JsonOk();
            };
            _cliente = new DriveFilesClient(new HttpClient(_manejador), _ => Task.FromResult("token-test"));
        }

        private static HttpResponseMessage JsonOk(string json = "{}") => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

        [Fact]
        public async Task ObtenerValores_LeePestanaDelXlsx()
        {
            Configurar(XlsxCon(("Alumnos", new[] { new[] { "Nombre", "NumeroCuenta" }, new[] { "Ana", "1845868-8" } })));

            var rango = await _cliente.ObtenerValoresAsync("archivo1", "Alumnos!A:B");

            Assert.Contains("/files/archivo1?alt=media", _manejador.Peticiones[0].RequestUri!.ToString());
            Assert.Equal(2, rango.Valores.Count);
            Assert.Equal("1845868-8", rango.Valores[1][1]);
        }

        [Fact]
        public async Task AgregarFilas_AnexaAlFinalYSube()
        {
            byte[]? subido = null;
            Configurar(XlsxCon(("Historial", new[] { new[] { "PrestamoId" }, new[] { "1" } })),
                req =>
                {
                    subido = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    return JsonOk();
                });

            var confirmadas = await _cliente.AgregarFilasAsync("archivo1", "Historial!A:K",
                new List<List<string>> { new() { "2", "Ana" } });

            Assert.Equal(1, confirmadas);
            Assert.Equal("PATCH", _manejador.Peticiones.Last().Method.Method);
            var filas = LeerXlsx(subido!, "Historial");
            Assert.Equal(3, filas.Count);
            Assert.Equal("2", filas[2][0]);
        }

        [Fact]
        public async Task ActualizarValores_ReescribeDesdeA1()
        {
            byte[]? subido = null;
            Configurar(XlsxCon(("Alumnos", new[] { new[] { "Viejo" }, new[] { "x" } })),
                req =>
                {
                    subido = req.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    return JsonOk();
                });

            await _cliente.ActualizarValoresAsync("archivo1", "Alumnos!A1",
                new List<List<string>> { new() { "Nombre", "NumeroCuenta" }, new() { "Ana", "1845868-8" } });

            var filas = LeerXlsx(subido!, "Alumnos");
            Assert.Equal(2, filas.Count);
            Assert.Equal("1845868-8", filas[1][1]);
        }

        [Fact]
        public async Task CrearHojaCalculo_ReutilizaExistenteSinDuplicar()
        {
            _manejador.Responder = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("mimeType")) // buscar carpeta
                    return JsonOk("{\"files\":[{\"id\":\"carpeta1\"}]}");
                if (req.Method == HttpMethod.Get) // buscar archivo
                    return JsonOk("{\"files\":[{\"id\":\"archivo-existente\"}]}");
                throw new InvalidOperationException("No debió subir nada.");
            };
            _cliente = new DriveFilesClient(new HttpClient(_manejador), _ => Task.FromResult("token-test"));

            var id = await _cliente.CrearHojaCalculoAsync("LabInventario - LAB-1");

            Assert.Equal("archivo-existente", id);
            Assert.DoesNotContain(_manejador.Peticiones, p => p.Method != HttpMethod.Get);
        }

        [Fact]
        public async Task CrearHojaCalculo_CreaCarpetaYArchivo()
        {
            string? cuerpoSubida = null;
            _manejador.Responder = req =>
            {
                var url = req.RequestUri!.ToString();
                if (url.Contains("mimeType")) // buscar carpeta: no existe
                    return JsonOk("{\"files\":[]}");
                if (req.Method == HttpMethod.Get) // buscar archivo: no existe
                    return JsonOk("{\"files\":[]}");
                if (url.Contains("uploadType=multipart"))
                {
                    cuerpoSubida = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return JsonOk("{\"id\":\"archivo-nuevo\"}");
                }
                return JsonOk("{\"id\":\"carpeta-nueva\"}"); // crear carpeta
            };
            _cliente = new DriveFilesClient(new HttpClient(_manejador), _ => Task.FromResult("token-test"));

            var id = await _cliente.CrearHojaCalculoAsync("LabInventario - LAB-1");

            Assert.Equal("archivo-nuevo", id);
            Assert.Contains("LabInventario - LAB-1", cuerpoSubida!);
        }

        [Fact]
        public async Task Error403_LanzaIndicandoReconectar()
        {
            _manejador.Responder = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":{\"message\":\"insufficientPermissions\"}}"),
            };
            _cliente = new DriveFilesClient(new HttpClient(_manejador), _ => Task.FromResult("token-test"));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _cliente.ObtenerValoresAsync("archivo1", "Alumnos!A:B"));
            Assert.Contains("Desconecta", ex.Message);
        }
    }
}
