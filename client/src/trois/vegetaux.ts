import * as THREE from "three/webgpu";
import { mergeGeometries } from "three/addons/utils/BufferGeometryUtils.js";
import type { Zone } from "./matieres";

/**
 * Formes des plantes texturées (0.41.5) : troncs et stipes d'écorce photographiée, feuillage en cartes découpées dans l'atlas
 * (feuillages.webp). Chaque modèle est haut de 1 : `positionGeometry.y` dit où l'on est sur la plante, ce dont le vent se sert ;
 * l'attribut `agite` (0 au point d'attache, 1 au bout d'une feuille) règle le frémissement.
 */

/** Petit tirage reproductible (les formes sont les mêmes à chaque ouverture). */
function tirage(graine: number): () => number {
  let s = graine >>> 0 || 1;
  return () => {
    s ^= s << 13;
    s ^= s >>> 17;
    s ^= s << 5;
    return (s >>> 0) / 4294967296;
  };
}

/** Ajoute l'attribut `agite` (valeur fixe) et garde l'index (tous les morceaux d'un modèle doivent avoir les mêmes attributs). */
function agiter(g: THREE.BufferGeometry, valeur: (i: number) => number): THREE.BufferGeometry {
  const n = g.getAttribute("position").count;
  const a = new Float32Array(n);
  for (let i = 0; i < n; i++) a[i] = valeur(i);
  g.setAttribute("agite", new THREE.BufferAttribute(a, 1));
  return g;
}

/** Tronc (ou branche) : cylindre de r0 à r1 entre deux points, écorce répétée selon la longueur. */
export function tronc(a: THREE.Vector3, b: THREE.Vector3, r0: number, r1: number, segments = 7, repetition = 14): THREE.BufferGeometry {
  const l = a.distanceTo(b);
  const g = new THREE.CylinderGeometry(r1, r0, l, segments, 3, true);
  const uv = g.getAttribute("uv");
  for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getX(i) * 2, uv.getY(i) * l * repetition);
  g.translate(0, l / 2, 0);
  const dir = b.clone().sub(a).normalize();
  g.applyQuaternion(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir));
  g.translate(a.x, a.y, a.z);
  return agiter(g, () => 0);
}

/**
 * Carte de feuillage courbée : de `base` dans la direction `dir`, longue de `l`, large de `largeur`, dans le plan qui contient `dir`
 * et `cote`. `courbe` abaisse le bout (fronde qui retombe). L'image de la zone est posée base en bas (bas de la zone = attache),
 * sauf `horizontal` : base à gauche (rameaux d'araucaria). Normales : `normale(p)` donne la normale voulue en chaque point.
 */
export function carte(
  zone: Zone, base: THREE.Vector3, dir: THREE.Vector3, cote: THREE.Vector3, l: number, largeur: number,
  options: { courbe?: number; segments?: number; horizontal?: boolean; v0?: number; v1?: number; normale?: (p: THREE.Vector3) => THREE.Vector3 } = {},
): THREE.BufferGeometry {
  const seg = options.segments ?? 1;
  const courbe = options.courbe ?? 0;
  const d = dir.clone().normalize();
  const c = cote.clone().sub(d.clone().multiplyScalar(cote.dot(d))).normalize();
  const plan = new THREE.Vector3().crossVectors(d, c).normalize();
  const positions: number[] = [];
  const normales: number[] = [];
  const uvs: number[] = [];
  const agite: number[] = [];
  const indices: number[] = [];
  const [zx, zy, zw, zh] = zone;
  const v0 = options.v0 ?? 0;
  const v1 = options.v1 ?? 1;
  const p = new THREE.Vector3();
  const bas = new THREE.Vector3(0, -1, 0);
  for (let k = 0; k <= seg; k++) {
    const t = k / seg;
    // Le long de la fronde : la courbe abaisse le bout (chute en t²).
    const centre = base.clone().addScaledVector(d, l * t).addScaledVector(bas, courbe * l * t * t);
    for (const s of [-0.5, 0.5]) {
      p.copy(centre).addScaledVector(c, s * largeur);
      positions.push(p.x, p.y, p.z);
      const n = options.normale ? options.normale(p) : plan;
      normales.push(n.x, n.y, n.z);
      if (options.horizontal) {
        // Base à gauche : u le long, v en travers (bande v0..v1 de la zone).
        uvs.push(zx + zw * t, zy + zh * (v0 + (v1 - v0) * (s + 0.5)));
      } else {
        // Base en bas de la zone (v grand), bout en haut.
        uvs.push(zx + zw * (s + 0.5), zy + zh * (1 - t));
      }
      agite.push(t);
    }
  }
  for (let k = 0; k < seg; k++) {
    const a = k * 2;
    indices.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  g.setAttribute("normal", new THREE.Float32BufferAttribute(normales, 3));
  g.setAttribute("uv", new THREE.Float32BufferAttribute(uvs, 2));
  g.setAttribute("agite", new THREE.Float32BufferAttribute(agite, 1));
  g.setIndex(indices);
  return g;
}

/** Normale « en dôme » : vers l'extérieur du centre donné et vers le ciel (un feuillage éclairé comme une masse, sans taches noires). */
function dome(centre: THREE.Vector3, ciel = 0.8): (p: THREE.Vector3) => THREE.Vector3 {
  return (p) => p.clone().sub(centre).setY(0).normalize().add(new THREE.Vector3(0, ciel, 0)).normalize();
}

function fondre(morceaux: THREE.BufferGeometry[]): THREE.BufferGeometry {
  return mergeGeometries(morceaux, false)!;
}

/** Un modèle en deux groupes : écorce (0) puis feuillage (1). */
function deuxMatieres(bois: THREE.BufferGeometry[], feuilles: THREE.BufferGeometry[]): THREE.BufferGeometry {
  return mergeGeometries([fondre(bois), fondre(feuilles)], true)!;
}

const V = (x: number, y: number, z: number) => new THREE.Vector3(x, y, z);

/** Araucaria : fût droit et nu, rameaux en étages vers le haut (en parasol chez les vieux), rameaux en corde d'écailles. */
export function araucaria(zones: Record<string, Zone>): THREE.BufferGeometry {
  const r = tirage(11);
  const bois = [tronc(V(0, 0, 0), V(0, 1, 0), 0.02, 0.005, 8)];
  const feuilles: THREE.BufferGeometry[] = [];
  const z = zones.araucaria;
  const etages = 14;
  for (let e = 0; e < etages; e++) {
    const y = 0.46 + (0.51 * e) / (etages - 1);
    const u = (y - 0.46) / 0.54;
    const l = 0.06 + 0.22 * Math.pow(1 - u, 0.6);
    for (let k = 0; k < 6; k++) {
      const a = ((k + e * 0.5) / 6) * Math.PI * 2 + r() * 0.4;
      const dir = V(Math.cos(a), 0.12 + 0.35 * u, Math.sin(a)).normalize();
      const base = V(0, y, 0);
      const bande = r() < 0.5 ? [0.11, 0.39] : [0.61, 0.89];
      const largeur = l * 0.3 + 0.02;
      const n = dome(V(0, y - 0.05, 0), 0.9);
      // Deux cartes croisées par rameau : à plat et debout, pour qu'il ait du volume vu de partout.
      feuilles.push(carte(z, base, dir, V(-dir.z, 0, dir.x), l, largeur, { horizontal: true, v0: bande[0], v1: bande[1], courbe: -0.12, segments: 3, normale: n }));
      feuilles.push(carte(z, base, dir, V(0, 1, 0), l, largeur, { horizontal: true, v0: bande[0], v1: bande[1], courbe: -0.12, segments: 3, normale: n }));
    }
  }
  return deuxMatieres(bois, feuilles);
}

/** Feuillu du Crétacé (magnolia, laurier) : tronc, trois maîtresses branches, houppier de rameaux feuillus. */
export function feuillu(zones: Record<string, Zone>): THREE.BufferGeometry {
  const r = tirage(23);
  const bois = [tronc(V(0, 0, 0), V(0.01, 0.52, 0), 0.04, 0.024, 8)];
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI * 2 + 0.4;
    bois.push(tronc(V(0.01, 0.46, 0), V(Math.cos(a) * 0.17, 0.74, Math.sin(a) * 0.17), 0.02, 0.008, 6));
  }
  const feuilles: THREE.BufferGeometry[] = [];
  const centre = V(0, 0.7, 0);
  for (let k = 0; k < 26; k++) {
    // Points sur un ellipsoïde ; chaque rameau part vers l'extérieur depuis l'intérieur du houppier.
    const a = r() * Math.PI * 2;
    const h = r() * 1.6 - 0.6;
    const ext = V(Math.cos(a) * Math.sqrt(1 - Math.min(1, h * h)), h, Math.sin(a) * Math.sqrt(1 - Math.min(1, h * h)));
    const base = centre.clone().add(V(ext.x * 0.12, ext.y * 0.08, ext.z * 0.12));
    const dir = ext.clone().add(V(0, 0.5, 0)).normalize();
    const cote = V(-ext.z, r() - 0.5, ext.x);
    const l = 0.3 + 0.12 * r();
    feuilles.push(carte(zones.feuillu, base, dir, cote, l, l, { normale: dome(V(0, 0.62, 0), 0.7), segments: 2 }));
  }
  return deuxMatieres(bois, feuilles);
}

/** Cycas : stipe épais et court, couronne de frondes raides en rosette. */
export function cycas(zones: Record<string, Zone>): THREE.BufferGeometry {
  const r = tirage(37);
  const bois = [tronc(V(0, 0, 0), V(0, 0.42, 0), 0.085, 0.07, 9, 4)];
  const feuilles: THREE.BufferGeometry[] = [];
  const haut = V(0, 0.42, 0);
  for (let k = 0; k < 18; k++) {
    const a = (k / 18) * Math.PI * 2 + r() * 0.3;
    const monte = 0.6 + 0.6 * r();
    const dir = V(Math.cos(a), monte, Math.sin(a)).normalize();
    const l = 0.55 + 0.15 * r();
    feuilles.push(carte(zones.cycas, haut, dir, V(-Math.sin(a), 0, Math.cos(a)), l, l * 0.26, { courbe: 0.35, segments: 5, normale: dome(V(0, 0.3, 0), 1.2) }));
  }
  return deuxMatieres(bois, feuilles);
}

/** Fougère arborescente : stipe fin et haut, couronne de grandes frondes arquées. */
export function fougereArborescente(zones: Record<string, Zone>): THREE.BufferGeometry {
  const r = tirage(41);
  const bois = [tronc(V(0, 0, 0), V(0.02, 0.8, 0), 0.03, 0.022, 7, 6)];
  const feuilles: THREE.BufferGeometry[] = [];
  const haut = V(0.02, 0.8, 0);
  for (let k = 0; k < 14; k++) {
    const a = (k / 14) * Math.PI * 2 + r() * 0.4;
    const dir = V(Math.cos(a), 0.5 + 0.7 * r(), Math.sin(a)).normalize();
    const l = 0.42 + 0.12 * r();
    const z = [zones.fougere, zones.fougere2, zones.fougere3][k % 3];
    feuilles.push(carte(z, haut, dir, V(-Math.sin(a), 0, Math.cos(a)), l, l * 0.26, { courbe: 0.55, segments: 5, normale: dome(V(0, 0.7, 0), 1.2) }));
  }
  return deuxMatieres(bois, feuilles);
}

/** Touffe de fougères (au sol) : frondes arquées depuis le pied. */
export function touffeFougere(zones: Record<string, Zone>, nombre = 9, graine = 53): THREE.BufferGeometry {
  const r = tirage(graine);
  const feuilles: THREE.BufferGeometry[] = [];
  for (let k = 0; k < nombre; k++) {
    const a = (k / nombre) * Math.PI * 2 + r() * 0.5;
    const dir = V(Math.cos(a) * (0.35 + 0.3 * r()), 1, Math.sin(a) * (0.35 + 0.3 * r())).normalize();
    const l = 0.9 + 0.25 * r();
    const z = [zones.fougere, zones.fougere2, zones.fougere3][k % 3];
    feuilles.push(carte(z, V(0, 0, 0), dir, V(-Math.sin(a), 0, Math.cos(a)), l, l * 0.25, { courbe: 0.45, segments: 5, normale: dome(V(0, -0.3, 0), 1.4) }));
  }
  return fondre(feuilles);
}

/** Buisson : cartes de feuillage croisées. */
export function buisson(zones: Record<string, Zone>): THREE.BufferGeometry {
  const r = tirage(67);
  const feuilles: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 7; k++) {
    const a = (k / 7) * Math.PI + r() * 0.3;
    const pied = V((r() - 0.5) * 0.3, 0, (r() - 0.5) * 0.3);
    const dir = V((r() - 0.5) * 0.4, 1, (r() - 0.5) * 0.4).normalize();
    feuilles.push(carte(zones.buisson, pied, dir, V(Math.cos(a), 0, Math.sin(a)), 0.95, 0.95, { normale: dome(V(0, 0.1, 0), 1.0) }));
  }
  return fondre(feuilles);
}

/** Prêles : trois cartes debout, croisées. */
export function preles(zones: Record<string, Zone>): THREE.BufferGeometry {
  const feuilles: THREE.BufferGeometry[] = [];
  for (let k = 0; k < 3; k++) {
    const a = (k / 3) * Math.PI;
    feuilles.push(carte(zones.preles, V(0, 0, 0), V(0, 1, 0), V(Math.cos(a), 0, Math.sin(a)), 1, 0.5, { normale: dome(V(0, 0, 0), 2) }));
  }
  return fondre(feuilles);
}

/** Litière : une carte à plat de feuilles mortes. */
export function litiere(zones: Record<string, Zone>): THREE.BufferGeometry {
  return carte(zones.litiere, V(0, 0, -0.5), V(0, 0, 1), V(1, 0, 0), 1, 1, { normale: () => V(0, 1, 0) });
}

/** Rocher : un polyèdre bosselé, la texture posée par projection selon l'orientation de chaque face. */
export function rocher(): THREE.BufferGeometry {
  const g = new THREE.DodecahedronGeometry(0.5, 1);
  const p = g.getAttribute("position");
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i);
    const y = p.getY(i);
    const z = p.getZ(i);
    // Bosses : fonction de la position seule (les sommets partagés bougent ensemble, sans fente).
    const k = 1 + 0.1 * Math.sin(x * 13 + y * 7) * Math.cos(z * 11 - y * 5);
    p.setXYZ(i, x * k, y * k * 0.7, z * k);
  }
  const n = g.index ? g.toNonIndexed() : g;
  n.computeVertexNormals();
  const q = n.getAttribute("position");
  const uv = new Float32Array(q.count * 2);
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  const c = new THREE.Vector3();
  for (let t = 0; t < q.count; t += 3) {
    a.fromBufferAttribute(q, t);
    b.fromBufferAttribute(q, t + 1);
    c.fromBufferAttribute(q, t + 2);
    const f = b.clone().sub(a).cross(c.clone().sub(a));
    const ax = Math.abs(f.x);
    const ay = Math.abs(f.y);
    const az = Math.abs(f.z);
    for (let k = 0; k < 3; k++) {
      const x = q.getX(t + k);
      const y = q.getY(t + k);
      const z = q.getZ(t + k);
      const [u, v] = ay >= ax && ay >= az ? [x, z] : ax >= az ? [z, y] : [x, y];
      uv[(t + k) * 2] = u * 1.6;
      uv[(t + k) * 2 + 1] = v * 1.6;
    }
  }
  n.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
  n.translate(0, 0.2, 0);
  return agiter(n, () => 0);
}

/** Coussin de mousse : un dôme bas, texture projetée d'en haut. */
export function coussin(): THREE.BufferGeometry {
  const g = new THREE.SphereGeometry(0.5, 14, 5, 0, Math.PI * 2, 0, Math.PI / 2).scale(1, 0.25, 1);
  const p = g.getAttribute("position");
  const uv = g.getAttribute("uv");
  for (let i = 0; i < p.count; i++) uv.setXY(i, p.getX(i) * 1.5, p.getZ(i) * 1.5);
  return agiter(g, () => 0);
}
