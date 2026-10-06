using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Muestra los cambios del catálogo definitivo de la hoja (altas,
    /// cambios y bajas) con una casilla por renglón. Al aplicar, solo los
    /// marcados se ejecutan en la base local; los no marcados se toman como
    /// rechazo parcial y la app restaura esas filas en la hoja con los datos
    /// locales. Cancelar no toca nada en ningún lado.
    /// </summary>
    public class ConfirmarCambiosDialog : SukiWindow
    {
        /// <summary>Renglones marcados al pulsar Aplicar.</summary>
        public List<CambioSincronizacion> Seleccionados { get; private set; } = new();

        /// <summary>Renglones sin marcar al pulsar Aplicar (rechazo parcial).</summary>
        public List<CambioSincronizacion> Rechazados { get; private set; } = new();

        private readonly List<(CambioSincronizacion Cambio, CheckBox Casilla)> _renglones = new();

        public ConfirmarCambiosDialog(IList<CambioSincronizacion> cambios)
        {
            Title = "Cambios pendientes desde Drive";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var lista = new StackPanel { Spacing = 4 };
            foreach (var cambio in cambios)
            {
                var casilla = new CheckBox { Content = cambio.Detalle, IsChecked = true };
                _renglones.Add((cambio, casilla));
                lista.Children.Add(casilla);
            }

            var btnTodos = new Button { Content = "Todos", Classes = { "Outlined" }, MinWidth = 80 };
            btnTodos.Click += (_, _) => MarcarTodos(true);

            var btnNinguno = new Button { Content = "Ninguno", Classes = { "Outlined" }, MinWidth = 80 };
            btnNinguno.Click += (_, _) => MarcarTodos(false);

            var panelMarcar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            panelMarcar.Children.Add(new TextBlock { Text = "Marcar:", VerticalAlignment = VerticalAlignment.Center });
            panelMarcar.Children.Add(btnTodos);
            panelMarcar.Children.Add(btnNinguno);

            var btnAplicar = new Button { Content = "Aplicar seleccionados", Classes = { "Flat" }, MinWidth = 160, IsDefault = true };
            btnAplicar.Click += (_, _) => { Recoger(); Close(); };

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnAplicar);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 8, Width = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = "La hoja trae estas propuestas para el catálogo local. " +
                       "Marca las que sí se aplican; lo no marcado se restaura en la hoja " +
                       "con los datos de esta computadora. Cancelar no cambia nada.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 520,
            });
            panel.Children.Add(panelMarcar);
            panel.Children.Add(new ScrollViewer
            {
                Content = lista,
                Width = 520,
                MaxHeight = 320,
            });
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }

        private void MarcarTodos(bool valor)
        {
            foreach (var (_, casilla) in _renglones)
                casilla.IsChecked = valor;
        }

        private void Recoger()
        {
            Seleccionados = _renglones
                .Where(r => r.Casilla.IsChecked == true)
                .Select(r => r.Cambio)
                .ToList();
            Rechazados = _renglones
                .Where(r => r.Casilla.IsChecked != true)
                .Select(r => r.Cambio)
                .ToList();
        }
    }
}
