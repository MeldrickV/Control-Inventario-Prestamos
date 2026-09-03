using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using LabInventario.Data;
using LabInventario.Dialogs;
using LabInventario.Helpers;

namespace LabInventario.Views
{
    /// <summary>Pestaña de gestión de alumnos (tabla `alumnos`): alta, edición, eliminación y búsqueda.</summary>
    public class AlumnosView : UserControl
    {
        private class FilaAlumno
        {
            public int Id { get; set; }
            public string Nombre { get; set; } = "";
            public string Cuenta { get; set; } = "";
        }

        private readonly AlumnoRepository _repo = new();
        private readonly ObservableCollection<FilaAlumno> _filas = new();
        private readonly DataGrid _grid;
        private readonly TextBox _txtFiltro = new() { Width = 260 };

        public AlumnosView()
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
                    new DataGridTextColumn { Header = "Nombre completo", Binding = new Avalonia.Data.Binding(nameof(FilaAlumno.Nombre)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                    new DataGridTextColumn { Header = "Número de cuenta", Binding = new Avalonia.Data.Binding(nameof(FilaAlumno.Cuenta)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) },
                },
            };

            var btnNuevo = new Button { Content = "Nuevo alumno", Width = 130 };
            btnNuevo.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), Nuevo);
            
            var btnEditar = new Button { Content = "Editar", Width = 100 };
            btnEditar.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), Editar);
            
            var btnEliminar = new Button { Content = "Eliminar", Width = 100 };
            btnEliminar.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), Eliminar);

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
            foreach (var a in _repo.Listar(_txtFiltro.Text?.Trim() ?? ""))
                _filas.Add(new FilaAlumno { Id = a.Id, Nombre = a.Nombre, Cuenta = a.NumeroCuenta });
        }
        
        /// <summary>Recarga los datos desde la base de datos. Se llama cada vez que esta pestaña se vuelve visible.</summary>
        public void Actualizar() => Cargar();
        
        private Window? VentanaPropietaria() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private async Task Nuevo()
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;

            var dialogo = new AlumnoDialog();
            await dialogo.ShowDialog(propietaria);
            if (dialogo.Resultado is null) return;

            var (nombre, cuenta) = dialogo.Resultado.Value;
            if (_repo.ExisteNumeroCuenta(cuenta))
            {
                await Dialogos.MostrarError(propietaria, "Ya existe un alumno con ese número de cuenta.", "Cuenta duplicada");
                return;
            }
            _repo.Crear(nombre, cuenta);
            Cargar();
        }

        private async Task Editar()
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;

            var seleccionado = _grid.SelectedItem as FilaAlumno;
            if (seleccionado is null)
            {
                await Dialogos.MostrarInfo(propietaria, "Elige un alumno de la tabla primero.", "Sin selección");
                return;
            }

            var alumno = _repo.ObtenerPorId(seleccionado.Id)!;
            var dialogo = new AlumnoDialog(alumno);
            await dialogo.ShowDialog(propietaria);
            if (dialogo.Resultado is null) return;

            var (nombre, cuenta) = dialogo.Resultado.Value;
            if (_repo.ExisteNumeroCuenta(cuenta, excluirId: alumno.Id))
            {
                await Dialogos.MostrarError(propietaria, "Ya existe otro alumno con ese número de cuenta.", "Cuenta duplicada");
                return;
            }
            _repo.Actualizar(alumno.Id, nombre, cuenta);
            Cargar();
        }

        private async Task Eliminar()
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;

            var seleccionado = _grid.SelectedItem as FilaAlumno;
            if (seleccionado is null)
            {
                await Dialogos.MostrarInfo(propietaria, "Elige un alumno de la tabla primero.", "Sin selección");
                return;
            }

            var confirmar = await Dialogos.Confirmar(propietaria,
                $"¿Eliminar al alumno '{seleccionado.Nombre}'? Esta acción no se puede deshacer.",
                "Confirmar eliminación");

            if (confirmar)
            {
                _repo.Eliminar(seleccionado.Id);
                Cargar();
            }
        }
    }
}
