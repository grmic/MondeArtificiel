import { connexion } from "./connexion";
import type { Indicateurs } from "./carte";
import { boutonCause, el } from "./fiche";
import { t } from "./langue";

export interface Communaute {
  id: number;
  nom: string;
  fondeeLe: string;
  disperseeLe: string | null;
  figure: number;
  nomFigure: string;
  membres: { id: number; nom: string; etape: string }[];
  x: number;
  y: number;
  rayon: number;
  abondance: number;
  disette: boolean;
  destination: string | null;
  faim: number;
  reserves: number;
  relations: { id: number; nom: string; etat: string; code: string; evenement: number }[];
  /** Usage du groupe envers ceux qui s'unissent hors de leur couple. */
  usage: string;
  /** Nom que se donnent ses membres, et ce qu'il veut dire (null tant qu'ils ne se sont pas nommés). */
  nomPropre: string | null;
  sens: string | null;
  noms: { nom: string; sens: string | null; depuis: string; evenement: number }[];
  mots: string[];
  terres: number;
  histoire: string[];
  /** Goût du groupe pour la violence, en mots (null tant qu'il n'est pas établi). */
  temperament: string | null;
  /** Raid en cours, soumission, vassaux. */
  guerre: string[];
  guerres: Guerre[];
  /** Guerre qu'elle prépare (null : aucune). */
  projet: Projet | null;
  /** Son regard sur les amours entre personnes de même sexe, et ce qu'elle trouve beau (null : pas encore établis). */
  moeurs: string | null;
  ideal: string | null;
  /** Ceux qu'elle a chassés et dont l'exil dure encore. */
  exiles: { id: number; nom: string; raison: string; jusqua: string | null; evenement: number }[];
  /** Morale du groupe : moyenne des fondements moraux de ses adultes (null : aucun adulte). */
  morale: string | null;
  /** Savoirs connus de ses membres, avec combien les ont. */
  savoirs: string[];
  /** Moyennes des adultes : karma (regard de l'Entité) et croyance en l'invisible (0 à 100). */
  karma: number | null;
  croyance: number | null;
  /** Guérisseurs, chamans et charlatans de la communauté. */
  soignants: { id: number; nom: string; role: string; gueris: number; perdus: number }[] | null;
  /** Style de ses arts (chants, rythmes, danses, parures, peintures), né du caractère de ses membres. */
  style: string | null;
  /** Images peintes sur la roche par ses membres, les plus récentes d'abord. */
  oeuvres: { id: number; auteurId: number; auteur: string; le: string; description: string; evenementId: number }[] | null;
  /** Sa langue : mode, stade, ordre des mots, mots et leur histoire, langues voisines, interprètes. */
  langue: Langue | null;
}

/** Un mot de la langue, avec sa traduction et qui lui a donné sa forme. */
interface MotLangue {
  sens: string; forme: string; signe: boolean; depuis: string; auteur: number; nomAuteur: string | null;
}

export interface Langue {
  mode: string; stade: string; ordre: string | null; premierMot: string | null; assemblage: string | null; ordreFixe: string | null;
  nombreDeMots: number; lexique: MotLangue[]; histoire: { date: string; texte: string; evenement: number }[];
  proches: { id: number; nom: string; pourcent: number }[]; interpretes: { id: number; nom: string; langue: number; nomLangue: string }[];
  /** Communauté dont elle parle encore un dialecte (null : sa langue est à elle). */
  dialecteDe: string | null;
}

/** Une guerre en préparation : qui la veut, contre qui, pourquoi, et les alliés sollicités. */
export interface Projet {
  instigateur: number;
  nomInstigateur: string;
  cible: number;
  nomCible: string;
  raison: string;
  depuis: string;
  allies: string[];
  evenement: number;
  /** Jour du débat. */
  debat: string;
}

/** Une guerre vue par la communauté (issue null : en cours). */
export interface Guerre {
  contre: number;
  nomContre: string;
  debut: string;
  finLe: string | null;
  raids: number;
  mortsSiens: number;
  mortsEnnemis: number;
  issue: string | null;
  evenement: number;
  fin: number;
}

export interface Taille {
  secondes: number;
  date: string;
  membres: number;
  adultes: number;
}

/** Courbe de la taille d'une communauté au début de chaque saison (membres en bleu, adultes en pointillés verts). */
export function courbe(tailles: Taille[]): SVGSVGElement {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("class", "courbe");
  svg.setAttribute("viewBox", "0 0 300 48");
  svg.setAttribute("preserveAspectRatio", "none");
  const max = Math.max(1, ...tailles.map((t) => t.membres));
  const points = (cle: "membres" | "adultes") => tailles
    .map((t, i) => `${tailles.length === 1 ? 150 : (i / (tailles.length - 1)) * 300},${46 - (t[cle] / max) * 44}`).join(" ");
  for (const cle of ["membres", "adultes"] as const) {
    const ligne = document.createElementNS(ns, "polyline");
    ligne.setAttribute("points", points(cle));
    if (cle === "adultes") ligne.setAttribute("class", "adultes");
    svg.append(ligne);
  }
  const titre = document.createElementNS(ns, "title");
  titre.textContent = t("communautes.courbeTailles", { debut: tailles[0].date, fin: tailles[tailles.length - 1].date, n: max });
  svg.append(titre);
  return svg;
}

/** Listes déroulantes de la section Langue restées ouvertes : la fiche se redessine à chaque nouvelle, elles ne doivent pas se refermer. */
const languesOuvertes = new Set<string>();

function deroulante(cle: string, titre: string): HTMLDetailsElement {
  const d = el("details");
  d.open = languesOuvertes.has(cle);
  d.append(el("summary", undefined, titre));
  d.addEventListener("toggle", () => {
    if (d.open) languesOuvertes.add(cle);
    else languesOuvertes.delete(cle);
  });
  return d;
}

/** Section « Langue » : comment on y parle, ses étapes, ses mots traduits, son histoire, les langues voisines et ses interprètes. */
export function blocLangue(bloc: HTMLElement, l: Langue, ouvrirFiche: (id: number) => void): void {
  bloc.append(el("h3", undefined, t("langage.titre")));
  const p = el("p", "langage");
  p.append(t(l.ordre ? "langage.resumeOrdre" : "langage.resume", { mode: l.mode, stade: l.stade, ordre: l.ordre }));
  if (l.dialecteDe) p.append(el("br"), t("langage.dialecte", { nom: l.dialecteDe, nomMin: `${l.dialecteDe.charAt(0).toLowerCase()}${l.dialecteDe.slice(1)}` }));
  const etapes = [l.premierMot ? t("langage.premierMot", { date: l.premierMot }) : "", l.assemblage ? t("langage.assemblage", { date: l.assemblage }) : "",
    l.ordreFixe ? t("langage.ordreFixe", { date: l.ordreFixe }) : ""].filter(Boolean);
  if (etapes.length > 0) p.append(el("br"), `${etapes.join(" ; ")}.`);
  if (l.proches.length > 0) {
    p.append(el("br"), t("langage.proches", { liste: l.proches.map((x) => t("langage.proche", { nom: x.nom, pourcent: x.pourcent })).join(", ") }));
  }
  if (l.interpretes.length > 0) {
    p.append(el("br"), t("langage.interpretes"));
    l.interpretes.forEach((x, i) => {
      if (i > 0) p.append(", ");
      const b = el("button", "lien", x.nom);
      b.type = "button";
      b.addEventListener("click", () => ouvrirFiche(x.id));
      p.append(b, ` ${t("langage.parleAussi", { nom: x.nomLangue })}`);
    });
    p.append(".");
  }
  bloc.append(p);
  if (l.lexique.length > 0) {
    const mots = deroulante("mots", t("langage.mots", { n: l.nombreDeMots }));
    const ul = el("ul", "liste mots");
    for (const m of l.lexique) {
      const li = el("li");
      li.append(el("span", "mot", m.forme), ` « ${m.sens} »`);
      if (m.depuis) li.append(el("span", "discret", ` (${m.depuis}${m.nomAuteur ? `, ${m.nomAuteur}` : ""})`));
      ul.append(li);
    }
    mots.append(ul);
    bloc.append(mots);
  }
  if (l.histoire.length > 0) {
    const histoire = deroulante("histoire", t("langage.histoire"));
    const ul = el("ul", "liste");
    for (const x of l.histoire) {
      const li = el("li");
      li.append(el("span", "date", x.date), ` ${x.texte} `);
      if (x.evenement > 0) li.append(boutonCause(li, x.evenement, t("commun.comment")));
      ul.append(li);
    }
    histoire.append(ul);
    bloc.append(histoire);
  }
}

/**
 * Communautés : les groupes d'humains liés qui vivent ensemble, constatés chaque semaine par le monde.
 * Choisir une communauté entoure ses membres sur la carte.
 */
export class PanneauCommunautes {
  private choisie = 0;
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly surligner: (ids: number[]) => void,
    private readonly ouvrirFiche: (id: number) => void,
    private readonly suivre: (id: number, nom: string) => void = () => {},
    private readonly montrerTerres: (id: number) => void = () => {},
    private readonly ouvrirCommunaute: (id: number) => void = () => {},
  ) {}

  /** Prévenu quand la communauté choisie change (0 : aucune). */
  surChoix: (id: number) => void = () => {};

  basculer(): void {
    if (this.racine.hidden) void this.ouvrir();
    else this.fermer();
  }

  async ouvrir(id?: number): Promise<void> {
    if (id !== undefined) {
      this.choisie = id;
      this.surChoix(id);
    }
    this.racine.hidden = false;
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.rafraichir(), 5000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.racine.hidden = true;
    this.choisie = 0;
    this.surChoix(0);
    this.surligner([]);
  }

  async rafraichir(): Promise<void> {
    if (this.racine.hidden) return;
    const [liste, nature] = await Promise.all([
      connexion.invoke<Communaute[]>("Communautes").catch(() => null),
      connexion.invoke<{ indicateurs: Indicateurs }>("Nature").catch(() => null),
    ]);
    if (liste) this.dessiner(liste, nature?.indicateurs);
  }

  private dessiner(liste: Communaute[], monde?: Indicateurs): void {
    const r = this.racine;
    const defilement = r.querySelector(".corps")?.scrollTop ?? 0;
    r.replaceChildren();
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("barre.communautes")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    const corps = el("div", "corps");
    const actives = liste.filter((c) => !c.disperseeLe);
    const dispersees = liste.filter((c) => c.disperseeLe);
    corps.append(el("p", "discret",
      actives.length === 0
        ? t("communautes.aucune")
        : `${t("communautes.nombre", { n: actives.length })} · ${t("communautes.yVivent", { n: actives.reduce((n, c) => n + c.membres.length, 0) })}`));
    if (monde) {
      corps.append(el("p", "discret",
        t("communautes.monde", { vivants: monde.vivants, abondance: Math.round(monde.abondance * 100), reserves: Math.round(monde.reservesMoyennes) }) +
        ` · ${t("communautes.secheresse", { n: monde.regionsSeches, habitees: monde.regionsHabitees })}` +
        (monde.communautesEnDisette > 0 ? ` · ${t("communautes.enDisette", { n: monde.communautesEnDisette })}` : "")));
    }

    for (const c of actives) corps.append(this.carte(c));
    if (dispersees.length > 0) {
      corps.append(el("h3", undefined, t("communautes.dispersees")));
      const ul = el("ul", "liste");
      for (const c of dispersees.slice(0, 30)) {
        const li = el("li", "discret");
        const nom = el("button", "lien", c.nom);
        nom.type = "button";
        nom.title = t("commun.ouvrirSaFiche");
        nom.addEventListener("click", () => this.ouvrirCommunaute(c.id));
        li.append(nom, ` : ${c.fondeeLe} → ${c.disperseeLe}${c.histoire.length > 0 ? ` · ${c.histoire.join(" ")}` : ""} `);
        const chrono = el("button", "lien", t("communautes.chronologieMin"));
        chrono.type = "button";
        chrono.addEventListener("click", () => this.suivre(c.id, c.nom));
        li.append(chrono);
        ul.append(li);
      }
      corps.append(ul);
    }
    r.append(tete, corps);
    corps.scrollTop = defilement;

    const choisie = actives.find((c) => c.id === this.choisie);
    this.surligner(choisie ? choisie.membres.map((m) => m.id) : []);
  }

  /** Une communauté dans la liste : l'essentiel, ses alertes, et ce qui ouvre sa fiche. Le détail est dans la fiche. */
  private carte(c: Communaute): HTMLElement {
    const bloc = el("div", `communaute-carte${c.id === this.choisie ? " choisie" : ""}`);
    const titre = el("button", "titre", `${c.nom} · ${t("communautes.membres", { n: c.membres.length })}${c.disette ? ` · ${t("communautes.disette")}` : ""}`);
    if (c.disette) titre.classList.add("disette");
    titre.type = "button";
    titre.title = t("communautes.montrerMembres");
    titre.addEventListener("click", () => {
      this.choisie = this.choisie === c.id ? 0 : c.id;
      this.surChoix(this.choisie);
      void this.rafraichir();
    });
    bloc.append(titre);
    if (c.nomPropre && c.sens) bloc.append(el("p", "nom-propre", `« ${c.nomPropre} » : ${c.sens}`));
    const adultes = c.membres.filter((m) => m.etape === "adulte").length;
    bloc.append(el("p", "discret", t("communautes.nee", { date: c.fondeeLe, adultes, enfants: c.membres.length - adultes, abondance: c.abondance })));
    if (!c.disperseeLe && c.figure > 0) {
      const figure = el("p", "discret", t("communautes.figure"));
      const lien = el("button", "lien", c.nomFigure);
      lien.type = "button";
      lien.addEventListener("click", () => this.ouvrirFiche(c.figure));
      figure.append(lien);
      bloc.append(figure);
    }
    if (c.destination) bloc.append(el("p", "alerte", t("communautes.enRoute", { destination: c.destination })));
    for (const g of c.guerre) bloc.append(el("p", "alerte", g));
    if (c.projet) bloc.append(el("p", "alerte projet", t("communautes.prepareGuerre", { cible: c.projet.nomCible })));
    const enCours = c.guerres.filter((g) => g.issue === null);
    if (enCours.length > 0) bloc.append(el("p", "relation-en", t("communautes.enGuerre", { liste: enCours.map((g) => g.nomContre).join(", ") })));
    const boutons = el("div", "boutons");
    const fiche = el("button", undefined, t("communautes.fiche"));
    fiche.type = "button";
    fiche.title = t("communautes.ficheAide");
    fiche.addEventListener("click", () => this.ouvrirCommunaute(c.id));
    const chrono = el("button", undefined, t("communautes.chronologie"));
    chrono.type = "button";
    chrono.title = t("communautes.chronologieAide");
    chrono.addEventListener("click", () => this.suivre(c.id, c.nom));
    const terres = el("button", undefined, t("communautes.terres"));
    terres.type = "button";
    terres.title = t("communautes.terresAide");
    terres.addEventListener("click", () => this.montrerTerres(c.id));
    boutons.append(fiche, chrono, terres);
    bloc.append(boutons);
    return bloc;
  }
}
