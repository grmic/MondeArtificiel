/**
 * Atmosphère (0.41.3, proposition C1) : diffusion de Rayleigh (l'air, qui bleuit le ciel) et de Mie (les poussières et la vapeur,
 * qui blanchissent l'horizon et font le halo du soleil), intégrées le long du regard comme dans le modèle de Nishita (1993), avec
 * les constantes usuelles de l'atmosphère terrestre. Le calcul se fait sur le processeur, dans une petite texture du ciel refaite
 * peu à peu ; la même fonction donne la couleur du soleil (ce qui reste de sa lumière après la traversée de l'air), la lumière
 * du ciel qui éclaire le sol et la couleur de la brume.
 */

const RT = 6360e3;
const RA = 6420e3;
/** Hauteurs d'échelle de l'air (Rayleigh) et des aérosols (Mie), en mètres. */
const HR = 7994;
const HM = 1200;
const BR = [5.8e-6, 13.5e-6, 33.1e-6];
const BM = 21e-6;
const G = 0.76;
const PAS = 16;
const PAS_LUMIERE = 6;
/** Pouvoir réfléchissant moyen du sol (forêts, sols, mers mêlés) : le sol renvoie un peu de lumière dans l'air. */
const ALBEDO = 0.25;

export type Rgb = [number, number, number];

/** Distance à la sortie d'une sphère de rayon r (centrée sur la Terre), depuis o le long de d (unitaire) ; -1 si aucune. */
function sortie(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number, r: number): number {
  const b = ox * dx + oy * dy + oz * dz;
  const c = ox * ox + oy * oy + oz * oz - r * r;
  const disc = b * b - c;
  if (disc < 0) return -1;
  return -b + Math.sqrt(disc);
}

/** Distance au sol le long de d, ou -1 si le rayon ne touche pas la Terre. */
function sol(ox: number, oy: number, oz: number, dx: number, dy: number, dz: number): number {
  const b = ox * dx + oy * dy + oz * dz;
  const c = ox * ox + oy * oy + oz * oz - RT * RT;
  const disc = b * b - c;
  if (disc < 0) return -1;
  const t = -b - Math.sqrt(disc);
  return t > 0 ? t : -1;
}

/** Profondeurs optiques (Rayleigh, Mie) de o vers la lumière l, ou null si la Terre fait de l'ombre. */
function versLumiere(ox: number, oy: number, oz: number, lx: number, ly: number, lz: number): [number, number] | null {
  if (sol(ox, oy, oz, lx, ly, lz) > 0) return null;
  const t = sortie(ox, oy, oz, lx, ly, lz, RA);
  const ds = t / PAS_LUMIERE;
  let r = 0;
  let m = 0;
  for (let j = 0; j < PAS_LUMIERE; j++) {
    const s = (j + 0.5) * ds;
    const x = ox + lx * s;
    const y = oy + ly * s;
    const z = oz + lz * s;
    const h = Math.sqrt(x * x + y * y + z * z) - RT;
    r += Math.exp(-h / HR) * ds;
    m += Math.exp(-h / HM) * ds;
  }
  return [r, m];
}

const phaseR = (mu: number) => (3 / (16 * Math.PI)) * (1 + mu * mu);
const phaseM = (mu: number) => (3 / (8 * Math.PI)) * ((1 - G * G) * (1 + mu * mu)) / ((2 + G * G) * Math.pow(1 + G * G - 2 * G * mu, 1.5));

/**
 * Diffusions multiples (0.41.6, proposition G1) : la lumière qui a déjà rebondi une fois ou plus dans l'air, selon la méthode de
 * Hillaire (« A Scalable and Production Ready Sky and Atmosphere Rendering Technique », 2020). Pour chaque hauteur et chaque
 * hauteur du soleil, on intègre sur toutes les directions la lumière d'une diffusion (phase isotrope) et la part de lumière
 * renvoyée ; la somme de la série géométrique L2 / (1 − f) donne la luminance des ordres supérieurs. Table calculée une fois.
 */
const MS_H = 24;
const MS_MU = 32;
const MS_HAUT = RA - RT;
let tableMs: Float32Array | null = null;

function directionsSphere(n: number): number[][] {
  const r: number[][] = [];
  const or = Math.PI * (3 - Math.sqrt(5));
  for (let i = 0; i < n; i++) {
    const y = 1 - ((i + 0.5) / n) * 2;
    const k = Math.sqrt(1 - y * y);
    r.push([Math.cos(or * i) * k, y, Math.sin(or * i) * k]);
  }
  return r;
}

function calculerTableMs(): Float32Array {
  const t = new Float32Array(MS_H * MS_MU * 3);
  const dirs = directionsSphere(64);
  const N = 20;
  const iso = 1 / (4 * Math.PI);
  for (let ih = 0; ih < MS_H; ih++) {
    const h = ((ih / (MS_H - 1)) ** 2) * MS_HAUT;
    const oy = RT + Math.max(h, 1);
    for (let im = 0; im < MS_MU; im++) {
      const mu = (im / (MS_MU - 1)) * 2 - 1;
      const sx = Math.sqrt(Math.max(0, 1 - mu * mu));
      const sy = mu;
      const L2 = [0, 0, 0];
      const F = [0, 0, 0];
      for (const [dx, dy, dz] of dirs) {
        let tmax = sortie(0, oy, 0, dx, dy, dz, RA);
        const ts = sol(0, oy, 0, dx, dy, dz);
        if (ts > 0) tmax = Math.min(tmax, ts);
        const ds = tmax / N;
        let odR = 0;
        let odM = 0;
        for (let i = 0; i < N; i++) {
          const s = (i + 0.5) * ds;
          const x = dx * s;
          const y = oy + dy * s;
          const z = dz * s;
          const hh = Math.sqrt(x * x + y * y + z * z) - RT;
          const hr = Math.exp(-hh / HR) * ds;
          const hm = Math.exp(-hh / HM) * ds;
          odR += hr;
          odM += hm;
          const ls = versLumiere(x, y, z, sx, sy, 0);
          for (let c = 0; c < 3; c++) {
            const tv = Math.exp(-(BR[c] * odR + BM * 1.1 * odM));
            const diff = BR[c] * hr + BM * hm;
            F[c] += tv * diff;
            if (ls) L2[c] += tv * diff * Math.exp(-(BR[c] * ls[0] + BM * 1.1 * ls[1])) * iso;
          }
        }
        // Le sol, s'il est touché, renvoie la lumière du soleil (lambertien).
        if (ts > 0) {
          const x = dx * ts;
          const y = oy + dy * ts;
          const z = dz * ts;
          const r = Math.sqrt(x * x + y * y + z * z);
          const cos = (x * sx + y * sy) / r;
          const ls = cos > 0 ? versLumiere(x, y, z, sx, sy, 0) : null;
          if (ls) for (let c = 0; c < 3; c++) {
            const tv = Math.exp(-(BR[c] * odR + BM * 1.1 * odM));
            L2[c] += tv * Math.exp(-(BR[c] * ls[0] + BM * 1.1 * ls[1])) * cos * (ALBEDO / Math.PI);
          }
        }
      }
      const k = (ih * MS_MU + im) * 3;
      for (let c = 0; c < 3; c++) {
        const l2 = L2[c] / dirs.length;
        const f = (F[c] * iso * 4 * Math.PI) / dirs.length;
        t[k + c] = l2 / Math.max(1e-4, 1 - f);
      }
    }
  }
  return t;
}

/** Luminance des diffusions multiples (par unité d'éclairement de l'astre) à la hauteur h pour un astre de hauteur μ. */
function multiple(h: number, mu: number, sortieRgb: number[]): void {
  if (!tableMs) tableMs = calculerTableMs();
  const fh = Math.sqrt(Math.max(0, Math.min(1, h / MS_HAUT))) * (MS_H - 1);
  const fm = ((Math.max(-1, Math.min(1, mu)) + 1) / 2) * (MS_MU - 1);
  const h0 = Math.min(MS_H - 2, Math.floor(fh));
  const m0 = Math.min(MS_MU - 2, Math.floor(fm));
  const a = fh - h0;
  const b = fm - m0;
  for (let c = 0; c < 3; c++) {
    const v00 = tableMs[(h0 * MS_MU + m0) * 3 + c];
    const v01 = tableMs[(h0 * MS_MU + m0 + 1) * 3 + c];
    const v10 = tableMs[((h0 + 1) * MS_MU + m0) * 3 + c];
    const v11 = tableMs[((h0 + 1) * MS_MU + m0 + 1) * 3 + c];
    sortieRgb[c] = (v00 * (1 - b) + v01 * b) * (1 - a) + (v10 * (1 - b) + v11 * b) * a;
  }
}

/**
 * Lumière du ciel dans la direction d (unitaire, y vers le haut), pour un observateur à `hauteur` m, éclairé par une ou deux
 * sources (le soleil d'intensité 1, la lune d'intensité `lune`). Écrit le résultat dans `sortieRgb`. Une diffusion calculée le
 * long du regard (pas serrés près de l'œil), plus les diffusions multiples (table) et, vers le bas, le sol éclairé.
 */
export function ciel(d: [number, number, number], hauteur: number, soleil: [number, number, number], lune: [number, number, number] | null, intensiteLune: number, sortieRgb: Rgb): void {
  const ox = 0;
  const oy = RT + Math.max(1, Math.min(hauteur, 60000));
  const oz = 0;
  const [dx, dy, dz] = d;
  let tmax = sortie(ox, oy, oz, dx, dy, dz, RA);
  const tsol = sol(ox, oy, oz, dx, dy, dz);
  if (tsol > 0) tmax = Math.min(tmax, tsol);
  if (tmax <= 0) {
    sortieRgb[0] = sortieRgb[1] = sortieRgb[2] = 0;
    return;
  }
  const avecLune = lune !== null && intensiteLune > 0;
  let odR = 0;
  let odM = 0;
  const sR = [0, 0, 0];
  const sM = [0, 0, 0];
  const lR = [0, 0, 0];
  const lM = [0, 0, 0];
  const ms = [0, 0, 0];
  const msS = [0, 0, 0];
  const msL = [0, 0, 0];
  let avant = 0;
  for (let i = 0; i < PAS; i++) {
    // Pas de plus en plus longs : l'air proche (dense, et qui fait la brume de l'horizon) est mieux suivi.
    const t1 = tmax * ((i + 1) / PAS) ** 2;
    const ds = t1 - avant;
    const s = (avant + t1) / 2;
    avant = t1;
    const x = ox + dx * s;
    const y = oy + dy * s;
    const z = oz + dz * s;
    const r = Math.sqrt(x * x + y * y + z * z);
    const h = r - RT;
    const hr = Math.exp(-h / HR) * ds;
    const hm = Math.exp(-h / HM) * ds;
    odR += hr;
    odM += hm;
    const ls = versLumiere(x, y, z, soleil[0], soleil[1], soleil[2]);
    multiple(h, (x * soleil[0] + y * soleil[1] + z * soleil[2]) / r, msS);
    if (avecLune) multiple(h, (x * lune[0] + y * lune[1] + z * lune[2]) / r, msL);
    for (let c = 0; c < 3; c++) {
      const tv = Math.exp(-(BR[c] * odR + BM * 1.1 * odM));
      ms[c] += tv * (BR[c] * hr + BM * hm) * (msS[c] + (avecLune ? msL[c] * intensiteLune : 0));
      if (ls) {
        const att = tv * Math.exp(-(BR[c] * ls[0] + BM * 1.1 * ls[1]));
        sR[c] += att * hr;
        sM[c] += att * hm;
      }
    }
    if (avecLune) {
      const ll = versLumiere(x, y, z, lune[0], lune[1], lune[2]);
      if (ll) {
        for (let c = 0; c < 3; c++) {
          const att = Math.exp(-(BR[c] * (odR + ll[0]) + BM * 1.1 * (odM + ll[1])));
          lR[c] += att * hr;
          lM[c] += att * hm;
        }
      }
    }
  }
  const mu = dx * soleil[0] + dy * soleil[1] + dz * soleil[2];
  const pR = phaseR(mu);
  const pM = phaseM(mu);
  let qR = 0;
  let qM = 0;
  if (lune) {
    const ml = dx * lune[0] + dy * lune[1] + dz * lune[2];
    qR = phaseR(ml) * intensiteLune;
    qM = phaseM(ml) * intensiteLune;
  }
  for (let c = 0; c < 3; c++) sortieRgb[c] = sR[c] * BR[c] * pR + sM[c] * BM * pM + lR[c] * BR[c] * qR + lM[c] * BM * qM + ms[c];
  // Vers le bas : le sol éclairé par le soleil (et le ciel, qui double à peu près sa lumière), vu à travers l'air.
  if (tsol > 0 && tsol <= tmax + 1) {
    const x = ox + dx * tsol;
    const y = oy + dy * tsol;
    const z = oz + dz * tsol;
    const r = Math.sqrt(x * x + y * y + z * z);
    const cos = Math.max(0, (x * soleil[0] + y * soleil[1] + z * soleil[2]) / r);
    const ls = cos > 0 ? versLumiere(x, y, z, soleil[0], soleil[1], soleil[2]) : null;
    for (let c = 0; c < 3; c++) {
      const tv = Math.exp(-(BR[c] * odR + BM * 1.1 * odM));
      const direct = ls ? Math.exp(-(BR[c] * ls[0] + BM * 1.1 * ls[1])) * cos : 0;
      sortieRgb[c] += tv * (ALBEDO / Math.PI) * (direct + 0.2 * Math.max(0, soleil[1] + 0.1));
    }
  }
}

/** Ce qui reste de la lumière d'un astre dans la direction l, vue d'une hauteur donnée (0 sous l'horizon, rougi près de lui). */
export function transmission(l: [number, number, number], hauteur: number, sortieRgb: Rgb): void {
  const oy = RT + Math.max(1, Math.min(hauteur, 60000));
  // Sous l'horizon, l'astre se couche peu à peu (son disque a une largeur, et le relief n'est pas une sphère parfaite).
  const od = versLumiere(0, oy, 0, l[0], Math.max(l[1], 0.0005), l[2]);
  const fondu = Math.max(0, Math.min(1, (l[1] + 0.02) / 0.025));
  for (let c = 0; c < 3; c++) sortieRgb[c] = od ? Math.exp(-(BR[c] * od[0] + BM * 1.1 * od[1])) * fondu : 0;
}

/** Ligne v de la texture (0 en bas, 1 en haut) pour une élévation e (radians) : plus de lignes près de l'horizon. */
export const ligneDe = (e: number) => 0.5 + 0.5 * Math.sign(e) * Math.sqrt(Math.min(1, Math.abs(e) / (Math.PI / 2)));
/** Élévation d'une ligne v. */
export const elevationDe = (v: number) => Math.sign(v - 0.5) * ((v - 0.5) * 2) ** 2 * (Math.PI / 2);
