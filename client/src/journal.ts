import { connexion } from "./connexion";
import { el, ligneEvenement, type Evenement } from "./fiche";

const TYPES: [string, string][] = [
  ["naissance", "Naissances"],
  ["mort", "Morts"],
  ["mort-ne", "Morts-nés"],
  ["maladie", "Maladies"],
  ["union", "Couples"],
  ["accouplement", "Accouplements"],
  ["grossesse", "Grossesses"],
  ["fausse-couche", "Fausses couches"],
  ["delaissement", "Délaissements"],
  ["lien", "Amitiés et hostilités"],
  ["humeur", "Deuils et humeur"],
  ["conflit", "Jalousie et conflits"],
  ["attirance", "Attirances et refus"],
  ["exclusion", "Exils et reniements"],
  ["decouverte", "Découvertes et savoirs"],
  ["vol", "Vols"],
  ["entraide", "Soins et consolation"],
  ["parole", "Paroles de l'Entité"],
  ["croyance", "Croyance en l'invisible"],
  ["medecine", "Médecine et transes"],
  ["art", "Arts, chants et fêtes"],
  ["communaute", "Communautés"],
  ["guerre", "Guerres et alliances"],
  ["nature", "Nature"],
  ["catastrophe", "Catastrophes"],
  ["partage", "Partages"],
  ["pensee", "Pensées"],
  ["journee", "Journées"],
  ["souvenir", "Oublis"],
];

/** Types très nombreux : décochés au départ, et non reçus en direct (le bouton ↻ les relit). */
const FREQUENTS = new Set(["accouplement", "pensee", "journee", "souvenir", "partage"]);

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
    this.types = new Set(["communaute", "guerre", "naissance", "mort", "conflit", "union", "catastrophe", "exclusion", "vol", "entraide", "decouverte", "parole", "medecine", "art"]);
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
    tete.append(el("h2", undefined, "Journal"));
    const relire = el("button", "fermer", "↻");
    relire.type = "button";
    relire.title = "Relire le journal (les pensées, journées et oublis n'arrivent pas en direct)";
    relire.addEventListener("click", () => void this.recharger());
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
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
    for (const [cle, libelle] of TYPES) {
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
    etiquette.title = "Projets de guerre et renoncements, raids, guerres et paix, pactes, soumissions, catastrophes…";
    etiquette.append(seulement, " ★ Significatifs seulement");
    filtres.append(etiquette);

    const recherche = el("div", "ligne");
    const champ = el("input");
    champ.type = "search";
    champ.placeholder = "Nom d'un humain (vide : tous)";
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
    const plus = el("button", "plus", "Plus anciens…");
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
      ? `Journal figé (la simulation continue) : ${this.enAttente.length} nouvel${this.enAttente.length > 1 ? "s" : ""} événement${this.enAttente.length > 1 ? "s" : ""} en attente. Cliquer pour reprendre.`
      : "Figer le journal pour le lire tranquillement (la simulation continue)";
    b.classList.toggle("actif", this.enPause);
  }

  private dessinerListe(): void {
    const suivi = this.racine.querySelector<HTMLElement>(".suivi")!;
    suivi.hidden = this.communaute === null;
    if (this.communaute) {
      const arreter = el("button", undefined, "× tout le monde");
      arreter.type = "button";
      arreter.addEventListener("click", () => {
        this.communaute = null;
        void this.recharger();
      });
      suivi.replaceChildren(`Chronologie de la ${this.communaute.nom.charAt(0).toLowerCase()}${this.communaute.nom.slice(1)}`, arreter);
    }
    const zone = this.racine.querySelector(".evenements")!;
    zone.replaceChildren(...this.evenements.map((e) => {
      const li = ligneEvenement(e);
      li.title = "Cliquer pour ouvrir la fiche";
      li.addEventListener("click", () => this.ouvrirFiche(e.humain));
      return li;
    }));
    if (this.evenements.length === 0) zone.append(el("li", "discret", this.humain === -1 ? "Aucun humain de ce nom." : "Aucun événement."));
    this.racine.querySelector<HTMLButtonElement>(".plus")!.hidden = !this.plusAncien || this.evenements.length < 100;
  }
}
