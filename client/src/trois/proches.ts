import * as THREE from "three/webgpu";
import { hacher, lisser, unite } from "./bruit";
import { Corps, PO, cleDe, formeDe } from "./corps";
import { A, porteurDe, type Corps as Etat, type Foule } from "./foule";
import { TeteDetaillee, type Expression } from "./tete";
import { baseHumaine, construireReel } from "./humain";
import { chargerVisages, couleurs, visageConnu, type Visage } from "../visage";
import { echange } from "../dialogue";
import { Animateur, mainVersPoint, osEn, type Situation, type SolLocal } from "./pose";
import { ARME, Armes3D, type ArmeEnMain } from "./combat";

/** Réglage des corps détaillés selon la Qualité : jusqu'où, et combien à la fois (proposition G1). */
export interface ReglageProches {
  portee: number;
  nombre: number;
  /** Jusqu'où les visages sont détaillés (m), et combien à la fois (0.41.2). */
  visages: number;
  nombreVisages: number;
  /** Corps réalistes (0.41.4) : combien au plus, et jusqu'où (m) ; au-delà, corps calculés. */
  reels: number;
  porteeReels: number;
}

/** Expressions visées selon l'humeur et la peur que donne le flux Scène (codes de MondeSimule.Scene). */
function expressionsDe(humeur: number, peur: number): Record<Expression, number> {
  const e: Record<Expression, number> = { joie: 0, triste: 0, colere: 0, peur: 0, bouche: 0, cligne: 0 };
  switch (humeur) {
    case 0: e.triste = 0.55; break;
    case 1: e.triste = 0.3; break;
    case 2: e.joie = 0.12; break;
    case 3: e.joie = 0.45; break;
    case 4: e.joie = 0.85; break;
    case 5: e.triste = 0.8; break;
    case 6: e.colere = 0.85; break;
    case 7: e.triste = 0.9; break;
  }
  if (peur > 40) {
    e.peur = Math.min(1, (peur - 40) / 45);
    e.joie *= 1 - e.peur;
  }
  return e;
}

/** Clé d'un visage : ce qui le change visiblement (l'âge par année, la faim, la santé). */
function cleVisage(v: Visage): string {
  const r = (x: number, pas: number) => Math.round(x / pas);
  return [v.id, r(v.age, 1), r(v.maigreur, 0.15), r(v.paleur, 0.2), v.fievre ? 1 : 0, v.cicatrice ? 1 : 0, r(v.gris, 0.15), r(v.calvitie, 0.15)].join(",");
}

const AXE_Y = new THREE.Vector3(0, 1, 0);
const V1 = new THREE.Vector3();
const V2 = new THREE.Vector3();
const V3 = new THREE.Vector3();
const M1 = new THREE.Matrix4();
const M2 = new THREE.Matrix4();

/** Rotation qui envoie les axes (haut, avant) d'un repère sur ceux d'un autre (vecteurs normés, presque orthogonaux). */
function aligner(hautDe: THREE.Vector3, avantDe: THREE.Vector3, hautVers: THREE.Vector3, avantVers: THREE.Vector3, q: THREE.Quaternion): THREE.Quaternion {
  const base = (h: THREE.Vector3, a: THREE.Vector3, m: THREE.Matrix4) => {
    const y = h.clone().normalize();
    const z = a.clone().addScaledVector(y, -a.dot(y)).normalize();
    const x = y.clone().cross(z);
    return m.makeBasis(x, y, z);
  };
  base(hautDe, avantDe, M1);
  base(hautVers, avantVers, M2);
  return q.setFromRotationMatrix(M2.multiply(M1.transpose()));
}

/**
 * Les humains proches de la caméra en corps détaillés (0.41.1) ; les autres restent en silhouettes (foule.ts). Un corps n'est
 * construit que si sa forme a changé (âge, grossesse, vêtement…), et pas plus de deux par image pour ne pas saccader.
 */
export class CorpsProches {
  readonly groupe = new THREE.Group();
  /** Armes tenues (0.42.0). */
  private readonly armes = new Armes3D();
  private readonly enMain: ArmeEnMain[] = [];
  private readonly corps = new Map<number, Corps>();
  private readonly materiau = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.72, metalness: 0 });
  reglage: ReglageProches = { portee: 90, nombre: 50, visages: 8, nombreVisages: 8, reels: 16, porteeReels: 30 };
  private readonly tetes = new Map<number, TeteDetaillee>();
  private readonly expressions = new Map<number, Record<Expression, number>>();
  private visagesDemandes = 0;
  /**
   * Temps et feu autour (0.43.0, H1 en partie) : pluie ou neige qui tombe là (0 à 1), et proximité d'un incendie (0 à 1 selon
   * le point). Sous la pluie, on rentre les épaules et on serre les bras comme dans le froid ; près du feu, la peur.
   */
  pluie = 0;
  feuEn: (x: number, z: number) => number = () => 0;
  /**
   * Catastrophes (0.43.1, H1) : direction d'un phénomène du ciel (comète, aurore, éclipse, boule de feu) vers lequel la plupart
   * lèvent les yeux, et effroi d'un séisme qu'on sent (0 à 1).
   */
  regardCiel: THREE.Vector3 | null = null;
  effroi = 0;
  /** Humain toujours détaillé, corps et visage, où qu'il soit (suivi ou Portrait, 0.41.3). */
  force: number | null = null;
  /** Corps réalistes MakeHuman (0.41.4) ; faux : corps calculés de la 0.41.1. */
  reels = true;
  private readonly matrice = new THREE.Matrix4();
  /** Animation de chacun (0.41.7) : elle survit à son corps, refait quand sa forme change. */
  private readonly animateurs = new Map<number, Animateur>();
  /** Ce que chacun regarde, et quand il en change. */
  private readonly regards = new Map<number, { id: number; jusqua: number; vus: number[] }>();

  constructor(private readonly foule: Foule) {
    this.groupe.add(this.armes.groupe);
  }

  /** Couleurs de peau et de cheveux du portrait, quand le visage est connu (les mêmes qu'à la fiche). */
  private couleursDe(c: Etat): [THREE.Color, THREE.Color] {
    const v = visageConnu(c.s.id);
    if (!v) return [c.peau, c.cheveux];
    const k = couleurs(v);
    const lin = (rgb: [number, number, number]) => new THREE.Color().setRGB(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, THREE.SRGBColorSpace);
    return [lin(k.peau), lin(k.cheveux)];
  }

  mettreAJour(camera: THREE.Vector3, temps: number): void {
    this.enMain.length = 0;
    // Les plus proches, dans la portée.
    const candidats: { c: ReturnType<Foule["placement"]> & { id: number }; d: number; corps: Etat }[] = [];
    for (const c of this.foule.liste) {
      if (Number.isNaN(c.x)) continue;
      const pl = this.foule.placement(c);
      const d = Math.hypot(pl.x - camera.x, pl.y - camera.y, pl.z - camera.z);
      if (d < this.reglage.portee || c.s.id === this.force) candidats.push({ c: { ...pl, id: c.s.id }, d: c.s.id === this.force ? -1 : d, corps: c });
    }
    candidats.sort((a, b) => a.d - b.d);
    const gardes = candidats.slice(0, this.reglage.nombre);
    const voulus = new Set(gardes.map((g) => g.c.id));
    for (const [id, corps] of this.corps) {
      if (!voulus.has(id)) {
        corps.liberer();
        this.corps.delete(id);
      }
    }
    // Visages des corps détaillés (leurs couleurs, et la tête détaillée de près) : demandés toutes les deux secondes.
    if (temps - this.visagesDemandes > 2 && gardes.length > 0) {
      this.visagesDemandes = temps;
      chargerVisages(gardes.map((g) => g.c.id), () => undefined);
    }
    let tetesFaites = 0;
    const tetesVoulues = new Set(gardes.filter((g) => g.d < this.reglage.visages).slice(0, this.reglage.nombreVisages).map((g) => g.c.id));
    if (this.force !== null && gardes.some((g) => g.c.id === this.force)) tetesVoulues.add(this.force);
    for (const [id, tete] of this.tetes) {
      if (!tetesVoulues.has(id) || !this.corps.has(id) || this.corps.get(id)!.anime) {
        tete.liberer();
        this.tetes.delete(id);
        this.corps.get(id)?.teteSimple(true);
      }
    }
    this.foule.detailles.clear();
    let construits = 0;
    // Corps réalistes (0.41.4) dès que la base MakeHuman est chargée ; corps calculés en attendant (ou si elle manque).
    const base = this.reels ? baseHumaine() : null;
    let rang = 0;
    const poses: { pl: (typeof gardes)[number]["c"]; c: Etat; corps: Corps; d: number }[] = [];
    const peau = (c: Etat) => this.couleursDe(c)[0];
    const cheveux = (c: Etat) => this.couleursDe(c)[1];
    for (const { c: pl, corps: c, d } of gardes) {
      const [peauC, cheveuxC] = this.couleursDe(c);
      const forme = formeDe(c.s, peauC, cheveuxC);
      let corps = this.corps.get(pl.id);
      // Réaliste : les plus proches, dans la portée (un peu plus pour qui l'est déjà, pour ne pas basculer sans cesse).
      const deja = corps?.anime ? 1.2 : 1;
      const reel = !!base && (c.s.id === this.force || (rang < this.reglage.reels * deja && d < this.reglage.porteeReels * deja));
      if (reel) rang++;
      const visage = reel ? visageConnu(c.s.id) ?? null : null;
      const cle = reel ? `${cleDe(forme)}|R|${visage ? cleVisage(visage) : "-"}` : cleDe(forme);
      if (!corps || corps.cle !== cle) {
        // Pas plus de deux constructions par image (une seule réaliste) : les suivants gardent leur silhouette (ou leur ancien
        // corps) en attendant.
        if (construits >= (reel ? 1 : 2)) {
          if (!corps) continue;
        } else {
          construits++;
          corps?.liberer();
          corps = reel ? new Corps(forme, this.materiau, construireReel(base!, forme, visage, c.s.id), cle) : new Corps(forme, this.materiau);
          this.corps.set(pl.id, corps);
          this.groupe.add(corps.maillage);
        }
      }
      this.foule.detailles.add(pl.id);
      poses.push({ pl, c, corps, d });
    }
    // Les mères d'abord : leurs enfants portés se placent dans leurs bras.
    poses.sort((a, b) => (a.c.posture === "porte" ? 1 : 0) - (b.c.posture === "porte" ? 1 : 0));
    const enfants = new Map<number, Etat>();
    for (const c of this.foule.liste) if (c.posture === "porte" && porteurDe(c.s) > 0) enfants.set(porteurDe(c.s), c);
    const places = new Map<number, { x: number; y: number; z: number; h: number }>();
    for (const { pl, c } of poses) places.set(pl.id, { x: pl.x, y: pl.y, z: pl.z, h: c.taille });
    // Ceux qui marchent (H1) : on lève la tête vers qui arrive.
    const arrivants: { id: number; x: number; z: number; cap: number }[] = [];
    for (const { pl, c } of poses) if (c.posture === "marche") arrivants.push({ id: pl.id, x: pl.x, z: pl.z, cap: pl.cap });
    for (const { pl, c, corps } of poses) {
      const s = c.s;
      let anim = this.animateurs.get(s.id);
      if (!anim) {
        anim = new Animateur(s.id);
        this.animateurs.set(s.id, anim);
      }
      const m = corps.maillage;
      const h = c.taille;
      const enfant = enfants.get(s.id);
      const porteur = porteurDe(s);
      const mere = c.posture === "porte" ? this.corps.get(porteur) : undefined;
      const etatMere = mere ? [...this.foule.liste].find((e) => e.s.id === porteur) : undefined;
      const dort = (c.posture === "couche" && (s.activite === 1 || s.activite === 7)) || c.role?.mort === true;
      const sit: Situation = {
        posture: c.posture, id: s.id, age: s.age, femme: s.femme, voute: formeDe(s, c.peau, c.cheveux).voute, boite: (s.etat & 8) !== 0,
        activite: s.activite, phase: c.phase, vitesse: c.vitesse, endurance: s.endurance, reserves: s.reserves,
        enfant: enfant && (s.etat & 4) !== 0 ? enfant.s.age : (s.etat & 4) !== 0 ? 0.4 : null,
        porteur: etatMere ? (etatMere.s.activite === A.Allaiter && (etatMere.s.etat & 4) !== 0 ? "allaite" : etatMere.posture) : null, dort, ecoute: c.ecoute,
        allaite: s.activite === A.Allaiter && (s.etat & 4) !== 0,
        // 0.45.0 : endormie, le tout-petit contre elle : couchée en cercle autour de lui (Ball, 2006).
        berceau: c.posture === "couche" && dort && enfant !== undefined && (enfant.s.coucher === 1 || enfant.s.coucher === 3),
        // F1 (0.41.8) : ce que vit l'humain ; la maladie pèse d'autant plus que la santé est basse.
        froid: Math.max(lisser(15, 70, s.froid), dort || c.posture === "nage" ? 0 : this.pluie * 0.6), faim: s.faim / 100, fatigue: s.fatigue / 100,
        peur: Math.max(lisser(10, 70, s.peur), dort ? 0 : Math.max(this.feuEn(pl.x, pl.z) * 0.7, this.effroi)),
        malade: s.etat & 1 ? 0.6 + 0.4 * (1 - s.sante / 100) : lisser(60, 25, s.sante), enceinte: (s.etat & 2) !== 0,
        humeur: s.humeur, nevrosisme: s.nevrosisme, extraversion: s.extraversion, nage: s.nage,
        parle: c.parle, converse: c.interlocuteur > 0 && c.posture !== "enlace",
        enlace: c.posture === "enlace" && c.partenaire ? (s.id < c.partenaire ? 1 : -1) : 0,
        combat: c.posture === "combat" && c.role?.geste ? { geste: c.role.geste, u: c.role.u, arme: c.role.arme, mort: c.role.mort } : null,
        outil: c.posture === "debout" || c.posture === "marche" ? (s.etat >> 11) & 7 : 0,
      };
      // Ce qu'il regarde (E1) : l'Entité qui lui parle, sinon tour à tour les gens proches, sa mère, son enfant.
      anim.ecoute = c.ecoute > 0.3;
      // En combat (0.42.0), on ne quitte pas l'adversaire des yeux.
      const adversaire = c.role?.face ? this.foule.position(c.role.face) : null;
      anim.regard = adversaire && !dort ? adversaire : c.ecoute > 0.3 ? camera : dort ? null : this.choisirRegard(s.id, s.mere, enfant?.s.id ?? 0, c.interlocuteur, places, pl, temps, arrivants);
      // Un phénomène du ciel : huit sur dix lèvent les yeux (les autres ne l'ont pas remarqué, ou s'en détournent).
      if (this.regardCiel && !dort && !adversaire && c.ecoute <= 0.3 && unite(hacher(s.id, 77, 5)) < 0.8) {
        anim.regard = new THREE.Vector3(pl.x, pl.y + 1.5, pl.z).addScaledVector(this.regardCiel, 1000);
      }
      // Au sein, elle regarde son enfant.
      const auSein = sit.allaite && enfant ? this.corps.get(enfant.s.id) : undefined;
      if (auSein && c.ecoute <= 0.3) anim.regard = auSein.maillage.localToWorld(osEn(auSein, "tete", new THREE.Vector3()));
      // Pente du sol sous lui, dans son repère (taille 1).
      let sol: SolLocal | null = null;
      if (c.posture !== "nage" && !(c.posture === "porte" && mere && sit.porteur !== "couche")) {
        const rel = this.foule.relief;
        const e = 0.5;
        const gx = (rel.hauteur(pl.x + e, pl.z) - rel.hauteur(pl.x - e, pl.z)) / (2 * e);
        const gz = (rel.hauteur(pl.x, pl.z + e) - rel.hauteur(pl.x, pl.z - e)) / (2 * e);
        const co = Math.cos(pl.cap);
        const si = Math.sin(pl.cap);
        const borne = (v: number) => Math.max(-0.7, Math.min(0.7, v));
        sol = { ax: borne(gx * co - gz * si), az: borne(gx * si + gz * co) };
      }
      m.scale.setScalar(h);
      m.quaternion.setFromAxisAngle(AXE_Y, pl.cap);
      m.position.set(pl.x, c.posture === "nage" ? pl.y - corps.hanche * h + 0.12 : pl.y, pl.z);
      if (c.posture === "porte" && mere && sit.porteur === "couche") {
        // Couché contre sa mère couchée, devant sa poitrine.
        osEn(mere, "poitrine", V1);
        mere.maillage.localToWorld(V1);
        const cap = mere.maillage.rotation.y;
        m.position.set(V1.x + Math.sin(cap) * 0.22, places.get(porteur)?.y ?? pl.y, V1.z + Math.cos(cap) * 0.22);
        m.quaternion.copy(mere.maillage.quaternion);
      }
      m.updateMatrixWorld();
      anim.animer(corps, sit, temps, sol);
      this.armeEnMain(c, corps);
      if (c.posture === "porte" && mere && sit.porteur !== "couche") {
        // Seul l'enfant porté bouge après l'animation : les autres squelettes sont déjà à jour.
        this.placerDansLesBras(corps, h, mere, places.get(porteur)?.h ?? 1.6, s.age, sit.porteur ?? "debout");
        m.updateMatrixWorld();
        corps.armature.updateMatrixWorld(true);
      }
      // Tête détaillée, de près (le corps réaliste a déjà son visage : on l'anime).
      if (corps.anime) {
        if (tetesVoulues.has(pl.id)) this.animerVisage(c, corps, anim.regard, temps);
      } else if (tetesVoulues.has(pl.id)) this.poserTete(c, corps, peau(c), cheveux(c), anim.regard, temps, tetesFaites < 1 ? () => tetesFaites++ : null);
    }
    for (const id of this.animateurs.keys()) if (!this.corps.has(id)) this.animateurs.delete(id);
    this.armes.poser(this.enMain);
  }

  /**
   * L'arme qu'il a en main (0.42.0) : celle du combat rejoué, sinon l'arme taillée qu'il porte toujours (épieu, lance, hache),
   * tenue quand il est debout ou marche. Un mort l'a lâchée.
   */
  private armeEnMain(c: Etat, corps: Corps): void {
    const combat = c.posture === "combat" && c.role;
    const arme = combat ? (c.role!.mort || c.role!.geste === "chute" || (c.role!.geste === "lancer" && c.role!.u > 0.5) ? 0 : c.role!.arme)
      : c.posture === "debout" || c.posture === "marche" ? (c.s.etat >> 11) & 7 : 0;
    if (arme === ARME.Poings || corps.idx[PO.mainD] < 0) return;
    const m = corps.maillage;
    const poing = m.localToWorld(osEn(corps, "mainD", new THREE.Vector3()));
    const coude = m.localToWorld(osEn(corps, "avantBrasD", new THREE.Vector3()));
    const cap = m.rotation.y;
    this.enMain.push({ arme, poing, coude, droite: new THREE.Vector3(Math.cos(cap), 0, -Math.sin(cap)), porte: !combat ? m.position : undefined });
  }

  /** Change de temps en temps ce qu'un humain regarde : quelqu'un de proche (sa mère, son enfant d'abord), ou rien. */
  private choisirRegard(id: number, mere: number, enfant: number, interlocuteur: number,
    places: Map<number, { x: number; y: number; z: number; h: number }>, pl: { x: number; z: number }, temps: number,
    arrivants: readonly { id: number; x: number; z: number; cap: number }[]): THREE.Vector3 | null {
    let r = this.regards.get(id);
    // Qui s'approche (à moins de 12 m, droit vers lui) attire le regard aussitôt, une fois.
    let arrive = 0;
    for (const a of arrivants) {
      if (a.id === id) continue;
      const dx = pl.x - a.x;
      const dz = pl.z - a.z;
      const d = Math.hypot(dx, dz);
      if (d < 12 && d > 1 && (Math.sin(a.cap) * dx + Math.cos(a.cap) * dz) / d > 0.8) arrive = a.id;
    }
    if (arrive && r?.id !== arrive && !(r?.vus ?? []).includes(arrive) && unite(hacher(id, arrive, 139)) < 0.85) {
      r = { id: arrive, jusqua: temps + 1.5 + 2 * unite(hacher(id, arrive, 141)), vus: [...(r?.vus ?? []).slice(-4), arrive] };
      this.regards.set(id, r);
    }
    if (!r || temps > r.jusqua || (r.id && !places.has(r.id))) {
      const proches: number[] = [];
      for (const [k, q] of places) if (k !== id && Math.hypot(q.x - pl.x, q.z - pl.z) < 7) proches.push(k);
      const h = unite(hacher(id, Math.floor(temps * 3), 131));
      let choix = 0;
      // En conversation, on regarde surtout l'autre (et de temps en temps ailleurs, comme on le fait en parlant).
      if (interlocuteur && places.has(interlocuteur) && h < 0.75) choix = interlocuteur;
      else if (enfant && places.has(enfant) && h < 0.45) choix = enfant;
      else if (mere && places.has(mere) && h < 0.5) choix = mere;
      else if (proches.length > 0 && h < 0.75) choix = proches[Math.floor(unite(hacher(id, Math.floor(temps * 3), 133)) * proches.length)];
      r = { id: choix, jusqua: temps + 1.5 + 5 * unite(hacher(id, Math.floor(temps * 3), 137)), vus: r?.vus ?? [] };
      this.regards.set(id, r);
    }
    const q = r.id ? places.get(r.id) : undefined;
    if (!q) return null;
    const corps = this.corps.get(r.id);
    if (corps) {
      // Les yeux de l'autre, d'après son corps posé.
      osEn(corps, "tete", V3);
      V3.y += corps.p.hauteTete * 0.45;
      return corps.maillage.localToWorld(V3.clone());
    }
    return new THREE.Vector3(q.x, q.y + q.h * 0.92, q.z);
  }

  /**
   * L'enfant porté dans les bras de sa mère (D1) : au creux du bras (moins de 10 mois environ), à califourchon sur la hanche, ou
   * assis sur ses cuisses quand elle est assise. La mère le tient : ses mains vont sur lui.
   */
  private placerDansLesBras(enfant: Corps, he: number, mere: Corps, hm: number, age: number, postureMere: string): void {
    const mm = mere.maillage;
    mm.updateMatrixWorld();
    const em = enfant.maillage;
    const haut = new THREE.Vector3();
    const avant = new THREE.Vector3();
    const ancre = new THREE.Vector3();
    const point = new THREE.Vector3(); // point de l'enfant (repère des os) posé sur l'ancre
    const qMere = mm.quaternion;
    const assise = postureMere === "assis" || postureMere === "travail" || postureMere === "accroupi";
    const auSein = postureMere === "allaite";
    if (age < 0.8 || auSein) {
      // Tête au pli du coude gauche, fesses sur la main gauche ; tourné vers le haut et vers sa mère (au sein : ventre contre
      // ventre, la bouche à hauteur du sein).
      const coude = osEn(mere, "avantBrasG", new THREE.Vector3());
      const poignet = osEn(mere, "mainG", new THREE.Vector3());
      coude.y += 0.03;
      poignet.y += 0.028;
      const milieu = coude.clone().add(poignet).multiplyScalar(0.5);
      mm.localToWorld(coude);
      mm.localToWorld(poignet);
      mm.localToWorld(milieu);
      haut.copy(coude).sub(poignet).normalize();
      osEn(mere, "poitrine", V1);
      mm.localToWorld(V1);
      avant.set(0, 1, 0).multiplyScalar(auSein ? 0.25 : 0.8).add(V1.sub(milieu).setY(0).normalize().multiplyScalar(auSein ? 1 : 0.35));
      if (auSein) {
        coude.y += 0.04;
        milieu.y += 0.04;
      }
      osEn(enfant, "bassin", V2);
      osEn(enfant, "cou", point);
      point.add(V2).multiplyScalar(0.5);
      ancre.copy(milieu);
      aligner(osEn(enfant, "cou", new THREE.Vector3()).sub(V2), new THREE.Vector3(0, 0, 1), haut, avant, em.quaternion);
    } else if (assise) {
      // Assis sur les cuisses de sa mère, tourné comme elle (un peu de biais).
      const a = osEn(mere, "cuisseG", new THREE.Vector3()).add(osEn(mere, "cuisseD", V1));
      const b = osEn(mere, "jambeG", new THREE.Vector3()).add(osEn(mere, "jambeD", V1));
      ancre.copy(a.multiplyScalar(0.5 * 0.3)).addScaledVector(b, 0.5 * 0.7);
      ancre.y += 0.075;
      mm.localToWorld(ancre);
      haut.set(0, 1, 0);
      avant.set(0.35, 0, 1).applyQuaternion(qMere).setY(0).normalize();
      osEn(enfant, "bassin", point);
      point.y -= 0.075;
      aligner(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 1), haut, avant, em.quaternion);
    } else {
      // À califourchon sur la hanche gauche, tourné vers sa mère.
      // 0.45.10 : assez en dehors pour que son buste ne rentre pas dans le flanc de sa mère et que sa tête reste à côté de la
      // sienne ; presque droit (penché vers elle, il lui rentrait dans le cou).
      ancre.copy(osEn(mere, "cuisseG", new THREE.Vector3()));
      ancre.x += 0.13 + 0.008 * Math.min(age, 5);
      ancre.y += 0.035;
      ancre.z += 0.02;
      mm.localToWorld(ancre);
      haut.set(-0.12, 1, 0).applyQuaternion(qMere).normalize();
      avant.set(-0.9, 0, 0.42).applyQuaternion(qMere).normalize();
      osEn(enfant, "bassin", point);
      point.y -= 0.06;
      aligner(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 1), haut, avant, em.quaternion);
    }
    em.scale.setScalar(he);
    // Position : le point de l'enfant tombe sur l'ancre.
    em.position.set(0, 0, 0);
    em.updateMatrixWorld();
    const p = point.clone().applyMatrix4(em.matrixWorld);
    em.position.copy(ancre).sub(p);
    em.updateMatrixWorld();
    // Les mains de la mère sur l'enfant (repère des os de la mère).
    const versMere = (nom: Parameters<typeof osEn>[1], dx: number, dy: number, dz: number) => {
      const w = osEn(enfant, nom, new THREE.Vector3()).add(new THREE.Vector3(dx, dy, dz));
      return mm.worldToLocal(em.localToWorld(w));
    };
    if (age < 0.8) {
      mainVersPoint(mere, 1, versMere("ventre", 0, 0, 0.09));
    } else if (assise) {
      mainVersPoint(mere, 0, versMere("ventre", 0.07, 0, 0.08));
      mainVersPoint(mere, 1, versMere("ventre", -0.07, 0.02, 0.08));
    } else {
      // 0.45.10 : le bras passe dans son dos (par-dessus, jamais à travers lui), la main sous ses fesses, côté dehors.
      // Le coude va derrière elle, en arrière de l'enfant.
      mainVersPoint(mere, 0, versMere("bassin", -0.08, -0.03, -0.08), 1, osEn(mere, "brasG", new THREE.Vector3()).add(new THREE.Vector3(0.03, -0.12, -0.12)));
    }
    mere.armature.updateMatrixWorld(true);
    void hm;
  }

  /** Expressions qui suivent l'humeur sans à-coups ; bouche qui bouge quand il répond à l'Entité. */
  private expressionsDuMoment(c: Etat, temps: number): Record<Expression, number> {
    const voulu = expressionsDe(c.s.humeur, c.s.peur);
    const e = this.expressions.get(c.s.id) ?? { ...voulu };
    for (const k of Object.keys(voulu) as Expression[]) e[k] += (voulu[k] - e[k]) * 0.04;
    this.expressions.set(c.s.id, e);
    const parle = c.s.id === echange.humain && performance.now() < echange.parleJusqua;
    const bouche = parle ? 0.25 + 0.45 * Math.abs(Math.sin(temps * 9.5)) * (0.6 + 0.4 * Math.sin(temps * 2.3)) : 0;
    return { ...e, bouche };
  }

  /** Visage du corps réaliste : expressions, clignements, regard (vers la caméra quand il écoute l'Entité). */
  private animerVisage(c: Etat, corps: Corps, regard: THREE.Vector3 | null, temps: number): void {
    const dort = (c.posture === "couche" && (c.s.activite === 1 || c.s.activite === 7)) || c.role?.mort === true;
    corps.matriceTete(this.matrice);
    corps.anime!.poser(corps.maillage, corps.os, this.matrice, this.expressionsDuMoment(c, temps), regard, dort, temps);
  }

  /** Construit au besoin (une par image au plus) puis pose la tête détaillée d'un humain proche. */
  private poserTete(c: Etat, corps: Corps, peau: THREE.Color, cheveux: THREE.Color, regard: THREE.Vector3 | null, temps: number, construire: (() => void) | null): void {
    const v = visageConnu(c.s.id);
    let tete = this.tetes.get(c.s.id);
    if (v) {
      const cle = `${cleVisage(v)}|${corps.cle}|${peau.getHexString()}|${cheveux.getHexString()}`;
      if ((!tete || tete.cle !== cle) && construire) {
        construire();
        tete?.liberer();
        const p = corps.p;
        tete = new TeteDetaillee(v, 1 - p.hauteTete, p.hauteTete, 0.004, peau, cheveux, cle);
        this.tetes.set(c.s.id, tete);
        this.groupe.add(tete.groupe);
      }
    }
    if (!tete) {
      corps.teteSimple(true);
      return;
    }
    corps.teteSimple(false);
    const dort = (c.posture === "couche" && (c.s.activite === 1 || c.s.activite === 7)) || c.role?.mort === true;
    corps.matriceTete(this.matrice);
    tete.poser(this.matrice, this.expressionsDuMoment(c, temps), regard, dort, temps);
  }

  /**
   * Yeux et regard d'un humain en corps détaillé (0.41.3, Portrait) : le milieu des yeux et la direction où regarde son visage,
   * d'après l'os de la tête (couché, il regarde le ciel). Faux s'il n'est pas détaillé.
   */
  yeux(id: number, yeux: THREE.Vector3, face: THREE.Vector3): boolean {
    const corps = this.corps.get(id);
    if (!corps) return false;
    corps.matriceTete(this.matrice);
    if (corps.yeuxRepos) yeux.copy(corps.yeuxRepos).applyMatrix4(this.matrice);
    else yeux.set(0, 1 - corps.p.hauteTete * 0.45, corps.p.hauteTete * 0.12).applyMatrix4(this.matrice);
    face.set(0, 0, 1).transformDirection(this.matrice);
    return true;
  }

  get nombre(): number {
    return this.corps.size;
  }

  vider(): void {
    for (const c of this.corps.values()) c.liberer();
    this.corps.clear();
    for (const t of this.tetes.values()) t.liberer();
    this.tetes.clear();
    this.foule.detailles.clear();
  }
}
