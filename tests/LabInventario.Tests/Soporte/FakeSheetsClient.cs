using LabInventario.Services;

namespace LabInventario.Tests
{
    /// <summary>
    /// Doble en memoria de <see cref="ISheetsClient"/>: guarda una rejilla
    /// por pestaña de cada spreadsheet y permite simular fallos de red.
    /// Toda la lógica de sincronización se prueba contra este doble, sin
    /// red ni credenciales reales.
    /// </summary>
    internal sealed class FakeSheetsClient : ISheetsClient
    {
        public bool FallarRed { get; set; }

        private readonly Dictionary<string, List<List<string>>> _pestanas = new();

        private static string Clave(string spreadsheetId, string rango) =>
            spreadsheetId + "|" + rango.Split('!')[0];

        public void Sembrar(string spreadsheetId, string pestana, List<List<string>> filas) =>
            _pestanas[spreadsheetId + "|" + pestana] = filas.Select(f => f.ToList()).ToList();

        public List<List<string>> Leer(string spreadsheetId, string pestana) =>
            _pestanas.TryGetValue(spreadsheetId + "|" + pestana, out var filas)
                ? filas.Select(f => f.ToList()).ToList()
                : new List<List<string>>();

        private void RevisarRed()
        {
            if (FallarRed) throw new HttpRequestException("Sin red (simulado).");
        }

        public Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            RevisarRed();
            return Task.FromResult(new RangoValores { Rango = rango, Valores = Leer(spreadsheetId, rango.Split('!')[0]) });
        }

        public Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            RevisarRed();
            var clave = Clave(spreadsheetId, rango);
            if (!_pestanas.TryGetValue(clave, out var rejilla))
            {
                rejilla = new List<List<string>>();
                _pestanas[clave] = rejilla;
            }
            foreach (var fila in filas) rejilla.Add(fila.ToList());
            return Task.FromResult(filas.Count);
        }

        public Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default)
        {
            RevisarRed();
            _pestanas[Clave(spreadsheetId, rango)] = filas.Select(f => f.ToList()).ToList();
            return Task.CompletedTask;
        }

        public Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default)
        {
            RevisarRed();
            _pestanas[Clave(spreadsheetId, rango)] = new List<List<string>>();
            return Task.CompletedTask;
        }

        private int _contadorHojas;

        public Task<string> CrearHojaCalculoAsync(string titulo, CancellationToken ct = default)
        {
            RevisarRed();
            _contadorHojas++;
            return Task.FromResult($"hoja-fake-{_contadorHojas}");
        }

        public Task AsegurarPestanasAsync(string spreadsheetId, string[] pestanas, CancellationToken ct = default)
        {
            RevisarRed();
            foreach (var pestana in pestanas)
            {
                var clave = spreadsheetId + "|" + pestana;
                if (!_pestanas.ContainsKey(clave)) _pestanas[clave] = new List<List<string>>();
            }
            return Task.CompletedTask;
        }
    }
}
