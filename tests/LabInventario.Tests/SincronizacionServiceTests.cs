using LabInventario.Data;
using LabInventario.Services;

namespace LabInventario.Tests
{
    public class SincronizacionServiceTests : BaseDePruebas
    {
        // El servicio usa las tiendas lógicas "principal"/"cambios" del script.
        private const string Hoja = "principal";
        private const string HojaCambios = "cambios";

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
        public async Task SubirHistorial_SubeSoloNuevos_YAvanzaMarcador()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 2, FechaPrueba(1));
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 3, FechaPrueba(2));

            var primera = await _sync.SubirHistorialAsync();
            Assert.False(primera.Omitido);
            Assert.Equal(2, primera.Subidos);
            Assert.Equal("2", _config.Obtener(SincronizacionService.ClaveMarcador));

            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(3));
            var segunda = await _sync.SubirHistorialAsync();
            Assert.Equal(1, segunda.Subidos);

            var rejilla = _fake.Leer(Hoja, "Historial");
            Assert.Equal(3, rejilla.Count); // solo filas de datos, sin duplicar
            Assert.Equal("3", rejilla[2][0]); // PrestamoId de la última
            Assert.Equal(CuentaAlumno, rejilla[2][2]);
            Assert.Equal(CodigoMaterial, rejilla[2][4]);
        }

        [Fact]
        public async Task SubirHistorial_SinRed_NoAvanzaMarcador_YReintentaSinDuplicar()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Servicio.RegistrarSalida(CuentaAlumno, CodigoMaterial, 1, FechaPrueba(1));

            _fake.FallarRed = true;
            await Assert.ThrowsAsync<HttpRequestException>(() => _sync.SubirHistorialAsync());
            Assert.Null(_config.Obtener(SincronizacionService.ClaveMarcador));

            _fake.FallarRed = false;
            var resultado = await _sync.SubirHistorialAsync();
            Assert.Equal(1, resultado.Subidos);
            Assert.Single(_fake.Leer(Hoja, "Historial"));
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
        public async Task ObtenerCambiosPendientes_DetectaAltaCambioBaja()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            Alumnos.Crear("Otro alumno", "11111111");
            Materiales.Crear("9999999999999", "Otro material", 5);

            _fake.Sembrar(HojaCambios, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { "Ana Sofía Cambiada", CuentaAlumno },   // cambio de nombre
                new() { "Alumno Nuevo", "22222222" },            // alta
                // "11111111" ausente → baja
            });
            _fake.Sembrar(HojaCambios, "Inventario", new List<List<string>>
            {
                new() { "Nombre", "CodigoBarras", "CantidadTotal" },
                new() { NombreMaterial, CodigoMaterial, CantidadTotalMaterial.ToString() }, // igual
                new() { "Material Nuevo", "8888888888888", "7" },                            // alta
                // "9999999999999" ausente → baja
            });

            var cambios = await _sync.ObtenerCambiosPendientesAsync();

            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Alta && c.Clave == "22222222");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Cambio && c.Clave == CuentaAlumno);
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Alumno && c.Accion == AccionCambio.Baja && c.Clave == "11111111");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Material && c.Accion == AccionCambio.Alta && c.Clave == "8888888888888");
            Assert.Contains(cambios, c => c.Entidad == EntidadCambio.Material && c.Accion == AccionCambio.Baja && c.Clave == "9999999999999");
            Assert.DoesNotContain(cambios, c => c.Entidad == EntidadCambio.Material && c.Clave == CodigoMaterial);
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
        public async Task AplicarCambios_BajaConHistorial_SeBloquea()
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

            Assert.Equal(0, resultado.Aplicados);
            Assert.Equal(2, resultado.Bloqueados.Count);
            Assert.NotNull(Alumnos.ObtenerPorCuenta(CuentaAlumno));
            Assert.NotNull(Materiales.ObtenerPorCodigo(CodigoMaterial));
        }

        [Fact]
        public async Task AsegurarHoja_EscribeEncabezadosDondeFaltan()
        {
            ConfigurarSync();

            await _sync.AsegurarHojaAsync();

            // El doble en memoria usa "principal"/"cambios" como tiendas.
            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, _fake.Leer("principal", "Alumnos")[0].ToArray());
            Assert.Equal(new[] { "Nombre", "CodigoBarras", "CantidadTotal" }, _fake.Leer("principal", "Inventario")[0].ToArray());
            Assert.Equal(11, _fake.Leer("principal", "Historial")[0].Count);
            Assert.Equal(new[] { "Nombre", "NumeroCuenta" }, _fake.Leer("cambios", "Alumnos")[0].ToArray());
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
        public async Task ConfirmarCambiosConsumidos_LimpiaArchivoDeCambios()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            _fake.Sembrar(HojaCambios, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { NombreAlumno, CuentaAlumno },
                new() { "Alumno Nuevo", "22222222" },
            });
            _fake.Sembrar(HojaCambios, "Inventario", new List<List<string>>
            {
                new() { "Nombre", "CodigoBarras", "CantidadTotal" },
                new() { NombreMaterial, CodigoMaterial, CantidadTotalMaterial.ToString() },
            });

            var cambios = await _sync.ObtenerCambiosPendientesAsync();
            var resultado = await _sync.AplicarCambiosAsync(cambios);
            await _sync.ConfirmarCambiosConsumidosAsync();

            Assert.Equal(1, resultado.Aplicados);
            Assert.NotNull(Alumnos.ObtenerPorCuenta("22222222"));
            Assert.Single(_fake.Leer(HojaCambios, "Alumnos")); // solo encabezado
            Assert.Single(_fake.Leer(HojaCambios, "Inventario")); // solo encabezado
            Assert.Empty(await _sync.ObtenerCambiosPendientesAsync()); // nada pendiente después
        }

        [Fact]
        public async Task ConfirmarCambiosConsumidos_ConEdicionIntermedia_NoBorra()
        {
            ConfigurarSync();
            CrearAlumnoYMaterial();
            _fake.Sembrar(HojaCambios, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { NombreAlumno, CuentaAlumno },
                new() { "Alumno Nuevo", "22222222" },
            });

            var cambios = await _sync.ObtenerCambiosPendientesAsync();
            // El revisor agrega otra fila antes de que se confirme el consumo.
            _fake.Sembrar(HojaCambios, "Alumnos", new List<List<string>>
            {
                new() { "Nombre", "NumeroCuenta" },
                new() { NombreAlumno, CuentaAlumno },
                new() { "Alumno Nuevo", "22222222" },
                new() { "Otro Más", "33333333" },
            });

            await _sync.AplicarCambiosAsync(cambios);
            await _sync.ConfirmarCambiosConsumidosAsync();

            Assert.Equal(4, _fake.Leer(HojaCambios, "Alumnos").Count); // encabezado + 3 filas: no se borró
            var pendientes = await _sync.ObtenerCambiosPendientesAsync();
            Assert.Single(pendientes); // solo la fila realmente nueva
            Assert.Equal("33333333", pendientes[0].Clave);
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
