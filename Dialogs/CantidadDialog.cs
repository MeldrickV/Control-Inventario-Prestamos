using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Pide al usuario cuántas unidades está devolviendo, entre 1 y un
    /// máximo (el total pendiente del grupo o de un registro específico).
    /// Se usa desde la pestaña de historial para las devoluciones
    /// manuales, y permite devoluciones parciales (ej. devolver 1 de 3).
    /// </summary>
    public class CantidadDialog : Window
    {
        private readonly NumericUpDown _numCantidad;

        public int? Resultado { get; private set; }

        public CantidadDialog(string mensaje, int maximo, int valorInicial)
        {
            Title = "¿Cuántos regresan?";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _numCantidad = new NumericUpDown
            {
                Minimum = 1,
                Maximum = maximo,
                Value = valorInicial,
                Width = 140,
                FormatString = "0",
            };

            var btnAceptar = new Button { Content = "Aceptar", Width = 90, IsDefault = true };
            btnAceptar.Click += (_, _) =>
            {
                Resultado = (int)(_numCantidad.Value ?? valorInicial);
                Close();
            };

            var btnCancelar = new Button { Content = "Cancelar", Width = 90, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 14, 0, 0),
            };
            panelBotones.Children.Add(btnAceptar);
            panelBotones.Children.Add(btnCancelar);

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8, Width = 280 };
            raiz.Children.Add(new TextBlock { Text = mensaje, TextWrapping = TextWrapping.Wrap });
            raiz.Children.Add(_numCantidad);
            raiz.Children.Add(panelBotones);

            Content = raiz;
        }
    }
}
