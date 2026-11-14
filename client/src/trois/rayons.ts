import * as THREE from "three/webgpu";
import { Fn, Loop, float, frameId, interleavedGradientNoise, max, screenCoordinate, screenUV, smoothstep, uniform, vec2 } from "three/tsl";

/** Pas le long du trait vers le soleil. */
const PAS = 40;

/**
 * Rayons du soleil dans la brume (0.41.6, proposition B1), à la manière de Mitchell (« Volumetric Light Scattering as a
 * Post-Process », GPU Gems 3, 2007) : chaque pixel regarde, le long du trait qui le relie au soleil sur l'écran, combien de ciel
 * dégagé laisse passer la lumière. Le ciel écrit sa clarté (moins derrière les nuages) dans la sortie « extra » de la passe ; les
 * troncs, les feuillages, le relief et les nuages découpent donc les faisceaux. Leur force suit l'épaisseur de la brume (fortes
 * au petit matin dans les fonds), la hauteur du soleil (plus longs quand il est bas) et ce qu'on regarde (vers le soleil).
 */
export class Rayons {
  /** Position du soleil à l'écran (0 à 1, origine en haut à gauche). */
  private readonly uSoleil = uniform(new THREE.Vector2(0.5, 0.5));
  /** Force des rayons, couleur du soleil comprise. */
  private readonly uCouleur = uniform(new THREE.Vector3());
  private readonly ndc = new THREE.Vector3();
  private readonly avant = new THREE.Vector3();

  /** Ajout de lumière (avant exposition) à partir du masque du ciel (canal vert de « extra »). */
  noeud(extra: THREE.TextureNode, _camera: THREE.Camera): THREE.Node<"vec3"> {
    return Fn(() => {
      const uv = screenUV;
      const vers = this.uSoleil.sub(uv);
      const bruit = interleavedGradientNoise(screenCoordinate.xy.add(float(frameId).mod(32).mul(7.13)));
      const somme = float(0).toVar();
      const poids = float(1).toVar();
      Loop(PAS, ({ i }) => {
        const s = uv.add(vers.mul(float(i).add(bruit).div(PAS)));
        const dedans = smoothstep(0, 0.02, s.x).mul(smoothstep(1, 0.98, s.x)).mul(smoothstep(0, 0.02, s.y)).mul(smoothstep(1, 0.98, s.y));
        somme.addAssign(extra.sample(s.clamp(0, 1)).level(float(0)).g.mul(dedans).mul(poids));
        poids.mulAssign(0.965);
      });
      // Les rayons s'éteignent loin du soleil sur l'écran.
      const ecart = max(vers.mul(vec2(1.6, 1)).length(), 0.0001);
      const pres = float(1).sub(smoothstep(0.15, 1.1, ecart));
      return this.uCouleur.mul(somme.div(PAS).mul(pres));
    })() as unknown as THREE.Node<"vec3">;
  }

  /**
   * À chaque image : place du soleil à l'écran et force. `brume` : épaisseur relative de la brume (0 à 1), `couleur` : lumière du
   * soleil après la traversée de l'air, `soleil` : direction du soleil.
   */
  regler(camera: THREE.PerspectiveCamera, soleil: THREE.Vector3, couleur: THREE.Vector3, brume: number): void {
    this.ndc.copy(camera.position).addScaledVector(soleil, 10000).project(camera);
    camera.getWorldDirection(this.avant);
    const face = Math.max(0, Math.min(1, (this.avant.dot(soleil) - 0.1) / 0.5));
    const visible = this.ndc.z < 1 ? face : 0;
    this.uSoleil.value.set(this.ndc.x * 0.5 + 0.5, 0.5 - this.ndc.y * 0.5);
    // Soleil bas : rayons plus longs et plus visibles ; brume du matin : plus forts. Couper sous l'horizon.
    const bas = soleil.y < -0.02 ? 0 : 1 - Math.min(1, Math.max(0, soleil.y - 0.05) / 0.9) * 0.6;
    const force = visible * bas * (0.035 + 0.11 * brume);
    this.uCouleur.value.copy(couleur).multiplyScalar(force);
  }
}
