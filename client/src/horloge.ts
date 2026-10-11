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
  facteur: number;
  facteurAtteint: number;
  objectif: string | null;
  reprendreVitesse: boolean;
  /** Le monde se met en pause quand une catastrophe menace des humains. */
  pauseCatastrophe: boolean;
}

interface Vitesse {
  niveau: number;
  nom: string;
  facteur: number;
}

// Curseur logarithmique : position 0 → ×0,1 (ralenti), position 1000 → ×10 000 000.
const LOG_MIN = -1;
const LOG_MAX = 7;
const POSITIONS = 1000;
const versFacteur = (position: number) => {
  const f = 10 ** (LOG_MIN + ((LOG_MAX - LOG_MIN) * position) / POSITIONS);
  // Arrondi lisible : 2 chiffres significatifs.
  return Number(f.toPrecision(2));
};
const versPosition = (facteur: number) =>
  Math.round(((Math.log10(Math.max(facteur, 0.1)) - LOG_MIN) / (LOG_MAX - LOG_MIN)) * POSITIONS);

/** Vitesse en mots : « ×1 440 (1 min = 1 jour) ». */
export function decrireFacteur(facteur: number): string {
  if (facteur === 0) return "pause";
  const nombre = facteur < 10 ? facteur.toLocaleString("fr-FR", { maximumFractionDigits: 1 }) : Math.round(facteur).toLocaleString("fr-FR");
  const parMinute = facteur * 60; // secondes du monde par minute réelle
  const unites: [number, string][] = [
    [360 * 86_400, "an"],
    [90 * 86_400, "saison"],
    [86_400, "jour"],
    [3_600, "heure"],
    [60, "minute"],
    [1, "seconde"],
  ];
  const [taille, nom] = unites.find(([t]) => parMinute >= t) ?? [1, "seconde"];
  const quantite = parMinute / taille;
  const texte = quantite.toLocaleString("fr-FR", { maximumFractionDigits: quantite < 10 ? 1 : 0 });
  const pluriel = quantite >= 2 ? `${nom}s` : nom;
  return `×${nombre} (1 min = ${texte} ${pluriel})`;
}

/**
 * Commandes du temps : vitesses préréglées, vitesse libre, pas à pas, « aller jusqu'à » et réglage du redémarrage.
 * Prévient la carte des changements de lumière.
 */
export class PanneauHorloge {
  private boutons: HTMLButtonElement[] = [];
  private derniere?: Horloge;
  private curseurActif = false;

  constructor(
    private readonly racine: HTMLElement,
    private readonly date: HTMLElement,
    private readonly zoneVitesses: HTMLElement,
    private readonly surHorloge: (h: Horloge) => void,
    private readonly surMessage: (message: string) => void,
  ) {
    // Les gestionnaires ne renvoient rien : une valeur renvoyée serait prise pour une réponse par SignalR.
    connexion.on("Horloge", (h: Horloge) => {
      this.afficher(h);
    });
    connexion.on("ObjectifAtteint", (description: string) => {
      this.surMessage(`Arrivé : ${description}. Le temps est en pause.`);
    });
    this.brancher();
  }

  private el<T extends HTMLElement>(selecteur: string): T {
    return this.racine.querySelector<T>(selecteur)!;
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
      bouton.addEventListener("click", () => void this.envoyer("ChoisirVitesse", v.niveau));
      this.zoneVitesses.appendChild(bouton);
      return bouton;
    });
    this.afficher(await connexion.invoke<Horloge>("Horloge"));
  }

  private brancher(): void {
    const curseur = this.el<HTMLInputElement>("#curseurVitesse");
    curseur.max = String(POSITIONS);
    curseur.addEventListener("input", () => {
      this.curseurActif = true;
      this.el("#vitesseDemandee").textContent = decrireFacteur(versFacteur(Number(curseur.value)));
    });
    curseur.addEventListener("change", () => {
      this.curseurActif = false;
      void this.envoyer("ChoisirFacteur", versFacteur(Number(curseur.value)));
    });

    for (const bouton of this.racine.querySelectorAll<HTMLButtonElement>("[data-pas]")) {
      bouton.addEventListener("click", () => void this.envoyer("AvancerDe", bouton.dataset.pas));
    }

    const type = this.el<HTMLSelectElement>("#typeObjectif");
    const champsDate = this.el("#champsDate");
    type.addEventListener("change", () => (champsDate.hidden = type.value !== "date"));
    this.el<HTMLFormElement>("#formObjectif").addEventListener("submit", (ev) => {
      ev.preventDefault();
      const demande: Record<string, unknown> = { type: type.value };
      if (type.value === "date") {
        const lire = (id: string) => Number(this.el<HTMLInputElement>(id).value);
        demande.annee = lire("#objAnnee");
        demande.saison = lire("#objSaison");
        demande.jour = lire("#objJour");
        demande.heure = lire("#objHeure");
        if (![demande.annee, demande.saison, demande.jour, demande.heure].every(Number.isInteger)) {
          this.surMessage("La date doit être faite de nombres entiers.");
          return;
        }
      }
      void this.envoyer("AllerJusqua", demande);
    });
    this.el("#annulerObjectif").addEventListener("click", () => void this.envoyer("ChoisirVitesse", 0));

    const reprendre = this.el<HTMLInputElement>("#reprendreVitesse");
    reprendre.addEventListener("change", () => void this.envoyer("ChoisirRedemarrage", reprendre.checked));
    const pause = this.el<HTMLInputElement>("#pauseCatastrophe");
    pause.addEventListener("change", () => void this.envoyer("ChoisirPauseCatastrophe", pause.checked));
  }

  private async envoyer(methode: string, ...args: unknown[]): Promise<void> {
    try {
      this.afficher(await connexion.invoke<Horloge>(methode, ...args));
    } catch (erreur) {
      // Les refus du serveur (HubException) ont un message clair ; on ne montre que lui.
      const texte = erreur instanceof Error ? erreur.message.replace(/^.*HubException: /, "") : "";
      this.surMessage(texte || "Le serveur a refusé cette commande.");
    }
  }

  private afficher(h: Horloge): void {
    const precedente = this.derniere;
    this.derniere = h;
    this.date.textContent = `${h.estJour ? "☀" : "☾"} ${h.texte}`;
    for (const b of this.boutons) {
      b.classList.toggle("actif", b.dataset.niveau === String(h.niveauVitesse));
    }

    if (!this.curseurActif) {
      const curseur = this.el<HTMLInputElement>("#curseurVitesse");
      if (h.facteur > 0 && !h.objectif) curseur.value = String(versPosition(h.facteur));
      this.el("#vitesseDemandee").textContent = h.objectif ? "vitesse maximale" : decrireFacteur(h.facteur);
    }
    // Vitesse réelle : utile seulement si la machine ne suit pas, ou pendant un « aller jusqu'à ».
    const atteint = this.el("#vitesseAtteinte");
    const enRetard = h.facteur > 0 && h.facteurAtteint > 0 && h.facteurAtteint < h.facteur * 0.9;
    atteint.textContent = h.facteur > 0 && h.facteurAtteint > 0 ? `réelle : ${decrireFacteur(h.facteurAtteint)}` : "";
    atteint.classList.toggle("alerte", enRetard);

    const zoneObjectif = this.el("#objectifEnCours");
    zoneObjectif.hidden = !h.objectif;
    this.el("#texteObjectif").textContent = h.objectif ? `En route vers : ${h.objectif}` : "";
    this.el<HTMLInputElement>("#reprendreVitesse").checked = h.reprendreVitesse;
    this.el<HTMLInputElement>("#pauseCatastrophe").checked = h.pauseCatastrophe;

    // Pré-remplit la date cible avec la date courante, une seule fois.
    if (!precedente) {
      this.el<HTMLInputElement>("#objAnnee").value = String(h.annee + 1);
      this.el<HTMLInputElement>("#objSaison").value = "1";
      this.el<HTMLInputElement>("#objJour").value = "1";
      this.el<HTMLInputElement>("#objHeure").value = "6";
    }
    this.surHorloge(h);
  }
}
