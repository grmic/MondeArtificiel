import { connexion } from "./connexion";
import { boutonCause, el } from "./fiche";
import { nombre, t } from "./langue";

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
  /** parHumain : savoirs que sait en moyenne chaque vivant (−1 ou absent : pas mesuré, avant la 0.33.1). */
  points: { annee: number; savoirs: number; reconstitue: boolean; parHumain?: number }[];
  marques: { annee: number; date: string; evenement: number; perdu: boolean; texte: string; cause: number }[];
  communautes: { id: number; nom: string; active: boolean }[];
}

/** Un nœud de l'arbre des savoirs : une trouvaille, et les savoirs dont elle est née (dessiné après coup). */
interface Noeud {
  cle: string;
  nom: string;
  date: string;
  secondes: number;
  humain: number;
  nomHumain: string;
  origine: string;
  entite: boolean;
  detenteurs: number;
  evenement: number;
  parents: string[];
  profondeur: number;
  /** Celui avec qui l'idée est née (0 : seul). */
  coauteur: number;
  nomCoauteur: string;
}

interface Arbre {
  noeuds: Noeud[];
  adjacents: string[];
  totalAdjacents: number;
}

const ACTUALISATION_MS = 5000;
const NOEUD = { l: 168, h: 20, ecartX: 44, ecartY: 6 };
const SVG = "http://www.w3.org/2000/svg";
const LARGEUR = 560;
const HAUTEUR = 150;
const MARGE = { g: 34, d: 34, h: 8, b: 20 };
/** Durées qu'on peut afficher, en années (0 : tout), comme pour la démographie. */
const PERIODES: [string, number][] = [["demographie.tout", 0], ["", 50], ["", 10], ["", 2]];

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
function dessinerProgres(p: Progres, detail: HTMLElement, periode: number): SVGSVGElement {
  const graphe = svg("svg", { viewBox: `0 0 ${LARGEUR} ${HAUTEUR}`, width: "100%", role: "img" });
  const tous = p.points;
  const premier = Math.min(1, ...tous.map((x) => x.annee));
  const fin = Math.max(premier + 1, ...tous.map((x) => x.annee));
  // La durée choisie : les dernières années seulement (avec le point qui précède, pour que la courbe entre par la gauche).
  const debut = periode > 0 ? Math.max(premier, fin - periode) : premier;
  const avant = tous.filter((q) => q.annee < debut).pop();
  const pts = [...(avant ? [avant] : []), ...tous.filter((q) => q.annee >= debut)];
  const visibles = pts.filter((q) => q.annee >= debut);
  const max = Math.max(4, ...visibles.map((x) => x.savoirs), ...(avant ? [avant.savoirs] : []));
  // Ce qui déborde à gauche est coupé au bord du graphique.
  const coupe = `coupe-progres-${periode}`;
  const defs = svg("defs");
  const clip = svg("clipPath", { id: coupe });
  clip.append(svg("rect", { x: MARGE.g, y: 0, width: LARGEUR - MARGE.g - MARGE.d, height: HAUTEUR }));
  defs.append(clip);
  graphe.append(defs);
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
    graphe.append(svg("polyline", { points: liste.map((q) => `${x(q.annee)},${y(q.savoirs)}`).join(" "), class: classe, "clip-path": `url(#${coupe})` }));
  };
  const reconstitues = pts.filter((q) => q.reconstitue);
  const mesures = pts.filter((q) => !q.reconstitue);
  // Le pointillé rejoint le premier point mesuré, pour que la courbe ne se coupe pas.
  trace(mesures.length > 0 ? [...reconstitues, mesures[0]] : reconstitues, "progres reconstitue");
  trace(mesures, "progres");
  // Diffusion : combien de savoirs chaque vivant sait en moyenne, sur l'échelle de droite.
  const diffusion = pts.filter((q) => (q.parHumain ?? -1) >= 0);
  if (diffusion.length > 0) {
    const maxD = Math.max(1, ...diffusion.map((q) => q.parHumain ?? 0));
    const yD = (v: number) => HAUTEUR - MARGE.b - (v / maxD) * (HAUTEUR - MARGE.b - MARGE.h);
    for (const v of graduations(0, maxD, 4)) {
      const etiquette = svg("text", { x: LARGEUR - MARGE.d + 4, y: yD(v) + 3, "text-anchor": "start", class: "axe diffusion" });
      etiquette.textContent = String(v);
      graphe.append(etiquette);
    }
    graphe.append(svg("polyline", { points: diffusion.map((q) => `${x(q.annee)},${yD(q.parHumain ?? 0)}`).join(" "), class: "diffusion", "clip-path": `url(#${coupe})` }));
  }
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
    if (m.annee < debut) continue;
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

/** Coupe un nom trop long pour sa case (le nom entier reste dans l'infobulle). */
function court(texte: string, max: number): string {
  return texte.length <= max ? texte : `${texte.slice(0, max - 1)}…`;
}

/**
 * Dessine l'arbre : une colonne par profondeur (les racines à gauche), les trouvailles de chaque colonne par date, un trait
 * de chaque parent vers ce qui en est né. Un clic sur un nœud en dit plus et souligne ses parents.
 */
function dessinerArbre(a: Arbre, choisi: string, surChoix: (n: Noeud) => void): SVGSVGElement {
  const colonnes: Noeud[][] = [];
  for (const n of a.noeuds) (colonnes[n.profondeur] ??= []).push(n);
  for (const c of colonnes) c?.sort((x, y) => x.secondes - y.secondes);
  const place = new Map<string, { x: number; y: number }>();
  colonnes.forEach((c, i) => c?.forEach((n, j) => place.set(n.cle, { x: 4 + i * (NOEUD.l + NOEUD.ecartX), y: 4 + j * (NOEUD.h + NOEUD.ecartY) })));
  const largeur = 8 + colonnes.length * (NOEUD.l + NOEUD.ecartX) - NOEUD.ecartX;
  const hauteur = 8 + Math.max(1, ...colonnes.map((c) => c?.length ?? 0)) * (NOEUD.h + NOEUD.ecartY);
  const graphe = svg("svg", { viewBox: `0 0 ${largeur} ${hauteur}`, width: largeur, height: hauteur, class: "arbre", role: "img" });
  const traits = svg("g");
  const boites = svg("g");
  graphe.append(traits, boites);
  const liens: { parent: string; enfant: string; trait: SVGPathElement }[] = [];
  for (const n of a.noeuds) {
    const b = place.get(n.cle);
    if (!b) continue;
    for (const p of n.parents) {
      const q = place.get(p);
      if (!q) continue;
      const [x1, y1, x2, y2] = [q.x + NOEUD.l, q.y + NOEUD.h / 2, b.x, b.y + NOEUD.h / 2];
      const m = (x1 + x2) / 2;
      const trait = svg("path", { d: `M${x1},${y1} C${m},${y1} ${m},${y2} ${x2},${y2}`, class: "lien-arbre" });
      traits.append(trait);
      liens.push({ parent: p, enfant: n.cle, trait });
    }
  }
  const cases = new Map<string, SVGGElement>();
  const souligner = (n: Noeud) => {
    for (const c of cases.values()) c.classList.remove("choisi", "parent");
    cases.get(n.cle)?.classList.add("choisi");
    for (const p of n.parents) cases.get(p)?.classList.add("parent");
    for (const l of liens) l.trait.classList.toggle("choisi", l.enfant === n.cle);
  };
  for (const n of a.noeuds) {
    const b = place.get(n.cle);
    if (!b || cases.has(n.cle)) continue;
    const g = svg("g", { class: `noeud${n.detenteurs === 0 ? " perdu" : ""}${n.entite ? " entite" : ""}`, transform: `translate(${b.x},${b.y})` });
    g.append(svg("rect", { width: NOEUD.l, height: NOEUD.h, rx: 4 }));
    const texte = svg("text", { x: 6, y: NOEUD.h / 2 + 4 });
    texte.textContent = court(`${n.entite ? "✦ " : ""}${n.nom}`, 26);
    const titre = svg("title");
    titre.textContent = `${n.nom} (${n.date})`;
    g.append(texte, titre);
    g.addEventListener("click", () => {
      souligner(n);
      surChoix(n);
    });
    cases.set(n.cle, g);
    boites.append(g);
  }
  // Le nœud choisi reste souligné d'un rafraîchissement à l'autre (sans réécrire le détail ouvert).
  const deja = a.noeuds.find((n) => n.cle === choisi);
  if (deja) souligner(deja);
  return graphe;
}

/** Fenêtre « Découvertes » : chronologie des premiers savoirs du monde, qui les a trouvés, comment, et s'ils sont encore connus. */
export class PanneauDecouvertes {
  private readonly corps = el("div", "corps");
  private readonly progres = el("div", "graphique");
  /** Durée affichée de la courbe, en années (0 : tout). */
  private periode = 0;
  private readonly choix = el("select");
  private readonly detail = el("p", "discret");
  /** Vue de l'arbre des savoirs plutôt que de la chronologie. */
  private arbre = false;
  private readonly boutonListe = el("button", undefined, t("arbre.liste"));
  private readonly boutonArbre = el("button", undefined, `🌳 ${t("arbre.bouton")}`);
  private readonly detailArbre = el("p", "discret");
  private noeudChoisi = "";
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
    for (const [b, arbre] of [[this.boutonListe, false], [this.boutonArbre, true]] as const) {
      b.type = "button";
      b.addEventListener("click", () => {
        this.arbre = arbre;
        this.noeudChoisi = "";
        this.detailArbre.replaceChildren();
        void this.charger();
      });
    }
  }

  /** Les deux vues : chronologie (avec la courbe du progrès) ou arbre des savoirs. */
  private onglets(): HTMLElement {
    const o = el("div", "onglets");
    this.boutonListe.classList.toggle("actif", !this.arbre);
    this.boutonArbre.classList.toggle("actif", this.arbre);
    o.append(this.boutonListe, this.boutonArbre);
    return o;
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
    if (this.arbre) return this.chargerArbre();
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
    this.corps.replaceChildren(this.onglets(), el("p", "discret", t("decouvertes.aide")));
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

  /** Arbre des savoirs du monde ou d'une communauté, puis le possible adjacent. */
  private async chargerArbre(): Promise<void> {
    const communaute = Number(this.choix.value || "0");
    const [a, progres] = await Promise.all([
      connexion.invoke<Arbre>("ArbreDesSavoirs", communaute).catch(() => null),
      connexion.invoke<Progres>("Progres", communaute).catch(() => null),
    ]);
    if (!this.ouvert || !this.arbre) return;
    if (!a) {
      this.corps.replaceChildren(this.onglets(), el("p", "discret", t("commun.pasDeReponse")));
      return;
    }
    const defilement = this.corps.scrollTop;
    const cadre = this.corps.querySelector(".cadre-arbre");
    const [gauche, haut] = cadre ? [cadre.scrollLeft, cadre.scrollTop] : [0, 0];
    this.corps.replaceChildren(this.onglets(), el("p", "discret", t("arbre.aide")));
    if (progres) this.corps.append(this.outilsCommunaute(progres, communaute));
    if (a.noeuds.length === 0) this.corps.append(el("p", "discret", t("arbre.vide")));
    else {
      const noms = new Map(a.noeuds.map((n) => [n.cle, n.nom]));
      const choisir = (n: Noeud) => {
        this.noeudChoisi = n.cle;
        this.detailArbre.replaceChildren(t("arbre.detail", {
          nom: n.nom, date: n.date, humain: n.coauteur > 0 ? t("arbre.avec", { a: n.nomHumain, b: n.nomCoauteur }) : n.nomHumain, origine: n.origine,
          detenteurs: n.detenteurs === 0 ? t("arbre.perdu") : t("arbre.detenteurs", { n: n.detenteurs }),
        }), " ", n.parents.length === 0 ? t("arbre.racine") : t("arbre.parents", { liste: n.parents.map((p) => noms.get(p) ?? p).join(t("commun.virgule")) }), " ");
        const qui = el("button", "lien", n.nomHumain);
        qui.type = "button";
        qui.addEventListener("click", () => this.ouvrirHumain(n.humain));
        this.detailArbre.append(qui);
        if (n.coauteur > 0) {
          const avec = el("button", "lien", n.nomCoauteur);
          avec.type = "button";
          avec.addEventListener("click", () => this.ouvrirHumain(n.coauteur));
          this.detailArbre.append(" ", avec);
        }
        if (n.evenement > 0) this.detailArbre.append(" ", boutonCause(this.detailArbre, n.evenement, t("commun.comment")));
      };
      const dessin = dessinerArbre(a, this.noeudChoisi, choisir);
      const nouveauCadre = el("div", "cadre-arbre");
      nouveauCadre.append(dessin);
      this.corps.append(nouveauCadre, this.detailArbre);
      nouveauCadre.scrollLeft = gauche;
      nouveauCadre.scrollTop = haut;
    }
    if (a.totalAdjacents === 0) this.corps.append(el("p", "discret", t("arbre.aucunAdjacent")));
    else {
      this.corps.append(el("p", "discret", t("arbre.adjacents", { n: a.totalAdjacents })));
      const ul = el("ul", "liste adjacents");
      for (const x of a.adjacents) ul.append(el("li", undefined, x));
      this.corps.append(ul);
    }
    this.corps.scrollTop = defilement;
  }

  /** Choix du monde entier ou d'une communauté (le même menu pour la courbe et pour l'arbre). */
  private outilsCommunaute(p: Progres, communaute: number): HTMLElement {
    const options: [number, string][] = [[0, t("decouvertes.monde")], ...p.communautes.map((c): [number, string] => [c.id, c.active ? c.nom : t("decouvertes.dispersee", { nom: c.nom })])];
    if (!options.some(([id]) => id === communaute)) options.push([communaute, t("decouvertes.disparue")]);
    this.choix.replaceChildren(...options.map(([id, nom]) => {
      const o = el("option", undefined, nom);
      o.value = String(id);
      return o;
    }));
    this.choix.value = String(communaute);
    const outils = el("div", "outils");
    outils.append(this.choix);
    return outils;
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
    if (dernier && (dernier.parHumain ?? -1) >= 0) outils.append(el("span", "discret diffusion", " · " + t("decouvertes.parHumain", { n: nombre(dernier.parHumain ?? 0, 1) })));
    for (const [libelle, ans] of PERIODES) {
      const b = el("button", ans === this.periode ? "actif" : undefined, libelle ? t(libelle) : t("age.ans", { n: ans }));
      b.type = "button";
      b.addEventListener("click", () => {
        this.periode = ans;
        this.progres.replaceChildren(dessinerProgres(p, this.detail, this.periode));
        for (const x of outils.querySelectorAll("button")) x.classList.toggle("actif", x === b);
      });
      outils.append(b);
    }
    this.progres.replaceChildren(dessinerProgres(p, this.detail, this.periode));
    const legende = el("p", "discret",
      communaute === 0
        ? t("decouvertes.legendeMonde")
        : t("decouvertes.legendeCommunaute"));
    legende.append(" " + t("decouvertes.legendeDiffusion"));
    this.corps.append(outils, this.progres, legende, this.detail);
  }
}
