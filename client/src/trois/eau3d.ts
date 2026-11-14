import * as THREE from "three/webgpu";
import {
  Fn, abs, cameraPosition, clamp, dot, exp, float, length, max, mix, normalize, positionLocal, positionWorld, pow, sin, smoothstep,
  texture, time, transformNormalToView, uniform, vec2, vec3, vec4,
} from "three/tsl";
import { Bruit } from "./bruit";
import type { Relief } from "./relief";
import type { Vent } from "./matieres";

/**
 * L'eau de la vue 3D (0.41.5, proposition D1) : reflets du ciel selon l'angle (Fresnel), vagues (houle qui soulève la mer près de
 * soi, rides qui courent avec le vent), transparence au bord où l'on voit le fond, couleur qui fonce avec la profondeur, écume
 * sur le rivage et les crêtes. La profondeur vient d'une carte des fonds autour de la caméra (fine sur 1 km, grossière sur 16 km),
 * calculée peu à peu depuis le relief.
 */

const FIN = 256;
const COTE_FIN = 1024;
const GROS = 128;
const COTE_GROS = 16384;

/** Carte des hauteurs du sol autour d'un point, refaite par lignes quand la caméra s'éloigne de son centre. */
class CarteFonds {
  readonly tex: THREE.DataTexture;
  readonly origine = uniform(new THREE.Vector2(-1e9, -1e9));
  private readonly calcul: Float32Array;
  private readonly donnees: Uint16Array;
  private ligne = -1;
  private ox = 0;
  private oz = 0;
  private pret = false;

  constructor(
    private readonly n: number,
    readonly taille: number,
    private readonly hauteur: (x: number, z: number) => number,
  ) {
    this.calcul = new Float32Array(n * n);
    this.donnees = new Uint16Array(n * n);
    this.tex = new THREE.DataTexture(this.donnees, n, n, THREE.RedFormat, THREE.HalfFloatType);
    this.tex.magFilter = THREE.LinearFilter;
    this.tex.minFilter = THREE.LinearFilter;
    this.tex.wrapS = this.tex.wrapT = THREE.ClampToEdgeWrapping;
    this.tex.colorSpace = THREE.NoColorSpace;
    this.tex.needsUpdate = true;
  }

  /** Avance le calcul (budget en ms) ; recommence autour de la caméra quand elle a passé le quart de la carte. */
  avancer(cx: number, cz: number, budget: number): void {
    const u = this.origine.value as THREE.Vector2;
    const loin = Math.abs(cx - (u.x + this.taille / 2)) > this.taille / 4 || Math.abs(cz - (u.y + this.taille / 2)) > this.taille / 4;
    if (this.ligne < 0) {
      if (!loin && this.pret) return;
      const pas = this.taille / this.n;
      this.ox = Math.round((cx - this.taille / 2) / pas) * pas;
      this.oz = Math.round((cz - this.taille / 2) / pas) * pas;
      this.ligne = 0;
    }
    const debut = performance.now();
    const pas = this.taille / this.n;
    while (this.ligne < this.n && performance.now() - debut < budget) {
      const j = this.ligne;
      for (let i = 0; i < this.n; i++) this.calcul[j * this.n + i] = this.hauteur(this.ox + (i + 0.5) * pas, this.oz + (j + 0.5) * pas);
      this.ligne++;
    }
    if (this.ligne >= this.n) {
      for (let k = 0; k < this.calcul.length; k++) this.donnees[k] = THREE.DataUtils.toHalfFloat(Math.max(-60000, Math.min(60000, this.calcul[k])));
      this.tex.needsUpdate = true;
      u.set(this.ox, this.oz);
      this.ligne = -1;
      this.pret = true;
    }
  }
}

/** Texture d'écume (bruit cellulaire et fractal), périodique. */
function textureEcume(): THREE.DataTexture {
  const n = 256;
  const b = new Bruit(777);
  const data = new Uint8Array(n * n * 4);
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      const fx = x / n;
      const fy = y / n;
      const e = (u: number, v: number) => b.fbm(u * 64, v * 64, 6, 4);
      const v = e(fx, fy) * (1 - fx) * (1 - fy) + e(fx - 1, fy) * fx * (1 - fy) + e(fx, fy - 1) * (1 - fx) * fy + e(fx - 1, fy - 1) * fx * fy;
      // Filaments : là où le bruit passe près de zéro.
      const filament = Math.max(0, 1 - Math.abs(v) * 7);
      const g = Math.round(255 * Math.min(1, filament * filament + Math.max(0, v) * 0.6));
      data.set([g, g, g, 255], (y * n + x) * 4);
    }
  }
  const tex = new THREE.DataTexture(data, n, n);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.magFilter = THREE.LinearFilter;
  tex.needsUpdate = true;
  return tex;
}

export class Eau {
  private readonly fin: CarteFonds;
  private readonly gros: CarteFonds;
  private readonly ecume = textureEcume();

  constructor(
    relief: Relief,
    private readonly vagues: THREE.Texture,
    private readonly reflets: THREE.Texture,
    private readonly vent: Vent,
  ) {
    this.fin = new CarteFonds(FIN, COTE_FIN, (x, z) => relief.hauteur(x, z));
    this.gros = new CarteFonds(GROS, COTE_GROS, (x, z) => relief.hauteurDeBase(x, z));
  }

  /** À chaque image : la carte des fonds suit la caméra. */
  mettreAJour(camera: THREE.Vector3): void {
    this.gros.avancer(camera.x, camera.z, 1);
    this.fin.avancer(camera.x, camera.z, 2.5);
  }

  /** Hauteur du sol sous un point (m), d'après les cartes des fonds : la fine près de soi, la grossière plus loin. */
  private fond(xz: THREE.Node<"vec2">): THREE.Node<"float"> {
    const uf = xz.sub(this.fin.origine).div(this.fin.taille);
    const ug = xz.sub(this.gros.origine).div(this.gros.taille);
    const hf = texture(this.fin.tex, uf).level(float(0)).r;
    const hg = texture(this.gros.tex, ug).level(float(0)).r;
    const bordF = max(abs(uf.x.sub(0.5)), abs(uf.y.sub(0.5)));
    const bordG = max(abs(ug.x.sub(0.5)), abs(ug.y.sub(0.5)));
    const loin = float(-40);
    const h = mix(hg, loin, smoothstep(0.45, 0.5, bordG));
    return mix(hf, h, smoothstep(0.42, 0.49, bordF));
  }

  /**
   * Matériau de l'eau. `mer` : houle qui soulève la surface (la géométrie doit être fine près de la caméra), rides plus fortes ;
   * sinon eau douce : rides fines, courant plus vif.
   */
  materiau(mer: boolean): THREE.MeshStandardNodeMaterial {
    const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, transparent: true, depthWrite: false });
    mat.envMap = this.reflets;
    const v = this.vent.u;
    const force = length(v);
    const dir = v.div(max(force, 1e-3));
    const t = time;

    // Houle : trois trains d'ondes, plus hauts avec le vent, amortis par le fond près du rivage.
    const houle = (xz: THREE.Node<"vec2">, prof: THREE.Node<"float">) => {
      const a = force.mul(0.45).add(0.12).mul(clamp(prof.div(4), 0, 1));
      const k1 = dot(xz, dir).mul(0.11).sub(t.mul(1.04));
      const k2 = dot(xz, vec2(dir.y.negate(), dir.x).mul(0.45).add(dir)).mul(0.19).sub(t.mul(1.36));
      const k3 = dot(xz, vec2(dir.y, dir.x.negate()).mul(0.7).add(dir)).mul(0.31).sub(t.mul(1.74));
      return sin(k1).mul(0.55).add(sin(k2).mul(0.3)).add(sin(k3).mul(0.15)).mul(a);
    };
    if (mer) {
      mat.positionNode = Fn(() => {
        const p = positionLocal;
        const xz = p.xz.add(this.origineMer.xy);
        const prof = this.fond(xz).negate();
        return vec3(p.x, p.y.add(houle(xz, prof)), p.z);
      })();
    }

    const xz = positionWorld.xz;
    const prof = max(positionWorld.y.sub(this.fond(xz)), 0);
    // Rides : deux échelles qui courent avec le vent (ou le courant), plus marquées quand il souffle.
    const vitesse = mer ? 0.6 : 1.4;
    const r1 = texture(this.vagues, xz.div(mer ? 23 : 9).add(dir.mul(t.mul(vitesse * 0.035)))).xyz.mul(2).sub(1);
    const r2 = texture(this.vagues, vec2(xz.y, xz.x.negate()).div(mer ? 7.3 : 3.1).add(dir.mul(t.mul(vitesse * 0.06)))).xyz.mul(2).sub(1);
    const intensite = force.mul(mer ? 1.1 : 0.5).add(mer ? 0.35 : 0.2);
    const pente = r1.xy.add(r2.xy.mul(0.6)).mul(intensite);
    const ecumeTex = texture(this.ecume, xz.div(6.5).add(dir.mul(t.mul(0.03)))).r.mul(texture(this.ecume, xz.div(2.7).sub(dir.mul(t.mul(0.05)))).r.add(0.4));
    // Écume : sur le rivage (là où l'eau est mince, elle va et vient), et sur les crêtes par grand vent en mer.
    const rivage = smoothstep(0.9, 0.05, prof).mul(mer ? sin(prof.mul(9).sub(t.mul(1.6))).mul(0.35).add(0.65) : float(0.5));
    const cretes = mer ? smoothstep(0.55, 1.0, r1.x.add(r2.y).mul(0.5).add(force.mul(0.5))).mul(force).mul(smoothstep(1, 4, prof)) : float(0);
    const ecume = clamp(rivage.add(cretes).mul(ecumeTex).mul(1.6), 0, 1);
    const nMonde = normalize(vec3(pente.x.negate(), 1, pente.y.negate()).mul(vec3(1, 1, 1)));
    const nPlat = mix(nMonde, vec3(0, 1, 0), ecume.mul(0.7));
    mat.normalNode = transformNormalToView(nPlat);
    // Couleur de l'eau : turquoise clair où elle est mince, bleu-vert sombre en profondeur (absorption).
    const peu = mer ? vec3(0.05, 0.19, 0.17) : vec3(0.06, 0.13, 0.08);
    const profond = mer ? vec3(0.006, 0.03, 0.05) : vec3(0.01, 0.03, 0.025);
    const couleur = mix(peu, profond, float(1).sub(exp(prof.mul(mer ? -0.18 : -0.35))));
    mat.colorNode = vec4(mix(couleur, vec3(0.85, 0.88, 0.9), ecume), 1);
    mat.roughnessNode = mix(float(mer ? 0.05 : 0.07), float(0.65), ecume);
    // Transparence : on voit le fond où l'eau est mince ; les reflets (Fresnel) restent visibles même là.
    const vue = normalize(cameraPosition.sub(positionWorld));
    const fresnel = float(0.02).add(pow(float(1).sub(max(dot(nPlat, vue), 0)), 5).mul(0.98));
    const epaisseur = float(1).sub(exp(prof.mul(mer ? -0.9 : -1.4)));
    mat.opacityNode = clamp(max(max(epaisseur.mul(0.93).add(0.04), fresnel), ecume.mul(0.95)), 0, 1).mul(smoothstep(0, 0.04, prof).mul(0.8).add(0.2));
    return mat;
  }

  /** Position (x, z) du maillage de la mer, qui suit la caméra (pour que la houle se calcule en coordonnées du monde). */
  readonly origineMer = uniform(new THREE.Vector2());
}

/**
 * Maillage de la mer : des anneaux autour de la caméra, serrés près d'elle (la houle s'y voit), de plus en plus larges jusqu'à
 * l'horizon (800 km).
 */
export function maillageMer(): THREE.BufferGeometry {
  const secteurs = 160;
  const rayons: number[] = [0];
  let r = 0.6;
  while (r < 800000) {
    rayons.push(r);
    r *= r < 400 ? 1.045 : 1.12;
  }
  rayons.push(800000);
  const positions: number[] = [];
  const indices: number[] = [];
  positions.push(0, 0, 0);
  for (let k = 1; k < rayons.length; k++) {
    for (let s = 0; s < secteurs; s++) {
      const a = (s / secteurs) * Math.PI * 2;
      positions.push(Math.cos(a) * rayons[k], 0, Math.sin(a) * rayons[k]);
    }
  }
  for (let s = 0; s < secteurs; s++) indices.push(0, 1 + ((s + 1) % secteurs), 1 + s);
  for (let k = 1; k + 1 < rayons.length; k++) {
    const a0 = 1 + (k - 1) * secteurs;
    const b0 = 1 + k * secteurs;
    for (let s = 0; s < secteurs; s++) {
      const s1 = (s + 1) % secteurs;
      indices.push(a0 + s, a0 + s1, b0 + s, a0 + s1, b0 + s1, b0 + s);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  const n = new Float32Array(positions.length);
  for (let i = 1; i < n.length; i += 3) n[i] = 1;
  g.setAttribute("normal", new THREE.BufferAttribute(n, 3));
  g.setIndex(indices);
  return g;
}
