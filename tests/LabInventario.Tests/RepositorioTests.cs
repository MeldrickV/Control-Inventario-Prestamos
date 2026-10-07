using LabInventario.Services;

namespace LabInventario.Tests
{
    public class RepositorioTests : BaseDePruebas
    {
        [Fact]
        public void TienePrestamosDeAlumno_SinHistorial_Falso()
        {
            CrearAlumno();

            Assert.False(Prestamos.TienePrestamosDeAlumno(Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id));
        }

        [Fact]
        public void TienePrestamosDeAlumno_ConHistorial_Verdadero()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);

            Assert.True(Prestamos.TienePrestamosDeAlumno(Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id));
        }

        [Fact]
        public void TienePrestamosDeMaterial_ConHistorial_Verdadero()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);

            Assert.True(Prestamos.TienePrestamosDeMaterial(Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id));
        }

        [Fact]
        public void EliminarAlumno_ConHistorial_SePermiteYConservaHistorial()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);
            var id = Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id;

            Alumnos.Eliminar(id); // ya no lo impide la FK (ON DELETE SET NULL)

            Assert.Null(Alumnos.ObtenerPorCuenta(CuentaAlumno));
            var fila = Assert.Single(Prestamos.ListarDetallado());
            Assert.Null(fila.AlumnoId);
            Assert.Equal(NombreAlumno, fila.AlumnoNombre); // el texto se conserva
            Assert.Equal(CuentaAlumno, fila.NumeroCuenta);
            Assert.Equal(NombreMaterial, fila.MaterialNombre);
        }

        [Fact]
        public void EliminarMaterial_ConHistorial_SePermiteYConservaHistorial()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);
            var id = Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id;

            Materiales.Eliminar(id);

            Assert.Null(Materiales.ObtenerPorCodigo(CodigoMaterial));
            var fila = Assert.Single(Prestamos.ListarDetallado());
            Assert.Null(fila.MaterialId);
            Assert.Equal(CodigoMaterial, fila.CodigoBarras); // el texto se conserva
            Assert.Equal(NombreAlumno, fila.AlumnoNombre);
        }

        [Fact]
        public void Historial_ConservaFotoAunqueSeRenombreElCatalogo()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));

            Alumnos.Actualizar(Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id, "Nombre Nuevo", CuentaAlumno);

            var fila = Assert.Single(Prestamos.ListarDetallado());
            Assert.Equal(NombreAlumno, fila.AlumnoNombre); // foto al momento del préstamo
        }

        [Fact]
        public void Eliminar_SinHistorial_SeCompleta()
        {
            CrearAlumnoYMaterial();

            Alumnos.Eliminar(Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id);
            Materiales.Eliminar(Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id);

            Assert.Null(Alumnos.ObtenerPorCuenta(CuentaAlumno));
            Assert.Null(Materiales.ObtenerPorCodigo(CodigoMaterial));
        }

        [Fact]
        public void EliminarDevueltosAntiguos_BorraSoloDevueltosAntiguos()
        {
            CrearAlumnoYMaterial();
            // Préstamo devuelto hace 20 días → dentro del umbral de borrado.
            var antiguo = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));
            Servicio.RegistrarEntradaDePrestamoEspecifico(antiguo.PrestamoId, 1, DateTime.Now.AddDays(-20));
            // Préstamo activo → jamás se borra por antigüedad.
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, DateTime.Now.AddDays(-1));

            var borrados = Prestamos.EliminarDevueltosAntiguos(dias: 7);

            Assert.Equal(1, borrados);
            Assert.Single(Prestamos.ListarDetallado()); // solo queda el préstamo activo 
        }

        [Fact]
        public void ContarDevueltosAntiguos_CuentaSoloDevueltosConAntiguedad_SinBorrar()
        {
            CrearAlumnoYMaterial();
            // Devuelto hace 20 días → cae dentro del umbral.
            var antiguo = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));
            Servicio.RegistrarEntradaDePrestamoEspecifico(antiguo.PrestamoId, 1, DateTime.Now.AddDays(-20));
            // Devuelto hace 2 días → no llega a la antigüedad mínima.
            var reciente = Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(2));
            Servicio.RegistrarEntradaDePrestamoEspecifico(reciente.PrestamoId, 1, DateTime.Now.AddDays(-2));
            // Activo → nunca se cuenta.
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, DateTime.Now.AddDays(-1));

            Assert.Equal(1, Prestamos.ContarDevueltosAntiguos(10));
            Assert.Equal(3, Prestamos.ListarDetallado().Count); // contar no borra nada
        }

        [Fact]
        public void ObtenerPorCuenta_EscaneoSinGuiones_EncuentraCuentaConGuiones()
        {
            var id = Alumnos.Crear(NombreAlumno, "1845868-8");

            var alumno = Alumnos.ObtenerPorCuenta("18458688");

            Assert.NotNull(alumno);
            Assert.Equal(id, alumno!.Id);
            Assert.Equal("1845868-8", alumno.NumeroCuenta);
        }

        [Fact]
        public void ObtenerPorCuenta_EscaneoConGuiones_EncuentraCuentaSinGuiones()
        {
            var id = Alumnos.Crear(NombreAlumno, "18458688");

            var alumno = Alumnos.ObtenerPorCuenta("1845868-8");

            Assert.NotNull(alumno);
            Assert.Equal(id, alumno!.Id);
        }

        [Fact]
        public void ObtenerPorCuenta_BuscaExactoPrimero_CuandoExistenAmbasFormas()
        {
            var idConGuiones = Alumnos.Crear(NombreAlumno, "1845868-8");
            var idSinGuiones = Alumnos.Crear("Otro alumno", "18458688");

            Assert.Equal(idConGuiones, Alumnos.ObtenerPorCuenta("1845868-8")!.Id);
            Assert.Equal(idSinGuiones, Alumnos.ObtenerPorCuenta("18458688")!.Id);
        }
    }
}