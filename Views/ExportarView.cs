using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LabInventario.Helpers;
using LabInventario.Services;

namespace LabInventario.Views
{
    /// <summary>
    /// Pestaña de exportación. Permite sacar alumnos, inventario e
    /// historial en CSV o Excel, y además hacer un respaldo de la base de
    /// datos completa (cifrado, o en un único Excel con una hoja por tabla).
    /// Solo visible para Administrador (se agrega en MainWindow igual que
    /// Inventario/Alumnos/Importar).
    /// </summary>
    public class ExportarView : UserControl
    {
        private readonly ExportService _servicio = new();
        private readonly TextBox _txtLog = new()
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,monospace"),
        };

        public ExportarView()
        {
            var lblTitulo = new TextBlock
            {
                Text = "Exportación de datos",
                FontWeight = FontWeight.Bold,
                FontSize = 14,
            };

            var raiz = new DockPanel { Margin = new Avalonia.Thickness(15) };
            DockPanel.SetDock(lblTitulo, Dock.Top);
            raiz.Children.Add(lblTitulo);
            raiz.Children.Add(new Border { Height = 10 });

            var seccionDatos = new StackPanel { Spacing = 10 };
            seccionDatos.Children.Add(FilaExportacion("Alumnos",
                () => ExportarCsv("Alumnos", "alumnos.csv", _servicio.ExportarAlumnosCsv),
                () => ExportarXlsx("Alumnos", "alumnos.xlsx", _servicio.ExportarAlumnosXlsx)));
            seccionDatos.Children.Add(FilaExportacion("Inventario",
                () => ExportarCsv("Inventario", "inventario.csv", _servicio.ExportarInventarioCsv),
                () => ExportarXlsx("Inventario", "inventario.xlsx", _servicio.ExportarInventarioXlsx)));
            seccionDatos.Children.Add(FilaExportacion("Historial de préstamos",
                () => ExportarCsv("Historial", "historial.csv", _servicio.ExportarHistorialCsv),
                () => ExportarXlsx("Historial", "historial.xlsx", _servicio.ExportarHistorialXlsx)));

            var cajaDatos = Cajas.GroupBox("Exportar por tipo de dato", seccionDatos);

            var seccionRespaldo = new StackPanel { Spacing = 10 };

            var btnRespaldoCifrado = new Button { Content = "Respaldo cifrado (.db)...", Width = 260, Height = 32 };
            btnRespaldoCifrado.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), async () =>
            {
                var propietaria = VentanaPropietaria();
                if (propietaria is null) return;
                var ruta = await Dialogos.GuardarArchivo(propietaria, "Guardar respaldo cifrado", "laboratorio-respaldo.db",
                    new FilePickerFileType("Base de datos SQLite") { Patterns = new[] { "*.db" } });
                if (ruta is null) return;
                _servicio.ExportarBaseDatosCifrada(ruta);
                Log($"Respaldo cifrado generado en: {ruta}");
                await Dialogos.MostrarInfo(propietaria, "Respaldo generado. Sigue cifrado: solo se puede volver a abrir con esta app en esta misma máquina.", "Respaldo completado");
            });

            var btnRespaldoXlsx = new Button { Content = "Volcado completo (.xlsx, sin cifrar)...", Width = 260, Height = 32 };
            btnRespaldoXlsx.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), async () =>
            {
                var propietaria = VentanaPropietaria();
                if (propietaria is null) return;
                var ruta = await Dialogos.GuardarArchivo(propietaria, "Guardar volcado completo", "laboratorio-completo.xlsx",
                    new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } });
                if (ruta is null) return;
                var confirmado = await Dialogos.Confirmar(propietaria,
                    "Este archivo queda SIN cifrar (cualquiera que lo abra ve los datos). Úsalo solo si vas a guardarlo/compartirlo con cuidado.\n\n¿Continuar?",
                    "Advertencia: archivo sin cifrar");
                if (!confirmado) return;
                _servicio.ExportarBaseDatosXlsx(ruta);
                Log($"Volcado completo (sin cifrar) generado en: {ruta}");
                await Dialogos.MostrarInfo(propietaria, "Volcado generado.", "Exportación completada");
            });

            seccionRespaldo.Children.Add(btnRespaldoCifrado);
            seccionRespaldo.Children.Add(btnRespaldoXlsx);
            var cajaRespaldo = Cajas.GroupBox("Base de datos completa", seccionRespaldo);

            var panelCajas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };
            panelCajas.Children.Add(cajaDatos);
            panelCajas.Children.Add(cajaRespaldo);
            DockPanel.SetDock(panelCajas, Dock.Top);
            raiz.Children.Add(panelCajas);
            raiz.Children.Add(new Border { Height = 10 });

            var lblLog = new TextBlock { Text = "Registro de exportaciones:" };
            DockPanel.SetDock(lblLog, Dock.Top);
            raiz.Children.Add(lblLog);

            raiz.Children.Add(new ScrollViewer { Content = _txtLog }); // último hijo: llena el resto

            Content = raiz;
        }

        private Control FilaExportacion(string etiqueta, Func<Task> exportarCsv, Func<Task> exportarXlsx)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = etiqueta, Width = 150, VerticalAlignment = VerticalAlignment.Center });

            var btnCsv = new Button { Content = "CSV...", Width = 90 };
            btnCsv.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), exportarCsv);

            var btnXlsx = new Button { Content = "Excel...", Width = 90 };
            btnXlsx.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), exportarXlsx);

            panel.Children.Add(btnCsv);
            panel.Children.Add(btnXlsx);
            return panel;
        }

        private async Task ExportarCsv(string etiqueta, string nombreSugerido, Action<string> accionExportar)
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;
            var ruta = await Dialogos.GuardarArchivo(propietaria, $"Exportar {etiqueta} a CSV", nombreSugerido,
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } });
            if (ruta is null) return;
            accionExportar(ruta);
            Log($"{etiqueta} exportado a CSV: {ruta}");
            await Dialogos.MostrarInfo(propietaria, "Exportación completada.", "Listo");
        }

        private async Task ExportarXlsx(string etiqueta, string nombreSugerido, Action<string> accionExportar)
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;
            var ruta = await Dialogos.GuardarArchivo(propietaria, $"Exportar {etiqueta} a Excel", nombreSugerido,
                new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } });
            if (ruta is null) return;
            accionExportar(ruta);
            Log($"{etiqueta} exportado a Excel: {ruta}");
            await Dialogos.MostrarInfo(propietaria, "Exportación completada.", "Listo");
        }

        private void Log(string mensaje) => _txtLog.Text += mensaje + Environment.NewLine;

        private Window? VentanaPropietaria() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }
}
