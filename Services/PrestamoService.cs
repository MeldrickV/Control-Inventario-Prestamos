using Microsoft.Data.Sqlite;
using LabInventario.Data;
using LabInventario.Models;

namespace LabInventario.Services
{
    /// <summary>
    /// Resultado de registrar una salida: útil para que la GUI muestre un
    /// mensaje de éxito sin tener que volver a consultar la base de datos.
    /// </summary>
    public record ResultadoSalida(Alumno Alumno, Material Material, int PrestamoId);

    public record ResultadoEntrada(Alumno Alumno, Material Material, int CantidadDevuelta);

    /// <summary>
    /// Un artículo acumulado en el lote de escaneo: el código escaneado y
    /// cuántas unidades acumuló (1 por escaneo, o más si el mismo material
    /// se escaneó varias veces).
    /// </summary>
    public record LoteItem(string Codigo, int Cantidad);

    /// <summary>
    /// Capa de servicios: aquí vive la lógica de negocio real (reglas de
    /// stock, validaciones), separada de la GUI y del acceso a datos.
    /// La GUI solo llama a estos métodos y reacciona a su resultado o a la
    /// excepción <see cref="PrestamoException"/> que puedan lanzar.
    ///
    /// Todas las operaciones que tocan más de una tabla (stock + préstamo)
    /// corren dentro de UNA transacción (ver
    /// <see cref="DatabaseManager.EjecutarTransaccion"/>): si fallan a la
    /// mitad, se revierte todo y la base queda consistente, nunca con el
    /// stock descontado pero sin préstamo, ni al revés.
    /// </summary>
    public class PrestamoService
    {
        private readonly DatabaseManager _db;
        private readonly AlumnoRepository _alumnoRepo;
        private readonly MaterialRepository _materialRepo;
        private readonly PrestamoRepository _prestamoRepo;

        /// <summary>
        /// Por defecto todo apunta a la base de datos real
        /// (<see cref="DatabaseManager.Instancia"/>), así las vistas pueden
        /// seguir creando el servicio sin argumentos. Inyectar un
        /// <see cref="DatabaseManager"/> (o repos concretos) permite apuntar
        /// a una base temporal en las pruebas.
        /// </summary>
        public PrestamoService(DatabaseManager? db = null, AlumnoRepository? alumnoRepo = null,
            MaterialRepository? materialRepo = null, PrestamoRepository? prestamoRepo = null)
        {
            _db = db ?? DatabaseManager.Instancia;
            _alumnoRepo = alumnoRepo ?? new AlumnoRepository(_db);
            _materialRepo = materialRepo ?? new MaterialRepository(_db);
            _prestamoRepo = prestamoRepo ?? new PrestamoRepository(_db);
        }

        /// <summary>
        /// Flujo principal de escaneo → salida de material. Atómico: si
        /// falla la creación del préstamo, el descuento de stock se revierte.
        /// </summary>
        /// <param name="fechaSalida">
        /// Fecha/hora a registrar. Cuando se procesa un lote de varios
        /// artículos escaneados para el mismo alumno (ver
        /// <c>OperacionView</c> y <see cref="RegistrarLote"/>), el llamador
        /// debe fijar una sola <see cref="DateTime"/> y pasarla en todas las
        /// llamadas del lote, para que todo el préstamo quede registrado a
        /// la misma hora en vez de con pequeñas diferencias entre artículo y
        /// artículo. Si se omite, se usa la hora actual.
        /// </param>
        public ResultadoSalida RegistrarSalida(string numeroCuenta, string codigoBarras, int cantidad = 1, DateTime? fechaSalida = null)
        {
            if (cantidad <= 0)
                throw new PrestamoException("La cantidad a prestar debe ser mayor que cero.");

            ResultadoSalida? resultado = null;
            _db.EjecutarTransaccion(conexion =>
            {
                var alumno = _alumnoRepo.ObtenerPorCuenta(numeroCuenta, conexion)
                    ?? throw new PrestamoException(
                        $"No existe ningún alumno registrado con el número de cuenta '{numeroCuenta}'.");

                var material = _materialRepo.ObtenerPorCodigo(codigoBarras, conexion)
                    ?? throw new PrestamoException(
                        $"No existe ningún material registrado con el código '{codigoBarras}'.");

                // --- Verificación automática de existencia (requisito clave) ---
                if (material.CantidadDisponible < cantidad)
                {
                    throw new PrestamoException(
                        $"Stock insuficiente de '{material.Nombre}'. " +
                        $"Disponible: {material.CantidadDisponible}, solicitado: {cantidad}.");
                }

                var fecha = fechaSalida ?? DateTime.Now;
                _materialRepo.AjustarDisponible(material.Id, -cantidad, conexion);
                var prestamoId = _prestamoRepo.Crear(alumno.Id, material.Id, cantidad, fecha, conexion);

                material.CantidadDisponible -= cantidad; // reflejar el cambio en el objeto en memoria
                resultado = new ResultadoSalida(alumno, material, prestamoId);
            });
            return resultado!;
        }

        /// <summary>
        /// Flujo principal de escaneo → devolución de material, buscando
        /// al alumno y al material por sus códigos. Delega en
        /// <see cref="RegistrarEntradaPorAlumnoYMaterial"/> para aplicar la
        /// devolución (con soporte de cantidad parcial y varios préstamos
        /// activos del mismo material, saldados en orden FIFO).
        /// </summary>
        public ResultadoEntrada RegistrarEntrada(string numeroCuenta, string codigoBarras, int cantidad = 1, DateTime? fechaRegreso = null)
        {
            var alumno = _alumnoRepo.ObtenerPorCuenta(numeroCuenta)
                ?? throw new PrestamoException(
                    $"No existe ningún alumno registrado con el número de cuenta '{numeroCuenta}'.");

            var material = _materialRepo.ObtenerPorCodigo(codigoBarras)
                ?? throw new PrestamoException(
                    $"No existe ningún material registrado con el código '{codigoBarras}'.");

            return RegistrarEntradaPorAlumnoYMaterial(alumno.Id, material.Id, cantidad, fechaRegreso);
        }

        /// <summary>
        /// Devuelve una cantidad de un material para un alumno, saldando
        /// sus préstamos activos en orden FIFO (el más antiguo primero):
        /// si el alumno tiene, por ejemplo, un préstamo de 2 unidades de
        /// hace una semana y otro de 1 unidad de ayer (total 3 pendientes),
        /// y regresa 1, se descuenta del préstamo de 2 (queda en 1
        /// pendiente, sigue "Activo"), sin tocar el de ayer.
        ///
        /// Es atómico: el saldado de préstamos y el alta de stock corren en
        /// una sola transacción.
        /// </summary>
        public ResultadoEntrada RegistrarEntradaPorAlumnoYMaterial(int alumnoId, int materialId, int cantidad, DateTime? fechaRegreso = null)
        {
            if (cantidad <= 0)
                throw new PrestamoException("La cantidad a devolver debe ser mayor que cero.");

            ResultadoEntrada? resultado = null;
            var fecha = fechaRegreso ?? DateTime.Now;

            _db.EjecutarTransaccion(conexion =>
            {
                var alumno = _alumnoRepo.ObtenerPorId(alumnoId, conexion)
                    ?? throw new PrestamoException("El alumno indicado ya no existe.");
                var material = _materialRepo.ObtenerPorId(materialId, conexion)
                    ?? throw new PrestamoException("El material indicado ya no existe.");

                var activos = _prestamoRepo.ListarActivosPorAlumnoYMaterial(alumnoId, materialId, conexion);
                var totalPendiente = activos.Sum(p => p.Cantidad);

                if (activos.Count == 0 || cantidad > totalPendiente)
                {
                    throw new PrestamoException(
                        $"No se puede devolver {cantidad} de '{material.Nombre}': " +
                        $"{alumno.Nombre} solo tiene {totalPendiente} pendiente(s) de ese material.");
                }

                // Red de seguridad contra datos ya inconsistentes: una
                // devolución nunca debe dejar el stock por encima del total.
                if (material.CantidadDisponible + cantidad > material.CantidadTotal)
                {
                    throw new PrestamoException(
                        $"No se puede devolver {cantidad} de '{material.Nombre}': el stock ya está en su total.");
                }

                var restante = cantidad;

                foreach (var prestamo in activos) // ya vienen ordenados del más antiguo al más reciente
                {
                    if (restante <= 0) break;

                    if (prestamo.Cantidad <= restante)
                    {
                        _prestamoRepo.MarcarDevuelto(prestamo.Id, fecha, conexion);
                        restante -= prestamo.Cantidad;
                    }
                    else
                    {
                        _prestamoRepo.ActualizarCantidad(prestamo.Id, prestamo.Cantidad - restante, conexion);
                        // Deja un registro nuevo y visible de lo que sí se
                        // devolvió, en vez de que la devolución parcial quede
                        // "escondida" como una simple resta de cantidad sobre
                        // el préstamo que sigue activo.
                        _prestamoRepo.CrearDevuelto(alumnoId, materialId, restante, prestamo.FechaSalida, fecha, conexion);
                        restante = 0;
                    }
                }

                _materialRepo.AjustarDisponible(materialId, cantidad, conexion);
                material.CantidadDisponible += cantidad;

                resultado = new ResultadoEntrada(alumno, material, cantidad);
            });
            return resultado!;
        }

        /// <summary>
        /// Devuelve una cantidad de UN préstamo específico (por su Id),
        /// sin tocar los demás préstamos activos del mismo alumno/material.
        /// Se usa cuando, desde el historial, el usuario expande el grupo
        /// y elige devolver justo ese registro puntual en vez de dejar que
        /// el sistema decida por FIFO. Atómico.
        /// </summary>
        public ResultadoEntrada RegistrarEntradaDePrestamoEspecifico(int idPrestamo, int cantidad, DateTime? fechaRegreso = null)
        {
            ResultadoEntrada? resultado = null;
            var fecha = fechaRegreso ?? DateTime.Now;

            _db.EjecutarTransaccion(conexion =>
            {
                var prestamo = _prestamoRepo.ObtenerPorId(idPrestamo, conexion)
                    ?? throw new PrestamoException("El préstamo indicado ya no existe.");

                if (prestamo.Estado != EstadoPrestamo.Activo)
                    throw new PrestamoException("Ese préstamo ya fue devuelto.");

                if (cantidad <= 0 || cantidad > prestamo.Cantidad)
                    throw new PrestamoException($"La cantidad a devolver debe ser entre 1 y {prestamo.Cantidad}.");

                var alumno = _alumnoRepo.ObtenerPorId(prestamo.AlumnoId, conexion)
                    ?? throw new PrestamoException("El alumno de ese préstamo ya no existe.");
                var material = _materialRepo.ObtenerPorId(prestamo.MaterialId, conexion)
                    ?? throw new PrestamoException("El material de ese préstamo ya no existe.");

                if (material.CantidadDisponible + cantidad > material.CantidadTotal)
                {
                    throw new PrestamoException(
                        $"No se puede devolver {cantidad} de '{material.Nombre}': el stock ya está en su total.");
                }

                if (cantidad == prestamo.Cantidad)
                    _prestamoRepo.MarcarDevuelto(prestamo.Id, fecha, conexion);
                else
                {
                    _prestamoRepo.ActualizarCantidad(prestamo.Id, prestamo.Cantidad - cantidad, conexion);
                    _prestamoRepo.CrearDevuelto(prestamo.AlumnoId, prestamo.MaterialId, cantidad, prestamo.FechaSalida, fecha, conexion);
                }

                _materialRepo.AjustarDisponible(prestamo.MaterialId, cantidad, conexion);
                material.CantidadDisponible += cantidad;

                resultado = new ResultadoEntrada(alumno, material, cantidad);
            });
            return resultado!;
        }

        /// <summary>
        /// Registra TODO el lote de escaneo del operador (varios materiales
        /// acumulados para un mismo alumno) en una sola operación atómica.
        ///
        /// Si cualquier artículo falla (stock insuficiente, código
        /// desconocido, devolución mayor a lo pendiente, etc.), se lanza una
        /// <see cref="PrestamoException"/> y NADA del lote queda guardado:
        /// o se registra el lote completo, o no se registra nada. Esto evita
        /// que un lote a medias deje datos inconsistentes.
        /// </summary>
        /// <returns>Número de artículos del lote que se registraron.</returns>
        public int RegistrarLote(string numeroCuenta, IReadOnlyList<LoteItem> items, bool esSalida, DateTime? fecha = null)
        {
            if (items.Count == 0)
                throw new PrestamoException("El lote está vacío: escanea al menos un material.");

            var fechaOperacion = fecha ?? DateTime.Now;
            var registrados = 0;

            _db.EjecutarTransaccion(conexion =>
            {
                // Se resuelve el alumno una sola vez para todo el lote.
                var alumno = _alumnoRepo.ObtenerPorCuenta(numeroCuenta, conexion)
                    ?? throw new PrestamoException(
                        $"No existe ningún alumno registrado con el número de cuenta '{numeroCuenta}'.");

                foreach (var item in items)
                {
                    if (item.Cantidad <= 0)
                        throw new PrestamoException(
                            $"La cantidad de '{item.Codigo}' debe ser mayor que cero.");

                    var material = _materialRepo.ObtenerPorCodigo(item.Codigo, conexion)
                        ?? throw new PrestamoException(
                            $"No existe ningún material registrado con el código '{item.Codigo}'.");

                    if (esSalida)
                    {
                        if (material.CantidadDisponible < item.Cantidad)
                        {
                            throw new PrestamoException(
                                $"Stock insuficiente de '{material.Nombre}'. " +
                                $"Disponible: {material.CantidadDisponible}, solicitado: {item.Cantidad}.");
                        }

                        _materialRepo.AjustarDisponible(material.Id, -item.Cantidad, conexion);
                        _prestamoRepo.Crear(alumno.Id, material.Id, item.Cantidad, fechaOperacion, conexion);
                    }
                    else
                    {
                        var activos = _prestamoRepo.ListarActivosPorAlumnoYMaterial(alumno.Id, material.Id, conexion);
                        var totalPendiente = activos.Sum(p => p.Cantidad);

                        if (activos.Count == 0 || item.Cantidad > totalPendiente)
                        {
                            throw new PrestamoException(
                                $"No se puede devolver {item.Cantidad} de '{material.Nombre}': " +
                                $"{alumno.Nombre} solo tiene {totalPendiente} pendiente(s) de ese material.");
                        }

                        AplicarDevolucionFifo(alumno.Id, material.Id, item.Cantidad, fechaOperacion, conexion);
                    }

                    registrados++;
                }
            });

            return registrados;
        }

        /// <summary>
        /// Descuenta <paramref name="cantidad"/> de los préstamos activos
        /// del alumno para el material, en orden FIFO (el más antiguo
        /// primero), dejando rastro visible de las devoluciones parciales
        /// igual que <see cref="RegistrarEntradaPorAlumnoYMaterial"/>.
        /// Debe invocarse DENTRO de una transacción ya abierta.
        /// </summary>
        private void AplicarDevolucionFifo(int alumnoId, int materialId, int cantidad, DateTime fecha, SqliteConnection conexion)
        {
            _materialRepo.AjustarDisponible(materialId, cantidad, conexion);

            var activos = _prestamoRepo.ListarActivosPorAlumnoYMaterial(alumnoId, materialId, conexion);
            var restante = cantidad;

            foreach (var prestamo in activos)
            {
                if (restante <= 0) break;

                if (prestamo.Cantidad <= restante)
                {
                    _prestamoRepo.MarcarDevuelto(prestamo.Id, fecha, conexion);
                    restante -= prestamo.Cantidad;
                }
                else
                {
                    _prestamoRepo.ActualizarCantidad(prestamo.Id, prestamo.Cantidad - restante, conexion);
                    _prestamoRepo.CrearDevuelto(alumnoId, materialId, restante, prestamo.FechaSalida, fecha, conexion);
                    restante = 0;
                }
            }
        }
    }
}