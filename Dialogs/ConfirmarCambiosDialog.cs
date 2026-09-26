using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Muestra los cambios del catálogo (alumnos e inventario) detectados
    /// en la hoja de Google y pide confirmación antes de aplicarlos en la
    /// base local. Cada línea describe "lo que se está enviando" (alta,
    /// cambio o baja con sus valores).
    /// </summary>
    public class ConfirmarCambiosDialog : SukiWindow
    {
        /// <summary>True si el administrador eligió aplicar los cambios.</summary>
        public bool Confirmado { get; private set; }

        public ConfirmarCambiosDialog(IList<CambioSincronizacion> cambios)
        {
            Title = "Cambios pendientes desde Google Sheets";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var lineas = cambios.Select(c => c.Detalle).ToList();

            var btnAplicar = new Button { Content = "Aplicar cambios", Classes = { "Flat" }, MinWidth = 130, IsDefault = true };
            btnAplicar.Click += (_, _) => { Confirmado = true; Close(); };

            var btnRechazar = new Button { Content = "Rechazar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnRechazar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnAplicar);
            panelBotones.Children.Add(btnRechazar);

            var panel = new StackPanel { Spacing = 8, Width = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = "La hoja de Google trae estos cambios para el catálogo local. " +
                       "Revísalos y decide si se aplican:",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 520,
            });
            panel.Children.Add(new ListBox
            {
                ItemsSource = lineas,
                Width = 520,
                MaxHeight = 320,
            });
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }
    }
}
