using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using LabInventario.Data;
using LabInventario.Helpers;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Configuración de la sincronización con la hoja de Google del
    /// laboratorio: identificador de la computadora, hoja destino, ruta
    /// del archivo de credenciales e intervalo de revisión automática.
    /// Todo se guarda en la tabla `configuracion`. La capa de
    /// autenticación con Google se conectará aquí cuando se decida
    /// (Service Account u OAuth): los campos ya están preparados.
    /// </summary>
    public class ConfiguracionSincronizacionDialog : SukiWindow
    {
        private readonly ConfiguracionRepository _config = new();
        private readonly TextBox _txtComputadora = new() { Width = 320 };
        private readonly TextBox _txtLaboratorio = new() { Width = 320 };
        private readonly TextBox _txtHoja = new() { Width = 320 };
        private readonly TextBox _txtCredenciales = new() { Width = 250, IsReadOnly = true };
        private readonly NumericUpDown _numIntervalo = new() { Width = 320, Minimum = 1, Maximum = 120, FormatString = "0" };

        public ConfiguracionSincronizacionDialog()
        {
            Title = "Configuración de sincronización";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtComputadora.Text = _config.Obtener(SincronizacionService.ClaveComputadora) ?? "";
            _txtLaboratorio.Text = _config.Obtener(SincronizacionService.ClaveLaboratorio) ?? "";
            _txtHoja.Text = _config.Obtener(SincronizacionService.ClaveHoja) ?? "";
            _txtCredenciales.Text = _config.Obtener(SincronizacionService.ClaveCredenciales) ?? "";
            _numIntervalo.Value = new SincronizacionService().IntervaloMinutos();

            var btnExaminar = new Button { Content = "Examinar…", Classes = { "Outlined" }, MinWidth = 60 };
            btnExaminar.Click += async (_, _) =>
            {
                var ruta = await Dialogos.SeleccionarArchivo(this, "Archivo de credenciales JSON",
                    new FilePickerFileType("Credenciales JSON") { Patterns = new[] { "*.json" } });
                if (ruta is not null) _txtCredenciales.Text = ruta;
            };

            var panelCredenciales = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            panelCredenciales.Children.Add(_txtCredenciales);
            panelCredenciales.Children.Add(btnExaminar);

            var btnGuardar = new Button { Content = "Guardar", Classes = { "Flat" }, MinWidth = 100, IsDefault = true };
            btnGuardar.Click += (_, _) => Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 8, Width = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = "Identifica esta computadora y la hoja de Google de su laboratorio. " +
                       "Sin el ID de la hoja la sincronización queda desactivada.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
            });
            panel.Children.Add(new TextBlock { Text = "Identificador de la computadora (p. ej. LAB-1):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtComputadora);
            panel.Children.Add(new TextBlock { Text = "Nombre del laboratorio:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtLaboratorio);
            panel.Children.Add(new TextBlock { Text = "ID de la hoja de cálculo (spreadsheetId):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtHoja);
            panel.Children.Add(new TextBlock { Text = "Archivo de credenciales JSON:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(panelCredenciales);
            panel.Children.Add(new TextBlock { Text = "Revisar la hoja cada (minutos):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_numIntervalo);
            panel.Children.Add(new TextBlock
            {
                Text = "Nota: la conexión con Google se activará cuando se defina la autenticación; " +
                       "estos datos dejan todo preparado.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
                Margin = new Avalonia.Thickness(0, 6, 0, 0),
            });
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }

        private void Guardar()
        {
            _config.Establecer(SincronizacionService.ClaveComputadora, _txtComputadora.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveLaboratorio, _txtLaboratorio.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveHoja, _txtHoja.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveCredenciales, _txtCredenciales.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveIntervalo, ((int)(_numIntervalo.Value ?? 5)).ToString());
            Close();
        }
    }
}
