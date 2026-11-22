import { t } from "./langue";

/**
 * Tiroir à onglets (0.45.27) : les fenêtres de droite (Journal, Suivis, Faune, Découvertes…) vivent dans un seul tiroir.
 * Chaque fenêtre ouverte y a son onglet ; celles qui ne sont pas devant restent ouvertes (défilement, filtres et recherche
 * gardés), simplement masquées. Les panneaux ne changent pas : ils s'ouvrent et se ferment toujours par leur `hidden`,
 * que le tiroir surveille pour poser ou retirer l'onglet. Le bord gauche du tiroir se tire à la souris pour l'élargir ;
 * le navigateur retient la largeur choisie pour chaque fenêtre.
 */
export interface PanneauDuTiroir {
  basculer(): void;
  fermer(): void;
}

interface Inscrit {
  racine: HTMLElement;
  panneau: PanneauDuTiroir;
  bouton: HTMLElement | null;
  largeurDefaut: number;
  onglet: HTMLElement | null;
}

const CLE_LARGEUR = "monde.onglets.largeur.";
const LARGEUR_MIN = 280;
const MARGE = 24;

function lireLargeur(id: string): number | null {
  try {
    const v = Number(localStorage.getItem(CLE_LARGEUR + id));
    return Number.isFinite(v) && v >= LARGEUR_MIN ? v : null;
  } catch {
    return null;
  }
}

function ecrireLargeur(id: string, largeur: number): void {
  try {
    localStorage.setItem(CLE_LARGEUR + id, String(Math.round(largeur)));
  } catch {
    // Stockage indisponible : la largeur vaut pour cette page seulement.
  }
}

/** Largeur d'origine d'une fenêtre, d'après la sorte de panneau qu'elle était (avant les onglets). */
function largeurDeClasse(racine: HTMLElement): number {
  if (racine.classList.contains("demographie-panneau")) return 760;
  if (racine.classList.contains("monde-panneau")) return 440;
  return 380;
}

export class Tiroir {
  private readonly racine: HTMLElement;
  private readonly barre: HTMLElement;
  private readonly pages: HTMLElement;
  private readonly inscrits: Inscrit[] = [];
  /** Ordre dans lequel les onglets ont été mis devant (le dernier est devant) : fermer l'onglet de devant ramène le précédent. */
  private historique: Inscrit[] = [];
  private devant: Inscrit | null = null;

  constructor(avant: HTMLElement) {
    this.racine = document.createElement("div");
    this.racine.className = "tiroir";
    this.racine.hidden = true;
    const poignee = document.createElement("div");
    poignee.className = "poignee";
    poignee.title = t("onglets.poigneeAide");
    this.barre = document.createElement("div");
    this.barre.className = "onglets";
    this.barre.setAttribute("role", "tablist");
    this.pages = document.createElement("div");
    this.pages.className = "pages";
    this.racine.append(poignee, this.barre, this.pages);
    avant.before(this.racine);
    this.tirer(poignee);
    window.addEventListener("resize", () => this.dimensionner());
  }

  /** Range une fenêtre dans le tiroir ; `bouton` est celui de la barre du haut qui l'ouvre (il gagne un point vert). */
  inscrire(racine: HTMLElement, panneau: PanneauDuTiroir, bouton: HTMLElement | null = null): void {
    const inscrit: Inscrit = { racine, panneau, bouton, largeurDefaut: largeurDeClasse(racine), onglet: null };
    this.inscrits.push(inscrit);
    this.pages.append(racine);
    racine.setAttribute("role", "tabpanel");
    // Le panneau montre ou masque sa racine par `hidden` ; on l'intercepte, même quand il la remontre alors qu'elle est
    // déjà ouverte (un lien qui l'ouvre sur autre chose doit la ramener devant).
    const proto = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "hidden")!;
    Object.defineProperty(racine, "hidden", {
      configurable: true,
      get: () => proto.get!.call(racine) as boolean,
      set: (v: boolean) => {
        proto.set!.call(racine, v);
        if (v) this.retirer(inscrit);
        else this.montrer(inscrit);
      },
    });
    if (!racine.hidden) this.montrer(inscrit);
  }

  /** Un clic sur le bouton de la barre : ramène devant l'onglet caché, sinon ouvre ou ferme la fenêtre comme avant. */
  bouton(racine: HTMLElement): boolean {
    const inscrit = this.inscrits.find((i) => i.racine === racine);
    if (inscrit && inscrit.onglet && this.devant !== inscrit) {
      this.activer(inscrit);
      return true;
    }
    return false;
  }

  private montrer(inscrit: Inscrit): void {
    if (!inscrit.onglet) {
      const onglet = document.createElement("div");
      onglet.className = "onglet";
      onglet.setAttribute("role", "tab");
      onglet.tabIndex = 0;
      const nom = document.createElement("span");
      nom.className = "nom";
      nom.textContent = t(inscrit.racine.dataset.tAriaLabel ?? "") || inscrit.racine.getAttribute("aria-label") || "?";
      const croix = document.createElement("button");
      croix.type = "button";
      croix.className = "croix";
      croix.textContent = "×";
      croix.title = t("onglets.fermerAide");
      croix.addEventListener("click", (e) => {
        e.stopPropagation();
        inscrit.panneau.fermer();
      });
      onglet.append(nom, croix);
      onglet.addEventListener("click", () => this.activer(inscrit));
      onglet.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          this.activer(inscrit);
        }
      });
      // Clic du milieu : ferme l'onglet, comme dans un navigateur.
      onglet.addEventListener("mousedown", (e) => {
        if (e.button === 1) e.preventDefault();
      });
      onglet.addEventListener("auxclick", (e) => {
        if (e.button !== 1) return;
        e.preventDefault();
        inscrit.panneau.fermer();
      });
      inscrit.onglet = onglet;
      this.barre.append(onglet);
      inscrit.bouton?.classList.add("onglet-ouvert");
    }
    this.activer(inscrit);
  }

  private retirer(inscrit: Inscrit): void {
    if (!inscrit.onglet) return;
    inscrit.onglet.remove();
    inscrit.onglet = null;
    inscrit.racine.classList.remove("derriere");
    inscrit.bouton?.classList.remove("onglet-ouvert");
    this.historique = this.historique.filter((i) => i !== inscrit);
    if (this.devant === inscrit) {
      this.devant = null;
      const precedent = this.historique.at(-1) ?? this.inscrits.find((i) => i.onglet);
      if (precedent) this.activer(precedent);
    }
    if (!this.inscrits.some((i) => i.onglet)) this.racine.hidden = true;
  }

  private activer(inscrit: Inscrit): void {
    this.devant = inscrit;
    this.historique = [...this.historique.filter((i) => i !== inscrit), inscrit];
    for (const i of this.inscrits) {
      const devant = i === inscrit;
      i.racine.classList.toggle("derriere", !devant);
      i.onglet?.classList.toggle("actif", devant);
      i.onglet?.setAttribute("aria-selected", String(devant));
    }
    this.racine.hidden = false;
    this.dimensionner();
  }

  /** Le tiroir à déplacer à la souris (0.45.29). */
  get element(): HTMLElement {
    return this.racine;
  }

  /** Largeur donnée par le coin du tiroir : retenue pour la fenêtre de devant, comme celle du bord gauche. */
  fixerLargeur(l: number): void {
    if (!this.devant) return;
    this.racine.style.width = `${l}px`;
    ecrireLargeur(this.devant.racine.id, l);
  }

  private largeur(inscrit: Inscrit): number {
    return lireLargeur(inscrit.racine.id) ?? inscrit.largeurDefaut;
  }

  private dimensionner(): void {
    if (!this.devant) return;
    const max = Math.max(LARGEUR_MIN, window.innerWidth - MARGE);
    this.racine.style.width = `${Math.min(this.largeur(this.devant), max)}px`;
  }

  /** Le bord gauche se tire pour élargir ou rétrécir le tiroir ; la largeur est retenue pour la fenêtre de devant. */
  private tirer(poignee: HTMLElement): void {
    let depart: { x: number; largeur: number; gauche: number } | null = null;
    poignee.addEventListener("pointerdown", (e) => {
      if (!this.devant) return;
      e.preventDefault();
      poignee.setPointerCapture(e.pointerId);
      const r = this.racine.getBoundingClientRect();
      depart = { x: e.clientX, largeur: r.width, gauche: r.left };
      this.racine.classList.add("tire");
    });
    poignee.addEventListener("pointermove", (e) => {
      if (!depart || !this.devant) return;
      const max = Math.max(LARGEUR_MIN, window.innerWidth - MARGE);
      const l = Math.min(max, Math.max(LARGEUR_MIN, depart.largeur + depart.x - e.clientX));
      this.racine.style.width = `${l}px`;
      // Déplacé à la souris (0.45.29), le tiroir est tenu par sa gauche : il faut la reculer pour qu'il grandisse vers la gauche.
      if (this.racine.classList.contains("deplacee")) this.racine.style.left = `${Math.max(0, depart.gauche + depart.largeur - l)}px`;
    });
    const lacher = () => {
      if (!depart || !this.devant) return;
      depart = null;
      this.racine.classList.remove("tire");
      ecrireLargeur(this.devant.racine.id, this.racine.getBoundingClientRect().width);
    };
    poignee.addEventListener("pointerup", lacher);
    poignee.addEventListener("pointercancel", lacher);
    // Double-clic : revenir à la largeur d'origine de la fenêtre.
    poignee.addEventListener("dblclick", () => {
      if (!this.devant) return;
      try {
        localStorage.removeItem(CLE_LARGEUR + this.devant.racine.id);
      } catch {
        // Stockage indisponible.
      }
      this.dimensionner();
    });
  }
}
