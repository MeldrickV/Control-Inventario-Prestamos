using System.Text.RegularExpressions;
using LabInventario.Data;

namespace LabInventario.Services
{
    public enum TipoCodigo { Alumno, Material, Desconocido }

    /// <summary>
    /// Decide, a partir del texto que llega del escáner, si conviene
    /// buscar primero en la tabla de alumnos o en la de materiales.
    ///
    /// Nota de rendimiento: `NumeroCuenta` y `CodigoBarras` son columnas
    /// UNIQUE en SQLite, así que ya tienen un índice implícito y una
    /// búsqueda exacta (`ObtenerPorCuenta` / `ObtenerPorCodigo`) es O(log n)
    /// sin importar cuántos alumnos o materiales haya. El patrón de aquí no
    /// existe para "acelerar" esas búsquedas (ya son rápidas), sino para
    /// no tener que lanzar dos consultas cuando basta con adivinar bien el
    /// orden, y para poder resolver ambigüedades cuando un código podría
    /// interpretarse de las dos formas.
    /// </summary>
    public class DetectorPatrones
    {
        private const string ClavePatron = "PatronCuentaAlumno";

        // Por defecto: números de cuenta / control típicos, solo dígitos,
        // de 6 a 12 caracteres. Ajustable desde Configuración (solo admin)
        // si el número de cuenta real de la institución no calza con esto.
        public const string PatronPorDefecto = @"^\d{6,12}$";

        private readonly ConfiguracionRepository _config = new();

        public string ObtenerPatron() => _config.Obtener(ClavePatron) ?? PatronPorDefecto;

        public void EstablecerPatron(string patron)
        {
            // Validar que compile antes de guardar, para no dejar un
            // patrón roto que tumbe el escaneo la próxima vez.
            _ = new Regex(patron);
            _config.Establecer(ClavePatron, patron);
        }

        public TipoCodigo TipoProbable(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return TipoCodigo.Desconocido;

            try
            {
                return Regex.IsMatch(codigo, ObtenerPatron())
                    ? TipoCodigo.Alumno
                    : TipoCodigo.Material;
            }
            catch (RegexParseException)
            {
                // Patrón inválido guardado por error: no forzar un orden,
                // dejar que el llamador pruebe ambas tablas.
                return TipoCodigo.Desconocido;
            }
        }
    }
}
