import { connexion } from "./connexion";
import { boutonCause, el } from "./fiche";

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
    const t = svg("text", { x: MARGE.g - 4, y: y(v) + 3, "text-anchor": "end", class: "axe" });
    t.textContent = String(v);
    graphe.append(t);
  }
  for (const a of graduations(debut, fin, 6)) {
    const t = svg("text", { x: x(a), y: HAUTEUR - 6, "text-anchor": "middle", class: "axe" });
    t.textContent = `an ${Math.round(a)}`;
    graphe.append(t);
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
    titre.textContent = `${m.date} : ${m.texte}`;
    rond.append(titre);
    rond.addEventListener("click", () => {
      detail.replaceChildren(el("span", "date", m.date), ` ${m.texte} `);
      if (m.cause > 0) detail.append(boutonCause(detail, m.cause, "pourquoi ?"));
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
    tete.append(el("h2", undefined, "Découvertes"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = "Fermer";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    racine.append(tete, this.corps);
    this.choix.id = "decouvertes-communaute";
    this.choix.title = "Monde entier ou une communauté";
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
      this.corps.replaceChildren(el("p", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    const defilement = this.corps.scrollTop;
    this.corps.replaceChildren(el("p", "discret",
      "Personne ne leur a appris : chaque savoir naît d'un essai, d'une observation, d'une réflexion ou d'une intuition de l'Entité, " +
      "se transmet par imitation ou parce qu'on le montre, et peut se perdre."));
    if (progres) this.afficherProgres(progres, communaute);
    if (liste.length === 0) this.corps.append(el("p", "discret", "Aucune découverte pour l'instant."));
    const ul = el("ul", "liste");
    for (const d of liste) {
      const li = el("li", d.detenteurs === 0 ? "discret" : undefined);
      const qui = el("button", "lien", d.nom);
      qui.type = "button";
      qui.addEventListener("click", () => this.ouvrirHumain(d.humain));
      li.append(el("span", "date", d.date), ` ${d.savoir} : `, qui, `, ${d.origine} · `,
        d.detenteurs === 0 ? "savoir perdu" : d.detenteurs === 1 ? "un seul vivant le sait" : `${d.detenteurs} vivants le savent`);
      if (d.origine.includes("Entité")) li.append(el("span", "entite", " ✦"));
      if (d.evenement > 0) li.append(" ", boutonCause(li, d.evenement, "comment ?"));
      ul.append(li);
    }
    this.corps.append(ul);
    this.corps.scrollTop = defilement;
  }

  /** Courbe des savoirs vivants, avec le choix du monde entier ou d'une communauté. */
  private afficherProgres(p: Progres, communaute: number): void {
    const options: [number, string][] = [[0, "Monde entier"], ...p.communautes.map((c): [number, string] => [c.id, c.active ? c.nom : `${c.nom} (dispersée)`])];
    if (!options.some(([id]) => id === communaute)) options.push([communaute, "Communauté disparue"]);
    this.choix.replaceChildren(...options.map(([id, nom]) => {
      const o = el("option", undefined, nom);
      o.value = String(id);
      return o;
    }));
    this.choix.value = String(communaute);
    const outils = el("div", "outils");
    outils.append("Savoirs vivants : ", this.choix);
    const dernier = p.points[p.points.length - 1];
    if (dernier) outils.append(el("span", "discret", ` ${dernier.savoirs} aujourd'hui`));
    this.progres.replaceChildren(dessinerProgres(p, this.detail));
    const legende = el("p", "discret",
      communaute === 0
        ? "Effets que sait obtenir au moins un vivant, au début de chaque année. En pointillé : reconstitué depuis le journal. "
          + "Points rouges : savoirs perdus ; verts : retrouvés."
        : "Effets que sait obtenir au moins un membre vivant, au début de chaque année depuis la 0.29.1.");
    this.corps.append(outils, this.progres, legende, this.detail);
  }
}
