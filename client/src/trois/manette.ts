/**
 * Manette de jeu (0.42.5) : PlayStation, Xbox ou autre, lue par l'API Gamepad du navigateur (disposition « standard » du W3C,
 * https://w3c.github.io/gamepad/#remapping). On lit l'état à chaque image (l'API n'envoie pas d'événement par bouton) : les
 * sticks avec une zone morte, les gâchettes analogiques, et les boutons tout juste appuyés.
 */

/** Indices des boutons de la disposition standard. */
export const BOUTON = {
  A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, LT: 6, RT: 7, VUE: 8, MENU: 9, L3: 10, R3: 11, HAUT: 12, BAS: 13, GAUCHE: 14, DROITE: 15,
} as const;

export type Famille = "playstation" | "xbox" | "autre";

/** Ce qu'une image lit de la manette. Sticks et gâchettes de −1 à 1 (0 à 1 pour les gâchettes), après la zone morte. */
export interface EtatManette {
  lx: number;
  ly: number;
  rx: number;
  ry: number;
  lt: number;
  rt: number;
  /** Boutons appuyés depuis la dernière lecture. */
  appuis: Set<number>;
  tenus: boolean[];
}

/** Zone morte des sticks : une manette usée ne revient pas tout à fait au centre (souvent 0,05 à 0,12). */
const ZONE_MORTE = 0.15;
const ZONE_GACHETTE = 0.06;

/** Stick : zone morte circulaire, puis courbe douce (les petits gestes restent fins, le stick au bout va vite). */
function stick(x: number, y: number): [number, number] {
  const r = Math.hypot(x, y);
  if (r < ZONE_MORTE) return [0, 0];
  const k = Math.min(1, (r - ZONE_MORTE) / (1 - ZONE_MORTE));
  const f = (k * k * 0.6 + k * 0.4) / r;
  return [x * f, y * f];
}

/** Famille d'après le nom donné par le navigateur (il contient souvent le fabricant : 054c Sony, 045e Microsoft). */
export function famille(id: string): Famille {
  const s = id.toLowerCase();
  if (/054c|playstation|dualsense|dualshock|sony|wireless controller/.test(s)) return "playstation";
  if (/045e|xbox|xinput|microsoft/.test(s)) return "xbox";
  return "autre";
}

/** Nom affiché de chaque bouton selon la famille (les « autres » suivent les lettres Xbox, les plus répandues). */
export function nomBouton(f: Famille, b: number): string {
  const ps = ["✕", "○", "□", "△", "L1", "R1", "L2", "R2", "Create", "Options", "L3", "R3", "↑", "↓", "←", "→"];
  const xb = ["A", "B", "X", "Y", "LB", "RB", "LT", "RT", "View", "Menu", "LS", "RS", "↑", "↓", "←", "→"];
  return (f === "playstation" ? ps : xb)[b] ?? String(b);
}

export class Manette {
  private index: number | null = null;
  private avant: boolean[] = [];
  private connue = false;
  /**
   * Axes qui, au branchement, reposaient loin du centre (0.44.6). Hors disposition standard, une gâchette ou un curseur se range
   * souvent parmi les axes et repose à −1 : lu comme un stick poussé à fond, il faisait tourner la caméra sans fin. Un tel axe
   * compte pour zéro tant qu'il reste près de sa position de repos.
   */
  private repos: (number | null)[] = [];
  /** Appuis vus entre deux images : à 5 images par seconde, un appui bref tomberait entre deux lectures. */
  private readonly enAttente = new Set<number>();
  private minuteur = 0;

  constructor(private readonly surConnexion: (f: Famille, branchee: boolean) => void) {}

  private readonly branchee = (e: GamepadEvent) => {
    if (this.index === null) this.choisir(e.gamepad);
  };

  private readonly debranchee = (e: GamepadEvent) => {
    if (e.gamepad.index !== this.index) return;
    this.index = null;
    this.avant = [];
    this.surConnexion(famille(e.gamepad.id), false);
    // Une autre manette encore branchée prend le relais.
    for (const g of this.toutes()) if (g.index !== e.gamepad.index) return this.choisir(g);
  };

  private choisir(g: Gamepad): void {
    this.index = g.index;
    this.enAttente.clear();
    // Les boutons déjà enfoncés au moment du branchement ne comptent pas comme des appuis.
    this.avant = g.buttons.map((b) => b.pressed);
    // 0.45.8 : en disposition standard, les quatre premiers axes sont les deux sticks, centrés au repos. Sous Windows, Chrome ne
    // montre la manette qu'au premier geste : si ce geste était de pousser le stick vers l'avant, l'axe était pris pour un axe
    // « au repos à −1 » et l'avant ne répondait plus jamais.
    this.repos = g.axes.map((v, i) => (Math.abs(v) > 0.5 && !(g.mapping === "standard" && i < 4) ? v : null));
    this.connue = true;
    this.surConnexion(famille(g.id), true);
  }

  private toutes(): Gamepad[] {
    try {
      return (navigator.getGamepads?.() ?? []).filter((g): g is Gamepad => g !== null && g.connected);
    } catch {
      // Refusé par la politique de la page (Permissions-Policy « gamepad ») : pas de manette.
      return [];
    }
  }

  brancher(): void {
    window.addEventListener("gamepadconnected", this.branchee);
    window.addEventListener("gamepaddisconnected", this.debranchee);
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => this.echantillonner(), 16);
  }

  /** Relevé des boutons toutes les 16 ms, indépendamment des images. */
  private echantillonner(): void {
    if (this.index === null) return;
    const g = this.toutes().find((x) => x.index === this.index);
    if (!g) return;
    g.buttons.forEach((b, i) => {
      if (b.pressed && !this.avant[i]) this.enAttente.add(i);
    });
    this.avant = g.buttons.map((b) => b.pressed);
  }

  debrancher(): void {
    window.removeEventListener("gamepadconnected", this.branchee);
    window.removeEventListener("gamepaddisconnected", this.debranchee);
    window.clearInterval(this.minuteur);
    this.minuteur = 0;
  }

  /** Famille de la manette en service, ou null. */
  get courante(): Famille | null {
    const g = this.index === null ? null : this.toutes().find((x) => x.index === this.index);
    return g ? famille(g.id) : null;
  }

  /** État à cette image, ou null sans manette. */
  lire(): EtatManette | null {
    const toutes = this.toutes();
    // Safari et certains navigateurs n'envoient « gamepadconnected » qu'au premier appui : on regarde aussi la liste.
    if (this.index === null || !toutes.some((g) => g.index === this.index)) {
      const g = toutes.find((x) => x.mapping === "standard") ?? toutes[0];
      if (!g) return null;
      if (this.index === null || !this.connue) this.choisir(g);
      else this.index = g.index;
    }
    const g = toutes.find((x) => x.index === this.index)!;
    const valeur = (i: number) => g.buttons[i]?.value ?? 0;
    this.echantillonner();
    const tenus = g.buttons.map((b) => b.pressed);
    const appuis = new Set(this.enAttente);
    this.enAttente.clear();
    const axe = (i: number) => {
      const v = g.axes[i] ?? 0;
      // Un axe pris pour « au repos loin du centre » qui revient au centre était en fait un stick : on l'oublie.
      if (Math.abs(v) < 0.15) this.repos[i] = null;
      const r = this.repos[i];
      return r !== null && r !== undefined && Math.abs(v - r) < 0.3 ? 0 : v;
    };
    const [lx, ly] = stick(axe(0), axe(1));
    const [rx, ry] = stick(axe(2), axe(3));
    const gachette = (v: number) => (v < ZONE_GACHETTE ? 0 : (v - ZONE_GACHETTE) / (1 - ZONE_GACHETTE));
    return { lx, ly, rx, ry, lt: gachette(valeur(BOUTON.LT)), rt: gachette(valeur(BOUTON.RT)), appuis, tenus };
  }

  /** Vibration douce (si la manette et le navigateur la permettent ; sinon rien). */
  vibrer(faible: number, forte: number, ms: number): void {
    const g = this.index === null ? null : this.toutes().find((x) => x.index === this.index);
    const v = (g as unknown as { vibrationActuator?: { playEffect?: (t: string, p: object) => Promise<unknown> } } | null | undefined)?.vibrationActuator;
    try {
      void v?.playEffect?.("dual-rumble", { startDelay: 0, duration: ms, weakMagnitude: faible, strongMagnitude: forte })?.catch(() => undefined);
    } catch {
      // Vibration non prise en charge.
    }
  }
}
