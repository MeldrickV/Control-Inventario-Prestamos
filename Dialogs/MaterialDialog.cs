using Avalonia.Controls;
using Avalonia.Layout;
using LabInventario.Helpers;
using LabInventario.Models;

namespace LabInventario.Dialogs
{
    /// <summary>
    /// Formulario modal para crear o editar un material.
    /// La cantidad disponible solo se muestra al editar (al crear siempre
    /// arranca igual a la cantidad total, pues aún no hay préstamos).
    /// </summary>
    public class MaterialDialog : Window
    {
        private readonly TextBox _txtCodigo = new() { Width = 280 };
        private readonly TextBox _txtNombre = new() { Width = 280 };
        private readonly NumericUpDown _numTotal = new() { Width = 280, Minimum = 0, Maximum = 100000, FormatString = "0" };
        private readonly NumericUpDown? _numDisponible;
        private readonly bool _esEdicion;

        public (string CodigoBarras, string Nombre, int CantidadTotal, int CantidadDisponible)? Resultado { get; private set; }

        public MaterialDialog(Material? material = null)
        {
            _esEdicion = material is not null;
            Title = _esEdicion ? "Editar material" : "Nuevo material";
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;

            _txtCodigo.Text = material?.CodigoBarras ?? "";
            _txtNombre.Text = material?.Nombre ?? "";
            _numTotal.Value = material?.CantidadTotal ?? 0;

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 6, Width = 320 };
            raiz.Children.Add(new TextBlock { Text = "Código de barras:" });
            raiz.Children.Add(_txtCodigo);
            raiz.Children.Add(new TextBlock { Text = "Nombre del elemento:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            raiz.Children.Add(_txtNombre);
            raiz.Children.Add(new TextBlock { Text = "Cantidad total:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
            raiz.Children.Add(_numTotal);

            if (_esEdicion)
            {
                _numDisponible = new NumericUpDown { Width = 280, Minimum = 0, Maximum = 100000, FormatString = "0", Value = material!.CantidadDisponible };
                raiz.Children.Add(new TextBlock { Text = "Cantidad disponible:", Margin = new Avalonia.Thickness(0, 10, 0, 0) });
                raiz.Children.Add(_numDisponible);
            }

            var btnGuardar = new Button { Content = "Guardar", Width = 90, IsDefault = true };
            btnGuardar.Click += async (_, _) => await Guardar();

            var btnCancelar = new Button { Content = "Cancelar", Width = 90, IsCancel = true };
            btnCancelar.Click += (_, _) => Close();

            var panelBotones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 14, 0, 0) };
            panelBotones.Children.Add(btnGuardar);
            panelBotones.Children.Add(btnCancelar);
            raiz.Children.Add(panelBotones);

            Content = raiz;
        }

        private async Task Guardar()
        {
            var codigo = _txtCodigo.Text?.Trim() ?? "";
            var nombre = _txtNombre.Text?.Trim() ?? "";
            var total = (int)(_numTotal.Value ?? 0);
            var disponible = _numDisponible is not null ? (int)(_numDisponible.Value ?? 0) : total;

            if (string.IsNullOrEmpty(codigo) || string.IsNullOrEmpty(nombre))
            {
                await Dialogos.MostrarAdvertencia(this, "Código de barras y nombre son obligatorios.", "Datos incompletos");
                return;
            }

            if (disponible > total)
            {
                await Dialogos.MostrarAdvertencia(this, "La cantidad disponible no puede superar la cantidad total.", "Dato inválido");
                return;
            }

            Resultado = (codigo, nombre, total, disponible);
            Close();
        }
    }
}
