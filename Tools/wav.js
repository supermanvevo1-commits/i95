// Minimal WAV reading and writing for the sound tools: PCM 16/24/32-bit and 32-bit float, any channel count.
const fs = require('fs');

function read(file) {
  const b = fs.readFileSync(file);
  if (b.toString('ascii', 0, 4) !== 'RIFF' || b.toString('ascii', 8, 12) !== 'WAVE') throw new Error('not a wav: ' + file);
  let p = 12, fmt = null, data = null;
  while (p + 8 <= b.length) {
    const id = b.toString('ascii', p, p + 4), size = b.readUInt32LE(p + 4), body = p + 8;
    if (id === 'fmt ') fmt = { format: b.readUInt16LE(body), channels: b.readUInt16LE(body + 2), rate: b.readUInt32LE(body + 4), bits: b.readUInt16LE(body + 14) };
    if (id === 'data') data = { start: body, size: Math.min(size, b.length - body) };
    p = body + size + (size & 1);
  }
  if (fmt.format === 0xFFFE) fmt.format = fmt.bits === 32 ? 3 : 1; // extensible: assume PCM or float by depth
  const bytes = fmt.bits / 8, frames = Math.floor(data.size / (bytes * fmt.channels));
  const ch = Array.from({ length: fmt.channels }, () => new Float32Array(frames));
  for (let i = 0; i < frames; i++) for (let c = 0; c < fmt.channels; c++) {
    const o = data.start + (i * fmt.channels + c) * bytes; let v;
    if (fmt.format === 3) v = b.readFloatLE(o);
    else if (fmt.bits === 16) v = b.readInt16LE(o) / 32768;
    else if (fmt.bits === 24) v = (b.readIntLE(o, 3)) / 8388608;
    else v = b.readInt32LE(o) / 2147483648;
    ch[c][i] = v;
  }
  return { rate: fmt.rate, channels: ch, frames };
}

// Mono or stereo 16-bit PCM.
function write(file, rate, chans) {
  const n = chans[0].length, c = chans.length, b = Buffer.alloc(44 + n * c * 2);
  b.write('RIFF', 0); b.writeUInt32LE(36 + n * c * 2, 4); b.write('WAVE', 8); b.write('fmt ', 12);
  b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(c, 22); b.writeUInt32LE(rate, 24);
  b.writeUInt32LE(rate * c * 2, 28); b.writeUInt16LE(c * 2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(n * c * 2, 40);
  for (let i = 0; i < n; i++) for (let k = 0; k < c; k++) b.writeInt16LE(Math.max(-32767, Math.min(32767, Math.round(chans[k][i] * 32767))), 44 + (i * c + k) * 2);
  fs.writeFileSync(file, b);
}

function mono(w) {
  const m = new Float32Array(w.frames);
  for (const c of w.channels) for (let i = 0; i < w.frames; i++) m[i] += c[i] / w.channels.length;
  return m;
}

// Fundamental frequency in [lo, hi] Hz by normalized autocorrelation over a window; returns [hz, clarity 0..1].
function pitch(x, start, len, rate, lo, hi) {
  const minLag = Math.floor(rate / hi), maxLag = Math.ceil(rate / lo);
  let e0 = 0; for (let i = 0; i < len; i++) e0 += x[start + i] * x[start + i];
  if (e0 < 1e-9) return [0, 0];
  let best = 0, bestLag = 0;
  for (let lag = minLag; lag <= maxLag; lag++) {
    let s = 0, e1 = 0;
    for (let i = 0; i < len; i++) { s += x[start + i] * x[start + i + lag]; e1 += x[start + i + lag] * x[start + i + lag]; }
    const r = s / Math.sqrt(e0 * e1 + 1e-12);
    if (r > best) { best = r; bestLag = lag; }
  }
  return [bestLag ? rate / bestLag : 0, best];
}

// Simple low-pass by moving average, then decimate, so pitch search on engine fundamentals is fast.
function down(x, factor) {
  const n = Math.floor(x.length / factor), o = new Float32Array(n);
  for (let i = 0; i < n; i++) { let s = 0; for (let k = 0; k < factor; k++) s += x[i * factor + k]; o[i] = s / factor; }
  return o;
}

module.exports = { read, write, mono, pitch, down };
