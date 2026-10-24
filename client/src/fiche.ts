import { boutonSuivre } from "./suivis";
import { ligneBilan, lignePriere, type BilanPrieres, type Priere } from "./cultes";
import { connexion } from "./connexion";
import type { HumainVue } from "./carte";
import { FormulaireEntite } from "./entite";
import { FormulaireGeste } from "./gestes";
import { nombre, t } from "./langue";

interface Evenement {
  id: number;
  secondes: number;
  date: string;
  type: string;
  humain: number;
  autre: number;
  texte: string;
  origine: "naturelle" | "entite";
  /** Numéro de l'événement qui l'a provoqué (0 : aucune cause notée). */
  cause: number;
  /** Communauté concernée, et l'autre pour ce qui se passe entre deux groupes (0 : aucune). */
  communaute: number;
  autreCommunaute: number;
  /** Un tournant (projet de guerre, renoncement, raid, paix, catastrophe…). */
  significatif: boolean;
  /** Karma gagné ou perdu par cet acte (regard de l'Entité), par l'humain et par l'autre. */
  karma?: number;
  karmaAutre?: number;
  /** Écrit avant la traduction du journal : la phrase reste en français dans une autre langue. */
  ancien?: boolean;
}

/** Une chose du jardin secret. */
interface Intime {
  date: string;
  type: string;
  sujet: number;
  nomSujet: string | null;
  texte: string;
  intensite: number;
  cause: number;
  /** Écrite avant la traduction du jardin secret (0.30.2) : reste en français. */
  ancien?: boolean;
}

/** Rubriques du jardin secret, dans l'ordre d'affichage. */
const RUBRIQUES: [string, string][] = [
  ["intention", "jardin.intention"],
  ["amour", "jardin.amour"],
  ["desir", "jardin.desir"],
  ["jalousie", "jardin.jalousie"],
  ["peur", "jardin.peur"],
  ["espoir", "jardin.espoir"],
  ["joie", "jardin.joie"],
  ["peine", "jardin.peine"],
  ["manque", "jardin.manque"],
  ["opinion", "jardin.opinion"],
  ["image-de-soi", "jardin.image-de-soi"],
  ["fierte", "jardin.fierte"],
  ["culpabilite", "jardin.culpabilite"],
  ["honte", "jardin.honte"],
  ["envie", "jardin.envie"],
  ["souvenir", "jardin.souvenir"],
  ["reve", "jardin.reve"],
  ["emerveillement", "jardin.emerveillement"],
  ["ennui", "jardin.ennui"],
  ["croyance", "jardin.croyance"],
  ["question", "jardin.question"],
];

interface Ame {
  etat: string;
  humeur: number;
  chagrin: number;
  deuilDe: number;
  deprimeDepuis: string | null;
  raisons: string[];
  colere: number;
  rancunes: { id: number; nom: string; motif: string; intensite: number; evenement: number }[];
  depressionEvenement: number;
}

interface Trait {
  nom: string;
  valeur: number;
  mots: string;
  aide: string;
}

interface Fiche {
  humain: HumainVue;
  caractere: Trait[];
  intelligence: Trait[];
  biologie: Record<string, number>;
  ame: Ame | null;
  communaute: { id: number; nom: string } | null;
  enfants: number[];
  pensees: { date: string; texte: string; ancien?: boolean }[];
  jardin: Intime[];
  lieux: { type: string; x: number; y: number; vuLe: string; visites: number }[];
  liens: { id: number; nom: string; familiarite: number; affection: number; statut: string; vivant: boolean }[];
  histoire: Evenement[];
  epreuves: Epreuves;
  attraits: Attraits;
  /** Compassion, empathies, honnêteté-humilité, psychopathie, attachement adulte. */
  coeur: Trait[];
  /** Fondements moraux (soin, équité, loyauté, autorité, pureté). */
  morale: Trait[];
  /** Le caractère en phrases : style d'attachement, psychopathie, morale dominante. */
  portrait: string[];
  savoirs: Savoirs;
  /** Paroles et penchants reçus de l'Entité, du plus récent au plus ancien. */
  paroles: { date: string; mode: string; texte: string; reponse: string; cible: number; evenement: number }[];
  /** Regard de l'Entité, invisible aux humains : karma et croyance en l'invisible (0 à 100). */
  regard: {
    karma: number; karmaMots: string; croyance: number; croyanceMots: string; dernierSigne: string | null;
    /** Regard sur l'invisible (0 à 100) : amour, crainte, révolte, et le dernier signe vu. */
    amour: number; amourMots: string; crainte: number; crainteMots: string; revolte: number; revolteMots: string; dernierGeste: string | null;
  };
  /** Médecine : soins donnés, rôle de soignant, plaie, malédiction. */
  medecine: {
    soignes: number; gueris: number; perdus: number; accouchementsAides: number; role: string | null; renommee: number;
    guerisseurDepuis: string | null; chamanDepuis: string | null; charlatanDepuis: string | null; desavoueLe: string | null;
    plaie: string | null; malediction: string | null;
  };
  /** Parole : stade, nom dans sa langue, mots les plus sûrs, ordre des mots, autres langues comprises. */
  langage: Langage | null;
  /** Sa religion, sa ferveur (0 à 100), son rôle, et ses prières récentes. */
  foi: { religion: number; nomReligion: string | null; ferveur: number; role: string | null; source: string | null; miracles: number; prieres: Priere[];
    bilan: BilanPrieres | null;
  } | null;
  /** Le feu qu'il porte, s'il est le porteur du feu, ses caches et ses leçons du feu (absent d'un serveur plus ancien). */
  feu?: { porte: string | null; porteurDepuis: string | null; feuxPortes: number; caches: string[]; lecons: string[] } | null;
  /** Visite, rassemblement, communauté d'origine, métier et ouvrages faits pour d'autres (absent d'un serveur plus ancien). */
  rencontres?: { visite: string | null; rassemblement: string | null; venuDe: string | null; metier: string | null; metierDepuis: string | null;
    servisTaille: number; servisAssemblage: number; servisPeau: number; attend: string | null } | null;
}

/** Un mot et sa traduction. */
interface Mot {
  sens: string; forme: string; signe: boolean; force: number; depuis: string; auteur: number; nomAuteur: string | null;
}

interface Langage {
  stade: string; nom: string | null; sens: string | null; nommeLe: string | null; nommePar: number; nomPar: string | null;
  nomEvenement: number; nombreDeMots: number; mots: Mot[]; ordre: string | null; comprises: { id: number; nom: string; pourcent: number }[];
}

/** Ce qu'il sait faire (et à quel point il y croit), ce qu'il porte, son vêtement, son aisance dans l'eau (0 à 100). */
interface Savoirs {
  savoirs: { texte: string; confiance: number; origine: string; maitre: number; nomMaitre: string | null; appriLe: string; essais: number; evenement: number;
    /** Vivants qui le tiennent de lui (absent d'un serveur plus ancien). */
    eleves?: { id: number; nom: string }[] }[];
  objets: string[];
  vetement: string | null;
  nage: number;
}

interface Choix {
  cle: string;
  libelle: string;
}

/** Un geste que l'Entité peut souffler, avec les matières qui vont avec lui (A, puis B reliée par la liaison). */
interface FormeGeste {
  cle: string;
  verbe: string;
  liaison: string | null;
  a: Choix[];
  b: Choix[];
}

/** Beauté et attractivité (0 à 100, aux yeux de sa communauté), orientation, ce qu'il en sait, exclusions. */
interface Attraits {
  beaute: number;
  attractivite: number;
  orientation: string;
  versHommes: number;
  versFemmes: number;
  conscience: string | null;
  ideal: string;
  exclusions: { type: string; par: number; nomPar: string; le: string; jusqua: string | null; raison: string; enCours: boolean; evenement: number }[];
  impubere: boolean;
}

/** Maladie en cours, immunités, et peur laissée par une catastrophe. */
interface Epreuves {
  maladie: string | null;
  maladieDepuis: string | null;
  maladieEvenement: number;
  immunites: string[];
  peur: number;
  peurDe: string | null;
  peurEvenement: number;
}

export type { Evenement };

/** Petit constructeur d'éléments : le texte passe toujours par textContent (jamais de HTML injecté). */
export function el<K extends keyof HTMLElementTagNameMap>(tag: K, classe?: string, texte?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texte !== undefined) e.textContent = texte;
  return e;
}

/** Sections de la fiche ouvertes (par leur titre) : Besoins et Humeur par défaut, puis ce que l'observateur a choisi. */
const CLE_SECTIONS = "fiche.sections";
let sectionsOuvertes = lireSections();

function lireSections(): Set<string> {
  try {
    const brut = localStorage.getItem(CLE_SECTIONS);
    const liste: unknown = brut ? JSON.parse(brut) : null;
    if (Array.isArray(liste)) return new Set(liste.filter((x): x is string => typeof x === "string").slice(0, 50));
  } catch {
    // Stockage indisponible ou valeur abîmée : on repart des sections par défaut.
  }
  return new Set([t("fiche.besoins"), t("fiche.humeur")]);
}

function enregistrerSections(): void {
  try {
    localStorage.setItem(CLE_SECTIONS, JSON.stringify([...sectionsOuvertes]));
  } catch {
    // Navigation privée ou stockage plein : le choix vaut pour cette session seulement.
  }
}

/** Fiche d'un humain : qui il est, ce qu'il pense, ce dont il se souvient et ce qu'il a vécu. */
export class PanneauFiche {
  private id = 0;
  private minuteur?: number;
  /** Formulaire de l'Entité : construit une fois, il survit aux rafraîchissements de la fiche. */
  private souffle: HTMLDetailsElement | null = null;
  private rechargerSouffle: (() => void) | null = null;
  private readonly resultatSouffle = el("p", "discret");
  /** Parler ou inspirer un amour : construit une fois, comme le formulaire du souffle. */
  private entite: FormulaireEntite | null = null;
  /** Gestes de l'Entité sur cet humain : construit une fois, comme les autres formulaires. */
  private geste: FormulaireGeste | null = null;
  private communaute = 0;
  private nomCourant = "";
  /** Fiche d'où l'on vient (une communauté), pour le bouton « retour » : fourni par la page, oublié à la fermeture. */
  retour: { libelle: string; aller: () => void } | null = null;

  constructor(
    private readonly racine: HTMLElement,
    private readonly nom: (id: number) => string,
    private readonly surChoix: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void = () => {},
  ) {}

  get courant(): number {
    return this.id;
  }

  async ouvrir(id: number): Promise<void> {
    if (id !== this.id) {
      replierCauses();
      this.resultatSouffle.textContent = "";
      this.entite?.oublier();
      this.geste?.oublier();
    }
    const autre = id !== this.id;
    this.id = id;
    this.surChoix(id);
    // Un autre humain n'est pas au même endroit : ses plantes ne sont pas les mêmes.
    if (autre && this.souffle?.open) this.rechargerSouffle?.();
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    // Tant qu'elle est ouverte, la fiche suit l'humain (deux fois par seconde au plus, sans file d'attente).
    this.minuteur = window.setInterval(() => void this.rafraichir(), 2000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.id = 0;
    this.retour = null;
    this.racine.hidden = true;
    this.surChoix(0);
  }

  private async rafraichir(): Promise<void> {
    if (!this.id) return;
    const id = this.id;
    const fiche = await connexion.invoke<Fiche | null>("Fiche", id).catch(() => null);
    if (!fiche || id !== this.id) return;
    // Redessiner pendant que l'on choisit dans le formulaire de l'Entité fermerait les menus déroulants.
    if (this.saisieEnCours()) return;
    this.dessiner(fiche);
  }

  /** Attraits, orientation et exclusions (exil, reniement), avec leur raison. */
  private attraits(corps: HTMLElement, f: Fiche, g: string): void {
    const a = f.attraits;
    corps.append(el("h3", undefined, t("fiche.attraits")));
    const p = el("p", "attraits");
    if (a.impubere) {
      p.append(t("fiche.tropJeune"));
      corps.append(p);
    } else {
      p.title = t("fiche.beauteAide");
      p.append(t("fiche.beaute", { beaute: a.beaute, attractivite: a.attractivite }), el("br"),
        t("fiche.ideal", { ideal: a.ideal }), el("br"),
        t("fiche.orientation", { orientation: a.orientation, hommes: a.versHommes, femmes: a.versFemmes }));
      if (a.conscience) p.append(` ; ${a.conscience}`);
      corps.append(p);
    }
    if (a.exclusions.length === 0) return;
    const ul = el("ul", "liste exclusions");
    for (const x of a.exclusions) {
      const li = el("li", x.enCours ? "alerte" : "discret");
      const duree = x.jusqua ? t("fiche.finPrevue", { date: x.jusqua }) : t("fiche.pourToujours");
      const groupe = { nom: x.nomPar, nomMin: `${x.nomPar.charAt(0).toLowerCase()}${x.nomPar.slice(1)}`, g };
      const qui = t(x.type === "renié" ? "fiche.renie" : x.type === "exilé" ? "fiche.chasse" : "fiche.parti", groupe);
      li.append(t(x.enCours ? "fiche.exclusion" : "fiche.exclusionFinie", { qui, raison: x.raison, le: x.le, duree }) + " ");
      if (x.evenement > 0) li.append(boutonCause(li, x.evenement, t("commun.ceQuiSestPasse")));
      ul.append(li);
    }
    corps.append(ul);
  }

  private lien(id: number, texte?: string): HTMLElement {
    const a = el("button", "lien", texte ?? this.nom(id));
    a.type = "button";
    a.addEventListener("click", () => void this.ouvrir(id));
    return a;
  }

  private dessiner(f: Fiche): void {
    const h = f.humain;
    const r = this.racine;
    const defilement = r.querySelector(".corps")?.scrollTop ?? 0;
    r.replaceChildren();
    r.hidden = false;

    const tete = el("div", "tete");
    const titre = el("h2", undefined, h.nom);
    if (f.langage?.nom) {
      const nom = el("span", "nom-langue", ` « ${f.langage.nom} »`);
      nom.title = f.langage.sens ? t("fiche.nomLangueSens", { sens: f.langage.sens }) : t("fiche.nomLangue");
      titre.append(nom);
    }
    tete.append(titre, boutonSuivre("humain", this.id, h.nom));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("fiche.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    r.append(tete);

    const corps = el("div", "corps");
    if (this.retour) {
      const retour = this.retour;
      const b = el("button", "lien retour", `← ${retour.libelle}`);
      b.type = "button";
      b.addEventListener("click", () => retour.aller());
      corps.append(b);
    }
    const g = h.sexe === "femme" ? "f" : "m";
    const age = h.age < 2 ? t("age.mois", { n: Math.round(h.age * 12) }) : t("age.ans", { n: Math.floor(h.age) });
    const etat = h.vivant ? `${h.activite}${h.malade ? ` · ${t("fiche.malade", { g })}` : ""}${h.enceinte ? ` · ${t("fiche.enceinte")}` : ""}`
      : t("fiche.mortDe", { g, cause: h.causeDeMort ?? "" });
    corps.append(el("p", "resume", t("fiche.resume", { sexe: t(h.sexe === "femme" ? "fiche.femme" : "fiche.homme"), age, etat })));
    if (h.pensee) corps.append(el("p", "pensee", t("commun.guillemets", { texte: h.pensee })));
    if (f.ame) {
      corps.append(el("h3", undefined, t("fiche.humeur")));
      const ame = el("p", "ame");
      ame.append(t("fiche.humeurValeur", { etat: f.ame.etat, v: f.ame.humeur }));
      if (f.ame.chagrin > 0) {
        ame.append(" · " + t("fiche.chagrin", { v: f.ame.chagrin }));
        if (f.ame.deuilDe) ame.append(" (", this.lien(f.ame.deuilDe), ")");
      }
      if (f.ame.colere >= 10) ame.append(" · " + t("fiche.colere", { v: f.ame.colere }));
      if (f.ame.deprimeDepuis) {
        ame.append(el("br"), t("fiche.deprime", { g, date: f.ame.deprimeDepuis }));
        if (f.ame.depressionEvenement > 0) ame.append(" ", boutonCause(ame, f.ame.depressionEvenement, t("commun.pourquoi")));
      }
      if (f.ame.raisons.length > 0) ame.append(el("br"), t("fiche.raisons", { raisons: f.ame.raisons.join(t("fiche.pointVirgule")) }));
      corps.append(ame);
      if (f.ame.rancunes.length > 0) {
        corps.append(el("h4", undefined, t("fiche.rancunes")));
        const rancunes = el("ul", "liste");
        for (const grief of f.ame.rancunes) {
          const li = el("li");
          li.append(this.lien(grief.id, grief.nom), " " + t("fiche.rancune", { motif: grief.motif, v: grief.intensite }) + " ");
          if (grief.evenement > 0) li.append(boutonCause(li, grief.evenement, t("commun.ceQuiSestPasse")));
          rancunes.append(li);
        }
        corps.append(rancunes);
      }
    }
    const ep = f.epreuves;
    if (ep.maladie || ep.peur >= 5 || ep.immunites.length > 0) {
      const p = el("p", "epreuves");
      if (ep.maladie) {
        p.append(t("fiche.porteMaladie", { maladie: ep.maladie }) + (h.contagieux ? t("fiche.contagieux", { g }) : "") + (ep.maladieDepuis ? t("fiche.depuis", { date: ep.maladieDepuis }) : "") + " ");
        if (ep.maladieEvenement > 0) p.append(boutonCause(p, ep.maladieEvenement, t("commun.comment")), " ");
        p.append(el("br"));
      }
      if (ep.peur >= 5 && ep.peurDe) {
        p.append(t("fiche.peur", { v: ep.peur, g, de: ep.peurDe }) + " ");
        if (ep.peurEvenement > 0) p.append(boutonCause(p, ep.peurEvenement, t("commun.pourquoi")));
        p.append(el("br"));
      }
      if (ep.immunites.length > 0) p.append(t("fiche.immunise", { g, liste: ep.immunites.join(t("commun.virgule")) }));
      corps.append(p);
    }
    this.attraits(corps, f, g);
    if (f.communaute) {
      const c = el("p", "communaute");
      const bouton = el("button", "lien", f.communaute.nom);
      bouton.type = "button";
      const id = f.communaute.id;
      bouton.addEventListener("click", () => this.ouvrirCommunaute(id));
      c.append(t("fiche.vitDans"), bouton);
      corps.append(c);
    }

    // Famille
    const famille = el("p", "famille");
    const ajouter = (libelle: string, ids: number[]) => {
      if (ids.length === 0) return;
      famille.append(t("fiche.libelle", { libelle }));
      ids.forEach((id, i) => {
        if (i > 0) famille.append(", ");
        famille.append(this.lien(id));
      });
      famille.append(el("br"));
    };
    if (h.mere) ajouter(t("fiche.parents"), [h.mere, h.pere].filter(Boolean));
    else famille.append(t("fiche.coupleDepart", { n: String(h.emplacement) }), el("br"));
    ajouter(t("fiche.compagnon"), h.partenaire ? [h.partenaire] : []);
    ajouter(t("fiche.enfants"), f.enfants);
    corps.append(famille);

    if (h.vivant) {
      corps.append(this.jauges(t("fiche.besoins"), {
        [t("besoin.faim")]: h.faim, [t("besoin.soif")]: h.soif, [t("besoin.fatigue")]: h.fatigue, [t("besoin.froid")]: h.froid,
        [t("besoin.sante")]: h.sante, [t("besoin.reserves")]: h.reserves,
      }, 100));
    }
    corps.append(this.traits(t("fiche.caractere"), f.caractere, 1));
    if (f.portrait.length > 0) corps.append(el("p", "portrait", f.portrait.join(" ")));
    corps.append(this.traits(t("fiche.coeur"), f.coeur, 1));
    corps.append(this.traits(t("fiche.morale"), f.morale, 1));
    corps.append(this.traits(t("fiche.intelligence"), f.intelligence, 160));
    const bio = el("p", "discret", Object.entries(f.biologie).map(([k, v]) => `${k} ${v}`).join(" · "));
    corps.append(el("h3", undefined, t("fiche.biologie")), bio);

    this.savoirs(corps, f.savoirs, g);
    corps.append(el("h3", undefined, t("fiche.regardEntite")));
    const regard = el("p", "regard");
    regard.append(t("fiche.karma", { v: signe(f.regard.karma), mots: f.regard.karmaMots }), el("br"),
      t("fiche.croyanceInvisible", { v: f.regard.croyance, mots: f.regard.croyanceMots }));
    if (f.regard.dernierSigne) regard.append(el("br"), t("fiche.dernierSigne", { date: f.regard.dernierSigne }));
    regard.title = t("fiche.regardAide");
    corps.append(regard);
    corps.append(el("h3", undefined, t("fiche.regardInvisible")));
    const invisible = el("p", "regard");
    invisible.append(t("fiche.croyance", { v: f.regard.croyance, mots: f.regard.croyanceMots }), el("br"),
      t("fiche.amour", { v: f.regard.amour, mots: f.regard.amourMots }), el("br"),
      t("fiche.crainte", { v: f.regard.crainte, mots: f.regard.crainteMots }), el("br"),
      t("fiche.revolte", { v: f.regard.revolte, mots: f.regard.revolteMots }));
    if (f.regard.dernierGeste) invisible.append(el("br"), t("fiche.dernierGeste", { date: f.regard.dernierGeste }));
    invisible.title = t("fiche.invisibleAide");
    corps.append(invisible);
    this.medecine(corps, f.medecine);
    this.feu(corps, f.feu ?? null);
    this.rencontres(corps, f.rencontres ?? null);
    this.foi(corps, f.foi);
    this.langage(corps, f.langage);
    if (h.vivant) corps.append(this.formulaireSouffle());
    this.communaute = f.communaute?.id ?? 0;
    this.nomCourant = h.nom;
    if (f.paroles.length > 0) {
      corps.append(el("h3", undefined, t("fiche.paroles")));
      const ul = el("ul", "liste");
      for (const x of f.paroles.slice(0, 20)) {
        const li = el("li");
        li.append(el("span", "entite", "✦ "), t("fiche.parole", { date: x.date, mode: x.mode, texte: x.texte }) + " ", el("span", "discret", `(${x.reponse})`), " ");
        if (x.evenement > 0) li.append(boutonCause(li, x.evenement, t("commun.ceQuiSestPasse")));
        ul.append(li);
      }
      corps.append(ul);
    }
    if (h.vivant && h.etape !== "bébé") {
      this.entite ??= new FormulaireEntite(() => ({
        humains: [this.id], communautes: [], pres: this.id, communaute: this.communaute, libelle: this.nomCourant,
      }));
      corps.append(this.entite.bloc);
    }
    if (h.vivant) {
      this.geste ??= new FormulaireGeste(true, () => ({ id: this.id, nom: this.nomCourant }));
      corps.append(this.geste.bloc);
    }

    corps.append(el("h3", undefined, t("fiche.liens")));
    const liens = el("ul", "liste");
    for (const l of f.liens.slice(0, 15)) {
      const li = el("li", `lien-${l.statut}`);
      li.append(this.lien(l.id, l.nom), ` ${l.vivant ? "" : "✝ "}— ` + t("fiche.lien", { statut: t(`statut.${l.statut}`), affection: l.affection, familiarite: l.familiarite }));
      liens.append(li);
    }
    if (f.liens.length === 0) liens.append(el("li", "discret", t("fiche.personne")));
    else if (f.liens.length > 15) liens.append(el("li", "discret", t("fiche.etAutres", { n: f.liens.length - 15 })));
    corps.append(liens);

    corps.append(el("h3", undefined, t("fiche.pensees")));
    const pensees = el("ul", "liste");
    for (const p of f.pensees) {
      const li = el("li");
      if (p.ancien) li.title = t("evenement.ancien");
      li.append(el("span", "date", p.date), ` ${p.texte}`);
      pensees.append(li);
    }
    if (f.pensees.length === 0) pensees.append(el("li", "discret", t("fiche.aucunePensee")));
    corps.append(pensees);

    corps.append(el("h3", undefined, t("fiche.jardin")));
    if (f.jardin.length === 0) corps.append(el("p", "discret", t("fiche.rienConfie")));
    for (const [cle, titre] of RUBRIQUES) {
      const choses = f.jardin.filter((x) => x.type === cle);
      if (choses.length === 0) continue;
      corps.append(el("h4", undefined, t(titre)));
      const ul = el("ul", "liste jardin");
      for (const x of choses) {
        const li = el("li");
        li.title = t("fiche.force", { v: x.intensite, date: x.date }) + (x.ancien ? `\n${t("evenement.ancien")}` : "");
        li.append(el("span", "date", x.date), ` ${x.texte} `, el("span", "discret", `(${x.intensite})`));
        if (x.sujet > 0 && x.nomSujet) li.append(" ", this.lien(x.sujet, "→ " + x.nomSujet));
        if (x.cause > 0) li.append(" ", boutonCause(li, x.cause, t("commun.pourquoi")));
        ul.append(li);
      }
      corps.append(ul);
    }

    corps.append(el("h3", undefined, t("fiche.memoire")));
    const lieux = el("ul", "liste");
    for (const l of f.lieux) {
      lieux.append(el("li", undefined, t(l.visites > 1 ? "fiche.lieuVisites" : "fiche.lieu", { type: t(`lieu.${l.type}`), x: String(l.x), y: String(l.y), n: l.visites, date: l.vuLe })));
    }
    if (f.lieux.length === 0) lieux.append(el("li", "discret", t("fiche.aucunLieu")));
    corps.append(lieux);

    corps.append(el("h3", undefined, t("fiche.histoire")));
    const histoire = el("ul", "liste");
    for (const e of f.histoire) histoire.append(ligneEvenement(e));
    if (f.histoire.length === 0) histoire.append(el("li", "discret", t("fiche.rienEncore")));
    corps.append(histoire);

    this.sectionner(corps);
    r.append(corps);
    corps.scrollTop = defilement;
  }

  /**
   * Range la fiche en sections déroulantes : chaque titre (h3) ouvre une section qui garde tout ce qui le suit. L'en-tête
   * (résumé, pensée, communauté, famille) reste visible ; les formulaires de l'Entité vont à la fin. Les sections ouvertes
   * sont retenues d'un rafraîchissement, d'un humain et d'une session à l'autre.
   */
  private sectionner(corps: HTMLElement): void {
    const entete: Element[] = [];
    const sections: HTMLDetailsElement[] = [];
    const fin: Element[] = [];
    let courante: HTMLDetailsElement | null = null;
    for (const n of Array.from(corps.children)) {
      if (n.matches(".resume, .pensee, .communaute, .famille")) {
        entete.push(n);
        continue;
      }
      if (n.matches(".entite-bloc")) {
        fin.push(n);
        continue;
      }
      let titre: Element | null = null;
      if (n.tagName === "H3") titre = n;
      else if (n.tagName === "DIV" && n.firstElementChild?.tagName === "H3") {
        titre = n.firstElementChild;
        titre.remove();
      }
      if (titre) {
        courante = this.section(titre.textContent ?? "");
        sections.push(courante);
        if (titre === n) continue;
      }
      if (courante) courante.append(n);
      else entete.push(n);
    }
    const boutons = el("div", "sections-boutons");
    const tout = (ouvrir: boolean) => {
      for (const s of sections) s.open = ouvrir;
      sectionsOuvertes = new Set(ouvrir ? sections.map((s) => s.dataset.titre ?? "") : []);
      enregistrerSections();
    };
    for (const [texte, ouvrir] of [[t("fiche.toutOuvrir"), true], [t("fiche.toutFermer"), false]] as const) {
      const b = el("button", "lien", texte);
      b.type = "button";
      b.addEventListener("click", () => tout(ouvrir));
      boutons.append(b);
    }
    corps.replaceChildren(boutons, ...entete, ...sections, ...fin);
  }

  private section(titre: string): HTMLDetailsElement {
    const d = el("details", "section");
    d.dataset.titre = titre;
    d.open = sectionsOuvertes.has(titre);
    d.append(el("summary", undefined, titre));
    d.addEventListener("toggle", () => {
      if (d.open === sectionsOuvertes.has(titre)) return;
      if (d.open) sectionsOuvertes.add(titre);
      else sectionsOuvertes.delete(titre);
      enregistrerSections();
    });
    return d;
  }

  /** Savoirs (texte, confiance, comment il les a appris et de qui, à qui il les a appris), objets portés, vêtement, nage. */
  private savoirs(corps: HTMLElement, s: Savoirs, g: string): void {
    corps.append(el("h3", undefined, t("fiche.savoirs")));
    const p = el("p", "discret");
    p.append(s.vetement ? t("fiche.vetu", { g, vetement: s.vetement }) : t("fiche.nu", { g }));
    if (s.objets.length > 0) p.append(" · " + t("fiche.porte", { objets: s.objets.join(t("commun.virgule")) }));
    if (s.nage > 0) p.append(" · " + t("fiche.nage", { v: s.nage }));
    corps.append(p);
    const ul = el("ul", "liste");
    for (const x of s.savoirs) {
      const li = el("li", x.confiance < 40 ? "discret" : undefined);
      li.title = t("fiche.essais", { n: x.essais, date: x.appriLe });
      li.append(`${x.texte} `, el("span", "discret", t("fiche.confiance", { v: x.confiance, origine: x.origine })));
      if (x.maitre > 0 && x.nomMaitre) li.append(el("span", "discret", " " + t("fiche.de") + " "), this.lien(x.maitre, x.nomMaitre));
      li.append(el("span", "discret", ")"));
      if (x.eleves && x.eleves.length > 0) {
        li.append(el("span", "discret", " · " + t("fiche.aApprisA") + " "));
        x.eleves.slice(0, 6).forEach((o, i) => {
          if (i > 0) li.append(", ");
          li.append(this.lien(o.id, o.nom));
        });
        if (x.eleves.length > 6) li.append(el("span", "discret", " " + t("fiche.etNAutres", { n: x.eleves.length - 6 })));
      }
      if (x.origine === t("origineSavoir.Entite")) li.append(el("span", "entite", " ✦"));
      if (x.evenement > 0) li.append(" ", boutonCause(li, x.evenement, t("commun.comment")));
      ul.append(li);
    }
    if (s.savoirs.length === 0) ul.append(el("li", "discret", t("fiche.rienFaire")));
    corps.append(ul);
  }

  /**
   * L'Entité souffle une idée : un geste et une ou deux matières. L'humain l'essaie aussitôt avec ce qu'il a sous la main ;
   * les lois du monde décident du résultat (rien n'est garanti).
   */
  /** Section « Médecine » : ce qu'il a fait comme soignant, son rôle, sa plaie et la malédiction qui le ronge. */
  private medecine(corps: HTMLElement, m: Fiche["medecine"]): void {
    const rien = m.soignes === 0 && m.accouchementsAides === 0 && !m.role && !m.desavoueLe && !m.plaie && !m.malediction;
    if (rien) return;
    corps.append(el("h3", undefined, t("fiche.medecine")));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (m.role) lignes.push(t("medecine.role", { role: m.role, v: m.renommee }));
    if (m.soignes > 0) lignes.push(t("medecine.soignes", { soignes: m.soignes, gueris: m.gueris, perdus: m.perdus }));
    if (m.accouchementsAides > 0) lignes.push(t("medecine.accouchements", { n: m.accouchementsAides }));
    if (m.guerisseurDepuis) lignes.push(t("medecine.guerisseur", { date: m.guerisseurDepuis }));
    if (m.chamanDepuis) lignes.push(t("medecine.transe", { date: m.chamanDepuis }));
    if (m.charlatanDepuis) lignes.push(t("medecine.charlatan", { date: m.charlatanDepuis }));
    if (m.desavoueLe) lignes.push(t("medecine.desavoue", { date: m.desavoueLe }));
    if (m.plaie) lignes.push(t("medecine.plaie", { plaie: m.plaie }));
    if (m.malediction) lignes.push(m.malediction.charAt(0).toUpperCase() + m.malediction.slice(1));
    lignes.forEach((l, i) => {
      if (i > 0) p.append(el("br"));
      p.append(l);
    });
    p.title = t("medecine.aide");
    corps.append(p);
  }

  /** Section « Feu et réserves » : le feu qu'il porte, s'il est le porteur du feu des siens, ses caches, ce qu'il sait du feu. */
  private feu(corps: HTMLElement, x: Fiche["feu"] | null): void {
    if (!x || !x.porte && !x.porteurDepuis && x.feuxPortes === 0 && x.caches.length === 0 && x.lecons.length === 0) return;
    corps.append(el("h3", undefined, t("fiche.feu")));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (x.porte) lignes.push(t("feu.porte", { objet: x.porte }));
    if (x.porteurDepuis) lignes.push(t("feu.porteur", { date: x.porteurDepuis }));
    if (x.feuxPortes > 0) lignes.push(t("feu.portes", { n: x.feuxPortes }));
    if (x.lecons.length > 0) lignes.push(t("feu.lecons", { liste: x.lecons.join(t("commun.virgule")) }));
    lignes.forEach((l, i) => {
      if (i > 0) p.append(el("br"));
      p.append(l);
    });
    corps.append(p);
    if (x.caches.length > 0) {
      const ul = el("ul", "liste");
      for (const c of x.caches) ul.append(el("li", undefined, c));
      corps.append(el("h4", undefined, t("feu.caches")), ul);
    }
  }

  /** Section « Rencontres et ouvrages » : sa visite ou son rassemblement, d'où il vient, ce que les siens disent qu'il fait pour eux. */
  private rencontres(corps: HTMLElement, x: Fiche["rencontres"] | null): void {
    if (!x) return;
    corps.append(el("h3", undefined, t("fiche.rencontres")));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (x.visite) lignes.push(t("rencontres.visite", { texte: x.visite }));
    if (x.rassemblement) lignes.push(t("rencontres.rassemblement", { texte: x.rassemblement }));
    if (x.venuDe) lignes.push(x.venuDe.charAt(0).toUpperCase() + x.venuDe.slice(1));
    if (x.metier) lignes.push(x.metierDepuis ? t("rencontres.metierDepuis", { metier: x.metier, date: x.metierDepuis }) : t("rencontres.metier", { metier: x.metier }));
    const faits: string[] = [];
    if (x.servisTaille > 0) faits.push(t("rencontres.tailles", { n: x.servisTaille }));
    if (x.servisAssemblage > 0) faits.push(t("rencontres.assembles", { n: x.servisAssemblage }));
    if (x.servisPeau > 0) faits.push(t("rencontres.peaux", { n: x.servisPeau }));
    if (faits.length > 0) lignes.push(t("rencontres.ouvrages", { liste: faits.join(t("commun.virgule")) }));
    if (x.attend) lignes.push(x.attend.charAt(0).toUpperCase() + x.attend.slice(1));
    lignes.forEach((l, i) => {
      if (i > 0) p.append(el("br"));
      p.append(l);
    });
    corps.append(p);
  }

  /** Section « Foi » : sa religion, sa ferveur, son rôle (et d'où vient sa parole, que seule l'Entité sait), ses prières. */
  private foi(corps: HTMLElement, f: Fiche["foi"]): void {
    if (!f || f.religion === 0 && f.prieres.length === 0) return;
    corps.append(el("h3", undefined, t("fiche.foi")));
    const p = el("p", "foi");
    if (f.nomReligion) p.append(t("foi.religion", { nom: f.nomReligion, v: f.ferveur }));
    else p.append(t("foi.aucune"));
    if (f.role) p.append(el("br"), t("foi.role", { role: f.role }) + (f.source ? ` (${f.source})` : "") + (f.miracles > 0 ? t("foi.miracles", { n: f.miracles }) : ""));
    corps.append(p);
    const bilan = ligneBilan(f.bilan, false);
    if (bilan) corps.append(bilan);
    if (f.prieres.length > 0) {
      const ul = el("ul", "liste prieres");
      for (const x of f.prieres) ul.append(lignePriere(x, (id) => void this.ouvrir(id)));
      corps.append(ul);
    }
  }

  /** Section « Langage » : son nom dans sa langue, sa parole, ses mots (avec leur traduction) et les langues qu'il comprend. */
  private langage(corps: HTMLElement, l: Langage | null): void {
    if (!l) return;
    corps.append(el("h3", undefined, t("fiche.langage")));
    const p = el("p", "langage");
    if (l.nom) {
      p.append(l.sens ? t("langage.nomSens", { nom: l.nom, sens: l.sens }) : t("langage.nom", { nom: l.nom }));
      if (l.nomPar) {
        p.append(t("langage.donnePar"), this.lien(l.nommePar, l.nomPar ?? undefined));
      }
      if (l.nommeLe) p.append(" " + t("langage.le", { date: l.nommeLe }));
      p.append(" ");
      if (l.nomEvenement > 0) p.append(boutonCause(p, l.nomEvenement, t("commun.comment")));
      p.append(el("br"));
    }
    p.append(t("langage.parle", { stade: l.stade, mots: t("langage.mots", { n: l.nombreDeMots }) }));
    if (l.ordre) p.append(el("br"), t("langage.ordre", { ordre: l.ordre }));
    if (l.comprises.length > 0) p.append(el("br"), t("langage.comprend", { liste: l.comprises.map((c) => t("langage.langueDe", { nom: c.nom, nomMin: `${c.nom.charAt(0).toLowerCase()}${c.nom.slice(1)}`, v: c.pourcent })).join(t("commun.virgule")) }));
    corps.append(p);
    if (l.mots.length > 0) {
      const ul = el("ul", "liste mots");
      for (const m of l.mots) {
        const li = el("li");
        li.append(el("span", "mot", m.forme), " " + t("commun.guillemets", { texte: m.sens }) + " ", el("span", "discret", m.nomAuteur ? t("langage.forceAuteur", { v: m.force, auteur: m.nomAuteur }) : t("langage.force", { v: m.force })));
        ul.append(li);
      }
      corps.append(ul);
    }
  }

  private formulaireSouffle(): HTMLDetailsElement {
    if (this.souffle) return this.souffle;
    const bloc = el("details", "entite-bloc");
    bloc.append(el("summary", "entite", t("souffle.titre")));
    const geste = el("select");
    const a = el("select");
    const b = el("select");
    const libelleA = el("span");
    const libelleB = el("span");
    const bouton = el("button", undefined, t("souffle.souffler"));
    bouton.type = "button";
    let formes: FormeGeste[] = [];
    let plantes: Choix[] = [];
    const remplir = (select: HTMLSelectElement, choix: Choix[]) => {
      const avant = select.value;
      select.replaceChildren(...choix.map((c) => {
        const o = el("option", undefined, c.libelle);
        o.value = c.cle;
        return o;
      }));
      if (choix.some((c) => c.cle === avant)) select.value = avant;
    };
    const ligne = (libelle: HTMLElement, champ: HTMLElement) => {
      const l = el("label", "ligne");
      l.append(libelle, " ", champ);
      return l;
    };
    const ligneA = ligne(libelleA, a);
    const ligneB = ligne(libelleB, b);
    // Chaque geste propose ses propres matières, reliées par son mot (« frapper … contre … », « racler … avec … ») ;
    // manger ou appliquer propose les plantes qui poussent là où se trouve l'humain.
    const accorder = () => {
      const f = formes.find((x) => x.cle === geste.value);
      if (!f) return;
      const plante = f.a.length === 1 && f.a[0].cle === "Plante";
      libelleA.textContent = plante ? t("souffle.quellePlante") : t("souffle.quoi");
      remplir(a, plante ? plantes : f.a);
      ligneA.hidden = plante && plantes.length === 0;
      libelleB.textContent = f.liaison ? f.liaison.charAt(0).toUpperCase() + f.liaison.slice(1) : "";
      remplir(b, f.b);
      ligneB.hidden = f.b.length === 0;
      bouton.disabled = plante && plantes.length === 0;
      this.resultatSouffle.textContent = plante && plantes.length === 0 ? t("souffle.aucunePlante") : "";
    };
    const charger = async () => {
      if (!this.id) return;
      try {
        const c = await connexion.invoke<{ gestes: FormeGeste[]; plantes: Choix[] }>("ChoixIntuition", this.id);
        formes = c.gestes;
        plantes = c.plantes;
        remplir(geste, formes.map((f) => ({ cle: f.cle, libelle: f.verbe })));
        accorder();
      } catch {
        this.resultatSouffle.textContent = t("commun.pasDeReponse");
      }
    };
    this.rechargerSouffle = () => void charger();
    geste.addEventListener("change", accorder);
    // Les plantes dépendent de l'endroit où se trouve l'humain : on les relit à chaque ouverture du formulaire.
    bloc.addEventListener("toggle", () => {
      if (bloc.open) void charger();
    });
    bouton.addEventListener("click", async () => {
      const f = formes.find((x) => x.cle === geste.value);
      if (!this.id || !f) return;
      const plante = f.a.length === 1 && f.a[0].cle === "Plante";
      bouton.disabled = true;
      try {
        this.resultatSouffle.textContent = await connexion.invoke<string>("Souffler", {
          humain: this.id,
          geste: geste.value,
          a: plante ? "Plante" : a.value,
          b: f.b.length === 0 ? "Aucune" : b.value,
          espece: plante ? Number(a.value) : 0,
        });
      } catch {
        this.resultatSouffle.textContent = t("souffle.echec");
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(ligne(el("span", undefined, t("agir.geste")), geste), ligneA, ligneB, bouton, this.resultatSouffle,
      el("p", "discret", t("souffle.aide")));
    this.souffle = bloc;
    return bloc;
  }

  /** Vrai si l'utilisateur est en train de remplir le formulaire de l'Entité (un menu ouvert ne doit pas être redessiné). */
  private saisieEnCours(): boolean {
    return !!this.souffle && (this.souffle.contains(document.activeElement) || this.souffle.matches(":hover")) || !!this.entite?.enCours || !!this.geste?.enCours;
  }

  private traits(titre: string, traits: Trait[], max: number): HTMLElement {
    const bloc = el("div");
    bloc.append(el("h3", undefined, titre));
    for (const trait of traits) {
      const ligne = el("div", "jauge");
      ligne.title = trait.aide;
      ligne.append(el("span", "nom", trait.nom));
      const piste = el("span", "piste");
      const plein = el("span", "plein");
      plein.style.width = `${Math.round((Math.max(0, Math.min(max, trait.valeur)) / max) * 100)}%`;
      piste.append(plein);
      ligne.append(piste, el("span", "valeur", max === 1 ? nombre(trait.valeur, 2) : String(Math.round(trait.valeur))), el("span", "mots", trait.mots));
      bloc.append(ligne);
    }
    return bloc;
  }

  private jauges(titre: string, valeurs: Record<string, number>, max: number): HTMLElement {
    const bloc = el("div");
    bloc.append(el("h3", undefined, titre));
    for (const [nom, v] of Object.entries(valeurs)) {
      const ligne = el("div", "jauge");
      ligne.append(el("span", "nom", nom));
      const barre = el("span", "piste");
      const plein = el("span", "plein");
      plein.style.width = `${Math.round((Math.max(0, Math.min(max, v)) / max) * 100)}%`;
      barre.append(plein);
      ligne.append(barre, el("span", "valeur", max === 1 ? nombre(v, 2) : String(Math.round(v))));
      bloc.append(ligne);
    }
    return bloc;
  }
}

/** Retrouve un humain d'après son nom, et ouvre sa fiche : fournis par la page au démarrage. */
let idDuNom: (nom: string) => number | undefined = () => undefined;
let ouvrirHumain: (id: number) => void = () => {};
let nomDeId: (id: number) => string = () => "?";

export function definirLiensNoms(resoudre: (nom: string) => number | undefined, ouvrir: (id: number) => void,
  nomDe?: (id: number) => string): void {
  idDuNom = resoudre;
  ouvrirHumain = ouvrir;
  if (nomDe) nomDeId = nomDe;
}

/** Un nombre signé, à la française : « +2 », « −30 », « +0,5 ». */
export function signe(v: number): string {
  const n = nombre(Math.abs(v), 1);
  return v > 0 ? `+${n}` : v < 0 ? `−${n}` : "0";
}

/** Le texte d'un événement, où chaque nom d'humain devient un lien vers sa fiche. */
function texteAvecLiens(e: Evenement): (string | HTMLElement)[] {
  const morceaux: (string | HTMLElement)[] = [];
  let debut = 0;
  for (const m of e.texte.matchAll(/\p{Lu}[\p{L}-]*/gu)) {
    const id = idDuNom(m[0]);
    if (id === undefined) continue;
    morceaux.push(e.texte.slice(debut, m.index));
    const b = el("button", "nom-humain", m[0]);
    b.type = "button";
    b.title = t("fiche.ouvrirDe", { nom: m[0] });
    b.addEventListener("click", (ev) => {
      ev.stopPropagation();
      ouvrirHumain(id);
    });
    morceaux.push(b);
    debut = m.index + m[0].length;
  }
  morceaux.push(e.texte.slice(debut));
  return morceaux;
}

/** Ligne d'événement : date, texte (noms cliquables), et la marque d'une intervention de l'Entité. */
export function ligneEvenement(e: Evenement): HTMLElement {
  const li = el("li", `evenement ${e.type}${e.significatif ? " significatif" : ""}`);
  if (e.ancien) li.title = t("evenement.ancien");
  li.append(el("span", "date", e.date));
  if (e.significatif) li.append(el("span", "etoile", " ★"));
  li.append(" ", ...texteAvecLiens(e));
  if (e.origine === "entite") li.append(el("span", "entite", " " + t("evenement.entite")));
  const karmas: string[] = [];
  if (e.karma) karmas.push(`${nomDeId(e.humain)} ${signe(e.karma)}`);
  if (e.karmaAutre) karmas.push(`${nomDeId(e.autre)} ${signe(e.karmaAutre)}`);
  if (karmas.length > 0) {
    const k = el("span", "karma", " " + t("evenement.karma", { liste: karmas.join(t("commun.virgule")) }));
    k.title = t("evenement.karmaAide");
    li.append(k);
  }
  if (e.cause > 0) li.append(" ", boutonCause(li, e.cause, t("commun.pourquoi")));
  return li;
}

/**
 * Causes déjà dépliées, gardées d'un rafraîchissement à l'autre : la fiche et le journal se redessinent toutes les
 * deux secondes, et la chaîne ouverte doit rester affichée tant qu'on la lit.
 */
const causesOuvertes = new Map<number, Evenement | null>();

/** Oublie les chaînes dépliées (quand on passe à une autre fiche). */
export function replierCauses(): void {
  causesOuvertes.clear();
}

/**
 * Bouton qui va chercher l'événement à l'origine d'un autre et l'affiche juste en dessous, avec à son tour
 * son propre bouton : on remonte ainsi la chaîne des causes aussi loin qu'elle va. Une cause déjà dépliée
 * est redessinée telle quelle.
 */
export function boutonCause(parent: HTMLElement, causeId: number, libelle: string): HTMLElement {
  if (causesOuvertes.has(causeId)) return blocCause(parent, causeId, causesOuvertes.get(causeId) ?? null, libelle);
  const bouton = el("button", "cause", libelle);
  bouton.type = "button";
  bouton.title = t("cause.aide");
  bouton.addEventListener("click", async (ev) => {
    ev.stopPropagation();
    bouton.disabled = true;
    const cause = await connexion.invoke<Evenement | null>("Evenement", causeId).catch(() => null);
    causesOuvertes.set(causeId, cause);
    bouton.remove();
    parent.append(blocCause(parent, causeId, cause, libelle));
  });
  return bouton;
}

function blocCause(parent: HTMLElement, causeId: number, cause: Evenement | null, libelle: string): HTMLElement {
  const bloc = el("div", "chaine-cause");
  if (cause) {
    bloc.append("↳ ", el("span", "date", cause.date), ` ${cause.texte}`);
    if (cause.cause > 0) bloc.append(" ", boutonCause(bloc, cause.cause, t("commun.pourquoi")));
  } else {
    bloc.append("↳ " + t("cause.introuvable"));
  }
  const replier = el("button", "cause", t("cause.replier"));
  replier.type = "button";
  replier.addEventListener("click", (ev) => {
    ev.stopPropagation();
    causesOuvertes.delete(causeId);
    bloc.replaceWith(boutonCause(parent, causeId, libelle));
  });
  bloc.append(" ", replier);
  return bloc;
}
