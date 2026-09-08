using Avalonia.Media;
using SukiUI.Models;

namespace LabInventario.Theme
{
    /// <summary>
    /// Paleta de acento institucional de la Universidad Autónoma de
    /// Sinaloa (azul y dorado/mostaza, tal como aparecen en su escudo y
    /// en la identidad de la institución) aplicada como
    /// <see cref="SukiColorTheme"/> de SukiUI.
    ///
    /// El objetivo es dar "toques" institucionales (color primario en
    /// botones, barra de título, selección, etc.) sin intentar reproducir
    /// el escudo oficial ni ningún elemento con derechos de autor propios
    /// de la universidad.
    ///
    /// NOTA: estos tonos son una aproximación razonable a partir de la
    /// paleta pública conocida (azul + dorado/mostaza). Si la UAS cuenta
    /// con un manual de identidad gráfica con códigos Pantone/HEX
    /// oficiales, basta con ajustar los dos valores de abajo para que la
    /// app quede exactamente alineada a ese manual; el resto de la app no
    /// necesita ningún otro cambio.
    /// </summary>
    public static class TemaUas
    {
        /// <summary>Azul institucional (color primario: barra de título, botones "Flat", selección).</summary>
        public static readonly Color AzulUas = Color.Parse("#0B3D78");

        /// <summary>Dorado/mostaza institucional (color de acento: detalles, botones "Flat Accent").</summary>
        public static readonly Color DoradoUas = Color.Parse("#C9A227");

        public static readonly SukiColorTheme Tema = new("UAS", AzulUas, DoradoUas);
    }
}
