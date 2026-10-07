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
    /// + Sheets): sube el historial (automático, con upsert), publica el
    /// catálogo completo (manual), detecta diferencias contra el catálogo
    /// definitivo de la hoja (para confirmar en diálogo con selección por
    /// renglón), las aplica de forma idempotente y restaura en la hoja lo
    /// rechazado.
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

        // Tienda única: un solo spreadsheet por laboratorio.
        private const string Tienda = "principal";

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

            await AsegurarPestanasConEncabezadoAsync(Tienda, new[] { TabHistorial, TabAlumnos, TabInventario }, ct);
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
                Tienda, TabHistorial, EncabezadoHistorial, filas, ct);

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

            await _sheets.LimpiarRangoAsync(Tienda, $"{TabAlumnos}!A:B", ct);
            await _sheets.ActualizarValoresAsync(Tienda, $"{TabAlumnos}!A1", filasAlumnos, ct);
            await _sheets.LimpiarRangoAsync(Tienda, $"{TabInventario}!A:C", ct);
            await _sheets.ActualizarValoresAsync(Tienda, $"{TabInventario}!A1", filasInventario, ct);
            _config.Establecer(ClaveUltimaSync, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return filasAlumnos.Count + filasInventario.Count - 2;
        }

        // ---------------- Bajada del catálogo (para confirmar) ----------------

        /// <summary>
        /// Lee el catálogo definitivo de la hoja (lo que el revisor edita
        /// directo en Alumnos/Inventario) y lo compara con la base local por
        /// clave de negocio, devolviendo altas, cambios y bajas. La hoja es
        /// el catálogo completo compartido, así que "en base y no en hoja"
        /// sí significa baja propuesta (con guardián aparte para vaciados
        /// accidentales). Si una pestaña llega vacía no se propone nada de
        /// esa entidad.
        /// </summary>
        public async Task<List<CambioSincronizacion>> ObtenerCambiosPendientesAsync(CancellationToken ct = default)
        {
            var cambios = new List<CambioSincronizacion>();
            if (!Configurada || _sheets is null) return cambios;

            var alumnosHoja = await _sheets.ObtenerValoresAsync(Tienda, $"{TabAlumnos}!A:B", ct);
            var inventarioHoja = await _sheets.ObtenerValoresAsync(Tienda, $"{TabInventario}!A:C", ct);

            var filasAlumnos = FilasDeDatos(alumnosHoja, EncabezadoAlumnos);
            var filasInventario = FilasDeDatos(inventarioHoja, EncabezadoInventario);
            if (filasAlumnos is not null) cambios.AddRange(DiferenciasAlumnos(filasAlumnos));
            if (filasInventario is not null) cambios.AddRange(DiferenciasMateriales(filasInventario));
            return cambios;
        }

        /// <summary>
        /// Regla del guardián: advierte cuando las bajas propuestas son
        /// muchas (>= 5 y más del 25 % del catálogo local), señal típica de
        /// un vaciado accidental de la hoja. No bloquea, solo obliga a
        /// revisar antes del diálogo.
        /// </summary>
        public static bool PareceVaciadoAccidental(int bajas, int totalLocal) =>
            bajas >= 5 && totalLocal > 0 && bajas > totalLocal * 0.25;

        /// <summary>Conteos del catálogo local (alumnos, materiales) para el guardián.</summary>
        public (int Alumnos, int Materiales) ObtenerTotalesLocales() =>
            (_alumnos.Listar().Count, _materiales.Listar().Count);

        /// <summary>
        /// Restauración quirúrgica tras un rechazo parcial: por cada clave
        /// indicada deja la pestaña igual a la base (re-agrega la fila local
        /// si la hoja la quitó, la sobrescribe si la editó, quita la fila si
        /// solo existe en la hoja). Lo no mencionado se conserva intacto.
        /// </summary>
        public async Task RestaurarClavesAsync(EntidadCambio entidad, IEnumerable<string> claves, CancellationToken ct = default)
        {
            if (!Configurada || _sheets is null) return;
            var pendientes = new HashSet<string>(
                claves.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
                StringComparer.Ordinal);
            if (pendientes.Count == 0) return;

            var (pestana, encabezado) = entidad == EntidadCambio.Alumno
                ? (TabAlumnos, EncabezadoAlumnos)
                : (TabInventario, EncabezadoInventario);

            var actual = await _sheets.ObtenerValoresAsync(Tienda, $"{pestana}!A:Z", ct);
            var filas = FilasDeDatos(actual, encabezado) ?? new List<List<string>>();
            var conservadas = filas.Where(f => !pendientes.Contains(ClaveDe(f))).ToList();

            var locales = entidad == EntidadCambio.Alumno
                ? _alumnos.Listar().Where(a => pendientes.Contains(a.NumeroCuenta))
                    .Select(a => new List<string> { a.Nombre, a.NumeroCuenta }).ToList()
                : _materiales.Listar().Where(m => pendientes.Contains(m.CodigoBarras))
                    .Select(m => new List<string> { m.Nombre, m.CodigoBarras, m.CantidadTotal.ToString() }).ToList();

            var salida = new List<List<string>> { encabezado.ToList() };
            salida.AddRange(conservadas);
            salida.AddRange(locales);
            await _sheets.ActualizarValoresAsync(Tienda, $"{pestana}!A1", salida, ct);
        }

        private static string ClaveDe(List<string> fila) =>
            fila.Count > 1 ? fila[1].Trim() : "";

        /// <summary>
        /// Restaura en la hoja lo rechazado en el diálogo, agrupando por
        /// entidad. Lo aplicado se mantiene (ya converge solo).
        /// </summary>
        public async Task RestaurarRechazadosAsync(IEnumerable<CambioSincronizacion> rechazados, CancellationToken ct = default)
        {
            foreach (var grupo in rechazados.GroupBy(c => c.Entidad))
                await RestaurarClavesAsync(grupo.Key, grupo.Select(c => c.Clave), ct);
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
            var vistos = new HashSet<string>(StringComparer.Ordinal);

            foreach (var f in filas)
            {
                var nombre = f[0].Trim();
                var cuenta = f[1].Trim();
                if (cuenta.Length == 0) continue;
                vistos.Add(cuenta);
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

            // La hoja es el catálogo completo compartido: lo que está en base
            // y no en hoja sí es una baja propuesta (el revisor quitó la
            // fila). El guardián frena los vaciados accidentales y aplicar
            // una baja con historial se bloquea como en el borrado local.
            foreach (var local in locales.Values.Where(a => !vistos.Contains(a.NumeroCuenta)))
            {
                cambios.Add(new CambioSincronizacion
                {
                    Entidad = EntidadCambio.Alumno, Accion = AccionCambio.Baja,
                    Clave = local.NumeroCuenta, Nombre = local.Nombre,
                    Detalle = $"BAJA alumno {local.NumeroCuenta} — {local.Nombre}",
                });
            }
            return cambios;
        }

        private List<CambioSincronizacion> DiferenciasMateriales(List<List<string>> filas)
        {
            var cambios = new List<CambioSincronizacion>();
            var locales = _materiales.Listar().ToDictionary(m => m.CodigoBarras, m => m, StringComparer.Ordinal);
            var vistos = new HashSet<string>(StringComparer.Ordinal);

            foreach (var f in filas)
            {
                var nombre = f[0].Trim();
                var codigo = f[1].Trim();
                if (codigo.Length == 0 || !int.TryParse(f[2].Trim(), out var total) || total < 0) continue;
                vistos.Add(codigo);
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

            // Igual que en alumnos: la hoja es el catálogo completo.
            foreach (var local in locales.Values.Where(m => !vistos.Contains(m.CodigoBarras)))
            {
                cambios.Add(new CambioSincronizacion
                {
                    Entidad = EntidadCambio.Material, Accion = AccionCambio.Baja,
                    Clave = local.CodigoBarras, Nombre = local.Nombre,
                    Detalle = $"BAJA material {local.CodigoBarras} — {local.Nombre}",
                });
            }
            return cambios;
        }

        // ---------------- Aplicación idempotente (tras confirmar) ----------------

        /// <summary>
        /// Aplica la lista confirmada en el diálogo. Cada operación es
        /// idempotente (repetirla no duplica ni corrompe): las bajas siempre
        /// se ejecutan y el historial conserva su texto (foto al momento del
        /// préstamo), igual que en el borrado local.
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
                default: // Baja (permitida con historial: el historial conserva su texto)
                    if (local is null) { resultado.Omitidos++; return; }
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
                default: // Baja (permitida con historial: el historial conserva su texto)
                    if (local is null) { resultado.Omitidos++; return; }
                    _materiales.Eliminar(local.Id);
                    resultado.Aplicados++;
                    break;
            }
        }
    }
}
