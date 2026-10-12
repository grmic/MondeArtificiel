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

const ACTUALISATION_MS = 5000;

/** Fenêtre « Découvertes » : chronologie des premiers savoirs du monde, qui les a trouvés, comment, et s'ils sont encore connus. */
export class PanneauDecouvertes {
  private readonly corps = el("div", "corps");
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
    const liste = await connexion.invoke<Decouverte[]>("Decouvertes").catch(() => null);
    if (!this.ouvert) return;
    if (!liste) {
      this.corps.replaceChildren(el("p", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    const defilement = this.corps.scrollTop;
    this.corps.replaceChildren(el("p", "discret",
      "Personne ne leur a appris : chaque savoir naît d'un essai, d'une observation, d'une réflexion ou d'une intuition de l'Entité, " +
      "se transmet par imitation et peut se perdre."));
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
}
