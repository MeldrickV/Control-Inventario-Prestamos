namespace LabInventario.Services
{
    /// <summary>
    /// Una sección del manual integrado (menú Ayuda). El texto vive en
    /// código a propósito: así se filtra por rol con <see cref="Para"/> y
    /// se verifica en pruebas sin leer archivos externos.
    /// </summary>
    /// <param name="Id">Identificador estable (lo usa el menú para abrir una sección concreta).</param>
    /// <param name="Titulo">Título que aparece en la lista de la izquierda.</param>
    /// <param name="SoloAdmin">True si solo se muestra entrando como Administrador.</param>
    /// <param name="Resumen">Párrafo corto de qué trata la sección.</param>
    /// <param name="Pasos">Pasos numerados, escritos para alguien sin experiencia previa.</param>
    public sealed record SeccionAyuda(string Id, string Titulo, bool SoloAdmin, string Resumen, string[] Pasos);

    /// <summary>
    /// Contenido del manual integrado. Los pasos nombran los botones y
    /// menús con su texto exacto para que el instalador no tenga que
    /// adivinar. Si se renombra un botón en una vista, hay que actualizar
    /// aquí el paso que lo menciona.
    /// </summary>
    public static class AyudaContenido
    {
        public const string IdPrimerosPasos = "primeros-pasos";
        public const string IdSalida = "salida";
        public const string IdDevolucion = "devolucion";
        public const string IdHistorial = "historial";
        public const string IdSiAlgoFalla = "si-algo-falla";
        public const string IdInventario = "inventario";
        public const string IdAlumnos = "alumnos";
        public const string IdImportar = "importar";
        public const string IdExportar = "exportar";
        public const string IdLimpiarHistorial = "limpiar-historial";
        public const string IdPasswordYPatron = "password-y-patron";
        public const string IdGoogle = "google-paso-a-paso";
        public const string IdAcercaDe = "acerca-de";

        private static readonly SeccionAyuda[] Todas =
        {
            new(IdPrimerosPasos, "Primeros pasos e inicio de sesión", false,
                "Qué es la aplicación, cómo entrar con cada rol y qué ve cada uno.",
                new[]
                {
                    "Qué es: la aplicación controla las salidas (préstamos) y entradas (devoluciones) de materiales del laboratorio. Todo se guarda en la computadora, en una base de datos cifrada.",
                    "Al abrir aparece «Iniciar sesión — Laboratorio de Electrónica» con dos opciones: «Usuario (registrar salidas/entradas)» y «Administrador (gestionar alumnos/materiales)».",
                    "Para operación diaria elige «Usuario» y pulsa «Entrar». No pide contraseña. Solo verás las pestañas «Operación» e «Historial», más este menú «Ayuda».",
                    "Para configurar el laboratorio elige «Administrador», escribe la contraseña y pulsa «Entrar» (o Enter). La primera vez la contraseña es admin123. Si ves el aviso de contraseña por defecto, cámbiala cuanto antes (menú Administración → Cambiar contraseña de administrador...).",
                    "Si la contraseña de administrador es incorrecta verás «Contraseña incorrecta.» en rojo: vuelve a escribirla con cuidado (distingue mayúsculas y minúsculas).",
                    "Arriba verás los menús «Sesión» (solo «Cerrar sesión»), «Administración» (solo administrador) y «Ayuda» (siempre). Para cambiar de rol usa «Sesión → Cerrar sesión» y vuelve a entrar con el otro rol.",
                }),
            new(IdSalida, "Registrar una salida (préstamo)", false,
                "Cómo prestar material con el escáner, paso a paso, sin usar el ratón si no quieres.",
                new[]
                {
                    "Ve a la pestaña «Operación». Arriba, en «Tipo de operación», deja marcado «Salida (préstamo)».",
                    "Haz clic en el cuadro grande de «Captura (escáner)». Todo lo que escanees o escribas va ahí. Pulsa Enter después de cada código.",
                    "PASO 1 — Alumno: escanea o escribe su credencial o número de cuenta y pulsa Enter. Abajo debe aparecer «Alumno: Nombre (cuenta)». Sin alumno no se puede continuar: si escaneas un material antes verás «Escanea primero la credencial del alumno.».",
                    "PASO 2 — Materiales: escanea cada material, uno por uno, con Enter entre cada uno. Cada lectura se suma a la «Lista de equipo a procesar». Si escaneas el MISMO material dos veces no se duplica: aumenta su «Cantidad: 2, 3…».",
                    "Materiales como fuente, generador u osciloscopio agregan solos 1 cable complementario. Puedes ajustarlos con el botón «Agregar cables...» en cualquier momento (cada material admitido muestra su propia cantidad).",
                    "Revisa la lista. Si te equivocaste pulsa «Cancelar / Limpiar todo» y empieza de nuevo con el mismo alumno.",
                    "PASO 3 — Enviar: con el cuadro de escaneo VACÍO pulsa Enter, o pulsa el botón grande «ACEPTAR». Todo el lote se guarda junto, con la misma fecha y hora.",
                    "Si todo salió bien verás en verde «✔ Se registraron N equipo(s) para el alumno …» y la lista se limpia sola. Si ves un aviso en rojo (por ejemplo sin stock), la lista SE CONSERVA para que corrijas y reintentes: no tienes que reescanear todo.",
                    "No escanees a otro alumno a mitad de una lista: verás un aviso de que ya hay una operación en curso. Confirma (Enter en blanco) o limpia antes de cambiar de alumno.",
                }),
            new(IdDevolucion, "Registrar una devolución (entrada)", false,
                "Cómo devolver material. Se descuenta primero lo más antiguo (FIFO).",
                new[]
                {
                    "Ve a la pestaña «Operación». En «Tipo de operación» marca «Entrada (devolución)».",
                    "Escanea primero al ALUMNO que devuelve (igual que en la salida) y luego cada MATERIAL que devuelve, con Enter entre cada uno.",
                    "Si el alumno devuelve solo parte de lo prestado, la fila activa conserva la cantidad restante y aparece además una fila «Devuelta» con lo devuelto. Es normal: así queda constancia de ambas partes.",
                    "Envía igual que la salida: Enter con el cuadro vacío o botón «ACEPTAR». Verás la confirmación en verde.",
                    "Si el material no tenía préstamo activo para ese alumno verás un aviso y no se registra nada. Revisa que el alumno y el código sean los correctos.",
                }),
            new(IdHistorial, "Consultar el historial", false,
                "Cómo buscar préstamos, entender los estados y expandir los grupos.",
                new[]
                {
                    "Ve a la pestaña «Historial». La tabla muestra: Alumno, Cuenta, Material, Código, Cantidad, Fecha salida, Fecha regreso y Estado («Activo» o «Devuelto»).",
                    "Usa el cuadro de búsqueda para filtrar por nombre, cuenta, material o código. Marca «Mostrar solo préstamos activos» para ocultar lo ya devuelto.",
                    "Puedes ordenar tocando cualquier encabezado (clic = ascendente/descendente). Sirve para ver lo más reciente primero.",
                    "Si un alumno tiene VARIOS préstamos activos del mismo material con distintas fechas, verás UNA fila resumen con una flecha «▶». Pulsa la flecha («▼») para expandir y ver cada préstamo por separado.",
                    "El historial NUNCA se borra solo. Si eres administrador y quieres depurarlo, lee la sección «Limpiar historial antiguo».",
                    "Si un alumno o material se eliminó del catálogo, sus filas del historial SIGUEN legibles (guardan el nombre, la cuenta y el código de ese momento).",
                }),
            new(IdSiAlgoFalla, "Si algo falla en Operación", false,
                "Qué hacer cuando el escáner no lee, un código no existe o no hay stock.",
                new[]
                {
                    "Si el escáner no lee, pulsa «Buscar manualmente…». Escribe parte del nombre o de la cuenta/código, elige el renglón correcto y se procesa EXACTAMENTE igual que un código escaneado.",
                    "Si ves «Código no reconocido en alumnos ni materiales», el código no existe en el catálogo: revisa que no falte un dígito o pide al administrador que lo registre en «Inventario» o «Alumnos».",
                    "Si ves un aviso de stock (no hay suficientes disponibles), devuelve antes material de ese tipo o pide al administrador que revise «Inventario → Disponible».",
                    "Si el cuadro no responde, haz clic dentro del cuadro de «Captura (escáner)» para devolverle el foco y sigue escaneando.",
                    "Ante cualquier error raro, la aplicación lo guarda en el archivo errores.log junto al programa. Anota qué hiciste y avisa al administrador.",
                }),
            new(IdInventario, "Gestionar el inventario (materiales)", true,
                "Solo administrador: dar de alta, editar, buscar y eliminar materiales.",
                new[]
                {
                    "Ve a la pestaña «Inventario». Verás la tabla con «Código de barras», «Nombre del elemento» y «Disponible».",
                    "Para BUSCAR escribe en el cuadro «Buscar:»: la tabla se filtra sola mientras escribes.",
                    "Para AGREGAR pulsa «Nuevo material». Escribe el código de barras (único, sin repetir), el nombre y la cantidad total. Guarda. La cantidad disponible empieza igual a la total.",
                    "Para CORREGIR selecciona la fila y pulsa «Editar». Si intentas repetir el código de otro material verás «Código duplicado».",
                    "Para ELIMINAR selecciona la fila y pulsa «Eliminar». Si el material tiene préstamos verás un aviso especial: SÍ se puede borrar, el historial conserva sus datos, pero el material desaparece del catálogo y ya no se puede prestar. Confirma solo si estás seguro.",
                }),
            new(IdAlumnos, "Gestionar alumnos", true,
                "Solo administrador: altas, edición, búsqueda y bajas de alumnos.",
                new[]
                {
                    "Ve a la pestaña «Alumnos». La tabla muestra «Nombre completo» y «Número de cuenta» (único, no se puede repetir).",
                    "Para BUSCAR escribe en «Buscar:». Para AGREGAR pulsa «Nuevo alumno» y captura nombre + número de cuenta.",
                    "Para CORREGIR selecciona la fila y pulsa «Editar». Si la cuenta ya la usa otro alumno verás «Cuenta duplicada».",
                    "Para ELIMINAR selecciona y pulsa «Eliminar». Igual que con materiales: un alumno CON historial SÍ se puede borrar (el historial conserva su nombre y cuenta), pero ya no se podrá prestar a su nombre. Lee el aviso antes de confirmar.",
                }),
            new(IdImportar, "Importar datos masivamente", true,
                "Solo administrador: cargar alumnos o materiales desde Excel, CSV, TXT, SQL o SQLite.",
                new[]
                {
                    "Ve a la pestaña «Importar datos». PASO 1: en «Tipo de datos a importar:» elige «Alumnos» o «Materiales».",
                    "PASO 2: pulsa «Seleccionar archivo e importar...» y elige el archivo (.xlsx, .xls, .csv, .txt, .sql, .db, .sqlite, .sqlite3).",
                    "PASO 3: la aplicación lee los encabezados y SUGIERE a qué campo va cada columna. Para alumnos los campos son Nombre y NumeroCuenta; para materiales son CodigoBarras, Nombre y CantidadTotal.",
                    "PASO 4: revisa el diálogo de mapeo («Mapeo de columnas»): corrige con las listas desplegables si alguna columna quedó mal asignada. Verás una vista previa de las primeras filas.",
                    "PASO 5: confirma. Al final verás «Insertados: N, Duplicados omitidos: M, Errores: K». Los duplicados (misma cuenta o mismo código) se OMITEN sin duplicar; las filas vacías o con cantidad inválida cuentan como errores.",
                    "Consejos: la primera fila debe ser el encabezado; evita celdas combinadas; la cantidad de materiales debe ser un número entero (si viene con decimales se trunca); guarda el CSV en UTF-8.",
                }),
            new(IdExportar, "Exportar datos y respaldos", true,
                "Solo administrador: sacar CSV/Excel, respetar el filtro del historial y hacer respaldos.",
                new[]
                {
                    "Ve a la pestaña «Exportar datos». Arriba, en «Exportar por tipo de dato», cada fila (Alumnos, Inventario, Historial) tiene botones «CSV...» y «Excel...». Elige carpeta y nombre al guardar.",
                    "Los archivos CSV/Excel salen en TEXTO PLANO (sin cifrar): cualquiera que los abra ve los datos. Guárdalos y compártelos con cuidado.",
                    "Para el HISTORIAL hay dos casillas: «Usar filtro de la pestaña Historial» (exporta justo lo que estás viendo ahí) y «Solo activos» (solo préstamos sin devolver).",
                    "En «Base de datos completa» tienes dos opciones: «Respaldo cifrado (.db)...» (copia tal cual, SIGUE cifrada: solo se abre con esta app EN ESTA MISMA computadora) y «Volcado completo (.xlsx, sin cifrar)...» (un Excel con una hoja por tabla; pide confirmación porque queda legible por cualquiera).",
                    "Abajo, en «Sincronización en la nube», están «Sincronizar ahora…», «Publicar catálogo…» y «Configurar…». Se explican en la sección «Conectar Google Sheets paso a paso».",
                    "El cuadro «Registro de exportaciones:» anota todo lo que exportaste en esta sesión.",
                }),
            new(IdLimpiarHistorial, "Limpiar historial antiguo", true,
                "Solo administrador: borrado manual y confirmado de devoluciones viejas.",
                new[]
                {
                    "Abre el menú «Administración → Limpiar historial antiguo...». Escribe cada cuántos días quieres conservar (por defecto 30).",
                    "La aplicación primero te dice CUÁNTOS préstamos devueltos con más de esos días se borrarían. Si dice 0, no hay nada que limpiar.",
                    "Lee la confirmación: «Se borrarán N préstamo(s)… Esta acción no se puede deshacer.» Solo pulsa «Sí» si estás seguro.",
                    "Reglas: los préstamos ACTIVOS nunca se tocan, y la limpieza local NO borra el respaldo en la nube (la nube nunca borra).",
                }),
            new(IdPasswordYPatron, "Contraseña y patrón de escaneo", true,
                "Solo administrador: cambiar la clave y ajustar qué se reconoce como número de cuenta.",
                new[]
                {
                    "Para CAMBIAR LA CONTRASEÑA abre «Administración → Cambiar contraseña de administrador...». Escribe la actual, luego la nueva dos veces, y guarda. Se guarda con hash y salt (nunca en texto plano).",
                    "Para el PATRÓN DE CUENTA abre «Administración → Configuración de escaneo...». Por defecto es ^\\d{6,12}$ (de 6 a 12 dígitos).",
                    "Cambia el patrón SOLO si los números de cuenta de tu escuela tienen otro formato. La ventana valida que sea una expresión regular válida antes de guardar.",
                    "Aunque el patrón no coincida, la aplicación igual busca el código en ambas tablas; y la búsqueda de cuenta perdona guiones (18458688 y 1845868-8 encuentran al mismo alumno).",
                }),
            new(IdGoogle, "Conectar Google Sheets paso a paso", true,
                "Solo administrador: vincular UN laboratorio con SU propia hoja de Google. Lee todo antes de tocar nada.",
                new[]
                {
                    "IDEA GENERAL: cada laboratorio tiene SU propia cuenta de Google, SU propia hoja de cálculo y SU propio script. La base de esta computadora manda; la nube es el buzón/respaldo. Sin URL y clave, la sincronización queda apagada y la app funciona igual en local.",
                    "QUÉ VAS A NECESITAR: una cuenta de Google del laboratorio, esta aplicación abierta como Administrador, y el script que está copiable al final de esta sección (botón «Copiar script»). No hay que instalar nada ni crear proyecto en la nube.",
                    "PASO 1 — Crear la hoja: entra a Google Drive con la cuenta del laboratorio, crea una hoja de cálculo VACÍA llamada por ejemplo «LabInventario - LAB-1» (cambia LAB-1 por tu laboratorio). Déjala vacía: el script crea las pestañas solo.",
                    "PASO 2 — Abrir el editor: dentro de ESA hoja abre «Extensiones → Apps Script». Es importante abrirlo DESDE la hoja (así el script queda vinculado a ella). Verás una pestaña de código vacía.",
                    "PASO 3 — Pegar el script: borra lo que haya en el editor, pulsa abajo «Copiar script», vuelve al editor y pégalo. Guarda (icono de disco o Ctrl+S).",
                    "PASO 4 — Primera ejecución: en la lista de funciones elige «configurar» y pulsa Ejecutar (▶). Google pedirá autorización: elige la cuenta del laboratorio, «Opciones avanzadas → Ir a (proyecto)» y «Permitir». Esto crea la clave (CLAVE) y las pestañas Historial, Alumnos e Inventario con sus encabezados.",
                    "PASO 5 — Desplegar: pulsa «Desplegar → Nueva implementación», tipo «Aplicación web», «Ejecutar como: Yo», «Quién tiene acceso: Cualquiera». Pulsa «Desplegar», autoriza si lo pide, y COPIA la «URL de la aplicación web» (termina en /exec). Guárdala: es la dirección de TU laboratorio.",
                    "PASO 6 — Copiar la clave: en el editor abre «⚙️ Configuración del proyecto → Propiedades del script». Copia el valor de «CLAVE». Es la contraseña entre la app y tu script: no la compartas fuera del laboratorio.",
                    "CASO ESPECIAL (script suelto): si en vez de crearlo dentro de la hoja lo creaste en script.google.com, guarda además el ID de la hoja en la propiedad «SPREADSHEET_ID» (lo que va entre /d/ y /edit en la dirección de la hoja).",
                    "PASO 7 — Conectar cada computadora: en la app, como Administrador, abre «Administración → Configuración de sincronización…». Escribe el «Identificador de la computadora» (por ejemplo LAB-1, LAB-2: uno distinto por PC), el «Nombre del laboratorio», pega la «URL del script» y la «Clave del script», y deja «Revisar la nube cada (minutos)» en 5.",
                    "PASO 8 — Probar: pulsa «Probar conexión…». Si ves «Conexión correcta: el script respondió y las pestañas están listas.» ya quedó. Si ves «Falta la URL o la clave» o «Sin configurar», revisa que pegaste AMBOS campos y pulsa «Guardar».",
                    "PASO 9 — Primera publicación: ve a «Exportar datos → Publicar catálogo…» y confirma. Esto COPIA tus Alumnos e Inventario actuales a la nube, tal cual. Hazlo una vez al vincular, y cada vez que quieras forzar que la hoja quede igual a esta PC.",
                    "USO DIARIO: al cerrar la app el historial se sube solo (upsert por PrestamoId: lo nuevo se agrega, las devoluciones corrigen su fila, nunca duplica ni borra). Cada N minutos (y con «Sincronizar ahora…») la app revisa si alguien editó Alumnos/Inventario DIRECTO en la hoja y te muestra un diálogo con casillas: lo MARCADO se aplica aquí, lo NO marcado se RESTAURA en la hoja con tus datos, y «Cancelar» no toca nada.",
                    "REGLAS DEL REVISOR (quien edita la hoja): nunca vacíes una pestaña completa; para dar de baja quita la fila (la app la propone y aquí se confirma); lo rechazado vuelve a aparecer hasta aplicarse o quitarse de común acuerdo; la pestaña Historial NO se edita a mano (la mantiene la app).",
                    "AVISOS NORMALES: tras actualizar el script, la primera sincronización reporta varias filas «actualizadas» (puesta al día del formato a texto plano) y luego se estabiliza en cero. Si propone MUCHAS bajas (5 o más y más del 25 % del catálogo) verás el guardián «Posible vaciado accidental»: cancela y usa «Publicar catálogo» si fue un error.",
                    "SI FALLA: sin internet queda en cola y el próximo intento se autocorrige. Revisa URL (debe terminar en /exec), CLAVE, acceso del despliegue en «Cualquiera», e intervalo. Los errores quedan en errores.log junto al programa.",
                }),
            new(IdAcercaDe, "Acerca de y soporte", false,
                "Versión, tecnología y dónde pedir ayuda.",
                new[]
                {
                    "Aplicación: LabInventario — Gestión de Salidas y Entradas del Laboratorio de Electrónica (C# + .NET 8 + Avalonia). Base local SQLite cifrada (SQLCipher AES-256) asociada a la máquina.",
                    "Roles: Usuario opera (Operación, Historial, Ayuda); Administrador además gestiona catálogos, importa/exporta, limpia historial y configura escaneo y nube.",
                    "Soporte: anota qué pestaña y botón usaste, el mensaje exacto que viste y la hora; adjunta errores.log (está junto al programa) y avisa al administrador del laboratorio.",
                }),
        };

        /// <summary>
        /// Secciones visibles para el rol actual. Usuario ve solo las
        /// generales; Administrador las ve todas.
        /// </summary>
        public static IReadOnlyList<SeccionAyuda> Para(bool esAdministrador) =>
            esAdministrador ? Todas : Todas.Where(s => !s.SoloAdmin).ToList();

        /// <summary>Busca una sección por su identificador estable.</summary>
        public static SeccionAyuda? PorId(string id) =>
            Todas.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// Lee el texto del script de sincronización embebido en el
        /// ejecutable (recurso apps-script/Codigo.gs). Si por alguna razón
        /// el recurso no se encuentra, devuelve una cadena vacía en vez de
        /// lanzar: el diálogo muestra entonces un aviso para copiarlo desde
        /// el repositorio.
        /// </summary>
        public static string LeerScriptSincronizacion()
        {
            try
            {
                var ensamblado = typeof(AyudaContenido).Assembly;
                var nombre = ensamblado.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Codigo.gs", StringComparison.Ordinal));
                if (nombre is null) return string.Empty;
                using var flujo = ensamblado.GetManifestResourceStream(nombre);
                if (flujo is null) return string.Empty;
                using var lector = new StreamReader(flujo);
                return lector.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
