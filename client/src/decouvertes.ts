import { connexion } from "./connexion";
import { boutonCause, el } from "./fiche";
import { t } from "./langue";

/** Première fois qu'un savoir est apparu dans le monde. */
interface Decouverte {
  date: string;
  secondes: number;
  savoir: string;
  humain: number;
  nom: string;
  origine: string;
  evenement: number;
  /** Vivants qui le savent encore (0 : savoir perdu). */
  detenteurs: number;
}

/** Courbe du progrès : savoirs vivants au début de chaque année, pertes et retrouvailles, communautés qu'on peut choisir. */
interface Progres {
  points: { annee: number; savoirs: number; reconstitue: boolean }[];
  marques: { annee: number; date: string; evenement: number; perdu: boolean; texte: string; cause: number }[];
  communautes: { id: number; nom: string; active: boolean }[];
}

const ACTUALISATION_MS = 5000;
const SVG = "http://www.w3.org/2000/svg";
const LARGEUR = 560;
const HAUTEUR = 150;
const MARGE = { g: 34, d: 8, h: 8, b: 20 };

function svg<K extends keyof SVGElementTagNameMap>(tag: K, attributs: Record<string, string | number> = {}): SVGElementTagNameMap[K] {
  const e = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attributs)) e.setAttribute(k, String(v));
  return e;
}

/** Graduations « rondes » (1, 2, 5 × 10ⁿ) entre min et max. */
function graduations(min: number, max: number, cible: number): number[] {
  if (max <= min) return [min];
  const brut = (max - min) / cible;
  const p = 10 ** Math.floor(Math.log10(brut));
  const pas = [1, 2, 5, 10].map((m) => m * p).find((x) => x >= brut) ?? 10 * p;
  const res: number[] = [];
  for (let v = Math.ceil(min / pas) * pas; v <= max + 1e-9; v += pas) res.push(v);
  return res;
}

/**
 * Dessine la courbe des savoirs vivants : trait plein pour les années mesurées, pointillé pour celles reconstituées
 * depuis le journal ; un point rouge par savoir perdu, vert par savoir retrouvé (un clic en montre l'événement).
 */
function dessinerProgres(p: Progres, detail: HTMLElement): SVGSVGElement {
  const graphe = svg("svg", { viewBox: `0 0 ${LARGEUR} ${HAUTEUR}`, width: "100%", role: "img" });
  const pts = p.points;
  const debut = Math.min(1, ...pts.map((x) => x.annee));
  const fin = Math.max(debut + 1, ...pts.map((x) => x.annee));
  const max = Math.max(4, ...pts.map((x) => x.savoirs));
  const x = (a: number) => MARGE.g + ((a - debut) / (fin - debut)) * (LARGEUR - MARGE.g - MARGE.d);
  const y = (v: number) => HAUTEUR - MARGE.b - (v / max) * (HAUTEUR - MARGE.b - MARGE.h);
  for (const v of graduations(0, max, 4)) {
    graphe.append(svg("line", { x1: MARGE.g, x2: LARGEUR - MARGE.d, y1: y(v), y2: y(v), class: "grille" }));
    const etiquette = svg("text", { x: MARGE.g - 4, y: y(v) + 3, "text-anchor": "end", class: "axe" });
    etiquette.textContent = String(v);
    graphe.append(etiquette);
  }
  for (const a of graduations(debut, fin, 6)) {
    const etiquette = svg("text", { x: x(a), y: HAUTEUR - 6, "text-anchor": "middle", class: "axe" });
    etiquette.textContent = t("decouvertes.an", { an: String(Math.round(a)) });
    graphe.append(etiquette);
  }
  graphe.append(svg("line", { x1: MARGE.g, x2: LARGEUR - MARGE.d, y1: y(0), y2: y(0), class: "axe-trait" }));
  const trace = (liste: typeof pts, classe: string) => {
    if (liste.length === 0) return;
    graphe.append(svg("polyline", { points: liste.map((q) => `${x(q.annee)},${y(q.savoirs)}`).join(" "), class: classe }));
  };
  const reconstitues = pts.filter((q) => q.reconstitue);
  const mesures = pts.filter((q) => !q.reconstitue);
  // Le pointillé rejoint le premier point mesuré, pour que la courbe ne se coupe pas.
  trace(mesures.length > 0 ? [...reconstitues, mesures[0]] : reconstitues, "progres reconstitue");
  trace(mesures, "progres");
  // Hauteur de la courbe à une date : entre deux mesures, sur le segment qui les relie.
  const valeurA = (a: number) => {
    if (pts.length === 0) return 0;
    if (a <= pts[0].annee) return pts[0].savoirs;
    for (let i = 1; i < pts.length; i++) {
      const [p0, p1] = [pts[i - 1], pts[i]];
      if (a <= p1.annee) return p0.savoirs + ((a - p0.annee) / Math.max(1e-9, p1.annee - p0.annee)) * (p1.savoirs - p0.savoirs);
    }
    return pts[pts.length - 1].savoirs;
  };
  for (const m of p.marques) {
    const rond = svg("circle", { cx: x(m.annee), cy: y(valeurA(m.annee)), r: 3.5, class: `marque ${m.perdu ? "perdu" : "retrouve"}` });
    const titre = svg("title");
    titre.textContent = t("commun.dateTexte", { date: m.date, texte: m.texte });
    rond.append(titre);
    rond.addEventListener("click", () => {
      detail.replaceChildren(el("span", "date", m.date), ` ${m.texte} `);
      if (m.cause > 0) detail.append(boutonCause(detail, m.cause, t("commun.pourquoi")));
    });
    graphe.append(rond);
  }
  return graphe;
}

/** Fenêtre « Découvertes » : chronologie des premiers savoirs du monde, qui les a trouvés, comment, et s'ils sont encore connus. */
export class PanneauDecouvertes {
  private readonly corps = el("div", "corps");
  private readonly progres = el("div", "graphique");
  private readonly choix = el("select");
  private readonly detail = el("p", "discret");
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("decouvertes.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    racine.append(tete, this.corps);
    this.choix.id = "decouvertes-communaute";
    this.choix.title = t("decouvertes.choix");
    this.choix.addEventListener("change", () => void this.charger());
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  async ouvrir(): Promise<void> {
    this.racine.hidden = false;
    await this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
  }

  private async charger(): Promise<void> {
    const communaute = Number(this.choix.value || "0");
    const [liste, progres] = await Promise.all([
      connexion.invoke<Decouverte[]>("Decouvertes").catch(() => null),
      connexion.invoke<Progres>("Progres", communaute).catch(() => null),
    ]);
    if (!this.ouvert) return;
    if (!liste) {
      this.corps.replaceChildren(el("p", "discret", t("commun.pasDeReponse")));
      return;
    }
    const defilement = this.corps.scrollTop;
    this.corps.replaceChildren(el("p", "discret", t("decouvertes.aide")));
    if (progres) this.afficherProgres(progres, communaute);
    if (liste.length === 0) this.corps.append(el("p", "discret", t("decouvertes.aucune")));
    const ul = el("ul", "liste");
    for (const d of liste) {
      const li = el("li", d.detenteurs === 0 ? "discret" : undefined);
      const qui = el("button", "lien", d.nom);
      qui.type = "button";
      qui.addEventListener("click", () => this.ouvrirHumain(d.humain));
      li.append(el("span", "date", d.date), " " + t("decouvertes.savoir", { savoir: d.savoir }), qui, t("decouvertes.origine", { origine: d.origine }),
        d.detenteurs === 0 ? t("decouvertes.perdu") : t("decouvertes.detenteurs", { n: d.detenteurs }));
      if (d.origine === t("origineSavoir.Entite")) li.append(el("span", "entite", " ✦"));
      if (d.evenement > 0) li.append(" ", boutonCause(li, d.evenement, t("commun.comment")));
      ul.append(li);
    }
    this.corps.append(ul);
    this.corps.scrollTop = defilement;
  }

  /** Courbe des savoirs vivants, avec le choix du monde entier ou d'une communauté. */
  private afficherProgres(p: Progres, communaute: number): void {
    const options: [number, string][] = [[0, t("decouvertes.monde")], ...p.communautes.map((c): [number, string] => [c.id, c.active ? c.nom : t("decouvertes.dispersee", { nom: c.nom })])];
    if (!options.some(([id]) => id === communaute)) options.push([communaute, t("decouvertes.disparue")]);
    this.choix.replaceChildren(...options.map(([id, nom]) => {
      const o = el("option", undefined, nom);
      o.value = String(id);
      return o;
    }));
    this.choix.value = String(communaute);
    const outils = el("div", "outils");
    outils.append(t("decouvertes.vivants"), this.choix);
    const dernier = p.points[p.points.length - 1];
    if (dernier) outils.append(el("span", "discret", " " + t("decouvertes.aujourdhui", { n: dernier.savoirs })));
    this.progres.replaceChildren(dessinerProgres(p, this.detail));
    const legende = el("p", "discret",
      communaute === 0
        ? t("decouvertes.legendeMonde")
        : t("decouvertes.legendeCommunaute"));
    this.corps.append(outils, this.progres, legende, this.detail);
  }
}
