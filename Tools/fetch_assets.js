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
  textures: ['asphalt_02', 'worn_asphalt', 'road_damaged', 'gravel_road', 'asphalt_snow', 'grass_ground', 'brushed_concrete',
    // City surfaces (5.4): stucco grain for walls, clay and concrete roof tiles, tilt-up concrete.
    'grey_plaster', 'painted_plaster_wall', 'clay_roof_tiles_02', 'grey_roof_tiles', 'concrete_wall_004'],
  // "id@4k" asks for a sharper file; the sky fills the screen, so the main one needs it.
  hdris: ['derelict_highway_noon', 'evening_road_01', 'citrus_orchard_road_puresky', 'autumn_field_puresky', 'kloofendal_48d_partly_cloudy_puresky@4k'],
  models: ['concrete_road_barrier', 'street_lamp_01', 'pine_tree_01', 'grass_medium_01'],
  // Overpass is an open-license lookalike of the US highway sign typeface.
  fonts: [{ file: 'Overpass-Bold.ttf', url: 'https://github.com/RedHatOfficial/Overpass/raw/master/fonts/ttf/Overpass-Bold.ttf', credit: 'Overpass (font) by Delve Withrington / Red Hat, SIL Open Font License 1.1, https://overpassfont.org' },
    // Barlow Semi Condensed is the dashboard font for the HUD.
    { file: 'BarlowSemiCondensed-Bold.ttf', url: 'https://github.com/google/fonts/raw/main/ofl/barlowsemicondensed/BarlowSemiCondensed-Bold.ttf', credit: 'Barlow Semi Condensed (font) by Jeremy Tribby, SIL Open Font License 1.1, https://tribby.com/fonts/barlow/' },
    { file: 'BarlowSemiCondensed-Medium.ttf', url: 'https://github.com/google/fonts/raw/main/ofl/barlowsemicondensed/BarlowSemiCondensed-Medium.ttf', credit: 'Barlow Semi Condensed (font) by Jeremy Tribby, SIL Open Font License 1.1, https://tribby.com/fonts/barlow/' }]
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

// Plants and roadside props for the city (5.4b, 5.4c): Poly Haven CC0 at 1k, and three CC BY palms from Sketchfab.
const PLANTS = {
  polyhaven: ['island_tree_01', 'island_tree_02', 'island_tree_03', 'shrub_01', 'shrub_02', 'shrub_03', 'shrub_04', 'grass_bermuda_01', 'grass_medium_02', 'modular_chainlink_fence', 'modular_electricity_poles', 'fire_hydrant'],
  sketchfab: [
    { name: 'coconut_palm', uid: '26e787f2ff2e4c0fb004c3b0210805a3' },
    { name: 'curly_palm', uid: '00f2b57dd0e844edbeb116034fa471ec' },
    { name: 'date_palm', uid: 'ced3b31f14f940d0b7480a83e6f8c9c0' },
  ]
};

async function plants() {
  const base = path.join(ROOT, 'Assets', 'ThirdParty', 'PolyHaven');
  for (const id of PLANTS.polyhaven) {
    const files = (await json('https://api.polyhaven.com/files/' + id)).gltf;
    const g = (files['1k'] || files['2k']).gltf;
    await save(g.url, path.join(base, 'Models', id, id + '.gltf'));
    for (const [rel, file] of Object.entries(g.include || {})) await save(file.url, path.join(base, 'Models', id, rel));
    console.log('model', id); credit(id + ' (model), Poly Haven, CC0, https://polyhaven.com/a/' + id);
  }
  const token = process.env.SKETCHFAB_TOKEN;
  if (!token) throw new Error('Set the SKETCHFAB_TOKEN environment variable first.');
  for (const p of PLANTS.sketchfab) {
    const info = await json('https://api.sketchfab.com/v3/models/' + p.uid);
    const lic = (info.license && info.license.slug) || '';
    if (!/^cc0$|^by$|cc-by$|^cc-by-4/.test(lic) && !/Attribution$|CC0/.test((info.license || {}).label || '')) throw new Error('Blocked license ' + lic + ' for ' + info.name);
    const dest = path.join(ROOT, 'Tools', 'Downloads', 'plants', p.name + '.zip');
    if (!fs.existsSync(dest)) {
      const d = await json('https://api.sketchfab.com/v3/models/' + p.uid + '/download', { Authorization: 'Token ' + token });
      await save(d.gltf.url, dest);
      await sleep(800);
    }
    console.log('plant', p.name, info.name, info.license.label);
    credit(`"${info.name}" by ${info.user.displayName}, ${info.license.label}, ${info.viewerUrl}`);
  }
}

// Real recordings for 5.9, from the Sonniss GDC Game Audio Bundles (royalty-free, commercial use, no attribution
// required), fetched file by file from the Internet Archive's copies of the bundles.
const SOUNDS = [
  ['2016', 'Pole Position Production - Skoda Octavia 1.6 (2008)/Skoda_Octavia_1.6_t1_onboard_start_stop_idle_blip_steadies_Exhaust_Catalysator.wav'],
  ['2016', 'Pole Position Production - Dodge Challenger 426 Hemi - 1970/Dodge_Challenger_Hemi_-70_t3_onboard_driving_steady_rpms_shutdown_Interior_L.wav'],
  ['2017', 'Pole Position - Ford Ltd Crown Victoria 1990/Ford_LTD_t2_ext_medium_passbys_forward_stop_reverse_CSS5.wav'],
  ['2017', 'Pole Position - Volvo F12 truck 1992/Volvo_F12_truck_t1_exterior_medium_passby_DPA4021.wav'],
  ['2017', 'Pole Position - Volvo F12 truck 1992/Volvo_F12_truck_t11_exterior_slow_passby_Neumann.RSM191.wav'],
  ['2018', 'Pole Position - Volvo 850 SE 2.5 1996/volvo_850_t24_var_sfx_ext_very_distant_horn_various_D100.wav'],
  ['2018', 'Pole Position - Chevrolet Silverado 2012 Tow Truck/chevrolet_silverado_tow_truck_t24_onbrd_var_sfx_horn_various_engine_right_PZM.wav'],
  ['2018', 'Pole Position - Skids & Screeches Tarmac/Skids_Tarmac_t4_exterior_skidding_in_circles_MKH8060_stand.wav'],
  ['2018', 'Pole Position - Skids & Screeches Tarmac/Skids_Tarmac_t3_exterior_skidding_wet_tires_CSS5_handheld.wav'],
  ['2015', 'Coll Anderson - Car Destruction/EFX EXT Car Impacts and Scrapes 19 A.wav'],
  ['2015', 'Coll Anderson - Car Destruction/EFX EXT Metal Impact Smash Move 07 B.M.wav'],
  ['2015', 'Coll Anderson - Car Destruction/EFX EXT Shattered Glass Impact 10 A.wav'],
  ['2015', 'Mechanical Wave - Ambience and Weather/City Day Far Highway Rumble_MB 02.wav'],
  ['2019', '2496SoundEffects - Surround In the City 5.1/Auto Interior On Busy Freeway Driving Fast Windows Open, Car Bys Fast -5.1 LCRLsRsLf.wav'],
  ['2018', 'Fox Audio Post-Production - Planes Passbys – Interior-Exterior/Plane_Passbys_window_02.wav'],
  ['2018', 'Fox Audio Post-Production - Planes Passbys – Interior-Exterior/Plane_Passbys_window_08.wav'],
  ['2017', 'Griffin Sound Design - Chicago Rapid Transit - The L/L_TRAIN_by_open_air_track_center_of_expressway_under_tracks_persp002.wav'],
];

async function sounds() {
  for (const [year, rel] of SOUNDS) {
    const item = `sonniss-gdc-${year}-game-audio-bundle-normalized`;
    const url = `https://archive.org/download/${item}/${rel.split('/').map(encodeURIComponent).join('/')}`;
    const dest = path.join(ROOT, 'Assets', 'ThirdParty', 'Sonniss', rel);
    try { if (await save(url, dest)) console.log('sound', rel); else console.log('have', rel); }
    catch (e) { console.log('FAILED', rel, e.message); continue; }
    credit(`"${rel.split('/')[1]}" from "${rel.split('/')[0]}", Sonniss GDC ${year} Game Audio Bundle, royalty-free (https://sonniss.com/gameaudiogdc)`);
  }
}

({ environment, shortlist, vehicles, plants, sounds })[process.argv[2]]().catch(e => { console.error(e.message); process.exit(1); });
