import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";

interface Choix { cle: string; libelle: string }
interface ChoixMonde { modes: Choix[]; communautes: Choix[]; vivants: number }
export interface Revelation {
  id: number; date: string; texte: string; a: string; mode: string; auteur: string; savent: number; croient: number; fidelite: number;
  repandue: string[]; perdue: string | null;
}
interface ResultatRevelation { revelation: Revelation; entendu: number; compris: number; crus: number; texte: string }

/** Les manières de révéler : en rêve, par une voix, par une apparition (pas par un signe). */
const ModesRevelation = ["Reve", "Voix", "Apparition"];

/**
 * « Révélations » (0.42.9) : ce que l'Entité a révélé, et ce que les humains en savent encore (combien, avec quelle fidélité, où
 * c'est répandu, quand ça s'est perdu). On y révèle aussi quelque chose au Monde entier ou à une communauté.
 */
export class PanneauRevelations {
  private readonly texte = el("textarea");
  private readonly mode = el("select");
  private readonly a = el("select");
  private readonly bouton = el("button", undefined, t("rev.c.reveler"));
  private readonly resultat = el("div");
  private readonly liste = el("ul", "liste");

  constructor(private readonly racine: HTMLElement) {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, t("rev.c.titre")), fermer);
    this.texte.rows = 4;
    this.texte.maxLength = 600;
    this.texte.placeholder = t("rev.c.exemple");
    this.bouton.type = "button";
    this.bouton.addEventListener("click", () => void this.reveler());
    const ligne = (libelle: string, champ: HTMLElement): HTMLLabelElement => {
      const l = el("label", "ligne");
      l.append(el("span", undefined, libelle), champ);
      return l;
    };
    const corps = el("div", "corps");
    corps.append(el("p", "discret", t("rev.c.aide")), this.texte, ligne(t("rev.c.a"), this.a), ligne(t("agir.comment"), this.mode), this.bouton,
      this.resultat, el("h3", undefined, t("rev.c.historique")), this.liste);
    racine.append(tete, corps);
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  async ouvrir(): Promise<void> {
    this.racine.hidden = false;
    await this.charger();
  }

  private async charger(): Promise<void> {
    try {
      const c = await connexion.invoke<ChoixMonde>("ChoixParolesMonde");
      const avantMode = this.mode.value || "Voix";
      this.mode.replaceChildren(...c.modes.filter((m) => ModesRevelation.includes(m.cle))
        .map((m) => Object.assign(el("option", undefined, m.libelle), { value: m.cle })));
      this.mode.value = avantMode;
      const avantA = this.a.value;
      this.a.replaceChildren(Object.assign(el("option", undefined, t("monde.toutLeMonde", { n: nombre(c.vivants) })), { value: "0" }),
        ...c.communautes.map((x) => Object.assign(el("option", undefined, x.libelle), { value: x.cle })));
      if (avantA && [...this.a.options].some((o) => o.value === avantA)) this.a.value = avantA;
    } catch {
      this.resultat.replaceChildren(el("p", "discret", t("commun.pasDeReponse")));
    }
    await this.chargerListe();
  }

  private async reveler(): Promise<void> {
    const texte = this.texte.value.trim();
    if (!texte) {
      this.resultat.replaceChildren(el("p", "discret", t("rev.c.vide")));
      return;
    }
    if (!window.confirm(t("rev.c.confirmer", { a: this.a.selectedOptions[0]?.textContent ?? "", texte }))) return;
    this.bouton.disabled = true;
    try {
      const r = await connexion.invoke<ResultatRevelation>("Reveler", { texte, mode: this.mode.value, communaute: Number(this.a.value) || 0 });
      this.resultat.replaceChildren(el("p", "entite", r.texte));
      this.texte.value = "";
      await this.chargerListe();
    } catch (e) {
      this.resultat.replaceChildren(el("p", "discret",
        e instanceof Error && e.message.includes("HubException") ? e.message.split("HubException: ").pop() ?? t("commun.refuse") : t("entite.paroleEchec")));
    } finally {
      this.bouton.disabled = false;
    }
  }

  private async chargerListe(): Promise<void> {
    let liste: Revelation[] = [];
    try {
      liste = await connexion.invoke<Revelation[]>("Revelations");
    } catch {
      return;
    }
    this.liste.replaceChildren(...(liste.length === 0 ? [el("li", "discret", t("rev.c.aucune"))] : liste.map((r) => ligneRevelation(r))));
  }
}

/** Une révélation : à qui, comment, et ce qu'il en reste dans les mémoires. */
export function ligneRevelation(r: Revelation): HTMLLIElement {
  const li = el("li", r.perdue ? "discret" : "entite");
  li.append(el("div", undefined, t("rev.c.ligne", { date: r.date, a: r.a, mode: r.mode, auteur: r.auteur })), el("blockquote", undefined, r.texte));
  const etat = r.perdue ? t("rev.c.perdue", { date: r.perdue })
    : t("rev.c.savent", { n: nombre(r.savent), croient: nombre(r.croient), fidelite: r.fidelite });
  const p = el("p", "discret", etat);
  if (r.repandue.length > 0) p.append(" ", t("rev.c.repandue", { liste: r.repandue.join(", ") }));
  li.append(p);
  return li;
}
