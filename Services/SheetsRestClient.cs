using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LabInventario.Services
{
    /// <summary>
    /// Implementación de <see cref="ISheetsClient"/> sobre la API REST v4
    /// de Google Sheets. El token Bearer se obtiene con la función
    /// inyectada: esta clase no sabe si viene de OAuth o de Service
    /// Account, solo lo usa. Sin paquetes extra (HttpClient + JSON del
    /// propio .NET).
    /// </summary>
    public class SheetsRestClient : ISheetsClient
    {
        private const string Base = "https://sheets.googleapis.com/v4/spreadsheets";

        private readonly HttpClient _http;
        private readonly Func<CancellationToken, Task<string>> _obtenerToken;

        public SheetsRestClient(HttpClient http, Func<CancellationToken, Task<string>> obtenerToken)
        {
            _http = http;
            _obtenerToken = obtenerToken;
        }

        public async Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            var url = $"{Base}/{spreadsheetId}/values/{Uri.EscapeDataString(rango)}";
            using var respuesta = await EnviarAsync(HttpMethod.Get, url, null, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var valores = new List<List<string>>();
            if (doc.RootElement.TryGetProperty("values", out var filas))
            {
                foreach (var fila in filas.EnumerateArray())
                    valores.Add(fila.EnumerateArray().Select(c => c.GetString() ?? "").ToList());
            }
            return new RangoValores { Rango = rango, Valores = valores };
        }

        public async Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            var url = $"{Base}/{spreadsheetId}/values/{Uri.EscapeDataString(rango)}:append" +
                      "?valueInputOption=USER_ENTERED&insertDataOption=INSERT_ROWS";
            using var respuesta = await EnviarAsync(HttpMethod.Post, url, new { values = filas }, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("updates", out var updates) &&
                updates.TryGetProperty("updatedRows", out var confirmadas))
                return confirmadas.GetInt32();
            return filas.Count;
        }

        public async Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            var url = $"{Base}/{spreadsheetId}/values/{Uri.EscapeDataString(rango)}?valueInputOption=USER_ENTERED";
            using var _ = await EnviarAsync(HttpMethod.Put, url, new { values = filas }, ct);
        }

        public async Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            var url = $"{Base}/{spreadsheetId}/values/{Uri.EscapeDataString(rango)}:clear";
            using var _ = await EnviarAsync(HttpMethod.Post, url, new { }, ct);
        }

        public async Task<string> CrearHojaCalculoAsync(string titulo, CancellationToken ct = default)
        {
            using var respuesta = await EnviarAsync(HttpMethod.Post, Base, new { properties = new { title = titulo } }, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var id = doc.RootElement.TryGetProperty("spreadsheetId", out var prop) ? prop.GetString() ?? "" : "";
            if (id.Length == 0) throw new InvalidOperationException("Google no devolvió el ID de la hoja creada.");
            return id;
        }

        public async Task AsegurarPestanasAsync(string spreadsheetId, string[] pestanas, CancellationToken ct = default)
        {
            using var respuesta = await EnviarAsync(HttpMethod.Get,
                $"{Base}/{spreadsheetId}?fields=sheets.properties.title", null, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("sheets", out var hojas))
            {
                foreach (var hoja in hojas.EnumerateArray())
                {
                    if (hoja.TryGetProperty("properties", out var props) &&
                        props.TryGetProperty("title", out var titulo))
                        existentes.Add(titulo.GetString() ?? "");
                }
            }

            var faltantes = pestanas.Where(p => !existentes.Contains(p)).ToList();
            if (faltantes.Count == 0) return;

            var solicitudes = faltantes
                .Select(p => new { addSheet = new { properties = new { title = p } } })
                .ToList<object>();
            using var _ = await EnviarAsync(HttpMethod.Post,
                $"{Base}/{spreadsheetId}:batchUpdate", new { requests = solicitudes }, ct);
        }

        private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, object? cuerpo, CancellationToken ct)
        {
            using var mensaje = new HttpRequestMessage(metodo, url);
            mensaje.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _obtenerToken(ct));
            if (cuerpo is not null)
                mensaje.Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
            var respuesta = await _http.SendAsync(mensaje, ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                var detalle = "";
                try { detalle = await respuesta.Content.ReadAsStringAsync(ct); } catch { }
                if (detalle.Length > 500) detalle = detalle[..500];
                respuesta.Dispose();
                throw new HttpRequestException($"Google Sheets {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}): {detalle}");
            }
            return respuesta;
        }
    }
}
