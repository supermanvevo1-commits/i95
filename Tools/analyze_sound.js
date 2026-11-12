// Prints loudness and engine firing frequency every quarter second, to find steady RPM holds and events:
//   node Tools/analyze_sound.js <file.wav> [cylinders=4] [stepSeconds=0.25]
const W = require('./wav');
const [, , file, cylS = '4', stepS = '0.25'] = process.argv;
const cyl = +cylS, step = +stepS;
const w = W.read(file), m = W.mono(w);
const f = 8, d = W.down(m, f), rate = w.rate / f;
console.log(`${file.split(/[\\/]/).pop()}: ${(w.frames / w.rate).toFixed(1)} s, ${w.rate} Hz, ${w.channels.length} ch`);
const rows = [];
for (let t = 0; t + step < w.frames / w.rate - .2; t += step) {
  const a = Math.floor(t * w.rate), len = Math.floor(step * w.rate);
  let s = 0; for (let i = 0; i < len; i++) s += m[a + i] * m[a + i];
  const db = 10 * Math.log10(s / len + 1e-12);
  // Firing frequency of a 4-cylinder runs ~25 Hz at idle to ~220 Hz near redline; RPM = Hz * 120 / cylinders.
  const [hz, clar] = W.pitch(d, Math.floor(t * rate), Math.floor(.08 * rate), rate, 18, 260);
  rows.push({ t, db, rpm: hz * 120 / cyl, clar });
}
for (const r of rows) console.log(`${r.t.toFixed(2).padStart(7)}s ${r.db.toFixed(1).padStart(6)} dB  rpm ${r.clar > .6 ? Math.round(r.rpm).toString().padStart(5) : '    -'}  clarity ${r.clar.toFixed(2)}`);
