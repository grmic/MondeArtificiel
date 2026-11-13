import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";

/** Bilan d'un saut dans le futur, envoyé par le serveur à l'arrivée. */
interface BilanSaut {
  debut: string;
  fin: string;
  interrompu: boolean;
  ans: number;
  minutes: number;
  premier: number;
  dernier: number;
  vivantsAvant: number;
  vivantsApres: number;
  naissances: number;
  morts: { cause: string; nombre: number }[];
  mortsNes: number;
  decouvertes: number;
  premieres: number;
  communautesNees: number;
  communautesDisparues: number;
  raids: number;
  religionsNees: number;
}

/** Ce qu'une ligne du bilan ouvre dans le journal. */
export interface PeriodeJournal {
  apres: number;
  avant: number;
  libelle: string;
  types: string[];
  significatifs?: boolean;
  premieres?: boolean;
}

/**
 * Fenêtre « Bilan du saut » : population, naissances, morts par cause, découvertes, premières fois, communautés nées et
 * disparues, raids, religions. Chaque ligne ouvre le journal de la période, filtré sur ce qu'elle compte.
 */
export class PanneauBilan {
  private readonly corps = el("div", "corps");

  constructor(private readonly racine: HTMLElement, private readonly ouvrirJournal: (p: PeriodeJournal) => void) {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, t("sautBilan.titre")), fermer);
    racine.append(tete, this.corps);
    connexion.on("BilanSaut", (b: BilanSaut) => this.montrer(b));
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  private montrer(b: BilanSaut): void {
    const periode = t("sautBilan.periode", { debut: b.debut, fin: b.fin });
    const ligne = (texte: string, types: string[], options: { significatifs?: boolean; premieres?: boolean } = {}) => {
      const li = el("li");
      const bouton = el("button", "lien", texte);
      bouton.type = "button";
      bouton.title = t("sautBilan.ouvrirJournal");
      bouton.addEventListener("click", () => this.ouvrirJournal({
        apres: b.premier - 1, avant: b.dernier + 1, libelle: `${texte} · ${periode}`, types, ...options,
      }));
      li.append(bouton);
      return li;
    };
    const totalMorts = b.morts.reduce((s, m) => s + m.nombre, 0);
    const lignes = [
      ligne(t("sautBilan.population", { avant: nombre(b.vivantsAvant), apres: nombre(b.vivantsApres) }), ["naissance", "mort"]),
      ligne(t("sautBilan.naissances", { n: b.naissances, nb: nombre(b.naissances) }), ["naissance"]),
      ligne(t("sautBilan.morts", { n: totalMorts, nb: nombre(totalMorts) }), ["mort"]),
    ];
    const causes = b.morts.filter((m) => m.cause !== "");
    const liste = el("ul", "causes");
    liste.append(...causes.map((m) => el("li", undefined, `${m.cause} : ${nombre(m.nombre)}`)));
    if (causes.length > 0) lignes[2].append(liste);
    lignes.push(
      ligne(t("sautBilan.mortsNes", { n: b.mortsNes, nb: nombre(b.mortsNes) }), ["mort-ne"]),
      ligne(t("sautBilan.decouvertes", { n: b.decouvertes, nb: nombre(b.decouvertes) }), ["decouverte"], { significatifs: true }),
      ligne(t("sautBilan.premieres", { n: b.premieres, nb: nombre(b.premieres) }), [], { premieres: true }),
      ligne(t("sautBilan.communautesNees", { n: b.communautesNees, nb: nombre(b.communautesNees) }), ["communaute"]),
      ligne(t("sautBilan.communautesDisparues", { n: b.communautesDisparues, nb: nombre(b.communautesDisparues) }), ["communaute"]),
      ligne(t("sautBilan.raids", { n: b.raids, nb: nombre(b.raids) }), ["guerre"]),
      ligne(t("sautBilan.religions", { n: b.religionsNees, nb: nombre(b.religionsNees) }), ["religion"]),
    );
    const resume = el("p", undefined, t("sautBilan.resume", {
      periode, ans: nombre(b.ans, b.ans < 10 ? 1 : 0), minutes: nombre(b.minutes, b.minutes < 10 ? 1 : 0),
    }));
    const contenu: HTMLElement[] = [resume];
    if (b.interrompu) contenu.push(el("p", "alerte", t("sautBilan.interrompu")));
    const ul = el("ul", "liste bilan");
    ul.append(...lignes);
    contenu.push(ul, el("p", "discret", t("sautBilan.aide")));
    this.corps.replaceChildren(...contenu);
    this.racine.hidden = false;
  }
}
