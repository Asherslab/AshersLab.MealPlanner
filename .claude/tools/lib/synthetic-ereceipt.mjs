// A synthetic Woolworths in-store eReceipt as a real text PDF, laid out like the real one (docs/modules/receipts/receipts.md):
// one tall page of the till roll, monospaced, each word its own run, a marker (# GST, ^ promotional price) one character
// left of the names, amounts right-aligned. Every name and number in it is invented. The same layout as
// Tests/Receipts/SyntheticEReceipt.cs, which feeds the parser directly; this one goes through pdf.js in the browser. Never
// written into the repo: the check writes it to its scratch directory.
import { writeFileSync } from 'node:fs';

const W = 224, H = 751, CH = 3.6, PITCH = 7.9, LEFT = 8, RIGHT = 209.6;
const esc = s => s.replace(/[\\()]/g, c => `\\${c}`);

/**
 * eReceiptPdf(path, { store, pos, trans, date: 'dd/mm/yyyy', time: 'hh:mm', items, count, subtotal, total }) - each item is
 * { name, amount, marker? }, { name, qty, each, amount, marker? }, { name, kilos, perKg, amount, marker? } or an offer
 * { offer, amount }. Money as printed: '3.40', '-4.00', '$64.20'.
 */
export function eReceiptPdf(path, { store, pos, trans, date, time, items, count, subtotal, total }) {
  const runs = [];
  let y = 54;
  const words = (x, text) => { for (const w of text.split(' ')) { if (w) runs.push([x, y, w]); x += (w.length + 1) * CH; } };
  const row = (x, text, amount) => { y += PITCH; words(x, text); if (amount) words(RIGHT - amount.length * CH, amount); };
  const name = (i, amount) => row(i.marker ? LEFT : LEFT + CH, (i.marker ?? '') + i.name, amount);

  row(LEFT + CH * 11, `${store} Example Town PH: 07 0000 0000`);
  row(LEFT + CH * 13, 'TAX INVOICE - ABN 00 000 000 000');
  y += PITCH;
  row(LEFT, 'Description'); words(RIGHT - CH, '$');
  for (const i of items) {
    if (i.offer) row(LEFT + CH, i.offer, i.amount);
    else if (i.qty) { name(i); row(LEFT + CH, `Qty ${i.qty} @ ${i.each} each`, i.amount); }
    else if (i.kilos) { name(i); row(LEFT + CH, `${i.kilos} kg NET @ ${i.perKg}/kg`, i.amount); }
    else name(i, i.amount);
  }
  y += PITCH;
  row(LEFT, '^Promotional Price');
  row(LEFT + CH * 4, `${count} SUBTOTAL`, subtotal);
  row(LEFT + CH * 7, 'TOTAL', total);
  y += PITCH;
  row(LEFT + CH * 15, `WOOLWORTHS ${store}`);
  row(LEFT + CH * 16, 'PURCHASE', total);
  row(LEFT + CH * 16, 'TOTAL', total);
  row(LEFT + CH * 7, 'Change', '$0.00');
  row(LEFT, 'TOTAL includes GST', '$0.50');
  y += PITCH * 4;
  row(LEFT + CH * 7, `POS ${pos} TRANS ${trans} ${time} ${date}`);
  row(LEFT + CH * 17, 'WOOLWORTHS GROUP LIMITED');

  // A minimal PDF: one monospaced font, each run placed absolutely, in a shuffled order.
  runs.sort(() => Math.random() - 0.5);
  const stream = runs.map(([x, yy, s]) => `BT /F1 6 Tf 1 0 0 1 ${x.toFixed(2)} ${(H - yy).toFixed(2)} Tm (${esc(s)}) Tj ET`).join('\n');
  const objects = [
    '<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>',
    `<< /Length ${Buffer.byteLength(stream, 'latin1')} >>\nstream\n${stream}\nendstream`,
    `<< /Type /Page /Parent 4 0 R /MediaBox [0 0 ${W} ${H}] /Resources << /Font << /F1 1 0 R >> >> /Contents 2 0 R >>`,
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Catalog /Pages 4 0 R >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets = [];
  objects.forEach((body, i) => { offsets.push(Buffer.byteLength(pdf, 'latin1')); pdf += `${i + 1} 0 obj\n${body}\nendobj\n`; });
  const xref = Buffer.byteLength(pdf, 'latin1');
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map(o => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 5 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  writeFileSync(path, Buffer.from(pdf, 'latin1'));
  return path;
}
