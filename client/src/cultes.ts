import { connexion } from "./connexion";
import { boutonCause, el } from "./fiche";
import { t } from "./langue";

export interface Priere {
  id: number;
  date: string;
  humain: number;
  nom: string;
  communaute: string | null;
  sujet: string;
  cible: number;
  nomCible: string | null;
  /** 0 à 100. */
  ferveur: number;
  forte: boolean;
  religion: string | null;
  texte: string;
  issue: string;
  issueDate: string | null;
  conclusion: string | null;
  evenement: number;
  exaucable: boolean;
  communauteId: number;
  /** Prière collective : nom et numéro de la communauté qui prie (le nom en tête est celui qui la guide). */
  collective: string | null;
  collectiveId: number;
  /** « refusée en secret » ou « refusée par un signe ». */
  refus: string | null;
}

/** Bilan de toutes les prières, depuis le début du monde. */
export interface BilanPrieres {
  total: number; exauceesEntite: number; exauceesHasard: number; refusees: number; refuseesEnSecret: number; pasEntendues: number;
  enAttente: number; remerciements: number; collectives: number;
}

/** Bilan en une ligne : combien de prières, et ce qu'il en est advenu. */
export function ligneBilan(b: BilanPrieres | null | undefined, collectives: boolean): HTMLElement | null {
  if (!b || b.total + b.remerciements === 0) return null;
  const parts = [
    t("bilan.total", { n: b.total }),
    t("bilan.entite", { n: b.exauceesEntite }),
    t("bilan.hasard", { n: b.exauceesHasard }),
    b.refuseesEnSecret > 0 ? t("bilan.refuseesSecret", { n: b.refusees, secret: b.refuseesEnSecret }) : t("bilan.refusees", { n: b.refusees }),
    t("bilan.pasEntendues", { n: b.pasEntendues }),
    t("bilan.enAttente", { n: b.enAttente }),
  ];
  if (b.remerciements > 0) parts.push(t("bilan.remerciements", { n: b.remerciements }));
  if (collectives && b.collectives > 0) parts.push(t("bilan.collectives", { n: b.collectives }));
  const p = el("p", "bilan-prieres", parts.join(" · "));
  p.title = t("bilan.aide");
  return p;
}

interface Dogme { texte: string; origine: string; deLEntite: boolean; date: string; evenement: number }
interface Porteur {
  id: number; nom: string; role: string; source: string | null; miracles: number; communaute: string | null; vivant: boolean; communauteId: number;
}
interface Religion {
  id: number; nom: string; sens: string; naissance: string; fondateur: number; nomFondateur: string; fondeeLe: string; parent: string | null;
  communaute: string | null; fideles: number; fidelesMax: number; ferveur: number; eteinteLe: string | null; gestes: string | null;
  dogmes: Dogme[]; porteurs: Porteur[]; communautes: string[]; lieuSacre: string | null; evenement: number;
}
export interface LieuSacre { id: number; x: number; y: number; raison: string; religion: string | null }

const ACTUALISATION_MS = 10000;

/** Ouvre la fiche d'une communauté : fourni par la page au démarrage. */
let ouvrirCommunaute: (id: number) => void = () => {};

export function definirOuvrirCommunaute(ouvrir: (id: number) => void): void {
  ouvrirCommunaute = ouvrir;
}

/** Nom de communauté cliquable (sa fiche), ou simple texte sans identifiant. */
function lienCommunaute(nom: string, id: number): HTMLElement | string {
  if (id <= 0) return nom;
  const b = el("button", "lien", nom);
  b.type = "button";
  b.title = t("suivis.ouvrirCommunaute");
  b.addEventListener("click", () => ouvrirCommunaute(id));
  return b;
}

/** Tête d'une fenêtre latérale : titre et bouton de fermeture. */
function tete(titre: string, fermer: () => void): HTMLElement {
  const entete = el("div", "tete");
  entete.append(el("h2", undefined, titre));
  const b = el("button", "fermer", "×");
  b.type = "button";
  b.title = t("commun.fermer");
  b.addEventListener("click", fermer);
  entete.append(b);
  return entete;
}

/** Une prière en une ligne : qui, quoi, avec quelle ferveur, ce qu'il en est advenu. */
export function lignePriere(p: Priere, ouvrirHumain: (id: number) => void, exaucer?: (p: Priere, li: HTMLElement) => void,
  refuser?: (p: Priere, li: HTMLElement) => void): HTMLLIElement {
  const li = el("li", p.forte ? "priere forte" : "priere");
  const qui = el("button", "lien", p.nom);
  qui.type = "button";
  qui.addEventListener("click", () => ouvrirHumain(p.humain));
  if (p.collective) {
    li.append(`${p.date} · `, lienCommunaute(p.collective, p.collectiveId), " " + t("priere.ensemble") + " ", qui, t("priere.pour", { texte: p.texte }));
  } else {
    li.append(`${p.date} · `, qui);
    if (p.communaute) li.append(" (", lienCommunaute(p.communaute, p.communauteId), ")");
    li.append(" " + t("priere.priePour", { texte: p.texte }));
  }
  li.append(el("span", "discret", ` · ${t("priere.ferveur", { v: p.ferveur })}${p.forte ? ` · ${t("priere.forte")}` : ""}${p.religion ? ` · ${p.religion}` : ""}`));
  const issue = el("span", p.issue === t("issuePriere.EnAttente") ? "issue attente" : "issue", ` · ${p.issueDate ? t("priere.issueLe", { issue: p.issue, date: p.issueDate }) : p.issue}`);
  li.append(issue);
  if (p.refus) li.append(el("span", "issue refus", ` · ${p.refus}`));
  if (p.conclusion) li.append(el("span", "discret", ` (${p.conclusion})`));
  if (p.evenement > 0) li.append(" ", boutonCause(li, p.evenement, t("commun.auJournal")));
  if (p.exaucable && exaucer) {
    const b = el("button", "exaucer", t("priere.exaucer"));
    b.type = "button";
    b.title = t("priere.exaucerAide");
    b.addEventListener("click", () => exaucer(p, li));
    li.append(" ", b);
  }
  if (p.exaucable && refuser) {
    const b = el("button", "refuser", t("priere.refuser"));
    b.type = "button";
    b.title = t("priere.refuserAide");
    b.addEventListener("click", () => refuser(p, li));
    li.append(" ", b);
  }
  return li;
}

/**
 * Fenêtre « Prières » : ce que les humains demandent à l'invisible, en attente d'abord (les plus fortes en tête), puis
 * ce qu'il est advenu des prières de l'année. Le bouton « Exaucer » fait arriver ce qui était demandé.
 */
export class PanneauPrieres {
  private readonly attente = el("input");
  /** Refuser par un signe (un rêve, une voix) ; sinon en secret, par défaut. */
  private readonly signe = el("input");
  private readonly liste = el("ul", "liste prieres");
  private readonly resultat = el("p", "resultat-exaucer");
  private minuteur?: number;

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void) {
    this.attente.type = "checkbox";
    this.attente.checked = true;
    const etiquette = el("label", "case-config");
    etiquette.append(this.attente, " " + t("priere.enAttenteSeulement"));
    this.attente.addEventListener("change", () => void this.charger());
    this.signe.type = "checkbox";
    this.signe.checked = false;
    const etiquetteSigne = el("label", "case-config");
    etiquetteSigne.append(this.signe, " " + t("priere.parUnSigne"));
    etiquetteSigne.title = t("priere.parUnSigneAide");
    const corps = el("div", "corps");
    corps.append(
      el("p", "discret", t("prieres.aide")),
      etiquette, etiquetteSigne, this.resultat, this.liste,
    );
    racine.append(tete(t("prieres.titre"), () => this.fermer()), corps);
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
    this.resultat.textContent = "";
    await this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    if (!this.ouvert) return;
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
  }

  private async exaucer(p: Priere, li: HTMLElement): Promise<void> {
    li.querySelectorAll<HTMLButtonElement>("button.exaucer, button.refuser").forEach((b) => b.setAttribute("disabled", ""));
    const texte = await connexion.invoke<string>("Exaucer", p.id).catch(() => t("commun.pasDeReponse"));
    this.resultat.textContent = t("priere.resultat", { nom: p.collective ?? p.nom, texte });
    await this.charger();
  }

  private async refuser(p: Priere, li: HTMLElement): Promise<void> {
    li.querySelectorAll<HTMLButtonElement>("button.exaucer, button.refuser").forEach((b) => b.setAttribute("disabled", ""));
    const texte = await connexion.invoke<string>("Refuser", p.id, this.signe.checked).catch(() => t("commun.pasDeReponse"));
    this.resultat.textContent = t("priere.resultat", { nom: p.collective ?? p.nom, texte });
    await this.charger();
  }

  private async charger(): Promise<void> {
    const liste = await connexion.invoke<Priere[]>("Prieres", this.attente.checked).catch(() => null);
    if (!this.ouvert) return;
    if (!liste) {
      this.liste.replaceChildren(el("li", "discret", t("commun.pasDeReponse")));
      return;
    }
    if (liste.length === 0) {
      this.liste.replaceChildren(el("li", "discret", this.attente.checked ? t("prieres.aucuneEnAttente") : t("prieres.personne")));
      return;
    }
    this.liste.replaceChildren(...liste.map((p) => lignePriere(p, this.ouvrirHumain, (x, li) => void this.exaucer(x, li), (x, li) => void this.refuser(x, li))));
  }
}

/**
 * Fenêtre « Religions » : les croyances partagées nées parmi les humains, leurs préceptes (et d'où ils viennent, que seule
 * l'Entité sait), leurs fidèles, leurs prophètes et leurs prêtres. Les lieux sacrés sont marqués sur la carte.
 */
export class PanneauReligions {
  private readonly liste = el("div", "religions");
  private minuteur?: number;
  private readonly ouverts = new Set<number>();

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    /** Marque les lieux sacrés sur la carte (liste vide : aucun). */
    private readonly marquerLieux: (lieux: LieuSacre[]) => void,
  ) {
    const corps = el("div", "corps");
    corps.append(
      el("p", "discret", t("religions.aide")),
      this.liste,
    );
    racine.append(tete(t("religions.titre"), () => this.fermer()), corps);
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
    if (!this.ouvert) return;
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
    this.marquerLieux([]);
  }

  private lien(id: number, nom: string): HTMLButtonElement {
    const b = el("button", "lien", nom);
    b.type = "button";
    b.addEventListener("click", () => this.ouvrirHumain(id));
    return b;
  }

  private async charger(): Promise<void> {
    const [religions, lieux] = await Promise.all([
      connexion.invoke<Religion[]>("Religions").catch(() => null),
      connexion.invoke<LieuSacre[]>("LieuxSacres").catch(() => [] as LieuSacre[]),
    ]);
    if (!this.ouvert) return;
    this.marquerLieux(lieux);
    if (!religions) {
      this.liste.replaceChildren(el("p", "discret", t("commun.pasDeReponse")));
      return;
    }
    if (religions.length === 0) {
      this.liste.replaceChildren(el("p", "discret", t("religions.aucune")));
      return;
    }
    this.liste.replaceChildren(...religions.map((r) => {
      const d = el("details", r.eteinteLe ? "religion eteinte" : "religion");
      d.open = this.ouverts.has(r.id);
      d.addEventListener("toggle", () => (d.open ? this.ouverts.add(r.id) : this.ouverts.delete(r.id)));
      const resume = el("summary");
      resume.append(el("strong", undefined, r.nom), " " + t("religion.sens", { sens: r.sens }) + " · ",
        r.eteinteLe ? t("religion.eteinte", { date: r.eteinteLe, n: r.fidelesMax }) : t("religion.vivante", { n: r.fideles, ferveur: r.ferveur }));
      d.append(resume);
      const p = el("p");
      p.append(t("religion.nee", { date: r.fondeeLe, naissance: r.naissance }), this.lien(r.fondateur, r.nomFondateur),
        r.communaute ? t("religion.dans", { nom: r.communaute, nomMin: `${r.communaute.charAt(0).toLowerCase()}${r.communaute.slice(1)}` }) : "",
        r.parent ? t("religion.separee", { parent: r.parent }) : "", ". ");
      if (r.evenement > 0) p.append(boutonCause(p, r.evenement, t("commun.auJournal")));
      if (r.gestes) p.append(el("br"), t("religion.gestes", { gestes: r.gestes }));
      if (r.lieuSacre) p.append(el("br"), t("religion.lieu", { lieu: r.lieuSacre }));
      if (r.communautes.length > 0) p.append(el("br"), t("religion.fideles", { liste: r.communautes.join(t("commun.virgule")) }));
      d.append(p);
      const dogmes = el("ul", "liste dogmes");
      for (const x of r.dogmes) {
        const li = el("li", x.deLEntite ? "dogme entite" : "dogme");
        li.append(t("commun.guillemets", { texte: x.texte }) + " ", el("span", "discret", t("religion.origine", { origine: x.origine, date: x.date })));
        dogmes.append(li);
      }
      d.append(el("h4", undefined, t("religion.preceptes")), dogmes);
      if (r.porteurs.length > 0) {
        const ul = el("ul", "liste");
        for (const x of r.porteurs) {
          const li = el("li", x.vivant ? undefined : "discret");
          li.append(this.lien(x.id, x.nom), t("religion.porteur", { role: x.role }) + (x.source ? ` (${x.source})` : "") + (x.miracles > 0 ? t("religion.miracles", { n: x.miracles }) : ""));
          if (x.communaute) li.append(" · ", lienCommunaute(x.communaute, x.communauteId));
          if (!x.vivant) li.append(" · " + t("religion.mort"));
          ul.append(li);
        }
        d.append(el("h4", undefined, t("religion.porteurs")), ul);
      }
      return d;
    }));
  }
}
