import * as THREE from "three/webgpu";
import {
  Fn, abs, attribute, cameraPosition, cos, float, fract, length, max, mix, normalize, positionGeometry, pow, sin, smoothstep, time,
  uniform, uv, vec2, vec3, vec4,
} from "three/tsl";
import { degatsDe, type ZoneJson } from "../meteo";
import { hacher, unite } from "./bruit";
import { meteoGPU } from "./meteo3d";
import { MANGROVE, MARAIS, type Relief } from "./relief";

type N<T extends string> = THREE.Node<T>;

/** Biomes (ordre de l'énumération de la simulation). */
const FOUGERES = 6, CONIFERES = 7, TROPICALE = 8, MONTAGNE = 10, FEUILLUS = 12, LANDE = 14;

/**
 * Hauteur des flammes (m) selon ce qui brûle : un feu de couronne dans les conifères dépasse les arbres (10 à 30 m) ; dans les
 * feuillus et la forêt humide, le feu court surtout au sol (2 à 6 m) ; la lande et les fougères flambent bas mais vite
 * (1 à 4 m) ; marais et mangrove brûlent à peine (ordres de grandeur des feux de forêt observés).
 */
function hauteurFlammes(biome: number): [number, number] {
  switch (biome) {
    case CONIFERES: return [8, 22];
    case FEUILLUS: return [3, 8];
    case TROPICALE: return [2, 6];
    case LANDE: return [1.5, 4.5];
    case FOUGERES: return [1, 3.5];
    case MONTAGNE: return [1, 3];
    case MARAIS:
    case MANGROVE: return [0.5, 1.5];
    default: return [1, 3];
  }
}

const MAX_FLAMMES = 24000;
const MAX_FUMEES = 12000;
const MAX_BRAISES = 3000;

/** Un maillage d'instances : un carré, et des attributs d'instance fixes (récrits seulement quand le feu change). */
class Instances {
  readonly maillage: THREE.Mesh;
  readonly geo = new THREE.InstancedBufferGeometry();
  readonly a: THREE.InstancedBufferAttribute;
  readonly b: THREE.InstancedBufferAttribute;
  n = 0;

  constructor(capacite: number, materiau: (a: N<"vec4">, b: N<"vec4">) => THREE.Material) {
    const quad = new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0);
    this.geo.copy(quad as unknown as THREE.InstancedBufferGeometry);
    this.a = new THREE.InstancedBufferAttribute(new Float32Array(capacite * 4), 4);
    this.b = new THREE.InstancedBufferAttribute(new Float32Array(capacite * 4), 4);
    this.geo.setAttribute("ia", this.a);
    this.geo.setAttribute("ib", this.b);
    this.geo.instanceCount = 0;
    this.maillage = new THREE.Mesh(this.geo, materiau(attribute("ia", "vec4"), attribute("ib", "vec4")));
    this.maillage.frustumCulled = false;
  }

  ajouter(a0: number, a1: number, a2: number, a3: number, b0: number, b1: number, b2: number, b3: number): void {
    if (this.n * 4 >= this.a.array.length) return;
    (this.a.array as Float32Array).set([a0, a1, a2, a3], this.n * 4);
    (this.b.array as Float32Array).set([b0, b1, b2, b3], this.n * 4);
    this.n++;
  }

  envoyer(): void {
    for (const att of [this.a, this.b]) {
      att.clearUpdateRanges();
      att.addUpdateRange(0, Math.max(4, this.n * 4));
      att.needsUpdate = true;
    }
    this.geo.instanceCount = this.n;
  }
}

/**
 * Les incendies dans la vue 3D (0.43.0, proposition D1). Chaque case en feu (0,8 km de côté) porte une ligne de flammes,
 * tournée en travers du vent du feu, sur le côté qui avance ; plus de flammes près de la caméra, de plus grandes et moins
 * nombreuses au loin. De chaque case monte une colonne de fumée qui se couche sous le vent (visible à des dizaines de km),
 * rougeoyante en bas la nuit ; des braises s'envolent près des flammes. La nuit, une lumière orange éclaire les alentours du
 * feu le plus proche. Le sol brûlé (charbon, puis reverdi peu à peu) et les arbres calcinés sont peints par les matériaux du sol
 * et des plantes, d'après la carte des terres (meteo3d.ts). Tout bouge sur le GPU ; les instances ne sont récrites que quand
 * le feu change (toutes les quelques secondes) ou que la caméra s'est beaucoup déplacée.
 */
export class Incendies3D {
  readonly groupe = new THREE.Group();
  private readonly flammes: Instances;
  private readonly fumees: Instances;
  private readonly braises: Instances;
  private readonly lumiere = new THREE.PointLight(0xff7a30, 0, 2500, 1.2);
  /** Éclat des flammes : elles brillent toujours autant ; l'œil adapté à la nuit les voit vives. */
  private readonly uEclat = uniform(1);
  /** Lumière du ciel (pour éclairer la fumée) et lueur du feu la nuit (0 le jour). */
  private readonly uCiel = uniform(new THREE.Vector3(0.5, 0.5, 0.5));
  private readonly uNuit = uniform(0);
  private zones: ZoneJson[] = [];
  private poseA = new THREE.Vector3(Infinity, 0, 0);
  private aRefaire = false;
  private plusProche: THREE.Vector3 | null = null;
  /** Fronts connus (m, monde), pour aller voir et pour les humains. */
  fronts: { x: number; z: number }[] = [];

  constructor(private readonly relief: Relief, private readonly largeur: number) {
    this.flammes = new Instances(MAX_FLAMMES, (a, b) => this.materiauFlamme(a, b));
    this.fumees = new Instances(MAX_FUMEES, (a, b) => this.materiauFumee(a, b));
    this.braises = new Instances(MAX_BRAISES, (a, b) => this.materiauBraise(a, b));
    this.fumees.maillage.renderOrder = 4;
    this.flammes.maillage.renderOrder = 3;
    this.groupe.add(this.flammes.maillage, this.fumees.maillage, this.braises.maillage, this.lumiere);
  }

  /** Flamme : un carré tourné vers la caméra autour de la verticale, qui vacille ; dégradé jaune-blanc, orange, rouge. */
  private materiauFlamme(a: N<"vec4">, b: N<"vec4">): THREE.Material {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false });
    const centre = a.xyz;
    const taille = a.w;
    const phase = b.x;
    const vers = cameraPosition.sub(centre);
    const droite = normalize(vec3(vers.z.negate(), 0, vers.x).add(vec3(0.0001, 0, 0)));
    const vacille = sin(time.mul(7.3).add(phase)).mul(sin(time.mul(12.1).add(phase.mul(2.3)))).mul(0.25).add(0.85);
    const p = positionGeometry;
    // Le haut de la flamme ondule et penche avec le vent du feu (b.yz).
    const penche = vec3(b.y, 0, b.z).mul(p.y.mul(p.y).mul(taille).mul(0.35));
    const ondule = droite.mul(sin(time.mul(5).add(phase).add(p.y.mul(3))).mul(p.y).mul(taille).mul(0.06));
    mat.positionNode = Fn(() => centre.add(droite.mul(p.x.mul(taille).mul(0.55))).add(vec3(0, p.y.mul(taille).mul(vacille), 0)).add(penche).add(ondule))();
    const q = uv();
    const v = q.y;
    const bord = abs(q.x.sub(0.5)).mul(2);
    const langue = sin(q.x.mul(17).add(time.mul(9)).add(phase)).mul(0.12).add(sin(q.x.mul(7).sub(time.mul(5.3))).mul(0.1));
    const forme = smoothstep(1, 0.55, bord.div(max(pow(float(1).sub(v), 0.6), 0.02)).add(langue.mul(v)))
      .mul(smoothstep(0, 0.08, v)).mul(smoothstep(1, 0.7, v));
    const coeur = smoothstep(0.55, 0, v).mul(smoothstep(0.7, 0, bord));
    const couleur = mix(mix(vec3(1, 0.35, 0.05), vec3(1, 0.62, 0.15), smoothstep(0.9, 0.3, v)), vec3(1, 0.93, 0.7), coeur);
    mat.colorNode = vec4(couleur.mul(this.uEclat), forme.mul(0.8));
    return mat;
  }

  /** Fumée : des bouffées qui montent, grossissent et se couchent sous le vent, puis s'effacent. */
  private materiauFumee(a: N<"vec4">, b: N<"vec4">): THREE.Material {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: THREE.DoubleSide, fog: true });
    const base = a.xyz;
    const echelle = a.w;
    // b.x : phase (partie décimale) et noirceur (partie entière, de 0 : fumée de bois, à 9 : cendre volcanique) ; b.w : durée d'une
    // bouffée (s), négative pour une bouffée qui ne monte pas (le chapeau d'un champignon, qui s'étale).
    const phase = fract(b.x);
    const sombre = b.x.floor().div(10).clamp(0, 0.9);
    const vent = vec2(b.y, b.z);
    const c = fract(time.div(abs(b.w)).add(phase));
    const montee = c.mul(echelle).mul(14).mul(b.w.sign().max(0));
    const derive = vent.mul(c.mul(c).mul(echelle).mul(30));
    const centre = base.add(vec3(derive.x, montee, derive.y));
    const taille = echelle.mul(c.mul(7).add(1));
    const vers = normalize(cameraPosition.sub(centre));
    const droite = normalize(vec3(vers.z.negate(), 0, vers.x).add(vec3(0.0001, 0, 0)));
    const haut = normalize(vers.cross(droite).negate());
    const p = positionGeometry;
    mat.positionNode = Fn(() => centre.add(droite.mul(p.x.mul(taille))).add(haut.mul(p.y.sub(0.5).mul(taille))))();
    const q = uv().sub(0.5);
    const r = length(q);
    const bosse = sin(q.x.mul(9).add(phase.mul(7))).mul(sin(q.y.mul(8).add(phase.mul(3)))).mul(0.08);
    const disque = smoothstep(0.5, 0.15, r.add(bosse));
    const gris = this.uCiel.mul(mix(float(0.75), float(0.45), c)).mul(float(1).sub(sombre.mul(0.8)));
    // La nuit, le bas du panache rougeoie de la lueur du feu.
    const lueur = vec3(1, 0.38, 0.1).mul(this.uNuit.mul(pow(float(1).sub(c), 4)).mul(this.uEclat).mul(0.25));
    const apparait = smoothstep(0, 0.08, c).mul(float(1).sub(smoothstep(0.55, 1, c)));
    mat.colorNode = vec4(gris.add(lueur), disque.mul(apparait).mul(0.45));
    return mat;
  }

  /** Braises : de petits points qui s'envolent en tournoyant et s'éteignent. */
  private materiauBraise(a: N<"vec4">, b: N<"vec4">): THREE.Material {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false });
    const phase = b.x;
    const c = fract(time.div(b.w).add(phase));
    const tourne = vec3(sin(time.mul(3).add(phase.mul(9))), 0, cos(time.mul(2.6).add(phase.mul(5)))).mul(c.mul(2.5));
    const centre = a.xyz.add(vec3(b.y, 0, b.z).mul(c.mul(14))).add(vec3(0, c.mul(a.w).mul(3), 0)).add(tourne);
    const vers = normalize(cameraPosition.sub(centre));
    const droite = normalize(vec3(vers.z.negate(), 0, vers.x).add(vec3(0.0001, 0, 0)));
    const p = positionGeometry;
    const s = float(0.12);
    mat.positionNode = Fn(() => centre.add(droite.mul(p.x.mul(s))).add(vec3(0, p.y.sub(0.5).mul(s), 0)))();
    const r = length(uv().sub(0.5));
    mat.colorNode = vec4(vec3(1, 0.45, 0.1).mul(this.uEclat).mul(2), smoothstep(0.5, 0.1, r).mul(float(1).sub(c)));
    return mat;
  }

  /** Nouvelles zones (relues du serveur). */
  definir(zones: ZoneJson[]): void {
    this.zones = zones;
    this.aRefaire = true;
    // Carte des terres : brûlé (dégâts qui restent), front, cendre, eau.
    meteoGPU.definirTerres((d, l, h) => {
      for (const z of zones) {
        const deg = degatsDe(z);
        const canal = z.type === "incendie" || z.type === "impact" ? 0 : z.type === "eruption" ? 2 : z.type === "crue" || z.type === "tsunami" ? 3 : -1;
        if (canal < 0) continue;
        z.cases.forEach((i, k) => {
          if (i < 0 || i >= l * h) return;
          // Les terres d'une crue ne sont sous l'eau que pendant la crue ; ensuite reste le limon (0.43.1), tant que les dégâts restent.
          // La vague n'a noyé les côtes qu'une fois arrivée (phase 1).
          const noye = z.active && (z.type !== "tsunami" || z.phase >= 1);
          const v = canal === 3 ? (noye ? 255 : z.active ? 0 : Math.round(90 * (deg[k] ?? 0) / 255)) : deg[k] ?? 255;
          d[i * 4 + canal] = Math.max(d[i * 4 + canal], v);
        });
        if (z.type === "incendie") for (const i of z.front) if (i >= 0 && i < l * h) {
          d[i * 4 + 1] = 255;
          d[i * 4] = Math.max(d[i * 4], 120);
        }
      }
      // Éruption (0.43.1) : au paroxysme, le cratère rougeoie ; une éruption effusive (VEI 0 à 2) épanche sa lave autour, sur
      // un à trois kilomètres (les coulées hawaïennes font de quelques centaines de mètres à des dizaines de kilomètres).
      for (const z of zones) {
        if (z.type !== "eruption" || !z.active || z.phase < 1) continue;
        const r = z.intensite <= 2 ? 1 + z.intensite : 0;
        for (let dy = -Math.ceil(r); dy <= Math.ceil(r); dy++) for (let dx = -Math.ceil(r); dx <= Math.ceil(r); dx++) {
          const x = z.x + dx, y = z.y + dy;
          if (x < 0 || y < 0 || x >= l || y >= h || dx * dx + dy * dy > r * r + 0.5) continue;
          // Des langues de lave, pas un disque : une case sur deux environ, d'autant moins qu'on s'éloigne du cratère.
          if ((dx || dy) && unite(hacher(z.id, x, y)) > 0.75 - 0.15 * Math.hypot(dx, dy)) continue;
          const i = y * l + x;
          d[i * 4] = 255;
          d[i * 4 + 1] = 255;
        }
      }
    });
    this.fronts = [];
    const m = this.relief.m;
    for (const z of zones) if (z.type === "incendie" && z.active) {
      for (const i of z.front) this.fronts.push({ x: (i % this.largeur + 0.5) * m, z: (Math.floor(i / this.largeur) + 0.5) * m });
    }
  }

  /** Une image. `nuit` : 0 le jour, 1 la nuit ; `ciel` : lumière du ciel ; `exposition` : adaptation de l'œil. */
  animer(camera: THREE.Vector3, nuit: number, ciel: THREE.Color, exposition: number, qualite: number): void {
    this.uEclat.value = 1.4 / Math.pow(Math.max(exposition, 0.01), 0.85);
    this.uCiel.value.set(ciel.r, ciel.g, ciel.b);
    this.uNuit.value = nuit;
    if (this.aRefaire || camera.distanceTo(this.poseA) > 1500) this.poser(camera, qualite);
    const p = this.plusProche;
    if (p && p.distanceTo(camera) < 6000) {
      this.lumiere.position.copy(p);
      const t = performance.now() / 1000;
      this.lumiere.intensity = 150 * (0.15 + 0.85 * nuit) * (0.85 + 0.15 * Math.sin(t * 9) * Math.sin(t * 4.3));
    } else this.lumiere.intensity = 0;
  }

  private poser(camera: THREE.Vector3, qualite: number): void {
    this.aRefaire = false;
    this.poseA.copy(camera);
    this.flammes.n = this.fumees.n = this.braises.n = 0;
    this.plusProche = null;
    const m = this.relief.m;
    const cases: { i: number; d: number; z: ZoneJson }[] = [];
    for (const z of this.zones) {
      if (z.type !== "incendie" || !z.active) continue;
      for (const i of z.front) {
        const x = (i % this.largeur + 0.5) * m, zz = (Math.floor(i / this.largeur) + 0.5) * m;
        cases.push({ i, d: Math.hypot(x - camera.x, zz - camera.z), z });
      }
    }
    cases.sort((a, b) => a.d - b.d);
    let meilleure = Infinity;
    for (const { i, d, z } of cases) {
      const cx = i % this.largeur, cz = Math.floor(i / this.largeur);
      const vx = Math.cos(z.direction), vz = Math.sin(z.direction);
      // La ligne de feu : en travers du vent, sur le côté qui avance (entre 55 et 95 % de la case sous le vent).
      const biome = this.relief.biomes[i] ?? 0;
      const [hMin, hMax] = hauteurFlammes(biome);
      const dense = d < 2500 ? 1 : d < 12000 ? 0.18 : 0.05;
      const n = Math.max(3, Math.round(180 * dense * qualite));
      const grandes = d < 2500 ? 1 : d < 12000 ? 2.2 : 4;
      for (let k = 0; k < n; k++) {
        const r = (u: number) => unite(hacher(i, k, u));
        const travers = (r(1) - 0.5) * m * 0.98;
        const avance = (0.55 + 0.4 * r(2) - 0.5) * m;
        const x = (cx + 0.5) * m + vx * avance - vz * travers;
        const zz = (cz + 0.5) * m + vz * avance + vx * travers;
        const y = this.relief.hauteur(x, zz);
        const h = (hMin + (hMax - hMin) * r(3) * r(3)) * grandes;
        this.flammes.ajouter(x, y - 0.2, zz, h, r(4) * 60, vx, vz, 0);
        if (d < 1500 && r(5) < 0.12 * qualite) this.braises.ajouter(x, y + h * 0.5, zz, h, r(6), vx, vz, 2 + 3 * r(7));
        const dc = Math.hypot(x - camera.x, zz - camera.z);
        if (dc < meilleure) {
          meilleure = dc;
          this.plusProche = new THREE.Vector3(x, y + 20, zz);
        }
      }
      // Fumée : un panache par case, seize bouffées (quatre au loin), de 30 à 60 m au départ.
      const nf = d < 20000 ? 16 : 4;
      for (let k = 0; k < nf; k++) {
        const r = (u: number) => unite(hacher(i, k, 200 + u));
        const x = (cx + 0.2 + 0.6 * r(1)) * m, zz = (cz + 0.2 + 0.6 * r(2)) * m;
        this.fumees.ajouter(x, this.relief.hauteur(x, zz) + 10, zz, 30 + 30 * r(3), r(4), vx, vz, 35 + 20 * r(5));
      }
    }
    this.poserPanaches(camera, qualite, () => meilleure, (v, d) => {
      meilleure = d;
      this.plusProche = v;
    });
    this.flammes.envoyer();
    this.fumees.envoyer();
    this.braises.envoyer();
  }

  /**
   * Panaches (0.43.1, E1) : celui d'une éruption (vapeur quand le volcan gronde, colonne de cendres au paroxysme, haute de 2 à
   * plus de 25 km selon l'indice d'explosivité, et fontaines de lave pour une éruption effusive), et le champignon d'un impact.
   */
  private poserPanaches(camera: THREE.Vector3, qualite: number, meilleure: () => number, plusProche: (v: THREE.Vector3, d: number) => void): void {
    const m = this.relief.m;
    for (const z of this.zones) {
      if (!z.active || (z.type !== "eruption" && z.type !== "impact")) continue;
      const x0 = (z.x + 0.5) * m, z0 = (z.y + 0.5) * m;
      const y0 = Math.max(0, this.relief.hauteur(x0, z0));
      const d = Math.hypot(x0 - camera.x, z0 - camera.z);
      const r = (k: number, u: number) => unite(hacher(z.id, k, 300 + u));
      if (z.type === "eruption") {
        const vei = Math.max(0, z.intensite);
        if (z.phase < 1) {
          // Le volcan gronde : un panache de vapeur et de gaz, blanchâtre.
          for (let k = 0; k < 8; k++) this.fumees.ajouter(x0 + (r(k, 1) - 0.5) * 200, y0 + 20, z0 + (r(k, 2) - 0.5) * 200, 40 + 30 * r(k, 3), r(k, 4), 0.2, 0.1, 40 + 20 * r(k, 5));
          continue;
        }
        // Colonne : la hauteur d'une bouffée au bout de sa montée vaut 14 fois sa taille.
        const echelle = Math.min(1800, 140 * Math.pow(1.6, vei));
        const n = Math.round((24 + 6 * vei) * Math.max(0.5, qualite));
        for (let k = 0; k < n; k++) {
          this.fumees.ajouter(x0 + (r(k, 1) - 0.5) * 300, y0 + 30, z0 + (r(k, 2) - 0.5) * 300, echelle * (0.6 + 0.6 * r(k, 3)), 7 + r(k, 4), 0.15, 0.1, 50 + 40 * r(k, 5));
        }
        // Fontaines et lueur du cratère.
        const fontaine = vei <= 2 ? 40 + 50 * vei : 25;
        const nf = d < 15000 ? 60 : 12;
        for (let k = 0; k < nf; k++) {
          const a = r(k, 6) * Math.PI * 2, e = r(k, 7) * 120;
          const x = x0 + Math.cos(a) * e, zz = z0 + Math.sin(a) * e;
          this.flammes.ajouter(x, Math.max(0, this.relief.hauteur(x, zz)) - 1, zz, fontaine * (0.3 + 0.7 * r(k, 8)), r(k, 9) * 60, 0, 0, 0);
          if (r(k, 10) < 0.3) this.braises.ajouter(x, y0 + fontaine * 0.4, zz, fontaine * 2, r(k, 11), 0, 0, 3 + 3 * r(k, 12));
        }
        if (d < meilleure()) plusProche(new THREE.Vector3(x0, y0 + 60, z0), d);
      } else {
        // Impact : la colonne monte, le chapeau s'étale à 8-15 km (champignon d'un souffle de l'ordre de la mégatonne).
        const mt = Math.max(0.01, z.intensite);
        const haut = Math.min(15000, 6000 + 2500 * Math.log10(1 + mt));
        const col = Math.round(18 * Math.max(0.5, qualite));
        for (let k = 0; k < col; k++) {
          this.fumees.ajouter(x0 + (r(k, 1) - 0.5) * 600, y0 + 50, z0 + (r(k, 2) - 0.5) * 600, (haut / 14) * (0.7 + 0.5 * r(k, 3)), 5 + r(k, 4), 0, 0, 60 + 30 * r(k, 5));
        }
        for (let k = 0; k < 30; k++) {
          const a = (k / 30) * Math.PI * 2 + r(k, 6);
          const ray = haut * 0.2 * r(k, 7);
          this.fumees.ajouter(x0 + Math.cos(a) * ray, y0 + haut * (0.85 + 0.2 * r(k, 8)), z0 + Math.sin(a) * ray, haut * 0.09 * (0.7 + 0.6 * r(k, 9)), 6 + r(k, 10),
            Math.cos(a) * 0.25, Math.sin(a) * 0.25, -(80 + 40 * r(k, 11)));
        }
      }
    }
  }

  /** Le front le plus proche d'un point (m), ou null. */
  frontProche(x: number, z: number): { x: number; z: number } | null {
    let best: { x: number; z: number } | null = null;
    let d = Infinity;
    for (const f of this.fronts) {
      const e = Math.hypot(f.x - x, f.z - z);
      if (e < d) {
        d = e;
        best = f;
      }
    }
    return best;
  }
}

