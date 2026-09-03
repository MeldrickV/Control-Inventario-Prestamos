using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.IO;

namespace LabInventario.Helpers
{
    /// <summary>
    /// Avalonia (a diferencia de WinForms) no trae un <c>MessageBox</c> ni un
    /// <c>OpenFileDialog</c> listos para usar de forma síncrona: hay que
    /// mostrar una ventana propia y, para archivos, usar el
    /// <c>IStorageProvider</c> asíncrono. Esta clase reúne esos reemplazos
    /// para que el resto de la app se lea casi igual que la versión WinForms.
    /// </summary>
    public static class Dialogos
    {
        public enum Icono { Informacion, Advertencia, Error, Pregunta }

        public static Task MostrarInfo(Window propietaria, string mensaje, string titulo = "Información") =>
            MostrarOk(propietaria, mensaje, titulo, Icono.Informacion);

        public static Task MostrarAdvertencia(Window propietaria, string mensaje, string titulo = "Atención") =>
            MostrarOk(propietaria, mensaje, titulo, Icono.Advertencia);

        public static Task MostrarError(Window propietaria, string mensaje, string titulo = "Error") =>
            MostrarOk(propietaria, mensaje, titulo, Icono.Error);

        private static async Task MostrarOk(Window propietaria, string mensaje, string titulo, Icono icono)
        {
            var ventana = ConstruirVentana(mensaje, titulo, icono);
            var btnOk = new Button { Content = "Aceptar", Width = 90, IsDefault = true };
            btnOk.Click += (_, _) => ventana.Close(true);
            ((StackPanel)ventana.Content!).Children.Add(EnvolverBotones(btnOk));
            await ventana.ShowDialog<bool>(propietaria);
        }

        /// <summary>Confirmación Sí/No. Devuelve true si el usuario eligió "Sí".</summary>
        public static async Task<bool> Confirmar(Window propietaria, string mensaje, string titulo = "Confirmar")
        {
            var ventana = ConstruirVentana(mensaje, titulo, Icono.Pregunta);
            var btnSi = new Button { Content = "Sí", Width = 90, IsDefault = true };
            var btnNo = new Button { Content = "No", Width = 90 };
            btnSi.Click += (_, _) => ventana.Close(true);
            btnNo.Click += (_, _) => ventana.Close(false);
            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            panelBotones.Children.Add(btnSi);
            panelBotones.Children.Add(btnNo);
            ((StackPanel)ventana.Content!).Children.Add(panelBotones);
            return await ventana.ShowDialog<bool>(propietaria);
        }

        private static Window ConstruirVentana(string mensaje, string titulo, Icono icono)
        {
            var color = icono switch
            {
                Icono.Error => Color.FromRgb(176, 0, 32),
                Icono.Advertencia => Color.FromRgb(180, 120, 0),
                Icono.Pregunta => Color.FromRgb(26, 78, 122),
                _ => Color.FromRgb(30, 30, 30),
            };

            var texto = new TextBlock
            {
                Text = mensaje,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 380,
                Foreground = new SolidColorBrush(color),
                Margin = new Avalonia.Thickness(24, 20, 24, 10),
            };

            var raiz = new StackPanel { Spacing = 15 };
            raiz.Children.Add(texto);

            return new Window
            {
                Title = titulo,
                CanResize = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = raiz,
            };
        }

        private static StackPanel EnvolverBotones(Button boton)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 0, 15) };
            panel.Children.Add(boton);
            return panel;
        }

        /// <summary>Selector de archivos (reemplazo async de OpenFileDialog). Devuelve null si se cancela.</summary>
        public static async Task<string?> SeleccionarArchivo(Window propietaria, string titulo, params FilePickerFileType[] tipos)
        {
            var archivos = await propietaria.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = titulo,
                AllowMultiple = false,
                FileTypeFilter = tipos,
            });
            var archivo = archivos.Count > 0 ? archivos[0] : null;
            return archivo?.TryGetLocalPath();
        }

        /// <summary>Selector de "guardar como" (reemplazo async de SaveFileDialog). Devuelve null si se cancela.</summary>
        public static async Task<string?> GuardarArchivo(Window propietaria, string titulo, string nombreSugerido, params FilePickerFileType[] tipos)
        {
            var archivo = await propietaria.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = titulo,
                SuggestedFileName = nombreSugerido,
                FileTypeChoices = tipos,
            });
            return archivo?.TryGetLocalPath();
        }
    }
    /// <summary>
    /// Envuelve el manejador de un botón/evento en un try/catch y muestra
    /// cualquier excepción en un diálogo, en vez de dejar que se pierda en
    /// silencio (que es lo que pasa por defecto con "async void" si nadie
    /// la atrapa: la operación se corta a medias, sin mensaje ni traza,
    /// y desde la UI parece que "no hizo nada").
    /// </summary>
    public static class Errores
    {
        public static async void Ejecutar(Window? propietaria, Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (Exception ex)
            {
                RegistrarEnArchivo(ex);
                if (propietaria is not null)
                    await Dialogos.MostrarError(propietaria, ex.Message, "Ocurrió un error");
            }
        }
    
        public static void RegistrarEnArchivo(Exception ex)
        {
            try
            {
                var ruta = Path.Combine(AppContext.BaseDirectory, "errores.log");
                File.AppendAllText(ruta, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch
            {
                // Si ni siquiera se puede escribir el log, no hay nada más que hacer aquí.
            }
        }
    }
    /// <summary>
    /// Avalonia no incluye un control "GroupBox" propio (como sí tenía
    /// WinForms). Este helper arma un recuadro con encabezado en negritas
    /// que cumple el mismo papel visual.
    /// </summary>
    public static class Cajas
    {
        public static Border GroupBox(string titulo, Control contenido, double? width = null)
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeight.Bold });
            panel.Children.Add(contenido);

            var borde = new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(14),
                Child = panel,
            };
            if (width.HasValue) borde.Width = width.Value;
            return borde;
        }
    }
}
