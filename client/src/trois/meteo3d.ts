import * as THREE from "three/webgpu";
import {
  Fn, abs, cameraPosition, max, clamp, cos, cross, float, fract, instanceIndex, length, mix, mod, normalize, positionGeometry,
  sin, smoothstep, texture, time, uniform, uv, vec2, vec3, vec4,
} from "three/tsl";
import type { EtatTemps } from "../meteo";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";

type N<T extends string> = THREE.Node<T>;

/**
 * Le temps et les terres touchées, pour les matériaux de la vue 3D (0.43.0). Deux textures que tous lisent :
 * - les régions climatiques (un pixel par région, lissé) : pluie qui tombe (r), neige (g), sol mouillé (b), orage (a) ;
 * - la carte (un pixel par case) : brûlé (r, 0 à 1 selon les dégâts qui restent), feu qui passe (g), cendre (b), eau d'une crue (a).
 * Les matériaux sont construits après `preparer` (à l'ouverture de la vue 3D) ; les textures ne changent ensuite que de contenu.
 */
export class MeteoGPU {
  private regions = new THREE.DataTexture(new Uint8Array(4), 1, 1);
  /** Lumière du ciel que renvoie l'eau d'une crue (0.43.1) : réglée à chaque image d'après le ciel. */
  readonly uCielEau = uniform(new THREE.Vector3(0.3, 0.35, 0.45));
  private terres = new THREE.DataTexture(new Uint8Array(4), 1, 1);
  /** Taille couverte par la texture des régions et par celle de la carte (m). */
  private readonly uRegionsM = uniform(new THREE.Vector2(1, 1));
  private readonly uCarteM = uniform(new THREE.Vector2(1, 1));
  private regionsX = 1;
  private regionsY = 1;
  private largeur = 1;
  private hauteur = 1;
  private m = 800;

  /** Une nouvelle vue 3D : les textures à la taille de la carte (régions de `tailleRegion` cases, comme la simulation). */
  preparer(largeur: number, hauteur: number, metresParCase: number, tailleRegion = 32): void {
    this.largeur = largeur;
    this.hauteur = hauteur;
    this.m = metresParCase;
    this.regionsX = Math.ceil(largeur / tailleRegion);
    this.regionsY = Math.ceil(hauteur / tailleRegion);
    this.regions.dispose();
    this.terres.dispose();
    this.regions = creer(this.regionsX, this.regionsY, true);
    this.terres = creer(largeur, hauteur, true);
    this.uRegionsM.value.set(this.regionsX * tailleRegion * metresParCase, this.regionsY * tailleRegion * metresParCase);
    this.uCarteM.value.set(largeur * metresParCase, hauteur * metresParCase);
  }

  /** Le ciel du moment, région par région. */
  definirTemps(e: EtatTemps | null): void {
    const d = this.regions.image.data as Uint8Array;
    d.fill(0);
    if (e && e.regionsX === this.regionsX && e.regionsY === this.regionsY) {
      for (let r = 0; r < e.pluie.length; r++) {
        d[r * 4] = Math.round(255 * e.pluie[r]);
        d[r * 4 + 1] = Math.round(255 * e.neige[r]);
        d[r * 4 + 2] = Math.round(255 * e.mouille[r]);
        d[r * 4 + 3] = Math.round(255 * e.orage[r]);
      }
    }
    this.regions.needsUpdate = true;
  }

  /** Les terres touchées : brûlé, front, cendre, eau (octets 0 à 255, case par case). */
  definirTerres(remplir: (d: Uint8Array, largeur: number, hauteur: number) => void): void {
    const d = this.terres.image.data as Uint8Array;
    d.fill(0);
    remplir(d, this.largeur, this.hauteur);
    this.terres.needsUpdate = true;
  }

  /** Temps au point p du monde : pluie, neige, mouillé, orage (de 0 à 1). */
  tempsEn(p: N<"vec2">): N<"vec4"> {
    return texture(this.regions, p.div(this.uRegionsM)).level(float(0));
  }

  /** Terres au point p du monde : brûlé, front, cendre, eau (de 0 à 1). */
  terresEn(p: N<"vec2">): N<"vec4"> {
    return texture(this.terres, p.div(this.uCarteM)).level(float(0));
  }

  get metresParCase(): number {
    return this.m;
  }
}

function creer(l: number, h: number, lisse: boolean): THREE.DataTexture {
  const t = new THREE.DataTexture(new Uint8Array(l * h * 4), l, h, THREE.RGBAFormat, THREE.UnsignedByteType);
  t.colorSpace = THREE.NoColorSpace;
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  t.magFilter = t.minFilter = lisse ? THREE.LinearFilter : THREE.NearestFilter;
  t.generateMipmaps = false;
  t.needsUpdate = true;
  return t;
}

/** Les textures partagées par les matériaux de la vue 3D. */
export const meteoGPU = new MeteoGPU();

/** Nombre au hasard dans [0, 1) pour l'instance i et l'usage k (sur le GPU). */
const alea = (i: N<"float">, k: number) => fract(sin(i.mul(12.9898).add(k * 78.233)).mul(43758.5453));

/**
 * Pluie et neige qui tombent autour de la caméra (0.43.0, proposition B1). Une boîte de gouttes accrochée au monde (elles ne
 * suivent pas la caméra : on les traverse), refaite sans fin par le GPU : rien n'est récrit dans les tampons à chaque image.
 * Chaque goutte n'est montrée que si la région où elle tombe reçoit assez de pluie : la pluie s'arrête au bord de l'averse.
 * Gouttes : traînées de 0,5 à 0,9 m (une goutte de 2 mm tombe à 6-7 m/s, l'œil la voit filer) penchées par le vent ;
 * flocons : 1 m/s environ, qui flottent.
 */
export class Precipitations {
  readonly groupe = new THREE.Group();
  private readonly pluie: THREE.Mesh;
  private readonly neige: THREE.Mesh;
  private readonly geoPluie: THREE.InstancedBufferGeometry;
  private readonly geoNeige: THREE.InstancedBufferGeometry;
  /** Lumière ambiante (couleur du ciel) qui éclaire les gouttes. */
  readonly uLumiere = uniform(new THREE.Vector3(0.5, 0.5, 0.5));
  private readonly uBoite = uniform(50);
  /** Cendre qui tombe d'une éruption (0 à 1, 0.43.1) : les flocons deviennent gris et tombent partout autour de la caméra. */
  readonly uCendre = uniform(0);
  private nombre = 0;

  constructor(vent: N<"vec2">) {
    const quad = new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0);
    this.geoPluie = new THREE.InstancedBufferGeometry().copy(quad as unknown as THREE.InstancedBufferGeometry);
    this.geoNeige = new THREE.InstancedBufferGeometry().copy(quad as unknown as THREE.InstancedBufferGeometry);
    this.pluie = new THREE.Mesh(this.geoPluie, this.materiau(vent, false));
    this.neige = new THREE.Mesh(this.geoNeige, this.materiau(vent, true));
    for (const m of [this.pluie, this.neige]) {
      m.frustumCulled = false;
      m.renderOrder = 5;
      this.groupe.add(m);
    }
    this.regler(0);
  }

  private materiau(vent: N<"vec2">, neige: boolean): THREE.MeshBasicNodeMaterial {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: THREE.DoubleSide, fog: true });
    const i = float(instanceIndex);
    const B = this.uBoite;
    const H = B.mul(0.7);
    const vitesse = neige ? alea(i, 4).mul(0.5).add(0.8) : alea(i, 4).mul(1.6).add(5.6);
    const chute = time.mul(vitesse);
    // Point de départ fixé dans le monde, ramené dans la boîte autour de la caméra.
    const bx = alea(i, 1).mul(B);
    const bz = alea(i, 2).mul(B);
    const by = alea(i, 3).mul(H);
    const cam = cameraPosition;
    const x = cam.x.add(mod(bx.sub(cam.x), B)).sub(B.mul(0.5));
    const z = cam.z.add(mod(bz.sub(cam.z), B)).sub(B.mul(0.5));
    const y = cam.y.sub(H.mul(0.35)).add(mod(by.sub(chute).sub(cam.y), H));
    // Le vent penche la pluie (et emporte les flocons, qui tourbillonnent).
    const v = vent.mul(neige ? 2.5 : 4);
    const flotte = neige ? vec2(sin(time.mul(1.3).add(i)).mul(0.4), cos(time.mul(1.1).add(i.mul(1.7))).mul(0.4)) : vec2(0, 0);
    const centre = vec3(x.add(flotte.x), y, z.add(flotte.y));
    const t = meteoGPU.tempsEn(centre.xz);
    const force = neige ? max(t.g, this.uCendre) : t.r;
    // Une goutte sur deux cents par petite pluie, toutes sous l'averse.
    const garde = smoothstep(0, 0.15, force.sub(alea(i, 5)));
    const dist = length(centre.sub(cam));
    const fondu = float(1).sub(smoothstep(B.mul(0.25), B.mul(0.5), dist)).mul(smoothstep(0.3, 1.2, dist));
    const descente = normalize(vec3(v.x, vitesse.negate(), v.y));
    const versCam = normalize(cam.sub(centre));
    const droite = normalize(cross(descente, versCam));
    const longueur = neige ? float(0.035) : alea(i, 6).mul(0.4).add(0.5);
    const largeur = neige ? float(0.035) : float(0.008).add(dist.mul(0.0007));
    const p = positionGeometry;
    const haut = neige ? vec3(0, 1, 0) : descente.negate();
    mat.positionNode = Fn(() => centre.add(droite.mul(p.x.mul(largeur).mul(garde))).add(haut.mul(p.y.sub(0.5).mul(longueur).mul(garde))))();
    const q = uv();
    const forme = neige
      ? smoothstep(0.5, 0.15, length(q.sub(0.5)))
      : smoothstep(0.5, 0.1, abs(q.x.sub(0.5))).mul(smoothstep(0, 0.4, q.y)).mul(smoothstep(1, 0.6, q.y));
    const teinte = neige ? mix(this.uLumiere.mul(1.1), this.uLumiere.mul(0.35), this.uCendre) : this.uLumiere.mul(0.9).add(0.02);
    mat.colorNode = vec4(teinte, forme.mul(fondu).mul(garde).mul(neige ? 0.9 : 0.42));
    return mat;
  }

  /** Nombre de gouttes (et de flocons) selon la Qualité : 0 en Basse. */
  regler(nombre: number): void {
    this.nombre = nombre;
    this.uBoite.value = nombre > 20000 ? 60 : 45;
    this.geoPluie.instanceCount = 0;
    this.geoNeige.instanceCount = 0;
  }

  /** Une image : on ne dessine les gouttes que s'il pleut (ou neige) près de la caméra. */
  animer(pluieIci: number, neigeIci: number, lumiere: THREE.Color, cendre = 0): void {
    this.uLumiere.value.set(lumiere.r, lumiere.g, lumiere.b);
    this.uCendre.value = cendre;
    this.geoPluie.instanceCount = pluieIci > 0.005 ? this.nombre : 0;
    this.geoNeige.instanceCount = neigeIci > 0.005 || cendre > 0.005 ? Math.round(this.nombre * 0.6) : 0;
  }
}

/**
 * Éclairs d'orage (0.43.0, B1) : de temps en temps, sous un orage proche, un éclair zèbre le ciel jusqu'au sol (un trait
 * brisé et ses branches, construit une fois par éclair) et le paysage s'illumine un instant, en deux ou trois battements.
 * Un orage fort donne un éclair toutes les quelques secondes de vue.
 */
export class Eclairs {
  readonly groupe = new THREE.Group();
  private readonly trait: THREE.Mesh;
  private readonly materiau = new THREE.MeshBasicMaterial({ color: 0xdfe4ff, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, fog: false, side: THREE.DoubleSide });
  private prochain = 0;
  private debut = -10;
  private compte = 0;
  /** Éclat du moment (0 à 1), à ajouter à la lumière du ciel. */
  eclat = 0;

  constructor(private readonly relief: Relief) {
    this.trait = new THREE.Mesh(new THREE.BufferGeometry(), this.materiau);
    this.trait.frustumCulled = false;
    this.trait.visible = false;
    this.groupe.add(this.trait);
  }

  /** `orages` : cœurs d'orage (m, monde) et force ; `maintenant` en secondes réelles. */
  animer(maintenant: number, camera: THREE.Vector3, orages: { x: number; z: number; force: number; rayon: number }[], exposition: number): void {
    const proches = orages.filter((o) => Math.hypot(o.x - camera.x, o.z - camera.z) < o.rayon + 25000);
    if (proches.length > 0 && maintenant > this.prochain) {
      const o = proches[this.compte % proches.length];
      this.frapper(o, camera);
      // Intervalle : quelques secondes sous un orage fort, une demi-minute sous un faible (ordre de grandeur : quelques éclairs
      // par minute dans une cellule orageuse active).
      this.prochain = maintenant + (2 + 18 * (1 - o.force)) * (0.4 + 1.2 * unite(hacher(this.compte, 3, 41)));
    }
    if (proches.length === 0) this.prochain = Math.max(this.prochain, maintenant + 1);
    const t = maintenant - this.debut;
    // Deux ou trois battements en un tiers de seconde.
    const battement = t < 0.35 ? Math.max(0, Math.sin(t * 60) * (1 - t / 0.35)) : 0;
    this.eclat = battement;
    this.trait.visible = battement > 0.05;
    this.materiau.color.setRGB(0.87, 0.89, 1).multiplyScalar((3 + 6 * battement) / Math.pow(Math.max(exposition, 0.01), 0.85));
  }

  private frapper(o: { x: number; z: number; rayon: number }, camera: THREE.Vector3): void {
    this.compte++;
    const k = this.compte;
    const r = (n: number) => unite(hacher(k, n, 77));
    // Quelque part dans l'orage, plus souvent un peu loin de la caméra.
    const a = r(1) * Math.PI * 2;
    const d = Math.min(o.rayon, 800 + 9000 * r(2));
    let x = o.x + Math.cos(a) * d * 0.6;
    let z = o.z + Math.sin(a) * d * 0.6;
    if (Math.hypot(x - camera.x, z - camera.z) < 400) {
      x += 600;
      z += 600;
    }
    const sol = Math.max(0, this.relief.hauteur(x, z));
    const haut = 1500;
    const points: THREE.Vector3[] = [];
    let px = x, pz = z;
    for (let s = 0; s <= 14; s++) {
      const y = haut - ((haut - sol) * s) / 14;
      points.push(new THREE.Vector3(px, y, pz));
      px += (r(10 + s) - 0.5) * 140;
      pz += (r(40 + s) - 0.5) * 140;
    }
    const segments: [THREE.Vector3, THREE.Vector3, number][] = [];
    for (let s = 0; s < points.length - 1; s++) segments.push([points[s], points[s + 1], 9]);
    // Branches.
    for (let b = 0; b < 3; b++) {
      const depart = points[2 + Math.floor(r(80 + b) * 7)];
      let q = depart.clone();
      for (let s = 0; s < 4; s++) {
        const n = q.clone().add(new THREE.Vector3((r(90 + b * 9 + s) - 0.5) * 220, -90 - 80 * r(120 + s), (r(130 + b * 9 + s) - 0.5) * 220));
        segments.push([q, n, 4]);
        q = n;
      }
    }
    // Rubans tournés vers la caméra.
    const pos: number[] = [];
    for (const [p1, p2, l] of segments) {
      const dir = p2.clone().sub(p1);
      const vers = camera.clone().sub(p1);
      const cote = dir.clone().cross(vers).normalize().multiplyScalar(l);
      pos.push(
        p1.x - cote.x, p1.y - cote.y, p1.z - cote.z, p1.x + cote.x, p1.y + cote.y, p1.z + cote.z, p2.x + cote.x, p2.y + cote.y, p2.z + cote.z,
        p1.x - cote.x, p1.y - cote.y, p1.z - cote.z, p2.x + cote.x, p2.y + cote.y, p2.z + cote.z, p2.x - cote.x, p2.y - cote.y, p2.z - cote.z,
      );
    }
    this.trait.geometry.dispose();
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
    this.trait.geometry = g;
    this.debut = performance.now() / 1000;
  }
}

/** Sur le sol : plus sombre et brillant quand il est mouillé (sert aux matériaux du sol). */
export function mouilleEn(p: N<"vec3">): N<"float"> {
  return clamp(meteoGPU.tempsEn(p.xz).b, 0, 1);
}

/** Terres : brûlé (de 0 à 1) au point p. */
export function bruleEn(p: N<"vec3">): N<"float"> {
  return meteoGPU.terresEn(p.xz).r;
}

/** Couleur d'une terre brûlée, puis cendre grise d'une éruption, et braises sous le feu qui passe. */
export function terreTouchee(couleur: N<"vec3">, p: N<"vec3">): { couleur: N<"vec3">; braises: N<"vec3">; eau: N<"float"> } {
  const t = meteoGPU.terresEn(p.xz);
  // 0.43.1 : l'eau boueuse d'une crue ou d'une vague couvre les terres basses ; après, un limon brun-gris sèche dessus.
  const eau = smoothstep(0.6, 0.9, t.a);
  const limon = smoothstep(0.08, 0.3, t.a).mul(float(1).sub(eau));
  // Le brûlé fonce le sol jusqu'au charbon ; la cendre le grise.
  let c = mix(couleur, vec3(0.035, 0.03, 0.026), clamp(t.r.mul(1.1), 0, 0.92));
  c = mix(c, vec3(0.42, 0.41, 0.39), clamp(t.b, 0, 0.85));
  // Braises : des taches qui rougeoient et palpitent là où le feu passe.
  // Taches de 20 à 60 m ; au loin elles se fondent en une lueur égale (sinon le motif fourmille en moiré).
  // Trois ondes en biais l'une de l'autre : des taches irrégulières, sans quadrillage.
  const n0 = sin(p.x.mul(0.071).add(p.z.mul(0.043)).add(time.mul(1.3)))
    .add(sin(p.x.mul(-0.052).add(p.z.mul(0.089)).sub(time.mul(1.1))))
    .add(sin(p.x.mul(0.097).sub(p.z.mul(0.061)).add(time.mul(0.7))))
    .div(6).add(0.5);
  // Une seconde nappe, plus large et dans d'autres directions, rompt la régularité : des foyers épars, pas un motif.
  const n1 = sin(p.x.mul(0.023).sub(p.z.mul(0.031)).add(1.7)).add(sin(p.x.mul(0.017).add(p.z.mul(0.026)).sub(time.mul(0.3)))).div(4).add(0.5);
  const n = mix(n0.mul(n1).mul(1.6), float(0.62), smoothstep(250, 1500, length(cameraPosition.xz.sub(p.xz))));
  const braises = vec3(1, 0.3, 0.05).mul(t.g.mul(smoothstep(0.45, 0.95, n)).mul(1.2));
  c = mix(c, vec3(0.42, 0.36, 0.27), limon.mul(0.7));
  c = mix(c, vec3(0.1, 0.085, 0.055), eau.mul(0.9));
  // L'eau renvoie le ciel, d'autant plus qu'on la regarde en rasant (Schlick, eau : 2 % de face).
  const rasant = float(1).sub(abs(normalize(cameraPosition.sub(p)).y));
  const reflet = meteoGPU.uCielEau.mul(eau.mul(rasant.pow(5).mul(0.98).add(0.02)));
  return { couleur: c, braises: braises.add(reflet), eau };
}

/** Feuillage brûlé : les feuilles tombent quand la brûlure est forte, ce qui reste roussit. */
export function feuillageBrule(couleur: N<"vec4">, p: N<"vec3">): N<"vec4"> {
  const b = bruleEn(p);
  const rgb = mix(couleur.rgb, vec3(0.09, 0.06, 0.035), clamp(b.mul(1.6), 0, 1));
  return vec4(rgb, couleur.a.mul(float(1).sub(smoothstep(0.3, 0.55, b))));
}

/** Écorce calcinée. */
export function ecorceBrulee(couleur: N<"vec3">, p: N<"vec3">): N<"vec3"> {
  return mix(couleur, vec3(0.025, 0.022, 0.02), clamp(bruleEn(p).mul(1.4), 0, 0.95));
}

