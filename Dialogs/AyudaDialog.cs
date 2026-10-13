using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Helpers;
using LabInventario.Services;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Manual integrado (menú Ayuda): lista de secciones a la izquierda y
    /// pasos detallados a la derecha. El contenido viene de
    /// <see cref="AyudaContenido"/> ya filtrado por rol: un Usuario nunca ve
    /// las secciones de administrador (importar, exportar, nube).
    ///
    /// Es la excepción al tamaño de los diálogos: por la cantidad de texto
    /// usa ventana grande con redimensionado y doble scroll (general +
    /// script), en vez de SizeToContent.
    /// </summary>
    public class AyudaDialog : SukiWindow
    {
        private readonly List<SeccionAyuda> _secciones;
        private readonly ListBox _lstSecciones = new() { Width = 250 };
        private readonly StackPanel _panelContenido = new() { Spacing = 8 };
        private readonly StackPanel _panelScript = new() { Spacing = 8, IsVisible = false };
        private readonly TextBox _txtScript = new()
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,monospace"),
            FontSize = 11,
            Height = 220,
        };
        private readonly TextBlock _lblEstadoCopiado = new() { Classes = { "Caption" } };

        /// <param name="seccionInicial">Id de <see cref="AyudaContenido"/> donde abrir (o null para la primera).</param>
        /// <param name="esAdministrador">Rol actual; por defecto el de la sesión.</param>
        public AyudaDialog(string? seccionInicial = null, bool? esAdministrador = null)
        {
            _secciones = AyudaContenido.Para(esAdministrador ?? SesionActual.EsAdministrador).ToList();

            Title = "Ayuda — Manual de uso";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 980;
            Height = 640;
            MinWidth = 800;
            MinHeight = 480;
            CanResize = true;
            CanMinimize = true;
            CanFullScreen = false;

            _lstSecciones.ItemsSource = _secciones.Select(s => s.Titulo).ToList();
            _lstSecciones.SelectionChanged += (_, _) => MostrarSeleccion();
            _lstSecciones.SelectedIndex = Math.Max(0, _secciones.FindIndex(s => s.Id == seccionInicial));

            var btnCopiar = new Button { Content = "Copiar script", Classes = { "Flat" }, MinWidth = 140 };
            btnCopiar.Click += (_, _) => Errores.Ejecutar(this, CopiarScriptAsync);

            _panelScript.Children.Add(new TextBlock
            {
                Text = "Script completo (apps-script/Codigo.gs). Cópialo y pégalo en Extensiones → Apps Script:",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
            });
            _panelScript.Children.Add(_txtScript);
            var filaCopiar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            filaCopiar.Children.Add(btnCopiar);
            filaCopiar.Children.Add(_lblEstadoCopiado);
            _panelScript.Children.Add(filaCopiar);

            var scrollContenido = new ScrollViewer { Content = _panelContenido };
            // DockPanel (no StackPanel): el scroll rellena el alto disponible
            // y el panel del script queda fijo abajo. En un StackPanel el
            // ScrollViewer tendría alto infinito y nunca mostraría scroll.
            var columnaDerecha = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_panelScript, Dock.Bottom);
            columnaDerecha.Children.Add(_panelScript);
            columnaDerecha.Children.Add(scrollContenido);

            var rejilla = new Grid { ColumnSpacing = 15 };
            rejilla.ColumnDefinitions.Add(new ColumnDefinition(250, GridUnitType.Pixel));
            rejilla.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(_lstSecciones, 0);
            Grid.SetColumn(columnaDerecha, 1);
            rejilla.Children.Add(_lstSecciones);
            rejilla.Children.Add(columnaDerecha);

            var btnCerrar = new Button { Content = "Cerrar", Classes = { "Outlined" }, MinWidth = 110, IsCancel = true };
            btnCerrar.Click += (_, _) => Close();
            var panelBotones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 10, 0, 0),
            };
            panelBotones.Children.Add(btnCerrar);

            var raiz = new DockPanel { Margin = new Avalonia.Thickness(20) };
            DockPanel.SetDock(panelBotones, Dock.Bottom);
            raiz.Children.Add(panelBotones);
            raiz.Children.Add(rejilla);

            Content = new GlassCard { Margin = new Avalonia.Thickness(10), Content = raiz };

            MostrarSeleccion();
        }

        private void MostrarSeleccion()
        {
            var indice = _lstSecciones.SelectedIndex;
            if (indice < 0 || indice >= _secciones.Count) return;
            var seccion = _secciones[indice];

            _panelContenido.Children.Clear();
            _panelContenido.Children.Add(new TextBlock { Text = seccion.Titulo, Classes = { "h4" }, TextWrapping = TextWrapping.Wrap });
            _panelContenido.Children.Add(new TextBlock
            {
                Text = seccion.Resumen + (seccion.SoloAdmin ? " (Solo administrador.)" : ""),
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
            });
            for (var i = 0; i < seccion.Pasos.Length; i++)
            {
                _panelContenido.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. {seccion.Pasos[i]}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 4, 0, 0),
                });
            }

            var esGoogle = seccion.Id == AyudaContenido.IdGoogle;
            _panelScript.IsVisible = esGoogle;
            _lblEstadoCopiado.Text = "";
            if (esGoogle && string.IsNullOrEmpty(_txtScript.Text))
            {
                var script = AyudaContenido.LeerScriptSincronizacion();
                _txtScript.Text = string.IsNullOrEmpty(script)
                    ? "No se encontró el script embebido en esta instalación. Cópialo desde el repositorio: apps-script/Codigo.gs"
                    : script;
            }
        }

        private async Task CopiarScriptAsync()
        {
            var texto = _txtScript.Text ?? "";
            if (string.IsNullOrWhiteSpace(texto)) return;
            var portapapeles = TopLevel.GetTopLevel(this)?.Clipboard;
            if (portapapeles is null) return;
            await portapapeles.SetTextAsync(texto);
            _lblEstadoCopiado.Text = "Copiado al portapapeles.";
        }
    }
}
