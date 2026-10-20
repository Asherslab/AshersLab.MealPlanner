// Run in the app's page (javascript_tool): copies mealplanner.db and its -wal out of OPFS to
// opfs-receive.py, named <label>-mealplanner.db[-wal]. The SAHPool VFS stores each file under an opaque
// name with a 4096-byte header that starts with the file's path; the header is stripped here.
// Change LABEL per read so successive dumps do not overwrite each other.
const label = 'LABEL';
const dir = await (await navigator.storage.getDirectory()).getDirectoryHandle('databases').then(d => d.getDirectoryHandle('.opaque'));
const sent = [];
for await (const [name, h] of dir.entries()) {
  const f = await h.getFile();
  const path = new TextDecoder().decode(new Uint8Array(await f.slice(0, 64).arrayBuffer())).replace(/\0.*$/s, '');
  if (path === '/databases/mealplanner.db' || path === '/databases/mealplanner.db-wal') {
    const out = label + '-' + path.split('/').pop();
    await fetch('http://127.0.0.1:18999/' + out, { method: 'POST', body: f.slice(4096) });
    sent.push(out + ' ' + (f.size - 4096));
  }
}
sent.join(', ')
