using System.Globalization;
using System.Text;
using LabInventario.Data;
using LabInventario.Models;

namespace LabInventario.Services
{
    /// <summary>
    /// Un renglón del buscador manual de Operación: trae al alumno o al
    /// material encontrado, más su línea legible para la lista.
    /// </summary>
    public sealed class ResultadoBusqueda
    {
        public Alumno? Alumno { get; private set; }
        public Material? Material { get; private set; }
        public string Texto { get; private set; } = string.Empty;

        public static ResultadoBusqueda DeAlumno(Alumno alumno) => new()
        {
            Alumno = alumno,
            Texto = $"Alumno: {alumno.Nombre} ({alumno.NumeroCuenta})",
        };

        public static ResultadoBusqueda DeMaterial(Material material) => new()
        {
            Material = material,
            Texto = $"Material: {material.Nombre} [{material.CodigoBarras}] (disp: {material.CantidadDisponible})",
        };

        public override string ToString() => Texto;
    }

    /// <summary>
    /// Respaldo del escáner en la pestaña Operación: busca alumnos por
    /// nombre o número de cuenta y materiales por nombre o código de
    /// barras, combinando ambos en una sola lista (alumnos primero). El
    /// filtrado ignora acentos y mayúsculas. Sin texto no devuelve nada
    /// para no volcar tablas completas.
    /// </summary>
    public class BusquedaManualService
    {
        private readonly AlumnoRepository _alumnos;
        private readonly MaterialRepository _materiales;

        public BusquedaManualService(DatabaseManager? db = null, AlumnoRepository? alumnos = null, MaterialRepository? materiales = null)
        {
            var baseDatos = db ?? DatabaseManager.Instancia;
            _alumnos = alumnos ?? new AlumnoRepository(baseDatos);
            _materiales = materiales ?? new MaterialRepository(baseDatos);
        }

        public List<ResultadoBusqueda> Buscar(string texto, bool incluirAlumnos = true, bool incluirMateriales = true, int limite = 50)
        {
            var resultado = new List<ResultadoBusqueda>();
            var filtro = Normalizar(texto?.Trim() ?? "");
            if (filtro.Length == 0 || limite <= 0) return resultado;

            // Se filtra en memoria ignorando acentos y mayúsculas: el LIKE
            // de SQLite solo pliega ASCII y el operador suele escribir sin
            // acentos ("sofia" debe encontrar "Sofía").
            if (incluirAlumnos)
                resultado.AddRange(_alumnos.Listar()
                    .Where(a => Normalizar(a.Nombre).Contains(filtro) ||
                                Normalizar(a.NumeroCuenta).Contains(filtro))
                    .Take(limite - resultado.Count)
                    .Select(ResultadoBusqueda.DeAlumno));

            if (incluirMateriales && resultado.Count < limite)
                resultado.AddRange(_materiales.Listar()
                    .Where(m => Normalizar(m.Nombre).Contains(filtro) ||
                                Normalizar(m.CodigoBarras).Contains(filtro))
                    .Take(limite - resultado.Count)
                    .Select(ResultadoBusqueda.DeMaterial));

            return resultado;
        }

        private static string Normalizar(string texto)
        {
            var descompuesto = texto.Normalize(NormalizationForm.FormD);
            var constructor = new StringBuilder(descompuesto.Length);
            foreach (var caracter in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                    constructor.Append(caracter);
            }
            return constructor.ToString().ToLowerInvariant();
        }
    }
}
