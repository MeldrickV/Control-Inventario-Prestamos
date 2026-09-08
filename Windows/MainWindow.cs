using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using LabInventario.Dialogs;
using LabInventario.Services;
using LabInventario.Theme;
using LabInventario.Views;
using SukiUI.Controls;
using SukiUI.Enums;

namespace LabInventario.Windows
{
    /// <summary>
    /// Ventana principal de la aplicación. Ensambla todas las pestañas
    /// dentro de un TabControl. Es la única clase que conoce a todas las
    /// vistas; cada pestaña, a su vez, solo conoce sus propios repositorios
    /// y servicios.
    ///
    /// Las pestañas de Inventario, Alumnos, Importar datos y Exportar datos
    /// —que pueden modificar o sacar la información delicada del
    /// laboratorio— solo se agregan cuando <see cref="SesionActual"/> indica
    /// que se entró como Administrador. El rol Usuario solo ve Operación e
    /// Historial.
    ///
    /// Nota de diseño: hereda de <see cref="SukiWindow"/> (barra de título
    /// moderna, fondo con degradado sutil) en vez de <see cref="Window"/>.
    /// El menú, antes armado a mano dentro de un <see cref="DockPanel"/>,
    /// ahora se entrega vía la propiedad nativa <c>MenuItems</c> de
    /// SukiWindow; toda la lógica de cada opción de menú es la misma.
    /// </summary>
    public class MainWindow : SukiWindow
    {
        /// <summary>
        /// Se pone en true cuando el usuario elige "Cerrar sesión" desde el
        /// menú, para que <c>App.cs</c> sepa que debe volver a mostrar la
        /// pantalla de inicio de sesión en vez de terminar la aplicación.
        /// </summary>
        public bool SolicitoCerrarSesion { get; private set; }

        public MainWindow()
        {
            var esAdmin = SesionActual.EsAdministrador;

            Title = $"Gestión de Salidas y Entradas - Laboratorio de Electrónica  [{(esAdmin ? "Administrador" : "Usuario")}]";
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LabInventario/Assets/icon.png")));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 1050;
            Height = 700;
            MinWidth = 880;
            MinHeight = 560;

            BackgroundStyle = SukiBackgroundStyle.GradientSoft;
            LogoContent = ConstruirInsigniaUas();

            IsMenuVisible = true;
            MenuItems = ConstruirMenu(esAdmin);

            var tabs = new TabControl();

            tabs.Items.Add(new TabItem { Header = "Operación", Content = new OperacionView() });
            tabs.Items.Add(new TabItem { Header = "Historial", Content = new PrestamosView() });

            if (esAdmin)
            {
                tabs.Items.Add(new TabItem { Header = "Inventario", Content = new InventarioView() });
                tabs.Items.Add(new TabItem { Header = "Alumnos", Content = new AlumnosView() });
                tabs.Items.Add(new TabItem { Header = "Importar datos", Content = new ImportarView() });
                tabs.Items.Add(new TabItem { Header = "Exportar datos", Content = new ExportarView() });
            }

            tabs.SelectionChanged += (_, _) =>
            {
                if (tabs.SelectedItem is TabItem { Content: AlumnosView alumnosView })
                    alumnosView.Actualizar();
                else if (tabs.SelectedItem is TabItem { Content: InventarioView inventarioView })
                    inventarioView.Actualizar();
                else if (tabs.SelectedItem is TabItem { Content: PrestamosView prestamosView })
                    prestamosView.Actualizar();
            };

            // Listón institucional: una franja delgada azul→dorado justo
            // arriba de las pestañas. Es el único "bloque" de color
            // institucional grande de toda la ventana (el resto de la marca
            // vive en la insignia de la barra de título y en el color con
            // que SukiUI ya resalta la pestaña activa), a propósito: se
            // busca que la app se sienta "de la UAS" sin que el color
            // compita con los botones ni con los datos.
            var liston = new Border
            {
                Height = 4,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(TemaUas.AzulUas, 0),
                        new GradientStop(TemaUas.DoradoUas, 1),
                    },
                },
            };
            DockPanel.SetDock(liston, Dock.Top);

            var raizVentana = new DockPanel();
            raizVentana.Children.Add(liston);
            raizVentana.Children.Add(tabs);

            Content = raizVentana;
        }

        /// <summary>
        /// Insignia circular "UAS" (dorado sobre azul) para la esquina de
        /// la barra de título. No es el escudo oficial de la universidad
        /// (evitamos reproducirlo por derechos de autor) — es solo un
        /// monograma propio, discreto, para que se note que es un
        /// programa institucional sin imitar el logo real.
        /// </summary>
        private static Border ConstruirInsigniaUas() => new()
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
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

        private AvaloniaList<MenuItem> ConstruirMenu(bool esAdmin)
        {
            var items = new AvaloniaList<MenuItem>();

            var itemCerrarSesion = new MenuItem { Header = "Cerrar sesión" };
            itemCerrarSesion.Click += (_, _) =>
            {
                SolicitoCerrarSesion = true;
                Close();
            };

            var menuSesion = new MenuItem { Header = "Sesión" };
            menuSesion.Items.Add(itemCerrarSesion);
            items.Add(menuSesion);

            if (esAdmin)
            {
                var itemPassword = new MenuItem { Header = "Cambiar contraseña de administrador..." };
                itemPassword.Click += async (_, _) =>
                {
                    var dialogo = new CambiarPasswordDialog();
                    await dialogo.ShowDialog(this);
                };

                var itemConfigEscaneo = new MenuItem { Header = "Configuración de escaneo..." };
                itemConfigEscaneo.Click += async (_, _) =>
                {
                    var dialogo = new ConfiguracionEscaneoDialog();
                    await dialogo.ShowDialog(this);
                };

                var menuAdmin = new MenuItem { Header = "Administración" };
                menuAdmin.Items.Add(itemPassword);
                menuAdmin.Items.Add(itemConfigEscaneo);
                items.Add(menuAdmin);
            }

            return items;
        }
    }
}
