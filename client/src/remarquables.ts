import { connexion } from "./connexion";
import { el } from "./fiche";
import { t } from "./langue";

interface Critere {
  cle: string;
  libelle: string;
  /** On peut aussi demander les plus bas (intelligence, aptitudes, karma, croyance). */
  bas: boolean;
}

interface Remarquable {
  rang: number;
  id: number;
  nom: string;
  sexe: string;
  age: number;
  communaute: string | null;
  communauteId: number;
  vivant: boolean;
  valeur: string;
  detail: string | null;
}

const ACTUALISATION_MS = 10000;

/**
 * Fenêtre « Remarquables » : les humains hors du commun selon un critère (QI, aptitudes, karma, croyance, découvertes,
 * soins, œuvres, descendance, longévité, victimes). Les vivants classés sont entourés sur la carte ; un clic ouvre la fiche.
 */
export class PanneauRemarquables {
  private readonly corps = el("div", "corps");
  private readonly critere = el("select");
  private readonly sens = el("select");
  private readonly morts = el("input");
  private readonly liste = el("ol", "liste remarquables");
  private criteres: Critere[] = [];
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    /** Entoure ces humains sur la carte (liste vide : plus personne). */
    private readonly entourer: (ids: number[]) => void,
    private readonly ouvrirCommunaute: (id: number) => void = () => {},
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("remarquables.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    const plusHaut = el("option", undefined, t("remarquables.plusHauts"));
    plusHaut.value = "haut";
    const plusBas = el("option", undefined, t("remarquables.plusBas"));
    plusBas.value = "bas";
    this.sens.append(plusHaut, plusBas);
    this.morts.type = "checkbox";
    const etiquetteMorts = el("label", "case-config");
    etiquetteMorts.append(this.morts, " " + t("remarquables.morts"));
    const filtres = el("div", "filtres-remarquables");
    filtres.append(this.critere, this.sens, etiquetteMorts);
    for (const e of [this.critere, this.sens, this.morts]) e.addEventListener("change", () => void this.charger());

    this.corps.append(
      el("p", "discret", t("remarquables.aide")),
      filtres,
      this.liste,
    );
    racine.append(tete, this.corps);
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
    if (this.criteres.length === 0) {
      const criteres = await connexion.invoke<Critere[]>("CriteresRemarquables").catch(() => null);
      if (!criteres) {
        this.liste.replaceChildren(el("li", "discret", t("commun.pasDeReponse")));
        return;
      }
      this.criteres = criteres;
      this.critere.replaceChildren(...criteres.map((c) => {
        const o = el("option", undefined, c.libelle);
        o.value = c.cle;
        return o;
      }));
    }
    await this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    // Déjà fermée : on ne touche pas aux humains qu'une autre fenêtre (les communautés) a pu entourer.
    if (!this.ouvert) return;
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
    this.entourer([]);
  }

  private async charger(): Promise<void> {
    const critere = this.criteres.find((c) => c.cle === this.critere.value);
    this.sens.disabled = !critere?.bas;
    if (!critere?.bas) this.sens.value = "haut";
    const liste = await connexion
      .invoke<Remarquable[]>("Remarquables", this.critere.value, this.sens.value === "bas", this.morts.checked)
      .catch(() => null);
    if (!this.ouvert) return;
    if (!liste) {
      this.liste.replaceChildren(el("li", "discret", t("commun.pasDeReponse")));
      return;
    }
    if (liste.length === 0) {
      this.liste.replaceChildren(el("li", "discret", t("remarquables.personne")));
      this.entourer([]);
      return;
    }
    this.liste.replaceChildren(...liste.map((r) => {
      const li = el("li", r.vivant ? undefined : "discret");
      const qui = el("button", "lien", r.nom);
      qui.type = "button";
      qui.addEventListener("click", () => this.ouvrirHumain(r.id));
      const g = r.sexe === "femme" ? "f" : "m";
      const qualite = [t("remarquables.qualite", { sexe: t(r.sexe === "femme" ? "sexe.femme" : "sexe.homme"), age: t("age.ans", { n: r.age }) }), r.vivant ? null : t("commun.mort", { g })]
        .filter((x) => x).join(" · ");
      li.append(qui, ` (${qualite}`);
      if (r.communaute && r.communauteId > 0) {
        const groupe = el("button", "lien", r.communaute);
        groupe.type = "button";
        groupe.title = t("commun.ficheCommunaute");
        groupe.addEventListener("click", () => this.ouvrirCommunaute(r.communauteId));
        li.append(" · ", groupe);
      }
      li.append(") : ", el("strong", undefined, r.valeur));
      if (r.detail) li.append(` · ${r.detail}`);
      return li;
    }));
    this.entourer(liste.filter((r) => r.vivant).map((r) => r.id));
  }
}
