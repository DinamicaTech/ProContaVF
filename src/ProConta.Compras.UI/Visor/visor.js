// Visor de PDF de compras (compras/pantallas). La pantalla le envía mensajes por WebView2:
//   { tipo: "abrir", datos: "<base64>", nombre }  abre el PDF
//   { tipo: "resaltar", valores: [{ campo, texto }], activo, puntos: { campo: { pagina, x, y } } }  marca dónde aparece cada
//     valor; en verde, la aparición usada: la del punto del campo (elegida o la que lee la plantilla) o, si solo hay una, esa
//   { tipo: "zoom", factor }  1 = ajustar al ancho
// y le envía { tipo: "seleccion", texto, punto } al soltar el ratón con texto seleccionado, y { tipo: "elegir", campos, punto }
// al hacer clic en un valor resaltado (para elegir de cuál de sus apariciones aprende la plantilla). El punto va en
// coordenadas del PDF (página, x, y desde abajo a la izquierda), las mismas que usa el reconocimiento.
import * as pdfjsLib from "./pdfjs/pdf.min.js";

pdfjsLib.GlobalWorkerOptions.workerSrc = "./pdfjs/pdf.worker.min.js";

const contenedor = document.getElementById("paginas");
const mensaje = document.getElementById("mensaje");
let documento = null;
let zoom = 1;
let valores = [];
let activo = null;
let puntos = {};
let generacion = 0;
const vistas = [];

const host = window.chrome && window.chrome.webview;
const enviar = (m) => host && host.postMessage(m);

function avisar(texto) {
  mensaje.textContent = texto || "";
  mensaje.hidden = !texto;
}

function base64ABytes(b64) {
  const binario = atob(b64);
  const bytes = new Uint8Array(binario.length);
  for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i);
  return bytes;
}

async function abrir(datos) {
  const mia = ++generacion;
  contenedor.replaceChildren();
  if (documento) { documento.destroy(); documento = null; }
  if (!datos) { avisar("Elige una factura con PDF."); return; }
  avisar("Abriendo el PDF…");
  try {
    const doc = await pdfjsLib.getDocument({
      data: base64ABytes(datos),
      cMapUrl: "./pdfjs/cmaps/", cMapPacked: true,
      standardFontDataUrl: "./pdfjs/standard_fonts/",
      isEvalSupported: false
    }).promise;
    if (mia !== generacion) { doc.destroy(); return; }
    documento = doc;
    avisar("");
    await pintar(mia);
  } catch (e) {
    if (mia === generacion) avisar("No he podido abrir el PDF: " + (e && e.message ? e.message : e));
  }
}

async function pintar(mia) {
  if (!documento) return;
  contenedor.replaceChildren();
  const ancho = Math.max(200, document.documentElement.clientWidth - 32);
  for (let n = 1; n <= documento.numPages; n++) {
    const pagina = await documento.getPage(n);
    if (mia !== generacion) return;
    const base = pagina.getViewport({ scale: 1 });
    const escala = (ancho / base.width) * zoom;
    const vista = pagina.getViewport({ scale: escala });
    const ppp = window.devicePixelRatio || 1;

    const div = document.createElement("div");
    div.className = "pagina";
    div.dataset.pagina = n;
    vistas[n] = vista;
    div.style.setProperty("--scale-factor", escala);
    div.style.width = vista.width + "px";
    div.style.height = vista.height + "px";
    const lienzo = document.createElement("canvas");
    lienzo.width = Math.floor(vista.width * ppp);
    lienzo.height = Math.floor(vista.height * ppp);
    lienzo.style.width = vista.width + "px";
    lienzo.style.height = vista.height + "px";
    const capa = document.createElement("div");
    capa.className = "textLayer";
    div.append(lienzo, capa);
    contenedor.append(div);

    await pagina.render({ canvasContext: lienzo.getContext("2d"), viewport: vista, transform: ppp !== 1 ? [ppp, 0, 0, ppp, 0, 0] : null }).promise;
    const capaTexto = new pdfjsLib.TextLayer({ textContentSource: await pagina.getTextContent(), container: capa, viewport: vista });
    await capaTexto.render();
    const fin = document.createElement("div");
    fin.className = "endOfContent";
    capa.append(fin);
    capa.addEventListener("mousedown", () => capa.classList.add("selecting"));
    if (mia !== generacion) return;
  }
  marcar();
}

// Compara sin espacios, sin puntos de miles ni guiones y sin distinguir mayúsculas.
const normalizar = (t) => (t || "").normalize("NFKD").replace(/[̀-ͯ]/g, "").replace(/[\s.\-\/]/g, "").toUpperCase();

// Un valor puede estar partido en varios trozos de texto («10», «de», «julio»…): se busca también en grupos de hasta seis
// trozos seguidos de la misma página y se marcan solo los del grupo más corto que lo contiene.
function marcar() {
  const capas = contenedor.querySelectorAll(".textLayer");
  contenedor.querySelectorAll(".textLayer span").forEach((s) => { s.classList.remove("origen", "activo", "elegido"); s.removeAttribute("title"); delete s.dataset.campos; delete s.dataset.grupo; });
  let grupo = 0;
  let primero = null;
  for (const capa of capas) {
    const spans = Array.from(capa.querySelectorAll("span")).filter((s) => !s.classList.contains("markedContent"));
    const textos = spans.map((s) => normalizar(s.textContent));
    for (const v of valores) {
      const buscado = normalizar(v.texto);
      if (buscado.length < 2) continue;
      for (let i = 0; i < spans.length; i++) {
        if (textos[i].length === 0) continue;
        let acumulado = "";
        for (let j = i; j < Math.min(spans.length, i + 6); j++) {
          acumulado += textos[j];
          if (!acumulado.includes(buscado)) { if (acumulado.length > buscado.length + 40) break; continue; }
          // El grupo es mínimo si sin el primer trozo ya no contiene el valor.
          if (j > i && acumulado.slice(textos[i].length).includes(buscado)) break;
          grupo++;
          for (let k = i; k <= j; k++) {
            spans[k].classList.add("origen");
            spans[k].title = v.campo + " · clic para que la plantilla aprenda de aquí";
            const campos = (spans[k].dataset.campos || "").split(",").filter((c) => c);
            if (!campos.includes(v.campo)) campos.push(v.campo);
            spans[k].dataset.campos = campos.join(",");
            spans[k].dataset.grupo = v.campo + ":" + grupo;
            if (v.campo === activo) { spans[k].classList.add("activo"); primero = primero || spans[k]; }
          }
          break;
        }
      }
    }
  }
  marcarUsadas();
  if (primero) primero.scrollIntoView({ block: "center", behavior: "smooth" });
}

// Distancia en pantalla del centro de un trozo de texto al punto del PDF (en su página), o Infinity si es de otra página.
function distancia(span, p) {
  const div = span.closest(".pagina");
  if (!div || Number(div.dataset.pagina) !== p.pagina || !vistas[p.pagina]) return Infinity;
  const [vx, vy] = vistas[p.pagina].convertToViewportPoint(p.x, p.y);
  const r = span.getBoundingClientRect(), d = div.getBoundingClientRect();
  return Math.hypot(r.left - d.left + r.width / 2 - vx, r.top - d.top + r.height / 2 - vy);
}

// En verde la aparición usada de cada campo (compras/pantallas 2026-11-07): la más cercana a su punto, si lo hay y cae
// cerca (40 px); si no, la única aparición. Con varias y sin punto, quedan todas en ámbar para elegir con un clic.
function marcarUsadas() {
  const grupos = {};
  contenedor.querySelectorAll(".textLayer span.origen").forEach((s) => {
    const g = s.dataset.grupo;
    if (!g) return;
    const campo = g.split(":")[0];
    (grupos[campo] = grupos[campo] || {})[g] = (grupos[campo][g] || []).concat(s);
  });
  for (const campo of Object.keys(grupos)) {
    const lista = Object.entries(grupos[campo]);
    let elegido = null;
    const p = puntos && puntos[campo];
    if (p) {
      let mejor = Infinity;
      for (const [g, spans] of lista) {
        const d = Math.min(...spans.map((s) => distancia(s, p)));
        if (d < mejor) { mejor = d; elegido = g; }
      }
      if (mejor > 40) elegido = null;
    }
    if (!elegido && lista.length === 1) elegido = lista[0][0];
    if (elegido) grupos[campo][elegido].forEach((s) => s.classList.add("elegido"));
  }
}

let temporizadorAviso = null;
function mostrarAviso(texto) {
  const aviso = document.getElementById("aviso");
  aviso.textContent = texto;
  aviso.classList.add("visible");
  clearTimeout(temporizadorAviso);
  temporizadorAviso = setTimeout(() => aviso.classList.remove("visible"), 1400);
}

// Clic derecho con texto seleccionado: lo copia al portapapeles (Ctrl+C también funciona).
document.addEventListener("contextmenu", (e) => {
  e.preventDefault();
  const texto = (window.getSelection() || "").toString().trim();
  if (!texto) return;
  navigator.clipboard.writeText(texto).then(() => mostrarAviso("Copiado"), () => { document.execCommand("copy"); mostrarAviso("Copiado"); });
});

// Punto del PDF en el centro de un rectángulo de la pantalla, o null si no cae en una página.
function puntoPdf(rect) {
  const cx = rect.left + rect.width / 2, cy = rect.top + rect.height / 2;
  for (const div of contenedor.querySelectorAll(".pagina")) {
    const r = div.getBoundingClientRect();
    if (cx < r.left || cx > r.right || cy < r.top || cy > r.bottom) continue;
    const n = Number(div.dataset.pagina);
    const [x, y] = vistas[n].convertToPdfPoint(cx - r.left, cy - r.top);
    return { pagina: n, x, y };
  }
  return null;
}

document.addEventListener("mouseup", (e) => {
  contenedor.querySelectorAll(".textLayer.selecting").forEach((c) => c.classList.remove("selecting"));
  const seleccion = window.getSelection();
  const texto = (seleccion || "").toString().replace(/\s+/g, " ").trim();
  if (texto.length > 0 && texto.length <= 200) {
    const rects = seleccion.rangeCount > 0 ? seleccion.getRangeAt(0).getClientRects() : [];
    enviar({ tipo: "seleccion", texto, punto: rects.length > 0 ? puntoPdf(rects[0]) : null });
    return;
  }
  // Clic en un valor resaltado: elige esta aparición.
  const span = e.target && e.target.closest ? e.target.closest(".textLayer span.origen") : null;
  if (!span || e.button !== 0) return;
  const punto = puntoPdf(span.getBoundingClientRect());
  if (!punto) return;
  const grupo = span.dataset.grupo;
  contenedor.querySelectorAll(".textLayer span.elegido").forEach((s) => s.classList.remove("elegido"));
  contenedor.querySelectorAll(".textLayer span").forEach((s) => { if (s.dataset.grupo === grupo) s.classList.add("elegido"); });
  enviar({ tipo: "elegir", campos: (span.dataset.campos || "").split(",").filter((c) => c), punto });
});

document.addEventListener("wheel", (e) => {
  if (!e.ctrlKey || !documento) return;
  e.preventDefault();
  zoom = Math.min(4, Math.max(0.4, zoom * (e.deltaY < 0 ? 1.1 : 1 / 1.1)));
  pintar(generacion);
}, { passive: false });

let redimension = null;
window.addEventListener("resize", () => {
  clearTimeout(redimension);
  redimension = setTimeout(() => pintar(generacion), 150);
});

if (host) {
  host.addEventListener("message", (e) => {
    const m = e.data || {};
    if (m.tipo === "abrir") { zoom = 1; abrir(m.datos); }
    else if (m.tipo === "resaltar") { valores = m.valores || []; activo = m.activo || null; puntos = m.puntos || {}; marcar(); }
    else if (m.tipo === "zoom") { zoom = m.factor || 1; pintar(generacion); }
  });
  enviar({ tipo: "listo" });
} else {
  avisar("Este visor solo funciona dentro de ProContaVF.");
}
