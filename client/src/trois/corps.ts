import * as THREE from "three/webgpu";
import { hacher, lisser, unite } from "./bruit";
import type { HumainScene } from "./foule";

/**
 * Corps détaillés de la vue 3D (0.41.1, proposition C2 précisée par zem) : chaque humain proche a son propre corps, construit
 * par le code à partir de ce que dit la simulation (âge, sexe, taille, réserves, endurance, enfants mis au monde, allaitement,
 * grossesse, vêtement), avec un squelette animé selon ce qu'il fait.
 *
 * - Proportions selon l'âge : grosse tête et jambes courtes chez le petit enfant, stature adulte vers 17 ans.
 * - Différences de sexe à partir de la puberté seulement (10 à 16 ans) : épaules, hanches, taille, poitrine.
 * - Poitrine de la femme : naît à la puberté, plus pleine pendant la grossesse et l'allaitement, plus basse après des enfants
 *   et avec l'âge, moins volumineuse dans la vieillesse ; aucune chez l'enfant.
 * - Musculature (pectoraux, épaules, bras, cuisses) : sexe, âge, endurance, réserves, chasse et travail des champs.
 * - Nudité tant que la simulation ne donne pas de vêtement : anatomie sobre des adultes seulement (pilosité pubienne, organes
 *   de l'homme), aucun détail chez l'enfant. Un vêtement (peau, fibres, feuilles) est porté et montre son usure.
 *
 * Tout est calculé à l'affichage : la simulation ne change pas.
 */

/** Ce qui décide de la forme d'un corps ; deux corps de même forme ont la même géométrie. */
export interface Forme {
  age: number;
  femme: boolean;
  /** Puberté accomplie, de 0 (enfant) à 1. */
  puberte: number;
  musculature: number;
  graisse: number;
  maigreur: number;
  /** Volume de la poitrine (0 : aucune ; 1 : moyenne d'une jeune femme). */
  poitrine: number;
  /** Poitrine plus basse : enfants nourris, âge (0 à 1). */
  ptose: number;
  grossesse: number;
  /** Matière du vêtement (0 : nu) et son état (1 neuf). */
  vetement: number;
  usure: number;
  peau: THREE.Color;
  cheveux: THREE.Color;
  cheveuxLongs: boolean;
  /** Courbure du dos des vieux, de 0 à 1. */
  voute: number;
}

const PEAU_MATIERE = 24;
const FIBRES = 8;
const FEUILLES = 7;
const ECORCE = 34;

/** Forme d'un corps à partir du flux Scène. */
export function formeDe(s: HumainScene, peau: THREE.Color, cheveux: THREE.Color): Forme {
  const age = s.age;
  const puberte = s.femme ? lisser(9.5, 15, age) : lisser(11, 16.5, age);
  const femme = s.femme;
  // Musculature : forte chez le jeune homme, moindre chez la femme ; elle vient de l'âge, de la constitution (endurance), de la
  // nourriture (réserves) et de la vie menée (chasse, champs). Elle décline après 45 ans.
  const ageMuscle = lisser(10, 22, age) * (1 - 0.5 * lisser(45, 80, age));
  const vie = 1 + (s.etat & 128 ? 0.3 : 0) + (s.etat & 256 ? 0.2 : 0);
  const nourri = 0.55 + 0.45 * lisser(5, 35, s.reserves);
  const musculature = Math.min(1.2, (femme ? 0.42 : 1) * ageMuscle * (0.5 + 0.6 * s.endurance) * vie * nourri);
  const graisse = lisser(55, 100, s.reserves) * (0.6 + 0.4 * unite(hacher(s.id, 61)));
  const maigreur = 1 - lisser(4, 30, s.reserves);
  // Poitrine : volume propre à chacune, poussée de la puberté, grossesse et allaitement, graisse ; ptose avec les enfants et l'âge.
  let poitrine = 0;
  let ptose = 0;
  if (femme && age >= 9) {
    const propre = 0.65 + 0.7 * unite(hacher(s.id, 63));
    const nourrice = s.etat & 64 ? 0.45 : 0;
    const enceinte = s.etat & 2 ? 0.3 : 0;
    const vieillesse = 1 - 0.3 * lisser(50, 80, age);
    poitrine = lisser(10, 17.5, age) * propre * (1 + nourrice + enceinte + 0.5 * graisse - 0.35 * maigreur) * vieillesse;
    ptose = Math.min(1, 0.12 * Math.min(s.enfants, 5) + 0.6 * lisser(32, 70, age) + 0.25 * poitrine * lisser(18, 40, age)) * puberte;
  }
  const vetement = s.vetement === PEAU_MATIERE || s.vetement === FIBRES || s.vetement === FEUILLES || s.vetement === ECORCE ? s.vetement : s.vetement > 0 ? FIBRES : 0;
  return {
    age, femme, puberte, musculature, graisse, maigreur, poitrine, ptose,
    grossesse: s.etat & 2 ? 1 : 0,
    vetement, usure: s.usure, peau, cheveux,
    cheveuxLongs: unite(hacher(s.id, 65)) < (femme ? 0.75 : 0.3),
    voute: lisser(58, 85, age),
  };
}

/** Clé de cache : la forme arrondie (un corps n'est refait que si elle change visiblement). */
export function cleDe(f: Forme): string {
  const r = (v: number, pas: number) => Math.round(v / pas);
  return [r(f.age < 18 ? f.age : 18 + (f.age - 18) / 4, 0.5), f.femme ? 1 : 0, r(f.musculature, 0.1), r(f.graisse, 0.1), r(f.maigreur, 0.1),
    r(f.poitrine, 0.08), r(f.ptose, 0.1), f.grossesse, f.vetement, r(f.usure, 0.25), r(f.voute, 0.2), f.peau.getHexString(), f.cheveux.getHexString()].join(",");
}

// Os du squelette.
const OS = ["bassin", "ventre", "poitrine", "cou", "tete", "brasG", "avantBrasG", "mainG", "brasD", "avantBrasD", "mainD",
  "cuisseG", "jambeG", "piedG", "cuisseD", "jambeD", "piedD"] as const;
type NomOs = (typeof OS)[number];
const O = Object.fromEntries(OS.map((n, i) => [n, i])) as Record<NomOs, number>;
const PARENT: Record<NomOs, NomOs | null> = {
  bassin: null, ventre: "bassin", poitrine: "ventre", cou: "poitrine", tete: "cou",
  brasG: "poitrine", avantBrasG: "brasG", mainG: "avantBrasG", brasD: "poitrine", avantBrasD: "brasD", mainD: "avantBrasD",
  cuisseG: "bassin", jambeG: "cuisseG", piedG: "jambeG", cuisseD: "bassin", jambeD: "cuisseD", piedD: "jambeD",
};

/** Proportions pour une taille de 1 (le maillage est ensuite mis à l'échelle de la stature). */
interface Proportions {
  hanche: number; genou: number; cheville: number; taille: number; seins: number; epaule: number; menton: number;
  hauteTete: number; xHanche: number; xEpaule: number; coude: number; poignet: number; longMain: number;
}

const entre = (a: number, b: number, t: number) => a + (b - a) * t;

/** Valeur selon l'âge, par paliers interpolés. */
function parAge(age: number, points: [number, number][]): number {
  if (age <= points[0][0]) return points[0][1];
  for (let k = 1; k < points.length; k++) {
    if (age <= points[k][0]) {
      const [a0, v0] = points[k - 1];
      const [a1, v1] = points[k];
      return v0 + ((age - a0) / (a1 - a0)) * (v1 - v0);
    }
  }
  return points[points.length - 1][1];
}

function proportions(f: Forme): Proportions {
  const hauteTete = parAge(f.age, [[0, 0.25], [2, 0.21], [6, 0.17], [12, 0.145], [17, 0.13]]);
  const hanche = parAge(f.age, [[0, 0.4], [2, 0.43], [6, 0.47], [12, 0.51], [17, 0.53]]);
  const menton = 1 - hauteTete;
  const epaule = menton - parAge(f.age, [[0, 0.015], [6, 0.025], [17, 0.035]]);
  const masc = f.femme ? 0 : f.puberte;
  const fem = f.femme ? f.puberte : 0;
  const xEpaule = entre(0.09, 0.1, masc) - 0.006 * fem + 0.008 * f.musculature * masc;
  const bras = parAge(f.age, [[0, 0.36], [6, 0.41], [17, 0.44]]);
  const coude = epaule - bras * 0.42;
  const poignet = epaule - bras * 0.77;
  return {
    hanche, genou: hanche * 0.53, cheville: hanche * 0.08, taille: hanche + (epaule - hanche) * 0.36, seins: hanche + (epaule - hanche) * 0.7,
    epaule, menton, hauteTete, xHanche: entre(0.048, 0.054, fem), xEpaule, coude, poignet, longMain: bras * 0.23,
  };
}

/** Accumulateur de géométrie skinnée. */
class Maillage {
  readonly positions: number[] = [];
  readonly couleurs: number[] = [];
  readonly os: number[] = [];
  readonly poids: number[] = [];
  readonly indices: number[] = [];
  readonly releves: number[] = [];

  sommet(p: THREE.Vector3, c: THREE.Color, liens: [number, number][]): void {
    this.positions.push(p.x, p.y, p.z);
    this.couleurs.push(c.r, c.g, c.b);
    const l = liens.filter(([, w]) => w > 0.001).sort((a, b) => b[1] - a[1]).slice(0, 4);
    const total = l.reduce((t, [, w]) => t + w, 0) || 1;
    for (let k = 0; k < 4; k++) {
      this.os.push(l[k]?.[0] ?? 0);
      this.poids.push(l[k] ? l[k][1] / total : 0);
    }
  }

  get nombre(): number {
    return this.positions.length / 3;
  }

  geometrie(): THREE.BufferGeometry {
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(this.positions, 3));
    g.setAttribute("color", new THREE.Float32BufferAttribute(this.couleurs, 3));
    g.setAttribute("skinIndex", new THREE.Uint16BufferAttribute(this.os, 4));
    g.setAttribute("skinWeight", new THREE.Float32BufferAttribute(this.poids, 4));
    g.setIndex(this.indices);
    g.computeVertexNormals();
    const n = g.getAttribute("normal");
    const v = new THREE.Vector3();
    this.releves.forEach((k, i) => {
      if (k <= 0) return;
      v.fromBufferAttribute(n, i);
      v.y = v.y + k * (Math.max(0, -v.y) * 1.6 + 0.2);
      v.normalize();
      n.setXYZ(i, v.x, v.y, v.z);
    });
    return g;
  }
}

interface Tube {
  /** Point de départ et direction de l'axe. */
  depart: THREE.Vector3;
  axe: THREE.Vector3;
  longueur: number;
  anneaux: number;
  cotes: number;
  /** Rayons (latéral, avant-arrière) à la part t de l'axe. */
  rayon: (t: number) => [number, number];
  /** Déformation d'un point (en coordonnées du corps), avec l'angle φ autour de l'axe (0 : vers l'avant). */
  deformer?: (p: THREE.Vector3, t: number, phi: number) => void;
  couleur: (p: THREE.Vector3, t: number, phi: number) => THREE.Color;
  liens: (p: THREE.Vector3, t: number) => [number, number][];
  /** Sens de l'avant (pour φ = 0) ; par défaut +z. */
  avant?: THREE.Vector3;
  /** Normales relevées vers le haut (0 à 1) : le dessous de l'entrejambe ne paraît pas noir à l'ombre. */
  relever?: (p: THREE.Vector3) => number;
}

/** Ajoute un tube d'anneaux elliptiques ; les extrémités de rayon nul le ferment. Les faces sont tournées vers l'extérieur. */
function tube(m: Maillage, d: Tube): void {
  const debut = m.nombre;
  const axe = d.axe.clone().normalize();
  const avant0 = (d.avant ?? new THREE.Vector3(0, 0, 1)).clone();
  const v = avant0.sub(axe.clone().multiplyScalar(avant0.dot(axe))).normalize();
  const u = new THREE.Vector3().crossVectors(v, axe).normalize();
  const p = new THREE.Vector3();
  const centre = new THREE.Vector3();
  for (let i = 0; i <= d.anneaux; i++) {
    const t = i / d.anneaux;
    centre.copy(d.depart).addScaledVector(axe, t * d.longueur);
    const [ru, rv] = d.rayon(t);
    for (let j = 0; j < d.cotes; j++) {
      const phi = (j / d.cotes) * Math.PI * 2;
      p.copy(centre).addScaledVector(u, Math.sin(phi) * ru).addScaledVector(v, Math.cos(phi) * rv);
      d.deformer?.(p, t, phi);
      m.sommet(p, d.couleur(p, t, phi), d.liens(p, t));
      m.releves.push(d.relever?.(p) ?? 0);
    }
  }
  // Sens des faces : vérifié sur le premier quadrilatère non dégénéré, comparé à la direction qui sort de l'axe.
  const P = m.positions;
  const lire = (k: number) => new THREE.Vector3(P[3 * k], P[3 * k + 1], P[3 * k + 2]);
  // Vote sur tous les quadrilatères : quelques points rentrés (visage sous les cheveux) ne trompent pas le résultat.
  let vote = 0;
  for (let i = 0; i < d.anneaux; i++) {
    const milieu = d.depart.clone().addScaledVector(axe, ((i + 0.5) / d.anneaux) * d.longueur);
    for (let j = 0; j < d.cotes; j++) {
      const a = lire(debut + i * d.cotes + j);
      const b = lire(debut + (i + 1) * d.cotes + j);
      const c = lire(debut + i * d.cotes + ((j + 1) % d.cotes));
      const n = new THREE.Vector3().crossVectors(b.clone().sub(a), c.clone().sub(a));
      if (n.lengthSq() < 1e-14) continue;
      vote += Math.sign(n.dot(a.clone().sub(milieu)));
    }
  }
  const inverse = vote < 0;
  for (let i = 0; i < d.anneaux; i++) {
    for (let j = 0; j < d.cotes; j++) {
      const a = debut + i * d.cotes + j;
      const b = debut + (i + 1) * d.cotes + j;
      const c = debut + i * d.cotes + ((j + 1) % d.cotes);
      const e = debut + (i + 1) * d.cotes + ((j + 1) % d.cotes);
      if (inverse) m.indices.push(a, c, b, c, e, b);
      else m.indices.push(a, b, c, c, b, e);
    }
  }
}

/** Bosse lisse en 2D : 1 au centre, 0 au-delà de son rayon (forme arrondie, plus bombée qu'une gaussienne). */
function bosse(dx: number, dy: number): number {
  const r2 = dx * dx + dy * dy;
  if (r2 >= 1) return 0;
  const k = 1 - r2;
  return k * Math.sqrt(k);
}

/** Lien progressif entre deux os autour d'une limite (y décroissant ou croissant selon l'axe). */
function fondu(valeur: number, limite: number, largeur: number, avant: number, apres: number): [number, number][] {
  const w = Math.min(1, Math.max(0, (valeur - (limite - largeur)) / (2 * largeur)));
  return [[avant, 1 - w], [apres, w]];
}

/** Construit le maillage d'un corps, les positions de repos de ses os, et ce qu'il faut pour le poser. */
export function construire(f: Forme): { geometrie: THREE.BufferGeometry; repos: THREE.Vector3[]; p: Proportions } {
  const p = proportions(f);
  const m = new Maillage();
  const masc = f.femme ? 0 : f.puberte;
  const fem = f.femme ? f.puberte : 0;
  const adulte = lisser(15, 18, f.age);
  const enfant = 1 - lisser(2, 12, f.age);
  const plein = 1 + 0.35 * f.graisse - 0.22 * f.maigreur + 0.06 * enfant;
  const muscle = f.musculature;
  const peau = f.peau;
  const sombre = peau.clone().multiplyScalar(0.72);
  const aureole = peau.clone().lerp(new THREE.Color(0.35, 0.17, 0.13), 0.35);
  const poils = f.cheveux.clone().multiplyScalar(0.85);

  // Torse : du périnée au haut des épaules.
  const bas = p.hanche - 0.045;
  const haut = p.epaule + 0.05;
  const rCou = (0.026 + 0.006 * masc * f.musculature + 0.006 * enfant) * (1 + 0.15 * f.graisse);
  const torse = (y: number): [number, number] => {
    // Largeur et profondeur à la hauteur y, entre des repères (bassin, taille, poitrine, épaules).
    const rxHanche = (0.09 + 0.016 * fem) * plein;
    const rxTaille = (0.07 - 0.008 * fem + 0.004 * masc * muscle) * (1 + 0.45 * f.graisse - 0.12 * f.maigreur);
    const rxPoitrine = (0.085 + 0.012 * masc * (0.5 + muscle)) * (1 + 0.15 * f.graisse - 0.08 * f.maigreur);
    const rxEpaule = p.xEpaule + 0.012;
    const rz = (0.055 + 0.004 * masc * muscle) * (1 + 0.3 * f.graisse - 0.12 * f.maigreur) + 0.01 * enfant;
    let rx: number;
    if (y < p.hanche) rx = rxHanche * Math.sqrt(Math.max(0, Math.min(1, (y - bas) / (p.hanche - bas)))) * 0.95 + 0.0001;
    else if (y < p.taille) rx = entre(rxHanche, rxTaille, lisser(p.hanche, p.taille, y));
    else if (y < p.seins) rx = entre(rxTaille, rxPoitrine, lisser(p.taille, p.seins, y));
    else if (y < p.epaule) rx = entre(rxPoitrine, rxEpaule, lisser(p.seins, p.epaule, y));
    // Pente des trapèzes : de l'épaule au cou.
    else rx = entre(rxEpaule, rCou * 1.05, Math.sqrt(lisser(p.epaule - 0.01, haut, y)));
    let rzy = rz * (y < p.hanche ? Math.sqrt(Math.max(0, Math.min(1, (y - bas) / (p.hanche - bas)))) : 1);
    if (y > p.epaule) rzy = entre(rzy, rCou, lisser(p.epaule, haut, y));
    return [rx, rzy];
  };
  // Poitrine : deux bosses à l'avant ; la ptose les descend et allonge le haut.
  const xSein = 0.047 + 0.005 * f.poitrine;
  const ySein = p.seins - 0.01 - 0.035 * f.ptose * Math.min(1, f.poitrine + 0.3);
  const rSein = 0.034 + 0.016 * Math.sqrt(f.poitrine);
  const projSein = 0.042 * f.poitrine;
  const deformerTorse = (q: THREE.Vector3, _t: number, phi: number) => {
    const avant = Math.cos(phi);
    if (avant > 0) {
      let dz = 0;
      for (const cote of [-1, 1]) {
        if (projSein > 0.001) {
          const dx = (q.x - cote * xSein) / rSein;
          const dyb = q.y - ySein;
          // Le haut du sein descend en pente douce (plus douce avec la ptose), le bas est arrondi.
          const dy = dyb > 0 ? dyb / (rSein * (1.1 + 1.3 * f.ptose)) : dyb / (rSein * 0.9);
          dz += projSein * bosse(dx, dy);
        }
        // Pectoraux : larges et plats, bord inférieur net.
        const pec = 0.016 * masc * muscle + 0.004 * fem * muscle;
        if (pec > 0) {
          const dy = (q.y - (p.seins + 0.012)) / (q.y > p.seins + 0.012 ? 0.05 : 0.028);
          dz += pec * bosse((q.x - cote * 0.048) / 0.062, dy);
        }
      }
      // Ventre : graisse, et grossesse (plus bas, plus rond).
      dz += (0.035 * f.graisse + 0.004 * enfant) * bosse(q.x / 0.09, (q.y - (p.taille - 0.02)) / 0.08);
      dz += 0.075 * f.grossesse * bosse(q.x / 0.085, (q.y - (p.hanche + 0.06)) / 0.085);
      // Nombril (un petit creux) et clavicules (plus visibles chez les maigres).
      dz -= 0.005 * (1 - enfant * 0.5) * bosse(q.x / 0.008, (q.y - (p.taille - 0.012)) / 0.008);
      const ax = Math.abs(q.x);
      if (ax > 0.012 && ax < p.xEpaule * 0.9) {
        const yClav = p.epaule - 0.014 + 0.008 * (ax / p.xEpaule);
        dz += (0.003 + 0.004 * f.maigreur) * bosse(0, (q.y - yClav) / 0.009) * lisser(0.012, 0.03, ax);
      }
      // Mont du pubis, chez l'adulte.
      dz += 0.006 * adulte * bosse(q.x / 0.03, (q.y - (p.hanche - 0.012)) / 0.02);
      q.z += dz * Math.sqrt(avant);
    } else {
      // Fesses ; omoplates.
      let dz = 0;
      for (const cote of [-1, 1]) {
        dz += (0.022 + 0.014 * fem + 0.02 * f.graisse) * bosse((q.x - cote * 0.042) / 0.05, (q.y - (p.hanche - 0.005)) / 0.055);
        dz += 0.006 * (0.4 + muscle) * bosse((q.x - cote * 0.05) / 0.04, (q.y - (p.epaule - 0.05)) / 0.05);
      }
      q.z -= dz * Math.sqrt(-avant);
    }
    // Côtes saillantes de la faim : légère ondulation sur les flancs.
    if (f.maigreur > 0.2 && q.y > p.taille && q.y < p.seins + 0.02) q.x *= 1 + 0.012 * f.maigreur * Math.sin((q.y - p.taille) * 260);
  };
  const couleurTorse = (q: THREE.Vector3, _t: number, phi: number): THREE.Color => {
    const avant = Math.cos(phi);
    if (avant > 0.3) {
      // Aréoles : chez la femme pubère et l'homme adulte ; rien de marqué chez l'enfant.
      const montre = f.femme ? f.puberte : adulte * 0.6;
      if (montre > 0.05) {
        for (const cote of [-1, 1]) {
          const cx = f.femme && f.poitrine > 0.05 ? cote * xSein : cote * 0.05;
          const cy = f.femme && f.poitrine > 0.05 ? ySein - 0.006 * f.ptose : p.seins + 0.004;
          const r = f.femme ? 0.011 + 0.006 * f.poitrine : 0.007;
          const d = Math.hypot(q.x - cx, q.y - cy) / r;
          if (d < 1) return peau.clone().lerp(aureole, montre * (1 - d * d * 0.6));
        }
      }
      // Nombril.
      if (Math.hypot(q.x / 0.007, (q.y - (p.taille - 0.012)) / 0.006) < 1) return peau.clone().multiplyScalar(0.7);
      // Pilosité pubienne de l'adulte (triangle), absente chez l'enfant.
      if (adulte > 0.05 && f.vetement === 0) {
        const h = q.y - (bas + 0.004);
        const hauteur = f.femme ? 0.042 : 0.05;
        if (h > 0 && h < hauteur) {
          const demi = 0.01 + (h / hauteur) * (f.femme ? 0.034 : 0.04);
          if (Math.abs(q.x) < demi) return peau.clone().lerp(poils, adulte * 0.85);
        }
      }
    }
    return peau;
  };
  const liensTorse = (q: THREE.Vector3): [number, number][] => {
    if (q.y < p.taille) return fondu(q.y, p.hanche + 0.03, 0.03, O.bassin, O.ventre);
    if (q.y < p.seins) return fondu(q.y, (p.taille + p.seins) / 2, 0.035, O.ventre, O.poitrine);
    // Haut des épaules : suit un peu les bras.
    const cote = q.x > 0 ? O.brasG : O.brasD;
    const w = 0.35 * lisser(p.xEpaule - 0.035, p.xEpaule + 0.01, Math.abs(q.x)) * lisser(p.epaule - 0.04, p.epaule, q.y);
    return [[O.poitrine, 1 - w], [cote, w]];
  };
  tube(m, {
    depart: new THREE.Vector3(0, bas, 0), axe: new THREE.Vector3(0, 1, 0), longueur: haut - bas, anneaux: 44, cotes: 40,
    rayon: (t) => torse(bas + t * (haut - bas)), deformer: deformerTorse, couleur: couleurTorse, liens: liensTorse,
    relever: (q) => Math.max(1 - lisser(bas, p.hanche + 0.01, q.y), q.z > 0.04 && q.y < p.taille ? 0.6 * (1 - lisser(p.hanche, p.taille, q.y)) : 0),
  });

  // Cou et tête.
  tube(m, {
    depart: new THREE.Vector3(0, p.epaule - 0.04, -0.008), axe: new THREE.Vector3(0, 1, 0.12), longueur: p.menton - p.epaule + 0.07,
    anneaux: 6, cotes: 16, rayon: () => [rCou, rCou * 0.95], couleur: () => peau,
    liens: (q) => fondu(q.y, p.menton - 0.005, 0.015, O.cou, O.tete),
  });
  // Bras (G = côté +x, à la gauche de l'humain) : un seul tube de l'épaule au poignet (pas de couture au coude), puis la main
  // avec ses doigts. La paume est tournée vers la cuisse.
  const rBras = (0.025 + 0.012 * muscle + 0.004 * masc) * (1 + 0.25 * f.graisse - 0.2 * f.maigreur) + 0.006 * enfant;
  const rAvant = (0.021 + 0.006 * muscle + 0.003 * masc) * (1 + 0.15 * f.graisse - 0.15 * f.maigreur) + 0.005 * enfant;
  const ouvert = 0.09;
  const paume = peau.clone().lerp(new THREE.Color(0.86, 0.68, 0.6), 0.35);
  for (const cote of [1, -1]) {
    const [b, ab, mn] = cote > 0 ? [O.brasG, O.avantBrasG, O.mainG] : [O.brasD, O.avantBrasD, O.mainD];
    const epaule = new THREE.Vector3(cote * p.xEpaule, p.epaule - 0.012, -0.004);
    const dir = new THREE.Vector3(cote * Math.sin(ouvert), -Math.cos(ouvert), 0);
    const lBras = p.epaule - p.coude;
    const lAvant = p.coude - p.poignet;
    const lTout = lBras + lAvant + 0.02;
    const rayonBras = (t: number) => {
      const deltoide = 1 + (0.12 + 0.25 * muscle) * bosse(0, (t - 0.15) / 0.25);
      const biceps = 1 + 0.18 * muscle * bosse(0, (t - 0.55) / 0.35);
      const rond = t < 0.14 ? Math.sqrt(0.25 + 0.75 * (t / 0.14)) : 1;
      return [rBras * deltoide * rond * (0.9 + 0.1 * (1 - t)), rBras * biceps * rond];
    };
    const rayonAvant = (t: number) => {
      const r = rAvant * (1 + 0.25 * bosse(0, (t - 0.25) / 0.4) * (0.6 + muscle)) * entre(1, 0.72, t);
      return [r, r * 0.85];
    };
    tube(m, {
      depart: epaule.clone().addScaledVector(dir, -0.015), axe: dir, longueur: lTout, anneaux: 22, cotes: 14,
      rayon: (t) => {
        const d = t * lTout - 0.015;
        const [a1, a2] = rayonBras(Math.min(1, Math.max(0, (d + 0.015) / (lBras + 0.03))));
        const [c1, c2] = rayonAvant(Math.min(1, Math.max(0, (d - lBras + 0.012) / (lAvant + 0.02))));
        const k = lisser(lBras - 0.02, lBras + 0.02, d);
        // Le poignet s'aplatit vers la main.
        const poignet = lisser(lBras + lAvant - 0.03, lBras + lAvant + 0.005, d);
        return [entre(a1, c1, k) * (1 - 0.1 * poignet), entre(a2, c2, k) * (1 - 0.15 * poignet)];
      },
      deformer: (q, t) => {
        // Pointe du coude, à l'arrière.
        const d = t * lTout - 0.015;
        if (q.z < -0.004) q.z -= 0.006 * bosse(0, (d - lBras) / 0.025);
      },
      couleur: () => peau,
      liens: (_q, t) => {
        const d = t * lTout - 0.015;
        if (d < 0.02) return [[O.poitrine, Math.max(0, 0.4 - 10 * (d + 0.015))], [b, 1 - Math.max(0, 0.4 - 10 * (d + 0.015))]];
        if (d > lBras + lAvant - 0.01) return fondu(d, lBras + lAvant, 0.01, ab, mn);
        return fondu(d, lBras, 0.022, b, ab);
      },
    });
    // Paume : large d'avant en arrière, mince de dedans en dehors.
    const poignet = epaule.clone().addScaledVector(dir, lBras + lAvant);
    const lPaume = p.longMain * 0.5;
    const largeur = rAvant * 1.05;
    const epais = rAvant * 0.45;
    const cotePaume = (q: THREE.Vector3, centre: THREE.Vector3) => ((q.x - centre.x) * cote < 0 ? paume : peau);
    tube(m, {
      depart: poignet.clone().addScaledVector(dir, -0.006), axe: dir, longueur: lPaume + 0.008, anneaux: 7, cotes: 14,
      avant: new THREE.Vector3(0, 0, 1),
      rayon: (t) => {
        const fin = t > 0.85 ? 0.75 + 0.25 * Math.sqrt(Math.max(0, 1 - ((t - 0.85) / 0.15) ** 2)) : 1;
        return [epais * (t < 0.2 ? 1.15 : 1) * fin, largeur * entre(0.8, 1, Math.min(1, t * 2.5)) * fin];
      },
      couleur: (q) => cotePaume(q, poignet),
      liens: (_q, t) => (t < 0.1 ? [[ab, 0.3], [mn, 0.7]] : [[mn, 1]]),
    });
    // Doigts : quatre, un peu repliés vers la paume, et le pouce devant.
    const rDoigt = 0.0047 * (rAvant / 0.023);
    const bout = poignet.clone().addScaledVector(dir, lPaume - 0.004);
    const longueurs = [0.42, 0.47, 0.44, 0.35];
    [0.72, 0.24, -0.24, -0.7].forEach((zr, k) => {
      const l = p.longMain * longueurs[k];
      const depart = bout.clone().add(new THREE.Vector3(0, 0, zr * largeur * 0.95));
      tube(m, {
        depart, axe: dir, longueur: l, anneaux: 6, cotes: 7,
        rayon: (t) => {
          const r = rDoigt * (k === 3 ? 0.85 : 1) * entre(1, 0.85, t) * Math.sqrt(Math.max(0, 1 - Math.max(0, (t - 0.8) / 0.2) ** 2));
          return [r + 0.0001, r * 0.9 + 0.0001];
        },
        deformer: (q, t) => {
          q.x -= cote * 0.45 * l * t * t;
          q.y += 0.12 * l * t * t;
        },
        couleur: (q) => cotePaume(q, depart),
        liens: () => [[mn, 1]],
      });
    });
    {
      const l = p.longMain * 0.36;
      const depart = poignet.clone().addScaledVector(dir, lPaume * 0.18).add(new THREE.Vector3(-cote * epais * 0.3, 0, largeur * 0.8));
      const axe = dir.clone().multiplyScalar(0.62).add(new THREE.Vector3(-cote * 0.3, 0, 0.6)).normalize();
      tube(m, {
        depart, axe, longueur: l, anneaux: 6, cotes: 8,
        rayon: (t) => {
          const r = rDoigt * 1.25 * entre(1.3, 0.85, t) * Math.sqrt(Math.max(0, 1 - Math.max(0, (t - 0.8) / 0.2) ** 2));
          return [r + 0.0001, r + 0.0001];
        },
        couleur: (q) => cotePaume(q, depart),
        liens: () => [[mn, 1]],
      });
    }
  }

  // Jambes : un seul tube de la hanche à la cheville (pas de couture au genou), rotule devant, mollet derrière.
  const rCuisse = (0.05 + 0.008 * fem + 0.012 * muscle) * (1 + 0.3 * f.graisse - 0.2 * f.maigreur) + 0.008 * enfant;
  const rMollet = (0.033 + 0.008 * muscle) * (1 + 0.15 * f.graisse - 0.15 * f.maigreur) + 0.004 * enfant;
  for (const cote of [1, -1]) {
    const [c, j, pd] = cote > 0 ? [O.cuisseG, O.jambeG, O.piedG] : [O.cuisseD, O.jambeD, O.piedD];
    const x = cote * p.xHanche;
    const haut = new THREE.Vector3(x - cote * 0.004, p.hanche + 0.02, 0);
    const basJ = new THREE.Vector3(x - cote * 0.006, p.cheville, 0);
    const axe = basJ.clone().sub(haut);
    const lTout = axe.length();
    const lCuisse = p.hanche + 0.02 - p.genou;
    const lMollet = p.genou - p.cheville;
    tube(m, {
      depart: haut, axe, longueur: lTout, anneaux: 30, cotes: 16,
      rayon: (t) => {
        const d = t * lTout;
        const tc = Math.min(1, d / lCuisse);
        const rc = rCuisse * entre(1, 0.6, tc ** 0.8) * (1 + 0.1 * muscle * bosse(0, (tc - 0.4) / 0.4)) * (tc < 0.2 ? 0.8 + tc : 1);
        const tm = Math.max(0, (d - lCuisse) / lMollet);
        const base = rMollet * entre(1, 0.55, tm) * (tm < 0.1 ? 0.85 + 1.5 * tm : 1);
        const k = lisser(lCuisse - 0.025, lCuisse + 0.03, d);
        return [entre(rc, base * 0.92 * (1 + 0.06 * muscle * bosse(0, (tm - 0.3) / 0.28)), k), entre(rc * 1.02, base, k)];
      },
      deformer: (q, t) => {
        const d = t * lTout;
        const tm = (d - lCuisse) / lMollet;
        // Le mollet ressort vers l'arrière ; la rotule devant le genou.
        if (q.z < 0 && tm > 0) q.z -= rMollet * (0.3 + 0.25 * muscle) * bosse(0, (tm - 0.3) / 0.3) * Math.min(1, -q.z / (rMollet * 0.5));
        if (q.z > 0) q.z += 0.007 * bosse(0, (d - lCuisse + 0.008) / 0.028) * Math.min(1, q.z / (rMollet * 0.6));
      },
      couleur: () => peau,
      liens: (_q, t) => {
        const d = t * lTout;
        if (d < lCuisse * 0.15) {
          const tc = d / lCuisse;
          return [[O.bassin, 0.5 - 3 * tc], [c, 0.5 + 3 * tc]];
        }
        if (d > lTout - 0.012) return fondu(d, lTout - 0.004, 0.008, j, pd);
        return fondu(d, lCuisse, 0.025, c, j);
      },
    });
    const pied = 0.15 * (0.85 + 0.15 * (1 - enfant));
    tube(m, {
      depart: new THREE.Vector3(x - cote * 0.006, p.cheville * 0.45, -0.035), axe: new THREE.Vector3(0, -0.12, 1), longueur: pied,
      anneaux: 11, cotes: 12, avant: new THREE.Vector3(0, 1, 0),
      rayon: (t) => {
        const s = Math.sqrt(Math.max(0, 1 - Math.max(0, (t - 0.7) / 0.3) ** 2)) * Math.sqrt(Math.min(1, t * 6 + 0.15));
        return [0.026 * (0.85 + 0.3 * t) * s + 0.0001, entre(0.03, 0.012, t) * s + 0.0001];
      },
      // Le gros orteil : le bout du pied avance un peu plus du côté intérieur.
      deformer: (q, t) => {
        if (t > 0.75 && (q.x - x) * cote < 0) q.z += 0.008 * (t - 0.75) / 0.25;
      },
      couleur: () => peau,
      liens: () => [[pd, 1]],
    });
  }

  // Organes de l'homme adulte nu : sobres, à leur taille réelle. Rien chez l'enfant.
  if (!f.femme && adulte > 0.5 && f.vetement === 0) {
    const yBase = bas + 0.012;
    const zBase = 0.03 + 0.012 * f.graisse;
    const rouge = peau.clone().lerp(sombre, 0.4);
    for (const cote of [-1, 1]) {
      tube(m, {
        depart: new THREE.Vector3(cote * 0.011, yBase - 0.004, zBase - 0.014), axe: new THREE.Vector3(0, -1, 0.15), longueur: 0.034,
        anneaux: 8, cotes: 10, rayon: (t) => {
          const s = Math.sin(Math.PI * t);
          return [0.014 * s + 0.0001, 0.015 * s + 0.0001];
        },
        couleur: () => rouge, liens: () => [[O.bassin, 1]],
      });
    }
    tube(m, {
      depart: new THREE.Vector3(0, yBase + 0.006, zBase - 0.004), axe: new THREE.Vector3(0, -1, 0.42), longueur: 0.05,
      anneaux: 9, cotes: 12, rayon: (t) => {
        const fin = t > 0.85 ? Math.sqrt(Math.max(0, 1 - ((t - 0.85) / 0.15) ** 2)) : 1;
        return [0.0105 * fin + 0.0001, 0.0105 * fin + 0.0001];
      },
      couleur: (_q, t) => (t > 0.8 ? peau.clone().lerp(aureole, 0.5) : peau), liens: () => [[O.bassin, 1]],
    });
  }

  // Vêtement : pagne ou jupe de la taille à mi-cuisse (raccourci et effrangé par l'usure).
  if (f.vetement > 0) {
    const couleur = f.vetement === PEAU_MATIERE ? new THREE.Color(0.42, 0.28, 0.17) : f.vetement === FEUILLES ? new THREE.Color(0.32, 0.42, 0.18)
      : f.vetement === ECORCE ? new THREE.Color(0.38, 0.3, 0.22) : new THREE.Color(0.62, 0.55, 0.36);
    couleur.multiplyScalar(0.75 + 0.25 * f.usure);
    const haut = p.taille - 0.02;
    const basJupe = entre(p.genou + 0.05, p.hanche - 0.11, 1 - Math.min(1, f.usure + 0.3));
    tube(m, {
      depart: new THREE.Vector3(0, haut, 0), axe: new THREE.Vector3(0, -1, 0), longueur: haut - basJupe, anneaux: 8, cotes: 28,
      rayon: (t) => {
        const y = haut - t * (haut - basJupe);
        const [rx, rz] = torse(Math.max(p.hanche, y));
        const evase = 1.12 + 0.35 * t;
        return [Math.max(rx, 0.1 * plein) * evase, (rz + 0.012) * evase];
      },
      deformer: (q, t, phi) => {
        if (t > 0.95) q.y += 0.015 * (1 - f.usure) * Math.abs(Math.sin(phi * 7));
      },
      couleur: (_q, t, phi) => couleur.clone().multiplyScalar(1 - 0.12 * Math.abs(Math.sin(phi * (f.vetement === PEAU_MATIERE ? 3 : 18))) - 0.05 * t),
      liens: (_q, t) => [[O.bassin, 1 - 0.3 * t]],
    });
  }

  // Tête et chevelure simples, en dernier : un groupe à part du maillage, que l'on cache quand la tête détaillée les remplace.
  const debutTete = m.indices.length;
  const hT = p.hauteTete;
  const yTete = 1 - hT * 0.5;
  const rxT = hT * 0.56;
  const rzT = hT * 0.66;
  const yeux = yTete + hT * 0.02;
  tube(m, {
    depart: new THREE.Vector3(0, 1 - hT - 0.002, 0.004), axe: new THREE.Vector3(0, 1, 0), longueur: hT + 0.002, anneaux: 22, cotes: 24,
    rayon: (t) => {
      const s = Math.sin(Math.PI * Math.min(1, Math.max(0, t)));
      // Mâchoire plus étroite en bas, crâne plus large en haut.
      const largeur = t < 0.45 ? entre(0.62, 1, lisser(0, 0.45, t)) : 1;
      return [rxT * Math.sqrt(s) * largeur, rzT * Math.sqrt(s) * (t < 0.3 ? 0.85 + 0.5 * t : 1)];
    },
    deformer: (q, t, phi) => {
      const avant = Math.cos(phi);
      if (avant > 0.6) {
        // Nez, arcades, lèvres : le visage complet viendra en 0.41.2.
        const nez = bosse(q.x / (hT * 0.09), (q.y - (yTete - hT * 0.08)) / (hT * 0.12));
        q.z += hT * 0.07 * nez * (0.6 + 0.4 * (1 - enfant));
        q.z += hT * 0.02 * bosse(q.x / (hT * 0.16), (q.y - (yTete - hT * 0.25)) / (hT * 0.05));
      }
      // Oreilles.
      if (Math.abs(Math.sin(phi)) > 0.93 && Math.abs(q.y - yeux + hT * 0.05) < hT * 0.1) q.x *= 1 + 0.08 * (1 - Math.abs(q.y - yeux + hT * 0.05) / (hT * 0.1));
      void t;
    },
    couleur: (q, _t, phi) => {
      const avant = Math.cos(phi);
      // Cheveux peints sur le crâne, au-dessus d'une ligne qui dégage le front et descend derrière les oreilles.
      if (q.y > yeux + hT * (avant > 0 ? 0.27 - (1 - avant) * 0.45 : -0.18 - 0.1 * avant) && f.age > 0.5) return f.cheveux;
      if (avant > 0.55) {
        for (const cote of [-1, 1]) {
          const d = Math.hypot((q.x - cote * hT * 0.18) / (hT * 0.055), (q.y - yeux) / (hT * 0.028));
          if (d < 1) return new THREE.Color(0.85, 0.82, 0.78).lerp(new THREE.Color(0.12, 0.08, 0.06), d < 0.65 ? 1 : 0);
          const s = Math.hypot((q.x - cote * hT * 0.19) / (hT * 0.11), (q.y - (yeux + hT * 0.085)) / (hT * 0.022));
          if (s < 1) return peau.clone().lerp(f.cheveux, 0.75);
        }
        const l = Math.hypot(q.x / (hT * 0.13), (q.y - (yTete - hT * 0.25)) / (hT * 0.035));
        if (l < 1) return peau.clone().lerp(new THREE.Color(0.55, 0.25, 0.22), 0.45);
      }
      return peau;
    },
    liens: () => [[O.tete, 1]],
  });
  // Chevelure : une coque un peu plus grande que le crâne, ouverte sur le visage (front, tempes), descendue dans le dos pour
  // les cheveux longs. Les points du visage sont rentrés sous la peau.
  const longs = f.cheveuxLongs && f.age > 3;
  const rayonTete = (y: number): [number, number] => {
    const t = Math.min(1, Math.max(0, (y - (1 - hT)) / hT));
    const s = Math.sin(Math.PI * t);
    const largeur = t < 0.45 ? entre(0.62, 1, lisser(0, 0.45, t)) : 1;
    return [rxT * Math.sqrt(s) * largeur, rzT * Math.sqrt(s) * (t < 0.3 ? 0.85 + 0.5 * t : 1)];
  };
  const basCheveux = yTete - hT * 0.95;
  if (longs) tube(m, {
    depart: new THREE.Vector3(0, basCheveux, 0.002), axe: new THREE.Vector3(0, 1, 0), longueur: 1 + 0.004 - basCheveux, anneaux: 18, cotes: 28,
    rayon: (t) => {
      const y = basCheveux + t * (1 + 0.004 - basCheveux);
      let [rx, rz] = rayonTete(Math.min(y, 1));
      if (y < yTete) [rx, rz] = [Math.max(rx, rxT * 0.98), Math.max(rz, rzT * 0.92)];
      const e = y > 1 - 0.001 ? 0.0001 : 1.08;
      return [rx * e + 0.0001, rz * e + 0.0001];
    },
    deformer: (q, _t, phi) => {
      const avant = Math.cos(phi);
      // La chevelure longue tombe dans le dos et sur les côtés ; devant, elle rentre sous la peau du crâne peint.
      if (avant > -0.2) {
        // Sous la peau du visage.
        q.x *= 0.75;
        q.z = (q.z - 0.004) * 0.75;
      }
    },
    couleur: () => f.cheveux,
    liens: (q) => (q.y < yTete - hT * 0.45 ? [[O.cou, 0.5], [O.tete, 0.5]] : [[O.tete, 1]]),
  });


  // Os au repos (positions absolues pour une taille de 1).
  const repos: THREE.Vector3[] = [];
  const ouv = Math.sin(ouvert);
  const lBras = p.epaule - p.coude;
  const lAvant = p.coude - p.poignet;
  const coudeY = p.epaule - 0.012 - lBras * Math.cos(ouvert);
  const poignetY = coudeY - lAvant * Math.cos(ouvert);
  const set = (n: NomOs, x: number, y: number, z: number) => (repos[O[n]] = new THREE.Vector3(x, y, z));
  set("bassin", 0, p.hanche, 0);
  set("ventre", 0, p.taille, 0);
  set("poitrine", 0, p.seins, 0);
  set("cou", 0, p.epaule, -0.008);
  set("tete", 0, p.menton, 0);
  for (const [cote, s] of [[1, "G"], [-1, "D"]] as const) {
    set(`bras${s}`, cote * p.xEpaule, p.epaule - 0.012, -0.004);
    set(`avantBras${s}`, cote * (p.xEpaule + lBras * ouv), coudeY, -0.004);
    set(`main${s}`, cote * (p.xEpaule + (lBras + lAvant) * ouv), poignetY, -0.004);
    set(`cuisse${s}`, cote * p.xHanche, p.hanche, 0);
    set(`jambe${s}`, cote * (p.xHanche - 0.006), p.genou, 0);
    set(`pied${s}`, cote * (p.xHanche - 0.006), p.cheville, 0);
  }
  const geometrie = m.geometrie();
  geometrie.addGroup(0, debutTete, 0);
  geometrie.addGroup(debutTete, m.indices.length - debutTete, 1);
  return { geometrie, repos, p };
}

const INVISIBLE = new THREE.MeshBasicMaterial({ visible: false });

/** Un corps posé dans la scène : son maillage, ses os, sa forme. */
export class Corps {
  readonly maillage: THREE.SkinnedMesh;
  readonly os: THREE.Bone[];
  readonly cle: string;
  readonly hautTete: number;
  readonly hanche: number;
  /** Proportions (pour poser la tête détaillée). */
  readonly p: Proportions;
  private readonly materiaux: [THREE.Material, THREE.Material];
  /**
   * Les os vivent dans un repère à part, près de l'origine, et non sous le maillage : posé à des centaines de kilomètres, le
   * skinning en flottants 32 bits arrondirait les sommets au centimètre. Le maillage, lui, est placé normalement.
   */
  readonly armature = new THREE.Object3D();

  constructor(f: Forme, materiau: THREE.Material) {
    const { geometrie, repos, p } = construire(f);
    this.cle = cleDe(f);
    this.hautTete = p.menton;
    this.hanche = p.hanche;
    this.p = p;
    this.materiaux = [materiau, materiau];
    this.os = OS.map((n) => Object.assign(new THREE.Bone(), { name: n }));
    OS.forEach((n, i) => {
      const parent = PARENT[n];
      const b = this.os[i];
      if (parent) {
        this.os[O[parent]].add(b);
        b.position.copy(repos[i]).sub(repos[O[parent]]);
      } else {
        b.position.copy(repos[i]);
      }
    });
    this.maillage = new THREE.SkinnedMesh(geometrie, this.materiaux);
    this.armature.add(this.os[0]);
    this.armature.updateMatrixWorld(true);
    this.maillage.bindMode = THREE.DetachedBindMode;
    this.maillage.bind(new THREE.Skeleton(this.os), new THREE.Matrix4());
    this.maillage.frustumCulled = false;
  }

  /** Montre ou cache la tête simple (remplacée de près par la tête détaillée). */
  teteSimple(visible: boolean): void {
    const voulu = visible ? this.materiaux[0] : INVISIBLE;
    if (this.materiaux[1] !== voulu) {
      this.materiaux[1] = voulu;
      this.maillage.material = [...this.materiaux];
    }
  }

  /** Repère du corps au repos (stature 1) → monde, pour ce qui suit la tête : maillage × os de la tête × inverse de liaison. */
  matriceTete(sortie: THREE.Matrix4): THREE.Matrix4 {
    const os = this.os[O.tete];
    const k = this.maillage.skeleton.bones.indexOf(os);
    return sortie.multiplyMatrices(this.maillage.matrixWorld, os.matrixWorld).multiply(this.maillage.skeleton.boneInverses[k]);
  }

  /** Remet tous les os droits. */
  private droit(): void {
    for (const b of this.os) b.rotation.set(0, 0, 0);
  }

  /**
   * Pose le squelette pour une posture. `phase` : cycle de marche ; `temps` : secondes réelles (petits mouvements) ;
   * `graine` : propre à chacun. Renvoie de combien descendre le bassin (en part de la taille) et la rotation du corps entier.
   */
  poser(posture: string, phase: number, temps: number, graine: number, voute: number, boite: boolean, porteBebe: boolean,
    regard: { lacet: number; tangage: number; poids: number } | null = null): { descente: number; couche: number } {
    this.droit();
    const b = this.os;
    const respire = Math.sin(temps * 1.6 + graine) * 0.015;
    b[O.poitrine].rotation.x = respire + voute * 0.25;
    b[O.ventre].rotation.x = voute * 0.12;
    b[O.cou].rotation.x = -voute * 0.25;
    b[O.tete].rotation.y = Math.sin(temps * 0.23 + graine) * 0.35;
    b[O.tete].rotation.x = Math.sin(temps * 0.17 + graine * 2) * 0.08;
    // Bras légèrement pliés au repos.
    b[O.avantBrasG].rotation.x = b[O.avantBrasD].rotation.x = -0.12;
    let descente = 0;
    let couche = 0;
    switch (posture) {
      case "marche": {
        const a = Math.sin(phase);
        const ampl = boite ? 0.3 : 0.45;
        b[O.cuisseG].rotation.x = -a * ampl;
        b[O.cuisseD].rotation.x = a * ampl * (boite ? 0.6 : 1);
        b[O.jambeG].rotation.x = Math.max(0, Math.sin(phase + 1.2)) * 0.9;
        b[O.jambeD].rotation.x = Math.max(0, Math.sin(phase + Math.PI + 1.2)) * 0.9;
        b[O.piedG].rotation.x = -Math.max(0, Math.sin(phase - 0.6)) * 0.3;
        b[O.piedD].rotation.x = -Math.max(0, Math.sin(phase + Math.PI - 0.6)) * 0.3;
        b[O.brasG].rotation.x = a * 0.4;
        b[O.brasD].rotation.x = -a * 0.4;
        b[O.avantBrasG].rotation.x = -0.25 - Math.max(0, a) * 0.3;
        b[O.avantBrasD].rotation.x = -0.25 - Math.max(0, -a) * 0.3;
        b[O.bassin].rotation.y = a * 0.08;
        b[O.poitrine].rotation.y = -a * 0.1;
        b[O.ventre].rotation.x += 0.04;
        descente = 0.012 * (1 - Math.abs(Math.cos(phase)));
        if (boite) b[O.bassin].rotation.z = Math.sin(phase) * 0.05;
        break;
      }
      case "assis":
      case "travail": {
        // Assis par terre, jambes repliées devant soi.
        for (const [c, j, pd, s] of [[O.cuisseG, O.jambeG, O.piedG, 1], [O.cuisseD, O.jambeD, O.piedD, -1]] as const) {
          b[c].rotation.set(-2.1, 0, s * 0.3);
          b[j].rotation.x = 1.75 + 0.1 * s;
          b[pd].rotation.x = -0.3;
        }
        b[O.ventre].rotation.x += 0.12;
        if (posture === "travail") {
          const g = Math.sin(temps * 2.4 + graine);
          b[O.brasG].rotation.set(-0.7 + g * 0.15, 0, -0.25);
          b[O.brasD].rotation.set(-0.6 - g * 0.12, 0, 0.25);
          b[O.avantBrasG].rotation.x = -1.1 - g * 0.2;
          b[O.avantBrasD].rotation.x = -1.2 + g * 0.2;
          b[O.tete].rotation.x = 0.35;
          b[O.tete].rotation.y *= 0.3;
        } else {
          b[O.brasG].rotation.set(-0.45, 0, -0.1);
          b[O.brasD].rotation.set(-0.45, 0, 0.1);
          b[O.avantBrasG].rotation.x = -0.6;
          b[O.avantBrasD].rotation.x = -0.6;
        }
        descente = this.hanche - 0.085;
        break;
      }
      case "accroupi": {
        const g = Math.sin(temps * 1.5 + graine);
        for (const [c, j, pd, s] of [[O.cuisseG, O.jambeG, O.piedG, 1], [O.cuisseD, O.jambeD, O.piedD, -1]] as const) {
          b[c].rotation.set(-1.85, 0, s * 0.3);
          b[j].rotation.x = 2.2;
          b[pd].rotation.x = -0.35;
        }
        b[O.ventre].rotation.x += 0.45;
        b[O.poitrine].rotation.x += 0.2;
        b[O.brasG].rotation.set(-1.0 + g * 0.25, 0, -0.1);
        b[O.brasD].rotation.set(-0.7, 0, 0.15);
        b[O.avantBrasG].rotation.x = -0.4;
        b[O.avantBrasD].rotation.x = -0.6;
        b[O.tete].rotation.x = 0.35;
        descente = this.hanche - 0.19;
        break;
      }
      case "couche":
      case "porte": {
        // Allongé sur le côté, un peu replié (le corps entier tourne).
        b[O.cuisseG].rotation.x = -0.5;
        b[O.cuisseD].rotation.x = -0.35;
        b[O.jambeG].rotation.x = 0.7;
        b[O.jambeD].rotation.x = 0.5;
        b[O.brasG].rotation.set(-0.8, 0, 0);
        b[O.brasD].rotation.set(-0.6, 0, 0.2);
        b[O.avantBrasG].rotation.x = -0.8;
        b[O.avantBrasD].rotation.x = -1;
        b[O.tete].rotation.y *= 0.2;
        couche = 1;
        if (posture === "porte") {
          b[O.cuisseG].rotation.x = b[O.cuisseD].rotation.x = -1.2;
          b[O.jambeG].rotation.x = b[O.jambeD].rotation.x = 1.4;
          couche = 2;
        }
        break;
      }
      case "nage": {
        const a = Math.sin(phase * 1.5);
        b[O.brasG].rotation.set(-2.6 + a * 0.5, 0, 0);
        b[O.brasD].rotation.set(-2.6 - a * 0.5, 0, 0);
        b[O.cuisseG].rotation.x = a * 0.35;
        b[O.cuisseD].rotation.x = -a * 0.35;
        b[O.tete].rotation.x = -0.6;
        couche = 3;
        break;
      }
      default: {
        // Debout : poids sur une jambe, bras ballants qui bougent un peu.
        const appui = unite(hacher(Math.floor(graine * 1000), Math.floor(temps / 9))) < 0.5 ? 1 : -1;
        b[O.bassin].rotation.z = appui * 0.04;
        b[O.ventre].rotation.z = -appui * 0.03;
        b[O.cuisseG].rotation.z = -appui * 0.02;
        b[O.cuisseD].rotation.z = -appui * 0.02;
        b[appui > 0 ? O.jambeD : O.jambeG].rotation.x = 0.12;
        b[O.brasG].rotation.x = respire * 2;
        b[O.brasD].rotation.x = -respire * 2;
      }
    }
    // Une mère qui porte son bébé le tient d'un bras.
    if (porteBebe && posture !== "couche" && posture !== "nage") {
      b[O.brasD].rotation.set(-0.35, 0, 0.35);
      b[O.avantBrasD].rotation.set(-1.6, 0.4, 0);
    }
    // Tourné vers quelqu'un (l'Entité qui lui parle) : la tête et un peu le buste se tournent vers lui.
    if (regard && regard.poids > 0.01 && couche === 0) {
      const w = regard.poids;
      const lacet = Math.max(-1.1, Math.min(1.1, regard.lacet));
      b[O.poitrine].rotation.y = b[O.poitrine].rotation.y * (1 - w) + lacet * 0.25 * w;
      b[O.cou].rotation.y = lacet * 0.3 * w;
      b[O.tete].rotation.y = b[O.tete].rotation.y * (1 - w) + lacet * 0.45 * w;
      b[O.tete].rotation.x = b[O.tete].rotation.x * (1 - w) + Math.max(-0.5, Math.min(0.5, regard.tangage)) * w;
    }
    return { descente, couche };
  }

  liberer(): void {
    this.maillage.geometry.dispose();
    this.maillage.skeleton.dispose();
    this.maillage.removeFromParent();
  }
}
