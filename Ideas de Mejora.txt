# AGENTS.md

Parámetros del proyecto **LabInventario** para tener en cuenta en toda interacción futura.

## Identidad del proyecto

- **Nombre**: LabInventario — Gestión de Salidas y Entradas del Laboratorio de Electrónica.
- **Ruta**: `~/Música/music/Control-Inventario-Prestamos`.
- **Qué es**: app de escritorio multiplataforma (Avalonia) para inventario y préstamos de materiales de laboratorio, con escaneo de códigos, historial FIFO, importación/exportación y respaldo cifrado.
- **Idioma del equipo/proyecto**: español (comentarios XML, mensajes de UI y README en español).

## Stack

- .NET 8, `net8.0`, C# `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`.
- Avalonia 11.3.14 + Avalonia.Themes.Fluent + Avalonia.Fonts.Inter + Avalonia.Controls.DataGrid 11.3.13 (+ Diagnostics solo en Debug).
- SukiUI 6.1.1: ventanas (`SukiWindow`), `SukiMessageBox`, `GlassCard`, `SukiBackgroundStyle`. **No migrar a SukiUI 7 / Avalonia 12**.
- SQLite cifrado: `Microsoft.Data.Sqlite.Core` 8.0.10 + `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.10 (AES-256 por máquina).
- ClosedXML 0.104.1 (lectura/escritura de `.xlsx`).
- xUnit: projecto de pruebas `tests/LabInventario.Tests` (net8.0), referenciado desde `LabInventario.sln`.
- CI: GitHub Actions (`build.yml` = restore + build Release + `dotnet test`; `publish.yml` = self-contained single-file por plataforma).

## Convenciones arquitectónicas

- **UI 100 % en C#**: el único archivo `.axaml` es `App.axaml` (necesario para que Avalonia cargue sus `avares://`). Ventanas, vistas y diálogos se construyen a mano con controles.
- Capas: `Data/` (repositorios + `DatabaseManager`), `Services/` (lógica de negocio), `Models/`, `Views/` (pestañas `UserControl`), `Dialogs/` (`SukiWindow` con `SizeToContent.WidthAndHeight`, `CanResize=false`, `WindowStartupLocation.CenterOwner`), `Helpers/` (`UiHelpers.cs`) y `Windows/` (`LoginWindow`, `MainWindow : SukiWindow`).
- `MainWindow` es la única clase que conoce todas las vistas; cada vista solo conoce sus repos/servicios. Pestañas de Administrador (Inventario, Alumnos, Importar, Exportar) solo se agregan si `SesionActual.EsAdministrador`.
- **DI ligera**: los repositorios/servicios aceptan `DatabaseManager? db = null` (fallback `DatabaseManager.Instancia`) para que las pruebas inyecten una base temporal.
- Refactor 2026: la ventana "propietaria" de los diálogos se obtiene con `Ventanas.Propietaria()` (definido una sola vez en `Helpers/UiHelpers.cs`); **no duplicar** métodos `VentanaPropietaria()` en vistas.

## Reglas de negocio fijadas

- Alumnos y materiales con historial de préstamos **nunca se borran** (se bloquea con mensaje claro; la FK es solo red de seguridad).
- Devoluciones: FIFO (descuenta primero la salida más antigua); las parciales dejan la fila activa con cantidad restante y crean una fila "Devuelta" visible con la parte devuelta.
- `RegistrarLote` crea **una fila de préstamo por ítem** (no agrupa cantidades).
- **Historial sin purga automática**: se conserva todo. La limpieza es SOLO manual (Administrador) vía menú `Administración → Limpiar historial antiguo...`: diálogo con días (default 30), conteo previo (`ContarDevueltosAntiguos`) y confirmación. Default de `PrestamoRepository.EliminarDevueltosAntiguos` = 30.
- Contraseña admin por defecto `admin123` (se recomienda cambiar), PBKDF2 100k, hash+salt en `configuracion`.
- Patrón de cuenta por defecto `^\d{6,12}$` (clave config `PatronCuentaAlumno`), configurable en Administración.
- Exportaciones CSV/XLSX son texto plano (aviso al usuario); el respaldo `.db` queda cifrado por máquina.
- Códigos CSV: BOM UTF-8, escape de comas/comillas/saltos.
- Importación: soporta `.xlsx/.xls/.csv/.txt/.sql/.db/.sqlite/.sqlite3`; al sugerir mapeos los encabezados se normalizan (minúsculas y sin acentos).

## Datos y seguridad

- Carpeta de datos (DB + `.machine-id-respaldo`): si existe `data/` portable con contenido junto al exe se conserva (continuidad); si no → carpeta del usuario: Windows `%LOCALAPPDATA%\LabInventario`, Linux `$XDG_DATA_HOME/LabInventario` o `~/.local/share/LabInventario`, macOS `~/Library/Application Support/LabInventario`.
- `laboratorio.db` cifrado con SQLCipher; clave derivada de secreto embebido + ID de máquina (machine-id / Registry MachineGuid / ioreg IOPlatformUUID, fallback a GUID guardado en la carpeta de datos).
- FK activas por conexión (`PRAGMA foreign_keys = ON`); fechas en texto `yyyy-MM-dd HH:mm:ss` con cultura invariable.

## Pruebas

- **IMPORTANTE**: en esta máquina de trabajo NO hay dotnet instalado y **no hay que instalarlo ni ejecutar** `dotnet build/test` localmente. Las pruebas corren vía GitHub Actions (`build.yml`). Todos los cambios de código se verifican con razonamiento lógico cuidadoso.
- Estructura de pruebas: `tests/LabInventario.Tests` con `BaseDePruebas` (base temporal por test, expone `Db`, `Alumnos`, `Materiales`, `Prestamos`, `Servicio`; constantes `CuentaAlumno`, `NombreAlumno`, `CodigoMaterial`, `NombreMaterial`, `CantidadTotalMaterial`; `FechaPrueba(dia, hora=8)` → fechas deterministas 2026-09-XX) y `InicializacionSqlite` (ModuleInitializer → `SQLitePCL.Batteries_V2.Init()`).
- El csproj principal **excluye `tests\**`** del glob por defecto del SDK (Compile/AvaloniaResource/EmbeddedResource/None/Content/Page Remove).

## Código reciente relevante

- Purga automática eliminada de `App.cs` y `PrestamosView.Actualizar()`; `PrestamosView` expone `FiltroActual` y `SoloActivosActual` para que `ExportarView` exporte el historial con los filtros vigentes.
- `App.cs` controla el ciclo login→main (ShutdownMode.OnExplicitShutdown) y registra excepciones no capturadas en `errores.log`.
- `Program.cs` es el punto de entrada ([STAThread] + `SQLitePCL.Batteries_V2.Init()` para el motor nativo SQLCipher + `StartWithClassicDesktopLifetime`).