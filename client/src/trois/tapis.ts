import * as THREE from "three/webgpu";
import {
  Fn, attribute, clamp, cos, dFdx, dFdy, dot, float, int, instanceIndex, ivec2, length, log2, max, mix, normalGeometry, normalize, positionGeometry,
  select, sin, smoothstep, sqrt, texture, textureLoad, time, transformNormalToView, uint, uniform, uv, varying, vec2, vec3, vec4,
} from "three/tsl";
import { bruleEn } from "./meteo3d";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { hacher, unite } from "./bruit";
import * as B from "./relief";
import type { Echantillon, Relief } from "./relief";
import { Couche, type Matieres, type SolTapis, type Zone } from "./matieres";
import * as Vg from "./vegetaux";

/**
 * Le tapis du sol (0.41.9, propositions A1 B1 C1 G1) : la flore basse du Crétacé, sans herbe (les graminées n'apparaissent, rares,
 * qu'à la toute fin de l'ère) : mousses en coussins, sélaginelles rampantes, jeunes prêles en colonies, petites fougères, cycas nains,
 * petites fleurs primitives près de l'eau, litière de feuilles ou d'aiguilles d'araucaria.
 *
 * Le processeur ne pose aucune plante. Il tient seulement, autour de la caméra, une carte de 1 m par point (hauteur du sol, densité de
 * chaque sorte, usure des sentiers) qui se remplit peu à peu là où l'on va, comme un tapis roulant (les points sortis d'un côté
 * reviennent de l'autre). La carte graphique pose ensuite chaque plante : une grille d'emplacements centrée sur la caméra, chaque
 * emplacement tiré de ses coordonnées dans le monde (les mêmes plantes au même endroit à chaque passage), gardé ou non selon la densité
 * lue dans la carte. Rien n'est renvoyé au GPU quand on marche, sinon les quelques lignes nouvelles de la carte.
 */

/** Côté de la carte du tapis (points de 1 m) : elle couvre ±96 m autour de la caméra. */
const N = 192;
/** Côté de la carte des pas (points de 0,5 m) : ±32 m. */
const NP = 128;
const PAS_P = 0.5;

const enum S {
  Mousse,
  Selaginelle,
  Prele,
  Fougere,
  Cycas,
  Fleur,
  Litiere,
}
const SORTES = 7;

/**
 * Part des emplacements occupés, par biome (avant l'abondance des plantes de la case) : mousse, sélaginelle, prêle, fougère, cycas,
 * fleur, litière ; puis plaques de mousses et d'hépatiques peintes sur le sol, et litière d'aiguilles (1) ou de feuilles (0).
 * Plaines du Crétacé : savanes de fougères. Forêts : litière épaisse, mousses. Marais : prêles en peuplements serrés.
 */
const TABLE: Partial<Record<number, [number[], number, number]>> = {
  [B.PLAINE]: [[0.12, 0.35, 0.22, 0.45, 0.123, 0.168, 0.05], 0.25, 0],
  [B.CONIFERES]: [[0.35, 0.22, 0.05, 0.25, 0.105, 0, 0.7], 0.5, 1],
  [B.FEUILLUS]: [[0.25, 0.25, 0.04, 0.3, 0.07, 0.112, 0.75], 0.4, 0],
  [B.TROPICALE]: [[0.3, 0.5, 0.05, 0.6, 0.21, 0.14, 0.5], 0.55, 0],
  [B.DESERT]: [[0, 0.04, 0.02, 0, 0.035, 0, 0], 0.03, 0],
  [B.MONTAGNE]: [[0.4, 0.3, 0.1, 0.12, 0.035, 0.084, 0.05], 0.45, 1],
  [B.SOMMET]: [[0.15, 0.08, 0, 0, 0, 0, 0], 0.2, 0],
  [B.MARAIS]: [[0.3, 0.2, 0.85, 0.2, 0, 0.336, 0.05], 0.6, 0],
  [B.MANGROVE]: [[0.1, 0.1, 0.25, 0.15, 0, 0.112, 0.3], 0.3, 0],
  [B.LANDE]: [[0.45, 0.35, 0.2, 0.15, 0.07, 0.224, 0.04], 0.5, 0],
  [B.PLAGE]: [[0, 0, 0.03, 0, 0, 0, 0], 0, 0],
};

/** Taille (m) : minimum et étendue ; souplesse au vent ; part couchée sous les pas ; posée à plat (suit la pente). */
interface Gabarit {
  sorte: S;
  esp: number;
  tmin: number;
  etendue: number;
  souplesse: number;
  pietine: number;
  plat: boolean;
  /** Décalage vertical (m) au pied. */
  dy: number;
  /** Emplacements lointains (×esp) ; 0 : pas de niveau lointain. */
  loin: number;
}

const GABARITS: Gabarit[] = [
  { sorte: S.Mousse, esp: 0.9, tmin: 0.25, etendue: 0.45, souplesse: 0, pietine: 0.5, plat: true, dy: -0.025, loin: 2.2 },
  { sorte: S.Selaginelle, esp: 0.7, tmin: 0.18, etendue: 0.22, souplesse: 1.5, pietine: 0.9, plat: true, dy: -0.01, loin: 0 },
  { sorte: S.Prele, esp: 0.8, tmin: 0.22, etendue: 0.45, souplesse: 2.6, pietine: 1, plat: false, dy: -0.02, loin: 2.5 },
  { sorte: S.Fougere, esp: 1.1, tmin: 0.3, etendue: 0.5, souplesse: 3.2, pietine: 1, plat: false, dy: -0.02, loin: 2.6 },
  { sorte: S.Cycas, esp: 6, tmin: 0.45, etendue: 0.55, souplesse: 1.2, pietine: 0.35, plat: false, dy: -0.03, loin: 1.5 },
  { sorte: S.Fleur, esp: 2.5, tmin: 0.11, etendue: 0.12, souplesse: 3, pietine: 1, plat: false, dy: -0.01, loin: 0 },
  { sorte: S.Litiere, esp: 0.7, tmin: 0.4, etendue: 0.4, souplesse: 0, pietine: 0.1, plat: true, dy: 0.012, loin: 0 },
];

/** Portées (m) et espacement selon la Qualité (proposition G1 ; Basse : pas de tapis, le sol d'avant). */
export interface ReglageTapis {
  proche: number;
  loin: number;
  /** Multiplie l'espacement : √2 donne deux fois moins de plantes. */
  espacement: number;
}

// ---------------------------------------------------------------------------------------------------------------------------
// Formes

const V = (x: number, y: number, z: number) => new THREE.Vector3(x, y, z);

function agiter(g: THREE.BufferGeometry, valeur: number | ((i: number) => number)): THREE.BufferGeometry {
  const n = g.getAttribute("position").count;
  const a = new Float32Array(n);
  for (let i = 0; i < n; i++) a[i] = typeof valeur === "number" ? valeur : valeur(i);
  g.setAttribute("agite", new THREE.BufferAttribute(a, 1));
  return g;
}

/** Sélaginelle : rameaux plats rampants, en étoile, qui se relèvent à peine au bout. */
function selaginelle(zones: Record<string, Zone>): THREE.BufferGeometry {
  const morceaux: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 4; k++) {
    const a = (k / 4) * Math.PI * 2 + k * 0.7;
    const dir = V(Math.cos(a), 0.18 + 0.1 * (k % 2), Math.sin(a)).normalize();
    morceaux.push(Vg.carte(k % 2 ? zones.fougere2 : zones.fougere3, V(0, 0.02, 0), dir, V(-Math.sin(a), 0, Math.cos(a)), 0.55, 0.2,
      { courbe: -0.05, segments: 2, normale: (p) => p.clone().setY(0).multiplyScalar(0.4).add(V(0, 1, 0)).normalize() }));
  }
  return mergeGeometries(morceaux, false)!;
}

/** Petite fougère au sol : frondes arquées depuis le pied (moins de sommets que celle de la 0.41.5). */
function fougere(zones: Record<string, Zone>, nombre: number, segments: number): THREE.BufferGeometry {
  const morceaux: THREE.BufferGeometry[] = [];
  for (let k = 0; k < nombre; k++) {
    const a = (k / nombre) * Math.PI * 2 + k * 0.9;
    const ouvre = 0.4 + 0.25 * ((k * 7) % 3) / 2;
    const dir = V(Math.cos(a) * ouvre, 1, Math.sin(a) * ouvre).normalize();
    const l = 0.9 + 0.2 * ((k * 5) % 4) / 3;
    const z = [zones.fougere, zones.fougere2, zones.fougere3][k % 3];
    morceaux.push(Vg.carte(z, V(0, 0, 0), dir, V(-Math.sin(a), 0, Math.cos(a)), l, l * 0.25,
      { courbe: 0.45, segments, normale: (p) => p.clone().setY(0).normalize().add(V(0, 1.4, 0)).normalize() }));
  }
  return mergeGeometries(morceaux, false)!;
}

/** Cycas nain : stipe court (ou aucun, de loin), rosette de frondes raides. Deux groupes : écorce, puis feuillage. */
function cycasNain(zones: Record<string, Zone>, nombre: number, segments: number, stipe: boolean): THREE.BufferGeometry {
  const haut = V(0, 0.28, 0);
  const feuilles: THREE.BufferGeometry[] = [];
  for (let k = 0; k < nombre; k++) {
    const a = (k / nombre) * Math.PI * 2 + k * 0.37;
    const dir = V(Math.cos(a), 0.7 + 0.4 * ((k * 3) % 4) / 3, Math.sin(a)).normalize();
    const l = 0.62 + 0.12 * ((k * 5) % 3) / 2;
    feuilles.push(Vg.carte(zones.cycas, haut, dir, V(-Math.sin(a), 0, Math.cos(a)), l, l * 0.26,
      { courbe: 0.3, segments, normale: (p) => p.clone().sub(V(0, 0.2, 0)).setY(0).normalize().add(V(0, 1.2, 0)).normalize() }));
  }
  const bois = stipe ? Vg.tronc(V(0, 0, 0), haut, 0.08, 0.07, 6, 3) : Vg.tronc(V(0, 0.2, 0), haut, 0.001, 0.001, 3, 1);
  return mergeGeometries([bois, mergeGeometries(feuilles, false)!], true)!;
}

/** Petites fleurs primitives (comme les premières plantes à fleurs, proches des magnolias et des nénuphars) : trois corolles en coupe. */
function fleurs(): THREE.BufferGeometry {
  const morceaux: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI * 2 + 0.4;
    const x = Math.cos(a) * 0.22;
    const z = Math.sin(a) * 0.22;
    const h = 0.55 + 0.35 * (k % 2);
    const tige = new THREE.CylinderGeometry(0.012, 0.016, h, 3, 1, true).translate(x, h / 2, z).toNonIndexed();
    const coupe = new THREE.ConeGeometry(0.13, 0.09, 6, 1, true).rotateX(Math.PI).translate(x, h + 0.03, z).toNonIndexed();
    tige.deleteAttribute("uv");
    coupe.deleteAttribute("uv");
    morceaux.push(agiter(tige, 0), agiter(coupe, 1));
  }
  return mergeGeometries(morceaux, false)!;
}

/** Coussin de mousse léger (moins de sommets que celui de la 0.41.5 : il y en a des milliers). */
function coussin(cotes = 7, etages = 2): THREE.BufferGeometry {
  const g = new THREE.SphereGeometry(0.5, cotes, etages, 0, Math.PI * 2, 0, Math.PI / 2).scale(1, 0.22, 1);
  const p = g.getAttribute("position");
  const t = g.getAttribute("uv");
  for (let i = 0; i < p.count; i++) t.setXY(i, p.getX(i) * 1.5, p.getZ(i) * 1.5);
  return agiter(g, 0);
}

// ---------------------------------------------------------------------------------------------------------------------------
// La carte du tapis (processeur)

function lisse(t: number): number {
  return t * t * (3 - 2 * t);
}

/** Bruit de valeur (0 à 1) de période `echelle` (m), pour les taches : colonies de prêles, plaques de mousse, clairières. */
function tache(x: number, z: number, echelle: number, graine: number): number {
  const u = x / echelle;
  const v = z / echelle;
  const iu = Math.floor(u);
  const iv = Math.floor(v);
  const fu = lisse(u - iu);
  const fv = lisse(v - iv);
  const a = unite(hacher(iu, iv, graine));
  const b = unite(hacher(iu + 1, iv, graine));
  const c = unite(hacher(iu, iv + 1, graine));
  const d = unite(hacher(iu + 1, iv + 1, graine));
  return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
}

/** Un morceau de sentier ou de lieu usé. */
interface Usure {
  /** Segment de A à B (disque si A = B), largeur à demi (m), force (0 à 1), méandre (m). */
  ax: number;
  az: number;
  bx: number;
  bz: number;
  demi: number;
  bord: number;
  force: number;
  meandre: number;
  graine: number;
}

interface Rectangle {
  x: number;
  z: number;
  demi: number;
  cos: number;
  sin: number;
}

export interface Lieux {
  huttes: { x: number; z: number; r: number; cap: number }[];
  champs: { x: number; z: number; cote: number; angle: number }[];
  enclos: { x: number; z: number; r: number }[];
  centres: { x: number; z: number; huttes: number }[];
  feux: { x: number; z: number }[];
  cle: string;
}

class Carte {
  readonly hauteurs = new Float32Array(N * N);
  readonly d0 = new Uint8Array(N * N * 4);
  readonly d1 = new Uint8Array(N * N * 4);
  readonly d2 = new Uint8Array(N * N * 4);
  readonly texHauteurs: THREE.DataTexture;
  readonly tex0: THREE.DataTexture;
  readonly tex1: THREE.DataTexture;
  readonly tex2: THREE.DataTexture;
  /** Point du monde que garde chaque case de la carte (ou SANS), et version de la carte qui l'a rempli. */
  private readonly cx = new Int32Array(N * N).fill(SANS);
  private readonly cz = new Int32Array(N * N).fill(SANS);
  private readonly versions = new Int32Array(N * N).fill(-1);
  private version = 0;
  /** Ordre de remplissage : du plus proche au plus lointain. */
  private readonly ordre: Int16Array;
  /** Centre de la carte (m, entier). */
  centreX = SANS;
  centreZ = SANS;
  private complet = false;
  private sale = false;
  private envoyeA = 0;
  private readonly tampon: Echantillon = { h: 0, eau: NaN, lit: 0 };
  /** Proximité de l'eau par carré de 4 m (0 à 1), gardée d'un passage à l'autre. */
  private readonly eauProche = new Map<number, number>();
  private usures: Usure[] = [];
  private champs: Rectangle[] = [];
  private cleLieux = "";
  private lieuxA = { x: NaN, z: NaN };
  private sources: Lieux | null = null;
  private readonly versLEau = new Map<string, [number, number] | null>();

  constructor(private readonly relief: Relief) {
    const tex = (donnees: Uint8Array) => {
      const t = new THREE.DataTexture(donnees, N, N, THREE.RGBAFormat, THREE.UnsignedByteType);
      t.wrapS = t.wrapT = THREE.RepeatWrapping;
      t.magFilter = t.minFilter = THREE.LinearFilter;
      t.generateMipmaps = false;
      t.needsUpdate = true;
      return t;
    };
    this.tex0 = tex(this.d0);
    this.tex1 = tex(this.d1);
    this.tex2 = tex(this.d2);
    this.texHauteurs = new THREE.DataTexture(this.hauteurs, N, N, THREE.RedFormat, THREE.FloatType);
    this.texHauteurs.magFilter = this.texHauteurs.minFilter = THREE.NearestFilter;
    this.texHauteurs.generateMipmaps = false;
    this.texHauteurs.needsUpdate = true;
    const liste: [number, number, number][] = [];
    for (let dz = -N / 2; dz < N / 2; dz++) for (let dx = -N / 2; dx < N / 2; dx++) liste.push([dx, dz, dx * dx + dz * dz]);
    liste.sort((a, b) => a[2] - b[2]);
    this.ordre = new Int16Array(liste.length * 2);
    liste.forEach(([dx, dz], k) => {
      this.ordre[k * 2] = dx;
      this.ordre[k * 2 + 1] = dz;
    });
  }

  /** Saison, gel ou lieux changés : tout sera refait peu à peu (l'ancien reste visible en attendant). */
  invalider(): void {
    this.version++;
    this.complet = false;
  }

  /** Nouveau monde ou saut : on oublie tout. */
  vider(): void {
    this.cx.fill(SANS);
    this.cz.fill(SANS);
    this.d0.fill(0);
    this.d1.fill(0);
    this.d2.fill(0);
    this.eauProche.clear();
    this.versLEau.clear();
    this.complet = false;
    this.sale = true;
  }

  definirLieux(lieux: Lieux, camera: THREE.Vector3): void {
    this.sources = lieux;
    const loin = Math.hypot(camera.x - this.lieuxA.x, camera.z - this.lieuxA.z);
    if (lieux.cle === this.cleLieux && loin < 60) return;
    const change = lieux.cle !== this.cleLieux;
    this.cleLieux = lieux.cle;
    this.lieuxA = { x: camera.x, z: camera.z };
    this.preparerUsures(camera);
    if (change) this.invalider();
  }

  /** Sentiers et sols tassés autour de la caméra (±300 m), d'après les huttes, le feu, les champs, les enclos et l'eau la plus proche. */
  private preparerUsures(camera: THREE.Vector3): void {
    const l = this.sources;
    this.usures = [];
    this.champs = [];
    if (!l) return;
    const pres = (x: number, z: number, marge = 300) => Math.abs(x - camera.x) < marge && Math.abs(z - camera.z) < marge;
    const disque = (x: number, z: number, demi: number, bord: number, force: number, graine: number) =>
      this.usures.push({ ax: x, az: z, bx: x, bz: z, demi, bord, force, meandre: 0, graine });
    const sentier = (ax: number, az: number, bx: number, bz: number, demi: number, force: number, graine: number) =>
      this.usures.push({ ax, az, bx, bz, demi, bord: demi + 0.6, force, meandre: Math.min(3, Math.hypot(bx - ax, bz - az) * 0.06), graine });
    for (const c of l.centres) {
      if (!pres(c.x, c.z, 500)) continue;
      const rayon = 7 + c.huttes * 0.9;
      // Le cœur du camp : sol tassé, plus nu près du feu.
      disque(c.x, c.z, rayon * 0.55, rayon * 0.5, 0.55, 1);
      disque(c.x, c.z, 3.2, 3.5, 0.95, 2);
      // Le chemin de l'eau : la plus proche à moins de 250 m.
      const eau = this.chercherEau(c.x, c.z);
      if (eau) sentier(c.x, c.z, eau[0], eau[1], 0.45, 0.85, hacher(Math.round(c.x), Math.round(c.z), 5));
      // Vers les champs de ce camp.
      for (const ch of l.champs) {
        if (Math.hypot(ch.x - c.x, ch.z - c.z) < 400) {
          const d = Math.hypot(ch.x - c.x, ch.z - c.z) || 1;
          const bord = ch.cote * 0.5;
          sentier(c.x, c.z, ch.x - ((ch.x - c.x) / d) * bord, ch.z - ((ch.z - c.z) / d) * bord, 0.4, 0.7, hacher(Math.round(ch.x), Math.round(ch.z), 7));
        }
      }
    }
    for (const f of l.feux) if (pres(f.x, f.z)) disque(f.x, f.z, 2.6, 4, 1, 3);
    for (const h of l.huttes) {
      if (!pres(h.x, h.z)) continue;
      // Autour de la hutte, puis de son entrée jusqu'au centre.
      disque(h.x, h.z, h.r + 0.6, 2.2, 0.85, 4);
      const ex = h.x + Math.sin(h.cap) * (h.r + 0.5);
      const ez = h.z + Math.cos(h.cap) * (h.r + 0.5);
      let c = l.centres[0];
      let dmin = Infinity;
      for (const k of l.centres) {
        const d = Math.hypot(k.x - h.x, k.z - h.z);
        if (d < dmin) {
          dmin = d;
          c = k;
        }
      }
      if (c) sentier(ex, ez, c.x, c.z, 0.38, 0.8, hacher(Math.round(h.x), Math.round(h.z), 9));
    }
    for (const e of l.enclos) if (pres(e.x, e.z)) disque(e.x, e.z, e.r * 0.9, 1.5, 0.7, 6);
    for (const ch of l.champs) {
      if (!pres(ch.x, ch.z)) continue;
      this.champs.push({ x: ch.x, z: ch.z, demi: ch.cote / 2 + 0.4, cos: Math.cos(ch.angle), sin: Math.sin(ch.angle) });
    }
  }

  private chercherEau(x: number, z: number): [number, number] | null {
    const cle = `${Math.round(x)},${Math.round(z)}`;
    if (this.versLEau.has(cle)) return this.versLEau.get(cle)!;
    let trouve: [number, number] | null = null;
    for (let r = 10; r <= 250 && !trouve; r += 8) {
      for (let k = 0; k < 24; k++) {
        const a = (k / 24) * Math.PI * 2;
        const px = x + Math.cos(a) * r;
        const pz = z + Math.sin(a) * r;
        const s = this.relief.surface(px, pz);
        if (!Number.isNaN(s.eau)) {
          // On s'arrête au bord, pas dans l'eau.
          trouve = [x + Math.cos(a) * (r - 4), z + Math.sin(a) * (r - 4)];
          break;
        }
      }
    }
    this.versLEau.set(cle, trouve);
    return trouve;
  }

  /** Usure (0 à 1) au point : 1 = sol nu. Les champs sont nus (leur terre est dessinée à part). */
  private usureEn(x: number, z: number): [number, boolean] {
    let u = 0;
    for (const s of this.usures) {
      const ex = s.bx - s.ax;
      const ez = s.bz - s.az;
      const l2 = ex * ex + ez * ez;
      // Boîte englobante rapide.
      const marge = s.demi + s.bord + s.meandre + 1;
      if (x < Math.min(s.ax, s.bx) - marge || x > Math.max(s.ax, s.bx) + marge || z < Math.min(s.az, s.bz) - marge || z > Math.max(s.az, s.bz) + marge) continue;
      let d: number;
      if (l2 < 1e-6) d = Math.hypot(x - s.ax, z - s.az);
      else {
        const t = Math.max(0, Math.min(1, ((x - s.ax) * ex + (z - s.az) * ez) / l2));
        const l = Math.sqrt(l2);
        // Le sentier serpente : un écart doux le long du trajet, nul aux deux bouts.
        const m = s.meandre * Math.sin(t * Math.PI) * (Math.sin(t * l * 0.11 + (s.graine % 97)) * 0.6 + Math.sin(t * l * 0.037 + (s.graine % 13)) * 0.4);
        const px = s.ax + ex * t - (ez / l) * m;
        const pz = s.az + ez * t + (ex / l) * m;
        d = Math.hypot(x - px, z - pz);
      }
      // Bord irrégulier : quelques décimètres de plus ou de moins.
      const bord = s.demi + (tache(x, z, 2.5, 431) - 0.5) * Math.min(0.5, s.demi * 0.6);
      const v = s.force * (1 - lisse(Math.max(0, Math.min(1, (d - bord) / s.bord))));
      if (v > u) u = v;
    }
    for (const c of this.champs) {
      const dx = x - c.x;
      const dz = z - c.z;
      const u2 = dx * c.cos + dz * c.sin;
      const v2 = -dx * c.sin + dz * c.cos;
      if (Math.abs(u2) < c.demi && Math.abs(v2) < c.demi) return [1, true];
    }
    return [u, false];
  }

  private procheDeLEau(x: number, z: number): number {
    const gx = Math.floor(x / 4);
    const gz = Math.floor(z / 4);
    const cle = gz * 200003 + gx;
    const connu = this.eauProche.get(cle);
    if (connu !== undefined) return connu;
    if (this.eauProche.size > 60000) this.eauProche.clear();
    let v = 0;
    const cx = gx * 4 + 2;
    const cz = gz * 4 + 2;
    for (const [r, poids] of [[4, 1], [10, 0.5]] as const) {
      for (let k = 0; k < 6 && v < poids; k++) {
        const a = (k / 6) * Math.PI * 2;
        const e = this.relief.echantillonner(cx + Math.cos(a) * r, cz + Math.sin(a) * r, this.tampon, 4);
        if (!Number.isNaN(e.eau) || e.lit > 0.2 || e.h < 0.4) v = Math.max(v, poids);
      }
    }
    this.eauProche.set(cle, v);
    return v;
  }

  private remplir(k: number, wx: number, wz: number): void {
    const rel = this.relief;
    const e = rel.echantillonner(wx, wz, this.tampon, 1);
    this.hauteurs[k] = e.h;
    const o = k * 4;
    const d0 = this.d0;
    const d1 = this.d1;
    const d2 = this.d2;
    d0[o] = d0[o + 1] = d0[o + 2] = d0[o + 3] = 0;
    d1[o] = d1[o + 1] = d1[o + 2] = d1[o + 3] = 0;
    d2[o] = d2[o + 1] = d2[o + 2] = d2[o + 3] = 0;
    const eau = Math.max(Number.isNaN(e.eau) ? -Infinity : e.eau, 0);
    const profondeur = eau - e.h;
    const biome = rel.biomeEn(wx, wz, unite(hacher(wx, wz, 307)));
    const ligne = TABLE[biome];
    if (!ligne || profondeur > 0.45) return;
    const [densites, plaques, aiguilles] = ligne;
    const abondance = 0.35 + 0.9 * rel.plantesEn(wx, wz);
    const temperature = rel.temperatureEn(wx, wz) ?? 12;
    const gel = temperature < 0;
    const saison = rel.saison;
    const mouille = Math.max(this.procheDeLEau(wx, wz), e.lit > 0.05 ? 1 : 0);
    const [usure, champ] = this.usureEn(wx, wz);
    d1[o + 3] = Math.round(usure * 255);
    d2[o] = Math.round(aiguilles * 255);
    d2[o + 1] = Math.round(mouille * 255);
    if (champ) return;
    // Taches : les prêles vont en colonies, les mousses par plaques, les fougères par nappes ; quelques clairières.
    const tPrele = lisse(Math.min(1, Math.max(0, tache(wx, wz, 7, 311) * 1.8 - 0.55)));
    const tMousse = tache(wx, wz, 4.5, 313);
    const tFougere = tache(wx, wz, 11, 317);
    const clairiere = 0.45 + 0.75 * tache(wx, wz, 23, 319);
    // Dans l'eau peu profonde : seules les prêles (et les fleurs d'eau au bord).
    const noye = profondeur > 0.02;
    const reste = 1 - usure;
    const sec = saison === 1 && temperature > 27 && mouille < 0.3 ? 0.55 : 1;
    const vivant = (s: S, f: number) => {
      if (gel || (noye && s !== S.Prele)) return 0;
      return (s === S.Mousse || s === S.Selaginelle ? 2.4 : 1.9) * densites[s] * abondance * f * reste * clairiere;
    };
    const fleurs = saison === 0 || saison === 1 ? densites[S.Fleur] * (0.25 + 2.6 * mouille) : 0;
    const v = [
      vivant(S.Mousse, (0.4 + 1.3 * tMousse) * (0.7 + 0.6 * mouille) * sec),
      vivant(S.Selaginelle, (0.6 + 0.8 * (1 - tFougere)) * sec),
      vivant(S.Prele, tPrele * 2.2 * (0.6 + 1.2 * mouille)),
      vivant(S.Fougere, 0.35 + 1.3 * tFougere),
      vivant(S.Cycas, mouille > 0.6 ? 0.4 : 1),
      gel || noye ? 0 : fleurs * reste,
      // La litière reste en hiver ; elle s'épaissit à l'automne.
      noye ? 0 : 0.7 * densites[S.Litiere] * (saison === 2 ? 1.25 : 1) * (1 - usure * 0.85) * (0.7 + 0.5 * tMousse),
    ];
    for (let s = 0; s < SORTES; s++) {
      const val = Math.round(Math.min(1, v[s]) * 255);
      if (s < 4) d0[o + s] = val;
      else d1[o + s - 4] = val;
    }
    d2[o + 2] = Math.round(Math.min(1, gel ? 0 : plaques * (0.5 + tMousse) * (0.6 + 0.6 * mouille) * reste * sec) * 255);
  }

  /** Remplit la carte autour de la caméra, du plus proche au plus lointain, dans le temps imparti (ms). */
  mettreAJour(camera: THREE.Vector3, budgetMs: number): void {
    const cxw = Math.floor(camera.x);
    const czw = Math.floor(camera.z);
    if (cxw !== this.centreX || czw !== this.centreZ) {
      this.centreX = cxw;
      this.centreZ = czw;
      this.complet = false;
    }
    if (this.complet) {
      this.envoyer(false);
      return;
    }
    const debut = performance.now();
    let temps = true;
    let reste = false;
    const ordre = this.ordre;
    const decale = N * 4096;
    for (let k = 0; k < ordre.length; k += 2) {
      const wx = cxw + ordre[k];
      const wz = czw + ordre[k + 1];
      const i = ((wz + decale) % N) * N + ((wx + decale) % N);
      if (this.cx[i] === wx && this.cz[i] === wz && this.versions[i] === this.version) continue;
      if (temps && (k & 31) === 0 && performance.now() - debut > budgetMs) temps = false;
      if (temps) {
        this.remplir(i, wx, wz);
        this.cx[i] = wx;
        this.cz[i] = wz;
        this.versions[i] = this.version;
        this.sale = true;
      } else {
        reste = true;
        // Une case qui gardait un autre point du monde est vidée tout de suite (sinon des plantes d'ailleurs y pousseraient).
        if (this.cx[i] !== wx || this.cz[i] !== wz) {
          const o = i * 4;
          this.d0.fill(0, o, o + 4);
          this.d1.fill(0, o, o + 4);
          this.d2.fill(0, o, o + 4);
          this.cx[i] = wx;
          this.cz[i] = wz;
          this.versions[i] = -1;
          this.sale = true;
        }
      }
    }
    this.complet = !reste;
    this.envoyer(this.complet);
  }

  /** Envoie la carte au GPU (au plus dix fois par seconde pendant qu'elle se remplit). */
  private envoyer(force: boolean): void {
    if (!this.sale) return;
    const maintenant = performance.now();
    if (!force && maintenant - this.envoyeA < 100) return;
    this.envoyeA = maintenant;
    this.sale = false;
    for (const t of [this.texHauteurs, this.tex0, this.tex1, this.tex2]) t.needsUpdate = true;
  }
}

const SANS = -0x7fffffff;

/**
 * Les pas (proposition C1) : là où passent humains et bêtes, les plantes se couchent, puis se relèvent en une demi-minute environ.
 * Carte de 0,5 m sur ±32 m autour de la caméra, en tapis roulant elle aussi.
 */
class Pas {
  private readonly valeurs = new Float32Array(NP * NP);
  private readonly octets = new Uint8Array(NP * NP);
  private readonly cx = new Int32Array(NP * NP).fill(SANS);
  private readonly cz = new Int32Array(NP * NP).fill(SANS);
  readonly tex: THREE.DataTexture;
  private envoyeA = 0;
  private actif = false;

  constructor() {
    this.tex = new THREE.DataTexture(this.octets, NP, NP, THREE.RedFormat, THREE.UnsignedByteType);
    this.tex.wrapS = this.tex.wrapT = THREE.RepeatWrapping;
    this.tex.magFilter = this.tex.minFilter = THREE.LinearFilter;
    this.tex.generateMipmaps = false;
    this.tex.needsUpdate = true;
  }

  vider(): void {
    this.valeurs.fill(0);
    this.octets.fill(0);
    this.cx.fill(SANS);
    this.tex.needsUpdate = true;
  }

  /** `pieds` : x, z, rayon (m), par trois nombres. */
  mettreAJour(camera: THREE.Vector3, pieds: number[], dt: number): void {
    const decale = NP * 8192;
    const ox = Math.floor(camera.x / PAS_P) - NP / 2;
    const oz = Math.floor(camera.z / PAS_P) - NP / 2;
    // Les pas : un creux doux de la taille du pied ou du corps couché.
    for (let k = 0; k + 2 < pieds.length; k += 3) {
      const x = pieds[k] / PAS_P;
      const z = pieds[k + 1] / PAS_P;
      const r = pieds[k + 2] / PAS_P;
      if (x < ox + 1 || z < oz + 1 || x > ox + NP - 2 || z > oz + NP - 2) continue;
      for (let gz = Math.floor(z - r); gz <= Math.ceil(z + r); gz++) {
        for (let gx = Math.floor(x - r); gx <= Math.ceil(x + r); gx++) {
          const d = Math.hypot(gx + 0.5 - x, gz + 0.5 - z) / Math.max(0.5, r);
          if (d >= 1) continue;
          const i = ((gz + decale) % NP) * NP + ((gx + decale) % NP);
          if (this.cx[i] !== gx || this.cz[i] !== gz) {
            this.cx[i] = gx;
            this.cz[i] = gz;
            this.valeurs[i] = 0;
          }
          const v = 1 - d * d;
          // Le pied appuie en un instant ; il faut rester un peu pour coucher entièrement la plante.
          this.valeurs[i] = Math.min(1, Math.max(this.valeurs[i], this.valeurs[i] + v * Math.min(1, dt * 6)));
          this.actif = true;
        }
      }
    }
    const maintenant = performance.now();
    if (!this.actif || maintenant - this.envoyeA < 100) return;
    const ecoule = Math.min(1, (maintenant - this.envoyeA) / 1000);
    this.envoyeA = maintenant;
    // Les plantes se relèvent (environ 25 s pour se redresser aux deux tiers).
    const garde = Math.exp(-ecoule / 25);
    let reste = false;
    for (let i = 0; i < this.valeurs.length; i++) {
      let v = this.valeurs[i];
      if (v <= 0) {
        this.octets[i] = 0;
        continue;
      }
      // Case qui ne correspond plus à la fenêtre : on l'oublie.
      const gx = this.cx[i];
      const gz = this.cz[i];
      if (gx < ox || gz < oz || gx >= ox + NP || gz >= oz + NP) v = 0;
      else v = v * garde - 0.002;
      if (v <= 0) v = 0;
      else reste = true;
      this.valeurs[i] = v;
      this.octets[i] = Math.round(v * 255);
    }
    this.actif = reste;
    this.tex.needsUpdate = true;
  }
}

// ---------------------------------------------------------------------------------------------------------------------------
// Pose des plantes (GPU)

/** Mélange d'entiers 32 bits (le même que `hacher` du processeur). */
function melanger(h0: THREE.Node<"uint">): THREE.Node<"uint"> {
  let h = h0;
  h = h.bitXor(h.shiftRight(uint(16)));
  h = h.mul(uint(0x7feb352d));
  h = h.bitXor(h.shiftRight(uint(15)));
  h = h.mul(uint(0x846ca68b));
  h = h.bitXor(h.shiftRight(uint(16)));
  return h;
}

const unit = (h: THREE.Node<"uint">) => float(h).div(4294967296);

interface Place {
  position: THREE.Node<"vec3">;
  normale: THREE.Node<"vec3">;
  teinte: THREE.Node<"vec3">;
}

/** Un niveau de détail d'une sorte : une grille d'emplacements centrée sur la caméra. */
class Niveau {
  readonly maillage: THREE.Mesh;
  private readonly geo: THREE.InstancedBufferGeometry;
  private readonly uCote = uniform(1, "uint");
  private readonly uOx = uniform(0, "int");
  private readonly uOz = uniform(0, "int");
  private readonly uEsp = uniform(1);
  private readonly uDecal = uniform(new THREE.Vector2());
  private readonly uCam = uniform(new THREE.Vector2());
  /** Entre r0 et r1 : les plantes apparaissent ; entre r2 et r3 : elles s'effacent. */
  private readonly uR = uniform(new THREE.Vector4(-2, -1, 20, 25));
  private cote = 0;
  private esp = 1;
  actif = true;

  constructor(
    readonly g: Gabarit,
    readonly lointain: boolean,
    geo: THREE.BufferGeometry,
    materiaux: (place: Place) => THREE.Material | THREE.Material[],
    t: Tapis,
  ) {
    this.geo = new THREE.InstancedBufferGeometry().copy(geo as THREE.InstancedBufferGeometry);
    this.geo.instanceCount = 0;
    const place = this.placer(t);
    this.maillage = new THREE.Mesh(this.geo, materiaux(place));
    this.maillage.frustumCulled = false;
    this.maillage.castShadow = false;
    this.maillage.receiveShadow = true;
    this.maillage.name = `tapis-${g.sorte}${lointain ? "-loin" : ""}`;
  }

  private placer(t: Tapis): Place {
    const g = this.g;
    const graine = uint(1009 + g.sorte * 31 + (this.lointain ? 17 : 0));
    const i = instanceIndex;
    const gx = int(i.mod(this.uCote));
    const gz = int(i.div(this.uCote));
    const wx = uint(gx.add(this.uOx));
    const wz = uint(gz.add(this.uOz));
    const h = melanger(wx.mul(uint(0x27d4eb2d)).bitXor(wz.mul(uint(0x165667b1))).bitXor(graine.mul(uint(0x9e3779b1)))).toVar();
    const r = (k: number) => unit(melanger(h.bitXor(uint((k * 0x9e3779b9) >>> 0))));
    const lx = float(gx).add(r(1)).mul(this.uEsp).add(this.uDecal.x).toVar();
    const lz = float(gz).add(r(2)).mul(this.uEsp).add(this.uDecal.y).toVar();
    const d = length(vec2(lx, lz).sub(this.uCam));
    // La carte : point du monde (entier) = origine du maillage + partie entière de la position locale.
    const fx = lx.floor();
    const fz = lz.floor();
    const decale = N * 4096;
    const tx = int(fx).add(t.uOrigineX).add(decale);
    const tz = int(fz).add(t.uOrigineZ).add(decale);
    const ix0 = tx.mod(N);
    const iz0 = tz.mod(N);
    const ix1 = tx.add(1).mod(N);
    const iz1 = tz.add(1).mod(N);
    const ch = t.carte.texHauteurs;
    const h00 = textureLoad(ch, ivec2(ix0, iz0)).r;
    const h10 = textureLoad(ch, ivec2(ix1, iz0)).r;
    const h01 = textureLoad(ch, ivec2(ix0, iz1)).r;
    const h11 = textureLoad(ch, ivec2(ix1, iz1)).r;
    const ax = lx.sub(fx);
    const az = lz.sub(fz);
    const sol = mix(mix(h00, h10, ax), mix(h01, h11, ax), az);
    const pentX = mix(h10.sub(h00), h11.sub(h01), az);
    const pentZ = mix(h01.sub(h00), h11.sub(h10), ax);
    // Densité au point le plus proche.
    const ixp = select(ax.greaterThan(0.5), ix1, ix0);
    const izp = select(az.greaterThan(0.5), iz1, iz0);
    const tex = g.sorte < 4 ? t.carte.tex0 : t.carte.tex1;
    const canal = ["r", "g", "b", "a"][g.sorte % 4] as "r";
    const dens = textureLoad(tex, ivec2(ixp, izp))[canal].toVar();
    const aiguilles = textureLoad(t.carte.tex2, ivec2(ixp, izp)).r;
    const R = this.uR;
    const fondu = smoothstep(R.x, R.y, d).mul(float(1).sub(smoothstep(R.z, R.w, d)));
    const garde = r(3).lessThan(dens).and(r(4).lessThan(fondu));
    const taille = float(g.tmin).add(r(5).mul(r(5)).mul(g.etendue)).mul(dens.mul(0.35).add(0.65)).mul(this.lointain ? 1.2 : 1);
    // 0.43.0 : rien ne pousse plus là où le feu vient de passer ; les plantes reviennent à mesure que la terre se remet.
    const brule = bruleEn(vec3(lx.add(t.uOrigineF.x), 0, lz.add(t.uOrigineF.y)));
    const echelle = select(garde, taille.mul(float(1).sub(smoothstep(0.2, 0.5, brule))), float(0)).toVar();
    const angle = r(6).mul(Math.PI * 2);
    const ca = cos(angle);
    const sa = sin(angle);
    const p = positionGeometry.mul(echelle);
    const px = p.x.mul(ca).sub(p.z.mul(sa));
    const pz = p.x.mul(sa).add(p.z.mul(ca));
    const py = g.plat ? p.y.add(px.mul(pentX)).add(pz.mul(pentZ)) : p.y;

    // Le vent (comme les plantes de la 0.41.5), en plus doux : ce sont de petites plantes.
    const vent = t.vent.u;
    const phase = r(7).mul(31);
    const force = length(vent);
    const y01 = clamp(positionGeometry.y, 0, 1.2);
    const hh = max(echelle, 0.05);
    const rafale = sin(time.mul(0.37).add(phase.mul(0.13))).mul(sin(time.mul(1.21).add(phase))).mul(0.35).add(0.65);
    const oscille = sin(time.mul(float(1.7).div(sqrt(hh.add(1))).add(0.5)).add(phase)).mul(0.3).add(0.7);
    const penche = force.mul(g.souplesse).mul(rafale).mul(oscille).mul(y01.mul(y01)).mul(hh).mul(0.06);
    const agite = g.sorte === S.Mousse || g.sorte === S.Litiere ? float(0) : attribute("agite", "float");
    const frisson = sin(time.mul(7.3).add(phase.mul(3)).add(positionGeometry.x.mul(11)).add(positionGeometry.z.mul(7)))
      .mul(agite).mul(force.add(0.08)).mul(sqrt(hh).mul(0.03));
    const dv = vent.div(max(force, 1e-3));
    let x = px.add(dv.x.mul(penche)).add(frisson.mul(0.6));
    let y = py.add(frisson).sub(penche.mul(penche).div(hh.mul(2).add(0.1)));
    let z = pz.add(dv.y.mul(penche)).add(frisson.mul(0.4));

    // Les pas : la plante se couche à l'opposé de qui l'écrase (pente de la carte des pas), plus ou moins selon sa souplesse.
    if (g.pietine > 0 && !this.lointain) {
      const wxF = lx.add(t.uOrigineF.x);
      const wzF = lz.add(t.uOrigineF.y);
      const uvP = vec2(wxF, wzF).div(NP * PAS_P);
      const e = 0.5 / (NP * PAS_P);
      const T = texture(t.pas.tex, uvP).level(float(0)).r;
      const Tx = texture(t.pas.tex, uvP.add(vec2(e, 0))).level(float(0)).r;
      const Tz = texture(t.pas.tex, uvP.add(vec2(0, e))).level(float(0)).r;
      const k = T.mul(g.pietine).mul(float(1).sub(smoothstep(26, 31, d))).toVar();
      const grad = vec2(T.sub(Tx), T.sub(Tz));
      const dir = select(length(grad).greaterThan(0.004), normalize(grad), vec2(ca, sa));
      x = x.add(dir.x.mul(y).mul(k).mul(1.1));
      z = z.add(dir.y.mul(y).mul(k).mul(1.1));
      y = y.mul(float(1).sub(k.mul(0.78)));
    }
    const position = vec3(lx.add(x), sol.add(g.dy).add(y), lz.add(z));
    const n = normalGeometry;
    const normale = varying(vec3(n.x.mul(ca).sub(n.z.mul(sa)), n.y, n.x.mul(sa).add(n.z.mul(ca))));

    // Teinte : saison, variété de chaque plante ; aiguilles d'araucaria roussies ; couleur des fleurs.
    const variete = r(8).mul(0.35).add(0.82);
    let teinte: THREE.Node<"vec3"> = t.uSaison[g.sorte].mul(variete);
    if (g.sorte === S.Litiere) teinte = teinte.mul(mix(vec3(0.66, 0.6, 0.55), vec3(0.75, 0.5, 0.38), aiguilles));
    if (g.sorte === S.Fleur) {
      const c = r(9);
      teinte = select(c.lessThan(0.35), vec3(1, 0.97, 0.88), select(c.lessThan(0.6), vec3(1, 0.9, 0.55), select(c.lessThan(0.8), vec3(0.98, 0.8, 0.76), vec3(0.93, 0.95, 0.72))));
    }
    return { position, normale, teinte: varying(teinte) };
  }

  /** Grille de cette sorte autour de la caméra (origine : point entier du monde où est posé le maillage). */
  regler(camera: THREE.Vector3, origine: THREE.Vector3, rg: ReglageTapis): void {
    const g = this.g;
    if (!this.actif) {
      this.geo.instanceCount = 0;
      return;
    }
    const esp = g.esp * rg.espacement * (this.lointain ? g.loin : 1);
    const rMax = this.lointain ? rg.loin : rg.proche;
    const cote = Math.ceil((2 * rMax) / esp) + 2;
    if (cote !== this.cote || esp !== this.esp) {
      this.cote = cote;
      this.esp = esp;
      this.uCote.value = cote;
      this.uEsp.value = esp;
    }
    const ox = Math.floor(camera.x / esp) - Math.floor(cote / 2);
    const oz = Math.floor(camera.z / esp) - Math.floor(cote / 2);
    this.uOx.value = ox;
    this.uOz.value = oz;
    this.uDecal.value.set(ox * esp - origine.x, oz * esp - origine.z);
    this.uCam.value.set(camera.x - origine.x, camera.z - origine.z);
    // Le niveau proche s'efface entre 80 % et 100 % de sa portée ; le lointain apparaît là et s'efface au bout de la sienne.
    if (this.lointain) this.uR.value.set(rg.proche * 0.8, rg.proche, rg.loin * 0.75, rg.loin);
    else if (g.loin > 0) this.uR.value.set(-2, -1, rg.proche * 0.8, rg.proche);
    // Sortes sans niveau lointain : elles s'effacent peu à peu jusqu'au bout de la portée proche (les plus petites plus tôt).
    else this.uR.value.set(-2, -1, rg.proche * 0.55, rg.proche);
    this.geo.instanceCount = cote * cote;
    this.maillage.position.copy(origine);
  }

  get emplacements(): number {
    return this.geo.instanceCount;
  }

  get sommets(): number {
    const index = this.geo.getIndex();
    return this.geo.instanceCount * (index ? index.count : this.geo.getAttribute("position").count);
  }
}

/** Le tapis : la carte (processeur), les pas, et les niveaux de chaque sorte (GPU). */
export class Tapis implements SolTapis {
  readonly groupe = new THREE.Group();
  readonly carte: Carte;
  readonly pas = new Pas();
  readonly vent: Matieres["vent"];
  /** Origine des maillages : point entier du monde, en entiers (pour la carte) et en flottants (pour la carte des pas). */
  readonly uOrigineX = uniform(0, "int");
  readonly uOrigineZ = uniform(0, "int");
  readonly uOrigineF = uniform(new THREE.Vector2());
  /** Teinte de saison de chaque sorte. */
  readonly uSaison: THREE.UniformNode<"vec3", THREE.Vector3>[];
  /** Pour le sol : centre de la carte (m) et 1 si le tapis est montré (0 : sol d'avant). */
  private readonly uCentre = uniform(new THREE.Vector2());
  private readonly uActif = uniform(0);
  /** Portées du tapis (m), pour le sol. */
  readonly uProche = uniform(25);
  readonly uLoin = uniform(80);
  private readonly niveaux: Niveau[] = [];
  private readonly origine = new THREE.Vector3();
  private reglage: ReglageTapis | null = null;
  private ombres = false;
  private saisonVue = "";

  constructor(private readonly relief: Relief, private readonly matieres: Matieres) {
    this.carte = new Carte(relief);
    this.vent = matieres.vent;
    this.groupe.name = "tapis";
    this.uSaison = GABARITS.map(() => uniform(new THREE.Vector3(1, 1, 1)));
    const z = matieres.zones;
    const formes: Record<S, [THREE.BufferGeometry, THREE.BufferGeometry | null]> = {
      [S.Mousse]: [coussin(7, 2), coussin(5, 1)],
      [S.Selaginelle]: [selaginelle(z), null],
      [S.Prele]: [Vg.preles(z), Vg.preles(z)],
      [S.Fougere]: [fougere(z, 5, 3), fougere(z, 4, 2)],
      [S.Cycas]: [cycasNain(z, 9, 3, true), cycasNain(z, 6, 2, false)],
      [S.Fleur]: [fleurs(), null],
      [S.Litiere]: [Vg.litiere(z), null],
    };
    for (const g of GABARITS) {
      const [proche, loin] = formes[g.sorte];
      const mats = (place: Place) => this.materiaux(g.sorte, place);
      this.niveaux.push(new Niveau(g, false, proche, mats, this));
      if (loin && g.loin > 0) this.niveaux.push(new Niveau(g, true, loin, mats, this));
    }
    for (const n of this.niveaux) this.groupe.add(n.maillage);
  }

  private materiaux(sorte: S, place: Place): THREE.Material | THREE.Material[] {
    const m = this.matieres;
    const normale = transformNormalToView(normalize(place.normale));
    const feuillage = () => {
      const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.78, side: THREE.DoubleSide });
      mat.alphaTest = 0.42;
      const c = texture(m.feuillages, uv());
      const px = uv().mul(2048);
      const niveau = max(log2(max(dot(dFdx(px), dFdx(px)), dot(dFdy(px), dFdy(px)))).mul(0.5), 0);
      mat.colorNode = vec4(c.rgb.mul(place.teinte), c.a.mul(niveau.mul(0.3).add(1)));
      mat.normalNode = normale;
      mat.positionNode = place.position;
      return mat;
    };
    if (sorte === S.Mousse) {
      const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.95 });
      mat.colorNode = texture(m.solsCouleur, uv()).depth(int(Couche.RocheMoussue)).rgb.mul(vec3(0.75, 1.15, 0.45)).mul(place.teinte);
      mat.normalNode = normale;
      mat.positionNode = place.position;
      return mat;
    }
    if (sorte === S.Fleur) {
      const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.6, side: THREE.DoubleSide });
      mat.colorNode = mix(vec3(0.07, 0.13, 0.035), place.teinte, attribute("agite", "float"));
      mat.normalNode = normale;
      mat.positionNode = place.position;
      return mat;
    }
    if (sorte === S.Cycas) {
      const stipe = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.92 });
      stipe.colorNode = texture(m.ecorcesCouleur, uv()).depth(int(1)).rgb.mul(2.2).mul(place.teinte);
      stipe.normalNode = normale;
      stipe.positionNode = place.position;
      return [stipe, feuillage()];
    }
    return feuillage();
  }

  /** La Qualité : null = pas de tapis (Basse). */
  regler(r: ReglageTapis | null, ombres: boolean): void {
    this.reglage = r;
    this.ombres = ombres;
    this.uActif.value = r ? 1 : 0;
    if (r) {
      this.uProche.value = r.proche;
      this.uLoin.value = r.loin;
    }
    for (const n of this.niveaux) {
      // Ombres du tapis : seulement les plantes proches, et seulement en Ultra.
      n.maillage.castShadow = ombres && !n.lointain && n.g.sorte !== S.Litiere && n.g.sorte !== S.Mousse;
      n.actif = r !== null && (!n.lointain || r.loin > r.proche + 5);
      if (!r) n.regler(this.origine, this.origine, { proche: 0, loin: 0, espacement: 1 });
    }
    this.groupe.visible = r !== null;
  }

  get portantOmbre(): boolean {
    return this.ombres;
  }

  invalider(): void {
    this.carte.invalider();
  }

  vider(): void {
    this.carte.vider();
    this.pas.vider();
  }

  /** Chaque image : carte, pas, grilles. `lieux` : camps, champs, feux ; `pieds` : x, z, rayon par trois nombres. */
  mettreAJour(camera: THREE.Vector3, budgetMs: number, lieux: Lieux | null, pieds: number[], dt: number): void {
    const r = this.reglage;
    if (!r) return;
    const haut = camera.y - this.relief.hauteur(camera.x, camera.z);
    if (haut > r.loin * 0.8) {
      this.groupe.visible = false;
      this.uActif.value = 0;
      return;
    }
    this.groupe.visible = true;
    this.uActif.value = 1;
    this.reglerSaison(camera);
    if (lieux) this.carte.definirLieux(lieux, camera);
    this.carte.mettreAJour(camera, budgetMs);
    this.pas.mettreAJour(camera, pieds, dt);
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    this.uOrigineX.value = this.origine.x;
    this.uOrigineZ.value = this.origine.z;
    this.uOrigineF.value.set(this.origine.x, this.origine.z);
    this.uCentre.value.set(this.carte.centreX, this.carte.centreZ);
    for (const n of this.niveaux) n.regler(camera, this.origine, r);
  }

  /** Teintes de saison (comme le sol de la 0.41.2) : sec en été chaud, roussi à l'automne, terne l'hiver. */
  private reglerSaison(camera: THREE.Vector3): void {
    const saison = this.relief.saison;
    const temperature = this.relief.temperatureEn(camera.x, camera.z) ?? 12;
    const cle = `${saison}:${Math.round(temperature / 4)}`;
    if (cle === this.saisonVue) return;
    this.saisonVue = cle;
    GABARITS.forEach((g, k) => {
      const v = this.uSaison[k].value.set(1, 1, 1);
      if (g.sorte === S.Litiere) {
        if (saison === 2) v.set(1.2, 0.95, 1);
        return;
      }
      if (g.sorte === S.Fleur) return;
      if (saison === 1 && temperature > 24) v.set(1.15, 1, 0.85);
      else if (saison === 2) v.set(1.25, 0.95, 0.72);
      else if (saison === 3) v.set(1.08, 0.88, 0.82);
      // Les sélaginelles et les mousses prennent des tons cuivrés quand il fait sec ou froid.
      if ((g.sorte === S.Selaginelle || g.sorte === S.Mousse) && (saison === 3 || (saison === 1 && temperature > 28))) v.multiply(new THREE.Vector3(1.1, 0.9, 0.85));
    });
  }

  /** Mesure (touche I) : emplacements tirés et sommets traités par image. */
  mesure(): { emplacements: number; sommets: number } {
    let e = 0;
    let s = 0;
    for (const n of this.niveaux) {
      if (!this.groupe.visible) break;
      e += n.emplacements;
      s += n.sommets;
    }
    return { emplacements: e, sommets: s };
  }

  /**
   * Pour le sol (matieres.materiauSol) : au point du monde `p`, x = usure (sentiers, sol tassé), y = plaques de mousses et
   * d'hépatiques, z = couvert des plantes (pour teinter le sol là où elles ne sont plus dessinées), w = poids (0 hors de la carte).
   */
  lireSol(p: THREE.Node<"vec3">): THREE.Node<"vec4"> {
    return Fn(() => {
      const uvC = p.xz.add(0.5).div(N);
      const a = texture(this.carte.tex0, uvC).level(float(0));
      const b = texture(this.carte.tex1, uvC).level(float(0));
      const c = texture(this.carte.tex2, uvC).level(float(0));
      const ecart = max(p.x.sub(this.uCentre.x).abs(), p.z.sub(this.uCentre.y).abs());
      const poids = float(1).sub(smoothstep(N / 2 - 14, N / 2 - 4, ecart)).mul(this.uActif);
      const couvert = clamp(a.r.mul(0.5).add(a.g.mul(0.6)).add(a.b.mul(0.35)).add(a.a.mul(0.5)).add(b.r.mul(0.3)), 0, 1);
      return vec4(b.a, c.b, couvert, poids);
    })();
  }

  /** Distance (m) au-delà de laquelle le sol porte seul la teinte des plantes basses. */
  get porteeLoin(): number {
    return this.reglage?.loin ?? 0;
  }
}

/** Portées du tapis par Qualité (G1) : Basse sans tapis, Moyenne à moitié, Haute et Ultra en entier. */
export const REGLAGES_TAPIS: Record<string, ReglageTapis | null> = {
  basse: null,
  moyenne: { proche: 16, loin: 48, espacement: Math.SQRT2 },
  haute: { proche: 25, loin: 80, espacement: 1 },
  ultra: { proche: 30, loin: 88, espacement: 1 },
};
