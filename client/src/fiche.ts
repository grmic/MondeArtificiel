import { boutonSuivre } from "./suivis";
import { ligneBilan, lignePriere, type BilanPrieres, type Priere } from "./cultes";
import { connexion } from "./connexion";
import type { HumainVue } from "./carte";
import { FormulaireEntite } from "./entite";
import { FormulaireGeste } from "./gestes";

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
}

/** Rubriques du jardin secret, dans l'ordre d'affichage. */
const RUBRIQUES: [string, string][] = [
  ["intention", "Intentions"],
  ["amour", "Amour"],
  ["desir", "Désir"],
  ["jalousie", "Jalousie"],
  ["peur", "Peurs"],
  ["espoir", "Espoirs"],
  ["joie", "Joies"],
  ["peine", "Peines"],
  ["manque", "Manque et nostalgie"],
  ["opinion", "Ce qu'il pense des autres"],
  ["image-de-soi", "Ce qu'il pense de lui-même"],
  ["fierte", "Fierté"],
  ["culpabilite", "Culpabilité"],
  ["honte", "Honte"],
  ["envie", "Envie"],
  ["souvenir", "Souvenirs"],
  ["reve", "Rêves"],
  ["emerveillement", "Émerveillement"],
  ["ennui", "Ennui"],
  ["croyance", "Croyances"],
  ["question", "Questions"],
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
  pensees: { date: string; texte: string }[];
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
  return new Set(["Besoins", "Humeur"]);
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
  private attraits(corps: HTMLElement, f: Fiche, e: string): void {
    const a = f.attraits;
    corps.append(el("h3", undefined, "Attraits et orientation"));
    const p = el("p", "attraits");
    if (a.impubere) {
      p.append("Trop jeune : l'attirance et l'orientation s'éveilleront à la puberté.");
      corps.append(p);
    } else {
      p.title = "Beauté relative : selon ce que sa communauté trouve beau. Attractivité : beauté, jeunesse, allure et vigueur.";
      p.append(`Beauté relative ${a.beaute}/100 · attractivité ${a.attractivite}/100`, el("br"),
        `Idéal de beauté de son groupe : ${a.ideal}`, el("br"),
        `Orientation : ${a.orientation} (attirance vers les hommes ${a.versHommes}/100, vers les femmes ${a.versFemmes}/100)`);
      if (a.conscience) p.append(` ; ${a.conscience}`);
      corps.append(p);
    }
    if (a.exclusions.length === 0) return;
    const ul = el("ul", "liste exclusions");
    for (const x of a.exclusions) {
      const li = el("li", x.enCours ? "alerte" : "discret");
      const duree = x.jusqua ? `fin prévue : ${x.jusqua}` : "pour toujours";
      const qui = x.type === "renié" ? `Renié${e} par ${x.nomPar}` : x.type === "exilé" ? `Chassé${e} de la ${x.nomPar.charAt(0).toLowerCase()}${x.nomPar.slice(1)}` : `Parti${e} de la ${x.nomPar.charAt(0).toLowerCase()}${x.nomPar.slice(1)}`;
      li.append(`${qui} ${x.raison} (${x.le} ; ${duree})${x.enCours ? "" : ", c'est fini"} `);
      if (x.evenement > 0) li.append(boutonCause(li, x.evenement, "ce qui s'est passé"));
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
      nom.title = `Son nom dans la langue des siens${f.langage.sens ? ` : « ${f.langage.sens} »` : ""}`;
      titre.append(nom);
    }
    tete.append(titre, boutonSuivre("humain", this.id, h.nom));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = "Fermer la fiche";
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
    const age = h.age < 2 ? `${Math.round(h.age * 12)} mois` : `${Math.floor(h.age)} ans`;
    const etat = h.vivant ? `${h.activite}${h.malade ? " · malade" : ""}${h.enceinte ? " · enceinte" : ""}` : `mort ${h.causeDeMort ?? ""}`;
    corps.append(el("p", "resume", `${h.sexe === "femme" ? "Femme" : "Homme"}, ${age} · ${etat}`));
    if (h.pensee) corps.append(el("p", "pensee", `« ${h.pensee} »`));
    if (f.ame) {
      corps.append(el("h3", undefined, "Humeur"));
      const ame = el("p", "ame");
      ame.append(`Humeur : ${f.ame.etat} (${f.ame.humeur}/100)`);
      if (f.ame.chagrin > 0) {
        ame.append(` · chagrin ${f.ame.chagrin}/100`);
        if (f.ame.deuilDe) ame.append(" (", this.lien(f.ame.deuilDe), ")");
      }
      if (f.ame.colere >= 10) ame.append(` · colère ${f.ame.colere}/100`);
      if (f.ame.deprimeDepuis) {
        ame.append(el("br"), `Déprimé${h.sexe === "femme" ? "e" : ""} depuis ${f.ame.deprimeDepuis}`);
        if (f.ame.depressionEvenement > 0) ame.append(" ", boutonCause(ame, f.ame.depressionEvenement, "pourquoi ?"));
      }
      if (f.ame.raisons.length > 0) ame.append(el("br"), `Ce qui pèse sur son humeur : ${f.ame.raisons.join(" ; ")}.`);
      corps.append(ame);
      if (f.ame.rancunes.length > 0) {
        corps.append(el("h4", undefined, "Rancunes"));
        const rancunes = el("ul", "liste");
        for (const g of f.ame.rancunes) {
          const li = el("li");
          li.append(this.lien(g.id, g.nom), ` — ${g.motif} (${g.intensite}/100) `);
          if (g.evenement > 0) li.append(boutonCause(li, g.evenement, "ce qui s'est passé"));
          rancunes.append(li);
        }
        corps.append(rancunes);
      }
    }
    const ep = f.epreuves;
    if (ep.maladie || ep.peur >= 5 || ep.immunites.length > 0) {
      const p = el("p", "epreuves");
      if (ep.maladie) {
        p.append(`Porte ${ep.maladie}${h.contagieux ? ", contagieux" : ""}${ep.maladieDepuis ? ` (depuis ${ep.maladieDepuis})` : ""} `);
        if (ep.maladieEvenement > 0) p.append(boutonCause(p, ep.maladieEvenement, "comment ?"), " ");
        p.append(el("br"));
      }
      if (ep.peur >= 5 && ep.peurDe) {
        p.append(`Peur : ${ep.peur}/100, hanté${h.sexe === "femme" ? "e" : ""} par ${ep.peurDe} `);
        if (ep.peurEvenement > 0) p.append(boutonCause(p, ep.peurEvenement, "pourquoi ?"));
        p.append(el("br"));
      }
      if (ep.immunites.length > 0) p.append(`Immunisé${h.sexe === "femme" ? "e" : ""} contre : ${ep.immunites.join(", ")}`);
      corps.append(p);
    }
    this.attraits(corps, f, h.sexe === "femme" ? "e" : "");
    if (f.communaute) {
      const c = el("p", "communaute");
      const bouton = el("button", "lien", f.communaute.nom);
      bouton.type = "button";
      const id = f.communaute.id;
      bouton.addEventListener("click", () => this.ouvrirCommunaute(id));
      c.append("Vit dans la ", bouton);
      corps.append(c);
    }

    // Famille
    const famille = el("p", "famille");
    const ajouter = (libelle: string, ids: number[]) => {
      if (ids.length === 0) return;
      famille.append(`${libelle} : `);
      ids.forEach((id, i) => {
        if (i > 0) famille.append(", ");
        famille.append(this.lien(id));
      });
      famille.append(el("br"));
    };
    if (h.mere) ajouter("Parents", [h.mere, h.pere].filter(Boolean));
    else famille.append(`Couple de départ n° ${h.emplacement} (créé par l'Entité)`, el("br"));
    ajouter("Compagnon", h.partenaire ? [h.partenaire] : []);
    ajouter("Enfants", f.enfants);
    corps.append(famille);

    if (h.vivant) {
      corps.append(this.jauges("Besoins", { faim: h.faim, soif: h.soif, fatigue: h.fatigue, froid: h.froid, santé: h.sante, réserves: h.reserves }, 100));
    }
    corps.append(this.traits("Caractère", f.caractere, 1));
    if (f.portrait.length > 0) corps.append(el("p", "portrait", f.portrait.join(" ")));
    corps.append(this.traits("Cœur et conscience", f.coeur, 1));
    corps.append(this.traits("Morale (fondements)", f.morale, 1));
    corps.append(this.traits("Intelligence (QI, moyenne 100)", f.intelligence, 160));
    const bio = el("p", "discret", Object.entries(f.biologie).map(([k, v]) => `${k} ${v}`).join(" · "));
    corps.append(el("h3", undefined, "Biologie"), bio);

    this.savoirs(corps, f.savoirs, h.sexe === "femme" ? "e" : "");
    corps.append(el("h3", undefined, "Regard de l'Entité"));
    const regard = el("p", "regard");
    regard.append(`Karma : ${signe(f.regard.karma)} (${f.regard.karmaMots})`, el("br"),
      `Croyance en l'invisible : ${f.regard.croyance}/100 (${f.regard.croyanceMots})`);
    if (f.regard.dernierSigne) regard.append(el("br"), `Dernier signe de l'invisible : ${f.regard.dernierSigne}`);
    regard.title = "Invisible aux humains. Les actes qui ont changé son karma sont marqués ☯ dans son histoire.";
    corps.append(regard);
    corps.append(el("h3", undefined, "Regard sur l'invisible"));
    const invisible = el("p", "regard");
    invisible.append(`Croyance : ${f.regard.croyance}/100 (${f.regard.croyanceMots})`, el("br"),
      `Amour : ${f.regard.amour}/100 (${f.regard.amourMots})`, el("br"),
      `Crainte : ${f.regard.crainte}/100 (${f.regard.crainteMots})`, el("br"),
      `Révolte : ${f.regard.revolte}/100 (${f.regard.revolteMots})`);
    if (f.regard.dernierGeste) invisible.append(el("br"), `Dernier signe vu : ${f.regard.dernierGeste}`);
    invisible.title = "Ce qu'il éprouve envers l'invisible : l'amour fait obéir de bon gré, la crainte à contrecœur et rend un peu plus honnête, la révolte fait refuser et cesser de prier.";
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
      corps.append(el("h3", undefined, "Paroles de l'Entité"));
      const ul = el("ul", "liste");
      for (const x of f.paroles.slice(0, 20)) {
        const li = el("li");
        li.append(el("span", "entite", "✦ "), `${x.date}, ${x.mode} : ${x.texte} `, el("span", "discret", `(${x.reponse})`), " ");
        if (x.evenement > 0) li.append(boutonCause(li, x.evenement, "ce qui s'est passé"));
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

    corps.append(el("h3", undefined, "Liens"));
    const liens = el("ul", "liste");
    for (const l of f.liens.slice(0, 15)) {
      const li = el("li", `lien-${l.statut.replace(/\s/g, "-")}`);
      li.append(this.lien(l.id, l.nom), ` ${l.vivant ? "" : "✝ "}— ${l.statut} (affection ${l.affection}, familiarité ${l.familiarite})`);
      liens.append(li);
    }
    if (f.liens.length === 0) liens.append(el("li", "discret", "Ne connaît encore personne."));
    else if (f.liens.length > 15) liens.append(el("li", "discret", `… et ${f.liens.length - 15} autres.`));
    corps.append(liens);

    corps.append(el("h3", undefined, "Pensées récentes"));
    const pensees = el("ul", "liste");
    for (const p of f.pensees) {
      const li = el("li");
      li.append(el("span", "date", p.date), ` ${p.texte}`);
      pensees.append(li);
    }
    if (f.pensees.length === 0) pensees.append(el("li", "discret", "Aucune pour l'instant."));
    corps.append(pensees);

    corps.append(el("h3", undefined, "Jardin secret"));
    if (f.jardin.length === 0) corps.append(el("p", "discret", "Rien de confié pour l'instant."));
    for (const [cle, titre] of RUBRIQUES) {
      const choses = f.jardin.filter((x) => x.type === cle);
      if (choses.length === 0) continue;
      corps.append(el("h4", undefined, titre));
      const ul = el("ul", "liste jardin");
      for (const x of choses) {
        const li = el("li");
        li.title = `Force ${x.intensite} sur 100, depuis le ${x.date}`;
        li.append(el("span", "date", x.date), ` ${x.texte} `, el("span", "discret", `(${x.intensite})`));
        if (x.sujet > 0 && x.nomSujet) li.append(" ", this.lien(x.sujet, "→ " + x.nomSujet));
        if (x.cause > 0) li.append(" ", boutonCause(li, x.cause, "pourquoi ?"));
        ul.append(li);
      }
      corps.append(ul);
    }

    corps.append(el("h3", undefined, "Mémoire"));
    const lieux = el("ul", "liste");
    for (const l of f.lieux) {
      lieux.append(el("li", undefined, `${l.type} en (${l.x}, ${l.y}), ${l.visites > 1 ? `${l.visites} visites, ` : ""}vu ${l.vuLe}`));
    }
    if (f.lieux.length === 0) lieux.append(el("li", "discret", "Ne se souvient d'aucun lieu."));
    corps.append(lieux);

    corps.append(el("h3", undefined, "Histoire"));
    const histoire = el("ul", "liste");
    for (const e of f.histoire) histoire.append(ligneEvenement(e));
    if (f.histoire.length === 0) histoire.append(el("li", "discret", "Rien encore."));
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
    for (const [texte, ouvrir] of [["tout ouvrir", true], ["tout fermer", false]] as const) {
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
  private savoirs(corps: HTMLElement, s: Savoirs, e: string): void {
    corps.append(el("h3", undefined, "Savoirs et objets"));
    const p = el("p", "discret");
    p.append(s.vetement ? `Vêtu${e} de ${s.vetement}` : `Nu${e}`);
    if (s.objets.length > 0) p.append(` · porte ${s.objets.join(", ")}`);
    if (s.nage > 0) p.append(` · nage ${s.nage}/100`);
    corps.append(p);
    const ul = el("ul", "liste");
    for (const x of s.savoirs) {
      const li = el("li", x.confiance < 40 ? "discret" : undefined);
      li.title = `${x.essais} essai${x.essais > 1 ? "s" : ""}, appris le ${x.appriLe}`;
      li.append(`${x.texte} `, el("span", "discret", `(confiance ${x.confiance} %, ${x.origine}`));
      if (x.maitre > 0 && x.nomMaitre) li.append(el("span", "discret", " de "), this.lien(x.maitre, x.nomMaitre));
      li.append(el("span", "discret", ")"));
      if (x.eleves && x.eleves.length > 0) {
        li.append(el("span", "discret", " · l'a appris à "));
        x.eleves.slice(0, 6).forEach((o, i) => {
          if (i > 0) li.append(", ");
          li.append(this.lien(o.id, o.nom));
        });
        if (x.eleves.length > 6) li.append(el("span", "discret", ` et ${x.eleves.length - 6} autres`));
      }
      if (x.origine.includes("Entité")) li.append(el("span", "entite", " ✦"));
      if (x.evenement > 0) li.append(" ", boutonCause(li, x.evenement, "comment ?"));
      ul.append(li);
    }
    if (s.savoirs.length === 0) ul.append(el("li", "discret", "Ne sait encore rien faire de particulier."));
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
    corps.append(el("h3", undefined, "Médecine"));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (m.role) lignes.push(`Rôle : ${m.role} (renommée ${m.renommee}/100)`);
    if (m.soignes > 0) lignes.push(`Malades et blessés soignés : ${m.soignes} ; guéris : ${m.gueris} ; perdus : ${m.perdus}`);
    if (m.accouchementsAides > 0) lignes.push(`Accouchements aidés : ${m.accouchementsAides}`);
    if (m.guerisseurDepuis) lignes.push(`Tenu pour guérisseur depuis le ${m.guerisseurDepuis}`);
    if (m.chamanDepuis) lignes.push(`Première transe le ${m.chamanDepuis}`);
    if (m.charlatanDepuis) lignes.push(`Prétend guérir depuis le ${m.charlatanDepuis}`);
    if (m.desavoueLe) lignes.push(`Désavoué ou démasqué le ${m.desavoueLe}`);
    if (m.plaie) lignes.push(`Plaie : ${m.plaie}`);
    if (m.malediction) lignes.push(m.malediction.charAt(0).toUpperCase() + m.malediction.slice(1));
    lignes.forEach((l, i) => {
      if (i > 0) p.append(el("br"));
      p.append(l);
    });
    p.title = "Les guérisons comptées sont celles que les siens lui attribuent, méritées ou non.";
    corps.append(p);
  }

  /** Section « Feu et réserves » : le feu qu'il porte, s'il est le porteur du feu des siens, ses caches, ce qu'il sait du feu. */
  private feu(corps: HTMLElement, x: Fiche["feu"] | null): void {
    if (!x || !x.porte && !x.porteurDepuis && x.feuxPortes === 0 && x.caches.length === 0 && x.lecons.length === 0) return;
    corps.append(el("h3", undefined, "Feu et réserves"));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (x.porte) lignes.push(`Porte le feu : ${x.porte}`);
    if (x.porteurDepuis) lignes.push(`Porteur du feu des siens depuis le ${x.porteurDepuis}`);
    if (x.feuxPortes > 0) lignes.push(`A porté le feu ${x.feuxPortes} fois jusqu'à un nouveau foyer`);
    if (x.lecons.length > 0) lignes.push(`Ce qu'il sait du feu : ${x.lecons.join(", ")}`);
    lignes.forEach((l, i) => {
      if (i > 0) p.append(el("br"));
      p.append(l);
    });
    corps.append(p);
    if (x.caches.length > 0) {
      const ul = el("ul", "liste");
      for (const c of x.caches) ul.append(el("li", undefined, c));
      corps.append(el("h4", undefined, "Ses caches"), ul);
    }
  }

  /** Section « Rencontres et ouvrages » : sa visite ou son rassemblement, d'où il vient, ce que les siens disent qu'il fait pour eux. */
  private rencontres(corps: HTMLElement, x: Fiche["rencontres"] | null): void {
    if (!x) return;
    corps.append(el("h3", undefined, "Rencontres et ouvrages"));
    const p = el("p", "medecine");
    const lignes: string[] = [];
    if (x.visite) lignes.push(`En visite : ${x.visite}`);
    if (x.rassemblement) lignes.push(`Rassemblement : ${x.rassemblement}`);
    if (x.venuDe) lignes.push(x.venuDe.charAt(0).toUpperCase() + x.venuDe.slice(1));
    if (x.metier) lignes.push(`Les siens disent : ${x.metier}${x.metierDepuis ? `, depuis le ${x.metierDepuis}` : ""}`);
    const faits: string[] = [];
    if (x.servisTaille > 0) faits.push(`${x.servisTaille} taillé${x.servisTaille > 1 ? "s" : ""}`);
    if (x.servisAssemblage > 0) faits.push(`${x.servisAssemblage} assemblé${x.servisAssemblage > 1 ? "s" : ""}`);
    if (x.servisPeau > 0) faits.push(`${x.servisPeau} vêtement${x.servisPeau > 1 ? "s" : ""} de peau`);
    if (faits.length > 0) lignes.push(`Ouvrages faits pour d'autres, à leur demande : ${faits.join(", ")}`);
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
    corps.append(el("h3", undefined, "Foi"));
    const p = el("p", "foi");
    if (f.nomReligion) p.append(`Religion : ${f.nomReligion}, ferveur ${f.ferveur}/100`);
    else p.append("Aucune religion");
    if (f.role) p.append(el("br"), `Rôle : ${f.role}${f.source ? ` (${f.source})` : ""}${f.miracles > 0 ? ` ; miracles attribués : ${f.miracles}` : ""}`);
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
    corps.append(el("h3", undefined, "Langage"));
    const p = el("p", "langage");
    if (l.nom) {
      p.append(`Nom dans sa langue : « ${l.nom} »${l.sens ? `, qui veut dire « ${l.sens} »` : ""}`);
      if (l.nomPar) {
        p.append(", donné par ", this.lien(l.nommePar, l.nomPar ?? undefined));
      }
      if (l.nommeLe) p.append(` le ${l.nommeLe}`);
      p.append(" ");
      if (l.nomEvenement > 0) p.append(boutonCause(p, l.nomEvenement, "comment ?"));
      p.append(el("br"));
    }
    p.append(`Parle par ${l.stade} ; connaît ${l.nombreDeMots} mot${l.nombreDeMots > 1 ? "s" : ""}.`);
    if (l.ordre) p.append(el("br"), `Ordre des mots : ${l.ordre}.`);
    if (l.comprises.length > 0) p.append(el("br"), `Comprend aussi : ${l.comprises.map((c) => `la langue de la ${c.nom.charAt(0).toLowerCase()}${c.nom.slice(1)} (${c.pourcent} %)`).join(", ")}.`);
    corps.append(p);
    if (l.mots.length > 0) {
      const ul = el("ul", "liste mots");
      for (const m of l.mots) {
        const li = el("li");
        li.append(el("span", "mot", m.forme), ` « ${m.sens} » `, el("span", "discret", `(${m.force}/100${m.nomAuteur ? `, forme due à ${m.nomAuteur}` : ""})`));
        ul.append(li);
      }
      corps.append(ul);
    }
  }

  private formulaireSouffle(): HTMLDetailsElement {
    if (this.souffle) return this.souffle;
    const bloc = el("details", "entite-bloc");
    bloc.append(el("summary", "entite", "✦ L'Entité souffle une idée…"));
    const geste = el("select");
    const a = el("select");
    const b = el("select");
    const libelleA = el("span");
    const libelleB = el("span");
    const bouton = el("button", undefined, "Souffler");
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
      libelleA.textContent = plante ? "Quelle plante" : "Quoi";
      remplir(a, plante ? plantes : f.a);
      ligneA.hidden = plante && plantes.length === 0;
      libelleB.textContent = f.liaison ? f.liaison.charAt(0).toUpperCase() + f.liaison.slice(1) : "";
      remplir(b, f.b);
      ligneB.hidden = f.b.length === 0;
      bouton.disabled = plante && plantes.length === 0;
      this.resultatSouffle.textContent = plante && plantes.length === 0 ? "Aucune plante ne pousse ici." : "";
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
        this.resultatSouffle.textContent = "Le serveur n'a pas répondu.";
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
        this.resultatSouffle.textContent = "L'intuition n'a pas pu être soufflée.";
      } finally {
        bouton.disabled = false;
      }
    });
    bloc.append(ligne(el("span", undefined, "Geste"), geste), ligneA, ligneB, bouton, this.resultatSouffle,
      el("p", "discret", "Il essaie avec ce qu'il a sous la main. Le journal marquera ce qu'il en tire comme venu de l'Entité."));
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
    for (const t of traits) {
      const ligne = el("div", "jauge");
      ligne.title = t.aide;
      ligne.append(el("span", "nom", t.nom));
      const piste = el("span", "piste");
      const plein = el("span", "plein");
      plein.style.width = `${Math.round((Math.max(0, Math.min(max, t.valeur)) / max) * 100)}%`;
      piste.append(plein);
      ligne.append(piste, el("span", "valeur", max === 1 ? t.valeur.toFixed(2) : String(Math.round(t.valeur))), el("span", "mots", t.mots));
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
      ligne.append(barre, el("span", "valeur", max === 1 ? v.toFixed(2) : String(Math.round(v))));
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
  const n = Math.abs(v).toLocaleString("fr-FR", { maximumFractionDigits: 1 });
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
    b.title = `Ouvrir la fiche de ${m[0]}`;
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
  li.append(el("span", "date", e.date));
  if (e.significatif) li.append(el("span", "etoile", " ★"));
  li.append(" ", ...texteAvecLiens(e));
  if (e.origine === "entite") li.append(el("span", "entite", " ✦ venu de l'Entité"));
  const karmas: string[] = [];
  if (e.karma) karmas.push(`${nomDeId(e.humain)} ${signe(e.karma)}`);
  if (e.karmaAutre) karmas.push(`${nomDeId(e.autre)} ${signe(e.karmaAutre)}`);
  if (karmas.length > 0) {
    const k = el("span", "karma", ` ☯ karma : ${karmas.join(", ")}`);
    k.title = "Regard de l'Entité sur cet acte (invisible aux humains)";
    li.append(k);
  }
  if (e.cause > 0) li.append(" ", boutonCause(li, e.cause, "pourquoi ?"));
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
  bouton.title = "Afficher l'événement qui en est la cause";
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
    if (cause.cause > 0) bloc.append(" ", boutonCause(bloc, cause.cause, "pourquoi ?"));
  } else {
    bloc.append("↳ cause introuvable");
  }
  const replier = el("button", "cause", "replier");
  replier.type = "button";
  replier.addEventListener("click", (ev) => {
    ev.stopPropagation();
    causesOuvertes.delete(causeId);
    bloc.replaceWith(boutonCause(parent, causeId, libelle));
  });
  bloc.append(" ", replier);
  return bloc;
}
