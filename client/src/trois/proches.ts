import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import { Corps, cleDe, formeDe } from "./corps";
import type { Corps as Etat, Foule } from "./foule";

/** Réglage des corps détaillés selon la Qualité : jusqu'où, et combien à la fois (proposition G1). */
export interface ReglageProches {
  portee: number;
  nombre: number;
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
  reglage: ReglageProches = { portee: 90, nombre: 50 };

  constructor(private readonly foule: Foule) {}

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
    this.foule.detailles.clear();
    let construits = 0;
    for (const { c: pl, corps: c } of gardes) {
      const forme = formeDe(c.s, c.peau, c.cheveux);
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
      const { descente, couche } = corps.poser(c.posture, c.phase, temps, graine, forme.voute, (s.etat & 8) !== 0, (s.etat & 4) !== 0);
      const m = corps.maillage;
      const h = c.taille;
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
    }
  }

  get nombre(): number {
    return this.corps.size;
  }

  vider(): void {
    for (const c of this.corps.values()) c.liberer();
    this.corps.clear();
    this.foule.detailles.clear();
  }
}
