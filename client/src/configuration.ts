import { connexion } from "./connexion";
import { el } from "./fiche";

/** Réglages du monde courant et limites, renvoyés par le serveur. Largeur et hauteur en cases. */
interface Configuration {
  graine: number;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
  origine: boolean;
  kilometresParCase: number;
  couplesMin: number;
  couplesMax: number;
  tailleMin: number;
  tailleMax: number;
  continentsMin: number;
  continentsMax: number;
  resume: string;
}

/** Réglages d'un nouveau monde (graine null : au hasard). */
export interface Reglages {
  graine: number | null;
  couples: number;
  largeur: number;
  hauteur: number;
  continents: number;
}

interface Apercu {
  possible: boolean;
  texte: string;
}

const GRAINE_MAX = 4_294_967_295;

function champ(libelle: string, aide: string, attributs: Record<string, string>): { ligne: HTMLElement; entree: HTMLInputElement } {
  const ligne = el("label", "reglage");
  const entree = el("input");
  entree.type = "number";
  entree.required = true;
  for (const [k, v] of Object.entries(attributs)) entree.setAttribute(k, v);
  ligne.append(el("span", "libelle", libelle), entree, el("span", "aide", aide));
  return { ligne, entree };
}

/**
 * Fenêtre « Configuration » : les réglages du monde courant (gardés dans sa base) et ceux d'un nouveau monde,
 * avec un aperçu de sa carte avant de le créer.
 */
export class PanneauConfiguration {
  private readonly corps = el("div", "corps");
  private config: Configuration | null = null;
  private readonly graine = champ("Graine", "même graine et mêmes réglages : même monde de départ", { min: "0", max: String(GRAINE_MAX), step: "1" });
  private readonly couples = champ("Couples de départ", "", { step: "1" });
  private readonly largeur = champ("Largeur de la carte (km)", "", { step: "1" });
  private readonly hauteur = champ("Hauteur de la carte (km)", "", { step: "1" });
  private readonly continents = champ("Continents", "les couples partent tous du plus grand ; les autres ne sont pas accessibles à pied", { step: "1" });
  private readonly actuel = el("p");
  private readonly message = el("p", "message-config");
  private readonly hasard = el("button", undefined, "🎲");
  private apercuAffiche = false;

  constructor(
    private readonly racine: HTMLElement,
    private readonly dialogue: HTMLDialogElement,
    /** Affiche sur la carte l'aperçu de ces réglages (null : revient au monde courant). Rend un message d'erreur ou null. */
    private readonly montrer: (reglages: Reglages | null) => Promise<string | null>,
    private readonly informer: (texte: string) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, "Configuration"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    this.hasard.type = "button";
    this.hasard.title = "Graine au hasard";
    this.hasard.addEventListener("click", () => {
      this.graine.entree.value = String(Math.floor(Math.random() * (GRAINE_MAX + 1)));
    });
    this.graine.ligne.insertBefore(this.hasard, this.graine.ligne.querySelector(".aide"));

    const formulaire = el("form", "config");
    const boutons = el("div", "boutons");
    const apercu = el("button", undefined, "Aperçu de la carte");
    apercu.type = "submit";
    const revenir = el("button", undefined, "Revenir au monde actuel");
    revenir.type = "button";
    revenir.addEventListener("click", () => void this.revenir());
    const creer = el("button", "danger", "Créer ce monde…");
    creer.type = "button";
    creer.addEventListener("click", () => this.demanderCreation());
    boutons.append(apercu, revenir, creer);
    formulaire.append(this.graine.ligne, this.couples.ligne, this.largeur.ligne, this.hauteur.ligne, this.continents.ligne, boutons, this.message);
    formulaire.addEventListener("submit", (ev) => {
      ev.preventDefault();
      void this.apercu();
    });

    this.corps.append(
      el("h3", undefined, "Monde actuel"),
      this.actuel,
      el("h3", undefined, "Nouveau monde"),
      el("p", "discret", "Ces réglages sont enregistrés dans la base du monde créé. Une case de la carte mesure 800 m : cette finesse reste fixe, la carte grandit en nombre de cases. Au-delà de 400 km de côté, la simulation ralentit."),
      formulaire,
    );
    racine.append(tete, this.corps);

    dialogue.addEventListener("close", () => {
      if (dialogue.returnValue === "confirmer") void this.creer();
    });
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
    this.actuel.textContent = "Lecture des réglages…";
    const c = await connexion.invoke<Configuration>("Configuration").catch(() => null);
    if (!c) {
      this.actuel.textContent = "Le serveur n'a pas répondu.";
      return;
    }
    this.config = c;
    this.actuel.textContent = `Graine ${c.graine} · ${c.couples} couples · ${c.continents} continent${c.continents > 1 ? "s" : ""}${c.origine ? " (réglages d'origine)" : ""}. ${c.resume}`;
    const km = (cases: number) => Math.round(cases * c.kilometresParCase);
    const bornes = (e: HTMLInputElement, min: number, max: number) => {
      e.min = String(min);
      e.max = String(max);
    };
    bornes(this.couples.entree, c.couplesMin, c.couplesMax);
    bornes(this.largeur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.hauteur.entree, Math.ceil(c.tailleMin * c.kilometresParCase), Math.floor(c.tailleMax * c.kilometresParCase));
    bornes(this.continents.entree, c.continentsMin, c.continentsMax);
    this.couples.ligne.querySelector(".aide")!.textContent = `${c.couplesMin} à ${c.couplesMax} ; un couple par emplacement, deux humains chacun`;
    const tailles = `${this.largeur.entree.min} à ${this.largeur.entree.max} km`;
    this.largeur.ligne.querySelector(".aide")!.textContent = tailles;
    this.hauteur.ligne.querySelector(".aide")!.textContent = tailles;
    this.graine.entree.value = String(c.graine);
    this.couples.entree.value = String(c.couples);
    this.largeur.entree.value = String(km(c.largeur));
    this.hauteur.entree.value = String(km(c.hauteur));
    this.continents.entree.value = String(c.continents);
    this.message.textContent = "";
  }

  fermer(): void {
    this.racine.hidden = true;
    if (this.apercuAffiche) void this.revenir();
  }

  /** Le monde a été recréé (ici ou dans un autre navigateur) : la carte affichée est celle du monde courant. */
  mondeRecree(): void {
    this.apercuAffiche = false;
    if (this.ouvert) void this.ouvrir();
  }

  /** Réglages saisis, vérifiés ; null (avec un message) s'ils sont invalides. */
  private lire(): Reglages | null {
    const c = this.config;
    if (!c) return null;
    const entier = (e: HTMLInputElement, nom: string): number | null => {
      const v = Number(e.value);
      if (e.value.trim() === "" || !Number.isInteger(v) || v < Number(e.min) || v > Number(e.max)) {
        this.message.textContent = `${nom} : un entier entre ${e.min} et ${e.max}.`;
        return null;
      }
      return v;
    };
    const graine = entier(this.graine.entree, "Graine");
    const couples = entier(this.couples.entree, "Couples");
    const largeur = entier(this.largeur.entree, "Largeur");
    const hauteur = entier(this.hauteur.entree, "Hauteur");
    const continents = entier(this.continents.entree, "Continents");
    if (graine === null || couples === null || largeur === null || hauteur === null || continents === null) return null;
    const cases = (km: number) => Math.min(c.tailleMax, Math.max(c.tailleMin, Math.round(km / c.kilometresParCase)));
    return { graine, couples, largeur: cases(largeur), hauteur: cases(hauteur), continents };
  }

  private async apercu(): Promise<boolean> {
    const r = this.lire();
    if (!r) return false;
    this.message.textContent = "Génération de la carte…";
    const a = await connexion.invoke<Apercu>("ApercuConfiguration", r).catch(() => null);
    if (!a) {
      this.message.textContent = "Le serveur n'a pas répondu.";
      return false;
    }
    if (!a.possible) {
      this.message.textContent = `Impossible : ${a.texte}`;
      return false;
    }
    const erreur = await this.montrer(r);
    this.apercuAffiche = erreur === null;
    this.message.textContent = erreur ?? `Aperçu : ${a.texte}`;
    return erreur === null;
  }

  private async revenir(): Promise<void> {
    this.apercuAffiche = false;
    await this.montrer(null);
  }

  private demanderCreation(): void {
    const r = this.lire();
    if (!r) return;
    const texte = this.dialogue.querySelector("#texteNouveauMonde");
    if (texte) {
      texte.textContent = `Le monde recommence à l'an 1 avec ${r.couples} couple${r.couples > 1 ? "s" : ""} (graine ${r.graine}, ` +
        `carte de ${Math.round(r.largeur * (this.config?.kilometresParCase ?? 0.8))} × ${Math.round(r.hauteur * (this.config?.kilometresParCase ?? 0.8))} km, ` +
        `${r.continents} continent${r.continents > 1 ? "s" : ""}). Toute son histoire est effacée ; une copie de l'ancien monde est gardée dans monde-avant-reinitialisation.db (seule la dernière).`;
    }
    this.dialogue.returnValue = "";
    this.dialogue.showModal();
  }

  private async creer(): Promise<void> {
    const r = this.lire();
    if (!r) return;
    this.message.textContent = "Création du monde…";
    this.informer("Création du nouveau monde…");
    try {
      await connexion.invoke("CreerMonde", r);
      this.message.textContent = "Monde créé.";
    } catch (e) {
      // Le serveur renvoie la raison (réglages impossibles) ; rien n'a changé.
      const raison = e instanceof Error ? e.message.replace(/^.*HubException: /, "") : "";
      this.message.textContent = `Le serveur a refusé : ${raison || "réglages impossibles"}`;
      this.informer("Le monde n'a pas changé.");
    }
  }
}
