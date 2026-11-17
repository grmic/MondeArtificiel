import * as THREE from "three/webgpu";
import { hacher, lisser, unite } from "./bruit";
import { DOIGTS, PO, POSE_OS, type Corps, type NomPose } from "./corps";
import { ACCELERATION_MAX, SAIT_NAGER } from "./foule";

/**
 * Postures vivantes (0.41.7, propositions A1 B1 C1 D1 E1 G1 validées par zem le 15/11) : chaque humain proche est posé d'après
 * des poses écrites sur les amplitudes articulaires et la marche mesurées (Winter, « Biomechanics and Motor Control of Human
 * Movement » ; Perry, « Gait Analysis »), en plusieurs variantes que chacun choisit selon son âge, son sexe, sa fatigue et un goût
 * propre (les façons de s'asseoir par terre relevées par Hewes, 1955, « World distribution of certain postural habits »).
 *
 * - Pose « canonique » : rotation locale de chaque os comme si le corps avait les bras le long du corps, paumes vers les cuisses.
 * - Passages : on s'assoit, se lève, s'allonge en passant par des poses intermédiaires, au lieu de sauter de l'une à l'autre.
 * - Appuis (C1) : le corps descend jusqu'à ce que ses appuis touchent le sol (pieds, fesses, genoux, flanc…), les pieds posés
 *   suivent la pente et les mains se posent (sol, genou, enfant) par cinématique inverse à deux os.
 * - Vivant (E1) : respiration au rythme de l'âge et de l'effort, poids qui passe d'une jambe à l'autre, petits mouvements
 *   propres à chacun, gestes d'attente, regard vers les gens proches.
 *
 * Tout est calculé à l'affichage, pour les corps détaillés seulement : la simulation ne change pas.
 */

const NB = POSE_OS.length;
const DOIGTS_PAR_MAIN = 16; // 15 phalanges + écartement

/** Où une main ou un pied veut aller : un point du corps (os et décalage, repère des poses) ou du sol (x, z du corps). */
interface Cible {
  poids: number;
  /** Os des poses auquel le point est attaché (−1 : point fixe du corps). */
  os: number;
  x: number;
  y: number;
  z: number;
  /** Le point est posé sur le sol (y en plus de la hauteur du sol). */
  sol: boolean;
}

/** Appuis qui décident de la hauteur : masques de familles (voir corps.ts). */
export const APPUI_PIEDS = 1;
export const APPUI_FESSES = 2;
export const APPUI_GENOUX = 4;
export const APPUI_CORPS = 8;

/** Une pose : rotations canoniques, décalage du bassin, mains et pieds visés, doigts. */
export class Pose {
  readonly q = new Float32Array(4 * NB);
  readonly racine = new THREE.Vector3();
  readonly mains: Cible[][] = [[], []];
  readonly pieds: Cible[][] = [[], []];
  /** Par main : flexion des 15 phalanges (pouce, index… auriculaire ; 3 chacun), puis écartement. */
  readonly doigts = new Float32Array(2 * DOIGTS_PAR_MAIN);
  /** Familles d'appuis qui portent le corps ; pieds posés au sol (0 à 1, G puis D). */
  appuis = APPUI_PIEDS;
  plantes: [number, number] = [1, 1];
  /** Le corps n'a pas de sol (porté, dans l'eau). */
  libre = false;

  zero(): this {
    for (let i = 0; i < NB; i++) {
      this.q[4 * i] = this.q[4 * i + 1] = this.q[4 * i + 2] = 0;
      this.q[4 * i + 3] = 1;
    }
    this.racine.set(0, 0, 0);
    this.mains[0].length = this.mains[1].length = this.pieds[0].length = this.pieds[1].length = 0;
    this.doigts.fill(0);
    this.appuis = APPUI_PIEDS;
    this.plantes = [1, 1];
    this.libre = false;
    return this;
  }

  copier(p: Pose): this {
    this.q.set(p.q);
    this.racine.copy(p.racine);
    for (let k = 0; k < 2; k++) {
      this.mains[k] = p.mains[k].map((c) => ({ ...c }));
      this.pieds[k] = p.pieds[k].map((c) => ({ ...c }));
    }
    this.doigts.set(p.doigts);
    this.appuis = p.appuis;
    this.plantes = [p.plantes[0], p.plantes[1]];
    this.libre = p.libre;
    return this;
  }

  /** Mélange `a` et `b` (t de 0 à 1) ; `this` peut être `a`. */
  melanger(a: Pose, b: Pose, t: number): this {
    const u = 1 - t;
    for (let i = 0; i < NB; i++) {
      const j = 4 * i;
      let bx = b.q[j], by = b.q[j + 1], bz = b.q[j + 2], bw = b.q[j + 3];
      if (a.q[j] * bx + a.q[j + 1] * by + a.q[j + 2] * bz + a.q[j + 3] * bw < 0) {
        bx = -bx; by = -by; bz = -bz; bw = -bw;
      }
      const x = a.q[j] * u + bx * t, y = a.q[j + 1] * u + by * t, z = a.q[j + 2] * u + bz * t, w = a.q[j + 3] * u + bw * t;
      const n = 1 / (Math.hypot(x, y, z, w) || 1);
      this.q[j] = x * n;
      this.q[j + 1] = y * n;
      this.q[j + 2] = z * n;
      this.q[j + 3] = w * n;
    }
    this.racine.lerpVectors(a.racine, b.racine, t);
    for (let k = 0; k < 2; k++) {
      this.mains[k] = melangerCibles(a.mains[k], b.mains[k], t);
      this.pieds[k] = melangerCibles(a.pieds[k], b.pieds[k], t);
    }
    for (let i = 0; i < this.doigts.length; i++) this.doigts[i] = a.doigts[i] * u + b.doigts[i] * t;
    this.appuis = t < 0.5 ? a.appuis | (t > 0.15 ? b.appuis : 0) : b.appuis | (t < 0.85 ? a.appuis : 0);
    this.plantes = [a.plantes[0] * u + b.plantes[0] * t, a.plantes[1] * u + b.plantes[1] * t];
    this.libre = t < 0.5 ? a.libre : b.libre;
    return this;
  }

  // ——— Écriture des articulations (angles en radians, côté G = +1, D = −1) ———

  private euler(i: number, x: number, y: number, z: number, ordre: THREE.EulerOrder): void {
    E.set(x, y, z, ordre);
    Q.setFromEuler(E);
    Q.toArray(this.q, 4 * i);
  }

  /** Ajoute une petite rotation après celle de l'os (mouvements qui s'ajoutent à la pose). */
  ajouter(nom: NomPose, x: number, y: number, z: number): void {
    const i = PO[nom];
    Q2.fromArray(this.q, 4 * i);
    E.set(x, y, z, "YXZ");
    Q.setFromEuler(E);
    Q2.multiply(Q).toArray(this.q, 4 * i);
  }

  /** Bassin (tout le corps) : flexion vers l'avant x, rotation y, bascule z. */
  bassin(x: number, y = 0, z = 0): void {
    this.euler(PO.bassin, x, y, z, "YXZ");
  }

  /** Bassin couché : `roule` sur le flanc (+ : sur le côté droit), `bascule` sur le dos (−π/2) ou le ventre (+π/2). */
  coucher(bascule: number, roule: number, lacet = 0): void {
    E.set(0, lacet, 0, "XYZ");
    Q.setFromEuler(E);
    Q2.setFromAxisAngle(AX, bascule);
    Q.multiply(Q2);
    Q2.setFromAxisAngle(AZ, roule);
    Q.multiply(Q2);
    Q.toArray(this.q, 4 * PO.bassin);
  }

  /** Colonne : flexion (avant +), rotation (vers la gauche +), inclinaison (vers la droite +). */
  colonne(nom: "ventre" | "poitrine" | "cou" | "tete", flexion: number, rotation = 0, inclinaison = 0): void {
    this.euler(PO[nom], flexion, rotation, inclinaison, "YXZ");
  }

  hanche(c: Cote, flexion: number, abduction = 0, rotationExterne = 0): void {
    const s = SIGNE[c];
    this.euler(PO[`cuisse${c}`], -flexion, s * rotationExterne, s * abduction, "XZY");
  }

  genou(c: Cote, flexion: number): void {
    this.euler(PO[`jambe${c}`], flexion, 0, 0, "XYZ");
  }

  /** Cheville : flexion dorsale (+ : pointe vers le haut), inversion (+ : plante vers l'intérieur). */
  cheville(c: Cote, dorsale: number, inversion = 0): void {
    this.euler(PO[`pied${c}`], -dorsale, 0, -SIGNE[c] * inversion, "XZY");
  }

  orteils(c: Cote, extension: number): void {
    this.euler(PO[`orteils${c}`], -extension, 0, 0, "XYZ");
  }

  clavicule(c: Cote, elevation: number, avant = 0): void {
    const s = SIGNE[c];
    this.euler(PO[`clavicule${c}`], 0, -s * avant, s * elevation, "XYZ");
  }

  /** Épaule : flexion (bras vers l'avant), abduction (vers le côté), rotation externe. */
  epaule(c: Cote, flexion: number, abduction = 0, rotationExterne = 0): void {
    const s = SIGNE[c];
    this.euler(PO[`bras${c}`], -flexion, s * rotationExterne, s * abduction, "XZY");
  }

  /** Coude : flexion, supination (+ : paume vers le haut quand l'avant-bras est devant). */
  coude(c: Cote, flexion: number, supination = 0): void {
    this.euler(PO[`avantBras${c}`], -flexion, SIGNE[c] * supination, 0, "XYZ");
  }

  /** Poignet : flexion vers la paume, déviation vers le pouce. */
  poignet(c: Cote, flexion: number, deviation = 0): void {
    this.euler(PO[`main${c}`], -deviation, 0, -SIGNE[c] * flexion, "ZXY");
  }

  /** Forme de la main (squelette fin) : `plie` 0 (ouverte) à 1,5 (poing), `pouce`, `ecart`, avec la cascade naturelle. */
  main(c: Cote, plie: number, pouce = plie * 0.6, ecart = 0.04, index = plie): void {
    const o = (c === "G" ? 0 : 1) * DOIGTS_PAR_MAIN;
    const d = this.doigts;
    d[o] = 0.15 + pouce * 0.5;
    d[o + 1] = pouce * 0.55;
    d[o + 2] = pouce * 0.6;
    for (let f = 1; f < 5; f++) {
      const p = f === 1 ? index : plie * (1 + 0.1 * (f - 1));
      d[o + 3 * f] = p * 0.85;
      d[o + 3 * f + 1] = p * 1.05;
      d[o + 3 * f + 2] = p * 0.65;
    }
    d[o + 15] = ecart;
  }

  /** Une main vise un point (`os` : −1 pour un point du corps fixe), avec un poids. */
  viserMain(c: Cote, os: number, x: number, y: number, z: number, poids = 1, sol = false): void {
    this.mains[c === "G" ? 0 : 1].push({ poids, os, x, y, z, sol });
  }

  /** Un pied se pose à un point du sol (x, z du corps). */
  poserPied(c: Cote, x: number, z: number, poids = 1): void {
    this.pieds[c === "G" ? 0 : 1].push({ poids, os: -1, x, y: 0, z, sol: true });
  }
}

type Cote = "G" | "D";
const SIGNE: Record<Cote, number> = { G: 1, D: -1 };
const COTES: Cote[] = ["G", "D"];
const E = new THREE.Euler();
const Q = new THREE.Quaternion();
const Q2 = new THREE.Quaternion();
const AX = new THREE.Vector3(1, 0, 0);
const AZ = new THREE.Vector3(0, 0, 1);

function melangerCibles(a: Cible[], b: Cible[], t: number): Cible[] {
  const l: Cible[] = [];
  for (const c of a) if (c.poids * (1 - t) > 0.01) l.push({ ...c, poids: c.poids * (1 - t) });
  for (const c of b) if (c.poids * t > 0.01) l.push({ ...c, poids: c.poids * t });
  if (l.length > 4) l.sort((x, y) => y.poids - x.poids).length = 4;
  return l;
}

/** Ce que l'animation sait d'un humain à cet instant. */
export interface Situation {
  posture: string;
  id: number;
  age: number;
  femme: boolean;
  /** Dos voûté des vieux (0 à 1). */
  voute: number;
  boite: boolean;
  activite: number;
  /** Cycle de marche (radians, deux pas par tour) et vitesse (m/s). */
  phase: number;
  vitesse: number;
  /** Endurance (0-1) et réserves (0-100) : fatigue et faim. */
  endurance: number;
  reserves: number;
  /** Âge de l'enfant porté (une mère), ou null. */
  enfant: number | null;
  /** Porté : posture de sa mère (pour savoir comment il est tenu). */
  porteur: string | null;
  /** Elle donne le sein à l'enfant qu'elle porte (0.41.8). */
  allaite?: boolean;
  /** Dort (yeux fermés, respiration lente, pas de gestes). */
  dort: boolean;
  /** Tourné vers l'Entité qui lui parle (0 à 1). */
  ecoute: number;
  /** F1 (0.41.8) : froid ressenti, faim, fatigue, peur (0 à 1), malade (0 ou 1, plus fort quand la santé est basse), enceinte. */
  froid: number;
  faim: number;
  fatigue: number;
  peur: number;
  malade: number;
  enceinte: boolean;
  /** Humeur du flux Scène : 0 abattu, 1 triste, 2 serein, 3 content, 4 joyeux, 5 en deuil, 6 furieux, 7 déprimé. */
  humeur: number;
  /** Névrosisme et extraversion (0 à 1). */
  nevrosisme: number;
  extraversion: number;
  /** H1 : il parle à quelqu'un en face de lui (0 à 1) ; l'autre écoute. */
  parle: number;
  /** H1 : il est en conversation (qu'il parle ou écoute). */
  converse: boolean;
  /** 0.45.0 : endormie, un tout-petit contre elle (couchée en cercle autour de lui). */
  berceau?: boolean;
  /** Aisance dans l'eau (0 à 1) : brasse ou nage du chien (D1). */
  nage?: number;
  /** Enlacé avec son partenaire : +1 ou −1 (de quel côté il se couche, pour que les deux têtes soient du même côté), 0 sinon. */
  enlace: number;
  /** 0.42.0 : geste de combat du rejeu d'un assaut (voir combat.ts), où il en est (0 à 1) et l'arme en main ; mort : plus de souffle. */
  combat?: { geste: string; u: number; arme: number; mort: boolean } | null;
  /** 0.45.10 : l'outil ou l'arme taillée qu'il tient hors combat (debout, en marche ; 0 : mains libres). */
  outil?: number;
}

/** Abattement (F1) : fatigue, faim, tristesse, maladie ; 0 à 1. */
function abattement(s: Situation): number {
  const triste = s.humeur === 0 || s.humeur === 7 ? 1 : s.humeur === 5 ? 0.8 : s.humeur === 1 ? 0.5 : 0;
  return Math.min(1, Math.max(lisser(0.35, 0.9, s.fatigue), lisser(0.3, 0.9, s.faim) * 0.8, triste, s.malade * 0.7));
}

/** Corps ouvert (F1) : joie et assurance, sans peur ni froid ; 0 à 1. */
function ouverture(s: Situation): number {
  const joie = s.humeur === 4 ? 1 : s.humeur === 3 ? 0.5 : 0;
  return joie * (1 - s.peur) * (1 - s.froid) * (0.6 + 0.4 * s.extraversion);
}

/** Repli (F1) : la peur et le froid serrent les bras et montent les épaules ; l'anxieux s'y met plus vite. */
function repli(s: Situation): number {
  return Math.min(1, Math.max(s.peur * (0.7 + 0.6 * s.nevrosisme), s.froid));
}

/** Hauteur du sol sous le corps, en repère du corps (taille 1) : y = ax·x + az·z. */
export interface SolLocal {
  ax: number;
  az: number;
}

/** Façons de faire, par posture (G1), avec leur poids selon la personne. */
type Variante = { nom: string; poids: (s: Situation) => number };
const ASSIS: Variante[] = [
  { nom: "tailleur", poids: (s) => (s.femme ? 0.8 : 1.3) * (1 - 0.5 * lisser(55, 80, s.age)) },
  { nom: "genoux", poids: (s) => 1.1 + (s.age < 14 ? 0.5 : 0) + 4 * repli(s) },
  { nom: "cote", poids: (s) => (s.femme ? 1.2 : 0.35) * lisser(8, 16, s.age) },
  { nom: "talons", poids: (s) => 0.6 * (1 - 0.7 * lisser(50, 75, s.age)) },
  { nom: "allonge", poids: (s) => ((s.femme ? 1.1 : 0.6) + 0.6 * lisser(50, 75, s.age)) * (1 - 0.85 * s.froid) * (1 - 0.6 * s.peur) },
  { nom: "uneJambe", poids: () => 0.9 },
];
const ACCROUPI: Variante[] = [
  { nom: "plat", poids: (s) => 1.4 * (1 - 0.6 * lisser(50, 80, s.age)) },
  { nom: "pointes", poids: () => 0.35 },
  { nom: "unGenou", poids: (s) => 0.7 + 0.8 * lisser(45, 75, s.age) },
];
const COUCHE: Variante[] = [
  // Endormis, on dort surtout sur le côté (environ 55 %), puis sur le dos, rarement sur le ventre (Skarpsno et al. 2017).
  { nom: "cote", poids: () => 1.6 },
  { nom: "dos", poids: (s) => (0.8 + (s.age < 2 ? 1 : 0)) * (1 - 0.75 * s.froid) },
  { nom: "fusil", poids: (s) => 0.6 + (s.age < 12 ? 0.6 : 0) + 4 * s.froid + 1.5 * s.malade + 1.5 * s.peur },
  { nom: "ventre", poids: (s) => (s.age < 1 || s.enceinte ? 0 : 0.2) * (1 - s.froid) },
];
const BRAS_DEBOUT: Variante[] = [
  { nom: "ballants", poids: (s) => 3 * (1 - repli(s)) ** 2 * (1 - 0.8 * s.malade) },
  { nom: "devant", poids: (s) => (s.age < 6 ? 0.1 : 0.55) * (1 + s.peur) * (1 - 0.8 * s.froid) * (1 - 0.6 * s.malade) },
  { nom: "dos", poids: (s) => (s.age < 6 ? 0.1 : 0.5) * (0.6 + 0.8 * (1 - s.nevrosisme)) * (1 - s.froid) },
  { nom: "hanche", poids: (s) => (s.age < 4 ? 0.1 : 0.45) * (0.5 + s.extraversion) * (1 - s.froid) },
  // Bras serrés contre soi : le froid et la peur (le petit enfant aussi se serre).
  { nom: "croises", poids: (s) => (s.age < 6 ? 0.05 : 0.35) + 5 * s.froid + 2 * s.peur * (0.5 + s.nevrosisme) },
  // Une main sur le ventre : le malade, la femme enceinte.
  { nom: "ventre", poids: (s) => (s.age < 8 ? 0 : 5 * s.malade + (s.enceinte ? 1.1 : 0)) },
];

/** Tire une variante : goût propre (hachage de l'id) × poids, renouvelé à chaque « époque ». */
function choisir(l: Variante[], s: Situation, epoque: number, sel: number): string {
  let total = 0;
  const p = l.map((v, k) => {
    const gout = 0.35 + 1.3 * unite(hacher(s.id, 401 + sel, k));
    const w = Math.max(0, v.poids(s)) * gout;
    total += w;
    return w;
  });
  let r = unite(hacher(s.id, epoque, 409 + sel)) * total;
  for (let k = 0; k < l.length; k++) {
    r -= p[k];
    if (r <= 0) return l[k].nom;
  }
  return l[l.length - 1].nom;
}

/** Les familles de postures, pour les passages. */
function famille(cle: string): "debout" | "sol" | "couche" | "eau" | "porte" | "quatre" {
  if (cle === "debout" || cle === "marche") return "debout";
  if (cle.startsWith("combat.")) {
    const g = cle.slice(7);
    return g === "chute" || g === "gisant" ? "couche" : g === "cache" || g === "blesse" || g === "reveil" ? "sol" : "debout";
  }
  if (cle.startsWith("couche")) return "couche";
  if (cle === "nage") return "eau";
  if (cle.startsWith("porte")) return "porte";
  if (cle === "quatre") return "quatre";
  return "sol";
}

/** Bruit lisse de −1 à 1 (somme de sinus incommensurables), propre à chacun. */
function onde(t: number, g: number, f: number): number {
  return 0.55 * Math.sin(t * f + g) + 0.3 * Math.sin(t * f * 2.17 + g * 1.7) + 0.15 * Math.sin(t * f * 4.31 + g * 2.9);
}

/** Bosse d'une part de cycle (0-1) centrée sur `c`, de largeur `l` (cycle refermé). */
/** Valeur sur un cycle (u de 0 à 1) donnée par des clés [instant, valeur] croissantes, passages adoucis entre deux clés. */
function cles(u: number, k: [number, number][]): number {
  for (let i = 1; i < k.length; i++) {
    if (u <= k[i][0]) {
      const [t0, v0] = k[i - 1];
      const [t1, v1] = k[i];
      const x = t1 > t0 ? (u - t0) / (t1 - t0) : 1;
      return v0 + (v1 - v0) * x * x * (3 - 2 * x);
    }
  }
  return k[k.length - 1][1];
}

function bosse(p: number, c: number, l: number): number {
  let d = p - c;
  d -= Math.round(d);
  return Math.exp(-0.5 * (d / l) * (d / l));
}

const ease = (t: number) => t * t * (3 - 2 * t);
const N_SOL = new THREE.Vector3();
const HAUT = new THREE.Vector3(0, 1, 0);
const Q_SOL = new THREE.Quaternion();
const Q_B = new THREE.Quaternion();

/** Geste d'attente en cours (E1). */
interface Geste {
  nom: string;
  cote: Cote;
  debut: number;
  duree: number;
}

/**
 * L'animation d'un humain : garde la pose en cours, les passages, l'appui, les gestes et le regard d'une image à l'autre (elle
 * survit au corps, refait quand sa forme change).
 */
/** Couché, la jambe et le bras du dessus retombent (adduction, rad) : flottants, ils semblaient levés. */
const FUSIL_HANCHE = -0.7;
const FUSIL_EPAULE = -1.15;
const COTE_HANCHE = -0.55;
/** Bras du dessus sur le flanc : flexion, abduction de l'épaule, flexion du coude. */
const COTE_EPAULE = [0.3, -0.1, 0.5];

export class Animateur {
  private cible = "";
  private etapes: { cle: string; duree: number }[] = [];
  private debutEtape = 0;
  private readonly depart = new Pose().zero();
  private readonly base = new Pose().zero();
  private readonly visee = new Pose().zero();
  private readonly sortie = new Pose().zero();
  private appui = 0;
  private appuiVise = 0;
  private prochainAppui = 0;
  private effort = 0;
  private geste: Geste | null = null;
  private prochainGeste = 0;
  private readonly graine: number;
  private derniere = -1;
  /** Regard : lacet et tangage de la tête (radians, repère du corps) et poids. */
  private lacet = 0;
  private tangage = 0;
  private poidsRegard = 0;
  /** Point regardé (monde), choisi par proches.ts, ou null. */
  regard: THREE.Vector3 | null = null;
  /** La tête est tournée vers ce point par l'écoute (l'Entité qui parle). */
  ecoute = false;

  constructor(readonly id: number) {
    this.graine = unite(hacher(id, 71)) * 100;
    this.prochainGeste = 6 + 30 * unite(hacher(id, 73));
  }

  /** Essais (?debug3d) : pose imposée à un humain (clé « assis.tailleur », « couche.dos »…). */
  static readonly imposees = new Map<number, string>();

  /** Clé de la pose voulue (posture et variante du moment). */
  private choisirCle(s: Situation, t: number): string {
    const imposee = Animateur.imposees.get(s.id);
    if (imposee) return imposee;
    if (s.combat) return "combat." + s.combat.geste;
    // L'état (froid, peur, maladie) fait revoir le choix dès qu'il change de palier (F1).
    const etat = Math.round(s.froid * 2) + 3 * Math.round(s.peur * 2) + 9 * Math.round(s.malade);
    const lent = (periode: number, sel: number) => Math.floor((t + unite(hacher(s.id, sel)) * periode) / periode) + 7919 * etat;
    switch (s.posture) {
      case "marche":
        // Le nourrisson ne marche pas : il va à quatre pattes (vers 7-12 mois).
        return s.age < 0.95 ? "quatre" : "marche";
      case "assis":
        if (s.age < 0.5) return "couche.dos";
        // Au sein, en tailleur ou de côté : l'enfant repose sur les cuisses (genoux dressés, il n'y aurait pas de place).
        if (s.allaite) return unite(hacher(s.id, 159)) < 0.6 ? "assis.tailleur" : "assis.cote";
        // Prier (activité 39, 0.42.8) : à genoux, ou assis sur les talons.
        if (s.activite === 39) return unite(hacher(s.id, 161)) < 0.6 ? "assis.genoux" : "assis.talons";
        return "assis." + choisir(ASSIS, s, lent(150 + 200 * unite(hacher(s.id, 77)), 75), 0);
      case "travail": {
        const v = choisir([ASSIS[0], ASSIS[3], ASSIS[4], ASSIS[1]], s, lent(400, 79), 1);
        return "travail." + v;
      }
      case "accroupi":
        if (s.age < 1.5) return "assis.allonge";
        return "accroupi." + choisir(ACCROUPI, s, lent(120, 81), 2);
      case "couche":
        if (s.berceau) return "couche.berceau";
        return "couche." + choisir(COUCHE, s, lent(s.dort ? 260 : 180, 83), 3);
      case "nage":
        return "nage";
      case "enlace":
        return "couche.enlace";
      case "porte":
        // Au sein (0.41.8) : couché en travers, dans le creux du bras, quel que soit son âge.
        if (s.age < 0.8 || !s.porteur || s.porteur === "allaite") return "porte.berceau";
        if (s.porteur === "couche") return "porte.couche";
        if (s.porteur === "assis" || s.porteur === "travail" || s.porteur === "accroupi") return "porte.genoux";
        return "porte.hanche";
      default:
        if (s.age < 0.75) return "couche.dos";
        if (s.age < 1.1) return "assis.allonge";
        return "debout";
    }
  }

  /** Le chemin d'une pose à l'autre : étapes intermédiaires (s'asseoir, se lever, s'allonger) et durées. */
  private chemin(de: string, vers: string, s: Situation): { cle: string; duree: number }[] {
    const a = famille(de);
    const b = famille(vers);
    // Les vieux se baissent et se relèvent plus lentement ; les enfants plus vite.
    const k = (s.age < 12 ? 0.75 : 1) * (1 + 0.6 * lisser(55, 80, s.age)) * (0.85 + 0.3 * unite(hacher(s.id, 85)));
    if (!de) return [{ cle: vers, duree: 0.01 }];
    // Combat (0.42.0) : tout va vite. La chute s'anime d'elle-même ; le dormeur se lève en sursaut.
    if (vers.startsWith("combat.") || de.startsWith("combat.")) {
      if (vers === "combat.chute" || vers === "combat.gisant") return [{ cle: vers, duree: 0.12 }];
      if (a === "couche" && b === "debout") return [{ cle: "assis.genoux", duree: 0.45 }, { cle: "releve", duree: 0.4 }, { cle: vers, duree: 0.25 }];
      if (a === "debout" && b === "sol") return [{ cle: vers, duree: 0.35 }];
      return [{ cle: vers, duree: 0.2 }];
    }
    if (a === b) {
      if (a === "debout") return [{ cle: vers, duree: 0.35 }];
      return [{ cle: vers, duree: 1.4 * k }];
    }
    if (a === "debout" && b === "sol") return [{ cle: "descente", duree: 0.9 * k }, { cle: vers, duree: 0.9 * k }];
    if (a === "sol" && b === "debout") return [{ cle: "releve", duree: 0.9 * k }, { cle: vers, duree: 0.8 * k }];
    if (a === "debout" && b === "couche") {
      return [{ cle: "descente", duree: 0.9 * k }, { cle: "assis.allonge", duree: 0.8 * k }, { cle: vers, duree: 1.2 * k }];
    }
    if (a === "couche" && b === "debout") {
      return [{ cle: "assis.genoux", duree: 1.2 * k }, { cle: "releve", duree: 0.9 * k }, { cle: vers, duree: 0.8 * k }];
    }
    if (a === "sol" && b === "couche") return [{ cle: "assis.allonge", duree: 0.8 * k }, { cle: vers, duree: 1.2 * k }];
    if (a === "couche" && b === "sol") return [{ cle: "assis.genoux", duree: 1.2 * k }, { cle: vers, duree: 0.9 * k }];
    return [{ cle: vers, duree: 0.5 }];
  }

  /**
   * Pose le corps pour cet instant. `t` : secondes réelles ; `sol` : pente sous lui (null : porté ou dans l'eau). Renvoie la pose
   * finale (rotations canoniques posées sur le corps, appuis et cinématique inverse faits).
   */
  animer(corps: Corps, s: Situation, t: number, sol: SolLocal | null): void {
    const dt = this.derniere < 0 ? 0 : Math.min(0.1 * ACCELERATION_MAX, t - this.derniere);
    if (this.derniere < 0) this.prochainGeste = t + 6 + 30 * unite(hacher(this.id, 73));
    this.derniere = t;
    // Effort : la marche essouffle un peu, le repos calme (≈ une minute).
    const effortVise = s.posture === "marche" ? Math.min(1, 0.35 + s.vitesse * 0.25) : 0;
    this.effort += (effortVise - this.effort) * Math.min(1, dt / (effortVise > this.effort ? 20 : 45));
    // Appui d'une jambe sur l'autre, changé toutes les quelques secondes (debout).
    if (t > this.prochainAppui) {
      const h = unite(hacher(this.id, Math.floor(t * 10), 87));
      this.appuiVise = (h < 0.5 ? -1 : 1) * (0.45 + 0.55 * unite(hacher(this.id, Math.floor(t * 10), 89)));
      this.prochainAppui = t + 4 + 14 * unite(hacher(this.id, Math.floor(t * 10), 91));
    }
    this.appui += (this.appuiVise - this.appui) * Math.min(1, dt * 1.6);

    const cle = this.choisirCle(s, t);
    if (cle !== this.cible) {
      this.depart.copier(this.base);
      this.etapes = this.chemin(this.cible, cle, s);
      this.debutEtape = t;
      this.cible = cle;
    }
    // Étape en cours du passage.
    while (this.etapes.length > 1 && t - this.debutEtape >= this.etapes[0].duree) {
      this.evaluer(this.etapes[0].cle, s, t, this.visee);
      this.depart.copier(this.visee);
      this.debutEtape += this.etapes[0].duree;
      this.etapes.shift();
    }
    const etape = this.etapes[0];
    this.evaluer(etape.cle, s, t, this.visee);
    const u = Math.min(1, (t - this.debutEtape) / etape.duree);
    if (u < 1) this.base.melanger(this.depart, this.visee, ease(u));
    else this.base.copier(this.visee);
    this.sortie.copier(this.base);
    this.vivre(corps, s, t, dt, this.sortie);
    poser(corps, this.sortie, sol);
  }

  /** Pose voulue pour une clé (sans le vivant qui s'y ajoute). */
  private evaluer(cle: string, s: Situation, t: number, p: Pose): void {
    p.zero();
    const g = this.graine;
    const [famille_, variante] = cle.split(".");
    const vieux = lisser(55, 85, s.age);
    const petit = 1 - lisser(1, 3, s.age);
    // Mains au repos : légèrement fléchies, plus chez l'auriculaire (cascade naturelle).
    p.main("G", 0.24 + 0.08 * onde(t, g, 0.2), 0.2);
    p.main("D", 0.24 + 0.08 * onde(t, g + 3, 0.2), 0.2);
    switch (famille_) {
      case "debout":
        this.debout(p, s, t, vieux, petit, variante);
        break;
      case "marche":
        this.marche(p, s, vieux, petit);
        break;
      case "quatre":
        this.quatrePattes(p, s, t);
        break;
      case "assis":
        this.assis(p, variante, s, t);
        break;
      case "travail":
        this.assis(p, variante, s, t);
        this.travailler(p, s, t);
        break;
      case "accroupi":
        this.accroupi(p, variante, s, t);
        break;
      case "couche":
        this.couche(p, variante, s, t);
        break;
      case "descente":
      case "releve":
        this.passage(p, s, famille_ === "releve");
        break;
      case "nage":
        this.nage(p, s);
        break;
      case "porte":
        this.porte(p, variante, s, t);
        break;
      case "combat":
        this.combattre(p, variante, s, t);
        break;
    }
    // Une mère porte son enfant (D1) : ses bras le tiennent selon son âge et sa propre posture.
    if (s.enfant !== null && famille_ !== "couche" && famille_ !== "nage" && famille_ !== "descente" && famille_ !== "releve"
      && !(famille_ === "combat" && (variante === "gisant" || variante === "chute"))) {
      porterEnfant(p, s.enfant, famille_, s.allaite === true);
    }
    // 0.45.10 : un outil court en main droite (hache, bâton), hors combat : bras un peu écarté de la cuisse, coude à peine plié,
    // l'outil tenu vers l'avant au lieu de pendre contre la jambe ; le balancier de ce bras est moitié moindre (on retient ce qu'on porte).
    if (s.outil && s.outil !== 3 && s.outil !== 4 && (famille_ === "debout" || famille_ === "marche")) {
      const phi = s.phase + Math.PI;
      const balancier = famille_ === "marche" ? -0.13 * Math.max(0.45, Math.min(1.25, s.vitesse / 1.25)) * Math.cos(phi) : 0;
      p.epaule("D", 0.08 + balancier, 0.16, 0.1);
      p.coude("D", 0.45, 0.35);
      p.poignet("D", 0.1);
      p.main("D", 1.35, 0.9);
      p.mains[1].length = 0;
    }
    // Les vieux : dos voûté, genoux un peu fléchis.
    if (s.voute > 0) {
      p.ajouter("poitrine", s.voute * 0.22, 0, 0);
      p.ajouter("ventre", s.voute * 0.1, 0, 0);
      p.ajouter("cou", -s.voute * 0.22, 0, 0);
    }
    const aTerre = famille_ === "combat" && (variante === "gisant" || variante === "chute");
    if (famille_ !== "couche" && famille_ !== "nage" && famille_ !== "porte" && !aTerre) this.allure(p, s);
  }

  /**
   * Ce que vit l'humain se voit à son port (F1) : abattu (fatigue, faim, tristesse, maladie), épaules tombantes, dos rond, tête
   * basse ; replié (peur, froid), épaules montées et rentrées ; joyeux et sûr de lui, poitrine ouverte, tête haute (Wallbott 1998 ;
   * Dael, Mortillaro et Scherer 2012 ; Coulson 2004).
   */
  private allure(p: Pose, s: Situation): void {
    const a = abattement(s);
    const r = repli(s);
    const o = ouverture(s);
    if (a + r + o < 0.02) return;
    p.ajouter("ventre", 0.05 * a + 0.03 * r - 0.02 * o, 0, 0);
    p.ajouter("poitrine", 0.1 * a + 0.07 * r - 0.07 * o, 0, 0);
    p.ajouter("cou", 0.06 * a + 0.04 * r - 0.02 * o, 0, 0);
    p.ajouter("tete", 0.12 * a + 0.03 * r - 0.06 * o, 0, 0);
    if (PO.claviculeG >= 0) {
      // Clavicules : élévation (z, signe par côté) et avancée (y).
      const monte = 0.12 * s.froid + 0.08 * s.peur - 0.06 * a + 0.02 * o;
      const avance = 0.07 * a + 0.06 * r - 0.05 * o;
      p.ajouter("claviculeG", 0, -avance, monte);
      p.ajouter("claviculeD", 0, avance, -monte);
    }
  }

  // ——— Debout ———

  private debout(p: Pose, s: Situation, t: number, vieux: number, petit: number, imposee?: string): void {
    const w = this.appui; // + : poids sur la jambe gauche
    const largeur = 1 + 0.6 * petit;
    // Le bassin se décale vers la jambe d'appui et monte de ce côté ; le buste compense (contrapposto).
    p.racine.x = 0.018 * w;
    p.bassin(0.02 - 0.06 * petit, 0.03 * w, 0.045 * w);
    p.colonne("ventre", -0.04 * petit, -0.02 * w, -0.035 * w);
    p.colonne("poitrine", 0.02, -0.02 * w, -0.025 * w);
    p.colonne("cou", 0.02 + 0.08 * vieux, 0, 0.01 * w);
    for (const c of COTES) {
      const sg = SIGNE[c];
      const libre = Math.max(0, -sg * w); // jambe qui ne porte pas
      p.hanche(c, 0.03 + 0.1 * libre + 0.08 * petit, 0.03 + 0.04 * petit, 0.12 + 0.08 * libre);
      p.genou(c, 0.05 + 0.25 * libre + 0.12 * vieux + 0.15 * petit);
      p.cheville(c, 0.04 + 0.06 * libre);
      p.poserPied(c, sg * (0.055 * largeur + 0.012 * libre), 0.025 * libre, 1);
    }
    // Bras : la façon de les tenir change de temps en temps.
    const etat = Math.round(s.froid * 2) + 3 * Math.round(s.peur * 2) + 9 * Math.round(s.malade);
    const epoque = Math.floor((t + 50 * unite(hacher(s.id, 93))) / 70) + 7919 * etat;
    // Un état fort l'emporte presque toujours sur l'habitude (F1) : transi ou terrorisé, on serre les bras ; très malade, on se
    // tient le ventre.
    const fort = unite(hacher(s.id, epoque, 95));
    const bras = imposee
      ?? (s.age >= 6 && repli(s) > 0.6 && fort < 0.85 ? "croises"
        : s.age >= 8 && s.malade > 0.6 && fort < 0.75 ? "ventre"
          : choisir(BRAS_DEBOUT, s, epoque, 4));
    this.brasDebout(p, bras, s, petit);
  }

  private brasDebout(p: Pose, bras: string, s: Situation, petit: number): void {
    const g = this.graine;
    const ballants = () => {
      for (const c of COTES) {
        p.epaule(c, 0.04, 0.01 + 0.25 * petit, 0.05);
        p.coude(c, 0.22 + 0.5 * petit + 0.06 * unite(hacher(s.id, 95)), 0.1);
        p.poignet(c, 0.12, 0.05);
        p.clavicule(c, -0.02, 0);
      }
    };
    switch (bras) {
      case "devant":
        for (const c of COTES) {
          p.epaule(c, 0.25, 0.05, -0.1);
          p.coude(c, 0.9, 0.2);
          p.viserMain(c, PO.bassin, SIGNE[c] * 0.01, -0.02, 0.105);
        }
        p.main("G", 0.45, 0.4);
        p.main("D", 0.45, 0.4);
        break;
      case "dos":
        for (const c of COTES) {
          p.epaule(c, -0.18, 0.18, -1.1);
          p.coude(c, 1.45, -0.2);
          p.viserMain(c, PO.bassin, SIGNE[c] * 0.02, 0.03, -0.115);
        }
        p.clavicule("G", 0, -0.05);
        p.clavicule("D", 0, -0.05);
        break;
      case "hanche": {
        ballants();
        const c: Cote = unite(hacher(s.id, 97)) < 0.5 ? "G" : "D";
        p.epaule(c, 0.0, 0.6, -0.35);
        p.coude(c, 1.6, -0.3);
        p.viserMain(c, PO.bassin, SIGNE[c] * 0.098, 0.075, 0.01);
        p.main(c, 0.15, 0.5);
        break;
      }
      case "croises": {
        // Le froid serre davantage : mains plus haut sur les bras, qui les frottent (F1).
        const f = s.froid;
        const frotte = f > 0.3 ? 0.02 * f * Math.sin(this.derniere * 6.5 + g) : 0;
        for (const c of COTES) {
          p.epaule(c, 0.35 + 0.1 * f, 0.15 - 0.08 * f, -0.55 - 0.1 * f);
          p.coude(c, 1.9 + 0.2 * f, 0);
          // Chaque main sur le haut du bras opposé, l'une devant l'autre.
          p.viserMain(c, PO.poitrine, -SIGNE[c] * (0.088 + 0.01 * f), -0.045 + 0.02 * f + (c === "G" ? 0.02 : 0) + SIGNE[c] * frotte, c === "G" ? 0.11 : 0.09);
        }
        p.main("G", 0.25 + 0.3 * f, 0.3);
        p.main("D", 0.25 + 0.3 * f, 0.3);
        break;
      }
      case "ventre": {
        // Une main (les deux chez le malade) à plat sur le ventre.
        ballants();
        const deux = s.malade > 0.5 && unite(hacher(s.id, 98)) < 0.6;
        const avance = 0.1 + (s.enceinte ? 0.035 : 0);
        for (const c of COTES) {
          if (!deux && c !== (unite(hacher(s.id, 97)) < 0.5 ? "G" : "D")) continue;
          p.epaule(c, 0.3, 0.1, -0.4);
          p.coude(c, 1.3, 0.2);
          p.viserMain(c, PO.ventre, SIGNE[c] * (deux ? 0.035 : 0.01), -0.015 + (c === "G" ? 0.025 : 0), avance);
          p.main(c, 0.12, 0.3);
        }
        break;
      }
      default:
        ballants();
    }
    void g;
  }

  // ——— Marche (Winter 2009 : hanche +30°/−10°, genou 15° à l'appui et 60° à l'oscillation, cheville −20° à l'envol) ———

  private marche(p: Pose, s: Situation, vieux: number, petit: number): void {
    const ampl = Math.max(0.45, Math.min(1.25, s.vitesse / 1.25)) * (1 - 0.3 * vieux) * (1 - 0.25 * petit);
    for (const c of COTES) {
      const sg = SIGNE[c];
      const phi = s.phase + (c === "G" ? 0 : Math.PI);
      const part = ((phi / (Math.PI * 2)) % 1 + 1) % 1; // 0 : le talon touche
      const boite = s.boite && c === "D" ? 0.55 : 1;
      const hanche = 0.17 * ampl + 0.33 * ampl * Math.cos(phi) * boite;
      const genou = 0.06 + 0.27 * ampl * bosse(part, 0.15, 0.08) * boite + 1.0 * ampl * bosse(part, 0.72, 0.11) * (1 - 0.2 * vieux);
      const cheville = -0.1 * bosse(part, 0.06, 0.04) + 0.17 * bosse(part, 0.42, 0.09) - 0.33 * ampl * bosse(part, 0.61, 0.05) + 0.03;
      p.hanche(c, hanche + 0.06 * petit, 0.03 + 0.1 * petit, 0.08);
      p.genou(c, genou + 0.1 * petit);
      p.cheville(c, cheville);
      p.orteils(c, 0.55 * bosse(part, 0.57, 0.05));
      // Bras en balancier, opposés à la jambe du même côté ; le tout-petit les garde en l'air (garde haute).
      const balancier = -0.27 * ampl * Math.cos(phi) * (1 - 0.6 * petit);
      p.epaule(c, 0.05 + balancier, 0.08 + 0.45 * petit, 0.05);
      p.coude(c, 0.3 + 0.25 * Math.max(0, balancier) / 0.27 + 0.8 * petit, 0.1);
      p.poignet(c, 0.1);
      p.clavicule(c, 0.01, 0.03 * Math.cos(phi) * ampl);
      void sg;
    }
    const cphi = Math.cos(s.phase);
    p.racine.x = 0.01 * Math.cos(s.phase - 0.5) * ampl;
    p.bassin(0.04 + 0.04 * ampl, -0.07 * cphi * ampl, 0.035 * Math.sin(s.phase) * ampl);
    p.colonne("ventre", 0.02, 0.04 * cphi * ampl, -0.02 * Math.sin(s.phase));
    p.colonne("poitrine", 0.02 + 0.03 * ampl, 0.05 * cphi * ampl, s.boite ? 0.06 * Math.sin(s.phase) : 0);
    p.colonne("cou", 0.02, -0.02 * cphi * ampl);
    p.plantes = [0, 0];
  }

  // ——— Nourrisson à quatre pattes (marche croisée : bras gauche avec jambe droite) ———

  private quatrePattes(p: Pose, s: Situation, _t: number): void {
    p.coucher(1.25, 0);
    for (const c of COTES) {
      const phi = s.phase * 0.8 + (c === "G" ? 0 : Math.PI);
      p.hanche(c, 1.25 + 0.25 * Math.sin(phi), 0.15);
      p.genou(c, 1.55 - 0.15 * Math.sin(phi));
      p.cheville(c, -0.5);
      p.epaule(c, 1.25 - 0.25 * Math.sin(phi + Math.PI), 0.12);
      p.coude(c, 0.1, 0.1);
      p.poignet(c, -0.9);
      p.viserMain(c, PO.poitrine, SIGNE[c] * 0.1, 0, 0, 0.5, true);
    }
    p.colonne("cou", -0.55);
    p.colonne("tete", -0.25);
    p.main("G", 0.05, 0.1, 0.15);
    p.main("D", 0.05, 0.1, 0.15);
    p.appuis = APPUI_GENOUX | APPUI_CORPS;
    p.plantes = [0, 0];
  }

  // ——— Assis par terre ———

  private assis(p: Pose, v: string, s: Situation, t: number): void {
    const g = this.graine;
    const raide = lisser(50, 85, s.age); // les vieux plient moins
    p.appuis = APPUI_FESSES;
    p.plantes = [0, 0];
    switch (v) {
      case "tailleur": {
        // En tailleur : cuisses écartées et tournées en dehors, tibias croisés, dos un peu rond.
        p.bassin(-0.12);
        p.hanche("G", 1.25, 0.72 - 0.15 * raide, 0.78);
        p.hanche("D", 1.3, 0.68 - 0.15 * raide, 0.85);
        p.genou("G", 2.3 - 0.3 * raide);
        p.genou("D", 2.4 - 0.3 * raide);
        p.cheville("G", -0.25, 0.35);
        p.cheville("D", -0.25, 0.35);
        p.colonne("ventre", 0.2);
        p.colonne("poitrine", 0.12);
        p.colonne("cou", -0.06);
        for (const c of COTES) {
          p.epaule(c, 0.45, 0.12, -0.15);
          p.coude(c, 0.75, 0.3);
          p.poignet(c, 0.25);
          p.viserMain(c, PO[`jambe${c}`], 0, 0.02, 0.05, 0.85);
        }
        break;
      }
      case "genoux": {
        // Genoux relevés, pieds à plat, bras posés dessus ou autour.
        p.bassin(-0.28);
        for (const c of COTES) {
          p.hanche(c, 1.9 - 0.2 * raide, 0.12, 0.05);
          p.genou(c, 2.05 - 0.25 * raide);
          p.cheville(c, 0.15);
        }
        p.plantes = [1, 1];
        p.colonne("ventre", 0.3);
        p.colonne("poitrine", 0.15);
        p.colonne("cou", -0.15);
        const enlace = repli(s) > 0.3 || unite(hacher(s.id, Math.floor(t / 200), 99)) < 0.5;
        for (const c of COTES) {
          p.epaule(c, 0.85, 0.2, -0.3);
          p.coude(c, enlace ? 1.2 : 0.7, 0.3);
          if (enlace) p.viserMain(c, PO[`jambe${c === "G" ? "D" : "G"}`], 0, -0.06, 0.045, 0.9);
          else p.viserMain(c, PO[`jambe${c}`], 0, 0.035, 0.03, 0.8);
        }
        p.main("G", enlace ? 0.8 : 0.4);
        p.main("D", enlace ? 0.8 : 0.4);
        break;
      }
      case "cote": {
        // Jambes repliées sur le côté (vers la droite), appui sur la main gauche.
        p.bassin(-0.08, 0.15, -0.12);
        p.hanche("G", 1.35, 0.15, -0.55);
        p.genou("G", 2.45);
        p.hanche("D", 1.2, 0.5, 0.9);
        p.genou("D", 2.5);
        p.cheville("G", -0.6);
        p.cheville("D", -0.5);
        p.colonne("ventre", 0.08, -0.08, 0.16);
        p.colonne("poitrine", 0.06, -0.08, 0.1);
        p.colonne("cou", 0, 0, -0.12);
        p.epaule("G", 0.05, 0.55, -0.2);
        p.coude("G", 0.12, 0.3);
        p.poignet("G", -1.1);
        p.viserMain("G", -1, 0.2, 0.0, 0.04, 1, true);
        p.main("G", 0.08, 0.15, 0.12);
        p.epaule("D", 0.45, 0.1, -0.2);
        p.coude("D", 0.8, 0.4);
        p.viserMain("D", PO.cuisseG, 0, -0.14, 0.05, 0.8);
        break;
      }
      case "talons": {
        // À genoux, assis sur les talons, mains sur les cuisses.
        for (const c of COTES) {
          p.hanche(c, 1.25, 0.1, 0.05);
          p.genou(c, 2.75);
          p.cheville(c, -0.85);
          p.epaule(c, 0.35, 0.08, -0.15);
          p.coude(c, 0.8, 0.2);
          p.viserMain(c, PO[`cuisse${c}`], 0, -0.16, 0.05, 0.9);
        }
        p.bassin(0.05);
        p.colonne("ventre", 0.05);
        p.colonne("poitrine", 0.04);
        p.appuis = APPUI_GENOUX | APPUI_FESSES;
        break;
      }
      case "allonge": {
        // Jambes allongées devant soi, penché en arrière sur les mains.
        p.bassin(-0.25);
        p.hanche("G", 1.35, -0.1, 0.25);
        p.hanche("D", 1.3, -0.08, 0.3);
        p.genou("G", 0.1 + 0.2 * unite(hacher(s.id, 101)));
        p.genou("D", 0.12);
        p.cheville("G", -0.35);
        p.cheville("D", -0.4);
        p.colonne("ventre", 0.08);
        p.colonne("poitrine", 0.02);
        p.colonne("cou", 0.05);
        for (const c of COTES) {
          p.epaule(c, -0.5, 0.25, 0.2);
          p.coude(c, 0.1, 0.3);
          p.poignet(c, -1.1);
          p.viserMain(c, PO.bassin, SIGNE[c] * 0.13, 0, -0.16, 0.9, true);
          p.main(c, 0.1, 0.15, 0.12);
        }
        break;
      }
      default: {
        // Une jambe relevée (bras posé dessus), l'autre repliée à plat.
        p.bassin(-0.2);
        p.hanche("G", 1.85, 0.15, 0.05);
        p.genou("G", 2.1);
        p.cheville("G", 0.12);
        p.hanche("D", 1.3, 0.6, 0.85);
        p.genou("D", 2.25);
        p.cheville("D", -0.3, 0.3);
        p.plantes = [1, 0];
        p.colonne("ventre", 0.22);
        p.colonne("poitrine", 0.1);
        p.colonne("cou", -0.08);
        p.epaule("G", 0.7, 0.2, -0.2);
        p.coude("G", 0.45, 0.2);
        p.poignet("G", 0.6);
        p.viserMain("G", PO.jambeG, 0, 0.04, 0.12, 0.7);
        p.epaule("D", 0.3, 0.2, -0.1);
        p.coude("D", 0.6, 0.3);
        p.viserMain("D", PO.cuisseD, 0, -0.18, 0.05, 0.85);
      }
    }
    // Petit balancement assis.
    p.ajouter("ventre", 0.02 * onde(t, g + 9, 0.12), 0.015 * onde(t, g + 11, 0.09), 0);
  }

  // ——— Travail assis : le geste dépend de ce qu'il fait ———

  private travailler(p: Pose, s: Situation, t: number): void {
    const g = this.graine;
    // Point de travail : devant soi, au sol ou sur les cuisses.
    const z = 0.2;
    const y = 0.05;
    p.colonne("poitrine", 0.22);
    p.colonne("ventre", 0.24);
    p.colonne("cou", 0.25);
    p.colonne("tete", 0.35);
    // Fabriquer : taper (environ un coup par seconde) ; essayer, soigner : mains qui manipulent ; enseigner : montrer.
    const frappe = s.activite === 24;
    const rythme = frappe ? 6.3 * (0.8 + 0.4 * unite(hacher(s.id, 103))) : 1.7;
    const k = frappe ? Math.pow(0.5 + 0.5 * Math.sin(t * rythme + g), 2.5) : 0.5 + 0.5 * onde(t, g, 1.3);
    p.epaule("G", 0.75, 0.15, -0.1);
    p.coude("G", 1.25, 0.6);
    p.viserMain("G", -1, 0.025, y, z, 1, true);
    p.main("G", 0.85, 0.6);
    p.epaule("D", 0.85 + 0.3 * k, 0.15, -0.1);
    p.coude("D", 1.0 + 0.4 * k, 0.2);
    p.viserMain("D", -1, -0.04 + 0.02 * onde(t, g + 5, 2), y + 0.02 + (frappe ? 0.16 * k : 0.04 * k), z + 0.02 * onde(t, g + 7, 1.7), 1, true);
    p.main("D", frappe ? 1.25 : 0.7 + 0.3 * k, 0.8);
  }

  // ——— Accroupi : cueillir, chercher au sol ———

  private accroupi(p: Pose, v: string, s: Situation, t: number): void {
    const g = this.graine;
    p.plantes = [1, 1];
    // Les mains cueillent devant, l'une après l'autre.
    const cueille = (c: Cote, dephase: number) => {
      const k = 0.5 + 0.5 * Math.sin(t * 1.6 + g + dephase);
      p.viserMain(c, -1, SIGNE[c] * (0.05 + 0.03 * onde(t, g + dephase, 0.4)), 0.015 + 0.09 * (1 - k), 0.2 + 0.05 * onde(t, g + 2 + dephase, 0.3), 1, true);
      p.main(c, 0.4 + 0.7 * k, 0.6 + 0.4 * k, 0.04, 0.3 + 0.6 * k);
    };
    switch (v) {
      case "pointes":
        for (const c of COTES) {
          p.hanche(c, 2.15, 0.35, 0.3);
          p.genou(c, 2.55);
          p.cheville(c, 0.45);
          p.orteils(c, 0.8);
        }
        p.colonne("ventre", 0.35);
        p.colonne("poitrine", 0.15);
        break;
      case "unGenou":
        // Un genou à terre, l'autre pied à plat devant.
        p.hanche("G", 1.75, 0.15, 0.1);
        p.genou("G", 1.9);
        p.cheville("G", 0.25);
        p.hanche("D", 0.15, 0.08, 0.1);
        p.genou("D", 1.75);
        p.cheville("D", -0.25);
        p.orteils("D", 0.9);
        p.plantes = [1, 0];
        p.appuis = APPUI_PIEDS | APPUI_GENOUX;
        p.colonne("ventre", 0.3);
        p.colonne("poitrine", 0.2);
        break;
      default:
        // Accroupi pieds à plat, talons au sol (habituel sans sièges).
        for (const c of COTES) {
          p.hanche(c, 2.05, 0.38, 0.32);
          p.genou(c, 2.45);
          p.cheville(c, 0.52);
        }
        p.colonne("ventre", 0.38);
        p.colonne("poitrine", 0.18);
    }
    p.colonne("cou", 0.0);
    p.colonne("tete", 0.3);
    for (const c of COTES) {
      p.epaule(c, 1.0, 0.15, -0.1);
      p.coude(c, 0.45, 0.5);
      p.poignet(c, 0.3);
    }
    if (s.activite === 4 || s.activite === 6 || s.activite === 37) {
      cueille("D", 0);
      cueille("G", 2.2);
    } else {
      // Avant-bras posés sur les genoux, mains pendantes.
      for (const c of COTES) {
        p.epaule(c, 0.55, 0.15, -0.1);
        p.coude(c, 0.9, 0.3);
        p.viserMain(c, PO[`jambe${c}`], 0, 0.05, 0.14, 0.7);
      }
      p.colonne("tete", 0.05);
    }
  }

  // ——— Couché ———

  private couche(p: Pose, v: string, s: Situation, t: number): void {
    p.appuis = APPUI_CORPS;
    p.plantes = [0, 0];
    const g = this.graine;
    const cote: Cote = unite(hacher(s.id, Math.floor(t / 300), 105)) < 0.5 ? "G" : "D";
    const sc = SIGNE[cote]; // côté qui touche le sol
    switch (v) {
      case "dos": {
        p.coucher(-Math.PI / 2, 0);
        // Un genou plié tombe sur le côté (personne ne dort genou dressé) ; pieds relâchés en dehors.
        const genou = unite(hacher(s.id, 107)) < 0.4;
        p.hanche("G", genou ? 0.45 : 0.08, genou ? 0.45 : 0.1, genou ? 0.9 : 0.4);
        p.genou("G", genou ? 1.0 : 0.12);
        p.hanche("D", 0.1, 0.12, 0.45);
        p.genou("D", 0.15);
        p.cheville("G", -0.4);
        p.cheville("D", -0.45);
        p.epaule("G", -0.25, 0.18, 0.15);
        p.coude("G", 0.3);
        const surVentre = unite(hacher(s.id, 109)) < 0.5;
        p.epaule("D", 0.3, 0.15, -0.3);
        p.coude("D", surVentre ? 1.6 : 0.4, 0.3);
        if (surVentre) p.viserMain("D", PO.ventre, 0.0, 0.0, 0.1, 0.9);
        p.colonne("cou", -0.15);
        p.colonne("tete", 0, 0.4 * onde(0, g, 1), 0);
        break;
      }
      case "fusil":
        p.coucher(0, sc * Math.PI / 2 * -1);
        // La jambe et le bras du dessus retombent sur ceux du dessous (sinon ils flottent à hauteur de hanche).
        for (const c of COTES) {
          const dessus = c !== cote;
          p.hanche(c, 1.5 + (dessus ? 0.15 : 0), dessus ? FUSIL_HANCHE : 0.05, 0.05);
          p.genou(c, 1.9);
          p.cheville(c, -0.3);
          p.epaule(c, 1.4, dessus ? FUSIL_EPAULE : 0.05, -0.2);
          p.coude(c, 1.8, 0.2);
          p.poignet(c, 0.6);
        }
        p.colonne("ventre", 0.35);
        p.colonne("poitrine", 0.2);
        p.colonne("cou", 0.35);
        p.colonne("tete", 0.15, 0, sc * 0.2);
        p.main("G", 0.9);
        p.main("D", 0.9);
        break;
      case "ventre": {
        // À plat ventre, la tête tournée sur la joue ; un bras replié près du visage, l'autre le long du corps ; une jambe
        // repliée sur le côté (genou en dehors).
        const autre: Cote = cote === "G" ? "D" : "G";
        p.coucher(Math.PI / 2, 0);
        p.hanche(cote, 0.55, 0.55, 1.25);
        p.genou(cote, 1.25);
        // Jambe tendue : un peu en extension pour reposer au sol (le bassin est plus épais que la cuisse).
        p.hanche(autre, 0.12, 0.06, 0.3);
        p.genou(autre, 0.08);
        for (const c of COTES) p.cheville(c, -0.75);
        // Bras du côté du visage remonté, la main près de la tête ; l'autre le long du corps, relâché.
        p.epaule(cote, 1.9, 0.5, -0.6);
        p.coude(cote, 1.9, 0);
        p.poignet(cote, 0.1);
        p.epaule(autre, 0.05, 0.12, 0.2);
        p.coude(autre, 0.25, 0);
        p.colonne("cou", -0.35, sc * 1.15);
        p.colonne("tete", -0.15, sc * 0.3, -sc * 0.1);
        break;
      }
      case "berceau": {
        // 0.45.0 : la mère qui dort avec son tout-petit (Ball, 2006) : sur le flanc, face à lui, genoux remontés sous ses pieds,
        // le bras du dessous au-dessus de sa tête, l'autre posé sur lui ; elle forme un abri dont il ne peut pas glisser.
        const autre: Cote = cote === "G" ? "D" : "G";
        p.coucher(0, -sc * Math.PI / 2);
        for (const c of COTES) {
          p.hanche(c, 1.25 + (c === autre ? 0.1 : 0), c === autre ? COTE_HANCHE * 0.6 : 0.05, 0);
          p.genou(c, 1.45);
          p.cheville(c, -0.3);
        }
        p.epaule(cote, 2.5, 0.1, 0.2);
        p.coude(cote, 0.7, 0.2);
        p.epaule(autre, 1.15, -0.05, -0.1);
        p.coude(autre, 0.7, 0.2);
        p.main(autre, 0.3);
        p.colonne("ventre", 0.22);
        p.colonne("poitrine", 0.15);
        p.colonne("cou", 0.2, 0, sc * 0.2);
        p.colonne("tete", 0.15, 0, sc * 0.1);
        break;
      }
      case "enlace": {
        // Deux amants couchés sur le flanc, face à face : le bras du dessus passe sur l'autre, jambes mêlées sans plus.
        const bas: Cote = s.enlace >= 0 ? "G" : "D";
        const haut: Cote = bas === "G" ? "D" : "G";
        const sb = SIGNE[bas];
        p.coucher(0, -sb * Math.PI / 2);
        p.hanche(bas, 0.35, 0.05, 0.05);
        p.genou(bas, 0.5);
        p.hanche(haut, 0.6, 0.1, -0.1);
        p.genou(haut, 0.7);
        p.cheville("G", -0.35);
        p.cheville("D", -0.35);
        p.epaule(bas, 1.6, 0.1, 0.1);
        p.coude(bas, 1.5, 0.3);
        p.epaule(haut, 0.95, -0.15, -0.2);
        p.coude(haut, 0.35, 0.2);
        p.main(haut, 0.3);
        p.colonne("ventre", 0.08);
        p.colonne("poitrine", 0.06);
        p.colonne("cou", 0.1, 0, sb * 0.2);
        p.colonne("tete", 0.08, 0, sb * 0.1);
        break;
      }
      default:
        // Sur le flanc, jambe du dessus plus pliée, bras du dessous replié sous la tête.
        p.coucher(0, -sc * Math.PI / 2);
        p.hanche(cote, 0.55, 0.05, 0.1);
        p.genou(cote, 0.75);
        // Jambe du dessus : le genou retombe au sol devant l'autre. Bras du dessus : posé le long du flanc, la main sur la cuisse.
        p.hanche(cote === "G" ? "D" : "G", 0.95, COTE_HANCHE, -0.1);
        p.genou(cote === "G" ? "D" : "G", 1.05);
        p.cheville("G", -0.35);
        p.cheville("D", -0.35);
        p.epaule(cote, 1.45, 0.15, 0.1);
        p.coude(cote, 2.0, 0.3);
        p.epaule(cote === "G" ? "D" : "G", COTE_EPAULE[0], COTE_EPAULE[1], 0);
        p.coude(cote === "G" ? "D" : "G", COTE_EPAULE[2], 0.2);
        p.colonne("ventre", 0.12);
        p.colonne("poitrine", 0.08);
        p.colonne("cou", 0.08, 0, sc * 0.25);
        p.colonne("tete", 0.05, 0, sc * 0.1);
    }
    void s;
  }

  // ——— S'asseoir et se relever : accroupi, une main vers le sol ———

  // ——— Combat (0.42.0) : gestes du rejeu d'un assaut, calculés comme les autres poses, sans capture de mouvement ———

  private combattre(p: Pose, geste: string, s: Situation, t: number): void {
    const c = s.combat;
    const u = c?.u ?? 0;
    const arme = c?.arme ?? 0;
    // Le peureux se tient plus en retrait, l'épuisé frappe mollement.
    const elan = (1 - 0.35 * s.fatigue) * (1 - 0.25 * s.peur);
    switch (geste) {
      case "garde":
        this.garde(p, s, t, arme);
        break;
      case "frappe":
      case "lancer": {
        this.garde(p, s, t, arme);
        // Armer (bras haut derrière), frapper (vers l'avant et le bas), revenir.
        const armer = ease(Math.min(1, u / 0.45));
        const coup = ease(Math.min(1, Math.max(0, (u - 0.45) / 0.15)));
        const retour = ease(Math.max(0, (u - 0.6) / 0.4));
        const k = armer * (1 - coup);
        const f = coup * (1 - retour) * elan;
        const lancer = geste === "lancer";
        p.epaule("D", 0.9 + 1.9 * k - 0.4 * f, 0.3 + (lancer ? 0.35 : 0.15) * k, lancer ? 0.6 * k : 0.2 * k);
        p.coude("D", 1.5 - 0.4 * k - 1.2 * f, 0.1);
        p.poignet("D", -0.4 * k + 0.6 * f);
        p.colonne("poitrine", 0.1 - 0.12 * k + 0.25 * f, -0.35 * k + 0.3 * f);
        p.colonne("ventre", 0.08 + 0.1 * f, -0.15 * k + 0.15 * f);
        p.hanche("G", 0.35 + 0.25 * f, 0.1);
        p.genou("G", 0.45 + 0.3 * f);
        if (lancer && u > 0.5) p.main("D", 0.1, 0.2, 0.1);
        break;
      }
      case "estoc": {
        // Épieu ou lance à deux mains : on ramène, on pousse en se fendant.
        this.garde(p, s, t, arme);
        const ramene = ease(Math.min(1, u / 0.4));
        const pousse = ease(Math.min(1, Math.max(0, (u - 0.4) / 0.18))) * (1 - ease(Math.max(0, (u - 0.62) / 0.38))) * elan;
        for (const k of COTES) {
          p.epaule(k, 0.55 - 0.15 * ramene * (1 - pousse) + 0.75 * pousse, k === "D" ? 0.15 : 0.05, k === "D" ? -0.2 : -0.5);
          p.coude(k, 1.5 + 0.2 * ramene * (1 - pousse) - 1.25 * pousse, 0.3);
        }
        p.hanche("G", 0.4 + 0.5 * pousse, 0.1);
        p.genou("G", 0.5 + 0.45 * pousse);
        p.colonne("poitrine", 0.08 + 0.2 * pousse, 0.1);
        p.colonne("ventre", 0.06 + 0.12 * pousse);
        break;
      }
      case "parer":
        // Les avant-bras devant le visage, le buste en arrière, un pas de recul.
        this.garde(p, s, t, arme);
        for (const k of COTES) {
          p.epaule(k, 1.45, 0.25, -0.3);
          p.coude(k, 1.85, 0.2);
          p.poignet(k, 0.2);
        }
        p.colonne("poitrine", -0.12, 0.1 * Math.sin(u * Math.PI * 2));
        p.colonne("tete", 0.25);
        p.hanche("D", -0.2, 0.08);
        p.genou("D", 0.35);
        break;
      case "touche": {
        // Le coup reçu : le buste part en arrière, une main va au flanc.
        this.garde(p, s, t, 0);
        const w = Math.sin(Math.min(1, u) * Math.PI);
        p.colonne("poitrine", -0.3 * w, 0.2 * w);
        p.colonne("cou", -0.2 * w);
        p.colonne("tete", -0.25 * w);
        p.epaule("G", 0.3, 0.1, -0.4);
        p.coude("G", 1.4, 0.2);
        p.viserMain("G", PO.ventre, 0.06, 0, 0.1, 0.9);
        for (const k of COTES) p.genou(k, 0.45 + 0.2 * w);
        break;
      }
      case "blesse":
        // Un genou à terre, la main pressée sur la plaie, la tête basse ; le souffle court (vivre).
        this.accroupi(p, "unGenou", s, t);
        p.epaule("G", 0.35, 0.1, -0.4);
        p.coude("G", 1.45, 0.2);
        p.viserMain("G", PO.ventre, 0.07, 0.01, 0.1, 1);
        p.main("G", 0.2, 0.3);
        p.epaule("D", 0.5, 0.25, -0.1);
        p.coude("D", 0.3, 0.2);
        p.viserMain("D", -1, -0.16, 0, 0.15, 0.8, true);
        p.colonne("tete", 0.45);
        p.colonne("poitrine", 0.3);
        break;
      case "chute": {
        // Il s'effondre en arrière : les genoux cèdent, le corps bascule, les bras s'ouvrent.
        const k = ease(u);
        p.coucher(-Math.PI / 2 * k, 0.25 * k * (unite(hacher(s.id, 131)) < 0.5 ? 1 : -1));
        for (const c2 of COTES) {
          p.hanche(c2, 0.5 * Math.sin(k * Math.PI) + 0.1 * k, 0.12 * k);
          p.genou(c2, 0.9 * Math.sin(k * Math.PI) + 0.25 * k);
          p.epaule(c2, 0.6 * (1 - k) + 0.2, 0.5 * k, 0.2);
          p.coude(c2, 0.5, 0.2);
        }
        p.colonne("cou", -0.3 * k);
        p.appuis = k < 0.55 ? APPUI_PIEDS : APPUI_CORPS;
        p.plantes = k < 0.55 ? [1, 1] : [0, 0];
        break;
      }
      case "gisant": {
        // Sur le dos, les membres relâchés là où ils sont tombés ; la tête roulée sur le côté.
        const h = (n: number) => unite(hacher(s.id, n));
        p.coucher(-Math.PI / 2, (h(131) < 0.5 ? 1 : -1) * 0.25);
        p.appuis = APPUI_CORPS;
        p.plantes = [0, 0];
        p.hanche("G", 0.1 + 0.3 * h(133), 0.15 + 0.2 * h(135), 0.5);
        p.hanche("D", 0.05 + 0.4 * h(137), 0.1 + 0.25 * h(139), 0.45);
        p.genou("G", 0.15 + 0.6 * h(141));
        p.genou("D", 0.1 + 0.5 * h(143));
        p.cheville("G", -0.45);
        p.cheville("D", -0.45);
        p.epaule("G", -0.1 + 0.4 * h(145), 0.4 + 0.7 * h(147), 0.3);
        p.epaule("D", -0.1 + 0.4 * h(149), 0.3 + 0.8 * h(151), 0.3);
        p.coude("G", 0.2 + 0.6 * h(153), 0.4);
        p.coude("D", 0.2 + 0.6 * h(155), 0.4);
        p.main("G", 0.5, 0.4);
        p.main("D", 0.5, 0.4);
        p.colonne("cou", -0.1);
        p.colonne("tete", 0, (h(157) < 0.5 ? 1 : -1) * 0.7, 0);
        break;
      }
      case "cache":
        // Accroupi, ramassé, la tête rentrée : se faire petit.
        this.accroupi(p, "pointes", s, t);
        p.colonne("poitrine", 0.35);
        p.colonne("tete", 0.1);
        for (const k of COTES) {
          p.epaule(k, 0.6, 0.15, -0.2);
          p.coude(k, 1.2, 0.3);
          p.viserMain(k, PO[`jambe${k}`], 0, 0.08, 0.12, 0.7);
        }
        break;
      case "reveil":
        this.passage(p, s, true);
        break;
      case "tire":
      case "tiree": {
        // Le ravisseur avance en tenant la captive par le poignet, bras tendu derrière lui ; elle résiste, penchée en arrière.
        this.marche(p, s, 0, 0);
        if (geste === "tire") {
          p.epaule("D", -0.55, 0.15, 0.2);
          p.coude("D", 0.15, 0);
          p.main("D", 1.2);
          p.colonne("cou", 0, -0.5);
        } else {
          p.epaule("G", 1.0, 0.1, 0);
          p.coude("G", 0.1, 0);
          p.colonne("poitrine", -0.15);
          p.colonne("tete", 0.2);
        }
        break;
      }
      default:
        this.debout(p, s, t, 0, 0);
    }
  }

  /** Garde : genoux fléchis, pied gauche devant, buste penché, poings ou arme levés. */
  private garde(p: Pose, s: Situation, t: number, arme: number): void {
    const g = this.graine;
    const balance = 0.03 * Math.sin(t * 3.1 + g);
    p.bassin(0.12, 0.25, balance);
    p.colonne("ventre", 0.08, -0.12);
    p.colonne("poitrine", 0.1, -0.1);
    p.colonne("cou", -0.12);
    p.colonne("tete", -0.05);
    p.hanche("G", 0.35, 0.1, 0.05);
    p.genou("G", 0.45);
    p.cheville("G", 0.15);
    p.hanche("D", -0.05, 0.12, 0.25);
    p.genou("D", 0.35);
    p.cheville("D", 0.2);
    p.poserPied("G", 0.07, 0.14, 1);
    p.poserPied("D", -0.08, -0.1, 1);
    const longue = arme === 3 || arme === 4;
    if (longue) {
      for (const k of COTES) {
        p.epaule(k, 0.55, k === "D" ? 0.15 : 0.05, k === "D" ? -0.2 : -0.5);
        p.coude(k, 1.5, 0.3);
      }
    } else {
      p.epaule("G", 0.75, 0.2, -0.3);
      p.coude("G", 1.7, 0.2);
      p.epaule("D", arme > 0 ? 0.9 : 0.7, 0.3, -0.2);
      p.coude("D", arme > 0 ? 1.5 : 1.75, 0.2);
    }
    p.main("G", arme > 0 && longue ? 1.4 : 1.35, 0.9);
    p.main("D", 1.4, 0.9);
    void s;
  }

  private passage(p: Pose, s: Situation, releve: boolean): void {
    const c: Cote = unite(hacher(s.id, 111)) < 0.5 ? "G" : "D";
    for (const k of COTES) {
      p.hanche(k, 1.75, 0.25, 0.2);
      p.genou(k, 2.05);
      p.cheville(k, 0.4);
    }
    p.colonne("ventre", 0.45);
    p.colonne("poitrine", 0.25);
    p.colonne("tete", 0.2);
    p.epaule(c, 0.9, 0.3, -0.1);
    p.coude(c, 0.15, 0.2);
    p.poignet(c, -0.9);
    p.viserMain(c, -1, SIGNE[c] * 0.15, 0, releve ? 0.02 : -0.02, 0.9, true);
    p.main(c, 0.1, 0.15, 0.12);
    const a: Cote = c === "G" ? "D" : "G";
    p.epaule(a, 0.6, 0.2, -0.2);
    p.coude(a, 0.6, 0.2);
    p.plantes = [1, 1];
  }

  // ——— Nage (D1) ———
  // Brasse tête hors de l'eau, en quatre temps sur un cycle (`phase`, un tour par mouvement) : traction des bras, ramené des mains
  // sous le menton avec les jambes qui se replient, coup de pied en fouet pendant que les bras repartent devant, glisse.
  // Qui sait à peine nager fait la nage du chien : buste redressé, bras qui battent sous l'eau tour à tour, jambes en pédalage.

  private nage(p: Pose, s: Situation): void {
    p.libre = true;
    p.plantes = [0, 0];
    const aise = s.nage ?? 0;
    if (aise < SAIT_NAGER) {
      this.nageDuChien(p, s, aise);
      return;
    }
    const u = (((s.phase / (Math.PI * 2)) % 1) + 1) % 1;
    // Ampleur : le bon nageur s'allonge davantage.
    const ample = 0.8 + 0.2 * aise;
    // Les bras : allongés devant (flexion ≈ 2,9) → écartés jusqu'aux épaules en tirant → mains sous le menton → relancés devant.
    const flexion = cles(u, [[0, 2.9], [0.12, 2.85], [0.4, 1.75], [0.55, 1.75], [0.75, 2.9], [1, 2.9]]);
    const abduction = cles(u, [[0, 0.15], [0.12, 0.25], [0.4, 0.95 * ample], [0.55, 0.3], [0.75, 0.12], [1, 0.15]]);
    const coude = cles(u, [[0, 0.05], [0.12, 0.15], [0.4, 1.1], [0.55, 1.9], [0.75, 0.15], [1, 0.05]]);
    const rot = cles(u, [[0, 0], [0.4, 0.6], [0.55, 0.9], [0.75, 0], [1, 0]]);
    // Les jambes : allongées pendant la traction, repliées talons vers les fesses, puis le fouet qui les rassemble.
    const hanche = cles(u, [[0, 0], [0.4, 0.05], [0.6, 0.85 * ample], [0.78, 0.1], [0.9, 0], [1, 0]]);
    const genou = cles(u, [[0, 0.05], [0.4, 0.15], [0.6, 2.0], [0.78, 0.4], [0.9, 0.05], [1, 0.05]]);
    const ecart = cles(u, [[0, 0.04], [0.4, 0.08], [0.6, 0.32], [0.72, 0.4], [0.86, 0.05], [1, 0.04]]);
    const pied = cles(u, [[0, -0.7], [0.5, -0.4], [0.62, 0.35], [0.75, 0.2], [0.86, -0.7], [1, -0.7]]);
    for (const c of COTES) {
      p.epaule(c, flexion, abduction, rot);
      p.coude(c, coude, 0.8);
      p.poignet(c, 0.25 * Math.sin(u * Math.PI * 2));
      p.main(c, 0.15, 0.2, 0.02);
      p.hanche(c, hanche, ecart, 0.45 * Math.min(1, genou));
      p.genou(c, genou);
      p.cheville(c, pied, -0.25);
    }
    // Le buste se soulève pendant la traction (on respire), s'allonge pendant la glisse ; la tête reste hors de l'eau.
    const souleve = cles(u, [[0, 0], [0.3, 0.12], [0.5, 0.3], [0.7, 0.1], [1, 0]]);
    p.coucher(Math.PI / 2 - 0.22 - souleve, 0);
    p.racine.y = 0.05 * souleve;
    p.colonne("poitrine", -0.05 - 0.1 * souleve);
    p.colonne("cou", -0.65 - 0.2 * souleve);
    p.colonne("tete", -0.3);
  }

  private nageDuChien(p: Pose, s: Situation, aise: number): void {
    // Celui qui ne sait pas nager se débat davantage : gestes plus amples et moins réguliers.
    const panique = 1 - aise / SAIT_NAGER;
    const a = s.phase;
    const tremble = 0.12 * panique * Math.sin(a * 2.7 + this.id);
    p.coucher(Math.PI / 2 - 0.75 - 0.2 * panique, 0.08 * Math.sin(a));
    for (const c of COTES) {
      const phi = a + (c === "G" ? 0 : Math.PI);
      p.epaule(c, 1.35 + 0.45 * Math.sin(phi) + tremble, 0.25 + 0.1 * panique, 0.2);
      p.coude(c, 0.9 + 0.5 * Math.cos(phi), 0.3);
      p.poignet(c, 0.4 + 0.3 * Math.sin(phi));
      p.main(c, 0.35, 0.3, 0.03);
      // Pédalage, jambes opposées aux bras.
      p.hanche(c, 0.65 + 0.4 * Math.sin(phi + Math.PI / 2), 0.15, 0.1);
      p.genou(c, 1.0 + 0.6 * Math.sin(phi + Math.PI));
      p.cheville(c, -0.3);
    }
    p.racine.y = 0.04 * Math.sin(a * 2);
    p.colonne("poitrine", 0.05);
    p.colonne("cou", -0.95 - 0.1 * panique);
    p.colonne("tete", -0.35 + 0.1 * Math.sin(a * 1.3) * panique);
  }

  // ——— Enfant porté (D1) ———

  private porte(p: Pose, v: string, _s: Situation, t: number): void {
    const g = this.graine;
    p.libre = true;
    p.plantes = [0, 0];
    switch (v) {
      case "hanche":
        // À califourchon sur la hanche, une jambe devant sa mère, l'autre derrière, un bras sur elle.
        p.hanche("G", 1.25, 0.85, 0.45);
        p.hanche("D", 1.2, 0.9, 0.45);
        p.genou("G", 1.7);
        p.genou("D", 1.6);
        p.cheville("G", -0.2);
        p.cheville("D", -0.2);
        p.colonne("ventre", 0.08);
        p.epaule("G", 1.0, 0.6, -0.2);
        p.coude("G", 1.2, 0.2);
        p.epaule("D", 0.65, 0.1, -0.3);
        p.coude("D", 1.4, 0.2);
        p.main("G", 0.7);
        p.main("D", 0.9);
        break;
      case "genoux":
        // Assis sur les cuisses de sa mère, jambes devant.
        p.bassin(-0.15);
        p.hanche("G", 1.45, 0.2, 0.2);
        p.hanche("D", 1.4, 0.22, 0.2);
        p.genou("G", 0.45 + 0.2 * onde(t, g, 0.3));
        p.genou("D", 0.55);
        p.colonne("ventre", 0.15);
        for (const c of COTES) {
          p.epaule(c, 0.5 + 0.2 * onde(t, g + SIGNE[c], 0.25), 0.15);
          p.coude(c, 0.9, 0.4);
        }
        p.main("G", 0.7);
        p.main("D", 0.7);
        break;
      case "couche":
        p.coucher(0, -Math.PI / 2);
        for (const c of COTES) {
          p.hanche(c, 1.3, 0.1);
          p.genou(c, 1.6);
          p.epaule(c, 1.3, 0.1);
          p.coude(c, 1.6);
        }
        p.colonne("cou", 0.2);
        p.libre = false;
        p.appuis = APPUI_CORPS;
        break;
      default:
        // Au creux du bras : jambes repliées, bras pliés, mains fermées.
        for (const c of COTES) {
          p.hanche(c, 1.25 + 0.15 * onde(t, g + SIGNE[c], 0.35), 0.45, 0.4);
          p.genou(c, 1.55);
          p.cheville(c, 0.2);
          p.epaule(c, 0.9 + 0.3 * onde(t, g + 2 * SIGNE[c], 0.3), 0.45);
          p.coude(c, 1.9);
        }
        p.colonne("cou", 0.1, 0.4);
        p.main("G", 1.1);
        p.main("D", 1.1);
    }
  }

  // ——— Le vivant (E1) : respiration, petits mouvements, regard, gestes ———

  private vivre(corps: Corps, s: Situation, t: number, dt: number, p: Pose): void {
    // Un mort ne respire plus et ne bouge plus (0.42.0).
    if (s.combat?.mort) return;
    const g = this.graine;
    // Respiration : environ 40/min chez le nourrisson, 25 vers 3 ans, 12-20 chez l'adulte (propre à chacun), plus vite avec
    // l'effort, plus lente et profonde en dormant (Fleming et al., 2011, Lancet).
    const base = s.age < 1 ? 40 : s.age < 3 ? 30 : s.age < 7 ? 23 : s.age < 13 ? 19 : 12 + 6 * unite(hacher(s.id, 113));
    // La peur et la fièvre accélèrent le souffle (F1).
    const parMinute = base * (1 + 0.6 * this.effort + 0.35 * s.peur + 0.25 * s.malade) * (s.dort ? 0.85 : 1);
    this.phaseSouffle = (this.phaseSouffle + dt * parMinute / 60 * Math.PI * 2) % (Math.PI * 2);
    // Inspiration plus courte que l'expiration.
    const souffle = Math.sin(this.phaseSouffle - 0.35 * Math.sin(this.phaseSouffle));
    const ampl = (1 + 0.8 * this.effort) * (s.dort ? 1.3 : 1);
    const ventre = s.age < 3 || s.dort ? 1.5 : 1;
    p.ajouter("poitrine", -0.018 * ampl * souffle, 0, 0);
    p.ajouter("ventre", 0.012 * ampl * ventre * souffle, 0, 0);
    p.ajouter("cou", 0.008 * ampl * souffle, 0, 0);
    if (corps.idx[PO.claviculeG] >= 0) {
      p.ajouter("claviculeG", 0, 0, 0.012 * ampl * souffle);
      p.ajouter("claviculeD", 0, 0, -0.012 * ampl * souffle);
    }
    const famille_ = famille(this.cible);
    // Frissons (F1) : tremblement rapide et irrégulier du buste et des épaules quand le froid mord, même endormi.
    const frisson = lisser(0.45, 0.9, s.froid);
    if (frisson > 0 && famille_ !== "eau") {
      const f = 0.01 * frisson * (Math.sin(t * 57 + g) * 0.6 + Math.sin(t * 43.7 + 2 * g) * 0.4) * (0.6 + 0.4 * Math.max(0, Math.sin(t * 0.9 + g)));
      p.ajouter("poitrine", f, 0, 0.5 * f);
      p.ajouter("cou", -f, 0, 0);
      if (corps.idx[PO.claviculeG] >= 0) {
        p.ajouter("claviculeG", 0, 0, 1.5 * f);
        p.ajouter("claviculeD", 0, 0, -1.5 * f);
      }
    }
    if (s.dort) return;
    // Petits mouvements propres à chacun (plus chez l'enfant, l'anxieux ; moins chez qui est las).
    const vif = (0.7 + 0.6 * unite(hacher(s.id, 115))) * (s.age < 12 ? 1.6 : 1) * (0.7 + 0.6 * s.nevrosisme) * (1 - 0.45 * abattement(s));
    if (famille_ !== "eau") {
      p.ajouter("poitrine", 0.012 * vif * onde(t, g + 13, 0.21), 0.02 * vif * onde(t, g + 17, 0.13), 0.01 * vif * onde(t, g + 19, 0.17));
      for (const c of COTES) {
        const k = SIGNE[c];
        p.ajouter(`bras${c}`, 0.025 * vif * onde(t, g + 21 * k, 0.19), 0, 0.015 * vif * onde(t, g + 23 * k, 0.23));
        p.ajouter(`avantBras${c}`, -0.035 * vif * onde(t, g + 29 * k, 0.27), 0, 0);
        const o = (c === "G" ? 0 : 1) * DOIGTS_PAR_MAIN;
        for (let f = 0; f < 5; f++) p.doigts[o + 3 * f + 1] += 0.08 * vif * onde(t, g + 31 * k + f, 0.31);
      }
    }
    // Regard : tête et buste vers ce qu'il regarde, sans à-coups.
    this.regarder(corps, s, t, dt, p);
    // Gestes d'attente (debout ou assis, sans travailler) ; en parlant, la main accompagne la parole (H1).
    const libre = (famille_ === "debout" && this.cible === "debout") || (famille_ === "sol" && this.cible.startsWith("assis"));
    if (libre && s.converse) {
      this.geste = null;
      this.causer(s, t, p);
    } else if (libre) this.gesticuler(s, t, p);
    else this.geste = null;
  }

  /**
   * Conversation (H1) : celui qui parle bouge la main dominante (les deux s'il est extraverti) en battements qui suivent la
   * parole, paume ouverte vers l'autre (McNeill 1992) ; celui qui écoute hoche la tête de temps en temps.
   */
  private causer(s: Situation, t: number, p: Pose): void {
    const g = this.graine;
    const w = ease(Math.min(1, s.parle * 1.4));
    if (w < 0.02) {
      // Il écoute : un hochement de temps en temps.
      const h = Math.max(0, Math.sin(t * 0.7 + g)) ** 12;
      p.ajouter("tete", 0.09 * h * Math.sin(t * 9), 0, 0);
      return;
    }
    const dominante: Cote = unite(hacher(s.id, 159)) < 0.88 ? "D" : "G";
    const ampleur = 0.55 + 0.7 * s.extraversion;
    // Battements : rapprochés et irréguliers (2 à 3 par seconde), avec des temps forts.
    const battement = Math.max(0, Math.sin(t * 5.3 + g)) * (0.5 + 0.5 * Math.max(0, onde(t, g + 61, 1.3)));
    for (const c of COTES) {
      const k = c === dominante ? 1 : lisser(0.55, 0.95, s.extraversion) * 0.7;
      if (k <= 0) continue;
      const wk = w * k;
      const i = c === "G" ? 0 : 1;
      for (const cible of p.mains[i]) cible.poids *= 1 - wk;
      p.ajouter(`bras${c}`, -(0.35 + 0.15 * onde(t, g + 67 * SIGNE[c], 0.5)) * ampleur * wk, 0, SIGNE[c] * 0.12 * wk);
      p.ajouter(`avantBras${c}`, -(0.9 + 0.25 * battement) * ampleur * wk, SIGNE[c] * 0.7 * wk, 0);
      p.ajouter(`main${c}`, 0, 0, -SIGNE[c] * 0.25 * battement * wk);
      p.main(c, 0.15 + 0.25 * (1 - wk) + 0.15 * onde(t, g + 71, 0.9) * wk, 0.2, 0.08 * wk);
    }
    p.ajouter("tete", 0.04 * battement * w, 0, 0);
    p.ajouter("poitrine", -0.02 * w * ampleur, 0, 0);
  }

  private phaseSouffle = 0;

  private regarder(corps: Corps, s: Situation, t: number, dt: number, p: Pose): void {
    let lacet = 0.25 * onde(t, this.graine + 37, 0.11);
    let tangage = 0.06 * onde(t, this.graine + 41, 0.09);
    let poids = 0;
    if (this.regard) {
      // Direction du point regardé dans le repère du corps (taille 1).
      corps.maillage.updateMatrixWorld();
      V.copy(this.regard);
      M.copy(corps.maillage.matrixWorld).invert();
      V.applyMatrix4(M);
      const tete = corps.os[corps.idx[PO.tete]];
      tete.getWorldPosition(V2);
      const dx = V.x - V2.x;
      const dz = V.z - V2.z;
      lacet = Math.atan2(dx, dz);
      tangage = -Math.atan2(V.y - V2.y - 0.06, Math.hypot(dx, dz));
      poids = 1;
    }
    lacet = Math.max(-1.6, Math.min(1.6, lacet));
    tangage = Math.max(-0.6, Math.min(0.7, tangage));
    // Mouvement de tête rapide puis lent (environ 0,4 s), comme une saccade suivie.
    const k = Math.min(1, dt * 5);
    this.lacet += (lacet - this.lacet) * k;
    this.tangage += (tangage - this.tangage) * k;
    this.poidsRegard += (poids - this.poidsRegard) * Math.min(1, dt * 3);
    const l = this.lacet;
    const ta = this.tangage;
    const torse = this.ecoute ? 0.3 : 0.15;
    p.ajouter("poitrine", 0, l * torse, 0);
    p.ajouter("cou", ta * 0.4, l * 0.35, 0);
    p.ajouter("tete", ta * 0.6, l * (0.65 - torse), 0);
    void s;
  }

  private gesticuler(s: Situation, t: number, p: Pose): void {
    if (!this.geste && t > this.prochainGeste) {
      const h = unite(hacher(s.id, Math.floor(t), 117));
      // Le froid fait frotter bras et mains ; l'anxieux touche son visage et ses cheveux (gestes d'auto-apaisement, Ekman et
      // Friesen 1969) ; las, on s'étire (F1).
      const poids: [string, number][] = [
        ["tete", 1], ["visage", 1 + 1.5 * s.nevrosisme + s.peur], ["cheveux", 0.8 + s.nevrosisme], ["bras", 1 + 5 * s.froid],
        ["etirer", 0.8 + 1.5 * s.fatigue], ["derriere", 1 + s.peur], ["mains", 1 + 5 * s.froid],
      ];
      let total = 0;
      for (const [, w] of poids) total += w;
      let r = h * total;
      let nom = "tete";
      for (const [n, w] of poids) {
        r -= w;
        if (r <= 0) {
          nom = n;
          break;
        }
      }
      const cote: Cote = unite(hacher(s.id, Math.floor(t), 119)) < 0.62 ? "D" : "G";
      this.geste = { nom, cote, debut: t, duree: nom === "etirer" ? 3.5 : nom === "derriere" ? 2.2 : 2.6 };
      // Un geste toutes les 15 à 60 s, plus souvent chez l'enfant.
      // Plus souvent chez l'enfant, l'anxieux, qui a peur ou froid ; moins chez qui est abattu.
      this.prochainGeste = t + (15 + 45 * unite(hacher(s.id, Math.floor(t), 121))) * (s.age < 12 ? 0.6 : 1)
        * (1.3 - 0.6 * s.nevrosisme) / (1 + 1.5 * Math.max(s.froid, s.peur)) * (1 + abattement(s));
    }
    const g = this.geste;
    if (!g) return;
    const u = (t - g.debut) / g.duree;
    if (u >= 1) {
      this.geste = null;
      return;
    }
    // Enveloppe : la main y va, reste, revient.
    const w = ease(Math.min(1, u * 3.5)) * ease(Math.min(1, (1 - u) * 3.5));
    const sc = SIGNE[g.cote];
    const frotte = Math.sin((t - g.debut) * 14);
    switch (g.nom) {
      case "tete":
        p.viserMain(g.cote, PO.tete, sc * 0.055, 0.1 + 0.008 * frotte, -0.01, w * 2);
        p.main(g.cote, 0.5 + 0.3 * frotte);
        p.ajouter("tete", 0, 0, -sc * 0.12 * w);
        break;
      case "visage":
        p.viserMain(g.cote, PO.tete, sc * 0.01 + 0.006 * frotte, 0.04, 0.095, w * 2);
        p.main(g.cote, 0.6);
        p.ajouter("tete", 0.12 * w, 0, 0);
        break;
      case "cheveux":
        p.viserMain(g.cote, PO.tete, sc * 0.05, 0.12, 0.04 - 0.1 * u, w * 2);
        p.main(g.cote, 0.35);
        p.ajouter("tete", -0.1 * w, 0, -sc * 0.08 * w);
        break;
      case "bras":
        p.viserMain(g.cote, PO[`bras${g.cote === "G" ? "D" : "G"}`], 0, -0.1 + 0.012 * frotte, 0.035, w * 2);
        p.main(g.cote, 0.55 + 0.25 * frotte);
        break;
      case "etirer":
        for (const c of COTES) p.ajouter(`bras${c}`, -2.6 * w, 0, SIGNE[c] * 0.2 * w);
        p.ajouter("poitrine", -0.15 * w, 0, 0);
        p.ajouter("tete", -0.2 * w, 0, 0);
        p.mains[0].length = p.mains[1].length = 0;
        break;
      case "derriere":
        p.ajouter("poitrine", 0, sc * 0.35 * w, 0);
        p.ajouter("cou", 0, sc * 0.4 * w, 0);
        p.ajouter("tete", 0, sc * 0.35 * w, 0);
        break;
      default:
        for (const c of COTES) p.viserMain(c, PO.poitrine, SIGNE[c] * 0.015 + 0.01 * frotte * SIGNE[c], -0.11, 0.13, w * 2);
        p.main("G", 0.3);
        p.main("D", 0.3);
    }
  }
}

/** Les bras d'une mère qui porte son enfant (D1), selon l'âge de l'enfant et sa propre posture. */
function porterEnfant(p: Pose, age: number, posture: string, allaite = false): void {
  if (age < 0.8 || allaite) {
    // Au creux du bras gauche, l'avant-bras sous l'enfant, la main droite par-dessus.
    p.clavicule("G", 0.03, 0.06);
    p.clavicule("D", 0.02, 0.08);
    p.epaule("G", 0.32, 0.38, -0.85);
    p.coude("G", 1.55, 1.1);
    p.poignet("G", 0.15);
    p.main("G", 0.6, 0.5);
    p.epaule("D", 0.55, 0.05, -0.55);
    p.coude("D", 1.35, 0.2);
    p.main("D", 0.4, 0.4);
    // Au sein, elle penche la tête vers lui.
    p.colonne("tete", allaite ? 0.5 : 0.3, 0.15);
    if (allaite) p.colonne("cou", 0.2, 0.1);
    p.mains[0].length = 0;
    p.mains[1].length = 0;
    return;
  }
  if (posture === "assis" || posture === "travail" || posture === "accroupi") {
    // Sur les genoux : les deux bras autour de l'enfant.
    for (const c of COTES) {
      p.epaule(c, 0.65, 0.2, -0.35);
      p.coude(c, 1.35, 0.2);
      p.main(c, 0.5);
    }
    p.mains[0].length = 0;
    p.mains[1].length = 0;
    return;
  }
  // Sur la hanche gauche : bras gauche autour de l'enfant, hanche gauche relevée, buste penché à droite.
  // 0.45.10 : bras écarté (le coude passe en dehors de l'enfant) pour l'entourer, au lieu de le traverser.
  p.epaule("G", -0.4, 0.55, -0.2);
  p.coude("G", 1.2, 0.3);
  p.poignet("G", 0.2);
  p.main("G", 0.7, 0.5);
  p.mains[0].length = 0;
  p.ajouter("bassin", 0, 0.05, 0.07);
  p.ajouter("ventre", 0, 0, -0.06);
  p.ajouter("poitrine", 0, 0, -0.04);
}

const V = new THREE.Vector3();
const V2 = new THREE.Vector3();
const V3 = new THREE.Vector3();
const V4 = new THREE.Vector3();
const M = new THREE.Matrix4();
const QA = new THREE.Quaternion();
const QB = new THREE.Quaternion();
const QC = new THREE.Quaternion();
const QD = new THREE.Quaternion();
const RACINE = new THREE.Vector3();
const QPIED = new THREE.Quaternion();

/** Hauteur du sol en un point du corps. */
const solEn = (sol: SolLocal | null, x: number, z: number) => (sol ? sol.ax * x + sol.az * z : 0);

/**
 * Pose le corps : rotations (doigts compris), descente jusqu'aux appuis, pieds et mains par cinématique inverse. Les matrices des
 * os sont à jour au retour.
 */
export function poser(corps: Corps, p: Pose, sol: SolLocal | null): void {
  // Doigts : flexion autour de l'axe propre à chaque phalange.
  const dg = corps.doigts;
  if (dg) {
    for (let m = 0; m < 2; m++) {
      const c = m === 0 ? "G" : "D";
      const o = m * DOIGTS_PAR_MAIN;
      for (let f = 0; f < 5; f++) {
        for (let k = 0; k < 3; k++) {
          const i = PO[`${DOIGTS[f]}${k + 1}${c}` as NomPose];
          const axe = dg.flexion[i];
          if (!axe) continue;
          QA.setFromAxisAngle(axe, p.doigts[o + 3 * f + k]);
          if (k === 0 && f > 0) {
            QB.setFromAxisAngle(dg.ecart[i]!, (f - 2.2) * p.doigts[o + 15] * (m === 0 ? 1 : -1));
            QA.premultiply(QB);
          }
          QA.toArray(p.q, 4 * i);
        }
      }
    }
  }
  RACINE.copy(p.racine);
  // Couché sur une pente : le corps épouse le sol (sinon une moitié flotte et l'autre s'enfonce).
  if (sol && p.appuis & APPUI_CORPS && !p.libre) {
    N_SOL.set(-sol.ax, 1, -sol.az).normalize();
    Q_SOL.setFromUnitVectors(HAUT, N_SOL);
    Q_B.fromArray(p.q, 4 * PO.bassin).premultiply(Q_SOL).toArray(p.q, 4 * PO.bassin);
  }
  corps.appliquer(p.q, RACINE);
  corps.armature.updateMatrixWorld(true);
  if (!p.libre) {
    // Descente : le plus bas des appuis qui portent touche le sol. Pieds posés : celui du sol le plus bas décide (l'autre plie).
    let bas = Infinity;
    const piedBas = [Infinity, Infinity];
    // Couché, c'est la peau qui touche : le point le plus bas d'un échantillon des sommets du maillage. Les points d'appui, pensés
    // debout, laissaient flotter le dormeur sur le dos et enfonçaient à moitié celui sur le ventre.
    const couche = (p.appuis & APPUI_CORPS) !== 0;
    if (couche) bas = plusBasDuMaillage(corps, sol);
    for (const a of corps.appuis) {
      const os = corps.os[a.os];
      if (couche && a.famille === 3) continue;
      V.copy(a.point).applyMatrix4(os.matrixWorld);
      const d = V.y - solEn(sol, V.x, V.z);
      if (a.famille === 0) {
        const k = corps.os[a.os] === corps.os[corps.idx[PO.piedG]] ? 0 : 1;
        piedBas[k] = Math.min(piedBas[k], d);
        continue;
      }
      if (p.appuis & (1 << a.famille)) bas = Math.min(bas, d);
    }
    if (p.appuis & APPUI_PIEDS) {
      const plantes = p.plantes[0] > 0.5 && p.plantes[1] > 0.5;
      bas = Math.min(bas, plantes ? Math.max(piedBas[0], piedBas[1]) : Math.min(piedBas[0], piedBas[1]));
    }
    if (Number.isFinite(bas)) {
      corps.os[0].position.y -= bas;
      decaler(corps, -bas);
    }
    // Pieds : posés (ou au moins jamais sous le sol).
    for (let k = 0; k < 2; k++) piedAuSol(corps, p, k, sol);
  }
  // Mains : vers leurs cibles, et jamais sous le sol.
  for (let k = 0; k < 2; k++) {
    mainVers(corps, p, k, sol);
    // Couché, on ne relève pas la main au-dessus du sol : l'IK pliait le coude et dressait l'avant-bras en l'air.
    if (!p.libre && !(p.appuis & APPUI_CORPS)) mainHorsDuSol(corps, k, sol);
  }
}

/** Sommets échantillonnés par maillage (environ 600 : assez pour ne pas manquer un coude ni une fesse) : positions, os et poids. */
interface Echantillon { pos: Float32Array; os: Uint16Array; poids: Float32Array; n: number }
const ECHANTILLONS = new WeakMap<THREE.BufferGeometry, Echantillon>();
let MATRICES = new Float32Array(16 * 64);
const MAT = new THREE.Matrix4();

function echantillon(g: THREE.BufferGeometry): Echantillon {
  let e = ECHANTILLONS.get(g);
  if (e) return e;
  const p = g.getAttribute("position");
  const si = g.getAttribute("skinIndex");
  const sw = g.getAttribute("skinWeight");
  const pas = Math.max(1, Math.floor(p.count / 600));
  const n = Math.ceil(p.count / pas);
  e = { pos: new Float32Array(3 * n), os: new Uint16Array(4 * n), poids: new Float32Array(4 * n), n };
  for (let i = 0, k = 0; i < p.count; i += pas, k++) {
    e.pos[3 * k] = p.getX(i);
    e.pos[3 * k + 1] = p.getY(i);
    e.pos[3 * k + 2] = p.getZ(i);
    for (let j = 0; j < 4; j++) {
      e.os[4 * k + j] = si.getComponent(i, j);
      e.poids[4 * k + j] = sw.getComponent(i, j);
    }
  }
  ECHANTILLONS.set(g, e);
  return e;
}

/** Point le plus bas de la peau, au-dessus du sol (calcul du skinning sur l'échantillon, comme le fait le GPU). */
function plusBasDuMaillage(corps: Corps, sol: SolLocal | null): number {
  const m = corps.maillage;
  const sq = m.skeleton;
  const e = echantillon(m.geometry);
  if (MATRICES.length < 16 * sq.bones.length) MATRICES = new Float32Array(16 * sq.bones.length);
  for (let b = 0; b < sq.bones.length; b++) MAT.multiplyMatrices(sq.bones[b].matrixWorld, sq.boneInverses[b]).toArray(MATRICES, 16 * b);
  const M = MATRICES;
  const ax = sol ? sol.ax : 0;
  const az = sol ? sol.az : 0;
  let bas = Infinity;
  for (let k = 0; k < e.n; k++) {
    const px = e.pos[3 * k], py = e.pos[3 * k + 1], pz = e.pos[3 * k + 2];
    let x = 0, y = 0, z = 0;
    for (let j = 0; j < 4; j++) {
      const w = e.poids[4 * k + j];
      if (w === 0) continue;
      const o = 16 * e.os[4 * k + j];
      x += w * (M[o] * px + M[o + 4] * py + M[o + 8] * pz + M[o + 12]);
      y += w * (M[o + 1] * px + M[o + 5] * py + M[o + 9] * pz + M[o + 13]);
      z += w * (M[o + 2] * px + M[o + 6] * py + M[o + 10] * pz + M[o + 14]);
    }
    const d = y - ax * x - az * z;
    if (d < bas) bas = d;
  }
  return bas;
}

function mainHorsDuSol(corps: Corps, k: number, sol: SolLocal | null): void {
  if (corps.idx[MAINS[k][2]] < 0) return;
  const ic = corps.os[corps.idx[MAINS[k][2]]];
  V4.copy(corps.paume);
  if (k === 1) V4.x = -V4.x;
  V4.multiplyScalar(1.8);
  corps.versOs(corps.idx[MAINS[k][2]], V4).applyMatrix4(ic.matrixWorld);
  const d = V4.y - solEn(sol, V4.x, V4.z) - 0.012;
  if (d >= 0) return;
  const [ia, ib] = MAINS[k].map((i) => corps.os[corps.idx[i]]);
  V2.setFromMatrixPosition(ic.matrixWorld);
  V2.y -= d;
  deuxOs(ia, ib, ic, V2, 1);
}

/** Décale toutes les matrices du squelette en hauteur (la descente ne change pas les rotations). */
function decaler(corps: Corps, dy: number): void {
  for (const b of corps.os) b.matrixWorld.elements[13] += dy;
}

const PIEDS: [number, number, number][] = [[PO.cuisseG, PO.jambeG, PO.piedG], [PO.cuisseD, PO.jambeD, PO.piedD]];
const MAINS: [number, number, number][] = [[PO.brasG, PO.avantBrasG, PO.mainG], [PO.brasD, PO.avantBrasD, PO.mainD]];

function piedAuSol(corps: Corps, p: Pose, k: number, sol: SolLocal | null): void {
  const [ia, ib, ic] = PIEDS[k].map((i) => corps.os[corps.idx[i]]);
  // Plus bas point de la plante, et l'écart au sol.
  let ecart = Infinity;
  for (const a of corps.appuis) {
    if (a.famille !== 0 || corps.os[a.os] !== ic) continue;
    V.copy(a.point).applyMatrix4(ic.matrixWorld);
    ecart = Math.min(ecart, V.y - solEn(sol, V.x, V.z));
  }
  if (!Number.isFinite(ecart)) return;
  const plante = p.plantes[k];
  // Où le pied veut aller (cible fixe sur le sol) ; sinon il reste où la pose le met.
  V2.setFromMatrixPosition(ic.matrixWorld);
  const cheville = V2.clone();
  let poidsXZ = 0;
  V3.set(0, 0, 0);
  for (const c of p.pieds[k]) {
    V3.x += c.x * c.poids;
    V3.z += c.z * c.poids;
    poidsXZ += c.poids;
  }
  const descend = plante > 0.01 ? ecart * plante : Math.min(0, ecart);
  if (Math.abs(descend) < 1e-4 && poidsXZ < 0.01) return;
  V2.y -= descend;
  if (poidsXZ > 0.01) {
    const w = Math.min(1, poidsXZ) * plante;
    V3.multiplyScalar(1 / poidsXZ);
    // La cible est celle du talon : on garde l'écart cheville-plante.
    V2.x += (V3.x - V2.x) * w;
    V2.z += (V3.z - V2.z) * w;
    V2.y += (solEn(sol, V2.x, V2.z) - solEn(sol, cheville.x, cheville.z)) * w;
  }
  // Le pied garde son orientation dans le monde (la plante reste à plat).
  rotationMonde(ic.matrixWorld, QPIED);
  deuxOs(ia, ib, ic, V2, 1);
  garderOrientation(ic, QPIED);
}

function mainVers(corps: Corps, p: Pose, k: number, sol: SolLocal | null): void {
  const l = p.mains[k];
  if (l.length === 0) return;
  let poids = 0;
  V3.set(0, 0, 0);
  for (const c of l) {
    if (c.os >= 0) {
      const i = corps.idx[c.os];
      if (i < 0) continue;
      V4.set(c.x, c.y, c.z);
      corps.versOs(i, V4).applyMatrix4(corps.os[i].matrixWorld);
    } else {
      V4.set(c.x, c.y, c.z);
      V4.x += corps.os[0].position.x;
      V4.z += corps.os[0].position.z;
    }
    if (c.sol) V4.y = c.y + solEn(sol, V4.x, V4.z);
    V3.addScaledVector(V4, c.poids);
    poids += c.poids;
  }
  if (poids < 0.01) return;
  V3.multiplyScalar(1 / poids);
  const [ia, ib, ic] = MAINS[k].map((i) => corps.os[corps.idx[i]]);
  // La cible est celle de la paume : le poignet garde son écart à la paume.
  V4.copy(corps.paume);
  if (k === 1) V4.x = -V4.x;
  corps.versOs(corps.idx[MAINS[k][2]], V4).applyMatrix4(ic.matrixWorld);
  V2.setFromMatrixPosition(ic.matrixWorld);
  V3.add(V2.sub(V4));
  deuxOs(ia, ib, ic, V3, Math.min(1, poids));
}

/**
 * Cinématique inverse analytique à deux os (cuisse-jambe ou bras-avant-bras) : plie l'articulation du milieu pour que le bout
 * (cheville, poignet) atteigne `cible`, dans le plan où il plie déjà, puis tourne le tout vers elle. `poids` : 0 à 1.
 */
function deuxOs(A: THREE.Bone, B: THREE.Bone, C: THREE.Bone, cible: THREE.Vector3, poids: number): void {
  // Les matrices des os sont à jour : on y lit positions et orientations sans remonter la chaîne des parents.
  const a = IK_A.setFromMatrixPosition(A.matrixWorld);
  const b = IK_B.setFromMatrixPosition(B.matrixWorld);
  const c = IK_C.setFromMatrixPosition(C.matrixWorld);
  const lab = a.distanceTo(b);
  const lcb = b.distanceTo(c);
  const lat = Math.max(0.01, Math.min(a.distanceTo(cible), (lab + lcb) * 0.999));
  const ac = IK_1.subVectors(c, a);
  const ab = IK_2.subVectors(b, a);
  const ba = IK_3.subVectors(a, b);
  const bc = IK_4.subVectors(c, b);
  const ac_ab_0 = angle(ac, ab);
  const ba_bc_0 = angle(ba, bc);
  const ac_ab_1 = Math.acos(Math.max(-1, Math.min(1, (lcb * lcb - lab * lab - lat * lat) / (-2 * lab * lat))));
  const ba_bc_1 = Math.acos(Math.max(-1, Math.min(1, (lat * lat - lab * lab - lcb * lcb) / (-2 * lab * lcb))));
  const axe0 = IK_5.crossVectors(ac, ab);
  if (axe0.lengthSq() < 1e-12) return;
  axe0.normalize();
  IK_QA0.copy(A.quaternion);
  IK_QB0.copy(B.quaternion);
  // Plier.
  rotationMonde(A.matrixWorld, QA);
  rotationMonde(B.matrixWorld, QB);
  QC.setFromAxisAngle(IK_6.copy(axe0).applyQuaternion(QA.invert()), ac_ab_1 - ac_ab_0);
  A.quaternion.multiply(QC);
  QC.setFromAxisAngle(IK_6.copy(axe0).applyQuaternion(QB.invert()), ba_bc_1 - ba_bc_0);
  B.quaternion.multiply(QC);
  chaine(A, B, C);
  // Tourner vers la cible.
  c.setFromMatrixPosition(C.matrixWorld);
  const ac2 = c.sub(a);
  const at = IK_1.subVectors(cible, a);
  const axe1 = IK_2.crossVectors(ac2, at);
  if (axe1.lengthSq() > 1e-12) {
    axe1.normalize();
    rotationMonde(A.matrixWorld, QA);
    QC.setFromAxisAngle(axe1.applyQuaternion(QA.invert()), angle(ac2, at));
    A.quaternion.multiply(QC);
  }
  if (poids < 1) {
    A.quaternion.slerpQuaternions(IK_QA0, QD.copy(A.quaternion), poids);
    B.quaternion.slerpQuaternions(IK_QB0, QD.copy(B.quaternion), poids);
  }
  A.updateMatrixWorld(true);
}

const IK_A = new THREE.Vector3();
const IK_B = new THREE.Vector3();
const IK_C = new THREE.Vector3();
const IK_1 = new THREE.Vector3();
const IK_2 = new THREE.Vector3();
const IK_3 = new THREE.Vector3();
const IK_4 = new THREE.Vector3();
const IK_5 = new THREE.Vector3();
const IK_6 = new THREE.Vector3();
const IK_QA0 = new THREE.Quaternion();
const IK_QB0 = new THREE.Quaternion();
const IK_S = new THREE.Vector3();
const IK_P = new THREE.Vector3();

function angle(u: THREE.Vector3, v: THREE.Vector3): number {
  return Math.acos(Math.max(-1, Math.min(1, u.dot(v) / (u.length() * v.length() || 1))));
}

/** Orientation dans le repère des os, lue sur la matrice (sans échelle dans ce repère). */
function rotationMonde(m: THREE.Matrix4, q: THREE.Quaternion): THREE.Quaternion {
  m.decompose(IK_P, q, IK_S);
  return q;
}

/** Remet à jour les matrices d'une chaîne de trois os seulement (pas les doigts), pour lire où arrive le bout. */
function chaine(A: THREE.Bone, B: THREE.Bone, C: THREE.Bone): void {
  for (const o of [A, B, C]) {
    o.updateMatrix();
    o.matrixWorld.multiplyMatrices(o.parent!.matrixWorld, o.matrix);
  }
}

/** Remet un os à l'orientation `monde` qu'il avait (après que ses parents ont bougé). */
function garderOrientation(os: THREE.Bone, monde: THREE.Quaternion): void {
  rotationMonde(os.parent!.matrixWorld, QA);
  os.quaternion.copy(QA.invert().multiply(monde));
  os.updateMatrixWorld(true);
}

/**
 * Porte la paume d'une main (0 : gauche, 1 : droite) vers un point du repère des os (taille 1), après la pose : la mère qui tient
 * son enfant (D1). Les matrices sont à jour au retour.
 */
export function mainVersPoint(corps: Corps, k: number, cible: THREE.Vector3, poids = 1, coude?: THREE.Vector3): void {
  if (corps.idx[MAINS[k][2]] < 0) return;
  const [ia, ib, ic] = MAINS[k].map((i) => corps.os[corps.idx[i]]);
  V4.copy(corps.paume);
  if (k === 1) V4.x = -V4.x;
  corps.versOs(corps.idx[MAINS[k][2]], V4).applyMatrix4(ic.matrixWorld);
  V2.setFromMatrixPosition(ic.matrixWorld);
  V3.copy(cible).add(V2.sub(V4));
  deuxOs(ia, ib, ic, V3, poids);
  if (coude) tournerCoude(ia, ib, ic, coude);
}

/**
 * 0.45.10 : la main restant où elle est, le bras tourne autour de la ligne épaule-main jusqu'à ce que le coude aille du côté de
 * `vers` (ex. derrière l'enfant porté plutôt qu'à travers lui).
 */
function tournerCoude(A: THREE.Bone, B: THREE.Bone, C: THREE.Bone, vers: THREE.Vector3): void {
  const a = IK_A.setFromMatrixPosition(A.matrixWorld);
  const b = IK_B.setFromMatrixPosition(B.matrixWorld);
  const c = IK_C.setFromMatrixPosition(C.matrixWorld);
  const axe = IK_1.subVectors(c, a);
  if (axe.lengthSq() < 1e-10) return;
  axe.normalize();
  const vb = IK_2.subVectors(b, a);
  vb.addScaledVector(axe, -vb.dot(axe));
  const vp = IK_3.subVectors(vers, a);
  vp.addScaledVector(axe, -vp.dot(axe));
  if (vb.lengthSq() < 1e-10 || vp.lengthSq() < 1e-10) return;
  const ang = Math.atan2(IK_4.crossVectors(vb, vp).dot(axe), vb.dot(vp));
  rotationMonde(A.matrixWorld, QA);
  QC.setFromAxisAngle(IK_6.copy(axe).applyQuaternion(QA.invert()), ang);
  A.quaternion.multiply(QC);
  A.updateMatrixWorld(true);
}

/** Position d'un os des poses dans le repère des os (taille 1). */
export function osEn(corps: Corps, nom: NomPose, sortie: THREE.Vector3): THREE.Vector3 {
  return corps.os[corps.idx[PO[nom]]].getWorldPosition(sortie);
}
