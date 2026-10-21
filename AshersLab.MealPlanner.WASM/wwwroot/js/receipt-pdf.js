// A receipt PDF read in the browser, never uploaded (docs/modules/receipts/receipts.md). pdf.js is vendored under
// lib/pdfjs (the legacy build, for older iPhones' Safari) because the app makes no third-party request. Only the text
// runs and their positions go back to .NET, which does all the reading (Shared.Domain/Receipts); the file stays in
// this module only while the review is open, to show it, and is dropped by forget().

let pdfjs;
let open = null;

async function library() {
  if (!pdfjs) {
    pdfjs = await import('../lib/pdfjs/pdf.min.mjs');
    pdfjs.GlobalWorkerOptions.workerSrc = new URL('../lib/pdfjs/pdf.worker.min.mjs', import.meta.url).href;
  }
  return pdfjs;
}

/** The text runs of every page as JSON: [{ items: [{ text, x, y, width }] }], y from the page's top, in points. */
export async function read(bytes) {
  const lib = await library();
  await forget();
  // No eval, no fonts loaded into the page: only the text is wanted.
  open = await lib.getDocument({ data: bytes, isEvalSupported: false, disableFontFace: true }).promise;
  const pages = [];
  for (let n = 1; n <= open.numPages; n++) {
    const page = await open.getPage(n);
    const height = page.getViewport({ scale: 1 }).height;
    const content = await page.getTextContent();
    pages.push({
      items: content.items.filter(i => typeof i.str === 'string' && i.str.trim() !== '')
        .map(i => ({ text: i.str, x: i.transform[4], y: height - i.transform[5], width: i.width })),
    });
  }
  return JSON.stringify(pages);
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

/** Drops the file: after an import, a cancel, or reading another. */
export async function forget() {
  if (open) {
    const doc = open;
    open = null;
    await doc.destroy();
  }
}
