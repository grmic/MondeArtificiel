import { connexion } from "./connexion";
import { boutonCause, el } from "./fiche";

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
    `${b.total} prière${b.total > 1 ? "s" : ""}`,
    `exaucées par l'Entité : ${b.exauceesEntite}`,
    `arrivées d'elles-mêmes : ${b.exauceesHasard}`,
    `refusées : ${b.refusees}${b.refuseesEnSecret > 0 ? ` (dont ${b.refuseesEnSecret} en secret)` : ""}`,
    `pas entendues : ${b.pasEntendues}`,
    `en attente : ${b.enAttente}`,
  ];
  if (b.remerciements > 0) parts.push(`remerciements : ${b.remerciements}`);
  if (collectives && b.collectives > 0) parts.push(`dont prières collectives : ${b.collectives}`);
  const p = el("p", "bilan-prieres", parts.join(" · "));
  p.title = "Toutes les prières depuis le début du monde. « Pas entendues » : trop anciennes, ou rien n'est venu.";
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
  b.title = "Ouvrir la fiche de la communauté";
  b.addEventListener("click", () => ouvrirCommunaute(id));
  return b;
}

/** Tête d'une fenêtre latérale : titre et bouton de fermeture. */
function tete(titre: string, fermer: () => void): HTMLElement {
  const t = el("div", "tete");
  t.append(el("h2", undefined, titre));
  const b = el("button", "fermer", "×");
  b.type = "button";
  b.title = "Fermer";
  b.addEventListener("click", fermer);
  t.append(b);
  return t;
}

/** Une prière en une ligne : qui, quoi, avec quelle ferveur, ce qu'il en est advenu. */
export function lignePriere(p: Priere, ouvrirHumain: (id: number) => void, exaucer?: (p: Priere, li: HTMLElement) => void,
  refuser?: (p: Priere, li: HTMLElement) => void): HTMLLIElement {
  const li = el("li", p.forte ? "priere forte" : "priere");
  const qui = el("button", "lien", p.nom);
  qui.type = "button";
  qui.addEventListener("click", () => ouvrirHumain(p.humain));
  if (p.collective) {
    li.append(`${p.date} · `, lienCommunaute(p.collective, p.collectiveId), " prie ensemble, guidée par ", qui, `, pour ${p.texte}`);
  } else {
    li.append(`${p.date} · `, qui);
    if (p.communaute) li.append(" (", lienCommunaute(p.communaute, p.communauteId), ")");
    li.append(` prie pour ${p.texte}`);
  }
  li.append(el("span", "discret", ` · ferveur ${p.ferveur}/100${p.forte ? " · forte" : ""}${p.religion ? ` · ${p.religion}` : ""}`));
  const issue = el("span", p.issue === "en attente" ? "issue attente" : "issue", ` · ${p.issue}${p.issueDate ? ` le ${p.issueDate}` : ""}`);
  li.append(issue);
  if (p.refus) li.append(el("span", "issue refus", ` · ${p.refus}`));
  if (p.conclusion) li.append(el("span", "discret", ` (${p.conclusion})`));
  if (p.evenement > 0) li.append(" ", boutonCause(li, p.evenement, "au journal"));
  if (p.exaucable && exaucer) {
    const b = el("button", "exaucer", "✦ Exaucer");
    b.type = "button";
    b.title = "L'Entité exauce cette prière : ce qui était demandé arrive.";
    b.addEventListener("click", () => exaucer(p, li));
    li.append(" ", b);
  }
  if (p.exaucable && refuser) {
    const b = el("button", "refuser", "✕ Refuser");
    b.type = "button";
    b.title = "L'Entité refuse cette prière (en secret, ou par un signe si la case « Refuser par un signe » est cochée).";
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
    etiquette.append(this.attente, " En attente seulement");
    this.attente.addEventListener("change", () => void this.charger());
    this.signe.type = "checkbox";
    this.signe.checked = false;
    const etiquetteSigne = el("label", "case-config");
    etiquetteSigne.append(this.signe, " Refuser par un signe");
    etiquetteSigne.title = "Coché : celui qui prie (ou toute la communauté) comprend que l'invisible a dit non. Décoché : le refus reste secret, il attend en vain.";
    const corps = el("div", "corps");
    corps.append(
      el("p", "discret", "Ce que les humains demandent à l'invisible. Seules les prières fortes vont au journal. Personne ne répond, sauf si l'Entité exauce ; ce qui arrive de soi-même, le croyant le prend pour une réponse. Une prière trop ancienne n'est plus entendue."),
      etiquette, etiquetteSigne, this.resultat, this.liste,
    );
    racine.append(tete("Prières", () => this.fermer()), corps);
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
    const texte = await connexion.invoke<string>("Exaucer", p.id).catch(() => "Le serveur n'a pas répondu.");
    this.resultat.textContent = `${p.collective ?? p.nom} : ${texte}`;
    await this.charger();
  }

  private async refuser(p: Priere, li: HTMLElement): Promise<void> {
    li.querySelectorAll<HTMLButtonElement>("button.exaucer, button.refuser").forEach((b) => b.setAttribute("disabled", ""));
    const texte = await connexion.invoke<string>("Refuser", p.id, this.signe.checked).catch(() => "Le serveur n'a pas répondu.");
    this.resultat.textContent = `${p.collective ?? p.nom} : ${texte}`;
    await this.charger();
  }

  private async charger(): Promise<void> {
    const liste = await connexion.invoke<Priere[]>("Prieres", this.attente.checked).catch(() => null);
    if (!this.ouvert) return;
    if (!liste) {
      this.liste.replaceChildren(el("li", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    if (liste.length === 0) {
      this.liste.replaceChildren(el("li", "discret", this.attente.checked ? "Aucune prière n'attend de réponse." : "Personne n'a encore prié."));
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
      el("p", "discret", "Les croyances partagées nées parmi les humains. L'origine de chaque précepte n'est connue que de l'Entité. Les lieux sacrés sont marqués ✦ sur la carte."),
      this.liste,
    );
    racine.append(tete("Religions", () => this.fermer()), corps);
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
      this.liste.replaceChildren(el("p", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    if (religions.length === 0) {
      this.liste.replaceChildren(el("p", "discret", "Aucune croyance n'est encore partagée."));
      return;
    }
    this.liste.replaceChildren(...religions.map((r) => {
      const d = el("details", r.eteinteLe ? "religion eteinte" : "religion");
      d.open = this.ouverts.has(r.id);
      d.addEventListener("toggle", () => (d.open ? this.ouverts.add(r.id) : this.ouverts.delete(r.id)));
      const resume = el("summary");
      resume.append(el("strong", undefined, r.nom), ` « ${r.sens} » · `,
        r.eteinteLe ? `éteinte le ${r.eteinteLe} (jusqu'à ${r.fidelesMax} fidèles)` : `${r.fideles} fidèle${r.fideles > 1 ? "s" : ""}, ferveur ${r.ferveur}/100`);
      d.append(resume);
      const p = el("p");
      p.append(`Née le ${r.fondeeLe} ${r.naissance}, fondée par `, this.lien(r.fondateur, r.nomFondateur),
        r.communaute ? ` dans la ${r.communaute.charAt(0).toLowerCase()}${r.communaute.slice(1)}` : "", r.parent ? `, séparée du ${r.parent}` : "", ". ");
      if (r.evenement > 0) p.append(boutonCause(p, r.evenement, "au journal"));
      if (r.gestes) p.append(el("br"), `On y prie ${r.gestes}.`);
      if (r.lieuSacre) p.append(el("br"), `Lieu sacré : ${r.lieuSacre}.`);
      if (r.communautes.length > 0) p.append(el("br"), `Fidèles : ${r.communautes.join(", ")}.`);
      d.append(p);
      const dogmes = el("ul", "liste dogmes");
      for (const x of r.dogmes) {
        const li = el("li", x.deLEntite ? "dogme entite" : "dogme");
        li.append(`« ${x.texte} » `, el("span", "discret", `(${x.origine}, ${x.date})`));
        dogmes.append(li);
      }
      d.append(el("h4", undefined, "Préceptes"), dogmes);
      if (r.porteurs.length > 0) {
        const ul = el("ul", "liste");
        for (const x of r.porteurs) {
          const li = el("li", x.vivant ? undefined : "discret");
          li.append(this.lien(x.id, x.nom), ` : ${x.role}${x.source ? ` (${x.source})` : ""}${x.miracles > 0 ? `, ${x.miracles} miracle${x.miracles > 1 ? "s" : ""} attribué${x.miracles > 1 ? "s" : ""}` : ""}`);
          if (x.communaute) li.append(" · ", lienCommunaute(x.communaute, x.communauteId));
          if (!x.vivant) li.append(" · mort");
          ul.append(li);
        }
        d.append(el("h4", undefined, "Ceux qui la portent"), ul);
      }
      return d;
    }));
  }
}
