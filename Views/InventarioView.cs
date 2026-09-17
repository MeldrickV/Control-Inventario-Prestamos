using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Layout;
using LabInventario.Data;
using LabInventario.Dialogs;
using LabInventario.Helpers;

namespace LabInventario.Views
{
    /// <summary>Pestaña de gestión de inventario (tabla `materiales`): alta, edición, eliminación y búsqueda.</summary>
    public class InventarioView : UserControl
    {
        private class FilaMaterial
        {
            public int Id { get; set; }
            public string Codigo { get; set; } = "";
            public string Nombre { get; set; } = "";
            public int Disponible { get; set; }
        }

        private readonly MaterialRepository _repo = new();
        private readonly PrestamoRepository _prestamoRepo = new();
        private readonly ObservableCollection<FilaMaterial> _filas = new();
        private readonly DataGrid _grid;
        private readonly TextBox _txtFiltro = new() { Width = 260 };

        public InventarioView()
        {
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = _filas,
                Columns =
                {
                    new DataGridTextColumn { Header = "Código de barras", Binding = new Avalonia.Data.Binding(nameof(FilaMaterial.Codigo)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                    new DataGridTextColumn { Header = "Nombre del elemento", Binding = new Avalonia.Data.Binding(nameof(FilaMaterial.Nombre)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) },
                    new DataGridTextColumn { Header = "Disponible", Binding = new Avalonia.Data.Binding(nameof(FilaMaterial.Disponible)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                },
            };

            var btnNuevo = new Button { Content = "Nuevo material", Classes = { "Flat" }, MinWidth = 130 };
            btnNuevo.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), Nuevo);
            
            var btnEditar = new Button { Content = "Editar", Classes = { "Outlined" }, MinWidth = 100 };
            btnEditar.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), Editar);
            
            var btnEliminar = new Button { Content = "Eliminar", Classes = { "Danger" }, MinWidth = 100 };
            btnEliminar.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), Eliminar);

            _txtFiltro.TextChanged += (_, _) => Cargar();

            var panelSuperior = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Avalonia.Thickness(0, 0, 0, 10),
            };
            panelSuperior.Children.Add(btnNuevo);
            panelSuperior.Children.Add(btnEditar);
            panelSuperior.Children.Add(btnEliminar);
            panelSuperior.Children.Add(new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(20, 0, 0, 0) });
            panelSuperior.Children.Add(_txtFiltro);

            var raiz = new DockPanel { Margin = new Avalonia.Thickness(15) };
            DockPanel.SetDock(panelSuperior, Dock.Top);
            raiz.Children.Add(panelSuperior);
            raiz.Children.Add(_grid);

            Content = raiz;
            Cargar();
        }

        private void Cargar()
        {
            _filas.Clear();
            foreach (var m in _repo.Listar(_txtFiltro.Text?.Trim() ?? ""))
                _filas.Add(new FilaMaterial { Id = m.Id, Codigo = m.CodigoBarras, Nombre = m.Nombre, Disponible = m.CantidadDisponible });
        }

        /// <summary>Recarga los datos desde la base de datos. Se llama cada vez que esta pestaña se vuelve visible.</summary>
        public void Actualizar() => Cargar();

        private async Task Nuevo()
        {
            var propietaria = Ventanas.Propietaria();
            if (propietaria is null) return;

            var dialogo = new MaterialDialog();
            await dialogo.ShowDialog(propietaria);
            if (dialogo.Resultado is null) return;

            var (codigo, nombre, total, _) = dialogo.Resultado.Value;
            if (_repo.ExisteCodigo(codigo))
            {
                await Dialogos.MostrarError(propietaria, "Ya existe un material con ese código de barras.", "Código duplicado");
                return;
            }
            _repo.Crear(codigo, nombre, total);
            Cargar();
        }

        private async Task Editar()
        {
            var propietaria = Ventanas.Propietaria();
            if (propietaria is null) return;

            var seleccionado = _grid.SelectedItem as FilaMaterial;
            if (seleccionado is null)
            {
                await Dialogos.MostrarInfo(propietaria, "Elige un material de la tabla primero.", "Sin selección");
                return;
            }

            var material = _repo.ObtenerPorId(seleccionado.Id)!;
            var dialogo = new MaterialDialog(material);
            await dialogo.ShowDialog(propietaria);
            if (dialogo.Resultado is null) return;

            var (codigo, nombre, total, disponible) = dialogo.Resultado.Value;
            if (_repo.ExisteCodigo(codigo, excluirId: material.Id))
            {
                await Dialogos.MostrarError(propietaria, "Ya existe otro material con ese código de barras.", "Código duplicado");
                return;
            }
            _repo.Actualizar(material.Id, codigo, nombre, total, disponible);
            Cargar();
        }

        private async Task Eliminar()
        {
            var propietaria = Ventanas.Propietaria();
            if (propietaria is null) return;

            var seleccionado = _grid.SelectedItem as FilaMaterial;
            if (seleccionado is null)
            {
                await Dialogos.MostrarInfo(propietaria, "Elige un material de la tabla primero.", "Sin selección");
                return;
            }

            // Un material con préstamos nunca se borra: la FK de la tabla
            // prestamos lo impediría de todos modos, pero con este chequeo el
            // usuario recibe un mensaje claro en vez de un error de SQLite.
            if (_prestamoRepo.TienePrestamosDeMaterial(seleccionado.Id))
            {
                await Dialogos.MostrarAdvertencia(propietaria,
                    $"No se puede eliminar el material '{seleccionado.Nombre}' porque tiene préstamos registrados. " +
                    "Si ya no debe usarse, revisa su historial en la pestaña Historial antes de darlo de baja.",
                    "Eliminación bloqueada");
                return;
            }

            var confirmar = await Dialogos.Confirmar(propietaria,
                $"¿Eliminar el material '{seleccionado.Nombre}'? Esta acción no se puede deshacer.",
                "Confirmar eliminación");

            if (confirmar)
            {
                _repo.Eliminar(seleccionado.Id);
                Cargar();
            }
        }
    }
}
