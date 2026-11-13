import { imageCarte, type Carte } from "../carte";
import { t } from "../langue";

/** Ce que la mini-carte montre à chaque mise à jour. */
export interface EtatMiniCarte {
  /** Caméra, en mètres (x vers l'est, z vers le sud). */
  x: number;
  z: number;
  /** Direction du regard (lacet de la caméra) et demi-ouverture du champ, en radians. */
  lacet: number;
  champ: number;
  /** Portée du regard dessinée, en mètres. */
  portee: number;
  /** Vivants connus, en cases. */
  vivants: { x: number; y: number }[];
}

const ZOOMS = [1, 2, 4, 8];

/**
 * Mini-carte 2D de la vue 3D (0.41.1) : tout le monde en petit, où l'on est et ce qu'on regarde, les vivants en points. Un clic
 * emmène la caméra à l'endroit choisi ; la molette rapproche la mini-carte autour de la caméra ; le bouton l'agrandit.
 */
export class MiniCarte {
  readonly element: HTMLElement;
  private readonly toile: HTMLCanvasElement;
  private readonly fond: HTMLCanvasElement;
  private zoom = 0;
  private grande = false;
  private dernier: EtatMiniCarte | null = null;
  /** Coin haut-gauche et taille (en cases) de la partie de la carte dessinée. */
  private vue = { x: 0, y: 0, cote: 1 };

  constructor(
    private readonly carte: Carte,
    private readonly metresParCase: number,
    private readonly aller: (x: number, z: number) => void,
  ) {
    this.fond = imageCarte(carte, 2);
    this.element = document.createElement("div");
    this.element.className = "minicarte3d";
    this.toile = document.createElement("canvas");
    const barre = document.createElement("div");
    barre.className = "outils";
    const plus = Object.assign(document.createElement("button"), { type: "button", textContent: "+", title: t("trois.mini.rapprocher") });
    const moins = Object.assign(document.createElement("button"), { type: "button", textContent: "−", title: t("trois.mini.eloigner") });
    const taille = Object.assign(document.createElement("button"), { type: "button", textContent: "⤢", title: t("trois.mini.agrandir") });
    plus.addEventListener("click", () => this.zoomer(1));
    moins.addEventListener("click", () => this.zoomer(-1));
    taille.addEventListener("click", () => {
      this.grande = !this.grande;
      this.element.classList.toggle("grande", this.grande);
      this.redessiner();
    });
    barre.append(plus, moins, taille);
    this.element.append(this.toile, barre);
    this.element.title = t("trois.mini.aide");
    this.toile.addEventListener("click", (e) => {
      const r = this.toile.getBoundingClientRect();
      const cx = this.vue.x + ((e.clientX - r.left) / r.width) * this.vue.cote;
      const cy = this.vue.y + ((e.clientY - r.top) / r.height) * this.vue.cote;
      if (cx < 0 || cy < 0 || cx >= this.carte.largeur || cy >= this.carte.hauteur) return;
      this.aller(cx * this.metresParCase, cy * this.metresParCase);
    });
    this.toile.addEventListener("wheel", (e) => {
      e.preventDefault();
      e.stopPropagation();
      this.zoomer(e.deltaY < 0 ? 1 : -1);
    }, { passive: false });
  }

  private zoomer(sens: number): void {
    this.zoom = Math.max(0, Math.min(ZOOMS.length - 1, this.zoom + sens));
    this.redessiner();
  }

  private redessiner(): void {
    if (this.dernier) this.dessiner(this.dernier);
  }

  dessiner(etat: EtatMiniCarte): void {
    this.dernier = etat;
    const { largeur: l, hauteur: h } = this.carte;
    const cssCote = this.element.clientWidth || 200;
    const ratio = window.devicePixelRatio || 1;
    const px = Math.round(cssCote * ratio);
    if (this.toile.width !== px) {
      this.toile.width = px;
      this.toile.height = px;
    }
    const camX = etat.x / this.metresParCase;
    const camY = etat.z / this.metresParCase;
    // Partie visible : toute la carte (carrée, centrée) au premier zoom ; autour de la caméra ensuite.
    const tout = Math.max(l, h);
    const cote = tout / ZOOMS[this.zoom];
    let vx = this.zoom === 0 ? (l - tout) / 2 : camX - cote / 2;
    let vy = this.zoom === 0 ? (h - tout) / 2 : camY - cote / 2;
    if (this.zoom > 0) {
      vx = Math.max(Math.min(vx, l - cote), Math.min(0, l - cote));
      vy = Math.max(Math.min(vy, h - cote), Math.min(0, h - cote));
    }
    this.vue = { x: vx, y: vy, cote };
    const ctx = this.toile.getContext("2d")!;
    const k = px / cote;
    ctx.fillStyle = "#0d2230";
    ctx.fillRect(0, 0, px, px);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = "high";
    ctx.drawImage(this.fond, 0, 0, this.fond.width, this.fond.height, -vx * k, -vy * k, l * k, h * k);
    // Les vivants.
    ctx.fillStyle = "#ffe08a";
    const r = Math.max(1.2, Math.min(3, k * 0.35)) * ratio;
    for (const v of etat.vivants) {
      const sx = (v.x + 0.5 - vx) * k;
      const sy = (v.y + 0.5 - vy) * k;
      if (sx < -r || sy < -r || sx > px + r || sy > px + r) continue;
      ctx.fillRect(sx - r / 2, sy - r / 2, r, r);
    }
    // Ce que regarde la caméra : un éventail de la portée du regard.
    const sx = (camX - vx) * k;
    const sy = (camY - vy) * k;
    const portee = Math.max(10 * ratio, (etat.portee / this.metresParCase) * k);
    // Le lacet 0 regarde vers −z (le nord) ; en 2D, le nord est en haut.
    const angle = -etat.lacet - Math.PI / 2;
    ctx.fillStyle = "rgba(255, 255, 255, 0.22)";
    ctx.beginPath();
    ctx.moveTo(sx, sy);
    ctx.arc(sx, sy, portee, angle - etat.champ, angle + etat.champ);
    ctx.closePath();
    ctx.fill();
    ctx.fillStyle = "#ff5a3c";
    ctx.strokeStyle = "#ffffff";
    ctx.lineWidth = 1.5 * ratio;
    ctx.beginPath();
    ctx.arc(sx, sy, 4 * ratio, 0, Math.PI * 2);
    ctx.fill();
    ctx.stroke();
  }
}
