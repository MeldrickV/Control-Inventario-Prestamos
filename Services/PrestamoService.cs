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
    /// Capa de servicios: aquí vive la lógica de negocio real (reglas de
    /// stock, validaciones), separada de la GUI y del acceso a datos.
    /// La GUI solo llama a estos métodos y reacciona a su resultado o a la
    /// excepción <see cref="PrestamoException"/> que puedan lanzar.
    /// </summary>
    public class PrestamoService
    {
        private readonly AlumnoRepository _alumnoRepo = new();
        private readonly MaterialRepository _materialRepo = new();
        private readonly PrestamoRepository _prestamoRepo = new();

        /// <summary>
        /// Flujo principal de escaneo → salida de material.
        /// </summary>
        /// <param name="fechaSalida">
        /// Fecha/hora a registrar. Cuando se procesa un lote de varios
        /// artículos escaneados para el mismo alumno (ver
        /// <c>OperacionView</c>), el llamador debe fijar una sola
        /// <see cref="DateTime"/> y pasarla en todas las llamadas del lote,
        /// para que todo el préstamo quede registrado a la misma hora en
        /// vez de con pequeñas diferencias de milisegundos entre artículo y
        /// artículo. Si se omite, se usa la hora actual.
        /// </param>
        public ResultadoSalida RegistrarSalida(string numeroCuenta, string codigoBarras, int cantidad = 1, DateTime? fechaSalida = null)
        {
            var alumno = _alumnoRepo.ObtenerPorCuenta(numeroCuenta)
                ?? throw new PrestamoException(
                    $"No existe ningún alumno registrado con el número de cuenta '{numeroCuenta}'.");

            var material = _materialRepo.ObtenerPorCodigo(codigoBarras)
                ?? throw new PrestamoException(
                    $"No existe ningún material registrado con el código '{codigoBarras}'.");

            if (cantidad <= 0)
                throw new PrestamoException("La cantidad a prestar debe ser mayor que cero.");

            // --- Verificación automática de existencia (requisito clave) ---
            if (material.CantidadDisponible < cantidad)
            {
                throw new PrestamoException(
                    $"Stock insuficiente de '{material.Nombre}'. " +
                    $"Disponible: {material.CantidadDisponible}, solicitado: {cantidad}.");
            }

            var fecha = fechaSalida ?? DateTime.Now;
            _materialRepo.AjustarDisponible(material.Id, -cantidad);
            var prestamoId = _prestamoRepo.Crear(alumno.Id, material.Id, cantidad, fecha);

            material.CantidadDisponible -= cantidad; // reflejar el cambio en el objeto en memoria
            return new ResultadoSalida(alumno, material, prestamoId);
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
        /// </summary>
        public ResultadoEntrada RegistrarEntradaPorAlumnoYMaterial(int alumnoId, int materialId, int cantidad, DateTime? fechaRegreso = null)
        {
            if (cantidad <= 0)
                throw new PrestamoException("La cantidad a devolver debe ser mayor que cero.");

            var alumno = _alumnoRepo.ObtenerPorId(alumnoId)
                ?? throw new PrestamoException("El alumno indicado ya no existe.");
            var material = _materialRepo.ObtenerPorId(materialId)
                ?? throw new PrestamoException("El material indicado ya no existe.");

            var activos = _prestamoRepo.ListarActivosPorAlumnoYMaterial(alumnoId, materialId);
            var totalPendiente = activos.Sum(p => p.Cantidad);

            if (activos.Count == 0 || cantidad > totalPendiente)
            {
                throw new PrestamoException(
                    $"No se puede devolver {cantidad} de '{material.Nombre}': " +
                    $"{alumno.Nombre} solo tiene {totalPendiente} pendiente(s) de ese material.");
            }

            var fecha = fechaRegreso ?? DateTime.Now;
            var restante = cantidad;

            foreach (var prestamo in activos) // ya vienen ordenados del más antiguo al más reciente
            {
                if (restante <= 0) break;

                if (prestamo.Cantidad <= restante)
                {
                    _prestamoRepo.MarcarDevuelto(prestamo.Id, fecha);
                    restante -= prestamo.Cantidad;
                }
                else
                {
                    _prestamoRepo.ActualizarCantidad(prestamo.Id, prestamo.Cantidad - restante);
                    restante = 0;
                }
            }

            _materialRepo.AjustarDisponible(materialId, cantidad);
            material.CantidadDisponible += cantidad;

            return new ResultadoEntrada(alumno, material, cantidad);
        }

        /// <summary>
        /// Devuelve una cantidad de UN préstamo específico (por su Id),
        /// sin tocar los demás préstamos activos del mismo alumno/material.
        /// Se usa cuando, desde el historial, el usuario expande el grupo
        /// y elige devolver justo ese registro puntual en vez de dejar que
        /// el sistema decida por FIFO.
        /// </summary>
        public ResultadoEntrada RegistrarEntradaDePrestamoEspecifico(int idPrestamo, int cantidad, DateTime? fechaRegreso = null)
        {
            var prestamo = _prestamoRepo.ObtenerPorId(idPrestamo)
                ?? throw new PrestamoException("El préstamo indicado ya no existe.");

            if (prestamo.Estado != EstadoPrestamo.Activo)
                throw new PrestamoException("Ese préstamo ya fue devuelto.");

            if (cantidad <= 0 || cantidad > prestamo.Cantidad)
                throw new PrestamoException($"La cantidad a devolver debe ser entre 1 y {prestamo.Cantidad}.");

            var alumno = _alumnoRepo.ObtenerPorId(prestamo.AlumnoId)
                ?? throw new PrestamoException("El alumno de ese préstamo ya no existe.");
            var material = _materialRepo.ObtenerPorId(prestamo.MaterialId)
                ?? throw new PrestamoException("El material de ese préstamo ya no existe.");

            var fecha = fechaRegreso ?? DateTime.Now;

            if (cantidad == prestamo.Cantidad)
                _prestamoRepo.MarcarDevuelto(prestamo.Id, fecha);
            else
                _prestamoRepo.ActualizarCantidad(prestamo.Id, prestamo.Cantidad - cantidad);

            _materialRepo.AjustarDisponible(prestamo.MaterialId, cantidad);
            material.CantidadDisponible += cantidad;

            return new ResultadoEntrada(alumno, material, cantidad);
        }
    }
}
