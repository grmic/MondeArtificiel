import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import { Corps, cleDe, formeDe } from "./corps";
import type { Corps as Etat, Foule } from "./foule";
import { TeteDetaillee, type Expression } from "./tete";
import { chargerVisages, couleurs, visageConnu, type Visage } from "../visage";
import { echange } from "../dialogue";

/** Réglage des corps détaillés selon la Qualité : jusqu'où, et combien à la fois (proposition G1). */
export interface ReglageProches {
  portee: number;
  nombre: number;
  /** Jusqu'où les visages sont détaillés (m), et combien à la fois (0.41.2). */
  visages: number;
  nombreVisages: number;
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

const AXE_Z = new THREE.Vector3(0, 0, 1);
const AXE_X = new THREE.Vector3(1, 0, 0);
const AXE_Y = new THREE.Vector3(0, 1, 0);

/**
 * Les humains proches de la caméra en corps détaillés (0.41.1) ; les autres restent en silhouettes (foule.ts). Un corps n'est
 * construit que si sa forme a changé (âge, grossesse, vêtement…), et pas plus de deux par image pour ne pas saccader.
 */
export class CorpsProches {
  readonly groupe = new THREE.Group();
  private readonly corps = new Map<number, Corps>();
  private readonly materiau = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.72, metalness: 0 });
  reglage: ReglageProches = { portee: 90, nombre: 50, visages: 8, nombreVisages: 8 };
  private readonly tetes = new Map<number, TeteDetaillee>();
  private readonly expressions = new Map<number, Record<Expression, number>>();
  private visagesDemandes = 0;
  private readonly matrice = new THREE.Matrix4();

  constructor(private readonly foule: Foule) {}

  /** Couleurs de peau et de cheveux du portrait, quand le visage est connu (les mêmes qu'à la fiche). */
  private couleursDe(c: Etat): [THREE.Color, THREE.Color] {
    const v = visageConnu(c.s.id);
    if (!v) return [c.peau, c.cheveux];
    const k = couleurs(v);
    const lin = (rgb: [number, number, number]) => new THREE.Color().setRGB(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, THREE.SRGBColorSpace);
    return [lin(k.peau), lin(k.cheveux)];
  }

  mettreAJour(camera: THREE.Vector3, temps: number): void {
    // Les plus proches, dans la portée.
    const candidats: { c: ReturnType<Foule["placement"]> & { id: number }; d: number; corps: Etat }[] = [];
    for (const c of this.foule.liste) {
      if (Number.isNaN(c.x)) continue;
      const pl = this.foule.placement(c);
      const d = Math.hypot(pl.x - camera.x, pl.y - camera.y, pl.z - camera.z);
      if (d < this.reglage.portee) candidats.push({ c: { ...pl, id: c.s.id }, d, corps: c });
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
    for (const [id, tete] of this.tetes) {
      if (!tetesVoulues.has(id) || !this.corps.has(id)) {
        tete.liberer();
        this.tetes.delete(id);
        this.corps.get(id)?.teteSimple(true);
      }
    }
    this.foule.detailles.clear();
    let construits = 0;
    for (const { c: pl, corps: c } of gardes) {
      const [peau, cheveux] = this.couleursDe(c);
      const forme = formeDe(c.s, peau, cheveux);
      let corps = this.corps.get(pl.id);
      if (!corps || corps.cle !== cleDe(forme)) {
        // Pas plus de deux constructions par image : les suivants gardent leur silhouette (ou leur ancien corps) en attendant.
        if (construits >= 2) {
          if (!corps) continue;
        } else {
          construits++;
          corps?.liberer();
          corps = new Corps(forme, this.materiau);
          this.corps.set(pl.id, corps);
          this.groupe.add(corps.maillage);
        }
      }
      this.foule.detailles.add(pl.id);
      const s = c.s;
      const graine = unite(hacher(s.id, 71)) * 100;
      const m = corps.maillage;
      const h = c.taille;
      // Celui qui écoute l'Entité tourne la tête vers elle (la caméra), en plus du corps qui se tourne.
      let regard: { lacet: number; tangage: number; poids: number } | null = null;
      if (c.ecoute > 0.01) {
        const dx = camera.x - pl.x;
        const dz = camera.z - pl.z;
        const lacet = Math.atan2(dx, dz) - pl.cap;
        const yeux = pl.y + h * (corps.hautTete + corps.p.hauteTete * 0.55);
        regard = {
          lacet: Math.atan2(Math.sin(lacet), Math.cos(lacet)),
          tangage: -Math.atan2(camera.y - yeux, Math.hypot(dx, dz)),
          poids: c.ecoute,
        };
      }
      const { descente, couche } = corps.poser(c.posture, c.phase, temps, graine, forme.voute, (s.etat & 8) !== 0, (s.etat & 4) !== 0, regard);
      m.scale.setScalar(h);
      m.quaternion.setFromAxisAngle(AXE_Y, pl.cap);
      let y = pl.y - descente * h;
      if (couche === 1) {
        // Allongé sur le flanc : le corps pivote autour de l'axe avant.
        m.quaternion.multiply(new THREE.Quaternion().setFromAxisAngle(AXE_Z, Math.PI / 2));
        y = pl.y + 0.09 * h;
      } else if (couche === 3) {
        // Nage : à plat ventre, la tête en avant.
        m.quaternion.multiply(new THREE.Quaternion().setFromAxisAngle(AXE_X, Math.PI / 2));
        y = pl.y - 0.05;
      } else if (couche === 2) {
        // Porté contre sa mère : le bassin à hauteur de la hanche qui le porte.
        y = pl.y - corps.hanche * h;
      }
      m.position.set(pl.x, y, pl.z);
      m.updateMatrixWorld();
      corps.armature.updateMatrixWorld(true);
      // Tête détaillée, de près.
      if (tetesVoulues.has(pl.id)) this.poserTete(c, corps, peau, cheveux, camera, temps, tetesFaites < 1 ? () => tetesFaites++ : null);
    }
  }

  /** Construit au besoin (une par image au plus) puis pose la tête détaillée d'un humain proche. */
  private poserTete(c: Etat, corps: Corps, peau: THREE.Color, cheveux: THREE.Color, camera: THREE.Vector3, temps: number, construire: (() => void) | null): void {
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
    // Expressions : elles suivent l'humeur sans à-coups ; la bouche bouge quand il répond à l'Entité.
    const voulu = expressionsDe(c.s.humeur, c.s.peur);
    const e = this.expressions.get(c.s.id) ?? { ...voulu };
    for (const k of Object.keys(voulu) as Expression[]) e[k] += (voulu[k] - e[k]) * 0.04;
    this.expressions.set(c.s.id, e);
    const parle = c.s.id === echange.humain && performance.now() < echange.parleJusqua;
    const bouche = parle ? 0.25 + 0.45 * Math.abs(Math.sin(temps * 9.5)) * (0.6 + 0.4 * Math.sin(temps * 2.3)) : 0;
    const dort = c.posture === "couche" && (c.s.activite === 1 || c.s.activite === 7);
    corps.matriceTete(this.matrice);
    tete.poser(this.matrice, { ...e, bouche }, c.ecoute > 0.3 ? camera : null, dort, temps);
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
