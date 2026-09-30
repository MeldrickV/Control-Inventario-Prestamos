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
    /// Configuración de la sincronización con la hoja de Google del
    /// laboratorio (solo administrador): identificador de la computadora,
    /// hoja destino, credenciales OAuth de la institución (Client ID y
    /// Secret, creados una vez por institución en Google Cloud) e
    /// intervalo de revisión automática. Todo se guarda en la tabla
    /// `configuracion` de la base local: un solo ejecutable sirve para
    /// todos los laboratorios y facultades, sin recompilar nada.
    /// </summary>
    public class ConfiguracionSincronizacionDialog : SukiWindow
    {
        private readonly ConfiguracionRepository _config = new();
        private readonly GoogleOAuthClient _oauth = new();
        private readonly TextBox _txtComputadora = new() { Width = 320 };
        private readonly TextBox _txtLaboratorio = new() { Width = 320 };
        private readonly TextBox _txtHoja = new() { Width = 320 };
        private readonly TextBox _txtClientId = new() { Width = 320 };
        private readonly TextBox _txtClientSecret = new() { Width = 320, PasswordChar = '•' };
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
            _txtHoja.Text = _config.Obtener(SincronizacionService.ClaveHoja) ?? "";
            _txtClientId.Text = _config.Obtener(GoogleOAuthClient.ClaveClientId) ?? "";
            _txtClientSecret.Text = _config.Obtener(GoogleOAuthClient.ClaveClientSecret) ?? "";
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
            panel.Children.Add(new TextBlock { Text = "ID de la hoja de cálculo (se llena solo al conectar):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtHoja);
            panel.Children.Add(new TextBlock { Text = "Client ID de Google (OAuth, una vez por institución):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtClientId);
            panel.Children.Add(new TextBlock { Text = "Client Secret de Google (OAuth):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtClientSecret);
            panel.Children.Add(new TextBlock
            {
                Text = "El Client ID y Secret se crean una sola vez por institución en Google Cloud " +
                       "(OAuth Client ID tipo «App de escritorio» con acceso a Google Sheets API) y se pegan aquí.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 340,
            });
            panel.Children.Add(new TextBlock { Text = "Revisar la hoja cada (minutos):", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_numIntervalo);

            var btnConectar = new Button { Content = "Conectar con Google…", Classes = { "Flat" }, MinWidth = 170 };
            btnConectar.Click += (_, _) => Errores.Ejecutar(this, ConectarAsync);

            var btnDesconectar = new Button { Content = "Desconectar", Classes = { "Outlined" }, MinWidth = 110 };
            btnDesconectar.Click += (_, _) => Errores.Ejecutar(this, DesconectarAsync);

            var panelConexion = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
            panelConexion.Children.Add(btnConectar);
            panelConexion.Children.Add(btnDesconectar);
            panel.Children.Add(new TextBlock { Text = "Cuenta de Google:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_lblEstado);
            panel.Children.Add(panelConexion);
            panel.Children.Add(new TextBlock
            {
                Text = "Al conectar se abre el navegador para iniciar sesión; si esta computadora " +
                       "aún no tiene hoja, se crea sola con las pestañas necesarias.",
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
            _lblEstado.Text = _oauth.EstaConectado
                ? "Estado: cuenta de Google conectada."
                : "Estado: sin conectar.";
        }

        private async Task ConectarAsync()
        {
            // Lo capturado en los campos debe quedar en base ANTES de
            // conectar: el cliente OAuth lee de ahí, no de los TextBox.
            GuardarCampos();
            bool conectado;
            try
            {
                conectado = await _oauth.ConectarAsync(PedirCodigoManualAsync);
            }
            catch (InvalidOperationException ex)
            {
                await Dialogos.MostrarAdvertencia(this, ex.Message, "No se pudo conectar");
                return;
            }
            if (!conectado) return;

            var compu = _txtComputadora.Text?.Trim() ?? "";
            var servicio = new SincronizacionService(sheets: _oauth);
            var id = await servicio.AsegurarHojaAsync("LabInventario - " + (compu.Length > 0 ? compu : "Laboratorio"));
            _txtHoja.Text = id;
            GuardarCampos();
            ActualizarEstado();
            await Dialogos.MostrarInfo(this, "Cuenta conectada y hoja lista para sincronizar.", "Listo");
            Close();
        }

        private async Task DesconectarAsync()
        {
            await _oauth.DesconectarAsync();
            ActualizarEstado();
            await Dialogos.MostrarInfo(this, "Cuenta de Google desconectada en esta computadora.", "Listo");
        }

        private async Task<string?> PedirCodigoManualAsync(string url)
        {
            var dialogo = new CodigoAuthDialog(url);
            await dialogo.ShowDialog(this);
            return dialogo.Codigo;
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
            _config.Establecer(SincronizacionService.ClaveHoja, _txtHoja.Text?.Trim() ?? "");
            _config.Establecer(GoogleOAuthClient.ClaveClientId, _txtClientId.Text?.Trim() ?? "");
            _config.Establecer(GoogleOAuthClient.ClaveClientSecret, _txtClientSecret.Text?.Trim() ?? "");
            _config.Establecer(SincronizacionService.ClaveIntervalo, ((int)(_numIntervalo.Value ?? 5)).ToString());
        }
    }
}
