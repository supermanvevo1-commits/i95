// Writes the runways near a route for air traffic, from OurAirports (public domain, https://ourairports.com/data/):
//   node Tools/make_air.js miami_ftl 25.76 -80.26 KMIA KFLL
// The two numbers are the route's origin latitude and longitude (as for the other fetch tools).
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s, ...airports] = process.argv;
const lat0 = +lat0s, lon0 = +lon0s, M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const root = path.join(__dirname, '..');

function parseCsv(line) {
  const out = []; let cur = '', q = false;
  for (const ch of line) {
    if (ch === '"') q = !q;
    else if (ch === ',' && !q) { out.push(cur); cur = ''; }
    else cur += ch;
  }
  out.push(cur); return out;
}

(async () => {
  const r = await fetch('https://davidmegginson.github.io/ourairports-data/runways.csv', { headers: { 'User-Agent': 'SurviveI95-RouteTool/1.0' } });
  if (!r.ok) throw new Error('OurAirports ' + r.status);
  const lines = (await r.text()).split('\n'), head = parseCsv(lines[0]);
  const runways = [];
  for (const l of lines.slice(1)) {
    const c = parseCsv(l), o = {}; head.forEach((k, i) => o[k] = c[i]);
    if (!airports.includes(o.airport_ident) || o.closed === '1' || !o.le_latitude_deg || !o.he_latitude_deg) continue;
    const xz = (lat, lon) => [+((+lon - lon0) * M_LON).toFixed(1), +((+lat - lat0) * M_LAT).toFixed(1)];
    const [ax, az] = xz(o.le_latitude_deg, o.le_longitude_deg), [bx, bz] = xz(o.he_latitude_deg, o.he_longitude_deg);
    runways.push({ airport: o.airport_ident, ref: o.le_ident + '/' + o.he_ident, ax, az, bx, bz });
  }
  const file = path.join(root, 'Assets/Resources/Data/Routes', id + '_air.json');
  fs.writeFileSync(file, JSON.stringify({ runways }, null, 1));
  console.log('saved', runways.length, 'runways:', runways.map(r => `${r.airport} ${r.ref} (${r.ax},${r.az})-(${r.bx},${r.bz})`).join('; '));
})().catch(e => { console.error(e.message); process.exit(1); });
