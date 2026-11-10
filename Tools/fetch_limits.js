// Adds real speed limits to an existing route, without touching its geometry:
//   node Tools/fetch_limits.js miami_ftl 25.76 -80.26
// The two numbers are the same origin the route was built from. Limits: OpenStreetMap maxspeed tags (ODbL).
// Writes Assets/Resources/Data/Routes/<id>_limits.json: [{ s, mph }] where each entry holds until the next.
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s] = process.argv;
const lat0 = +lat0s, lon0 = +lon0s;
const M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const UA = { 'User-Agent': 'SurviveI95-RouteTool/1.0' };
const routeFile = path.join(__dirname, '..', 'Assets', 'Resources', 'Data', 'Routes', id + '.json');
const route = JSON.parse(fs.readFileSync(routeFile, 'utf8'));
const pts = route.points;

async function overpass(bbox) {
  const q = `[out:json][timeout:180];way["highway"="motorway"]["ref"~"(^|;)\\\\s*I ?95"](${bbox});out body geom;`;
  for (const url of ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']) {
    const r = await fetch(url, { method: 'POST', headers: { ...UA, 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
    if (r.ok) return (await r.json()).elements;
    console.log('overpass', url, r.status);
  }
  throw new Error('Overpass unavailable');
}

const mph = tag => {
  if (!tag) return null;
  const m = /^(\d+)\s*(mph)?/.exec(tag.trim());
  if (!m) return null;
  return m[2] ? +m[1] : Math.round(+m[1] / 1.609);
};

(async () => {
  // A box around the route with a little margin.
  const lats = pts.map(p => lat0 + p.z / M_LAT), lons = pts.map(p => lon0 + p.x / M_LON);
  const bbox = [Math.min(...lats) - .01, Math.min(...lons) - .01, Math.max(...lats) + .01, Math.max(...lons) + .01].map(v => v.toFixed(4)).join(',');
  const ways = (await overpass(bbox)).filter(w => w.type === 'way');
  // The route follows the northbound carriageway, so only northbound ways count.
  const segs = [];
  for (const w of ways) {
    const g = w.geometry, limit = mph(w.tags.maxspeed);
    if (g[g.length - 1].lat <= g[0].lat) continue;
    for (let i = 1; i < g.length; i++) segs.push({
      a: { x: (g[i - 1].lon - lon0) * M_LON, z: (g[i - 1].lat - lat0) * M_LAT },
      b: { x: (g[i].lon - lon0) * M_LON, z: (g[i].lat - lat0) * M_LAT },
      limit, express: /express|toll/i.test((w.tags.name || '') + (w.tags.toll || '') + (w.tags.hov || ''))
    });
  }
  const dist = (p, s) => {
    const dx = s.b.x - s.a.x, dz = s.b.z - s.a.z, len = dx * dx + dz * dz;
    const t = len ? Math.max(0, Math.min(1, ((p.x - s.a.x) * dx + (p.z - s.a.z) * dz) / len)) : 0;
    return Math.hypot(p.x - s.a.x - dx * t, p.z - s.a.z - dz * t);
  };
  // Every 50 m along the route, take the limit of the nearest general-purpose northbound way.
  const samples = [];
  for (let s = 0, i = 0; s <= route.length; s += 50) {
    while (i < pts.length - 2 && pts[i + 1].s < s) i++;
    const a = pts[i], b = pts[i + 1], t = (s - a.s) / Math.max(.01, b.s - a.s);
    const p = { x: a.x + (b.x - a.x) * t, z: a.z + (b.z - a.z) * t };
    let best = null, bd = 25;
    for (const sg of segs) { if (sg.limit == null || sg.express) continue; const d = dist(p, sg); if (d < bd) { bd = d; best = sg; } }
    samples.push({ s, mph: best ? best.limit : null });
  }
  // Fill gaps from the neighbors, then drop blips under 300 m so signs don't flicker.
  let last = samples.find(x => x.mph != null)?.mph ?? 65;
  samples.forEach(x => { if (x.mph == null) x.mph = last; else last = x.mph; });
  const runs = [];
  for (const x of samples) { if (!runs.length || runs[runs.length - 1].mph !== x.mph) runs.push({ s: x.s, mph: x.mph, until: x.s }); runs[runs.length - 1].until = x.s; }
  const kept = runs.filter((r, k) => r.until - r.s >= 300 || k === 0);
  const out = [];
  kept.forEach(r => { if (!out.length || out[out.length - 1].mph !== r.mph) out.push({ s: r.s, mph: r.mph }); });
  const dest = path.join(path.dirname(routeFile), id + '_limits.json');
  fs.writeFileSync(dest, JSON.stringify({ id, source: 'OpenStreetMap contributors (ODbL), maxspeed', limits: out }, null, 1));
  console.log('ways', ways.length, 'tagged', ways.filter(w => w.tags.maxspeed).length, 'segments', segs.length);
  console.log(out.map(r => `${(r.s / 1000).toFixed(1)}km:${r.mph}`).join('  '));
})().catch(e => { console.error(e); process.exit(1); });
