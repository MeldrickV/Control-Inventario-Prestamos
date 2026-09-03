using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using LabInventario.Data;
using Microsoft.Data.Sqlite;

namespace LabInventario.Services
{
    /// <summary>
    /// Resultado normalizado de leer cualquier archivo soportado: siempre
    /// se reduce a una lista de encabezados y una lista de filas de valores,
    /// sin importar si el origen fue Excel, CSV/TXT o un script SQL.
    /// </summary>
    public record DatosImportados(List<string> Headers, List<List<string?>> Filas);

    /// <summary>
    /// Servicio de importación masiva de datos.
    ///
    /// Responsabilidades:
    /// 1. Leer un archivo (xlsx/xls, csv/txt, sql) y normalizarlo siempre a
    ///    la misma forma: (Headers, Filas).
    /// 2. Sugerir automáticamente, mediante expresiones regulares, qué
    ///    columna del archivo corresponde a cada campo del sistema
    ///    (heurística de mapeo). El usuario confirma o corrige esta
    ///    sugerencia en la GUI antes de importar.
    /// </summary>
    public class ImportService
    {
        // Campos que el sistema necesita para cada entidad importable.
        public static readonly string[] CamposAlumno = { "Nombre", "NumeroCuenta" };
        public static readonly string[] CamposMaterial = { "CodigoBarras", "Nombre", "CantidadTotal" };

        // Patrones heurísticos: nombres de columna típicos en español/inglés
        // que suelen corresponder a cada campo del sistema.
        private static readonly Dictionary<string, string> Patrones = new()
        {
            ["NumeroCuenta"] = @"(numero.*cuenta|no\.?\s*cuenta|matricula|account|control|carnet|boleta)",
            ["CodigoBarras"] = @"(codigo.*barra|barcode|cod\.?\s*barras|sku|ean|upc)",
            ["Nombre"] = @"(nombre|descripcion|material|elemento|item|articulo|name)",
            ["CantidadTotal"] = @"(cantidad|stock|existenc|total|qty|cant\.)",
        };

        // ------------------------------------------------------------
        // Lectura de archivos: cada formato tiene su propio parser interno,
        // pero todos devuelven la misma estructura (Headers, Filas).
        // ------------------------------------------------------------
        public DatosImportados LeerArchivo(string ruta, bool esAlumnos = true)
        {
            var extension = Path.GetExtension(ruta).ToLowerInvariant();
            return extension switch
            {
                ".xlsx" or ".xls" => LeerExcel(ruta),
                ".csv" or ".txt" => LeerPlano(ruta),
                ".sql" => LeerSql(ruta),
                ".db" or ".sqlite" or ".sqlite3" => LeerBaseDeDatos(ruta, esAlumnos ? "alumnos" : "materiales"),
                _ => throw new NotSupportedException($"Formato de archivo no soportado: '{extension}'."),
            };
        }

        /// <summary>
        /// Lee directamente una tabla (alumnos/materiales) de un archivo
        /// SQLite .db/.sqlite. Sirve tanto para importar un .db externo sin
        /// cifrar (por ejemplo, de otro sistema) como para importar un
        /// respaldo cifrado hecho por esta misma app en esta misma máquina.
        /// </summary>
        private DatosImportados LeerBaseDeDatos(string ruta, string tabla)
        {
            using var conexion = AbrirConexionSqlite(ruta);
            using var comando = conexion.CreateCommand();
            comando.CommandText = $"SELECT * FROM \"{tabla}\"";

            using var lector = comando.ExecuteReader();
            var headers = Enumerable.Range(0, lector.FieldCount).Select(lector.GetName).ToList();

            var filas = new List<List<string?>>();
            while (lector.Read())
            {
                var valores = new List<string?>();
                for (int i = 0; i < lector.FieldCount; i++)
                    valores.Add(lector.IsDBNull(i) ? null : lector.GetValue(i).ToString());
                filas.Add(valores);
            }
            return new DatosImportados(headers, filas);
        }

        /// <summary>
        /// Abre el .db probando primero SIN contraseña (por si es un
        /// archivo sin cifrar, como uno exportado desde otro sistema) y,
        /// si eso falla, reintenta con la clave de cifrado propia de esta
        /// instalación (por si es un respaldo hecho por esta misma app en
        /// esta misma máquina). Se abre en modo solo-lectura: importar
        /// nunca debe modificar el archivo de origen.
        /// </summary>
        private static SqliteConnection AbrirConexionSqlite(string ruta)
        {
            try
            {
                var conexionPlano = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = ruta, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
                conexionPlano.Open();
                using (var prueba = conexionPlano.CreateCommand())
                {
                    prueba.CommandText = "SELECT count(*) FROM sqlite_master";
                    prueba.ExecuteScalar();
                }
                return conexionPlano;
            }
            catch (SqliteException)
            {
                var conexionCifrada = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = ruta,
                        Mode = SqliteOpenMode.ReadOnly,
                        Password = DatabaseManager.ClaveCifradoActual,
                    }.ConnectionString);
                conexionCifrada.Open();
                return conexionCifrada;
            }
        }

        private DatosImportados LeerExcel(string ruta)
        {
            using var libro = new XLWorkbook(ruta);
            var hoja = libro.Worksheets.First();
            var rangoUsado = hoja.RangeUsed();
            if (rangoUsado is null)
                return new DatosImportados(new List<string>(), new List<List<string?>>());

            var filasCrudas = rangoUsado.RowsUsed().ToList();
            if (filasCrudas.Count == 0)
                return new DatosImportados(new List<string>(), new List<List<string?>>());

            var filaEncabezado = filasCrudas[0];
            var headers = filaEncabezado.Cells()
                .Select(c => c.GetValue<string>().Trim())
                .ToList();

            var filas = new List<List<string?>>();
            foreach (var fila in filasCrudas.Skip(1))
            {
                var valores = new List<string?>();
                for (int i = 1; i <= headers.Count; i++)
                {
                    var celda = fila.Cell(i);
                    valores.Add(celda.IsEmpty() ? null : celda.GetValue<string>());
                }
                if (valores.Any(v => !string.IsNullOrWhiteSpace(v)))
                    filas.Add(valores);
            }
            return new DatosImportados(headers, filas);
        }

        private DatosImportados LeerPlano(string ruta)
        {
            var lineas = File.ReadAllLines(ruta, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (lineas.Count == 0)
                return new DatosImportados(new List<string>(), new List<List<string?>>());

            char separador = DetectarSeparador(lineas[0]);
            var headers = DividirLinea(lineas[0], separador).Select(h => h.Trim()).ToList();
            var filas = lineas.Skip(1)
                .Select(l => DividirLinea(l, separador).Select(v => (string?)v).ToList())
                .ToList();
            return new DatosImportados(headers, filas);
        }

        private static char DetectarSeparador(string primeraLinea)
        {
            // Heurística simple: se elige el separador más frecuente entre los usuales.
            var candidatos = new[] { ',', ';', '\t', '|' };
            return candidatos.OrderByDescending(c => primeraLinea.Count(ch => ch == c)).First();
        }

        private static List<string> DividirLinea(string linea, char separador)
        {
            // Split simple respetando comillas dobles, suficiente para archivos exportados
            // desde Excel/hojas de cálculo comunes.
            var resultado = new List<string>();
            var actual = new StringBuilder();
            bool dentroComillas = false;

            foreach (var c in linea)
            {
                if (c == '"')
                {
                    dentroComillas = !dentroComillas;
                }
                else if (c == separador && !dentroComillas)
                {
                    resultado.Add(actual.ToString());
                    actual.Clear();
                }
                else
                {
                    actual.Append(c);
                }
            }
            resultado.Add(actual.ToString());
            return resultado;
        }

        /// <summary>
        /// Extrae columnas y valores de sentencias `INSERT INTO tabla (...) VALUES (...);`.
        /// Soporta múltiples tuplas de VALUES en una sola sentencia.
        /// </summary>
        private DatosImportados LeerSql(string ruta)
        {
            var contenido = File.ReadAllText(ruta, Encoding.UTF8);

            var patronInsert = new Regex(
                @"INSERT\s+INTO\s+[`""\[]?\w+[`""\]]?\s*\(([^)]+)\)\s*VALUES\s*(.+?);",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var coincidencias = patronInsert.Matches(contenido);
            if (coincidencias.Count == 0)
                throw new InvalidDataException(
                    "No se encontraron sentencias INSERT INTO reconocibles en el archivo SQL.");

            var headers = coincidencias[0].Groups[1].Value
                .Split(',')
                .Select(c => c.Trim().Trim('`', '"', '[', ']'))
                .ToList();

            var patronTupla = new Regex(@"\(([^()]*)\)");
            var filas = new List<List<string?>>();

            foreach (Match coincidencia in coincidencias)
            {
                var bloqueValores = coincidencia.Groups[2].Value;
                foreach (Match tupla in patronTupla.Matches(bloqueValores))
                {
                    var valores = ParsearValoresSql(tupla.Groups[1].Value);
                    if (valores.Count == headers.Count)
                        filas.Add(valores);
                }
            }
            return new DatosImportados(headers, filas);
        }

        private static List<string?> ParsearValoresSql(string textoTupla)
        {
            // Divide por comas respetando cadenas entre comillas simples.
            var crudos = DividirLinea(textoTupla, ',');
            return crudos
                .Select(v => v.Trim())
                .Select(v => v.Trim('\'', '"'))
                .Select(v => v.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : (string?)v)
                .ToList();
        }

        // ------------------------------------------------------------
        // Heurística de mapeo de columnas
        // ------------------------------------------------------------
        /// <summary>
        /// Para cada campo que el sistema necesita, busca en los encabezados
        /// del archivo la primera columna cuyo nombre coincida con el patrón
        /// regex asociado. Devuelve un diccionario {campo: índice de columna | null}.
        /// El usuario puede corregir esta sugerencia en la GUI antes de importar.
        /// </summary>
        public Dictionary<string, int?> SugerirMapeo(List<string> headers, string[] camposDestino)
        {
            var mapeo = new Dictionary<string, int?>();
            var columnasUsadas = new HashSet<int>();

            foreach (var campo in camposDestino)
            {
                int? indiceEncontrado = null;
                if (Patrones.TryGetValue(campo, out var patron))
                {
                    for (int i = 0; i < headers.Count; i++)
                    {
                        if (columnasUsadas.Contains(i)) continue;
                        if (Regex.IsMatch(headers[i], patron, RegexOptions.IgnoreCase))
                        {
                            indiceEncontrado = i;
                            break;
                        }
                    }
                }
                mapeo[campo] = indiceEncontrado;
                if (indiceEncontrado.HasValue)
                    columnasUsadas.Add(indiceEncontrado.Value);
            }
            return mapeo;
        }
    }
}
