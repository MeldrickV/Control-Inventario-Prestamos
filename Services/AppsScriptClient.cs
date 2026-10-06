using System.Text;
using System.Text.Json;
using LabInventario.Data;

namespace LabInventario.Services
{
    /// <summary>
    /// Implementación de <see cref="ISheetsClient"/> sobre un Google Apps
    /// Script desplegado como app web en la cuenta del laboratorio
    /// (ver <c>apps-script/Codigo.gs</c>). Sin OAuth, sin consola de Cloud:
    /// solo la URL del despliegue y una clave compartida, ambas
    /// configuradas en el diálogo del administrador.
    ///
    /// El script administra UN spreadsheet con pestañas Historial,
    /// Alumnos, Inventario y Cambios_Alumnos/Cambios_Inventario. El
    /// <c>spreadsheetId</c> de la interfaz selecciona la tienda lógica:
    /// "principal" (Historial/Alumnos/Inventario) o "cambios"
    /// (Cambios_Alumnos/Cambios_Inventario, lo que el revisor edita).
    /// </summary>
    public class AppsScriptClient : ISheetsClient
    {
        public const string ClaveScriptUrl = "Sync.ScriptUrl";
        public const string ClaveSecreta = "Sync.ClaveSecreta";

        public const string TiendaPrincipal = "principal";
        public const string TiendaCambios = "cambios";

        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _clave;

        public AppsScriptClient(HttpClient http, string url, string clave)
        {
            _http = http;
            _url = url.Trim().TrimEnd('/');
            _clave = clave;
        }

        /// <summary>Si hay URL y clave guardadas devuelve el cliente listo; si no, null.</summary>
        public static AppsScriptClient? CrearSiConectado(DatabaseManager? db = null, ConfiguracionRepository? config = null, HttpClient? http = null)
        {
            var baseDatos = db ?? DatabaseManager.Instancia;
            var repo = config ?? new ConfiguracionRepository(baseDatos);
            var url = repo.Obtener(ClaveScriptUrl) ?? "";
            var clave = repo.Obtener(ClaveSecreta) ?? "";
            if (url.Length == 0 || clave.Length == 0) return null;
            return new AppsScriptClient(http ?? new HttpClient(), url, clave);
        }

        public async Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            var pestana = ResolverPestana(spreadsheetId, rango);
            var url = $"{_url}?clave={Uri.EscapeDataString(_clave)}&accion=leer&pestana={Uri.EscapeDataString(pestana)}";
            using var respuesta = await _http.GetAsync(url, ct);
            var doc = await LeerJsonAsync(respuesta, ct);
            var valores = new List<List<string>>();
            if (doc.RootElement.TryGetProperty("valores", out var filas))
            {
                foreach (var fila in filas.EnumerateArray())
                    valores.Add(fila.EnumerateArray().Select(c => c.GetString() ?? "").ToList());
            }
            return new RangoValores { Rango = rango, Valores = valores };
        }

        public async Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            if (filas.Count == 0) return 0;
            var doc = await PublicarAsync("agregar", ResolverPestana(spreadsheetId, rango), filas, ct);
            if (doc.RootElement.TryGetProperty("confirmadas", out var confirmadas))
                return confirmadas.GetInt32();
            return filas.Count;
        }

        public async Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            await PublicarAsync("reescribir", ResolverPestana(spreadsheetId, rango), filas, ct);
        }

        public async Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            await PublicarAsync("reescribir", ResolverPestana(spreadsheetId, rango), new List<List<string>>(), ct);
        }

        public async Task<string> CrearHojaCalculoAsync(string titulo, CancellationToken ct = default)
        {
            await AsegurarAsync(ct);
            return TiendaPrincipal;
        }

        public async Task AsegurarPestanasAsync(string spreadsheetId, string[] pestanas, CancellationToken ct = default)
        {
            await AsegurarAsync(ct);
        }

        public async Task<(int Agregadas, int Actualizadas)> SincronizarHistorialAsync(
            string spreadsheetId, string pestana, string[] encabezado, List<List<string>> filas,
            CancellationToken ct = default)
        {
            using var contenido = new StringContent(
                JsonSerializer.Serialize(new
                {
                    clave = _clave,
                    accion = "sincronizarHistorial",
                    pestana = ResolverPestana(spreadsheetId, pestana),
                    encabezado,
                    filas,
                }),
                Encoding.UTF8, "application/json");
            using var respuesta = await _http.PostAsync(_url, contenido, ct);
            var doc = await LeerJsonAsync(respuesta, ct);
            using (doc)
            {
                var agregadas = doc.RootElement.TryGetProperty("agregadas", out var a) ? a.GetInt32() : 0;
                var actualizadas = doc.RootElement.TryGetProperty("actualizadas", out var m) ? m.GetInt32() : 0;
                return (agregadas, actualizadas);
            }
        }

        /// <summary>Llama a `asegurar` del script (crea pestañas+encabezados si faltan).</summary>
        public async Task AsegurarAsync(CancellationToken ct = default)
        {
            using var contenido = new StringContent(
                JsonSerializer.Serialize(new { clave = _clave, accion = "asegurar" }),
                Encoding.UTF8, "application/json");
            using var respuesta = await _http.PostAsync(_url, contenido, ct);
            await LeerJsonAsync(respuesta, ct);
        }

        private static string ResolverPestana(string tienda, string rango)
        {
            var corte = rango.IndexOf('!');
            var pestana = (corte < 0 ? rango : rango[..corte]).Trim();
            if (tienda == TiendaCambios && (pestana == "Alumnos" || pestana == "Inventario"))
                return "Cambios_" + pestana;
            return pestana;
        }

        private async Task<JsonDocument> PublicarAsync(string accion, string pestana, List<List<string>> filas, CancellationToken ct)
        {
            using var contenido = new StringContent(
                JsonSerializer.Serialize(new { clave = _clave, accion, pestana, filas }),
                Encoding.UTF8, "application/json");
            using var respuesta = await _http.PostAsync(_url, contenido, ct);
            return await LeerJsonAsync(respuesta, ct);
        }

        private static async Task<JsonDocument> LeerJsonAsync(HttpResponseMessage respuesta, CancellationToken ct)
        {
            using (respuesta)
            {
                var texto = await respuesta.Content.ReadAsStringAsync(ct);
                if (!respuesta.IsSuccessStatusCode)
                {
                    var detalle = texto.Length > 500 ? texto[..500] : texto;
                    throw new HttpRequestException($"Script {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}): {detalle}");
                }
                var doc = JsonDocument.Parse(texto);
                if (doc.RootElement.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                {
                    var error = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
                    doc.Dispose();
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "El script rechazó la petición." : error);
                }
                return doc;
            }
        }
    }
}
