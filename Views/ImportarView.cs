using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LabInventario.Data;
using LabInventario.Dialogs;
using LabInventario.Helpers;
using LabInventario.Services;

namespace LabInventario.Views
{
    /// <summary>
    /// Pestaña de importación masiva. Flujo:
    /// 1. El usuario elige qué va a importar (Alumnos o Materiales).
    /// 2. Selecciona un archivo (.xlsx, .xls, .csv, .txt, .sql, .db/.sqlite).
    /// 3. ImportService lee el archivo y sugiere un mapeo de columnas.
    /// 4. Se muestra un diálogo gráfico para confirmar/corregir el mapeo.
    /// 5. Se insertan los registros válidos, reportando duplicados y errores.
    /// </summary>
    public class ImportarView : UserControl
    {
        private readonly ImportService _servicio = new();
        private readonly AlumnoRepository _alumnoRepo = new();
        private readonly MaterialRepository _materialRepo = new();

        private readonly ComboBox _cmbEntidad = new() { Width = 180 };
        private readonly TextBox _txtLog = new()
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,monospace"),
        };

        public ImportarView()
        {
            var lblTitulo = new TextBlock
            {
                Text = "Importación masiva de datos (Excel, CSV/TXT, SQL, .db/.sqlite)",
                FontWeight = FontWeight.Bold,
                FontSize = 14,
            };

            _cmbEntidad.ItemsSource = new[] { "Alumnos", "Materiales" };
            _cmbEntidad.SelectedIndex = 0;

            var panelOpciones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            panelOpciones.Children.Add(new TextBlock { Text = "Tipo de datos a importar:", VerticalAlignment = VerticalAlignment.Center });
            panelOpciones.Children.Add(_cmbEntidad);

            var btnImportar = new Button { Content = "Seleccionar archivo e importar...", Width = 260, Height = 32 };
            btnImportar.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), IniciarImportacion);

            var lblLog = new TextBlock { Text = "Registro de importaciones:" };

            var scrollLog = new ScrollViewer { Content = _txtLog };

            var raiz = new DockPanel { Margin = new Avalonia.Thickness(15) };
            DockPanel.SetDock(lblTitulo, Dock.Top);
            DockPanel.SetDock(panelOpciones, Dock.Top);
            DockPanel.SetDock(btnImportar, Dock.Top);
            DockPanel.SetDock(lblLog, Dock.Top);

            raiz.Children.Add(lblTitulo);
            raiz.Children.Add(new Border { Height = 10 });
            raiz.Children.Add(panelOpciones);
            raiz.Children.Add(new Border { Height = 10 });
            raiz.Children.Add(btnImportar);
            raiz.Children.Add(new Border { Height = 10 });
            raiz.Children.Add(lblLog);
            raiz.Children.Add(scrollLog); // último hijo: llena el resto

            Content = raiz;
        }

        private void Log(string mensaje) => _txtLog.Text += mensaje + Environment.NewLine;

        private Window? VentanaPropietaria() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private async Task IniciarImportacion()
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;

            var rutaArchivo = await Dialogos.SeleccionarArchivo(propietaria, "Selecciona el archivo a importar",
                new FilePickerFileType("Archivos soportados") { Patterns = new[] { "*.xlsx", "*.xls", "*.csv", "*.txt", "*.sql", "*.db", "*.sqlite", "*.sqlite3" } },
                new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx", "*.xls" } },
                new FilePickerFileType("Texto / CSV") { Patterns = new[] { "*.csv", "*.txt" } },
                new FilePickerFileType("SQL") { Patterns = new[] { "*.sql" } },
                new FilePickerFileType("Base de datos SQLite") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" } },
                FilePickerFileTypes.All);

            if (rutaArchivo is null) return;

            var esAlumnos = _cmbEntidad.SelectedItem?.ToString() == "Alumnos";

            DatosImportados datos;
            try
            {
                datos = _servicio.LeerArchivo(rutaArchivo, esAlumnos);
            }
            catch (Exception error)
            {
                await Dialogos.MostrarError(propietaria, error.Message, "Error al leer el archivo");
                Log($"[ERROR] No se pudo leer '{rutaArchivo}': {error.Message}");
                return;
            }

            if (datos.Headers.Count == 0 || datos.Filas.Count == 0)
            {
                await Dialogos.MostrarAdvertencia(propietaria, "No se encontraron datos para importar en ese archivo.", "Archivo vacío");
                return;
            }

            var camposDestino = esAlumnos ? ImportService.CamposAlumno : ImportService.CamposMaterial;
            var mapeoSugerido = _servicio.SugerirMapeo(datos.Headers, camposDestino);

            var dialogoMapeo = new MapeoColumnasDialog(datos.Headers, camposDestino, mapeoSugerido, datos.Filas);
            await dialogoMapeo.ShowDialog(propietaria);
            if (dialogoMapeo.Resultado is null) return;

            await EjecutarImportacion(propietaria, esAlumnos, dialogoMapeo.Resultado, datos.Filas);
        }

        private async Task EjecutarImportacion(Window propietaria, bool esAlumnos, Dictionary<string, int?> mapeo, List<List<string?>> filas)
        {
            int insertados = 0, duplicados = 0, errores = 0;

            foreach (var fila in filas)
            {
                var resultado = esAlumnos
                    ? ImportarFilaAlumno(mapeo, fila)
                    : ImportarFilaMaterial(mapeo, fila);

                switch (resultado)
                {
                    case "insertado": insertados++; break;
                    case "duplicado": duplicados++; break;
                    default: errores++; break;
                }
            }

            var entidadTexto = esAlumnos ? "Alumnos" : "Materiales";
            Log($"Importación de {entidadTexto} finalizada -> " +
                $"Insertados: {insertados}, Duplicados omitidos: {duplicados}, Errores: {errores}");

            await Dialogos.MostrarInfo(propietaria,
                $"Insertados: {insertados}\nDuplicados omitidos: {duplicados}\nErrores: {errores}",
                "Importación completada");
        }

        private string ImportarFilaAlumno(Dictionary<string, int?> mapeo, List<string?> fila)
        {
            try
            {
                var idxNombre = mapeo.GetValueOrDefault("Nombre");
                var idxCuenta = mapeo.GetValueOrDefault("NumeroCuenta");
                if (idxNombre is null || idxCuenta is null) return "error";

                var nombre = fila.ElementAtOrDefault(idxNombre.Value)?.Trim() ?? "";
                var cuenta = fila.ElementAtOrDefault(idxCuenta.Value)?.Trim() ?? "";
                if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(cuenta)) return "error";

                if (_alumnoRepo.ExisteNumeroCuenta(cuenta)) return "duplicado";

                _alumnoRepo.Crear(nombre, cuenta);
                return "insertado";
            }
            catch
            {
                return "error";
            }
        }

        private string ImportarFilaMaterial(Dictionary<string, int?> mapeo, List<string?> fila)
        {
            try
            {
                var idxCodigo = mapeo.GetValueOrDefault("CodigoBarras");
                var idxNombre = mapeo.GetValueOrDefault("Nombre");
                var idxCantidad = mapeo.GetValueOrDefault("CantidadTotal");
                if (idxCodigo is null || idxNombre is null) return "error";

                var codigo = fila.ElementAtOrDefault(idxCodigo.Value)?.Trim() ?? "";
                var nombre = fila.ElementAtOrDefault(idxNombre.Value)?.Trim() ?? "";
                if (string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(nombre)) return "error";

                var cantidad = 0;
                if (idxCantidad is not null)
                {
                    var valorCantidad = fila.ElementAtOrDefault(idxCantidad.Value);
                    if (!string.IsNullOrWhiteSpace(valorCantidad))
                        cantidad = (int)double.Parse(valorCantidad);
                }

                if (_materialRepo.ExisteCodigo(codigo)) return "duplicado";

                _materialRepo.Crear(codigo, nombre, cantidad);
                return "insertado";
            }
            catch
            {
                return "error";
            }
        }
    }
}
