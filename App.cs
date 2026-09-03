using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LabInventario.Helpers;
using LabInventario.Services;
using LabInventario.Windows;

namespace LabInventario
{
    /// <summary>
    /// Clase de aplicación. Los estilos (FluentTheme + tema del DataGrid)
    /// se declaran en App.axaml, que es el único archivo .axaml del
    /// proyecto: Avalonia necesita al menos uno para activar su mecanismo
    /// de carga de recursos "avares://", incluso para recursos que vienen
    /// de paquetes de terceros como Avalonia.Controls.DataGrid. El resto
    /// de la aplicación (ventanas, vistas, diálogos) sigue construido
    /// 100% en C#, igual que antes.
    ///
    /// Reproduce el ciclo que antes vivía en Program.cs: se muestra
    /// LoginWindow, y si se entra correctamente se abre MainWindow. Si
    /// desde el menú "Sesión" se elige "Cerrar sesión", MainWindow se
    /// cierra y el ciclo vuelve a mostrar el login; si se cierra de
    /// cualquier otra forma (o se cancela el login), la aplicación termina.
    /// </summary>
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Red de seguridad: si una excepción escapa de cualquier lugar
            // que no esté ya envuelto en Errores.Ejecutar (por ejemplo, un
            // manejador de menú), al menos queda registrada en
            // "errores.log" junto al ejecutable en vez de perderse en
            // silencio.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) Errores.RegistrarEnArchivo(ex);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Errores.RegistrarEnArchivo(e.Exception);
                e.SetObserved();
            };

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Nosotros controlamos manualmente cuándo termina la app
                // (al cerrar el login o al cerrar MainWindow sin pedir
                // "cerrar sesión"), en vez de que Avalonia la cierre sola
                // al cerrarse la primera ventana.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = EjecutarCicloDeSesionAsync(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async Task EjecutarCicloDeSesionAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            while (true)
            {
                var login = new LoginWindow();
                desktop.MainWindow = login;
                login.Show();
                await EsperarCierre(login);

                if (!login.Aceptado)
                {
                    desktop.Shutdown();
                    return;
                }

                SesionActual.Rol = login.RolSeleccionado;

                // Limpieza automática de préstamos ya devueltos con más de
                // una semana de antigüedad (ver PrestamoRepository.EliminarDevueltosAntiguos).
                new Data.PrestamoRepository().EliminarDevueltosAntiguos();

                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                await EsperarCierre(mainWindow);

                if (!mainWindow.SolicitoCerrarSesion)
                {
                    desktop.Shutdown();
                    return;
                }
                // Si se pidió cerrar sesión, el ciclo vuelve a mostrar el login.
            }
        }

        private static Task EsperarCierre(Window ventana)
        {
            var tcs = new TaskCompletionSource();
            ventana.Closed += (_, _) => tcs.TrySetResult();
            return tcs.Task;
        }
    }
}
