// Fetches real interchange ramps along a route from OpenStreetMap (ODbL):
//   node Tools/fetch_ramps.js miami_ftl 25.76 -80.26
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s] = process.argv;
const lat0 = +lat0s, lon0 = +lon0s, M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const root = path.join(__dirname, '..');
const route = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/Data/Routes', id + '.json'), 'utf8'));
const samples = []; let next = 0;
for (const p of route.points) if (p.s >= next) { samples.push(`${(lat0 + p.z / M_LAT).toFixed(5)},${(lon0 + p.x / M_LON).toFixed(5)}`); next = p.s + 800; }
const q = `[out:json][timeout:170];way["highway"="motorway_link"](around:900,${samples.join(',')});out geom;`;
(async () => {
  for (let attempt = 0; attempt < 5; attempt++) {
    for (const url of ['https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter']) {
      try {
        const r = await fetch(url, { method: 'POST', headers: { 'User-Agent': 'SurviveI95-RouteTool/1.0', 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
        if (!r.ok) { console.log('overpass', r.status); continue; }
        const ways = (await r.json()).elements.filter(w => w.geometry && w.geometry.length > 1);
        const roads = ways.map(w => ({ w: 5.6, p: w.geometry.flatMap(g => [+((g.lon - lon0) * M_LON).toFixed(1), +((g.lat - lat0) * M_LAT).toFixed(1)]) }));
        fs.writeFileSync(path.join(root, 'Tools/Data', id + '_ramps.json'), JSON.stringify({ roads, lots: [] }));
        console.log('saved ramps', roads.length);
        return;
      } catch (e) { console.log(e.message); }
    }
    await new Promise(res => setTimeout(res, 15000));
  }
  process.exit(1);
})();
