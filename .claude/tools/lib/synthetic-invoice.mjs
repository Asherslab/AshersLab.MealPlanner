// A synthetic Woolworths invoice as a real text PDF, laid out like the real ones (docs/modules/receipts/receipts.md) and
// carrying their traps - bold printed twice, a weighed line wrapped round its numbers, totals on a second page - but
// every name, number and line in it invented. The same layout as Tests/Receipts/SyntheticInvoice.cs, which feeds the
// parser directly; this one goes through pdf.js in the browser. Never written into the repo: the check writes it to its
// scratch directory.
import { writeFileSync } from 'node:fs';

const W = 595.92, H = 842.88;
const esc = s => s.replace(/[\\()]/g, c => `\\${c}`);

/**
 * invoice({ order, date: 'd Mon yyyy', lines: [...], subtotal, bags, total }) - each line is
 * { n, desc, ordered, supplied, price, amount, heading? } or a weighed one { n, name, range, perKg, supplied, amount, heading? }.
 * Money as printed: '$1.00'.
 */
export function invoicePdf(path, { order, date, lines, subtotal, bags, total }) {
  const pages = [[], []];
  const put = (p, x, y, text, bold = false) => { pages[p].push([x, y, text]); if (bold) pages[p].push([x, y, text]); };
  for (const p of [0, 1]) {
    put(p, 255, 27, 'Tax Invoice', true);
    put(p, 517, 27, `Page ${p + 1} of 2`);
    put(p, 247, 39.7, 'ABN 00 000 000 000');
    put(p, 375, 53.2, `Invoice/Order Number: ${order}`, true);
    put(p, 375, 62.2, 'Customer: Sam Example', true);
    put(p, 375, 71.2, `Date: ${date}`, true);
    put(p, 375, 80.2, 'Pickup: 1300 - 1400', true);
    put(p, 112, 727.5, 'Registered Office: Woolworths Group Limited');
  }
  let y = 171;
  for (const [x, h] of [[48, 'Line'], [81, 'Description'], [350, 'Ordered'], [404, 'Supplied'], [467, 'Price'], [516, 'Amount']]) put(0, x, y, h, true);
  const right = (s, edge = 552) => edge - s.length * 4.3;
  for (const l of lines) {
    if (l.heading) { y += 12; put(0, 81, y, l.heading, true); }
    y += 12.7;
    if (l.range) {
      const [low, ...rest] = l.range.split(' ');
      put(0, 81, y - 3.8, `${l.name} ${low}`);
      put(0, 53, y, String(l.n)); put(0, 365, y, '1'); put(0, 460, y, l.perKg);
      put(0, 81, y + 4.5, rest.join(' '));
      y += 21;
      put(0, 88, y - 3.8, `~ ${l.name}`);
      put(0, 406, y, l.supplied); put(0, 460, y, l.perKg); put(0, right(l.amount), y, l.amount);
      put(0, 81, y + 4.5, l.range);
    } else {
      put(0, l.n < 10 ? 55 : 53, y, String(l.n)); put(0, 81, y, l.desc);
      put(0, 365, y, String(l.ordered)); put(0, 420, y, String(l.supplied));
      put(0, right(l.price, 487), y, l.price); put(0, right(l.amount), y, l.amount);
    }
  }
  let t = 145;
  const row = (x, label, value, before) => { t += 11.3; if (before) put(1, 217, t, before); put(1, x, t, label, true); put(1, right(value), t, value); };
  row(443, 'Sub Total:', subtotal);
  row(434, 'Paper bags:', bags, 'Thank you for working with us towards a greener future.');
  row(427, 'Collection fee', '$0.00');
  row(433, 'Service fees', '$0.00');
  row(428, 'Invoice Total:', total);
  row(428, 'Paid Amount:', total);

  // A minimal PDF: one Helvetica font, each run placed absolutely, in a shuffled order (a PDF's order is not reading order).
  const objects = [];
  const add = body => { objects.push(body); return objects.length; };
  const font = add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
  const pagesId = objects.length + 1 + pages.length * 2;
  const kids = [];
  for (const runs of pages) {
    runs.sort(() => Math.random() - 0.5);
    const stream = runs.map(([x, yy, s]) => `BT /F1 8 Tf 1 0 0 1 ${x.toFixed(2)} ${(H - yy).toFixed(2)} Tm (${esc(s)}) Tj ET`).join('\n');
    const content = add(`<< /Length ${Buffer.byteLength(stream, 'latin1')} >>\nstream\n${stream}\nendstream`);
    kids.push(add(`<< /Type /Page /Parent ${pagesId} 0 R /MediaBox [0 0 ${W} ${H}] /Resources << /Font << /F1 ${font} 0 R >> >> /Contents ${content} 0 R >>`));
  }
  add(`<< /Type /Pages /Kids [${kids.map(k => `${k} 0 R`).join(' ')}] /Count ${kids.length} >>`);
  const catalog = add(`<< /Type /Catalog /Pages ${pagesId} 0 R >>`);
  let pdf = '%PDF-1.4\n';
  const offsets = [];
  objects.forEach((body, i) => { offsets.push(Buffer.byteLength(pdf, 'latin1')); pdf += `${i + 1} 0 obj\n${body}\nendobj\n`; });
  const xref = Buffer.byteLength(pdf, 'latin1');
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map(o => `${String(o).padStart(10, '0')} 00000 n \n`).join('')}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root ${catalog} 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  writeFileSync(path, Buffer.from(pdf, 'latin1'));
  return path;
}
