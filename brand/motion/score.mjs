// An original, sample-free score. The fold has a little friction, a short
// contact transient, and two resonances that are allowed to ring together.
const rate = 48000;
const tau = 2 * Math.PI;

function envelope(age, attack, decay) {
  if (age < 0) return 0;
  return (1 - Math.exp(-age / attack)) * Math.exp(-age / decay);
}

function resonance(time, start, frequency, gain, decay) {
  const age = time - start;
  if (age < 0) return 0;
  const phase = tau * frequency * age;
  const mallet = Math.exp(-age / 0.09);
  return gain * envelope(age, 0.009, decay) * (
    Math.sin(phase + 0.22 * mallet * Math.sin(phase * 1.5))
    + 0.32 * Math.exp(-age / 0.23) * Math.sin(2.01 * phase)
    + 0.12 * mallet * Math.sin(3.92 * phase)
  );
}

function bandNoise(seed) {
  let high = 0;
  let low = 0;
  return () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    const white = seed / 2147483648 - 1;
    high += 0.26 * (white - high);
    low += 0.028 * (white - low);
    return high - low;
  };
}

export function createSound(duration) {
  const samples = Math.round(duration * rate);
  const direct = new Float64Array(samples);
  const grain = bandNoise(8181);
  const room = bandNoise(9787);
  const stereo = [new Float64Array(samples), new Float64Array(samples)];
  const reflection = (i, delay) => direct[i - Math.round(rate * delay)] || 0;
  let peak = 0;
  for (let i = 0; i < samples; i++) {
    const t = i / rate;
    const gesture = t < 1.04 ? Math.sin(Math.PI * t / 1.04) ** 2 : 0;
    const pressure = 0.8 + 0.2 * Math.cos(18 * t + 26 * t * t);
    const fiber = grain() * pressure * (
      0.31 * gesture + 0.13 * envelope(t - 0.63, 0.002, 0.045)
    );
    const body = resonance(t, 0.31, 174.61, 0.09, 0.27)
      + resonance(t, 0.66, 261.63, 0.22, 0.50)
      + resonance(t, 1.49, 392, 0.18, 0.53)
      + resonance(t, 1.52, 196, 0.13, 0.40);
    direct[i] = fiber + body;
    const space = room() * gesture * 0.051;
    const fade = Math.min(1, (duration - t) / 0.14);
    stereo[0][i] = fade * (direct[i] + space + 0.13 * reflection(i, 0.031) + 0.055 * reflection(i, 0.099));
    stereo[1][i] = fade * (body + (0.72 + 0.47 * t) * fiber - space
      + 0.12 * reflection(i, 0.047) + 0.05 * reflection(i, 0.129));
    peak = Math.max(peak, Math.abs(stereo[0][i]), Math.abs(stereo[1][i]));
  }

  const channels = 2;
  const frameBytes = 2 * channels;
  const wav = Buffer.alloc(44 + samples * frameBytes);
  wav.write('RIFF');
  wav.writeUInt32LE(wav.length - 8, 4);
  wav.write('WAVEfmt ', 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(channels, 22);
  wav.writeUInt32LE(rate, 24);
  wav.writeUInt32LE(rate * frameBytes, 28);
  wav.writeUInt16LE(frameBytes, 32);
  wav.writeUInt16LE(16, 34);
  wav.write('data', 36);
  wav.writeUInt32LE(samples * frameBytes, 40);
  const gain = 32767 * 0.59 / peak;
  for (let i = 0; i < samples; i++) {
    for (let c = 0; c < channels; c++) {
      wav.writeInt16LE(Math.round(stereo[c][i] * gain), 44 + frameBytes * i + 2 * c);
    }
  }
  return wav;
}
