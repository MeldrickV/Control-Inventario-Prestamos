using LabInventario.Services;
using Microsoft.Data.Sqlite;

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
        public void EliminarAlumno_ConHistorial_LaForeignKeyLoImpide()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);
            var id = Alumnos.ObtenerPorCuenta(CuentaAlumno)!.Id;

            Assert.Throws<SqliteException>(() => Alumnos.Eliminar(id));
            Assert.NotNull(Alumnos.ObtenerPorId(id)); // el alumno sigue ahí
        }

        [Fact]
        public void EliminarMaterial_ConHistorial_LaForeignKeyLoImpide()
        {
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);
            var id = Materiales.ObtenerPorCodigo(CodigoMaterial)!.Id;

            Assert.Throws<SqliteException>(() => Materiales.Eliminar(id));
            Assert.NotNull(Materiales.ObtenerPorId(id));
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
    }
}