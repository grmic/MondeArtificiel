import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";
import { aPiedSec } from "./foule";
import { CHAMPS_BETES, corpsDe, DETAILS, longueur, materiauBetes, type Espece, type Gabarit } from "./betes3d";
import { envoyer } from "./envoi";
import { enclosDe } from "./camp";

/**
 * Les bêtes vivantes de la vue 3D (0.44.0, choix C1 et E1). La simulation ne connaît que des nombres : le gibier de chaque case,
 * et les troupeaux (adultes, petits, docilité, gardien). Tout ce qu'on voit ici est calculé à l'affichage, sans rien changer au
 * monde : une meneuse que le troupeau suit, des bêtes qui broutent, boivent, dorment la nuit (ou rôdent, pour les chasseurs), qui
 * relèvent la tête puis s'enfuient quand un humain approche (plus tôt si l'espèce panique), ou lui font face et chargent si elle
 * est irascible. Les bêtes gardées suivent leur gardien à la pâture le jour et reviennent à l'enclos la nuit ; les chiens suivent
 * leur maître. Distances de fuite : quelques mètres pour les bêtes calmes, 50 m et plus pour les craintives (ordres de grandeur
 * des ongulés sauvages, Stankowich 2008), réduites par la docilité chez les bêtes gardées.
 */


type Etat = "paitre" | "marcher" | "fuir" | "charger" | "toiser" | "dormir" | "veiller";

interface Bete {
  x: number;
  z: number;
  y: number;
  cap: number;
  v: number;
  phase: number;
  tete: number;
  regard: number;
  couche: number;
  teinte: number;
  /** Taille par rapport à un adulte (les petits). */
  taille: number;
  petit: boolean;
  /** Pour un petit : l'index de sa mère dans le groupe. */
  mere: number;
  /** But propre (bêtes solitaires, ou enclos) et jusqu'à quand le garder (s, temps réel). */
  but: [number, number] | null;
  jusqua: number;
  etat: Etat;
  /** Place dans le troupeau, autour de la meneuse : angle et distance (en longueurs du corps). */
  placeA: number;
  placeR: number;
  /** Fin de la fuite ou de la charge, et prochain moment où l'on peut charger à nouveau. */
  finAlerte: number;
  repos: number;
  /** D'où vient le danger (pour s'en éloigner, ou lui faire face). */
  dx: number;
  dz: number;
  /** Image où l'on a mesuré la hauteur du sol pour la dernière fois. */
  imageSol: number;
}

interface Groupe {
  id: number;
  /** Numéro du groupe dans le flux Camp (pour la fiche et « Aller voir ») ; un petit groupe sauvage a son propre `id`. */
  serveur: number;
  espece: Espece;
  /** Longueur d'un adulte (m). */
  L: number;
  cx: number;
  cz: number;
  rayon: number;
  gardee: boolean;
  gardien: number;
  docilite: number;
  chiens: boolean;
  enclos: { x: number; z: number; r: number } | null;
  adultes: number;
  petits: number;
  betes: Bete[];
  but: [number, number] | null;
  butJusqua: number;
  alerte: number;
  alerteX: number;
  alerteZ: number;
  vu: number;
  /** 0.45.1 : bête connue des humains (son numéro, 0 sinon) et minutes depuis sa dernière attaque ce jour (−1 : aucune). */
  fauve: number;
  attaque: number;
}

/** Un rendu : les bêtes d'une espèce à un degré de détail, en un seul dessin. */
class Rendu {
  readonly maillage: THREE.Mesh;
  private readonly geo: THREE.InstancedBufferGeometry;
  private a!: THREE.InstancedBufferAttribute;
  private b!: THREE.InstancedBufferAttribute;
  private c!: THREE.InstancedBufferAttribute;
  private capacite = 0;
  n = 0;

  constructor(base: THREE.BufferGeometry, materiau: THREE.Material, nom: string) {
    this.geo = new THREE.InstancedBufferGeometry();
    for (const [k, v] of Object.entries(base.attributes)) this.geo.setAttribute(k, v);
    this.geo.setIndex(base.index);
    this.agrandir(32);
    this.geo.instanceCount = 0;
    this.maillage = new THREE.Mesh(this.geo, materiau);
    this.maillage.frustumCulled = false;
    this.maillage.name = nom;
  }

  private agrandir(n: number): void {
    const cap = Math.max(n, this.capacite * 2);
    const neuf = (ancien: THREE.InstancedBufferAttribute | undefined) => {
      const a = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
      if (ancien) a.array.set(ancien.array as Float32Array);
      return a;
    };
    this.a = neuf(this.a);
    this.b = neuf(this.b);
    this.c = neuf(this.c);
    this.geo.setAttribute("iA", this.a);
    this.geo.setAttribute("iB", this.b);
    this.geo.setAttribute("iC", this.c);
    this.capacite = cap;
  }

  ajouter(x: number, y: number, z: number, cap: number, echelle: number, phase: number, allure: number, tete: number,
    regard: number, couche: number, teinte: number, hanche: number): void {
    if (this.n >= this.capacite) this.agrandir(this.n + 1);
    const k = this.n * 4;
    const a = this.a.array as Float32Array;
    const b = this.b.array as Float32Array;
    const c = this.c.array as Float32Array;
    a[k] = x;
    a[k + 1] = y;
    a[k + 2] = z;
    a[k + 3] = cap;
    b[k] = echelle;
    b[k + 1] = phase;
    b[k + 2] = allure;
    b[k + 3] = tete;
    c[k] = regard;
    c[k + 1] = couche;
    c[k + 2] = teinte;
    c[k + 3] = hanche;
    this.n++;
  }

  envoyer(): void {
    this.geo.instanceCount = this.n;
    this.maillage.visible = this.n > 0;
    envoyer(this.a, this.n);
    envoyer(this.b, this.n);
    envoyer(this.c, this.n);
  }
}

/** Distances (m) où l'on passe d'un degré de détail au suivant, pour une bête de 2 m (proportionnelles à la taille). */
const SEUILS = [45, 220];

/** Bêtes sauvages du flux Camp (MondeSimule.IdSauvageScene), et ce qu'on en montre autour de la caméra (0.45.18). */
const ID_SAUVAGE = 10_000_000;
const PORTEE_SAUVAGES = 600;
const PLAFOND_SAUVAGES = 150;
/** Rayon (m) où erre un petit groupe sauvage autour de sa place. */
const RAYON_SOUS_GROUPE = 120;

const DEUX_PI = Math.PI * 2;

function angle(a: number): number {
  return Math.atan2(Math.sin(a), Math.cos(a));
}

export class Faune3D {
  readonly groupe = new THREE.Group();
  private readonly materiau = materiauBetes();
  private readonly especes = new Map<number, { e: Espece; gab: Gabarit; rendus: (Rendu | null)[] }>();
  private groupes = new Map<number, Groupe>();
  private readonly origine = new THREE.Vector3();
  private image = 0;
  /** Horloge des bêtes (s) : avance avec les images (dt), pour que fuites et repos durent ce qu'ils doivent même si l'on saccade. */
  private temps = 0;
  private ombres = false;
  /** Portée (m) au-delà de laquelle on ne montre plus de bêtes, et part des bêtes montrées (Qualité Basse : la moitié). */
  portee = 1500;
  part = 1;
  /** 0.45.24 : le groupe du serveur qu'on est allé voir (« Aller voir cette espèce ») : montré même au-delà du plafond et de la portée. */
  vise: number | null = null;
  /** 0.45.24 : quand le flux Camp a donné ses bêtes pour la dernière fois (ms, performance.now), pour la ligne « Bêtes » (touche I). */
  recuA = 0;
  /** Bêtes prises à la chasse depuis moins d'un jour, à montrer près de leur chasseur arrêté (0.44.1). */
  prises: { x: number; z: number; cap: number; espece: number }[] = [];
  /** Bêtes proches de la caméra à cette image (x, z, rayon des pas), pour le tapis du sol. */
  readonly pietineurs: number[] = [];

  constructor(private readonly relief: Relief) {}

  /** Ombres : les bêtes proches en portent, toutes en reçoivent. */
  reglerOmbres(actif: boolean): void {
    this.ombres = actif;
    for (const { rendus } of this.especes.values()) rendus.forEach((r, k) => r && this.ombrer(r, k));
  }

  private ombrer(r: Rendu, niveau: number): void {
    r.maillage.castShadow = this.ombres && niveau === 0;
    r.maillage.receiveShadow = this.ombres;
  }

  /** Les espèces du monde (au début, puis de temps en temps : l'Entité peut en inventer). */
  definirEspeces(liste: Espece[]): void {
    for (const e of liste) {
      const ancienne = this.especes.get(e.numero);
      if (ancienne) {
        ancienne.e = e;
        continue;
      }
      this.especes.set(e.numero, { e, gab: corpsDe(e, DETAILS[2]).gabarit, rendus: [null, null, null] });
    }
  }

  espece(numero: number): Espece | null {
    return this.especes.get(numero)?.e ?? null;
  }

  private rendu(numero: number, niveau: number): Rendu | null {
    const s = this.especes.get(numero);
    if (!s) return null;
    let r = s.rendus[niveau];
    if (!r) {
      const { geo } = corpsDe(s.e, DETAILS[niveau]);
      r = new Rendu(geo, this.materiau, `bete-${numero}-${niveau}`);
      this.ombrer(r, niveau);
      s.rendus[niveau] = r;
      this.groupe.add(r.maillage);
    }
    return r;
  }

  /**
   * Nouvelles données du serveur (CampDto.Betes) : les groupes gardent leurs bêtes et leur état ; on ajoute ou retire des bêtes.
   * 0.45.18 : les bêtes sauvages d'une case arrivent au nombre de l'estimation (des centaines parfois) ; on les répartit en petits
   * groupes posés dans la case, et l'on ne garde que les plus proches de la caméra, à 600 m au plus, jusqu'au plafond (150 bêtes,
   * moitié en Qualité Basse) : près de la caméra, on voit autant de bêtes que la fenêtre Faune en compte.
   */
  recevoir(betes: number[]): void {
    const rel = this.relief;
    const vus = new Set<number>();
    const o = this.origine;
    const candidats: { cle: number; serveur: number; champs: number[]; x: number; z: number; adultes: number; petits: number; d: number }[] = [];
    for (let k = 0; k + CHAMPS_BETES <= betes.length; k += CHAMPS_BETES) {
      const champs = betes.slice(k, k + CHAMPS_BETES);
      const [id, x, y, numero, , , adultes0, petits0, gardees] = champs;
      const s = this.especes.get(numero);
      if (!s) continue;
      if (gardees === 1 || id > -ID_SAUVAGE) {
        vus.add(id);
        this.prendre(champs, id, null);
        continue;
      }
      // Petits groupes : un troupeau de 6 à 20 bêtes pour les espèces qui vivent en troupeau, un grand chasseur seul, sinon 1 à 3.
      const total = adultes0 + petits0;
      const taille = (j: number) => s.e.troupeau ? 6 + Math.floor(unite(hacher(id, j, 81)) * 15) : s.e.forme === 5 ? 1 : 1 + Math.floor(unite(hacher(id, j, 83)) * 3);
      let reste = total;
      for (let j = 0; reste > 0; j++) {
        const n = Math.min(reste, taille(j));
        reste -= n;
        // Le premier groupe reste au milieu de la case (là où « Aller voir » mène) ; les autres, n'importe où dans la case.
        const gx = j === 0 ? (x + 0.5) * rel.m + (unite(hacher(x, y, 59)) - 0.5) * rel.m * 0.7 : (x + unite(hacher(id, j, 85))) * rel.m;
        const gz = j === 0 ? (y + 0.5) * rel.m + (unite(hacher(x, y, 61)) - 0.5) * rel.m * 0.7 : (y + unite(hacher(id, j, 87))) * rel.m;
        // L'espèce visée (0.45.24) passe avant les autres : distance −1, et ni portée ni plafond pour elle.
        const d = id === this.vise && j === 0 ? -1 : Math.hypot(gx - o.x, gz - o.z);
        if (d > PORTEE_SAUVAGES) continue;
        const petits = Math.round((n * petits0) / Math.max(1, total));
        candidats.push({ cle: j === 0 ? id : id * 1024 - j, serveur: id, champs, x: gx, z: gz, adultes: Math.max(1, n - petits), petits, d });
      }
    }
    // Les plus proches d'abord, jusqu'au plafond.
    candidats.sort((a, b) => a.d - b.d);
    let montrees = 0;
    const plafond = Math.round(PLAFOND_SAUVAGES * this.part);
    for (const c of candidats) {
      if (c.d >= 0 && montrees + c.adultes + c.petits > plafond) break;
      montrees += c.adultes + c.petits;
      vus.add(c.cle);
      const champs = [...c.champs];
      champs[6] = c.adultes;
      champs[7] = c.petits;
      this.prendre(champs, c.cle, [c.x, c.z], c.serveur);
    }
    for (const id of [...this.groupes.keys()]) if (!vus.has(id)) this.groupes.delete(id);
    this.recuA = performance.now();
  }

  /** 0.45.24 : la bête montrée la plus proche de la caméra (m), ou −1 s'il n'y en a aucune (ligne « Bêtes », touche I). */
  plusProche(camera: THREE.Vector3): number {
    let d = Infinity;
    for (const g of this.groupes.values()) for (const b of g.betes) d = Math.min(d, Math.hypot(b.x - camera.x, b.z - camera.z));
    return Number.isFinite(d) ? d : -1;
  }

  /** Crée ou met à jour un groupe d'après ses nombres du flux Camp ; `centre` : où le poser (petits groupes sauvages). */
  private prendre(champs: number[], id: number, centre: [number, number] | null, serveur = id): void {
    const rel = this.relief;
    const maintenant = this.temps;
    const [, x, y, numero, forme, poids, adultes0, petits0, gardees, gardien, docilite, fauve, minutes] = champs;
    const s = this.especes.get(numero);
    if (!s) return;
    const L = longueur(forme, poids);
    const chiens = gardees === 1 && forme === 4;
    // Bêtes gardées : on en montre jusqu'à 30 ; bêtes connues : ce que donne le serveur (12 au plus) ; petits groupes sauvages : tout
    // (le plafond a déjà été appliqué).
    const adultes = centre ? Math.max(1, adultes0) : Math.max(1, Math.ceil(Math.min(gardees ? 30 : 12, adultes0) * this.part));
    const petits = centre ? petits0 : Math.ceil(Math.min(gardees ? 20 : 8, petits0) * this.part);
    let g = this.groupes.get(id);
    if (!g) {
      let cx: number;
      let cz: number;
      let enclos: Groupe["enclos"] = null;
      if (gardees === 1) {
        const [ex, ez, r] = enclosDe(rel, id, x, y, adultes + petits, L);
        [cx, cz] = [ex, ez];
        if (!chiens) enclos = { x: ex, z: ez, r };
      } else {
        cx = centre ? centre[0] : (x + 0.5) * rel.m + (unite(hacher(x, y, 59)) - 0.5) * rel.m * 0.7;
        cz = centre ? centre[1] : (y + 0.5) * rel.m + (unite(hacher(x, y, 61)) - 0.5) * rel.m * 0.7;
        // Le grand nageur guette au bord de l'eau : sur la rive la plus proche du milieu de la case.
        if (forme === 8) [cx, cz] = aPiedSec(rel, cx, cz, 0.05, hacher(x, y, 67));
      }
      g = {
        id, espece: s.e, L, cx, cz, rayon: gardees ? enclos?.r ?? 20 : centre ? RAYON_SOUS_GROUPE : rel.m * 0.45, serveur, gardee: gardees === 1, gardien, docilite: docilite / 1000, chiens,
        enclos, adultes: 0, petits: 0, betes: [], but: null, butJusqua: 0, alerte: 0, alerteX: 0, alerteZ: 0, vu: maintenant, fauve: 0, attaque: -2,
      };
      this.groupes.set(id, g);
    }
    // 0.45.1 : une bête connue qui vient d'attaquer (le jour même) rôde autour du camp de la case, puis s'éloigne.
    g.fauve = fauve ?? 0;
    const attaque = fauve > 0 && minutes >= 0 ? minutes : -1;
    if (attaque !== g.attaque) {
      g.attaque = attaque;
      if (attaque >= 0 && attaque < 180) {
        const [ax, az] = rel.camp(x, y);
        const a = unite(hacher(id, 71)) * DEUX_PI;
        const r = attaque < 30 ? 18 : 18 + attaque * 1.5;
        [g.cx, g.cz] = [ax + Math.cos(a) * r, az + Math.sin(a) * r];
        g.rayon = 12 + attaque * 0.5;
        g.but = null;
      } else if (fauve > 0) {
        g.cx = (x + 0.5) * rel.m;
        g.cz = (y + 0.5) * rel.m;
        g.rayon = rel.m * 0.45;
      }
    }
    g.espece = s.e;
    g.gardien = gardien;
    g.docilite = docilite / 1000;
    g.L = L;
    g.vu = maintenant;
    this.ajuster(g, adultes, petits);
  }

  /** Ajoute ou retire des bêtes pour en avoir le nombre voulu (les nouvelles apparaissent près des autres). */
  private ajuster(g: Groupe, adultes: number, petits: number): void {
    const adultesAvant = g.betes.filter((b) => !b.petit);
    const petitsAvant = g.betes.filter((b) => b.petit);
    while (adultesAvant.length > adultes) adultesAvant.pop();
    while (petitsAvant.length > petits) petitsAvant.pop();
    const nouvelle = (k: number, petit: boolean, mere: number): Bete => {
      const graine = hacher(g.id, k, petit ? 67 : 63);
      const ref = adultesAvant[0];
      const a = unite(graine) * DEUX_PI;
      const r = (petit ? 1 : 2 + 6 * unite(hacher(graine, 2))) * g.L;
      let x = (ref?.x ?? g.cx) + Math.cos(a) * r;
      let z = (ref?.z ?? g.cz) + Math.sin(a) * r;
      if (!ref && !g.enclos) [x, z] = aPiedSec(this.relief, x, z, 0.2, graine);
      return {
        x, z, y: this.relief.hauteur(x, z), cap: unite(hacher(graine, 3)) * DEUX_PI, v: 0, phase: unite(hacher(graine, 4)) * DEUX_PI, tete: 0, regard: 0,
        couche: 0, teinte: 0.85 + 0.3 * unite(hacher(graine, 5)), taille: petit ? 0.4 : 0.88 + 0.24 * unite(hacher(graine, 6)), petit, mere,
        but: null, jusqua: 0, etat: "paitre", placeA: unite(hacher(graine, 7)) * DEUX_PI, placeR: 1.2 + 3.5 * Math.sqrt(unite(hacher(graine, 8))),
        finAlerte: 0, repos: 0, dx: 0, dz: 0, imageSol: -99,
      };
    };
    for (let k = adultesAvant.length; k < adultes; k++) adultesAvant.push(nouvelle(k, false, 0));
    for (let k = petitsAvant.length; k < petits; k++) petitsAvant.push(nouvelle(k, true, k % Math.max(1, adultes)));
    g.betes = [...adultesAvant, ...petitsAvant];
    g.adultes = adultes;
    g.petits = petits;
  }

  /**
   * Chaque image : le comportement de chaque bête proche, puis le dessin. `humains` : x, z (m) et id de chaque humain affiché ;
   * `nuit` : 0 en plein jour, 1 en pleine nuit ; `croissance` : âge des petits de la saison (0 à 1).
   */
  animer(dt: number, camera: THREE.Vector3, nuit: number, humains: Float64Array, nHumains: number, croissance: number): void {
    this.image++;
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    const o = this.origine;
    dt = Math.min(dt, 0.1);
    this.temps += dt;
    const ts = this.temps;
    for (const s of this.especes.values()) for (const r of s.rendus) if (r) r.n = 0;
    this.pietineurs.length = 0;
    for (const g of this.groupes.values()) {
      const d = Math.hypot(g.cx - camera.x, g.cz - camera.z);
      if (d > this.portee + g.rayon + 300) continue;
      const s = this.especes.get(g.espece.numero);
      if (!s || g.betes.length === 0) continue;
      this.comportement(g, s.gab, dt, ts, nuit, humains, nHumains, camera);
      const tPetit = 0.3 + 0.35 * croissance;
      for (const b of g.betes) {
        const db = Math.hypot(b.x - camera.x, b.z - camera.z);
        if (db > this.portee) continue;
        const taille = b.petit ? tPetit : b.taille;
        const L = g.L * taille;
        const niveau = db < SEUILS[0] * Math.max(0.6, L / 2) ? 0 : db < SEUILS[1] * Math.max(0.6, L / 2) ? 1 : 2;
        // La hauteur du sol coûte : à chaque image près de soi, de temps en temps au loin.
        if (niveau < 2 || this.image - b.imageSol > 8) {
          b.y = this.relief.hauteur(b.x, b.z);
          b.imageSol = this.image;
        }
        const r = this.rendu(g.espece.numero, niveau);
        if (!r) continue;
        const vM = vitesseMarche(L);
        const allure = b.v < 0.05 ? 0 : b.v <= vM * 1.25 ? b.v / vM : 1 + Math.min(1, (b.v - vM * 1.25) / Math.max(0.5, vitesseCourse(L) - vM * 1.25));
        r.ajouter(b.x - o.x, b.y, b.z - o.z, b.cap, L, b.phase, allure, b.tete, b.regard, b.couche, b.teinte, s.gab.hanche);
        if (db < 34 && this.pietineurs.length < 600) this.pietineurs.push(b.x, b.z, 0.12 + 0.18 * L);
      }
    }
    // Les prises rapportées par les chasseurs (0.44.1) : la bête gît à côté de lui quand il s'arrête.
    for (const p of this.prises) {
      const s = this.especes.get(p.espece);
      if (!s || Math.hypot(p.x - camera.x, p.z - camera.z) > Math.min(this.portee, 400)) continue;
      const L = longueur(s.e.forme, s.e.poids);
      const x = p.x + Math.cos(p.cap) * (0.6 + L * 0.15);
      const z = p.z - Math.sin(p.cap) * (0.6 + L * 0.15);
      const r = this.rendu(p.espece, Math.hypot(x - camera.x, z - camera.z) < 60 ? 0 : 1);
      r?.ajouter(x - o.x, this.relief.hauteur(x, z), z - o.z, p.cap + Math.PI / 2, L, 0, 0, -1, 0, 0, 0.8, s.gab.hanche);
    }
    for (const s of this.especes.values()) {
      for (const r of s.rendus) {
        if (!r) continue;
        r.maillage.position.copy(o);
        r.envoyer();
      }
    }
  }

  /** Ce que fait chaque bête du groupe à cette image. */
  private comportement(g: Groupe, gab: Gabarit, dt: number, ts: number, nuit: number, humains: Float64Array, nH: number, camera: THREE.Vector3): void {
    const e = g.espece;
    const panique = e.panique / 1000;
    const agressivite = e.agressivite / 1000;
    const carnivore = e.regime === 2;
    // Actives le jour (herbivores, omnivores) ; les chasseurs rôdent plutôt à l'aube, au crépuscule et la nuit, et dorment à midi.
    const actif = carnivore ? nuit > 0.25 || unite(hacher(g.id, Math.floor(ts / 300))) < 0.3 : nuit < 0.65;
    // Distance de fuite (m) : bêtes craintives et grandes bêtes de loin ; la docilité l'efface chez les bêtes gardées.
    let fuite = (5 + 45 * panique) * (0.7 + 0.6 * Math.min(1, e.poids / 150));
    if (g.gardee) fuite *= (1 - g.docilite) ** 2;
    if (g.chiens) fuite = 0;
    // Les humains proches du groupe, sauf le gardien pour des bêtes qui le connaissent.
    const prochesX: number[] = [];
    const prochesZ: number[] = [];
    const ref = g.betes[0];
    const portee = fuite * 1.8 + 40 + g.L * 6;
    let gardienX = NaN;
    let gardienZ = NaN;
    for (let k = 0; k < nH; k++) {
      const hx = humains[k * 3];
      const hz = humains[k * 3 + 1];
      const id = humains[k * 3 + 2];
      if (g.gardee && id === g.gardien) {
        gardienX = hx;
        gardienZ = hz;
        if (g.docilite > 0.25) continue;
      }
      if (Math.abs(hx - ref.x) < portee + 60 && Math.abs(hz - ref.z) < portee + 60) {
        prochesX.push(hx);
        prochesZ.push(hz);
      }
    }
    // Le but de la meneuse.
    const meneuse = g.betes[0];
    const jour = nuit < 0.5;
    const enPature = g.gardee && !g.chiens && jour && !Number.isNaN(gardienX) && !!g.enclos && Math.hypot(gardienX - g.enclos.x, gardienZ - g.enclos.z) < 1500;
    if (g.chiens) {
      g.but = !Number.isNaN(gardienX) ? [gardienX, gardienZ] : [g.cx, g.cz];
    } else if (enPature) {
      // À la pâture : le troupeau broute derrière son gardien, plus loin s'il est farouche.
      const recul = (6 + 20 * (1 - g.docilite)) * Math.max(1, g.L / 2);
      const a = unite(hacher(g.id, Math.floor(ts / 120), 3)) * DEUX_PI;
      g.but = [gardienX + Math.cos(a) * recul, gardienZ + Math.sin(a) * recul];
    } else if (!g.but || ts > g.butJusqua || Math.hypot(meneuse.x - g.but[0], meneuse.z - g.but[1]) < g.L * 1.5) {
      const graine = hacher(g.id, Math.floor(ts * 10));
      if (g.enclos) {
        const a = unite(graine) * DEUX_PI;
        const r = g.enclos.r * 0.55 * Math.sqrt(unite(hacher(graine, 1)));
        g.but = [g.enclos.x + Math.cos(a) * r, g.enclos.z + Math.sin(a) * r];
      } else {
        // Errer dans son domaine ; parfois aller boire si l'eau est proche.
        const x = g.cx + (unite(graine) - 0.5) * g.rayon * 2;
        const z = g.cz + (unite(hacher(graine, 1)) - 0.5) * g.rayon * 2;
        g.but = aPiedSec(this.relief, x, z, 0.15, graine);
      }
      g.butJusqua = ts + 25 + 70 * unite(hacher(graine, 2));
    }
    const vM = vitesseMarche(g.L);
    const vC = vitesseCourse(g.L);
    for (let k = 0; k < g.betes.length; k++) {
      const b = g.betes[k];
      const L = g.L * b.taille;
      // Le danger le plus proche.
      let dMin = Infinity;
      let hx = 0;
      let hz = 0;
      for (let j = 0; j < prochesX.length; j++) {
        const dd = Math.hypot(prochesX[j] - b.x, prochesZ[j] - b.z);
        if (dd < dMin) [dMin, hx, hz] = [dd, prochesX[j], prochesZ[j]];
      }
      let vVoulue = 0;
      let capVoulu = b.cap;
      let teteVoulue = 0;
      let regardVoulu = 0;
      let coucheVoulue = 0;
      const irascible = !b.petit && agressivite > 0.6 && e.poids > 35 && unite(hacher(g.id, k, 71)) < agressivite;
      // Alerte : danger proche, ou le troupeau qui fuit.
      if (dMin < fuite && ts > b.repos) {
        b.dx = hx;
        b.dz = hz;
        if (irascible && b.etat !== "fuir") {
          b.etat = dMin < 3 + L ? "toiser" : "charger";
          b.finAlerte = ts + (b.etat === "toiser" ? 3 : 1);
        } else {
          b.etat = "fuir";
          b.finAlerte = ts + 4 + 4 * panique;
          if (e.troupeau) [g.alerte, g.alerteX, g.alerteZ] = [ts + 5, hx, hz];
        }
      } else if (e.troupeau && ts < g.alerte && b.etat !== "fuir" && b.etat !== "charger" && b.etat !== "toiser" && !g.chiens) {
        b.etat = "fuir";
        b.dx = g.alerteX;
        b.dz = g.alerteZ;
        b.finAlerte = g.alerte;
      }
      if ((b.etat === "fuir" || b.etat === "charger" || b.etat === "toiser") && ts > b.finAlerte) {
        if (b.etat === "toiser") b.repos = ts + 15;
        b.etat = "marcher";
      }
      if (b.etat === "fuir") {
        capVoulu = Math.atan2(b.x - b.dx, b.z - b.dz) + (unite(hacher(g.id, k, 73)) - 0.5) * 0.6;
        vVoulue = vC * (0.8 + 0.2 * panique);
      } else if (b.etat === "charger") {
        capVoulu = Math.atan2(b.dx - b.x, b.dz - b.z);
        vVoulue = vC * 0.85;
        teteVoulue = 0.25;
        // Elle s'arrête net à quelques mètres (charge d'intimidation) : il lui faut v²/2a pour freiner.
        if (Math.hypot(b.dx - b.x, b.dz - b.z) < 3 + L + (b.v * b.v) / 20) {
          b.etat = "toiser";
          b.finAlerte = ts + 3;
        }
      } else if (b.etat === "toiser") {
        // Elle fait face, tête basse (cornes en avant), puis s'en va.
        capVoulu = Math.atan2(b.dx - b.x, b.dz - b.z);
        teteVoulue = 0.35;
      } else if (!actif && !enPature && !g.chiens || g.chiens && nuit > 0.6 && Number.isNaN(gardienX)) {
        // Repos : on rejoint le groupe, puis on se couche.
        const [px, pz] = b === meneuse || !e.troupeau ? [b.x, b.z] : this.place(g, b, meneuse);
        const dp = Math.hypot(px - b.x, pz - b.z);
        if (dp > L * 2.5 && b.couche < 0.5) {
          capVoulu = Math.atan2(px - b.x, pz - b.z);
          vVoulue = dp > 25 * Math.max(1, L / 2) ? Math.min(vC * 0.6, vM * 2.2) : vM * 0.8;
          b.etat = "marcher";
        } else {
          b.etat = "dormir";
          coucheVoulue = 1;
          teteVoulue = 0.4;
        }
      } else {
        // Vigilance : on relève la tête et l'on regarde l'humain qui approche.
        const vigilant = dMin < Math.max(fuite * 1.8, 8);
        let px: number;
        let pz: number;
        if (b.petit) {
          const mere = g.betes[Math.min(b.mere, g.betes.length - 1)];
          px = mere.x - Math.sin(mere.cap) * g.L * 0.7 + Math.cos(b.placeA) * g.L * 0.4;
          pz = mere.z - Math.cos(mere.cap) * g.L * 0.7 + Math.sin(b.placeA) * g.L * 0.4;
        } else if (g.chiens) {
          // Les chiens suivent leur maître à quelques mètres.
          px = g.but![0] + Math.cos(b.placeA + ts * 0.05) * (2.5 + b.placeR);
          pz = g.but![1] + Math.sin(b.placeA + ts * 0.05) * (2.5 + b.placeR);
        } else if (k === 0 || !e.troupeau && !g.enclos) {
          // La meneuse (ou une bête solitaire) : son but, ou le sien propre.
          if (k > 0) {
            if (!b.but || ts > b.jusqua || Math.hypot(b.x - b.but[0], b.z - b.but[1]) < L) {
              const graine = hacher(g.id, k, Math.floor(ts * 10));
              const ang = unite(graine) * DEUX_PI;
              const r = (10 + 40 * unite(hacher(graine, 1))) * Math.max(1, g.L / 2);
              b.but = aPiedSec(this.relief, Math.max(g.cx - g.rayon, Math.min(g.cx + g.rayon, b.x + Math.cos(ang) * r)),
                Math.max(g.cz - g.rayon, Math.min(g.cz + g.rayon, b.z + Math.sin(ang) * r)), 0.15, graine);
              b.jusqua = ts + 20 + 50 * unite(hacher(graine, 2));
            }
            [px, pz] = b.but;
          } else [px, pz] = g.but!;
        } else {
          [px, pz] = this.place(g, b, meneuse);
        }
        const dp = Math.hypot(px - b.x, pz - b.z);
        const proche = (b.petit ? 0.6 : g.chiens ? 2 : 1.5) * L;
        if (dp > proche) {
          capVoulu = Math.atan2(px - b.x, pz - b.z);
          // Au trot si l'on est loin derrière.
          vVoulue = k > 0 && dp > 25 * Math.max(1, L / 2) ? Math.min(vC * 0.6, vM * 2.2) : vM * (b.petit ? 1.3 : 1);
          b.etat = "marcher";
        } else {
          b.etat = "paitre";
          // Brouter : la tête monte et descend ; un chasseur, lui, reste aux aguets.
          const broute = carnivore ? 0 : 0.5 + 0.5 * Math.sin(ts * 0.3 + k * 1.7 + g.id);
          teteVoulue = broute > 0.35 ? 1 : 0.2;
          // Quelques pas de temps en temps en broutant.
          if (!carnivore && unite(hacher(g.id, k, Math.floor(ts / 7))) < 0.15) vVoulue = vM * 0.3;
          if (g.chiens && nuit > 0.6) coucheVoulue = 1;
        }
        if (vigilant && Number.isFinite(dMin)) {
          teteVoulue = 0;
          regardVoulu = Math.max(-1.1, Math.min(1.1, angle(Math.atan2(hx - b.x, hz - b.z) - b.cap)));
          if (b.etat === "paitre") vVoulue = 0;
        }
      }
      // Bêtes en enclos : elles restent dedans (sauf à la pâture), les farouches tournent le long de la clôture.
      // Mouvement : on tourne vers le cap voulu, on accélère ou ralentit.
      const virage = (b.etat === "fuir" || b.etat === "charger" ? 4 : 2.2) * dt;
      const ecart = angle(capVoulu - b.cap);
      if (vVoulue > 0.01 || b.etat === "toiser") b.cap += Math.max(-virage, Math.min(virage, ecart));
      // Une bête ne court pas droit devant si elle doit d'abord faire demi-tour.
      if (Math.abs(ecart) > 1.6) vVoulue *= 0.3;
      const acc = (b.etat === "fuir" ? 6 : 2.5) * dt;
      b.v += Math.max(b.etat === "toiser" ? -10 * dt : -acc * 1.5, Math.min(acc, vVoulue - b.v));
      if (b.v < 0.02 && vVoulue === 0) b.v = 0;
      if (coucheVoulue > 0 && b.v > 0.05) coucheVoulue = 0;
      b.couche += (coucheVoulue - b.couche) * Math.min(1, dt * 0.8);
      b.tete += (teteVoulue - b.tete) * Math.min(1, dt * 2.5);
      b.regard += (regardVoulu - b.regard) * Math.min(1, dt * 3);
      if (b.v > 0) {
        const nx = b.x + Math.sin(b.cap) * b.v * dt;
        const nz = b.z + Math.cos(b.cap) * b.v * dt;
        const s = this.relief.surface(nx, nz);
        // Pas dans l'eau profonde : elle s'arrête au bord et cherchera un autre but.
        if (!Number.isNaN(s.eau) && s.eau - s.sol > gabProfond(gab, L)) {
          b.v = 0;
          b.cap += Math.PI * 0.5;
          if (k === 0) g.butJusqua = 0;
          b.jusqua = 0;
        } else {
          b.x = nx;
          b.z = nz;
        }
        b.phase = (b.phase + (DEUX_PI * b.v * dt) / (gab.foulee * L * (b.v > vM * 1.3 ? 1.5 : 1))) % (DEUX_PI * 64);
      }
      // Enclos : on n'en sort pas, sauf à la pâture ; une bête très farouche s'en échappe parfois un moment.
      if (g.enclos && !enPature) {
        const echappee = g.docilite < 0.25 && unite(hacher(g.id, k, Math.floor(ts / 90))) < 0.12;
        const ex = b.x - g.enclos.x;
        const ez = b.z - g.enclos.z;
        const de = Math.hypot(ex, ez);
        const borne = g.enclos.r * 0.92 - L * 0.3 + (echappee ? 10 : 0);
        if (de > borne && de > 0) {
          b.x = g.enclos.x + (ex / de) * borne;
          b.z = g.enclos.z + (ez / de) * borne;
          if (b.etat !== "fuir") b.cap += Math.PI * 0.6 * dt * 4;
        }
      }
      void camera;
    }
  }

  /** Place d'une bête autour de la meneuse (le troupeau se tient groupé, chacune à sa place, sans s'empiler). */
  private place(g: Groupe, b: Bete, meneuse: Bete): [number, number] {
    if (b === meneuse) return g.but ?? [b.x, b.z];
    const r = b.placeR * g.L * (g.enclos ? 0.6 : 1);
    const a = b.placeA + meneuse.cap;
    return [meneuse.x + Math.sin(a) * r - Math.sin(meneuse.cap) * g.L, meneuse.z + Math.cos(a) * r - Math.cos(meneuse.cap) * g.L];
  }

  /** La bête sous un rayon (clic), la plus proche ; null si aucune. */
  sous(origine: THREE.Vector3, direction: THREE.Vector3, max = 400): string | null {
    let meilleur = max;
    let cle: string | null = null;
    const c = new THREE.Vector3();
    for (const g of this.groupes.values()) {
      const s = this.especes.get(g.espece.numero);
      if (!s) continue;
      g.betes.forEach((b, k) => {
        const L = g.L * b.taille;
        c.set(b.x, b.y + s.gab.hanche * L * (1 - 0.6 * b.couche), b.z).sub(origine);
        const t = c.dot(direction);
        if (t <= 0 || t > meilleur) return;
        const ecart = c.addScaledVector(direction, -t).length();
        if (ecart < Math.max(0.25, L * 0.32)) [meilleur, cle] = [t, `${g.id}:${k}`];
      });
    }
    return cle;
  }

  /** La bête la plus proche devant soi (à moins de 60 m, dans un cône d'environ 60°), ou null. */
  devant(camera: THREE.Vector3, avant: THREE.Vector3): string | null {
    let meilleur = 60;
    let cle: string | null = null;
    for (const g of this.groupes.values()) {
      g.betes.forEach((b, k) => {
        const vx = b.x - camera.x;
        const vy = b.y - camera.y;
        const vz = b.z - camera.z;
        const d = Math.hypot(vx, vy, vz);
        if (d < meilleur && vx * avant.x + vy * avant.y + vz * avant.z > d * 0.5) [meilleur, cle] = [d, `${g.id}:${k}`];
      });
    }
    return cle;
  }

  /** Où est une bête (milieu du dos), son cap, sa longueur, et ce qu'il faut pour demander sa fiche ; null si elle n'est plus là. */
  etat(cle: string): { centre: THREE.Vector3; cap: number; longueur: number; groupe: number; espece: number; enMarche: boolean } | null {
    const [gid, k] = cle.split(":").map(Number);
    const g = this.groupes.get(gid);
    const b = g?.betes[k];
    const s = g ? this.especes.get(g.espece.numero) : null;
    if (!g || !b || !s) return null;
    const L = g.L * b.taille;
    return { centre: new THREE.Vector3(b.x, b.y + s.gab.dos * L * (1 - 0.5 * b.couche), b.z), cap: b.cap, longueur: L, groupe: g.serveur, espece: g.espece.numero, enMarche: b.v > 0.1 };
  }

  /** Nombre de bêtes montrées à la dernière image (compteur, touche I). */
  get montrees(): number {
    let n = 0;
    for (const s of this.especes.values()) for (const r of s.rendus) if (r) n += r.n;
    return n;
  }
}

/** Allure de marche (m/s) d'une bête de cette longueur : environ 1,2 m/s pour 2 m (ordres de grandeur des pistes fossiles). */
function vitesseMarche(L: number): number {
  return 0.4 + 0.42 * L;
}

/** Course (m/s) : de 5 à 13 m/s selon la taille (les petits coureurs sont rapides, Alexander 1976). */
function vitesseCourse(L: number): number {
  return Math.min(13, 2.5 + 2.8 * L);
}

/** Profondeur d'eau où une bête ne va plus : à mi-hauteur de la hanche. */
function gabProfond(gab: Gabarit, L: number): number {
  return Math.max(0.15, gab.hanche * L * 0.6);
}
