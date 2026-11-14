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
const PAS = 12;
const PAS_LUMIERE = 6;

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
 * Lumière du ciel dans la direction d (unitaire, y vers le haut), pour un observateur à `hauteur` m, éclairé par une ou deux
 * sources (le soleil d'intensité 1, la lune d'intensité `lune`). Écrit le résultat dans `sortieRgb`.
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
  const ds = tmax / PAS;
  let odR = 0;
  let odM = 0;
  const sR = [0, 0, 0];
  const sM = [0, 0, 0];
  const lR = [0, 0, 0];
  const lM = [0, 0, 0];
  for (let i = 0; i < PAS; i++) {
    const s = (i + 0.5) * ds;
    const x = ox + dx * s;
    const y = oy + dy * s;
    const z = oz + dz * s;
    const h = Math.sqrt(x * x + y * y + z * z) - RT;
    const hr = Math.exp(-h / HR) * ds;
    const hm = Math.exp(-h / HM) * ds;
    odR += hr;
    odM += hm;
    const ls = versLumiere(x, y, z, soleil[0], soleil[1], soleil[2]);
    if (ls) {
      for (let c = 0; c < 3; c++) {
        const att = Math.exp(-(BR[c] * (odR + ls[0]) + BM * 1.1 * (odM + ls[1])));
        sR[c] += att * hr;
        sM[c] += att * hm;
      }
    }
    if (lune && intensiteLune > 0) {
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
  for (let c = 0; c < 3; c++) sortieRgb[c] = sR[c] * BR[c] * pR + sM[c] * BM * pM + lR[c] * BR[c] * qR + lM[c] * BM * qM;
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
