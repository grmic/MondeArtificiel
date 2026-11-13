import type { Carte } from "../carte";
import { Bruit, hacher, lisser, unite } from "./bruit";

/** Ce que la vue 3D lit du terrain (hub « Terrain », tableaux déjà décodés). */
export interface TerrainVue {
  roches: Uint8Array;
  pente: Uint8Array;
  debit: Uint8Array;
  profondeur: Uint8Array;
  lits: number[];
}

/** Températures du jour par case (hub « Temperatures »). */
export interface Temperatures {
  jour: number;
  largeur: number;
  hauteur: number;
  midi: number[];
  aube: number[];
}

/** Résultat d'un échantillon du sol : hauteur (m, 0 = niveau de la mer), surface d'eau douce (NaN : aucune), lit mouillé (0 à 1). */
export interface Echantillon {
  h: number;
  eau: number;
  lit: number;
}

// Biomes (ordre de l'énumération Biome du serveur).
export const OCEAN = 0;
export const EAU_PEU_PROFONDE = 1;
export const LAC = 2;
export const RIVIERE = 3;
export const PLAGE = 4;
export const MARAIS = 5;
export const PLAINE = 6;
export const CONIFERES = 7;
export const TROPICALE = 8;
export const DESERT = 9;
export const MONTAGNE = 10;
export const SOMMET = 11;
export const FEUILLUS = 12;
export const MANGROVE = 13;
export const LANDE = 14;

/** Couleur du sol de chaque biome (sRGB 0-255), vue de près : sous-bois, fougères, sable, roche. */
const SOLS: [number, number, number][] = [
  [150, 140, 110], [180, 165, 125], [110, 100, 80], [120, 110, 85], [222, 206, 160], [86, 98, 58],
  [104, 136, 62], [58, 80, 46], [52, 96, 42], [206, 174, 116], [128, 118, 104], [236, 238, 244],
  [78, 110, 52], [72, 88, 56], [140, 126, 82],
];

/** Couleur des roches à nu (ordre de l'énumération Roche) : granite, basalte, volcanique, calcaire, craie, grès, schiste, alluvions, évaporites. */
const ROCHES: [number, number, number][] = [
  [150, 135, 130], [70, 70, 74], [90, 62, 56], [176, 170, 156], [222, 220, 206], [178, 132, 92], [96, 100, 108], [140, 124, 100], [214, 210, 200],
];

const lineaire = (c: number) => Math.pow(c / 255, 2.2);
const versLineaire = (t: [number, number, number][]) => t.map(([r, g, b]) => [lineaire(r), lineaire(g), lineaire(b)] as const);
const SOLS_L = versLineaire(SOLS);
const ROCHES_L = versLineaire(ROCHES);
const NEIGE_L = [lineaire(240), lineaire(244), lineaire(250)] as const;
const LIT_L = [lineaire(70), lineaire(84), lineaire(80)] as const;
const SABLE_L = [lineaire(214), lineaire(198), lineaire(150)] as const;

/** Un bras de rivière entre deux cases : extrémités (m), demi-largeur et profondeur à chaque bout, surface de l'eau à chaque bout. */
interface Bras {
  ax: number;
  az: number;
  bx: number;
  bz: number;
  la: number;
  lb: number;
  pa: number;
  pb: number;
  sa: number;
  sb: number;
}

/**
 * Le relief de la vue 3D, à l'échelle du mètre. La simulation pense en cases de 800 m : on part de l'altitude de chaque case
 * (interpolée sans à-coups), puis on ajoute des bosses d'autant plus fortes que la case est pentue, les lacs et les grandes rivières
 * (cases d'eau), et le lit des rivières tracé de case en case avec des méandres. Tout vient de la graine : le même à chaque visite.
 */
export class Relief {
  readonly l: number;
  readonly hc: number;
  readonly m: number;
  readonly largeurM: number;
  readonly hauteurM: number;
  readonly biomes: Uint8Array;
  readonly plantes: Uint8Array;
  private readonly base: Float32Array;
  private readonly amplitude: Float32Array;
  private readonly eau: Float32Array;
  private readonly niveau: Float32Array;
  private readonly roches: Uint8Array;
  private readonly bras: Bras[] = [];
  private readonly brasParCase: Map<number, number[]> = new Map();
  private readonly b1: Bruit;
  private readonly b2: Bruit;
  private readonly b3: Bruit;
  private temperatures: Temperatures | null = null;
  /** Saison (0 printemps … 3 hiver) et avancée dans la saison (0 à 1), pour les couleurs. */
  saison = 0;
  dansSaison = 0.5;
  private readonly camps = new Map<number, [number, number]>();

  constructor(carte: Carte, terrain: TerrainVue | null) {
    this.l = carte.largeur;
    this.hc = carte.hauteur;
    this.m = carte.kilometresParCase * 1000;
    this.largeurM = this.l * this.m;
    this.hauteurM = this.hc * this.m;
    this.biomes = carte.biomes;
    this.plantes = carte.plantes;
    this.roches = terrain?.roches ?? new Uint8Array(this.l * this.hc);
    this.b1 = new Bruit(carte.graine);
    this.b2 = new Bruit(carte.graine ^ 0x5bd1e995);
    this.b3 = new Bruit(carte.graine ^ 0x1b873593);
    const n = this.l * this.hc;
    this.base = new Float32Array(n);
    this.amplitude = new Float32Array(n);
    this.eau = new Float32Array(n);
    this.niveau = new Float32Array(n);
    const mpu = carte.metresParUnite;
    for (let i = 0; i < n; i++) {
      const b = carte.biomes[i];
      const alt = carte.altitudes[i] - carte.niveauMer;
      let h: number;
      if (b === OCEAN || b === EAU_PEU_PROFONDE) {
        const p = terrain ? terrain.profondeur[i] * 10 : -alt * mpu;
        h = -Math.max(b === OCEAN ? 12 : 3, p, -alt * mpu);
      } else if (b === PLAGE) h = 3;
      else if (b === MANGROVE) h = 1.5;
      else h = Math.max(2, alt * mpu - 30);
      if (b === MARAIS) h = Math.min(h, 8);
      this.base[i] = h;
      const pente = terrain ? terrain.pente[i] * mpu : 40;
      let a = Math.min(45, 2 + 0.12 * pente);
      if (b === MONTAGNE || b === SOMMET) a *= 1.5;
      else if (b === PLAGE || b === MARAIS || b === MANGROVE) a *= 0.3;
      else if (b <= RIVIERE) a *= 0.25;
      this.amplitude[i] = a;
      if (b === LAC || b === RIVIERE) {
        this.eau[i] = 1;
        this.niveau[i] = h;
      }
    }
    if (terrain) this.tracerRivieres(terrain);
  }

  private tracerRivieres(terrain: TerrainVue): void {
    const decalage = (i: number): [number, number] => {
      const x = i % this.l;
      const z = Math.floor(i / this.l);
      return [(x + 0.5 + (unite(hacher(i, 3, 11)) - 0.5) * 0.36) * this.m, (z + 0.5 + (unite(hacher(i, 5, 17)) - 0.5) * 0.36) * this.m];
    };
    const largeur = (d: number) => {
      const q = Math.pow(10, (d - 1) / 40) - 1;
      return [Math.min(80, Math.max(1, 1.5 * Math.sqrt(q))), Math.min(6, Math.max(0.4, 0.35 * Math.pow(q, 0.33)))];
    };
    for (let k = 0; k + 1 < terrain.lits.length; k += 2) {
      const a = terrain.lits[k];
      const b = terrain.lits[k + 1];
      if (this.eau[a] && this.eau[b]) continue;
      const [ax, az] = decalage(a);
      const [bx, bz] = decalage(b);
      const [la, pa] = largeur(terrain.debit[a]);
      const [lb, pb] = largeur(Math.max(terrain.debit[a], terrain.debit[b] || terrain.debit[a]));
      const sa = this.hauteurDeBase(ax, az) - 1;
      const sb = Math.min(sa, this.hauteurDeBase(bx, bz) - 1);
      const indice = this.bras.length;
      this.bras.push({ ax, az, bx, bz, la, lb, pa, pb, sa, sb });
      for (const c of [a, b]) {
        const liste = this.brasParCase.get(c);
        if (liste) liste.push(indice);
        else this.brasParCase.set(c, [indice]);
      }
    }
  }

  definirTemperatures(t: Temperatures | null): void {
    this.temperatures = t && t.largeur === this.l && t.hauteur === this.hc ? t : null;
  }

  private valeur(tableau: Float32Array, x: number, z: number, horsCarte: number): number {
    if (x < 0 || z < 0 || x >= this.l || z >= this.hc) return horsCarte;
    return tableau[z * this.l + x];
  }

  /** Altitude des cases interpolée (Catmull-Rom), sans les bosses ni l'eau. */
  hauteurDeBase(x: number, z: number): number {
    const u = x / this.m - 0.5;
    const v = z / this.m - 0.5;
    const iu = Math.floor(u);
    const iv = Math.floor(v);
    const fu = u - iu;
    const fv = v - iv;
    const cr = (p0: number, p1: number, p2: number, p3: number, t: number) =>
      p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
    const ligne = (j: number) =>
      cr(this.valeur(this.base, iu - 1, j, -200), this.valeur(this.base, iu, j, -200), this.valeur(this.base, iu + 1, j, -200),
        this.valeur(this.base, iu + 2, j, -200), fu);
    return cr(ligne(iv - 1), ligne(iv), ligne(iv + 1), ligne(iv + 2), fv);
  }

  /** Interpolation bilinéaire d'un tableau par case, aux coordonnées (en cases) u, v. */
  private bilineaire(tableau: Float32Array, u: number, v: number, horsCarte: number): number {
    const iu = Math.floor(u);
    const iv = Math.floor(v);
    const fu = u - iu;
    const fv = v - iv;
    const a = this.valeur(tableau, iu, iv, horsCarte);
    const b = this.valeur(tableau, iu + 1, iv, horsCarte);
    const c = this.valeur(tableau, iu, iv + 1, horsCarte);
    const d = this.valeur(tableau, iu + 1, iv + 1, horsCarte);
    return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
  }

  /** Coordonnées en cases, légèrement ondulées (limites de biomes et rivages moins droits). */
  private onduler(x: number, z: number): [number, number] {
    const u = x / this.m - 0.5 + this.b2.fbm(x, z, 900, 3) * 0.32;
    const v = z / this.m - 0.5 + this.b3.fbm(x, z, 900, 3) * 0.32;
    return [u, v];
  }

  /**
   * Le sol au point (x, z) en mètres. `finesse` : écart entre deux points du maillage qui demande (m) ; les rivières plus étroites
   * restent visibles comme un lit mouillé.
   */
  echantillonner(x: number, z: number, sortie: Echantillon, finesse = 1): Echantillon {
    const base = this.hauteurDeBase(x, z);
    const [u, v] = this.onduler(x, z);
    const amp = this.bilineaire(this.amplitude, x / this.m - 0.5, z / this.m - 0.5, 0.5);
    const cote = Math.min(1, Math.max(0.12, Math.abs(base) / 20));
    let h = base + (this.b1.fbm(x, z, 700, 5) * amp + this.b1.fbm(x, z, 37, 2) * 0.5 * Math.min(1, amp / 4)) * cote;
    let eau = NaN;
    let lit = 0;

    // Lacs et grandes rivières (cases d'eau douce).
    const w = this.bilineaire(this.eau, u, v, 0);
    if (w > 0.01) {
      const iu = Math.floor(u);
      const iv = Math.floor(v);
      const fu = u - iu;
      const fv = v - iv;
      let somme = 0;
      let poids = 0;
      for (const [dx, dz, p] of [[0, 0, (1 - fu) * (1 - fv)], [1, 0, fu * (1 - fv)], [0, 1, (1 - fu) * fv], [1, 1, fu * fv]] as const) {
        if (this.valeur(this.eau, iu + dx, iv + dz, 0) > 0) {
          somme += this.valeur(this.niveau, iu + dx, iv + dz, 0) * p;
          poids += p;
        }
      }
      const niveau = poids > 0 ? somme / poids : h;
      const terre = h + Math.max(0, niveau + 0.6 + (0.5 - w) * 30 - h) * lisser(0, 0.3, w);
      const fond = niveau - 0.8 - 5 * lisser(0.5, 0.9, w);
      h = terre + (fond - terre) * lisser(0.42, 0.58, w);
      if (w > 0.44) eau = niveau;
    }

    // Rivières de case en case.
    const cx = Math.floor(x / this.m);
    const cz = Math.floor(z / this.m);
    if (this.bras.length > 0 && cx >= 0 && cz >= 0 && cx < this.l && cz < this.hc) {
      const px = x + this.b2.fbm(x, z, 520, 2) * 70;
      const pz = z + this.b3.fbm(x, z, 520, 2) * 70;
      let meilleur = Infinity;
      let choisi: Bras | null = null;
      let tChoisi = 0;
      for (let dz = -1; dz <= 1; dz++) {
        for (let dx = -1; dx <= 1; dx++) {
          const liste = this.brasParCase.get((cz + dz) * this.l + cx + dx);
          if (!liste || cx + dx < 0 || cx + dx >= this.l) continue;
          for (const k of liste) {
            const r = this.bras[k];
            const ex = r.bx - r.ax;
            const ez = r.bz - r.az;
            const t = Math.min(1, Math.max(0, ((px - r.ax) * ex + (pz - r.az) * ez) / (ex * ex + ez * ez)));
            const d = Math.hypot(px - r.ax - ex * t, pz - r.az - ez * t) - (r.la + (r.lb - r.la) * t);
            if (d < meilleur) {
              meilleur = d;
              choisi = r;
              tChoisi = t;
            }
          }
        }
      }
      if (choisi && meilleur < 200) {
        const r = choisi;
        const t = tChoisi;
        const demi = r.la + (r.lb - r.la) * t;
        const surface = r.sa + (r.sb - r.sa) * t;
        const d = meilleur + demi;
        const rive = demi * 2 + 14;
        if (d < rive) {
          const profondeur = r.pa + (r.pb - r.pa) * t;
          const cible = d < demi ? surface - profondeur * (1 - (d / demi) ** 2) : surface + 0.3 + (d - demi) * 0.12;
          h = cible + (h - cible) * lisser(demi, rive, d);
          if (d < demi + 0.6) eau = Number.isNaN(eau) ? surface : Math.max(eau, surface);
        }
        lit = Math.max(lit, 1 - lisser(demi, demi + Math.max(1.5, finesse * 0.7), d));
      }
    }
    sortie.h = h;
    sortie.eau = eau;
    sortie.lit = lit;
    return sortie;
  }

  /** Hauteur du sol seule. */
  hauteur(x: number, z: number): number {
    return this.echantillonner(x, z, TAMPON).h;
  }

  /** Hauteur de ce qui porte un marcheur : le sol, ou la surface de l'eau (mer comprise) s'il y en a. */
  surface(x: number, z: number): { sol: number; eau: number } {
    const e = this.echantillonner(x, z, TAMPON);
    const eau = Math.max(Number.isNaN(e.eau) ? -Infinity : e.eau, 0);
    return { sol: e.h, eau: e.h < eau ? eau : NaN };
  }

  /** Index de case (−1 hors de la carte). */
  caseEn(x: number, z: number): number {
    const cx = Math.floor(x / this.m);
    const cz = Math.floor(z / this.m);
    return cx < 0 || cz < 0 || cx >= this.l || cz >= this.hc ? -1 : cz * this.l + cx;
  }

  /** Biome tiré au hasard parmi les cases voisines selon leur poids : des lisières naturelles, sans grille. */
  biomeEn(x: number, z: number, hasard: number): number {
    const [u, v] = this.onduler(x, z);
    const iu = Math.floor(u);
    const iv = Math.floor(v);
    const fu = u - iu;
    const fv = v - iv;
    const poids = [(1 - fu) * (1 - fv), fu * (1 - fv), (1 - fu) * fv, fu * fv];
    let r = hasard;
    for (let k = 0; k < 4; k++) {
      r -= poids[k];
      if (r <= 0 || k === 3) {
        const x2 = Math.min(this.l - 1, Math.max(0, iu + (k & 1)));
        const z2 = Math.min(this.hc - 1, Math.max(0, iv + (k >> 1)));
        return this.biomes[z2 * this.l + x2];
      }
    }
    return OCEAN;
  }

  /** Abondance des plantes (0 à 1) interpolée. */
  plantesEn(x: number, z: number): number {
    const i = this.caseEn(x, z);
    return i < 0 ? 0 : this.plantes[i] / 255;
  }

  /** Température du jour (midi) interpolée, en °C (null : inconnue). */
  temperatureEn(x: number, z: number): number | null {
    const t = this.temperatures;
    if (!t) return null;
    const u = Math.min(this.l - 1, Math.max(0, x / this.m - 0.5));
    const v = Math.min(this.hc - 1, Math.max(0, z / this.m - 0.5));
    const iu = Math.floor(u);
    const iv = Math.floor(v);
    const fu = u - iu;
    const fv = v - iv;
    const lire = (a: number, b: number) => t.midi[Math.min(this.hc - 1, b) * this.l + Math.min(this.l - 1, a)];
    return (lire(iu, iv) * (1 - fu) + lire(iu + 1, iv) * fu) * (1 - fv) + (lire(iu, iv + 1) * (1 - fu) + lire(iu + 1, iv + 1) * fu) * fv;
  }

  /**
   * Couleur du sol (linéaire, 0 à 1) au point donné : biomes mêlés aux lisières, roche à nu dans les pentes, sable au bord de la mer,
   * lit mouillé des rivières, neige quand il gèle à midi, et teintes de saison (sec en été, roux à l'automne).
   */
  couleur(x: number, z: number, e: Echantillon, pente: number, sortie: Float32Array, k: number): void {
    const [u, v] = this.onduler(x, z);
    const iu = Math.floor(u);
    const iv = Math.floor(v);
    const fu = u - iu;
    const fv = v - iv;
    let r = 0;
    let g = 0;
    let b = 0;
    for (const [dx, dz, p] of [[0, 0, (1 - fu) * (1 - fv)], [1, 0, fu * (1 - fv)], [0, 1, (1 - fu) * fv], [1, 1, fu * fv]] as const) {
      const ix = Math.min(this.l - 1, Math.max(0, iu + dx));
      const iz = Math.min(this.hc - 1, Math.max(0, iv + dz));
      const c = SOLS_L[this.biomes[iz * this.l + ix]] ?? SOLS_L[PLAINE];
      r += c[0] * p;
      g += c[1] * p;
      b += c[2] * p;
    }
    // Saisons : l'été sèche les plaines et les landes, l'automne roussit les feuillus.
    const vert = g > r * 1.15;
    if (vert && this.saison === 1) {
      const sec = 0.25 * Math.sin(this.dansSaison * Math.PI);
      r += (r * 1.5 - r) * sec;
      b *= 1 - sec * 0.3;
    } else if (vert && this.saison === 2) {
      const roux = 0.35 * lisser(0, 0.6, this.dansSaison);
      r += (g * 0.95 - r) * roux;
      g *= 1 - 0.18 * roux;
    }
    // Plage : sable près du niveau de la mer.
    const sable = lisser(4, 1.5, e.h) * lisser(-3, 0.2, e.h);
    r += (SABLE_L[0] - r) * sable;
    g += (SABLE_L[1] - g) * sable;
    b += (SABLE_L[2] - b) * sable;
    // Roche à nu dans les pentes fortes.
    const i = this.caseEn(x, z);
    const roc = lisser(0.55, 0.9, pente);
    if (roc > 0) {
      const c = ROCHES_L[i >= 0 ? this.roches[i] : 0] ?? ROCHES_L[0];
      r += (c[0] - r) * roc;
      g += (c[1] - g) * roc;
      b += (c[2] - b) * roc;
    }
    // Lit des rivières.
    if (e.lit > 0) {
      r += (LIT_L[0] - r) * e.lit;
      g += (LIT_L[1] - g) * e.lit;
      b += (LIT_L[2] - b) * e.lit;
    }
    // Neige : là où il gèle à midi (et toujours sur les sommets enneigés), moins sur les parois.
    const temp = this.temperatureEn(x, z);
    const sommet = i >= 0 && this.biomes[i] === SOMMET ? 1 : 0;
    const neige = Math.max(sommet * lisser(800, 1600, e.h), temp === null ? 0 : lisser(1, -3, temp)) * (1 - lisser(0.7, 0.95, pente)) * lisser(0.5, 2, e.h);
    if (neige > 0) {
      r += (NEIGE_L[0] - r) * neige;
      g += (NEIGE_L[1] - g) * neige;
      b += (NEIGE_L[2] - b) * neige;
    }
    // Petites variations, pour casser l'uniformité.
    const variation = 1 + this.b2.bruit(x / 23, z / 23) * 0.08 + this.b3.bruit(x / 140, z / 140) * 0.12;
    // Grandes taches (sols plus secs ou plus riches), visibles d'avion.
    const tache = this.b1.fbm(x + 9000, z - 7000, 2500, 3) * (1 - neige);
    sortie[k] = r * variation * (1 + 0.28 * tache);
    sortie[k + 1] = g * variation * (1 + 0.06 * tache);
    sortie[k + 2] = b * variation * (1 - 0.12 * tache);
  }

  /**
   * Le coin d'une case où campent ceux qui y vivent (le même à chaque visite) : près du centre, au sec. La simulation ne sait pas où,
   * dans ses 800 m, se tient le camp ; la vue 3D le choisit une fois pour toutes.
   */
  camp(cx: number, cz: number): [number, number] {
    const cle = cz * this.l + cx;
    const connu = this.camps.get(cle);
    if (connu) return connu;
    let choisi: [number, number] = [(cx + 0.5) * this.m, (cz + 0.5) * this.m];
    for (let essai = 0; essai < 8; essai++) {
      const portee = 0.28 * (1 - essai / 8);
      const x = (cx + 0.5 + (unite(hacher(cle, essai, 21)) - 0.5) * 2 * portee) * this.m;
      const z = (cz + 0.5 + (unite(hacher(cle, essai, 23)) - 0.5) * 2 * portee) * this.m;
      const s = this.surface(x, z);
      if (Number.isNaN(s.eau) && s.sol > 0.5) {
        choisi = [x, z];
        break;
      }
    }
    this.camps.set(cle, choisi);
    return choisi;
  }
}

const TAMPON: Echantillon = { h: 0, eau: NaN, lit: 0 };
