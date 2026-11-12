// Loudness envelope summary: the loudest moments and the quiet floor, to locate events in a recording.
//   node Tools/peaks.js <file.wav> [step=0.1] [count=12]
const W = require('./wav');
const [, , file, stepS = '0.1', countS = '12'] = process.argv;
const w = W.read(file), m = W.mono(w), step = +stepS, n = Math.floor(w.frames / w.rate / step);
const db = []; for (let k = 0; k < n; k++) { const a = Math.floor(k * step * w.rate), len = Math.floor(step * w.rate); let s = 0; for (let i = 0; i < len; i++) s += m[a + i] * m[a + i]; db.push(10 * Math.log10(s / len + 1e-12)); }
const sorted = [...db].sort((a, b) => a - b), floor = sorted[Math.floor(n * .1)], med = sorted[Math.floor(n * .5)];
const peaks = []; for (let k = 1; k < n - 1; k++) if (db[k] >= db[k - 1] && db[k] >= db[k + 1] && db[k] > med + 6) peaks.push(k);
peaks.sort((a, b) => db[b] - db[a]); const pick = [];
for (const k of peaks) { if (pick.every(j => Math.abs(j - k) * step > 2)) pick.push(k); if (pick.length >= +countS) break; }
pick.sort((a, b) => a - b);
console.log(`${file.split(/[\/]/).pop()} | ${(w.frames / w.rate).toFixed(1)} s, ${w.channels.length} ch, ${w.rate} Hz | floor ${floor.toFixed(1)} dB, median ${med.toFixed(1)} dB`);
for (const k of pick) { let a = k, b = k; while (a > 0 && db[a] > db[k] - 12) a--; while (b < n - 1 && db[b] > db[k] - 12) b++; console.log(`  peak ${(k * step).toFixed(1)} s ${db[k].toFixed(1)} dB, within 12 dB from ${(a * step).toFixed(1)} to ${(b * step).toFixed(1)} s`); }
