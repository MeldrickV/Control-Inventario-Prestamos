using LabInventario.Data;

namespace LabInventario.Services
{
    /// <summary>Resultado de un intento de subida del historial.</summary>
    public sealed class ResultadoSubida
    {
        public int Subidos { get; set; }
        public int Actualizados { get; set; }
        public string Mensaje { get; set; } = string.Empty;

        /// <summary>True si no se intentó nada (sin configurar o sin cliente).</summary>
        public bool Omitido { get; set; }
    }

    /// <summary>Resultado de aplicar una lista de cambios del catálogo.</summary>
    public sealed class ResultadoAplicacion
    {
        public int Aplicados { get; set; }
        public int Omitidos { get; set; }
        public List<string> Bloqueados { get; set; } = new();
    }

    /// <summary>
    /// Orquesta la sincronización con la nube del laboratorio (Apps Script
    /// + Sheets): sube el historial nuevo (automático), publica el
    /// catálogo completo (manual), detecta diferencias en la pestaña de
    /// Cambios que edita el revisor (para confirmar en diálogo), las aplica
    /// de forma idempotente y deja Cambios listo de nuevo.
    ///
    /// La base local sigue siendo la fuente de verdad; la nube es el
    /// buzón/respaldo. El cliente de red (<see cref="ISheetsClient"/>) se
    /// inyecta: en producción es el cliente del script y en pruebas un
    /// doble en memoria. Sin cliente o sin configurar, los métodos remotos
    /// se omiten en silencio para no interrumpir la operación local.
    /// </summary>
    public class SincronizacionService
    {
        public const string ClaveComputadora = "Sync.ComputadoraId";
        public const string ClaveLaboratorio = "Sync.LaboratorioNombre";
        public const string ClaveIntervalo = "Sync.IntervaloMinutos";
        public const string ClaveUltimaSync = "Sync.UltimaSincronizacion";

        public const string TabHistorial = "Historial";
        public const string TabAlumnos = "Alumnos";
        public const string TabInventario = "Inventario";

        private static readonly string[] EncabezadoHistorial =
            { "PrestamoId", "Alumno", "NumeroCuenta", "Material", "CodigoBarras", "Cantidad", "CablesExtra", "FechaSalida", "FechaRegreso", "Estado", "SyncId" };
        private static readonly string[] EncabezadoAlumnos = { "Nombre", "NumeroCuenta" };
        private static readonly string[] EncabezadoInventario = { "Nombre", "CodigoBarras", "CantidadTotal" };

        private readonly DatabaseManager _db;
        private readonly ConfiguracionRepository _config;
        private readonly ISheetsClient? _sheets;
        private readonly AlumnoRepository _alumnos;
        private readonly MaterialRepository _materiales;
        private readonly PrestamoRepository _prestamos;

        /// <summary>
        /// Usa la base real por defecto; inyectar base/configuración permite
        /// apuntar a una base temporal en las pruebas. El cliente de Sheets
        /// es opcional: sin él, la sincronización remota se omite.
        /// </summary>
        public SincronizacionService(DatabaseManager? db = null, ConfiguracionRepository? config = null, ISheetsClient? sheets = null)
        {
            _db = db ?? DatabaseManager.Instancia;
            _config = config ?? new ConfiguracionRepository(_db);
            _sheets = sheets;
            _alumnos = new AlumnoRepository(_db);
            _materiales = new MaterialRepository(_db);
            _prestamos = new PrestamoRepository(_db);
        }

        // Tiendas lógicas del script (un solo spreadsheet por laboratorio).
        private const string TiendaPrincipal = AppsScriptClient.TiendaPrincipal;
        private const string TiendaCambios = AppsScriptClient.TiendaCambios;

        /// <summary>Hay URL y clave del script configuradas.</summary>
        public bool Configurada =>
            !string.IsNullOrWhiteSpace(_config.Obtener(AppsScriptClient.ClaveScriptUrl)) &&
            !string.IsNullOrWhiteSpace(_config.Obtener(AppsScriptClient.ClaveSecreta));

        /// <summary>Hay cliente de red conectado (la autenticación de Google ya está enchufada).</summary>
        public bool ClienteDisponible => _sheets is not null;

        /// <summary>Minutos entre revisiones automáticas (1 a 120, por defecto 5).</summary>
        public int IntervaloMinutos()
        {
            var texto = _config.Obtener(ClaveIntervalo) ?? "";
            return int.TryParse(texto, out var minutos) ? Math.Clamp(minutos, 1, 120) : 5;
        }

        public string UltimaSincronizacion => _config.Obtener(ClaveUltimaSync) ?? "";

        /// <summary>Encabezado oficial de cada pestaña de la hoja.</summary>
        public static string[] EncabezadoPara(string pestana) => pestana switch
        {
            TabAlumnos => EncabezadoAlumnos,
            TabInventario => EncabezadoInventario,
            _ => EncabezadoHistorial,
        };

        /// <summary>
        /// Deja la nube lista: asegura las pestañas del spreadsheet del
        /// laboratorio y escribe el encabezado donde falte.
        /// </summary>
        public async Task AsegurarHojaAsync(CancellationToken ct = default)
        {
            if (_sheets is null)
                throw new InvalidOperationException("Sin conexión con el script (falta URL o clave).");

            await AsegurarPestanasConEncabezadoAsync(TiendaPrincipal, new[] { TabHistorial, TabAlumnos, TabInventario }, ct);
            await AsegurarPestanasConEncabezadoAsync(TiendaCambios, new[] { TabAlumnos, TabInventario }, ct);
            _config.Establecer(ClaveUltimaSync, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private async Task AsegurarPestanasConEncabezadoAsync(string tienda, string[] pestanas, CancellationToken ct)
        {
            await _sheets!.AsegurarPestanasAsync(tienda, pestanas, ct);
            foreach (var pestana in pestanas)
            {
                var actual = await _sheets.ObtenerValoresAsync(tienda, $"{pestana}!A1:Z1", ct);
                if (actual.Valores.Count == 0)
                    await _sheets.ActualizarValoresAsync(tienda, $"{pestana}!A1",
                        new List<List<string>> { EncabezadoPara(pestana).ToList() }, ct);
            }
        }

        // ---------------- Subida del historial (automática) ----------------

        /// <summary>
        /// Envía el historial local COMPLETO y la nube hace upsert por
        /// PrestamoId (actualiza lo que cambió —devoluciones totales y
        /// parciales—, agrega lo nuevo y nunca borra). Ante un fallo de red
        /// no se confirma nada y el próximo intento reenvía el estado
        /// completo, que se autocorrige solo.
        /// </summary>
        public async Task<ResultadoSubida> SubirHistorialAsync(CancellationToken ct = default)
        {
            var resultado = new ResultadoSubida { Omitido = true };
            if (!Configurada)
            {
                resultado.Mensaje = "Sincronización no configurada (falta la URL o la clave).";
                return resultado;
            }
            if (_sheets is null)
            {
                resultado.Mensaje = "Sin conexión con el script.";
                return resultado;
            }

            var todos = _prestamos.ListarDetallado().OrderBy(p => p.Id).ToList();
            if (todos.Count == 0)
            {
                resultado.Mensaje = "No hay préstamos registrados.";
                return resultado;
            }

            var filas = todos.Select(p => new List<string>
            {
                p.Id.ToString(), p.AlumnoNombre, p.NumeroCuenta, p.MaterialNombre, p.CodigoBarras,
                p.Cantidad.ToString(), p.CablesExtra.ToString(),
                p.FechaSalida.ToString("yyyy-MM-dd HH:mm:ss"),
                p.FechaRegreso?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                p.Estado, Guid.NewGuid().ToString("N"),
            }).ToList();

            var (agregadas, actualizadas) = await _sheets.SincronizarHistorialAsync(
                TiendaPrincipal, TabHistorial, EncabezadoHistorial, filas, ct);

            _config.Establecer(ClaveUltimaSync, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            resultado.Omitido = false;
            resultado.Subidos = agregadas;
            resultado.Actualizados = actualizadas;
            resultado.Mensaje = agregadas == 0 && actualizadas == 0
                ? "Historial al día: sin cambios que subir."
                : $"Historial sincronizado: {agregadas} fila(s) nueva(s), {actualizadas} actualizada(s).";
            return resultado;
        }

        // ---------------- Publicación del catálogo (manual) ----------------

        /// <summary>
        /// Reescribe las pestañas Alumnos e Inventario con el catálogo
        /// local completo. Es la única vía de subida del catálogo y siempre
        /// la dispara el administrador a mano. Devuelve filas publicadas.
        /// </summary>
        public async Task<int> PublicarCatalogoAsync(CancellationToken ct = default)
        {
            if (!Configurada || _sheets is null) return 0;

            var filasAlumnos = new List<List<string>> { EncabezadoAlumnos.ToList() };
            filasAlumnos.AddRange(_alumnos.Listar().Select(a => new List<string> { a.Nombre, a.NumeroCuenta }));

            var filasInventario = new List<List<string>> { EncabezadoInventario.ToList() };
            filasInventario.AddRange(_materiales.Listar().Select(m => new List<string> { m.Nombre, m.CodigoBarras, m.CantidadTotal.ToString() }));

            await _sheets.LimpiarRangoAsync(TiendaPrincipal, $"{TabAlumnos}!A:B", ct);
            await _sheets.ActualizarValoresAsync(TiendaPrincipal, $"{TabAlumnos}!A1", filasAlumnos, ct);
            await _sheets.LimpiarRangoAsync(TiendaPrincipal, $"{TabInventario}!A:C", ct);
            await _sheets.ActualizarValoresAsync(TiendaPrincipal, $"{TabInventario}!A1", filasInventario, ct);
            _config.Establecer(ClaveUltimaSync, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return filasAlumnos.Count + filasInventario.Count - 2;
        }

        // ---------------- Bajada del catálogo (para confirmar) ----------------

        // Foto de lo leído en la última revisión, para solo limpiar el
        // archivo de Cambios si nadie lo tocó mientras se aplicaba.
        private List<List<string>>? _vistosAlumnos;
        private List<List<string>>? _vistosInventario;

        /// <summary>
        /// Lee el archivo de Cambios (lo que el revisor editó en su lugar)
        /// y lo compara con la base local por clave de negocio, devolviendo
        /// altas y cambios. Nunca propone bajas: la hoja es una lista de
        /// propuestas, no el catálogo completo. Si una pestaña llega vacía
        /// no se propone nada de esa entidad.
        /// </summary>
        public async Task<List<CambioSincronizacion>> ObtenerCambiosPendientesAsync(CancellationToken ct = default)
        {
            var cambios = new List<CambioSincronizacion>();
            _vistosAlumnos = null;
            _vistosInventario = null;
            if (!Configurada || _sheets is null) return cambios;

            var alumnosHoja = await _sheets.ObtenerValoresAsync(TiendaCambios, $"{TabAlumnos}!A:B", ct);
            var inventarioHoja = await _sheets.ObtenerValoresAsync(TiendaCambios, $"{TabInventario}!A:C", ct);

            var filasAlumnos = FilasDeDatos(alumnosHoja, EncabezadoAlumnos);
            var filasInventario = FilasDeDatos(inventarioHoja, EncabezadoInventario);
            if (filasAlumnos is not null)
            {
                _vistosAlumnos = filasAlumnos.Select(f => f.ToList()).ToList();
                cambios.AddRange(DiferenciasAlumnos(filasAlumnos));
            }
            if (filasInventario is not null)
            {
                _vistosInventario = filasInventario.Select(f => f.ToList()).ToList();
                cambios.AddRange(DiferenciasMateriales(filasInventario));
            }
            return cambios;
        }

        /// <summary>
        /// Tras aplicar los cambios confirmados, deja el archivo de Cambios
        /// listo para la siguiente ronda (solo encabezados). Solo limpia si
        /// el contenido sigue igual a lo revisado: si el revisor editó en
        /// ese lapso, se conserva para el próximo ciclo.
        /// </summary>
        public async Task ConfirmarCambiosConsumidosAsync(CancellationToken ct = default)
        {
            if (!Configurada || _sheets is null) return;
            await LimpiarSiSigueIgualAsync(TabAlumnos, EncabezadoAlumnos, _vistosAlumnos, ct);
            await LimpiarSiSigueIgualAsync(TabInventario, EncabezadoInventario, _vistosInventario, ct);
            _vistosAlumnos = null;
            _vistosInventario = null;
        }

        private async Task LimpiarSiSigueIgualAsync(string pestana, string[] encabezado, List<List<string>>? vistos, CancellationToken ct)
        {
            if (vistos is null) return;
            var actual = await _sheets!.ObtenerValoresAsync(TiendaCambios, $"{pestana}!A:Z", ct);
            var filas = FilasDeDatos(actual, encabezado) ?? new List<List<string>>();
            if (!MismasFilas(filas, vistos)) return;
            await _sheets.ActualizarValoresAsync(TiendaCambios, $"{pestana}!A1",
                new List<List<string>> { encabezado.ToList() }, ct);
        }

        private static bool MismasFilas(List<List<string>> a, List<List<string>>? b)
        {
            if (b is null || a.Count != b.Count) return false;
            return a.Zip(b).All(par => par.First.SequenceEqual(par.Second));
        }

        private static List<List<string>>? FilasDeDatos(RangoValores rango, string[] encabezado)
        {
            if (rango.Valores.Count == 0) return null;
            var primera = rango.Valores[0];
            var esEncabezado = primera.Count >= encabezado.Length &&
                primera.Take(encabezado.Length).Select((v, i) =>
                    string.Equals(v?.Trim(), encabezado[i], StringComparison.OrdinalIgnoreCase)).All(x => x);
            var datos = (esEncabezado ? rango.Valores.Skip(1) : rango.Valores)
                .Where(f => f.Any(c => !string.IsNullOrWhiteSpace(c)))
                .Select(f => NormalizarFila(f, encabezado.Length))
                .ToList();
            return datos.Count == 0 ? null : datos;
        }

        private static List<string> NormalizarFila(List<string> fila, int columnas)
        {
            var normalizada = fila.Take(columnas).Select(c => c ?? "").ToList();
            while (normalizada.Count < columnas) normalizada.Add("");
            return normalizada;
        }

        private List<CambioSincronizacion> DiferenciasAlumnos(List<List<string>> filas)
        {
            var cambios = new List<CambioSincronizacion>();
            var locales = _alumnos.Listar().ToDictionary(a => a.NumeroCuenta, a => a, StringComparer.Ordinal);

            foreach (var f in filas)
            {
                var nombre = f[0].Trim();
                var cuenta = f[1].Trim();
                if (cuenta.Length == 0) continue;
                if (!locales.TryGetValue(cuenta, out var local))
                {
                    cambios.Add(new CambioSincronizacion
                    {
                        Entidad = EntidadCambio.Alumno, Accion = AccionCambio.Alta,
                        Clave = cuenta, Nombre = nombre,
                        Detalle = $"ALTA alumno {cuenta} — {nombre}",
                    });
                }
                else if (!string.Equals(local.Nombre, nombre, StringComparison.Ordinal))
                {
                    cambios.Add(new CambioSincronizacion
                    {
                        Entidad = EntidadCambio.Alumno, Accion = AccionCambio.Cambio,
                        Clave = cuenta, Nombre = nombre,
                        Detalle = $"CAMBIO alumno {cuenta}: \"{local.Nombre}\" → \"{nombre}\"",
                    });
                }
            }

            // Sin propuestas de baja: la hoja de Cambios es una lista de
            // propuestas (altas y cambios), no el catálogo completo. Lo que
            // está en base y no en hoja simplemente no se propone; las bajas
            // se hacen en la app local (con su protección de historial).
            return cambios;
        }

        private List<CambioSincronizacion> DiferenciasMateriales(List<List<string>> filas)
        {
            var cambios = new List<CambioSincronizacion>();
            var locales = _materiales.Listar().ToDictionary(m => m.CodigoBarras, m => m, StringComparer.Ordinal);

            foreach (var f in filas)
            {
                var nombre = f[0].Trim();
                var codigo = f[1].Trim();
                if (codigo.Length == 0 || !int.TryParse(f[2].Trim(), out var total) || total < 0) continue;
                if (!locales.TryGetValue(codigo, out var local))
                {
                    cambios.Add(new CambioSincronizacion
                    {
                        Entidad = EntidadCambio.Material, Accion = AccionCambio.Alta,
                        Clave = codigo, Nombre = nombre, CantidadTotal = total,
                        Detalle = $"ALTA material {codigo} — {nombre} (total {total})",
                    });
                }
                else if (!string.Equals(local.Nombre, nombre, StringComparison.Ordinal) || local.CantidadTotal != total)
                {
                    cambios.Add(new CambioSincronizacion
                    {
                        Entidad = EntidadCambio.Material, Accion = AccionCambio.Cambio,
                        Clave = codigo, Nombre = nombre, CantidadTotal = total,
                        Detalle = $"CAMBIO material {codigo}: \"{local.Nombre}\" → \"{nombre}\"; total {local.CantidadTotal} → {total}",
                    });
                }
            }

            // Igual que en alumnos: sin bajas automáticas (ver arriba).
            return cambios;
        }

        // ---------------- Aplicación idempotente (tras confirmar) ----------------

        /// <summary>
        /// Aplica la lista confirmada en el diálogo. Cada operación es
        /// idempotente (repetirla no duplica ni corrompe): las bajas de
        /// registros con historial se bloquean con mensaje, como en el
        /// borrado local.
        /// </summary>
        public Task<ResultadoAplicacion> AplicarCambiosAsync(IEnumerable<CambioSincronizacion> cambios, CancellationToken ct = default)
        {
            var resultado = new ResultadoAplicacion();
            foreach (var cambio in cambios)
            {
                ct.ThrowIfCancellationRequested();
                if (cambio.Entidad == EntidadCambio.Alumno) AplicarAlumno(cambio, resultado);
                else AplicarMaterial(cambio, resultado);
            }
            return Task.FromResult(resultado);
        }

        private void AplicarAlumno(CambioSincronizacion cambio, ResultadoAplicacion resultado)
        {
            var local = string.IsNullOrWhiteSpace(cambio.Clave) ? null : _alumnos.ObtenerPorCuenta(cambio.Clave);
            switch (cambio.Accion)
            {
                case AccionCambio.Alta:
                    if (local is not null) { resultado.Omitidos++; return; }
                    if (string.IsNullOrWhiteSpace(cambio.Nombre))
                    {
                        resultado.Bloqueados.Add($"ALTA alumno {cambio.Clave}: sin nombre.");
                        return;
                    }
                    _alumnos.Crear(cambio.Nombre, cambio.Clave);
                    resultado.Aplicados++;
                    break;
                case AccionCambio.Cambio:
                    if (local is null)
                    {
                        if (string.IsNullOrWhiteSpace(cambio.Nombre))
                        {
                            resultado.Bloqueados.Add($"CAMBIO alumno {cambio.Clave}: sin nombre.");
                            return;
                        }
                        _alumnos.Crear(cambio.Nombre, cambio.Clave);
                        resultado.Aplicados++;
                    }
                    else if (local.Nombre == cambio.Nombre) resultado.Omitidos++;
                    else
                    {
                        _alumnos.Actualizar(local.Id, cambio.Nombre, cambio.Clave);
                        resultado.Aplicados++;
                    }
                    break;
                default: // Baja
                    if (local is null) { resultado.Omitidos++; return; }
                    if (_prestamos.TienePrestamosDeAlumno(local.Id))
                    {
                        resultado.Bloqueados.Add($"BAJA alumno {cambio.Clave}: tiene préstamos registrados y no se puede eliminar.");
                        return;
                    }
                    _alumnos.Eliminar(local.Id);
                    resultado.Aplicados++;
                    break;
            }
        }

        private void AplicarMaterial(CambioSincronizacion cambio, ResultadoAplicacion resultado)
        {
            var local = string.IsNullOrWhiteSpace(cambio.Clave) ? null : _materiales.ObtenerPorCodigo(cambio.Clave);
            var total = Math.Max(0, cambio.CantidadTotal);
            switch (cambio.Accion)
            {
                case AccionCambio.Alta:
                    if (local is not null) { resultado.Omitidos++; return; }
                    if (string.IsNullOrWhiteSpace(cambio.Nombre))
                    {
                        resultado.Bloqueados.Add($"ALTA material {cambio.Clave}: sin nombre.");
                        return;
                    }
                    _materiales.Crear(cambio.Clave, cambio.Nombre, total);
                    resultado.Aplicados++;
                    break;
                case AccionCambio.Cambio:
                    if (local is null)
                    {
                        if (string.IsNullOrWhiteSpace(cambio.Nombre))
                        {
                            resultado.Bloqueados.Add($"CAMBIO material {cambio.Clave}: sin nombre.");
                            return;
                        }
                        _materiales.Crear(cambio.Clave, cambio.Nombre, total);
                        resultado.Aplicados++;
                    }
                    else if (local.Nombre == cambio.Nombre && local.CantidadTotal == total) resultado.Omitidos++;
                    else
                    {
                        // Al cambiar el total se conserva lo prestado: la
                        // disponible nueva es total menos lo que sigue fuera.
                        var prestados = local.CantidadTotal - local.CantidadDisponible;
                        var disponible = Math.Max(0, total - prestados);
                        _materiales.Actualizar(local.Id, cambio.Clave, cambio.Nombre, total, disponible);
                        resultado.Aplicados++;
                    }
                    break;
                default: // Baja
                    if (local is null) { resultado.Omitidos++; return; }
                    if (_prestamos.TienePrestamosDeMaterial(local.Id))
                    {
                        resultado.Bloqueados.Add($"BAJA material {cambio.Clave}: tiene préstamos registrados y no se puede eliminar.");
                        return;
                    }
                    _materiales.Eliminar(local.Id);
                    resultado.Aplicados++;
                    break;
            }
        }
    }
}
