using Avalonia.Controls;
using Avalonia.Layout;
using SukiUI.Controls;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Una entrada a editar en <see cref="CablesDialog"/>: el material
    /// escaneado, la etiqueta del cable que le corresponde (según su tipo,
    /// ver <see cref="LabInventario.Services.DetectorComplementos"/>) y la
    /// cantidad actual que lleva asociada.
    /// </summary>
    public record LineaCable(string NombreMaterial, string Etiqueta, int Cantidad);

    /// <summary>
    /// Formulario modal para ajustar los cables complementarios de los
    /// materiales escaneados en la pestaña Operación. Solo lista los
    /// materiales que admiten cable (fuente, generador u osciloscopio),
    /// cada uno con su propio contador 0..9: 0 = no lleva, 1 = el caso
    /// normal, más de 1 = casos especiales. Se puede abrir en cualquier
    /// momento, sin importar el orden en que se escanearon los materiales.
    /// </summary>
    public class CablesDialog : SukiWindow
    {
        private readonly List<NumericUpDown> _controles = new();

        /// <summary>Cantidades resultantes en el MISMO orden de las <see cref="LineaCable"/> recibidas, o null si se canceló.</summary>
        public List<int>? Resultado { get; private set; }

        public CablesDialog(IReadOnlyList<LineaCable> lineas)
        {
            Title = "Cables complementarios";
            CanResize = false;
            CanMinimize = false;
            CanFullScreen = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            var panel = new StackPanel { Spacing = 6, Width = 380 };

            panel.Children.Add(new TextBlock
            {
                Text = "Ajusta la cantidad de cable que lleva cada material (0 = no lleva).",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });

            foreach (var linea in lineas)
            {
                var numero = new NumericUpDown
                {
                    Width = 80,
                    Minimum = 0,
                    Maximum = 9,
                    FormatString = "0",
                    Value = linea.Cantidad,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                _controles.Add(numero);

                var fila = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Avalonia.Thickness(0, 10, 0, 0),
                };
                fila.Children.Add(new TextBlock
                {
                    Text = $"{linea.NombreMaterial}\n{linea.Etiqueta}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                });
                fila.Children.Add(numero);
                panel.Children.Add(fila);
            }

            var btnGuardar = new Button { Content = "Guardar", Classes = { "Flat" }, MinWidth = 90, IsDefault = true };
            btnGuardar.Click += (_, _) => Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Classes = { "Outlined" }, MinWidth = 90, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 14, 0, 0),
            };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);
            panel.Children.Add(panelBotones);

            Content = new GlassCard { Margin = new Avalonia.Thickness(20), Content = panel };
        }

        private void Guardar()
        {
            if (_controles.Count == 0)
            {
                Close();
                return;
            }

            Resultado = _controles.Select(c => (int)(c.Value ?? 0)).ToList();
            Close();
        }
    }
}