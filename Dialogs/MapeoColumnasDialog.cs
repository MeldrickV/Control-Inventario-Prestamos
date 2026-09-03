using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Permite al usuario confirmar o corregir gráficamente el mapeo entre
    /// las columnas detectadas en el archivo y los campos que el sistema
    /// necesita, mostrando una vista previa de los datos para facilitar
    /// la decisión.
    /// </summary>
    public class MapeoColumnasDialog : Window
    {
        private readonly Dictionary<string, ComboBox> _combos = new();

        public Dictionary<string, int?>? Resultado { get; private set; }

        public MapeoColumnasDialog(List<string> headers, string[] camposDestino,
            Dictionary<string, int?> mapeoSugerido, List<List<string?>> vistaPrevia)
        {
            Title = "Mapeo de columnas para importación";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var lblTitulo = new TextBlock
            {
                Text = "Asocia cada campo del sistema con la columna correspondiente del archivo:",
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                Width = 530,
            };

            var opciones = new List<string> { "(ignorar)" };
            opciones.AddRange(headers);

            var grid = new Grid { Margin = new Avalonia.Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            for (var i = 0; i < camposDestino.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var campo = camposDestino[i];
                var lbl = new TextBlock { Text = campo, VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 4, 0, 4) };
                Grid.SetRow(lbl, i);
                Grid.SetColumn(lbl, 0);

                var combo = new ComboBox { ItemsSource = opciones, Width = 260, Margin = new Avalonia.Thickness(0, 4, 0, 4) };
                var indiceSugerido = mapeoSugerido.TryGetValue(campo, out var idx) ? idx : null;
                combo.SelectedIndex = indiceSugerido.HasValue ? indiceSugerido.Value + 1 : 0;
                Grid.SetRow(combo, i);
                Grid.SetColumn(combo, 1);

                grid.Children.Add(lbl);
                grid.Children.Add(combo);
                _combos[campo] = combo;
            }

            var lblPreview = new TextBlock { Text = "Vista previa (primeras filas del archivo):", Margin = new Avalonia.Thickness(0, 15, 0, 4) };

            var lineas = new List<string> { string.Join(" | ", headers) };
            lineas.AddRange(vistaPrevia.Take(5).Select(fila => string.Join(" | ", fila.Select(v => v ?? ""))));

            var txtPreview = new TextBox
            {
                Text = string.Join(Environment.NewLine, lineas),
                Width = 530,
                Height = 120,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas,monospace"),
                FontSize = 12,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(txtPreview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(txtPreview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

            var btnImportar = new Button { Content = "Importar", Width = 110, IsDefault = true };
            btnImportar.Click += (_, _) => Confirmar();

            var btnCancelar = new Button { Content = "Cancelar", Width = 110, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnImportar);
            panelBotones.Children.Add(btnCancelar);

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 4, Width = 530 };
            raiz.Children.Add(lblTitulo);
            raiz.Children.Add(grid);
            raiz.Children.Add(lblPreview);
            raiz.Children.Add(txtPreview);
            raiz.Children.Add(panelBotones);

            Content = raiz;
        }

        private void Confirmar()
        {
            var mapeoFinal = new Dictionary<string, int?>();
            foreach (var (campo, combo) in _combos)
            {
                var seleccionado = combo.SelectedIndex; // 0 = "(ignorar)"
                mapeoFinal[campo] = seleccionado <= 0 ? null : seleccionado - 1;
            }
            Resultado = mapeoFinal;
            Close();
        }
    }
}
