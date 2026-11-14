import * as THREE from "three/webgpu";
import type { Echantillon, Relief } from "./relief";
import { PERIODE_SOL } from "./matieres";

/** Segments par côté de tuile : 33 × 33 points, plus une jupe qui cache les fentes entre tuiles de finesses différentes. */
const N = 32;

class Noeud {
  enfants: Noeud[] | null = null;
  maillage: THREE.Mesh | null = null;
  eau: THREE.Mesh | null = null;
  version = -1;
  vu = 0;
  hMin = 0;
  hMax = 0;
  constructor(
    readonly x0: number,
    readonly z0: number,
    readonly taille: number,
    readonly profondeur: number,
  ) {}
}

/**
 * Le sol de la vue 3D, en tuiles d'un arbre quaternaire : fines près de la caméra (jusqu'à environ 3 m entre deux points),
 * grossières au loin. Les tuiles se construisent quelques-unes par image ; une tuile ne cède la place à ses quatre enfants
 * que lorsqu'ils sont tous prêts, sans trou ni recouvrement.
 */
export class Terrain3D {
  readonly groupe = new THREE.Group();
  private readonly racine: Noeud;
  private readonly profondeurMax: number;
  private version = 0;
  private readonly enAttente: Noeud[] = [];
  private image = 0;
  private readonly construits = new Set<Noeud>();
  /** Finesse : plus haut, plus de tuiles fines (Basse 1,2 ; Moyenne 1,8 ; Haute 2,6). */
  facteur = 1.8;

  constructor(
    private readonly relief: Relief,
    private readonly materiau: THREE.Material,
    private readonly materiauEau: THREE.Material,
    /** Couleurs moyennes des couches photographiées du sol (0.41.5) ; null : couleurs seules, comme avant. */
    private readonly moyennes: readonly (readonly number[])[] | null = null,
  ) {
    let taille = relief.m;
    while (taille < Math.max(relief.largeurM, relief.hauteurM)) taille *= 2;
    this.racine = new Noeud(0, 0, taille, 0);
    let p = 0;
    for (let t = taille; t > 110; t /= 2) p++;
    this.profondeurMax = p;
    this.groupe.name = "terrain";
  }

  /** Les couleurs ont changé (saison, neige) : les tuiles seront refaites peu à peu, les anciennes restant visibles en attendant. */
  invalider(): void {
    this.version++;
  }

  /** Tuiles en cours de construction (0 : tout est prêt). */
  get attente(): number {
    return this.enAttente.length;
  }

  /** Choisit les tuiles à montrer depuis la caméra, et en construit quelques-unes dans le temps imparti (ms). */
  mettreAJour(camera: THREE.Vector3, budgetMs: number): void {
    this.image++;
    this.enAttente.length = 0;
    for (const c of this.groupe.children) c.visible = false;
    this.parcourir(this.racine, camera);
    this.enAttente.sort((a, b) => b.taille - a.taille || this.distance(a, camera) - this.distance(b, camera));
    const debut = performance.now();
    for (const n of this.enAttente) {
      if (performance.now() - debut > budgetMs) break;
      this.construire(n);
    }
    // Les tuiles qui n'ont pas servi depuis longtemps sont libérées.
    if (this.construits.size > 900) {
      const vieux = [...this.construits].filter((n) => this.image - n.vu > 300).sort((a, b) => a.vu - b.vu);
      for (const n of vieux.slice(0, this.construits.size - 700)) this.liberer(n);
    }
  }

  private distance(n: Noeud, c: THREE.Vector3): number {
    const dx = Math.max(n.x0 - c.x, 0, c.x - (n.x0 + n.taille));
    const dz = Math.max(n.z0 - c.z, 0, c.z - (n.z0 + n.taille));
    const dy = n.maillage ? Math.max(n.hMin - c.y, 0, c.y - n.hMax) : Math.max(0, c.y - 200);
    return Math.hypot(dx, dy, dz);
  }

  private dansLaCarte(n: Noeud): boolean {
    const marge = 6000;
    return n.x0 < this.relief.largeurM + marge && n.z0 < this.relief.hauteurM + marge && n.x0 + n.taille > -marge && n.z0 + n.taille > -marge;
  }

  private parcourir(n: Noeud, camera: THREE.Vector3): void {
    if (!this.dansLaCarte(n)) return;
    n.vu = this.image;
    const diviser = n.profondeur < this.profondeurMax && this.distance(n, camera) < n.taille * this.facteur;
    if (diviser) {
      n.enfants ??= [0, 1, 2, 3].map((k) => new Noeud(n.x0 + (k & 1) * n.taille / 2, n.z0 + (k >> 1) * n.taille / 2, n.taille / 2, n.profondeur + 1));
      const enfants = n.enfants.filter((e) => this.dansLaCarte(e));
      const prets = enfants.every((e) => e.maillage !== null);
      if (prets) {
        for (const e of enfants) this.parcourir(e, camera);
        return;
      }
      for (const e of enfants) if (!e.maillage) this.enAttente.push(e);
    }
    if (n.maillage) {
      n.maillage.visible = true;
      if (n.eau) n.eau.visible = true;
      if (n.version !== this.version) this.enAttente.push(n);
    } else {
      this.enAttente.push(n);
    }
  }

  private liberer(n: Noeud): void {
    for (const m of [n.maillage, n.eau]) {
      if (!m) continue;
      this.groupe.remove(m);
      m.geometry.dispose();
    }
    n.maillage = null;
    n.eau = null;
    this.construits.delete(n);
  }

  private construire(n: Noeud): void {
    const r = this.relief;
    const pas = n.taille / N;
    const cote = N + 3; // une rangée de plus de chaque côté pour les normales
    const h = new Float32Array(cote * cote);
    const eau = new Float32Array(cote * cote);
    const lit = new Float32Array(cote * cote);
    const e: Echantillon = { h: 0, eau: NaN, lit: 0 };
    let hMin = Infinity;
    let hMax = -Infinity;
    for (let j = 0; j < cote; j++) {
      for (let i = 0; i < cote; i++) {
        r.echantillonner(n.x0 + (i - 1) * pas, n.z0 + (j - 1) * pas, e, pas);
        const k = j * cote + i;
        h[k] = e.h;
        eau[k] = e.eau;
        lit[k] = e.lit;
        if (i >= 1 && j >= 1 && i <= N + 1 && j <= N + 1) {
          hMin = Math.min(hMin, e.h);
          hMax = Math.max(hMax, e.h);
        }
      }
    }
    const points = (N + 1) * (N + 1);
    const jupe = 4 * (N + 1);
    const positions = new Float32Array((points + jupe) * 3);
    const normales = new Float32Array((points + jupe) * 3);
    const couleurs = new Float32Array((points + jupe) * 3);
    const uvs = new Float32Array((points + jupe) * 2);
    const couches = this.moyennes ? new Float32Array((points + jupe) * 8) : null;
    const lointains = this.moyennes ? new Float32Array((points + jupe) * 3) : null;
    // Coordonnées de texture en mètres, ramenées dans une période commune à toutes les échelles du sol (raccord exact, précision gardée).
    const ux = ((n.x0 % PERIODE_SOL) + PERIODE_SOL) % PERIODE_SOL;
    const uz = ((n.z0 % PERIODE_SOL) + PERIODE_SOL) % PERIODE_SOL;
    const normale = new THREE.Vector3();
    for (let j = 0; j <= N; j++) {
      for (let i = 0; i <= N; i++) {
        const k = (j + 1) * cote + i + 1;
        const v = j * (N + 1) + i;
        const x = i * pas;
        const z = j * pas;
        positions[v * 3] = x;
        positions[v * 3 + 1] = h[k];
        positions[v * 3 + 2] = z;
        normale.set(h[k - 1] - h[k + 1], 2 * pas, h[k - cote] - h[k + cote]).normalize();
        normales[v * 3] = normale.x;
        normales[v * 3 + 1] = normale.y;
        normales[v * 3 + 2] = normale.z;
        e.h = h[k];
        e.eau = eau[k];
        e.lit = lit[k];
        const pente = 1 - normale.y * normale.y > 0 ? Math.sqrt(1 - normale.y * normale.y) : 0;
        if (couches && lointains && this.moyennes) r.couches(n.x0 + x, n.z0 + z, e, pente, this.moyennes, couches, v * 8, couleurs, v * 3, lointains);
        else r.couleur(n.x0 + x, n.z0 + z, e, pente, couleurs, v * 3);
        uvs[v * 2] = ux + x;
        uvs[v * 2 + 1] = uz + z;
      }
    }
    // Jupe : les points du bord, abaissés.
    const bord: number[] = [];
    for (let i = 0; i <= N; i++) bord.push(i);
    for (let j = 1; j <= N; j++) bord.push(j * (N + 1) + N);
    for (let i = N - 1; i >= 0; i--) bord.push(N * (N + 1) + i);
    for (let j = N - 1; j >= 1; j--) bord.push(j * (N + 1));
    const descente = pas * 2 + 8;
    bord.forEach((v, s) => {
      const w = points + s;
      positions[w * 3] = positions[v * 3];
      positions[w * 3 + 1] = positions[v * 3 + 1] - descente;
      positions[w * 3 + 2] = positions[v * 3 + 2];
      for (let c = 0; c < 3; c++) {
        normales[w * 3 + c] = normales[v * 3 + c];
        couleurs[w * 3 + c] = couleurs[v * 3 + c];
      }
      if (couches) for (let c = 0; c < 8; c++) couches[w * 8 + c] = couches[v * 8 + c];
      if (lointains) for (let c = 0; c < 3; c++) lointains[w * 3 + c] = lointains[v * 3 + c];
      uvs[w * 2] = uvs[v * 2];
      uvs[w * 2 + 1] = uvs[v * 2 + 1];
    });
    const indices: number[] = [];
    for (let j = 0; j < N; j++) {
      for (let i = 0; i < N; i++) {
        const a = j * (N + 1) + i;
        const b = a + 1;
        const c = a + N + 1;
        const d = c + 1;
        indices.push(a, c, b, b, c, d);
      }
    }
    const tour = bord.length;
    for (let s = 0; s < tour; s++) {
      const a = bord[s];
      const b = bord[(s + 1) % tour];
      const a2 = points + s;
      const b2 = points + ((s + 1) % tour);
      indices.push(a, b, a2, b, b2, a2, a, a2, b, b, a2, b2);
    }
    const geometrie = new THREE.BufferGeometry();
    geometrie.setAttribute("position", new THREE.BufferAttribute(positions, 3));
    geometrie.setAttribute("normal", new THREE.BufferAttribute(normales, 3));
    geometrie.setAttribute("color", new THREE.BufferAttribute(couleurs, 3));
    geometrie.setAttribute("uv", new THREE.BufferAttribute(uvs, 2));
    if (couches) {
      const p0 = new Float32Array((points + jupe) * 4);
      const p1 = new Float32Array((points + jupe) * 4);
      for (let v = 0; v < points + jupe; v++) {
        p0.set(couches.subarray(v * 8, v * 8 + 4), v * 4);
        p1.set(couches.subarray(v * 8 + 4, v * 8 + 8), v * 4);
      }
      geometrie.setAttribute("poids0", new THREE.BufferAttribute(p0, 4));
      geometrie.setAttribute("poids1", new THREE.BufferAttribute(p1, 4));
      if (lointains) geometrie.setAttribute("lointain", new THREE.BufferAttribute(lointains, 3));
    }
    geometrie.setIndex(indices);
    geometrie.computeBoundingSphere();

    // L'eau douce de la tuile (lacs, rivières) : une nappe là où il y en a, prolongée sous les rives.
    let eauGeo: THREE.BufferGeometry | null = null;
    const ie: number[] = [];
    const pe: number[] = [];
    const index = new Int32Array(points).fill(-1);
    const hauteurEau = (i: number, j: number) => eau[(j + 1) * cote + i + 1];
    for (let j = 0; j < N; j++) {
      for (let i = 0; i < N; i++) {
        const coins = [[i, j], [i + 1, j], [i, j + 1], [i + 1, j + 1]] as const;
        const niveaux = coins.map(([a, b]) => hauteurEau(a, b)).filter((v) => !Number.isNaN(v));
        if (niveaux.length === 0) continue;
        const moyen = niveaux.reduce((s, v) => s + v, 0) / niveaux.length;
        const ids = coins.map(([a, b]) => {
          const v = b * (N + 1) + a;
          if (index[v] < 0) {
            index[v] = pe.length / 3;
            const niveau = hauteurEau(a, b);
            pe.push(a * pas, Number.isNaN(niveau) ? moyen : niveau, b * pas);
          }
          return index[v];
        });
        ie.push(ids[0], ids[2], ids[1], ids[1], ids[2], ids[3]);
      }
    }
    if (ie.length > 0) {
      eauGeo = new THREE.BufferGeometry();
      eauGeo.setAttribute("position", new THREE.Float32BufferAttribute(pe, 3));
      const uvEau = new Float32Array((pe.length / 3) * 2);
      for (let v = 0; v < pe.length / 3; v++) {
        uvEau[v * 2] = (n.x0 + pe[v * 3]) / 40;
        uvEau[v * 2 + 1] = (n.z0 + pe[v * 3 + 2]) / 40;
      }
      eauGeo.setAttribute("uv", new THREE.BufferAttribute(uvEau, 2));
      eauGeo.setIndex(ie);
      eauGeo.computeVertexNormals();
    }

    for (const m of [n.maillage, n.eau]) {
      if (!m) continue;
      this.groupe.remove(m);
      m.geometry.dispose();
    }
    const maillage = new THREE.Mesh(geometrie, this.materiau);
    maillage.position.set(n.x0, 0, n.z0);
    maillage.matrixAutoUpdate = false;
    maillage.updateMatrix();
    maillage.visible = false;
    this.groupe.add(maillage);
    n.maillage = maillage;
    n.eau = null;
    if (eauGeo) {
      const m = new THREE.Mesh(eauGeo, this.materiauEau);
      m.position.set(n.x0, 0.05, n.z0);
      m.matrixAutoUpdate = false;
      m.updateMatrix();
      m.visible = false;
      m.renderOrder = 1;
      this.groupe.add(m);
      n.eau = m;
    }
    n.hMin = hMin;
    n.hMax = hMax;
    n.version = this.version;
    this.construits.add(n);
  }
}
