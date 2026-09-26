namespace LabInventario.Services
{
    /// <summary>
    /// Bloque de celdas tal como viaja a la API de Google Sheets
    /// (<c>ValueRange</c>): el rango en notación A1 (p. ej.
    /// <c>Historial!A:K</c>) y las filas como listas de texto.
    /// </summary>
    public sealed class RangoValores
    {
        public string Rango { get; set; } = string.Empty;
        public List<List<string>> Valores { get; set; } = new();
    }

    /// <summary>
    /// Contrato mínimo con Google Sheets que necesita la sincronización:
    /// leer un rango, agregar filas al final, reescribir un rango y
    /// limpiar un rango. La autenticación (Service Account u OAuth, aún
    /// por decidir) vive DENTRO de la implementación concreta, nunca en
    /// esta interfaz: así toda la lógica de negocio se prueba con un
    /// doble en memoria sin red ni credenciales.
    /// </summary>
    public interface ISheetsClient
    {
        /// <summary>Lee las celdas del rango indicado (incluye el encabezado si existe).</summary>
        Task<RangoValores> ObtenerValoresAsync(string spreadsheetId, string rango, CancellationToken ct = default);

        /// <summary>
        /// Agrega filas al final de la pestaña (<c>values.append</c>).
        /// Devuelve cuántas filas confirmó la hoja.
        /// </summary>
        Task<int> AgregarFilasAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default);

        /// <summary>Reescribe el rango desde su primera celda (<c>values.update</c>).</summary>
        Task ActualizarValoresAsync(string spreadsheetId, string rango, List<List<string>> filas, CancellationToken ct = default);

        /// <summary>Vacía el rango indicado (<c>values.clear</c>).</summary>
        Task LimpiarRangoAsync(string spreadsheetId, string rango, CancellationToken ct = default);
    }
}
