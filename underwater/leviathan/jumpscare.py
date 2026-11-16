import sys, aud, numpy as np
args = sys.argv[sys.argv.index('--')+1:]
scream_f, boom_f, out = args[0], args[1], args[2]
SR = 48000
def load(f):
    s = aud.Sound(f).resample(SR, False)
    x = s.data()
    if x.ndim == 1: x = x[:, None]
    if x.shape[1] == 1: x = np.repeat(x, 2, axis=1)
    return x.astype(np.float64)
def repitch(x, p):
    n = int(len(x) / p); t = np.arange(n) * p
    return np.stack([np.interp(t, np.arange(len(x)), x[:, c]) for c in range(2)], axis=1)
def lowpass(x, hz):
    a = np.exp(-2 * np.pi * hz / SR); y = np.empty_like(x); z = np.zeros(2)
    for i in range(len(x)): z = (1 - a) * x[i] + a * z; y[i] = z
    return y
s = load(scream_f)
seg = s[int(2.62 * SR):int(8.6 * SR)]
main = repitch(seg, 0.9)
sub = lowpass(repitch(seg, 0.55), 900)
boom = repitch(load(boom_f), 0.75)
n = max(len(main), len(sub), len(boom))
mix = np.zeros((n, 2))
mix[:len(main)] += main * 1.0
mix[:len(sub)] += sub * 0.9
mix[:len(boom)] += boom * 2.2
# a 15 ms ramp so it hits at once without a click
r = int(0.015 * SR); mix[:r] *= np.linspace(0, 1, r)[:, None]
# drive into a soft limiter: dense and loud from the first frame
mix = np.tanh(mix * 2.4) / np.tanh(2.4)
f = int(1.8 * SR); mix[-f:] *= np.linspace(1, 0, f)[:, None] ** 2
mix *= 0.97 / np.abs(mix).max()
rms = np.sqrt((mix[: int(3 * SR)] ** 2).mean())
print('LEN %.2f s, first 3 s rms %.1f dBFS' % (len(mix) / SR, 20 * np.log10(rms)))
aud.Sound.buffer(mix.astype(np.float32), SR).write(out, rate=SR, channels=aud.CHANNELS_STEREO, format=aud.FORMAT_S16, container=aud.CONTAINER_WAV)
print('WROTE', out)
