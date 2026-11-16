import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";

import { CHAMPS_BETES, longueur } from "./betes3d";

/** Ce que donne le serveur (CampDto) : huttes par 5, champs par 6, bêtes par 11 nombres (0.44.0). */
export interface CampJson {
  huttes: number[];
  champs: number[];
  betes: number[];
}

interface Hutte {
  id: number;
  x: number;
  z: number;
  y: number;
  solidite: number;
  enduite: boolean;
  cap: number;
}

interface Champ {
  id: number;
  x: number;
  z: number;
  cote: number;
  etat: number;
  croissance: number;
  angle: number;
}

/** Un enclos : là où un troupeau passe ses nuits (les bêtes elles-mêmes sont dans faune.ts depuis la 0.44.0). */
interface Enclos {
  id: number;
  x: number;
  z: number;
  rayon: number;
}

/**
 * Place et rayon (m) de l'enclos d'un troupeau : près du camp de sa case, à l'écart des huttes, assez grand pour ses bêtes
 * (longueur du corps `L`). Le même calcul sert aux bêtes (faune.ts) et aux piquets.
 */
export function enclosDe(rel: Relief, id: number, x: number, y: number, nombre: number, L: number): [number, number, number] {
  const [cx, cz] = rel.camp(x, y);
  const a = unite(hacher(id, 57)) * Math.PI * 2;
  const rayon = 3 + Math.sqrt(Math.max(1, nombre)) * L * 0.9;
  return [cx + Math.cos(a) * (22 + rayon), cz + Math.sin(a) * (22 + rayon), rayon];
}

const MAX_HUTTES = 600;
const MAX_PLANTES = 24000;
const MAX_PIQUETS = 3000;

/**
 * Le camp et les bêtes de la vue 3D (0.41.1) : huttes de branches (enduites d'argile ou non, affaissées quand elles s'abîment),
 * champs semés avec leurs plantes qui lèvent et mûrissent, bêtes gardées dans un enclos près de leur gardien, et bêtes sauvages
 * selon le gibier de chaque case. La simulation ne connaît que la case : la place dans la case est tirée de la graine (toujours
 * la même), et rien de tout cela n'est montré au-delà de quelques kilomètres.
 */
export class Camp3D {
  readonly groupe = new THREE.Group();
  private readonly branches: THREE.InstancedMesh;
  private readonly argile: THREE.InstancedMesh;
  private readonly entrees: THREE.InstancedMesh;
  private readonly plantes: THREE.InstancedMesh;
  private readonly sols: THREE.Mesh;

  /** Profondeur inversée (WebGPU) : le décalage qui garde les sols du camp au-dessus du relief change de signe. */
  inverserDecalage(): void {
    const m = this.sols.material as THREE.Material;
    m.polygonOffsetFactor = -m.polygonOffsetFactor;
    m.needsUpdate = true;
  }
  private readonly piquets: THREE.InstancedMesh;
  private huttes: Hutte[] = [];
  private champs: Champ[] = [];
  private enclos: Enclos[] = [];
  private readonly origine = new THREE.Vector3();
  /** Les plantes des champs ne bougent pas : refaites seulement quand les champs ou la caméra changent beaucoup. */
  private plantesA = new THREE.Vector3(NaN, 0, 0);
  private plantesCle = "";
  portee = 2500;
  /** Centres des camps (coin de la case) et nombre de huttes : le tapis du sol s'y use (0.41.9). */
  private centres: { x: number; z: number; huttes: number }[] = [];

  constructor(private readonly relief: Relief) {
    const dome = new THREE.SphereGeometry(1, 18, 9, 0, Math.PI * 2, 0, Math.PI / 2);
    this.branches = new THREE.InstancedMesh(dome, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 1, flatShading: true }), MAX_HUTTES);
    this.argile = new THREE.InstancedMesh(dome, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.95 }), MAX_HUTTES);
    this.entrees = new THREE.InstancedMesh(new THREE.CircleGeometry(1, 12, 0, Math.PI),
      new THREE.MeshStandardMaterial({ color: 0x15100b, roughness: 1, side: THREE.DoubleSide }), MAX_HUTTES);
    this.plantes = new THREE.InstancedMesh(new THREE.ConeGeometry(0.12, 1, 4).translate(0, 0.5, 0),
      new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.9 }), MAX_PLANTES);
    this.sols = new THREE.Mesh(new THREE.BufferGeometry(), new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1, polygonOffset: true, polygonOffsetFactor: -2 }));
    this.piquets = new THREE.InstancedMesh(new THREE.CylinderGeometry(0.05, 0.06, 1.2, 5).translate(0, 0.6, 0),
      new THREE.MeshStandardMaterial({ color: 0x5c4630, roughness: 1 }), MAX_PIQUETS);
    for (const m of [this.branches, this.argile, this.entrees, this.plantes, this.piquets]) {
      m.count = 0;
      m.frustumCulled = false;
      this.groupe.add(m);
    }
    for (const m of [this.branches, this.argile, this.plantes]) m.setColorAt(0, new THREE.Color());
    this.sols.frustumCulled = false;
    this.groupe.add(this.sols);
  }

  /** Nouvelles données du serveur. */
  recevoir(json: CampJson): void {
    const rel = this.relief;
    // Huttes : en cercle autour du coin du camp de leur case, ouverture tournée vers le feu.
    const parCase = new Map<number, number[][]>();
    for (let k = 0; k + 5 <= json.huttes.length; k += 5) {
      const h = json.huttes.slice(k, k + 5);
      const cle = h[2] * rel.l + h[1];
      parCase.set(cle, [...(parCase.get(cle) ?? []), h]);
    }
    this.huttes = [];
    this.centres = [];
    for (const [cle, liste] of parCase) {
      const [cx, cz] = rel.camp(cle % rel.l, Math.floor(cle / rel.l));
      this.centres.push({ x: cx, z: cz, huttes: liste.length });
      liste.sort((a, b) => a[0] - b[0]);
      const depart = unite(hacher(cle, 31)) * Math.PI * 2;
      const rayon = 7 + liste.length * 0.9;
      liste.forEach(([id, , , solidite, enduite], k) => {
        const a = depart + (k / liste.length) * Math.PI * 2 + (unite(hacher(id, 33)) - 0.5) * 0.3;
        const r = rayon * (0.9 + 0.25 * unite(hacher(id, 35)));
        const x = cx + Math.cos(a) * r;
        const z = cz + Math.sin(a) * r;
        this.huttes.push({ id, x, z, y: rel.hauteur(x, z), solidite: solidite / 1000, enduite: enduite === 1, cap: Math.atan2(cx - x, cz - z) });
      });
    }
    // Champs : des carrés de la surface semée, à quelques dizaines de mètres du camp.
    this.champs = [];
    const rangs = new Map<number, number>();
    for (let k = 0; k + 6 <= json.champs.length; k += 6) {
      const [id, x, y, ares, etat, croissance] = json.champs.slice(k, k + 6);
      const cle = y * rel.l + x;
      const rang = rangs.get(cle) ?? 0;
      rangs.set(cle, rang + 1);
      const cote = Math.max(6, Math.min(180, Math.sqrt(ares * 100)));
      const [cx, cz] = rel.camp(x, y);
      const a = unite(hacher(cle, 41)) * Math.PI * 2 + rang * 1.3;
      const d = 35 + cote * 0.75 + rang * 25;
      this.champs.push({ id, x: cx + Math.cos(a) * d, z: cz + Math.sin(a) * d, cote, etat, croissance: croissance / 1000, angle: unite(hacher(id, 43)) * 0.6 - 0.3 });
    }
    // Enclos des troupeaux gardés (pas pour les chiens, qui suivent leur maître).
    this.enclos = [];
    for (let k = 0; k + CHAMPS_BETES <= json.betes.length; k += CHAMPS_BETES) {
      const [id, x, y, , forme, poids, adultes, petits, gardees] = json.betes.slice(k, k + CHAMPS_BETES);
      if (gardees !== 1 || forme === 4 || adultes + petits < 2) continue;
      const [ex, ez, rayon] = enclosDe(rel, id, x, y, Math.min(30, adultes) + Math.min(20, petits), longueur(forme, poids));
      this.enclos.push({ id, x: ex, z: ez, rayon });
    }
    this.plantesCle = "";
  }

  /**
   * Lieux où l'on passe sans cesse (0.41.9) : huttes (rayon, entrée tournée vers le centre), champs, enclos et centres des camps.
   * `cle` ne change que si l'un d'eux apparaît, disparaît ou bouge.
   */
  lieux(): { huttes: { x: number; z: number; r: number; cap: number }[]; champs: { x: number; z: number; cote: number; angle: number }[];
    enclos: { x: number; z: number; r: number }[]; centres: { x: number; z: number; huttes: number }[]; cle: string } {
    const huttes = this.huttes.map((h) => ({ x: h.x, z: h.z, r: 1.9 + 0.5 * unite(hacher(h.id, 37)), cap: h.cap }));
    const champs = this.champs.map((c) => ({ x: c.x, z: c.z, cote: c.cote, angle: c.angle }));
    const enclos = this.enclos.map((t) => ({ x: t.x, z: t.z, r: t.rayon * 1.05 }));
    const arrondi = (v: number) => Math.round(v);
    const cle = [
      ...huttes.map((h) => `h${arrondi(h.x)},${arrondi(h.z)}`),
      ...champs.map((c) => `c${arrondi(c.x)},${arrondi(c.z)},${arrondi(c.cote)}`),
      ...enclos.map((e) => `e${arrondi(e.x)},${arrondi(e.z)},${arrondi(e.r)}`),
    ].join(";");
    return { huttes, champs, enclos, centres: this.centres, cle };
  }

  /** Chaque image : place huttes, bêtes et enclos autour de la caméra. */
  animer(camera: THREE.Vector3): void {
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    const o = this.origine;
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const p = new THREE.Vector3();
    const e = new THREE.Vector3();
    const c = new THREE.Color();
    const haut = new THREE.Vector3(0, 1, 0);
    // Huttes.
    let nb = 0;
    let na = 0;
    let ne = 0;
    for (const h of this.huttes) {
      if (Math.hypot(h.x - camera.x, h.z - camera.z) > this.portee) continue;
      const r = 1.9 + 0.5 * unite(hacher(h.id, 37));
      const affaisse = 0.55 + 0.45 * Math.min(1, h.solidite * 1.3);
      q.setFromAxisAngle(haut, h.cap);
      if (h.solidite < 0.4) q.multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), (0.4 - h.solidite) * 0.5));
      m.compose(p.set(h.x - o.x, h.y - 0.1, h.z - o.z), q, e.set(r, r * 0.9 * affaisse, r));
      if (h.enduite) {
        this.argile.setMatrixAt(na, m);
        this.argile.setColorAt(na++, c.setRGB(0.6, 0.47, 0.33).multiplyScalar(0.8 + 0.25 * h.solidite));
      } else {
        this.branches.setMatrixAt(nb, m);
        this.branches.setColorAt(nb++, c.setRGB(0.36, 0.3, 0.2).lerp(new THREE.Color(0.3, 0.34, 0.2), unite(hacher(h.id, 39))).multiplyScalar(0.7 + 0.4 * h.solidite));
      }
      // L'entrée, face au feu.
      const avant = new THREE.Vector3(Math.sin(h.cap), 0, Math.cos(h.cap));
      m.compose(p.set(h.x - o.x + avant.x * r * 0.995, h.y - 0.1, h.z - o.z + avant.z * r * 0.995), q.setFromAxisAngle(haut, h.cap), e.set(0.55, 0.95 * affaisse, 1));
      this.entrees.setMatrixAt(ne++, m);
    }
    this.branches.count = nb;
    this.argile.count = na;
    this.entrees.count = ne;
    // Enclos : un cercle de piquets.
    let npi = 0;
    for (const t of this.enclos) {
      if (Math.hypot(t.x - camera.x, t.z - camera.z) > Math.min(this.portee, 1800)) continue;
      const n = Math.min(80, Math.ceil((t.rayon * 2 * Math.PI) / 1.6));
      for (let k = 0; k < n && npi < MAX_PIQUETS; k++) {
        const a = (k / n) * Math.PI * 2;
        const x = t.x + Math.cos(a) * t.rayon * 1.05;
        const z = t.z + Math.sin(a) * t.rayon * 1.05;
        m.compose(p.set(x - o.x, this.relief.hauteur(x, z), z - o.z), q.setFromAxisAngle(haut, a), e.set(1, 0.9 + 0.2 * unite(hacher(t.id, k)), 1));
        this.piquets.setMatrixAt(npi++, m);
      }
    }
    this.piquets.count = npi;
    for (const x of [this.branches, this.argile, this.entrees, this.piquets]) {
      x.position.copy(o);
      x.instanceMatrix.needsUpdate = true;
      if (x.instanceColor) x.instanceColor.needsUpdate = true;
    }
    this.poserChamps(camera);
  }

  /** Sols et plantes des champs : refaits quand les champs changent ou que la caméra s'est éloignée de plus de 300 m. */
  private poserChamps(camera: THREE.Vector3): void {
    const cle = this.champs.map((c) => `${c.id}:${c.etat}:${Math.round(c.croissance * 20)}`).join(",");
    if (cle === this.plantesCle && Math.hypot(camera.x - this.plantesA.x, camera.z - this.plantesA.z) < 300) return;
    this.plantesCle = cle;
    this.plantesA.copy(camera);
    const o = new THREE.Vector3(Math.round(camera.x), 0, Math.round(camera.z));
    const positions: number[] = [];
    const couleurs: number[] = [];
    const indices: number[] = [];
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const p = new THREE.Vector3();
    const e = new THREE.Vector3();
    const c = new THREE.Color();
    let n = 0;
    const proches = this.champs.filter((ch) => Math.hypot(ch.x - camera.x, ch.z - camera.z) < Math.min(this.portee, 2000))
      .sort((a, b) => Math.hypot(a.x - camera.x, a.z - camera.z) - Math.hypot(b.x - camera.x, b.z - camera.z));
    for (const ch of proches) {
      // Sol retourné : une grille posée sur le relief.
      const cotes = 10;
      const debut = positions.length / 3;
      const cos = Math.cos(ch.angle);
      const sin = Math.sin(ch.angle);
      const terre = ch.etat === 3 ? new THREE.Color(0.42, 0.36, 0.22) : new THREE.Color(0.33, 0.24, 0.16);
      for (let j = 0; j <= cotes; j++) {
        for (let i = 0; i <= cotes; i++) {
          const u = (i / cotes - 0.5) * ch.cote;
          const v = (j / cotes - 0.5) * ch.cote;
          const x = ch.x + u * cos - v * sin;
          const z = ch.z + u * sin + v * cos;
          positions.push(x - o.x, this.relief.hauteur(x, z) + 0.06, z - o.z);
          const k = 0.9 + 0.15 * Math.sin(i * 1.7 + j * 0.3);
          couleurs.push(terre.r * k, terre.g * k, terre.b * k);
        }
      }
      for (let j = 0; j < cotes; j++) {
        for (let i = 0; i < cotes; i++) {
          const a = debut + j * (cotes + 1) + i;
          indices.push(a, a + cotes + 1, a + 1, a + 1, a + cotes + 1, a + cotes + 2);
        }
      }
      if (ch.etat === 0) continue;
      // Plantes en rangs ; au plus 1 500 par champ (rangs plus espacés dans les grands champs).
      const pas = Math.max(1.1, ch.cote / Math.sqrt(1500));
      const hauteur = ch.etat === 2 ? 1.0 : ch.etat === 3 ? 0.15 : 0.08 + 0.85 * ch.croissance;
      const vert = new THREE.Color(0.25, 0.48, 0.16);
      const mur = new THREE.Color(0.78, 0.65, 0.3);
      const teinte = ch.etat === 2 ? mur : ch.etat === 3 ? new THREE.Color(0.55, 0.45, 0.28) : vert.clone().lerp(mur, Math.max(0, ch.croissance - 0.6) * 1.5);
      for (let v = -ch.cote / 2 + pas / 2; v < ch.cote / 2; v += pas) {
        for (let u = -ch.cote / 2 + pas / 2; u < ch.cote / 2; u += pas * 0.6) {
          if (n >= MAX_PLANTES) break;
          const g = hacher(ch.id, Math.round(u * 10), Math.round(v * 10));
          const x = ch.x + u * cos - v * sin + (unite(g) - 0.5) * 0.3;
          const z = ch.z + u * sin + v * cos + (unite(hacher(g, 1)) - 0.5) * 0.3;
          const hh = hauteur * (0.75 + 0.5 * unite(hacher(g, 2)));
          m.compose(p.set(x - o.x, this.relief.hauteur(x, z), z - o.z), q.setFromAxisAngle(new THREE.Vector3(0, 1, 0), unite(g) * 6.3), e.set(1 + hh, hh, 1 + hh));
          this.plantes.setMatrixAt(n, m);
          this.plantes.setColorAt(n++, c.copy(teinte).multiplyScalar(0.85 + 0.3 * unite(hacher(g, 3))));
        }
      }
    }
    this.plantes.count = n;
    this.plantes.position.copy(o);
    this.plantes.instanceMatrix.needsUpdate = true;
    if (this.plantes.instanceColor) this.plantes.instanceColor.needsUpdate = true;
    const geo = new THREE.BufferGeometry();
    geo.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
    geo.setAttribute("color", new THREE.Float32BufferAttribute(couleurs, 3));
    geo.setIndex(indices);
    geo.computeVertexNormals();
    this.sols.geometry.dispose();
    this.sols.geometry = geo;
    this.sols.position.copy(o);
  }
}
