// Makes the minimap's street data from the streets fetch_ground.js saved (OpenStreetMap, ODbL). No network.
//   node Tools/make_map.js miami_ftl
// Writes Assets/Resources/Data/Routes/<id>_map.json: each street as its width class and points in whole meters.
const fs = require('fs'), path = require('path');
const id = process.argv[2];
const root = path.join(__dirname, '..');
const ground = JSON.parse(fs.readFileSync(path.join(root, 'Tools/Data', id + '_ground.json'), 'utf8'));
// Drop points that add nothing at map scale (closer than 6 m to the last kept one), keeping each street's ends.
const roads = ground.roads.map(r => {
  const p = [];
  for (let i = 0; i + 1 < r.p.length; i += 2) {
    const x = Math.round(r.p[i]), y = Math.round(r.p[i + 1]), last = i + 2 >= r.p.length;
    if (p.length && !last && Math.hypot(x - p[p.length - 2], y - p[p.length - 1]) < 6) continue;
    p.push(x, y);
  }
  return { w: r.w, p };
}).filter(r => r.p.length >= 4);
const file = path.join(root, 'Assets/Resources/Data/Routes', id + '_map.json');
fs.writeFileSync(file, JSON.stringify({ note: 'Streets for the minimap, from OpenStreetMap (ODbL). Made by Tools/make_map.js.', roads }));
console.log(`saved ${roads.length} streets, ${(fs.statSync(file).size / 1e3).toFixed(0)} kB`);
