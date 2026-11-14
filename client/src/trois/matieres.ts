import * as THREE from "three/webgpu";
import {
  Fn, If, attribute, cameraPosition, dot, int, log2, positionWorld, smoothstep, clamp, cross, dFdx, dFdy, float, length, max, mix, normalWorldGeometry, normalize, positionGeometry, positionLocal,
  pow, sin, texture, time, transformNormalToView, uniform, uv, vec2, vec3, vec4,
} from "three/tsl";
import { hacher, unite } from "./bruit";

/**
 * Matières de la nature (0.41.5, proposition D1) : textures photographiées CC0 (Poly Haven, ambientCG) préparées par
 * outils/nature/composer.py dans client/public/nature, et le vent qui fait bouger les plantes.
 */

/** Couches du sol, dans l'ordre des bandes de sols-couleur.webp et sols-normales.webp. */
export const enum Couche {
  SousBois,
  Litiere,
  Boue,
  Sable,
  Roche,
  RocheMoussue,
  Neige,
}
export const COUCHES = 7;

/** Rugosité de chaque couche (la boue luit un peu, la neige brille au soleil rasant). */
const RUGOSITE = [0.92, 0.95, 0.62, 0.9, 0.82, 0.88, 0.7];

/** Zone d'une carte de feuillage dans l'atlas (u, v depuis le haut, largeur, hauteur ; fractions de l'atlas). */
export type Zone = [number, number, number, number];

interface Description {
  sols: [number, number, number][];
  ecorces: [number, number, number][];
  zones: Record<string, Zone>;
}

/** Période des coordonnées de texture du sol (m) : multiple de toutes les échelles employées, pour un raccord exact entre tuiles. */
export const PERIODE_SOL = 1200;

async function bitmap(url: string): Promise<ImageBitmap> {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url} : ${r.status}`);
  return createImageBitmap(await r.blob(), { colorSpaceConversion: "none", premultiplyAlpha: "none" });
}

/** Une bande d'images carrées empilées → texture à couches (une couche par image). */
function couches(img: ImageBitmap, srgb: boolean): THREE.DataArrayTexture {
  const l = img.width;
  const n = Math.round(img.height / l);
  const toile = new OffscreenCanvas(l, img.height);
  const g = toile.getContext("2d", { willReadFrequently: true })!;
  g.drawImage(img, 0, 0);
  const data = new Uint8Array(g.getImageData(0, 0, l, img.height).data.buffer);
  const tex = new THREE.DataArrayTexture(data, l, l, n);
  tex.format = THREE.RGBAFormat;
  tex.type = THREE.UnsignedByteType;
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.magFilter = THREE.LinearFilter;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.generateMipmaps = true;
  tex.anisotropy = 8;
  tex.colorSpace = srgb ? THREE.SRGBColorSpace : THREE.NoColorSpace;
  tex.needsUpdate = true;
  return tex;
}

/** Le vent du moment : direction et force tirées de chaque jour (même vent à chaque visite), plus fort l'après-midi. */
export class Vent {
  /** Direction × force (0 calme à 1 grand vent), dans le plan (x, z). */
  readonly u = uniform(new THREE.Vector2(0.3, 0.1));
  force = 0.3;
  private readonly dir = new THREE.Vector2();

  regler(secondes: number): void {
    const jour = Math.floor(secondes / 86400);
    const f = (secondes % 86400) / 86400;
    const tirer = (j: number, k: number) => unite(hacher(j, k, 977));
    const lisse = (k: number) => tirer(jour, k) + (tirer(jour + 1, k) - tirer(jour, k)) * f * f * (3 - 2 * f);
    // La plupart des jours sont calmes ou venteux ; quelques-uns soufflent fort.
    const base = Math.pow(lisse(1), 1.6);
    const apresMidi = Math.max(0, Math.sin(((f - 0.3) / 0.5) * Math.PI));
    this.force = Math.min(1, 0.08 + 0.75 * base + 0.17 * apresMidi);
    const angle = lisse(2) * Math.PI * 4;
    this.dir.set(Math.cos(angle), Math.sin(angle));
    this.u.value.copy(this.dir).multiplyScalar(this.force);
  }
}

export class Matieres {
  readonly vent = new Vent();
  /** Portée des plantes dessinées (m) : au-delà, le sol prend la couleur du pays vu de loin (forêts vertes). */
  readonly portee = uniform(900);
  readonly zones: Record<string, Zone>;
  /** Couleur moyenne (linéaire) de chaque couche du sol : la teinte calculée par le relief s'y rapporte. */
  readonly moyennes: [number, number, number][];

  private constructor(
    readonly solsCouleur: THREE.DataArrayTexture,
    readonly solsNormales: THREE.DataArrayTexture,
    readonly ecorcesCouleur: THREE.DataArrayTexture,
    readonly ecorcesNormales: THREE.DataArrayTexture,
    readonly feuillages: THREE.Texture,
    description: Description,
  ) {
    this.zones = description.zones;
    this.moyennes = description.sols;
  }

  /** Charge les textures (une fois par ouverture de la vue 3D). Null si elles manquent : la vue garde alors les couleurs seules. */
  static async charger(base = "nature/"): Promise<Matieres | null> {
    try {
      const [desc, sc, sn, ec, en, f] = await Promise.all([
        fetch(base + "nature.json").then((r) => r.json() as Promise<Description>),
        bitmap(base + "sols-couleur.webp"),
        bitmap(base + "sols-normales.webp"),
        bitmap(base + "ecorces-couleur.webp"),
        bitmap(base + "ecorces-normales.webp"),
        bitmap(base + "feuillages.webp"),
      ]);
      const feuillages = new THREE.Texture(f);
      feuillages.colorSpace = THREE.SRGBColorSpace;
      feuillages.flipY = false;
      feuillages.generateMipmaps = true;
      feuillages.minFilter = THREE.LinearMipmapLinearFilter;
      feuillages.anisotropy = 4;
      feuillages.needsUpdate = true;
      return new Matieres(couches(sc, true), couches(sn, false), couches(ec, true), couches(en, false), feuillages, desc);
    } catch (e) {
      console.warn("nature", e);
      return null;
    }
  }

  /**
   * Le sol : sept couches photographiées mêlées selon les poids de chaque sommet (attributs `poids0`, `poids1` ; `poids1.w` = mouillé),
   * départagées par leur relief (le sable remplit les creux de la roche), à deux échelles pour casser la répétition, avec leurs
   * cartes de normales. La couleur du sommet (`color`) teinte le tout : saisons, sols plus secs ou plus riches, neige.
   */
  materiauSol(): THREE.MeshStandardNodeMaterial {
    const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0 });
    const p0 = attribute("poids0", "vec4");
    const p1 = attribute("poids1", "vec4");
    const teinte = attribute("color", "vec3");
    const m = uv();
    const uA = m.div(2.5);
    const uB = m.mul(70 / PERIODE_SOL).add(vec2(0.37, 0.11));
    const gAx = dFdx(uA);
    const gAy = dFdy(uA);
    const gBx = dFdx(uB);
    const gBy = dFdy(uB);
    const loin = smoothstep(3, 45, length(positionWorld.sub(cameraPosition))).mul(0.32).add(0.1);
    const melange = Fn(() => {
      const poids = [p0.x, p0.y, p0.z, p0.w, p1.x, p1.y, p1.z];
      const couleur = vec3(0).toVar();
      const rugo = float(0).toVar();
      const total = float(0).toVar();
      poids.forEach((w, i) => {
        If(w.greaterThan(0.003), () => {
          const a = texture(this.solsCouleur, uA).depth(int(i)).grad(gAx, gAy);
          const b = texture(this.solsCouleur, uB).depth(int(i)).grad(gBx, gBy);
          // Près de soi, la grande échelle se fond moins (le détail reste net) ; au loin elle casse la répétition.
          const c = mix(a, b, loin);
          // Hauteur rangée dans 0,5 à 1 par le compositeur.
          const h = c.a.sub(0.5).mul(2);
          const ww = w.mul(pow(h.add(0.35), 4));
          couleur.addAssign(c.rgb.mul(ww));
          rugo.addAssign(ww.mul(RUGOSITE[i]));
          total.addAssign(ww);
        });
      });
      const t = max(total, 1e-4);
      return vec4(couleur.div(t), rugo.div(t));
    });
    const normaleTangente = Fn(() => {
      const poids = [p0.x, p0.y, p0.z, p0.w, p1.x, p1.y, p1.z];
      const normale = vec3(0).toVar();
      const total = float(0).toVar();
      poids.forEach((w, i) => {
        If(w.greaterThan(0.003), () => {
          const c = texture(this.solsCouleur, uA).depth(int(i)).grad(gAx, gAy);
          const ww = w.mul(pow(c.a.sub(0.5).mul(2).add(0.35), 4));
          normale.addAssign(texture(this.solsNormales, uA).depth(int(i)).grad(gAx, gAy).xyz.mul(2).sub(1).mul(ww));
          total.addAssign(ww);
        });
      });
      return normale.div(max(total, 1e-4));
    });
    const mouille = p1.w;
    const resultat = melange();
    const pres = resultat.rgb.mul(teinte).mul(mix(float(1), float(0.55), mouille));
    const distance = length(positionWorld.sub(cameraPosition));
    // Peu à peu (dès 20 m, entièrement au bout de la portée) : vu d'avion, le sol entre les arbres prend déjà la couleur du pays.
    const loinPays = pow(clamp(distance.sub(20).div(this.portee), 0, 1), 0.7);
    mat.colorNode = mix(pres, attribute("lointain", "vec3"), loinPays);
    mat.roughnessNode = mix(resultat.a, float(0.3), mouille);
    mat.normalNode = Fn(() => {
      const n = normalize(normalWorldGeometry);
      const tx = normalize(vec3(1, 0, 0).sub(n.mul(n.x)));
      const bz = cross(tx, n);
      const nt = normaleTangente();
      const force = mix(float(1), float(0.4), mouille);
      const w = normalize(tx.mul(nt.x.mul(force)).add(bz.mul(nt.y.mul(force))).add(n.mul(nt.z)));
      return transformNormalToView(w);
    })();
    return mat;
  }

  /**
   * Déplacement des plantes par le vent : le tronc ou la tige penche et oscille (davantage vers le haut, `positionGeometry.y` allant
   * de 0 au pied à 1 au sommet), les feuilles frémissent (`agite` par sommet). Attribut d'instance `vent` : phase, souplesse, hauteur (m).
   */
  deplacementVent(): THREE.Node<"vec3"> {
    const inst = attribute("vent", "vec3");
    const agite = attribute("agite", "float");
    const v = this.vent.u;
    return Fn(() => {
      const phase = inst.x;
      const souplesse = inst.y;
      const h = inst.z;
      const force = length(v);
      const y = clamp(positionGeometry.y, 0, 1.2);
      const rafale = sin(time.mul(0.37).add(phase.mul(0.13))).mul(sin(time.mul(1.21).add(phase))).mul(0.35).add(0.65);
      // Fréquence propre : plus lente pour les grands arbres.
      const oscille = sin(time.mul(float(1.7).div(h.add(1).sqrt()).add(0.5)).add(phase)).mul(0.3).add(0.7);
      const penche = force.mul(souplesse).mul(rafale).mul(oscille).mul(y.mul(y)).mul(h).mul(0.04);
      const frisson = sin(time.mul(7.3).add(phase.mul(3)).add(positionGeometry.x.mul(11)).add(positionGeometry.z.mul(7)))
        .mul(agite).mul(force.add(0.08)).mul(h.sqrt().mul(0.035));
      const d = v.div(max(force, 1e-3));
      return positionLocal.add(vec3(d.x.mul(penche).add(frisson.mul(0.6)), frisson.sub(penche.mul(penche).div(h.mul(2).add(0.1))), d.y.mul(penche).add(frisson.mul(0.4))));
    })();
  }

  /** Feuillage : cartes de l'atlas, découpées par leur transparence, éclairées des deux côtés de la même façon, et qui bougent au vent. */
  materiauFeuillage(): THREE.MeshStandardNodeMaterial {
    const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.75, side: THREE.DoubleSide });
    mat.alphaTest = 0.42;
    const c = texture(this.feuillages, uv());
    // Au loin, les niveaux réduits de l'atlas moyennent la transparence et les feuilles fondraient : on la relève selon le niveau lu.
    const px = uv().mul(2048);
    const niveau = max(log2(max(dot(dFdx(px), dFdx(px)), dot(dFdy(px), dFdy(px)))).mul(0.5), 0);
    mat.colorNode = vec4(c.rgb, c.a.mul(niveau.mul(0.3).add(1)));
    // La normale de la géométrie, sans la retourner sur la face arrière : une feuille vue de dos reste éclairée (lumière qui la traverse).
    mat.normalNode = transformNormalToView(normalWorldGeometry);
    mat.positionNode = this.deplacementVent();
    return mat;
  }

  /** Écorce (couche 0 : arbres ; 1 : stipes de cycas et de fougères arborescentes), avec ses normales ; bouge avec l'arbre. */
  materiauEcorce(couche: number): THREE.MeshStandardNodeMaterial {
    const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.92 });
    // Les stipes (photo sombre) sont éclaircis pour ne pas paraître noirs.
    mat.colorNode = texture(this.ecorcesCouleur, uv()).depth(int(couche)).rgb.mul(couche === 1 ? 2.2 : 1.1);
    mat.normalMap = null;
    const n = texture(this.ecorcesNormales, uv()).depth(int(couche)).xyz.mul(2).sub(1);
    mat.normalNode = Fn(() => {
      // Tronc vertical : tangente autour (u), bitangente le long (v).
      const nw = normalize(normalWorldGeometry);
      const b = vec3(0, 1, 0);
      const t = normalize(cross(b, nw));
      return transformNormalToView(normalize(t.mul(n.x).add(b.mul(n.y)).add(nw.mul(n.z.add(0.3)))));
    })();
    mat.positionNode = this.deplacementVent();
    return mat;
  }

  /** Rochers : la couche « roche moussue » du sol (ou la roche nue), teintée par instance. */
  materiauRocher(couche: Couche = Couche.RocheMoussue, teinte: [number, number, number] = [1.15, 1.15, 1.15]): THREE.MeshStandardNodeMaterial {
    const mat = new THREE.MeshStandardNodeMaterial({ metalness: 0, roughness: 0.88 });
    mat.colorNode = texture(this.solsCouleur, uv()).depth(int(couche)).rgb.mul(vec3(...teinte));
    const n = texture(this.solsNormales, uv()).depth(int(couche)).xyz.mul(2).sub(1);
    mat.normalNode = Fn(() => {
      const nw = normalize(normalWorldGeometry);
      const t = normalize(cross(vec3(0, 1, 0.001), nw));
      const b = cross(nw, t);
      return transformNormalToView(normalize(t.mul(n.x).add(b.mul(n.y)).add(nw.mul(n.z.mul(0.8)))));
    })();
    return mat;
  }
}
