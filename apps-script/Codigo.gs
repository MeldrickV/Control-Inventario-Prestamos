/**
 * LabInventario — Puente de sincronización (Google Apps Script).
 *
 * INSTALACIÓN (una vez por laboratorio, ~5 minutos):
 *  1. Con la cuenta Google del laboratorio, abrir script.google.com → proyecto nuevo → pegar este archivo → guardar.
 *  2. Ejecutar UNA vez la función configurar() (crea el spreadsheet con sus
 *     4 pestañas y guarda la clave inicial; anótala).
 *  3. Desplegar → Nueva implementación → tipo "App web" → Ejecutar como: yo →
 *     Acceso: cualquiera → Desplegar → autorizar una sola vez.
 *  4. Copiar la URL /exec. En la app (admin): pegarla en "URL del script" junto
 *     con la clave → Probar conexión → listo.
 *
 * PROTOCOLO (la app solo hace GET/POST con {clave, accion, ...}):
 *  - GET  ?clave=&accion=ping                                   → {ok:true}
 *  - GET  ?clave=&accion=leer&pestana=Alumnos                   → {ok:true, valores:[[...]]}
 *  - POST {clave, accion:"agregar", pestana, filas}             → {ok:true, confirmadas:N}
 *  - POST {clave, accion:"reescribir", pestana, filas}          → {ok:true} (vacía y escribe desde A1)
 *  - POST {clave, accion:"asegurar"}                            → {ok:true} (crea pestañas+encabezados si faltan)
 *
 * SEGURIDAD: cada petición debe traer la clave guardada en las Propiedades
 * del script (la pone configurar(); cámbiala ahí y en la app). Sin clave
 * correcta se responde error genérico sin revelar nada.
 */

var PESTANAS = {
  Historial: ['PrestamoId', 'Alumno', 'NumeroCuenta', 'Material', 'CodigoBarras', 'Cantidad', 'CablesExtra', 'FechaSalida', 'FechaRegreso', 'Estado', 'SyncId'],
  Alumnos: ['Nombre', 'NumeroCuenta'],
  Inventario: ['Nombre', 'CodigoBarras', 'CantidadTotal'],
  Cambios: null // espejo de Alumnos+Inventario para propuestas del revisor (ver abajo)
};

var HOJAS_CAMBIOS = ['Alumnos', 'Inventario'];

function configurar() {
  var props = PropertiesService.getScriptProperties();
  if (!props.getProperty('CLAVE')) {
    props.setProperty('CLAVE', 'CAMBIA-ESTA-CLAVE-' + Math.random().toString(36).slice(2, 10));
  }
  asegurarEstructura();
}

function doGet(e) {
  return responder(function () {
    var p = (e && e.parameter) || {};
    verificarClave(p.clave);
    var accion = p.accion || '';
    if (accion === 'ping') return { app: 'LabInventario' };
    if (accion === 'leer') return { valores: leerPestana(p.pestana) };
    throw new Error('Acción no soportada: ' + accion);
  });
}

function doPost(e) {
  return responder(function () {
    var cuerpo = JSON.parse((e && e.postData && e.postData.contents) || '{}');
    verificarClave(cuerpo.clave);
    var accion = cuerpo.accion || '';
    var pestana = cuerpo.pestana || '';
    var filas = cuerpo.filas || [];
    // Escrituras bajo candado: dos PCs no se pisan entre sí.
    var candado = LockService.getScriptLock();
    candado.waitLock(15000);
    try {
      if (accion === 'agregar') return { confirmadas: agregarFilas(pestana, filas) };
      if (accion === 'reescribir') { reescribirPestana(pestana, filas); return {}; }
      if (accion === 'asegurar') { asegurarEstructura(); return {}; }
    } finally {
      candado.releaseLock();
    }
    throw new Error('Acción no soportada: ' + accion);
  });
}

// ---------------- Internos ----------------

function responder(fn) {
  try {
    var datos = fn() || {};
    datos.ok = true;
    return ContentService.createTextOutput(JSON.stringify(datos)).setMimeType(ContentService.MimeType.JSON);
  } catch (err) {
    return ContentService.createTextOutput(JSON.stringify({ ok: false, error: String((err && err.message) || err) }))
      .setMimeType(ContentService.MimeType.JSON);
  }
}

function verificarClave(clave) {
  var esperada = PropertiesService.getScriptProperties().getProperty('CLAVE') || '';
  if (!esperada || clave !== esperada) throw new Error('No autorizado.');
}

function libro() {
  return SpreadsheetApp.getActiveSpreadsheet();
}

function asegurarEstructura() {
  var ss = libro();
  Object.keys(PESTANAS).forEach(function (nombre) {
    if (nombre === 'Cambios') return; // Cambios usa las mismas pestañas de catálogo
    var hoja = ss.getSheetByName(nombre) || ss.insertSheet(nombre);
    if (hoja.getLastRow() === 0) {
      hoja.getRange(1, 1, 1, PESTANAS[nombre].length).setValues([PESTANAS[nombre]]);
    }
  });
  // El archivo de Cambios es el MISMO spreadsheet: pestañas Alumnos/Inventario
  // con el contenido que el revisor edita en su lugar. Si están vacías (solo
  // encabezado o nada), se dejan con encabezado para que la app las lea.
  HOJAS_CAMBIOS.forEach(function (nombre) {
    var hoja = ss.getSheetByName('Cambios_' + nombre) || ss.insertSheet('Cambios_' + nombre);
    if (hoja.getLastRow() === 0) {
      hoja.getRange(1, 1, 1, PESTANAS[nombre].length).setValues([PESTANAS[nombre]]);
    }
  });
}

function nombreReal(pestana) {
  // La app pide "Alumnos"/"Inventario" para el archivo de Cambios con el
  // prefijo "Cambios_"; el resto de pestañas van tal cual.
  if (pestana === 'Cambios_Alumnos' || pestana === 'Cambios_Inventario') return pestana;
  return pestana;
}

function leerPestana(pestana) {
  var hoja = libro().getSheetByName(nombreReal(pestana));
  if (!hoja || hoja.getLastRow() === 0) return [];
  var valores = hoja.getDataRange().getValues();
  return valores.map(function (fila) {
    return fila.map(function (celda) { return celda === null || celda === undefined ? '' : String(celda); });
  });
}

function agregarFilas(pestana, filas) {
  if (!filas || filas.length === 0) return 0;
  var hoja = libro().getSheetByName(nombreReal(pestana)) || libro().insertSheet(nombreReal(pestana));
  var inicio = hoja.getLastRow() + 1;
  hoja.getRange(inicio, 1, filas.length, filas[0].length).setValues(filas);
  return filas.length;
}

function reescribirPestana(pestana, filas) {
  var hoja = libro().getSheetByName(nombreReal(pestana)) || libro().insertSheet(nombreReal(pestana));
  hoja.clearContents();
  if (filas && filas.length > 0) {
    hoja.getRange(1, 1, filas.length, filas[0].length).setValues(filas);
  }
}
