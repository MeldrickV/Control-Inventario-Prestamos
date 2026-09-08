using Avalonia.Controls;
using Avalonia.Layout;
using LabInventario.Helpers;
using LabInventario.Models;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Formulario modal para crear o editar un alumno.
    /// El resultado (Nombre, NumeroCuenta) queda en <see cref="Resultado"/>
    /// tras cerrar el diálogo con "Guardar"; es null si se cancela.
    /// </summary>
    public class AlumnoDialog : SukiWindow
    {
        private readonly TextBox _txtNombre = new() { Width = 280 };
        private readonly TextBox _txtCuenta = new() { Width = 280 };

        public (string Nombre, string NumeroCuenta)? Resultado { get; private set; }

        public AlumnoDialog(Alumno? alumno = null)
        {
            Title = alumno is null ? "Nuevo alumno" : "Editar alumno";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtNombre.Text = alumno?.Nombre ?? "";
            _txtCuenta.Text = alumno?.NumeroCuenta ?? "";

            var btnGuardar = new Button { Content = "Guardar", Classes = { "Flat" }, MinWidth = 90, IsDefault = true };
            btnGuardar.Click += async (_, _) => await Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 90, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 6, Width = 320 };
            panel.Children.Add(new TextBlock { Text = "Nombre completo:" });
            panel.Children.Add(_txtNombre);
            panel.Children.Add(new TextBlock { Text = "Número de cuenta:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            panel.Children.Add(_txtCuenta);
            panel.Children.Add(new Border { Height = 10 });
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
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
