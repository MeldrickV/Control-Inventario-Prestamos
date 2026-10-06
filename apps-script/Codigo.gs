var PESTANAS = {
  Historial: ['PrestamoId', 'Alumno', 'NumeroCuenta', 'Material', 'CodigoBarras', 'Cantidad', 'CablesExtra', 'FechaSalida', 'FechaRegreso', 'Estado', 'SyncId'],
  Alumnos: ['Nombre', 'NumeroCuenta'],
  Inventario: ['Nombre', 'CodigoBarras', 'CantidadTotal']
};

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
      if (accion === 'sincronizarHistorial') return sincronizarHistorial(pestana, filas, cuerpo.encabezado);
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
  var ss = null;
  try { ss = SpreadsheetApp.getActiveSpreadsheet(); } catch (e) { ss = null; }
  if (ss) return ss;
  // Script standalone (creado en script.google.com): se abre la hoja por ID.
  var id = PropertiesService.getScriptProperties().getProperty('SPREADSHEET_ID') || '';
  if (!id) {
    throw new Error('El script no está vinculado a ninguna hoja: créalo desde ' +
      'Extensiones → Apps Script dentro del spreadsheet del laboratorio, o guarda ' +
      'el ID en la propiedad SPREADSHEET_ID.');
  }
  return SpreadsheetApp.openById(id);
}

function asegurarEstructura() {
  // Catálogo definitivo compartido: el revisor edita directo en
  // Alumnos/Inventario. Sin pestañas de propuestas.
  var ss = libro();
  Object.keys(PESTANAS).forEach(function (nombre) {
    var hoja = ss.getSheetByName(nombre) || ss.insertSheet(nombre);
    if (hoja.getLastRow() === 0) {
      escribirTexto(hoja, [PESTANAS[nombre]]);
    }
  });
}

// Escribe filas como TEXTO PLANO (formato '@' antes de los valores): evita
// que Sheets convierta fechas ("2026-09-15 08:00:00" → Date) o números
// largos, lo que hacía que todo "cambiara" en cada sincronización.
function escribirTexto(hoja, filas) {
  if (!filas || filas.length === 0) return;
  var columnas = filas[0].length;
  hoja.getRange(1, 1, filas.length, columnas).setNumberFormat('@');
  hoja.getRange(1, 1, filas.length, columnas).setValues(filas);
}

function agregarTexto(hoja, inicio, filas) {
  if (!filas || filas.length === 0) return;
  var columnas = filas[0].length;
  hoja.getRange(inicio, 1, filas.length, columnas).setNumberFormat('@');
  hoja.getRange(inicio, 1, filas.length, columnas).setValues(filas);
}

function nombreReal(pestana) {
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
  agregarTexto(hoja, inicio, filas);
  return filas.length;
}

function reescribirPestana(pestana, filas) {
  var hoja = libro().getSheetByName(nombreReal(pestana)) || libro().insertSheet(nombreReal(pestana));
  hoja.clearContents();
  if (filas && filas.length > 0) {
    escribirTexto(hoja, filas);
  }
}

// Sincroniza el historial completo: por cada fila busca su PrestamoId
// (columna A); si existe y cambió → actualiza, si no existe → agrega, y
// nunca borra (la purga local no toca el respaldo). Compara columnas 0-9;
// SyncId (col 10) se regenera en cada envío y se ignora. Todo se escribe
// en una sola operación al final.
function sincronizarHistorial(pestana, filas, encabezado) {
  var head = (encabezado && encabezado.length > 0)
    ? encabezado.map(String)
    : PESTANAS['Historial'];
  var hoja = libro().getSheetByName(nombreReal(pestana)) || libro().insertSheet(nombreReal(pestana));
  var actuales = hoja.getLastRow() === 0 ? [] : hoja.getDataRange().getValues();
  var grilla = [];
  var indice = {}; // PrestamoId (texto) -> posición en grilla
  var inicio = 0;
  if (actuales.length > 0 && actuales[0].map(String).join('|') === head.join('|')) inicio = 1;
  for (var i = inicio; i < actuales.length; i++) {
    var previa = normalizarFila(actuales[i], head.length);
    grilla.push(previa);
    if (previa[0] !== '') indice[previa[0]] = grilla.length - 1;
  }
  var agregadas = 0, actualizadas = 0;
  (filas || []).forEach(function (f) {
    var fila = normalizarFila(f, head.length);
    if (fila[0] === '') return;
    if (indice.hasOwnProperty(fila[0])) {
      if (!igualesSinSync(grilla[indice[fila[0]]], fila)) {
        grilla[indice[fila[0]]] = fila;
        actualizadas++;
      }
    } else {
      indice[fila[0]] = grilla.length;
      grilla.push(fila);
      agregadas++;
    }
  });
  var salida = [head].concat(grilla);
  hoja.clearContents();
  escribirTexto(hoja, salida);
  return { agregadas: agregadas, actualizadas: actualizadas };
}

function normalizarFila(fila, n) {
  var r = (fila || []).map(String);
  while (r.length < n) r.push('');
  return r.slice(0, n);
}

function igualesSinSync(a, b) {
  for (var c = 0; c < 10; c++) {
    var x = a.length > c ? a[c] : '';
    var y = b.length > c ? b[c] : '';
    if (x !== y) return false;
  }
  return true;
}
