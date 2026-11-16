import * as THREE from "three/webgpu";
import { abs, attribute, clamp, cos, float, max, min, mix, normalize, sin, smoothstep, transformNormalToView, vec3 } from "three/tsl";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { hacher, unite } from "./bruit";

/**
 * Corps des bêtes de la vue 3D (0.44.0, choix A1 et B1) : chaque espèce du monde a un corps calculé d'après sa forme, son trait et
 * son numéro, à l'allure d'un animal du Crétacé. La simulation garde ses huit formes ; la vue les habille ainsi :
 * 0 bête à cornes → cératopsien ; 1 bête à sabots → ornithopode (hadrosaure léger) ; 2 petit brouteur → petit ornithopode ou
 * pachycéphalosaure à dôme ; 3 fouisseur → ankylosaure trapu ; 4 rôdeur → petit dromaeosaure à plumes ; 5 grand chasseur →
 * théropode moyen ; 6 oiseau coureur → ornithomimidé ; 7 grimpeur → petit dinosaure à plumes des arbres.
 *
 * Le corps est fait d'une colonne (queue, dos, cou, tête) dont la coupe suit un profil, et de pattes à deux articulations. Chaque
 * sommet sait à quel « os » il appartient (aOs) et autour de quel point il tourne (aPivot, aGenou) : la carte graphique plie les
 * pattes, baisse la tête, balance la queue et couche la bête d'après quatre nombres par bête (iA, iB, iC). Rien n'est téléchargé.
 */

/** Nombres par groupe de bêtes dans le flux Camp (CampDto.Betes, 0.44.0). */
export const CHAMPS_BETES = 11;

/** Une espèce telle que la donne le serveur (EspeceDto). */
export interface Espece {
  numero: number;
  forme: number;
  trait: number;
  poids: number;
  regime: number;
  panique: number;
  agressivite: number;
  troupeau: boolean;
  charognarde: boolean;
  nom: string;
  disparue: boolean;
}

/** Longueur du museau au bout de la queue (m) pour un kilo, à la racine cubique : d'après des squelettes connus (Protocératops 1,8 m
 *  pour 80 kg, Vélociraptor 2 m pour 15 kg, Ornithomimus 3,5 m pour 170 kg, petit ankylosaure 3 m pour 300 kg). */
const LONGUEUR_PAR_KG = [0.42, 0.5, 0.55, 0.45, 0.8, 0.65, 0.62, 0.72];

/** Longueur (m) d'une bête de cette forme et de ce poids. */
export function longueur(forme: number, poids: number): number {
  return (LONGUEUR_PAR_KG[forme] ?? 0.5) * Math.cbrt(Math.max(0.5, poids));
}

/** Formes couvertes de plumes (les autres : écailles, parfois quelques filaments). */
export const A_PLUMES = new Set([4, 5, 6, 7]);

/** Ce que la vue sait du corps d'une espèce, une fois calculé (longueur 1). */
export interface Gabarit {
  bipede: boolean;
  /** Hauteur de la hanche et du dos, pour une bête de longueur 1. */
  hanche: number;
  dos: number;
  /** Pas (longueur d'une foulée en marchant) pour une bête de longueur 1. */
  foulee: number;
}

/** Degré de détail d'un corps : côtés d'une coupe, anneaux par unité de longueur, côtés d'une patte, accessoires. */
export interface Detail {
  cotes: number;
  anneaux: number;
  cotesPatte: number;
  accessoires: boolean;
}

export const DETAILS: Detail[] = [
  { cotes: 14, anneaux: 46, cotesPatte: 7, accessoires: true },
  { cotes: 8, anneaux: 20, cotesPatte: 5, accessoires: true },
  { cotes: 5, anneaux: 9, cotesPatte: 3, accessoires: false },
];

/** Un point du profil de la colonne : avant (z), hauteur (y), demi-largeur, demi-hauteur. */
type Cle = [number, number, number, number];

interface Plan {
  cles: Cle[];
  /** Index de la clé où commence la queue (vers l'arrière) et où commence le cou (vers l'avant). */
  iQueue: number;
  iCou: number;
  bipede: boolean;
  /** Pattes : position de la hanche (x, y, z), longueur relative des segments et épaisseur. */
  arriere: { x: number; y: number; z: number; r: number };
  avant: { x: number; y: number; z: number; r: number } | null;
  /** Bras qui ne touchent pas le sol (bipèdes). */
  bras: { x: number; y: number; z: number; l: number; r: number } | null;
}

/** Tirage stable pour une espèce. */
function tirage(numero: number, k: number): number {
  return unite(hacher(numero, k, 4421));
}

/** Le profil de chaque forme, de la pointe de la queue au bout du museau (longueur totale environ 1). */
function plan(e: Espece): Plan {
  const r = (k: number) => tirage(e.numero, k);
  const cou = e.trait === 9 ? 1.5 : 1;
  const queue = e.trait === 5 ? 1.35 : 1;
  const museau = e.trait === 4 ? 1.35 : 1;
  const gros = e.trait === 6 ? 1.12 : 1;
  const v = (a: number, b: number, k: number) => a + (b - a) * r(k);
  let cles: Cle[];
  let iQueue: number;
  let iCou: number;
  let bipede = false;
  let arriere: Plan["arriere"];
  let avant: Plan["avant"] = null;
  let bras: Plan["bras"] = null;
  switch (e.forme) {
    case 0: {
      // Cératopsien : trapu, quatre pattes, grosse tête à collerette.
      const h = v(0.2, 0.25, 1);
      cles = [[-0.5 * queue, h * 0.65, 0.008, 0.008], [-0.32 * queue, h * 0.82, 0.04, 0.05], [-0.14, h * 1.02, 0.09, 0.1], [-0.02, h * 1.0, 0.12, 0.115],
        [0.12, h * 0.92, 0.1, 0.1], [0.19, h * 0.88, 0.065, 0.07], [0.25, h * 0.9, 0.06, 0.075], [0.25 + 0.08 * museau, h * 0.78, 0.045, 0.055],
        [0.25 + 0.15 * museau, h * 0.66, 0.022, 0.03], [0.25 + 0.17 * museau, h * 0.6, 0.006, 0.008]];
      iQueue = 2;
      iCou = 5;
      arriere = { x: 0.08, y: h * 0.95, z: -0.11, r: 0.045 };
      avant = { x: 0.08, y: h * 0.85, z: 0.11, r: 0.04 };
      break;
    }
    case 1: {
      // Ornithopode (hadrosaure léger) : longues pattes arrière, bras plus courts posés au sol, bec large.
      const h = v(0.26, 0.3, 1);
      cles = [[-0.52 * queue, h * 0.8, 0.008, 0.012], [-0.3 * queue, h * 0.98, 0.035, 0.06], [-0.12, h * 1.12, 0.075, 0.1], [0.0, h * 1.06, 0.085, 0.1],
        [0.1, h * 0.98, 0.065, 0.075], [0.16, h * 1.0 + 0.04 * cou, 0.035, 0.04], [0.16 + 0.07 * cou, h * 1.15 + 0.06 * cou, 0.03, 0.035],
        [0.25 + 0.07 * cou, h * 1.18 + 0.06 * cou, 0.035, 0.04], [0.25 + 0.07 * cou + 0.08 * museau, h * 1.08 + 0.06 * cou, 0.03, 0.022],
        [0.25 + 0.07 * cou + 0.11 * museau, h * 1.03 + 0.06 * cou, 0.028, 0.01]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.06, y: h * 1.05, z: -0.1, r: 0.03 };
      avant = { x: 0.055, y: h * 0.95, z: 0.09, r: 0.016 };
      break;
    }
    case 2: {
      // Petit ornithopode bipède, ou pachycéphalosaure (dôme) selon l'espèce.
      const h = v(0.24, 0.3, 1);
      bipede = true;
      cles = [[-0.55 * queue, h * 0.9, 0.006, 0.008], [-0.3 * queue, h * 1.0, 0.03, 0.04], [-0.1, h * 1.08, 0.07, 0.085], [0.03, h * 1.1, 0.07, 0.08],
        [0.11, h * 1.18, 0.045, 0.05], [0.15, h * 1.28 + 0.04 * cou, 0.025, 0.028], [0.15 + 0.06 * cou, h * 1.38 + 0.04 * cou, 0.035, 0.04],
        [0.15 + 0.06 * cou + 0.07 * museau, h * 1.33 + 0.04 * cou, 0.022, 0.026], [0.15 + 0.06 * cou + 0.09 * museau, h * 1.3 + 0.04 * cou, 0.006, 0.008]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.05, y: h * 1.05, z: -0.06, r: 0.026 };
      bras = { x: 0.045, y: h * 1.1, z: 0.09, l: 0.09, r: 0.01 };
      break;
    }
    case 3: {
      // Ankylosaure : bas, large, cuirassé, massue au bout de la queue.
      const h = v(0.15, 0.18, 1);
      cles = [[-0.5 * queue, h * 0.75, 0.03, 0.025], [-0.42 * queue, h * 0.8, 0.02, 0.018], [-0.25 * queue, h * 0.9, 0.04, 0.035], [-0.12, h * 1.05, 0.13, 0.08],
        [0.04, h * 1.1, 0.15, 0.09], [0.16, h * 1.0, 0.11, 0.07], [0.22, h * 0.95, 0.07, 0.05], [0.22 + 0.06 * museau, h * 0.9, 0.065, 0.045],
        [0.22 + 0.12 * museau, h * 0.82, 0.04, 0.03], [0.22 + 0.13 * museau, h * 0.8, 0.01, 0.01]];
      iQueue = 3;
      iCou = 6;
      arriere = { x: 0.11, y: h * 0.95, z: -0.1, r: 0.035 };
      avant = { x: 0.1, y: h * 0.9, z: 0.13, r: 0.03 };
      break;
    }
    case 4: {
      // Dromaeosaure : fin, longue queue raide, tête allongée, bras emplumés repliés.
      const h = v(0.17, 0.2, 1);
      bipede = true;
      cles = [[-0.6 * queue, h * 1.02, 0.006, 0.008], [-0.3 * queue, h * 1.04, 0.02, 0.024], [-0.08, h * 1.05, 0.045, 0.055], [0.05, h * 1.1, 0.045, 0.06],
        [0.11, h * 1.2, 0.03, 0.035], [0.14, h * 1.35 + 0.03 * cou, 0.02, 0.022], [0.14 + 0.04 * cou, h * 1.45 + 0.04 * cou, 0.024, 0.028],
        [0.14 + 0.04 * cou + 0.07 * museau, h * 1.4 + 0.04 * cou, 0.016, 0.02], [0.14 + 0.04 * cou + 0.1 * museau, h * 1.36 + 0.04 * cou, 0.005, 0.006]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.035, y: h * 1.02, z: -0.04, r: 0.02 };
      bras = { x: 0.035, y: h * 1.15, z: 0.08, l: 0.09, r: 0.008 };
      break;
    }
    case 5: {
      // Théropode moyen : grosse tête, cou en S, petits bras.
      const h = v(0.22, 0.26, 1);
      bipede = true;
      cles = [[-0.55 * queue, h * 0.95, 0.008, 0.01], [-0.3 * queue, h * 1.02, 0.035, 0.045], [-0.08, h * 1.08, 0.075, 0.09], [0.06, h * 1.1, 0.075, 0.095],
        [0.13, h * 1.18, 0.05, 0.06], [0.17, h * 1.3 + 0.03 * cou, 0.04, 0.045], [0.17 + 0.05 * cou, h * 1.36 + 0.04 * cou, 0.045, 0.055],
        [0.17 + 0.05 * cou + 0.08 * museau, h * 1.32 + 0.04 * cou, 0.035, 0.04], [0.17 + 0.05 * cou + 0.12 * museau, h * 1.27 + 0.04 * cou, 0.012, 0.015]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.05, y: h * 1.04, z: -0.05, r: 0.032 };
      bras = { x: 0.05, y: h * 1.08, z: 0.11, l: 0.06, r: 0.01 };
      break;
    }
    case 6: {
      // Ornithomimidé : long cou, petite tête à bec, très longues pattes.
      const h = v(0.3, 0.34, 1);
      bipede = true;
      cles = [[-0.5 * queue, h * 0.98, 0.006, 0.008], [-0.28 * queue, h * 1.0, 0.025, 0.03], [-0.07, h * 1.02, 0.06, 0.07], [0.05, h * 1.05, 0.06, 0.07],
        [0.1, h * 1.12, 0.03, 0.03], [0.14, h * 1.35 + 0.06 * cou, 0.026, 0.028], [0.17, h * 1.55 + 0.1 * cou, 0.022, 0.024],
        [0.19 + 0.02 * cou, h * 1.62 + 0.12 * cou, 0.022, 0.024], [0.19 + 0.02 * cou + 0.05 * museau, h * 1.6 + 0.12 * cou, 0.01, 0.012],
        [0.19 + 0.02 * cou + 0.07 * museau, h * 1.58 + 0.12 * cou, 0.003, 0.004]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.04, y: h * 1.0, z: -0.04, r: 0.024 };
      bras = { x: 0.04, y: h * 1.1, z: 0.075, l: 0.1, r: 0.009 };
      break;
    }
    default: {
      // Grimpeur : petit, longue queue, longs bras griffus, grands yeux.
      const h = v(0.16, 0.2, 1);
      bipede = true;
      cles = [[-0.62 * queue, h * 1.2, 0.006, 0.008], [-0.3 * queue, h * 1.08, 0.022, 0.026], [-0.08, h * 1.05, 0.05, 0.06], [0.04, h * 1.12, 0.05, 0.06],
        [0.1, h * 1.25, 0.03, 0.032], [0.13, h * 1.38 + 0.03 * cou, 0.035, 0.04], [0.13 + 0.06 * museau, h * 1.33 + 0.03 * cou, 0.018, 0.022],
        [0.13 + 0.08 * museau, h * 1.3 + 0.03 * cou, 0.005, 0.006]];
      iQueue = 2;
      iCou = 4;
      arriere = { x: 0.035, y: h * 1.0, z: -0.04, r: 0.02 };
      bras = { x: 0.04, y: h * 1.2, z: 0.07, l: 0.13, r: 0.01 };
    }
  }
  cles = cles.map(([z, y, rx, ry]) => [z, y, rx * gros, ry * gros]);
  return { cles, iQueue, iCou, bipede, arriere, avant, bras };
}

/** Couleurs d'une espèce : dos, ventre, motif, et ce que le trait y ajoute. */
function robe(e: Espece): { dos: THREE.Color; ventre: THREE.Color; motif: THREE.Color } {
  const r = (k: number) => tirage(e.numero, k);
  // Teintes de grands reptiles et d'oiseaux terrestres : bruns, olives, gris, sables ; les plumes un peu plus vives.
  const bases = [[0.36, 0.3, 0.2], [0.3, 0.32, 0.2], [0.34, 0.33, 0.3], [0.45, 0.38, 0.26], [0.26, 0.24, 0.2], [0.4, 0.34, 0.22], [0.3, 0.28, 0.24]];
  const b = bases[Math.floor(r(11) * bases.length)];
  const dos = new THREE.Color(b[0], b[1], b[2]);
  if (e.trait === 0) dos.setRGB(0.55, 0.3, 0.15);
  if (A_PLUMES.has(e.forme)) dos.offsetHSL((r(12) - 0.5) * 0.06, 0.08 * r(13), 0.03);
  dos.offsetHSL((r(14) - 0.5) * 0.04, (r(15) - 0.5) * 0.1, (r(16) - 0.5) * 0.08);
  const ventre = dos.clone().lerp(new THREE.Color(0.78, 0.72, 0.6), 0.55);
  const motif = dos.clone().multiplyScalar(0.45);
  if (e.trait === 6) ventre.lerp(new THREE.Color(0.85, 0.82, 0.75), 0.3);
  return { dos, ventre, motif };
}

/** Données d'un morceau de corps en construction (sommets, normales recalculées ensuite, couleurs, os). */
class Morceau {
  pos: number[] = [];
  col: number[] = [];
  os: number[] = [];
  pivot: number[] = [];
  genou: number[] = [];
  idx: number[] = [];

  sommet(p: THREE.Vector3, c: THREE.Color, os: [number, number, number, number], pivot: THREE.Vector3, genou: [number, number, number, number]): number {
    this.pos.push(p.x, p.y, p.z);
    this.col.push(c.r, c.g, c.b);
    this.os.push(...os);
    this.pivot.push(pivot.x, pivot.y, pivot.z);
    this.genou.push(...genou);
    return this.pos.length / 3 - 1;
  }

  geometrie(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(this.pos, 3));
    g.setAttribute("color", new THREE.Float32BufferAttribute(this.col, 3));
    g.setAttribute("aOs", new THREE.Float32BufferAttribute(this.os, 4));
    g.setAttribute("aPivot", new THREE.Float32BufferAttribute(this.pivot, 3));
    g.setAttribute("aGenou", new THREE.Float32BufferAttribute(this.genou, 4));
    g.setIndex(this.idx);
    g.computeVertexNormals();
    return g;
  }
}

const RIEN4: [number, number, number, number] = [0, 0, 0, 0];
const ZERO = new THREE.Vector3();

/** Interpole le profil à l'indice fractionnaire f (lissé entre deux clés). */
function profil(cles: Cle[], f: number): Cle {
  const i = Math.max(0, Math.min(cles.length - 2, Math.floor(f)));
  const u = Math.max(0, Math.min(1, f - i));
  const s = u * u * (3 - 2 * u);
  const a = cles[i];
  const b = cles[i + 1];
  return [a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * s, a[2] + (b[2] - a[2]) * s, a[3] + (b[3] - a[3]) * s];
}

/**
 * Le corps d'une espèce, à la longueur 1, au degré de détail voulu. Les sommets portent : couleur, os (genre, avancée le long de
 * l'os, décalage de la foulée, patte avant 1 / arrière 0), pivot de l'os, genou (point et poids).
 */
export function corpsDe(e: Espece, d: Detail): { geo: THREE.BufferGeometry; gabarit: Gabarit } {
  const p = plan(e);
  const { dos, ventre, motif } = robe(e);
  const m = new Morceau();
  const cles = p.cles;
  const n = cles.length;
  const pivotQueue = new THREE.Vector3(0, cles[p.iQueue][1], cles[p.iQueue][0]);
  const pivotCou = new THREE.Vector3(0, cles[p.iCou][1], cles[p.iCou][0]);
  // Longueur le long de la colonne, pour répartir les anneaux.
  let total = 0;
  for (let k = 0; k + 1 < n; k++) total += Math.hypot(cles[k + 1][0] - cles[k][0], cles[k + 1][1] - cles[k][1]);
  const anneaux = Math.max(6, Math.round(d.anneaux * total));
  const c = new THREE.Color();
  const v = new THREE.Vector3();
  const cote = d.cotes;
  const zTete = cles[n - 1][0];
  // Colonne.
  const fs: number[] = [];
  {
    // Indices fractionnaires répartis selon la longueur.
    const longueurs = [0];
    for (let k = 0; k + 1 < n; k++) longueurs.push(longueurs[k] + Math.hypot(cles[k + 1][0] - cles[k][0], cles[k + 1][1] - cles[k][1]));
    for (let a = 0; a <= anneaux; a++) {
      const l = (a / anneaux) * total;
      let k = 0;
      while (k + 1 < longueurs.length - 1 && longueurs[k + 1] < l) k++;
      fs.push(k + (l - longueurs[k]) / Math.max(1e-6, longueurs[k + 1] - longueurs[k]));
    }
  }
  for (let a = 0; a <= anneaux; a++) {
    const f = fs[a];
    const [z, y, rx, ry] = profil(cles, f);
    const genre = f < p.iQueue ? 2 : f > p.iCou ? 1 : 0;
    const t = genre === 2 ? (p.iQueue - f) / p.iQueue : genre === 1 ? (f - p.iCou) / (n - 1 - p.iCou) : 0;
    const pivot = genre === 2 ? pivotQueue : genre === 1 ? pivotCou : ZERO;
    for (let s = 0; s < cote; s++) {
      const ang = (s / cote) * Math.PI * 2;
      const cy = Math.cos(ang);
      const sx = Math.sin(ang);
      // Ventre un peu plus plat que le dos.
      const yy = cy >= 0 ? cy : cy * 0.85;
      v.set(sx * rx, y + yy * ry, z);
      // Couleur : dos sombre, ventre clair (contre-ombrage), motifs du trait.
      const haut = (cy + 1) / 2;
      c.copy(ventre).lerp(dos, Math.min(1, haut * 1.4));
      if (e.trait === 2 && haut > 0.35 && Math.sin(z * 70 + sx) > 0.35) c.lerp(motif, 0.7);
      if (e.trait === 1 && haut > 0.3 && unite(hacher(Math.round(z * 40), Math.round(sx * 6 + cy * 6), e.numero)) > 0.72) c.lerp(motif, 0.6);
      if (e.trait === 7 && z > zTete - 0.05) c.lerp(new THREE.Color(0.08, 0.07, 0.06), 0.8);
      m.sommet(v, c, [genre, t, 0, 0], pivot, RIEN4);
    }
  }
  for (let a = 0; a < anneaux; a++) {
    for (let s = 0; s < cote; s++) {
      const i0 = a * cote + s;
      const i1 = a * cote + ((s + 1) % cote);
      m.idx.push(i0, i0 + cote, i1, i1, i0 + cote, i1 + cote);
    }
  }
  const morceaux: THREE.BufferGeometry[] = [m.geometrie()];
  // Pattes : hanche, genou, cheville, pied. Arrière digitigrade (genou en avant), avant droit.
  const pattes: { x: number; y: number; z: number; r: number; avant: boolean; decal: number }[] = [];
  const a0 = p.arriere;
  if (p.bipede) {
    pattes.push({ ...a0, avant: false, decal: 0 }, { ...a0, x: -a0.x, avant: false, decal: Math.PI });
  } else {
    const av = p.avant!;
    // Marche en séquence latérale : arrière gauche, avant gauche, arrière droite, avant droite.
    pattes.push({ ...a0, avant: false, decal: 0 }, { ...av, avant: true, decal: Math.PI / 2 }, { ...a0, x: -a0.x, avant: false, decal: Math.PI },
      { ...av, x: -av.x, avant: true, decal: Math.PI * 1.5 });
  }
  for (const pa of pattes) morceaux.push(patte(pa, d, p.bipede, dos, ventre, e.trait === 8));
  if (p.bras) {
    for (const sgn of [1, -1]) morceaux.push(bras(p.bras, sgn, d, dos, A_PLUMES.has(e.forme)));
  }
  // Yeux et accessoires (cornes, collerette, dôme, crête, cuirasse, voile, plumes de la queue).
  const tete = profil(cles, n - 1 - (n - 1 - p.iCou) * 0.3);
  const tTete = 0.75;
  const surTete = (g: THREE.BufferGeometry, col: THREE.Color) => colorer(g, col, [1, tTete, 0, 0], pivotCou);
  const surDos = (g: THREE.BufferGeometry, col: THREE.Color) => colorer(g, col, [0, 0, 0, 0], ZERO);
  const yeux = (r: number) => {
    for (const sgn of [1, -1]) morceaux.push(surTete(new THREE.SphereGeometry(r, 6, 4).translate(sgn * tete[2] * 0.85, tete[1] + tete[3] * 0.35, tete[0]), new THREE.Color(0.04, 0.03, 0.02)));
  };
  if (d.accessoires) {
    yeux(Math.max(0.006, tete[3] * (e.forme === 7 ? 0.35 : 0.2)));
    const corne = dos.clone().lerp(new THREE.Color(0.8, 0.75, 0.62), 0.6);
    const grand = e.trait === 11 ? 1.6 : 1;
    if (e.forme === 0) {
      // Collerette et cornes.
      const back = profil(cles, p.iCou + 1);
      morceaux.push(surTete(new THREE.SphereGeometry(1, 10, 6).scale(0.085, 0.07, 0.012).rotateX(-1.05).translate(0, back[1] + 0.045, back[0] - 0.015), dos.clone().multiplyScalar(0.85)));
      for (const sgn of [1, -1]) morceaux.push(surTete(new THREE.ConeGeometry(0.012, 0.07 * grand, 5).rotateX(0.9).translate(sgn * 0.035, tete[1] + 0.06, tete[0] + 0.02), corne));
      const nez = profil(cles, n - 2.3);
      morceaux.push(surTete(new THREE.ConeGeometry(0.012, 0.045 * grand, 5).rotateX(0.5).translate(0, nez[1] + nez[3] + 0.01, nez[0]), corne));
    }
    if (e.forme === 2 && tirage(e.numero, 21) < 0.5) {
      // Pachycéphalosaure : dôme épais.
      morceaux.push(surTete(new THREE.SphereGeometry(tete[2] * 1.3, 8, 5).scale(1, 0.8, 1).translate(0, tete[1] + tete[3] * 0.7, tete[0] - 0.01), corne));
    }
    if (e.forme === 3) {
      // Ankylosaure : rangées d'épines sur les flancs et massue au bout de la queue.
      for (let k = 0; k < 6; k++) {
        const f = p.iQueue + (p.iCou - p.iQueue) * (k + 0.5) / 6;
        const [z, y, rx] = profil(cles, f);
        for (const sgn of [1, -1]) morceaux.push(surDos(new THREE.ConeGeometry(0.012, 0.04 * grand, 4).rotateZ(-sgn * 1.3).translate(sgn * rx * 1.02, y, z), corne));
      }
      const bout = cles[0];
      morceaux.push(colorer(new THREE.SphereGeometry(0.045, 8, 5).scale(1.3, 0.7, 1).translate(0, bout[1], bout[0] + 0.02), corne, [2, 1, 0, 0], pivotQueue));
    }
    if (e.trait === 3 || e.forme === 1 && tirage(e.numero, 22) < 0.4) {
      // Crête : sur la tête, vers l'arrière.
      morceaux.push(surTete(new THREE.SphereGeometry(1, 8, 5).scale(0.008, 0.045, 0.06).rotateX(0.5).translate(0, tete[1] + tete[3] + 0.025, tete[0] - 0.03), motif.clone().lerp(new THREE.Color(0.6, 0.2, 0.1), 0.4)));
    }
    if (e.trait === 10) {
      // Bosse ou voile sur le dos.
      const [z, y, , ry] = profil(cles, (p.iQueue + p.iCou) / 2);
      morceaux.push(surDos(new THREE.SphereGeometry(1, 10, 6).scale(0.012, 0.07, 0.12).translate(0, y + ry, z), dos.clone().multiplyScalar(0.8)));
    }
    if (e.trait === 11 && e.forme !== 3) {
      // Épines le long du dos et de la queue.
      for (let k = 0; k < 8; k++) {
        const f = 0.5 + (p.iCou - 0.5) * (k / 8);
        const [z, y, , ry] = profil(cles, f);
        const genre = f < p.iQueue ? 2 : 0;
        morceaux.push(colorer(new THREE.ConeGeometry(0.008, 0.03, 4).translate(0, y + ry + 0.012, z), corne,
          [genre, genre === 2 ? (p.iQueue - f) / p.iQueue : 0, 0, 0], genre === 2 ? pivotQueue : ZERO));
      }
    }
    if (A_PLUMES.has(e.forme) && e.forme !== 5) {
      // Éventail de plumes au bout de la queue.
      const [z, y] = profil(cles, 0.6);
      morceaux.push(colorer(new THREE.SphereGeometry(1, 8, 4).scale(0.05, 0.008, 0.11).translate(0, y, z), motif.clone().lerp(dos, 0.4), [2, 0.85, 0, 0], pivotQueue));
    }
  }
  const geo = mergeGeometries(morceaux)!;
  for (const g of morceaux) g.dispose();
  const ar = p.arriere;
  return { geo, gabarit: { bipede: p.bipede, hanche: ar.y, dos: Math.max(...cles.map((k) => k[1] + k[3])), foulee: ar.y * (p.bipede ? 1.6 : 1.3) } };
}

/** Donne à une géométrie simple les attributs d'un os (tous ses sommets tournent avec lui). */
function colorer(g0: THREE.BufferGeometry, col: THREE.Color, os: [number, number, number, number], pivot: THREE.Vector3): THREE.BufferGeometry {
  return attacher(g0, col, os, pivot, [0, 0, 0, 0]);
}

/** Comme colorer, avec un genou (pour ce qui suit le bas d'une patte). */
function attacher(g0: THREE.BufferGeometry, col: THREE.Color, os: [number, number, number, number], pivot: THREE.Vector3,
  genou: [number, number, number, number]): THREE.BufferGeometry {
  const g = g0.index ? g0.toNonIndexed() : g0;
  if (g !== g0) g0.dispose();
  g.deleteAttribute("uv");
  const n = g.getAttribute("position").count;
  const remplir = (k: number, vals: number[]) => {
    const a = new Float32Array(n * k);
    for (let i = 0; i < n; i++) a.set(vals, i * k);
    return new THREE.Float32BufferAttribute(a, k);
  };
  g.setAttribute("color", remplir(3, [col.r, col.g, col.b]));
  g.setAttribute("aOs", remplir(4, os));
  g.setAttribute("aPivot", remplir(3, [pivot.x, pivot.y, pivot.z]));
  g.setAttribute("aGenou", remplir(4, genou));
  // Le corps est indexé : on rend celle-ci indexée aussi (un index trivial) pour pouvoir fusionner.
  const idx: number[] = [];
  for (let i = 0; i < n; i++) idx.push(i);
  g.setIndex(idx);
  return g;
}

/** Une patte : tube le long de hanche → genou → cheville → pied, avec son os et son genou. */
function patte(pa: { x: number; y: number; z: number; r: number; avant: boolean; decal: number }, d: Detail, bipede: boolean,
  dos: THREE.Color, ventre: THREE.Color, blanches: boolean): THREE.BufferGeometry {
  const h = pa.y;
  const s = Math.sign(pa.x) || 1;
  const pts = pa.avant
    ? [new THREE.Vector3(pa.x, h, pa.z), new THREE.Vector3(pa.x * 1.05, h * 0.55, pa.z - 0.02 * h / 0.2), new THREE.Vector3(pa.x * 1.05, h * 0.12, pa.z + 0.005),
      new THREE.Vector3(pa.x * 1.05, 0.004, pa.z + 0.025 * h / 0.2)]
    : [new THREE.Vector3(pa.x, h, pa.z), new THREE.Vector3(pa.x * 1.1, h * 0.55, pa.z + 0.07 * h / 0.25), new THREE.Vector3(pa.x * 1.1, h * (bipede ? 0.22 : 0.15), pa.z - 0.05 * h / 0.25),
      new THREE.Vector3(pa.x * 1.1, 0.004, pa.z + 0.03 * h / 0.25)];
  const rayons = [pa.r * 1.5, pa.r, pa.r * 0.65, pa.r * 0.5];
  const m = new Morceau();
  const cote = d.cotesPatte;
  const genou = pts[1];
  const c = new THREE.Color();
  const pivot = pts[0];
  const parSegment = Math.max(2, Math.round(d.anneaux / 10));
  const anneaux: { p: THREE.Vector3; r: number; t: number }[] = [];
  for (let k = 0; k < 3; k++) {
    for (let j = 0; j < parSegment; j++) {
      const u = j / parSegment;
      anneaux.push({ p: pts[k].clone().lerp(pts[k + 1], u), r: rayons[k] + (rayons[k + 1] - rayons[k]) * u, t: (k + u) / 3 });
    }
  }
  anneaux.push({ p: pts[3].clone(), r: rayons[3], t: 1 });
  const v = new THREE.Vector3();
  anneaux.forEach((a, i) => {
    const wGenou = a.t <= 1 / 3 ? 0 : Math.min(1, (a.t - 1 / 3) * 12);
    for (let k = 0; k < cote; k++) {
      const ang = (k / cote) * Math.PI * 2;
      v.set(a.p.x + Math.cos(ang) * a.r, a.p.y, a.p.z + Math.sin(ang) * a.r * 1.15);
      c.copy(dos).lerp(ventre, 0.25 * a.t);
      if (blanches && a.t > 0.6) c.setRGB(0.82, 0.8, 0.74);
      m.sommet(v, c, [3, a.t, pa.decal, pa.avant ? 1 : 0], pivot, [genou.x, genou.y, genou.z, wGenou]);
    }
    if (i + 1 < anneaux.length) {
      for (let k = 0; k < cote; k++) {
        const i0 = i * cote + k;
        const i1 = i * cote + ((k + 1) % cote);
        m.idx.push(i0, i1, i0 + cote, i1, i1 + cote, i0 + cote);
      }
    }
  });
  void s;
  // Pied : une sole aplatie, allongée vers l'avant (les doigts), qui suit le bas de la patte.
  const pied = pts[3];
  const lp = rayons[0] * (bipede ? 2.6 : 1.5);
  const sole = attacher(new THREE.SphereGeometry(1, Math.max(4, cote), 3).scale(rayons[2] * 1.5, rayons[3] * 0.9, lp).translate(pied.x, rayons[3] * 0.7, pied.z + lp * 0.45),
    c, [3, 1, pa.decal, pa.avant ? 1 : 0], pivot, [genou.x, genou.y, genou.z, 1]);
  const g = mergeGeometries([m.geometrie(), sole])!;
  return g;
}

/** Un bras de bipède : deux segments pendants, emplumés chez les formes à plumes. Il suit le corps. */
function bras(b: { x: number; y: number; z: number; l: number; r: number }, sgn: number, d: Detail, dos: THREE.Color, plumes: boolean): THREE.BufferGeometry {
  const coude = new THREE.Vector3(sgn * b.x * 1.2, b.y - b.l * 0.55, b.z - b.l * 0.15);
  const main = new THREE.Vector3(sgn * b.x * 1.1, b.y - b.l * 0.75, b.z + b.l * 0.35);
  const morceaux: THREE.BufferGeometry[] = [];
  const seg = (a: THREE.Vector3, c: THREE.Vector3, r: number) => {
    const l = a.distanceTo(c);
    const g = new THREE.CylinderGeometry(r * 0.7, r, l, Math.max(3, d.cotesPatte - 2));
    const dir = c.clone().sub(a).normalize();
    g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir));
    g.translate((a.x + c.x) / 2, (a.y + c.y) / 2, (a.z + c.z) / 2);
    return g;
  };
  morceaux.push(seg(new THREE.Vector3(sgn * b.x, b.y, b.z), coude, b.r), seg(coude, main, b.r * 0.8));
  if (plumes && d.accessoires) morceaux.push(new THREE.SphereGeometry(1, 6, 3).scale(0.006, b.l * 0.35, b.l * 0.45).translate(sgn * b.x * 1.25, b.y - b.l * 0.5, b.z - b.l * 0.1));
  const g = mergeGeometries(morceaux.map((x) => colorer(x, dos.clone().multiplyScalar(0.9), [0, 0, 0, 0], ZERO)))!;
  return g;
}

/**
 * Matériau des bêtes : la carte graphique plie le corps. Par instance : iA = position (x, y, z, par rapport à l'origine du
 * maillage) et cap ; iB = échelle (longueur, m), phase de la foulée, allure (0 arrêt, 1 pas, 2 course), tête basse (0 à 1) ;
 * iC = regard (rad), couché (0 à 1), éclat de la robe, hauteur de la hanche (longueur 1).
 */
export function materiauBetes(): THREE.MeshStandardNodeMaterial {
  const mat = new THREE.MeshStandardNodeMaterial({ roughness: 0.8, metalness: 0 });
  const iA = attribute("iA", "vec4");
  const iB = attribute("iB", "vec4");
  const iC = attribute("iC", "vec4");
  const os = attribute("aOs", "vec4");
  const pivot = attribute("aPivot", "vec3");
  const genou = attribute("aGenou", "vec4");
  const genre = os.x;
  const t = os.y;
  const masque = (g: number) => clamp(float(1).sub(abs(genre.sub(g)).mul(4)), 0, 1);
  const mCou = masque(1);
  const mQueue = masque(2);
  const mPatte = masque(3);
  const phase = iB.y;
  const allure = iB.z;
  const tete = iB.w;
  const regard = iC.x;
  const couche = iC.y;
  const ampl = min(allure, 1).mul(0.36).add(max(allure.sub(1), 0).mul(0.3));
  const ph = phase.add(os.z);
  const avant = os.w;
  const debout = float(1).sub(couche);
  // Hanche : la patte va et vient ; couché, elle se replie sous le corps.
  const theta1 = sin(ph).mul(ampl).mul(debout).add(couche.mul(mix(float(-0.7), float(0.35), avant)));
  // Genou : il plie pendant que la patte revient (pied levé).
  const theta2 = max(cos(ph), 0).mul(ampl).mul(1.3).mul(debout).add(couche.mul(mix(float(2.1), float(1.6), avant)));
  // Cou : tête basse pour brouter, regard de côté, petit hochement en marchant.
  const wCou = smoothstep(0, 0.6, t);
  const aCou = tete.mul(0.95).add(sin(phase.mul(2)).mul(0.04).mul(min(allure, 1))).add(couche.mul(0.35)).mul(wCou);
  const yCou = regard.mul(wCou);
  // Queue : balancement, plus ample à la course.
  const aQueue = sin(phase.mul(0.5).add(t.mul(2.5))).mul(0.14).mul(t).mul(allure.add(0.35)).add(sin(phase.mul(0.13).add(iC.z.mul(40))).mul(0.06).mul(t));
  const rotX = (v: THREE.Node<"vec3">, a: THREE.Node<"float">) => vec3(v.x, v.y.mul(cos(a)).sub(v.z.mul(sin(a))), v.y.mul(sin(a)).add(v.z.mul(cos(a))));
  const rotY = (v: THREE.Node<"vec3">, a: THREE.Node<"float">) => vec3(v.x.mul(cos(a)).add(v.z.mul(sin(a))), v.y, v.z.mul(cos(a)).sub(v.x.mul(sin(a))));
  const plier = (v0: THREE.Node<"vec3">, point: boolean) => {
    const g = genou.xyz;
    const pv = point ? pivot : vec3(0, 0, 0);
    const gv = point ? g : vec3(0, 0, 0);
    // Patte : genou puis hanche.
    const p1 = mix(v0, rotX(v0.sub(gv), theta2).add(gv), genou.w.mul(mPatte));
    const p2 = mix(p1, rotX(p1.sub(pv), theta1).add(pv), mPatte);
    // Cou et tête.
    const p3 = mix(p2, rotY(rotX(p2.sub(pv), aCou), yCou).add(pv), mCou);
    // Queue.
    return mix(p3, rotY(p3.sub(pv), aQueue).add(pv), mQueue);
  };
  const local = plier(attribute("position", "vec3"), true);
  // Le corps monte et descend à chaque pas ; couché, il s'abaisse sur ses pattes repliées.
  const souleve = abs(sin(phase)).mul(0.012).mul(min(allure, 2)).mul(debout).sub(couche.mul(iC.w).mul(0.72));
  const echelle = iB.x;
  const monde = rotY(vec3(local.x, local.y.add(souleve), local.z).mul(echelle), iA.w).add(iA.xyz);
  mat.positionNode = monde;
  const n = rotY(plier(attribute("normal", "vec3"), false), iA.w);
  mat.normalNode = transformNormalToView(normalize(n));
  mat.colorNode = attribute("color", "vec3").mul(iC.z);
  return mat;
}
