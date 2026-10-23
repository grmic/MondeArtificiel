import { connexion } from "./connexion";
import { el, ligneEvenement, type Evenement } from "./fiche";
import { t } from "./langue";

const TYPES: [string, string][] = [
  ["naissance", "naissance"],
  ["mort", "mort"],
  ["mort-ne", "mort-ne"],
  ["maladie", "maladie"],
  ["union", "union"],
  ["accouplement", "accouplement"],
  ["grossesse", "grossesse"],
  ["fausse-couche", "fausse-couche"],
  ["delaissement", "delaissement"],
  ["lien", "lien"],
  ["humeur", "humeur"],
  ["conflit", "conflit"],
  ["attirance", "attirance"],
  ["exclusion", "exclusion"],
  ["decouverte", "decouverte"],
  ["vol", "vol"],
  ["entraide", "entraide"],
  ["parole", "parole"],
  ["croyance", "croyance"],
  ["religion", "religion"],
  ["geste", "geste"],
  ["medecine", "medecine"],
  ["art", "art"],
  ["langage", "langage"],
  ["mots", "mots"],
  ["communaute", "communaute"],
  ["guerre", "guerre"],
  ["nature", "nature"],
  ["catastrophe", "catastrophe"],
  ["partage", "partage"],
  ["pensee", "pensee"],
  ["journee", "journee"],
  ["souvenir", "souvenir"],
];

/** Types très nombreux : décochés au départ, et non reçus en direct (le bouton ↻ les relit). */
const FREQUENTS = new Set(["accouplement", "pensee", "journee", "souvenir", "partage", "mots"]);

/** Journal du monde : filtrable par type et par humain, mis à jour en direct, avec les plus anciens à la demande. */
export class PanneauJournal {
  private evenements: Evenement[] = [];
  private types = new Set(TYPES.map(([cle]) => cle).filter((c) => !FREQUENTS.has(c)));
  private humain: number | null = null;
  /** Chronologie d'une communauté suivie (null : tout le monde). */
  private communaute: { id: number; nom: string } | null = null;
  private plusAncien = true;
  /** Seulement les événements significatifs. */
  private significatifs = false;
  /** Journal figé : la simulation continue, les nouveaux événements attendent la reprise. */
  private enPause = false;
  private enAttente: Evenement[] = [];
  private readonly boutonPause = el("button", "fermer", "⏸");

  constructor(
    private readonly racine: HTMLElement,
    private readonly idDuNom: (nom: string) => number | undefined,
    private readonly nomsConnus: () => string[],
    private readonly ouvrirFiche: (id: number) => void,
  ) {
    this.construire();
    connexion.on("Journal", (nouveaux: Evenement[]) => {
      this.recevoir(nouveaux);
    });
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  /**
   * Chronologie d'une communauté : le journal ne montre plus que ce qui la concerne (sa vie de groupe, les naissances,
   * morts et conflits de ses membres, ses relations avec les autres groupes).
   */
  suivreCommunaute(id: number, nom: string): void {
    this.communaute = { id, nom };
    this.types = new Set(["communaute", "guerre", "naissance", "mort", "conflit", "union", "catastrophe", "exclusion", "vol", "entraide", "decouverte", "parole", "medecine", "art", "langage", "religion", "geste"]);
    this.racine.querySelectorAll<HTMLInputElement>(".filtres input").forEach((c) => (c.checked = this.types.has(c.dataset.cle ?? "")));
    this.racine.hidden = false;
    void this.recharger();
  }

  basculer(): void {
    this.racine.hidden = !this.racine.hidden;
    if (!this.racine.hidden) void this.recharger();
  }

  /** Après une reconnexion ou une réinitialisation. */
  async recharger(): Promise<void> {
    if (this.racine.hidden) return;
    this.evenements = await this.lire();
    this.enAttente = [];
    this.majPause();
    this.plusAncien = true;
    this.dessinerListe();
  }

  private filtre(avantId?: number) {
    return { types: [...this.types], humain: this.humain, avantId: avantId ?? null, limite: 100, communaute: this.communaute?.id ?? null, significatifs: this.significatifs };
  }

  private async lire(avantId?: number): Promise<Evenement[]> {
    if (this.types.size === 0) return [];
    return connexion.invoke<Evenement[]>("Journal", this.filtre(avantId)).catch(() => []);
  }

  private recevoir(nouveaux: Evenement[]): void {
    if (this.racine.hidden) return;
    const c = this.communaute?.id;
    const gardes = nouveaux.filter((e) => this.types.has(e.type) && (this.humain === null || e.humain === this.humain || e.autre === this.humain)
      && (c === undefined || e.communaute === c || e.autreCommunaute === c) && (!this.significatifs || e.significatif));
    if (gardes.length === 0) return;
    if (this.enPause) {
      this.enAttente.push(...gardes);
      if (this.enAttente.length > 1000) this.enAttente = this.enAttente.slice(-1000);
      this.majPause();
      return;
    }
    this.ajouter(gardes);
  }

  private ajouter(gardes: Evenement[]): void {
    const connus = new Set(this.evenements.map((e) => e.id));
    this.evenements = [...gardes.filter((e) => !connus.has(e.id)).reverse(), ...this.evenements].slice(0, 1000);
    this.dessinerListe();
  }

  private construire(): void {
    const r = this.racine;
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, t("journal.entete")));
    const relire = el("button", "fermer", "↻");
    relire.type = "button";
    relire.title = t("journal.relire");
    relire.addEventListener("click", () => void this.recharger());
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.basculer());
    this.boutonPause.type = "button";
    this.boutonPause.addEventListener("click", () => {
      this.enPause = !this.enPause;
      if (!this.enPause && this.enAttente.length > 0) {
        this.ajouter(this.enAttente);
        this.enAttente = [];
      }
      this.majPause();
    });
    this.majPause();
    const boutons = el("span");
    boutons.append(this.boutonPause, relire, fermer);
    tete.append(boutons);

    const filtres = el("div", "filtres");
    for (const [cle] of TYPES) {
      const libelle = t(`journal.type.${cle}`);
      const etiquette = el("label", "puce");
      const case_ = el("input");
      case_.type = "checkbox";
      case_.checked = this.types.has(cle);
      case_.dataset.cle = cle;
      case_.addEventListener("change", () => {
        if (case_.checked) this.types.add(cle);
        else this.types.delete(cle);
        void this.recharger();
      });
      etiquette.append(case_, ` ${libelle}`);
      filtres.append(etiquette);
    }

    const etiquette = el("label", "puce significatifs");
    const seulement = el("input");
    seulement.type = "checkbox";
    seulement.addEventListener("change", () => {
      this.significatifs = seulement.checked;
      void this.recharger();
    });
    etiquette.title = t("journal.significatifsAide");
    etiquette.append(seulement, " " + t("journal.significatifs"));
    filtres.append(etiquette);

    const recherche = el("div", "ligne");
    const champ = el("input");
    champ.type = "search";
    champ.placeholder = t("journal.recherche");
    champ.setAttribute("list", "nomsHumains");
    champ.maxLength = 40;
    const liste = el("datalist");
    liste.id = "nomsHumains";
    champ.addEventListener("focus", () => liste.replaceChildren(...this.nomsConnus().map((n) => Object.assign(el("option"), { value: n }))));
    champ.addEventListener("change", () => {
      const nom = champ.value.trim();
      this.humain = nom === "" ? null : this.idDuNom(nom) ?? -1;
      void this.recharger();
    });
    recherche.append(champ, liste);

    const suivi = el("div", "suivi");
    suivi.hidden = true;
    r.append(suivi);

    const zone = el("ul", "liste evenements");
    const plus = el("button", "plus", t("journal.plusAnciens"));
    plus.type = "button";
    plus.addEventListener("click", async () => {
      const dernier = this.evenements.at(-1);
      if (!dernier) return;
      const anciens = await this.lire(dernier.id);
      this.plusAncien = anciens.length === 100;
      this.evenements.push(...anciens);
      this.dessinerListe();
    });
    r.append(tete, filtres, recherche, suivi, zone, plus);
  }

  /** Bouton de pause : ⏸ en direct ; en pause, ▶ et le nombre d'événements arrivés depuis. */
  private majPause(): void {
    const b = this.boutonPause;
    b.textContent = this.enPause ? `▶${this.enAttente.length > 0 ? ` ${this.enAttente.length}` : ""}` : "⏸";
    b.title = this.enPause
      ? t("journal.fige", { n: this.enAttente.length })
      : t("journal.figer");
    b.classList.toggle("actif", this.enPause);
  }

  private dessinerListe(): void {
    const suivi = this.racine.querySelector<HTMLElement>(".suivi")!;
    suivi.hidden = this.communaute === null;
    if (this.communaute) {
      const arreter = el("button", undefined, t("journal.toutLeMonde"));
      arreter.type = "button";
      arreter.addEventListener("click", () => {
        this.communaute = null;
        void this.recharger();
      });
      suivi.replaceChildren(t("journal.chronologie", { nom: this.communaute.nom, nomMin: `${this.communaute.nom.charAt(0).toLowerCase()}${this.communaute.nom.slice(1)}` }), arreter);
    }
    const zone = this.racine.querySelector(".evenements")!;
    zone.replaceChildren(...this.evenements.map((e) => {
      const li = ligneEvenement(e);
      li.title = t("journal.ouvrirFiche");
      li.addEventListener("click", () => this.ouvrirFiche(e.humain));
      return li;
    }));
    if (this.evenements.length === 0) zone.append(el("li", "discret", this.humain === -1 ? t("journal.aucunHumain") : t("journal.aucun")));
    this.racine.querySelector<HTMLButtonElement>(".plus")!.hidden = !this.plusAncien || this.evenements.length < 100;
  }
}
