using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LabInventario.Data;

namespace LabInventario.Services
{
    /// <summary>
    /// Autenticación con Google por OAuth 2.0 ("Conectar con Google"): el
    /// administrador inicia sesión una vez en el navegador y la app guarda
    /// el refresh token en la base local (cifrada). De ahí en adelante
    /// todo es automático, incluido crear la hoja si no existe.
    ///
    /// Implementa <see cref="ISheetsClient"/> delegando en
    /// <see cref="SheetsRestClient"/>: cuando la autenticación está lista,
    /// una instancia de esta clase ES el cliente que usa
    /// <see cref="SincronizacionService"/>.
    ///
    /// El ClientId/Secret los pega UNA vez el responsable del despliegue
    /// (un OAuth Client ID tipo "App de escritorio" en Google Cloud); los
    /// usuarios finales nunca tocan la consola de Google.
    /// </summary>
    public class GoogleOAuthClient : ISheetsClient
    {
        // TODO(despliegue): crear un OAuth Client ID ("App de escritorio")
        // en Google Cloud con acceso a Google Sheets API y pegar aquí los
        // valores. Sin esto, ConectarAsync explica qué falta.
        public const string ClientId = "TU_CLIENT_ID_DE_GOOGLE";
        public const string ClientSecret = "TU_CLIENT_SECRET_DE_GOOGLE";

        public const string ClaveRefreshToken = "Sync.OAuthRefreshToken";

        private const string Scope = "https://www.googleapis.com/auth/spreadsheets";
        private const string UrlAutorizar = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string UrlToken = "https://oauth2.googleapis.com/token";
        private const string UrlRevocar = "https://oauth2.googleapis.com/revoke";

        private readonly ConfiguracionRepository _config;
        private readonly HttpClient _http;
        private readonly SheetsRestClient _rest;
        private string? _accessToken;
        private DateTime _expira = DateTime.MinValue;

        public GoogleOAuthClient(DatabaseManager? db = null, ConfiguracionRepository? config = null, HttpClient? http = null)
        {
            var baseDatos = db ?? DatabaseManager.Instancia;
            _config = config ?? new ConfiguracionRepository(baseDatos);
            _http = http ?? new HttpClient();
            _rest = new SheetsRestClient(_http, ObtenerTokenAsync);
        }

        /// <summary>El responsable del despliegue ya pegó ClientId y Secret.</summary>
        public static bool ConfiguradoOAuth =>
            ClientId != "TU_CLIENT_ID_DE_GOOGLE" && ClientSecret != "TU_CLIENT_SECRET_DE_GOOGLE";

        /// <summary>Hay refresh token guardado: la cuenta ya se conectó.</summary>
        public bool EstaConectado => !string.IsNullOrWhiteSpace(_config.Obtener(ClaveRefreshToken));

        /// <summary>Si hay sesión guardada devuelve el cliente listo; si no, null.</summary>
        public static GoogleOAuthClient? CrearSiConectado(DatabaseManager? db = null)
        {
            var cliente = new GoogleOAuthClient(db);
            return cliente.EstaConectado ? cliente : null;
        }

        /// <summary>
        /// Conecta la cuenta: abre el navegador para iniciar sesión y
        /// guarda el refresh token. Devuelve false si el usuario cancela.
        /// <paramref name="pedirCodigoManual"/> se usa solo si el regreso
        /// automático del navegador falla (recibe la URL de autorización y
        /// devuelve la URL con el código pegada por el usuario, o null).
        /// </summary>
        public async Task<bool> ConectarAsync(Func<string, Task<string?>>? pedirCodigoManual = null, CancellationToken ct = default)
        {
            if (!ConfiguradoOAuth)
                throw new InvalidOperationException(
                    "Falta el OAuth Client ID en el código (GoogleOAuthClient.ClientId/Secret). " +
                    "El responsable del despliegue debe crearlo una vez en Google Cloud.");

            var puerto = PuertoLibre();
            var redirectUri = $"http://127.0.0.1:{puerto}/";
            var url = $"{UrlAutorizar}?client_id={Uri.EscapeDataString(ClientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      "&response_type=code" +
                      $"&scope={Uri.EscapeDataString(Scope)}" +
                      "&access_type=offline&prompt=consent";

            string? codigo = null;
            try
            {
                using var listener = new HttpListener();
                listener.Prefixes.Add(redirectUri);
                listener.Start();
                try
                {
                    AbrirNavegador(url);
                    codigo = await EsperarCodigoAsync(listener, ct);
                }
                finally
                {
                    listener.Stop();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is InvalidOperationException)
            {
                if (pedirCodigoManual is null) throw;
                var pegado = await pedirCodigoManual(url);
                codigo = string.IsNullOrWhiteSpace(pegado) ? null : Dialogs.CodigoAuthDialog.ExtraerCodigo(pegado);
            }

            if (string.IsNullOrWhiteSpace(codigo)) return false;
            await IntercambiarCodigoAsync(codigo, redirectUri, ct);
            return true;
        }

        /// <summary>Desconecta la cuenta: revoca el token y borra lo guardado.</summary>
        public async Task DesconectarAsync(CancellationToken ct = default)
        {
            var refresh = _config.Obtener(ClaveRefreshToken) ?? "";
            if (refresh.Length > 0)
            {
                try
                {
                    using var contenido = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh });
                    using var _ = await _http.PostAsync(UrlRevocar, contenido, ct);
                }
                catch { }
            }
            _config.Establecer(ClaveRefreshToken, "");
            _accessToken = null;
            _expira = DateTime.MinValue;
        }

        /// <summary>Access token vigente (usa el guardado o refresca).</summary>
        public async Task<string> ObtenerTokenAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTime.UtcNow < _expira)
                return _accessToken;

            var refresh = _config.Obtener(ClaveRefreshToken) ?? "";
            if (refresh.Length == 0)
                throw new InvalidOperationException("Cuenta de Google no conectada. Usa «Conectar con Google».");

            using var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["refresh_token"] = refresh,
                ["grant_type"] = "refresh_token",
            });
            using var respuesta = await _http.PostAsync(UrlToken, contenido, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (!respuesta.IsSuccessStatusCode)
                throw new InvalidOperationException("Google rechazó la sesión guardada; vuelve a conectar la cuenta. " +
                    DetalleError(doc));
            GuardarTokens(doc.RootElement);
            return _accessToken!;
        }

        /// <summary>Canjea el código de autorización por tokens y los guarda. Pública para poder probarse.</summary>
        public async Task IntercambiarCodigoAsync(string codigo, string redirectUri, CancellationToken ct = default)
        {
            using var contenido = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = codigo,
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
            });
            using var respuesta = await _http.PostAsync(UrlToken, contenido, ct);
            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (!respuesta.IsSuccessStatusCode)
                throw new InvalidOperationException("Google rechazó el código de autorización. " + DetalleError(doc));
            GuardarTokens(doc.RootElement);
        }

        private void GuardarTokens(JsonElement raiz)
        {
            _accessToken = raiz.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "";
            var segundos = raiz.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;
            _expira = DateTime.UtcNow.AddSeconds(Math.Max(0, segundos - 60));
            if (raiz.TryGetProperty("refresh_token", out var rt) && (rt.GetString() ?? "").Length > 0)
                _config.Establecer(ClaveRefreshToken, rt.GetString()!);
            if (_accessToken.Length == 0)
                throw new InvalidOperationException("Google no devolvió access token.");
        }

        private static string DetalleError(JsonDocument doc)
        {
            if (doc.RootElement.TryGetProperty("error_description", out var d))
                return d.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString() ?? "";
            return "";
        }

        private static void AbrirNavegador(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo abrir el navegador automáticamente.", ex);
            }
        }

        private static async Task<string?> EsperarCodigoAsync(HttpListener listener, CancellationToken ct)
        {
            using var espera = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var ligado = CancellationTokenSource.CreateLinkedTokenSource(ct, espera.Token);
            var tareaContexto = listener.GetContextAsync();
            var tareaCancelar = Task.Delay(Timeout.Infinite, ligado.Token);
            var terminada = await Task.WhenAny(tareaContexto, tareaCancelar);
            if (terminada != tareaContexto) return null;

            var contexto = await tareaContexto;
            try
            {
                var codigo = contexto.Request.QueryString["code"];
                var error = contexto.Request.QueryString["error"];
                var html = string.IsNullOrWhiteSpace(codigo)
                    ? "<html><body><h2>No se autorizó el acceso.</h2><p>Puedes cerrar esta ventana y reintentar.</p></body></html>"
                    : "<html><body><h2>Cuenta conectada.</h2><p>Puedes cerrar esta ventana y volver a la aplicación.</p></body></html>";
                var bytes = System.Text.Encoding.UTF8.GetBytes(html);
                contexto.Response.ContentType = "text/html; charset=utf-8";
                await contexto.Response.OutputStream.WriteAsync(bytes, ct);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException("Google no autorizó el acceso (" + error + ").");
                return codigo;
            }
            finally
            {
                try { contexto.Response.Close(); } catch { }
            }
        }

        private static int PuertoLibre()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var puerto = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return puerto;
        }

        // ISheetsClient: todo se delega al cliente REST con el token OAuth.
        public Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default) =>
            _rest.ObtenerValoresAsync(spreadsheetId, rango, ct);
        public Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default) =>
            _rest.AgregarFilasAsync(spreadsheetId, rango, filas, ct);
        public Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default) =>
            _rest.ActualizarValoresAsync(spreadsheetId, rango, filas, ct);
        public Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default) =>
            _rest.LimpiarRangoAsync(spreadsheetId, rango, ct);
        public Task<string> CrearHojaCalculoAsync(string titulo, CancellationToken ct = default) =>
            _rest.CrearHojaCalculoAsync(titulo, ct);
        public Task AsegurarPestanasAsync(string spreadsheetId, string[] pestanas, CancellationToken ct = default) =>
            _rest.AsegurarPestanasAsync(spreadsheetId, pestanas, ct);
    }
}
