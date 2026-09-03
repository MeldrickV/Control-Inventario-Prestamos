using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Data;
using LabInventario.Dialogs;
using LabInventario.Helpers;
using LabInventario.Models;
using LabInventario.Services;

namespace LabInventario.Views
{
    /// <summary>
    /// Pestaña de préstamos: historial completo con filtro de búsqueda y
    /// opción de mostrar solo los activos.
    ///
    /// Los préstamos ACTIVOS del mismo alumno+material con MÁS DE UNA
    /// fecha de salida distinta se agrupan en una sola fila resumen
    /// (cantidad total pendiente y fecha del último escaneo), con una
    /// flecha para expandir y ver el detalle de cada registro individual
    /// por separado. Si solo hay una fecha, se muestra como fila normal,
    /// sin flecha ni comportamiento de grupo. Los ya devueltos se
    /// muestran como filas sueltas, tal como quedaron en su momento.
    ///
    /// El orden de la tabla se puede cambiar tocando cualquier encabezado
    /// (alumno, cuenta, material, cantidad, fecha, etc., asc/desc). El
    /// ordenamiento se aplica "a mano" (evento Sorting) en vez de dejar
    /// que el DataGrid reordene su ItemsSource por su cuenta, porque el
    /// orden automático rompería la relación entre cada fila-grupo y las
    /// filas de detalle que dependen de estar justo debajo de ella.
    ///
    /// Al marcar una devolución, si se selecciona la fila-grupo, la
    /// cantidad indicada se descuenta en orden FIFO entre los registros
    /// agrupados (el más antiguo primero). Si se selecciona un registro
    /// específico dentro del detalle expandido (o una fila sin agrupar),
    /// la devolución se aplica solo a ese registro puntual.
    /// </summary>
    public class PrestamosView : UserControl
    {
        private class FilaPrestamo
        {
            public bool EsGrupo { get; set; }
            public bool Expandido { get; set; }
            public int? PrestamoId { get; set; }
            public int AlumnoId { get; set; }
            public int MaterialId { get; set; }
            public string Alumno { get; set; } = "";
            public string Cuenta { get; set; } = "";
            public string Material { get; set; } = "";
            public string Codigo { get; set; } = "";
            public int Cantidad { get; set; }
            public string Salida { get; set; } = "";
            public string Regreso { get; set; } = "";
            public string Estado { get; set; } = "";
        }

        private readonly PrestamoRepository _repo = new();
        private readonly PrestamoService _servicio = new();
        private readonly ObservableCollection<FilaPrestamo> _filas = new();
        private readonly HashSet<(int AlumnoId, int MaterialId)> _gruposExpandidos = new();
        private readonly DataGrid _grid;
        private readonly Dictionary<string, string> _encabezadosBase = new();
        private readonly TextBox _txtFiltro = new() { Width = 260 };
        private readonly CheckBox _chkSoloActivos = new() { Content = "Mostrar solo préstamos activos" };

        // Clave de la columna por la que se ordena actualmente ("Alumno",
        // "Cuenta", "Material", "Codigo", "Cantidad", "Salida", "Regreso",
        // "Estado") y si el orden es descendente. Por defecto: más
        // reciente primero, igual que el comportamiento original.
        private string _columnaOrden = "Salida";
        private bool _ordenDescendente = true;

        public PrestamosView()
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
                    new DataGridTemplateColumn
                    {
                        Header = "",
                        Width = new DataGridLength(36),
                        CanUserSort = false,
                        CellTemplate = new FuncDataTemplate<FilaPrestamo>((fila, _) =>
                        {
                            if (fila is null || !fila.EsGrupo) return new TextBlock();
                            var boton = new Button
                            {
                                Content = fila.Expandido ? "▼" : "▶",
                                Padding = new Avalonia.Thickness(6, 0),
                                Background = Brushes.Transparent,
                                BorderThickness = new Avalonia.Thickness(0),
                            };
                            boton.Click += (_, _) => AlternarExpansion(fila.AlumnoId, fila.MaterialId);
                            return boton;
                        }),
                    },
                    new DataGridTextColumn { Header = "Alumno", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Alumno)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star), Tag = "Alumno" },
                    new DataGridTextColumn { Header = "Cuenta", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Cuenta)), Width = new DataGridLength(0.9, DataGridLengthUnitType.Star), Tag = "Cuenta" },
                    new DataGridTextColumn { Header = "Material", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Material)), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star), Tag = "Material" },
                    new DataGridTextColumn { Header = "Código", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Codigo)), Width = new DataGridLength(0.9, DataGridLengthUnitType.Star), Tag = "Codigo" },
                    new DataGridTextColumn { Header = "Cant.", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Cantidad)), Width = new DataGridLength(0.5, DataGridLengthUnitType.Star), Tag = "Cantidad" },
                    new DataGridTextColumn { Header = "Fecha salida", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Salida)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star), Tag = "Salida" },
                    new DataGridTextColumn { Header = "Fecha regreso", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Regreso)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star), Tag = "Regreso" },
                    new DataGridTextColumn { Header = "Estado", Binding = new Avalonia.Data.Binding(nameof(FilaPrestamo.Estado)), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star), Tag = "Estado" },
                },
            };
            _grid.Sorting += Grid_Sorting;

            foreach (var columna in _grid.Columns)
            {
                if (columna.Tag is string clave)
                    _encabezadosBase[clave] = columna.Header?.ToString() ?? "";
            }
            ActualizarEncabezadosOrden();

            _txtFiltro.TextChanged += (_, _) => Cargar();
            _chkSoloActivos.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Cargar(); };

            var btnDevolver = new Button { Content = "Marcar como devuelto", Width = 180 };
            btnDevolver.Click += (_, _) => Errores.Ejecutar(VentanaPropietaria(), MarcarDevuelto);

            var lblAyuda = new TextBlock
            {
                Text = "Toca ▶ para ver el detalle, o un encabezado para ordenar.",
                Foreground = Brushes.DimGray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(15, 0, 0, 0),
            };

            var panelSuperior = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Margin = new Avalonia.Thickness(0, 0, 0, 10),
            };
            panelSuperior.Children.Add(new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center });
            panelSuperior.Children.Add(_txtFiltro);
            panelSuperior.Children.Add(_chkSoloActivos);
            panelSuperior.Children.Add(btnDevolver);
            panelSuperior.Children.Add(lblAyuda);

            var raiz = new DockPanel { Margin = new Avalonia.Thickness(15) };
            DockPanel.SetDock(panelSuperior, Dock.Top);
            raiz.Children.Add(panelSuperior);
            raiz.Children.Add(_grid);

            Content = raiz;
            Actualizar();
        }

        /// <summary>
        /// Recarga los datos desde la base de datos. Se llama cada vez que
        /// esta pestaña se vuelve visible. De paso, purga los préstamos ya
        /// devueltos con más de 7 días de antigüedad (ver
        /// <see cref="PrestamoRepository.EliminarDevueltosAntiguos"/>): así
        /// el historial de devoluciones queda disponible el tiempo
        /// suficiente para revisarlo, sin acumularse indefinidamente.
        /// </summary>
        public void Actualizar()
        {
            _repo.EliminarDevueltosAntiguos();
            Cargar();
        }

        /// <summary>
        /// Maneja el clic en un encabezado de columna: alterna la
        /// dirección si es la misma columna, o cambia de columna con
        /// orden ascendente. Se marca <c>e.Handled = true</c> para que el
        /// DataGrid NO haga su propio ordenamiento automático del
        /// ItemsSource (eso separaría cada fila-grupo de su detalle
        /// expandido); en vez de eso, reconstruimos la lista nosotros
        /// mismos respetando el agrupado.
        /// </summary>
        private void Grid_Sorting(object? sender, DataGridColumnEventArgs e)
        {
            var clave = e.Column.Tag as string;
            if (clave is null)
            {
                e.Handled = true;
                return;
            }

            if (_columnaOrden == clave)
                _ordenDescendente = !_ordenDescendente;
            else
            {
                _columnaOrden = clave;
                _ordenDescendente = false;
            }

            ActualizarEncabezadosOrden();
            Cargar();
            e.Handled = true;
        }

        /// <summary>
        /// Agrega ▲/▼ al encabezado de la columna por la que se está
        /// ordenando (y lo quita de las demás), como señal visual simple
        /// que no depende de ninguna propiedad especial del control.
        /// </summary>
        private void ActualizarEncabezadosOrden()
        {
            foreach (var columna in _grid.Columns)
            {
                if (columna.Tag is not string clave || !_encabezadosBase.TryGetValue(clave, out var baseTexto))
                    continue;

                columna.Header = clave == _columnaOrden
                    ? $"{baseTexto} {(_ordenDescendente ? "▼" : "▲")}"
                    : baseTexto;
            }
        }

        private List<T> Ordenar<T, TKey>(List<T> lista, Func<T, TKey> selector) =>
            (_ordenDescendente ? lista.OrderByDescending(selector) : lista.OrderBy(selector)).ToList();

        private void Cargar()
        {
            _filas.Clear();

            var detallados = _repo.ListarDetallado(_txtFiltro.Text?.Trim() ?? "", _chkSoloActivos.IsChecked == true);
            var activos = detallados.Where(p => p.Estado == "Activo").ToList();

            var devueltosBase = detallados.Where(p => p.Estado != "Activo").ToList();
            var devueltos = _columnaOrden switch
            {
                "Alumno" => Ordenar(devueltosBase, p => p.AlumnoNombre),
                "Cuenta" => Ordenar(devueltosBase, p => p.NumeroCuenta),
                "Material" => Ordenar(devueltosBase, p => p.MaterialNombre),
                "Codigo" => Ordenar(devueltosBase, p => p.CodigoBarras),
                "Cantidad" => Ordenar(devueltosBase, p => p.Cantidad),
                "Regreso" => Ordenar(devueltosBase, p => p.FechaRegreso ?? p.FechaSalida),
                "Estado" => Ordenar(devueltosBase, p => p.Estado),
                _ => Ordenar(devueltosBase, p => p.FechaSalida), // "Salida" (por defecto)
            };

            var gruposBase = activos
                .GroupBy(p => (p.AlumnoId, p.MaterialId))
                .Select(g => new
                {
                    g.Key.AlumnoId,
                    g.Key.MaterialId,
                    Alumno = g.First().AlumnoNombre,
                    Cuenta = g.First().NumeroCuenta,
                    Material = g.First().MaterialNombre,
                    Codigo = g.First().CodigoBarras,
                    Total = g.Sum(x => x.Cantidad),
                    UltimaSalida = g.Max(x => x.FechaSalida),
                    Detalle = g.OrderBy(x => x.FechaSalida).ToList(),
                })
                .ToList();

            var grupos = _columnaOrden switch
            {
                "Alumno" => Ordenar(gruposBase, g => g.Alumno),
                "Cuenta" => Ordenar(gruposBase, g => g.Cuenta),
                "Material" => Ordenar(gruposBase, g => g.Material),
                "Codigo" => Ordenar(gruposBase, g => g.Codigo),
                "Cantidad" => Ordenar(gruposBase, g => g.Total),
                _ => Ordenar(gruposBase, g => g.UltimaSalida), // "Salida"/"Regreso"/"Estado": los activos no tienen fecha de regreso ni distintos estados, se cae a la fecha de salida
            };

            foreach (var grupo in grupos)
            {
                // Si el alumno solo tiene UNA fecha de salida pendiente de
                // este material, se muestra como fila normal (sin flecha
                // de expandir): el agrupado con detalle solo tiene sentido
                // cuando hay varias fechas distintas por reconciliar.
                if (grupo.Detalle.Count == 1)
                {
                    var unico = grupo.Detalle[0];
                    _filas.Add(new FilaPrestamo
                    {
                        EsGrupo = false,
                        PrestamoId = unico.Id,
                        AlumnoId = grupo.AlumnoId,
                        MaterialId = grupo.MaterialId,
                        Alumno = grupo.Alumno,
                        Cuenta = grupo.Cuenta,
                        Material = grupo.Material,
                        Codigo = grupo.Codigo,
                        Cantidad = unico.Cantidad,
                        Salida = unico.FechaSalida.ToString("yyyy-MM-dd HH:mm:ss"),
                        Regreso = "-",
                        Estado = "Activo",
                    });
                    continue;
                }

                var expandido = _gruposExpandidos.Contains((grupo.AlumnoId, grupo.MaterialId));

                _filas.Add(new FilaPrestamo
                {
                    EsGrupo = true,
                    Expandido = expandido,
                    AlumnoId = grupo.AlumnoId,
                    MaterialId = grupo.MaterialId,
                    Alumno = grupo.Alumno,
                    Cuenta = grupo.Cuenta,
                    Material = grupo.Material,
                    Codigo = grupo.Codigo,
                    Cantidad = grupo.Total,
                    Salida = grupo.UltimaSalida.ToString("yyyy-MM-dd HH:mm:ss") + "  (última)",
                    Regreso = "-",
                    Estado = "Activo",
                });

                if (!expandido) continue;

                foreach (var item in grupo.Detalle)
                {
                    _filas.Add(new FilaPrestamo
                    {
                        EsGrupo = false,
                        PrestamoId = item.Id,
                        AlumnoId = grupo.AlumnoId,
                        MaterialId = grupo.MaterialId,
                        Alumno = "      ↳ detalle",
                        Cuenta = "",
                        Material = item.MaterialNombre,
                        Codigo = item.CodigoBarras,
                        Cantidad = item.Cantidad,
                        Salida = item.FechaSalida.ToString("yyyy-MM-dd HH:mm:ss"),
                        Regreso = "-",
                        Estado = "Activo",
                    });
                }
            }

            foreach (var p in devueltos)
            {
                _filas.Add(new FilaPrestamo
                {
                    EsGrupo = false,
                    PrestamoId = p.Id,
                    AlumnoId = p.AlumnoId,
                    MaterialId = p.MaterialId,
                    Alumno = p.AlumnoNombre,
                    Cuenta = p.NumeroCuenta,
                    Material = p.MaterialNombre,
                    Codigo = p.CodigoBarras,
                    Cantidad = p.Cantidad,
                    Salida = p.FechaSalida.ToString("yyyy-MM-dd HH:mm:ss"),
                    Regreso = p.FechaRegreso?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                    Estado = p.Estado,
                });
            }
        }

        private void AlternarExpansion(int alumnoId, int materialId)
        {
            var clave = (alumnoId, materialId);
            if (!_gruposExpandidos.Add(clave))
                _gruposExpandidos.Remove(clave);
            Cargar();
        }

        private Window? VentanaPropietaria() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        private async Task MarcarDevuelto()
        {
            var propietaria = VentanaPropietaria();
            if (propietaria is null) return;

            var seleccionado = _grid.SelectedItem as FilaPrestamo;
            if (seleccionado is null)
            {
                await Dialogos.MostrarInfo(propietaria, "Elige una fila de la tabla primero (el grupo o un detalle expandido).", "Sin selección");
                return;
            }

            if (seleccionado.Estado != "Activo")
            {
                await Dialogos.MostrarInfo(propietaria, "Este préstamo ya fue devuelto.", "Sin cambios");
                return;
            }

            var mensaje = seleccionado.EsGrupo
                ? $"¿Cuántos '{seleccionado.Material}' regresa {seleccionado.Alumno}? (de {seleccionado.Cantidad} pendientes en total; se descuenta primero de la salida más antigua)"
                : $"¿Cuántos regresan de este registro puntual? (de {seleccionado.Cantidad})";

            var dialogoCantidad = new CantidadDialog(mensaje, maximo: seleccionado.Cantidad, valorInicial: seleccionado.Cantidad);
            await dialogoCantidad.ShowDialog(propietaria);
            if (dialogoCantidad.Resultado is not int cantidad) return;

            if (seleccionado.EsGrupo)
                _servicio.RegistrarEntradaPorAlumnoYMaterial(seleccionado.AlumnoId, seleccionado.MaterialId, cantidad, DateTime.Now);
            else
                _servicio.RegistrarEntradaDePrestamoEspecifico(seleccionado.PrestamoId!.Value, cantidad, DateTime.Now);

            Cargar();
        }
    }
}
