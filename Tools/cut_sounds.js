// Cuts the downloaded recordings (node Tools/fetch_assets.js sounds) into the game's sounds:
// seamless loops (the end crossfaded into the start) and trimmed one-shots, at 48 kHz.
//   node Tools/cut_sounds.js
// Output: Assets/ThirdParty/Sonniss/Resources/Sound (git-ignored, like the recordings).
const fs = require('fs'), path = require('path'), W = require('./wav');
const root = path.join(__dirname, '..'), src = path.join(root, 'Assets/ThirdParty/Sonniss'), out = path.join(src, 'Resources/Sound');
fs.mkdirSync(out, { recursive: true });
const SKODA = 'Pole Position Production - Skoda Octavia 1.6 (2008)/Skoda_Octavia_1.6_t1_onboard_start_stop_idle_blip_steadies_Exhaust_Catalysator.wav';

// [output name, source, start s, end s, loop?, mono?, gain dB]
const CUTS = [
  // The player's engine: a 1.6 L four-cylinder holding steady RPMs, measured from its firing frequency.
  ['engine_0890', SKODA, 28, 31.5, true, true, 0],
  ['engine_2050', SKODA, 74, 77.5, true, true, 0],
  ['engine_3100', SKODA, 104.5, 108, true, true, 0],
  ['engine_4000', SKODA, 130, 133.5, true, true, 0],
  ['engine_5100', SKODA, 148, 151.5, true, true, 0],
  ['engine_6000', SKODA, 168, 171.5, true, true, 0],
  // Tire roar and wind: inside a car on a busy freeway, windows open.
  ['road_roar', '2496SoundEffects - Surround In the City 5.1/Auto Interior On Busy Freeway Driving Fast Windows Open, Car Bys Fast -5.1 LCRLsRsLf.wav', 30, 50, true, false, 0],
  // The highway heard from the land around it: a constant far rumble under everything.
  ['highway_bed', 'Mechanical Wave - Ambience and Weather/City Day Far Highway Rumble_MB 02.wav', 20, 50, true, false, 0],
  ['skid_loop', 'Pole Position - Skids & Screeches Tarmac/Skids_Tarmac_t4_exterior_skidding_in_circles_MKH8060_stand.wav', 30, 36, true, true, 0],
  ['passby_car', 'Pole Position - Ford Ltd Crown Victoria 1990/Ford_LTD_t2_ext_medium_passbys_forward_stop_reverse_CSS5.wav', 17, 24.5, false, true, 0],
  ['passby_truck', 'Pole Position - Volvo F12 truck 1992/Volvo_F12_truck_t11_exterior_slow_passby_Neumann.RSM191.wav', 5, 12.5, false, true, 0],
  ['horn_short_1', 'Pole Position - Chevrolet Silverado 2012 Tow Truck/chevrolet_silverado_tow_truck_t24_onbrd_var_sfx_horn_various_engine_right_PZM.wav', 0, .75, false, true, 0],
  ['horn_short_2', 'Pole Position - Chevrolet Silverado 2012 Tow Truck/chevrolet_silverado_tow_truck_t24_onbrd_var_sfx_horn_various_engine_right_PZM.wav', 4.2, 5, false, true, 0],
  ['horn_short_3', 'Pole Position - Chevrolet Silverado 2012 Tow Truck/chevrolet_silverado_tow_truck_t24_onbrd_var_sfx_horn_various_engine_right_PZM.wav', 10.2, 11.1, false, true, 0],
  ['horn_hold', 'Pole Position - Chevrolet Silverado 2012 Tow Truck/chevrolet_silverado_tow_truck_t24_onbrd_var_sfx_horn_various_engine_right_PZM.wav', 15.6, 17.6, true, true, 0],
  ['crash_hit', 'Coll Anderson - Car Destruction/EFX EXT Car Impacts and Scrapes 19 A.wav', 1.4, 4.3, false, true, 0],
  ['crash_smash', 'Coll Anderson - Car Destruction/EFX EXT Metal Impact Smash Move 07 B.M.wav', .6, 3.4, false, true, 0],
  ['crash_glass', 'Coll Anderson - Car Destruction/EFX EXT Shattered Glass Impact 10 A.wav', .3, 4.6, false, true, 0],
  ['plane_pass', 'Fox Audio Post-Production - Planes Passbys – Interior-Exterior/Plane_Passbys_window_02.wav', 3, 26.5, false, true, 0],
  ['plane_loop', 'Fox Audio Post-Production - Planes Passbys – Interior-Exterior/Plane_Passbys_window_02.wav', 13, 19, true, true, 0],
  ['train_loop', 'Griffin Sound Design - Chicago Rapid Transit - The L/L_TRAIN_by_open_air_track_center_of_expressway_under_tracks_persp002.wav', 6, 12, true, true, 0],
  ['train_pass', 'Griffin Sound Design - Chicago Rapid Transit - The L/L_TRAIN_by_open_air_track_center_of_expressway_under_tracks_persp002.wav', 3, 15.5, false, true, 0],
];

const RATE = 48000;
function resample(x, from, to) {
  const n = Math.floor(x.length * to / from), o = new Float32Array(n), k = from / to;
  // Average the source samples each output sample covers (a box low-pass), so nothing aliases going down.
  for (let i = 0; i < n; i++) { const a = i * k, b = a + k; let s = 0, c = 0; for (let j = Math.floor(a); j < Math.ceil(b) && j < x.length; j++) { s += x[j]; c++; } o[i] = c ? s / c : 0; }
  return o;
}

const cache = {};
for (const [name, file, a, b, loop, monoOut, gain] of CUTS) {
  const full = path.join(src, file);
  if (!fs.existsSync(full)) { console.log('missing', file); continue; }
  const w = cache[file] || (cache[file] = W.read(full));
  // Surround recordings: keep front left and right only; stereo stays stereo unless the cut is a positioned mono sound.
  let chans = w.channels.length > 2 ? [w.channels[0], w.channels[1]] : w.channels;
  if (monoOut && chans.length > 1) { const m = new Float32Array(w.frames); for (const c of chans) for (let i = 0; i < w.frames; i++) m[i] += c[i] / chans.length; chans = [m]; }
  const s0 = Math.floor(a * w.rate), s1 = Math.min(w.frames, Math.floor(b * w.rate));
  let parts = chans.map(c => resample(c.subarray(s0, s1), w.rate, RATE));
  const g = Math.pow(10, gain / 20);
  if (loop) {
    // The last 0.3 s fades out over the first 0.3 s fading in (equal power), and is dropped from the end.
    const x = Math.floor(.3 * RATE);
    parts = parts.map(p => {
      const o = new Float32Array(p.length - x);
      for (let i = 0; i < o.length; i++) {
        let v = p[i];
        if (i < x) { const t = i / x; v = p[i] * Math.sin(t * Math.PI / 2) + p[p.length - x + i] * Math.cos(t * Math.PI / 2); }
        o[i] = v * g;
      }
      return o;
    });
  } else {
    // One-shots: short fades so nothing clicks.
    const f = Math.floor(.01 * RATE), fo = Math.floor(.15 * RATE);
    parts = parts.map(p => p.map((v, i) => v * g * Math.min(1, i / f, (p.length - i) / fo)));
  }
  // Leave headroom: peak at -1 dB.
  let peak = 0; for (const p of parts) for (const v of p) peak = Math.max(peak, Math.abs(v));
  const norm = peak > 0 ? .89 / peak : 1;
  parts = parts.map(p => p.map(v => v * norm));
  W.write(path.join(out, name + '.wav'), RATE, parts);
  console.log(`${name}: ${(parts[0].length / RATE).toFixed(2)} s, ${parts.length} ch${loop ? ', loop' : ''}`);
}
