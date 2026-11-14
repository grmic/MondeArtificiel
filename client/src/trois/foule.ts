import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";

/** Activités, dans l'ordre de l'énumération Activite du serveur (pour les textes « activite.X »). */
export const ACTIVITES = [
  "Repos", "Dormir", "Boire", "ChercherEau", "Cueillir", "Chasser", "ChercherNourriture", "SAbriter", "ChercherAbri", "RejoindrePartenaire",
  "Explorer", "Mort", "SAccoupler", "SuivreParent", "PorteParSaMere", "VeillerEnfant", "RejoindreProche", "EviterRival", "Migrer", "Fuir",
  "Raid", "RentrerDuRaid", "PartirEnExil", "Experimenter", "Fabriquer", "Veiller", "Soigner", "Nager", "SuivreRavisseur", "ChercherLeFeu",
  "Enseigner", "Visiter", "Rassembler", "Rentrer", "Demander", "Naviguer", "Pecher", "Cultiver",
] as const;
const A = Object.fromEntries(ACTIVITES.map((a, i) => [a, i])) as Record<(typeof ACTIVITES)[number], number>;

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
  /** Essais (?debug3d) : un figurant posé à un endroit précis, dans une posture donnée. */
  figurant?: { x: number; z: number; cap: number; posture: Posture };
}

export const CHAMPS = 22;

export function lireScene(n: number[]): HumainScene[] {
  const liste: HumainScene[] = [];
  for (let k = 0; k + CHAMPS <= n.length; k += CHAMPS) {
    liste.push({
      id: n[k], x: n[k + 1], y: n[k + 2], versX: n[k + 3], versY: n[k + 4], avance: n[k + 5] / 1000, parSeconde: n[k + 6] / 1e6,
      activite: n[k + 7], etat: n[k + 8], age: n[k + 9] / 10, femme: n[k + 10] === 1, teint: n[k + 11] / 1000, humeur: n[k + 12],
      peur: n[k + 13], communaute: n[k + 14], mere: n[k + 15], reserves: n[k + 16], endurance: n[k + 17] / 1000, tailleEcart: n[k + 18] / 100,
      enfants: n[k + 19], vetement: n[k + 20], usure: n[k + 21] / 1000,
    });
  }
  return liste;
}

export type Posture = "debout" | "marche" | "assis" | "couche" | "accroupi" | "nage" | "travail" | "porte";

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
  /** Chemin en cours (case de départ et d'arrivée) et où il en était au dernier pas du monde (0.41.2). */
  chemin: string;
  avancePrec: number;
  secondesPrec: number;
  /** Il devrait marcher mais n'avance plus d'un pas à l'autre (bloqué, en attente) : on ne le fait pas avancer. */
  bloque: boolean;
  /** Il marche vraiment (son corps se déplace) : seule raison d'animer la marche. */
  enMarche: boolean;
  /** Tourné vers l'Entité qui lui parle (0 à 1, pour la tête). */
  ecoute: number;
}

/**
 * Taille (m) selon l'âge et le sexe : croissance de l'enfance, puis stature adulte propre à chacun (sa taille dans la simulation,
 * écart-type d'environ 7 cm ; Roser et al., 2013), et un peu de tassement dans la vieillesse.
 */
function stature(age: number, femme: boolean, ecart: number): number {
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

function posture(s: HumainScene, enMouvement: boolean, dansLEau: boolean): Posture {
  if (s.etat & 32 || s.activite === A.PorteParSaMere) return "porte";
  if (dansLEau) return "nage";
  if (enMouvement) return "marche";
  switch (s.activite) {
    case A.Dormir:
    case A.SAbriter:
      return "couche";
    case A.Veiller:
    case A.VeillerEnfant:
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

/** Où se tient, dans sa case, quelqu'un qui ne marche pas : autour du camp, plus loin pour cueillir ou chasser. */
function decalage(s: HumainScene, secondes: number): [number, number] {
  const angle0 = unite(hacher(s.id, 7)) * Math.PI * 2;
  const r0 = unite(hacher(s.id, 9));
  let rayon: number;
  let derive = 0;
  switch (s.activite) {
    case A.Veiller:
      rayon = 2.4 + r0;
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

const NOMBRE_MAX = 700;

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
  /** Au-delà, ni silhouette ni corps : un point de vie (niveau de détail lointain). */
  porteeSilhouettes = 2500;
  private readonly points: THREE.InstancedMesh;

  constructor(private readonly relief: Relief) {
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
      m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      m.setColorAt(0, new THREE.Color());
      this.groupe.add(m);
      return m;
    });
    this.points = new THREE.InstancedMesh(new THREE.OctahedronGeometry(1, 0), new THREE.MeshBasicMaterial({ color: 0xffe2a0, fog: false }), NOMBRE_MAX);
    this.points.count = 0;
    this.points.frustumCulled = false;
    this.groupe.add(this.points);
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
        });
      }
    }
    for (const [id, c] of this.corps) if (c.vu !== this.image) this.corps.delete(id);
  }

  /** Position visée d'un humain à l'instant `secondes` du monde. */
  private cible(c: Corps, secondes: number): [number, number, boolean] {
    const s = c.s;
    const [ax, az] = this.relief.camp(s.x, s.y);
    if (s.versX >= 0) {
      const [bx, bz] = this.relief.camp(s.versX, s.versY);
      const parSeconde = c.bloque ? 0 : s.parSeconde;
      const f = Math.min(0.985, s.avance + parSeconde * Math.max(0, secondes - this.secondesScene));
      const lateral = (unite(hacher(s.id, 13)) - 0.5) * 6;
      const dx = bx - ax;
      const dz = bz - az;
      const l = Math.hypot(dx, dz) || 1;
      return [ax + dx * f - (dz / l) * lateral, az + dz * f + (dx / l) * lateral, parSeconde > 0 && f < 0.985];
    }
    const [ox, oz] = decalage(s, secondes);
    return [ax + ox, az + oz, false];
  }

  /** Avance l'animation : `dt` secondes réelles, `secondes` instant estimé du monde. */
  animer(dt: number, secondes: number, camera: THREE.Vector3): void {
    const rel = this.relief;
    for (const c of this.corps.values()) {
      const s = c.s;
      // Un dormeur (on lui parle en rêve), un bébé porté ou un nageur ne se retournent pas.
      const ecoute = s.id === this.interlocuteur && c.posture !== "couche" && c.posture !== "porte" && c.posture !== "nage";
      let [tx, tz] = s.figurant ? [s.figurant.x, s.figurant.z] : this.cible(c, secondes);
      if (Number.isNaN(c.x) || Math.hypot(tx - c.x, tz - c.z) > 400) {
        c.x = tx;
        c.z = tz;
      }
      // Celui à qui l'Entité parle s'arrête pour l'écouter.
      if (ecoute && !Number.isNaN(c.x)) [tx, tz] = [c.x, c.z];
      const dx = tx - c.x;
      const dz = tz - c.z;
      const d = Math.hypot(dx, dz);
      const k = Math.min(1, dt * 1.5);
      c.x += dx * k;
      c.z += dz * k;
      const pas = d * k;
      const v = dt > 0 ? pas / dt : 0;
      c.vitesse += (v - c.vitesse) * Math.min(1, dt * 4);
      // Il ne marche que si son corps se déplace vraiment (avec un peu d'hystérésis) : plus de marche sur place quand le monde
      // est arrêté, qu'il attend au bout de son chemin ou qu'il est bloqué.
      c.enMarche = c.enMarche ? c.vitesse > 0.12 : c.vitesse > 0.25;
      const voulu = ecoute ? Math.atan2(camera.x - c.x, camera.z - c.z) : d > 0.05 && c.vitesse > 0.15 ? Math.atan2(dx, dz) : null;
      if (voulu !== null) {
        const ecart = ((((voulu - c.cap + Math.PI) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) - Math.PI;
        c.cap += ecart * Math.min(1, dt * (ecoute ? 2.2 : 5));
      }
      c.ecoute += ((ecoute ? 1 : 0) - c.ecoute) * Math.min(1, dt * 3);
      c.taille = stature(s.age, s.femme, s.tailleEcart);
      // Les jambes avancent avec le sol parcouru : une foulée (deux pas) fait environ 0,8 fois la taille.
      if (c.enMarche) c.phase += Math.min(dt * 12, (pas * Math.PI * 2) / (0.82 * c.taille));
      const surf = rel.surface(c.x, c.z);
      const eau = !Number.isNaN(surf.eau) && surf.eau - surf.sol > 1.1;
      if (eau) c.phase += dt * Math.min(6, 1 + c.vitesse * 3);
      c.posture = s.figurant?.posture ?? posture(s, c.enMarche, eau && !(s.etat & 16));
      if (s.figurant) {
        c.cap = s.figurant.cap;
        if (s.figurant.posture === "marche") c.phase += dt * 5;
      }
      c.y = eau ? surf.eau - (s.etat & 16 ? -0.3 : 0.15) : surf.sol;
    }
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
      poser(0, m.copy(buste).multiply(t.makeScale(large, 1, 0.75)), c.peau);
      poser(1, m.copy(buste).multiply(t.makeTranslation(0, 0.935, 0)).multiply(r.makeScale(tetePlus, tetePlus, tetePlus)), c.peau);
      poser(2, m.copy(buste).multiply(t.makeTranslation(0, 0.94, -0.004)).multiply(r.makeScale(tetePlus, tetePlus, tetePlus)), c.cheveux);
      poser(3, m.copy(racine).multiply(t.makeTranslation(0.05, 0.5, 0)).multiply(r.makeRotationX(jambes[0])), c.peau);
      poser(4, m.copy(racine).multiply(t.makeTranslation(-0.05, 0.5, 0)).multiply(r.makeRotationX(jambes[1])), c.peau);
      poser(5, m.copy(buste).multiply(t.makeTranslation(0.11 * large, 0.84, 0)).multiply(r.makeRotationX(bras[0])).multiply(t.makeRotationZ(0.06)), c.peau);
      poser(6, m.copy(buste).multiply(t.makeTranslation(-0.11 * large, 0.84, 0)).multiply(r.makeRotationX(bras[1])).multiply(t.makeRotationZ(-0.06)), c.peau);
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
    if (c.posture === "porte") {
      const mere = this.corps.get(c.s.mere);
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
      const haut = c.posture === "couche" || c.posture === "nage" || c.posture === "porte" ? 0.2 : c.posture === "assis" || c.posture === "travail" ? 0.5 : c.posture === "accroupi" ? 0.6 : 0.9;
      // Allongé, le corps s'étend le long de son cap : « pied » est alors l'autre bout du corps.
      const couche = c.posture === "couche" || c.posture === "nage";
      const pied = couche
        ? new THREE.Vector3(pl.x - Math.sin(pl.cap) * c.taille * 0.8, pl.y + 0.15, pl.z - Math.cos(pl.cap) * c.taille * 0.8)
        : new THREE.Vector3(pl.x, pl.y + 0.05, pl.z);
      yield { id: c.s.id, tete: new THREE.Vector3(pl.x, pl.y + c.taille * haut, pl.z), pied, s: c.s, cap: pl.cap, taille: c.taille };
    }
  }

  /** Où est un humain affiché (tête), son cap et s'il marche ; null s'il n'est pas dans la scène. */
  etat(id: number): { tete: THREE.Vector3; cap: number; enMarche: boolean } | null {
    const c = this.corps.get(id);
    if (!c || Number.isNaN(c.x)) return null;
    const pl = this.placement(c);
    const haut = c.posture === "couche" || c.posture === "nage" || c.posture === "porte" ? 0.25 : c.posture === "assis" || c.posture === "travail" ? 0.5 : c.posture === "accroupi" ? 0.6 : 0.85;
    return { tete: new THREE.Vector3(pl.x, pl.y + c.taille * haut, pl.z), cap: pl.cap, enMarche: c.enMarche };
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

  constructor(private readonly relief: Relief) {
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

  animer(temps: number, camera: THREE.Vector3, nuit: number): void {
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
      l.intensity = (6 + 25 * nuit) * (0.85 + 0.15 * Math.sin(temps * 11 + a.f.graine));
    });
  }
}
