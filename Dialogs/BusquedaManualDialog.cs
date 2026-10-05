using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Respaldo cuando el escáner falla: busca alumnos (nombre o cuenta) y
    /// materiales (nombre o código) mientras se escribe, con casillas para
    /// filtrar por tipo. Elegir un renglón (doble clic o Seleccionar) lo
    /// devuelve para procesarlo igual que un código escaneado.
    /// </summary>
    public class BusquedaManualDialog : SukiWindow
    {
        /// <summary>Renglón elegido, o null si se canceló.</summary>
        public ResultadoBusqueda? Seleccion { get; private set; }

        private readonly BusquedaManualService _busqueda = new();
        private readonly TextBox _txtBuscar = new() { Width = 460 };
        private readonly CheckBox _chkAlumnos = new() { Content = "Alumnos", IsChecked = true };
        private readonly CheckBox _chkMateriales = new() { Content = "Materiales", IsChecked = true };
        private readonly ListBox _lstResultados = new() { Width = 460, MaxHeight = 300 };

        private List<ResultadoBusqueda> _actuales = new();

        public BusquedaManualDialog()
        {
            Title = "Buscar alumno o material";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtBuscar.TextChanged += (_, _) => Actualizar();
            _chkAlumnos.PropertyChanged += FiltroCambiado;
            _chkMateriales.PropertyChanged += FiltroCambiado;
            _lstResultados.DoubleTapped += (_, _) => Aceptar();

            var btnSeleccionar = new Button { Content = "Seleccionar", Classes = { "Flat" }, MinWidth = 110, IsDefault = true };
            btnSeleccionar.Click += (_, _) => Aceptar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnSeleccionar);
            panelBotones.Children.Add(btnCancelar);

            var panelFiltros = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            panelFiltros.Children.Add(_chkAlumnos);
            panelFiltros.Children.Add(_chkMateriales);

            var panel = new StackPanel { Spacing = 8, Width = 480 };
            panel.Children.Add(new TextBlock
            {
                Text = "Escribe nombre, número de cuenta o código de barras. " +
                       "Doble clic o Seleccionar para usar el renglón.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 480,
            });
            panel.Children.Add(new TextBlock { Text = "Buscar:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtBuscar);
            panel.Children.Add(panelFiltros);
            panel.Children.Add(_lstResultados);
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
            Opened += (_, _) => _txtBuscar.Focus();
        }

        private void FiltroCambiado(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ToggleButton.IsCheckedProperty) Actualizar();
        }

        private void Actualizar()
        {
            _actuales = _busqueda.Buscar(
                _txtBuscar.Text ?? "",
                _chkAlumnos.IsChecked == true,
                _chkMateriales.IsChecked == true);
            _lstResultados.ItemsSource = _actuales;
        }

        private void Aceptar()
        {
            if (_lstResultados.SelectedItem is not ResultadoBusqueda elegido)
            {
                _lstResultados.Focus();
                return;
            }
            Seleccion = elegido;
            Close();
        }
    }
}
