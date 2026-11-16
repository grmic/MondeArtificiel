import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";
import { etreAvecLesAutres } from "./social";
import { teindre } from "./corps";
import type { Rejeu, Role } from "./combat";

/** Activités, dans l'ordre de l'énumération Activite du serveur (pour les textes « activite.X »). */
export const ACTIVITES = [
  "Repos", "Dormir", "Boire", "ChercherEau", "Cueillir", "Chasser", "ChercherNourriture", "SAbriter", "ChercherAbri", "RejoindrePartenaire",
  "Explorer", "Mort", "SAccoupler", "SuivreParent", "PorteParSaMere", "VeillerEnfant", "RejoindreProche", "EviterRival", "Migrer", "Fuir",
  "Raid", "RentrerDuRaid", "PartirEnExil", "Experimenter", "Fabriquer", "Veiller", "Soigner", "Nager", "SuivreRavisseur", "ChercherLeFeu",
  "Enseigner", "Visiter", "Rassembler", "Rentrer", "Demander", "Naviguer", "Pecher", "Cultiver", "Allaiter", "Prier", "Chanter",
] as const;
export const A = Object.fromEntries(ACTIVITES.map((a, i) => [a, i])) as Record<(typeof ACTIVITES)[number], number>;

/** Ce que le flux Scène dit d'un humain (voir SceneDto côté serveur). */
export interface HumainScene {
  id: number;
  x: number;
  y: number;
  versX: number;
  versY: number;
  avance: number;
  parSeconde: number;
  activite: number;
  etat: number;
  age: number;
  femme: boolean;
  teint: number;
  humeur: number;
  peur: number;
  communaute: number;
  mere: number;
  /** Depuis la 0.41.1 : réserves (0-100), endurance (0-1), taille propre (écart −3 à 3), enfants mis au monde, vêtement, usure. */
  reserves: number;
  endurance: number;
  tailleEcart: number;
  enfants: number;
  vetement: number;
  usure: number;
  /** 0.41.8 : froid ressenti, faim, fatigue, santé (0 à 100), névrosisme et extraversion (0 à 1). */
  froid: number;
  faim: number;
  fatigue: number;
  sante: number;
  nevrosisme: number;
  extraversion: number;
  /** Aisance dans l'eau (0 à 1) : brasse de qui sait nager, nage du chien de qui sait à peine. */
  nage: number;
  /** 0.42.6 : matière de la semelle nouée aux pieds (0 : pieds nus) et de l'enduit de la peau (0 rien, 11 boue, 18 ocre). */
  chaussures: number;
  enduit: number;
  /**
   * 0.45.0 : un tout-petit, la nuit, là où sa mère l'a couché (0 rien de décidé, 1 contre elle, 2 à côté d'elle, 3 confié à un
   * autre, 4 à l'écart) et celui contre qui, ou à côté de qui, il dort.
   */
  coucher: number;
  dortContre: number;
  /** Essais (?debug3d) : un figurant posé à un endroit précis, dans une posture donnée. */
  figurant?: { x: number; z: number; cap: number; posture: Posture };
}

export const CHAMPS = 33;

export function lireScene(n: number[]): HumainScene[] {
  const liste: HumainScene[] = [];
  for (let k = 0; k + CHAMPS <= n.length; k += CHAMPS) {
    liste.push({
      id: n[k], x: n[k + 1], y: n[k + 2], versX: n[k + 3], versY: n[k + 4], avance: n[k + 5] / 1000, parSeconde: n[k + 6] / 1e6,
      activite: n[k + 7], etat: n[k + 8], age: n[k + 9] / 10, femme: n[k + 10] === 1, teint: n[k + 11] / 1000, humeur: n[k + 12],
      peur: n[k + 13], communaute: n[k + 14], mere: n[k + 15], reserves: n[k + 16], endurance: n[k + 17] / 1000, tailleEcart: n[k + 18] / 100,
      enfants: n[k + 19], vetement: n[k + 20], usure: n[k + 21] / 1000,
      froid: n[k + 22], faim: n[k + 23], fatigue: n[k + 24], sante: n[k + 25], nevrosisme: n[k + 26] / 1000, extraversion: n[k + 27] / 1000,
      nage: n[k + 28] / 1000, chaussures: n[k + 29], enduit: n[k + 30], coucher: n[k + 31], dortContre: n[k + 32],
    });
  }
  return liste;
}

export type Posture = "debout" | "marche" | "assis" | "couche" | "accroupi" | "nage" | "travail" | "porte" | "enlace" | "combat";

export interface Corps {
  s: HumainScene;
  x: number;
  y: number;
  z: number;
  cap: number;
  phase: number;
  vitesse: number;
  taille: number;
  posture: Posture;
  vu: number;
  peau: THREE.Color;
  cheveux: THREE.Color;
  /** Peau telle qu'on la voit (teinte d'ocre ou de boue), et l'enduit pour lequel elle a été calculée. */
  peauVue?: THREE.Color;
  enduitVu?: number;
  /** Chemin en cours (case de départ et d'arrivée) et où il en était au dernier pas du monde (0.41.2). */
  chemin: string;
  avancePrec: number;
  /** H1 (0.41.8) : celui à qui il parle (0 : personne), et s'il a la parole (0 à 1). */
  interlocuteur: number;
  parle: number;
  /** Celui ou celle avec qui il fait l'amour (0 : personne), apparié sur sa case. */
  partenaire: number;
  secondesPrec: number;
  /** Il devrait marcher mais n'avance plus d'un pas à l'autre (bloqué, en attente) : on ne le fait pas avancer. */
  bloque: boolean;
  /** Il marche vraiment (son corps se déplace) : seule raison d'animer la marche. */
  enMarche: boolean;
  /** Tourné vers l'Entité qui lui parle (0 à 1, pour la tête). */
  ecoute: number;
  /** 0.42.0 : ce que le rejeu d'un assaut lui fait faire (ou, mort, comment il gît), sinon null. */
  role: Role | null;
  /** Ce qu'il fait dans sa case (C1) : ses arrêts déjà posés sur la terre ferme, et le trajet d'une case à l'autre (détour). */
  arrets?: { cle: string; j: number[]; x: number[]; z: number[] };
  trajet?: { cle: string; points: [number, number][]; longueurs: number[] };
  /** Sa place autour du camp, voulue (vx, vz) et posée sur la terre ferme (x, z). */
  place?: { vx: number; vz: number; x: number; z: number };
}

/**
 * Taille (m) selon l'âge et le sexe : croissance de l'enfance, puis stature adulte propre à chacun (sa taille dans la simulation,
 * écart-type d'environ 7 cm ; Roser et al., 2013), et un peu de tassement dans la vieillesse.
 */
export function stature(age: number, femme: boolean, ecart: number): number {
  const adulte = (femme ? 1.58 : 1.7) + ecart * 0.07;
  const courbe: [number, number][] = [[0, 0.29], [1, 0.44], [2, 0.51], [5, 0.65], [10, 0.82], [14, 0.93], [17, 1]];
  if (age >= 17) return adulte * (age > 65 ? 1 - (age - 65) * 0.002 : 1);
  for (let k = 1; k < courbe.length; k++) {
    if (age <= courbe[k][0]) {
      const [a0, f0] = courbe[k - 1];
      const [a1, f1] = courbe[k];
      return adulte * (f0 + ((age - a0) / (a1 - a0)) * (f1 - f0));
    }
  }
  return adulte;
}

/** Manières de coucher un tout-petit la nuit (énumération Coucher du serveur). */
export const COUCHER = { Contre: 1, ACote: 2, Confie: 3, AEcart: 4 } as const;

/** Celui qui le tient ou contre qui il dort : la nuit, celui que sa mère a choisi ; sinon sa mère. */
export function porteurDe(s: HumainScene): number {
  return s.coucher > 0 && s.dortContre > 0 ? s.dortContre : s.mere;
}

function posture(s: HumainScene, enMouvement: boolean, dansLEau: boolean): Posture {
  if (s.activite === A.Mort) return "couche";
  // La nuit (0.45.0), contre sa mère ou contre celui à qui elle l'a confié ; à côté d'elle ou à l'écart, il dort seul sur le sol.
  if (s.coucher === COUCHER.Contre || s.coucher === COUCHER.Confie) return "porte";
  if (s.coucher === COUCHER.ACote || s.coucher === COUCHER.AEcart) return "couche";
  if (s.etat & 32 || s.activite === A.PorteParSaMere) return "porte";
  if (dansLEau) return "nage";
  if (enMouvement) return "marche";
  switch (s.activite) {
    case A.SAccoupler:
      // Faire l'amour : le couple allongé, enlacé, sans plus de détail ; entre adultes seulement à l'écran.
      return s.age >= 18 ? "enlace" : "assis";
    case A.Dormir:
    case A.SAbriter:
      return "couche";
    case A.Veiller:
    case A.VeillerEnfant:
    case A.Allaiter:
    case A.Prier:
      return "assis";
    case A.Repos:
      return unite(hacher(s.id, 3)) < 0.5 ? "assis" : "couche";
    case A.Cueillir:
    case A.Cultiver:
    case A.ChercherNourriture:
      return "accroupi";
    case A.Fabriquer:
    case A.Experimenter:
    case A.Soigner:
    case A.Enseigner:
      return "travail";
    default:
      return "debout";
  }
}

/** Couché (endormi, au repos, ou à terre dans un combat). */
export function aTerre(c: Corps): boolean {
  return c.posture === "couche" || (c.posture === "combat" && (c.role?.geste === "gisant" || c.role?.geste === "chute"));
}

/** Où se tient, dans sa case, quelqu'un qui ne marche pas : autour du camp, plus loin pour cueillir ou chasser. */
function decalage(s: HumainScene, secondes: number, auFeu: boolean): [number, number] {
  const angle0 = unite(hacher(s.id, 7)) * Math.PI * 2;
  const r0 = unite(hacher(s.id, 9));
  let rayon: number;
  let derive = 0;
  switch (s.activite) {
    case A.Veiller:
      rayon = 2.4 + r0;
      break;
    case A.Repos:
    case A.VeillerEnfant:
    case A.Allaiter:
    case A.Prier:
    case A.Chanter:
    case A.Fabriquer:
    case A.Enseigner:
    case A.Soigner:
    case A.Experimenter:
      // Au camp où brûle un feu, on s'assoit autour (H1) ; sinon on se disperse.
      if (auFeu) {
        rayon = 2.1 + 1.6 * r0;
        break;
      }
      rayon = 4 + r0 * 12;
      derive = secondes / 36000;
      break;
    case A.Dormir:
    case A.SAbriter:
      rayon = 3 + r0 * 6;
      break;
    case A.Cueillir:
    case A.Chasser:
    case A.ChercherNourriture:
    case A.Cultiver:
    case A.Pecher:
    case A.Explorer:
      rayon = 40 + r0 * 200;
      derive = secondes / 5400;
      break;
    default:
      rayon = 4 + r0 * 12;
      derive = secondes / 36000;
  }
  const a = angle0 + derive * (0.6 + r0);
  return [Math.cos(a) * rayon, Math.sin(a) * rayon];
}


// ——— Ce qu'il fait dans sa case (C1, suivi 3D cohérent) ———
// La simulation ne connaît que la case (800 m). Tant qu'il y reste, la vue lui donne un itinéraire tiré de son id et de l'heure
// (tout le monde voit la même chose, rien n'est demandé au serveur) : cueillir de touffe en touffe, pister puis guetter, explorer.

/** Profondeur d'eau (m) en un point, 0 au sec. */
export function profondeur(rel: Relief, x: number, z: number): number {
  const s = rel.surface(x, z);
  return Number.isNaN(s.eau) ? 0 : s.eau - s.sol;
}

/** Pente du sol (m par m), mesurée sur quelques mètres. */
function pente(rel: Relief, x: number, z: number): number {
  const e = 1.5;
  return Math.hypot(rel.hauteur(x + e, z) - rel.hauteur(x - e, z), rel.hauteur(x, z + e) - rel.hauteur(x, z - e)) / (2 * e);
}

/**
 * Le point praticable le plus proche de (x, z) : pas plus d'eau que `eauAdmise` (m), pas une pente de falaise (au-delà de 35°,
 * on ne se tient plus debout sans les mains). Cherché en spirale jusqu'à 200 m ; sinon le point tel quel.
 */
export function aPiedSec(rel: Relief, x: number, z: number, eauAdmise: number, graine: number): [number, number] {
  const bon = (px: number, pz: number) => profondeur(rel, px, pz) <= eauAdmise && pente(rel, px, pz) < 0.7;
  if (bon(x, z)) return [x, z];
  for (let r = 6; r <= 200; r *= 1.35) {
    const depart = unite(hacher(graine, Math.round(r), 191)) * Math.PI * 2;
    for (let k = 0; k < 10; k++) {
      const a = depart + (k / 10) * Math.PI * 2;
      const px = x + Math.cos(a) * r;
      const pz = z + Math.sin(a) * r;
      if (bon(px, pz)) return [px, pz];
    }
  }
  return [x, z];
}

/**
 * Manière de faire de chaque activité qui fait bouger dans la case : à quelle distance du camp, pas entre deux arrêts (m), arrêt
 * moyen (s), allure (m/s). Ordres de grandeur : cueillette des Hadza et des !Kung, de buisson en buisson avec des arrêts de quelques
 * minutes (Hawkes et al. 1991 ; Lee 1979) ; chasse à l'approche lente, entrecoupée d'affûts (Marlowe 2010) ; pêche postée au bord.
 * Chacun a sa façon (×0,7 à ×1,4), tirée de son id.
 */
interface Maniere { loin: [number, number]; pas: [number, number]; arret: [number, number]; allure: [number, number]; eau: number }
function maniere(activite: number): Maniere | null {
  switch (activite) {
    case A.Cueillir:
    case A.ChercherNourriture:
      return { loin: [40, 220], pas: [5, 25], arret: [60, 300], allure: [0.5, 0.9], eau: 0.05 };
    case A.Cultiver:
      return { loin: [20, 90], pas: [2, 8], arret: [90, 400], allure: [0.4, 0.7], eau: 0.05 };
    case A.Chasser:
      return { loin: [120, 320], pas: [15, 70], arret: [30, 300], allure: [0.35, 0.8], eau: 0.05 };
    case A.Pecher:
      // Au bord de l'eau, dans l'eau jusqu'aux genoux au plus.
      return { loin: [30, 200], pas: [3, 12], arret: [300, 900], allure: [0.4, 0.7], eau: 0.4 };
    case A.Explorer:
      return { loin: [60, 300], pas: [40, 120], arret: [10, 60], allure: [0.9, 1.3], eau: 0.05 };
    default:
      return null;
  }
}

function entre([a, b]: [number, number], u: number): number {
  return a + (b - a) * u;
}

/** Arrêt n° j de son itinéraire (avant de le poser sur la terre ferme). */
function arretBrut(id: number, j: number, cx: number, cz: number, rayon: number, pas: number): [number, number] {
  const w = pas / Math.max(rayon, 1);
  const f = (k: number) => unite(hacher(id, k, 197)) * Math.PI * 2;
  const x = cx + rayon * (0.7 * Math.sin(w * j + f(1)) + 0.3 * Math.sin(2.3 * w * j + f(2)));
  const z = cz + rayon * (0.7 * Math.cos(1.1 * w * j + f(3)) + 0.3 * Math.sin(1.7 * w * j + f(4)));
  const jx = (unite(hacher(id, j, 199)) - 0.5) * pas * 0.6;
  const jz = (unite(hacher(id, j, 211)) - 0.5) * pas * 0.6;
  return [x + jx, z + jz];
}

/** Place qu'occupe au sol un corps arrêté (rayon, m) : couché, il lui faut sa longueur. */
function emprise(c: Corps): number {
  // D'après ce que fait l'humain dans la simulation, pas d'après la posture affichée : sinon celui qu'on écarte se met à marcher,
  // perd sa place, revient, et ainsi de suite (il se lève et se recouche sans fin).
  switch (posture(c.s, false, false)) {
    case "couche":
      return 0.62 * c.taille + 0.05;
    case "assis":
    case "travail":
    case "accroupi":
      return 0.28 * c.taille + 0.05;
    case "debout":
      return 0.2 * c.taille + 0.05;
    default:
      return 0;
  }
}

/**
 * Deux corps arrêtés ne se chevauchent pas : leurs places s'écartent jusqu'à ce que chacun ait la sienne (dormeurs côte à côte
 * plutôt que les uns sur les autres). Les figurants des essais ne bougent pas.
 */
function ecarter(cibles: Map<Corps, [number, number, boolean]>): void {
  const arretes: { c: Corps; t: [number, number, boolean]; r: number }[] = [];
  for (const [c, t] of cibles) {
    const r = t[2] || c.s.versX >= 0 || (c.role && !c.role.mort) ? 0 : emprise(c);
    if (r > 0) arretes.push({ c, t, r });
  }
  if (arretes.length < 2) return;
  for (let passe = 0; passe < 4; passe++) {
    const grille = new Map<string, number[]>();
    arretes.forEach((a, i) => {
      const k = `${Math.floor(a.t[0] / 3)},${Math.floor(a.t[1] / 3)}`;
      let l = grille.get(k);
      if (!l) grille.set(k, (l = []));
      l.push(i);
    });
    let bouge = false;
    arretes.forEach((a, i) => {
      const gx = Math.floor(a.t[0] / 3);
      const gz = Math.floor(a.t[1] / 3);
      for (let u = -1; u <= 1; u++) {
        for (let v = -1; v <= 1; v++) {
          for (const j of grille.get(`${gx + u},${gz + v}`) ?? []) {
            if (j <= i) continue;
            const b = arretes[j];
            let dx = b.t[0] - a.t[0];
            let dz = b.t[1] - a.t[1];
            let d = Math.hypot(dx, dz);
            const voulu = a.r + b.r;
            if (d >= voulu) continue;
            if (d < 1e-3) {
              const ang = unite(hacher(a.c.s.id, b.c.s.id, 17)) * Math.PI * 2;
              dx = Math.cos(ang);
              dz = Math.sin(ang);
              d = 1;
            }
            const fa = a.c.s.figurant ? 0 : b.c.s.figurant ? 1 : 0.5;
            const fb = b.c.s.figurant ? 0 : a.c.s.figurant ? 1 : 0.5;
            const pousse = (voulu - Math.min(d, voulu)) / d;
            a.t[0] -= dx * pousse * fa;
            a.t[1] -= dz * pousse * fa;
            b.t[0] += dx * pousse * fb;
            b.t[1] += dz * pousse * fb;
            bouge = true;
          }
        }
      }
    });
    if (!bouge) break;
  }
}

interface Trajet { cle: string; points: [number, number][]; longueurs: number[] }

/** Point du trajet à la part `f` (0 à 1) de sa longueur, et la direction (unitaire) du segment. */
function surTrajet(t: Trajet, f: number): [number, number, number, number] {
  const total = t.longueurs[t.longueurs.length - 1];
  const d = f * total;
  let k = 1;
  while (k < t.points.length - 1 && t.longueurs[k] < d) k++;
  const [x0, z0] = t.points[k - 1];
  const [x1, z1] = t.points[k];
  const l = t.longueurs[k] - t.longueurs[k - 1] || 1;
  const g = Math.max(0, Math.min(1, (d - t.longueurs[k - 1]) / l));
  return [x0 + (x1 - x0) * g, z0 + (z1 - z0) * g, (x1 - x0) / l, (z1 - z0) / l];
}

const NOMBRE_MAX = 700;

/** Foulées par seconde au plus à l'écran (un coureur rapide en fait un peu plus de trois). */
const CADENCE_MAX = 3.5;
/** Aisance dans l'eau à partir de laquelle on nage la brasse plutôt que comme un chien. */
export const SAIT_NAGER = 0.35;
/** Accélération des gestes en film accéléré (A1), au plus ×10 : au-delà, souffle et gestes deviendraient un tremblement. */
export const ACCELERATION_MAX = 10;
export function accelere(facteur: number): number {
  return Math.max(1, Math.min(ACCELERATION_MAX, facteur));
}

/**
 * Les humains vus en 3D, en silhouettes (0.41.0) : taille selon l'âge et le sexe, teint, posture selon ce qu'ils font (marcher, dormir,
 * s'asseoir au feu, cueillir accroupi, nager), bébés portés par leur mère. Ils vont de case en case à l'allure que donne la simulation.
 * Les corps détaillés viendront en 0.41.1.
 */
export class Foule {
  readonly groupe = new THREE.Group();
  private readonly corps = new Map<number, Corps>();
  private readonly parties: THREE.InstancedMesh[];
  private readonly origine = new THREE.Vector3();
  private image = 0;
  /** Instant du monde du dernier flux et moment (performance.now) où il est arrivé, vitesse atteinte. */
  private secondesScene = 0;
  /** Humains montrés en corps détaillés (0.41.1) : pas de silhouette pour eux. */
  readonly detailles = new Set<number>();
  /** L'humain à qui l'Entité parle (0 : personne) : il s'arrête et se tourne vers la caméra (0.41.2). */
  interlocuteur = 0;
  /** Figurants des essais automatiques (?debug3d), ajoutés à chaque flux. */
  figurants: HumainScene[] = [];
  /** Feux de camp (H1) : où ils sont (m) et sur quelles cases. */
  feux: { x: number; z: number }[] = [];
  private feuxCases = new Set<string>();
  /** Temps des gestes écoulé (tours de parole) : secondes réelles, accélérées comme le monde (A1). */
  private horloge = 0;
  /** Vitesse du monde atteinte (secondes du monde par seconde réelle), donnée par la vue à chaque image. */
  facteur = 1;
  definirFeux(feux: { x: number; y: number }[]): void {
    this.feuxCases = new Set(feux.map((f) => `${f.x},${f.y}`));
    this.feux = feux.map((f) => {
      const [x, z] = this.relief.camp(f.x, f.y);
      return { x, z };
    });
  }
  /** Au-delà, ni silhouette ni corps : un point de vie (niveau de détail lointain). */
  porteeSilhouettes = 2500;
  private readonly points: THREE.InstancedMesh;

  constructor(readonly relief: Relief) {
    const peau = new THREE.MeshStandardMaterial({ roughness: 0.75, metalness: 0 });
    const torse = new THREE.CapsuleGeometry(0.085, 0.2, 4, 8).translate(0, 0.685, 0);
    const tete = new THREE.SphereGeometry(0.065, 12, 10);
    const cheveux = new THREE.SphereGeometry(0.069, 12, 6, 0, Math.PI * 2, 0, Math.PI * 0.55);
    const jambe = new THREE.CapsuleGeometry(0.042, 0.4, 4, 6).translate(0, -0.24, 0);
    const bras = new THREE.CapsuleGeometry(0.03, 0.3, 4, 6).translate(0, -0.18, 0);
    const geometries = [torse, tete, cheveux, jambe, jambe, bras, bras];
    this.parties = geometries.map((g) => {
      const m = new THREE.InstancedMesh(g, peau, NOMBRE_MAX);
      m.count = 0;
      m.frustumCulled = false;
      m.setColorAt(0, new THREE.Color());
      this.groupe.add(m);
      return m;
    });
    this.points = new THREE.InstancedMesh(new THREE.OctahedronGeometry(1, 0), new THREE.MeshBasicMaterial({ color: 0xffe2a0, fog: false }), NOMBRE_MAX);
    this.points.count = 0;
    this.points.frustumCulled = false;
    this.groupe.add(this.points);
  }

  /** Les points lointains sont des repères : leur éclat suit l'inverse de l'exposition (0.41.3). */
  set exposition(e: number) {
    (this.points.material as THREE.MeshBasicMaterial).color.setHex(0xffe2a0).multiplyScalar(Math.min(4, 0.6 / Math.max(e, 0.01)));
  }

  /** Nouveau flux Scène. */
  recevoir(humains: HumainScene[], secondes: number): void {
    this.image++;
    humains = [...humains, ...this.figurants];
    this.secondesScene = secondes;
    for (const s of humains) {
      const c = this.corps.get(s.id);
      if (c) {
        // Même chemin, pas suivant du monde, et pas d'avancée : il est arrêté, même si la simulation le dit en route.
        const chemin = `${s.x},${s.y},${s.versX},${s.versY}`;
        if (chemin === c.chemin && s.versX >= 0) {
          if (secondes > c.secondesPrec) {
            c.bloque = s.avance <= c.avancePrec + 1e-6;
            c.avancePrec = s.avance;
            c.secondesPrec = secondes;
          }
        } else {
          c.chemin = chemin;
          c.bloque = false;
          c.avancePrec = s.avance;
          c.secondesPrec = secondes;
        }
        c.s = s;
        c.vu = this.image;
      } else {
        const t = s.teint;
        const peau = new THREE.Color().setRGB(0.93 - 0.6 * t, 0.78 - 0.56 * t, 0.66 - 0.5 * t, THREE.SRGBColorSpace);
        const gris = s.age > 50 ? Math.min(1, (s.age - 50) / 25) : 0;
        const brun = 0.05 + 0.12 * unite(hacher(s.id, 5)) * (1 - t);
        const cheveux = new THREE.Color().setRGB(brun + gris * 0.55, brun * 0.7 + gris * 0.55, brun * 0.5 + gris * 0.55, THREE.SRGBColorSpace);
        this.corps.set(s.id, {
          s, x: NaN, y: 0, z: NaN, cap: unite(hacher(s.id, 11)) * Math.PI * 2, phase: 0, vitesse: 0, taille: 1, posture: "debout", vu: this.image, peau, cheveux,
          chemin: `${s.x},${s.y},${s.versX},${s.versY}`, avancePrec: s.avance, secondesPrec: secondes, bloque: false, enMarche: false, ecoute: 0,
          interlocuteur: 0, parle: 0, partenaire: 0, role: null,
        });
      }
    }
    for (const [id, c] of this.corps) if (c.vu !== this.image) this.corps.delete(id);
    // Les couples qui font l'amour : appariés deux à deux sur leur case (le flux ne dit pas avec qui).
    const amants = new Map<string, Corps[]>();
    for (const c of this.corps.values()) {
      c.partenaire = 0;
      if (c.s.activite !== A.SAccoupler || c.s.age < 18) continue;
      const k = `${c.s.x},${c.s.y}`;
      let l = amants.get(k);
      if (!l) amants.set(k, (l = []));
      l.push(c);
    }
    for (const l of amants.values()) {
      l.sort((a, b) => a.s.id - b.s.id);
      for (let i = 0; i + 1 < l.length; i += 2) {
        l[i].partenaire = l[i + 1].s.id;
        l[i + 1].partenaire = l[i].s.id;
      }
    }
  }

  /** Position visée d'un humain à l'instant `secondes` du monde, et s'il est en route. */
  private cible(c: Corps, secondes: number): [number, number, boolean] {
    const s = c.s;
    const [ax, az] = this.relief.camp(s.x, s.y);
    if (s.versX >= 0) {
      const [bx, bz] = this.relief.camp(s.versX, s.versY);
      const parSeconde = c.bloque ? 0 : s.parSeconde;
      const f = Math.min(0.985, s.avance + parSeconde * Math.max(0, secondes - this.secondesScene));
      const lateral = (unite(hacher(s.id, 13)) - 0.5) * 6;
      const [x, z, dx, dz] = surTrajet(this.trajet(c, ax, az, bx, bz), f);
      return [x - dz * lateral, z + dx * lateral, parSeconde > 0 && f < 0.985];
    }
    if (c.partenaire) {
      // À l'écart du camp (douze à vingt mètres), l'un contre l'autre.
      const bas = Math.min(s.id, c.partenaire);
      const angle = unite(hacher(bas, 23)) * Math.PI * 2;
      const r = 12 + 8 * unite(hacher(bas, 29));
      const [px, pz] = this.aSec(c, ax + Math.cos(angle) * r, az + Math.sin(angle) * r, 0.05, bas);
      const cote = s.id === bas ? -0.23 : 0.23;
      return [px - Math.sin(angle) * cote, pz + Math.cos(angle) * cote, false];
    }
    const m = maniere(s.activite);
    if (m) return this.itineraire(c, m, ax, az, secondes);
    const [ox, oz] = decalage(s, secondes, this.feuxCases.has(`${s.x},${s.y}`));
    const [x, z] = this.aSec(c, ax + ox, az + oz, 0.05, s.id);
    return [x, z, false];
  }

  /**
   * Sa place, posée sur la terre ferme. La place voulue ne bouge que lentement (dérive autour du camp) : le résultat est gardé tant
   * qu'elle reste à moins de trois mètres de celle qu'on a cherchée, et suit son mouvement.
   */
  private aSec(c: Corps, x: number, z: number, eau: number, graine: number): [number, number] {
    const p = c.place;
    if (p && Math.hypot(x - p.vx, z - p.vz) < 3) return [p.x + x - p.vx, p.z + z - p.vz];
    const [sx, sz] = aPiedSec(this.relief, x, z, eau, graine);
    c.place = { vx: x, vz: z, x: sx, z: sz };
    return [sx, sz];
  }

  /**
   * Le chemin d'un camp à l'autre (C1, D1) : tout droit, sauf si l'eau y est plus profonde qu'à mi-corps ; on passe alors par un
   * détour à pied sec s'il y en a un à moins de 600 m (un lac se contourne, une rivière trop large se traverse à la nage).
   */
  private trajet(c: Corps, ax: number, az: number, bx: number, bz: number): Trajet {
    const cle = `${ax},${az},${bx},${bz}`;
    if (c.trajet?.cle === cle) return c.trajet;
    const rel = this.relief;
    const profond = Math.max(0.45, 0.55 * stature(c.s.age, c.s.femme, c.s.tailleEcart));
    const passe = (x0: number, z0: number, x1: number, z1: number) => {
      const n = Math.max(4, Math.ceil(Math.hypot(x1 - x0, z1 - z0) / 40));
      for (let k = 1; k < n; k++) if (profondeur(rel, x0 + ((x1 - x0) * k) / n, z0 + ((z1 - z0) * k) / n) > profond) return false;
      return true;
    };
    let points: [number, number][] = [[ax, az], [bx, bz]];
    if (!passe(ax, az, bx, bz)) {
      const l = Math.hypot(bx - ax, bz - az) || 1;
      const [nx, nz] = [-(bz - az) / l, (bx - ax) / l];
      const [mx, mz] = [(ax + bx) / 2, (az + bz) / 2];
      cherche: for (const d of [80, 160, 280, 420, 600]) {
        for (const sg of unite(hacher(c.s.id, 227)) < 0.5 ? [1, -1] : [-1, 1]) {
          const px = mx + nx * d * sg;
          const pz = mz + nz * d * sg;
          if (profondeur(rel, px, pz) <= profond && passe(ax, az, px, pz) && passe(px, pz, bx, bz)) {
            points = [[ax, az], [px, pz], [bx, bz]];
            break cherche;
          }
        }
      }
    }
    const longueurs = [0];
    for (let k = 1; k < points.length; k++) {
      longueurs.push(longueurs[k - 1] + Math.hypot(points[k][0] - points[k - 1][0], points[k][1] - points[k - 1][1]));
    }
    c.trajet = { cle, points, longueurs };
    return c.trajet;
  }

  /**
   * Itinéraire dans la case (C1) : il va d'arrêt en arrêt à son allure, s'y arrête le temps de cueillir, de guetter ou de pêcher.
   * Chaque arrêt est posé une fois sur la terre ferme (gardé tant qu'il fait la même chose sur la même case).
   */
  private itineraire(c: Corps, m: Maniere, ax: number, az: number, secondes: number): [number, number, boolean] {
    const s = c.s;
    const u = (k: number) => unite(hacher(s.id, k, 223));
    const facon = 0.7 + 0.7 * u(1);
    const angle = u(2) * Math.PI * 2;
    const loin = entre(m.loin, u(3));
    const cx = ax + Math.cos(angle) * loin;
    const cz = az + Math.sin(angle) * loin;
    const rayon = Math.max(12, loin * 0.45);
    const pas = entre(m.pas, u(4));
    // Les petits vont moins vite ; l'endurant un peu plus.
    const allure = entre(m.allure, u(6)) * (0.85 + 0.3 * s.endurance) * (s.age < 10 ? 0.6 + 0.04 * s.age : 1);
    const duree = entre(m.arret, u(5)) * facon + pas / allure;
    const temps = secondes + u(7) * duree;
    const j = Math.floor(temps / duree);
    const dans = temps - j * duree;
    const cle = `${s.activite},${s.x},${s.y}`;
    if (c.arrets?.cle !== cle) c.arrets = { cle, j: [], x: [], z: [] };
    const arret = (k: number): [number, number] => {
      const a = c.arrets!;
      const i = a.j.indexOf(k);
      if (i >= 0) return [a.x[i], a.z[i]];
      const [bx, bz] = arretBrut(s.id, k, cx, cz, rayon, pas);
      const [x, z] = aPiedSec(this.relief, bx, bz, m.eau, s.id + k);
      a.j.push(k);
      a.x.push(x);
      a.z.push(z);
      if (a.j.length > 4) {
        a.j.shift();
        a.x.shift();
        a.z.shift();
      }
      return [x, z];
    };
    const [x0, z0] = arret(j - 1);
    const [x1, z1] = arret(j);
    const marche = Math.hypot(x1 - x0, z1 - z0) / allure;
    if (dans < marche) {
      const f = dans / marche;
      return [x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, true];
    }
    return [x1, z1, false];
  }

  /** 0.42.0 : l'assaut rejoué en ce moment et où il en est (s), et où gisent les morts des assauts déjà vus. */
  rejeu: Rejeu | null = null;
  tRejeu = 0;
  readonly gisants = new Map<number, { x: number; z: number; cap: number }>();

  /** Rôle d'un humain dans l'assaut rejoué ; un mort, hors rejeu, gît là où il est tombé. */
  private roleDe(c: Corps): Role | null {
    const r = this.rejeu?.role(c.s.id, this.tRejeu) ?? null;
    if (r) {
      if (r.mort) this.gisants.set(c.s.id, { x: r.x, z: r.z, cap: r.cap ?? c.cap });
      return r;
    }
    if (c.s.activite !== A.Mort) return null;
    const g = this.gisants.get(c.s.id);
    const x = g?.x ?? (Number.isNaN(c.x) ? this.relief.camp(c.s.x, c.s.y)[0] : c.x);
    const z = g?.z ?? (Number.isNaN(c.z) ? this.relief.camp(c.s.x, c.s.y)[1] : c.z);
    return { x, z, cap: g?.cap ?? c.cap, geste: "gisant", u: 1, arme: 0, court: false, mort: true, face: 0 };
  }

  /** Avance l'animation : `dt` secondes réelles, `secondes` instant estimé du monde. */
  animer(dt: number, secondes: number, camera: THREE.Vector3): void {
    const rel = this.relief;
    const cibles = new Map<Corps, [number, number, boolean]>();
    for (const c of this.corps.values()) {
      // Les figurants des essais jouent aussi l'assaut rejoué (essai des gestes de combat).
      c.role = this.roleDe(c);
      cibles.set(c, c.role ? [c.role.x, c.role.z, c.role.court] : c.s.figurant ? [c.s.figurant.x, c.s.figurant.z, false] : this.cible(c, secondes));
    }
    ecarter(cibles);
    for (const c of this.corps.values()) {
      const s = c.s;
      // Un dormeur (on lui parle en rêve), un bébé porté ou un nageur ne se retournent pas.
      const ecoute = s.id === this.interlocuteur && c.posture !== "couche" && c.posture !== "porte" && c.posture !== "nage";
      let [tx, tz] = cibles.get(c)!;
      if (Number.isNaN(c.x) || Math.hypot(tx - c.x, tz - c.z) > 400) {
        c.x = tx;
        c.z = tz;
      }
      // Celui à qui l'Entité parle s'arrête pour l'écouter.
      if (ecoute && !Number.isNaN(c.x)) [tx, tz] = [c.x, c.z];
      const dx = tx - c.x;
      const dz = tz - c.z;
      const d = Math.hypot(dx, dz);
      // En combat, on suit le rejeu de près : on court, on recule d'un pas, on tombe là où l'on est. Sinon, plus le monde va vite,
      // plus le corps colle à sa place (il traînerait loin derrière en film accéléré).
      const k = Math.min(1, dt * (c.role ? (c.role.court ? 5 : 4) : 1.5 * Math.sqrt(Math.max(1, this.facteur))));
      c.x += dx * k;
      c.z += dz * k;
      const pas = d * k;
      const v = dt > 0 ? pas / dt : 0;
      c.vitesse += (v - c.vitesse) * Math.min(1, dt * 4);
      // Il ne marche que si son corps se déplace vraiment (avec un peu d'hystérésis) : plus de marche sur place quand le monde
      // est arrêté, qu'il attend au bout de son chemin ou qu'il est bloqué.
      c.enMarche = c.enMarche ? c.vitesse > 0.12 : c.vitesse > 0.25;
      const voulu = ecoute ? Math.atan2(camera.x - c.x, camera.z - c.z) : d > 0.05 && c.vitesse > 0.15 ? Math.atan2(dx, dz) : null;
      const vouluCombat = c.role && c.role.cap !== null && !(c.role.court && d > 0.3) ? c.role.cap : null;
      const vise = vouluCombat ?? voulu;
      if (vise !== null) {
        const ecart = ((((vise - c.cap + Math.PI) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) - Math.PI;
        c.cap += ecart * Math.min(1, dt * (ecoute ? 2.2 : c.role ? 7 : 5));
      }
      c.ecoute += ((ecoute ? 1 : 0) - c.ecoute) * Math.min(1, dt * 3);
      c.taille = stature(s.age, s.femme, s.tailleEcart);
      // Les jambes avancent avec le sol parcouru (une foulée, deux pas, fait environ 0,8 fois la taille), en film accéléré quand le monde va vite (A1), jusqu'à CADENCE_MAX foulées par
      // seconde : au-delà, l'œil ne suit plus (quelques images par foulée).
      if (c.enMarche) c.phase += Math.min(dt * Math.PI * 2 * CADENCE_MAX, (pas * Math.PI * 2) / (0.82 * c.taille));
      const surf = rel.surface(c.x, c.z);
      // On marche dans l'eau jusqu'à la poitrine (D1) ; plus profond, on nage. Un enfant nage donc plus tôt qu'un adulte.
      const fond = Number.isNaN(surf.eau) ? 0 : surf.eau - surf.sol;
      const eau = fond > Math.max(0.35, 0.7 * c.taille);
      // Brasse de 0,5 à 0,8 cycle par seconde selon l'aisance (nageurs de loisir), nage du chien plus rapide et désordonnée.
      if (eau) c.phase += dt * accelere(this.facteur) * Math.PI * 2 * (s.nage >= SAIT_NAGER ? 0.5 + 0.3 * s.nage : 1.3 + 0.4 * unite(hacher(s.id, 229)));
      c.posture = c.role?.geste ? "combat" : c.role ? (c.enMarche ? "marche" : "debout") : s.figurant?.posture ?? posture(s, c.enMarche, eau && !(s.etat & 16));
      if (s.figurant && !c.role) {
        c.cap = s.figurant.cap;
        if (s.figurant.posture === "marche") c.phase += dt * 5;
      }
      c.y = eau ? surf.eau - (s.etat & 16 ? -0.3 : 0.15) : surf.sol;
      if (c.posture === "enlace" && c.partenaire) {
        const autre = this.corps.get(c.partenaire);
        if (autre && !Number.isNaN(autre.x) && Math.hypot(autre.x - c.x, autre.z - c.z) > 0.05) {
          const voulu = Math.atan2(autre.x - c.x, autre.z - c.z);
          const ecart = ((((voulu - c.cap + Math.PI) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) - Math.PI;
          c.cap += ecart * Math.min(1, dt * 3);
        }
      }
    }
    const dtGestes = dt * accelere(this.facteur);
    this.horloge += dtGestes;
    etreAvecLesAutres(this.corps.values(), this.feux, dtGestes, this.horloge);
    this.dessiner(camera, secondes);
  }

  private dessiner(camera: THREE.Vector3, secondes: number): void {
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    const racine = new THREE.Matrix4();
    const buste = new THREE.Matrix4();
    const m = new THREE.Matrix4();
    const t = new THREE.Matrix4();
    const r = new THREE.Matrix4();
    let n = 0;
    const poser = (partie: number, matrice: THREE.Matrix4, couleur: THREE.Color) => {
      this.parties[partie].setMatrixAt(n, matrice);
      this.parties[partie].setColorAt(n, couleur);
    };
    let np = 0;
    const pm = new THREE.Matrix4();
    for (const c of this.corps.values()) {
      if (n >= NOMBRE_MAX) break;
      const s = c.s;
      if (this.detailles.has(s.id) || Number.isNaN(c.x)) continue;
      const { x: px, y: py, z: pz, cap } = this.placement(c);
      const h = c.taille;
      const loin = Math.hypot(px - camera.x, pz - camera.z);
      if (loin > this.porteeSilhouettes) {
        // Point de vie : assez gros pour se voir de loin (environ deux pixels).
        const r = Math.max(0.5, loin * 0.0015);
        this.points.setMatrixAt(np++, pm.makeScale(r, r, r).setPosition(px - this.origine.x, py + 1 + r, pz - this.origine.z));
        continue;
      }
      racine.makeTranslation(px - this.origine.x, py, pz - this.origine.z);
      racine.multiply(r.makeRotationY(cap));
      let flexion = 0;
      let jambes = [0, 0];
      let bras = [0.08, 0.08];
      const respire = Math.sin(secondes / 3 + s.id) * 0.03;
      switch (c.posture) {
        case "marche": {
          const a = Math.sin(c.phase) * 0.5;
          jambes = [a, -a];
          bras = [-a * 0.8, a * 0.8];
          flexion = s.etat & 4 ? 0.1 : 0.04;
          break;
        }
        case "assis":
        case "travail":
          racine.multiply(t.makeTranslation(0, -0.42 * h, 0));
          jambes = [-1.45, -1.35];
          bras = c.posture === "travail" ? [-0.9 + Math.sin(secondes * 2 + s.id) * 0.25, -0.7] : [-0.3, -0.25];
          flexion = 0.15 + respire;
          break;
        case "couche":
        case "enlace":
        case "porte":
          racine.multiply(t.makeTranslation(0, 0.09 * h, 0.45 * h)).multiply(r.makeRotationX(-Math.PI / 2));
          bras = [0.15, 0.2];
          break;
        case "accroupi":
          racine.multiply(t.makeTranslation(0, -0.27 * h, 0));
          jambes = [-1.1, -0.8];
          bras = [-1.1 + Math.sin(secondes * 1.5 + s.id) * 0.2, -0.9];
          flexion = 0.6;
          break;
        case "combat": {
          const g = c.role?.geste;
          if (g === "gisant" || g === "chute") {
            racine.multiply(t.makeTranslation(0, 0.09 * h, 0.45 * h)).multiply(r.makeRotationX(-Math.PI / 2));
            bras = [0.4, 0.5];
          } else if (g === "cache" || g === "blesse" || g === "reveil") {
            racine.multiply(t.makeTranslation(0, -0.27 * h, 0));
            jambes = [-1.1, -0.8];
            bras = [-0.9, -0.8];
            flexion = 0.6;
          } else {
            const coup = g === "frappe" || g === "estoc" || g === "lancer" ? Math.sin((c.role?.u ?? 0) * Math.PI * 2) : 0;
            jambes = [0.35, -0.15];
            bras = [-1.1 - 1.2 * coup, -0.9];
            flexion = 0.15;
          }
          break;
        }
        case "nage": {
          racine.multiply(t.makeTranslation(0, 0.05, -0.5 * h)).multiply(r.makeRotationX(Math.PI / 2));
          const a = Math.sin(c.phase * 1.5) * 0.3;
          jambes = [a, -a];
          bras = [-2.6 + a, -2.6 - a];
          break;
        }
        default:
          bras = [0.08 + respire, 0.08 - respire];
      }
      const tetePlus = 1 + 0.55 * Math.max(0, 1 - s.age / 12);
      racine.multiply(t.makeScale(h, h, h));
      // Buste : plié vers l'avant à partir des hanches.
      buste.copy(racine).multiply(t.makeTranslation(0, 0.5, 0)).multiply(r.makeRotationX(flexion)).multiply(t.makeTranslation(0, -0.5, 0));
      const large = s.femme ? 1.0 : 1.12;
      if (c.enduitVu !== s.enduit || !c.peauVue) {
        c.peauVue = teindre(c.peau, s.enduit);
        c.enduitVu = s.enduit;
      }
      const peau = c.peauVue;
      poser(0, m.copy(buste).multiply(t.makeScale(large, 1, 0.75)), peau);
      poser(1, m.copy(buste).multiply(t.makeTranslation(0, 0.935, 0)).multiply(r.makeScale(tetePlus, tetePlus, tetePlus)), peau);
      poser(2, m.copy(buste).multiply(t.makeTranslation(0, 0.94, -0.004)).multiply(r.makeScale(tetePlus, tetePlus, tetePlus)), c.cheveux);
      poser(3, m.copy(racine).multiply(t.makeTranslation(0.05, 0.5, 0)).multiply(r.makeRotationX(jambes[0])), peau);
      poser(4, m.copy(racine).multiply(t.makeTranslation(-0.05, 0.5, 0)).multiply(r.makeRotationX(jambes[1])), peau);
      poser(5, m.copy(buste).multiply(t.makeTranslation(0.11 * large, 0.84, 0)).multiply(r.makeRotationX(bras[0])).multiply(t.makeRotationZ(0.06)), peau);
      poser(6, m.copy(buste).multiply(t.makeTranslation(-0.11 * large, 0.84, 0)).multiply(r.makeRotationX(bras[1])).multiply(t.makeRotationZ(-0.06)), peau);
      n++;
    }
    this.points.count = np;
    this.points.position.copy(this.origine);
    this.points.instanceMatrix.needsUpdate = true;
    for (const p of this.parties) {
      p.count = n;
      p.position.copy(this.origine);
      p.instanceMatrix.needsUpdate = true;
      if (p.instanceColor) p.instanceColor.needsUpdate = true;
    }
  }

  /** Où est posé un humain : à sa place, ou contre sa mère s'il est porté. */
  placement(c: Corps): { x: number; y: number; z: number; cap: number } {
    if (c.s.coucher === COUCHER.ACote || c.s.coucher === COUCHER.AEcart) {
      // 0.45.0 : à côté d'elle, à portée de main devant sa poitrine ; à l'écart, à quelques pas, seul.
      const mere = this.corps.get(porteurDe(c.s));
      if (mere && !Number.isNaN(mere.x)) {
        if (c.s.coucher === COUCHER.ACote) {
          const d = 0.55 * mere.taille;
          return { cap: mere.cap + Math.PI / 2, x: mere.x + Math.sin(mere.cap) * d, z: mere.z + Math.cos(mere.cap) * d, y: mere.y };
        }
        const angle = unite(hacher(c.s.id, 233)) * Math.PI * 2;
        const r = 2.5 + 1.5 * unite(hacher(c.s.id, 239));
        const x = mere.x + Math.cos(angle) * r;
        const z = mere.z + Math.sin(angle) * r;
        return { cap: angle, x, z, y: this.relief.hauteur(x, z) };
      }
    }
    if (c.posture === "porte") {
      const mere = this.corps.get(porteurDe(c.s));
      if (mere && !Number.isNaN(mere.x)) {
        const cap = mere.cap;
        return {
          cap,
          x: mere.x + Math.sin(cap) * 0.16 * mere.taille + Math.cos(cap) * 0.1 * mere.taille,
          z: mere.z + Math.cos(cap) * 0.16 * mere.taille - Math.sin(cap) * 0.1 * mere.taille,
          y: mere.y + mere.taille * (mere.posture === "couche" ? 0.12 : mere.posture === "assis" || mere.posture === "travail" ? 0.4 : 0.6),
        };
      }
    }
    return { x: c.x, y: c.y, z: c.z, cap: c.cap };
  }

  /** Tous les humains affichés, avec leur état d'animation. */
  get liste(): IterableIterator<Corps> {
    return this.corps.values();
  }

  /** Position (tête) d'un humain affiché, ou null. */
  position(id: number): THREE.Vector3 | null {
    const c = this.corps.get(id);
    return c && !Number.isNaN(c.x) ? new THREE.Vector3(c.x, c.y + c.taille * 0.9, c.z) : null;
  }

  /** Humains affichés : id, position de la tête et ce qu'on sait d'eux. */
  *tous(): Generator<{ id: number; tete: THREE.Vector3; pied: THREE.Vector3; s: HumainScene; cap: number; taille: number }> {
    for (const c of this.corps.values()) {
      if (Number.isNaN(c.x)) continue;
      const pl = this.placement(c);
      const haut = aTerre(c) || c.posture === "nage" || c.posture === "porte" ? 0.2 : c.posture === "assis" || c.posture === "travail" ? 0.5 : c.posture === "accroupi" ? 0.6 : 0.9;
      // Allongé, le corps s'étend le long de son cap : « pied » est alors l'autre bout du corps.
      const couche = aTerre(c) || c.posture === "nage";
      const pied = couche
        ? new THREE.Vector3(pl.x - Math.sin(pl.cap) * c.taille * 0.8, pl.y + 0.15, pl.z - Math.cos(pl.cap) * c.taille * 0.8)
        : new THREE.Vector3(pl.x, pl.y + 0.05, pl.z);
      yield { id: c.s.id, tete: new THREE.Vector3(pl.x, pl.y + c.taille * haut, pl.z), pied, s: c.s, cap: pl.cap, taille: c.taille };
    }
  }

  /** Où est un humain affiché (tête), son cap et s'il marche ; null s'il n'est pas dans la scène. */
  etat(id: number): { tete: THREE.Vector3; cap: number; enMarche: boolean; taille: number } | null {
    const c = this.corps.get(id);
    if (!c || Number.isNaN(c.x)) return null;
    const pl = this.placement(c);
    const haut = aTerre(c) || c.posture === "nage" || c.posture === "porte" ? 0.25 : c.posture === "assis" || c.posture === "travail" ? 0.5 : c.posture === "accroupi" ? 0.6 : 0.85;
    return { tete: new THREE.Vector3(pl.x, pl.y + c.taille * haut, pl.z), cap: pl.cap, enMarche: c.enMarche, taille: c.taille };
  }

  get nombre(): number {
    return this.corps.size;
  }
}

/** Un feu de camp : flammes, bûches, fumée, et la lumière des quatre plus proches la nuit. */
export class Feux3D {
  readonly groupe = new THREE.Group();
  private readonly flammes: THREE.InstancedMesh;
  private readonly buches: THREE.InstancedMesh;
  private readonly fumee: THREE.InstancedMesh;
  /** Pierres du foyer (0.41.1). */
  private readonly pierres: THREE.InstancedMesh;
  private readonly lumieres: THREE.PointLight[] = [];
  private feux: { x: number; y: number; z: number; couve: boolean; graine: number }[] = [];

  constructor(readonly relief: Relief) {
    this.flammes = new THREE.InstancedMesh(new THREE.ConeGeometry(0.28, 0.9, 7).translate(0, 0.45, 0),
      new THREE.MeshBasicMaterial({ color: 0xffa040, transparent: true, opacity: 0.85, depthWrite: false, fog: false }), 400);
    this.buches = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.06, 0.07, 0.9, 6).rotateZ(Math.PI / 2).translate(0, 0.07, 0),
      new THREE.MeshStandardMaterial({ color: 0x3a2a1c, roughness: 1 }), 1200);
    this.fumee = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(1, 1),
      new THREE.MeshStandardMaterial({ color: 0x9a9a9a, transparent: true, opacity: 0.18, depthWrite: false, roughness: 1 }), 3200);
    this.pierres = new THREE.InstancedMesh(new THREE.DodecahedronGeometry(0.16, 0).scale(1, 0.6, 1),
      new THREE.MeshStandardMaterial({ color: 0x6d6a64, roughness: 1, flatShading: true }), 4000);
    for (const m of [this.flammes, this.buches, this.fumee, this.pierres]) {
      m.count = 0;
      m.frustumCulled = false;
      this.groupe.add(m);
    }
    for (let k = 0; k < 4; k++) {
      const l = new THREE.PointLight(0xff9a50, 0, 40, 1.6);
      this.lumieres.push(l);
      this.groupe.add(l);
    }
  }

  definir(feux: { x: number; y: number; couve: boolean }[]): void {
    const vus = new Set<string>();
    this.feux = [];
    for (const f of feux) {
      const cle = `${f.x},${f.y}`;
      if (vus.has(cle)) continue;
      vus.add(cle);
      const [x, z] = this.relief.camp(f.x, f.y);
      this.feux.push({ x, y: this.relief.hauteur(x, z), z, couve: f.couve, graine: hacher(f.x, f.y, 5) });
    }
  }

  /**
   * `exposition` (0.41.3) : un feu brille toujours autant ; il paraît vif la nuit, quand l'œil s'est adapté, et pâle en plein jour.
   * Les flammes dépassent alors le blanc et donnent un halo.
   */
  animer(temps: number, camera: THREE.Vector3, nuit: number, exposition = 0.6): void {
    (this.flammes.material as THREE.MeshBasicMaterial).color.setHex(0xffa040).multiplyScalar(1.2 / Math.pow(Math.max(exposition, 0.01), 0.85));
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const p = new THREE.Vector3();
    const e = new THREE.Vector3();
    const proches = this.feux.map((f) => ({ f, d: Math.hypot(f.x - camera.x, f.z - camera.z) })).filter((a) => a.d < 6000).sort((a, b) => a.d - b.d);
    let nf = 0;
    let nb = 0;
    let ns = 0;
    let np = 0;
    for (const { f, d } of proches) {
      if (d < 800) {
        for (let k = 0; k < 10 && np < 4000; k++) {
          const a = (k / 10) * Math.PI * 2 + unite(hacher(f.graine, k)) * 0.3;
          const r = 0.62 + unite(hacher(f.graine, k, 1)) * 0.08;
          const t = 0.8 + unite(hacher(f.graine, k, 2)) * 0.5;
          q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), unite(hacher(f.graine, k, 3)) * 6.3);
          m.compose(p.set(f.x + Math.cos(a) * r, f.y + 0.03, f.z + Math.sin(a) * r), q, e.set(t, t, t));
          this.pierres.setMatrixAt(np++, m);
        }
      }
      if (d < 1500 && !f.couve && nf < 400) {
        for (let k = 0; k < 1; k++) {
          const flicker = 0.8 + 0.25 * Math.sin(temps * 9 + f.graine) + 0.15 * Math.sin(temps * 23 + f.graine * 0.3);
          m.compose(p.set(f.x, f.y, f.z), q.identity(), e.set(1, flicker, 1));
          this.flammes.setMatrixAt(nf++, m);
        }
      }
      if (d < 1500) {
        for (let k = 0; k < 3 && nb < 1200; k++) {
          q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), (k * Math.PI) / 3 + f.graine);
          m.compose(p.set(f.x, f.y, f.z), q, e.set(1, 1, 1));
          this.buches.setMatrixAt(nb++, m);
        }
      }
      for (let k = 0; k < 8 && ns < 3200; k++) {
        const cycle = (temps / 14 + k / 8 + unite(f.graine)) % 1;
        const montee = cycle * (f.couve ? 14 : 30);
        const taille = (f.couve ? 0.4 : 0.6) + cycle * (f.couve ? 2 : 4);
        m.compose(p.set(f.x + Math.sin(cycle * 5 + k) * cycle * 3 + cycle * 6, f.y + 1 + montee, f.z + cycle * 2), q.identity(), e.set(taille, taille, taille));
        this.fumee.setMatrixAt(ns++, m);
      }
    }
    this.flammes.count = nf;
    this.buches.count = nb;
    this.fumee.count = ns;
    this.pierres.count = np;
    for (const x of [this.flammes, this.buches, this.fumee, this.pierres]) x.instanceMatrix.needsUpdate = true;
    const flambants = proches.filter((a) => !a.f.couve && a.d < 400);
    this.lumieres.forEach((l, k) => {
      const a = flambants[k];
      if (!a) {
        l.intensity = 0;
        return;
      }
      l.position.set(a.f.x, a.f.y + 1, a.f.z);
      l.intensity = (0.2 + 0.7 * nuit) * (0.85 + 0.15 * Math.sin(temps * 11 + a.f.graine));
    });
  }
}
