// Fetches real building footprints along a route from OpenStreetMap (ODbL):
//   node Tools/fetch_buildings.js miami_ftl 25.76 -80.26
// The two numbers are the route's origin latitude and longitude from fetch_route.js.
// Large buildings (hospitals, malls, campuses) are often mapped as multipolygon relations, not single ways;
// those are fetched too. Add --relations-only to add just them to an existing file.
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s, flag] = process.argv;
const relationsOnly = flag === '--relations-only';
const lat0 = +lat0s, lon0 = +lon0s, M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const root = path.join(__dirname, '..');
const route = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/Data/Routes', id + '.json'), 'utf8'));
const pts = route.points.filter((p, i) => i === 0 || p.s - route.points[i - 1].s > 0);
const samples = []; let next = 0;
for (const p of pts) if (p.s >= next) { samples.push(`${(lat0 + p.z / M_LAT).toFixed(5)},${(lon0 + p.x / M_LON).toFixed(5)}`); next = p.s + 800; }
const list = samples.join(',');

async function query(q) {
  for (const url of ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']) {
    const r = await fetch(url, { method: 'POST', headers: { 'User-Agent': 'SurviveI95-RouteTool/1.0', 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
    if (r.ok) return (await r.json()).elements;
    console.log('overpass', url, r.status);
  }
  throw new Error('Overpass unavailable');
}

// Joins a relation's outer member ways end to end into closed rings.
function outerRings(rel) {
  const parts = rel.members.filter(m => m.type === 'way' && m.role !== 'inner' && m.geometry && m.geometry.length > 1).map(m => m.geometry.slice());
  const key = g => g.lat.toFixed(7) + ',' + g.lon.toFixed(7), rings = [];
  while (parts.length) {
    let ring = parts.shift();
    for (let grew = true; grew && key(ring[0]) !== key(ring[ring.length - 1]);) {
      grew = false;
      for (let i = 0; i < parts.length; i++) {
        const q = parts[i], end = key(ring[ring.length - 1]);
        if (key(q[0]) === end) ring = ring.concat(q.slice(1));
        else if (key(q[q.length - 1]) === end) ring = ring.concat(q.slice().reverse().slice(1));
        else continue;
        parts.splice(i, 1); grew = true; break;
      }
    }
    if (ring.length >= 4 && key(ring[0]) === key(ring[ring.length - 1])) rings.push(ring);
  }
  return rings;
}

(async () => {
  // The 'around' filter doesn't reach relations, so ask box by box along the road and keep those near it.
  let rels = []; const seenRel = new Set();
  for (let i = 0; i < pts.length; ) {
    let j = i; while (j < pts.length - 1 && pts[j].s - pts[i].s < 2500) j++;
    const seg = pts.slice(i, j + 1), la = seg.map(p => lat0 + p.z / M_LAT), lo = seg.map(p => lon0 + p.x / M_LON);
    const box = [Math.min(...la) - .005, Math.min(...lo) - .005, Math.max(...la) + .005, Math.max(...lo) + .005].map(v => v.toFixed(4)).join(',');
    for (let attempt = 0; attempt < 5; attempt++) {
      try { for (const r of await query(`[out:json][timeout:120];relation["building"](${box});out body geom;`)) if (!seenRel.has(r.id)) { seenRel.add(r.id); rels.push(r); } break; }
      catch (e) { console.log('retrying', e.message); await new Promise(r => setTimeout(r, 15000)); }
    }
    if (j === pts.length - 1) break; i = j;
  }
  const nearRoute = g => pts.some(p => { const dx = (g.lon - lon0) * M_LON - p.x, dz = (g.lat - lat0) * M_LAT - p.z; return dx * dx + dz * dz < 420 * 420; });
  rels = rels.filter(r => r.members && r.members.some(m => m.geometry && m.geometry.some(nearRoute)));
  console.log('building relations', rels.length);
  const relWays = [];
  for (const r of rels) outerRings(r).forEach((g, i) => relWays.push({ id: 'r' + r.id + '_' + i, tags: r.tags || {}, geometry: g }));
  const near = relationsOnly ? [] : await query(`[out:json][timeout:170];way["building"](around:420,${list});out tags geom;`);
  console.log('nearby footprints', near.length);
  const zs = route.points.map(p => lat0 + p.z / M_LAT), xs = route.points.map(p => lon0 + p.x / M_LON);
  const bbox = [Math.min(...zs) - 0.03, Math.min(...xs) - 0.035, Math.max(...zs) + 0.03, Math.max(...xs) + 0.035].map(v => v.toFixed(4)).join(',');
  let tall = [];
  if (!relationsOnly) for (const q of [`way["building"]["building:levels"~"^([89]|[1-9][0-9]+)"](${bbox});`, `way["building"]["height"~"^([3-9][0-9]|[1-9][0-9][0-9])"](${bbox});`]) {
    for (let attempt = 0; attempt < 4; attempt++) {
      try { tall = tall.concat(await query('[out:json][timeout:120];' + q + 'out tags geom;')); break; }
      catch (e) { console.log('retrying', e.message); await new Promise(r => setTimeout(r, 15000)); }
    }
  }
  console.log('tall footprints', tall.length);
  const dir0 = path.join(root, 'Tools/Data', id + '_buildings.json');
  const seen = new Set(), items = relationsOnly ? JSON.parse(fs.readFileSync(dir0, 'utf8')).items : [];
  const before = items.length;
  const heightOf = t => {
    const h = parseFloat(t.height); if (h > 2) return h;
    const l = parseFloat(t['building:levels']); if (l > 0) return l * 3.3;
    return 0;
  };
  for (const w of near.concat(tall, relWays)) {
    if (seen.has(w.id) || !w.geometry || w.geometry.length < 4) continue;
    seen.add(w.id);
    const raw = w.geometry.slice(0, -1).map(g => [(g.lon - lon0) * M_LON, (g.lat - lat0) * M_LAT]);
    const step = Math.max(1, Math.ceil(raw.length / 14));
    const simple = raw.filter((_, i) => i % step === 0);
    if (simple.length < 3) continue;
    const pts = [];
    simple.forEach(p => pts.push(+p[0].toFixed(1), +p[1].toFixed(1)));
    items.push({ h: +heightOf(w.tags).toFixed(1), k: w.tags.building === 'yes' ? '' : w.tags.building, p: pts });
  }
  const dir = path.join(root, 'Tools/Data'); fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, id + '_buildings.json');
  fs.writeFileSync(file, JSON.stringify({ items }));
  const hs = items.map(i => i.h).filter(h => h > 0).sort((a, b) => b - a);
  console.log(`added ${items.length - before} buildings from relations or ways`);
  console.log(`saved ${items.length} buildings, ${hs.length} with height data, tallest ${hs.slice(0, 5).join(', ')} m, file ${(fs.statSync(file).size / 1e6).toFixed(1)} MB`);
})().catch(e => { console.error(e.message); process.exit(1); });
