import * as THREE from "three/webgpu";
import {
  Fn, abs, asin, atan, cameraPosition, clamp, dot, exp, float, fog, max, min, mix, mx_fractal_noise_float, normalize, positionWorld,
  pow, select, sign, smoothstep, sqrt, texture, uniform, vec2, vec3,
} from "three/tsl";
import { ciel as diffusion, elevationDe, ligneDe, transmission, type Rgb } from "./atmosphere";
import { hacher, unite } from "./bruit";

/** Durée du jour par saison (heures), comme le calendrier de la simulation (Calendrier.DureeJour). */
const DUREE_JOUR = [12, 15, 12, 9];
/** Hauteur du soleil à midi selon la saison (degrés). */
const MIDI = [58, 72, 58, 42];

/** Texture du ciel : largeur (azimut) et hauteur (élévation, serrée près de l'horizon). */
const L = 96;
const H = 48;
/** Lignes refaites par image : le ciel entier est refait en huit images, sans à-coup. */
const LIGNES_PAR_IMAGE = 6;
/** Les valeurs sont gardées ×1000 dans une texture en demi-flottants (le ciel de nuit est très faible). */
const ECHELLE = 1000;
/**
 * Échelle des lumières : un soleil au zénith hors de l'atmosphère vaut S. Le sol, la lumière du ciel et le ciel lui-même sont
 * dans la même unité, si bien que le rendu des tons et l'exposition font le reste, comme un appareil photo.
 */
const S = 3.8;
/** Lune pleine par rapport au soleil (plus forte que la vraie, 1/400 000, pour qu'on voie la nuit après l'adaptation). */
const LUNE = 0.004;
/** Lueur du ciel nocturne (étoiles, lumière de l'air), en éclairement. */
const LUEUR_NUIT: Rgb = [0.0016, 0.002, 0.0034];
/** Part de lumière du ciel ajoutée par les diffusions multiples (le calcul n'en suit qu'une). */
const MULTIPLE = 2.1;
/** Altitude des nuages (m). */
const NUAGES = 2200;

const lum = (c: Rgb) => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];

/**
 * Ciel, soleil, lune, étoiles, nuages et brume (0.41.3, proposition C1). Le ciel est calculé selon l'atmosphère (atmosphere.ts) ;
 * le soleil se lève et se couche aux heures de la simulation (même durée du jour par saison), à l'est, passe au sud à midi et se
 * couche à l'ouest ; sa lumière rougit en traversant plus d'air. La lune éclaire la nuit et porte des ombres. La brume s'épaissit
 * au fond des vallées, surtout au petit matin, et prend la couleur du ciel à l'horizon dans chaque direction.
 */
export class Ciel {
  /** La lumière qui porte les ombres : le soleil le jour, la lune la nuit. */
  readonly astre = new THREE.DirectionalLight(0xffffff, 3);
  readonly hemi = new THREE.HemisphereLight(0xbfd6ff, 0x5a5040, 0.9);
  readonly direction = new THREE.Vector3();
  readonly directionLune = new THREE.Vector3();
  /** Clarté du moment (0 nuit, 1 plein jour), comme la simulation. */
  clarte = 1;
  /** Exposition visée (l'œil, ou l'appareil, s'adapte) : plus haute la nuit. */
  expositionVisee = 0.6;
  /** Éclairement total au sol (soleil, ciel, nuit), pour l'exposition. */
  eclairement = 1;
  /** Texture du ciel pour les reflets de l'eau (équirectangulaire classique). */
  readonly reflets: THREE.DataTexture;

  private readonly maillage: THREE.Mesh;
  private readonly etoiles: THREE.Points;
  private readonly materiauEtoiles: THREE.PointsMaterial;
  private readonly tex: THREE.DataTexture;
  private readonly calcul = new Float32Array(L * H * 3);
  private ligne = 0;
  private premiere = true;
  /** Soleil et lune du calcul en cours (figés pendant qu'on refait la texture). */
  private readonly soleilCalc: [number, number, number] = [0, 1, 0];
  private readonly luneCalc: [number, number, number] = [0, -1, 0];
  private hauteurCalc = 0;
  private cielSol: Rgb = [0.3, 0.35, 0.45];
  private readonly rgb: Rgb = [0, 0, 0];

  // Uniformes partagés par le ciel et la brume.
  private readonly gain = uniform(S / ECHELLE);
  private readonly uSoleil = uniform(new THREE.Vector3(0, 1, 0));
  private readonly uLune = uniform(new THREE.Vector3(0, -1, 0));
  private readonly uCouleurSoleil = uniform(new THREE.Vector3(1, 1, 1));
  private readonly uCouleurLune = uniform(new THREE.Vector3(0, 0, 0));
  private readonly uNuit = uniform(new THREE.Vector3(0, 0, 0));
  private readonly uCouverture = uniform(0.35);
  private readonly uVent = uniform(new THREE.Vector2());
  private readonly uNuageClair = uniform(new THREE.Vector3(1, 1, 1));
  private readonly uNuageOmbre = uniform(new THREE.Vector3(0.5, 0.5, 0.55));
  // Brume : voile général (d'après le niveau de la mer) et brume des fonds, posée sur le bas du relief alentour.
  private readonly uVoile = uniform(2.2e-5);
  private readonly uBrume = uniform(0.0004);
  private readonly uFond = uniform(0);
  private readonly uEpaisseur = uniform(1 / 70);

  constructor(scene: THREE.Scene) {
    this.tex = new THREE.DataTexture(new Uint16Array(L * H * 4), L, H, THREE.RGBAFormat, THREE.HalfFloatType);
    this.tex.wrapS = THREE.RepeatWrapping;
    this.tex.wrapT = THREE.ClampToEdgeWrapping;
    this.tex.magFilter = THREE.LinearFilter;
    this.tex.minFilter = THREE.LinearFilter;
    this.tex.generateMipmaps = false;
    this.tex.colorSpace = THREE.NoColorSpace;
    this.reflets = new THREE.DataTexture(new Uint16Array(64 * 32 * 4), 64, 32, THREE.RGBAFormat, THREE.HalfFloatType);
    this.reflets.mapping = THREE.EquirectangularReflectionMapping;
    this.reflets.magFilter = THREE.LinearFilter;
    this.reflets.minFilter = THREE.LinearFilter;
    this.reflets.generateMipmaps = false;
    this.reflets.colorSpace = THREE.NoColorSpace;

    const cielDans = Fn(([d]: [THREE.Node<"vec3">]) => {
      const u = atan(d.z, d.x).div(Math.PI * 2).add(0.5);
      const e = asin(clamp(d.y, -1, 1));
      const v = sign(e).mul(sqrt(min(abs(e).div(Math.PI / 2), 1))).mul(0.5).add(0.5);
      return texture(this.tex, vec2(u, v)).rgb.mul(this.gain).add(this.uNuit);
    });

    // Le ciel : diffusion, disque du soleil (dont le halo naît du rendu), disque de la lune, nuages.
    const materiau = new THREE.MeshBasicNodeMaterial({ side: THREE.BackSide, depthWrite: false, fog: false });
    materiau.colorNode = Fn(() => {
      const d = normalize(positionWorld.sub(cameraPosition));
      const fond = cielDans(d).toVar();
      const cs = dot(d, this.uSoleil);
      const disque = smoothstep(0.99996, 0.999975, cs);
      fond.addAssign(this.uCouleurSoleil.mul(disque.mul(30)));
      const cl = dot(d, this.uLune);
      const lune = smoothstep(0.99996, 0.999972, cl);
      fond.addAssign(this.uCouleurLune.mul(lune));
      // Nuages : une couche vers 2 200 m, d'un bruit fractal qui avance avec le vent ; éclairés par le soleil, plus clairs vers lui.
      const dy = select(abs(d.y).lessThan(0.002), float(0.002).mul(sign(d.y).add(0.0001)), d.y);
      const t = float(NUAGES).sub(cameraPosition.y).div(dy);
      const p = cameraPosition.xz.add(d.xz.mul(t)).mul(0.00022).add(this.uVent);
      const n = mx_fractal_noise_float(vec3(p.x, p.y, this.uVent.x.mul(0.3)), 5, 2.0, 0.5, 1.0).mul(0.5).add(0.5);
      const seuil = float(1).sub(this.uCouverture);
      const couvre = smoothstep(seuil, seuil.add(0.28), n);
      const fondu = select(t.greaterThan(0), smoothstep(0.0, 0.1, abs(d.y)).mul(float(1).sub(smoothstep(15000, 45000, t))), float(0));
      const epais = smoothstep(seuil, seuil.add(0.6), n);
      const avant = pow(max(cs, 0), 6).mul(0.6);
      const couleur = mix(this.uNuageClair, this.uNuageOmbre, epais).add(this.uCouleurSoleil.mul(avant.mul(this.gain).mul(ECHELLE * 0.08)));
      // Au loin, les nuages se fondent dans la couleur du ciel.
      const loin = smoothstep(4000, 40000, t);
      return mix(fond, mix(couleur, fond, loin.mul(0.6)), couvre.mul(fondu).mul(0.92));
    })();
    this.maillage = new THREE.Mesh(new THREE.SphereGeometry(1, 48, 24), materiau);
    this.maillage.scale.setScalar(450000);
    this.maillage.frustumCulled = false;
    this.maillage.renderOrder = -1;
    scene.add(this.maillage, this.astre, this.astre.target, this.hemi);

    const n = 2500;
    const pos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const u = unite(hacher(i, 1, 99)) * 2 - 1;
      const a = unite(hacher(i, 2, 99)) * Math.PI * 2;
      const r = Math.sqrt(1 - u * u);
      pos.set([Math.cos(a) * r * 300000, Math.abs(u) * 300000, Math.sin(a) * r * 300000], i * 3);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    this.materiauEtoiles = new THREE.PointsMaterial({ color: 0xffffff, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0, depthWrite: false, fog: false });
    this.etoiles = new THREE.Points(g, this.materiauEtoiles);
    this.etoiles.frustumCulled = false;
    scene.add(this.etoiles);

    // Brume intégrée le long du regard : densité a·exp(−b·(y − y0)) (Quílez, « better fog »), deux couches, couleur du ciel à l'horizon.
    scene.fogNode = Fn(() => {
      const v = positionWorld.sub(cameraPosition);
      const dist = v.length();
      const d = v.div(max(dist, 0.001));
      const couche = (a: THREE.Node<"float">, b: THREE.Node<"float">, y0: THREE.Node<"float"> | number) => {
        const k = b.mul(v.y);
        const integre = select(abs(k).greaterThan(0.0001), float(1).sub(exp(min(k.negate(), 40))).div(k), float(1));
        return a.mul(exp(min(b.negate().mul(cameraPosition.y.sub(y0)), 40))).mul(dist).mul(integre);
      };
      const epaisseur = couche(this.uVoile, float(1 / 1800), 0).add(couche(this.uBrume, this.uEpaisseur, this.uFond));
      const facteur = float(1).sub(exp(epaisseur.negate()));
      const horizon = cielDans(normalize(vec3(d.x, max(d.y, 0.02), d.z)));
      return fog(horizon, facteur);
    })();
  }

  /** Bas du relief alentour (m) : la brume des fonds s'y pose. */
  set fond(y: number) {
    this.uFond.value += (y - this.uFond.value) * 0.05;
  }

  /** Les étoiles sont des repères : leur éclat suit l'inverse de l'exposition pour rester des points fins. */
  set exposition(e: number) {
    this.materiauEtoiles.color.setScalar(Math.min(1, 0.9 / Math.max(e, 0.01)));
  }

  /** Place le soleil et la lune pour l'instant `secondes` du monde, la caméra étant en `camera`. */
  regler(secondes: number, camera: THREE.Vector3): void {
    const jour = Math.floor(secondes / 86400);
    const jourAn = jour % 360;
    const saison = Math.floor(jourAn / 90);
    const heure = (secondes % 86400) / 3600;
    const duree = DUREE_JOUR[saison];
    const lever = 12 - duree / 2;
    // Angle de 0 (lever) à π (coucher) ; la nuit continue sous l'horizon.
    let phase: number;
    if (heure >= lever && heure <= lever + duree) phase = ((heure - lever) / duree) * Math.PI;
    else {
      const nuit = 24 - duree;
      const depuis = (heure - (lever + duree) + 24) % 24;
      phase = Math.PI + (depuis / nuit) * Math.PI;
    }
    const hauteurMax = (MIDI[saison] * Math.PI) / 180;
    const elevation = Math.sin(phase) * (phase <= Math.PI ? hauteurMax : 0.6);
    const azimut = phase; // 0 est, π/2 sud, π ouest
    this.direction.set(Math.cos(azimut) * Math.cos(elevation), Math.sin(elevation), Math.sin(azimut) * Math.cos(elevation) * 0.7).normalize();
    // La lune, pleine, à l'opposé du soleil (un peu décalée pour ne pas suivre exactement le même arc).
    this.directionLune.set(-this.direction.x, -this.direction.y * 0.85 + 0.08, -this.direction.z - 0.25).normalize();
    this.maillage.position.copy(camera);
    this.etoiles.position.copy(camera);

    // Clarté, comme la simulation : une heure d'aube et de crépuscule centrées sur le lever et le coucher.
    const coucher = lever + duree;
    this.clarte = heure <= lever - 0.5 || heure >= coucher + 0.5 ? 0 : heure >= lever + 0.5 && heure <= coucher - 0.5 ? 1 : heure < 12 ? heure - (lever - 0.5) : coucher + 0.5 - heure;

    const haut = Math.max(0, camera.y);
    const soleil = this.direction.toArray() as [number, number, number];
    const lune = this.directionLune.toArray() as [number, number, number];
    // La texture du ciel se refait quelques lignes par image ; on recommence quand elle est finie.
    if (this.ligne === 0) {
      this.soleilCalc.splice(0, 3, ...soleil);
      this.luneCalc.splice(0, 3, ...lune);
      this.hauteurCalc = haut;
    }
    this.avancerCalcul(this.premiere ? H : LIGNES_PAR_IMAGE);

    // Lumière du soleil et de la lune après la traversée de l'air.
    const ts: Rgb = [0, 0, 0];
    const tl: Rgb = [0, 0, 0];
    transmission(soleil, haut, ts);
    transmission(lune, haut, tl);
    this.uSoleil.value.copy(this.direction);
    this.uLune.value.copy(this.directionLune);
    this.uCouleurSoleil.value.set(ts[0] * S, ts[1] * S, ts[2] * S);
    const disqueLune = 0.9 * Math.min(1, tl[1] * 4 + 0.2);
    this.uCouleurLune.value.set(disqueLune * 0.95, disqueLune * 0.95, disqueLune * 0.9);
    const eSoleil = lum(ts) * S * Math.max(0, this.direction.y);
    const eLune = lum(tl) * S * LUNE * Math.max(0, this.directionLune.y);
    if (eSoleil >= eLune) {
      this.astre.color.setRGB(ts[0], ts[1], ts[2]);
      this.astre.intensity = S;
      this.astre.position.copy(camera).addScaledVector(this.direction, 5000);
    } else {
      this.astre.color.setRGB(tl[0] * 0.8, tl[1] * 0.88, tl[2]);
      this.astre.intensity = S * LUNE;
      this.astre.position.copy(camera).addScaledVector(this.directionLune, 5000);
    }
    this.astre.target.position.copy(camera);
    this.astre.target.updateMatrixWorld();

    // Lumière du ciel (calculée sur la texture), lueur de la nuit, et sol qui renvoie un peu de lumière.
    const [r, v, b] = this.cielSol;
    this.hemi.color.setRGB(r + LUEUR_NUIT[0], v + LUEUR_NUIT[1], b + LUEUR_NUIT[2]);
    const renvoi = 0.16 * (Math.max(eSoleil, eLune) + lum(this.cielSol));
    this.hemi.groundColor.setRGB(renvoi * 0.95, renvoi * 0.85, renvoi * 0.65);
    this.hemi.intensity = 1;
    this.uNuit.value.set(LUEUR_NUIT[0] * 0.08, LUEUR_NUIT[1] * 0.08, LUEUR_NUIT[2] * 0.08);
    this.eclairement = eSoleil + eLune + lum(this.cielSol) + lum(LUEUR_NUIT);
    // Exposition visée : un gris moyen bien exposé de jour ; la nuit, l'adaptation s'arrête avant de rendre la nuit claire.
    this.expositionVisee = 1.9 / Math.max(this.eclairement, 0.09);
    this.materiauEtoiles.opacity = Math.max(0, 1 - this.clarte * 2.5);

    // Nuages : la couverture change d'un jour à l'autre (sans à-coup), le vent les pousse.
    const c0 = unite(hacher(jour, 31, 7));
    const c1 = unite(hacher(jour + 1, 31, 7));
    const f = (secondes % 86400) / 86400;
    this.uCouverture.value = 0.12 + 0.5 * (c0 + (c1 - c0) * f);
    this.uVent.value.set((secondes * 0.004) % 1000, (secondes * 0.0015) % 1000);
    const ciel = lum(this.cielSol);
    this.uNuageClair.value.set(ts[0] * S * 0.09 + ciel * 0.4 + 0.002, ts[1] * S * 0.09 + ciel * 0.42 + 0.002, ts[2] * S * 0.09 + ciel * 0.48 + 0.003);
    this.uNuageOmbre.value.copy(this.uNuageClair.value).multiplyScalar(0.55);

    // Brume des fonds : épaisse au petit matin et la nuit, légère l'après-midi.
    const apresLever = (heure - lever + 24) % 24;
    const matin = apresLever < 4 ? 1 - apresLever / 4 : 0;
    const nuitBrume = this.clarte < 0.5 ? 0.5 : 0;
    this.uBrume.value = 0.00012 + 0.0008 * Math.max(matin, nuitBrume);
    this.uVoile.value = 2.2e-5 / (1 + haut / 4000);
  }

  /** Refait `n` lignes de la texture du ciel ; à la dernière, l'envoie au GPU et calcule la lumière du ciel. */
  private avancerCalcul(n: number): void {
    const d: [number, number, number] = [0, 0, 0];
    const intensiteLune = this.luneCalc[1] > -0.1 ? LUNE : 0;
    for (let k = 0; k < n && this.ligne < H; k++, this.ligne++) {
      const y = this.ligne;
      const e = elevationDe((y + 0.5) / H);
      for (let x = 0; x < L; x++) {
        const a = ((x + 0.5) / L - 0.5) * Math.PI * 2;
        d[0] = Math.cos(a) * Math.cos(e);
        d[1] = Math.sin(e);
        d[2] = Math.sin(a) * Math.cos(e);
        diffusion(d, this.hauteurCalc, this.soleilCalc, this.luneCalc, intensiteLune, this.rgb);
        this.calcul.set(this.rgb, (y * L + x) * 3);
      }
    }
    if (this.ligne < H) return;
    this.ligne = 0;
    this.premiere = false;
    const donnees = this.tex.image.data as Uint16Array;
    for (let i = 0; i < L * H; i++) {
      for (let c = 0; c < 3; c++) donnees[i * 4 + c] = THREE.DataUtils.toHalfFloat(Math.min(60000, this.calcul[i * 3 + c] * ECHELLE));
      donnees[i * 4 + 3] = THREE.DataUtils.toHalfFloat(1);
    }
    this.tex.needsUpdate = true;
    // Éclairement du sol par le ciel : ∫ L·sin(e) dΩ sur la moitié haute.
    const e: Rgb = [0, 0, 0];
    for (let y = H / 2; y < H; y++) {
      const e0 = elevationDe(y / H);
      const e1 = elevationDe((y + 1) / H);
      const em = (e0 + e1) / 2;
      const poids = Math.sin(em) * Math.cos(em) * (e1 - e0) * ((Math.PI * 2) / L);
      for (let x = 0; x < L; x++) for (let c = 0; c < 3; c++) e[c] += this.calcul[(y * L + x) * 3 + c] * poids;
    }
    // Le calcul ne suit qu'une diffusion ; les diffusions multiples doublent à peu près la lumière du ciel qui arrive au sol.
    this.cielSol = [e[0] * S * MULTIPLE, e[1] * S * MULTIPLE, e[2] * S * MULTIPLE];
    // Reflets de l'eau : la même lumière en projection équirectangulaire (le haut seulement ; le bas reprend l'horizon).
    const r = this.reflets.image.data as Uint16Array;
    for (let y = 0; y < 32; y++) {
      const el = Math.max(0.01, ((y + 0.5) / 32 - 0.5) * Math.PI);
      const v = ligneDe(el) * H - 0.5;
      const y0 = Math.max(0, Math.min(H - 1, Math.floor(v)));
      const y1 = Math.min(H - 1, y0 + 1);
      const fy = Math.max(0, Math.min(1, v - y0));
      for (let x = 0; x < 64; x++) {
        const xs = Math.floor(((x + 0.5) / 64) * L) % L;
        for (let c = 0; c < 3; c++) {
          const val = this.calcul[(y0 * L + xs) * 3 + c] * (1 - fy) + this.calcul[(y1 * L + xs) * 3 + c] * fy;
          r[(y * 64 + x) * 4 + c] = THREE.DataUtils.toHalfFloat(Math.min(60000, val * S));
        }
        r[(y * 64 + x) * 4 + 3] = THREE.DataUtils.toHalfFloat(1);
      }
    }
    this.reflets.needsUpdate = true;
    this.reflets.needsPMREMUpdate = true;
  }
}
