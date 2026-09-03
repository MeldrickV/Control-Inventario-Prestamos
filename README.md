# LabInventario

Sistema de gestión de inventario y préstamos de materiales para laboratorio, desarrollado en **C# con .NET 8 y Avalonia UI**.

La aplicación permite administrar alumnos y materiales, registrar salidas y devoluciones mediante códigos escaneados, consultar el historial de préstamos e importar datos de forma masiva desde distintos formatos.

## Características

### 🔐 Control de acceso

El sistema dispone de dos roles:

- **Administrador**
  - Gestionar alumnos.
  - Gestionar materiales e inventario.
  - Importar datos masivamente.
  - Cambiar la contraseña del administrador.
  - Configurar el patrón utilizado para identificar números de cuenta durante el escaneo.
- **Usuario**
  - Registrar salidas de materiales.
  - Registrar devoluciones.
  - Consultar el historial de préstamos.

La contraseña del administrador se almacena mediante **hash y salt** en la base de datos, en lugar de guardarse como texto plano.

### 📦 Gestión de inventario

Permite:

- Registrar materiales.
- Editar materiales existentes.
- Eliminar materiales.
- Buscar materiales.
- Consultar cantidad total y cantidad disponible.
- Identificar materiales mediante código de barras.

Cada material utiliza un código de barras único.

### 👨‍🎓 Gestión de alumnos

Permite:

- Registrar alumnos.
- Editar información.
- Eliminar alumnos.
- Buscar alumnos.
- Identificar alumnos mediante número de cuenta.

El número de cuenta es único dentro de la base de datos.

### 🔄 Préstamos y devoluciones

El módulo de operación permite trabajar con un escáner de códigos.

El flujo principal es:

1. Escanear la credencial o número de cuenta del alumno.
2. Identificar al alumno.
3. Escanear uno o varios materiales.
4. Acumular cantidades cuando se escanea nuevamente el mismo material.
5. Registrar la salida.
6. Actualizar automáticamente la cantidad disponible.

Para las devoluciones:

1. Identificar al alumno.
2. Identificar el material.
3. Indicar o escanear la cantidad a devolver.
4. Registrar la devolución.
5. Incrementar nuevamente el inventario disponible.

Las devoluciones de préstamos activos del mismo alumno y material se procesan en orden de antigüedad cuando corresponde.

### 📋 Historial

El sistema conserva los préstamos registrados y permite consultar información como:

- Alumno.
- Número de cuenta.
- Material.
- Código de barras.
- Cantidad.
- Fecha de salida.
- Fecha de regreso.
- Estado del préstamo.

También permite filtrar el historial y consultar únicamente préstamos activos.

### 📥 Importación masiva

El módulo de importación permite cargar información desde:

- `.xlsx`
- `.xls`
- `.csv`
- `.txt`
- `.sql`

Actualmente se pueden importar:

- **Alumnos**
  - Nombre
  - Número de cuenta
- **Materiales**
  - Código de barras
  - Nombre
  - Cantidad total

El sistema analiza los encabezados del archivo y propone automáticamente un mapeo entre las columnas del archivo y los campos internos. El usuario puede revisar o modificar ese mapeo antes de realizar la importación.

Los archivos de ejemplo se encuentran en:

```text
ejemplos/
├── alumnos_ejemplo.csv
└── materiales_ejemplo.csv
```

## Detección automática durante el escaneo

El sistema puede determinar si un código corresponde probablemente a un alumno o a un material.

Por defecto, se considera que un número de cuenta tiene entre **6 y 12 dígitos**:

```regex
^\d{6,12}$
```

Este patrón puede modificarse desde la configuración del sistema.

Si el patrón no permite determinar correctamente el tipo de código, la aplicación puede recurrir a la búsqueda correspondiente en las tablas de alumnos y materiales.

## Tecnologías utilizadas

| Tecnología | Uso |
|---|---|
| C# | Lenguaje principal |
| .NET 8 | Plataforma de ejecución |
| Avalonia UI 11.1.3 | Interfaz gráfica multiplataforma |
| SQLite | Base de datos local |
| Microsoft.Data.Sqlite | Acceso a SQLite |
| ClosedXML | Lectura de archivos Excel |
| GitHub Actions | Integración y publicación automática |

## Arquitectura del proyecto

El proyecto está organizado en varias capas y responsabilidades:

```text
LabInventario
│
├── Data/
│   ├── AlumnoRepository.cs
│   ├── ConfiguracionRepository.cs
│   ├── DatabaseManager.cs
│   ├── MaterialRepository.cs
│   └── PrestamoRepository.cs
│
├── Dialogs/
│   ├── AlumnoDialog.cs
│   ├── CambiarPasswordDialog.cs
│   ├── CantidadDialog.cs
│   ├── ConfiguracionEscaneoDialog.cs
│   ├── MapeoColumnasDialog.cs
│   └── MaterialDialog.cs
│
├── Helpers/
│   └── UiHelpers.cs
│
├── Models/
│   ├── Alumno.cs
│   ├── Material.cs
│   ├── Prestamo.cs
│   └── Rol.cs
│
├── Services/
│   ├── AuthService.cs
│   ├── DetectorPatrones.cs
│   ├── ImportService.cs
│   ├── PrestamoException.cs
│   ├── PrestamoService.cs
│   └── SesionActual.cs
│
├── Views/
│   ├── AlumnosView.cs
│   ├── ImportarView.cs
│   ├── InventarioView.cs
│   ├── OperacionView.cs
│   └── PrestamosView.cs
│
├── Windows/
│   ├── LoginWindow.cs
│   └── MainWindow.cs
│
├── ejemplos/
│   ├── alumnos_ejemplo.csv
│   └── materiales_ejemplo.csv
│
├── App.axaml
├── App.cs
├── Program.cs
└── LabInventario.Avalonia.csproj
```

### Responsabilidades principales

**Models/**  
Contiene las entidades utilizadas por la aplicación: alumnos, materiales, préstamos y roles.

**Data/**  
Contiene los repositorios y la administración de la base de datos SQLite.

**Services/**  
Contiene la lógica de negocio, autenticación, préstamos, importación y detección de patrones de códigos.

**Views/**  
Contiene las pantallas principales de la aplicación.

**Windows/**  
Contiene las ventanas de inicio de sesión y ventana principal.

**Dialogs/**  
Contiene los cuadros de diálogo utilizados para capturar o modificar información.

**Helpers/**  
Contiene utilidades relacionadas con la interfaz de usuario y los diálogos multiplataforma.

## Base de datos

La aplicación utiliza **SQLite**, por lo que no necesita un servidor de base de datos independiente.

Al iniciar por primera vez se crea automáticamente:

```text
data/laboratorio.db
```

El esquema incluye las siguientes tablas:

```text
alumnos
materiales
prestamos
configuracion
```

### `alumnos`

Guarda:

- ID.
- Nombre.
- Número de cuenta.

### `materiales`

Guarda:

- ID.
- Código de barras.
- Nombre.
- Cantidad total.
- Cantidad disponible.

### `prestamos`

Guarda:

- Alumno asociado.
- Material asociado.
- Cantidad.
- Fecha de salida.
- Fecha de regreso.
- Estado.

Los estados disponibles son:

```text
Activo
Devuelto
```

### `configuracion`

Almacena valores de configuración como:

- Hash y salt de la contraseña del administrador.
- Patrón de detección de números de cuenta.

Las relaciones entre préstamos, alumnos y materiales utilizan claves foráneas y SQLite tiene activada la integridad referencial.

## Requisitos

Para ejecutar el proyecto desde código fuente se necesita:

- **.NET 8 SDK**
- Un sistema operativo compatible con Avalonia, como:
  - Windows
  - Linux
  - macOS

También se requiere acceso a **NuGet** durante la restauración inicial de dependencias.

## Ejecutar desde el código fuente

Clona o descarga el proyecto y entra en su directorio:

```bash
cd pruebas-lab-extra-main
```

Restaura las dependencias:

```bash
dotnet restore
```

Ejecuta la aplicación:

```bash
dotnet run
```

## Compilar

Para generar una compilación de Release:

```bash
dotnet build --configuration Release
```

## Publicar

Es posible generar una aplicación autocontenida para una plataforma concreta.

### Linux x64

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

### Windows x64

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### macOS x64

```bash
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

Los archivos publicados se generan dentro de:

```text
bin/Release/net8.0/<runtime>/publish/
```

La opción `--self-contained true` permite distribuir la aplicación sin exigir que el equipo de destino tenga instalado el runtime de .NET correspondiente.

## GitHub Actions

El repositorio incluye automatización mediante GitHub Actions.

### `build.yml`

Se ejecuta:

- En cada `push` a `main`.
- En cada `pull request` hacia `main`.
- Manualmente mediante `workflow_dispatch`.

Comprueba la compilación en:

```text
Ubuntu
Windows
macOS
```

El workflow restaura las dependencias y ejecuta una compilación de Release.

### `publish.yml`

Genera versiones autocontenidas para:

```text
linux-x64
win-x64
osx-x64
```

Los resultados se publican como **artifacts** de GitHub Actions.

También puede ejecutarse automáticamente al crear un tag con formato:

```text
v1.0.0
v1.1.0
v2.0.0
```

Cuando se ejecuta mediante un tag de versión, los ejecutables también se adjuntan al Release correspondiente.

## Inicio de sesión

Al iniciar la aplicación se presenta la selección de rol:

```text
Administrador
Usuario
```

El acceso como **Administrador** requiere la contraseña configurada.

La contraseña inicial prevista por la aplicación es:

```text
admin123
```

**Se recomienda cambiarla inmediatamente desde el menú de administración.**

El acceso como **Usuario** está destinado a la operación diaria del laboratorio y no requiere gestionar los catálogos de alumnos o materiales.

## Flujo de trabajo recomendado

Una instalación nueva puede configurarse siguiendo este orden:

1. Iniciar sesión como administrador.
2. Cambiar la contraseña inicial.
3. Registrar o importar los alumnos.
4. Registrar o importar los materiales.
5. Revisar las cantidades disponibles.
6. Configurar el patrón de números de cuenta si el formato utilizado por la institución es diferente al predeterminado.
7. Utilizar la sección **Operación** para registrar salidas y devoluciones.
8. Consultar **Historial** para revisar los movimientos realizados.

## Seguridad e integridad de datos

El proyecto incorpora varias medidas para mantener consistencia en las operaciones:

- Números de cuenta únicos.
- Códigos de barras únicos.
- Claves foráneas entre préstamos, alumnos y materiales.
- Validación de cantidades antes de registrar movimientos.
- Comprobación de stock disponible.
- Hash y salt para la contraseña del administrador.
- Validación del patrón de expresiones regulares antes de guardarlo.
- Actualización del inventario al registrar salidas y devoluciones.

## Estructura conceptual

El flujo principal de la aplicación puede resumirse así:

```text
                    ┌─────────────────┐
                    │  Inicio sesión  │
                    └────────┬────────┘
                             │
                 ┌───────────┴───────────┐
                 │                       │
          Administrador               Usuario
                 │                       │
        ┌────────┼────────┐              │
        │        │        │              │
     Alumnos  Inventario Importar         │
        │        │        │              │
        └────────┴────────┴──────┐       │
                                 │       │
                            ┌────▼────┐  │
                            │Operación│◄─┘
                            └────┬────┘
                                 │
                       ┌─────────┴─────────┐
                       │                   │
                    Salida              Entrada
                       │                   │
                       └─────────┬─────────┘
                                 │
                           ┌─────▼─────┐
                           │  SQLite   │
                           └─────┬─────┘
                                 │
                           ┌─────▼─────┐
                           │ Historial │
                           └───────────┘
```

## Estado del proyecto

El proyecto está estructurado como una aplicación de escritorio multiplataforma basada en **Avalonia UI + .NET 8**, con persistencia local mediante SQLite y automatización de compilación/publicación mediante GitHub Actions.

La interfaz y la lógica están separadas en ventanas, vistas, diálogos, servicios, repositorios y modelos para facilitar el mantenimiento y futuras modificaciones.
