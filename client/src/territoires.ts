import { connexion } from "./connexion";
import { couleurCommunaute, surfaceDeCases, type Territoires, type VueCarte } from "./carte";
import { el } from "./fiche";
import { t } from "./langue";

interface DateArchive {
  secondes: number;
  date: string;
}

/**
 * Terres des communautés sur la carte, aujourd'hui ou au début de n'importe quelle saison passée :
 * le curseur fait défiler l'histoire, la légende donne la couleur de chaque groupe.
 */
export class PanneauTerritoires {
  private dates: DateArchive[] = [];
  /** Indice dans les dates ; égal à leur nombre : maintenant. */
  private position = 0;
  private choisie = 0;
  private minuteur?: number;
  private readonly curseur = el("input");
  private readonly etiquette = el("span", "valeur");
  private readonly legende = el("ul", "liste legende");

  constructor(
    private readonly racine: HTMLElement,
    private readonly vue: VueCarte,
    private readonly ouvrirCommunaute: (id: number) => void,
  ) {
    const tete = el("div", "ligne");
    tete.append(el("strong", undefined, t("territoires.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("territoires.cacher");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    this.curseur.type = "range";
    this.curseur.min = "0";
    this.curseur.step = "1";
    this.curseur.title = t("territoires.curseur");
    this.curseur.addEventListener("input", () => {
      this.position = Number(this.curseur.value);
      void this.dessiner();
    });
    const ligne = el("div", "ligne");
    ligne.append(this.curseur, this.etiquette);
    racine.append(tete, ligne, el("p", "discret", t("territoires.aide")), this.legende);
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
    this.dates = await connexion.invoke<DateArchive[]>("DatesTerritoires").catch(() => []);
    this.position = this.dates.length;
    this.curseur.max = String(this.dates.length);
    this.curseur.value = String(this.position);
    await this.dessiner();
    window.clearInterval(this.minuteur);
    // Au présent, les terres bougent : relues toutes les 5 secondes.
    this.minuteur = window.setInterval(() => {
      if (this.position === this.dates.length) void this.dessiner();
    }, 5000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.racine.hidden = true;
    this.vue.afficherTerritoires(null);
  }

  /** Fait ressortir les terres d'une communauté (0 : aucune). */
  choisir(id: number): void {
    this.choisie = id;
    if (this.ouvert) void this.dessiner();
  }

  private async dessiner(): Promise<void> {
    const date = this.position < this.dates.length ? this.dates[this.position] : null;
    const terres = await connexion.invoke<Territoires | null>("Territoires", date?.secondes ?? null).catch(() => null);
    if (!this.ouvert) return;
    this.vue.afficherTerritoires(terres, this.choisie);
    this.etiquette.textContent = !terres ? t("territoires.rien") : terres.actuel ? t("territoires.maintenant", { date: terres.date }) : terres.date;
    this.legende.replaceChildren();
    for (const c of [...(terres?.communautes ?? [])].sort((a, b) => b.membres - a.membres)) {
      const li = el("li");
      const [r, g, b] = couleurCommunaute(c.id);
      const pastille = el("span", "pastille");
      pastille.style.background = `rgb(${r}, ${g}, ${b})`;
      const lien = el("button", "lien", c.nom);
      lien.type = "button";
      lien.addEventListener("click", () => this.ouvrirCommunaute(c.id));
      const cases = c.plages.reduce((n, v, i) => (i % 2 === 1 ? n + v : n), 0);
      li.append(pastille, lien, t("territoires.ligne", { n: c.membres, surface: surfaceDeCases(cases) }));
      this.legende.append(li);
    }
    if (terres && terres.communautes.length === 0) this.legende.append(el("li", "discret", t("territoires.aucune")));
  }
}
