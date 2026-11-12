// Fetches what the land along a route is used for, from OpenStreetMap (ODbL):
//   node Tools/fetch_landuse.js miami_ftl 25.76 -80.26
// Zones (homes, shops, industry, parks), water (ponds, lakes, canals), railways, and the kind of
// business at each shop or restaurant, so the builder can shape every building by what it really is.
const fs = require('fs'), path = require('path');
const [, , id, lat0s, lon0s] = process.argv;
const lat0 = +lat0s, lon0 = +lon0s, M_LAT = 111320, M_LON = M_LAT * Math.cos(lat0 * Math.PI / 180);
const root = path.join(__dirname, '..');
const route = JSON.parse(fs.readFileSync(path.join(root, 'Assets/Resources/Data/Routes', id + '.json'), 'utf8'));
const samples = []; let next = 0;
for (const p of route.points) if (p.s >= next) { samples.push(`${(lat0 + p.z / M_LAT).toFixed(5)},${(lon0 + p.x / M_LON).toFixed(5)}`); next = p.s + 800; }
const list = samples.join(',');
const R = 700;

async function query(q) {
  for (let attempt = 0; attempt < 4; attempt++) {
    for (const url of ['https://overpass.kumi.systems/api/interpreter', 'https://overpass-api.de/api/interpreter']) {
      try {
        const r = await fetch(url, { method: 'POST', headers: { 'User-Agent': 'SurviveI95-RouteTool/1.0', 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'data=' + encodeURIComponent(q) });
        if (r.ok) {
          const j = await r.json();
          if (j.remark && /timed out|memory|runtime error/i.test(j.remark)) { console.log('overpass remark', j.remark.slice(0, 120)); continue; }
          return j.elements;
        }
        console.log('overpass', r.status);
      } catch (e) { console.log(e.message); }
    }
    await new Promise(r => setTimeout(r, 15000));
  }
  throw new Error('Overpass unavailable');
}
const xy = g => [+((g.lon - lon0) * M_LON).toFixed(1), +((g.lat - lat0) * M_LAT).toFixed(1)];

function zoneOf(t) {
  const lu = t.landuse, le = t.leisure, na = t.natural;
  if (lu === 'residential') return 'home';
  if (lu === 'retail' || lu === 'commercial') return 'shop';
  if (lu === 'industrial' || lu === 'railway') return 'industry';
  if (lu === 'cemetery' || lu === 'grass' || lu === 'recreation_ground' || lu === 'village_green' || le === 'park' || le === 'golf_course' || le === 'pitch' || le === 'garden') return 'park';
  if (na === 'water' || t.water || t.waterway === 'riverbank' || lu === 'basin' || lu === 'reservoir') return 'water';
  if (na === 'wetland') return 'wetland';
  if (na === 'wood' || na === 'scrub' || lu === 'forest') return 'wild';
  if (lu === 'religious' || t.amenity === 'school' || t.amenity === 'hospital' || t.amenity === 'university' || t.amenity === 'college') return 'civic';
  return null;
}

// Closed rings from a way, or the outer rings of a multipolygon relation (members joined end to end).
function rings(e) {
  if (e.type === 'way') return e.geometry && e.geometry.length > 3 ? [e.geometry] : [];
  const outs = (e.members || []).filter(m => m.role === 'outer' && m.geometry && m.geometry.length > 1).map(m => m.geometry.slice());
  const done = [];
  while (outs.length) {
    let ring = outs.shift();
    for (let guard = 0; guard < 500; guard++) {
      const a = ring[0], z = ring[ring.length - 1];
      if (a.lat === z.lat && a.lon === z.lon) break;
      const i = outs.findIndex(o => (o[0].lat === z.lat && o[0].lon === z.lon) || (o[o.length - 1].lat === z.lat && o[o.length - 1].lon === z.lon));
      if (i < 0) break;
      const o = outs.splice(i, 1)[0];
      ring = ring.concat(o[0].lat === z.lat && o[0].lon === z.lon ? o.slice(1) : o.reverse().slice(1));
    }
    if (ring.length > 3) done.push(ring);
  }
  return done;
}

function simplify(ring, maxPts) {
  const pts = ring.slice(0, -1).map(xy);
  const step = Math.max(1, Math.ceil(pts.length / maxPts));
  return pts.filter((_, i) => i % step === 0).flat();
}

const BUSINESS = {
  fuel: 'gas', fast_food: 'food', restaurant: 'food', cafe: 'food', bar: 'food', ice_cream: 'food',
  pharmacy: 'pharmacy', bank: 'bank', place_of_worship: 'church', school: 'school', car_wash: 'carwash', car_rental: 'cars',
  supermarket: 'grocery', convenience: 'convenience', alcohol: 'liquor', hairdresser: 'barber', beauty: 'nails',
  laundry: 'laundry', dry_cleaning: 'laundry', car_repair: 'auto', tyres: 'tires', car_parts: 'auto', car: 'cars',
  furniture: 'furniture', mobile_phone: 'phones', clothes: 'clothes', shoes: 'clothes', pawnbroker: 'pawn', money_lender: 'cash',
  dentist: 'dental', doctors: 'clinic', clinic: 'clinic', veterinary: 'vet', hotel: 'hotel', motel: 'motel', storage_rental: 'storage',
  hardware: 'hardware', bakery: 'bakery', butcher: 'grocery', tobacco: 'smoke', e_cigarette: 'smoke', variety_store: 'discount',
  department_store: 'discount', insurance: 'insurance', tax_advisor: 'tax', travel_agency: 'travel', church: 'church'
};
function businessOf(t) {
  return BUSINESS[t.amenity] || BUSINESS[t.shop] || BUSINESS[t.office] || BUSINESS[t.tourism] || BUSINESS[t.healthcare] || (t.shop ? 'shop' : null);
}

// The route in pieces of about 3 km, so no single request is too heavy for the server. Each piece is saved as it
// arrives (Tools/Data/cache), so a run that dies keeps what it got, and a piece that keeps failing is skipped.
const cacheDir = path.join(root, 'Tools/Data/cache'); fs.mkdirSync(cacheDir, { recursive: true });
let stage = 0;
async function each(make) {
  const out = [], seen = new Set(); stage++;
  for (let i = 0; i < samples.length; i += 4) {
    const part = samples.slice(i, i + 4).join(',');
    const cf = path.join(cacheDir, `${id}_${stage}_${i}.json`);
    let got;
    // An empty answer is how an overloaded server often fails quietly, so it's never kept: that piece is asked again next run.
    const cached = fs.existsSync(cf) ? JSON.parse(fs.readFileSync(cf, 'utf8')) : null;
    if (cached && cached.length > 0) got = cached;
    else {
      try { got = await query(make(part)); if (got.length > 0) fs.writeFileSync(cf, JSON.stringify(got)); }
      catch (e) { console.log('\nskipped a piece:', e.message); got = []; }
    }
    for (const e of got) { const k = e.type + e.id; if (!seen.has(k)) { seen.add(k); out.push(e); } }
    process.stdout.write('.');
  }
  console.log();
  return out;
}

(async () => {
  const areas = await each(list => `[out:json][timeout:180];(
    way["landuse"](around:${R},${list}); relation["landuse"](around:${R},${list});
    way["leisure"~"^(park|golf_course|pitch|garden)$"](around:${R},${list}); relation["leisure"~"^(park|golf_course)$"](around:${R},${list});
    way["natural"~"^(water|wetland|wood|scrub)$"](around:${R},${list}); relation["natural"~"^(water|wetland)$"](around:${R},${list});
    way["amenity"~"^(school|hospital|university|college)$"](around:${R},${list});
  );out tags geom;`);
  console.log('areas', areas.length);
  const zones = [];
  for (const e of areas) {
    const z = zoneOf(e.tags || {}); if (!z) continue;
    for (const r of rings(e)) zones.push({ k: z, p: simplify(r, z === 'water' ? 60 : 40) });
  }
  const lines = await each(list => `[out:json][timeout:180];(
    way["waterway"~"^(canal|river|ditch|drain|stream)$"](around:${R},${list});
    way["railway"="rail"](around:${R},${list});
    way["power"="line"](around:${R},${list}); way["power"="minor_line"](around:${R},${list});
  );out tags geom;`);
  console.log('lines', lines.length);
  const ways = [];
  for (const w of lines) {
    if (!w.geometry || w.geometry.length < 2) continue;
    const t = w.tags || {};
    const k = t.railway ? 'rail' : t.power ? 'power' : t.waterway === 'canal' || t.waterway === 'river' ? 'canal' : 'ditch';
    const width = k === 'canal' ? (parseFloat(t.width) || 18) : k === 'ditch' ? 4 : k === 'rail' ? 4 : 0;
    ways.push({ k, w: width, p: w.geometry.flatMap(xy) });
  }
  const pois = await each(list => `[out:json][timeout:180];(
    node["shop"](around:${R},${list}); node["amenity"](around:${R},${list}); node["office"](around:${R},${list}); node["tourism"~"^(hotel|motel)$"](around:${R},${list});
    way["amenity"="fuel"](around:${R},${list}); way["shop"](around:${R},${list}); way["amenity"~"^(fast_food|restaurant|bank|pharmacy|place_of_worship|car_wash)$"](around:${R},${list});
  );out tags center;`);
  const shops = [];
  for (const n of pois) {
    const b = businessOf(n.tags || {}); if (!b) continue;
    const g = n.type === 'node' ? n : n.center; if (!g) continue;
    shops.push({ k: b, p: xy(g) });
  }
  console.log('businesses', shops.length);
  const out = { zones, ways, shops };
  const file = path.join(root, 'Tools/Data', id + '_landuse.json');
  fs.writeFileSync(file, JSON.stringify(out));
  const count = a => Object.entries(a.reduce((m, x) => (m[x.k] = (m[x.k] || 0) + 1, m), {})).map(([k, v]) => k + ' ' + v).join(', ');
  console.log(`saved zones (${count(zones)}), ways (${count(ways)}), businesses (${count(shops)}), ${(fs.statSync(file).size / 1e6).toFixed(1)} MB`);
})().catch(e => { console.error(e.message); process.exit(1); });
