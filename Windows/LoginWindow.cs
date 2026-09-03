using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using LabInventario.Models;
using LabInventario.Services;

namespace LabInventario.Windows
{
    /// <summary>
    /// Pantalla inicial: el operador elige con qué rol entra.
    /// - "Usuario" no pide contraseña (así de rápido puede alguien haciendo
    ///   prácticas registrar una salida o entrada).
    /// - "Administrador" exige la contraseña guardada (hash) en la BD,
    ///   porque desde ese rol se puede tocar la información delicada de
    ///   alumnos y materiales.
    /// </summary>
    public class LoginWindow : Window
    {
        private readonly AuthService _auth = new();

        private readonly RadioButton _radioUsuario = new() { Content = "Usuario (registrar salidas/entradas)", GroupName = "rol", IsChecked = true };
        private readonly RadioButton _radioAdmin = new() { Content = "Administrador (gestionar alumnos/materiales)", GroupName = "rol" };
        private readonly TextBox _txtPassword = new() { Width = 260, PasswordChar = '*', IsEnabled = false };
        private readonly TextBlock _lblPassword = new() { Text = "Contraseña de administrador:", IsEnabled = false };
        private readonly TextBlock _lblHint = new() { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        private readonly TextBlock _lblError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };

        public bool Aceptado { get; private set; }
        public Rol RolSeleccionado { get; private set; }

        public LoginWindow()
        {
            Title = "Iniciar sesión — Laboratorio de Electrónica";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 400;
            SizeToContent = SizeToContent.Height;

            var lblTitulo = new TextBlock { Text = "¿Con qué rol quieres entrar?", FontWeight = FontWeight.Bold, FontSize = 14 };

            _radioUsuario.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) ActualizarEstadoPassword(); };
            _radioAdmin.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) ActualizarEstadoPassword(); };

            _txtPassword.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    Entrar();
                }
            };

            var btnEntrar = new Button { Content = "Entrar", Width = 100, Height = 32, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Left };
            btnEntrar.Click += (_, _) => Entrar();

            var raiz = new StackPanel
            {
                Margin = new Avalonia.Thickness(25),
                Spacing = 12,
            };
            raiz.Children.Add(lblTitulo);
            raiz.Children.Add(_radioUsuario);
            raiz.Children.Add(_radioAdmin);
            raiz.Children.Add(_lblPassword);
            raiz.Children.Add(_txtPassword);
            raiz.Children.Add(_lblHint);
            raiz.Children.Add(_lblError);
            raiz.Children.Add(btnEntrar);

            Content = raiz;

            ActualizarEstadoPassword();
            Opened += (_, _) => _txtPassword.Focus();
        }

        private void ActualizarEstadoPassword()
        {
            var esAdmin = _radioAdmin.IsChecked == true;
            _txtPassword.IsEnabled = esAdmin;
            _lblPassword.IsEnabled = esAdmin;

            _lblHint.Text = esAdmin && _auth.UsaPasswordPorDefecto()
                ? "Contraseña por defecto: admin123 (cámbiala desde el menú Administración)."
                : "";

            if (esAdmin) _txtPassword.Focus();
        }

        private void Entrar()
        {
            _lblError.Text = "";

            if (_radioAdmin.IsChecked == true)
            {
                if (!_auth.ValidarPasswordAdmin(_txtPassword.Text ?? ""))
                {
                    _lblError.Text = "Contraseña incorrecta.";
                    _txtPassword.Clear();
                    _txtPassword.Focus();
                    return;
                }
                RolSeleccionado = Rol.Administrador;
            }
            else
            {
                RolSeleccionado = Rol.Usuario;
            }

            Aceptado = true;
            Close();
        }
    }
}
