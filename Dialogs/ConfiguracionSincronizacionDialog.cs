using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Data;
using LabInventario.Helpers;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Configuración de la sincronización con la nube del laboratorio
    /// (solo administrador): identificador de la computadora, URL del Apps
    /// Script desplegado en la cuenta del laboratorio, clave compartida e
    /// intervalo de revisión automática. Todo se guarda en la tabla
    /// `configuracion` de la base local: un solo ejecutable sirve para
    /// todos los laboratorios y facultades, sin recompilar nada.
    /// </summary>
    public class ConfiguracionSincronizacionDialog : SukiWindow
    {
        private readonly ConfiguracionRepository _config = new();
        private readonly TextBox _txtComputadora = new() { Width = 320 };
        private readonly TextBox _txtLaboratorio = new() { Width = 320 };
        private readonly TextBox _txtUrl = new() { Width = 320 };
        private readonly TextBox _txtClave = new() { Width = 320, PasswordChar = '•' };
        private readonly NumericUpDown _numIntervalo = new() { Width = 320, Minimum = 1, Maximum = 120, FormatString = "0" };
        private readonly TextBlock _lblEstado = new() { TextWrapping = TextWrapping.Wrap, Width = 340 };

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
            _txtUrl.Text = _config.Obtener(AppsScriptClient.ClaveScriptUrl) ?? "";
            _txtClave.Text = _config.Obtener(AppsScriptClient.ClaveSecreta) ?? "";
            _numIntervalo.Value = new SincronizacionService().IntervaloMinutos();

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
                Text = "Conecta esta computadora con el Apps Script desplegado en la cuenta " +
                       "del laboratorio. Sin URL y clave, la sincronización queda desactivada.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
            });
            panel.Children.Add(new TextBlock { Text = "Identificador de la computadora (p. ej. LAB-1):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtComputadora);
            panel.Children.Add(new TextBlock { Text = "Nombre del laboratorio:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtLaboratorio);
            panel.Children.Add(new TextBlock { Text = "URL del script (termina en /exec):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtUrl);
            panel.Children.Add(new TextBlock { Text = "Clave del script:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtClave);
            panel.Children.Add(new TextBlock
            {
                Text = "La URL y la clave salen del despliegue del script (una vez por laboratorio, " +
                       "ver el instructivo). Cada laboratorio usa los suyos.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
            });
            panel.Children.Add(new TextBlock { Text = "Revisar la nube cada (minutos):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_numIntervalo);

            var btnProbar = new Button { Content = "Probar conexión…", Classes = { "Flat" }, MinWidth = 170 };
            btnProbar.Click += (_, _) => Errores.Ejecutar(this, ProbarConexionAsync);

            var panelConexion = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
            panelConexion.Children.Add(btnProbar);
            panel.Children.Add(new TextBlock { Text = "Estado:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_lblEstado);
            panel.Children.Add(panelConexion);
            panel.Children.Add(new TextBlock
            {
                Text = "Al probar se verifican la URL y la clave y se dejan listas las pestañas " +
                       "de la nube si faltan.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
                Margin = new Avalonia.Thickness(0, 6, 0, 0),
            });
            panel.Children.Add(panelBotones);

            // Scroll con altura máxima: el diálogo ya no depende del alto
            // de la pantalla, lo que no quepa se desplaza.
            Content = new GlassCard
            {
                Margin = new Avalonia.Thickness(20),
                Content = new ScrollViewer { Content = panel, MaxHeight = 540 },
            };
            ActualizarEstado();
        }

        private void ActualizarEstado()
        {
            var url = _config.Obtener(AppsScriptClient.ClaveScriptUrl) ?? "";
            var clave = _config.Obtener(AppsScriptClient.ClaveSecreta) ?? "";
            _lblEstado.Text = url.Length > 0 && clave.Length > 0
                ? "Estado: URL y clave guardadas."
                : "Estado: sin configurar.";
        }

        private async Task ProbarConexionAsync()
        {
            // Lo capturado debe quedar en base ANTES de probar: el cliente
            // lee de ahí, no de los TextBox.
            GuardarCampos();
            var cliente = AppsScriptClient.CrearSiConectado();
            if (cliente is null)
            {
                await Dialogos.MostrarAdvertencia(this,
                    "Falta la URL del script o la clave. Pégalas y pulsa Probar conexión.",
                    "Sin configurar");
                return;
            }

            var servicio = new SincronizacionService(sheets: cliente);
            await servicio.AsegurarHojaAsync();
            ActualizarEstado();
            await Dialogos.MostrarInfo(this,
                "Conexión correcta: el script respondió y las pestañas están listas.", "Listo");
        }

        private void Guardar()
        {
            GuardarCampos();
            Close();
        }

        private void GuardarCampos()
        {
            _config.Establecer(SincronizacionService.ClaveComputadora, _txtComputadora.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveLaboratorio, _txtLaboratorio.Text?.Trim() ?? "");
            _config.Establecer(AppsScriptClient.ClaveScriptUrl, _txtUrl.Text?.Trim() ?? "");
            _config.Establecer(AppsScriptClient.ClaveSecreta, _txtClave.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveIntervalo, ((int)(_numIntervalo.Value ?? 5)).ToString());
        }
    }
}
