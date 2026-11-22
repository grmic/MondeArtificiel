import { t } from "./langue";

/**
 * Fenêtres déplaçables (0.45.29) : chaque fenêtre se prend par sa barre de titre et se pose où l'on veut ; le coin en bas à
 * droite change sa taille. Tant qu'on n'y touche pas, elle garde sa place d'origine (celle de la feuille de style). Le
 * navigateur retient la place et la taille choisies ; un double-clic sur la barre de titre la remet à sa place d'origine.
 * Une fenêtre reste toujours dans l'écran, et s'aimante aux bords et au bas de la barre du haut quand on l'en approche.
 */
const CLE = "monde.fenetre.";
const MARGE = 12;
const AIMANT = 12;
const SEUIL = 4;
const LARGEUR_MIN = 220;
const HAUTEUR_MIN = 90;

interface Place {
  x: number;
  y: number;
  l?: number;
  h?: number;
}

export interface OptionsDeplacable {
  /** La zone qu'on attrape (barre de titre) ; par défaut les éléments `.tete`, sinon tout ce qui n'est pas un contrôle. */
  poignee?: (cible: Element) => boolean;
  /** Le coin agrandit-il aussi la largeur ? (le tiroir gère sa largeur onglet par onglet). */
  largeur?: (l: number) => void;
  /** Appelé quand la fenêtre est déplacée ou remise à sa place. */
  change?: () => void;
}

const fenetres: Fenetre[] = [];

const CONTROLES = "button, input, select, textarea, a, label, summary, option, canvas, [contenteditable], .lien, .croix, .coin-taille";

function lire(id: string): Place | null {
  try {
    const v = JSON.parse(localStorage.getItem(CLE + id) ?? "null") as Place | null;
    return v && Number.isFinite(v.x) && Number.isFinite(v.y) ? v : null;
  } catch {
    return null;
  }
}

function ecrire(id: string, p: Place | null): void {
  try {
    if (p) localStorage.setItem(CLE + id, JSON.stringify(p));
    else localStorage.removeItem(CLE + id);
  } catch {
    // Stockage indisponible : la place vaut pour cette page seulement.
  }
}

/** Le bas de la barre du haut (là où les fenêtres se posent d'habitude). */
function sousLaBarre(): number {
  const v = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--sous-barre"));
  return Number.isFinite(v) ? v : 0;
}

class Fenetre {
  place: Place | null;
  private readonly coin: HTMLElement;

  constructor(readonly el: HTMLElement, readonly id: string, private readonly options: OptionsDeplacable) {
    this.place = lire(id);
    this.coin = document.createElement("div");
    this.coin.className = "coin-taille";
    this.coin.title = t("fenetres.coinAide");
    this.el.classList.add("deplacable");
    this.el.append(this.coin);
    // Certaines fenêtres refont tout leur contenu : le coin revient à chaque fois.
    new MutationObserver(() => {
      if (this.coin.parentElement !== this.el) this.el.append(this.coin);
    }).observe(this.el, { childList: true });
    this.attraper();
    this.agrandir();
    this.appliquer();
  }

  private estPoignee(cible: Element): boolean {
    if (cible.closest(CONTROLES)) return false;
    if (this.options.poignee) return this.options.poignee(cible);
    if (this.el.querySelector(".tete")) return cible.closest(".tete") !== null;
    return true;
  }

  /** Pose la fenêtre à sa place choisie (ou d'origine), ramenée dans l'écran. */
  appliquer(): void {
    const s = this.el.style;
    if (!this.place) {
      for (const p of ["left", "top", "right", "bottom", "height", "max-height"]) s.removeProperty(p);
      if (!this.options.largeur) s.removeProperty("width");
      this.el.classList.remove("deplacee");
      this.options.change?.();
      return;
    }
    const p = this.place;
    this.el.classList.add("deplacee");
    if (p.l && !this.options.largeur) s.width = `${Math.min(p.l, window.innerWidth - 2 * MARGE)}px`;
    if (p.h) {
      s.height = `${Math.min(p.h, window.innerHeight - 2 * MARGE)}px`;
      s.maxHeight = "none";
    }
    s.right = "auto";
    s.bottom = "auto";
    s.left = `${p.x}px`;
    s.top = `${p.y}px`;
    this.garderDansLEcran();
    this.options.change?.();
  }

  /** Une fenêtre ne sort jamais de l'écran (même quand le navigateur rétrécit). */
  garderDansLEcran(): void {
    if (!this.place || this.el.hidden) return;
    const r = this.el.getBoundingClientRect();
    if (r.width === 0) return;
    const x = Math.max(0, Math.min(r.left, window.innerWidth - r.width));
    const y = Math.max(0, Math.min(r.top, window.innerHeight - Math.min(r.height, window.innerHeight)));
    this.el.style.left = `${x}px`;
    this.el.style.top = `${y}px`;
  }

  /** Aimant : à moins de 12 px d'un bord (marge habituelle comprise) ou du bas de la barre du haut, la fenêtre s'y colle. */
  private aimanter(x: number, y: number, l: number, h: number): { x: number; y: number } {
    const W = window.innerWidth;
    const H = window.innerHeight;
    if (Math.abs(x - MARGE) < AIMANT || x < MARGE) x = MARGE;
    if (Math.abs(W - (x + l) - MARGE) < AIMANT || x + l > W - MARGE) x = Math.max(MARGE, W - MARGE - l);
    const barre = sousLaBarre();
    if (Math.abs(y - barre) < AIMANT) y = barre;
    else if (Math.abs(y - MARGE) < AIMANT || y < MARGE) y = MARGE;
    if (Math.abs(H - (y + h) - MARGE) < AIMANT || y + h > H - MARGE) y = Math.max(MARGE, H - MARGE - h);
    return { x: Math.max(0, Math.min(x, W - l)), y: Math.max(0, Math.min(y, H - Math.min(h, H))) };
  }

  private attraper(): void {
    let depart: { px: number; py: number; x: number; y: number; l: number; h: number; parti: boolean; id: number } | null = null;
    // La fenêtre qu'on touche passe devant les autres (une fiche posée sur le tiroir, par exemple).
    this.el.addEventListener("pointerdown", () => {
      for (const f of fenetres) f.el.style.removeProperty("z-index");
      this.el.style.zIndex = "4";
    }, { capture: true });
    this.el.addEventListener("pointerdown", (e) => {
      if (e.button !== 0 || !(e.target instanceof Element) || !this.estPoignee(e.target)) return;
      const r = this.el.getBoundingClientRect();
      depart = { px: e.clientX, py: e.clientY, x: r.left, y: r.top, l: r.width, h: r.height, parti: false, id: e.pointerId };
    });
    window.addEventListener("pointermove", (e) => {
      if (!depart || e.pointerId !== depart.id) return;
      const dx = e.clientX - depart.px;
      const dy = e.clientY - depart.py;
      if (!depart.parti) {
        if (Math.hypot(dx, dy) < SEUIL) return;
        depart.parti = true;
        this.el.classList.add("en-deplacement");
        // La fenêtre quitte sa place de feuille de style : elle garde sa taille du moment.
        const s = this.el.style;
        s.right = "auto";
        s.bottom = "auto";
        s.height = `${depart.h}px`;
        s.maxHeight = "none";
        if (!this.options.largeur) s.width = `${depart.l}px`;
      }
      e.preventDefault();
      const p = this.aimanter(depart.x + dx, depart.y + dy, depart.l, depart.h);
      this.el.style.left = `${p.x}px`;
      this.el.style.top = `${p.y}px`;
    });
    const lacher = (e: PointerEvent) => {
      if (!depart || e.pointerId !== depart.id) return;
      const parti = depart.parti;
      depart = null;
      if (!parti) return;
      this.el.classList.remove("en-deplacement");
      // Le clic qui suit le lâcher ne doit pas ouvrir l'onglet ou le lien sous le pointeur.
      window.addEventListener("click", stopper, { capture: true, once: true });
      window.setTimeout(() => window.removeEventListener("click", stopper, { capture: true }), 0);
      this.retenir();
    };
    const stopper = (c: Event) => c.stopPropagation();
    window.addEventListener("pointerup", lacher);
    window.addEventListener("pointercancel", lacher);
    this.el.addEventListener("dblclick", (e) => {
      if (!(e.target instanceof Element) || !this.estPoignee(e.target)) return;
      this.remettre();
    });
  }

  private agrandir(): void {
    let depart: { px: number; py: number; l: number; h: number; x: number; y: number } | null = null;
    this.coin.addEventListener("pointerdown", (e) => {
      if (e.button !== 0) return;
      e.preventDefault();
      e.stopPropagation();
      this.coin.setPointerCapture(e.pointerId);
      const r = this.el.getBoundingClientRect();
      depart = { px: e.clientX, py: e.clientY, l: r.width, h: r.height, x: r.left, y: r.top };
      const s = this.el.style;
      s.left = `${r.left}px`;
      s.top = `${r.top}px`;
      s.right = "auto";
      s.bottom = "auto";
      s.maxHeight = "none";
      this.el.classList.add("en-deplacement");
    });
    this.coin.addEventListener("pointermove", (e) => {
      if (!depart) return;
      const l = Math.max(LARGEUR_MIN, Math.min(depart.l + e.clientX - depart.px, window.innerWidth - depart.x));
      const h = Math.max(HAUTEUR_MIN, Math.min(depart.h + e.clientY - depart.py, window.innerHeight - depart.y));
      if (this.options.largeur) this.options.largeur(l);
      else this.el.style.width = `${l}px`;
      this.el.style.height = `${h}px`;
    });
    const lacher = () => {
      if (!depart) return;
      depart = null;
      this.el.classList.remove("en-deplacement");
      this.retenir();
    };
    this.coin.addEventListener("pointerup", lacher);
    this.coin.addEventListener("pointercancel", lacher);
    this.coin.addEventListener("dblclick", (e) => {
      e.stopPropagation();
      this.remettre();
    });
  }

  private retenir(): void {
    // La taille se mesure bordures comprises (comme elle sera reposée), donc après avoir marqué la fenêtre comme déplacée.
    this.el.classList.add("deplacee");
    const r = this.el.getBoundingClientRect();
    this.place = { x: Math.round(r.left), y: Math.round(r.top), l: Math.round(r.width), h: Math.round(r.height) };
    ecrire(this.id, this.place);
    this.options.change?.();
  }

  remettre(): void {
    this.place = null;
    ecrire(this.id, null);
    this.appliquer();
  }
}

/** Rend une fenêtre déplaçable ; `id` sert de clé pour retenir sa place. */
export function rendreDeplacable(el: HTMLElement, id: string, options: OptionsDeplacable = {}): { remettre: () => void } {
  const f = new Fenetre(el, id, options);
  fenetres.push(f);
  // Une fenêtre cachée au moment où l'écran a changé de taille y est ramenée quand elle se montre.
  new ResizeObserver(() => f.garderDansLEcran()).observe(el);
  return f;
}

/** Toutes les fenêtres reviennent à leur place et à leur taille d'origine. */
export function remettreToutesLesFenetres(): void {
  for (const f of fenetres) f.remettre();
}

window.addEventListener("resize", () => {
  for (const f of fenetres) f.garderDansLEcran();
});
