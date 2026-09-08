using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using LabInventario.Helpers;
using LabInventario.Models;
using LabInventario.Services;
using LabInventario.Theme;
using SukiUI.Controls;
using SukiUI.Enums;

namespace LabInventario.Windows
{
    /// <summary>
    /// Pantalla inicial: el operador elige con qué rol entra.
    /// - "Usuario" no pide contraseña (así de rápido puede alguien haciendo
    ///   prácticas registrar una salida o entrada).
    /// - "Administrador" exige la contraseña guardada (hash) en la BD,
    ///   porque desde ese rol se puede tocar la información delicada de
    ///   alumnos y materiales.
    ///
    /// Nota de diseño: hereda de <see cref="SukiWindow"/> (en vez de
    /// <see cref="Window"/>) únicamente por estética — barra de título
    /// moderna y fondo con degradado sutil en los colores institucionales
    /// de la UAS. El flujo, la validación y las propiedades públicas
    /// (<see cref="Aceptado"/>, <see cref="RolSeleccionado"/>) que consume
    /// App.cs quedan exactamente iguales.
    /// </summary>
    public class LoginWindow : SukiWindow
    {
        private readonly AuthService _auth = new();

        private readonly RadioButton _radioUsuario = new() { Content = "Usuario (registrar salidas/entradas)", GroupName = "rol", IsChecked = true };
        private readonly RadioButton _radioAdmin = new() { Content = "Administrador (gestionar alumnos/materiales)", GroupName = "rol" };
        private readonly TextBox _txtPassword = new() { Width = 260, PasswordChar = '*', IsEnabled = false };
        private readonly TextBlock _lblPassword = new() { Text = "Contraseña de administrador:", IsEnabled = false };
        private readonly TextBlock _lblHint = new() { Classes = { "Caption" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        private readonly TextBlock _lblError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };

        public bool Aceptado { get; private set; }
        public Rol RolSeleccionado { get; private set; }

        public LoginWindow()
        {
            Title = "Iniciar sesión — Laboratorio de Electrónica";
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LabInventario/Assets/icon.png")));
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 420;
            SizeToContent = SizeToContent.Height;

            BackgroundStyle = SukiBackgroundStyle.GradientSoft;
            LogoContent = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new Avalonia.CornerRadius(13),
                Background = new SolidColorBrush(TemaUas.DoradoUas),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "UAS",
                    FontWeight = FontWeight.Black,
                    FontSize = 8,
                    Foreground = new SolidColorBrush(TemaUas.AzulUas),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var lblTitulo = new TextBlock { Text = "¿Con qué rol quieres entrar?", Classes = { "h4" } };
            var lblInstitucion = new TextBlock
            {
                Text = "Universidad Autónoma de Sinaloa — Laboratorio de Electrónica",
                Classes = { "Caption" },
                Margin = new Avalonia.Thickness(0, -6, 0, 6),
            };

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

            var btnEntrar = new Button
            {
                Content = "Entrar",
                Classes = { "Outlined" },
                MinWidth = 110,
                MinHeight = 34,
                IsDefault = true,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            btnEntrar.Click += (_, _) => Entrar();

            var panelFormulario = new StackPanel { Spacing = 12 };
            panelFormulario.Children.Add(lblTitulo);
            panelFormulario.Children.Add(lblInstitucion);
            panelFormulario.Children.Add(_radioUsuario);
            panelFormulario.Children.Add(_radioAdmin);
            panelFormulario.Children.Add(_lblPassword);
            panelFormulario.Children.Add(_txtPassword);
            panelFormulario.Children.Add(_lblHint);
            panelFormulario.Children.Add(_lblError);
            panelFormulario.Children.Add(btnEntrar);

            var tarjeta = new GlassCard
            {
                Margin = new Avalonia.Thickness(25),
                Content = panelFormulario,
            };

            Content = tarjeta;

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
