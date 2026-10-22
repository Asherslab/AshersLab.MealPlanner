// A receipt PDF read in the browser, never uploaded (docs/modules/receipts/receipts.md). pdf.js is vendored under
// lib/pdfjs (the legacy build, for older iPhones' Safari) and tesseract.js under lib/tesseract, because the app makes no
// third-party request. Only text goes back to .NET, which does all the reading (Shared.Domain/Receipts): a text PDF's runs
// and their positions, or for a picture (Costco's receipt is image-only) the lines OCR read and where they are on it. The
// file and its picture stay in this module only while the review is open, to show them, and are dropped by forget().

let pdfjs;
let open = null;
let picture = null;   // a canvas: the receipt's bitmap, white behind it

async function library() {
  if (!pdfjs) {
    pdfjs = await import('../lib/pdfjs/pdf.min.mjs');
    pdfjs.GlobalWorkerOptions.workerSrc = new URL('../lib/pdfjs/pdf.worker.min.mjs', import.meta.url).href;
  }
  return pdfjs;
}

let reading = 0;   // the latest pick read() has seen: an earlier one still going gives way to it

/** Thrown by a read() a later one replaced: the page ignores it rather than reporting it. */
const superseded = 'receipt-read-superseded';

/**
 * The text runs of every page as JSON: [{ items: [{ text, x, y, width }] }], y from the page's top, in points.
 * Two reads can overlap - a file picked twice, or a change event Safari fires twice. Each holds its own document and
 * destroys only that one, never the other's mid-read: that was "Worker task was terminated", and both failing.
 * Which is latest is the page's pick number, not the order reads arrive here: an earlier pick can arrive later, its
 * file having taken longer to copy.
 */
export async function read(bytes, pick) {
  const lib = await library();
  const mine = pick;
  if (mine < reading) throw new Error(superseded);
  reading = mine;
  await forget();
  // No eval, no fonts loaded into the page: only the text is wanted.
  const doc = await lib.getDocument({ data: bytes, isEvalSupported: false, disableFontFace: true }).promise;
  const stale = async () => { if (mine === reading) return; await doc.destroy(); throw new Error(superseded); };
  await stale();
  open = doc;
  const pages = [];
  try {
    for (let n = 1; n <= doc.numPages; n++) {
      const page = await doc.getPage(n);
      const height = page.getViewport({ scale: 1 }).height;
      const content = await page.getTextContent();
      pages.push({
        items: content.items.filter(i => typeof i.str === 'string' && i.str.trim() !== '')
          .map(i => ({ text: i.str, x: i.transform[4], y: height - i.transform[5], width: i.width })),
      });
    }
  } catch (e) {
    // A later read destroyed this document on its way in: not a problem with this file.
    if (mine !== reading) throw new Error(superseded);
    throw e;
  }
  await stale();
  return JSON.stringify(pages);
}

/** A decoded pdf.js image as a canvas: an ImageBitmap, or raw pixels (1 bit grey, RGB or RGBA). */
function canvasOf(img) {
  const canvas = document.createElement('canvas');
  canvas.width = img.width;
  canvas.height = img.height;
  const ctx = canvas.getContext('2d');
  if (img.bitmap) {
    ctx.drawImage(img.bitmap, 0, 0);
    return canvas;
  }
  const out = ctx.createImageData(img.width, img.height);
  const src = img.data, dst = out.data, pixels = img.width * img.height;
  if (img.kind === 3) dst.set(src.subarray(0, pixels * 4));
  else if (img.kind === 2) for (let i = 0; i < pixels; i++) { dst[i * 4] = src[i * 3]; dst[i * 4 + 1] = src[i * 3 + 1]; dst[i * 4 + 2] = src[i * 3 + 2]; dst[i * 4 + 3] = 255; }
  else for (let i = 0; i < pixels; i++) { const v = (src[i >> 3] >> (7 - (i & 7))) & 1 ? 255 : 0; dst[i * 4] = dst[i * 4 + 1] = dst[i * 4 + 2] = v; dst[i * 4 + 3] = 255; }
  ctx.putImageData(out, 0, 0);
  return canvas;
}

function fingerprint(canvas) {
  const d = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
  let sum = 0;
  for (let i = 0; i < d.length; i += 997) sum = (sum * 31 + d[i]) >>> 0;
  return `${canvas.width}x${canvas.height}:${sum}`;
}

/**
 * The receipt as one picture. Costco's PDF holds the whole receipt as ONE bitmap shown across two pages, each page a
 * window onto it: reading the bitmap itself, not the pages, means no line is ever cut at the page break. Distinct images
 * are stacked in page order; a PDF with none is drawn page by page at twice its size.
 */
async function pictureOf() {
  const lib = await library();
  const seen = new Set();
  const parts = [];
  for (let n = 1; n <= open.numPages; n++) {
    const page = await open.getPage(n);
    const ops = await page.getOperatorList();
    for (let i = 0; i < ops.fnArray.length; i++) {
      if (ops.fnArray[i] !== lib.OPS.paintImageXObject) continue;
      const name = ops.argsArray[i][0];
      const objs = name.startsWith('g_') ? page.commonObjs : page.objs;
      const img = await new Promise(resolve => objs.get(name, resolve));
      const canvas = canvasOf(img);
      const key = fingerprint(canvas);
      if (!seen.has(key)) { seen.add(key); parts.push(canvas); }
    }
  }
  if (parts.length === 0) {
    for (let n = 1; n <= open.numPages; n++) {
      const page = await open.getPage(n);
      const viewport = page.getViewport({ scale: 2 });
      const canvas = document.createElement('canvas');
      canvas.width = viewport.width;
      canvas.height = viewport.height;
      await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
      parts.push(canvas);
    }
  }
  const width = Math.max(...parts.map(p => p.width));
  const whole = document.createElement('canvas');
  whole.width = width;
  whole.height = parts.reduce((h, p) => h + Math.round(p.height * width / p.width), 0);
  const ctx = whole.getContext('2d');
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, whole.width, whole.height);
  let y = 0;
  for (const p of parts) {
    const h = Math.round(p.height * width / p.width);
    ctx.drawImage(p, 0, y, width, h);
    y += h;
  }
  return whole;
}

/**
 * OCR of the open file's picture, as JSON: [{ text, top, bottom }], one per line, in pixels from the picture's top.
 * Tesseract's LSTM engine, one block of text (psm 6), at the bitmap's own size - measured best on the real receipts
 * (docs/modules/receipts/receipts.md); larger was worse. Progress goes to progress.invokeMethodAsync('Progress', 0..1).
 */
export async function ocr(progress) {
  if (!open) throw new Error('No file is open.');
  picture = await pictureOf();
  const { default: Tesseract } = await import('../lib/tesseract/tesseract.esm.min.js');
  const base = new URL('../lib/tesseract/', import.meta.url).href;
  let shown = -1;
  const worker = await Tesseract.createWorker('eng', Tesseract.OEM.LSTM_ONLY, {
    workerPath: `${base}worker.min.js`,
    corePath: `${base}core`,
    langPath: `${base}lang`,
    workerBlobURL: false,
    gzip: true,
    logger: m => {
      // Loading the engine is the first third, recognising the rest.
      const at = m.status === 'recognizing text' ? 0.33 + m.progress * 0.67 : Math.min(0.33, (m.progress ?? 0) * 0.33);
      const step = Math.floor(at * 20);
      if (step !== shown) { shown = step; progress?.invokeMethodAsync('Progress', at); }
    },
  });
  try {
    await worker.setParameters({ tessedit_pageseg_mode: '6', preserve_interword_spaces: '1' });
    const { data } = await worker.recognize(picture, {}, { blocks: true, text: false });
    const lines = (data.blocks ?? []).flatMap(b => b.paragraphs.flatMap(p => p.lines))
      .map(l => ({ text: l.text.trim(), top: l.bbox.y0, bottom: l.bbox.y1 }));
    return JSON.stringify(lines);
  } finally {
    await worker.terminate();
  }
}

/**
 * Draws each strip of the picture into its canvas: every canvas[data-top][data-bottom] under the element not drawn yet.
 * The review puts one beside each line, so a misread is checked against what the receipt printed.
 */
export function strips(element) {
  if (!picture || !element) return;
  for (const canvas of element.querySelectorAll('canvas[data-top][data-bottom]:not([data-drawn])')) {
    const top = Math.max(0, Number(canvas.dataset.top) - 4);
    const bottom = Math.min(picture.height, Number(canvas.dataset.bottom) + 4);
    canvas.width = picture.width;
    canvas.height = Math.max(1, bottom - top);
    canvas.getContext('2d').drawImage(picture, 0, top, picture.width, bottom - top, 0, 0, picture.width, bottom - top);
    canvas.dataset.drawn = '';
  }
}

/** Draws the open file's pages into the element, one canvas each, as wide as the element. */
export async function show(element) {
  if (!open || !element) return;
  element.replaceChildren();
  for (let n = 1; n <= open.numPages; n++) {
    const page = await open.getPage(n);
    const width = element.clientWidth || 600;
    const scale = width / page.getViewport({ scale: 1 }).width;
    const viewport = page.getViewport({ scale: scale * (window.devicePixelRatio || 1) });
    const canvas = document.createElement('canvas');
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    canvas.style.width = '100%';
    canvas.dataset.testid = 'receipt-file-page';
    element.appendChild(canvas);
    await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
  }
}

/** Drops the file and its picture: after an import, a cancel, or reading another. */
export async function forget() {
  picture = null;
  if (open) {
    const doc = open;
    open = null;
    await doc.destroy();
  }
}
