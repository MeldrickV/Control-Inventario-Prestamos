using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LabInventario.Data;
using LabInventario.Helpers;
using LabInventario.Models;
using LabInventario.Services;

namespace LabInventario.Views
{
    /// <summary>
    /// Pestaña de "Operación rápida": pensada para usarse casi sin mouse,
    /// solo escáner + Enter.
    ///
    /// Flujo:
    /// 1. Se escanea CUALQUIER código (carnet de alumno o material) en un
    ///    único cuadro de texto. El sistema detecta automáticamente de cuál
    ///    se trata (ver <see cref="DetectorPatrones"/>) consultando ambas
    ///    tablas por su columna única indexada, así que no importa el orden
    ///    en que se escaneen alumno/material la primera vez.
    /// 2. Una vez identificado el alumno, cada material que se escanea se
    ///    agrega a una lista temporal en memoria. Si el MISMO material se
    ///    escanea de nuevo, no se duplica la fila: se le suma la cantidad.
    /// 3. Para terminar y enviar la solicitud, basta con presionar Enter en
    ///    el cuadro de escaneo estando vacío (o presionar el botón
    ///    ACEPTAR). Todo el lote se registra con la MISMA fecha/hora.
    /// </summary>
    public class OperacionView : UserControl
    {
        private readonly PrestamoService _servicio = new();
        private readonly AlumnoRepository _alumnoRepo = new();
        private readonly MaterialRepository _materialRepo = new();
        private readonly DetectorPatrones _detector = new();

        private class ItemEscaneado
        {
            public string Codigo { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public int Cantidad { get; set; }
        }

        private readonly List<ItemEscaneado> _listaTemporal = new();
        private readonly ObservableCollection<string> _itemsAcumulados = new();
        private Alumno? _alumnoActual;

        private readonly RadioButton _radioSalida = new() { Content = "Salida (préstamo)", GroupName = "modo", IsChecked = true };
        private readonly RadioButton _radioEntrada = new() { Content = "Entrada (devolución)", GroupName = "modo" };

        private readonly TextBox _txtEscaneo = new() { FontSize = 16, HorizontalAlignment = HorizontalAlignment.Stretch };

        private readonly ListBox _lstAcumulados = new() { Height = 220, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly Button _btnConfirmar = new()
        {
            Content = "ACEPTAR",
            Classes = { "Success" },
            Height = 45,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
        };

        private readonly TextBlock _lblEscaneo = new() { Text = "Escanea: credencial del alumno", FontWeight = FontWeight.Bold };
        private readonly TextBlock _lblAlumnoInfo = new() { Text = "Alumno: (esperando escaneo)", FontStyle = FontStyle.Italic };
        private readonly TextBlock _lblEstado = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold, Height = 55, HorizontalAlignment = HorizontalAlignment.Stretch };

        public OperacionView()
        {
            _lstAcumulados.ItemsSource = _itemsAcumulados;

            // Panel: tipo de operación
            _radioSalida.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) ActualizarEtiquetaEscaneo(); };
            var panelModo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            panelModo.Children.Add(_radioSalida);
            panelModo.Children.Add(_radioEntrada);
            var grupoModo = Cajas.GroupBox("Tipo de operación", panelModo, 430);

            // Panel: captura del escáner
            _txtEscaneo.KeyDown += TxtEscaneo_KeyDown;

            var lblAyuda = new TextBlock
            {
                Text = "Escanea la identificacion, luego cada material\n" +
                       "Para enviar la solicitud, presiona Enter con el cuadro vacío.",
                Classes = { "Caption" },
                TextWrapping = TextWrapping.Wrap,
            };

            var btnLimpiar = new Button { Content = "Cancelar / Limpiar todo", Classes = { "Outlined" }, MinWidth = 200 };
            btnLimpiar.Click += (_, _) => Limpiar();

            var panelCaptura = new StackPanel { Spacing = 8 };
            panelCaptura.Children.Add(_lblEscaneo);
            panelCaptura.Children.Add(_txtEscaneo);
            panelCaptura.Children.Add(lblAyuda);
            panelCaptura.Children.Add(btnLimpiar);
            var grupoCaptura = Cajas.GroupBox("Captura (escáner)", panelCaptura, 430);

            // Panel: lista acumulada
            _btnConfirmar.Click += (_, _) => Errores.Ejecutar(Ventanas.Propietaria(), Confirmar);
            var panelLista = new StackPanel { Spacing = 10 };
            panelLista.Children.Add(_lstAcumulados);
            panelLista.Children.Add(_btnConfirmar);
            var grupoLista = Cajas.GroupBox("Lista de equipo a procesar", panelLista, 380);

            // Columna izquierda (modo + captura) y derecha (lista).
            // Se usa un Grid con columnas "*" (en vez del StackPanel
            // horizontal anterior) porque un StackPanel nunca reparte el
            // espacio sobrante entre sus hijos: cada uno se queda en su
            // ancho mínimo y el resto de la ventana se ve vacío. Con
            // columnas "*", ambas mitades crecen junto con la ventana.
            var columnaIzquierda = new StackPanel { Spacing = 15, HorizontalAlignment = HorizontalAlignment.Stretch };
            columnaIzquierda.Children.Add(grupoModo);
            columnaIzquierda.Children.Add(grupoCaptura);

            var filaSuperior = new Grid { ColumnSpacing = 15 };
            filaSuperior.ColumnDefinitions.Add(new ColumnDefinition(1.1, GridUnitType.Star));
            filaSuperior.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(columnaIzquierda, 0);
            Grid.SetColumn(grupoLista, 1);
            filaSuperior.Children.Add(columnaIzquierda);
            filaSuperior.Children.Add(grupoLista);

            var raiz = new StackPanel { Margin = new Avalonia.Thickness(15), Spacing = 15, HorizontalAlignment = HorizontalAlignment.Stretch };
            raiz.Children.Add(filaSuperior);
            raiz.Children.Add(_lblAlumnoInfo);
            raiz.Children.Add(_lblEstado);

            Content = raiz;

            AttachedToVisualTree += (_, _) => _txtEscaneo.Focus();
        }

        private void ActualizarEtiquetaEscaneo()
        {
            _lblEscaneo.Text = _alumnoActual is null
                ? "Escanea: credencial del alumno"
                : "Escanea: material (Enter en blanco para enviar)";
        }

        private void TxtEscaneo_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var codigo = _txtEscaneo.Text?.Trim() ?? "";
            _txtEscaneo.Clear();

            if (string.IsNullOrEmpty(codigo))
            {
                if (_alumnoActual is not null && _listaTemporal.Count > 0)
                    Errores.Ejecutar(Ventanas.Propietaria(), Confirmar);
                _txtEscaneo.Focus();
                return;
            }

            ProcesarCodigoEscaneado(codigo);
            _txtEscaneo.Focus();
        }

        private void ProcesarCodigoEscaneado(string codigo)
        {
            var tipoProbable = _detector.TipoProbable(codigo);

            Alumno? alumno;
            Material? material;

            if (tipoProbable == TipoCodigo.Material)
            {
                material = _materialRepo.ObtenerPorCodigo(codigo);
                alumno = material is null ? _alumnoRepo.ObtenerPorCuenta(codigo) : null;
            }
            else
            {
                alumno = _alumnoRepo.ObtenerPorCuenta(codigo);
                material = alumno is null ? _materialRepo.ObtenerPorCodigo(codigo) : null;
            }

            if (alumno is not null)
            {
                ProcesarAlumnoEscaneado(alumno);
                return;
            }

            if (material is not null)
            {
                ProcesarMaterialEscaneado(material);
                return;
            }

            MostrarError($"Código no reconocido en alumnos ni materiales: '{codigo}'.");
        }

        private void ProcesarAlumnoEscaneado(Alumno alumno)
        {
            if (_alumnoActual is not null && _alumnoActual.Id != alumno.Id && _listaTemporal.Count > 0)
            {
                MostrarError(
                    $"Ya hay una operación en curso para {_alumnoActual.Nombre}. " +
                    "Confirma (Enter en blanco) o limpia antes de escanear a otro alumno.");
                return;
            }

            _alumnoActual = alumno;
            _lblAlumnoInfo.Text = $"Alumno: {alumno.Nombre} ({alumno.NumeroCuenta})";
            _lblEstado.Text = "";
            ActualizarEtiquetaEscaneo();
        }

        private void ProcesarMaterialEscaneado(Material material)
        {
            if (_alumnoActual is null)
            {
                MostrarError("Escanea primero la credencial del alumno.");
                return;
            }

            var existente = _listaTemporal.FirstOrDefault(i => i.Codigo == material.CodigoBarras);

            if (existente is not null)
                existente.Cantidad += 1;
            else
                _listaTemporal.Add(new ItemEscaneado { Codigo = material.CodigoBarras, Nombre = material.Nombre, Cantidad = 1 });

            RefrescarLista();
            _lblEstado.Classes.Clear();
            _lblEstado.Classes.Add("Primary");
            _lblEstado.Text = $"Agregado: {material.Nombre} — presiona Enter en blanco cuando termines.";
        }

        private void RefrescarLista()
        {
            _itemsAcumulados.Clear();
            foreach (var item in _listaTemporal)
                _itemsAcumulados.Add($"{item.Nombre} [{item.Codigo}]  —  Cantidad: {item.Cantidad}");
        }

        private async Task Confirmar()
        {
            var propietaria = Ventanas.Propietaria();

            if (_alumnoActual is null)
            {
                if (propietaria is not null)
                    await Dialogos.MostrarAdvertencia(propietaria, "Escanea primero el carnet del alumno.", "Atención");
                _txtEscaneo.Focus();
                return;
            }

            if (_listaTemporal.Count == 0)
            {
                if (propietaria is not null)
                    await Dialogos.MostrarAdvertencia(propietaria, "Debe escanear al menos un material.", "Atención");
                _txtEscaneo.Focus();
                return;
            }

            var fechaOperacion = DateTime.Now;
            var items = _listaTemporal
                .Select(i => new LoteItem(i.Codigo, i.Cantidad))
                .ToList();
            var esSalida = _radioSalida.IsChecked == true;
            var alumnoNombre = _alumnoActual.Nombre;

            try
            {
                // El lote completo se registra en UNA transacción: o se
                // guarda todo, o no se guarda nada (ver RegistrarLote).
                var exitos = _servicio.RegistrarLote(_alumnoActual.NumeroCuenta, items, esSalida, fechaOperacion);
                MostrarExito($"Se registraron {exitos} equipo(s) para el alumno {alumnoNombre}.");
                Limpiar();
            }
            catch (PrestamoException error)
            {
                // Error de negocio esperado (stock, códigos, pendientes):
                // se muestra y se conserva la lista sin limpiar, para que
                // el operador pueda corregir (p. ej. escanear stock) y
                // reintentar con Enter.
                MostrarError(error.Message);
                if (propietaria is not null)
                    await Dialogos.MostrarAdvertencia(propietaria, error.Message, "No se pudo registrar el lote");
            }
            catch (Exception error)
            {
                // Fallo inesperado: se registra en errores.log y se muestra
                // el mensaje real, también sin limpiar la lista.
                Errores.RegistrarEnArchivo(error);
                MostrarError(error.Message);
                if (propietaria is not null)
                    await Dialogos.MostrarError(propietaria, error.Message, "Ocurrió un error");
            }
        }

        private void MostrarExito(string mensaje)
        {
            _lblEstado.Classes.Clear();
            _lblEstado.Classes.Add("Success");
            _lblEstado.Text = "✔ " + mensaje;
        }

        private void MostrarError(string mensaje)
        {
            _lblEstado.Classes.Clear();
            _lblEstado.Classes.Add("Danger");
            _lblEstado.Text = "✘ " + mensaje;
        }

        private void Limpiar()
        {
            _txtEscaneo.Clear();
            _listaTemporal.Clear();
            _itemsAcumulados.Clear();
            _alumnoActual = null;
            _lblAlumnoInfo.Text = "Alumno: (esperando escaneo)";
            ActualizarEtiquetaEscaneo();
            _txtEscaneo.Focus();
        }
    }
}
