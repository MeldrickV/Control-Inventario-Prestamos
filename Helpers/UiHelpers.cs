using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SukiUI.Controls;
using SukiUI.MessageBox;
using System.IO;

namespace LabInventario.Helpers
{
    /// <summary>
    /// Reemplazos de <c>MessageBox</c> / <c>OpenFileDialog</c> / <c>SaveFileDialog</c>
    /// (que Avalonia no trae de forma nativa), apoyados en el sistema de
    /// diálogos de SukiUI (<see cref="SukiMessageBox"/>) para que luzcan
    /// acordes al resto de la aplicación (mismo tema, iconos, animaciones).
    ///
    /// La firma pública de esta clase se mantiene igual a la versión
    /// original a propósito: todas las vistas y diálogos que ya llaman a
    /// <c>Dialogos.MostrarInfo/MostrarError/MostrarAdvertencia/Confirmar/
    /// SeleccionarArchivo/GuardarArchivo</c> siguen funcionando sin cambiar
    /// una sola línea.
    /// </summary>
    public static class Dialogos
    {
        public static Task MostrarInfo(Window propietaria, string mensaje, string titulo = "Información") =>
            SukiMessageBox.ShowDialogResult(propietaria, mensaje, SukiMessageBoxButtons.OK, titulo,
                icon: SukiMessageBoxIcons.Information);

        public static Task MostrarAdvertencia(Window propietaria, string mensaje, string titulo = "Atención") =>
            SukiMessageBox.ShowDialogResult(propietaria, mensaje, SukiMessageBoxButtons.OK, titulo,
                icon: SukiMessageBoxIcons.Warning);

        public static Task MostrarError(Window propietaria, string mensaje, string titulo = "Error") =>
            SukiMessageBox.ShowDialogResult(propietaria, mensaje, SukiMessageBoxButtons.OK, titulo,
                icon: SukiMessageBoxIcons.Error);

        /// <summary>Confirmación Sí/No. Devuelve true si el usuario eligió "Sí".</summary>
        public static async Task<bool> Confirmar(Window propietaria, string mensaje, string titulo = "Confirmar")
        {
            var resultado = await SukiMessageBox.ShowDialogResult(propietaria, mensaje, SukiMessageBoxButtons.YesNo, titulo,
                icon: SukiMessageBoxIcons.Question);
            return resultado == SukiMessageBoxResult.Yes;
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
    /// Avalonia no incluye un control "GroupBox" propio, pero SukiUI sí
    /// (<see cref="SukiUI.Controls.GroupBox"/>). Este helper envuelve ese
    /// control dentro de una <see cref="GlassCard"/> para obtener el
    /// aspecto de "tarjeta" del resto de la librería, manteniendo la misma
    /// firma que antes (<c>Cajas.GroupBox(titulo, contenido, ancho)</c>)
    /// para no tener que tocar las vistas que ya la usan.
    ///
    /// El "ancho" que reciben las vistas se aplica como <c>MinWidth</c> (no
    /// como <c>Width</c> fijo): así la tarjeta nunca queda más angosta de lo
    /// pensado, pero si el contenedor que la aloja le da más espacio (por
    /// ejemplo, una columna de <see cref="Grid"/> con ancho "*"), la
    /// tarjeta lo aprovecha en vez de quedarse pegada a un tamaño fijo.
    /// </summary>
    public static class Cajas
    {
        public static Control GroupBox(string titulo, Control contenido, double? width = null)
        {
            var caja = new SukiUI.Controls.GroupBox
            {
                Header = titulo,
                Content = contenido,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };

            var tarjeta = new GlassCard
            {
                Content = caja,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            if (width.HasValue) tarjeta.MinWidth = width.Value;
            return tarjeta;
        }
    }
}
