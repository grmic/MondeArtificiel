import * as THREE from "three/webgpu";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";

/** Ce que donne le serveur (CampDto) : huttes par 5, champs par 6, bêtes par 7 nombres. */
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

interface Troupe {
  id: number;
  /** Centre de la troupe (m) et rayon où elle se tient. */
  x: number;
  z: number;
  rayon: number;
  forme: number;
  echelle: number;
  nombre: number;
  gardees: boolean;
  couleur: THREE.Color;
  /** Les gardées vivent dans un enclos ; les sauvages errent lentement. */
  errance: number;
}

const MAX_HUTTES = 600;
const MAX_PLANTES = 24000;
const MAX_BETES = 500;
const MAX_PIQUETS = 3000;

/** Silhouette des bêtes (huit formes de la faune, Faune.cs) : corps d'un seul tenant et pattes à part, pour 1 m de long. */
interface Gabarit {
  corps: THREE.BufferGeometry;
  patte: THREE.BufferGeometry;
  /** Points d'attache des pattes (x, y, z) et hauteur du dos. */
  pattes: [number, number, number][];
  couleur: THREE.Color;
}

function ellipsoide(rx: number, ry: number, rz: number, x: number, y: number, z: number): THREE.BufferGeometry {
  return new THREE.SphereGeometry(1, 12, 8).scale(rx, ry, rz).translate(x, y, z);
}

function cylindre(r1: number, r2: number, l: number, x: number, y: number, z: number, rx = 0, rz = 0): THREE.BufferGeometry {
  return new THREE.CylinderGeometry(r1, r2, l, 7).rotateX(rx).rotateZ(rz).translate(x, y, z);
}

function cone(r: number, l: number, x: number, y: number, z: number, rx: number, rz: number): THREE.BufferGeometry {
  return new THREE.ConeGeometry(r, l, 6).rotateX(rx).rotateZ(rz).translate(x, y, z);
}

function patte(l: number, r: number): THREE.BufferGeometry {
  return new THREE.CylinderGeometry(r, r * 0.6, l, 6).translate(0, -l / 2, 0);
}

function gabarits(): Gabarit[] {
  const g = (corps: THREE.BufferGeometry[], p: THREE.BufferGeometry, pattes: [number, number, number][], c: number): Gabarit =>
    ({ corps: mergeGeometries(corps)!, patte: p, pattes, couleur: new THREE.Color(c) });
  const quatre = (x: number, y: number, z: number): [number, number, number][] => [[x, y, z], [-x, y, z], [x, y, -z], [-x, y, -z]];
  return [
    // 0 la bête à cornes : massive, cornes en avant.
    g([ellipsoide(0.22, 0.24, 0.48, 0, 0.72, 0), cylindre(0.11, 0.14, 0.3, 0, 0.82, 0.45, 1.1), ellipsoide(0.1, 0.12, 0.17, 0, 0.8, 0.62),
      cone(0.035, 0.28, 0.13, 0.95, 0.6, -0.4, -0.9), cone(0.035, 0.28, -0.13, 0.95, 0.6, -0.4, 0.9), cylindre(0.015, 0.01, 0.4, 0, 0.6, -0.5, 0.3)],
    patte(0.52, 0.05), quatre(0.13, 0.55, 0.3), 0x5b4332),
    // 1 la bête à sabots : élancée, long cou dressé.
    g([ellipsoide(0.16, 0.18, 0.45, 0, 0.92, 0), cylindre(0.07, 0.1, 0.45, 0, 1.12, 0.42, 0.55), ellipsoide(0.07, 0.08, 0.17, 0, 1.3, 0.6),
      cone(0.025, 0.1, 0.05, 1.38, 0.52, 0, 0), cone(0.025, 0.1, -0.05, 1.38, 0.52, 0, 0), cylindre(0.03, 0.01, 0.35, 0, 0.85, -0.5, 0.5)],
    patte(0.78, 0.03), quatre(0.09, 0.8, 0.32), 0x8a6a45),
    // 2 le petit brouteur : rond, longues oreilles.
    g([ellipsoide(0.2, 0.2, 0.32, 0, 0.3, 0), ellipsoide(0.11, 0.11, 0.14, 0, 0.42, 0.3), cylindre(0.03, 0.02, 0.22, 0.04, 0.6, 0.27, -0.2),
      cylindre(0.03, 0.02, 0.22, -0.04, 0.6, 0.27, -0.2), ellipsoide(0.06, 0.06, 0.06, 0, 0.32, -0.32)],
    patte(0.16, 0.035), quatre(0.1, 0.18, 0.16), 0x8f7e66),
    // 3 le fouisseur : bas et allongé.
    g([ellipsoide(0.2, 0.15, 0.45, 0, 0.27, 0), ellipsoide(0.1, 0.09, 0.16, 0, 0.26, 0.5), cylindre(0.03, 0.01, 0.15, 0, 0.27, -0.5, 1.3)],
    patte(0.15, 0.04), quatre(0.13, 0.15, 0.27), 0x4a4440),
    // 4 le rôdeur : fin, museau pointu, queue touffue.
    g([ellipsoide(0.13, 0.15, 0.4, 0, 0.58, 0), cylindre(0.06, 0.08, 0.2, 0, 0.68, 0.38, 1.0), ellipsoide(0.07, 0.08, 0.1, 0, 0.74, 0.5),
      cone(0.04, 0.16, 0, 0.72, 0.64, Math.PI / 2, 0), cone(0.03, 0.09, 0.04, 0.84, 0.48, 0, 0), cone(0.03, 0.09, -0.04, 0.84, 0.48, 0, 0),
      ellipsoide(0.05, 0.05, 0.22, 0, 0.5, -0.55)],
    patte(0.45, 0.025), quatre(0.08, 0.48, 0.26), 0x7a6a58),
    // 5 le grand chasseur : long, tête ronde, longue queue.
    g([ellipsoide(0.18, 0.2, 0.55, 0, 0.72, 0), ellipsoide(0.13, 0.12, 0.14, 0, 0.82, 0.62), cone(0.06, 0.12, 0, 0.8, 0.76, Math.PI / 2, 0),
      cylindre(0.03, 0.02, 0.7, 0, 0.6, -0.8, 0.9)],
    patte(0.58, 0.05), quatre(0.11, 0.6, 0.35), 0xa07a42),
    // 6 l'oiseau coureur : sur deux longues pattes.
    g([ellipsoide(0.25, 0.22, 0.32, 0, 1.0, 0), cylindre(0.04, 0.06, 0.6, 0, 1.35, 0.22, 0.35), ellipsoide(0.06, 0.06, 0.09, 0, 1.65, 0.33),
      cone(0.025, 0.1, 0, 1.64, 0.43, Math.PI / 2, 0), ellipsoide(0.18, 0.12, 0.12, 0, 1.05, -0.32)],
    patte(0.9, 0.03), [[0.1, 0.92, 0], [-0.1, 0.92, 0]], 0x3f3a36),
    // 7 le grimpeur : dos rond, longs bras, queue.
    g([ellipsoide(0.15, 0.2, 0.25, 0, 0.5, 0), ellipsoide(0.1, 0.1, 0.1, 0, 0.72, 0.22), cylindre(0.02, 0.015, 0.5, 0, 0.55, -0.4, 0.6)],
    patte(0.45, 0.03), [[0.12, 0.62, 0.15], [-0.12, 0.62, 0.15], [0.1, 0.42, -0.15], [-0.1, 0.42, -0.15]], 0x5a4a3a),
  ];
}

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
  private readonly piquets: THREE.InstancedMesh;
  private readonly gabarits = gabarits();
  private readonly corpsBetes: THREE.InstancedMesh[];
  private readonly pattesBetes: THREE.InstancedMesh[];
  private huttes: Hutte[] = [];
  private champs: Champ[] = [];
  private troupes: Troupe[] = [];
  private readonly origine = new THREE.Vector3();
  /** Les plantes des champs ne bougent pas : refaites seulement quand les champs ou la caméra changent beaucoup. */
  private plantesA = new THREE.Vector3(NaN, 0, 0);
  private plantesCle = "";
  portee = 2500;

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
    this.corpsBetes = this.gabarits.map((g) => new THREE.InstancedMesh(g.corps, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.9 }), MAX_BETES));
    this.pattesBetes = this.gabarits.map((g) => new THREE.InstancedMesh(g.patte, new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.9 }), MAX_BETES * 4));
    for (const m of [this.branches, this.argile, this.entrees, this.plantes, this.piquets, ...this.corpsBetes, ...this.pattesBetes]) {
      m.count = 0;
      m.frustumCulled = false;
      m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      this.groupe.add(m);
    }
    for (const m of [this.branches, this.argile, this.plantes, ...this.corpsBetes, ...this.pattesBetes]) m.setColorAt(0, new THREE.Color());
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
    for (const [cle, liste] of parCase) {
      const [cx, cz] = rel.camp(cle % rel.l, Math.floor(cle / rel.l));
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
    // Bêtes.
    this.troupes = [];
    for (let k = 0; k + 7 <= json.betes.length; k += 7) {
      const [id, x, y, forme, poids, nombre, gardees] = json.betes.slice(k, k + 7);
      const [cx, cz] = rel.camp(x, y);
      const g = this.gabarits[Math.max(0, Math.min(this.gabarits.length - 1, forme))];
      const echelle = Math.max(0.25, Math.min(3.2, Math.cbrt(Math.max(1, poids) / 120) * 1.5));
      const montre = Math.min(gardees ? 30 : 8, nombre);
      const couleur = g.couleur.clone().offsetHSL((unite(hacher(id, 51)) - 0.5) * 0.04, (unite(hacher(id, 53)) - 0.5) * 0.1, (unite(hacher(id, 55)) - 0.5) * 0.12);
      if (gardees === 1) {
        const a = unite(hacher(id, 57)) * Math.PI * 2;
        const rayon = 2.5 + Math.sqrt(montre) * echelle * 1.6;
        this.troupes.push({ id, x: cx + Math.cos(a) * (22 + rayon), z: cz + Math.sin(a) * (22 + rayon), rayon, forme, echelle, nombre: montre, gardees: true, couleur, errance: 0 });
      } else {
        const ox = (unite(hacher(x, y, 59)) - 0.5) * rel.m * 0.7;
        const oz = (unite(hacher(x, y, 61)) - 0.5) * rel.m * 0.7;
        this.troupes.push({ id, x: (x + 0.5) * rel.m + ox, z: (y + 0.5) * rel.m + oz, rayon: 6 + Math.sqrt(montre) * echelle * 3, forme, echelle, nombre: montre, gardees: false, couleur, errance: 1 });
      }
    }
    this.plantesCle = "";
  }

  /** Chaque image : place huttes, bêtes et enclos autour de la caméra. */
  animer(camera: THREE.Vector3, secondes: number, temps: number): void {
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
    // Bêtes et enclos.
    const nc = this.gabarits.map(() => 0);
    const np = this.gabarits.map(() => 0);
    let npi = 0;
    for (const t of this.troupes) {
      const d = Math.hypot(t.x - camera.x, t.z - camera.z);
      if (d > Math.min(this.portee, 1800)) continue;
      const g = this.gabarits[t.forme] ?? this.gabarits[0];
      // Les sauvages errent lentement (en heures du monde) ; les gardées tournent dans leur enclos.
      const errX = t.errance * Math.sin(secondes / 7200 + t.id) * 120;
      const errZ = t.errance * Math.cos(secondes / 9100 + t.id * 1.7) * 120;
      for (let k = 0; k < t.nombre; k++) {
        const graine = hacher(t.id, k, 63);
        const a = unite(graine) * Math.PI * 2 + secondes / (2400 + 1200 * unite(hacher(graine, 1)));
        const r = t.rayon * Math.sqrt(unite(hacher(graine, 2))) * 0.85;
        const x = t.x + errX + Math.cos(a) * r;
        const z = t.z + errZ + Math.sin(a) * r;
        const s = this.relief.surface(x, z);
        if (!Number.isNaN(s.eau) && s.eau - s.sol > 0.4) continue;
        const marche = Math.sin(secondes / 600 + k * 3.1 + t.id) > 0.2;
        const cap = a + Math.PI / 2 + (unite(hacher(graine, 3)) - 0.5) * 0.6;
        const ech = t.echelle * (0.85 + 0.3 * unite(hacher(graine, 4)));
        const broute = !marche && t.forme !== 6 ? -0.25 : 0;
        q.setFromAxisAngle(haut, cap).multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -broute * 0.3));
        m.compose(p.set(x - o.x, s.sol, z - o.z), q, e.set(ech, ech, ech));
        const corps = this.corpsBetes[t.forme] ?? this.corpsBetes[0];
        if (nc[t.forme] >= MAX_BETES) continue;
        corps.setMatrixAt(nc[t.forme], m);
        corps.setColorAt(nc[t.forme]++, c.copy(t.couleur).multiplyScalar(0.9 + 0.2 * unite(hacher(graine, 5))));
        const pattes = this.pattesBetes[t.forme] ?? this.pattesBetes[0];
        g.pattes.forEach(([px, py, pz], j) => {
          const angle = marche ? Math.sin(temps * 6 + j * Math.PI * (j % 2 ? 1 : 0.5) + k) * 0.45 : 0;
          const pm = new THREE.Matrix4().compose(p.set(px, py, pz), new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), angle), e.set(1, 1, 1));
          pattes.setMatrixAt(np[t.forme], m.clone().multiply(pm));
          pattes.setColorAt(np[t.forme]++, c.copy(t.couleur).multiplyScalar(0.75));
        });
      }
      // Enclos : un cercle de piquets autour des bêtes gardées.
      if (t.gardees && t.nombre >= 2) {
        const n = Math.min(80, Math.ceil((t.rayon * 2 * Math.PI) / 1.6));
        for (let k = 0; k < n && npi < MAX_PIQUETS; k++) {
          const a = (k / n) * Math.PI * 2;
          const x = t.x + Math.cos(a) * t.rayon * 1.05;
          const z = t.z + Math.sin(a) * t.rayon * 1.05;
          m.compose(p.set(x - o.x, this.relief.hauteur(x, z), z - o.z), q.setFromAxisAngle(haut, a), e.set(1, 0.9 + 0.2 * unite(hacher(t.id, k)), 1));
          this.piquets.setMatrixAt(npi++, m);
        }
      }
    }
    this.corpsBetes.forEach((x, k) => (x.count = nc[k]));
    this.pattesBetes.forEach((x, k) => (x.count = np[k]));
    this.piquets.count = npi;
    for (const x of [this.branches, this.argile, this.entrees, this.piquets, ...this.corpsBetes, ...this.pattesBetes]) {
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
