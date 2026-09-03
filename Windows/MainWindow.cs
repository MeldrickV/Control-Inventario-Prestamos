using Avalonia.Controls;
using Avalonia.Layout;
using LabInventario.Dialogs;
using LabInventario.Services;
using LabInventario.Views;

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
    /// laboratorio— solo se agregan cuando <see cref="SesionActual"/> indica que se entró como
    /// Administrador. El rol Usuario solo ve Operación e Historial.
    /// </summary>
    public class MainWindow : Window
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
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = 1050;
            Height = 700;
            MinWidth = 880;
            MinHeight = 560;

            var menu = ConstruirMenu(esAdmin);

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

            var raiz = new DockPanel();
            DockPanel.SetDock(menu, Dock.Top);
            raiz.Children.Add(menu);
            raiz.Children.Add(tabs); // último hijo: llena el resto del espacio

            Content = raiz;
        }

        private Menu ConstruirMenu(bool esAdmin)
        {
            var menu = new Menu();

            var itemCerrarSesion = new MenuItem { Header = "Cerrar sesión" };
            itemCerrarSesion.Click += (_, _) =>
            {
                SolicitoCerrarSesion = true;
                Close();
            };

            var menuSesion = new MenuItem { Header = "Sesión" };
            menuSesion.Items.Add(itemCerrarSesion);
            menu.Items.Add(menuSesion);

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
                menu.Items.Add(menuAdmin);
            }

            return menu;
        }
    }
}
