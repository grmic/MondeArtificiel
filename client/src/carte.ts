import { Application, Container, Graphics, Sprite, Texture } from "pixi.js";
import { langue, nombre, t } from "./langue";
import type { CatastropheVue, DetailCatastrophe, Geologie } from "./catastrophes";

export interface Emplacement {
  numero: number;
  x: number;
  y: number;
}

/** Carte telle que renvoyée par GET /api/carte (tableaux d'octets en Base64). */
interface CarteJson {
  graine: number;
  largeur: number;
  hauteur: number;
  niveauMer: number;
  nomsBiomes: string[];
  biomes: string;
  altitudes: string;
  gibier: string;
  plantes: string;
  emplacements: Emplacement[];
  kilometresParCase: number;
  metresParUnite: number;
}

export interface Carte {
  graine: number;
  largeur: number;
  hauteur: number;
  niveauMer: number;
  nomsBiomes: string[];
  biomes: Uint8Array;
  altitudes: Uint8Array;
  gibier: Uint8Array;
  plantes: Uint8Array;
  emplacements: Emplacement[];
  /** Côté d'une case en km, et dénivelé d'un degré d'altitude en mètres. */
  kilometresParCase: number;
  metresParUnite: number;
}


/** Distance en mots : « 600 m », « 3,2 km », « 48 km ». */
export function distanceEnMots(km: number): string {
  if (km < 1) return `${Math.round((km * 1000) / 50) * 50} m`;
  return `${nombre(km, km < 10 ? 1 : 0)} km`;
}

/** Surface en mots : « 40 hectares », « 3,8 km² », « 1 250 km² ». */
export function surfaceEnMots(km2: number): string {
  if (km2 < 1) return t("mesure.hectares", { n: Math.round(km2 * 100) });
  return `${nombre(km2, km2 < 100 ? 1 : 0)} km²`;
}

/** Côté d'une case en km, tel que l'a donné le serveur avec la dernière carte chargée. */
let kmParCase = 0.8;

/** Surface d'un nombre de cases de la carte, en mots. */
export function surfaceDeCases(cases: number): string {
  return surfaceEnMots(cases * kmParCase * kmParCase);
}

/** Distance d'un nombre de cases, en mots. */
export function distanceDeCases(cases: number): string {
  return distanceEnMots(cases * kmParCase);
}

/** Couleurs des biomes, dans l'ordre de l'énumération côté serveur. */
const COULEURS: [number, number, number][] = [
  [28, 60, 110],   // Océan
  [52, 100, 160],  // Eau peu profonde
  [70, 130, 190],  // Lac
  [80, 150, 210],  // Rivière
  [225, 210, 160], // Plage
  [90, 110, 80],   // Marais
  [140, 170, 90],  // Plaine de fougères
  [50, 100, 60],   // Forêt de conifères
  [30, 120, 50],   // Forêt tropicale
  [220, 190, 120], // Désert
  [130, 120, 110], // Montagne
  [240, 240, 245], // Sommet enneigé
  [75, 135, 55],   // Forêt de feuillus
  [55, 105, 95],   // Mangrove
  [165, 150, 105], // Lande sèche
];

function decoderBase64(texte: string): Uint8Array {
  return Uint8Array.from(atob(texte), (c) => c.charCodeAt(0));
}

/**
 * Charge la carte du monde courant, ou l'aperçu d'autres réglages (graine, couples, largeur et hauteur en cases,
 * continents). En cas de refus, l'erreur porte la raison donnée par le serveur.
 */
export async function chargerCarte(reglages?: Record<string, number | null>): Promise<Carte> {
  const parametres = new URLSearchParams();
  for (const [k, v] of Object.entries(reglages ?? {})) if (v !== null) parametres.set(k, String(v));
  parametres.set("langue", langue());
  const requete = `/api/carte?${parametres}`;
  const reponse = await fetch(requete);
  if (!reponse.ok) {
    const probleme = (await reponse.json().catch(() => null)) as { detail?: string } | null;
    throw new Error(probleme?.detail ?? t("commun.reponseServeur", { statut: String(reponse.status) }));
  }
  const json = (await reponse.json()) as CarteJson;
  if (json.kilometresParCase > 0) kmParCase = json.kilometresParCase;
  return {
    ...json,
    biomes: decoderBase64(json.biomes),
    altitudes: decoderBase64(json.altitudes),
    gibier: decoderBase64(json.gibier),
    plantes: decoderBase64(json.plantes),
  };
}

/** Biomes d'eau (océan, eau peu profonde, lac, rivière) : le rivage passe entre eux et les terres. */
const EAU = [true, true, true, true];

/**
 * Pixels de l'image par case : assez pour des rivages courbes, sans dépasser 4 096 pixels de côté (limite sûre des cartes
 * graphiques) ni environ 6 millions de pixels (une seconde de dessin au plus sur un ordinateur courant). Un aperçu, redessiné
 * à chaque réglage, se contente de 4.
 */
export function facteurImage(carte: Carte, apercu = false): number {
  return facteurPour(carte.largeur, carte.hauteur, apercu ? 4 : 8);
}

function facteurPour(largeur: number, hauteur: number, plafond: number): number {
  const cote = Math.floor(4096 / Math.max(largeur, hauteur));
  const budget = Math.floor(Math.sqrt(6_000_000 / (largeur * hauteur)));
  return Math.max(1, Math.min(plafond, cote, budget));
}

/** Bruit fixe entre 0 et 1 pour un point entier (même dessin à chaque affichage). */
function hachage(x: number, y: number): number {
  let h = Math.imul(x, 374761393) ^ Math.imul(y, 668265263);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** Bruit de valeur lissé, de période « pas » (dans l'unité de x et y). */
function bruitLisse(x: number, y: number, pas: number): number {
  const fx = x / pas, fy = y / pas;
  const x0 = Math.floor(fx), y0 = Math.floor(fy);
  const u = fx - x0, v = fy - y0;
  const su = u * u * (3 - 2 * u), sv = v * v * (3 - 2 * v);
  const a = hachage(x0, y0), b = hachage(x0 + 1, y0), c = hachage(x0, y0 + 1), d = hachage(x0 + 1, y0 + 1);
  return a + (b - a) * su + (c - a) * sv + (a - b - c + d) * su * sv;
}

/**
 * Point d'échantillonnage déplacé par deux bruits lisses (périodes de 4 et 1,5 cases, jusqu'à 0,7 case en tout), en cases :
 * les limites entre biomes ondulent au lieu de suivre les marches de la grille. Ne dépend pas du facteur de l'image, si bien
 * que la carte et ses calques ondulent ensemble.
 */
function deformer(cx: number, cy: number, sortie: number[]): void {
  sortie[0] = cx + (bruitLisse(cx, cy, 4) - 0.5) * 0.9 + (bruitLisse(cx, cy, 1.5) - 0.5) * 0.5;
  sortie[1] = cy + (bruitLisse(cx + 131.7, cy + 57.1, 4) - 0.5) * 0.9 + (bruitLisse(cx + 131.7, cy + 57.1, 1.5) - 0.5) * 0.5;
}

/**
 * Limite nette et courbe entre valeurs voisines : les poids des quatre cases sont cumulés par valeur (clé), la valeur la plus
 * lourde l'emporte, et le fondu avec la suivante ne dure qu'environ un pixel. La limite suit ainsi une courbe (où deux
 * valeurs pèsent autant) au lieu des marches de la grille, sans le flou d'un dégradé sur toute une case.
 * Rend dans « choix » : [voisine de la première valeur, voisine de la deuxième (ou la même), part de la première].
 */
function departager(cles: number[], poids: number[], facteur: number, choix: number[]): void {
  let k1 = -1, w1 = -1, k2 = -1, w2 = -1;
  for (let k = 0; k < 4; k++) {
    let deja = false;
    for (let j = 0; j < k; j++) if (cles[j] === cles[k]) { deja = true; break; }
    if (deja) continue;
    let w = poids[k];
    for (let j = k + 1; j < 4; j++) if (cles[j] === cles[k]) w += poids[j];
    if (w > w1) { k2 = k1; w2 = w1; k1 = k; w1 = w; }
    else if (w > w2) { k2 = k; w2 = w; }
  }
  if (k2 < 0) { choix[0] = k1; choix[1] = k1; choix[2] = 1; return; }
  choix[0] = k1; choix[1] = k2;
  choix[2] = Math.min(1, 0.5 + (w1 - w2) * facteur * 0.6);
}

/** Déplacements des pixels d'un calque, gardés par taille (les calques sont redessinés souvent). */
const deformations = new Map<string, Float32Array>();

function tableDeformation(largeur: number, hauteur: number, facteur: number): Float32Array {
  const cle = `${largeur}x${hauteur}x${facteur}`;
  let table = deformations.get(cle);
  if (table) return table;
  const w = largeur * facteur, hh = hauteur * facteur;
  table = new Float32Array(w * hh * 2);
  const d = [0, 0];
  for (let py = 0; py < hh; py++) {
    for (let px = 0; px < w; px++) {
      deformer((px + 0.5) / facteur, (py + 0.5) / facteur, d);
      table[(py * w + px) * 2] = d[0];
      table[(py * w + px) * 2 + 1] = d[1];
    }
  }
  deformations.clear();
  deformations.set(cle, table);
  return table;
}

/**
 * Agrandit un calque d'un pixel par case (couleurs avec transparence) en image lissée : mêmes limites ondulées que la carte,
 * limites nettes et courbes (voir departager). Rend un sprite ramené à l'échelle d'une case.
 */
export function calqueLisse(source: HTMLCanvasElement): Sprite {
  const l = source.width, h = source.height;
  const facteur = facteurPour(l, h, 4);
  const src = source.getContext("2d")!.getImageData(0, 0, l, h).data;
  const canvas = document.createElement("canvas");
  canvas.width = l * facteur;
  canvas.height = h * facteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(canvas.width, canvas.height);
  const table = tableDeformation(l, h, facteur);
  const poids = [0, 0, 0, 0];
  const voisines = [0, 0, 0, 0];
  const cles = [0, 0, 0, 0];
  const choix = [0, 0, 1];
  const case_ = (x: number, y: number) => Math.min(h - 1, Math.max(0, y)) * l + Math.min(l - 1, Math.max(0, x));
  for (let q = 0; q < canvas.width * canvas.height; q++) {
    const fx = table[q * 2] - 0.5, fy = table[q * 2 + 1] - 0.5;
    const x0 = Math.floor(fx), y0 = Math.floor(fy), u = fx - x0, v = fy - y0;
    voisines[0] = case_(x0, y0); voisines[1] = case_(x0 + 1, y0); voisines[2] = case_(x0, y0 + 1); voisines[3] = case_(x0 + 1, y0 + 1);
    if (src[voisines[0] * 4 + 3] + src[voisines[1] * 4 + 3] + src[voisines[2] * 4 + 3] + src[voisines[3] * 4 + 3] === 0) continue;
    poids[0] = (1 - u) * (1 - v); poids[1] = u * (1 - v); poids[2] = (1 - u) * v; poids[3] = u * v;
    for (let k = 0; k < 4; k++) {
      const o = voisines[k] * 4;
      cles[k] = src[o + 3] === 0 ? -1 : ((src[o] << 24) | (src[o + 1] << 16) | (src[o + 2] << 8) | src[o + 3]);
    }
    departager(cles, poids, facteur, choix);
    const o1 = voisines[choix[0]] * 4, o2 = voisines[choix[1]] * 4;
    const w1 = choix[2] * src[o1 + 3], w2 = (1 - choix[2]) * src[o2 + 3];
    const a = w1 + w2;
    const r = src[o1] * w1 + src[o2] * w2, g = src[o1 + 1] * w1 + src[o2 + 1] * w2, b = src[o1 + 2] * w1 + src[o2 + 2] * w2;
    if (a < 1) continue;
    const p = q * 4;
    image.data[p] = r / a; image.data[p + 1] = g / a; image.data[p + 2] = b / a; image.data[p + 3] = a;
  }
  ctx.putImageData(image, 0, 0);
  const texture = Texture.from(canvas);
  texture.source.scaleMode = "linear";
  const sprite = new Sprite(texture);
  sprite.scale.set(1 / facteur);
  return sprite;
}

/**
 * Dessine la carte dans un canvas, « facteur » pixels par case. Entre les centres des cases, l'altitude, la part d'eau
 * et les couleurs sont interpolées : les rivages deviennent des courbes, les biomes voisins se fondent, et l'ombrage du
 * relief (lumière du nord-ouest) suit la pente interpolée. Un léger grain évite les aplats. Facteur 1 : une case, un pixel.
 */
function dessinerImage(carte: Carte, facteur = facteurImage(carte)): HTMLCanvasElement {
  const { largeur: l, hauteur: h } = carte;
  const canvas = document.createElement("canvas");
  canvas.width = l * facteur;
  canvas.height = h * facteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(canvas.width, canvas.height);
  const eau = new Float32Array(l * h);
  for (let i = 0; i < l * h; i++) eau[i] = EAU[carte.biomes[i]] ? 1 : 0;
  const alt = carte.altitudes;
  const case_ = (x: number, y: number) => Math.min(h - 1, Math.max(0, y)) * l + Math.min(l - 1, Math.max(0, x));
  /** Altitude interpolée au point (cx, cy) en cases, centres des cases aux demi-entiers. */
  const altitude = (cx: number, cy: number) => {
    const fx = cx - 0.5, fy = cy - 0.5;
    const x0 = Math.floor(fx), y0 = Math.floor(fy), u = fx - x0, v = fy - y0;
    const a = alt[case_(x0, y0)], b = alt[case_(x0 + 1, y0)], c = alt[case_(x0, y0 + 1)], d = alt[case_(x0 + 1, y0 + 1)];
    return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
  };
  const voisines = [0, 0, 0, 0];
  const poids = [0, 0, 0, 0];
  const RIVIERE = 3;
  const d = [0, 0];
  const clesTerre = [0, 0, 0, 0], clesEau = [0, 0, 0, 0], poidsCote = [0, 0, 0, 0], choix = [0, 0, 1];
  const terreRgb = [0, 0, 0], eauRgb = [0, 0, 0];
  /** Couleur d'un côté du rivage (clé -1 : case de l'autre côté) dans « rgb » ; 0 si aucune case de ce côté. */
  const couleurCote = (cles: number[], rgb: number[]): number => {
    for (let k = 0; k < 4; k++) poidsCote[k] = cles[k] === -1 ? 0 : poids[k];
    if (poidsCote[0] + poidsCote[1] + poidsCote[2] + poidsCote[3] === 0) return 0;
    departager(cles, poidsCote, facteur, choix);
    const c1 = COULEURS[biomes[voisines[choix[0]]]] ?? [255, 0, 255], c2 = COULEURS[biomes[voisines[choix[1]]]] ?? [255, 0, 255];
    const m = choix[2];
    for (let j = 0; j < 3; j++) rgb[j] = c1[j] * m + c2[j] * (1 - m);
    return 1;
  };
  const biomes = carte.biomes;
  for (let py = 0; py < canvas.height; py++) {
    const cy = (py + 0.5) / facteur;
    for (let px = 0; px < canvas.width; px++) {
      const cx = (px + 0.5) / facteur;
      // Limites ondulées (voir deformer) ; l'ombrage du relief reste au point exact.
      deformer(cx, cy, d);
      const sx = d[0], sy = d[1];
      const fx = sx - 0.5, x0 = Math.floor(fx), u = fx - x0;
      const fy = sy - 0.5, y0 = Math.floor(fy), v = fy - y0;
      voisines[0] = case_(x0, y0); voisines[1] = case_(x0 + 1, y0);
      voisines[2] = case_(x0, y0 + 1); voisines[3] = case_(x0 + 1, y0 + 1);
      poids[0] = (1 - u) * (1 - v); poids[1] = u * (1 - v); poids[2] = (1 - u) * v; poids[3] = u * v;
      let partEau = 0;
      for (let k = 0; k < 4; k++) partEau += poids[k] * eau[voisines[k]];
      // Rivière qui passe en diagonale (deux cases de rivière en coin, deux terres) : on relie les deux centres d'un trait
      // continu, sinon elle se dessinerait en chapelet de perles.
      if (biomes[voisines[0]] === RIVIERE && biomes[voisines[3]] === RIVIERE && !eau[voisines[1]] && !eau[voisines[2]]) {
        partEau = Math.max(partEau, 1 - Math.abs(u - v));
      } else if (biomes[voisines[1]] === RIVIERE && biomes[voisines[2]] === RIVIERE && !eau[voisines[0]] && !eau[voisines[3]]) {
        partEau = Math.max(partEau, 1 - Math.abs(u + v - 1));
      }
      // Un peu de bruit sur le seuil : le rivage ondule au lieu de suivre la grille.
      const seuil = 0.5 + (bruitLisse(cx + 17.3, cy + 9.1, 1.5) - 0.5) * 0.3;
      // Part d'eau du pixel : 0 en terre, 1 en eau, fondu sur environ un pixel au rivage (bord sans marches au zoom).
      const t = Math.min(1, Math.max(0, (partEau - seuil) * facteur * 1.5 + 0.5));
      // Couleurs : de chaque côté du rivage, le biome qui pèse le plus l'emporte, limite nette et courbe (voir departager).
      for (let k = 0; k < 4; k++) {
        const bio = biomes[voisines[k]];
        clesTerre[k] = eau[voisines[k]] === 1 ? -1 : bio;
        clesEau[k] = eau[voisines[k]] === 1 ? bio : -1;
      }
      const tt = couleurCote(clesTerre, terreRgb), et = couleurCote(clesEau, eauRgb);
      let [tr, tg, tb] = terreRgb, [er, eg, eb] = eauRgb;
      // Une seule sorte de case autour : l'autre couleur reprend la même (rien à fondre).
      if (tt === 0) { tr = er; tg = eg; tb = eb; }
      if (et === 0) { er = tr; eg = tg; eb = tb; }
      let lumiere = 1;
      if (t < 1) {
        const pente = altitude(cx, cy) - altitude(cx - 1, cy - 1);
        lumiere = Math.min(1.25, Math.max(0.75, 1 + pente * 0.04));
        // Grain : un bruit doux d'une demi-case et un fin, pixel par pixel, pour une texture nette.
        lumiere *= 0.94 + bruitLisse(cx, cy, 0.5) * 0.08 + hachage(px, py) * 0.04;
      }
      const lumiereEau = 0.97 + bruitLisse(cx, cy, 3) * 0.04 + hachage(px, py) * 0.02;
      const terre = (1 - t) * lumiere, mer = t * lumiereEau;
      const r = tr * terre + er * mer;
      const g = tg * terre + eg * mer;
      const b = tb * terre + eb * mer;
      const p = (py * canvas.width + px) * 4;
      image.data[p] = Math.min(255, r);
      image.data[p + 1] = Math.min(255, g);
      image.data[p + 2] = Math.min(255, b);
      image.data[p + 3] = 255;
    }
  }
  ctx.putImageData(image, 0, 0);
  return canvas;
}

export interface HumainVue {
  id: number;
  nom: string;
  sexe: "femme" | "homme";
  partenaire: number;
  emplacement: number;
  age: number;
  x: number;
  y: number;
  activite: string;
  vivant: boolean;
  malade: boolean;
  faim: number;
  soif: number;
  fatigue: number;
  froid: number;
  sante: number;
  causeDeMort: string | null;
  mere: number;
  pere: number;
  enceinte: boolean;
  etape: "bébé" | "enfant" | "adulte";
  pensee: string | null;
  humeur: string | null;
  communaute: number;
  reserves: number;
  /** Peur laissée par une catastrophe (0 à 100). */
  peur: number;
  /** Porteur d'une maladie qui se transmet. */
  contagieux: boolean;
  /** Son nom dans la langue des siens (null tant qu'on ne lui en a pas donné). */
  nomLangue: string | null;
}

/** Indicateurs du monde, tels que renvoyés par le hub (méthode « Nature »). */
export interface Indicateurs {
  vivants: number;
  enfants: number;
  abondance: number;
  reservesMoyennes: number;
  regionsSeches: number;
  regionsHabitees: number;
  communautesEnDisette: number;
}

/** Nature : abondance de chaque tuile (Base64, 128 = ce que la saison et les pluies permettent) et pluies de l'année par région. */
export interface Nature {
  indicateurs: Indicateurs;
  largeur: number;
  hauteur: number;
  abondance: string;
  regionsX: number;
  regionsY: number;
  tailleRegion: number;
  pluies: number[];
}

/** Couleur d'une tuile selon ses ressources : rouge (épuisée) → jaune → transparent (intacte). */
function dessinerAbondance(nature: Nature, carte: Carte): HTMLCanvasElement {
  const valeurs = decoderBase64(nature.abondance);
  const canvas = document.createElement("canvas");
  canvas.width = nature.largeur;
  canvas.height = nature.hauteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(nature.largeur, nature.hauteur);
  for (let i = 0; i < valeurs.length; i++) {
    const v = valeurs[i];
    if (v === 0 || carte.biomes[i] <= 3) continue; // ni eau ni terre stérile
    const r = v / 128; // 1 : niveau permis par la saison
    const p = i * 4;
    const manque = Math.max(0, Math.min(1, 1 - r));
    image.data[p] = 255;
    image.data[p + 1] = Math.round(220 * (1 - manque));
    image.data[p + 2] = 40;
    image.data[p + 3] = Math.round(200 * Math.min(1, manque * 1.4));
  }
  ctx.putImageData(image, 0, 0);
  return canvas;
}

/** Terres d'une communauté : cases en plages (début, longueur, début, longueur…). */
export interface Territoire {
  id: number;
  nom: string;
  membres: number;
  plages: number[];
}

export interface Territoires {
  secondes: number;
  date: string;
  actuel: boolean;
  communautes: Territoire[];
}

/** Couleurs bien distinctes (palette qualitative de 12 teintes, ColorBrewer « Paired » éclaircie) : une par communauté, en boucle. */
const PALETTE: [number, number, number][] = [
  [31, 120, 180], [255, 127, 0], [51, 160, 44], [227, 26, 28], [106, 61, 154], [177, 89, 40],
  [166, 206, 227], [253, 191, 111], [178, 223, 138], [251, 154, 153], [202, 178, 214], [255, 255, 153],
];

/** Couleur propre à une communauté. */
export function couleurCommunaute(id: number): [number, number, number] {
  return PALETTE[(id - 1) % PALETTE.length];
}

/** Terres des communautés : un voile de leur couleur, et un trait plein là où elles s'arrêtent. */
function dessinerTerritoires(t: Territoires, largeur: number, hauteur: number, choisie: number): { canvas: HTMLCanvasElement; proprietaires: Int32Array } {
  const proprietaires = new Int32Array(largeur * hauteur);
  for (const c of t.communautes) {
    for (let i = 0; i + 1 < c.plages.length; i += 2) {
      for (let k = 0; k < c.plages[i + 1]; k++) {
        const j = c.plages[i] + k;
        if (j >= 0 && j < proprietaires.length) proprietaires[j] = c.id;
      }
    }
  }
  const canvas = document.createElement("canvas");
  canvas.width = largeur;
  canvas.height = hauteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(largeur, hauteur);
  for (let y = 0; y < hauteur; y++) {
    for (let x = 0; x < largeur; x++) {
      const i = y * largeur + x;
      const id = proprietaires[i];
      if (id === 0) continue;
      const bord = x === 0 || y === 0 || x === largeur - 1 || y === hauteur - 1
        || proprietaires[i - 1] !== id || proprietaires[i + 1] !== id || proprietaires[i - largeur] !== id || proprietaires[i + largeur] !== id;
      const [r, g, b] = couleurCommunaute(id);
      const p = i * 4;
      image.data[p] = r;
      image.data[p + 1] = g;
      image.data[p + 2] = b;
      const terne = choisie !== 0 && id !== choisie;
      image.data[p + 3] = bord ? (terne ? 110 : 230) : (terne ? 25 : 70);
    }
  }
  ctx.putImageData(image, 0, 0);
  return { canvas, proprietaires };
}

/** Couleur de chaque type de catastrophe sur la carte. */
const COULEURS_CATASTROPHES: Record<string, number> = {
  seisme: 0xc8a060, tsunami: 0x3fa9ff, eruption: 0xff4020, crue: 0x4fc3f7, incendie: 0xff8c1a, cyclone: 0xb0c4de,
  froid: 0x9fd8ff, canicule: 0xffb347, epidemie: 0xa0e060, impact: 0xff3030, bolide: 0xffe080,
};

/** Terres touchées par une catastrophe (cases), et le feu qui brûle encore (front) plus vif. */
function dessinerCases(detail: DetailCatastrophe, largeur: number, hauteur: number): HTMLCanvasElement {
  const canvas = document.createElement("canvas");
  canvas.width = largeur;
  canvas.height = hauteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(largeur, hauteur);
  const couleur = COULEURS_CATASTROPHES[detail.catastrophe.type] ?? 0xffffff;
  const peindre = (i: number, c: number, alpha: number) => {
    if (i < 0 || i >= largeur * hauteur) return;
    const p = i * 4;
    image.data[p] = (c >> 16) & 255;
    image.data[p + 1] = (c >> 8) & 255;
    image.data[p + 2] = c & 255;
    image.data[p + 3] = alpha;
  };
  // Une terre brûlée est sombre ; ailleurs, la couleur de la catastrophe.
  for (const i of detail.cases) peindre(i, detail.catastrophe.type === "incendie" ? 0x2a1a10 : couleur, 150);
  for (const i of detail.front) peindre(i, 0xffd040, 230);
  ctx.putImageData(image, 0, 0);
  return canvas;
}

/** Feux et réserves du monde (voir FeuxDto côté serveur). */
export interface Feux {
  feux: { x: number; y: number; couve: boolean }[];
  reserves: { x: number; y: number; eau: boolean; texte: string }[];
}

export type InfoTuile = { x: number; y: number; carte: Carte; emplacement?: Emplacement; humains: HumainVue[] } | null;

const COULEUR_FEMME = 0xffb347;
const COULEUR_HOMME = 0x6ec6ff;

/** Vue de la carte : affichage, zoom à la molette, déplacement à la souris, survol des tuiles. */
export class VueCarte {
  private readonly monde = new Container();
  private readonly marqueurs = new Container();
  private readonly nuit = new Graphics();
  private readonly calqueHumains = new Container();
  private readonly pointsHumains = new Map<number, Graphics>();
  private humains: HumainVue[] = [];
  private sprite?: Sprite;
  private carte?: Carte;
  private echelleMin = 1;
  private readonly selection = new Graphics().circle(0, 0, 7).stroke({ width: 2, color: 0xffffff });
  private selectionId = 0;
  private readonly halo = new Graphics();
  private surlignes = new Set<number>();
  /** Lieux sacrés marqués (fenêtre des religions ouverte). */
  private readonly lieuxSacres = new Graphics();
  private lieux: { x: number; y: number }[] = [];
  private surClic: (info: InfoTuile) => void = () => {};
  private readonly calqueNature = new Container();
  private nature?: Nature;
  private readonly calqueTerritoires = new Container();
  private territoires?: { donnees: Territoires; proprietaires: Int32Array };
  private readonly calqueCatastrophes = new Container();
  private readonly calqueTrajet = new Container();
  private readonly calqueTerrain = new Container();
  private readonly calqueFeux = new Graphics();
  private feux: Feux = { feux: [], reserves: [] };
  private reservesVisibles = true;
  private barreEchelle?: HTMLElement;
  private catastrophes: { actives: CatastropheVue[]; detail: DetailCatastrophe | null; cases: Set<number> } = { actives: [], detail: null, cases: new Set() };

  private constructor(
    private readonly app: Application,
    private readonly surSurvol: (info: InfoTuile) => void,
  ) {
    app.stage.addChild(this.monde);
    this.brancherSouris();
  }

  static async creer(hote: HTMLElement, surSurvol: (info: InfoTuile) => void): Promise<VueCarte> {
    const app = new Application();
    // Résolution de l'écran (Retina, écrans 4K) : traits, cercles et carte nets ; autoDensity garde les mesures en pixels CSS.
    await app.init({ resizeTo: window, background: "#0f1a2b", antialias: true, resolution: window.devicePixelRatio || 1, autoDensity: true });
    hote.appendChild(app.canvas);
    const vue = new VueCarte(app, surSurvol);
    // Barre d'échelle en bas à gauche : sa longueur suit le zoom.
    vue.barreEchelle = document.createElement("div");
    vue.barreEchelle.className = "barre-echelle";
    vue.barreEchelle.setAttribute("aria-label", t("carte.echelle"));
    hote.appendChild(vue.barreEchelle);
    return vue;
  }

  /** Met la barre d'échelle à une longueur ronde (1, 2, 5, 10, 20, 50… km) qui tient en 60 à 150 pixels. */
  private ajusterEchelle(): void {
    const barre = this.barreEchelle;
    if (!barre || !this.carte) return;
    const pixelsParKm = this.monde.scale.x / this.carte.kilometresParCase;
    const ronds = [0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
    const km = ronds.find((r) => r * pixelsParKm >= 60) ?? ronds[ronds.length - 1];
    barre.style.width = `${Math.round(km * pixelsParKm)}px`;
    barre.textContent = distanceEnMots(km);
  }

  /** Trace un trajet (cases dans l'ordre) et ses deux bouts ; null : efface. */
  afficherTrajet(cases: number[] | null, points: { x: number; y: number }[] = []): void {
    for (const c of this.calqueTrajet.removeChildren()) c.destroy({ children: true });
    if (!this.carte) return;
    const g = new Graphics();
    const l = this.carte.largeur;
    if (cases && cases.length > 1) {
      g.moveTo((cases[0] % l) + 0.5, Math.floor(cases[0] / l) + 0.5);
      for (const i of cases.slice(1)) g.lineTo((i % l) + 0.5, Math.floor(i / l) + 0.5);
      g.stroke({ width: 0.9, color: 0xfff27a, alpha: 0.95 });
    }
    for (const p of points) {
      g.circle(p.x + 0.5, p.y + 0.5, 1.6).fill({ color: 0xfff27a, alpha: 0.9 }).stroke({ width: 0.5, color: 0x000000 });
    }
    this.calqueTrajet.addChild(g);
  }

  /** Largeur de la carte affichée, en cases (0 si aucune). */
  largeurCarte(): number {
    return this.carte?.largeur ?? 0;
  }

  /** Centre la carte sur une case et zoome un peu si besoin, puis la marque. */
  montrer(x: number, y: number): void {
    const { width, height } = this.app.screen;
    const echelle = Math.max(this.monde.scale.x, 6);
    this.monde.scale.set(echelle);
    this.monde.position.set(width / 2 - (x + 0.5) * echelle, height / 2 - (y + 0.5) * echelle);
    this.ajusterMarqueurs();
    this.afficherTrajet(null, [{ x, y }]);
  }

  /** Affiche une carte ; un aperçu (autres réglages) cache les humains du monde courant. */
  afficher(carte: Carte, apercu = false): void {
    this.calqueHumains.visible = !apercu;
    this.calqueTerrain.visible = !apercu;
    this.afficherNature(null);
    this.afficherTerritoires(null);
    this.afficherCatastrophes(null, [], null);
    this.carte = carte;
    this.monde.removeChildren();
    this.sprite?.texture.destroy(true);

    // Image lissée, plusieurs pixels par case, ramenée à l'échelle d'une case ; filtrage linéaire et mipmaps pour un
    // rendu doux de près comme de loin.
    const facteur = facteurImage(carte, apercu);
    const texture = Texture.from(dessinerImage(carte, facteur));
    texture.source.scaleMode = "linear";
    texture.source.autoGenerateMipmaps = true;
    texture.source.updateMipmaps();
    this.sprite = new Sprite(texture);
    this.sprite.scale.set(1 / facteur);
    this.monde.addChild(this.sprite);

    this.marqueurs.removeChildren();
    for (const e of carte.emplacements) {
      // Lieu de départ d'un couple : simple anneau, les humains eux-mêmes sont dessinés par-dessus.
      const point = new Graphics().circle(0, 0, 7).stroke({ width: 1.5, color: 0xffffff, alpha: 0.5 });
      point.position.set(e.x + 0.5, e.y + 0.5);
      this.marqueurs.addChild(point);
    }
    this.nuit.clear().rect(0, 0, carte.largeur, carte.hauteur).fill({ color: 0x050a1e });
    this.afficherTrajet(null);
    this.monde.addChild(this.calqueTerrain, this.nuit, this.calqueTerritoires, this.calqueNature, this.calqueCatastrophes, this.calqueFeux, this.calqueTrajet, this.marqueurs, this.calqueHumains);
    this.cadrer();
  }

  /**
   * Dessine les feux (orange quand ils flambent, rouge sombre quand ils couvent sous la cendre ou ne sont plus que braises) et,
   * si elles ne sont pas masquées, les réserves (un petit carré discret : brun pour la nourriture, bleu pour l'eau).
   */
  afficherFeux(feux: Feux | null, reservesVisibles = this.reservesVisibles): void {
    this.feux = feux ?? { feux: [], reserves: [] };
    this.reservesVisibles = reservesVisibles;
    const g = this.calqueFeux.clear();
    if (this.reservesVisibles) {
      for (const r of this.feux.reserves) {
        g.rect(r.x + 0.62, r.y + 0.62, 0.3, 0.3).fill({ color: r.eau ? 0x4f86b8 : 0x8a6236, alpha: 0.9 });
      }
    }
    for (const f of this.feux.feux) {
      g.circle(f.x + 0.5, f.y + 0.5, f.couve ? 0.3 : 0.38).fill({ color: f.couve ? 0x6e1e0c : 0xff8a1e, alpha: f.couve ? 0.85 : 0.95 });
    }
  }

  /** Feux et réserves d'une tuile, en mots. */
  feuxEn(x: number, y: number): string[] {
    const lignes: string[] = [];
    for (const f of this.feux.feux) if (f.x === x && f.y === y) lignes.push(f.couve ? t("carte.feuCouve") : t("carte.feuFlambe"));
    if (this.reservesVisibles) for (const r of this.feux.reserves) if (r.x === x && r.y === y) lignes.push(t("carte.reserve", { texte: r.texte }));
    return lignes;
  }

  /** Calques du terrain (roches, sols, eaux, fonds, grottes, gisements), dessinés par le module des calques ; null : aucun. */
  afficherCalques(dessin: Container | null): void {
    for (const c of this.calqueTerrain.removeChildren()) c.destroy({ children: true, texture: true });
    if (dessin) this.calqueTerrain.addChild(dessin);
  }

  /** Montre (ou cache, avec null) les ressources des terres et les régions touchées par la sécheresse ou très arrosées. */
  afficherNature(nature: Nature | null): void {
    this.nature = nature ?? undefined;
    for (const c of this.calqueNature.removeChildren()) c.destroy({ children: true, texture: true });
    if (!nature || !this.carte || nature.largeur !== this.carte.largeur || nature.hauteur !== this.carte.hauteur) return;
    this.calqueNature.addChild(calqueLisse(dessinerAbondance(nature, this.carte)));
    const regions = new Graphics();
    const valeurs = decoderBase64(nature.abondance);
    nature.pluies.forEach((pluie, r) => {
      if (pluie >= 0.7 && pluie <= 1.35) return;
      // Cadre réduit aux terres de la région (tuiles qui portent des ressources) : rien n'est tracé sur la mer.
      const x0 = (r % nature.regionsX) * nature.tailleRegion;
      const y0 = Math.floor(r / nature.regionsX) * nature.tailleRegion;
      let minX = Infinity, minY = Infinity, maxX = -1, maxY = -1;
      for (let y = y0; y < Math.min(y0 + nature.tailleRegion, nature.hauteur); y++) {
        for (let x = x0; x < Math.min(x0 + nature.tailleRegion, nature.largeur); x++) {
          if (valeurs[y * nature.largeur + x] === 0) continue;
          minX = Math.min(minX, x); minY = Math.min(minY, y);
          maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
        }
      }
      if (maxX < 0) return;
      regions.rect(minX + 0.5, minY + 0.5, maxX - minX, maxY - minY)
        .stroke({ width: 0.8, color: pluie < 0.7 ? 0xd98c3a : 0x5fb4ff, alpha: 0.9 });
    });
    this.calqueNature.addChild(regions);
  }

  /** Montre (ou cache, avec null) les terres des communautés ; celles de la communauté choisie ressortent. */
  afficherTerritoires(t: Territoires | null, choisie = 0): void {
    for (const c of this.calqueTerritoires.removeChildren()) c.destroy({ children: true, texture: true });
    this.territoires = undefined;
    if (!t || !this.carte) return;
    const { canvas, proprietaires } = dessinerTerritoires(t, this.carte.largeur, this.carte.hauteur, choisie);
    this.calqueTerritoires.addChild(calqueLisse(canvas));
    this.territoires = { donnees: t, proprietaires };
  }

  /**
   * Montre la géologie (failles, volcans), les catastrophes en cours (un cercle par zone touchée) et les terres
   * touchées par la catastrophe choisie. Tout à null : rien.
   */
  afficherCatastrophes(geologie: Geologie | null, actives: CatastropheVue[], detail: DetailCatastrophe | null): void {
    for (const c of this.calqueCatastrophes.removeChildren()) c.destroy({ children: true, texture: true });
    this.catastrophes = { actives, detail, cases: new Set([...(detail?.cases ?? []), ...(detail?.front ?? [])]) };
    if (!this.carte) return;
    if (detail) {
      this.calqueCatastrophes.addChild(calqueLisse(dessinerCases(detail, this.carte.largeur, this.carte.hauteur)));
    }
    const g = new Graphics();
    for (const f of geologie?.failles ?? []) {
      g.moveTo(f.x1 + 0.5, f.y1 + 0.5).lineTo(f.x2 + 0.5, f.y2 + 0.5)
        .stroke({ width: 0.7, color: f.sousMarine ? 0x7a6cff : 0xc0503a, alpha: 0.75 });
    }
    for (const v of geologie?.volcans ?? []) {
      g.poly([v.x + 0.5, v.y - 1.5, v.x - 1.3, v.y + 1.8, v.x + 2.3, v.y + 1.8])
        .fill({ color: v.enEruption ? 0xff4020 : 0x5a3a2a, alpha: 0.95 }).stroke({ width: 0.4, color: 0xffd0a0 });
    }
    const zones = [...actives];
    if (detail && !zones.some((c) => c.id === detail.catastrophe.id)) zones.push(detail.catastrophe);
    for (const c of zones) {
      // Ce qui couvre tout le continent (froid, canicule, ciel) n'a pas de zone à dessiner.
      if (c.rayon >= this.carte.largeur || c.phenomene && c.type !== "bolide") continue;
      const couleur = COULEURS_CATASTROPHES[c.type] ?? 0xffffff;
      const choisie = detail?.catastrophe.id === c.id;
      g.circle(c.x + 0.5, c.y + 0.5, Math.max(1.5, c.rayon))
        .fill({ color: couleur, alpha: choisie ? 0.05 : 0.12 })
        .stroke({ width: choisie ? 1.2 : 0.8, color: couleur, alpha: c.active ? 0.9 : 0.5 });
    }
    this.calqueCatastrophes.addChild(g);
  }

  /** Catastrophes qui touchent une tuile (zones en cours, ou cases de la catastrophe choisie). */
  catastrophesEn(x: number, y: number): CatastropheVue[] {
    if (!this.carte) return [];
    const { actives, detail, cases } = this.catastrophes;
    const resultat = actives.filter((c) => c.rayon < this.carte!.largeur && !c.phenomene && Math.hypot(c.x - x, c.y - y) <= c.rayon);
    if (detail && cases.has(y * this.carte.largeur + x) && !resultat.some((c) => c.id === detail.catastrophe.id)) resultat.push(detail.catastrophe);
    return resultat;
  }

  /** Communauté dont la tuile fait partie des terres, si la couche des territoires est affichée. */
  territoireEn(x: number, y: number): Territoire | null {
    const t = this.territoires;
    if (!t || !this.carte || x < 0 || y < 0 || x >= this.carte.largeur || y >= this.carte.hauteur) return null;
    const id = t.proprietaires[y * this.carte.largeur + x];
    return id === 0 ? null : t.donnees.communautes.find((c) => c.id === id) ?? null;
  }

  /** Abondance d'une tuile (1 = ce que la saison et les pluies permettent) et pluie de sa région, si la couche des ressources est affichée. */
  natureEn(x: number, y: number): { abondance: number; pluie: number } | null {
    const n = this.nature;
    if (!n || x < 0 || y < 0 || x >= n.largeur || y >= n.hauteur) return null;
    if (!this.octetsNature || this.octetsNature.source !== n.abondance) {
      this.octetsNature = { source: n.abondance, valeurs: decoderBase64(n.abondance) };
    }
    const r = Math.floor(y / n.tailleRegion) * n.regionsX + Math.floor(x / n.tailleRegion);
    return { abondance: this.octetsNature.valeurs[y * n.largeur + x] / 128, pluie: n.pluies[r] ?? 1 };
  }
  private octetsNature?: { source: string; valeurs: Uint8Array };

  /** Place les humains : un point par vivant (orange : femme, bleu : homme), une croix grise par mort. */
  afficherHumains(humains: HumainVue[]): void {
    this.humains = humains;
    // Retire les points des humains qui ne sont plus envoyés (morts depuis plus d'un an).
    const presents = new Set(humains.map((h) => h.id));
    for (const [id, point] of this.pointsHumains) {
      if (!presents.has(id)) {
        point.destroy();
        this.pointsHumains.delete(id);
      }
    }
    for (const h of humains) {
      let point = this.pointsHumains.get(h.id);
      // Les bébés sont portés par leur mère : on ne les dessine pas à part.
      const etat = !h.vivant ? "mort" : `${h.sexe}-${h.etape}-${h.enceinte}`;
      if (h.vivant && h.etape === "bébé") {
        point?.destroy();
        this.pointsHumains.delete(h.id);
        continue;
      }
      const etatComplet = `${etat}-${h.vivant && h.contagieux}`;
      if (!point || point.label !== etatComplet) {
        point?.destroy();
        point = new Graphics();
        point.label = etatComplet;
        if (h.vivant) {
          const rayon = h.etape === "enfant" ? 2.3 : 3.5;
          // Un malade contagieux est cerclé de vert.
          point.circle(0, 0, rayon).fill({ color: h.sexe === "femme" ? COULEUR_FEMME : COULEUR_HOMME })
            .stroke({ width: h.enceinte || h.contagieux ? 2 : 1, color: h.contagieux ? 0x7cdc50 : h.enceinte ? 0xffffff : 0x000000 });
        } else {
          point.moveTo(-3, -3).lineTo(3, 3).moveTo(3, -3).lineTo(-3, 3).stroke({ width: 1.5, color: 0x9aa7b8 });
        }
        this.pointsHumains.set(h.id, point);
        // Les morts restent sous les vivants.
        if (h.vivant) this.calqueHumains.addChild(point);
        else this.calqueHumains.addChildAt(point, 0);
      }
      // Léger décalage pour distinguer les deux membres d'un couple sur la même tuile.
      const decalage = h.etape === "enfant" ? 0.5 : h.sexe === "femme" ? 0.3 : 0.7;
      point.position.set(h.x + decalage, h.y + 0.5);
    }
    this.ajusterMarqueurs();
    this.placerSelection();
    this.dessinerHalo();
  }

  /** Entoure d'un halo les membres d'une communauté (liste vide : aucun). */
  surligner(ids: number[]): void {
    this.surlignes = new Set(ids);
    this.dessinerHalo();
  }

  /** Marque des lieux sacrés sur la carte (liste vide : aucun). */
  marquerLieuxSacres(lieux: { x: number; y: number }[]): void {
    this.lieux = lieux;
    this.dessinerHalo();
  }

  private dessinerHalo(): void {
    this.lieuxSacres.clear();
    if (this.lieuxSacres.parent !== this.monde && this.calqueHumains.parent === this.monde) {
      this.monde.addChildAt(this.lieuxSacres, this.monde.getChildIndex(this.calqueHumains));
    }
    for (const l of this.lieux) {
      const cx = l.x + 0.5;
      const cy = l.y + 0.5;
      this.lieuxSacres.poly([cx, cy - 1.6, cx + 0.5, cy - 0.5, cx + 1.6, cy, cx + 0.5, cy + 0.5, cx, cy + 1.6, cx - 0.5, cy + 0.5, cx - 1.6, cy, cx - 0.5, cy - 0.5])
        .fill({ color: 0xffe08a, alpha: 0.85 })
        .stroke({ width: 0.25, color: 0x5a3d00 });
    }
    this.halo.clear();
    // Le halo est en coordonnées de la carte : il est hors du calque des humains, dont les éléments sont mis à l'échelle du zoom.
    if (this.halo.parent !== this.monde && this.calqueHumains.parent === this.monde) {
      this.monde.addChildAt(this.halo, this.monde.getChildIndex(this.calqueHumains));
    }
    for (const id of this.surlignes) {
      const point = this.pointsHumains.get(id);
      if (!point) continue;
      this.halo.circle(point.position.x, point.position.y, 2.2)
        .fill({ color: 0xd9a8ff, alpha: 0.25 })
        .stroke({ width: 0.5, color: 0xd9a8ff, alpha: 0.9 });
    }
  }

  /** Appelé quand on clique (sans glisser) sur la carte. */
  quandClic(action: (info: InfoTuile) => void): void {
    this.surClic = action;
  }

  /** Entoure l'humain choisi (0 : aucun). */
  selectionner(id: number): void {
    this.selectionId = id;
    this.placerSelection();
  }

  private placerSelection(): void {
    const point = this.selectionId ? this.pointsHumains.get(this.selectionId) : undefined;
    // Un bébé n'a pas de point : on entoure sa mère.
    const h = this.humains.find((x) => x.id === this.selectionId);
    const cible = point ?? (h ? this.pointsHumains.get(h.mere) : undefined);
    this.selection.visible = !!cible;
    if (!cible) return;
    if (this.selection.parent !== this.calqueHumains) this.calqueHumains.addChild(this.selection);
    this.selection.position.copyFrom(cible.position);
    this.selection.scale.set(1 / this.monde.scale.x);
  }

  /** Assombrit la carte selon la clarté du jour (1 = plein jour, 0 = nuit). */
  eclairer(lumiere: number): void {
    this.nuit.alpha = (1 - Math.min(1, Math.max(0, lumiere))) * 0.55;
  }

  /** Ajuste le zoom pour voir toute la carte. */
  cadrer(): void {
    if (!this.carte) return;
    const { width, height } = this.app.screen;
    const echelle = Math.min(width / this.carte.largeur, height / this.carte.hauteur) * 0.92;
    this.echelleMin = echelle * 0.5;
    this.monde.scale.set(echelle);
    this.monde.position.set((width - this.carte.largeur * echelle) / 2, (height - this.carte.hauteur * echelle) / 2);
    this.ajusterMarqueurs();
  }

  /** Les marqueurs gardent la même taille à l'écran quel que soit le zoom. */
  private ajusterMarqueurs(): void {
    const inverse = 1 / this.monde.scale.x;
    for (const m of this.marqueurs.children) m.scale.set(inverse);
    for (const m of this.calqueHumains.children) m.scale.set(inverse);
    this.ajusterEchelle();
  }

  /** Humains sous le pointeur, du plus proche au plus éloigné. */
  humainsSous(info: InfoTuile): HumainVue[] {
    if (!info) return [];
    const d = (h: HumainVue) => Math.max(Math.abs(h.x - info.x), Math.abs(h.y - info.y));
    return [...info.humains].sort((a, b) => Number(b.vivant) - Number(a.vivant) || d(a) - d(b));
  }

  private zoomer(facteur: number, ecranX: number, ecranY: number): void {
    const ancienne = this.monde.scale.x;
    const nouvelle = Math.min(60, Math.max(this.echelleMin, ancienne * facteur));
    // Le point sous la souris reste fixe pendant le zoom.
    const mx = (ecranX - this.monde.x) / ancienne;
    const my = (ecranY - this.monde.y) / ancienne;
    this.monde.scale.set(nouvelle);
    this.monde.position.set(ecranX - mx * nouvelle, ecranY - my * nouvelle);
    this.ajusterMarqueurs();
  }

  private tuileSous(ecranX: number, ecranY: number): InfoTuile {
    if (!this.carte) return null;
    const x = Math.floor((ecranX - this.monde.x) / this.monde.scale.x);
    const y = Math.floor((ecranY - this.monde.y) / this.monde.scale.y);
    if (x < 0 || y < 0 || x >= this.carte.largeur || y >= this.carte.hauteur) return null;
    const rayon = Math.max(1, 6 / this.monde.scale.x);
    const emplacement = this.carte.emplacements.find((e) => Math.abs(e.x - x) <= rayon && Math.abs(e.y - y) <= rayon);
    const humains = this.humains.filter((h) => Math.abs(h.x - x) <= rayon && Math.abs(h.y - y) <= rayon);
    humains.sort((a, b) => Number(b.vivant) - Number(a.vivant));
    return { x, y, carte: this.carte, emplacement, humains };
  }

  private brancherSouris(): void {
    const canvas = this.app.canvas;
    let glisse: { x: number; y: number } | null = null;
    let depart: { x: number; y: number } | null = null;

    canvas.addEventListener("wheel", (ev) => {
      ev.preventDefault();
      this.zoomer(Math.exp(-ev.deltaY * 0.0015), ev.offsetX, ev.offsetY);
    }, { passive: false });

    canvas.addEventListener("pointerdown", (ev) => {
      glisse = { x: ev.clientX, y: ev.clientY };
      depart = { x: ev.clientX, y: ev.clientY };
      canvas.setPointerCapture(ev.pointerId);
      canvas.classList.add("glisse");
    });

    canvas.addEventListener("pointermove", (ev) => {
      if (glisse) {
        this.monde.x += ev.clientX - glisse.x;
        this.monde.y += ev.clientY - glisse.y;
        glisse = { x: ev.clientX, y: ev.clientY };
      }
      this.surSurvol(this.tuileSous(ev.offsetX, ev.offsetY));
    });

    const finGlisse = (ev: PointerEvent) => {
      glisse = null;
      if (canvas.hasPointerCapture(ev.pointerId)) canvas.releasePointerCapture(ev.pointerId);
      canvas.classList.remove("glisse");
    };
    canvas.addEventListener("pointerup", (ev) => {
      // Un clic : le pointeur n'a presque pas bougé depuis l'appui.
      if (depart && Math.hypot(ev.clientX - depart.x, ev.clientY - depart.y) < 5) {
        this.surClic(this.tuileSous(ev.offsetX, ev.offsetY));
      }
      depart = null;
      finGlisse(ev);
    });
    canvas.addEventListener("pointercancel", finGlisse);
    canvas.addEventListener("dblclick", () => this.cadrer());
  }
}
