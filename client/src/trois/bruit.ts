/**
 * Bruits déterministes de la vue 3D : tout le détail que la simulation ne connaît pas (bosses du sol, place des arbres, méandres)
 * est tiré de la graine du monde, donc le même à chaque visite.
 */

/** Mélange d'entiers 32 bits (type « lowbias32 ») : rapide et sans motif visible. */
export function hacher(a: number, b = 0, c = 0): number {
  let h = (Math.imul(a, 0x27d4eb2d) ^ Math.imul(b, 0x165667b1) ^ Math.imul(c, 0x9e3779b1)) >>> 0;
  h ^= h >>> 16;
  h = Math.imul(h, 0x7feb352d);
  h ^= h >>> 15;
  h = Math.imul(h, 0x846ca68b);
  h ^= h >>> 16;
  return h >>> 0;
}

/** Nombre de 0 à 1 tiré d'un hachage. */
export const unite = (h: number): number => h / 4294967296;

/** Bruit de gradient 2D (Perlin), de −1 à 1 environ, de période 1 ; `graine` change tout le motif. */
export class Bruit {
  private readonly perm = new Uint8Array(512);
  private readonly gx = new Float32Array(256);
  private readonly gy = new Float32Array(256);

  constructor(graine: number) {
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) {
      const j = hacher(graine, i, 7) % (i + 1);
      [p[i], p[j]] = [p[j], p[i]];
    }
    for (let i = 0; i < 512; i++) this.perm[i] = p[i & 255];
    for (let i = 0; i < 256; i++) {
      const a = unite(hacher(graine, i, 13)) * Math.PI * 2;
      this.gx[i] = Math.cos(a);
      this.gy[i] = Math.sin(a);
    }
  }

  bruit(x: number, y: number): number {
    const xi = Math.floor(x);
    const yi = Math.floor(y);
    const fx = x - xi;
    const fy = y - yi;
    const X = xi & 255;
    const Y = yi & 255;
    const p = this.perm;
    const a = p[p[X] + Y];
    const b = p[p[X + 1] + Y];
    const c = p[p[X] + Y + 1];
    const d = p[p[X + 1] + Y + 1];
    const u = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
    const v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
    const n00 = this.gx[a] * fx + this.gy[a] * fy;
    const n10 = this.gx[b] * (fx - 1) + this.gy[b] * fy;
    const n01 = this.gx[c] * fx + this.gy[c] * (fy - 1);
    const n11 = this.gx[d] * (fx - 1) + this.gy[d] * (fy - 1);
    const x1 = n00 + u * (n10 - n00);
    const x2 = n01 + u * (n11 - n01);
    return (x1 + v * (x2 - x1)) * 1.41;
  }

  /** Somme d'octaves : la première de période `periode`, chacune deux fois plus fine et `gain` fois plus faible. */
  fbm(x: number, y: number, periode: number, octaves: number, gain = 0.5): number {
    let somme = 0;
    let amplitude = 1;
    let f = 1 / periode;
    let total = 0;
    for (let o = 0; o < octaves; o++) {
      somme += amplitude * this.bruit(x * f + o * 17.3, y * f - o * 31.7);
      total += amplitude;
      amplitude *= gain;
      f *= 2.03;
    }
    return somme / total;
  }
}

export const lisser = (a: number, b: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};
