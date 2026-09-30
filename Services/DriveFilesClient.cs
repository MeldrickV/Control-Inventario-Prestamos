using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace LabInventario.Services
{
    /// <summary>
    /// Implementación de <see cref="ISheetsClient"/> sobre archivos .xlsx
    /// guardados en el Google Drive de la cuenta (API Drive v3). Cada
    /// "spreadsheetId" de la interfaz es el ID de un archivo .xlsx y cada
    /// rango indica su pestaña ("Alumnos!A:B"): se descarga el archivo,
    /// se opera la pestaña con ClosedXML y se vuelve a subir.
    ///
    /// La app crea y posee todos los archivos que toca (carpeta
    /// "LabInventario"), así que basta el alcance drive.file: mínimo
    /// privilegio, sin verificación dura de Google y sin instalar nada en
    /// la PC. El revisor edita el contenido en su lugar desde Drive web.
    /// </summary>
    public class DriveFilesClient : ISheetsClient
    {
        private const string Base = "https://www.googleapis.com/drive/v3/files";
        private const string Subida = "https://www.googleapis.com/upload/drive/v3/files";
        private const string NombreCarpeta = "LabInventario";
        private const string MimeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private const string MimeCarpeta = "application/vnd.google-apps.folder";

        private readonly HttpClient _http;
        private readonly Func<CancellationToken, Task<string>> _obtenerToken;

        public DriveFilesClient(HttpClient http, Func<CancellationToken, Task<string>> obtenerToken)
        {
            _http = http;
            _obtenerToken = obtenerToken;
        }

        public async Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            using var libro = await DescargarLibroAsync(spreadsheetId, ct);
            return new RangoValores { Rango = rango, Valores = LeerPestana(libro, NombrePestana(rango)) };
        }

        public async Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            if (filas.Count == 0) return 0;
            using var libro = await DescargarLibroAsync(spreadsheetId, ct);
            var hoja = ObtenerOCrearPestana(libro, NombrePestana(rango));
            var siguiente = hoja.LastRowUsed()?.RowNumber() + 1 ?? 1;
            EscribirFilas(hoja, siguiente, filas);
            await SubirLibroAsync(spreadsheetId, libro, ct);
            return filas.Count;
        }

        public async Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            using var libro = await DescargarLibroAsync(spreadsheetId, ct);
            var hoja = ObtenerOCrearPestana(libro, NombrePestana(rango));
            hoja.Clear();
            EscribirFilas(hoja, 1, filas);
            await SubirLibroAsync(spreadsheetId, libro, ct);
        }

        public async Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            using var libro = await DescargarLibroAsync(spreadsheetId, ct);
            var hoja = libro.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, NombrePestana(rango), StringComparison.OrdinalIgnoreCase));
            hoja?.Clear();
            await SubirLibroAsync(spreadsheetId, libro, ct);
        }

        public async Task<string> CrearHojaCalculoAsync(string titulo, CancellationToken ct = default)
        {
            var carpetaId = await AsegurarCarpetaAsync(ct);
            var existente = await BuscarArchivoAsync(titulo, carpetaId, ct);
            if (existente is not null) return existente;

            using var libro = new XLWorkbook();
            libro.Worksheets.Add("Historial");
            libro.Worksheets.Add("Alumnos");
            libro.Worksheets.Add("Inventario");
            var bytes = GuardarBytes(libro);

            using var contenido = new MultipartFormDataContent();
            contenido.Add(new StringContent(JsonSerializer.Serialize(new
            {
                name = titulo,
                mimeType = MimeXlsx,
                parents = new[] { carpetaId },
            }), Encoding.UTF8, "application/json"));
            contenido.Add(new ByteArrayContent(bytes), "file", titulo + ".xlsx");

            using var respuesta = await EnviarAsync(HttpMethod.Post, Subida + "?uploadType=multipart", contenido, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var id = doc.RootElement.TryGetProperty("id", out var prop) ? prop.GetString() ?? "" : "";
            if (id.Length == 0) throw new InvalidOperationException("Google Drive no devolvió el ID del archivo creado.");
            return id;
        }

        public async Task AsegurarPestanasAsync(string spreadsheetId, string[] pestanas, CancellationToken ct = default)
        {
            using var libro = await DescargarLibroAsync(spreadsheetId, ct);
            var cambio = false;
            foreach (var pestana in pestanas)
            {
                if (!libro.Worksheets.Any(w => string.Equals(w.Name, pestana, StringComparison.OrdinalIgnoreCase)))
                {
                    libro.Worksheets.Add(pestana);
                    cambio = true;
                }
            }
            if (cambio) await SubirLibroAsync(spreadsheetId, libro, ct);
        }

        // ---------------- Internos ----------------

        private static string NombrePestana(string rango)
        {
            var corte = rango.IndexOf('!');
            return (corte < 0 ? rango : rango[..corte]).Trim();
        }

        private static List<List<string>> LeerPestana(XLWorkbook libro, string pestana)
        {
            var valores = new List<List<string>>();
            var hoja = libro.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, pestana, StringComparison.OrdinalIgnoreCase));
            if (hoja is null) return valores;
            foreach (var fila in hoja.RowsUsed())
            {
                var ultima = fila.LastCellUsed()?.Address.ColumnNumber ?? 0;
                var celdas = new List<string>();
                for (var c = 1; c <= ultima; c++)
                    celdas.Add(fila.Cell(c).GetString());
                valores.Add(celdas);
            }
            return valores;
        }

        private static IXLWorksheet ObtenerOCrearPestana(XLWorkbook libro, string pestana) =>
            libro.Worksheets.FirstOrDefault(w =>
                string.Equals(w.Name, pestana, StringComparison.OrdinalIgnoreCase))
            ?? libro.Worksheets.Add(pestana);

        private static void EscribirFilas(IXLWorksheet hoja, int filaInicial, List<List<string>> filas)
        {
            var fila = filaInicial;
            foreach (var valores in filas)
            {
                for (var c = 0; c < valores.Count; c++)
                    hoja.Cell(fila, c + 1).Value = valores[c] ?? "";
                fila++;
            }
        }

        private static byte[] GuardarBytes(XLWorkbook libro)
        {
            using var salida = new MemoryStream();
            libro.SaveAs(salida);
            return salida.ToArray();
        }

        private async Task<XLWorkbook> DescargarLibroAsync(string archivoId, CancellationToken ct)
        {
            using var respuesta = await EnviarAsync(HttpMethod.Get, $"{Base}/{archivoId}?alt=media", null, ct);
            var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
            return new XLWorkbook(new MemoryStream(bytes));
        }

        private async Task SubirLibroAsync(string archivoId, XLWorkbook libro, CancellationToken ct)
        {
            using var contenido = new ByteArrayContent(GuardarBytes(libro));
            contenido.Headers.ContentType = new MediaTypeHeaderValue(MimeXlsx);
            using var _ = await EnviarAsync(new HttpMethod("PATCH"), $"{Subida}/{archivoId}?uploadType=media", contenido, ct);
        }

        private async Task<string> AsegurarCarpetaAsync(CancellationToken ct)
        {
            var url = $"{Base}?q={Uri.EscapeDataString($"mimeType='{MimeCarpeta}' and name='{NombreCarpeta}' and trashed=false")}" +
                      "&fields=files(id)&pageSize=1";
            using var respuesta = await EnviarAsync(HttpMethod.Get, url, null, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("files", out var archivos))
            {
                foreach (var archivo in archivos.EnumerateArray())
                {
                    var id = archivo.TryGetProperty("id", out var prop) ? prop.GetString() ?? "" : "";
                    if (id.Length > 0) return id;
                }
            }

            using var contenido = new StringContent(JsonSerializer.Serialize(new
            {
                name = NombreCarpeta,
                mimeType = MimeCarpeta,
            }), Encoding.UTF8, "application/json");
            using var creada = await EnviarAsync(HttpMethod.Post, Base, contenido, ct);
            using var docCreada = JsonDocument.Parse(await creada.Content.ReadAsStringAsync(ct));
            var nuevoId = docCreada.RootElement.TryGetProperty("id", out var p) ? p.GetString() ?? "" : "";
            if (nuevoId.Length == 0) throw new InvalidOperationException("Google Drive no devolvió el ID de la carpeta creada.");
            return nuevoId;
        }

        private async Task<string?> BuscarArchivoAsync(string titulo, string carpetaId, CancellationToken ct)
        {
            var consulta = $"name='{titulo.Replace("'", "\\'")}' and '{carpetaId}' in parents and trashed=false";
            var url = $"{Base}?q={Uri.EscapeDataString(consulta)}&fields=files(id)&pageSize=1";
            using var respuesta = await EnviarAsync(HttpMethod.Get, url, null, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("files", out var archivos))
            {
                foreach (var archivo in archivos.EnumerateArray())
                {
                    var id = archivo.TryGetProperty("id", out var prop) ? prop.GetString() ?? "" : "";
                    if (id.Length > 0) return id;
                }
            }
            return null;
        }

        private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, HttpContent? cuerpo, CancellationToken ct)
        {
            using var mensaje = new HttpRequestMessage(metodo, url);
            mensaje.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _obtenerToken(ct));
            if (cuerpo is not null) mensaje.Content = cuerpo;
            var respuesta = await _http.SendAsync(mensaje, ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                var codigo = (int)respuesta.StatusCode;
                var detalle = "";
                try { detalle = await respuesta.Content.ReadAsStringAsync(ct); } catch { }
                if (detalle.Length > 500) detalle = detalle[..500];
                respuesta.Dispose();
                if (codigo == 403)
                    throw new InvalidOperationException(
                        "Google denegó el acceso (403). Desconecta y vuelve a conectar la cuenta " +
                        "para otorgar el permiso de Drive. " + detalle);
                throw new HttpRequestException($"Google Drive {codigo} ({respuesta.ReasonPhrase}): {detalle}");
            }
            return respuesta;
        }
    }
}
