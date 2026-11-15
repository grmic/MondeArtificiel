import type * as THREE from "three/webgpu";

/**
 * Envoie au GPU les `n` premiers éléments d'un tableau d'instances (0.41.6). Ne pas marquer ces tableaux « DynamicDrawUsage » :
 * avec le moteur WebGPU de Three.js, un tableau ainsi marqué est renvoyé en entier à chaque dessin (et à chaque cascade
 * d'ombre), ce qui faisait 25 Mo par image pour la seule végétation et bridait toutes les Qualités.
 */
export function envoyer(attribut: THREE.BufferAttribute, n: number): void {
  if (n <= 0) return;
  attribut.clearUpdateRanges();
  attribut.addUpdateRange(0, n * attribut.itemSize);
  attribut.needsUpdate = true;
}
