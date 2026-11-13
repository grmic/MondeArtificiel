import * as THREE from "three/webgpu";
import { SkyMesh } from "three/addons/objects/SkyMesh.js";
import { hacher, unite } from "./bruit";

/** Durée du jour par saison (heures), comme le calendrier de la simulation (Calendrier.DureeJour). */
const DUREE_JOUR = [12, 15, 12, 9];
/** Hauteur du soleil à midi selon la saison (degrés). */
const MIDI = [58, 72, 58, 42];

/**
 * Ciel, soleil, lune, étoiles et brume. Le soleil se lève et se couche aux heures de la simulation (même durée du jour par saison),
 * à l'est, passe au sud à midi et se couche à l'ouest.
 */
export class Ciel {
  readonly ciel = new SkyMesh();
  readonly soleil = new THREE.DirectionalLight(0xffffff, 3);
  readonly lune = new THREE.DirectionalLight(0x8fa8d8, 0.25);
  readonly hemi = new THREE.HemisphereLight(0xbfd6ff, 0x5a5040, 0.9);
  private readonly etoiles: THREE.Points;
  private readonly materiauEtoiles: THREE.PointsMaterial;
  readonly direction = new THREE.Vector3();
  /** Clarté du moment (0 nuit, 1 plein jour). */
  clarte = 1;
  readonly brume = new THREE.Color();

  constructor(private readonly scene: THREE.Scene) {
    this.ciel.scale.setScalar(450000);
    this.ciel.turbidity.value = 5;
    this.ciel.rayleigh.value = 1.6;
    this.ciel.mieCoefficient.value = 0.004;
    this.ciel.mieDirectionalG.value = 0.8;
    this.ciel.cloudCoverage.value = 0.35;
    this.ciel.frustumCulled = false;
    scene.add(this.ciel, this.soleil, this.soleil.target, this.lune, this.lune.target, this.hemi);
    const n = 2500;
    const p = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const u = unite(hacher(i, 1, 99)) * 2 - 1;
      const a = unite(hacher(i, 2, 99)) * Math.PI * 2;
      const r = Math.sqrt(1 - u * u);
      p.set([Math.cos(a) * r * 300000, Math.abs(u) * 300000, Math.sin(a) * r * 300000], i * 3);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(p, 3));
    this.materiauEtoiles = new THREE.PointsMaterial({ color: 0xffffff, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0, depthWrite: false, fog: false });
    this.etoiles = new THREE.Points(g, this.materiauEtoiles);
    this.etoiles.frustumCulled = false;
    scene.add(this.etoiles);
    scene.fog = new THREE.FogExp2(0xb0c4d8, 0.00002);
  }

  /** Place le soleil pour l'instant `secondes` du monde, la caméra étant en `camera`. */
  regler(secondes: number, camera: THREE.Vector3): void {
    const jourAn = Math.floor(secondes / 86400) % 360;
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
    this.ciel.sunPosition.value.copy(this.direction);
    this.ciel.position.copy(camera);
    this.etoiles.position.copy(camera);

    // Clarté, comme la simulation : une heure d'aube et de crépuscule centrées sur le lever et le coucher.
    const coucher = lever + duree;
    this.clarte = heure <= lever - 0.5 || heure >= coucher + 0.5 ? 0 : heure >= lever + 0.5 && heure <= coucher - 0.5 ? 1 : heure < 12 ? heure - (lever - 0.5) : coucher + 0.5 - heure;
    const jour = Math.max(0, this.direction.y);
    this.soleil.position.copy(camera).addScaledVector(this.direction, 5000);
    this.soleil.target.position.copy(camera);
    this.soleil.intensity = 3.2 * Math.min(1, jour * 4);
    this.soleil.color.setRGB(1, 0.75 + 0.25 * Math.min(1, jour * 3), 0.55 + 0.45 * Math.min(1, jour * 3));
    this.lune.position.copy(camera).add(new THREE.Vector3(-0.3, 0.8, -0.5).multiplyScalar(5000));
    this.lune.target.position.copy(camera);
    this.lune.intensity = 0.35 * (1 - this.clarte);
    this.hemi.intensity = 0.08 + 0.9 * this.clarte;
    this.materiauEtoiles.opacity = Math.max(0, 1 - this.clarte * 2.5);
    this.ciel.showSunDisc.value = elevation > -0.05 ? 1 : 0;

    // Brume : couleur du ciel près de l'horizon, plus légère en altitude.
    const crepuscule = Math.max(0, 1 - Math.abs(this.direction.y) * 6) * (this.clarte > 0 ? 1 : 0);
    this.brume.setRGB(0.62, 0.72, 0.84).lerp(new THREE.Color(0.85, 0.6, 0.45), crepuscule * 0.6).multiplyScalar(0.04 + 0.96 * this.clarte);
    const fog = this.scene.fog as THREE.FogExp2;
    fog.color.copy(this.brume);
    fog.density = 0.000028 / (1 + Math.max(0, camera.y) / 1500);
  }
}
