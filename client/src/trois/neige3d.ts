import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Foule } from "./foule";
import type { Relief } from "./relief";

/** Traces de pas gardées au plus (les plus anciennes s'effacent les premières). */
const TRACES_MAX = 4000;
/** Bouffées de souffle visibles à la fois (une par humain proche). */
const SOUFFLES_MAX = 80;
/** Litières visibles à la fois (0.45.32). */
const LITIERES_MAX = 120;
/** Bit de l'état d'un humain : couché sur une litière (0.45.32). */
const SUR_LITIERE = 1 << 25;

interface Trace {
  x: number;
  y: number;
  z: number;
  cap: number;
  /** Taille de l'humain (m) : un enfant laisse un petit pied. */
  taille: number;
  /** Neige tombée ici (cm, compte de la vue) quand le pas a été fait. */
  cumul: number;
  /** Instant du monde où le pas a été fait (s). */
  le: number;
}

/**
 * Ce que la neige montre des humains (0.45.26), à l'affichage seulement : les pas qu'ils laissent dans la neige fraîche (un pas
 * tous les 70 cm environ pour un adulte, en alternant les pieds), que la neige suivante recouvre (deux centimètres suffisent) ou
 * que la fonte efface ; et la buée de leur souffle par temps froid. L'air expiré, à 32-34 °C et saturé d'eau, se condense en
 * brouillard visible quand l'air est assez froid : couramment sous 5 à 8 °C, davantage par air humide.
 */
export class TracesEtSouffle {
  readonly groupe = new THREE.Group();
  private readonly traces: THREE.InstancedMesh;
  private readonly souffles: THREE.InstancedMesh;
  /** 0.45.32 : la litière de branches, de fougères ou d'herbes sous qui dort par le froid. */
  private readonly litieres: THREE.InstancedMesh;
  private readonly matLitiere = new THREE.MeshBasicMaterial({
    color: 0x4a4230, transparent: true, opacity: 0.92, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -1, polygonOffsetUnits: -1,
  });
  private readonly matSouffle = new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.2, depthWrite: false });
  private readonly matTrace = new THREE.MeshBasicMaterial({
    color: 0x7d8aa0, transparent: true, opacity: 0.5, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2,
  });
  private liste: Trace[] = [];
  private readonly derniers = new Map<number, { x: number; z: number; gauche: boolean }>();
  /** Neige tombée sur la caméra depuis l'ouverture de la vue (cm, à peu près). */
  private cumul = 0;
  private refaireA = 0;
  private aRefaire = false;
  private readonly m = new THREE.Matrix4();
  private readonly q = new THREE.Quaternion();
  private readonly v = new THREE.Vector3();
  private readonly e = new THREE.Vector3();
  private static readonly HAUT = new THREE.Vector3(0, 1, 0);

  constructor(private readonly relief: Relief) {
    this.traces = new THREE.InstancedMesh(new THREE.PlaneGeometry(0.1, 0.25).rotateX(-Math.PI / 2), this.matTrace, TRACES_MAX);
    this.souffles = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(1, 1), this.matSouffle, SOUFFLES_MAX);
    // Un ovale aux bords irréguliers : des rameaux étalés, plus longs que larges.
    const g = new THREE.CircleGeometry(0.5, 18);
    const pos = g.attributes.position;
    for (let k = 1; k < pos.count; k++) {
      const f = 0.82 + 0.3 * unite(hacher(k, 311));
      pos.setXY(k, pos.getX(k) * f, pos.getY(k) * f);
    }
    this.litieres = new THREE.InstancedMesh(g.rotateX(-Math.PI / 2), this.matLitiere, LITIERES_MAX);
    for (const x of [this.traces, this.souffles, this.litieres]) {
      x.count = 0;
      x.frustumCulled = false;
      x.renderOrder = 4;
      this.groupe.add(x);
    }
  }

  /**
   * Une image. `secondes` : instant du monde ; `dtMonde` : secondes du monde écoulées depuis l'image d'avant ; `neigeIci` : force
   * de la neige qui tombe sur la caméra (0 à 1) ; `air` : température de l'air ici (°C) ; `lumiere` : couleur du ciel.
   */
  animer(foule: Foule, camera: THREE.Vector3, secondes: number, dtMonde: number, neigeIci: number, air: number | null, lumiere: THREE.Color, temps: number): void {
    // Neige qui tombe : de l'ordre d'un centimètre par heure sous une chute modérée, quatre sous une forte (comme la simulation).
    if (neigeIci > 0.02 && dtMonde > 0) this.cumul += (0.3 + 4.5 * neigeIci * neigeIci) * Math.min(dtMonde, 7200) / 3600;
    let nS = 0;
    let nL = 0;
    const froid = air === null ? 0 : Math.min(1, Math.max(0, (7 - air) / 9));
    for (const h of foule.tous()) {
      const dx = h.pied.x - camera.x, dz = h.pied.z - camera.z;
      const d2 = dx * dx + dz * dz;
      if (d2 > 150 * 150) continue;
      const etat = foule.etat(h.id);
      if (!etat) continue;
      // La litière sous le dormeur : centrée sur le corps, dans le sens du corps, à sa taille.
      if (etat.couche && h.s.etat & SUR_LITIERE && nL < LITIERES_MAX) {
        const cx = (h.tete.x + h.pied.x) / 2, cz = (h.tete.z + h.pied.z) / 2;
        const l = 0.55 * h.taille + 0.55, w = 0.75 + 0.15 * h.taille;
        this.q.setFromAxisAngle(TracesEtSouffle.HAUT, h.cap);
        this.m.compose(this.v.set(cx, Math.max(0, this.relief.hauteur(cx, cz)) + 0.02, cz), this.q, this.e.set(w, 1, l));
        this.litieres.setMatrixAt(nL++, this.m);
      }
      // Les pas : seulement en marchant debout, là où la neige tient.
      if (etat.enMarche && this.relief.neigeEn(h.pied.x, h.pied.z) >= 1) this.marcher(h.id, h.pied, h.cap, h.taille, secondes);
      else this.derniers.delete(h.id);
      // Le souffle : une bouffée à chaque expiration (12 à 20 par minute au repos, plus en marchant).
      if (froid > 0 && d2 < 40 * 40 && nS < SOUFFLES_MAX) {
        const periode = etat.enMarche ? 2.4 : 3.8;
        const t = (temps / periode + unite(hacher(h.id, 5))) % 1;
        if (t < 0.45) {
          const u = t / 0.45;
          const fond = u < 0.7 ? 1 : (1 - u) / 0.3;
          const r = (0.05 + 0.22 * u) * fond * Math.min(1.2, etat.taille / 1.6);
          const av = 0.1 + 0.3 * u;
          this.v.set(etat.tete.x + Math.sin(etat.cap) * av, etat.tete.y - 0.04 * etat.taille + 0.06 * u, etat.tete.z + Math.cos(etat.cap) * av);
          this.m.compose(this.v, this.q.identity(), this.e.set(r, r * 0.8, r));
          this.souffles.setMatrixAt(nS++, this.m);
        }
      }
    }
    this.litieres.count = nL;
    this.litieres.instanceMatrix.needsUpdate = nL > 0;
    this.matLitiere.color.setRGB(0.29, 0.26, 0.19).multiply(lumiere);
    this.souffles.count = nS;
    this.souffles.instanceMatrix.needsUpdate = nS > 0;
    this.matSouffle.opacity = 0.16 * froid;
    this.matSouffle.color.setRGB(lumiere.r, lumiere.g, lumiere.b).multiplyScalar(1.1);
    this.matTrace.color.setRGB(0.5, 0.56, 0.66).multiply(lumiere);
    // Les traces que la neige a recouvertes ou que la fonte a effacées disparaissent ; les autres se comblent peu à peu.
    if (this.aRefaire || performance.now() - this.refaireA > 500) this.refaire(secondes);
  }

  private marcher(id: number, pied: THREE.Vector3, cap: number, taille: number, secondes: number): void {
    const enjambee = 0.42 * taille;
    const d = this.derniers.get(id);
    if (d && Math.hypot(pied.x - d.x, pied.z - d.z) < enjambee) return;
    const gauche = !(d?.gauche ?? false);
    this.derniers.set(id, { x: pied.x, z: pied.z, gauche });
    const ecart = (gauche ? 1 : -1) * 0.06 * taille;
    const x = pied.x + Math.cos(cap) * ecart;
    const z = pied.z - Math.sin(cap) * ecart;
    this.liste.push({ x, y: Math.max(0, this.relief.hauteur(x, z)) + 0.015, z, cap, taille, cumul: this.cumul, le: secondes });
    if (this.liste.length > TRACES_MAX) this.liste.splice(0, this.liste.length - TRACES_MAX);
    this.aRefaire = true;
  }

  private refaire(secondes: number): void {
    this.refaireA = performance.now();
    this.aRefaire = false;
    // Deux centimètres de neige fraîche recouvrent un pas ; la fonte l'efface ; au bout de trois jours, le vent et le tassement aussi.
    this.liste = this.liste.filter((t) => this.cumul - t.cumul < 2 && secondes - t.le < 3 * 86400 && secondes >= t.le - 60
      && this.relief.neigeEn(t.x, t.z) >= 0.5);
    let n = 0;
    for (const t of this.liste) {
      const comble = 1 - (this.cumul - t.cumul) / 2;
      const s = (t.taille / 1.65) * (0.6 + 0.4 * comble);
      this.q.setFromAxisAngle(TracesEtSouffle.HAUT, t.cap);
      this.m.compose(this.v.set(t.x, t.y, t.z), this.q, this.e.set(s, 1, s));
      this.traces.setMatrixAt(n++, this.m);
    }
    this.traces.count = n;
    this.traces.instanceMatrix.needsUpdate = true;
  }

  /** On change de lieu (téléportation) : les traces d'ailleurs ne servent plus. */
  vider(): void {
    this.liste = [];
    this.derniers.clear();
    this.aRefaire = true;
  }
}
