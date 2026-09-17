using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Pide al administrador cuántos días de antigüedad deben tener los
    /// préstamos YA DEVUELTOS para ser borrados del historial. No borra
    /// nada por su cuenta: solo devuelve el valor elegido en
    /// <see cref="DiasRetencion"/> (o null si se canceló) para que quien
    /// lo abrió haga la limpieza, previa confirmación (ver el menú
    /// Administración de MainWindow).
    /// </summary>
    public class LimpiarHistorialDialog : SukiWindow
    {
        private readonly TextBox _txtDias = new() { MaxLength = 4, Width = 120 };
        private readonly TextBlock _lblError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Width = 330 };

        /// <summary>Días de retención elegidos (1 o más), o null si el administrador canceló.</summary>
        public int? DiasRetencion { get; private set; }

        public LimpiarHistorialDialog()
        {
            Title = "Limpiar historial antiguo";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtDias.Text = "30";

            var lblExplicacion = new TextBlock
            {
                Text = "Elimina del historial los préstamos YA DEVUELTOS cuya fecha de regreso " +
                       "tenga más de la antigüedad indicada (en días). " +
                       "Los préstamos activos nunca se tocan.\n\n" +
                       "Antes de borrar se mostrará cuántos registros se eliminarán.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 330,
            };

            var btnSiguiente = new Button { Content = "Siguiente", Classes = { "Danger" }, MinWidth = 100, IsDefault = true };
            btnSiguiente.Click += (_, _) => IntentarAvanzar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 14, 0, 0),
            };
            panelBotones.Children.Add(btnSiguiente);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 8, Width = 330 };
            panel.Children.Add(lblExplicacion);
            panel.Children.Add(new TextBlock { Text = "Conservar devoluciones de al menos:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtDias);
            panel.Children.Add(_lblError);
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };

            _txtDias.TextChanged += (_, _) => _lblError.Text = "";
        }

        private void IntentarAvanzar()
        {
            var texto = _txtDias.Text?.Trim() ?? "";
            if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var dias) || dias < 1)
            {
                _lblError.Text = "Ingresa un número entero de días (1 o más).";
                return;
            }

            DiasRetencion = dias;
            Close();
        }
    }
}