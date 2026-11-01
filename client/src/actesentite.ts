import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";

interface Acte {
  id: number; date: string; auteur: string; sorte: string; quoi: string; humain: number; nomHumain: string | null; communaute: number;
  nomCommunaute: string | null; priere: number; nombre: number; resultats: string[]; issue: string; journalPremier: number; journalDernier: number;
  echange: number; repris: boolean;
}
interface An { an: number; joueur: number; llm: number }
interface Comptes {
  total: number; parLlm: number; exaucees: number; refusees: number; morts: number; bienfaits: number; chatiments: number; paroles: number;
  souffles: number; soufflesReussis: number; catastrophes: number; parAn: An[];
}
interface Page { comptes: Comptes; actes: Acte[]; plus: boolean }
interface Ligne { id: number; date: string; texte: string }
interface Echange { mots: string; pensee: string; occasion: string; statut: string }

const SORTES = ["parler", "parler_communaute", "penchant", "geste.bienfait", "geste.chatiment", "catastrophe", "exaucer", "refuser", "exaucer_toutes",
  "refuser_toutes", "souffler_geste", "souffler_combinaison", "reglage"];

const SVG = "http://www.w3.org/2000/svg";

function svg<K extends keyof SVGElementTagNameMap>(tag: K, attributs: Record<string, string | number> = {}): SVGElementTagNameMap[K] {
  const e = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attributs)) e.setAttribute(k, String(v));
  return e;
}

function bouton(texte: string, action: () => void, classe?: string): HTMLButtonElement {
  const b = el("button", classe, texte);
  b.type = "button";
  b.addEventListener("click", action);
  return b;
}

function nombre(min: number, place: string, large = false): HTMLInputElement {
  const i = el("input");
  i.type = "number";
  i.min = String(min);
  i.step = "1";
  i.className = large ? "nombre-moyen" : "nombre-court";
  i.placeholder = place;
  return i;
}

function message(e: unknown): string {
  return e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e);
}

/**
 * Fenêtre « Actes de l'Entité » : tout ce qu'elle a fait, par le joueur ou par le LLM, avec filtres, comptes, une courbe par année,
 * ce que les règles en ont fait et des liens vers les fiches, le journal et l'échange du LLM.
 */
export class PanneauActesEntite {
  private readonly auteur = el("select");
  private readonly sorte = el("select");
  private readonly humain = nombre(1, t("actes.filtre.humain"), true);
  private readonly communaute = nombre(1, t("actes.filtre.communaute"), true);
  private readonly depuis = nombre(1, t("actes.filtre.depuis"));
  private readonly jusqua = nombre(1, t("actes.filtre.jusqua"));
  private readonly comptes = el("p", "comptes-actes");
  private readonly courbe = el("div", "courbe-actes");
  private readonly liste = el("ul", "liste actes-entite");
  private readonly plus = el("button", "lien");
  private readonly retour = el("p", "discret");
  private dernier = 0;

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void) {
    for (const [v, cle] of [["", "actes.tous"], ["joueur", "actes.auteur.joueur"], ["llm", "actes.auteur.llm"]])
      this.auteur.append(Object.assign(el("option", undefined, t(cle)), { value: v }));
    this.sorte.append(Object.assign(el("option", undefined, t("actes.toutes")), { value: "" }));
    for (const s of SORTES) this.sorte.append(Object.assign(el("option", undefined, t(`actes.sorte.${s}`)), { value: s }));
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, t("actes.titre")), fermer);
    const filtres = el("div", "filtres-actes");
    const lab = (cle: string, champ: HTMLElement) => {
      const l = el("label");
      l.append(t(cle), " ", champ);
      return l;
    };
    filtres.append(lab("actes.filtre.auteur", this.auteur), " ", lab("actes.filtre.sorte", this.sorte), el("br"), this.humain, " ", this.communaute, " ",
      lab("actes.filtre.annees", this.depuis), " – ", this.jusqua, " ", bouton(t("actes.appliquer"), () => void this.charger()),
      " ", bouton(t("actes.effacer"), () => this.effacer(), "lien"));
    for (const c of [this.auteur, this.sorte]) c.addEventListener("change", () => void this.charger());
    this.plus.type = "button";
    this.plus.textContent = t("actes.plus");
    this.plus.hidden = true;
    this.plus.addEventListener("click", () => void this.charger(true));
    const corps = el("div", "corps");
    corps.append(el("p", "discret", t("actes.aide")), filtres, this.comptes, this.courbe, this.retour, this.liste, this.plus);
    racine.append(tete, corps);
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  async ouvrir(filtre?: { humain?: number; communaute?: number }): Promise<void> {
    this.racine.hidden = false;
    if (filtre) {
      this.effacerChamps();
      if (filtre.humain) this.humain.value = String(filtre.humain);
      if (filtre.communaute) this.communaute.value = String(filtre.communaute);
    }
    await this.charger();
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  private effacerChamps(): void {
    this.auteur.value = "";
    this.sorte.value = "";
    for (const i of [this.humain, this.communaute, this.depuis, this.jusqua]) i.value = "";
  }

  private effacer(): void {
    this.effacerChamps();
    void this.charger();
  }

  private filtre(avant: number) {
    const n = (i: HTMLInputElement) => Math.max(0, Math.round(Number(i.value) || 0));
    return { auteur: this.auteur.value, sorte: this.sorte.value, humain: n(this.humain), communaute: n(this.communaute), depuisAn: n(this.depuis),
      jusquAn: n(this.jusqua), avant };
  }

  private async charger(suite = false): Promise<void> {
    this.retour.textContent = "";
    let page: Page;
    try {
      page = await connexion.invoke<Page>("ActesEntite", this.filtre(suite ? this.dernier : 0));
    } catch (e) {
      this.retour.textContent = message(e);
      return;
    }
    if (!suite) {
      this.montrerComptes(page.comptes);
      this.liste.replaceChildren();
      if (page.actes.length === 0) this.liste.append(el("li", "discret", t("actes.aucun")));
    }
    for (const a of page.actes) this.liste.append(this.ligne(a));
    if (page.actes.length > 0) this.dernier = page.actes[page.actes.length - 1].id;
    this.plus.hidden = !page.plus;
  }

  private montrerComptes(c: Comptes): void {
    const morceaux = [t("actes.compte.total", { n: c.total, llm: c.parLlm }), t("actes.compte.prieres", { e: c.exaucees, r: c.refusees }),
      t("actes.compte.morts", { n: c.morts }), t("actes.compte.bienfaits", { n: c.bienfaits }), t("actes.compte.chatiments", { n: c.chatiments }),
      t("actes.compte.paroles", { n: c.paroles }), t("actes.compte.souffles", { n: c.souffles, r: c.soufflesReussis }),
      t("actes.compte.catastrophes", { n: c.catastrophes })];
    this.comptes.textContent = morceaux.join(" · ");
    this.courbe.replaceChildren();
    if (c.parAn.length === 0) return;
    // Une barre par année du monde : le joueur en bas, le LLM au-dessus.
    const premier = c.parAn[0].an, dernier = c.parAn[c.parAn.length - 1].an;
    const annees = dernier - premier + 1;
    const largeur = 520, hauteur = 110, marge = { g: 34, b: 18, h: 6 };
    const max = Math.max(1, ...c.parAn.map((a) => a.joueur + a.llm));
    const l = (largeur - marge.g) / annees;
    const y = (v: number) => hauteur - marge.b - (hauteur - marge.b - marge.h) * v / max;
    const s = svg("svg", { width: largeur, height: hauteur, viewBox: `0 0 ${largeur} ${hauteur}`, role: "img", "aria-label": t("actes.courbe") });
    for (const v of [0, max]) {
      s.append(svg("line", { x1: marge.g, x2: largeur, y1: y(v), y2: y(v), class: "grille" }));
      const tx = svg("text", { x: marge.g - 4, y: y(v) + 4, class: "axe", "text-anchor": "end" });
      tx.textContent = String(v);
      s.append(tx);
    }
    for (const a of c.parAn) {
      const x = marge.g + (a.an - premier) * l;
      const w = Math.max(1, l - 1);
      const r1 = svg("rect", { x, y: y(a.joueur), width: w, height: y(0) - y(a.joueur), class: "barre-joueur" });
      const r2 = svg("rect", { x, y: y(a.joueur + a.llm), width: w, height: y(a.joueur) - y(a.joueur + a.llm), class: "barre-llm" });
      const titre = svg("title");
      titre.textContent = t("actes.courbeAn", { an: a.an, joueur: a.joueur, llm: a.llm });
      r1.append(titre.cloneNode(true));
      r2.append(titre);
      s.append(r1, r2);
    }
    for (const an of [premier, dernier]) {
      const tx = svg("text", { x: marge.g + (an - premier + 0.5) * l, y: hauteur - 4, class: "axe", "text-anchor": an === premier && annees > 1 ? "start" : "end" });
      tx.textContent = t("actes.an", { an });
      s.append(tx);
    }
    const legende = el("p", "discret legende-actes");
    legende.append(el("span", "pastille barre-joueur"), " " + t("actes.auteur.joueur") + "  ", el("span", "pastille barre-llm"), " " + t("actes.auteur.llm"));
    this.courbe.append(s, legende);
  }

  private ligne(a: Acte): HTMLLIElement {
    const li = el("li", `acte-entite ${a.issue} ${a.auteur}`);
    li.append(`${a.date} · `, el("span", `auteur-acte ${a.auteur}`, t(`actes.auteur.${a.auteur}`)), " · ", el("strong", undefined, a.quoi));
    if (a.humain > 0 && a.nomHumain) li.append(" · ", bouton(a.nomHumain, () => this.ouvrirHumain(a.humain), "lien"));
    if (a.communaute > 0 && a.nomCommunaute) li.append(" · ", bouton(a.nomCommunaute, () => this.ouvrirCommunaute(a.communaute), "lien"));
    li.append(el("span", "issue-acte", ` · ${t(`actes.issue.${a.issue}`)}`));
    if (a.resultats.length > 0) {
      const ul = el("ul", "resultats-acte");
      for (const r of a.resultats.slice(0, 6)) ul.append(el("li", undefined, r));
      if (a.resultats.length > 6) ul.append(el("li", "discret", t("actes.etAutres", { n: a.resultats.length - 6 })));
      li.append(ul);
    }
    const details = el("div", "details-acte");
    details.hidden = true;
    const liens = el("div", "liens-acte");
    if (a.journalPremier > 0) liens.append(bouton(t("actes.voirJournal"), () => void this.voirJournal(a, details), "lien"), " ");
    if (a.echange > 0) liens.append(bouton(t("actes.voirEchange"), () => void this.voirEchange(a, details), "lien"), " ");
    if (a.repris) liens.append(el("span", "discret", t("actes.repris")));
    li.append(liens, details);
    return li;
  }

  private async voirJournal(a: Acte, details: HTMLElement): Promise<void> {
    if (!details.hidden && details.dataset.vue === "journal") {
      details.hidden = true;
      return;
    }
    const lignes = await connexion.invoke<Ligne[]>("JournalActe", a.id).catch(() => [] as Ligne[]);
    const ul = el("ul", "journal-acte");
    for (const l of lignes) ul.append(el("li", undefined, `${l.date} : ${l.texte}`));
    if (lignes.length === 0) ul.append(el("li", "discret", t("actes.journalVide")));
    details.replaceChildren(ul);
    details.dataset.vue = "journal";
    details.hidden = false;
  }

  private async voirEchange(a: Acte, details: HTMLElement): Promise<void> {
    if (!details.hidden && details.dataset.vue === "echange") {
      details.hidden = true;
      return;
    }
    const e = await connexion.invoke<Echange | null>("EchangeLlm", a.echange).catch(() => null);
    details.replaceChildren();
    if (!e) details.append(el("p", "discret", t("actes.echangeIntrouvable")));
    else {
      details.append(el("p", "discret", `${e.occasion} · ${e.statut}`));
      if (e.mots) details.append(el("q", "mots-entite", e.mots));
      if (e.pensee) details.append(el("p", "discret", `${t("ellm.pensee")} : ${e.pensee}`));
    }
    details.dataset.vue = "echange";
    details.hidden = false;
  }
}
