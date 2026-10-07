using LabInventario.Data;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class SincronizacionServiceTests : BaseDePruebas
    {
        // El servicio usa la tienda única "principal" del script.
        private const string Hoja = "principal";

        private readonly FakeSheetsClient _fake = new();
        private ConfiguracionRepository _config = null!;
        private SincronizacionService _sync = null!;

        private void ConfigurarSync()
        {
            _config = new ConfiguracionRepository(Db);
            _config.Establecer(AppsScriptClient.ClaveScriptUrl, "https://script.google.com/macros/s/test/exec");
            _config.Establecer(AppsScriptClient.ClaveSecreta, "clave-test");
            _config.Establecer(SincronizacionService.ClaveComputadora, "LAB-TEST");
            _sync = new SincronizacionService(Db, _config, _fake);
        }

        [Fact]
        public async Task SubirHistorial_DevolucionTotal_ActualizaEnSuLugar()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(2));

            var primera = await _sync.SubirHistorialAsync();
            Assert.False(primera.Omitido);
            Assert.Equal(2, primera.Subidos);
            Assert.Equal(0, primera.Actualizados);

            Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(3)); // devuelve todo el préstamo 1
            var segunda = await _sync.SubirHistorialAsync();
            Assert.Equal(0, segunda.Subidos);
            Assert.Equal(1, segunda.Actualizados);

            var rejilla = _fake.Leer(Hoja, "Historial");
            Assert.Equal(3, rejilla.Count); // encabezado + 2 filas, sin duplicar
            Assert.Equal("Devuelto", rejilla[1][9]);
            Assert.NotEqual("", rejilla[1][8]); // FechaRegreso informada
            Assert.Equal("Activo", rejilla[2][9]);
            Assert.Equal("3", rejilla[2][5]);
        }

        [Fact]
        public async Task SubirHistorial_DevolucionParcial_AjustaCantidadYAgregaFila()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(1));

            var primera = await _sync.SubirHistorialAsync();
            Assert.Equal(1, primera.Subidos);

            Servicio.RegistrarEntrada(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(2)); // parcial: 3 → 2 + fila devuelta
            var segunda = await _sync.SubirHistorialAsync();
            Assert.Equal(1, segunda.Subidos);
            Assert.Equal(1, segunda.Actualizados);

            var rejilla = _fake.Leer(Hoja, "Historial");
            Assert.Equal(3, rejilla.Count); // encabezado + original ajustado + fila devuelta
            Assert.Equal("2", rejilla[1][5]); // cantidad restante del préstamo 1
            Assert.Equal("Activo", rejilla[1][9]);
            Assert.Equal("1", rejilla[2][5]);
            Assert.Equal("Devuelto", rejilla[2][9]);
        }

        [Fact]
        public async Task SubirHistorial_SinCambios_NoAgregaNiActualiza()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));

            await _sync.SubirHistorialAsync();
            var segunda = await _sync.SubirHistorialAsync();

            Assert.Equal(0, segunda.Subidos);
            Assert.Equal(0, segunda.Actualizados);
            Assert.Equal(2, _fake.Leer(Hoja, "Historial").Count);
        }

        [Fact]
        public async Task SubirHistorial_SinRed_LanzaYReintentaSinDuplicar()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));

            _fake.FallarRed = true;
            await Assert.ThrowsAsync<HttpRequestException>(() => _sync.SubirHistorialAsync());
            Assert.Empty(_fake.Leer(Hoja, "Historial"));

            _fake.FallarRed = false;
            var resultado = await _sync.SubirHistorialAsync();
            Assert.Equal(1, resultado.Subidos);
            Assert.Equal(2, _fake.Leer(Hoja, "Historial").Count); // encabezado + 1 fila
        }

        [Fact]
        public async Task SubirHistorial_SinConfigurar_SeOmite()
        {
            _sync = new SincronizacionService(Db, new ConfiguracionRepository(Db), _fake);

            var resultado = await _sync.SubirHistorialAsync();
            Assert.True(resultado.Omitido);
            Assert.Empty(_fake.Leer(Hoja, "Historial"));
        }

        [Fact]
        public async Task PublicarCatalogo_EscribePestanasConEncabezado()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();

            var filas = await _sync.PublicarCatalogoAsync();
            Assert.Equal(2, filas);

            var alumnos = _fake.Leer(Hoja, "Alumnos");
            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, alumnos[0].ToArray());
            Assert.Equal(new[] { NombreAlumno, CuentaAlumno }, alumnos[1].ToArray());

            var inventario = _fake.Leer(Hoja, "Inventario");
            Assert.Equal(new[] { "Nombre", "CodigoBarras", "CantidadTotal" }, inventario[0].ToArray());
            Assert.Equal(new[] { NombreMaterial, CodigoMaterial, CantidadTotalMaterial.ToString() }, inventario[1].ToArray());
        }

        [Fact]
        public async Task ObtenerCambiosPendientes_DetectaAltaCambioYBaja()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Alumnos.Crear("Otro alumno", "11111111");
            Materiales.Crear("9999999999999", "Otro material", 5);

            // La hoja es el catálogo completo: trae todo menos un local
            // (baja propuesta) más un alta y un cambio.
            _fake.Sembrar(Hoja, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { "Ana Sofía Cambiada", CuentaAlumno },   // cambio de nombre
                new() { "Alumno Nuevo", "22222222" },            // alta
                // "11111111" ausente → baja propuesta (el revisor quitó la fila)
            });
            _fake.Sembrar(Hoja, "Inventario", new List<List<string>>
            {
                new() { "Nombre", "CodigoBarras", "CantidadTotal" },
                new() { NombreMaterial, CodigoMaterial, CantidadTotalMaterial.ToString() }, // igual
                new() { "Material Nuevo", "8888888888888", "7" },                            // alta
                // "9999999999999" ausente → baja propuesta
            });

            var cambios = await _sync.ObtenerCambiosPendientesAsync();

            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Alta && c.Clave == "22222222");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Cambio && c.Clave == CuentaAlumno);
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Baja && c.Clave == "11111111");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Material && c.Accion == AccionCambio.Alta && c.Clave == "8888888888888");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Material && c.Accion == AccionCambio.Baja && c.Clave == "9999999999999");
            Assert.DoesNotContain(cambios, c => c.Entidad == EntidadCambio.Material && c.Clave == CodigoMaterial);
            Assert.Equal(5, cambios.Count);
        }

        [Fact]
        public async Task ObtenerCambiosPendientes_PestanaVacia_NoProponeNada()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            // Hoja recién creada: pestañas vacías → no se propone borrar todo.
            var cambios = await _sync.ObtenerCambiosPendientesAsync();
            Assert.Empty(cambios);
        }

        [Fact]
        public async Task AplicarCambios_EsIdempotente_NoDuplica()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();

            var cambios = new List<CambioSincronizacion>
            {
                new() { Entidad = EntidadCambio.Alumno, Accion = AccionCambio.Alta, Clave = "22222222", Nombre = "Alumno Nuevo" },
                new() { Entidad = EntidadCambio.Material, Accion = AccionCambio.Cambio, Clave = CodigoMaterial, Nombre = NombreMaterial, CantidadTotal = CantidadTotalMaterial },
            };

            var primera = await _sync.AplicarCambiosAsync(cambios);
            var segunda = await _sync.AplicarCambiosAsync(cambios);

            Assert.Equal(1, primera.Aplicados);
            Assert.Equal(1, primera.Omitidos);
            Assert.Equal(0, segunda.Aplicados);
            Assert.Equal(2, segunda.Omitidos);
            Assert.NotNull(Alumnos.ObtenerPorCuenta("22222222"));
        }

        [Fact]
        public async Task AplicarCambios_BajaConHistorial_EliminaYConservaHistorial()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1);

            var cambios = new List<CambioSincronizacion>
            {
                new() { Entidad = EntidadCambio.Alumno, Accion = AccionCambio.Baja, Clave = CuentaAlumno, Nombre = NombreAlumno },
                new() { Entidad = EntidadCambio.Material, Accion = AccionCambio.Baja, Clave = CodigoMaterial, Nombre = NombreMaterial },
            };

            var resultado = await _sync.AplicarCambiosAsync(cambios);

            Assert.Equal(2, resultado.Aplicados);
            Assert.Null(Alumnos.ObtenerPorCuenta(CuentaAlumno));
            Assert.Null(Materiales.ObtenerPorCodigo(CodigoMaterial));
            var fila = Assert.Single(Prestamos.ListarDetallado());
            Assert.Equal(NombreAlumno, fila.AlumnoNombre);
            Assert.Equal(NombreMaterial, fila.MaterialNombre);
        }

        [Fact]
        public async Task AsegurarHoja_EscribeEncabezadosDondeFaltan()
        {
            ConfigurarSync();

            await _sync.AsegurarHojaAsync();

            // El doble en memoria usa "principal" como tienda única.
            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, _fake.Leer("principal", "Alumnos")[0].ToArray());
            Assert.Equal(new[] { "Nombre", "CodigoBarras", "CantidadTotal" }, _fake.Leer("principal", "Inventario")[0].ToArray());
            Assert.Equal(11, _fake.Leer("principal", "Historial")[0].Count);
        }

        [Fact]
        public async Task AsegurarHoja_Existente_ConservaDatosYSoloAgregaPestanas()
        {
            ConfigurarSync();
            _fake.Sembrar("principal", "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { NombreAlumno, CuentaAlumno },
            });

            await _sync.AsegurarHojaAsync();

            var alumnos = _fake.Leer("principal", "Alumnos");
            Assert.Equal(2, alumnos.Count); // no duplicó el encabezado ni borró filas
            Assert.Equal(CuentaAlumno, alumnos[1][1]);
            Assert.Equal(new[] { "Nombre", "CodigoBarras", "CantidadTotal" }, _fake.Leer("principal", "Inventario")[0].ToArray());
        }

        [Fact]
        public async Task RestaurarClaves_RechazoBaja_ReagregaFilaSinTocarResto()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            // El revisor quitó la fila del alumno de la hoja.
            _fake.Sembrar(Hoja, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { "Alumno Nuevo", "22222222" },
            });

            await _sync.RestaurarClavesAsync(EntidadCambio.Alumno, new[] { CuentaAlumno });

            var alumnos = _fake.Leer(Hoja, "Alumnos");
            Assert.Equal(3, alumnos.Count);
            Assert.Contains(alumnos, f => f[0] == NombreAlumno && f[1] == CuentaAlumno);
            Assert.Contains(alumnos, f => f[1] == "22222222"); // lo demás intacto
        }

        [Fact]
        public async Task RestaurarClaves_RechazoCambio_SobrescribeConLocal()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            _fake.Sembrar(Hoja, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { "Nombre Editado", CuentaAlumno },
            });

            await _sync.RestaurarClavesAsync(EntidadCambio.Alumno, new[] { CuentaAlumno });

            var alumnos = _fake.Leer(Hoja, "Alumnos");
            Assert.Equal(2, alumnos.Count);
            Assert.Equal(NombreAlumno, alumnos[1][0]); // valor local restaurado
        }

        [Fact]
        public async Task RestaurarClaves_RechazoAlta_QuitaFilaConservandoResto()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            _fake.Sembrar(Hoja, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { NombreAlumno, CuentaAlumno },
                new() { "No Deseado", "22222222" },
            });

            await _sync.RestaurarClavesAsync(EntidadCambio.Alumno, new[] { "22222222" });

            var alumnos = _fake.Leer(Hoja, "Alumnos");
            Assert.Equal(2, alumnos.Count); // encabezado + local
            Assert.Equal(CuentaAlumno, alumnos[1][1]);
        }

        [Fact]
        public async Task FlujoParcial_AplicaAltaYRestauraBaja()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            // Hoja: trae un alta y quitó al alumno local (baja propuesta).
            _fake.Sembrar(Hoja, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { "Alumno Nuevo", "22222222" },
            });
            _fake.Sembrar(Hoja, "Inventario", new List<List<string>>
            {
                new() { "Nombre", "CodigoBarras", "CantidadTotal" },
                new() { NombreMaterial, CodigoMaterial, CantidadTotalMaterial.ToString() },
            });

            var cambios = await _sync.ObtenerCambiosPendientesAsync();
            Assert.Equal(2, cambios.Count); // ALTA 22222222 + BAJA CuentaAlumno

            var alta = cambios.Where(c => c.Accion == AccionCambio.Alta).ToList();
            var baja = cambios.Where(c => c.Accion == AccionCambio.Baja).ToList();
            var resultado = await _sync.AplicarCambiosAsync(alta);
            await _sync.RestaurarRechazadosAsync(baja);

            Assert.Equal(1, resultado.Aplicados);
            Assert.NotNull(Alumnos.ObtenerPorCuenta("22222222")); // alta aplicada
            Assert.NotNull(Alumnos.ObtenerPorCuenta(CuentaAlumno)); // baja no aplicada
            var alumnos = _fake.Leer(Hoja, "Alumnos");
            Assert.Contains(alumnos, f => f[1] == CuentaAlumno); // fila restaurada en hoja
            Assert.Contains(alumnos, f => f[1] == "22222222");
            Assert.Empty(await _sync.ObtenerCambiosPendientesAsync()); // converge en silencio
        }

        [Theory]
        [InlineData(0, 10, false)]
        [InlineData(4, 10, false)]
        [InlineData(5, 20, false)]
        [InlineData(6, 20, true)]
        [InlineData(30, 30, true)]
        public void PareceVaciadoAccidental_Umbrales(int bajas, int total, bool esperado)
        {
            Assert.Equal(esperado, SincronizacionService.PareceVaciadoAccidental(bajas, total));
        }

        [Fact]
        public void ObtenerTotalesLocales_CuentaCatalogo()
        {
            CrearAlumnoYMaterial();

            var (alumnos, materiales) = new SincronizacionService(Db).ObtenerTotalesLocales();

            Assert.Equal(1, alumnos);
            Assert.Equal(1, materiales);
        }

        [Fact]
        public async Task AplicarCambios_MaterialConservaLoPrestadoAlCambiarTotal()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(1)); // 3 fuera, 7 disponibles

            var cambios = new List<CambioSincronizacion>
            {
                new() { Entidad = EntidadCambio.Material, Accion = AccionCambio.Cambio, Clave = CodigoMaterial, Nombre = NombreMaterial, CantidadTotal = 12 },
            };

            var resultado = await _sync.AplicarCambiosAsync(cambios);

            Assert.Equal(1, resultado.Aplicados);
            var material = Materiales.ObtenerPorCodigo(CodigoMaterial)!;
            Assert.Equal(12, material.CantidadTotal);
            Assert.Equal(9, material.CantidadDisponible); // los 3 prestados se conservan
        }
    }
}
