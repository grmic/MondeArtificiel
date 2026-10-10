import { Application, Container, Graphics, Sprite, Texture } from "pixi.js";

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

export async function chargerCarte(graine?: number): Promise<Carte> {
  const url = graine === undefined ? "/api/carte" : `/api/carte?graine=${encodeURIComponent(String(graine))}`;
  const reponse = await fetch(url);
  if (!reponse.ok) throw new Error(`Le serveur a répondu ${reponse.status}`);
  const json = (await reponse.json()) as CarteJson;
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

export type InfoTuile = { x: number; y: number; carte: Carte; emplacement?: Emplacement } | null;

/** Vue de la carte : affichage, zoom à la molette, déplacement à la souris, survol des tuiles. */
export class VueCarte {
  private readonly monde = new Container();
  private readonly marqueurs = new Container();
  private readonly nuit = new Graphics();
  private sprite?: Sprite;
  private carte?: Carte;
  private echelleMin = 1;

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
    return new VueCarte(app, surSurvol);
  }

  afficher(carte: Carte): void {
    this.carte = carte;
    this.monde.removeChildren();
    this.sprite?.texture.destroy(true);

    const texture = Texture.from(dessinerImage(carte));
    texture.source.scaleMode = "nearest"; // tuiles nettes au zoom
    this.sprite = new Sprite(texture);
    this.monde.addChild(this.sprite);

    this.marqueurs.removeChildren();
    for (const e of carte.emplacements) {
      const point = new Graphics().circle(0, 0, 5).fill({ color: 0xe5484d }).stroke({ width: 2, color: 0xffffff });
      point.position.set(e.x + 0.5, e.y + 0.5);
      this.marqueurs.addChild(point);
    }
    this.nuit.clear().rect(0, 0, carte.largeur, carte.hauteur).fill({ color: 0x050a1e });
    this.monde.addChild(this.nuit, this.marqueurs);
    this.cadrer();
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
    return { x, y, carte: this.carte, emplacement };
  }

  private brancherSouris(): void {
    const canvas = this.app.canvas;
    let glisse: { x: number; y: number } | null = null;

    canvas.addEventListener("wheel", (ev) => {
      ev.preventDefault();
      this.zoomer(Math.exp(-ev.deltaY * 0.0015), ev.offsetX, ev.offsetY);
    }, { passive: false });

    canvas.addEventListener("pointerdown", (ev) => {
      glisse = { x: ev.clientX, y: ev.clientY };
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
    canvas.addEventListener("pointerup", finGlisse);
    canvas.addEventListener("pointercancel", finGlisse);
    canvas.addEventListener("dblclick", () => this.cadrer());
  }
}
