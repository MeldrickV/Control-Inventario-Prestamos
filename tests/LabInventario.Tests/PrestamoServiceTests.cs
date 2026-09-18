using LabInventario.Models;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class PrestamoServiceTests : BaseDePruebas
    {
        // ------------------------------ Salida ------------------------------

        [Fact]
        public void RegistrarSalida_AlumnoInexistente_LanzaYNoTocaNada()
        {
            var ex = Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarSalida("999999", CodigoMaterial));

            Assert.Contains("no existe ningún alumno", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RegistrarSalida_MaterialInexistente_Lanza()
        {
            CrearAlumno();

            var ex = Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarSalida(CuentaAlumno, "999"));

            Assert.Contains("no existe ningún material", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RegistrarSalida_StockInsuficiente_LanzaYSinDescontar()
        {
            CrearAlumnoYMaterial();

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, cantidad: 11));

            Assert.Equal(CantidadTotalMaterial, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            Assert.Empty(Prestamos.ListarDetallado());
        }

        [Fact]
        public void RegistrarSalida_Exitosa_DescuentaStockYRegistraActivo()
        {
            CrearAlumnoYMaterial();
            var fecha = FechaPrueba(1);

            var resultado = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, cantidad: 3, fecha);

            Assert.NotEqual(0, resultado.PrestamoId);
            Assert.Equal(7, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            var prestamo = Prestamos.ObtenerPorId(resultado.PrestamoId)!;
            Assert.Equal(EstadoPrestamo.Activo, prestamo.Estado);
            Assert.Equal(3, prestamo.Cantidad);
        }

        // ------------------------- Entrada (FIFO) ---------------------------

        [Fact]
        public void RegistrarEntrada_Fifo_SaldaPrimeroElMasAntiguo()
        {
            CrearAlumnoYMaterial();

            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(2));

            var resultado = Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(3));

            Assert.Equal(3, resultado.CantidadDevuelta);
            // Solo queda pendiente 1 del préstamo más reciente (el de 2 quedó saldado).
            var activos = Prestamos.ListarActivosPorAlumnoYMaterial(Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id,
                Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id);
            Assert.Single(activos);
            Assert.Equal(1, activos[0].Cantidad);
            Assert.Equal(9, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
        }

        [Fact]
        public void RegistrarEntrada_DevueltaParcial_DejaRastroVisible()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(1));

            Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(2));

            var detalle = Prestamos.ListarDetallado();
            Assert.Equal(2, detalle.Count); // l activo (2) + 1 devuelto visible (1)
            Assert.Single(detalle.Where(p => p.Estado == "Activo" && p.Cantidad == 2));
            Assert.Single(detalle.Where(p => p.Estado != "Activo" && p.Cantidad == 1));
        }

        [Fact]
        public void RegistrarEntrada_MasQueLoPendiente_LanzaSinCambios()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(2)));

            Assert.Equal(8, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            Assert.Single(Prestamos.ListarDetallado());
        }

        [Fact]
        public void RegistrarEntrada_SinPrestamosActivos_Lanza()
        {
            CrearAlumnoYMaterial();

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 1));
        }

        [Fact]
        public void RegistrarEntradaDePrestamoEspecifico_ParcialYCompleta()
        {
            CrearAlumnoYMaterial();
            var sol = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(1));

            Servicio.RegistrarEntradaDePrestamoEspecifico(sol.PrestamoId, 2, FechaPrueba(2));
            Assert.Equal(1, Prestamos.ObtenerPorId(sol.PrestamoId)!.Cantidad); // sigue activo con 1
            var detalle = Prestamos.ListarDetallado();
            Assert.Equal(2, detalle.Count); // 1 activo (1) + 1 devuelto visible (2)
            Assert.Equal(9, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);

            Servicio.RegistrarEntradaDePrestamoEspecifico(sol.PrestamoId, 1, FechaPrueba(3));
            Assert.Equal(EstadoPrestamo.Devuelto, Prestamos.ObtenerPorId(sol.PrestamoId)!.Estado);
            Assert.Equal(10, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
        }

        [Fact]
        public void RegistrarEntradaDePrestamoEspecifico_YaDevuelto_Lanza()
        {
            CrearAlumnoYMaterial();
            var sol = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));
            Servicio.RegistrarEntradaDePrestamoEspecifico(sol.PrestamoId, 1);

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarEntradaDePrestamoEspecifico(sol.PrestamoId, 1));
        }

        [Fact]
        public void RegistrarEntradaDePrestamoEspecifico_CantidadMayorALoPrestado_Lanza()
        {
            CrearAlumnoYMaterial();
            var sol = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarEntradaDePrestamoEspecifico(sol.PrestamoId, 3));
        }

        // ------------------------------ Cables extra ----------------------------

        [Fact]
        public void RegistrarSalida_ConCables_PersisteCablesExtra()
        {
            CrearAlumnoYMaterial();
            // El material de ejemplo es un multímetro (no admite cable), pero
            // el servicio no filtra: registrar lo que la UI le indique.
            var resultado = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1), cablesExtra: 3);

            var prestamo = Prestamos.ObtenerPorId(resultado.PrestamoId)!;
            Assert.Equal(3, prestamo.CablesExtra);

            var detalle = Prestamos.ListarDetallado();
            var fila = Assert.Single(detalle);
            Assert.Equal(3, fila.CablesExtra);
        }

        [Fact]
        public void RegistrarSalida_SinCables_CablesExtraQuedaEnCero()
        {
            CrearAlumnoYMaterial();

            var resultado = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));

            Assert.Equal(0, Prestamos.ObtenerPorId(resultado.PrestamoId)!.CablesExtra);
        }

        [Fact]
        public void RegistrarLote_ConCables_PersistePorItem()
        {
            CrearAlumnoYMaterial();

            Servicio.RegistrarLote(CuentaAlumno,
                new[]
                {
                    new LoteItem(CodigoMaterial, 1, CablesExtra: 2),
                    new LoteItem(CodigoMaterial, 1, CablesExtra: 0),
                },
                esSalida: true, FechaPrueba(1));

            var detalle = Prestamos.ListarDetallado();
            Assert.Equal(2, detalle.Count);
            Assert.Equal(new[] { 0, 2 }, detalle.Select(d => d.CablesExtra).OrderBy(c => c));
        }

        [Fact]
        public void RegistrarLote_FallaUnItemConCables_RollbackTotal()
        {
            CrearAlumnoYMaterial();
            Materiales.Actualizar(Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id, CodigoMaterial, NombreMaterial, 3, 3);

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarLote(CuentaAlumno,
                    new[] { new LoteItem(CodigoMaterial, 1, CablesExtra: 2), new LoteItem(CodigoMaterial, 5) },
                    esSalida: true));

            // Rollback completo: ni préstamos ni stock, incluyendo los cables.
            Assert.Empty(Prestamos.ListarDetallado());
            Assert.Equal(3, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
        }

        // ------------------------------ Lote --------------------------------
        [Fact]
        public void RegistrarLote_SalidaExitosa_RegistraTodoConLaMismaFecha()
        {
            CrearAlumnoYMaterial();

            var fecha = FechaPrueba(1);
            var exitos = Servicio.RegistrarLote(CuentaAlumno,
                new[] { new LoteItem(CodigoMaterial, 1), new LoteItem(CodigoMaterial, 2) },
                esSalida: true, fecha);

            Assert.Equal(2, exitos);
            Assert.Equal(7, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            var detalle = Prestamos.ListarDetallado();
            Assert.Equal(2, detalle.Count); // un préstamo por ítem del lote (1 y 2)
            Assert.Equal(2, detalle.Count(p => p.FechaSalida == fecha));
            Assert.Equal(new[] { 1, 2 }, detalle.Select(p => p.Cantidad).OrderBy(c => c));
        }

        [Fact]
        public void RegistrarLote_UnItemInvalido_NoRegistraNada()
        {
            CrearAlumnoYMaterial();
            Materiales.Actualizar(Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id, CodigoMaterial, NombreMaterial, 3, 3);

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarLote(CuentaAlumno,
                    new[] { new LoteItem(CodigoMaterial, 1), new LoteItem(CodigoMaterial, 5) },
                    esSalida: true));

            // Rollback completo: el stock no se tocó y no quedó ningún préstamo.
            Assert.Equal(3, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            Assert.Empty(Prestamos.ListarDetallado());
        }

        [Fact]
        public void RegistrarLote_Entrada_RespetaFifoYAltaStock()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(2));

            var exitos = Servicio.RegistrarLote(CuentaAlumno,
                new[] { new LoteItem(CodigoMaterial, 3) },
                esSalida: false, FechaPrueba(3));

            Assert.Equal(1, exitos);
            Assert.Equal(9, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
            var activos = Prestamos.ListarActivosPorAlumnoYMaterial(
                Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id, Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id);
            Assert.Single(activos);
            Assert.Equal(1, activos[0].Cantidad);
        }

        [Fact]
        public void RegistrarLote_Vacio_Lanza()
        {
            CrearAlumnoYMaterial();

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarLote(CuentaAlumno, Array.Empty<LoteItem>(), esSalida: true));
        }

        [Fact]
        public void AlumnoNoRegistrado_LoteNoCambiaNada()
        {
            CrearAlumnoYMaterial();

            Assert.Throws<PrestamoException>(() =>
                Servicio.RegistrarLote("888888",
                    new[] { new LoteItem(CodigoMaterial, 1) }, esSalida: true));

            Assert.Empty(Prestamos.ListarDetallado());
            Assert.Equal(CantidadTotalMaterial, Materiales.ObtenerPorCodigo(CodigoMaterial)!.CantidadDisponible);
        }
    }
}