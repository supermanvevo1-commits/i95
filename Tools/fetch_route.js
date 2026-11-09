// Builds a route data file from real map data:
//   node Tools/fetch_route.js florida_jupiter 26.80 -80.20 26.98 -80.05
// Road, lanes, bridges and exits: OpenStreetMap (ODbL). Elevation: USGS 3DEP (public domain).
const fs = require('fs'), path = require('path');
const [, , id, s, w, n, e] = process.argv;
const bbox = [s, w, n, e].join(',');
const UA = { 'User-Agent': 'SurviveI95-RouteTool/1.0' };
const M_LAT = 111320;

async function overpass() {
  const q = `[out:json][timeout:90];(
    way["highway"="motorway"]["ref"~"(^|;)\\\\s*I ?95"](${bbox});
    way["highway"="motorway_link"](${bbox});
    node["highway"="motorway_junction"](${bbox});
    way["bridge"]["highway"~"^(trunk|primary|secondary|tertiary|unclassified|residential)$"](${bbox});
  );out body geom;`;
  for (const url of ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']) {
    const r = await fetch(url, { method: 'POST', headers: { ...UA, 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
    if (r.ok) return (await r.json()).elements;
    console.log('overpass', url, r.status);
  }
  throw new Error('Overpass unavailable');
}

function stitchNorthbound(ways) {
  const byStart = new Map();
  ways.forEach(w => { const k = w.nodes[0]; (byStart.get(k) || byStart.set(k, []).get(k)).push(w); });
  const north = w => w.geometry[w.geometry.length - 1].lat - w.geometry[0].lat;
  const ends = new Set(ways.map(w => w.nodes[w.nodes.length - 1]));
  let cur = ways.filter(w => north(w) > 0 && !ends.has(w.nodes[0])).sort((a, b) => a.geometry[0].lat - b.geometry[0].lat)[0];
  const out = [], seen = new Set();
  while (cur && !seen.has(cur.id)) {
    seen.add(cur.id); out.push(cur);
    const next = byStart.get(cur.nodes[cur.nodes.length - 1]) || [];
    cur = next.sort((a, b) => north(b) - north(a))[0];
  }
  return out;
}

function segCross(a, b, c, d) {
  const r = { x: b.x - a.x, z: b.z - a.z }, q = { x: d.x - c.x, z: d.z - c.z };
  const den = r.x * q.z - r.z * q.x;
  if (Math.abs(den) < 1e-9) return null;
  const t = ((c.x - a.x) * q.z - (c.z - a.z) * q.x) / den, u = ((c.x - a.x) * r.z - (c.z - a.z) * r.x) / den;
  return t >= 0 && t <= 1 && u >= 0 && u <= 1 ? t : null;
}

async function elevation(lat, lon) {
  const url = `https://epqs.nationalmap.gov/v1/json?x=${lon}&y=${lat}&units=Meters&wkid=4326&includeDate=false`;
  for (let i = 0; i < 3; i++) {
    try { const r = await fetch(url, { headers: UA }); if (r.ok) { const v = parseFloat((await r.json()).value); if (isFinite(v) && v > -100) return v; } } catch { }
    await new Promise(res => setTimeout(res, 500));
  }
  return null;
}

(async () => {
  const el = await overpass();
  const ways = el.filter(x => x.type === 'way');
  const motorway = ways.filter(w => w.tags.highway === 'motorway');
  const chain = stitchNorthbound(motorway);
  const lat0 = +s, lon0 = +w, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
  const xz = g => ({ x: (g.lon - lon0) * M_LON, z: (g.lat - lat0) * M_LAT });

  const pts = [], nodeS = new Map();
  let dist = 0;
  chain.forEach(way => way.geometry.forEach((g, i) => {
    const prev = pts[pts.length - 1], p = xz(g);
    if (prev && Math.hypot(p.x - prev.x, p.z - prev.z) < 0.5) { nodeS.set(way.nodes[i], prev.s); return; }
    if (prev) dist += Math.hypot(p.x - prev.x, p.z - prev.z);
    const pt = { ...p, s: dist, lat: g.lat, lon: g.lon, lanes: parseInt(way.tags.lanes, 10) || 3, bridge: way.tags.bridge ? 1 : 0, y: 0 };
    pts.push(pt); nodeS.set(way.nodes[i], dist);
  }));

  const samples = [];
  for (let d = 0; d <= dist + 400; d += 400) {
    const p = pts.find(q => q.s >= d) || pts[pts.length - 1];
    const v = await elevation(p.lat, p.lon);
    if (v !== null) samples.push({ s: p.s, y: v });
  }
  console.log('elevation samples', samples.length);
  if (samples.length) pts.forEach(p => {
    let i = samples.findIndex(q => q.s >= p.s); if (i < 0) i = samples.length - 1;
    const b = samples[i], a = samples[Math.max(0, i - 1)], t = b.s > a.s ? Math.min(1, Math.max(0, (p.s - a.s) / (b.s - a.s))) : 0;
    p.y = a.y + (b.y - a.y) * t;
  });

  const exits = el.filter(x => x.type === 'node' && x.tags && x.tags.highway === 'motorway_junction' && nodeS.has(x.id)).map(n => {
    const link = ways.find(w => w.tags.highway === 'motorway_link' && w.nodes[0] === n.id);
    const t = link ? link.tags : {};
    return { s: nodeS.get(n.id), ref: n.tags.ref || t.junction_ref || '', name: t.destination || n.tags.name || t['destination:street'] || t['destination:ref'] || '' };
  }).sort((a, b) => a.s - b.s);

  const overpasses = [], chainIds = new Set(chain.map(w => w.id));
  ways.filter(w => w.tags.bridge && w.tags.highway !== 'motorway' && w.tags.highway !== 'motorway_link' && !chainIds.has(w.id)).forEach(w => {
    for (let k = 1; k < w.geometry.length; k++) for (let i = 1; i < pts.length; i++) {
      const c = xz(w.geometry[k - 1]), d = xz(w.geometry[k]), t = segCross(pts[i - 1], pts[i], c, d);
      if (t !== null) overpasses.push({ s: pts[i - 1].s + (pts[i].s - pts[i - 1].s) * t, name: w.tags.name || w.tags.ref || 'Overpass', angle: Math.atan2(d.x - c.x, d.z - c.z) * 180 / Math.PI });
    }
  });

  const minY = Math.min(...pts.map(p => p.y));
  const route = {
    id, source: 'OpenStreetMap contributors (ODbL); USGS 3DEP', length: dist,
    points: pts.map(p => ({ x: +p.x.toFixed(2), y: +(p.y - minY).toFixed(2), z: +p.z.toFixed(2), s: +p.s.toFixed(2), lanes: p.lanes, bridge: p.bridge })),
    exits, overpasses: overpasses.sort((a, b) => a.s - b.s)
  };
  const dest = path.join(__dirname, '..', 'Assets', 'Resources', 'Data', 'Routes', id + '.json');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, JSON.stringify(route));
  const lanes = {}; route.points.forEach(p => lanes[p.lanes] = (lanes[p.lanes] || 0) + 1);
  console.log(`ways ${chain.length}/${motorway.length}, ${(dist / 1000).toFixed(1)} km, points ${pts.length}, lanes ${JSON.stringify(lanes)}, bridgePts ${route.points.filter(p => p.bridge).length}, elev ${Math.min(...route.points.map(p => p.y))}-${Math.max(...route.points.map(p => p.y))}`);
  console.log('start', pts[0].lat, pts[0].lon, 'end', pts[pts.length - 1].lat, pts[pts.length - 1].lon);
  console.log('exits', JSON.stringify(exits)); console.log('overpasses', JSON.stringify(route.overpasses));
})().catch(e => { console.error(e); process.exit(1); });
