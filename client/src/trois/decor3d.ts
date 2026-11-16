import * as THREE from "three/webgpu";
import { abs, attribute, clamp, cos, faceDirection, float, mix, normalize, sin, smoothstep, transformNormalToView, vec3 } from "three/tsl";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import { t } from "../langue";
import type { Carte } from "../carte";
import { hacher, unite } from "./bruit";
import { envoyer } from "./envoi";
import { meteoGPU } from "./meteo3d";
import {
  CONIFERES, EAU_PEU_PROFONDE, FEUILLUS, LAC, MANGROVE, MARAIS, MONTAGNE, OCEAN, PLAGE, PLAINE, RIVIERE, SOMMET, TROPICALE, type Relief,
} from "./relief";

/**
 * Le ciel et la mer de la vue 3D (0.44.1, choix F1) : ptérosaures planant au-dessus des côtes et des falaises, oiseaux primitifs en
 * vols au-dessus des forêts et des marais, libellules près de l'eau douce le jour, bancs de poissons dans les rivières, les lacs et
 * près des côtes, mosasaures et plésiosaures au large, qui remontent respirer. La simulation ne les compte pas : c'est un décor,
 * mais réglé par elle (biome, abondance des plantes et du gibier de la case, terres brûlées, jour et nuit, saison, température), et
 * le même pour tous (tout est tiré de la case et de l'heure). Les poissons s'assemblent sous les pêcheurs, et le poisson qu'un
 * pêcheur a pris (la simulation le dit) se voit à ses pieds.
 *
 * Chaque sorte a un corps calculé (une seule passe de dessin par sorte) que la carte graphique anime : ondulation des nageurs,
 * battement des ailes et des nageoires. Le processeur ne calcule que la place de chaque bête, sans état : une fonction de l'heure.
 */

/** « Espèce » d'une prise qui est un poisson, dans le flux Scène (Humain.PrisePoisson). */
export const PRISE_POISSON = 1023;

const enum S {
  Poisson,
  Mosasaure,
  Plesiosaure,
  Pterosaure,
  Oiseau,
  Libellule,
}
const SORTES = 6;
const NOMS = ["poisson", "mosasaure", "plesiosaure", "pterosaure", "oiseau", "libellule"];
/** Les volants se mesurent à l'envergure, les nageurs à la longueur. */
const VOLANT = [false, false, false, true, true, true];

const DEUX_PI = Math.PI * 2;

// ——— Corps ———

type Os = (p: THREE.Vector3) => [number, number, number, number];

/** Os d'un nageur : tout le corps ondule, de la tête (t = 0, à l'avant z = 0,5) à la queue (t = 1). */
const nage: Os = (p) => [2, Math.min(1, Math.max(0, 0.5 - p.z)), 0, 0];

/** Une pièce du corps, couleur du dos au ventre selon la normale, et ses os. */
function piece(g0: THREE.BufferGeometry, dos: THREE.Color, ventre: THREE.Color, os: Os): THREE.BufferGeometry {
  const g = g0.index ? g0.toNonIndexed() : g0;
  g.deleteAttribute("uv");
  if (!g.getAttribute("normal")) g.computeVertexNormals();
  const pos = g.getAttribute("position");
  const nor = g.getAttribute("normal");
  const col = new Float32Array(pos.count * 3);
  const a = new Float32Array(pos.count * 4);
  const p = new THREE.Vector3();
  const c = new THREE.Color();
  for (let i = 0; i < pos.count; i++) {
    p.fromBufferAttribute(pos, i);
    c.copy(ventre).lerp(dos, Math.min(1, Math.max(0, 0.5 + nor.getY(i) * 0.8)));
    c.toArray(col, i * 3);
    a.set(os(p), i * 4);
  }
  g.setAttribute("color", new THREE.BufferAttribute(col, 3));
  g.setAttribute("aOs", new THREE.BufferAttribute(a, 4));
  return g;
}

function ellipsoide(rx: number, ry: number, rz: number, x: number, y: number, z: number, seg = 10): THREE.BufferGeometry {
  return new THREE.SphereGeometry(1, seg, Math.max(5, Math.round(seg * 0.6))).scale(rx, ry, rz).translate(x, y, z);
}

/** Des triangles plats (nageoires, ailes), donnés par leurs sommets. */
function triangles(...sommets: [number, number, number][]): THREE.BufferGeometry {
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.BufferAttribute(new Float32Array(sommets.flat()), 3));
  g.computeVertexNormals();
  return g;
}

/** Une aile ou une nageoire qui bat autour de sa racine (|x| = racine), de chaque côté. */
function paire(sommets: [number, number, number][], racine: number, dos: THREE.Color, ventre: THREE.Color): THREE.BufferGeometry[] {
  return [1, -1].map((s) => {
    const pts = sommets.map(([x, y, z]) => [x * s, y, z] as [number, number, number]);
    if (s < 0) for (let k = 0; k < pts.length; k += 3) [pts[k + 1], pts[k + 2]] = [pts[k + 2], pts[k + 1]];
    return piece(triangles(...pts), dos, ventre, (p) => [1, Math.min(1, Math.max(0, 0.5 - p.z)), s, racine]);
  });
}

const C = (h: number) => new THREE.Color(h);

function corps(sorte: S): THREE.BufferGeometry {
  const parts: THREE.BufferGeometry[] = [];
  const oeil = (x: number, y: number, z: number, r: number) =>
    parts.push(piece(ellipsoide(r, r, r, x, y, z, 6), C(0x111111), C(0x111111), nage), piece(ellipsoide(r, r, r, -x, y, z, 6), C(0x111111), C(0x111111), nage));
  switch (sorte) {
    case S.Poisson: {
      const dos = C(0x56707a);
      const ventre = C(0xd8dcd6);
      parts.push(piece(ellipsoide(0.08, 0.15, 0.46, 0, 0, 0.02), dos, ventre, nage));
      parts.push(piece(triangles([0, 0, -0.4], [0, 0.18, -0.66], [0, 0, -0.56], [0, 0, -0.4], [0, 0, -0.56], [0, -0.18, -0.66]), dos, dos, nage));
      parts.push(piece(triangles([0, 0.13, 0.08], [0, 0.21, -0.12], [0, 0.12, -0.16]), dos, dos, nage));
      oeil(0.055, 0.04, 0.34, 0.025);
      break;
    }
    case S.Mosasaure: {
      const dos = C(0x3e4648);
      const ventre = C(0xc9c3b2);
      parts.push(piece(ellipsoide(0.065, 0.075, 0.36, 0, 0, 0.08), dos, ventre, nage));
      parts.push(piece(ellipsoide(0.04, 0.038, 0.13, 0, 0.005, 0.44), dos, ventre, nage));
      parts.push(piece(ellipsoide(0.035, 0.05, 0.26, 0, -0.005, -0.36), dos, ventre, nage));
      parts.push(piece(triangles([0, 0, -0.55], [0, -0.1, -0.74], [0, 0, -0.64], [0, 0, -0.6], [0, 0, -0.66], [0, 0.05, -0.7]), dos, dos, nage));
      for (const z of [0.25, -0.08]) parts.push(...paire([[0.05, -0.03, z + 0.04], [0.17, -0.06, z - 0.07], [0.05, -0.03, z - 0.04]], 0.05, dos, ventre));
      oeil(0.032, 0.025, 0.47, 0.01);
      break;
    }
    case S.Plesiosaure: {
      const dos = C(0x4d5550);
      const ventre = C(0xbfbcab);
      parts.push(piece(ellipsoide(0.11, 0.085, 0.22, 0, 0, -0.02), dos, ventre, nage));
      parts.push(piece(new THREE.CylinderGeometry(0.022, 0.05, 0.4, 8, 3).rotateX(Math.PI / 2 - 0.15).translate(0, 0.03, 0.37), dos, ventre, nage));
      parts.push(piece(ellipsoide(0.03, 0.026, 0.06, 0, 0.06, 0.6), dos, ventre, nage));
      parts.push(piece(ellipsoide(0.03, 0.035, 0.14, 0, -0.005, -0.3), dos, ventre, nage));
      for (const z of [0.1, -0.13]) parts.push(...paire([[0.08, -0.03, z + 0.05], [0.33, -0.06, z - 0.06], [0.08, -0.03, z - 0.05]], 0.08, dos, ventre));
      oeil(0.022, 0.07, 0.62, 0.008);
      break;
    }
    case S.Pterosaure: {
      const corpsC = C(0xe2ddd0);
      const aile = C(0x6a5446);
      const os: Os = () => [0, 0, 0, 0];
      parts.push(piece(ellipsoide(0.035, 0.04, 0.11, 0, 0, 0), corpsC, corpsC, os));
      parts.push(piece(ellipsoide(0.024, 0.03, 0.05, 0, 0.02, 0.14), corpsC, corpsC, os));
      parts.push(piece(new THREE.ConeGeometry(0.014, 0.17, 6).rotateX(Math.PI / 2).translate(0, 0.012, 0.26), C(0xc7a46a), C(0xc7a46a), os));
      parts.push(piece(triangles([0, 0.035, 0.15], [0, 0.13, 0.05], [0, 0.03, 0.11]), C(0xb4432a), C(0xb4432a), os));
      parts.push(...paire([[0.03, 0, 0.06], [0.26, 0.01, 0.07], [0.21, 0, -0.09], [0.03, 0, 0.06], [0.21, 0, -0.09], [0.03, 0, -0.08],
        [0.26, 0.01, 0.07], [0.5, 0, -0.03], [0.21, 0, -0.09]], 0.03, aile, aile));
      break;
    }
    case S.Oiseau: {
      const dos = C(0x5b4a3a);
      const ventre = C(0xc8b89a);
      const os: Os = () => [0, 0, 0, 0];
      parts.push(piece(ellipsoide(0.07, 0.07, 0.16, 0, 0, 0), dos, ventre, os));
      parts.push(piece(ellipsoide(0.05, 0.05, 0.055, 0, 0.025, 0.17), dos, ventre, os));
      parts.push(piece(new THREE.ConeGeometry(0.016, 0.07, 5).rotateX(Math.PI / 2).translate(0, 0.02, 0.25), C(0x3a3028), C(0x3a3028), os));
      parts.push(piece(triangles([0, 0.01, -0.12], [0.08, 0.01, -0.32], [-0.08, 0.01, -0.32]), dos, dos, os));
      parts.push(...paire([[0.05, 0.01, 0.07], [0.26, 0.01, 0.06], [0.22, 0.01, -0.1], [0.05, 0.01, 0.07], [0.22, 0.01, -0.1], [0.05, 0.01, -0.06],
        [0.26, 0.01, 0.06], [0.5, 0.01, -0.09], [0.22, 0.01, -0.1]], 0.05, dos, ventre));
      break;
    }
    case S.Libellule: {
      const corpsC = C(0x2f6f8f);
      const aile = C(0xc9d6dc);
      const os: Os = () => [0, 0, 0, 0];
      parts.push(piece(ellipsoide(0.04, 0.04, 0.07, 0, 0, 0), corpsC, corpsC, os));
      parts.push(piece(ellipsoide(0.05, 0.045, 0.045, 0, 0.005, 0.09), C(0x6a8a3a), C(0x6a8a3a), os));
      parts.push(piece(new THREE.CylinderGeometry(0.012, 0.018, 0.62, 6).rotateX(Math.PI / 2).translate(0, 0, -0.38), corpsC, corpsC, os));
      for (const [z, l] of [[0.03, 0.5], [-0.045, 0.47]] as const)
        parts.push(...paire([[0.03, 0.02, z + 0.02], [l - 0.02, 0.02, z + 0.01], [l, 0.02, z - 0.02], [0.03, 0.02, z + 0.02], [l, 0.02, z - 0.02], [0.03, 0.02, z - 0.02]],
          0.03, aile, aile));
      break;
    }
  }
  const g = mergeGeometries(parts);
  if (!g) throw new Error("decor3d: corps " + sorte);
  return g;
}

/**
 * Le matériau commun : iA = (x, y, z, cap), iB = (échelle, phase, battement, tangage), iC = (roulis, ondulation, teinte, 0).
 * aOs = (genre : 0 rigide, 1 aile ou nageoire qui bat, 2 corps qui ondule ; t de la tête à la queue ; côté ; racine de l'aile).
 */
function materiau(): THREE.MeshStandardNodeMaterial {
  const mat = new THREE.MeshStandardNodeMaterial({ roughness: 0.75, metalness: 0, side: THREE.DoubleSide });
  const iA = attribute("iA", "vec4");
  const iB = attribute("iB", "vec4");
  const iC = attribute("iC", "vec4");
  const os = attribute("aOs", "vec4");
  const genre = os.x;
  const tt = os.y;
  const cote = os.z;
  const racine = os.w;
  const phase = iB.y;
  const mAile = clamp(float(1).sub(abs(genre.sub(1)).mul(4)), 0, 1);
  const rotX = (v: THREE.Node<"vec3">, a: THREE.Node<"float">) => vec3(v.x, v.y.mul(cos(a)).sub(v.z.mul(sin(a))), v.y.mul(sin(a)).add(v.z.mul(cos(a))));
  const rotY = (v: THREE.Node<"vec3">, a: THREE.Node<"float">) => vec3(v.x.mul(cos(a)).add(v.z.mul(sin(a))), v.y, v.z.mul(cos(a)).sub(v.x.mul(sin(a))));
  const rotZ = (v: THREE.Node<"vec3">, a: THREE.Node<"float">) => vec3(v.x.mul(cos(a)).sub(v.y.mul(sin(a))), v.x.mul(sin(a)).add(v.y.mul(cos(a))), v.z);
  // Ondulation : une vague qui court de la tête à la queue, plus ample vers la queue.
  const p0 = attribute("position", "vec3");
  const ox = iC.y.mul(sin(phase.sub(tt.mul(5.5)))).mul(tt.add(0.15));
  const p1 = vec3(p0.x.add(ox), p0.y, p0.z);
  // Battement : l'aile tourne autour de sa racine, le bout un peu plus (le poignet plie).
  const a = sin(phase).mul(iB.z).add(0.06).mul(float(1).add(smoothstep(0.15, 0.5, abs(p0.x)).mul(0.5))).mul(cote);
  const r = vec3(cote.mul(racine), 0, 0);
  const p2 = mix(p1, rotZ(p1.sub(r), a).add(r), mAile);
  const orienter = (v: THREE.Node<"vec3">) => rotY(rotX(rotZ(v, iC.x), float(0).sub(iB.w)), iA.w);
  mat.positionNode = orienter(p2.mul(iB.x)).add(iA.xyz);
  const n0 = attribute("normal", "vec3");
  const n1 = mix(n0, rotZ(n0, a), mAile);
  mat.normalNode = transformNormalToView(normalize(orienter(n1))).mul(faceDirection);
  mat.colorNode = attribute("color", "vec3").mul(iC.z);
  return mat;
}

class Rendu {
  readonly maillage: THREE.Mesh;
  private readonly geo = new THREE.InstancedBufferGeometry();
  private a!: THREE.InstancedBufferAttribute;
  private b!: THREE.InstancedBufferAttribute;
  private c!: THREE.InstancedBufferAttribute;
  private capacite = 0;
  n = 0;

  constructor(base: THREE.BufferGeometry, mat: THREE.Material, nom: string) {
    for (const [k, v] of Object.entries(base.attributes)) this.geo.setAttribute(k, v);
    this.agrandir(32);
    this.geo.instanceCount = 0;
    this.maillage = new THREE.Mesh(this.geo, mat);
    this.maillage.frustumCulled = false;
    this.maillage.name = nom;
    this.maillage.visible = false;
  }

  private agrandir(n: number): void {
    const cap = Math.max(n, this.capacite * 2);
    const neuf = (ancien?: THREE.InstancedBufferAttribute) => {
      const at = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4);
      if (ancien) at.array.set(ancien.array as Float32Array);
      return at;
    };
    this.a = neuf(this.a);
    this.b = neuf(this.b);
    this.c = neuf(this.c);
    this.geo.setAttribute("iA", this.a);
    this.geo.setAttribute("iB", this.b);
    this.geo.setAttribute("iC", this.c);
    this.capacite = cap;
  }

  ajouter(x: number, y: number, z: number, cap: number, echelle: number, phase: number, battement: number, tangage: number,
    roulis: number, ondulation: number, teinte: number): void {
    if (this.n >= this.capacite) this.agrandir(this.n + 1);
    const k = this.n * 4;
    (this.a.array as Float32Array).set([x, y, z, cap], k);
    (this.b.array as Float32Array).set([echelle, phase, battement, tangage], k);
    (this.c.array as Float32Array).set([roulis, ondulation, teinte, 0], k);
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

// ——— Qui vit où ———

interface Membre {
  /** Place dans le groupe (m) : de côté, en hauteur, en avant ; décalage de phase ; taille (m) ; teinte. */
  dx: number;
  dy: number;
  dz: number;
  phase: number;
  echelle: number;
  teinte: number;
  /** Place à la dernière image : x, y, z, cap. */
  x: number;
  y: number;
  z: number;
  cap: number;
}

interface Groupe {
  cle: string;
  sorte: S;
  /** Point d'attache, rayon et vitesse de la ronde, sens (±1), hauteur de base (sol, surface de l'eau ou altitude de vol). */
  x: number;
  z: number;
  rayon: number;
  vitesse: number;
  sens: number;
  base: number;
  graine: number;
  membres: Membre[];
}

/** Grandeurs de chaque sorte : maille (m) où l'on tire un groupe, portée (m) où on le voit. */
const MAILLE = [30, 700, 700, 500, 260, 14];
const PORTEE = [90, 900, 900, 1200, 650, 40];

function regarde(dx: number, dz: number): number {
  return Math.atan2(dx, dz);
}

export class Decor3D {
  readonly groupe = new THREE.Group();
  private readonly mat = materiau();
  private readonly rendus: Rendu[];
  private groupes = new Map<string, Groupe>();
  private temps = 0;
  private readonly dernier = new THREE.Vector3(Infinity, 0, Infinity);
  private recalculeA = -Infinity;
  /** Qualité Basse : ni poissons ni libellules, moitié moins d'oiseaux. */
  basse = false;
  /** Pêcheurs à l'ouvrage (x, z, id) et poissons pris depuis moins d'un jour (x, y, z, cap), donnés à chaque image. */
  readonly pecheurs: number[] = [];
  readonly poissonsPris: number[] = [];

  constructor(private readonly relief: Relief, private readonly carte: () => Carte) {
    this.rendus = Array.from({ length: SORTES }, (_, s) => new Rendu(corps(s as S), this.mat, "decor-" + NOMS[s]));
    for (const r of this.rendus) this.groupe.add(r.maillage);
  }

  /** Sol ou surface de l'eau (m) et profondeur d'eau (m) en un point. */
  private eauEn(x: number, z: number): { surface: number; prof: number } {
    const s = this.relief.surface(x, z);
    return Number.isNaN(s.eau) ? { surface: s.sol, prof: 0 } : { surface: s.eau, prof: s.eau - s.sol };
  }

  private biome(x: number, z: number): number {
    const i = this.relief.caseEn(x, z);
    return i < 0 ? -1 : this.relief.biomes[i];
  }

  /** Une case au bord de la mer : la mer est à moins d'une case. */
  private cotiere(x: number, z: number): boolean {
    const m = this.relief.m;
    for (const [a, b] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) if (this.biome(x + a * m, z + b * m) === OCEAN) return true;
    return false;
  }

  /** Les groupes autour de la caméra, recalculés quand elle a bougé ou de temps en temps (jour, nuit, saison). */
  private recalculer(cam: THREE.Vector3, nuit: number): void {
    const neufs = new Map<string, Groupe>();
    const garder = (g: Groupe) => neufs.set(g.cle, (!g.cle.startsWith("p:") && this.groupes.get(g.cle)) || g);
    const carte = this.carte();
    const rel = this.relief;
    const jour = nuit < 0.6;
    const temperature = rel.temperatureEn(cam.x, cam.z);
    const chaud = rel.saison !== 3 && (temperature === null || temperature >= 14);
    for (let s = 0; s < SORTES; s++) {
      const sorte = s as S;
      if (this.basse && (sorte === S.Poisson || sorte === S.Libellule)) continue;
      if (!jour && (sorte === S.Pterosaure || sorte === S.Oiseau)) continue;
      if ((sorte === S.Libellule && (!chaud || nuit > 0.35))) continue;
      if (sorte === S.Plesiosaure) continue; // tirés avec les mosasaures (une même maille)
      const m = MAILLE[s];
      const portee = PORTEE[s] * (this.basse ? 0.7 : 1);
      const i0 = Math.floor((cam.x - portee) / m);
      const i1 = Math.floor((cam.x + portee) / m);
      const j0 = Math.floor((cam.z - portee) / m);
      const j1 = Math.floor((cam.z + portee) / m);
      for (let i = i0; i <= i1; i++) {
        for (let j = j0; j <= j1; j++) {
          const h = (k: number) => unite(hacher(i, j, 300 + s * 17 + k));
          const x = (i + 0.15 + 0.7 * h(1)) * m;
          const z = (j + 0.15 + 0.7 * h(2)) * m;
          if (Math.hypot(x - cam.x, z - cam.z) > portee) continue;
          const c = rel.caseEn(x, z);
          if (c < 0) continue;
          const b = rel.biomes[c];
          const brule = meteoGPU.bruleEn(c);
          const plantes = rel.plantes[c] / 255;
          const gibier = (carte.gibier?.[c] ?? 128) / 255;
          const g = this.tirer(sorte, `${s}:${i}:${j}`, x, z, b, brule, plantes, gibier, h);
          if (g) garder(g);
        }
      }
    }
    // Sous chaque pêcheur, un banc qui tourne (0.44.1).
    if (!this.basse) {
      for (let k = 0; k + 2 < this.pecheurs.length; k += 3) {
        const [x, z, id] = [this.pecheurs[k], this.pecheurs[k + 1], this.pecheurs[k + 2]];
        if (Math.hypot(x - cam.x, z - cam.z) > 150) continue;
        const e = this.eauEn(x, z);
        if (e.prof < 1) continue;
        const g = this.banc(`p:${id}`, x, z, e, (k2) => unite(hacher(id, k2, 331)), 1.2);
        if (g) garder(g);
      }
    }
    this.groupes = neufs;
  }

  /** Un groupe de cette sorte sur ce point, ou null (le terrain n'en porte pas, ou le tirage dit non). */
  private tirer(sorte: S, cle: string, x: number, z: number, b: number, brule: number, plantes: number, gibier: number, h: (k: number) => number): Groupe | null {
    switch (sorte) {
      case S.Poisson: {
        const e = this.eauEn(x, z);
        if (e.prof < 0.6) return null;
        const p = b === EAU_PEU_PROFONDE || b === MANGROVE ? 0.75 : b === LAC || b === RIVIERE ? 0.55 : b === OCEAN ? 0.3 : 0.4;
        if (h(3) > p) return null;
        return this.banc(cle, x, z, e, h, 1);
      }
      case S.Mosasaure: {
        if (b !== OCEAN || h(3) > 0.14) return null;
        const e = this.eauEn(x, z);
        if (e.prof < 15) return null;
        const plesio = h(4) < 0.45;
        const L = plesio ? 3 + 2 * h(5) : 4 + 3 * h(5);
        const g: Groupe = { cle, sorte: plesio ? S.Plesiosaure : S.Mosasaure, x, z, rayon: 60 + 90 * h(6), vitesse: 1.4 + 0.8 * h(7), sens: h(8) < 0.5 ? 1 : -1, base: 0, graine: h(9), membres: [] };
        g.membres.push(this.membre(0, 0, 0, h(10), L, 0.9 + 0.2 * h(11)));
        // Le cercle reste en eau profonde ; sinon on le resserre.
        return this.resserrer(g, 8) ? g : null;
      }
      case S.Pterosaure: {
        const cote = b === PLAGE || b === MANGROVE || b === EAU_PEU_PROFONDE || (b !== OCEAN && this.cotiere(x, z));
        const falaise = b === MONTAGNE || b === SOMMET;
        if (!cote && !falaise) return null;
        if (h(3) > 0.4 * (1 - brule) * (0.6 + 0.6 * gibier)) return null;
        const sol = Math.max(0, this.relief.hauteur(x, z));
        const g: Groupe = { cle, sorte, x, z, rayon: 30 + 50 * h(4), vitesse: 8 + 3 * h(5), sens: h(6) < 0.5 ? 1 : -1, base: sol + 25 + 45 * h(7), graine: h(8), membres: [] };
        const n = 1 + Math.floor(h(9) * 3);
        for (let k = 0; k < n; k++) g.membres.push(this.membre(0, 8 * k * (h(20 + k) - 0.3), 0, k / n, 1.6 + 2.2 * h(30 + k), 0.85 + 0.3 * h(40 + k)));
        return g;
      }
      case S.Oiseau: {
        const foret = b === FEUILLUS || b === CONIFERES || b === TROPICALE;
        const marais = b === MARAIS || b === MANGROVE || b === LAC;
        const f = foret ? 0.75 : marais ? 0.85 : b === PLAINE || b === RIVIERE ? 0.35 : b === PLAGE ? 0.2 : 0;
        if (h(3) > f * (0.3 + 0.7 * plantes) * (1 - brule) * (0.6 + 0.6 * gibier) * (this.basse ? 0.5 : 1)) return null;
        const sol = Math.max(0, this.relief.hauteur(x, z));
        const g: Groupe = { cle, sorte, x, z, rayon: 40 + 80 * h(4), vitesse: 8 + 4 * h(5), sens: 1, base: sol + (foret ? 30 + 15 * h(6) : 8 + 10 * h(6)), graine: h(7), membres: [] };
        const n = 5 + Math.floor(h(8) * 14);
        const taille = 0.3 + 0.35 * h(9);
        for (let k = 0; k < n; k++)
          g.membres.push(this.membre((h(20 + k) - 0.5) * 9, (h(40 + k) - 0.5) * 3, (h(60 + k) - 0.5) * 9, h(80 + k), taille * (0.85 + 0.3 * h(100 + k)), 0.8 + 0.4 * h(120 + k)));
        return g;
      }
      case S.Libellule: {
        const e = this.eauEn(x, z);
        if (e.prof <= 0.05 || b === OCEAN || b === EAU_PEU_PROFONDE || h(3) > 0.45) return null;
        const g: Groupe = { cle, sorte, x, z, rayon: 2.5, vitesse: 0, sens: 1, base: e.surface, graine: h(4), membres: [] };
        const n = 1 + Math.floor(h(5) * 2);
        for (let k = 0; k < n; k++) g.membres.push(this.membre(0, 0, 0, h(10 + k), 0.12 + 0.08 * h(20 + k), 0.8 + 0.5 * h(30 + k)));
        return g;
      }
      default:
        return null;
    }
  }

  private membre(dx: number, dy: number, dz: number, phase: number, echelle: number, teinte: number): Membre {
    return { dx, dy, dz, phase, echelle, teinte, x: NaN, y: 0, z: 0, cap: 0 };
  }

  /** Un banc de poissons qui tourne dans l'eau, entre deux eaux. */
  private banc(cle: string, x: number, z: number, e: { surface: number; prof: number }, h: (k: number) => number, plus: number): Groupe | null {
    const gros = h(4) < 0.25;
    const n = Math.round((gros ? 3 + h(5) * 4 : 8 + h(5) * 18) * plus);
    const taille = gros ? 0.45 + 0.45 * h(6) : 0.14 + 0.2 * h(6);
    const g: Groupe = { cle, sorte: S.Poisson, x, z, rayon: 3 + 6 * h(7), vitesse: 0.4 + 0.5 * h(8), sens: h(9) < 0.5 ? 1 : -1, base: e.surface - Math.min(0.25 + 0.3 * e.prof, 1.4), graine: h(10), membres: [] };
    const etendue = gros ? 2 : 1.2;
    for (let k = 0; k < n; k++)
      g.membres.push(this.membre((h(20 + k) - 0.5) * etendue, (h(50 + k) - 0.5) * 0.4, (h(80 + k) - 0.5) * etendue * 1.6, h(110 + k), taille * (0.85 + 0.3 * h(140 + k)), 0.85 + 0.3 * h(170 + k)));
    return this.resserrer(g, 0.5) ? g : null;
  }

  /** Resserre la ronde tant qu'elle passe là où l'eau est moins profonde que `fond` ; faux si même immobile ça ne va pas. */
  private resserrer(g: Groupe, fond: number): boolean {
    for (const f of [1, 0.6, 0.35, 0]) {
      const r = g.rayon * f;
      let bon = true;
      for (let k = 0; k < 8 && bon; k++) bon = this.eauEn(g.x + Math.cos((k / 8) * DEUX_PI) * r, g.z + Math.sin((k / 8) * DEUX_PI) * r).prof >= fond;
      if (bon) {
        g.rayon = r;
        return true;
      }
    }
    return false;
  }

  // ——— Chaque image ———

  animer(dt: number, cam: THREE.Vector3, nuit: number): void {
    this.temps += dt;
    const ts = this.temps;
    if (Math.hypot(cam.x - this.dernier.x, cam.z - this.dernier.z) > 20 || ts - this.recalculeA > 3) {
      this.recalculer(cam, nuit);
      this.dernier.copy(cam);
      this.recalculeA = ts;
    }
    for (const r of this.rendus) r.n = 0;
    for (const g of this.groupes.values()) {
      const r = this.rendus[g.sorte];
      switch (g.sorte) {
        case S.Poisson: {
          const w = g.rayon > 0.1 ? (g.vitesse / g.rayon) * g.sens : 0;
          const th = g.graine * DEUX_PI + ts * w;
          const cx = g.x + Math.cos(th) * g.rayon;
          const cz = g.z + Math.sin(th) * g.rayon;
          const cap = w !== 0 ? regarde(-Math.sin(th) * g.sens, Math.cos(th) * g.sens) : g.graine * DEUX_PI + Math.sin(ts * 0.2) * 0.5;
          const [sx, sz] = [Math.sin(cap), Math.cos(cap)];
          for (const b of g.membres) {
            const ox = b.dx + Math.sin(ts * 0.7 + b.phase * 9) * 0.25;
            const oz = b.dz + Math.cos(ts * 0.5 + b.phase * 7) * 0.25;
            b.x = cx + ox * sz + oz * sx;
            b.z = cz - ox * sx + oz * sz;
            b.y = g.base + b.dy;
            b.cap = cap + Math.sin(ts * 0.9 + b.phase * 5) * 0.25;
            r.ajouter(b.x, b.y, b.z, b.cap, b.echelle, ts * (3 + g.vitesse * 6 / b.echelle) + b.phase * DEUX_PI, 0, 0, 0, 0.08, b.teinte);
          }
          break;
        }
        case S.Mosasaure:
        case S.Plesiosaure: {
          const w = (g.vitesse / Math.max(g.rayon, 5)) * g.sens;
          const th = g.graine * DEUX_PI + ts * w;
          const b = g.membres[0];
          // Une remontée pour respirer toutes les une à deux minutes, une quinzaine de secondes en surface.
          const periode = 70 + 50 * g.graine;
          const u = (ts / periode + g.graine * 3) % 1;
          const plonge = smooth(0.18, 0.3, u) * (1 - smooth(0.85, 1, u));
          b.x = g.x + Math.cos(th) * g.rayon;
          b.z = g.z + Math.sin(th) * g.rayon;
          b.y = -0.06 * b.echelle - 6 * plonge;
          b.cap = regarde(-Math.sin(th) * g.sens, Math.cos(th) * g.sens);
          const pente = (smooth(0.18, 0.3, u + 0.01) - smooth(0.18, 0.3, u)) - (smooth(0.85, 1, u + 0.01) - smooth(0.85, 1, u));
          const plesio = g.sorte === S.Plesiosaure;
          r.ajouter(b.x, b.y, b.z, b.cap, b.echelle, ts * (plesio ? 1.3 : 1.8) + b.phase * DEUX_PI, plesio ? 0.45 : 0.2, -pente * 30, 0,
            plesio ? 0.015 : 0.06, b.teinte);
          break;
        }
        case S.Pterosaure: {
          const w = (g.vitesse / g.rayon) * g.sens;
          // Le point de la ronde dérive lentement (une ascendance qui bouge avec le vent).
          const ax = g.x + 120 * Math.sin(ts * 0.004 + g.graine * 20);
          const az = g.z + 120 * Math.cos(ts * 0.005 + g.graine * 30);
          for (const b of g.membres) {
            const th = (g.graine + b.phase) * DEUX_PI + ts * w;
            b.x = ax + Math.cos(th) * (g.rayon + b.dy * 0.5);
            b.z = az + Math.sin(th) * (g.rayon + b.dy * 0.5);
            b.y = g.base + b.dy + 4 * Math.sin(ts * 0.05 + b.phase * 6);
            b.cap = regarde(-Math.sin(th) * g.sens, Math.cos(th) * g.sens);
            // Penché vers l'intérieur de la ronde.
            const roulis = this.penche(b.cap, ax - b.x, az - b.z, 0.35);
            // Plane presque toujours ; quelques battements de temps en temps pour reprendre de la hauteur.
            const bat = Math.sin(ts * 0.09 + b.phase * 11 + g.graine * 5) > 0.82 ? 0.5 : 0.03;
            r.ajouter(b.x, b.y, b.z, b.cap, b.echelle, ts * (9 / Math.sqrt(b.echelle)) + b.phase * DEUX_PI, bat, 0.04, roulis, 0, b.teinte);
          }
          break;
        }
        case S.Oiseau: {
          // Le vol suit une boucle (courbe de Lissajous) au-dessus de son coin.
          const w1 = g.vitesse / g.rayon;
          const w2 = w1 * 0.73;
          const f1 = g.graine * 40;
          const f2 = g.graine * 70;
          const cx = g.x + g.rayon * Math.sin(ts * w1 + f1);
          const cz = g.z + g.rayon * 0.8 * Math.sin(ts * w2 + f2);
          const vx = g.rayon * w1 * Math.cos(ts * w1 + f1);
          const vz = g.rayon * 0.8 * w2 * Math.cos(ts * w2 + f2);
          const ax = -g.rayon * w1 * w1 * Math.sin(ts * w1 + f1);
          const az = -g.rayon * 0.8 * w2 * w2 * Math.sin(ts * w2 + f2);
          const cap = regarde(vx, vz);
          const [sx, sz] = [Math.sin(cap), Math.cos(cap)];
          const roulis = this.penche(cap, ax, az, Math.min(0.7, Math.hypot(ax, az) * 0.12));
          for (const b of g.membres) {
            const ox = b.dx + Math.sin(ts * 0.6 + b.phase * 9);
            const oz = b.dz + Math.cos(ts * 0.5 + b.phase * 7);
            b.x = cx + ox * sz + oz * sx;
            b.z = cz - ox * sx + oz * sz;
            b.y = g.base + b.dy + 1.5 * Math.sin(ts * 1.1 + b.phase * 6);
            b.cap = cap;
            // Vol ondulé : quelques battements, puis ailes repliées un instant.
            const bat = Math.sin(ts * 1.7 + b.phase * 13) > -0.4 ? 0.85 : 0.08;
            r.ajouter(b.x, b.y, b.z, cap, b.echelle, ts * 17 + b.phase * DEUX_PI, bat, 0, roulis, 0, b.teinte);
          }
          break;
        }
        case S.Libellule: {
          for (const b of g.membres) {
            // Vols brefs d'un point à l'autre au-dessus de l'eau, puis du sur-place.
            const T = 2.2 + b.phase * 1.5;
            const v = ts / T + b.phase * 10;
            const k = Math.floor(v);
            const f = smooth(0, 0.3, v - k);
            const pt = (n: number): [number, number, number] => {
              const a = unite(hacher(n, Math.round(g.graine * 1e6), 401)) * DEUX_PI;
              const d = unite(hacher(n, 7, 409)) * g.rayon;
              return [g.x + Math.cos(a) * d, g.base + 0.35 + 0.9 * unite(hacher(n, 9, 419)), g.z + Math.sin(a) * d];
            };
            const p0 = pt(k - 1);
            const p1 = pt(k);
            b.x = p0[0] + (p1[0] - p0[0]) * f + Math.sin(ts * 7 + b.phase) * 0.03;
            b.y = p0[1] + (p1[1] - p0[1]) * f + Math.sin(ts * 5 + b.phase * 3) * 0.04;
            b.z = p0[2] + (p1[2] - p0[2]) * f;
            b.cap = regarde(p1[0] - p0[0], p1[2] - p0[2]);
            r.ajouter(b.x, b.y, b.z, b.cap, b.echelle, ts * 70 + b.phase * DEUX_PI, 0.35, 0, 0, 0, b.teinte);
          }
          break;
        }
      }
    }
    // Poissons pris : couchés sur le flanc près du pêcheur, un soubresaut de temps en temps.
    const rp = this.rendus[S.Poisson];
    for (let k = 0; k + 3 < this.poissonsPris.length; k += 4) {
      const [x, y, z, cap] = [this.poissonsPris[k], this.poissonsPris[k + 1], this.poissonsPris[k + 2], this.poissonsPris[k + 3]];
      const graine = unite(hacher(Math.round(x * 10), Math.round(z * 10), 433));
      const sursaut = Math.max(0, Math.sin(ts * 0.7 + graine * 30) - 0.93) * 3;
      rp.ajouter(x, y + 0.04, z, cap + 1.2, 0.45 + 0.3 * graine, ts * 14, 0, 0, Math.PI / 2, sursaut, 0.95);
    }
    for (const r of this.rendus) r.envoyer();
  }

  /** Roulis (rad) qui penche vers (vx, vz) : l'aile du côté de l'intérieur du virage descend. */
  private penche(cap: number, vx: number, vz: number, ampleur: number): number {
    const cote = Math.cos(cap) * vx - Math.sin(cap) * vz;
    return cote > 0 ? -ampleur : cote < 0 ? ampleur : 0;
  }

  // ——— Voir, suivre ———

  private *toutes(): Generator<[string, Groupe, Membre]> {
    for (const g of this.groupes.values()) for (let k = 0; k < g.membres.length; k++) if (!Number.isNaN(g.membres[k].x)) yield [`d:${g.cle}:${k}`, g, g.membres[k]];
  }

  /** La bête du décor sous le rayon (le plus près), ou null. */
  sous(origine: THREE.Vector3, direction: THREE.Vector3, max = 600): { cle: string; distance: number } | null {
    let meilleur = max;
    let cle: string | null = null;
    const c = new THREE.Vector3();
    for (const [k, , b] of this.toutes()) {
      c.set(b.x, b.y, b.z).sub(origine);
      const d = c.dot(direction);
      if (d <= 0 || d > meilleur) continue;
      const ecart = c.addScaledVector(direction, -d).length();
      if (ecart < Math.max(b.echelle * 0.5, d * 0.012)) [meilleur, cle] = [d, k];
    }
    return cle ? { cle, distance: meilleur } : null;
  }

  /** La bête du décor la plus proche devant soi (à moins de 60 m, dans un cône d'environ 60°). */
  devant(camera: THREE.Vector3, avant: THREE.Vector3): { cle: string; distance: number } | null {
    let meilleur = 60;
    let cle: string | null = null;
    for (const [k, , b] of this.toutes()) {
      const vx = b.x - camera.x;
      const vy = b.y - camera.y;
      const vz = b.z - camera.z;
      const d = Math.hypot(vx, vy, vz);
      if (d < meilleur && vx * avant.x + vy * avant.y + vz * avant.z > d * 0.5) [meilleur, cle] = [d, k];
    }
    return cle ? { cle, distance: meilleur } : null;
  }

  private trouver(cle: string): [Groupe, Membre] | null {
    const i = cle.lastIndexOf(":");
    const g = this.groupes.get(cle.slice(2, i));
    const b = g?.membres[Number(cle.slice(i + 1))];
    return g && b && !Number.isNaN(b.x) ? [g, b] : null;
  }

  etat(cle: string): { centre: THREE.Vector3; cap: number; longueur: number; groupe: number; espece: number; enMarche: boolean } | null {
    const r = this.trouver(cle);
    if (!r) return null;
    const [, b] = r;
    return { centre: new THREE.Vector3(b.x, b.y, b.z), cap: b.cap, longueur: Math.max(b.echelle, 0.3), groupe: 0, espece: -1, enMarche: true };
  }

  /** Petite fiche d'une bête du décor : son nom, sa taille, ce qu'elle fait, et qu'elle n'est pas comptée par la simulation. */
  fiche(cle: string): { titre: string; lignes: string[] } | null {
    const r = this.trouver(cle);
    if (!r) return null;
    const [g, b] = r;
    const nom = NOMS[g.sorte];
    const taille = Math.round(b.echelle * (b.echelle < 1 ? 100 : 10)) / (b.echelle < 1 ? 100 : 10);
    return {
      titre: t(`trois.decor.${nom}`),
      lignes: [
        t(VOLANT[g.sorte] ? "trois.decor.envergure" : "trois.decor.longueur", { m: taille.toLocaleString() }),
        t(`trois.decor.${nom}.vie`),
        g.membres.length > 1 ? t("trois.decor.groupe", { n: g.membres.length }) : t("trois.decor.seul"),
        t("trois.decor.horsSimulation"),
      ],
    };
  }

  /** Nombre de bêtes du décor montrées à la dernière image (compteur, touche I). */
  get montrees(): number {
    return this.rendus.reduce((s, r) => s + r.n, 0);
  }
}

function smooth(a: number, b: number, x: number): number {
  const u = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return u * u * (3 - 2 * u);
}
