using LabInventario.Services;

namespace LabInventario.Tests
{
    /// <summary>Manejador HTTP falso: captura peticiones y responde lo programado.</summary>
    internal sealed class ManejadorFalso : HttpMessageHandler
    {
        public List<HttpRequestMessage> Peticiones { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticiones.Add(request);
            return Task.FromResult(Responder(request));
        }
    }
}
