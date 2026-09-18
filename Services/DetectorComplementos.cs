using System.Globalization;
using System.Text;

namespace LabInventario.Services
{
    /// <summary>
    /// Tipo de cable complementario que acompaña a un material en un
    /// préstamo. Son tres clases fijas definidas por el laboratorio:
    /// cables de fuente de poder, cables de generador y puntas de
    /// osciloscopio. Los cables NO llevan inventario ni stock: solo se
    /// registra cuántos se prestaron (columna <c>CablesExtra</c> de
    /// <c>prestamos</c>) como dato informativo del historial.
    /// </summary>
    public enum TipoCable
    {
        Ninguno,
        Fuente,
        Generador,
        Osciloscopio,
    }

    /// <summary>
    /// Decide, a partir del nombre de un material, qué tipo de cable suele
    /// acompañarlo (si alguno). El material <c>Material</c> solo tiene
    /// código de barras y nombre (sin campo "tipo"), así que la detección
    /// se hace por palabras clave sobre el nombre normalizado (minúsculas y
    /// sin acentos): "fuente" → cables de fuente, "generador" → cables de
    /// generador, "osciloscopio" → puntas de osciloscopio.
    /// </summary>
    public static class DetectorComplementos
    {
        /// <summary>Tipo de cable que corresponde a un material, o <see cref="TipoCable.Ninguno"/> si no lleva.</summary>
        public static TipoCable Detectar(string nombreMaterial)
        {
            var nombre = Normalizar(nombreMaterial);

            if (nombre.Contains("osciloscopio")) return TipoCable.Osciloscopio;
            if (nombre.Contains("generador")) return TipoCable.Generador;
            if (nombre.Contains("fuente")) return TipoCable.Fuente;

            return TipoCable.Ninguno;
        }

        /// <summary>Nombre legible del cable, según su tipo ("" para <see cref="TipoCable.Ninguno"/>).</summary>
        public static string Etiqueta(TipoCable tipo) => tipo switch
        {
            TipoCable.Fuente => "Cables de fuente de poder",
            TipoCable.Generador => "Cables de generador",
            TipoCable.Osciloscopio => "Puntas de osciloscopio",
            _ => "",
        };

        /// <summary>
        /// Texto resuelto para el historial y las exportaciones, según el
        /// material asociado: "Cables de fuente de poder: 2", "Puntas de
        /// osciloscopio: 1", o "" si no lleva cable. Es el "campo extra
        /// variable" que solo aplica cuando el material lo admite.
        /// </summary>
        public static string TextoExtra(string nombreMaterial, int cablesExtra)
        {
            if (cablesExtra <= 0) return "";
            var tipo = Detectar(nombreMaterial);
            if (tipo == TipoCable.Ninguno) return "";
            return $"{Etiqueta(tipo)}: {cablesExtra}";
        }

        /// <summary>
        /// Normaliza un texto a minúsculas y sin signos diacríticos
        /// ("FUENTE De Pôder" → "fuente de poder") para que la detección
        /// de palabras clave sea robusta ante acentos y mayúsculas.
        /// </summary>
        private static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";

            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizado.Length);
            foreach (var caracter in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(caracter));
            }
            return sb.ToString();
        }
    }
}