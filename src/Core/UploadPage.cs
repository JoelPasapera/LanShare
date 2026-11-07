using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Pagina de /__subir: lo que ve el movil para enviar archivos al PC.
    ///
    /// Autocontenida (sin recursos externos) porque se sirve desde cualquier
    /// sitio, tenga o no conexion a internet. Envia los archivos de uno en uno:
    /// en paralelo competirian por el mismo Wi-Fi sin terminar antes, y un
    /// fallo a medias dejaria varios archivos incompletos en vez de uno.
    ///
    /// La plantilla no lleva comillas dobles para poder vivir en una cadena
    /// literal sin escapes.
    /// </summary>
    public static class UploadPage
    {
        public static string Render(long maxBytes)
        {
            return Template
                .Replace("{{MAX_BYTES}}", maxBytes.ToString(CultureInfo.InvariantCulture))
                .Replace("{{MAX_TEXT}}", HumanSize(maxBytes));
        }

        /// <summary>Pagina de resultado para el formulario sin JavaScript.</summary>
        public static string RenderResult(string summary, bool ok)
        {
            return "<!DOCTYPE html><html lang='es'><head><meta charset='utf-8'>" +
                   "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
                   "<title>LanShare</title></head><body style='font-family:system-ui,sans-serif;padding:24px'>" +
                   "<h1 style='font-size:20px'>" + (ok ? "Enviado" : "No se pudo enviar") + "</h1>" +
                   "<p>" + WebUtility.HtmlEncode(summary) + "</p>" +
                   "<p><a href='" + UploadHandler.Endpoint + "'>Enviar mas archivos</a></p></body></html>";
        }

        public static string HumanSize(long bytes)
        {
            return ByteSize.Format(bytes);
        }

        private const string Template =
@"<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Enviar al PC - LanShare</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--text:#1d232b;--muted:#5b6573;--line:#dfe3e8;--accent:#1f6feb;--ok:#1a7f37;--err:#c62828}
@media (prefers-color-scheme:dark){:root{--bg:#0f1318;--card:#171c22;--text:#e6e9ee;--muted:#9aa4b1;--line:#2a313a;--accent:#4c8dff;--ok:#3fb950;--err:#ff6b6b}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:16px/1.45 system-ui,-apple-system,Segoe UI,Roboto,sans-serif}
main{max-width:560px;margin:0 auto;padding:20px 16px 40px}
h1{font-size:22px;margin:4px 0 6px}
.sub{color:var(--muted);font-size:14px;margin:0 0 18px}
.zone{display:flex;flex-direction:column;align-items:center;gap:10px;padding:28px 16px;border:2px dashed var(--line);border-radius:14px;background:var(--card);cursor:pointer;text-align:center}
.zone.over{border-color:var(--accent)}
.zone svg{width:38px;height:38px;stroke:var(--accent)}
.btn{display:inline-block;background:var(--accent);color:#fff;border-radius:10px;padding:12px 22px;font-weight:600}
input[type=file]{position:absolute;width:1px;height:1px;opacity:0}
ul{list-style:none;margin:18px 0 0;padding:0}
li{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px 14px;margin-bottom:10px}
.row{display:flex;justify-content:space-between;gap:12px}
.name{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500}
.size{color:var(--muted);font-size:13px;white-space:nowrap}
.bar{height:6px;background:var(--line);border-radius:3px;margin-top:9px;overflow:hidden}
.fill{height:100%;width:0;background:var(--accent);transition:width .2s}
.state{font-size:13px;color:var(--muted);margin-top:6px}
li.ok .fill{background:var(--ok)}li.ok .state{color:var(--ok)}
li.err .fill{background:var(--err)}li.err .state{color:var(--err)}
.foot{color:var(--muted);font-size:13px;margin-top:22px;text-align:center}
</style>
</head>
<body>
<main>
<h1>Enviar al PC</h1>
<p class='sub'>Hasta {{MAX_TEXT}} por archivo. Se guardan en el PC sin reemplazar nada de lo que ya hay.</p>
<label class='zone' id='zona' for='archivos'>
<svg viewBox='0 0 24 24' fill='none' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'><path d='M12 16V4M7 9l5-5 5 5M4 16v3a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-3'/></svg>
<span class='btn'>Elegir archivos</span>
<span class='sub' style='margin:0'>o arrastralos aqui</span>
</label>
<input type='file' id='archivos' multiple>
<ul id='lista'></ul>
<p class='foot' id='resumen'></p>
<noscript>
<form method='post' enctype='multipart/form-data'>
<p><input type='file' name='archivo' multiple style='position:static;width:auto;height:auto;opacity:1'></p>
<p><button type='submit'>Enviar</button></p>
</form>
</noscript>
</main>
<script>
// @ts-check
(function () {
  'use strict';

  /** Tope por archivo, el mismo que aplica el servidor. */
  var MAX = {{MAX_BYTES}};

  var input = /** @type {HTMLInputElement} */ (document.getElementById('archivos'));
  var list = /** @type {HTMLElement} */ (document.getElementById('lista'));
  var zone = /** @type {HTMLElement} */ (document.getElementById('zona'));
  var summary = /** @type {HTMLElement} */ (document.getElementById('resumen'));

  /** @type {Array<{file: File, row: HTMLElement}>} */
  var queue = [];
  var busy = false;
  var sent = 0;
  var failed = 0;

  /**
   * @param {number} bytes
   * @returns {string}
   */
  function human(bytes) {
    var units = ['B', 'KB', 'MB', 'GB'];
    var i = 0;
    while (bytes >= 1024 && i < units.length - 1) { bytes /= 1024; i++; }
    return (i === 0 ? String(bytes) : bytes.toFixed(1)) + ' ' + units[i];
  }

  /**
   * @param {string} tag
   * @param {string} cls
   * @param {string} text
   * @returns {HTMLElement}
   */
  function el(tag, cls, text) {
    var node = document.createElement(tag);
    node.className = cls;
    node.textContent = text;
    return node;
  }

  /**
   * @param {HTMLElement} row
   * @param {string} kind
   * @param {string} text
   */
  function finish(row, kind, text) {
    row.className = kind;
    var fill = /** @type {HTMLElement} */ (row.querySelector('.fill'));
    var state = /** @type {HTMLElement} */ (row.querySelector('.state'));
    fill.style.width = '100%';
    state.textContent = text;
    if (kind === 'ok') sent++; else failed++;
    summary.textContent = sent + (sent === 1 ? ' enviado' : ' enviados') +
      (failed ? ' \u00b7 ' + failed + (failed === 1 ? ' con error' : ' con errores') : '');
  }

  /** @param {File} file */
  function enqueue(file) {
    var row = el('li', '', '');
    var head = el('div', 'row', '');
    head.appendChild(el('span', 'name', file.name));
    head.appendChild(el('span', 'size', human(file.size)));
    var bar = el('div', 'bar', '');
    bar.appendChild(el('div', 'fill', ''));
    row.appendChild(head);
    row.appendChild(bar);
    row.appendChild(el('div', 'state', 'En cola'));
    list.appendChild(row);

    if (file.size > MAX) {
      finish(row, 'err', 'Supera el maximo de ' + human(MAX));
      return;
    }
    queue.push({ file: file, row: row });
  }

  function next() {
    if (busy || queue.length === 0) return;
    busy = true;

    var item = /** @type {{file: File, row: HTMLElement}} */ (queue.shift());
    var fill = /** @type {HTMLElement} */ (item.row.querySelector('.fill'));
    var state = /** @type {HTMLElement} */ (item.row.querySelector('.state'));
    state.textContent = 'Enviando...';

    var form = new FormData();
    form.append('archivo', item.file, item.file.name);

    var xhr = new XMLHttpRequest();
    xhr.open('POST', location.pathname);
    xhr.setRequestHeader('X-LanShare-Upload', '1');
    xhr.upload.onprogress = function (e) {
      if (!e.lengthComputable) return;
      var pct = Math.floor(e.loaded * 100 / e.total);
      fill.style.width = pct + '%';
      state.textContent = 'Enviando... ' + pct + '%';
    };
    xhr.onload = function () {
      /** @type {{ok?: boolean, error?: string, files?: Array<{saved: string}>}} */
      var answer = {};
      try { answer = JSON.parse(xhr.responseText); } catch (e) { answer = {}; }
      if (xhr.status === 200 && answer.ok) {
        var saved = answer.files && answer.files.length ? answer.files[0].saved : item.file.name;
        finish(item.row, 'ok', saved === item.file.name ? 'Guardado' : 'Guardado como ' + saved);
      } else {
        finish(item.row, 'err', answer.error || ('Error ' + xhr.status));
      }
      busy = false;
      next();
    };
    xhr.onerror = function () {
      finish(item.row, 'err', 'Sin conexion con el PC');
      busy = false;
      next();
    };
    xhr.send(form);
  }

  /** @param {FileList | null} files */
  function add(files) {
    if (!files) return;
    for (var i = 0; i < files.length; i++) enqueue(files[i]);
    next();
  }

  input.addEventListener('change', function () {
    add(input.files);
    input.value = '';
  });

  ['dragenter', 'dragover'].forEach(function (type) {
    zone.addEventListener(type, function (e) { e.preventDefault(); zone.classList.add('over'); });
  });
  ['dragleave', 'drop'].forEach(function (type) {
    zone.addEventListener(type, function (e) { e.preventDefault(); zone.classList.remove('over'); });
  });
  zone.addEventListener('drop', function (e) {
    add(e.dataTransfer ? e.dataTransfer.files : null);
  });
})();
</script>
</body>
</html>";
    }
}
