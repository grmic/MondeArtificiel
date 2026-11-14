import * as THREE from "three/webgpu";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { hacher, unite } from "./bruit";
import * as B from "./relief";
import type { Relief } from "./relief";

/**
 * Le sol tout près de soi (0.41.2) : plantes basses, mousses, fleurs, cailloux et feuilles mortes, posés de façon déterministe
 * (les mêmes au même endroit) selon le biome, l'abondance des plantes, la saison et le gel. Flore du Crétacé : prêles et petites
 * fougères plutôt que des prairies (les graminées étaient encore rares), premières plantes à fleurs.
 */
const enum Sorte {
  Prele,
  Fougere,
  Mousse,
  Fleur,
  Caillou,
  Litiere,
}
const SORTES = 6;

/** Densité par case de 1,5 m, selon le biome. */
const DENSITES: Partial<Record<number, [Sorte, number][]>> = {
  [B.PLAINE]: [[Sorte.Prele, 0.35], [Sorte.Fougere, 0.25], [Sorte.Fleur, 0.06], [Sorte.Mousse, 0.08], [Sorte.Caillou, 0.04]],
  [B.CONIFERES]: [[Sorte.Litiere, 0.45], [Sorte.Fougere, 0.25], [Sorte.Mousse, 0.18], [Sorte.Caillou, 0.05]],
  [B.FEUILLUS]: [[Sorte.Litiere, 0.5], [Sorte.Fougere, 0.22], [Sorte.Mousse, 0.12], [Sorte.Fleur, 0.03], [Sorte.Caillou, 0.04]],
  [B.TROPICALE]: [[Sorte.Fougere, 0.45], [Sorte.Litiere, 0.25], [Sorte.Mousse, 0.15], [Sorte.Fleur, 0.04]],
  [B.DESERT]: [[Sorte.Caillou, 0.28], [Sorte.Prele, 0.02]],
  [B.MONTAGNE]: [[Sorte.Caillou, 0.35], [Sorte.Mousse, 0.15], [Sorte.Prele, 0.12], [Sorte.Fleur, 0.03]],
  [B.SOMMET]: [[Sorte.Caillou, 0.4], [Sorte.Mousse, 0.05]],
  [B.MARAIS]: [[Sorte.Prele, 0.7], [Sorte.Mousse, 0.15]],
  [B.MANGROVE]: [[Sorte.Prele, 0.3], [Sorte.Litiere, 0.2]],
  [B.LANDE]: [[Sorte.Mousse, 0.25], [Sorte.Fleur, 0.12], [Sorte.Prele, 0.15], [Sorte.Caillou, 0.1]],
  [B.PLAGE]: [[Sorte.Caillou, 0.05]],
};

/** Taille (m) : minimum et étendue. */
const TAILLES: [number, number][] = [[0.25, 0.5], [0.25, 0.4], [0.25, 0.45], [0.12, 0.15], [0.05, 0.3], [0.8, 0.8]];

function colorer(g: THREE.BufferGeometry, couleur: (y: number, k: number) => [number, number, number]): THREE.BufferGeometry {
  const p = g.getAttribute("position");
  const c = new Float32Array(p.count * 3);
  const col = new THREE.Color();
  for (let i = 0; i < p.count; i++) {
    const [r, v, b] = couleur(p.getY(i), i);
    col.setRGB(r / 255, v / 255, b / 255, THREE.SRGBColorSpace);
    c.set([col.r, col.g, col.b], i * 3);
  }
  g.setAttribute("color", new THREE.BufferAttribute(c, 3));
  return g.index ? g.toNonIndexed() : g;
}

/** Penche les normales vers le ciel : les plantes basses s'éclairent comme le sol qui les porte (sinon leurs flancs paraissent noirs). */
function versLeCiel(g: THREE.BufferGeometry, k: number): THREE.BufferGeometry {
  const n = g.getAttribute("normal");
  const v = new THREE.Vector3();
  for (let i = 0; i < n.count; i++) {
    v.set(n.getX(i), n.getY(i), n.getZ(i)).multiplyScalar(1 - k);
    v.y += k;
    v.normalize();
    n.setXYZ(i, v.x, v.y, v.z);
  }
  return g;
}

/** Modèles hauts de 1 (ou larges de 1 pour ce qui est posé à plat). */
function modeles(): THREE.BufferGeometry[] {
  // Prêles : tiges creuses à nœuds sombres, un peu penchées.
  const tiges: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 7; k++) {
    const a = (k / 7) * Math.PI * 2 + k;
    const h = 0.6 + 0.4 * ((k * 37) % 10) / 10;
    const t = new THREE.CylinderGeometry(0.008, 0.014, h, 5, 6).translate(0, h / 2, 0).rotateZ(Math.sin(a) * 0.18).rotateX(Math.cos(a) * 0.18)
      .translate(Math.cos(a) * 0.06, 0, Math.sin(a) * 0.06);
    tiges.push(colorer(t, (y) => (Math.abs((y * 9) % 1) < 0.12 ? [40, 52, 30] : [92 + 40 * y, 128 + 30 * y, 58])));
  }
  const prele = mergeGeometries(tiges)!;
  // Petites fougères : frondes arquées, plus claires au bout.
  const frondes: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 6; k++) {
    const f = new THREE.PlaneGeometry(0.16, 0.7, 1, 6);
    const p = f.getAttribute("position");
    for (let i = 0; i < p.count; i++) {
      const v = p.getY(i) / 0.7 + 0.5;
      // La fronde s'effile et se courbe vers le sol.
      p.setX(i, p.getX(i) * Math.sin(Math.PI * Math.min(1, v * 1.1)) + 0);
      p.setZ(i, v * 0.55);
      p.setY(i, Math.sin(v * Math.PI * 0.85) * 0.55);
    }
    f.computeVertexNormals();
    f.rotateY((k / 6) * Math.PI * 2 + k * 0.4);
    frondes.push(colorer(versLeCiel(f, 0.5), (y) => [62 + 60 * y, 104 + 50 * y, 44 + 10 * y]));
  }
  const fougere = mergeGeometries(frondes)!;
  // Coussin de mousse : un dôme bas (sans dessous), éclairé comme le sol.
  const mousse = colorer(versLeCiel(new THREE.SphereGeometry(0.5, 12, 4, 0, Math.PI * 2, 0, Math.PI / 2).scale(1, 0.3, 1), 0.6), (y) => [62 + 160 * y, 92 + 160 * y, 40]);
  // Fleurs : un bouquet de corolles sur de courtes tiges (la couleur vient de chaque fleur).
  const fleurs: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 5; k++) {
    const a = (k / 5) * Math.PI * 2;
    const x = Math.cos(a) * 0.25;
    const z = Math.sin(a) * 0.25;
    const h = 0.6 + 0.4 * (k % 2);
    fleurs.push(colorer(new THREE.CylinderGeometry(0.012, 0.012, h, 3).translate(x, h / 2, z), () => [70, 100, 50]));
    fleurs.push(colorer(new THREE.IcosahedronGeometry(0.11, 0).scale(1, 0.45, 1).translate(x, h, z), () => [255, 255, 255]));
  }
  const fleur = mergeGeometries(fleurs)!;
  const caillou = colorer(new THREE.DodecahedronGeometry(0.5, 0).scale(1.2, 0.55, 0.9).translate(0, 0.1, 0), () => [150, 144, 134]);
  // Feuilles mortes : quelques feuilles à plat, un peu bombées, de teintes variées.
  const feuilles: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 12; k++) {
    const a = k * 2.4;
    const r = 0.06 + 0.3 * ((k * 7) % 5) / 5;
    const f = new THREE.CircleGeometry(0.07, 6).scale(0.55, 1, 1).rotateX(-Math.PI / 2).rotateY(a * 1.7).translate(Math.cos(a) * r, 0.002 * k, Math.sin(a) * r);
    const teinte = [[132, 92, 52], [150, 116, 60], [104, 72, 44], [160, 98, 48]][k % 4];
    feuilles.push(colorer(f, () => teinte as [number, number, number]));
  }
  const litiere = mergeGeometries(feuilles)!;
  return [prele, fougere, mousse, fleur, caillou, litiere];
}

const COULEURS_FLEURS: [number, number, number][] = [[1, 1, 0.95], [1, 0.85, 0.25], [0.95, 0.55, 0.7], [0.7, 0.45, 0.9], [0.95, 0.4, 0.25]];

interface Pose {
  x: number;
  y: number;
  z: number;
  echelle: number;
  angle: number;
  penche: number;
  r: number;
  g: number;
  b: number;
}

const COTE_BLOC = 24;
const PAS = 1.5;

export class Sol3D {
  readonly groupe = new THREE.Group();
  private readonly maillages: THREE.InstancedMesh[] = [];
  private readonly blocs = new Map<number, Pose[][]>();
  private readonly versions = new Map<number, number>();
  private version = 0;
  private origine = new THREE.Vector3();
  private derniere = new THREE.Vector3(Infinity, 0, Infinity);
  private aRefaire = true;
  /** Portée en mètres (selon la qualité) ; 0 : rien. */
  portee = 55;

  constructor(private readonly relief: Relief) {
    // Lambert : moins coûteux, et les normales de sommets avec MeshStandardMaterial donnaient des taches noires au loin (SwiftShader).
    const plantes = new THREE.MeshLambertMaterial({ vertexColors: true, side: THREE.DoubleSide });
    const tapis = new THREE.MeshLambertMaterial({ vertexColors: true });
    const pierres = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.95, metalness: 0, flatShading: true });
    modeles().forEach((g, k) => {
      const m = new THREE.InstancedMesh(g, k === Sorte.Caillou ? pierres : k === Sorte.Mousse || k === Sorte.Litiere ? tapis : plantes, 24000);
      m.count = 0;
      m.frustumCulled = false;
      m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      m.setColorAt(0, new THREE.Color(1, 1, 1));
      this.maillages.push(m);
      this.groupe.add(m);
    });
  }

  /** La saison a changé (ou le gel) : les blocs seront refaits peu à peu. */
  invalider(): void {
    this.version++;
  }

  vider(): void {
    this.blocs.clear();
    this.versions.clear();
    this.aRefaire = true;
  }

  private cle(bx: number, bz: number): number {
    return (bz + 40000) * 80000 + bx + 40000;
  }

  mettreAJour(camera: THREE.Vector3, budgetMs: number): void {
    const r = this.portee;
    // En vol haut, le sol proche ne se voit pas : on ne le fait pas.
    const haut = camera.y - this.relief.hauteur(camera.x, camera.z);
    if (r <= 0 || haut > r * 0.8) {
      if (this.maillages[0].count > 0 || this.aRefaire) {
        for (const m of this.maillages) m.count = 0;
        this.aRefaire = true;
      }
      return;
    }
    const debut = performance.now();
    const voulus: [number, number, number][] = [];
    for (let bz = Math.floor((camera.z - r) / COTE_BLOC); bz <= Math.floor((camera.z + r) / COTE_BLOC); bz++) {
      for (let bx = Math.floor((camera.x - r) / COTE_BLOC); bx <= Math.floor((camera.x + r) / COTE_BLOC); bx++) {
        const d = Math.hypot((bx + 0.5) * COTE_BLOC - camera.x, (bz + 0.5) * COTE_BLOC - camera.z);
        if (d < r + COTE_BLOC) voulus.push([bx, bz, d]);
      }
    }
    voulus.sort((a, b) => a[2] - b[2]);
    let change = false;
    const garde = new Set<number>();
    for (const [bx, bz] of voulus) {
      const cle = this.cle(bx, bz);
      garde.add(cle);
      if (this.blocs.has(cle) && this.versions.get(cle) === this.version) continue;
      if (performance.now() - debut > budgetMs) continue;
      this.blocs.set(cle, this.poser(bx, bz));
      this.versions.set(cle, this.version);
      change = true;
    }
    for (const cle of [...this.blocs.keys()]) {
      if (!garde.has(cle)) {
        this.blocs.delete(cle);
        this.versions.delete(cle);
        change = true;
      }
    }
    if (change || this.aRefaire || camera.distanceTo(this.derniere) > 4) this.assembler(camera);
  }

  private poser(bx: number, bz: number): Pose[][] {
    const rel = this.relief;
    const parSorte: Pose[][] = Array.from({ length: SORTES }, () => []);
    const n = COTE_BLOC / PAS;
    const saison = rel.saison;
    for (let j = 0; j < n; j++) {
      for (let i = 0; i < n; i++) {
        const gx = bx * n + i;
        const gz = bz * n + j;
        const x = (gx + unite(hacher(gx, gz, 201))) * PAS;
        const z = (gz + unite(hacher(gx, gz, 203))) * PAS;
        const biome = rel.biomeEn(x, z, unite(hacher(gx, gz, 207)));
        const table = DENSITES[biome];
        if (!table) continue;
        const abondance = 0.35 + 0.9 * rel.plantesEn(x, z);
        const temperature = rel.temperatureEn(x, z) ?? 12;
        const gel = temperature < 0;
        let tirage = unite(hacher(gx, gz, 209));
        let sorte: Sorte | -1 = -1;
        for (const [s, d] of table) {
          const vivant = s !== Sorte.Caillou && s !== Sorte.Litiere;
          // Fleurs au printemps et en été ; rien de vert sous la neige.
          if ((vivant && gel) || (s === Sorte.Fleur && saison !== 0 && saison !== 1)) {
            tirage -= vivant ? d * abondance : d;
            continue;
          }
          const dd = vivant ? d * abondance : d;
          if (tirage < dd) {
            sorte = s;
            break;
          }
          tirage -= dd;
        }
        if (sorte < 0) continue;
        const s = rel.surface(x, z);
        if (!Number.isNaN(s.eau) && !(sorte === Sorte.Prele && biome === B.MARAIS && s.eau - s.sol < 0.4)) continue;
        if (s.sol < 0.3 && sorte !== Sorte.Prele && sorte !== Sorte.Caillou) continue;
        const [tmin, etendue] = TAILLES[sorte];
        const v = unite(hacher(gx, gz, 211));
        const pose: Pose = {
          x, y: s.sol + (sorte === Sorte.Caillou ? -0.03 : sorte === Sorte.Litiere ? 0.02 : -0.01), z, echelle: tmin + etendue * v * v, angle: unite(hacher(gx, gz, 213)) * Math.PI * 2,
          penche: (unite(hacher(gx, gz, 217)) - 0.5) * 0.25, r: 1, g: 1, b: 1,
        };
        const teinte = 0.8 + 0.4 * unite(hacher(gx, gz, 219));
        pose.r = pose.g = pose.b = teinte;
        if (sorte === Sorte.Fleur) {
          const c = COULEURS_FLEURS[Math.floor(unite(hacher(gx, gz, 223)) * (biome === B.LANDE ? 2 : COULEURS_FLEURS.length)) + (biome === B.LANDE ? 2 : 0)];
          [pose.r, pose.g, pose.b] = c;
        } else if (sorte === Sorte.Caillou) {
          pose.r *= 0.95 + 0.1 * unite(hacher(gx, gz, 227));
          pose.b *= 0.9;
          if (biome === B.DESERT || biome === B.PLAGE) {
            pose.r *= 1.15;
            pose.g *= 1.02;
            pose.b *= 0.8;
          }
        } else if (sorte !== Sorte.Litiere) {
          // Saisons : plus sec en été, roussi à l'automne, terne l'hiver.
          if (saison === 1 && temperature > 24) {
            pose.r *= 1.2;
            pose.b *= 0.85;
          } else if (saison === 2) {
            pose.r *= 1.3;
            pose.g *= 0.95;
            pose.b *= 0.7;
          } else if (saison === 3) {
            pose.r *= 1.1;
            pose.g *= 0.85;
            pose.b *= 0.8;
          }
        } else if (saison === 2) {
          pose.r *= 1.2;
          pose.g *= 0.95;
        }
        parSorte[sorte].push(pose);
      }
    }
    return parSorte;
  }

  private assembler(camera: THREE.Vector3): void {
    this.aRefaire = false;
    this.derniere.copy(camera);
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const e = new THREE.Euler();
    const pos = new THREE.Vector3();
    const ech = new THREE.Vector3();
    const col = new THREE.Color();
    const comptes = new Array(SORTES).fill(0);
    const r2 = this.portee * this.portee;
    for (const parSorte of this.blocs.values()) {
      for (let s = 0; s < SORTES; s++) {
        const maillage = this.maillages[s];
        // Les plus petites choses ne se voient que de plus près.
        const limite = s === Sorte.Caillou || s === Sorte.Fleur || s === Sorte.Litiere ? r2 * 0.5 : r2;
        for (const p of parSorte[s]) {
          if (comptes[s] >= maillage.instanceMatrix.count) break;
          const dx = p.x - camera.x;
          const dz = p.z - camera.z;
          if (dx * dx + dz * dz > limite) continue;
          const penche = s === Sorte.Litiere || s === Sorte.Mousse ? p.penche * 0.2 : p.penche;
          q.setFromEuler(e.set(penche, p.angle, penche * 0.5));
          pos.set(p.x - this.origine.x, p.y, p.z - this.origine.z);
          const plat = s === Sorte.Litiere ? 1 : s === Sorte.Mousse ? 1.6 : 1;
          ech.set(p.echelle * plat, p.echelle, p.echelle * plat);
          m.compose(pos, q, ech);
          maillage.setMatrixAt(comptes[s], m);
          maillage.setColorAt(comptes[s], col.setRGB(p.r, p.g, p.b));
          comptes[s]++;
        }
      }
    }
    this.maillages.forEach((maillage, s) => {
      maillage.count = comptes[s];
      maillage.position.copy(this.origine);
      maillage.instanceMatrix.needsUpdate = true;
      if (maillage.instanceColor) maillage.instanceColor.needsUpdate = true;
    });
  }
}
