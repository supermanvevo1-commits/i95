// Engine RPM from the spectrum: finds the strongest firing-frequency candidate whose harmonics line up
// (harmonic product spectrum), more robust than autocorrelation against octave errors.
//   node Tools/spectrum_rpm.js <file.wav> <cylinders> <from s> <to s> [step s]
const W = require('./wav');
const [, , file, cylS, fromS, toS, stepS = '1'] = process.argv;
const w = W.read(file), m = W.mono(w), cyl = +cylS;
const N = 16384, rate = w.rate, f = 4, d = W.down(m, f), r = rate / f;
function fft(re, im) { const n = re.length; for (let i = 1, j = 0; i < n; i++) { let bit = n >> 1; for (; j & bit; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { [re[i], re[j]] = [re[j], re[i]]; [im[i], im[j]] = [im[j], im[i]]; } }
  for (let len = 2; len <= n; len <<= 1) { const a = -2 * Math.PI / len; for (let i = 0; i < n; i += len) for (let k = 0; k < len / 2; k++) { const c = Math.cos(a * k), s = Math.sin(a * k), xr = re[i + k + len / 2] * c - im[i + k + len / 2] * s, xi = re[i + k + len / 2] * s + im[i + k + len / 2] * c; re[i + k + len / 2] = re[i + k] - xr; im[i + k + len / 2] = im[i + k] - xi; re[i + k] += xr; im[i + k] += xi; } } }
for (let t = +fromS; t < +toS; t += +stepS) {
  const a = Math.floor(t * r); if (a + N > d.length) break;
  const re = new Float64Array(N), im = new Float64Array(N);
  for (let i = 0; i < N; i++) re[i] = d[a + i] * (.5 - .5 * Math.cos(2 * Math.PI * i / N));
  fft(re, im);
  const mag = new Float64Array(N / 2); for (let i = 0; i < N / 2; i++) mag[i] = Math.log(1e-9 + Math.hypot(re[i], im[i]));
  const hz = i => i * r / N; let best = -1e9, bi = 0;
  for (let i = Math.ceil(18 / (r / N)); hz(i) < 300; i++) { let s = 0; for (let h = 1; h <= 4; h++) s += mag[Math.min(N / 2 - 1, i * h)]; if (s > best) { best = s; bi = i; } }
  let e = 0; for (let i = 0; i < N; i++) e += d[a + i] * d[a + i];
  console.log(`${t.toFixed(1).padStart(6)}s ${(10 * Math.log10(e / N + 1e-12)).toFixed(1).padStart(6)} dB  firing ${hz(bi).toFixed(1).padStart(6)} Hz  rpm ${Math.round(hz(bi) * 120 / cyl)}`);
}
