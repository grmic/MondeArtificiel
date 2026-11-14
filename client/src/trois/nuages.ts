import * as THREE from "three/webgpu";
import {
  Break, Fn, If, Loop, cameraPosition, clamp, dot, exp, float, frameId, interleavedGradientNoise, max, min, mix, screenCoordinate,
  select, smoothstep, texture, texture3D, vec2, vec3, vec4,
} from "three/tsl";
import { hacher, unite } from "./bruit";

/**
 * Nuages (0.41.6, proposition A1). Une même carte de couverture (bruit fractal périodique, poussée par le vent) donne :
 * - en Basse et Moyenne, une nappe peinte sur le ciel, comme avant ;
 * - en Haute et Ultra, des cumulus en volume, parcourus par marche de rayons (forme 3D, éclairage vers le soleil avec atténuation
 *   de Beer et effet « poudre », diffusion avant et arrière, lumière du ciel), d'après Schneider (« The real-time volumetric
 *   cloudscapes of Horizon Zero Dawn », 2015) ;
 * - partout, l'ombre des nuages sur le sol, qui court avec eux.
 */

/** Base et sommet de la couche de cumulus (m). */
export const BASE = 1500;
export const SOMMET = 3300;
/** Côté couvert par une répétition de la carte de couverture (m) ; une seconde lecture, tournée, casse la répétition. */
const COTE_COUVERTURE = 14000;
/** Côté d'une répétition du bruit 3D des formes (m). */
const COTE_FORME = 2600;
/** Extinction d'un nuage de densité 1 (par mètre). */
const EXTINCTION = 0.03;

const COTE_2D = 512;
const COTE_3D = 64;

/** Bruit de valeur 2D périodique (période `p` cellules), lissé (quintique). */
function valeur2D(x: number, y: number, p: number, graine: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const fx = x - xi;
  const fy = y - yi;
  const u = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
  const v = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
  const h = (a: number, b: number) => unite(hacher(((a % p) + p) % p, ((b % p) + p) % p, graine));
  const a = h(xi, yi);
  const b = h(xi + 1, yi);
  const c = h(xi, yi + 1);
  const d = h(xi + 1, yi + 1);
  return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
}

/** Carte de couverture : bruit fractal périodique (0 à 1), contrasté pour faire des amas séparés par du ciel clair. */
function textureCouverture(): THREE.DataTexture {
  const n = COTE_2D;
  const data = new Uint8Array(n * n);
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      let s = 0;
      let a = 0.5;
      let tot = 0;
      for (let o = 0, p = 6; o < 5; o++, p *= 2, a *= 0.5) {
        s += valeur2D((x / n) * p, (y / n) * p, p, 501 + o) * a;
        tot += a;
      }
      data[y * n + x] = Math.round(Math.min(1, Math.max(0, (s / tot - 0.5) * 1.7 + 0.5)) * 255);
    }
  }
  const tex = new THREE.DataTexture(data, n, n, THREE.RedFormat, THREE.UnsignedByteType);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.magFilter = THREE.LinearFilter;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.generateMipmaps = true;
  tex.colorSpace = THREE.NoColorSpace;
  tex.needsUpdate = true;
  return tex;
}

/** Bruit de Worley 3D périodique (1 − distance au point le plus proche), `c` cellules par côté. */
function worley(x: number, y: number, z: number, c: number, graine: number): number {
  const xi = Math.floor(x);
  const yi = Math.floor(y);
  const zi = Math.floor(z);
  let best = 9;
  for (let dz = -1; dz <= 1; dz++) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
    const cx = xi + dx;
    const cy = yi + dy;
    const cz = zi + dz;
    const w = (k: number) => ((k % c) + c) % c;
    const h = hacher(w(cx) + w(cy) * 97, w(cz), graine);
    const px = cx + unite(h);
    const py = cy + unite(hacher(h, 1));
    const pz = cz + unite(hacher(h, 2));
    const d = (px - x) ** 2 + (py - y) ** 2 + (pz - z) ** 2;
    if (d < best) best = d;
  }
  return 1 - Math.min(1, Math.sqrt(best));
}

/** Formes des nuages : Worley à trois fréquences (les bosses des cumulus), dans le canal rouge ; détail fin dans le vert. */
function textureForme(): THREE.Data3DTexture {
  const n = COTE_3D;
  const data = new Uint8Array(n * n * n * 2);
  for (let z = 0; z < n; z++) {
    for (let y = 0; y < n; y++) {
      for (let x = 0; x < n; x++) {
        const u = x / n;
        const v = y / n;
        const w = z / n;
        const f = worley(u * 4, v * 4, w * 4, 4, 11) * 0.625 + worley(u * 8, v * 8, w * 8, 8, 12) * 0.25 + worley(u * 16, v * 16, w * 16, 16, 13) * 0.125;
        const d = worley(u * 24, v * 24, w * 24, 24, 14);
        const k = (z * n * n + y * n + x) * 2;
        data[k] = Math.round(f * 255);
        data[k + 1] = Math.round(d * 255);
      }
    }
  }
  const tex = new THREE.Data3DTexture(data, n, n, n);
  tex.format = THREE.RGFormat;
  tex.type = THREE.UnsignedByteType;
  tex.wrapS = tex.wrapT = tex.wrapR = THREE.RepeatWrapping;
  tex.magFilter = THREE.LinearFilter;
  tex.minFilter = THREE.LinearFilter;
  tex.generateMipmaps = false;
  tex.colorSpace = THREE.NoColorSpace;
  tex.needsUpdate = true;
  return tex;
}

type N<T extends string> = THREE.Node<T>;

/** Phase de Henyey-Greenstein. */
const hg = (mu: N<"float">, g: number) => float((1 - g * g) / (4 * Math.PI)).div(float(1 + g * g).sub(mu.mul(2 * g)).pow(1.5));

export class Nuages {
  private readonly couvertureTex = textureCouverture();
  private formeTex: THREE.Data3DTexture | null = null;
  constructor(
    private readonly vent: N<"vec2">,
    private readonly couverture: N<"float">,
    private readonly soleil: N<"vec3">,
    private readonly couleurSoleil: N<"vec3">,
    private readonly ambiant: N<"vec3">,
  ) {}

  /** Le bruit 3D ne se calcule que si on en a besoin (Haute et Ultra), une seule fois (un quart de seconde environ). */
  private forme(): THREE.Data3DTexture {
    if (!this.formeTex) this.formeTex = textureForme();
    return this.formeTex;
  }

  /** Couverture en (x, z) du monde, de 0 (ciel clair) à 1 (cœur d'un amas), selon la couverture du jour. */
  amas(xz: N<"vec2">, sansMip = false): N<"float"> {
    const p = xz.add(this.vent);
    // Dans la marche de rayons (boucles et branches), WGSL n'admet que des lectures à niveau explicite.
    const lire = (uv: N<"vec2">) => (sansMip ? texture(this.couvertureTex, uv).level(float(0)) : texture(this.couvertureTex, uv)).r;
    const a = lire(p.div(COTE_COUVERTURE));
    const b = lire(vec2(p.y.mul(0.8).add(p.x.mul(0.6)), p.x.mul(0.8).sub(p.y.mul(0.6))).div(COTE_COUVERTURE * 0.37));
    const n = a.mul(0.7).add(b.mul(0.3));
    const seuil = float(1).sub(this.couverture);
    return smoothstep(seuil, seuil.add(0.3), n);
  }

  /** Densité des nuages au point p (m, monde). */
  densite(p: N<"vec3">, detail: boolean, amas?: N<"float">): N<"float"> {
    const h = clamp(p.y.sub(BASE).div(SOMMET - BASE), 0, 1);
    const c = amas ?? this.amas(p.xz, true);
    // Profil d'un cumulus : base plate, sommet arrondi qui monte avec la couverture locale.
    // Le sommet monte avec la couverture locale ; la racine arrondit le dôme au lieu d'en faire une pointe.
    const sommet = c.sqrt().mul(0.85).add(0.15);
    const profil = smoothstep(0, 0.08, h).mul(float(1).sub(h.div(sommet).clamp(0, 1).pow(3)));
    const q = p.add(vec3(this.vent.x, 0, this.vent.y)).div(COTE_FORME);
    const f = texture3D(this.forme(), q).level(float(0)).r;
    let d = clamp(c.mul(profil).sub(float(1).sub(f).mul(0.62)).div(0.38), 0, 1);
    if (detail) {
      // Érosion des bords : volutes fines, plus marquées vers le bas (bords effilochés) que vers les sommets bourgeonnants.
      const e = texture3D(this.forme(), q.mul(4.7)).level(float(0)).g;
      d = clamp(d.sub(float(1).sub(e).mul(mix(float(0.35), float(0.2), h)).mul(float(1).sub(d))).div(0.8), 0, 1);
    }
    return d;
  }

  /** Ombre des nuages au point p du sol (1 : en plein soleil), lue le long du rayon vers le soleil à mi-hauteur de la couche. */
  ombre(p: N<"vec3">): N<"float"> {
    const s = this.soleil;
    const t = float((BASE + SOMMET) / 2).sub(p.y).div(max(s.y, 0.08));
    const q = p.xz.add(s.xz.mul(t));
    const c = this.amas(q);
    return float(1).sub(smoothstep(0.05, 0.6, c).mul(0.72));
  }

  /** Nappe peinte (Basse, Moyenne) : couleur et opacité vues dans la direction d. */
  nappe(d: N<"vec3">, clair: N<"vec3">, sombre: N<"vec3">): N<"vec4"> {
    const dy = select(d.y.abs().lessThan(0.002), float(0.002), d.y);
    const t = float((BASE + SOMMET) / 2).sub(cameraPosition.y).div(dy);
    const xz = cameraPosition.xz.add(d.xz.mul(t));
    const c = this.amas(xz);
    const fondu = select(t.greaterThan(0), smoothstep(0.0, 0.1, d.y.abs()).mul(float(1).sub(smoothstep(15000, 45000, t))), float(0));
    const avant = max(dot(d, this.soleil), 0).pow(6).mul(0.6);
    const couleur = mix(clair, sombre, smoothstep(0.3, 1, c)).add(this.couleurSoleil.mul(avant.mul(0.08)));
    return vec4(couleur, smoothstep(0.02, 0.4, c).mul(fondu).mul(0.92));
  }

  /**
   * Cumulus en volume vus dans la direction d (Haute, Ultra) : marche de `pas` pas dans la couche, `pasLumiere` vers le soleil.
   * Rend la lumière diffusée (rgb) et l'opacité (a). Le départ des pas varie d'un pixel et d'une image à l'autre (le lissage
   * temporel fond le grain).
   */
  volume(d: N<"vec3">, pas: number, pasLumiere: number): N<"vec4"> {
    return Fn(() => {
      const o = cameraPosition;
      const dy = select(d.y.abs().lessThan(1e-4), float(1e-4), d.y);
      const tb = float(BASE).sub(o.y).div(dy);
      const ts = float(SOMMET).sub(o.y).div(dy);
      const t0 = max(min(tb, ts), 0).toVar();
      // Pas de plus de 260 m : au ras de la couche, on ne suit que les premiers kilomètres (le reste se perd dans l'air).
      const t1 = min(max(tb, ts), t0.add(Math.min(14000, pas * 260))).toVar();
      const lumiere = vec3(0).toVar();
      const transmis = float(1).toVar();
      const loin = float(1).sub(smoothstep(18000, 42000, t0));
      If(t1.greaterThan(t0).and(loin.greaterThan(0)), () => {
        const ds = t1.sub(t0).div(pas);
        const bruit = interleavedGradientNoise(screenCoordinate.xy.add(float(frameId).mod(64).mul(5.588)));
        const mu = dot(d, this.soleil);
        const phase = mix(hg(mu, 0.8), hg(mu, -0.25), 0.35);
        Loop(pas, ({ i }) => {
          const t = t0.add(float(i).add(bruit).mul(ds));
          const p = o.add(d.mul(t));
          // Le ciel clair coûte peu : la forme 3D et la lumière ne se calculent que dans un amas.
          const c = this.amas(p.xz, true);
          const dens = float(0).toVar();
          If(c.greaterThan(0.01), () => {
            dens.assign(this.densite(p, true, c));
          });
          If(dens.greaterThan(0.002), () => {
            // Vers le soleil : épaisseur traversée, pas croissants.
            const od = float(0).toVar();
            let l = 90;
            let s = 0;
            for (let k = 0; k < pasLumiere; k++) {
              s += l / 2;
              od.addAssign(this.densite(p.add(this.soleil.mul(s)), false).mul(l));
              s += l / 2;
              l *= 1.9;
            }
            const sigma = dens.mul(EXTINCTION);
            // Diffusions multiples dans le nuage (Wrenninge, 2013) : trois « octaves » de lumière de plus en plus diffuse, moins
            // atténuée et moins dirigée ; c'est ce qui rend blanc le cœur d'un cumulus.
            const versSoleil = exp(od.mul(-EXTINCTION)).mul(phase)
              .add(exp(od.mul(-EXTINCTION * 0.25)).mul(mix(phase, float(1 / (4 * Math.PI)), 0.5)).mul(0.5))
              .add(exp(od.mul(-EXTINCTION * 0.0625)).mul(1 / (4 * Math.PI)).mul(0.25));
            // Effet « poudre » : les bords tournés vers l'œil, peu remplis, sont plus sombres que l'intérieur éclairé.
            const poudre = float(1).sub(exp(dens.mul(-6))).mul(0.6).add(0.4);
            const h = clamp(p.y.sub(BASE).div(SOMMET - BASE), 0, 1);
            const direct = this.couleurSoleil.mul(versSoleil.mul(poudre).mul(3));
            const ciel = this.ambiant.mul(h.mul(0.6).add(0.4));
            const pasT = exp(sigma.mul(ds).negate());
            // Intégration analytique sur le pas (Hillaire 2016) : lumière diffusée × (1 − transmission du pas).
            lumiere.addAssign(direct.add(ciel).mul(transmis).mul(float(1).sub(pasT)));
            transmis.mulAssign(pasT);
          });
          If(transmis.lessThan(0.02), () => {
            Break();
          });
        });
      });
      const alpha = float(1).sub(transmis).mul(loin);
      return vec4(lumiere.mul(loin), alpha);
    })();
  }
}
