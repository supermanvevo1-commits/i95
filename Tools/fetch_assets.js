// Free asset fetcher. Run from the project root:
//   node Tools/fetch_assets.js environment        Poly Haven CC0 assets, no account needed
//   node Tools/fetch_assets.js shortlist          Writes Tools/vehicle_candidates.json from Sketchfab search
//   node Tools/fetch_assets.js vehicles           Downloads entries with "approved": true
//                                                 (requires env var SKETCHFAB_TOKEN)
// Every download is recorded in ASSET_CREDITS.md. CC BY assets legally require that credit.
const fs = require('fs'), path = require('path');
const ROOT = path.join(__dirname, '..');
const HEADERS = { 'User-Agent': 'SurviveI95-AssetFetch/1.0' };
const CANDIDATES = path.join(__dirname, 'vehicle_candidates.json');
const CREDITS = path.join(ROOT, 'ASSET_CREDITS.md');
const ALLOWED = ['cc0', 'by'];
const BRANDS = /porsche|mercedes|bmw|ford|chevrolet|chevy|chrysler|dodge|toyota|honda|nissan|tesla|audi|volvo|volkswagen|vw\b|mclaren|ferrari|lamborghini|jeep|gmc|yamaha|harley|kawasaki|suzuki|ducati|oshkosh|magirus|peterbilt|kenworth|freightliner|mack\b|fatboy|fallout/i;

const ENV = {
  textures: ['asphalt_02', 'worn_asphalt', 'road_damaged', 'gravel_road', 'asphalt_snow', 'grass_ground', 'brushed_concrete'],
  // "id@4k" asks for a sharper file; the sky fills the screen, so the main one needs it.
  hdris: ['derelict_highway_noon', 'evening_road_01', 'citrus_orchard_road_puresky', 'autumn_field_puresky', 'kloofendal_48d_partly_cloudy_puresky@4k'],
  models: ['concrete_road_barrier', 'street_lamp_01', 'pine_tree_01', 'grass_medium_01'],
  // Overpass is an open-license lookalike of the US highway sign typeface.
  fonts: [{ file: 'Overpass-Bold.ttf', url: 'https://github.com/RedHatOfficial/Overpass/raw/master/fonts/ttf/Overpass-Bold.ttf', credit: 'Overpass (font) by Delve Withrington / Red Hat, SIL Open Font License 1.1, https://overpassfont.org' }]
};
const VEHICLES = {
  sedan: 'sedan car', coupe: 'coupe car', hatchback: 'hatchback', convertible: 'convertible car', station_wagon: 'station wagon',
  luxury: 'luxury car', pickup: 'pickup truck', suv: 'suv car', minivan: 'minivan', taxi: 'taxi car', semi: 'semi truck',
  box_truck: 'box truck', delivery_van: 'delivery van', tanker: 'fuel tanker truck', motorhome: 'motorhome rv',
  fifth_wheel: 'fifth wheel trailer', suv_boat: 'boat trailer', motorcycle_cruiser: 'cruiser motorcycle',
  motorcycle_sport: 'sport motorcycle', police: 'police car'
};
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function json(url, headers) {
  const r = await fetch(url, { headers: { ...HEADERS, ...headers } });
  if (!r.ok) throw new Error(r.status + ' for ' + url);
  return r.json();
}
async function save(url, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  if (fs.existsSync(dest)) return false;
  const r = await fetch(url, { headers: HEADERS });
  if (!r.ok) throw new Error(r.status + ' for ' + url);
  fs.writeFileSync(dest, Buffer.from(await r.arrayBuffer()));
  return true;
}
function credit(line) {
  const old = fs.existsSync(CREDITS) ? fs.readFileSync(CREDITS, 'utf8') : '# Asset credits\n\n';
  if (!old.includes(line)) fs.writeFileSync(CREDITS, old + '- ' + line + '\n');
}

async function environment() {
  const base = path.join(ROOT, 'Assets', 'ThirdParty', 'PolyHaven');
  for (const id of ENV.textures) {
    const f = await json('https://api.polyhaven.com/files/' + id);
    for (const map of ['Diffuse', 'nor_gl', 'Rough']) {
      if (!f[map]) { console.log('  missing', id, map); continue; }
      await save(f[map]['2k'].jpg.url, path.join(base, 'Textures', id, id + '_' + map + '.jpg'));
    }
    console.log('texture', id); credit(id + ' (texture), Poly Haven, CC0, https://polyhaven.com/a/' + id);
  }
  for (const entry of ENV.hdris) {
    const [id, size = '2k'] = entry.split('@');
    const f = await json('https://api.polyhaven.com/files/' + id);
    await save(f.hdri[size].hdr.url, path.join(base, 'Skies', id + '.hdr'));
    console.log('sky', id); credit(id + ' (HDRI), Poly Haven, CC0, https://polyhaven.com/a/' + id);
  }
  for (const font of ENV.fonts) {
    await save(font.url, path.join(ROOT, 'Assets', 'ThirdParty', 'Fonts', font.file));
    console.log('font', font.file); credit(font.credit);
  }
  for (const id of ENV.models) {
    const g = (await json('https://api.polyhaven.com/files/' + id)).gltf['2k'].gltf;
    await save(g.url, path.join(base, 'Models', id, id + '.gltf'));
    for (const [rel, file] of Object.entries(g.include || {})) await save(file.url, path.join(base, 'Models', id, rel));
    console.log('model', id); credit(id + ' (model), Poly Haven, CC0, https://polyhaven.com/a/' + id);
  }
}

async function shortlist() {
  const out = {};
  for (const [vehicle, q] of Object.entries(VEHICLES)) {
    out[vehicle] = [];
    for (const license of ALLOWED) {
      const d = await json('https://api.sketchfab.com/v3/search?type=models&downloadable=true&sort_by=-likeCount&count=24&license=' + license + '&q=' + encodeURIComponent(q));
      d.results.filter(m => m.faceCount > 12000 && m.faceCount < 300000).forEach(m => out[vehicle].push({
        name: m.name, uid: m.uid, url: m.viewerUrl, author: m.user.displayName, license, faces: m.faceCount,
        likes: m.likeCount, brandRisk: BRANDS.test(m.name), approved: false
      }));
      await sleep(400);
    }
    out[vehicle].sort((a, b) => a.brandRisk - b.brandRisk || b.likes - a.likes);
  }
  fs.writeFileSync(CANDIDATES, JSON.stringify(out, null, 2));
  for (const [v, list] of Object.entries(out)) console.log(v.padEnd(20), String(list.filter(x => !x.brandRisk).length).padStart(3), 'no-brand candidates | top:', (list[0] || {}).name || 'NONE');
}

async function vehicles() {
  const token = process.env.SKETCHFAB_TOKEN;
  if (!token) throw new Error('Set the SKETCHFAB_TOKEN environment variable first.');
  const data = JSON.parse(fs.readFileSync(CANDIDATES, 'utf8'));
  for (const [vehicle, list] of Object.entries(data)) for (const m of list.filter(x => x.approved)) {
    if (!ALLOWED.includes(m.license)) throw new Error('Blocked license: ' + m.name);
    const dest = path.join(ROOT, 'Tools', 'Downloads', vehicle, m.uid + '.zip');
    if (fs.existsSync(dest)) continue; // avoids spending API rate limit on finished files
    try {
      const d = await json('https://api.sketchfab.com/v3/models/' + m.uid + '/download', { Authorization: 'Token ' + token });
      await save(d.gltf.url, dest);
      console.log('vehicle', vehicle, m.name);
      credit(`"${m.name}" by ${m.author}, ${m.license === 'by' ? 'CC BY 4.0' : 'CC0'}, ${m.url}`);
    } catch (e) { console.log('FAILED', vehicle, m.name, e.message); }
    await sleep(800);
  }
}

({ environment, shortlist, vehicles })[process.argv[2]]().catch(e => { console.error(e.message); process.exit(1); });
