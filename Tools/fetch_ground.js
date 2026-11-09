// Fetches real local streets and parking lots along a route from OpenStreetMap (ODbL):
//   node Tools/fetch_ground.js miami_ftl 25.76 -80.26
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s] = process.argv;
const lat0 = +lat0s, lon0 = +lon0s, M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const root = path.join(__dirname, '..');
const route = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/Data/Routes', id + '.json'), 'utf8'));
const samples = []; let next = 0;
for (const p of route.points) if (p.s >= next) { samples.push(`${(lat0 + p.z / M_LAT).toFixed(5)},${(lon0 + p.x / M_LON).toFixed(5)}`); next = p.s + 800; }
const list = samples.join(',');
const WIDTH = { primary: 15, secondary: 12, tertiary: 10, unclassified: 7, residential: 7 };

async function query(q) {
  for (let attempt = 0; attempt < 4; attempt++) {
    for (const url of ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']) {
      try {
        const r = await fetch(url, { method: 'POST', headers: { 'User-Agent': 'SurviveI95-RouteTool/1.0', 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
        if (r.ok) return (await r.json()).elements;
        console.log('overpass', r.status);
      } catch (e) { console.log(e.message); }
    }
    await new Promise(r => setTimeout(r, 15000));
  }
  throw new Error('Overpass unavailable');
}
const xy = g => [+((g.lon - lon0) * M_LON).toFixed(1), +((g.lat - lat0) * M_LAT).toFixed(1)];

(async () => {
  const streets = await query(`[out:json][timeout:170];way["highway"~"^(primary|secondary|tertiary|unclassified|residential)$"](around:520,${list});out tags geom;`);
  const lots = await query(`[out:json][timeout:170];way["amenity"="parking"](around:520,${list});out tags geom;`);
  const out = {
    roads: streets.filter(w => w.geometry && w.geometry.length > 1 && !w.tags.bridge && !w.tags.tunnel).map(w => ({ w: WIDTH[w.tags.highway], p: w.geometry.flatMap(xy) })),
    lots: lots.filter(w => w.geometry && w.geometry.length > 3).map(w => ({ w: 0, p: w.geometry.slice(0, -1).flatMap(xy) }))
  };
  const file = path.join(root, 'Tools/Data', id + '_ground.json');
  fs.writeFileSync(file, JSON.stringify(out));
  console.log(`saved ${out.roads.length} streets, ${out.lots.length} parking lots, ${(fs.statSync(file).size / 1e6).toFixed(1)} MB`);
})().catch(e => { console.error(e.message); process.exit(1); });
