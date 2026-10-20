import { Application, Container, Graphics, Sprite, Texture } from "pixi.js";
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

const nombreFr = (v: number, decimales: number) =>
  v.toLocaleString("fr-FR", { maximumFractionDigits: decimales }).replace(/\u202f|\u00a0/g, " ");

/** Distance en mots : « 600 m », « 3,2 km », « 48 km ». */
export function distanceEnMots(km: number): string {
  if (km < 1) return `${Math.round((km * 1000) / 50) * 50} m`;
  return `${nombreFr(km, km < 10 ? 1 : 0)} km`;
}

/** Surface en mots : « 40 hectares », « 3,8 km² », « 1 250 km² ». */
export function surfaceEnMots(km2: number): string {
  if (km2 < 1) return `${Math.round(km2 * 100)} hectares`;
  return `${nombreFr(km2, km2 < 100 ? 1 : 0)} km²`;
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
  const requete = parametres.size === 0 ? "/api/carte" : `/api/carte?${parametres}`;
  const reponse = await fetch(requete);
  if (!reponse.ok) {
    const probleme = (await reponse.json().catch(() => null)) as { detail?: string } | null;
    throw new Error(probleme?.detail ?? `Le serveur a répondu ${reponse.status}`);
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

/** Dessine la carte dans un canvas, un pixel par tuile, avec un léger ombrage du relief. */
function dessinerImage(carte: Carte): HTMLCanvasElement {
  const canvas = document.createElement("canvas");
  canvas.width = carte.largeur;
  canvas.height = carte.hauteur;
  const ctx = canvas.getContext("2d")!;
  const image = ctx.createImageData(carte.largeur, carte.hauteur);
  for (let y = 0; y < carte.hauteur; y++) {
    for (let x = 0; x < carte.largeur; x++) {
      const i = y * carte.largeur + x;
      const [r, g, b] = COULEURS[carte.biomes[i]] ?? [255, 0, 255];
      let lumiere = 1;
      if (carte.altitudes[i] >= carte.niveauMer && x > 0 && y > 0) {
        // Lumière venant du nord-ouest : une pente qui lui fait face est plus claire.
        const pente = carte.altitudes[i] - carte.altitudes[i - carte.largeur - 1];
        lumiere = Math.min(1.25, Math.max(0.75, 1 + pente * 0.04));
      }
      const p = i * 4;
      image.data[p] = Math.min(255, r * lumiere);
      image.data[p + 1] = Math.min(255, g * lumiere);
      image.data[p + 2] = Math.min(255, b * lumiere);
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
    await app.init({ resizeTo: window, background: "#0f1a2b", antialias: true });
    hote.appendChild(app.canvas);
    const vue = new VueCarte(app, surSurvol);
    // Barre d'échelle en bas à gauche : sa longueur suit le zoom.
    vue.barreEchelle = document.createElement("div");
    vue.barreEchelle.className = "barre-echelle";
    vue.barreEchelle.setAttribute("aria-label", "Échelle de la carte");
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
    this.afficherNature(null);
    this.afficherTerritoires(null);
    this.afficherCatastrophes(null, [], null);
    this.carte = carte;
    this.monde.removeChildren();
    this.sprite?.texture.destroy(true);

    const texture = Texture.from(dessinerImage(carte));
    texture.source.scaleMode = "nearest"; // tuiles nettes au zoom
    this.sprite = new Sprite(texture);
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
    this.monde.addChild(this.nuit, this.calqueTerritoires, this.calqueNature, this.calqueCatastrophes, this.calqueTrajet, this.marqueurs, this.calqueHumains);
    this.cadrer();
  }

  /** Montre (ou cache, avec null) les ressources des terres et les régions touchées par la sécheresse ou très arrosées. */
  afficherNature(nature: Nature | null): void {
    this.nature = nature ?? undefined;
    for (const c of this.calqueNature.removeChildren()) c.destroy({ children: true, texture: true });
    if (!nature || !this.carte || nature.largeur !== this.carte.largeur || nature.hauteur !== this.carte.hauteur) return;
    const texture = Texture.from(dessinerAbondance(nature, this.carte));
    texture.source.scaleMode = "nearest";
    this.calqueNature.addChild(new Sprite(texture));
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
    const texture = Texture.from(canvas);
    texture.source.scaleMode = "nearest";
    this.calqueTerritoires.addChild(new Sprite(texture));
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
      const texture = Texture.from(dessinerCases(detail, this.carte.largeur, this.carte.hauteur));
      texture.source.scaleMode = "nearest";
      this.calqueCatastrophes.addChild(new Sprite(texture));
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
