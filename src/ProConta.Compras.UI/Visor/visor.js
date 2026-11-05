// Visor de PDF de compras (compras/pantallas). La pantalla le envía mensajes por WebView2:
//   { tipo: "abrir", datos: "<base64>", nombre }  abre el PDF
//   { tipo: "resaltar", valores: [{ campo, texto }], activo }  marca dónde aparece cada valor
//   { tipo: "zoom", factor }  1 = ajustar al ancho
// y recibe { tipo: "seleccion", texto } al soltar el ratón con texto seleccionado.
import * as pdfjsLib from "./pdfjs/pdf.min.js";

pdfjsLib.GlobalWorkerOptions.workerSrc = "./pdfjs/pdf.worker.min.js";

const contenedor = document.getElementById("paginas");
const mensaje = document.getElementById("mensaje");
let documento = null;
let zoom = 1;
let valores = [];
let activo = null;
let generacion = 0;

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

function marcar() {
  const spans = contenedor.querySelectorAll(".textLayer span");
  spans.forEach((s) => s.classList.remove("origen", "activo"));
  let primero = null;
  for (const v of valores) {
    const buscado = normalizar(v.texto);
    if (buscado.length < 2) continue;
    for (const s of spans) {
      const texto = normalizar(s.textContent);
      if (texto.length === 0 || !texto.includes(buscado)) continue;
      s.classList.add("origen");
      s.title = v.campo;
      if (v.campo === activo) { s.classList.add("activo"); primero = primero || s; }
    }
  }
  if (primero) primero.scrollIntoView({ block: "center", behavior: "smooth" });
}

document.addEventListener("mouseup", () => {
  contenedor.querySelectorAll(".textLayer.selecting").forEach((c) => c.classList.remove("selecting"));
  const texto = (window.getSelection() || "").toString().replace(/\s+/g, " ").trim();
  if (texto.length > 0 && texto.length <= 200) enviar({ tipo: "seleccion", texto });
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
    else if (m.tipo === "resaltar") { valores = m.valores || []; activo = m.activo || null; marcar(); }
    else if (m.tipo === "zoom") { zoom = m.factor || 1; pintar(generacion); }
  });
  enviar({ tipo: "listo" });
} else {
  avisar("Este visor solo funciona dentro de ProContaVF.");
}
