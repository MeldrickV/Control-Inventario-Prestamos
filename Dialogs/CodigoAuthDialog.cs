using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Respaldo cuando el navegador no logra regresar solo a la app:
    /// muestra la URL de autorización para abrirla a mano y recibe pegada
    /// la dirección a la que Google redirigió (contiene el código).
    /// </summary>
    public class CodigoAuthDialog : SukiWindow
    {
        /// <summary>Código de autorización extraído, o null si se canceló.</summary>
        public string? Codigo { get; private set; }

        private readonly TextBox _txtPegado = new() { Width = 460 };

        public CodigoAuthDialog(string urlAutorizacion)
        {
            Title = "Autorización manual de Google";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var btnAceptar = new Button { Content = "Aceptar", Classes = { "Flat" }, MinWidth = 100, IsDefault = true };
            btnAceptar.Click += async (_, _) =>
            {
                var codigo = ExtraerCodigo(_txtPegado.Text ?? "");
                if (string.IsNullOrWhiteSpace(codigo))
                {
                    await Dialogos.MostrarAdvertencia(this,
                        "No se encontró el código en lo pegado. Pega la dirección completa de la barra del navegador.",
                        "Sin código");
                    return;
                }
                Codigo = codigo;
                Close();
            };

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 100, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnAceptar);
            panelBotones.Children.Add(btnCancelar);

            var panel = new StackPanel { Spacing = 8, Width = 480 };
            panel.Children.Add(new TextBlock
            {
                Text = "El navegador no regresó solo. Abre esta dirección, inicia sesión y autoriza; " +
                       "luego pega aquí la dirección completa a la que te mandó Google:",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
                Width = 480,
            });
            panel.Children.Add(new TextBox { Text = urlAutorizacion, IsReadOnly = true, Width = 460 });
            panel.Children.Add(new TextBlock { Text = "Dirección con el código:", Margin = new Avalonia.Thickness(0, 6, 0, 0) });
            panel.Children.Add(_txtPegado);
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }

        /// <summary>
        /// Extrae el parámetro <c>code</c> de una URL redirigida por Google;
        /// si no parece URL, lo trata como el código ya copiado.
        /// </summary>
        public static string? ExtraerCodigo(string texto)
        {
            var limpio = texto.Trim();
            if (limpio.Length == 0) return null;
            var indice = limpio.IndexOf("code=", StringComparison.Ordinal);
            if (indice >= 0)
            {
                var resto = limpio[(indice + 5)..];
                var fin = resto.IndexOf('&');
                var codigo = Uri.UnescapeDataString(fin < 0 ? resto : resto[..fin]);
                return codigo.Length > 0 ? codigo : null;
            }
            // Parece URL pero sin código (p. ej. error=access_denied): nada que extraer.
            if (limpio.Contains("://") || limpio.Contains('?')) return null;
            return limpio;
        }
    }
}
