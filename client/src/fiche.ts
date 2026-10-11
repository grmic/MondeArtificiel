import { connexion } from "./connexion";
import type { HumainVue } from "./carte";

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
  ["peur", "Peurs"],
  ["espoir", "Espoirs"],
  ["joie", "Joies"],
  ["peine", "Peines"],
  ["opinion", "Ce qu'il pense des autres"],
  ["image-de-soi", "Ce qu'il pense de lui-même"],
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

/** Fiche d'un humain : qui il est, ce qu'il pense, ce dont il se souvient et ce qu'il a vécu. */
export class PanneauFiche {
  private id = 0;
  private minuteur?: number;

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
    if (id !== this.id) replierCauses();
    this.id = id;
    this.surChoix(id);
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    // Tant qu'elle est ouverte, la fiche suit l'humain (deux fois par seconde au plus, sans file d'attente).
    this.minuteur = window.setInterval(() => void this.rafraichir(), 2000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.id = 0;
    this.racine.hidden = true;
    this.surChoix(0);
  }

  private async rafraichir(): Promise<void> {
    if (!this.id) return;
    const id = this.id;
    const fiche = await connexion.invoke<Fiche | null>("Fiche", id).catch(() => null);
    if (!fiche || id !== this.id) return;
    this.dessiner(fiche);
  }

  /** Attraits, orientation et exclusions (exil, reniement), avec leur raison. */
  private attraits(corps: HTMLElement, f: Fiche, e: string): void {
    const a = f.attraits;
    corps.append(el("h3", undefined, "Attraits et orientation"));
    const p = el("p", "attraits");
    p.title = "Beauté relative : selon ce que sa communauté trouve beau. Attractivité : beauté, jeunesse, allure et vigueur.";
    p.append(`Beauté relative ${a.beaute}/100 · attractivité ${a.attractivite}/100`, el("br"),
      `Idéal de beauté de son groupe : ${a.ideal}`, el("br"),
      `Orientation : ${a.orientation} (attirance vers les hommes ${a.versHommes}/100, vers les femmes ${a.versFemmes}/100)`);
    if (a.conscience) p.append(` ; ${a.conscience}`);
    corps.append(p);
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
    tete.append(el("h2", undefined, h.nom));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = "Fermer la fiche";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    r.append(tete);

    const corps = el("div", "corps");
    const age = h.age < 2 ? `${Math.round(h.age * 12)} mois` : `${Math.floor(h.age)} ans`;
    const etat = h.vivant ? `${h.activite}${h.malade ? " · malade" : ""}${h.enceinte ? " · enceinte" : ""}` : `mort ${h.causeDeMort ?? ""}`;
    corps.append(el("p", "resume", `${h.sexe === "femme" ? "Femme" : "Homme"}, ${age} · ${etat}`));
    if (h.pensee) corps.append(el("p", "pensee", `« ${h.pensee} »`));
    if (f.ame) {
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
        corps.append(el("h3", undefined, "Rancunes"));
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
    corps.append(this.traits("Intelligence (QI, moyenne 100)", f.intelligence, 160));
    const bio = el("p", "discret", Object.entries(f.biologie).map(([k, v]) => `${k} ${v}`).join(" · "));
    corps.append(el("h3", undefined, "Biologie"), bio);

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

    r.append(corps);
    corps.scrollTop = defilement;
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

/** Ligne d'événement : date, texte, et la marque d'une intervention de l'Entité. */
export function ligneEvenement(e: Evenement): HTMLElement {
  const li = el("li", `evenement ${e.type}${e.significatif ? " significatif" : ""}`);
  li.append(el("span", "date", e.date));
  if (e.significatif) li.append(el("span", "etoile", " ★"));
  li.append(` ${e.texte}`);
  if (e.origine === "entite") li.append(el("span", "entite", " ✦ soufflé par l'Entité"));
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
