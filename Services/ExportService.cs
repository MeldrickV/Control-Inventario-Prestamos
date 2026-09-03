using System.Text;
using ClosedXML.Excel;
using LabInventario.Data;

namespace LabInventario.Services
{
    /// <summary>
    /// Exportación de datos hacia formatos abiertos (CSV/XLSX) o hacia una
    /// copia de respaldo de la base completa.
    ///
    /// Aviso importante que vale la pena tener presente al usar esto: la
    /// base de datos vive cifrada en disco (ver <see cref="DatabaseManager"/>),
    /// pero un archivo CSV/XLSX exportado NO queda cifrado — es una acción
    /// explícita del administrador para sacar información de la app, así
    /// que a partir de ahí la responsabilidad de dónde se guarda o comparte
    /// ese archivo exportado es suya. Si se quiere un respaldo que siga
    /// protegido, usar <see cref="ExportarBaseDatosCifrada"/> en vez de los
    /// formatos planos.
    /// </summary>
    public class ExportService
    {
        private readonly AlumnoRepository _alumnoRepo = new();
        private readonly MaterialRepository _materialRepo = new();
        private readonly PrestamoRepository _prestamoRepo = new();

        // ---------------- Alumnos ----------------
        private static readonly string[] EncabezadosAlumnos = { "Id", "Nombre", "NumeroCuenta" };

        private IEnumerable<string?[]> FilasAlumnos() =>
            _alumnoRepo.Listar().Select(a => new string?[] { a.Id.ToString(), a.Nombre, a.NumeroCuenta });

        public void ExportarAlumnosCsv(string ruta) => EscribirCsv(ruta, EncabezadosAlumnos, FilasAlumnos());
        public void ExportarAlumnosXlsx(string ruta) => EscribirXlsx(ruta, "Alumnos", EncabezadosAlumnos, FilasAlumnos());

        // ---------------- Inventario (materiales) ----------------
        private static readonly string[] EncabezadosInventario =
            { "Id", "CodigoBarras", "Nombre", "CantidadTotal", "CantidadDisponible" };

        private IEnumerable<string?[]> FilasInventario() =>
            _materialRepo.Listar().Select(m => new string?[]
                { m.Id.ToString(), m.CodigoBarras, m.Nombre, m.CantidadTotal.ToString(), m.CantidadDisponible.ToString() });

        public void ExportarInventarioCsv(string ruta) => EscribirCsv(ruta, EncabezadosInventario, FilasInventario());
        public void ExportarInventarioXlsx(string ruta) => EscribirXlsx(ruta, "Inventario", EncabezadosInventario, FilasInventario());

        // ---------------- Historial de préstamos ----------------
        private static readonly string[] EncabezadosHistorial =
            { "Id", "Alumno", "NumeroCuenta", "Material", "CodigoBarras", "Cantidad", "FechaSalida", "FechaRegreso", "Estado" };

        private IEnumerable<string?[]> FilasHistorial() =>
            _prestamoRepo.ListarDetallado().Select(p => new string?[]
            {
                p.Id.ToString(), p.AlumnoNombre, p.NumeroCuenta, p.MaterialNombre, p.CodigoBarras,
                p.Cantidad.ToString(), p.FechaSalida.ToString("yyyy-MM-dd HH:mm"),
                p.FechaRegreso?.ToString("yyyy-MM-dd HH:mm") ?? "", p.Estado,
            });

        public void ExportarHistorialCsv(string ruta) => EscribirCsv(ruta, EncabezadosHistorial, FilasHistorial());
        public void ExportarHistorialXlsx(string ruta) => EscribirXlsx(ruta, "Historial", EncabezadosHistorial, FilasHistorial());

        // ---------------- Base de datos completa ----------------

        /// <summary>
        /// Copia binaria exacta del archivo .db, TAL CUAL, sin descifrar.
        /// Es la opción recomendada para respaldos: el archivo resultante
        /// sigue cifrado y solo se puede volver a abrir con esta misma app
        /// en esta misma máquina.
        /// </summary>
        public void ExportarBaseDatosCifrada(string rutaDestino) =>
            File.Copy(DatabaseManager.Instancia.DbPath, rutaDestino, overwrite: true);

        /// <summary>
        /// Vuelca las tres tablas a un único .xlsx (una hoja por tabla), en
        /// texto plano. Útil para revisar o migrar datos fuera de la app —
        /// pero, al ser texto plano, pierde la protección del cifrado (ver
        /// aviso en el resumen de la clase).
        /// </summary>
        public void ExportarBaseDatosXlsx(string ruta)
        {
            using var libro = new XLWorkbook();
            AgregarHoja(libro, "Alumnos", EncabezadosAlumnos, FilasAlumnos());
            AgregarHoja(libro, "Inventario", EncabezadosInventario, FilasInventario());
            AgregarHoja(libro, "Historial", EncabezadosHistorial, FilasHistorial());
            libro.SaveAs(ruta);
        }

        // ---------------- Helpers genéricos ----------------
        private static void EscribirCsv(string ruta, string[] headers, IEnumerable<string?[]> filas)
        {
            using var writer = new StreamWriter(ruta, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine(string.Join(",", headers.Select(EscaparCsv)));
            foreach (var fila in filas)
                writer.WriteLine(string.Join(",", fila.Select(EscaparCsv)));
        }

        private static string EscaparCsv(string? valor)
        {
            valor ??= "";
            if (valor.Contains(',') || valor.Contains('"') || valor.Contains('\n'))
                return "\"" + valor.Replace("\"", "\"\"") + "\"";
            return valor;
        }

        private static void EscribirXlsx(string ruta, string nombreHoja, string[] headers, IEnumerable<string?[]> filas)
        {
            using var libro = new XLWorkbook();
            AgregarHoja(libro, nombreHoja, headers, filas);
            libro.SaveAs(ruta);
        }

        private static void AgregarHoja(XLWorkbook libro, string nombreHoja, string[] headers, IEnumerable<string?[]> filas)
        {
            var hoja = libro.Worksheets.Add(nombreHoja);
            for (int c = 0; c < headers.Length; c++)
                hoja.Cell(1, c + 1).Value = headers[c];

            int fila = 2;
            foreach (var valores in filas)
            {
                for (int c = 0; c < valores.Length; c++)
                    hoja.Cell(fila, c + 1).Value = valores[c] ?? "";
                fila++;
            }
            hoja.Row(1).Style.Font.Bold = true;
            hoja.Columns().AdjustToContents();
        }
    }
}
