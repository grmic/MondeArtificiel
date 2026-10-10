import { connexion } from "./connexion";

export interface Horloge {
  secondes: number;
  pas: number;
  texte: string;
  annee: number;
  saison: string;
  jour: number;
  heure: number;
  minute: number;
  estJour: boolean;
  lumiere: number;
  niveauVitesse: number;
}

interface Vitesse {
  niveau: number;
  nom: string;
  facteur: number;
}

/** Affiche la date du monde et les boutons de vitesse ; prévient la carte des changements de lumière. */
export class PanneauHorloge {
  private boutons: HTMLButtonElement[] = [];

  constructor(
    private readonly date: HTMLElement,
    private readonly zoneVitesses: HTMLElement,
    private readonly surHorloge: (h: Horloge) => void,
    private readonly surErreur: (message: string) => void,
  ) {
    connexion.on("Horloge", (h: Horloge) => this.afficher(h));
  }

  /** À appeler à chaque (re)connexion : recharge les vitesses et l'heure courante. */
  async synchroniser(): Promise<void> {
    const vitesses = await connexion.invoke<Vitesse[]>("Vitesses");
    this.zoneVitesses.replaceChildren();
    this.boutons = vitesses.map((v) => {
      const bouton = document.createElement("button");
      bouton.type = "button";
      bouton.textContent = v.niveau === 0 ? "⏸ Pause" : v.niveau === 1 ? "▶ Temps réel" : v.nom;
      bouton.title = v.niveau <= 1 ? v.nom : `Accéléré : ${v.nom}`;
      bouton.dataset.niveau = String(v.niveau);
      bouton.addEventListener("click", () => void this.choisir(v.niveau));
      this.zoneVitesses.appendChild(bouton);
      return bouton;
    });
    this.afficher(await connexion.invoke<Horloge>("Horloge"));
  }

  private async choisir(niveau: number): Promise<void> {
    try {
      this.afficher(await connexion.invoke<Horloge>("ChoisirVitesse", niveau));
    } catch {
      this.surErreur("Le serveur a refusé ce changement de vitesse.");
    }
  }

  private afficher(h: Horloge): void {
    this.date.textContent = `${h.estJour ? "☀" : "☾"} ${h.texte}`;
    for (const b of this.boutons) {
      b.classList.toggle("actif", b.dataset.niveau === String(h.niveauVitesse));
    }
    this.surHorloge(h);
  }
}
