using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>Diálogo modal para que el administrador cambie su contraseña.</summary>
    public class CambiarPasswordDialog : SukiWindow
    {
        private readonly AuthService _auth = new();

        private readonly TextBox _txtActual = new() { Width = 260, PasswordChar = '*' };
        private readonly TextBox _txtNueva = new() { Width = 260, PasswordChar = '*' };
        private readonly TextBox _txtConfirmar = new() { Width = 260, PasswordChar = '*' };
        private readonly TextBlock _lblError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };

        public CambiarPasswordDialog()
        {
            Title = "Cambiar contraseña de administrador";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var btnGuardar = new Button { Content = "Guardar", Classes = { "Flat" }, MinWidth = 100, IsDefault = true };
            btnGuardar.Click += async (_, _) => await Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 6, Width = 300 };
            panel.Children.Add(new TextBlock { Text = "Contraseña actual:" });
            panel.Children.Add(_txtActual);
            panel.Children.Add(new TextBlock { Text = "Nueva contraseña:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            panel.Children.Add(_txtNueva);
            panel.Children.Add(new TextBlock { Text = "Confirmar nueva contraseña:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            panel.Children.Add(_txtConfirmar);
            panel.Children.Add(_lblError);
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }

        private async Task Guardar()
        {
            if (!_auth.ValidarPasswordAdmin(_txtActual.Text ?? ""))
            {
                _lblError.Text = "La contraseña actual no es correcta.";
                return;
            }

            if ((_txtNueva.Text ?? "").Length < 4)
            {
                _lblError.Text = "La nueva contraseña debe tener al menos 4 caracteres.";
                return;
            }

            if (_txtNueva.Text != _txtConfirmar.Text)
            {
                _lblError.Text = "Las contraseñas nuevas no coinciden.";
                return;
            }

            _auth.EstablecerPasswordAdmin(_txtNueva.Text ?? "");
            await Dialogos.MostrarInfo(this, "Contraseña actualizada correctamente.", "Listo");
            Close();
        }
    }
}
