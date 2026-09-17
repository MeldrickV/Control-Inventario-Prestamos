using Avalonia.Controls;
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
        private readonly PrestamosView? _historialView;
        private readonly CheckBox _chkUsarFiltro = new() { Content = "Usar filtro de la pestaña Historial" };
        private readonly CheckBox _chkSoloActivos = new() { Content = "Solo activos" };
        private readonly TextBox _txtLog = new()
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas,monospace"),
        };

        /// <summary>
        /// Referencia opcional a la pestaña de Historial (la que MainWindow
        /// mantiene como su única instancia). Cuando la exportación del
        /// historial lo pide, se usa su filtro de búsqueda y su casilla
        /// "solo activos" para exportar exactamente lo que el administrador
        /// está viendo en esa pestaña.
        /// </summary>
        public ExportarView(PrestamosView? historialView = null)
        {
            _historialView = historialView;

            var lblTitulo = new TextBlock
            {
                Text = "Exportación de datos",
                Classes = { "h4" },
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

            // El historial trae además sus dos opciones de filtrado: usar o
            // no el filtro y "solo activos" que están vigentes en la pestaña
            // Historial en el momento de exportar.
            var panelHistorial = new StackPanel { Spacing = 4 };
            panelHistorial.Children.Add(FilaExportacion("Historial de préstamos",
                () => ExportarCsv("Historial", "historial.csv", ExportarHistorialCsv),
                () => ExportarXlsx("Historial", "historial.xlsx", ExportarHistorialXlsx)));

            var panelOpcionesHistorial = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Avalonia.Thickness(160, 0, 0, 0) };
            panelOpcionesHistorial.Children.Add(_chkUsarFiltro);
            panelOpcionesHistorial.Children.Add(_chkSoloActivos);
            panelHistorial.Children.Add(panelOpcionesHistorial);
            seccionDatos.Children.Add(panelHistorial);

            var cajaDatos = Cajas.GroupBox("Exportar por tipo de dato", seccionDatos);

            var seccionRespaldo = new StackPanel { Spacing = 10 };

            var btnRespaldoCifrado = new Button { Content = "Respaldo cifrado (.db)...", Classes = { "Flat" }, MinWidth = 260, Height = 32 };
            btnRespaldoCifrado.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), async () =>
            {
                var propietaria = Ventanas.Propietaria();
                if (propietaria is null) return;
                var ruta = await Dialogos.GuardarArchivo(propietaria, "Guardar respaldo cifrado", "laboratorio-respaldo.db",
                    new FilePickerFileType("Base de datos SQLite") { Patterns = new[] { "*.db" } });
                if (ruta is null) return;
                _servicio.ExportarBaseDatosCifrada(ruta);
                Log($"Respaldo cifrado generado en: {ruta}");
                await Dialogos.MostrarInfo(propietaria, "Respaldo generado. Sigue cifrado: solo se puede volver a abrir con esta app en esta misma máquina.", "Respaldo completado");
            });

            var btnRespaldoXlsx = new Button { Content = "Volcado completo (.xlsx, sin cifrar)...", Classes = { "Warning" }, MinWidth = 260, Height = 32 };
            btnRespaldoXlsx.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), async () =>
            {
                var propietaria = Ventanas.Propietaria();
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

            // Grid con columnas "*" (no StackPanel horizontal) para que
            // ambas cajas se repartan el ancho disponible en vez de
            // quedarse pegadas a la izquierda con espacio vacío a la derecha.
            var panelCajas = new Grid { ColumnSpacing = 15 };
            panelCajas.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            panelCajas.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(cajaDatos, 0);
            Grid.SetColumn(cajaRespaldo, 1);
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

            var btnCsv = new Button { Content = "CSV...", Classes = { "Outlined" }, MinWidth = 90 };
            btnCsv.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), exportarCsv);

            var btnXlsx = new Button { Content = "Excel...", Classes = { "Outlined" }, MinWidth = 90 };
            btnXlsx.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), exportarXlsx);

            panel.Children.Add(btnCsv);
            panel.Children.Add(btnXlsx);
            return panel;
        }

        private async Task ExportarCsv(string etiqueta, string nombreSugerido, Action<string> accionExportar)
        {
            var propietaria = Ventanas.Propietaria();
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
            var propietaria = Ventanas.Propietaria();
            if (propietaria is null) return;
            var ruta = await Dialogos.GuardarArchivo(propietaria, $"Exportar {etiqueta} a Excel", nombreSugerido,
                new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } });
            if (ruta is null) return;
            accionExportar(ruta);
            Log($"{etiqueta} exportado a Excel: {ruta}");
            await Dialogos.MostrarInfo(propietaria, "Exportación completada.", "Listo");
        }

        private void ExportarHistorialCsv(string ruta) =>
            _servicio.ExportarHistorialCsv(ruta, FiltroHistorialActual, SoloActivosHistorial);

        private void ExportarHistorialXlsx(string ruta) =>
            _servicio.ExportarHistorialXlsx(ruta, FiltroHistorialActual, SoloActivosHistorial);

        /// <summary>Filtro a aplicar al exportar el historial: el de la pestaña Historial solo si la casilla correspondiente está marcada.</summary>
        private string FiltroHistorialActual => _chkUsarFiltro.IsChecked == true ? _historialView?.FiltroActual ?? "" : "";

        /// <summary>Si la exportación del historial debe limitarse a los préstamos activos.</summary>
        private bool SoloActivosHistorial => _chkSoloActivos.IsChecked == true;

        private void Log(string mensaje) => _txtLog.Text += mensaje + Environment.NewLine;
    }
}
