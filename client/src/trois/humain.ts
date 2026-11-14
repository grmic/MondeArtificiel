import * as THREE from "three/webgpu";
import { attribute, float, mix, texture, uv, vec2 } from "three/tsl";
import { hacher, lisser, unite } from "./bruit";
import { OS, type Construit, type Forme, type NomOs, type Proportions } from "./corps";
import { couleurs, type Rgb, type Visage } from "../visage";

/**
 * Humains réalistes de la vue 3D (0.41.4, proposition B1) : le corps vient de la base libre MakeHuman (données CC0 1.0 : maillage,
 * cibles de forme, poids des os ; voir outils/makehuman), converti en un fichier binaire (humain/makehuman.bin, ~5 Mo, chargé
 * une fois). Chaque humain proche est façonné par ce que dit la simulation, comme le faisait le corps calculé de la 0.41.1 :
 *
 * - sexe, âge, musculature, graisse et maigreur → les cibles « macro » de MakeHuman (nourrisson, enfant, jeune, vieux) ;
 * - poitrine (puberté, grossesse, allaitement, enfants, âge) → volume et fermeté ; grossesse → ventre ;
 * - les 18 traits hérités du portrait → largeur et longueur de la tête, mâchoire, pommettes, yeux, nez, lèvres, oreilles ;
 * - peau (teint, pâleur), lèvres, aréoles, sourcils, barbe, pilosité des adultes, cheveux (couleur, longueur, texture, calvitie,
 *   gris), yeux (couleur de l'iris) ;
 * - humeur et peur → expressions (unités d'expression de MakeHuman), clignements, regard qui suit, bouche qui parle.
 *
 * Le squelette reste celui des postures existantes (17 os, plus les deux yeux) : les poids des 163 os de MakeHuman y sont
 * regroupés. Tout est calculé à l'affichage ; la simulation ne change pas.
 */

interface Bloc {
  debut: number;
  n: number;
  type: string;
}

interface Entete {
  sommets: number;
  rendu: number;
  os: string[];
  positions: Bloc;
  origine: Bloc;
  uv: Bloc;
  osIdx: Bloc;
  osPoids: Bloc;
  morceaux: Record<string, Bloc>;
  articulations: Record<string, number[]>;
  groupes: Record<string, number[]>;
  cibles: { nom: string; idx: Bloc; val: Bloc; echelle: number }[];
}

interface Cible {
  idx: Uint16Array;
  val: Int16Array;
  echelle: number;
}

export interface BaseHumaine {
  n: number;
  positions: Float32Array;
  /** Sommet d'origine de chaque sommet de rendu, et sa coordonnée de texture. */
  origine: Uint16Array;
  uv: Float32Array;
  osIdx: Uint8Array;
  osPoids: Uint8Array;
  morceaux: Record<string, Uint16Array>;
  articulations: Record<string, number[]>;
  groupes: Record<string, number[]>;
  cibles: Map<string, Cible>;
  /** Triangles de la peau, en sommets d'origine (hauteur du corps). */
  triangles: Uint16Array;
  /** Triangles de tous les morceaux, en sommets d'origine (normales sans coutures). */
  tous: Uint16Array;
  /** Sommets des oreilles, des lèvres, des aréoles (repérés par les cibles qui les déplacent), avec leur poids 0-1. */
  oreilles: Float32Array;
  levres: Float32Array;
  areoles: Float32Array;
}

let base: BaseHumaine | null = null;
let chargement: Promise<void> | null = null;
let echec = false;

/** La base humaine si elle est chargée ; lance le chargement au premier appel. */
export function baseHumaine(): BaseHumaine | null {
  if (!base && !chargement && !echec) {
    chargement = charger().then(
      (b) => void (base = b),
      (e) => {
        echec = true;
        console.warn("MakeHuman base not loaded, keeping computed bodies.", e);
      },
    );
  }
  return base;
}

async function charger(): Promise<BaseHumaine> {
  const r = await fetch("humain/makehuman.bin");
  if (!r.ok) throw new Error(`HTTP ${r.status}`);
  const buf = await r.arrayBuffer();
  const vue = new DataView(buf);
  if (vue.getUint32(0, true) !== 0x4d55484d) throw new Error("fichier humain inconnu");
  const lg = vue.getUint32(4, true);
  const e = JSON.parse(new TextDecoder().decode(new Uint8Array(buf, 8, lg))) as Entete;
  const d0 = 8 + lg;
  const lire = <T>(b: Bloc, C: new (buf: ArrayBuffer, off: number, n: number) => T): T => new C(buf, d0 + b.debut, b.n);
  const cibles = new Map<string, Cible>();
  for (const c of e.cibles) cibles.set(c.nom, { idx: lire(c.idx, Uint16Array), val: lire(c.val, Int16Array), echelle: c.echelle });
  const morceaux: Record<string, Uint16Array> = {};
  for (const [k, b] of Object.entries(e.morceaux)) morceaux[k] = lire(b, Uint16Array);
  const origine = lire(e.origine, Uint16Array);
  // Triangles en sommets d'origine (pour des normales sans coutures de texture).
  const peau = morceaux.peau;
  const triangles = new Uint16Array(peau.length);
  for (let k = 0; k < peau.length; k++) triangles[k] = origine[peau[k]];
  const tousR = Object.values(morceaux).reduce((t, m) => t + m.length, 0);
  const tous = new Uint16Array(tousR);
  let n = 0;
  for (const m of Object.values(morceaux)) for (let k = 0; k < m.length; k++) tous[n++] = origine[m[k]];
  const marque = (noms: string[]): Float32Array => {
    const m = new Float32Array(e.sommets);
    for (const nom of noms) {
      const c = cibles.get(nom);
      if (!c) continue;
      let max = 0;
      for (let k = 0; k < c.idx.length; k++) {
        const v = Math.hypot(c.val[3 * k], c.val[3 * k + 1], c.val[3 * k + 2]);
        max = Math.max(max, v);
      }
      for (let k = 0; k < c.idx.length; k++) {
        const v = Math.hypot(c.val[3 * k], c.val[3 * k + 1], c.val[3 * k + 2]) / max;
        m[c.idx[k]] = Math.max(m[c.idx[k]], v);
      }
    }
    return m;
  };
  return {
    n: e.sommets,
    positions: lire(e.positions, Float32Array),
    origine,
    uv: lire(e.uv, Float32Array),
    osIdx: lire(e.osIdx, Uint8Array),
    osPoids: lire(e.osPoids, Uint8Array),
    morceaux,
    articulations: e.articulations,
    groupes: e.groupes,
    cibles,
    triangles,
    tous,
    oreilles: marque(["l-ear-scale-incr", "r-ear-scale-incr"]),
    levres: marque(["mouth-upperlip-volume-incr", "mouth-lowerlip-volume-incr"]),
    areoles: marque(["nipple-size-incr"]),
  };
}

// ——— Formes ———

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

/** Âge en années → curseur d'âge de MakeHuman (0 : 1 an, 0,1875 : 11 ans, 0,5 : 25 ans, 1 : 90 ans). */
function curseurAge(ans: number): number {
  if (ans <= 1) return 0;
  if (ans <= 11) return ((ans - 1) / 10) * 0.1875;
  if (ans <= 25) return 0.1875 + ((ans - 11) / 14) * 0.3125;
  return Math.min(1, 0.5 + ((ans - 25) / 65) * 0.5);
}

/** Parts des quatre âges de MakeHuman pour un curseur (même règle que MakeHuman). */
function partsAge(v: number): Record<"baby" | "child" | "young" | "old", number> {
  if (v < 0.5) {
    const young = Math.max(0, (v - 0.1875) * 3.2);
    return { baby: Math.max(0, 1 - v * 5.333), child: Math.max(0, Math.min(1, 5.333 * v) - young), young, old: 0 };
  }
  const old = Math.max(0, (v - 0.5) * 2);
  return { baby: 0, child: 0, young: 1 - old, old };
}

/** Parts min / moyen / max d'un curseur 0-1 (0,5 : moyen). */
function trois(v: number): [number, number, number] {
  const min = Math.max(0, 1 - v * 2);
  const max = Math.max(0, v * 2 - 1);
  return [min, 1 - min - max, max];
}

/** Ce qui façonne un humain réaliste (en plus de la forme du corps calculé). */
interface Reglages {
  poids: Map<string, number>;
  /** Pilosité et chevelure. */
  calvitie: number;
  longueur: number;
  crepus: number;
  barbe: number;
  sourcils: number;
  adulte: number;
  homme: number;
}

function reglages(f: Forme, v: Visage | null, id: number): Reglages {
  const poids = new Map<string, number>();
  const ajouter = (nom: string, w: number) => {
    if (Math.abs(w) > 1e-3) poids.set(nom, (poids.get(nom) ?? 0) + w);
  };
  const genres: [string, number][] = f.femme ? [["female", 1]] : [["male", 1]];
  const ages = partsAge(curseurAge(f.age));
  // Musculature : relative au sexe (celle d'une femme forte vaut celle d'un homme fort pour MakeHuman, dont la femme moyenne
  // est déjà moins musclée).
  const muscle = clamp(0.5 + ((f.musculature / (f.femme ? 0.42 : 1)) - 0.72) * 0.9, 0, 1);
  const masse = clamp(0.5 + 0.55 * f.graisse - 0.5 * f.maigreur, 0, 1);
  const pm = trois(muscle);
  const pp = trois(masse);
  const M = ["minmuscle", "averagemuscle", "maxmuscle"];
  const P = ["minweight", "averageweight", "maxweight"];
  for (const [g, wg] of genres) {
    for (const [a, wa] of Object.entries(ages)) {
      if (wa <= 0) continue;
      ajouter(`race-${g}-${a}`, wg * wa);
      for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) ajouter(`universal-${g}-${a}-${M[i]}-${P[j]}`, wg * wa * pm[i] * pp[j]);
    }
  }
  // Poitrine : volume et fermeté (la cible moyenne est dans la forme « jeune femme »).
  if (f.femme && f.age > 8) {
    const cup = clamp(0.5 + 0.55 * (f.poitrine - 1), 0, 1);
    const ferme = clamp(0.62 - 0.6 * f.ptose, 0, 1);
    const pc = trois(cup);
    const pf = trois(ferme);
    const C = ["mincup", "averagecup", "maxcup"];
    const F = ["minfirmness", "averagefirmness", "maxfirmness"];
    for (const [a, wa] of [["child", ages.child], ["young", ages.young], ["old", ages.old]] as const) {
      if (wa <= 0) continue;
      for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) if (i !== 1 || j !== 1) ajouter(`breast-${a}-${C[i]}-${F[j]}`, wa * pc[i] * pf[j]);
    }
    ajouter("breast-trans-down", 0.5 * f.ptose * f.puberte);
    ajouter("breast-volume-vert-down", 0.4 * f.ptose * f.puberte);
    if (f.grossesse || f.poitrine > 1.2) ajouter("nipple-size-incr", 0.6 * f.puberte);
  }
  if (f.grossesse) ajouter("stomach-pregnant-incr", 0.85);
  // Traits du visage (écarts-types) → cibles, et ce que la vie y met.
  const t = (k: number) => clamp((v?.traits[k] ?? 0) / 2.4, -1, 1);
  const paire = (incr: string, decr: string, x: number, gain = 1) => ajouter(x >= 0 ? incr : decr, Math.abs(x) * gain);
  const deux = (nom: (c: string) => string, x: number, incr: string, decr: string, gain = 1) => {
    for (const c of ["l", "r"]) paire(nom(c) + incr, nom(c) + decr, x, gain);
  };
  paire("head-scale-horiz-incr", "head-scale-horiz-decr", t(0), 0.6);
  paire("head-scale-vert-incr", "head-scale-vert-decr", t(1), 0.5);
  paire("chin-width-incr", "chin-width-decr", t(2), 0.8);
  if (t(2) > 0) ajouter("head-square", 0.35 * t(2));
  else ajouter("head-oval", -0.35 * t(2));
  deux((c) => `${c}-cheek-bones-`, t(3), "incr", "decr", 0.8);
  deux((c) => `${c}-eye-trans-`, t(4), "out", "in", 0.6);
  deux((c) => `${c}-eye-scale-`, t(5), "incr", "decr", 0.5);
  deux((c) => `${c}-eye-corner2-`, t(6), "up", "down", 0.7);
  paire("nose-scale-vert-incr", "nose-scale-vert-decr", t(8), 0.6);
  paire("nose-scale-horiz-incr", "nose-scale-horiz-decr", t(9), 0.5);
  paire("nose-nostrils-width-incr", "nose-nostrils-width-decr", t(9), 0.4);
  paire("mouth-upperlip-volume-incr", "mouth-upperlip-volume-decr", t(10), 0.7);
  paire("mouth-lowerlip-volume-incr", "mouth-lowerlip-volume-decr", t(10), 0.7);
  deux((c) => `${c}-ear-scale-`, t(12), "incr", "decr", 0.6);
  // Traits que le portrait ne donne pas : tirés de l'identité, petits.
  const h = (k: number) => unite(hacher(id, 300 + k)) * 2 - 1;
  ajouter("nose-hump-incr", Math.max(0, h(1)) * 0.6);
  paire("chin-prominent-incr", "chin-prominent-decr", h(2), 0.5);
  paire("buttocks-volume-incr", "buttocks-volume-decr", h(3), 0.5);
  paire("mouth-scale-horiz-incr", "mouth-scale-horiz-decr", h(4), 0.4);
  deux((c) => `${c}-ear-flap-`, Math.max(0, h(5)), "incr", "incr", 0.6);
  const maigre = v ? v.maigreur : f.maigreur;
  if (maigre > 0.05) {
    ajouter("l-cheek-volume-decr", maigre);
    ajouter("r-cheek-volume-decr", maigre);
    ajouter("head-fat-decr", 0.6 * maigre);
  }
  if (f.graisse > 0.3) {
    ajouter("head-fat-incr", 0.6 * (f.graisse - 0.3));
    ajouter("neck-double-incr", 0.8 * (f.graisse - 0.3));
  }
  const vieux = lisser(48, 85, f.age);
  ajouter("head-age-incr", vieux * 0.9);
  ajouter("l-eye-bag-incr", vieux * 0.6);
  ajouter("r-eye-bag-incr", vieux * 0.6);
  const adulte = lisser(13, 18, f.age);
  const homme = f.femme ? 0 : adulte;
  if (homme > 0) {
    paire("penis-length-incr", "penis-length-decr", h(6), 0.4);
    ajouter("penis-circ-incr", Math.max(0, h(7)) * 0.4);
  }
  const calvitie = clamp(v?.calvitie ?? 0, 0, 1);
  const crepus = clamp(0.5 + 0.3 * (v?.traits[14] ?? 0) + 0.35 * ((v?.teint ?? 0.5) - 0.5), 0, 1);
  const longueur = (v?.coupe ?? (f.cheveuxLongs ? 0.6 : 0.15)) * (1 - 0.6 * calvitie) * (f.age < 2 ? 0.15 : f.age < 6 ? 0.6 : 1);
  return {
    poids, calvitie, longueur, crepus, adulte, homme,
    barbe: homme * lisser(15, 22, f.age) * clamp(0.5 + 0.3 * (v?.traits[16] ?? 0), 0, 1),
    sourcils: clamp(0.5 + 0.25 * (v?.traits[11] ?? 0), 0, 1) * (0.55 + 0.45 * lisser(1, 12, f.age)),
  };
}

// ——— Textures partagées ———

/** Bruit de valeur pavable (période `p` cellules) sur une texture `n` × `n`. */
function bruitPavable(n: number, p: number, graine: number, x: number, y: number): number {
  const fx = (x / n) * p;
  const fy = (y / n) * p;
  const x0 = Math.floor(fx);
  const y0 = Math.floor(fy);
  const tx = fx - x0;
  const ty = fy - y0;
  const v = (i: number, j: number) => unite(hacher(((i % p) + p) % p, ((j % p) + p) % p, graine));
  const sx = tx * tx * (3 - 2 * tx);
  const sy = ty * ty * (3 - 2 * ty);
  const a = v(x0, y0) + (v(x0 + 1, y0) - v(x0, y0)) * sx;
  const b = v(x0, y0 + 1) + (v(x0 + 1, y0 + 1) - v(x0, y0 + 1)) * sx;
  return a + (b - a) * sy;
}

let textures: { peau: THREE.DataTexture; brins: THREE.DataTexture; meches: THREE.DataTexture } | null = null;

function texturesHumaines() {
  if (textures) return textures;
  // Peau : grain (R), marbrures (G) ; pavable.
  const N = 256;
  const peau = new Uint8Array(N * N * 4);
  for (let y = 0; y < N; y++) {
    for (let x = 0; x < N; x++) {
      const k = 4 * (y * N + x);
      const grain = 0.55 * bruitPavable(N, 64, 1, x, y) + 0.3 * bruitPavable(N, 128, 2, x, y) + 0.15 * unite(hacher(x, y, 3));
      const marbre = 0.5 * bruitPavable(N, 8, 4, x, y) + 0.3 * bruitPavable(N, 16, 5, x, y) + 0.2 * bruitPavable(N, 32, 6, x, y);
      peau[k] = Math.round(255 * grain);
      peau[k + 1] = Math.round(255 * marbre);
      peau[k + 2] = 0;
      peau[k + 3] = 255;
    }
  }
  // Brins vus du dessus (coques de cheveux, poils) : chaque petite cellule a une hauteur au hasard.
  const brins = new Uint8Array(N * N * 4);
  for (let y = 0; y < N; y++) {
    for (let x = 0; x < N; x++) {
      const k = 4 * (y * N + x);
      const cellule = unite(hacher(x >> 1, y >> 1, 11));
      const fin = unite(hacher(x, y, 12));
      brins[k] = Math.round(255 * (0.75 * cellule + 0.25 * fin));
      brins[k + 1] = Math.round(255 * (0.8 + 0.2 * unite(hacher(x >> 2, y >> 2, 13))));
      brins[k + 2] = 0;
      brins[k + 3] = 255;
    }
  }
  // Mèches (cheveux longs) : brins le long de v, pavable en u ; R = présence, G = clarté.
  const L = 128;
  const H = 256;
  const meches = new Uint8Array(L * H * 4);
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < L; x++) {
      const k = 4 * (y * L + x);
      const ondule = x + 3 * Math.sin((y / H) * Math.PI * 6 + x * 0.05);
      const col = ((Math.floor(ondule) % L) + L) % L;
      const brin = unite(hacher(col, 21));
      const fin = (y / H) > 0.55 + 0.45 * unite(hacher(col, 22)) ? 0 : 1;
      meches[k] = Math.round(255 * clamp(brin * 1.25, 0, 1) * fin);
      meches[k + 1] = Math.round(255 * (0.6 + 0.4 * unite(hacher(col, 23))));
      meches[k + 2] = 0;
      meches[k + 3] = 255;
    }
  }
  const tex = (d: Uint8Array, l: number, h: number) => {
    const t = new THREE.DataTexture(d, l, h, THREE.RGBAFormat);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.magFilter = THREE.LinearFilter;
    t.minFilter = THREE.LinearMipmapLinearFilter;
    t.generateMipmaps = true;
    t.colorSpace = THREE.NoColorSpace;
    t.needsUpdate = true;
    return t;
  };
  textures = { peau: tex(peau, N, N), brins: tex(brins, N, N), meches: tex(meches, L, H) };
  return textures;
}

/** Texture d'un œil (iris, pupille, limbe, blanc), en projection de face ; une par couleur d'iris. */
const texturesYeux = new Map<string, THREE.DataTexture>();
function textureOeil(iris: THREE.Color): THREE.DataTexture {
  const cle = iris.getHexString();
  const deja = texturesYeux.get(cle);
  if (deja) return deja;
  const N = 128;
  const d = new Uint8Array(N * N * 4);
  const blanc = new THREE.Color(0.86, 0.83, 0.8);
  const veine = new THREE.Color(0.75, 0.42, 0.4);
  const c = new THREE.Color();
  for (let y = 0; y < N; y++) {
    for (let x = 0; x < N; x++) {
      const u = (x + 0.5) / N * 2 - 1;
      const v = (y + 0.5) / N * 2 - 1;
      const r = Math.hypot(u, v);
      const a = Math.atan2(v, u);
      // Rayons de l'iris (r en part du rayon du globe en projection : iris jusqu'à 0,5, pupille 0,17).
      const fibres = 0.75 + 0.25 * bruitPavable(256, 64, 31, ((a / (Math.PI * 2)) + 1) * 256, r * 40);
      const cryptes = bruitPavable(256, 16, 32, ((a / (Math.PI * 2)) + 1) * 256, r * 60);
      if (r < 0.17) c.setRGB(0.012, 0.01, 0.01);
      else if (r < 0.5) {
        const t = (r - 0.17) / 0.33;
        c.copy(iris).multiplyScalar(fibres * (0.75 + 0.45 * cryptes) * (1.15 - 0.35 * t));
        // Collerette plus claire autour de la pupille, limbe sombre au bord.
        if (t < 0.25) c.lerp(iris.clone().multiplyScalar(1.5), 0.3 * (1 - t / 0.25));
        c.multiplyScalar(1 - 0.75 * lisser(0.8, 1, t));
      } else {
        const t = lisser(0.5, 0.56, r);
        c.copy(iris).multiplyScalar(0.25).lerp(blanc, t);
        if (r > 0.75) c.lerp(veine, 0.25 * lisser(0.75, 1, r) * bruitPavable(256, 32, 33, ((a / (Math.PI * 2)) + 1) * 256, r * 30));
      }
      const k = 4 * (y * N + x);
      d[k] = Math.round(255 * Math.pow(clamp(c.r, 0, 1), 1 / 2.2));
      d[k + 1] = Math.round(255 * Math.pow(clamp(c.g, 0, 1), 1 / 2.2));
      d[k + 2] = Math.round(255 * Math.pow(clamp(c.b, 0, 1), 1 / 2.2));
      d[k + 3] = 255;
    }
  }
  const t = new THREE.DataTexture(d, N, N, THREE.RGBAFormat);
  t.colorSpace = THREE.SRGBColorSpace;
  t.magFilter = THREE.LinearFilter;
  t.minFilter = THREE.LinearMipmapLinearFilter;
  t.generateMipmaps = true;
  t.needsUpdate = true;
  texturesYeux.set(cle, t);
  return t;
}

// ——— Matériaux partagés ———

let materiaux: Record<"peau" | "bouche" | "cils" | "pagne" | "coque" | "meche", THREE.Material> | null = null;
const materiauxYeux = new Map<string, THREE.Material>();

function materiauxHumains() {
  if (materiaux) return materiaux;
  const tx = texturesHumaines();
  // Peau : couleur du sommet (teint, lèvres, aréoles…) × grain et marbrures ; poils (sourcils, barbe, pubis) mêlés selon leur
  // densité, en brins.
  const peau = new THREE.MeshStandardNodeMaterial();
  const grain = texture(tx.peau, uv().mul(23)).r;
  const marbre = texture(tx.peau, uv().mul(5)).g;
  const base = attribute("color", "vec3").mul(float(0.9).add(grain.mul(0.12)).add(marbre.mul(0.08)));
  const poil = attribute("poil", "vec4");
  const brin = texture(tx.brins, uv().mul(31)).r;
  const couvre = poil.w.mul(brin.mul(1.1).add(0.3)).clamp(0, 1);
  peau.colorNode = mix(base, poil.xyz.mul(brin.mul(0.5).add(0.6)), couvre);
  peau.roughnessNode = mix(float(0.5).add(grain.mul(0.12)), float(0.78), couvre);
  peau.metalness = 0;
  const bouche = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.32, metalness: 0 });
  // Cils : des brins (texture des mèches en travers), pas une bande.
  const cils = new THREE.MeshStandardNodeMaterial({ side: THREE.DoubleSide });
  cils.colorNode = attribute("color", "vec3");
  cils.opacityNode = texture(tx.meches, uv().mul(vec2(40, 0.6))).r.mul(0.8);
  cils.alphaTest = 0.5;
  cils.roughness = 0.85;
  cils.metalness = 0;
  const pagne = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.92, metalness: 0, side: THREE.DoubleSide });
  // Cheveux courts et racines : coques superposées ; chaque coque ne garde que les brins plus hauts qu'elle (« seuil »).
  const coque = new THREE.MeshStandardNodeMaterial({ side: THREE.DoubleSide });
  // (Les coordonnées de texture des coques sont déjà multipliées par la densité des brins.)
  const b2 = texture(tx.brins, uv());
  coque.colorNode = attribute("color", "vec3").mul(b2.g);
  coque.opacityNode = b2.r.sub(attribute("seuil", "float")).add(0.5);
  coque.alphaTest = 0.5;
  coque.roughness = 0.62;
  coque.metalness = 0;
  // Mèches des cheveux longs.
  const meche = new THREE.MeshStandardNodeMaterial({ side: THREE.DoubleSide });
  const m2 = texture(tx.meches, uv());
  meche.colorNode = attribute("color", "vec3").mul(m2.g);
  meche.opacityNode = m2.r.sub(attribute("seuil", "float"));
  meche.alphaTest = 0.45;
  meche.roughness = 0.6;
  meche.metalness = 0;
  materiaux = { peau, bouche, cils, pagne, coque, meche };
  return materiaux;
}

function materiauOeil(iris: THREE.Color): THREE.Material {
  const cle = iris.getHexString();
  let m = materiauxYeux.get(cle);
  if (!m) {
    m = new THREE.MeshPhysicalMaterial({ map: textureOeil(iris), roughness: 0.32, clearcoat: 1, clearcoatRoughness: 0.04, metalness: 0 });
    materiauxYeux.set(cle, m);
  }
  return m;
}

// ——— Construction ———

const O = Object.fromEntries(OS.map((n, i) => [n, i])) as Record<NomOs, number>;
const OEIL_G = OS.length;
const OEIL_D = OS.length + 1;

const rgbLin = (c: Rgb) => new THREE.Color().setRGB(c[0] / 255, c[1] / 255, c[2] / 255, THREE.SRGBColorSpace);

/** Unités d'expression de MakeHuman qui composent nos expressions. */
const UNITES: Record<string, [string, number][]> = {
  joie: [["mouth-corner-puller", 1.2], ["eye-left-slit", 0.4], ["eye-right-slit", 0.4], ["mouth-elevation", 0.2]],
  triste: [["mouth-depression", 0.9], ["eyebrows-left-inner-up", 1.1], ["eyebrows-right-inner-up", 1.1], ["mouth-depression-retraction", 0.3]],
  colere: [["eyebrows-left-down", 1.25], ["eyebrows-right-down", 1.25], ["mouth-compression", 0.6], ["nose-left-elevation", 0.5], ["nose-right-elevation", 0.5], ["eye-left-slit", 0.3], ["eye-right-slit", 0.3]],
  peur: [["eyebrows-left-inner-up", 0.8], ["eyebrows-right-inner-up", 0.8], ["eyebrows-left-up", 0.8], ["eyebrows-right-up", 0.8], ["eye-left-opened-up", 0.9], ["eye-right-opened-up", 0.9], ["mouth-retraction", 0.6], ["mouth-open", 0.2]],
  bouche: [["mouth-open", 0.55]],
  cligne: [["eye-left-closure", 1], ["eye-right-closure", 1]],
};
export const EXPRESSIONS_REELLES = Object.keys(UNITES);

/** Expressions, clignements et regard d'un visage réaliste. */
export class VisageReel {
  private regard = { lacet: 0, tangage: 0, cibleL: 0, cibleT: 0, prochain: 0 };
  private clignement = { debut: -1, prochain: 0 };
  private dernier: number[] = [];
  private readonly inverse = new THREE.Matrix4();
  private readonly local = new THREE.Vector3();

  constructor(
    private readonly graine: number,
    /** Positions au repos (sommets de rendu) et, par expression, sommets touchés et déplacements. */
    private readonly repos: Float32Array,
    private readonly deltas: { idx: Uint32Array; d: Float32Array }[],
    readonly milieuYeux: THREE.Vector3,
  ) {
    this.dernier = deltas.map(() => 0);
  }

  /**
   * Anime le visage : `expressions` (poids 0-1 par nom), `regard` : point du monde (ou null), `tete` : repère du corps au repos →
   * monde (pour viser), `os` : les os du corps (les yeux sont les deux derniers).
   */
  poser(maillage: THREE.SkinnedMesh, os: THREE.Bone[], tete: THREE.Matrix4, expressions: Record<string, number>, regard: THREE.Vector3 | null,
    dort: boolean, temps: number): void {
    if (temps > this.clignement.prochain) {
      this.clignement.debut = temps;
      this.clignement.prochain = temps + 2 + 5 * unite(hacher(Math.floor(temps * 10), Math.floor(this.graine)));
    }
    const dt = temps - this.clignement.debut;
    const cligne = dt >= 0 && dt < 0.16 ? Math.sin((dt / 0.16) * Math.PI) : 0;
    const poids = EXPRESSIONS_REELLES.map((e) => (e === "cligne" ? (dort ? 1 : Math.max(cligne, expressions.cligne ?? 0)) : expressions[e] ?? 0));
    // Ne refaire les positions que si une expression a bougé.
    let change = false;
    for (let k = 0; k < poids.length; k++) if (Math.abs(poids[k] - this.dernier[k]) > 0.01) change = true;
    if (change) {
      const pos = maillage.geometry.getAttribute("position") as THREE.BufferAttribute;
      const a = pos.array as Float32Array;
      for (const { idx } of this.deltas) for (let k = 0; k < idx.length; k++) {
        const i = 3 * idx[k];
        a[i] = this.repos[i];
        a[i + 1] = this.repos[i + 1];
        a[i + 2] = this.repos[i + 2];
      }
      this.deltas.forEach(({ idx, d }, e) => {
        const w = poids[e];
        if (w < 0.005) return;
        for (let k = 0; k < idx.length; k++) {
          const i = 3 * idx[k];
          a[i] += d[3 * k] * w;
          a[i + 1] += d[3 * k + 1] * w;
          a[i + 2] += d[3 * k + 2] * w;
        }
      });
      pos.needsUpdate = true;
      this.dernier = poids;
    }
    // Regard : vers le point demandé (dans les limites de l'orbite), sinon de petits mouvements de temps en temps.
    const r = this.regard;
    if (regard) {
      this.inverse.copy(tete).invert();
      this.local.copy(regard).applyMatrix4(this.inverse).sub(this.milieuYeux);
      const d = this.local;
      r.cibleL = clamp(Math.atan2(d.x, d.z), -0.6, 0.6);
      r.cibleT = clamp(-Math.atan2(d.y, Math.hypot(d.x, d.z)), -0.4, 0.45);
    } else if (temps > r.prochain) {
      r.cibleL = (unite(hacher(Math.floor(temps * 3), Math.floor(this.graine), 1)) - 0.5) * 0.3;
      r.cibleT = (unite(hacher(Math.floor(temps * 3), Math.floor(this.graine), 2)) - 0.5) * 0.12;
      r.prochain = temps + 0.8 + 2.5 * unite(hacher(Math.floor(temps * 7), Math.floor(this.graine), 3));
    }
    r.lacet += (r.cibleL - r.lacet) * 0.35;
    r.tangage += (r.cibleT - r.tangage) * 0.35;
    for (const k of [OEIL_G, OEIL_D]) {
      os[k].rotation.set(r.tangage, r.lacet, 0, "YXZ");
      os[k].updateMatrixWorld(true);
    }
  }
}

/** Les poils d'une zone : densité 0-1 sur chaque sommet d'origine. */
interface Poils {
  cheveux: Float32Array;
  autres: Float32Array;
  barbe: Float32Array;
  sourcils: Float32Array;
}

/**
 * Construit un humain réaliste : forme, peau, poils, yeux, cheveux, vêtement. `v` : son portrait (traits, couleurs), s'il est
 * connu ; sinon les couleurs du flux Scène.
 */
export function construireReel(b: BaseHumaine, f: Forme, v: Visage | null, id: number): Construit & { anime: VisageReel } {
  const R = reglages(f, v, id);
  const N = b.n;
  // 1. Forme : base + cibles pondérées.
  const P = new Float64Array(N * 3);
  for (let k = 0; k < N * 3; k++) P[k] = b.positions[k];
  for (const [nom, w] of R.poids) {
    const c = b.cibles.get(nom);
    if (!c) continue;
    const e = c.echelle * w;
    for (let k = 0; k < c.idx.length; k++) {
      const i = 3 * c.idx[k];
      P[i] += c.val[3 * k] * e;
      P[i + 1] += c.val[3 * k + 1] * e;
      P[i + 2] += c.val[3 * k + 2] * e;
    }
  }
  // 2. Stature 1, pieds au sol, hanches sur l'axe.
  const art = (nom: string): THREE.Vector3 => {
    const l = b.articulations[nom];
    const p = new THREE.Vector3();
    for (const i of l) p.add(new THREE.Vector3(P[3 * i], P[3 * i + 1], P[3 * i + 2]));
    return p.multiplyScalar(1 / l.length);
  };
  let bas = Infinity;
  let haut = -Infinity;
  const peauIdx = b.triangles;
  for (let k = 0; k < peauIdx.length; k++) {
    const y = P[3 * peauIdx[k] + 1];
    if (y < bas) bas = y;
    if (y > haut) haut = y;
  }
  const s = 1 / (haut - bas);
  const hanches = art("cuisseG").add(art("cuisseD")).multiplyScalar(0.5);
  const ox = 0;
  const oz = hanches.z;
  for (let k = 0; k < N; k++) {
    P[3 * k] = (P[3 * k] - ox) * s;
    P[3 * k + 1] = (P[3 * k + 1] - bas) * s;
    P[3 * k + 2] = (P[3 * k + 2] - oz) * s;
  }
  const A: Record<string, THREE.Vector3> = {};
  for (const nom of Object.keys(b.articulations)) A[nom] = art(nom);
  A.bassin = A.cuisseG.clone().add(A.cuisseD).multiplyScalar(0.5);
  A.bassin.y += 0.01;
  // Yeux : centre et rayon des globes de MakeHuman.
  const globe = (g: string): [THREE.Vector3, number] => {
    const l = b.groupes[g];
    const c = new THREE.Vector3();
    for (const i of l) c.add(new THREE.Vector3(P[3 * i], P[3 * i + 1], P[3 * i + 2]));
    c.multiplyScalar(1 / l.length);
    let r = 0;
    for (const i of l) r += Math.hypot(P[3 * i] - c.x, P[3 * i + 1] - c.y, P[3 * i + 2] - c.z);
    return [c, r / l.length];
  };
  const [oeilG, rayonG] = globe("oeilG");
  const [oeilD, rayonD] = globe("oeilD");
  const yeux = oeilG.clone().add(oeilD).multiplyScalar(0.5);
  const e = oeilG.distanceTo(oeilD);

  // 3. Normales (sur les sommets d'origine : pas de couture).
  const normales = new Float32Array(N * 3);
  const tri = b.tous;
  for (let k = 0; k < tri.length; k += 3) {
    const [i, j, l] = [tri[k], tri[k + 1], tri[k + 2]];
    const ax = P[3 * j] - P[3 * i], ay = P[3 * j + 1] - P[3 * i + 1], az = P[3 * j + 2] - P[3 * i + 2];
    const bx = P[3 * l] - P[3 * i], by = P[3 * l + 1] - P[3 * i + 1], bz = P[3 * l + 2] - P[3 * i + 2];
    const nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
    for (const q of [i, j, l]) {
      normales[3 * q] += nx;
      normales[3 * q + 1] += ny;
      normales[3 * q + 2] += nz;
    }
  }
  for (let k = 0; k < N; k++) {
    const l = Math.hypot(normales[3 * k], normales[3 * k + 1], normales[3 * k + 2]) || 1;
    normales[3 * k] /= l;
    normales[3 * k + 1] /= l;
    normales[3 * k + 2] /= l;
  }

  // 4. Couleurs : peau, lèvres, aréoles ; poils (cheveux, sourcils, barbe, pubis).
  const c = v ? couleurs(v) : null;
  const peau = c ? rgbLin(c.peau) : f.peau.clone();
  const cheveux = c ? rgbLin(c.cheveux) : f.cheveux.clone();
  const levres = c ? rgbLin(c.levres) : peau.clone().lerp(new THREE.Color(0.32, 0.07, 0.07), 0.3);
  const aureole = peau.clone().lerp(new THREE.Color(0.24, 0.1, 0.07), 0.4);
  const iris = c ? rgbLin(c.yeux) : new THREE.Color(0.06, 0.035, 0.02);
  const poils = poilsDe(b, P, normales, R, yeux, e, A);
  const couleur = new Float32Array(N * 3);
  const poilAttr = new Float32Array(N * 4);
  const sourcilCouleur = cheveux.clone().lerp(new THREE.Color(0.02, 0.015, 0.01), 0.25);
  const tmp = new THREE.Color();
  for (let k = 0; k < N; k++) {
    tmp.copy(peau);
    if (b.levres[k] > 0) tmp.lerp(levres, clamp(b.levres[k] * 1.6, 0, 0.85));
    if (b.areoles[k] > 0 && f.age > 9) tmp.lerp(aureole, clamp(b.areoles[k] * 1.5, 0, 1) * (f.femme ? lisser(9, 15, f.age) : 0.6));
    couleur[3 * k] = tmp.r;
    couleur[3 * k + 1] = tmp.g;
    couleur[3 * k + 2] = tmp.b;
    // Cheveux : le cuir chevelu prend leur couleur sous les coques ; autres poils un peu plus sombres.
    const ch = poils.cheveux[k];
    const au = poils.autres[k];
    const pc = ch >= au ? cheveux.clone().multiplyScalar(0.75) : sourcilCouleur;
    poilAttr[4 * k] = pc.r;
    poilAttr[4 * k + 1] = pc.g;
    poilAttr[4 * k + 2] = pc.b;
    poilAttr[4 * k + 3] = Math.max(ch * 0.95, au);
  }

  // 5. Géométrie de rendu : peau (et sexe), yeux, bouche, cils, pagne, cheveux.
  const pos: number[] = [];
  const nor: number[] = [];
  const uvs: number[] = [];
  const col: number[] = [];
  const pol: number[] = [];
  const osI: number[] = [];
  const osW: number[] = [];
  const seuils: number[] = [];
  const indices: number[] = [];
  const groupes: { debut: number; n: number; m: THREE.Material }[] = [];
  const mats = materiauxHumains();
  const ajouterSommet = Object.assign((x: number, y: number, z: number, nx: number, ny: number, nz: number, u: number, vv: number, r: number, g: number, bl: number,
    os: ArrayLike<number>, w: ArrayLike<number>, poil: [number, number, number, number] = [0, 0, 0, 0], seuil = 0, densite = 1): number => {
    pos.push(x, y, z);
    nor.push(nx, ny, nz);
    uvs.push(u * densite, vv * densite);
    col.push(r, g, bl);
    pol.push(...poil);
    osI.push(os[0], os[1], os[2], os[3]);
    osW.push(w[0], w[1], w[2], w[3]);
    seuils.push(seuil);
    return pos.length / 3 - 1;
  }, { compte: () => pos.length / 3 });
  const rendu = new Map<number, number>();
  const sommetRendu = (r: number): number => {
    let k = rendu.get(r);
    if (k !== undefined) return k;
    const o = b.origine[r];
    k = ajouterSommet(P[3 * o], P[3 * o + 1], P[3 * o + 2], normales[3 * o], normales[3 * o + 1], normales[3 * o + 2], b.uv[2 * r], b.uv[2 * r + 1],
      couleur[3 * o], couleur[3 * o + 1], couleur[3 * o + 2], [b.osIdx[4 * o], b.osIdx[4 * o + 1], b.osIdx[4 * o + 2], b.osIdx[4 * o + 3]],
      [b.osPoids[4 * o] / 255, b.osPoids[4 * o + 1] / 255, b.osPoids[4 * o + 2] / 255, b.osPoids[4 * o + 3] / 255],
      [poilAttr[4 * o], poilAttr[4 * o + 1], poilAttr[4 * o + 2], poilAttr[4 * o + 3]]);
    rendu.set(r, k);
    return k;
  };
  const groupe = (m: THREE.Material, faire: () => void) => {
    const debut = indices.length;
    faire();
    if (indices.length > debut) groupes.push({ debut, n: indices.length - debut, m });
  };
  const nu = f.vetement === 0;
  groupe(mats.peau, () => {
    for (const r of b.morceaux.peau) indices.push(sommetRendu(r));
    // Anatomie de l'homme adulte, sobre, tant qu'il est nu (aucun détail chez l'enfant).
    if (nu && R.homme > 0.6 && b.morceaux.sexe) for (const r of b.morceaux.sexe) indices.push(sommetRendu(r));
  });
  // Yeux : globes faits ici (iris en texture), sur les os des yeux.
  const repos: THREE.Vector3[] = [];
  groupe(materiauOeil(iris), () => {
    for (const [centre, rayon, os] of [[oeilG, rayonG, OEIL_G], [oeilD, rayonD, OEIL_D]] as const) globeOeil(centre, rayon * 0.97, os, ajouterSommet, indices);
  });
  // Dents et langue.
  const dents = new THREE.Color(0.78, 0.74, 0.64).multiplyScalar(f.age > 50 ? 0.8 : 1);
  const langue = new THREE.Color(0.55, 0.22, 0.22);
  const dejaRendu = new Map(rendu);
  const recolorer = (morceau: Uint16Array | undefined, couleurM: THREE.Color) => {
    if (!morceau) return;
    for (const r of morceau) {
      const k = sommetRendu(r);
      if (!dejaRendu.has(r)) {
        col[3 * k] = couleurM.r;
        col[3 * k + 1] = couleurM.g;
        col[3 * k + 2] = couleurM.b;
      }
      indices.push(k);
    }
  };
  groupe(mats.bouche, () => {
    if (f.age > 0.6) recolorer(b.morceaux.dents, dents);
    recolorer(b.morceaux.langue, langue);
  });
  groupe(mats.cils, () => recolorer(b.morceaux.cils, cheveux.clone().lerp(new THREE.Color(0.01, 0.008, 0.006), 0.6)));
  // Vêtement : la jupe d'aide de MakeHuman, du bas des hanches vers les genoux (raccourcie et effrangée par l'usure).
  if (!nu && b.morceaux.pagne) {
    const couleurV = f.vetement === 24 ? new THREE.Color(0.42, 0.28, 0.17) : f.vetement === 7 ? new THREE.Color(0.32, 0.42, 0.18)
      : f.vetement === 34 ? new THREE.Color(0.38, 0.3, 0.22) : new THREE.Color(0.62, 0.55, 0.36);
    couleurV.multiplyScalar(0.75 + 0.25 * f.usure);
    const coupe = A.jambeG.y + 0.06 + (A.cuisseG.y - A.jambeG.y) * 0.55 * Math.min(1, 1 - f.usure + 0.15);
    groupe(mats.pagne, () => {
      const m = b.morceaux.pagne;
      for (let k = 0; k < m.length; k += 3) {
        const ys = [m[k], m[k + 1], m[k + 2]].map((r) => P[3 * b.origine[r] + 1]);
        const frange = 0.012 * unite(hacher(m[k], 41));
        if (Math.min(...ys) < coupe - frange) continue;
        for (let j = 0; j < 3; j++) {
          const kk = sommetRendu(m[k + j]);
          const t = 0.9 + 0.1 * unite(hacher(m[k + j], 42));
          col[3 * kk] = couleurV.r * t;
          col[3 * kk + 1] = couleurV.g * t;
          col[3 * kk + 2] = couleurV.b * t;
          pol[4 * kk + 3] = 0;
          indices.push(kk);
        }
      }
    });
  }
  // Cheveux : coques sur le cuir chevelu, mèches pour les longs.
  if (f.age > 0.3) {
    groupe(mats.coque, () => coques(b, P, normales, poils.cheveux, R, cheveux, e, ajouterSommet, indices));
    // Sourcils en relief.
    groupe(mats.coque, () => coques(b, P, normales, poils.sourcils, R, cheveux.clone().lerp(new THREE.Color(0.02, 0.015, 0.01), 0.3), e, ajouterSommet, indices,
      e * 0.035 * (0.6 + 0.6 * R.sourcils)));
    // Barbe en relief (courte ou longue selon l'homme).
    if (R.barbe > 0.15) {
      const longue = 0.04 + 0.3 * Math.pow(unite(hacher(id, 77)), 2) * lisser(20, 35, f.age);
      groupe(mats.coque, () => coques(b, P, normales, poils.barbe, R, cheveux.clone().multiplyScalar(0.85), e, ajouterSommet, indices, e * longue * R.barbe));
    }
    if (R.longueur > 0.22) groupe(mats.meche, () => meches(b, P, normales, poils.cheveux, R, cheveux, yeux, e, A, ajouterSommet, indices, id));
  }

  // 6. Géométrie et os.
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute("normal", new THREE.Float32BufferAttribute(nor, 3));
  g.setAttribute("uv", new THREE.Float32BufferAttribute(uvs, 2));
  g.setAttribute("color", new THREE.Float32BufferAttribute(col, 3));
  g.setAttribute("poil", new THREE.Float32BufferAttribute(pol, 4));
  g.setAttribute("seuil", new THREE.Float32BufferAttribute(seuils, 1));
  g.setAttribute("skinIndex", new THREE.Uint16BufferAttribute(osI, 4));
  g.setAttribute("skinWeight", new THREE.Float32BufferAttribute(osW, 4));
  (g.getAttribute("position") as THREE.BufferAttribute).setUsage(THREE.DynamicDrawUsage);
  g.setIndex(pos.length / 3 > 65535 ? new THREE.Uint32BufferAttribute(indices, 1) : new THREE.Uint16BufferAttribute(indices, 1));
  const materiauxListe: THREE.Material[] = [];
  for (const gr of groupes) {
    let i = materiauxListe.indexOf(gr.m);
    if (i < 0) i = materiauxListe.push(gr.m) - 1;
    g.addGroup(gr.debut, gr.n, i);
  }
  for (const nom of OS) repos[O[nom]] = A[nom].clone();
  repos[OEIL_G] = oeilG.clone();
  repos[OEIL_D] = oeilD.clone();
  // Correction des bras (repos en A → le long du corps, légèrement écartés comme le corps calculé).
  const corrections: (THREE.Quaternion | null)[] = repos.map(() => null);
  const ouvert = 0.09;
  for (const [cote, sx] of [["G", 1], ["D", -1]] as const) {
    const vise = new THREE.Vector3(sx * Math.sin(ouvert), -Math.cos(ouvert), 0);
    const bras = A[`avantBras${cote}`].clone().sub(A[`bras${cote}`]).normalize();
    const avant = A[`main${cote}`].clone().sub(A[`avantBras${cote}`]).normalize();
    corrections[O[`bras${cote}` as NomOs]] = new THREE.Quaternion().setFromUnitVectors(bras, vise);
    const qa = new THREE.Quaternion().setFromUnitVectors(avant, vise);
    corrections[O[`avantBras${cote}` as NomOs]] = qa;
    corrections[O[`main${cote}` as NomOs]] = qa.clone();
  }
  // Proportions (pour les poses et ce qui se place sur le corps).
  let menton = Infinity;
  for (let k = 0; k < b.triangles.length; k++) {
    const o = b.triangles[k];
    if (b.osIdx[4 * o] === O.tete && b.osPoids[4 * o] > 200 && P[3 * o + 2] > yeux.z - e * 0.6) menton = Math.min(menton, P[3 * o + 1]);
  }
  const p: Proportions = {
    hanche: A.bassin.y, genou: A.jambeG.y, cheville: A.piedG.y, taille: A.ventre.y, seins: A.poitrine.y, epaule: A.brasG.y,
    menton, hauteTete: 1 - menton, xHanche: Math.abs(A.cuisseG.x), xEpaule: Math.abs(A.brasG.x),
    coude: A.brasG.y - A.brasG.distanceTo(A.avantBrasG), poignet: A.brasG.y - A.brasG.distanceTo(A.avantBrasG) - A.avantBrasG.distanceTo(A.mainG),
    longMain: 0.1,
  };
  // Expressions : déplacements des unités, en coordonnées du corps (même échelle), sur les sommets de rendu.
  const versRendu = new Map<number, number[]>();
  for (const [r, k] of rendu) {
    const o = b.origine[r];
    const l = versRendu.get(o);
    if (l) l.push(k);
    else versRendu.set(o, [k]);
  }
  const deltas = EXPRESSIONS_REELLES.map((nom) => {
    const somme = new Map<number, [number, number, number]>();
    for (const [unite_, w] of UNITES[nom]) {
      const c2 = b.cibles.get("expr-" + unite_);
      if (!c2) continue;
      const k2 = c2.echelle * w * s;
      for (let k = 0; k < c2.idx.length; k++) {
        const o = c2.idx[k];
        const d = somme.get(o) ?? [0, 0, 0];
        d[0] += c2.val[3 * k] * k2;
        d[1] += c2.val[3 * k + 1] * k2;
        d[2] += c2.val[3 * k + 2] * k2;
        somme.set(o, d);
      }
    }
    const idx: number[] = [];
    const d: number[] = [];
    for (const [o, v3] of somme) for (const k of versRendu.get(o) ?? []) {
      idx.push(k);
      d.push(...v3);
    }
    return { idx: new Uint32Array(idx), d: new Float32Array(d) };
  });
  const anime = new VisageReel(unite(hacher(id, 91)) * 100, new Float32Array(pos), deltas, yeux.clone());
  return {
    geometrie: g, repos, p, corrections, materiaux: materiauxListe, yeux,
    autres: [{ nom: "oeilG", parent: "tete" }, { nom: "oeilD", parent: "tete" }],
    anime,
  };
}

type AjouterSommet = ((x: number, y: number, z: number, nx: number, ny: number, nz: number, u: number, v: number, r: number, g: number, b: number,
  os: ArrayLike<number>, w: ArrayLike<number>, poil?: [number, number, number, number], seuil?: number, densite?: number) => number) & { compte: () => number };

/** Globe de l'œil tourné vers +z, coordonnées de texture en projection de face, porté par l'os de l'œil. */
function globeOeil(centre: THREE.Vector3, rayon: number, os: number, ajouter: AjouterSommet, indices: number[]): void {
  const anneaux = 20;
  const segments = 32;
  const debut = ajouter(centre.x, centre.y, centre.z + rayon * 1.04, 0, 0, 1, 0.5, 0.5, 1, 1, 1, [os, 0, 0, 0], [1, 0, 0, 0]);
  for (let i = 1; i <= anneaux; i++) {
    // Anneaux serrés à l'avant (iris), lâches à l'arrière.
    const phi = Math.PI * Math.pow(i / anneaux, 1.5);
    for (let j = 0; j < segments; j++) {
      const a = (j / segments) * Math.PI * 2;
      const x = Math.sin(phi) * Math.cos(a);
      const y = Math.sin(phi) * Math.sin(a);
      const z = Math.cos(phi);
      // Cornée : légère bosse devant l'iris.
      const bosse = phi < 0.55 ? 0.04 * Math.cos((phi / 0.55) * Math.PI * 0.5) : 0;
      const r = rayon * (1 + bosse);
      ajouter(centre.x + x * r, centre.y + y * r, centre.z + z * r, x, y, z, 0.5 + 0.5 * x, 0.5 + 0.5 * y, 1, 1, 1, [os, 0, 0, 0], [1, 0, 0, 0]);
    }
  }
  for (let j = 0; j < segments; j++) indices.push(debut, debut + 1 + j, debut + 1 + ((j + 1) % segments));
  for (let i = 0; i < anneaux - 1; i++) {
    for (let j = 0; j < segments; j++) {
      const a = debut + 1 + i * segments + j;
      const b = debut + 1 + i * segments + ((j + 1) % segments);
      const c = a + segments;
      const d = b + segments;
      indices.push(a, c, b, b, c, d);
    }
  }
}

/**
 * Où poussent les poils : cheveux (ligne frontale, tempes, nuque, calvitie), sourcils, barbe, pubis. Repère de la tête : le
 * milieu des yeux, en écarts entre les yeux (`e`, 6 cm chez l'adulte).
 */
function poilsDe(b: BaseHumaine, P: Float64Array, normales: Float32Array, R: Reglages, yeux: THREE.Vector3, e: number, A: Record<string, THREE.Vector3>): Poils {
  const N = b.n;
  const cheveux = new Float32Array(N);
  const autres = new Float32Array(N);
  const barbe = new Float32Array(N);
  const sourcils = new Float32Array(N);
  const tete = O.tete;
  const cou = O.cou;
  const c = R.calvitie;
  for (let k = 0; k < N; k++) {
    const o0 = b.osIdx[4 * k];
    const w0 = b.osPoids[4 * k] / 255;
    const qx = (P[3 * k] - yeux.x) / e;
    const qy = (P[3 * k + 1] - yeux.y) / e;
    const qz = (P[3 * k + 2] - yeux.z) / e;
    const surTete = (o0 === tete && w0 > 0.3) || (o0 === cou && qy > -2.6);
    if (surTete && b.oreilles[k] < 0.05) {
      // Angle autour de l'axe vertical, depuis l'avant (0) jusqu'à la nuque (π).
      const a = Math.atan2(Math.abs(qx), qz + 1.5);
      const ligne = a < 0.55 ? 1.12 + 0.95 * lisser(0.35, 0.75, c)
        : a < 0.95 ? 1.12 - 0.15 * (a - 0.55) / 0.4 + 1.3 * lisser(0.3, 0.7, c)
          : a < 1.35 ? 0.97 - 0.75 * (a - 0.95) / 0.4
            : a < 1.9 ? 0.22 - 0.95 * (a - 1.35) / 0.55
              : -0.73 - 0.85 * Math.min(1, (a - 1.9) / 0.8);
      let d = lisser(ligne - 0.06, ligne + 0.14, qy);
      // Calvitie du sommet.
      if (c > 0.35) d *= 1 - lisser(2.35 - 1.2 * (c - 0.35), 2.15 - 1.2 * (c - 0.35), qy) * (a < 2.3 ? 1 : 0.6);
      cheveux[k] = d;
      // Sourcils : un arc au-dessus de chaque œil.
      const dx = Math.abs(qx);
      if (qz > -0.6 && dx > 0.1 && dx < 1.12) {
        const t = clamp((dx - 0.1) / 1.0, 0, 1);
        const yb = 0.4 + 0.12 * Math.sin(Math.PI * Math.min(1, t * 1.2)) - 0.12 * Math.max(0, t - 0.75);
        const ep = (0.085 + 0.06 * R.sourcils) * (1 - 0.4 * t);
        const s = (1 - lisser(ep * 0.55, ep, Math.abs(qy - yb))) * lisser(0.1, 0.22, dx) * (1 - lisser(0.95, 1.12, dx));
        sourcils[k] = Math.min(1, s * (0.8 + 0.5 * R.sourcils));
        autres[k] = Math.max(autres[k], sourcils[k]);
      }
      // Barbe : bas du visage, devant les oreilles, sous la mâchoire ; moustache.
      if (R.barbe > 0.02 && qz > -1.0 && b.levres[k] < 0.5) {
        // Ligne de la barbe : sous le nez, puis de la commissure vers la patte devant l'oreille.
        const haut = -0.8 + 1.25 * lisser(0.45, 1.25, a);
        const zone = lisser(haut + 0.12, haut - 0.12, qy) * lisser(-2.75 + 0.5 * lisser(0.6, 1.2, a), -2.35 + 0.5 * lisser(0.6, 1.2, a), qy)
          * (1 - lisser(1.2, 1.45, a)) * (1 - lisser(0.45, 0.7, b.levres[k]));
        barbe[k] = zone * R.barbe;
        autres[k] = Math.max(autres[k], zone * R.barbe);
      }
    }
    // Pubis et aisselles des adultes.
    if (R.adulte > 0.05) {
      const y = P[3 * k + 1];
      const x = P[3 * k];
      const z = P[3 * k + 2];
      const hy = A.bassin.y;
      const nz = normales[3 * k + 2];
      if (nz > 0.1 && z > 0 && y < hy + 0.035 && y > hy - 0.075) {
        const larg = 0.042 * (1 - 0.55 * clamp((hy + 0.035 - y) / 0.11, 0, 1));
        const zone = (1 - lisser(larg * 0.7, larg, Math.abs(x))) * lisser(hy + 0.035, hy + 0.015, y);
        autres[k] = Math.max(autres[k], zone * R.adulte * 0.95);
      }
    }
  }
  return { cheveux, autres, barbe, sourcils };
}

/**
 * Cheveux en coques : copies du cuir chevelu poussées vers l'extérieur (et un peu vers le bas pour les mi-longs) ; chaque coque
 * ne garde que les brins plus hauts qu'elle. Crépus : chevelure plus épaisse, coques plus nombreuses.
 */
function coques(b: BaseHumaine, P: Float64Array, normales: Float32Array, cheveux: Float32Array, R: Reglages, couleur: THREE.Color, e: number,
  ajouter: AjouterSommet, indices: number[], epaisseurDonnee?: number): void {
  const epaisseur = epaisseurDonnee ?? e * (0.06 + 0.22 * Math.min(1, R.longueur * 2.2) * (0.5 + R.crepus));
  const nombre = epaisseurDonnee !== undefined ? 5 : R.crepus > 0.6 ? 9 : 7;
  // Triangles du cuir chevelu (en sommets d'origine), rendus avec leurs coordonnées de texture.
  const tri = b.morceaux.peau;
  const garde: number[] = [];
  for (let k = 0; k < tri.length; k += 3) {
    const a = b.origine[tri[k]], bb = b.origine[tri[k + 1]], c = b.origine[tri[k + 2]];
    if (Math.max(cheveux[a], cheveux[bb], cheveux[c]) > 0.02) garde.push(k);
  }
  const densite = 14 + 8 * R.crepus;
  for (let s = 1; s <= nombre; s++) {
    const t = s / nombre;
    const plan = new Map<number, number>();
    for (const k of garde) {
      for (let j = 0; j < 3; j++) {
        const r = tri[k + j];
        let i = plan.get(r);
        if (i === undefined) {
          const o = b.origine[r];
          const d = cheveux[o];
          const h = epaisseur * t * (0.25 + 0.75 * d);
          const chute = R.longueur > 0.2 && epaisseurDonnee === undefined ? epaisseur * 0.5 * t * t : 0;
          const ombre = 0.45 + 0.55 * t;
          i = ajouter(P[3 * o] + normales[3 * o] * h, P[3 * o + 1] + normales[3 * o + 1] * h - chute, P[3 * o + 2] + normales[3 * o + 2] * h,
            normales[3 * o], normales[3 * o + 1], normales[3 * o + 2], b.uv[2 * r], b.uv[2 * r + 1],
            couleur.r * ombre, couleur.g * ombre, couleur.b * ombre,
            [b.osIdx[4 * o], b.osIdx[4 * o + 1], b.osIdx[4 * o + 2], b.osIdx[4 * o + 3]],
            [b.osPoids[4 * o] / 255, b.osPoids[4 * o + 1] / 255, b.osPoids[4 * o + 2] / 255, b.osPoids[4 * o + 3] / 255],
            [0, 0, 0, 0],
            // Moins de brins là où la chevelure s'arrête (la ligne frontale reste nette mais pas tranchée).
            0.22 + 0.55 * t + 0.6 * (1 - d), densite);
          plan.set(r, i);
        }
        indices.push(i);
      }
    }
  }
}

/**
 * Cheveux longs : un rideau en deux couches, de l'arrière des tempes à la nuque, qui épouse le crâne, tombe à la verticale
 * sous sa partie la plus large et se pose sur le cou, les épaules et le dos ; plus long derrière que sur les côtés. Les brins
 * (texture) finissent à des longueurs inégales. Le haut suit la tête, le bas le haut du dos.
 */
function meches(b: BaseHumaine, P: Float64Array, _normales: Float32Array, _cheveux: Float32Array, R: Reglages, couleur: THREE.Color, yeux: THREE.Vector3,
  e: number, A: Record<string, THREE.Vector3>, ajouter: AjouterSommet, indices: number[], id: number): void {
  // Axe de la tête et profil du corps : plus grande distance à l'axe par angle (autour de la verticale) et par hauteur.
  let zMin = Infinity;
  let zMax = -Infinity;
  for (let k = 0; k < b.triangles.length; k++) {
    const o = b.triangles[k];
    if (b.osIdx[4 * o] === O.tete && P[3 * o + 1] > yeux.y) {
      zMin = Math.min(zMin, P[3 * o + 2]);
      zMax = Math.max(zMax, P[3 * o + 2]);
    }
  }
  const cx = 0;
  const cz = (zMin + zMax) / 2;
  const NA = 48;
  const pasY = 0.006;
  const yHaut = 1.002;
  const longueur = Math.min(1, R.longueur) * (1 - 0.45 * R.crepus);
  const yBasDos = A.cou.y - (0.035 + 0.24 * longueur);
  const yBasCote = Math.max(yBasDos, A.brasG.y + 0.035 - 0.08 * longueur);
  const NY = Math.ceil((yHaut - Math.min(yBasDos, yBasCote) + 0.02) / pasY) + 1;
  const profil = new Float32Array(NA * NY);
  const exclus = new Set([O.brasG, O.brasD, O.avantBrasG, O.avantBrasD, O.mainG, O.mainD]);
  for (let k = 0; k < b.triangles.length; k++) {
    const o = b.triangles[k];
    if (exclus.has(b.osIdx[4 * o])) continue;
    const y = P[3 * o + 1];
    const iy = Math.round((yHaut - y) / pasY);
    if (iy < 0 || iy >= NY) continue;
    const dx = P[3 * o] - cx;
    const dz = P[3 * o + 2] - cz;
    const ia = ((Math.round((Math.atan2(dx, dz) / (Math.PI * 2)) * NA) % NA) + NA) % NA;
    profil[ia * NY + iy] = Math.max(profil[ia * NY + iy], Math.hypot(dx, dz));
  }
  // Trous comblés par les voisins (angle).
  const rayon = (a: number, y: number): number => {
    const fa = ((a / (Math.PI * 2)) * NA + NA * 4) % NA;
    const iy = clamp(Math.round((yHaut - y) / pasY), 0, NY - 1);
    let r = 0;
    for (let d = -1; d <= 1; d++) {
      const ia = (Math.round(fa) + d + NA) % NA;
      for (let dy = -1; dy <= 1; dy++) r = Math.max(r, profil[ia * NY + clamp(iy + dy, 0, NY - 1)]);
    }
    return r;
  };
  // Le rideau : colonnes d'angle (θ de l'avant), lignes de hauteur.
  const theta0 = 1.2;
  const colonnes = 56;
  const lignes = 26;
  const epais = e * (0.1 + 0.25 * R.crepus);
  const yNuque = yeux.y - 0.6 * e;
  for (const couche of [0, 1]) {
    const debut = ajouter.compte();
    for (let j = 0; j <= colonnes; j++) {
      const th = theta0 + ((Math.PI * 2 - 2 * theta0) * j) / colonnes;
      const cote = Math.abs(Math.PI - th) / (Math.PI - theta0);
      const yFin = yBasDos + (yBasCote - yBasDos) * lisser(0.35, 0.95, cote) - 0.02 * unite(hacher(id, j, 51));
      // Rayon du crâne le plus large sous lequel les cheveux tombent.
      let rLarge = 0;
      let yLarge = yHaut;
      for (let y = yHaut; y > yNuque; y -= pasY) {
        const r = rayon(th, y);
        if (r > rLarge) {
          rLarge = r;
          yLarge = y;
        }
      }
      for (let i = 0; i <= lignes; i++) {
        const t = i / lignes;
        // Lignes serrées en haut (le crâne s'arrondit), plus lâches dans la chute.
        const y = yHaut - (yHaut - yFin) * t * t * 0.5 - (yHaut - yFin) * t * 0.5;
        let r = rayon(th, y);
        if (y < yLarge) r = Math.max(r, rLarge - 0.25 * (yLarge - y));
        r += epais * (couche ? 1 : 0.45) * (0.4 + 0.6 * lisser(yHaut, yHaut - 0.03, y)) + 0.003;
        const x = cx + Math.sin(th) * r;
        const z = cz + Math.cos(th) * r;
        const sur = lisser(A.cou.y + 0.02, A.cou.y - 0.05, y);
        const ombre = (couche ? 1 : 0.7) * (0.6 + 0.4 * lisser(0, 0.15, t));
        // Les bords du rideau s'éclaircissent (moins de brins), pour ne pas finir net.
        const bord = Math.min(j, colonnes - j) / colonnes;
        ajouter(x, y, z, Math.sin(th), 0.25, Math.cos(th), (th / (Math.PI * 2)) * (7 + couche * 2) + couche * 0.37, t,
          couleur.r * ombre, couleur.g * ombre, couleur.b * ombre, [O.tete, O.poitrine, 0, 0], [1 - sur, sur, 0, 0], [0, 0, 0, 0],
          0.55 * (1 - lisser(0, 0.14, bord)));
      }
    }
    for (let j = 0; j < colonnes; j++) {
      for (let i = 0; i < lignes; i++) {
        const a = debut + j * (lignes + 1) + i;
        const c = a + lignes + 1;
        indices.push(a, a + 1, c, c, a + 1, c + 1);
      }
    }
  }
}
