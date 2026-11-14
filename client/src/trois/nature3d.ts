import * as THREE from "three/webgpu";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { hacher, unite } from "./bruit";
import * as B from "./relief";
import type { Relief } from "./relief";
import type { Matieres } from "./matieres";
import * as Vg from "./vegetaux";

/** Sortes de plantes et de pierres posées sur le sol. */
const enum Sorte {
  Conifere,
  Feuillu,
  Tropical,
  Fougere,
  Buisson,
  Rocher,
  Roseau,
  FougereArbo,
}
const SORTES = 8;

/** Jusqu'où chaque sorte est dessinée, en part de la portée de la végétation. */
const PORTEE: number[] = [1, 1, 1, 0.3, 0.55, 0.6, 0.35, 1];

/** Souplesse au vent de chaque sorte (0.41.5). */
const SOUPLESSE: number[] = [0.45, 0.8, 0.5, 1.6, 1.0, 0, 2.0, 0.9];

/** Densité de chaque sorte par case de 10 m, selon le biome. */
const DENSITES: Partial<Record<number, [Sorte, number][]>> = {
  [B.PLAINE]: [[Sorte.Fougere, 0.55], [Sorte.Buisson, 0.08], [Sorte.Feuillu, 0.015], [Sorte.Conifere, 0.01]],
  [B.CONIFERES]: [[Sorte.Conifere, 0.32], [Sorte.Fougere, 0.3], [Sorte.Buisson, 0.05]],
  [B.TROPICALE]: [[Sorte.Tropical, 0.12], [Sorte.FougereArbo, 0.13], [Sorte.Feuillu, 0.12], [Sorte.Fougere, 0.4]],
  [B.FEUILLUS]: [[Sorte.Feuillu, 0.28], [Sorte.FougereArbo, 0.02], [Sorte.Fougere, 0.3], [Sorte.Buisson, 0.08]],
  [B.DESERT]: [[Sorte.Rocher, 0.03], [Sorte.Buisson, 0.02], [Sorte.Tropical, 0.005]],
  [B.MONTAGNE]: [[Sorte.Conifere, 0.08], [Sorte.Rocher, 0.1], [Sorte.Fougere, 0.1]],
  [B.SOMMET]: [[Sorte.Rocher, 0.08]],
  [B.MARAIS]: [[Sorte.Roseau, 0.5], [Sorte.Feuillu, 0.03], [Sorte.FougereArbo, 0.03], [Sorte.Fougere, 0.2]],
  [B.MANGROVE]: [[Sorte.Feuillu, 0.25], [Sorte.Roseau, 0.2]],
  [B.LANDE]: [[Sorte.Buisson, 0.15], [Sorte.Fougere, 0.15], [Sorte.Rocher, 0.03], [Sorte.Tropical, 0.01]],
  [B.PLAGE]: [[Sorte.Tropical, 0.01], [Sorte.Rocher, 0.01]],
};

function colorer(g: THREE.BufferGeometry, rgb: [number, number, number]): THREE.BufferGeometry {
  const n = g.getAttribute("position").count;
  const c = new Float32Array(n * 3);
  const col = new THREE.Color().setRGB(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, THREE.SRGBColorSpace);
  for (let i = 0; i < n; i++) c.set([col.r, col.g, col.b], i * 3);
  g.setAttribute("color", new THREE.BufferAttribute(c, 3));
  return g.index ? g.toNonIndexed() : g;
}

/** Modèles simples, hauts de 1 (mis à l'échelle à la pose). Plantes du Crétacé : araucarias, magnolias, fougères arborescentes, prêles. */
function modeles(): THREE.BufferGeometry[] {
  const tronc = (h: number, r: number, couleur: [number, number, number] = [92, 70, 50]) =>
    colorer(new THREE.CylinderGeometry(r * 0.7, r, h, 6).translate(0, h / 2, 0), couleur);
  const conifere = mergeGeometries([
    tronc(0.55, 0.025),
    colorer(new THREE.ConeGeometry(0.16, 0.3, 7).translate(0, 0.5, 0), [44, 72, 44]),
    colorer(new THREE.ConeGeometry(0.13, 0.25, 7).translate(0, 0.68, 0), [48, 78, 46]),
    colorer(new THREE.ConeGeometry(0.09, 0.22, 7).translate(0, 0.84, 0), [52, 84, 48]),
    colorer(new THREE.ConeGeometry(0.05, 0.16, 6).translate(0, 0.97, 0), [56, 88, 50]),
  ])!;
  const feuillu = mergeGeometries([
    tronc(0.5, 0.04),
    colorer(new THREE.IcosahedronGeometry(0.28, 1).scale(1, 0.75, 1).translate(0, 0.66, 0), [70, 112, 50]),
    colorer(new THREE.IcosahedronGeometry(0.2, 1).translate(0.16, 0.8, 0.06), [78, 120, 54]),
    colorer(new THREE.IcosahedronGeometry(0.18, 1).translate(-0.14, 0.78, -0.08), [64, 104, 46]),
  ])!;
  const palmes: THREE.BufferGeometry[] = [tronc(0.8, 0.05, [104, 84, 60])];
  for (let k = 0; k < 9; k++) {
    const p = new THREE.BoxGeometry(0.42, 0.012, 0.08).translate(0.21, 0, 0).rotateZ(-0.35 - (k % 2) * 0.25).rotateY((k / 9) * Math.PI * 2).translate(0, 0.8, 0);
    palmes.push(colorer(p, [60, 104, 44]));
  }
  const tropical = mergeGeometries(palmes)!;
  const frondes: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 7; k++) {
    const f = new THREE.BoxGeometry(0.6, 0.02, 0.16).translate(0.3, 0, 0).rotateZ(0.7 + (k % 3) * 0.15).rotateY((k / 7) * Math.PI * 2 + k);
    frondes.push(colorer(f, k % 2 ? [96, 146, 62] : [84, 134, 54]));
  }
  const fougere = mergeGeometries(frondes)!;
  const buisson = mergeGeometries([
    colorer(new THREE.IcosahedronGeometry(0.5, 0).scale(1, 0.7, 1).translate(0, 0.38, 0), [74, 96, 52]),
    colorer(new THREE.IcosahedronGeometry(0.35, 0).translate(0.3, 0.3, 0.1), [82, 104, 56]),
  ])!;
  const rocher = colorer(new THREE.DodecahedronGeometry(0.5, 0).scale(1.2, 0.7, 1).translate(0, 0.2, 0), [128, 122, 112]);
  const tiges: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 10; k++) {
    const a = (k / 10) * Math.PI * 2;
    tiges.push(colorer(new THREE.CylinderGeometry(0.012, 0.02, 1, 4).translate(0, 0.5, 0).rotateZ(Math.sin(a * 3) * 0.15).translate(Math.cos(a) * 0.15, 0, Math.sin(a) * 0.15), [96, 124, 64]));
  }
  const roseau = mergeGeometries(tiges)!;
  return [conifere, feuillu, tropical, fougere, buisson, rocher, roseau];
}

/** Hauteurs (m) de chaque sorte : minimum et étendue. */
const TAILLES: [number, number][] = [[16, 16], [9, 9], [1.6, 3], [0.6, 0.9], [0.9, 1.4], [0.6, 2.4], [1, 1.2], [3.5, 5]];

interface Pose {
  x: number;
  y: number;
  z: number;
  echelle: number;
  angle: number;
  r: number;
  g: number;
  b: number;
}

const COTE_BLOC = 80;
const PAS = 10;

/**
 * La végétation autour de la caméra, posée de façon déterministe (même arbre au même endroit à chaque visite) selon le biome, l'abondance
 * des plantes de la case, la pente, l'eau et la saison. Construite par blocs de 80 m, quelques-uns par image.
 */
export class Nature3D {
  readonly groupe = new THREE.Group();
  private readonly maillages: THREE.InstancedMesh[] = [];
  private readonly blocs = new Map<number, Pose[][]>();
  private origine = new THREE.Vector3();
  private aRefaire = true;
  private derniere = new THREE.Vector3(Infinity, 0, Infinity);
  private version = 0;
  private readonly versions = new Map<number, number>();
  /** Portée en mètres (selon la qualité). */
  portee = 900;
  private readonly texture: boolean;

  constructor(private readonly relief: Relief, matieres: Matieres | null = null) {
    const materiau = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.9, metalness: 0, flatShading: true });
    // 0.41.5 : plantes texturées qui bougent au vent ; sans les textures, les formes simples d'avant.
    const formes: { g: THREE.BufferGeometry; m: THREE.Material | THREE.Material[] }[] = [];
    if (matieres) {
      const z = matieres.zones;
      const feuilles = matieres.materiauFeuillage();
      const ecorce = matieres.materiauEcorce(0);
      const stipe = matieres.materiauEcorce(1);
      formes.push(
        { g: Vg.araucaria(z), m: [ecorce, feuilles] },
        { g: Vg.feuillu(z), m: [ecorce, feuilles] },
        { g: Vg.cycas(z), m: [stipe, feuilles] },
        { g: Vg.touffeFougere(z), m: feuilles },
        { g: Vg.buisson(z), m: feuilles },
        { g: Vg.rocher(), m: matieres.materiauRocher() },
        { g: Vg.preles(z), m: feuilles },
        { g: Vg.fougereArborescente(z), m: [stipe, feuilles] },
      );
    } else {
      const simples = modeles();
      simples.forEach((g) => formes.push({ g, m: materiau }));
      formes.push({ g: simples[Sorte.Tropical], m: materiau });
    }
    this.texture = matieres !== null;
    formes.forEach(({ g, m: mat }, k) => {
      const capacite = k <= Sorte.Tropical || k === Sorte.FougereArbo ? 40000 : 60000;
      if (this.texture) g.setAttribute("vent", new THREE.InstancedBufferAttribute(new Float32Array(capacite * 3), 3));
      const m = new THREE.InstancedMesh(g, mat, capacite);
      m.count = 0;
      m.frustumCulled = false;
      m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      m.setColorAt(0, new THREE.Color(1, 1, 1));
      this.maillages.push(m);
      this.groupe.add(m);
    });
  }

  /** La saison a changé : les blocs seront refaits peu à peu. */
  invalider(): void {
    this.version++;
  }

  private cle(bx: number, bz: number): number {
    return (bz + 4096) * 8192 + bx + 4096;
  }

  mettreAJour(camera: THREE.Vector3, budgetMs: number): void {
    const r = this.portee;
    const debut = performance.now();
    const bx0 = Math.floor((camera.x - r) / COTE_BLOC);
    const bx1 = Math.floor((camera.x + r) / COTE_BLOC);
    const bz0 = Math.floor((camera.z - r) / COTE_BLOC);
    const bz1 = Math.floor((camera.z + r) / COTE_BLOC);
    const voulus: [number, number, number][] = [];
    for (let bz = bz0; bz <= bz1; bz++) {
      for (let bx = bx0; bx <= bx1; bx++) {
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
    if (change || this.aRefaire || camera.distanceTo(this.derniere) > 40) this.assembler(camera);
  }

  private poser(bx: number, bz: number): Pose[][] {
    const rel = this.relief;
    const parSorte: Pose[][] = Array.from({ length: SORTES }, () => []);
    const n = COTE_BLOC / PAS;
    const neige = (x: number, z: number) => (rel.temperatureEn(x, z) ?? 10) < 0;
    for (let j = 0; j < n; j++) {
      for (let i = 0; i < n; i++) {
        const gx = bx * n + i;
        const gz = bz * n + j;
        const h1 = hacher(gx, gz, 101);
        const x = (gx + unite(h1)) * PAS;
        const z = (gz + unite(hacher(gx, gz, 103))) * PAS;
        const biome = rel.biomeEn(x, z, unite(hacher(gx, gz, 107)));
        const table = DENSITES[biome];
        if (!table) continue;
        let tirage = unite(hacher(gx, gz, 109));
        const abondance = 0.4 + 0.8 * rel.plantesEn(x, z);
        let sorte: Sorte | -1 = -1;
        for (const [s, d] of table) {
          const dd = s === Sorte.Rocher ? d : d * abondance;
          if (tirage < dd) {
            sorte = s;
            break;
          }
          tirage -= dd;
        }
        if (sorte < 0) continue;
        const s = rel.surface(x, z);
        const mer = !Number.isNaN(s.eau) && s.eau === 0 && biome !== B.MANGROVE;
        if (mer || (!Number.isNaN(s.eau) && !((sorte === Sorte.Roseau && s.eau - s.sol < 0.8) || (biome === B.MANGROVE && s.eau - s.sol < 1.2)))) continue;
        if (s.sol < 0.6 && sorte !== Sorte.Roseau && biome !== B.MANGROVE) continue;
        const pente = Math.abs(rel.hauteur(x + 2, z) - s.sol) + Math.abs(rel.hauteur(x, z + 2) - s.sol);
        if (pente > (sorte === Sorte.Rocher ? 3 : 1.6)) continue;
        if (sorte === Sorte.Fougere && neige(x, z)) continue;
        const [hmin, etendue] = TAILLES[sorte];
        const v = unite(hacher(gx, gz, 113));
        const pose: Pose = { x, y: s.sol - 0.1, z, echelle: hmin + etendue * v * v, angle: unite(hacher(gx, gz, 127)) * Math.PI * 2, r: 1, g: 1, b: 1 };
        const teinte = 0.85 + 0.3 * unite(hacher(gx, gz, 131));
        pose.r = pose.g = pose.b = teinte;
        if (sorte === Sorte.Feuillu) this.saisonFeuillu(pose, x, z, biome);
        if ((sorte === Sorte.Conifere || sorte === Sorte.Buisson) && neige(x, z)) {
          pose.r = 1.9;
          pose.g = 1.9;
          pose.b = 2.1;
        }
        if (biome === B.MANGROVE && sorte === Sorte.Feuillu) {
          pose.echelle *= 0.55;
          pose.g *= 0.85;
        }
        parSorte[sorte].push(pose);
      }
    }
    return parSorte;
  }

  /** Feuillus : roux à l'automne, dénudés l'hiver là où il gèle, d'un vert tendre au printemps. */
  private saisonFeuillu(p: Pose, x: number, z: number, biome: number): void {
    if (biome === B.TROPICALE || biome === B.MANGROVE) return;
    const saison = this.relief.saison;
    const avance = this.relief.dansSaison;
    const froid = (this.relief.temperatureEn(x, z) ?? 15) < 8;
    const variete = unite(hacher(Math.floor(x), Math.floor(z), 7));
    if (saison === 2 && variete < 0.7) {
      const roux = Math.min(1, avance * 1.6) * (0.5 + variete * 0.7);
      p.r *= 1 + 1.6 * roux;
      p.g *= 1 + 0.25 * roux;
      p.b *= 1 - 0.5 * roux;
    } else if (saison === 3 && froid && variete < 0.6) {
      p.r *= 1.35;
      p.g *= 0.8;
      p.b *= 0.75;
    } else if (saison === 0) {
      p.g *= 1.12;
      p.r *= 1.05;
    }
  }

  private assembler(camera: THREE.Vector3): void {
    this.aRefaire = false;
    this.derniere.copy(camera);
    this.origine.set(Math.round(camera.x), 0, Math.round(camera.z));
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion();
    const axe = new THREE.Vector3(0, 1, 0);
    const pos = new THREE.Vector3();
    const ech = new THREE.Vector3();
    const col = new THREE.Color();
    const comptes = new Array(SORTES).fill(0);
    for (const parSorte of this.blocs.values()) {
      for (let s = 0; s < SORTES; s++) {
        const maillage = this.maillages[s];
        const limite = this.portee * PORTEE[s];
        for (const p of parSorte[s]) {
          if (comptes[s] >= maillage.instanceMatrix.count) break;
          const dx = p.x - camera.x;
          const dz = p.z - camera.z;
          if (dx * dx + dz * dz > limite * limite) continue;
          q.setFromAxisAngle(axe, p.angle);
          pos.set(p.x - this.origine.x, p.y, p.z - this.origine.z);
          ech.set(p.echelle * (s === Sorte.Rocher ? 1.3 : 1), p.echelle, p.echelle * (s === Sorte.Rocher ? 1.1 : 1));
          m.compose(pos, q, ech);
          maillage.setMatrixAt(comptes[s], m);
          maillage.setColorAt(comptes[s], col.setRGB(p.r, p.g, p.b));
          if (this.texture) {
            const v = maillage.geometry.getAttribute("vent") as THREE.InstancedBufferAttribute;
            v.setXYZ(comptes[s], p.angle * 7.3, SOUPLESSE[s], p.echelle);
          }
          comptes[s]++;
        }
      }
    }
    this.maillages.forEach((maillage, s) => {
      maillage.count = comptes[s];
      maillage.position.copy(this.origine);
      maillage.instanceMatrix.needsUpdate = true;
      if (maillage.instanceColor) maillage.instanceColor.needsUpdate = true;
      const v = maillage.geometry.getAttribute("vent");
      if (v) v.needsUpdate = true;
    });
  }

  vider(): void {
    this.blocs.clear();
    this.versions.clear();
    this.aRefaire = true;
  }
}
