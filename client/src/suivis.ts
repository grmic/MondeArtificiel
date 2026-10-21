import { connexion } from "./connexion";
import { el } from "./fiche";

/** Un humain ou une communauté que l'on suit, avec son nom au moment où on l'a suivi. */
export interface Suivi {
  genre: "humain" | "communaute";
  id: number;
  nom: string;
}

interface Evenement {
  id: number;
  date: string;
  texte: string;
  significatif: boolean;
}

/** Suivis retenus dans ce navigateur. */
const CLE_SUIVIS = "monde.suivis";
const ACTUALISATION_MS = 10000;
/** Nouvelles demandées pour chacun, puis gardées en tout. */
const PAR_SUIVI = 15;
const NOUVELLES_MAX = 40;

let liste: Suivi[] = lire();

function lire(): Suivi[] {
  try {
    const brut = JSON.parse(localStorage.getItem(CLE_SUIVIS) ?? "[]") as unknown;
    if (!Array.isArray(brut)) return [];
    return brut.filter((s): s is Suivi =>
      typeof s === "object" && s !== null && (s.genre === "humain" || s.genre === "communaute")
      && Number.isInteger(s.id) && s.id > 0 && typeof s.nom === "string");
  } catch {
    return [];
  }
}

function enregistrer(): void {
  try {
    localStorage.setItem(CLE_SUIVIS, JSON.stringify(liste));
  } catch {
    // Stockage refusé : les suivis valent pour cette page seulement.
  }
  window.dispatchEvent(new Event("suivis"));
}

export function suivis(): readonly Suivi[] {
  return liste;
}

export function estSuivi(genre: Suivi["genre"], id: number): boolean {
  return liste.some((s) => s.genre === genre && s.id === id);
}

export function basculerSuivi(genre: Suivi["genre"], id: number, nom: string): void {
  liste = estSuivi(genre, id) ? liste.filter((s) => !(s.genre === genre && s.id === id)) : [...liste, { genre, id, nom }];
  enregistrer();
}

/** Nouveau monde : les numéros ne désignent plus les mêmes humains. */
export function oublierSuivis(): void {
  liste = [];
  enregistrer();
}

/** Bouton « ☆ Suivre » / « ★ Suivi » pour la tête d'une fiche. */
export function boutonSuivre(genre: Suivi["genre"], id: number, nom: string): HTMLButtonElement {
  const b = el("button", "suivre");
  b.type = "button";
  const peindre = () => {
    const oui = estSuivi(genre, id);
    b.textContent = oui ? "★ Suivi" : "☆ Suivre";
    b.title = oui ? "Ne plus suivre (fenêtre « Suivis »)" : "Suivre ses nouvelles dans la fenêtre « Suivis »";
    b.classList.toggle("actif", oui);
  };
  b.addEventListener("click", () => {
    basculerSuivi(genre, id, nom);
    peindre();
  });
  peindre();
  return b;
}

/**
 * Fenêtre « Suivis » : les humains et les communautés que l'on suit, et leurs dernières nouvelles mêlées, de la plus récente
 * à la plus ancienne. Les humains suivis sont entourés sur la carte tant qu'elle est ouverte.
 */
export class PanneauSuivis {
  private readonly corps = el("div", "corps");
  private readonly qui = el("ul", "liste suivis");
  private readonly nouvelles = el("ul", "liste nouvelles");
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void,
    private readonly entourer: (ids: number[]) => void,
  ) {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = "Fermer";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, "Suivis"), fermer);
    this.corps.append(this.qui, el("h3", undefined, "Leurs nouvelles"), this.nouvelles);
    racine.append(tete, this.corps);
    window.addEventListener("suivis", () => {
      if (this.ouvert) void this.charger();
    });
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else this.ouvrir();
  }

  ouvrir(): void {
    this.racine.hidden = false;
    void this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    if (!this.ouvert) return;
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
    this.entourer([]);
  }

  private async charger(): Promise<void> {
    const tous = [...liste];
    this.qui.replaceChildren(...tous.map((s) => {
      const li = el("li");
      const nom = el("button", "lien", s.nom);
      nom.type = "button";
      nom.title = s.genre === "humain" ? "Ouvrir sa fiche" : "Ouvrir la fiche de la communauté";
      nom.addEventListener("click", () => (s.genre === "humain" ? this.ouvrirHumain(s.id) : this.ouvrirCommunaute(s.id)));
      const retirer = el("button", "fermer", "×");
      retirer.type = "button";
      retirer.title = "Ne plus suivre";
      retirer.addEventListener("click", () => basculerSuivi(s.genre, s.id, s.nom));
      li.append(s.genre === "humain" ? "👤 " : "👥 ", nom, " ", retirer);
      return li;
    }));
    if (tous.length === 0) {
      this.qui.replaceChildren(el("li", "discret", "Personne n'est suivi. Sur la fiche d'un humain ou d'une communauté, « ☆ Suivre » l'ajoute ici."));
      this.nouvelles.replaceChildren();
      this.entourer([]);
      return;
    }
    this.entourer(tous.filter((s) => s.genre === "humain").map((s) => s.id));
    const listes = await Promise.all(tous.map((s) => connexion
      .invoke<Evenement[]>("Journal", s.genre === "humain" ? { humain: s.id, limite: PAR_SUIVI } : { communaute: s.id, limite: PAR_SUIVI })
      .catch(() => [] as Evenement[])));
    if (!this.ouvert) return;
    const vus = new Set<number>();
    const mele = listes.flat().filter((e) => !vus.has(e.id) && vus.add(e.id)).sort((a, b) => b.id - a.id).slice(0, NOUVELLES_MAX);
    this.nouvelles.replaceChildren(...(mele.length === 0
      ? [el("li", "discret", "Rien de noté pour l'instant.")]
      : mele.map((e) => el("li", e.significatif ? "significatif" : undefined, `${e.significatif ? "★ " : ""}${e.date} · ${e.texte}`))));
  }
}
