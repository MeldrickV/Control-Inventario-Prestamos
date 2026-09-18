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
    /// combina dos señales sobre el nombre normalizado (minúsculas y sin
    /// acentos):
    /// - palabras clave: "osciloscopio"/"tektronix" → puntas de
    ///   osciloscopio, "generador"/"sfg" → cables de generador y "fuente"
    ///   → cables de fuente;
    /// - código de laboratorio al inicio (letra + dígito, con separador
    ///   opcional): F7/F8 "LEYBOLD" o G1/G5/G12 "SFG 1013" no mencionan la
    ///   palabra clave pero se reconocen por su letra de clase (F=fuente,
    ///   G=generador, O=osciloscopio).
    /// </summary>
    public static class DetectorComplementos
    {
        /// <summary>Tipo de cable que corresponde a un material, o <see cref="TipoCable.Ninguno"/> si no lleva.</summary>
        public static TipoCable Detectar(string nombreMaterial)
        {
            var nombre = Normalizar(nombreMaterial);

            if (nombre.Contains("osciloscopio") || nombre.Contains("tektronix")) return TipoCable.Osciloscopio;
            if (EsCodigoTipo(nombre, 'o')) return TipoCable.Osciloscopio;

            if (nombre.Contains("generador") || nombre.Contains("sfg")) return TipoCable.Generador;
            if (EsCodigoTipo(nombre, 'g')) return TipoCable.Generador;

            if (nombre.Contains("fuente")) return TipoCable.Fuente;
            if (EsCodigoTipo(nombre, 'f')) return TipoCable.Fuente;

            return TipoCable.Ninguno;
        }

        /// <summary>
        /// true si el nombre normalizado empieza con la letra de clase del
        /// equipo seguida (con separadores opcionales) de un dígito: "o1041",
        /// "o 1041", "o-1041", "g1", "f10"... Así "fusibles 5a" u "ondulador"
        /// no se confunden (no llevan dígito justo tras la letra).
        /// </summary>
        private static bool EsCodigoTipo(string nombre, char letra)
        {
            if (string.IsNullOrEmpty(nombre)) return false;
            var texto = nombre.TrimStart();
            if (texto.Length < 2 || texto[0] != letra) return false;

            var i = 1;
            while (i < texto.Length && (texto[i] == ' ' || texto[i] == '-' || texto[i] == '.' || texto[i] == '_'))
                i++;

            return i < texto.Length && char.IsDigit(texto[i]);
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