// A synthetic Costco receipt as an image-only PDF, built like the Costco app's (docs/modules/receipts/receipts.md): the
// whole receipt ONE bitmap, shown across two A4 pages as two windows onto it, so a line falls across the page break - but
// every name, number and line in it invented. The bitmap is drawn by a headless Chrome page (a canvas, as JPEG) and wrapped
// in a PDF here. Never written into the repo: the check writes it to its scratch directory.
import { writeFileSync } from 'node:fs';

/**
 * costcoPdf(page, path, { items, total, sold, stamp }) - page is a profile from lib/profiles.mjs (only its evaluate is
 * used). items: [{ name, detail?, code, qty, unit, amount, gst, discount? }] with money as printed ('21.49'); a discount
 * prints its amount with a trailing '-'. stamp: 'dd/mm/yyyy hh:mm:ss AM 100 001 0002 003'.
 */
export async function costcoPdf(page, path, { items, total, sold, stamp }) {
  const rows = [
    ['center', 'COSTCO WHOLESALE', 60, true], ['center', 'Costco Wholesale Australia Pty Ltd', 28, true],
    ['center', '1 Example Street'], ['center', 'Exampleton'], ['center', 'QLD 4000'], ['center', 'ABN No: 00 000 000 000'], ['gap'],
    ['center', 'TAX INVOICE ($)'], ['gap'], ['left', 'Member 00000000000'], ['gap'],
  ];
  for (const i of items) {
    rows.push(['left', i.name]);
    if (i.detail) rows.push(['left', i.detail]);
    rows.push(['item', i.code, `${i.qty}x`, i.unit, `${i.amount}${i.discount ? ' -' : ''} ${i.gst ? 1 : 0}`]);
  }
  rows.push(['gap'], ['money', '**** TOTAL (INCL GST)', total], ['money', '**** TOTAL (EXCL GST)', '0.00'], ['money', 'GST Amount', '0.00'],
    ['money', 'VISA CREDIT', total], ['gap'], ['center', `TOTAL NUMBER OF ITEMS SOLD = ${sold}`], ['center', stamp],
    ['center', 'COSTCO WHOLESALE AUSTRALIA'], ['center', 'ABN : 00 000 000 000'], ['gap'], ['center', `Items Sold: ${sold}`]);

  const jpeg = await page.evaluate(`(() => {
    const rows = ${JSON.stringify(rows)};
    const W = 780, pitch = 48;
    const canvas = document.createElement('canvas');
    canvas.width = W;
    canvas.height = 80 + rows.reduce((h, r) => h + (r[0] === 'gap' ? 24 : r[2] === 60 ? 90 : pitch), 0);
    const c = canvas.getContext('2d');
    c.fillStyle = '#fff'; c.fillRect(0, 0, W, canvas.height); c.fillStyle = '#222'; c.textBaseline = 'alphabetic';
    let y = 40;
    for (const r of rows) {
      if (r[0] === 'gap') { y += 24; continue; }
      const size = r[0] === 'center' && typeof r[2] === 'number' ? r[2] : 26;
      y += size === 60 ? 90 : pitch;
      c.font = (r[3] ? 'bold ' : '') + size + 'px Helvetica, Arial, sans-serif';
      if (r[0] === 'center') { c.textAlign = 'center'; c.fillText(r[1], W / 2, y); }
      else if (r[0] === 'left') { c.textAlign = 'left'; c.fillText(r[1], 40, y); }
      else if (r[0] === 'money') { c.textAlign = 'left'; c.fillText(r[1], 40, y); c.textAlign = 'right'; c.fillText(r[2], W - 40, y); }
      else { c.textAlign = 'left'; c.fillText(r[1], 40, y); c.fillText(r[2], 230, y); c.textAlign = 'right'; c.fillText(r[3], 470, y); c.fillText(r[4], W - 50, y); }
    }
    return canvas.toDataURL('image/jpeg', 0.95) + '|' + canvas.width + '|' + canvas.height;
  })()`);
  const [url, w, h] = jpeg.split('|');
  const image = Buffer.from(url.split(',')[1], 'base64');
  const width = Number(w), height = Number(h);

  // Two A4 pages, each a 320 pt wide window 801.9 pt tall onto the one image, the second carrying on where the first ends.
  const PW = 595.27559, PH = 841.88976, IW = 320, IH = IW * height / width, WIN = 801.88976;
  const content = shift => `q 1 0 0 1 137.6378 20 cm 0 0 ${IW} ${WIN} re W n q 1 0 0 1 0 ${(WIN - IH + shift).toFixed(4)} cm ${IW} 0 0 ${IH.toFixed(4)} 0 0 cm /I6 Do Q Q`;
  const chunks = [];
  const offsets = [];
  let length = 0;
  const put = data => { const b = Buffer.isBuffer(data) ? data : Buffer.from(data, 'latin1'); chunks.push(b); length += b.length; };
  const obj = (n, body, stream) => {
    offsets[n] = length;
    if (stream) { put(`${n} 0 obj\n${body}\nstream\n`); put(stream); put('\nendstream\nendobj\n'); }
    else put(`${n} 0 obj\n${body}\nendobj\n`);
  };
  put('%PDF-1.4\n');
  obj(1, '<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>');
  obj(2, '<< /Type /Catalog /Pages 1 0 R >>');
  const page1 = content(0), page2 = content(WIN);
  obj(3, `<< /Type /Page /Parent 1 0 R /MediaBox [0 0 ${PW} ${PH}] /Resources << /XObject << /I6 6 0 R >> >> /Contents 4 0 R >>`);
  obj(4, `<< /Length ${page1.length} >>`, page1);
  obj(5, `<< /Type /Page /Parent 1 0 R /MediaBox [0 0 ${PW} ${PH}] /Resources << /XObject << /I6 6 0 R >> >> /Contents 7 0 R >>`);
  obj(6, `<< /Type /XObject /Subtype /Image /Width ${width} /Height ${height} /BitsPerComponent 8 /ColorSpace /DeviceRGB /Filter /DCTDecode /Length ${image.length} >>`, image);
  obj(7, `<< /Length ${page2.length} >>`, page2);
  const xref = length;
  put(`xref\n0 8\n0000000000 65535 f \n${offsets.slice(1).map(o => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`);
  put(`trailer\n<< /Size 8 /Root 2 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
  writeFileSync(path, Buffer.concat(chunks));
  return path;
}
