import * as THREE from "three/webgpu";
import { abs, exp, float, fract, mix, pow, sin, smoothstep, time, uniform, uv, vec3, vec4 } from "three/tsl";
import type { EnCoursJson, ZoneJson } from "../meteo";
import { hacher, unite } from "./bruit";

/**
 * Les phénomènes du ciel (0.43.1, E1) : comète et sa queue, aurore, boule de feu. Ils sont posés loin, sur la voûte, autour de la
 * caméra, et ne se voient que la nuit (la comète et l'aurore) ou un instant (la boule de feu). Ils disent aussi où regarder :
 * les humains proches lèvent les yeux vers eux (H1).
 *
 * Ordres de grandeur : une grande comète montre une queue de 10 à 40° de ciel, tournée à l'opposé du soleil ; une aurore vue loin
 * des pôles (grand orage magnétique, 1859, 1921, mai 2024) est surtout rouge (oxygène à 200-400 km d'altitude), le vert ne
 * paraissant que bas sur l'horizon nord ; une boule de feu traverse le ciel en 2 à 6 secondes.
 */
const R = 200000;

export class Phenomenes3D {
  readonly groupe = new THREE.Group();
  private readonly comete: THREE.Mesh;
  private readonly aurore: THREE.Mesh;
  private readonly bolide: THREE.Mesh;
  private readonly uEclat = uniform(1);
  private readonly uNuit = uniform(0);
  private readonly uAurore = uniform(0);
  private readonly uComete = uniform(0);
  private readonly uBolide = uniform(0);
  private idComete = -1;
  private dirComete = new THREE.Vector3();
  /** Boules de feu déjà vues (une seule traversée chacune), et celle en cours : départ, arrivée, début (ms). */
  private readonly vus = new Set<number>();
  private traversee: { a: THREE.Vector3; b: THREE.Vector3; debut: number; duree: number } | null = null;
  /** Éclat d'une boule de feu (0 à 1), pour éclairer un instant le paysage. */
  eclat = 0;
  /** Où les humains regardent (direction depuis eux, ou null). */
  regard: THREE.Vector3 | null = null;

  constructor() {
    this.comete = this.creerComete();
    this.aurore = this.creerAurore();
    this.bolide = this.creerBolide();
    for (const m of [this.comete, this.aurore, this.bolide]) {
      m.frustumCulled = false;
      m.renderOrder = -0.5;
      m.visible = false;
      this.groupe.add(m);
    }
  }

  /** Comète : tête brillante et queue qui s'élargit et pâlit, sur un carré (x : travers, y : de la tête au bout de la queue). */
  private creerComete(): THREE.Mesh {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, fog: false, side: THREE.DoubleSide });
    const q = uv();
    const y = q.y;
    const x = q.x.sub(0.5).mul(2);
    const largeur = y.mul(0.85).add(0.04);
    const queue = exp(x.div(largeur).pow(2).negate().mul(3)).mul(pow(float(1).sub(y), 1.6)).mul(smoothstep(0, 0.03, y));
    const tete = exp(x.mul(x).add(y.mul(y).mul(25)).negate().mul(900));
    const couleur = mix(vec3(0.75, 0.85, 1), vec3(0.9, 0.95, 1), tete);
    mat.colorNode = vec4(couleur.mul(this.uEclat).mul(this.uComete).mul(this.uNuit), queue.mul(0.5).add(tete.mul(3)));
    return new THREE.Mesh(new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0), mat);
  }

  /** Aurore : rideaux qui ondulent sur la moitié nord du ciel, rouges en haut, un peu de vert tout en bas. */
  private creerAurore(): THREE.Mesh {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, fog: false, side: THREE.BackSide });
    const q = uv();
    const x = q.x;
    const t = time.mul(0.15);
    // Plis du rideau : ils se déplacent lentement le long de l'arc, et des rayons verticaux s'allument et s'éteignent.
    const pli = sin(x.mul(23).add(sin(x.mul(5).add(t)).mul(3)).add(t.mul(2)));
    const rayons = fract(sin(x.mul(900).floor().mul(12.9898)).mul(43758.5453)).mul(0.6).add(sin(t.mul(7).add(x.mul(160))).mul(0.2)).add(0.4);
    const bas = smoothstep(0, 0.12, q.y).mul(smoothstep(1, 0.55, q.y));
    const voile = abs(pli).mul(0.6).add(0.4).mul(rayons).mul(bas);
    const vert = smoothstep(0.35, 0.05, q.y);
    const couleur = mix(vec3(0.95, 0.12, 0.25), vec3(0.15, 1, 0.45), vert.mul(0.6));
    mat.colorNode = vec4(couleur.mul(this.uEclat).mul(0.5), voile.mul(this.uAurore).mul(this.uNuit));
    const g = new THREE.CylinderGeometry(R, R, R * 0.8, 96, 1, true, Math.PI / 2, Math.PI).translate(0, R * 0.55, 0);
    return new THREE.Mesh(g, mat);
  }

  /** Boule de feu : tête blanc-vert et traînée orangée. */
  private creerBolide(): THREE.Mesh {
    const mat = new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, fog: false, side: THREE.DoubleSide });
    const q = uv();
    const x = q.x.sub(0.5).mul(2);
    const y = q.y;
    const trainee = exp(x.mul(x).negate().mul(30)).mul(pow(float(1).sub(y), 2.5));
    const tete = exp(x.mul(x).add(y.mul(y).mul(4)).negate().mul(60));
    const couleur = mix(vec3(1, 0.55, 0.2), vec3(0.85, 1, 0.9), tete);
    mat.colorNode = vec4(couleur.mul(this.uEclat).mul(4), trainee.add(tete.mul(2)).mul(this.uBolide));
    return new THREE.Mesh(new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0), mat);
  }

  /**
   * Une image. `continent` et `zones` : ce qui est en cours ; `soleil` : direction du soleil ; `nuit` : 0 le jour, 1 la nuit noire ;
   * `exposition` : adaptation de l'œil ; `maintenant` : ms.
   */
  animer(camera: THREE.Vector3, en: EnCoursJson | null, zones: ZoneJson[], soleil: THREE.Vector3, nuit: number, exposition: number, maintenant: number): void {
    this.uEclat.value = 1.2 / Math.pow(Math.max(exposition, 0.01), 0.85);
    this.uNuit.value = nuit;
    this.regard = null;
    const comete = en?.continent.find((c) => c.type === "comete");
    const aurore = en?.continent.some((c) => c.type === "aurore") ?? false;

    // La comète : une place fixe dans le ciel pour toute son apparition, du côté opposé au soleil de midi, haute de 20 à 45°.
    if (comete) {
      if (comete.id !== this.idComete) {
        this.idComete = comete.id;
        const az = unite(hacher(comete.id, 1, 61)) * Math.PI * 2;
        const el = (20 + 25 * unite(hacher(comete.id, 2, 61))) * Math.PI / 180;
        this.dirComete.set(Math.cos(az) * Math.cos(el), Math.sin(el), Math.sin(az) * Math.cos(el));
      }
      this.uComete.value += (1 - this.uComete.value) * 0.02;
      const d = this.dirComete;
      // Queue à l'opposé du soleil, dans le plan du ciel.
      const loin = soleil.clone().multiplyScalar(-1);
      const t = loin.sub(d.clone().multiplyScalar(loin.dot(d)));
      if (t.lengthSq() < 1e-6) t.set(0, 1, 0);
      t.normalize();
      const droite = new THREE.Vector3().crossVectors(t, d).normalize();
      const longueur = R * 0.45, largeur = R * 0.12;
      this.comete.matrix.makeBasis(droite.multiplyScalar(largeur), t.multiplyScalar(longueur), d.clone());
      this.comete.matrix.setPosition(camera.clone().addScaledVector(d, R));
      this.comete.matrixAutoUpdate = false;
      this.comete.matrixWorldNeedsUpdate = true;
      this.comete.visible = nuit > 0.05;
      if (nuit > 0.5) this.regard = d.clone();
    } else {
      this.comete.visible = false;
      this.uComete.value = 0;
    }

    this.uAurore.value += ((aurore ? 1 : 0) - this.uAurore.value) * 0.02;
    this.aurore.visible = this.uAurore.value > 0.01 && nuit > 0.05;
    this.aurore.position.set(camera.x, 0, camera.z);
    if (aurore && nuit > 0.5 && !this.regard) this.regard = new THREE.Vector3(0, 0.45, -1).normalize();

    // Boule de feu : une traversée de quelques secondes la première fois qu'on la voit, du côté de sa chute.
    for (const z of zones) {
      if (z.type !== "bolide" || !z.active || this.vus.has(z.id)) continue;
      this.vus.add(z.id);
      const az = unite(hacher(z.id, 3, 61)) * Math.PI * 2;
      const vers = Math.atan2((z.y + 0.5) * 800 - camera.z, (z.x + 0.5) * 800 - camera.x);
      const dirA = new THREE.Vector3(Math.cos(az), 0.9, Math.sin(az)).normalize();
      const dirB = new THREE.Vector3(Math.cos(vers), 0.12, Math.sin(vers)).normalize();
      this.traversee = { a: dirA, b: dirB, debut: maintenant, duree: 2500 + 3500 * unite(hacher(z.id, 4, 61)) };
    }
    const tr = this.traversee;
    if (tr) {
      const u = (maintenant - tr.debut) / tr.duree;
      if (u >= 1) {
        this.traversee = null;
        this.bolide.visible = false;
        this.eclat = 0;
        this.uBolide.value = 0;
      } else {
        const tete = tr.a.clone().lerp(tr.b, u).normalize();
        const avant = tr.a.clone().lerp(tr.b, Math.max(0, u - 0.08)).normalize();
        const t = avant.sub(tete.clone().multiplyScalar(avant.dot(tete)));
        if (t.lengthSq() < 1e-8) t.copy(tr.a).sub(tr.b);
        t.normalize();
        const droite = new THREE.Vector3().crossVectors(t, tete).normalize();
        this.bolide.matrix.makeBasis(droite.multiplyScalar(R * 0.012), t.multiplyScalar(R * 0.18), tete.clone());
        this.bolide.matrix.setPosition(camera.clone().addScaledVector(tete, R));
        this.bolide.matrixAutoUpdate = false;
        this.bolide.matrixWorldNeedsUpdate = true;
        this.bolide.visible = true;
        // Elle s'allume, flamboie (parfois éclate) puis s'éteint.
        const f = Math.min(1, u * 6) * (u > 0.85 ? (1 - u) / 0.15 : 1) * (1 + 0.6 * Math.max(0, Math.sin(u * 40)));
        this.uBolide.value = f;
        this.eclat = 0.25 * f;
        this.regard = tete.clone();
      }
    }
  }
}
