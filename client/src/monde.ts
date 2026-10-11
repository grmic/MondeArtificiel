import type { VueCarte } from "./carte";
import { connexion } from "./connexion";
import { el } from "./fiche";

interface Mesure {
  libelle: string;
  valeur: string;
  aide: string | null;
  case: number;
}

interface Rubrique {
  titre: string;
  mesures: Mesure[];
}

interface Caracteristiques {
  nom: string;
  graine: number;
  date: string;
  kilometresParCase: number;
  metresParUnite: number;
  rubriques: Rubrique[];
  traversee: number[];
}

interface Trajet {
  kilometres: number;
  volDOiseau: number;
  heures: number;
  texte: string;
  cases: number[];
}

/** Fenêtre « Caractéristiques du Monde » : échelle, continent, relief, eaux, terrains, climat, population, marche. */
export class PanneauMonde {
  private readonly corps = el("div", "corps");
  private largeur = 0;

  constructor(
    private readonly racine: HTMLElement,
    private readonly vue: VueCarte,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, "Caractéristiques du Monde"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
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
    this.corps.replaceChildren(el("p", "discret", "Mesure du monde…"));
    const c = await connexion.invoke<Caracteristiques>("Caracteristiques").catch(() => null);
    if (!c) {
      this.corps.replaceChildren(el("p", "discret", "Le serveur n'a pas répondu."));
      return;
    }
    this.afficher(c);
  }

  fermer(): void {
    this.racine.hidden = true;
    this.vue.afficherTrajet(null);
  }

  private afficher(c: Caracteristiques): void {
    this.largeur = this.vue.largeurCarte();
    const entete = el("p", "discret", `${c.nom} · graine ${c.graine} · ${c.date}`);
    const parties: HTMLElement[] = [entete];
    for (const r of c.rubriques) {
      parties.push(el("h3", undefined, r.titre));
      const table = el("table", "mesures");
      for (const m of r.mesures) {
        const ligne = el("tr");
        const libelle = el("th", undefined, m.libelle);
        const valeur = el("td");
        valeur.append(el("span", undefined, m.valeur));
        if (m.aide) valeur.append(el("span", "aide", m.aide));
        if (m.case >= 0 && this.largeur > 0) {
          const voir = el("button", "lien", "voir");
          voir.type = "button";
          voir.title = "Montrer sur la carte";
          voir.addEventListener("click", () => {
            if (r.titre === "Marche" && c.traversee.length > 1) this.montrerTraversee(c.traversee);
            else this.vue.montrer(m.case % this.largeur, Math.floor(m.case / this.largeur));
          });
          valeur.append(" ", voir);
        }
        ligne.append(libelle, valeur);
        table.append(ligne);
      }
      parties.push(table);
    }
    this.corps.replaceChildren(...parties);
  }

  private montrerTraversee(cases: number[]): void {
    const l = this.largeur;
    const bout = (i: number) => ({ x: i % l, y: Math.floor(i / l) });
    this.vue.afficherTrajet(cases, [bout(cases[0]), bout(cases[cases.length - 1])]);
  }
}

/**
 * Règle : deux clics sur la carte donnent la distance à vol d'oiseau, le trajet à pied le moins pénible
 * (terrain et pentes) et le temps de marche d'un adulte.
 */
export class OutilMesure {
  private depart: { x: number; y: number } | null = null;
  private actif = false;

  constructor(
    private readonly bouton: HTMLButtonElement,
    private readonly vue: VueCarte,
    private readonly message: (texte: string) => void,
  ) {
    bouton.addEventListener("click", () => (this.actif ? this.arreter() : this.demarrer()));
  }

  get enCours(): boolean {
    return this.actif;
  }

  demarrer(): void {
    this.actif = true;
    this.depart = null;
    this.bouton.classList.add("actif");
    this.vue.afficherTrajet(null);
    this.message("Mesure : clique le point de départ sur la carte.");
  }

  arreter(): void {
    this.actif = false;
    this.depart = null;
    this.bouton.classList.remove("actif");
    this.vue.afficherTrajet(null);
    this.message("Mesure terminée.");
  }

  /** Un clic sur la carte pendant la mesure : départ, puis arrivée (un troisième clic recommence). */
  async cliquer(x: number, y: number): Promise<void> {
    if (!this.depart) {
      this.depart = { x, y };
      this.vue.afficherTrajet(null, [this.depart]);
      this.message("Mesure : clique maintenant le point d'arrivée.");
      return;
    }
    const a = this.depart;
    this.depart = null;
    const trajet = await connexion.invoke<Trajet | null>("Trajet", a.x, a.y, x, y).catch(() => null);
    if (!trajet) {
      this.vue.afficherTrajet(null, [a, { x, y }]);
      this.message("On ne peut pas aller de l'un à l'autre à pied (eau, île). Clique un nouveau départ.");
      return;
    }
    this.vue.afficherTrajet(trajet.cases, [a, { x, y }]);
    this.message(`${trajet.texte}\nClique un nouveau départ pour une autre mesure, ou « Mesurer » pour arrêter.`);
  }
}
