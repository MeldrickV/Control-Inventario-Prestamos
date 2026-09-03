using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using LabInventario.Services;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Permite al administrador ajustar el patrón (expresión regular) que
    /// se usa para adivinar si un código escaneado es un número de cuenta
    /// de alumno o un código de material, por si el patrón por defecto
    /// (solo dígitos, 6 a 12 caracteres) no calza con los números de cuenta
    /// reales de la institución.
    /// </summary>
    public class ConfiguracionEscaneoDialog : Window
    {
        private readonly DetectorPatrones _detector = new();
        private readonly TextBox _txtPatron = new() { Width = 320 };
        private readonly TextBlock _lblError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };

        public ConfiguracionEscaneoDialog()
        {
            Title = "Configuración de escaneo";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var lblExplicacion = new TextBlock
            {
                Text = "Patrón (expresión regular) que identifica un número de cuenta " +
                       "de alumno. Si un código escaneado NO coincide, se busca " +
                       "directamente como material.\n\n" +
                       "Por defecto: solo dígitos, de 6 a 12 caracteres.",
                TextWrapping = TextWrapping.Wrap,
                Width = 330,
            };

            _txtPatron.Text = _detector.ObtenerPatron();

            var btnRestaurar = new Button { Content = "Restaurar valor por defecto", Width = 220 };
            btnRestaurar.Click += (_, _) => _txtPatron.Text = DetectorPatrones.PatronPorDefecto;

            var btnGuardar = new Button { Content = "Guardar", Width = 100, IsDefault = true };
            btnGuardar.Click += async (_, _) => await Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Width = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8, Width = 330 };
            raiz.Children.Add(lblExplicacion);
            raiz.Children.Add(new TextBlock { Text = "Patrón:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            raiz.Children.Add(_txtPatron);
            raiz.Children.Add(btnRestaurar);
            raiz.Children.Add(_lblError);
            raiz.Children.Add(panelBotones);

            Content = raiz;
        }

        private async Task Guardar()
        {
            var patron = _txtPatron.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(patron))
            {
                _lblError.Text = "El patrón no puede estar vacío.";
                return;
            }

            try
            {
                _ = new Regex(patron);
            }
            catch (RegexParseException)
            {
                _lblError.Text = "Esa expresión regular no es válida.";
                return;
            }

            _detector.EstablecerPatron(patron);
            await Dialogos.MostrarInfo(this, "Patrón de escaneo actualizado.", "Listo");
            Close();
        }
    }
}
