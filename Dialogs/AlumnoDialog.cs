using Avalonia.Controls;
using Avalonia.Layout;
using LabInventario.Helpers;
using LabInventario.Models;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Formulario modal para crear o editar un alumno.
    /// El resultado (Nombre, NumeroCuenta) queda en <see cref="Resultado"/>
    /// tras cerrar el diálogo con "Guardar"; es null si se cancela.
    /// </summary>
    public class AlumnoDialog : Window
    {
        private readonly TextBox _txtNombre = new() { Width = 280 };
        private readonly TextBox _txtCuenta = new() { Width = 280 };

        public (string Nombre, string NumeroCuenta)? Resultado { get; private set; }

        public AlumnoDialog(Alumno? alumno = null)
        {
            Title = alumno is null ? "Nuevo alumno" : "Editar alumno";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtNombre.Text = alumno?.Nombre ?? "";
            _txtCuenta.Text = alumno?.NumeroCuenta ?? "";

            var btnGuardar = new Button { Content = "Guardar", Width = 90, IsDefault = true };
            btnGuardar.Click += async (_, _) => await Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Width = 90, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 6, Width = 320 };
            raiz.Children.Add(new TextBlock { Text = "Nombre completo:" });
            raiz.Children.Add(_txtNombre);
            raiz.Children.Add(new TextBlock { Text = "Número de cuenta:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            raiz.Children.Add(_txtCuenta);
            raiz.Children.Add(new Border { Height = 10 });
            raiz.Children.Add(panelBotones);

            Content = raiz;
        }

        private async Task Guardar()
        {
            var nombre = _txtNombre.Text?.Trim() ?? "";
            var cuenta = _txtCuenta.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(cuenta))
            {
                await Dialogos.MostrarAdvertencia(this, "Nombre y número de cuenta son obligatorios.", "Datos incompletos");
                return;
            }

            Resultado = (nombre, cuenta);
            Close();
        }
    }
}
