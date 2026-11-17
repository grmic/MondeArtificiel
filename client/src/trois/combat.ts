import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";

/**
 * Rejeu d'un assaut dans la vue 3D (0.42.0). La simulation calcule l'assaut d'un coup, à l'aube, et en garde le déroulé : qui a
 * frappé qui, dans quel ordre, avec quoi, et ce qui en est sorti. Ici on le met en scène sur une à deux minutes : la troupe cachée
 * se lève et charge, les dormeurs se réveillent, les duels s'enchaînent, les enfants fuient, les blessés se tiennent le flanc, les
 * morts tombent et restent couchés, les ravisseurs emmènent les captives et la troupe se retire. Rien n'est inventé : chaque issue
 * est celle de la simulation ; seuls la place des corps et le rythme des gestes sont calculés à l'affichage.
 */

/** Ce que le serveur envoie d'un assaut (AssautDto). */
export interface AssautJson {
  id: number;
  secondes: number;
  x: number;
  y: number;
  assaillants: number;
  cible: number;
  troupe: number[];
  defenseurs: number[];
  endormis: number[];
  /** Par 5 : assaillant, défenseur, arme de l'assaillant, arme du défenseur, issue. */
  duels: number[];
  mortsDeBlessures: number[];
}

/** Issues d'un duel, dans l'ordre de l'énumération IssueDuel du serveur. */
export const ISSUE = { Rien: 0, DefenseurTue: 1, AssaillantTue: 2, DefenseurBlesse: 3, AssaillantBlesse: 4, Enlevee: 5 } as const;

/** Armes, dans l'ordre de l'énumération Arme du serveur. */
export const ARME = { Poings: 0, Pierre: 1, Baton: 2, Epieu: 3, Lance: 4, Hache: 5 } as const;

/**
 * Geste de combat en cours : `garde`, `frappe`, `estoc`, `lancer`, `parer`, `touche` (coup reçu), `blesse` (un genou à terre, la
 * main au flanc), `chute`, `gisant`, `cache` (accroupi, caché), `reveil` (on se lève en sursaut), `tire` (emmène une captive),
 * `tiree` (emmenée), ou null : il court, marche ou se tient debout (la posture vient alors du déplacement).
 */
export type GesteCombat = "garde" | "frappe" | "estoc" | "lancer" | "parer" | "touche" | "blesse" | "chute" | "gisant" | "cache" | "reveil" | "tire" | "tiree"
  // Face à face avec l'Entité (0.45.11, rencontre.ts) : réactions jouées comme les gestes d'un assaut.
  | "sursaut" | "effroi" | "couvrir" | "tombeAssis" | "bouche" | "tendre" | "genoux" | "prosterne" | "hausser" | "chercher" | "hesiter"
  | "refuser" | "fixer";

/** Ce que le rejeu impose à un humain à cet instant. */
export interface Role {
  x: number;
  z: number;
  /** Cap voulu (radians, comme Corps.cap), ou null : celui de la marche. */
  cap: number | null;
  geste: GesteCombat | null;
  /** Où en est le geste (0 à 1, cyclique pour frappe/estoc/parer), et depuis quand il dure (s). */
  u: number;
  arme: number;
  /** Il court (vitesse de déplacement plus vive que la marche). */
  court: boolean;
  /** Mort : yeux clos, plus de souffle. */
  mort: boolean;
  /** Celui qu'il affronte (pour le regard), ou 0. */
  face: number;
  /** 0.45.11 : il regarde l'Entité apparue (rencontre.ts). */
  entite?: boolean;
}

interface Acteur {
  id: number;
  /** Place au départ et place de repli. */
  x0: number;
  z0: number;
  arme: number;
  assaillant: boolean;
  endormi: boolean;
  /** Duel où il se bat (index), ou −1. */
  duel: number;
  /** Où il fuit (non-combattants) ou se retire (assaillants). */
  fuiteX: number;
  fuiteZ: number;
}

interface Duel {
  a: number;
  d: number;
  armeA: number;
  armeD: number;
  issue: number;
  /** Début des coups (s depuis le début du rejeu) et instant de l'issue. */
  debut: number;
  issueA: number;
  /** Lieu du duel. */
  x: number;
  z: number;
  /** Direction de l'assaillant vers le défenseur. */
  angle: number;
}

/** Durées du rejeu (s, à ×1) : la troupe cachée, la charge, l'écart entre deux duels, la durée d'un échange, le repli. */
const CACHE = 6;
const CHARGE = 7;
const ECART_DUELS = 2.6;
const ECHANGE = 5;
const REPLI = 14;

/** Le plan d'un assaut : chaque acteur, chaque duel, et l'instant où tout s'achève. */
export class Rejeu {
  readonly acteurs = new Map<number, Acteur>();
  readonly duels: Duel[] = [];
  readonly duree: number;
  readonly centre: THREE.Vector3;
  /** Où sont tombés les morts (pour qu'ils restent là après le rejeu). */
  readonly gisants = new Map<number, { x: number; z: number; cap: number }>();
  private readonly mortsTard = new Set<number>();
  private readonly finDuels: number;

  /**
   * `places` : où se tient chacun au début (m, monde), connu de la foule ; `presents` : les autres humains du camp (enfants, vieux,
   * captives) qui ne se battent pas et fuient.
   */
  constructor(readonly assaut: AssautJson, relief: Relief, places: Map<number, { x: number; z: number }>, presents: number[]) {
    const [cx, cz] = relief.camp(assaut.x, assaut.y);
    this.centre = new THREE.Vector3(cx, relief.hauteur(cx, cz), cz);
    // La troupe arrive d'un côté, propre à cet assaut.
    const theta = unite(hacher(assaut.id, 3)) * Math.PI * 2;
    const ux = Math.cos(theta);
    const uz = Math.sin(theta);
    const place = (id: number, rayon: number) => {
      const p = places.get(id);
      if (p && Number.isFinite(p.x)) return [p.x, p.z];
      const a = unite(hacher(id, assaut.id, 5)) * Math.PI * 2;
      const r = rayon * (0.4 + 0.6 * unite(hacher(id, assaut.id, 7)));
      return [cx + Math.cos(a) * r, cz + Math.sin(a) * r];
    };
    const endormis = new Set(assaut.endormis);
    assaut.troupe.forEach((id, k) => {
      // Cachés en ligne lâche, à 30-45 m du camp, derrière ce qu'offre le terrain.
      const lateral = (k - (assaut.troupe.length - 1) / 2) * 2.2 + (unite(hacher(id, 11)) - 0.5) * 1.5;
      const loin = 30 + 15 * unite(hacher(id, 13));
      const x0 = cx + ux * loin - uz * lateral;
      const z0 = cz + uz * loin + ux * lateral;
      this.acteurs.set(id, { id, x0, z0, arme: ARME.Poings, assaillant: true, endormi: false, duel: -1, fuiteX: cx + ux * 70 - uz * lateral, fuiteZ: cz + uz * 70 + ux * lateral });
    });
    for (const id of assaut.defenseurs) {
      const [x0, z0] = place(id, 10);
      this.acteurs.set(id, { id, x0, z0, arme: ARME.Poings, assaillant: false, endormi: endormis.has(id), duel: -1, fuiteX: x0, fuiteZ: z0 });
    }
    for (const id of presents) {
      if (this.acteurs.has(id)) continue;
      const [x0, z0] = place(id, 10);
      // Ils fuient du côté opposé à la troupe, à 25-45 m, et s'y cachent.
      const loin = 25 + 20 * unite(hacher(id, 17));
      const ecart = (unite(hacher(id, 19)) - 0.5) * 30;
      this.acteurs.set(id, {
        id, x0, z0, arme: ARME.Poings, assaillant: false, endormi: true, duel: -1,
        fuiteX: cx - ux * loin - uz * ecart, fuiteZ: cz - uz * loin + ux * ecart,
      });
    }
    let fin = CACHE + CHARGE;
    for (let k = 0; k + 5 <= assaut.duels.length; k += 5) {
      const [a, d, armeA, armeD, issue] = assaut.duels.slice(k, k + 5);
      const def = this.acteurs.get(d);
      const ass = this.acteurs.get(a);
      if (!def || !ass) continue;
      const i = this.duels.length;
      const debut = CACHE + CHARGE + i * ECART_DUELS;
      // Le défenseur fait face, à un pas de chez lui, vers la troupe.
      const angle = Math.atan2(def.x0 - ass.x0, def.z0 - ass.z0);
      this.duels.push({ a, d, armeA, armeD, issue, debut, issueA: debut + ECHANGE + 1.5 * unite(hacher(a, d, 23)), x: def.x0, z: def.z0, angle });
      ass.duel = i;
      def.duel = i;
      ass.arme = armeA;
      def.arme = armeD;
      fin = Math.max(fin, debut + ECHANGE + 3);
    }
    for (const id of assaut.mortsDeBlessures) this.mortsTard.add(id);
    this.finDuels = fin;
    this.duree = fin + 3 + REPLI;
  }

  /** Celui qui ouvre la marche : le chef de la troupe. */
  get chef(): number {
    return this.assaut.troupe[0] ?? 0;
  }

  /** Point à regarder à l'instant t (caméra de combat) : la troupe cachée, puis le duel en cours, puis le camp. */
  foyer(t: number): { x: number; z: number; large: boolean } {
    if (t < CACHE + CHARGE * 0.6) {
      const a = this.acteurs.get(this.chef);
      if (a) return { x: (a.x0 * 2 + this.centre.x) / 3, z: (a.z0 * 2 + this.centre.z) / 3, large: true };
    }
    let actuel: Duel | undefined;
    for (const d of this.duels) if (t >= d.debut - 1 && t <= d.issueA + 2.5) actuel = d;
    if (actuel) return { x: actuel.x, z: actuel.z, large: false };
    return { x: this.centre.x, z: this.centre.z, large: true };
  }

  /** Ce que fait `id` à l'instant t (s depuis le début du rejeu), ou null s'il n'y joue aucun rôle. */
  role(id: number, t: number): Role | null {
    const a = this.acteurs.get(id);
    if (!a) return null;
    const r: Role = { x: a.x0, z: a.z0, cap: null, geste: null, u: 0, arme: a.arme, court: false, mort: false, face: 0 };
    const d = a.duel >= 0 ? this.duels[a.duel] : undefined;
    if (d) r.face = a.assaillant ? d.d : d.a;
    if (a.assaillant) this.assaillant(a, d, t, r);
    else if (d) this.defenseur(a, d, t, r);
    else if (this.acteursDefenseurs(a)) this.sansDuel(a, t, r);
    else this.fuir(a, t, r);
    // Mort de ses blessures, peu après les combats.
    if (this.mortsTard.has(id) && !r.mort && t > this.finDuels) this.tomber(a, r, t - this.finDuels, r.x, r.z, r.cap ?? 0);
    if (r.mort) this.gisants.set(id, { x: r.x, z: r.z, cap: r.cap ?? 0 });
    return r;
  }

  private acteursDefenseurs(a: Acteur): boolean {
    return this.assaut.defenseurs.includes(a.id);
  }

  private assaillant(a: Acteur, d: Duel | undefined, t: number, r: Role): void {
    if (t < CACHE) {
      r.geste = "cache";
      r.cap = Math.atan2(this.centre.x - a.x0, this.centre.z - a.z0);
      return;
    }
    // Sans adversaire (plus de défenseurs debout) : il entre dans le camp et reste sur ses gardes.
    const vise = d ? [d.x - Math.sin(d.angle) * 1.6, d.z - Math.cos(d.angle) * 1.6] : [this.centre.x + (a.x0 - this.centre.x) * 0.15, this.centre.z + (a.z0 - this.centre.z) * 0.15];
    const arrivee = d ? d.debut : CACHE + CHARGE;
    if (t < arrivee) {
      const u = Math.min(1, (t - CACHE) / Math.max(1, arrivee - CACHE));
      r.x = a.x0 + (vise[0] - a.x0) * u;
      r.z = a.z0 + (vise[1] - a.z0) * u;
      r.court = u < 1;
      r.geste = u < 1 ? null : "garde";
      r.cap = Math.atan2(vise[0] - a.x0, vise[1] - a.z0);
      return;
    }
    r.x = vise[0];
    r.z = vise[1];
    r.cap = d ? d.angle : Math.atan2(this.centre.x - a.x0, this.centre.z - a.z0);
    if (!d) {
      r.geste = "garde";
      if (t > this.finDuels) this.seRetirer(a, t, r);
      return;
    }
    if (t < d.issueA) {
      this.echanger(t - d.debut, d.armeA, r, a.id, true);
      return;
    }
    const apres = t - d.issueA;
    switch (d.issue) {
      case ISSUE.AssaillantTue:
        this.tomber(a, r, apres, r.x, r.z, r.cap!);
        return;
      case ISSUE.AssaillantBlesse:
        if (apres < 1) {
          r.geste = "touche";
          r.u = apres;
        } else {
          r.geste = "blesse";
          r.u = apres;
          // Il recule de quelques pas, puis se retire avec les autres.
          if (t > this.finDuels) this.seRetirer(a, t, r);
        }
        return;
      case ISSUE.Enlevee: {
        // Il la saisit et l'emmène vers son côté, puis rejoint la retraite.
        r.geste = "tire";
        const u = Math.min(1, apres / (REPLI + this.finDuels - d.issueA));
        r.x = vise[0] + (a.fuiteX - vise[0]) * u;
        r.z = vise[1] + (a.fuiteZ - vise[1]) * u;
        r.cap = Math.atan2(a.fuiteX - vise[0], a.fuiteZ - vise[1]);
        return;
      }
      default:
        r.geste = "garde";
        if (t > this.finDuels) this.seRetirer(a, t, r);
    }
  }

  private defenseur(a: Acteur, d: Duel, t: number, r: Role): void {
    r.cap = d.angle + Math.PI;
    const reveil = CACHE + CHARGE * 0.5 + 3 * unite(hacher(a.id, 29));
    if (a.endormi && t < reveil) {
      r.geste = t > reveil - 1.2 ? "reveil" : null;
      r.u = t > reveil - 1.2 ? (t - reveil + 1.2) / 1.2 : 0;
      r.cap = null;
      return;
    }
    if (t < d.debut) {
      r.geste = "garde";
      return;
    }
    if (t < d.issueA) {
      this.echanger(t - d.debut, d.armeD, r, a.id, false);
      return;
    }
    const apres = t - d.issueA;
    switch (d.issue) {
      case ISSUE.DefenseurTue:
        this.tomber(a, r, apres, r.x, r.z, r.cap);
        return;
      case ISSUE.DefenseurBlesse:
        r.geste = apres < 1 ? "touche" : "blesse";
        r.u = apres;
        return;
      case ISSUE.Enlevee: {
        const ravisseur = this.acteurs.get(d.a)!;
        const vise = [d.x - Math.sin(d.angle) * 1.6, d.z - Math.cos(d.angle) * 1.6];
        const u = Math.min(1, apres / (REPLI + this.finDuels - d.issueA));
        // Un pas derrière celui qui la tient par le poignet.
        const dir = Math.atan2(ravisseur.fuiteX - vise[0], ravisseur.fuiteZ - vise[1]);
        r.x = vise[0] + (ravisseur.fuiteX - vise[0]) * u - Math.sin(dir) * 0.9;
        r.z = vise[1] + (ravisseur.fuiteZ - vise[1]) * u - Math.cos(dir) * 0.9;
        r.cap = dir;
        r.geste = "tiree";
        return;
      }
      default:
        r.geste = "garde";
    }
  }

  /** Un défenseur sans adversaire : il se lève et fait face à la troupe. */
  private sansDuel(a: Acteur, t: number, r: Role): void {
    const reveil = CACHE + CHARGE * 0.5 + 3 * unite(hacher(a.id, 29));
    if (a.endormi && t < reveil) {
      r.geste = t > reveil - 1.2 ? "reveil" : null;
      r.u = t > reveil - 1.2 ? (t - reveil + 1.2) / 1.2 : 0;
      return;
    }
    r.geste = "garde";
    r.cap = Math.atan2(this.centre.x - a.x0, this.centre.z - a.z0);
  }

  /** Ceux qui ne se battent pas fuient à l'opposé de la troupe et se cachent. */
  private fuir(a: Acteur, t: number, r: Role): void {
    const depart = CACHE + CHARGE * 0.4 + 2.5 * unite(hacher(a.id, 31));
    if (t < depart) {
      r.geste = null;
      return;
    }
    const d = Math.hypot(a.fuiteX - a.x0, a.fuiteZ - a.z0);
    const u = Math.min(1, ((t - depart) * 4.2) / Math.max(1, d));
    r.x = a.x0 + (a.fuiteX - a.x0) * u;
    r.z = a.z0 + (a.fuiteZ - a.z0) * u;
    r.court = u < 1;
    r.cap = u < 1 ? Math.atan2(a.fuiteX - a.x0, a.fuiteZ - a.z0) : Math.atan2(this.centre.x - r.x, this.centre.z - r.z);
    r.geste = u < 1 ? null : "cache";
  }

  private seRetirer(a: Acteur, t: number, r: Role): void {
    const u = Math.min(1, (t - this.finDuels) / REPLI);
    const x0 = r.x;
    const z0 = r.z;
    r.x = x0 + (a.fuiteX - x0) * u;
    r.z = z0 + (a.fuiteZ - z0) * u;
    r.cap = Math.atan2(a.fuiteX - x0, a.fuiteZ - z0);
    r.geste = u < 1 ? null : "garde";
  }

  /** L'échange de coups : l'assaillant frappe, le défenseur pare puis rend ; on lance d'abord une pierre si l'on en a une. */
  private echanger(dt: number, arme: number, r: Role, id: number, assaillant: boolean): void {
    const rythme = 1.1 + 0.4 * unite(hacher(id, 37));
    const cycle = dt / rythme + (assaillant ? 0 : 0.5);
    const tour = Math.floor(cycle);
    const u = cycle - tour;
    const frappe = assaillant ? tour % 3 !== 2 : tour % 2 === 1;
    if (arme === ARME.Pierre && dt < rythme) {
      r.geste = "lancer";
      r.u = dt / rythme;
      return;
    }
    r.geste = frappe ? (arme === ARME.Epieu || arme === ARME.Lance ? "estoc" : "frappe") : "parer";
    r.u = u;
  }

  private tomber(a: Acteur, r: Role, apres: number, x: number, z: number, cap: number): void {
    r.geste = apres < 0.9 ? "chute" : "gisant";
    r.u = Math.min(1, apres / 0.9);
    r.mort = apres >= 0.9;
    // Il tombe en arrière, à un pas de là où il se tenait.
    r.x = x - Math.sin(cap) * 0.5 * r.u;
    r.z = z - Math.cos(cap) * 0.5 * r.u;
    r.cap = cap;
    void a;
  }
}

/** Une arme à poser dans une main : sa sorte, le poing, l'avant-bras et la droite du corps (monde). */
export interface ArmeEnMain {
  arme: number;
  poing: THREE.Vector3;
  coude: THREE.Vector3;
  droite: THREE.Vector3;
  /** 0.45.10 : hors combat, où se tient celui qui la porte (pieds) : l'outil court est tenu levé et à l'écart du corps. */
  porte?: THREE.Vector3;
}

const NB_ARMES = 96;
const Y = new THREE.Vector3(0, 1, 0);
const dehors = new THREE.Vector3();
const tenu = new THREE.Vector3();

/**
 * Armes tenues en main (D1) : pierre, branche, épieu durci au feu, lance à pointe de pierre, hache emmanchée. Quelques maillages
 * partagés, posés à chaque image sur les corps proches qui en tiennent une.
 */
export class Armes3D {
  readonly groupe = new THREE.Group();
  private readonly manches: THREE.InstancedMesh;
  private readonly pointes: THREE.InstancedMesh;
  private readonly tetes: THREE.InstancedMesh;
  private readonly pierres: THREE.InstancedMesh;
  private readonly m = new THREE.Matrix4();
  private readonly q = new THREE.Quaternion();
  private readonly e = new THREE.Vector3();
  private readonly p = new THREE.Vector3();
  private readonly axe = new THREE.Vector3();

  constructor() {
    const bois = new THREE.MeshStandardMaterial({ color: 0x6b4a2c, roughness: 0.85 });
    const pierre = new THREE.MeshStandardMaterial({ color: 0x77736b, roughness: 0.9, flatShading: true });
    // Manche : cylindre de 1 m le long de y, centré ; pointe : cône ; tête de hache : pierre aplatie ; pierre à lancer.
    this.manches = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.016, 0.02, 1, 6), bois, NB_ARMES);
    this.pointes = new THREE.InstancedMesh(new THREE.ConeGeometry(0.03, 0.16, 5), pierre, NB_ARMES);
    this.tetes = new THREE.InstancedMesh(new THREE.DodecahedronGeometry(0.07, 0).scale(1.4, 0.8, 0.45), pierre, NB_ARMES);
    this.pierres = new THREE.InstancedMesh(new THREE.DodecahedronGeometry(0.055, 0), pierre, NB_ARMES);
    for (const x of [this.manches, this.pointes, this.tetes, this.pierres]) {
      x.count = 0;
      x.frustumCulled = false;
      x.castShadow = true;
      this.groupe.add(x);
    }
  }

  poser(liste: ArmeEnMain[]): void {
    let nm = 0, np = 0, nt = 0, nr = 0;
    const { m, q, e, p, axe } = this;
    for (const a of liste) {
      if (nm >= NB_ARMES) break;
      const avantBras = p.copy(a.poing).sub(a.coude).normalize();
      const longue = a.arme === ARME.Epieu || a.arme === ARME.Lance;
      // Une arme longue prolonge l'avant-bras ; une branche ou une hache sort du poing, en travers.
      if (longue) axe.copy(avantBras);
      else axe.crossVectors(avantBras, a.droite).normalize();
      let poing = a.poing;
      if (a.porte && !longue) {
        // Le manche part du poing vers l'avant, à peine incliné vers le bas, un peu en dehors du corps : il ne croise jamais la cuisse.
        axe.crossVectors(a.droite, Y).setY(-0.25).normalize();
        dehors.copy(a.poing).sub(a.porte).setY(0);
        if (dehors.lengthSq() > 1e-6) {
          dehors.normalize();
          axe.addScaledVector(dehors, 0.22).normalize();
          poing = tenu.copy(a.poing).addScaledVector(dehors, 0.02);
        }
      }
      q.setFromUnitVectors(Y, axe);
      if (a.arme === ARME.Pierre) {
        m.compose(poing, q, e.set(1, 1, 1));
        this.pierres.setMatrixAt(nr++, m);
        continue;
      }
      const longueur = longue ? 1.9 : a.arme === ARME.Hache ? 0.62 : 0.9;
      const avant = longue ? 0.4 : longueur * 0.32;
      m.compose(p.copy(poing).addScaledVector(axe, avant), q, e.set(1, longueur, 1));
      this.manches.setMatrixAt(nm++, m);
      const bout = p.copy(poing).addScaledVector(axe, avant + longueur / 2);
      if (longue) {
        m.compose(bout.addScaledVector(axe, 0.06), q, e.set(a.arme === ARME.Lance ? 1 : 0.6, a.arme === ARME.Lance ? 1 : 0.8, a.arme === ARME.Lance ? 1 : 0.6));
        this.pointes.setMatrixAt(np++, m);
      } else if (a.arme === ARME.Hache) {
        m.compose(bout.addScaledVector(axe, -0.05), q, e.set(1, 1, 1));
        this.tetes.setMatrixAt(nt++, m);
      }
    }
    this.manches.count = nm;
    this.pointes.count = np;
    this.tetes.count = nt;
    this.pierres.count = nr;
    for (const x of [this.manches, this.pointes, this.tetes, this.pierres]) x.instanceMatrix.needsUpdate = true;
  }
}
