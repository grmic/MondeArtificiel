import { boutonSuivre } from "./suivis";
import { connexion } from "./connexion";
import { distanceDeCases, surfaceDeCases } from "./carte";
import { blocLangue, courbe, type Communaute, type Taille } from "./communautes";
import { ligneBilan, lignePriere, type BilanPrieres, type Priere } from "./cultes";
import { FormulaireEntite } from "./entite";
import { FormulaireGeste } from "./gestes";
import { boutonCause, el, ligneEvenement, replierCauses, signe, type Evenement } from "./fiche";
import { langue, nombre as ecrireNombre, t } from "./langue";

interface Nomme { id: number; nom: string; valeur: number }
interface Annee { an: number; naissances: number; morts: number; mortsMoins5: number; population: number | null; solde: number | null }
interface Indicateur { nom: string; communaute: number | null; monde: number | null; reference: string | null; unite: string; cas: number }
interface Aptitude { nom: string; moyenne: number; moyenneMonde: number; meilleur: Nomme | null }
interface Personne { id: number; nom: string; detail: string }
interface CatastropheResume { id: number; libelle: string; nom: string; origine: string; debut: string; morts: number }
interface Membre { id: number; nom: string; sexe: string; age: number; qi: number; karma: number; croyance: number; etape: string; role: string | null }

/** Une parole de l'Entité adressée au groupe, ou à un autre groupe contre celui-ci (visee), et ce qu'il en est advenu. */
interface Mandat {
  id: number; le: string; mode: string; phrase: string; cibleId: number; visee: boolean; communauteId: number; communaute: string | null;
  etat: string; code: string; reception: string; evenement: number;
}
interface Autorite { figure: number; nomFigure: string; autorite: string; crainte: string; emprise: number; tyran: boolean }

interface FicheCommunaute {
  base: Communaute;
  age: string;
  vivants: number;
  adultes: number;
  population: {
    pyramide: [number, number][]; annees: Annee[]; indicateurs: Indicateur[]; causes: string[]; mortsEnTout: number; tailles: Taille[];
  };
  sexes: { hommes: number[]; femmes: number[]; classes: string[]; orientations: number[]; couplesMemeSexe: number; moeurs: string | null };
  intelligence: { mesures: number; moyenne: number; mediane: number; repartition: number[]; plusHaut: Nomme | null; plusBas: Nomme | null; aptitudes: Aptitude[] };
  caractere: { traits: Aptitude[]; distinctifs: string[]; karmaMoyen: number | null; meilleurKarma: Nomme | null; pireKarma: Nomme | null };
  savoirs: {
    decouvertes: { date: string; savoir: string; humain: number; nom: string; origine: string; evenement: number; estimee: boolean }[];
    connus: string[]; appris: string[]; perdus: string[];
    /** Savoirs que seuls un ou deux membres détiennent (absent d'un serveur plus ancien). */
    fragiles?: { savoir: string; detenteurs: Nomme[] }[];
  };
  vie: { huttes: number; foyers: number; malades: Personne[]; mortsDeMaladie: number; catastrophes: CatastropheResume[]; captifs: Personne[];
    /** Le feu et les réserves (absent d'un serveur plus ancien). */
    feu?: {
      braises: number; saventAllumer: number; adultes: number; porteur: { id: number; nom: string; valeur: number } | null; joursDeVivres: number;
      reserves: string[]; idee: string | null; incendies: CatastropheResume[];
    } | null;
    /** Spécialistes, rassemblements, visites et membres venus d'ailleurs (absent d'un serveur plus ancien). */
    rencontres?: {
      specialistes: Personne[]; enVisite: Personne[]; visiteurs: Personne[]; venusDAilleurs: Personne[];
      rassemblements: { debut: string; autre: number; nomAutre: string; hote: boolean; jours: number; enCours: boolean; participants: number;
        cadeaux: number; appris: number; couples: number; evenement: number; bilan: number }[];
    } | null;
  };
  foi: {
    croyance: number | null; religions: string[]; porteurs: { id: number; nom: string; role: string; source: string | null; miracles: number }[];
    lieux: string[]; prieres: Priere[];
    /** Moyennes des adultes du regard sur l'invisible (0 à 100). */
    amour: number | null; crainte: number | null; revolte: number | null;
    /** Gestes de l'Entité qui pèsent encore sur elle, et ce qu'elle se raconte de ses gestes visibles. */
    effets: string[];
    souvenirs: { date: string; texte: string; bienfait: boolean; force: number; faute: string | null; evenement: number }[];
    /** Prières de ses membres actuels et prières collectives, depuis le début du monde. */
    bilan: BilanPrieres | null;
  };
  marquants: Evenement[];
  entite: Evenement[];
  membres: Membre[];
  autorite: Autorite | null;
  paroles: Mandat[] | null;
}

const ACTUALISATION_MS = 10000;
const K_TRAIT = "fc.trait";
const K_DISTINCTIFS = "fc.distinctifs";
const TRANCHES_QI = ["fc.qi.0", "fc.qi.1", "fc.qi.2", "fc.qi.3", "fc.qi.4"];

/** Sections ouvertes de la fiche Communauté (par leur clé, la même dans toutes les langues), retenues d'une fiche et d'une session à l'autre. */
const CLE_SECTIONS = "ficheCommunaute.sections";
let sectionsOuvertes = lireSections();

function lireSections(): Set<string> {
  try {
    const brut = localStorage.getItem(CLE_SECTIONS);
    const liste: unknown = brut ? JSON.parse(brut) : null;
    if (Array.isArray(liste)) return new Set(liste.filter((x): x is string => typeof x === "string").slice(0, 50));
  } catch {
    // Stockage indisponible ou valeur abîmée : on repart des sections par défaut.
  }
  return new Set(["population"]);
}

function enregistrerSections(): void {
  try {
    localStorage.setItem(CLE_SECTIONS, JSON.stringify([...sectionsOuvertes]));
  } catch {
    // Navigation privée ou stockage plein : le choix vaut pour cette session seulement.
  }
}

function minuscule(nom: string): string {
  return nom.charAt(0).toLowerCase() + nom.slice(1);
}

function nombre(v: number, decimales = 1): string {
  return ecrireNombre(v, decimales);
}

/**
 * Fiche d'une communauté : en tête l'essentiel, puis des sections déroulantes par thème (histoire, population, sexes et
 * orientations, intelligence, caractère, savoirs, guerres, terres et santé, foi, langue et arts, membres), et la section où
 * l'Entité agit sur le groupe.
 */
export class PanneauFicheCommunaute {
  private id = 0;
  private minuteur?: number;
  private entite: FormulaireEntite | null = null;
  private geste: FormulaireGeste | null = null;
  /** Tri de la liste des membres. */
  private tri: keyof Membre = "age";
  private triDescendant = true;
  private toutesLesAnnees = false;
  /** Fiche d'où l'on vient (humain ou communauté), pour le bouton « retour » : fourni par la page. */
  retour: { libelle: string; aller: () => void } | null = null;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirHumain: (id: number) => void,
    private readonly surligner: (ids: number[]) => void,
    private readonly suivre: (id: number, nom: string) => void,
    private readonly montrerTerres: (id: number) => void,
    private readonly centrer: (x: number, y: number) => void,
  ) {}

  get courant(): number {
    return this.id;
  }

  /** Nom de la communauté affichée (vide avant le premier dessin). */
  nom = "";

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  async ouvrir(id: number): Promise<void> {
    if (id !== this.id) {
      replierCauses();
      this.entite?.oublier();
      this.entite = null;
      this.geste = null;
    }
    this.id = id;
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.rafraichir(), ACTUALISATION_MS);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.id = 0;
    this.racine.hidden = true;
    this.surligner([]);
  }

  private async rafraichir(): Promise<void> {
    if (!this.id) return;
    const id = this.id;
    const fiche = await connexion.invoke<FicheCommunaute | null>("FicheCommunaute", id).catch(() => null);
    if (id !== this.id) return;
    if (!fiche) {
      this.racine.hidden = false;
      this.racine.replaceChildren(el("p", "discret", t("commun.pasDeReponse")));
      return;
    }
    // Redessiner pendant que l'on compose une parole de l'Entité fermerait les menus.
    if (this.entite?.enCours || this.geste?.enCours) return;
    this.dessiner(fiche);
  }

  private lienHumain(id: number, nom: string): HTMLButtonElement {
    const b = el("button", "lien", nom);
    b.type = "button";
    b.addEventListener("click", () => this.ouvrirHumain(id));
    return b;
  }

  private bouton(texte: string, titre: string, action: () => void): HTMLButtonElement {
    const b = el("button", undefined, texte);
    b.type = "button";
    b.title = titre;
    b.addEventListener("click", action);
    return b;
  }

  /** Section déroulante ; <paramref name="titre"/> est sa clé (fc.section.&lt;clé&gt; donne son nom). */
  private section(titre: string, libelle = t(`fc.section.${titre}`)): HTMLDetailsElement {
    const d = el("details", "section");
    d.dataset.titre = titre;
    d.open = sectionsOuvertes.has(titre);
    d.append(el("summary", undefined, libelle));
    d.addEventListener("toggle", () => {
      if (d.open === sectionsOuvertes.has(titre)) return;
      if (d.open) sectionsOuvertes.add(titre);
      else sectionsOuvertes.delete(titre);
      enregistrerSections();
    });
    return d;
  }

  private dessiner(f: FicheCommunaute): void {
    const c = f.base;
    this.nom = c.nom;
    const r = this.racine;
    const defilement = r.querySelector(".corps")?.scrollTop ?? 0;
    r.replaceChildren();
    r.hidden = false;
    this.surligner(f.membres.map((m) => m.id));

    const tete = el("div", "tete");
    const titre = el("h2", undefined, c.nom);
    if (c.nomPropre) {
      const nom = el("span", "nom-langue", " " + t("commun.guillemets", { texte: c.nomPropre }));
      if (c.sens) nom.title = t("fc.nomSens", { sens: c.sens });
      titre.append(nom);
    }
    tete.append(titre, boutonSuivre("communaute", c.id, c.nom));
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
    // En tête : l'essentiel, toujours visible.
    const resume = el("p", "resume");
    resume.append(c.disperseeLe
      ? t("fc.dispersee", { fondee: c.fondeeLe, dispersee: c.disperseeLe, age: f.age })
      : t("fc.resume", {
        fondee: c.fondeeLe, age: f.age,
        vivants: t("fc.vivants", { n: f.vivants }),
        adultes: t("fc.adultes", { n: f.adultes }),
        enfants: t("fc.enfants", { n: f.vivants - f.adultes }),
      }) + (c.disette ? " · " + t("fc.disette") : ""));
    corps.append(resume);
    if (c.nomPropre && c.sens) corps.append(el("p", "nom-propre", t("fc.nomPropre", { nom: c.nomPropre, sens: c.sens })));
    if (!c.disperseeLe) {
      const figure = el("p", "communaute");
      figure.append(t("fc.figure"), this.lienHumain(c.figure, c.nomFigure),
        " · " + t("fc.position", { x: c.x, y: c.y, rayon: distanceDeCases(c.rayon) }));
      corps.append(figure);
      const a = f.autorite;
      if (a) {
        const p = el("p", a.tyran ? "alerte" : "discret", t("fc.autorite", { autorite: a.autorite, crainte: a.crainte, emprise: a.emprise }));
        p.title = t("fc.autoriteAide");
        corps.append(p);
      }
    }
    const boutons = el("div", "boutons");
    if (!c.disperseeLe) boutons.append(this.bouton(t("commun.voirSurCarte"), t("fc.centrer"), () => this.centrer(c.x, c.y)));
    boutons.append(
      this.bouton(t("fc.chronologie"), t("fc.chronologieAide"), () => this.suivre(c.id, c.nom)),
      this.bouton(t("fc.terres"), t("fc.terresAide"), () => this.montrerTerres(c.id)),
    );
    corps.append(boutons);
    for (const g of c.guerre) corps.append(el("p", "alerte", g));

    const sections = [
      this.histoire(f), this.population(f), this.sexes(f), this.intelligence(f), this.caractere(f), this.savoirs(f),
      this.guerres(f), this.vie(f), this.foi(f), this.langueEtArts(f), this.membres(f),
    ];
    const outils = el("div", "sections-boutons");
    for (const [texte, ouvrir] of [[t("fc.toutOuvrir"), true], [t("fc.toutFermer"), false]] as const) {
      const b = el("button", "lien", texte);
      b.type = "button";
      b.addEventListener("click", () => {
        for (const s of sections) s.open = ouvrir;
        sectionsOuvertes = new Set(ouvrir ? sections.map((s) => s.dataset.titre ?? "") : []);
        enregistrerSections();
      });
      outils.append(b);
    }
    corps.prepend(outils);
    corps.append(...sections);
    if (!c.disperseeLe) corps.append(...this.agir(c));
    r.append(corps);
    corps.scrollTop = defilement;
  }

  // ───────────── Sections ─────────────

  private histoire(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("histoire");
    const c = f.base;
    s.append(el("p", undefined, c.disperseeLe ? t("fc.fondeeDispersee", { fondee: c.fondeeLe, dispersee: c.disperseeLe })
      : t("fc.fondee", { fondee: c.fondeeLe })));
    if (c.histoire.length > 0) {
      const ul = el("ul", "histoire");
      for (const h of c.histoire) ul.append(el("li", undefined, h));
      s.append(ul);
    }
    if (c.noms.length > 1) {
      s.append(el("h4", undefined, t("fc.noms")));
      const ul = el("ul", "liste");
      for (const n of c.noms) {
        const li = el("li");
        li.append(el("span", "date", n.depuis), ` ${n.nom} ${n.sens ? t("fc.nomSensCourt", { sens: n.sens }) : t("fc.nomObservateur")} `);
        if (n.evenement > 0) li.append(boutonCause(li, n.evenement, t("commun.comment")));
        ul.append(li);
      }
      s.append(ul);
    }
    s.append(el("h4", undefined, t("fc.marquants")));
    if (f.marquants.length === 0) s.append(el("p", "discret", t("fc.aucunMarquant")));
    else {
      const ul = el("ul", "journal-liste");
      for (const e of f.marquants) ul.append(ligneEvenement(e));
      s.append(ul);
    }
    return s;
  }

  private population(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("population");
    const p = f.population;
    if (p.tailles.length > 1) {
      s.append(el("h4", undefined, t("fc.courbe")), courbe(p.tailles));
    }
    // Pyramide des âges : hommes à gauche, femmes à droite.
    if (f.vivants > 0) {
      s.append(el("h4", undefined, t("fc.pyramide")));
      const max = Math.max(1, ...p.pyramide.flat());
      const pyr = el("div", "pyramide");
      for (let i = p.pyramide.length - 1; i >= 0; i--) {
        const [h, fe] = p.pyramide[i];
        if (h === 0 && fe === 0 && i > 0 && p.pyramide.slice(i).every(([a, b]) => a === 0 && b === 0)) continue;
        const ligne = el("div", "ligne-pyramide");
        const gauche = el("span", "barre hommes");
        gauche.style.width = `${(h / max) * 45}%`;
        const droite = el("span", "barre femmes");
        droite.style.width = `${(fe / max) * 45}%`;
        ligne.append(el("span", "nombre", String(h || "")), gauche, el("span", "tranche", i === p.pyramide.length - 1 ? "80+" : `${i * 5}-${i * 5 + 4}`),
          droite, el("span", "nombre", String(fe || "")));
        pyr.append(ligne);
      }
      s.append(pyr, el("p", "discret", t("fc.pyramideAide")));
    }
    s.append(el("h4", undefined, t("fc.indicateurs")));
    const tab = el("table", "tableau");
    const entete = el("tr");
    for (const x of ["", t("fc.ici"), t("fc.monde"), t("fc.reference")]) entete.append(el("th", undefined, x));
    tab.append(entete);
    for (const i of p.indicateurs) {
      const tr = el("tr");
      const v = (x: number | null) => (x === null ? "—" : `${nombre(x)}${i.unite ? ` ${i.unite}` : ""}`);
      tr.append(el("td", undefined, i.nom), el("td", undefined, v(i.communaute)), el("td", undefined, v(i.monde)), el("td", "discret", i.reference ?? ""));
      if (i.communaute === null) tr.title = t("fc.pasAssez", { n: i.cas });
      tab.append(tr);
    }
    s.append(tab);
    if (p.annees.length > 0) {
      s.append(el("h4", undefined, t("fc.annees")));
      const annees = this.toutesLesAnnees ? p.annees : p.annees.slice(-10);
      const table = el("table", "tableau");
      const tr0 = el("tr");
      for (const x of ["fc.an", "fc.naissances", "fc.morts", "fc.moins5", "fc.solde", "fc.population"]) tr0.append(el("th", undefined, t(x)));
      table.append(tr0);
      for (const a of [...annees].reverse()) {
        const tr = el("tr");
        tr.append(el("td", undefined, String(a.an)), el("td", undefined, String(a.naissances)), el("td", undefined, String(a.morts)),
          el("td", undefined, String(a.mortsMoins5)), el("td", undefined, a.solde === null ? "—" : signe(a.solde)),
          el("td", undefined, a.population === null ? "—" : String(a.population)));
        table.append(tr);
      }
      s.append(table);
      if (p.annees.length > 10) {
        const b = el("button", "lien", this.toutesLesAnnees ? t("fc.dixDernieres") : t("fc.toutes", { n: p.annees.length }));
        b.type = "button";
        b.addEventListener("click", () => {
          this.toutesLesAnnees = !this.toutesLesAnnees;
          void this.rafraichir();
        });
        s.append(b);
      }
      s.append(el("p", "discret", t("fc.anneesAide")));
    }
    if (p.causes.length > 0) {
      s.append(el("h4", undefined, t("fc.causes", { n: p.mortsEnTout })), el("p", undefined, `${p.causes.join(" · ")}.`));
    }
    return s;
  }

  private sexes(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("sexes");
    const x = f.sexes;
    const table = el("table", "tableau");
    const tr0 = el("tr");
    for (const v of ["", t("fc.enfantsCol"), t("fc.age14"), t("fc.age45"), t("fc.total")]) tr0.append(el("th", undefined, v));
    table.append(tr0);
    for (const [nom, l] of [[t("fc.hommes"), x.hommes], [t("fc.femmes"), x.femmes]] as const) {
      const tr = el("tr");
      tr.append(el("td", undefined, nom), ...l.map((n) => el("td", undefined, String(n))), el("td", undefined, String(l.reduce((a, b) => a + b, 0))));
      table.append(tr);
    }
    s.append(table);
    const adultes = x.orientations.reduce((a, b) => a + b, 0);
    if (adultes > 0) {
      s.append(el("h4", undefined, t("fc.attirance")));
      const ul = el("ul", "liste");
      x.classes.forEach((cl, i) => {
        if (x.orientations[i] > 0) ul.append(el("li", undefined, t("fc.classe", { classe: cl, n: x.orientations[i], pourcent: t("commun.pourcent", { v: Math.round((100 * x.orientations[i]) / adultes) }) })));
      });
      s.append(ul);
      s.append(el("p", "discret", t("fc.classesAide")));
    }
    if (x.couplesMemeSexe > 0) s.append(el("p", undefined, t("fc.memeSexe", { n: x.couplesMemeSexe })));
    if (x.moeurs) s.append(el("p", "usage", t("fc.moeurs", { v: x.moeurs })));
    s.append(el("p", "usage", t("fc.usage", { v: f.base.usage })));
    if (f.base.ideal) s.append(el("p", "discret", t("fc.trouveBeaux", { v: f.base.ideal })));
    return s;
  }

  private intelligence(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("intelligence");
    const q = f.intelligence;
    if (q.mesures === 0) {
      s.append(el("p", "discret", t("fc.aucun6ans")));
      return s;
    }
    const p = el("p");
    p.append(t("fc.qiMoyen", { moyenne: nombre(q.moyenne), mediane: nombre(q.mediane), n: q.mesures }));
    if (q.plusHaut) p.append(el("br"), t("fc.plusHaut"), this.lienHumain(q.plusHaut.id, q.plusHaut.nom), ` (${q.plusHaut.valeur})`);
    if (q.plusBas) p.append(el("br"), t("fc.plusBas"), this.lienHumain(q.plusBas.id, q.plusBas.nom), ` (${q.plusBas.valeur})`);
    s.append(p);
    s.append(el("p", "discret", t("fc.repartition", { liste: TRANCHES_QI.map((x, i) => t("fc.tranche", { tranche: t(x), n: q.repartition[i] })).join(" · ") })));
    s.append(el("h4", undefined, t("fc.aptitudes")));
    const ul = el("ul", "liste");
    for (const a of q.aptitudes) {
      const li = el("li", undefined, t("fc.aptitude", { nom: a.nom, ici: nombre(a.moyenne), monde: nombre(a.moyenneMonde) }));
      if (a.meilleur) li.append(" · " + t("fc.meilleure"), this.lienHumain(a.meilleur.id, a.meilleur.nom), ` (${a.meilleur.valeur})`);
      ul.append(li);
    }
    s.append(ul);
    return s;
  }

  private caractere(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("caractere");
    const k = f.caractere;
    const ul = el("ul", "liste");
    for (const x of k.traits) ul.append(el("li", undefined, t(K_TRAIT, { nom: x.nom, ici: nombre(x.moyenne, 2), monde: nombre(x.moyenneMonde, 2) })));
    s.append(el("h4", undefined, t("fc.traits")), ul);
    if (k.distinctifs.length > 0) s.append(el("p", undefined, t(K_DISTINCTIFS, { liste: k.distinctifs.join(t("fiche.pointVirgule")) })));
    const c = f.base;
    if (c.morale) s.append(el("p", "usage", t("fc.morale", { v: c.morale })));
    if (c.temperament) s.append(el("p", "usage", t("fc.temperament", { v: c.temperament })));
    if (k.karmaMoyen !== null) {
      const p = el("p", "usage");
      p.title = t("fc.regardEntite");
      p.append(t("fc.karmaMoyen", { v: signe(k.karmaMoyen) }));
      if (k.meilleurKarma) p.append(el("br"), t("fc.meilleur"), this.lienHumain(k.meilleurKarma.id, k.meilleurKarma.nom), ` (${signe(k.meilleurKarma.valeur)})`);
      if (k.pireKarma) p.append(el("br"), t("fc.pire"), this.lienHumain(k.pireKarma.id, k.pireKarma.nom), ` (${signe(k.pireKarma.valeur)})`);
      s.append(p);
    }
    return s;
  }

  private savoirs(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("savoirs");
    const x = f.savoirs;
    s.append(el("h4", undefined, t("fc.decouvertes")));
    if (x.decouvertes.length === 0) s.append(el("p", "discret", t("fc.aucune")));
    else {
      const ul = el("ul", "liste");
      for (const d of x.decouvertes) {
        const li = el("li");
        li.append(el("span", "date", d.date), " " + t("fc.par", { savoir: d.savoir }), this.lienHumain(d.humain, d.nom), ` (${d.origine})${d.estimee ? " · " + t("fc.estimee") : ""} `);
        if (d.evenement > 0) li.append(boutonCause(li, d.evenement, t("commun.comment")));
        ul.append(li);
      }
      s.append(ul);
    }
    if (x.connus.length > 0) s.append(el("h4", undefined, t("fc.connus")), el("p", undefined, `${x.connus.join(t("commun.virgule"))}.`));
    if (x.fragiles && x.fragiles.length > 0) {
      s.append(el("h4", undefined, t("fc.fragiles")));
      const ul = el("ul", "liste");
      for (const f of x.fragiles) {
        const li = el("li");
        li.append(t("fc.savoirDe", { savoir: f.savoir }));
        f.detenteurs.forEach((d, i) => {
          if (i > 0) li.append(t("fc.et"));
          li.append(this.lienHumain(d.id, d.nom));
        });
        li.append(" seulement");
        ul.append(li);
      }
      s.append(ul, el("p", "discret", t("fc.fragilesAide")));
    }
    if (x.appris.length > 0) s.append(el("h4", undefined, t("fc.appris")), el("p", undefined, `${x.appris.join(t("commun.virgule"))}.`));
    if (x.perdus.length > 0) {
      s.append(el("h4", undefined, t("fc.perdus")), el("p", undefined, `${x.perdus.join(t("commun.virgule"))}.`),
        el("p", "discret", t("fc.perdusAide")));
    }
    return s;
  }

  private guerres(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("guerres");
    const c = f.base;
    if (c.relations.length > 0) {
      const rel = el("p");
      rel.append(t("fc.voisines"));
      c.relations.forEach((r, i) => {
        if (i > 0) rel.append(", ");
        rel.append(el("span", `relation-${r.code}`, `${r.nom} (${r.etat})`));
        if (r.evenement > 0) rel.append(" ", boutonCause(rel, r.evenement, t("commun.pourquoi")));
      });
      s.append(rel);
    } else s.append(el("p", "discret", t("fc.aucuneVoisine")));
    if (c.projet) {
      const p = c.projet;
      const ligne = el("p", "alerte projet");
      ligne.append(t("fc.prepare"), el("strong", undefined, p.nomCible),
        t("fc.poussee", { depuis: p.depuis }),
        this.lienHumain(p.instigateur, p.nomInstigateur), `, ${p.raison}.`,
        " " + (p.allies.length > 0 ? t("fc.allies", { liste: p.allies.join(t("commun.virgule")) })
          : t("fc.aucunAllie")),
        " " + t("fc.debat", { debat: p.debat }) + " ");
      if (p.evenement > 0) ligne.append(boutonCause(ligne, p.evenement, t("fc.leProjet")));
      s.append(ligne);
    }
    if (c.guerres.length > 0) {
      const ul = el("ul", "liste");
      for (const g of c.guerres) {
        const li = el("li");
        li.append(el("span", "date", g.finLe ? `${g.debut} → ${g.finLe}` : t("fc.depuis", { date: g.debut })),
          " " + t("fc.guerre", {
            contre: g.nomContre,
            raids: t("fc.raids", { n: g.raids }),
            siens: t("fc.mortsN", { n: g.mortsSiens }),
            ennemis: g.mortsEnnemis,
            issue: g.issue ?? t("fc.enCours"),
          }) + " ");
        if (g.evenement > 0) li.append(boutonCause(li, g.evenement, t("fc.premierSang")));
        if (g.fin > 0) li.append(" ", boutonCause(li, g.fin, t("fc.fin")));
        ul.append(li);
      }
      s.append(el("h4", undefined, t("fc.guerres")), ul);
    } else s.append(el("p", "discret", t("fc.aucuneGuerre")));
    if (f.vie.captifs.length > 0) {
      const p = el("p", undefined, t("fc.captifs"));
      f.vie.captifs.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), ` (${x.detail})`);
      });
      s.append(p);
    }
    for (const x of c.exiles) {
      const ligne = el("p", "alerte");
      ligne.append(t("fc.aChasse"), this.lienHumain(x.id, x.nom), ` ${x.raison} (${x.jusqua ? t("fiche.finPrevue", { date: x.jusqua }) : t("fiche.pourToujours")}). `);
      if (x.evenement > 0) ligne.append(boutonCause(ligne, x.evenement, t("commun.pourquoi")));
      s.append(ligne);
    }
    return s;
  }

  /** Le feu (qui sait l'allumer, qui le porte) et les réserves (en jours de vivres pour tous), l'idée qu'on s'en fait, les incendies. */
  private feu(s: HTMLElement, x: NonNullable<FicheCommunaute["vie"]["feu"]>): void {
    const p = el("p", undefined, x.adultes > 0
      ? t("fc.saventAllumer", { n: x.saventAllumer, adultes: x.adultes })
      : t("fc.aucunAdulte"));
    if (x.porteur) {
      p.append(" " + t("fc.porteur"), this.lienHumain(x.porteur.id, x.porteur.nom),
        " " + t("fc.aPorte", { n: x.porteur.valeur }));
    }
    s.append(p);
    if (x.reserves.length > 0) {
      s.append(el("p", undefined, t("fc.reserves",
        { liste: x.reserves.join(t("commun.virgule")), v: ecrireNombre(x.joursDeVivres, 1), n: x.joursDeVivres })));
    } else s.append(el("p", "discret", t("fc.aucuneReserve")));
    if (x.idee) s.append(el("p", "usage", x.idee));
    if (x.incendies.length > 0) {
      const ul = el("ul", "liste");
      for (const i of x.incendies) ul.append(el("li", undefined, `${i.debut} · ${i.nom}${i.morts > 0 ? ` (${t("fc.mortsN", { n: i.morts })})` : ""}`));
      s.append(el("h4", undefined, t("fc.incendies")), ul);
    }
  }

  /** Ceux qu'on vient voir pour un ouvrage, les rassemblements avec d'autres groupes, les visites, ceux qui sont venus d'ailleurs. */
  private rencontres(s: HTMLElement, x: NonNullable<FicheCommunaute["vie"]["rencontres"]>): void {
    const personnes = (titre: string, liste: Personne[]): void => {
      if (liste.length === 0) return;
      const p = el("p", undefined, t("fc.titreListe", { titre: t(titre) }));
      liste.forEach((y, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(y.id, y.nom), ` (${y.detail})`);
      });
      s.append(p);
    };
    personnes("fc.specialistes", x.specialistes);
    personnes("fc.enVisite", x.enVisite);
    personnes("fc.visiteurs", x.visiteurs);
    personnes("fc.venus", x.venusDAilleurs);
    if (x.rassemblements.length > 0) {
      s.append(el("h4", undefined, t("fc.rassemblements")));
      const ul = el("ul", "liste");
      for (const r of x.rassemblements) {
        const faits = [t("fc.presents", { n: r.participants })];
        if (r.cadeaux > 0) faits.push(t("fc.cadeaux", { n: r.cadeaux }));
        if (r.appris > 0) faits.push(t("fc.gestesAppris", { n: r.appris }));
        if (r.couples > 0) faits.push(t("fc.couples", { n: r.couples }));
        const autre = { nom: r.nomAutre, nomMin: minuscule(r.nomAutre) };
        const li = el("li", undefined, `${r.debut} · ${t(r.hote ? "fc.recoit" : "fc.chez", autre)}, ${t("fc.jours", { n: r.jours })}${r.enCours ? " " + t("fc.enCoursParenthese") : ""} · ${faits.join(t("commun.virgule"))} `);
        if (r.bilan > 0) li.append(boutonCause(li, r.bilan, t("fc.bilan")));
        else if (r.evenement > 0) li.append(boutonCause(li, r.evenement, t("commun.pourquoi")));
        ul.append(li);
      }
      s.append(ul);
    }
  }

  private vie(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("vie");
    const c = f.base;
    const v = f.vie;
    if (!c.disperseeLe) {
      s.append(el("p", c.abondance < 40 ? "alerte" : undefined,
        t("fc.terresAlentour",
          { v: c.abondance, surface: surfaceDeCases(c.terres), faim: c.faim, reserves: c.reserves }) + (c.disette ? " · " + t("fc.disetteEnCours") : "") + "."));
      if (c.destination) s.append(el("p", "alerte", t("fc.enRoute", { v: c.destination })));
      s.append(el("p", undefined, t("fc.abris", {
        huttes: t("fc.huttes", { n: v.huttes }),
        feux: t("fc.feux", { n: v.foyers }),
      }) + (v.feu && v.feu.braises > 0 ? " · " + t("fc.braises", { n: v.feu.braises }) : "") + "."));
    }
    if (v.feu) this.feu(s, v.feu);
    if (v.rencontres) this.rencontres(s, v.rencontres);
    if (v.malades.length > 0) {
      const p = el("p", undefined, t("fc.malades"));
      v.malades.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), ` (${x.detail})`);
      });
      s.append(p);
    } else if (!c.disperseeLe) s.append(el("p", "discret", t("fc.personneMalade")));
    s.append(el("p", undefined, t("fc.mortsMaladie", { n: v.mortsDeMaladie })));
    if (c.soignants && c.soignants.length > 0) {
      const p = el("p", "usage", t("fc.soignants"));
      c.soignants.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), " " + t("fc.soignant", { role: x.role, gueris: x.gueris, perdus: x.perdus }));
      });
      s.append(p);
    }
    if (v.catastrophes.length > 0) {
      const ul = el("ul", "liste");
      for (const x of v.catastrophes) {
        ul.append(el("li", undefined, `${x.debut} · ${x.nom}${x.morts > 0 ? ` (${t("fc.mortsN", { n: x.morts })})` : ""}${x.origine === "entite" ? " " + t("fc.venuEntite") : ""}`));
      }
      s.append(el("h4", undefined, t("fc.catastrophes")), ul);
    }
    return s;
  }

  private foi(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("foi");
    const x = f.foi;
    if (x.croyance !== null) {
      s.append(el("p", undefined, t("fc.croyance", { v: x.croyance })),
        el("p", undefined, t("fc.regard",
          { amour: x.amour ?? 0, crainte: x.crainte ?? 0, revolte: x.revolte ?? 0 })));
    }
    if (x.effets.length > 0) {
      const ul = el("ul", "liste");
      for (const e of x.effets) ul.append(el("li", "entite", `✦ ${e}`));
      s.append(el("h4", undefined, t("fc.gestesEnCours")), ul);
    }
    if (x.souvenirs.length > 0) {
      const ul = el("ul", "liste");
      for (const r of x.souvenirs) {
        ul.append(el("li", undefined, `${r.date} · ${r.texte}${r.faute ? ` (${r.faute})` : ""}` + t("fc.souvenir",
          { nature: t(r.bienfait ? "fc.bienfait" : "fc.chatiment"), v: r.force })));
      }
      s.append(el("h4", undefined, t("fc.raconte")), ul);
    }
    if (x.religions.length > 0) {
      const ul = el("ul", "liste");
      for (const r of x.religions) ul.append(el("li", undefined, r));
      s.append(el("h4", undefined, t("fc.religions")), ul);
    } else s.append(el("p", "discret", t("fc.aucuneReligion")));
    if (x.porteurs.length > 0) {
      const p = el("p", undefined, t("fc.porteurs"));
      x.porteurs.forEach((y, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(y.id, y.nom), ` (${y.role}${y.source ? `, ${y.source}` : ""}${y.miracles > 0 ? ", " + t("fc.miracles", { n: y.miracles }) : ""})`);
      });
      s.append(p);
    }
    if (x.lieux.length > 0) s.append(el("p", undefined, t("fc.lieux", { liste: x.lieux.join(t("fiche.pointVirgule")) })));
    const bilan = ligneBilan(x.bilan, true);
    if (bilan) s.append(el("h4", undefined, t("fc.prieres")), bilan);
    if (x.prieres.length > 0) {
      const ul = el("ul", "liste prieres");
      for (const p of x.prieres) ul.append(lignePriere(p, this.ouvrirHumain));
      s.append(el("h4", undefined, t("fc.prieresRecentes")), ul);
    }
    const paroles = f.paroles ?? [];
    if (paroles.length > 0) {
      const ul = el("ul", "liste");
      for (const m of paroles) {
        const li = el("li", m.visee ? "alerte" : "entite");
        li.append(m.visee ? t("fc.paroleVisant", { le: m.le, c: m.communaute ?? "…", mode: m.mode, phrase: m.phrase })
          : t("fc.paroleRecue", { le: m.le, mode: m.mode, phrase: m.phrase }), " ", el("strong", undefined, m.etat));
        if (!m.visee) li.append(el("span", "discret", ` · ${m.reception}`));
        if (m.evenement > 0) li.append(" ", boutonCause(li, m.evenement, t("commun.pourquoi")));
        ul.append(li);
      }
      s.append(el("h4", undefined, t("fc.parolesGroupe")), ul);
    }
    s.append(el("h4", undefined, t("fc.gestesGroupe")));
    if (f.entite.length === 0) s.append(el("p", "discret", t("fc.aucunGeste")));
    else {
      const ul = el("ul", "journal-liste");
      for (const e of f.entite) ul.append(ligneEvenement(e));
      s.append(ul);
    }
    return s;
  }

  private langueEtArts(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("langue");
    const c = f.base;
    if (c.langue) blocLangue(s, c.langue, this.ouvrirHumain);
    else if (c.mots.length > 0) s.append(el("p", "discret", t("fc.premiersMots", { liste: c.mots.join(" · ") })));
    else s.append(el("p", "discret", t("fc.pasDeLangue")));
    if (c.style) s.append(el("h3", undefined, t("fc.arts")), el("p", "usage", c.style));
    if (c.oeuvres && c.oeuvres.length > 0) {
      const p = el("p", "discret", t("fc.peint"));
      c.oeuvres.forEach((o, i) => {
        if (i > 0) p.append(" ; ");
        p.append(o.description, " (", this.lienHumain(o.auteurId, o.auteur), `, ${o.le})`);
      });
      p.append(".");
      s.append(p);
    }
    return s;
  }

  private membres(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("membres", t("fc.membresN", { n: f.membres.length }));
    if (f.membres.length === 0) {
      s.append(el("p", "discret", t("fc.plusAucun")));
      return s;
    }
    const colonnes: [keyof Membre, string][] = [["nom", t("fc.colNom")], ["age", t("fc.colAge")], ["qi", t("fc.colQi")], ["karma", t("fc.colKarma")], ["croyance", t("fc.colCroyance")]];
    const table = el("table", "tableau membres");
    const tr0 = el("tr");
    for (const [cle, libelle] of colonnes) {
      const th = el("th");
      const b = el("button", "lien", `${libelle}${this.tri === cle ? (this.triDescendant ? " ▾" : " ▴") : ""}`);
      b.type = "button";
      b.addEventListener("click", () => {
        this.triDescendant = this.tri === cle ? !this.triDescendant : cle !== "nom";
        this.tri = cle;
        void this.rafraichir();
      });
      th.append(b);
      tr0.append(th);
    }
    table.append(tr0);
    const cle = this.tri;
    const tries = [...f.membres].sort((a, b) => {
      const x = a[cle] ?? "";
      const y = b[cle] ?? "";
      const ordre = typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), langue());
      return this.triDescendant ? -ordre : ordre;
    });
    for (const m of tries) {
      const tr = el("tr");
      const nom = el("td");
      nom.append(this.lienHumain(m.id, m.nom), el("span", "discret", ` ${m.sexe === "femme" ? "♀" : "♂"}${m.role ? ` · ${m.role}` : ""}`));
      tr.append(nom, el("td", undefined, String(m.age)), el("td", undefined, String(m.qi)), el("td", undefined, signe(m.karma)),
        el("td", undefined, String(m.croyance)));
      table.append(tr);
    }
    s.append(table);
    return s;
  }

  /** L'Entité parle ou inspire chaque membre, ou agit sur le groupe. */
  private agir(c: Communaute): HTMLElement[] {
    if (!this.entite) {
      this.entite = new FormulaireEntite(() => ({
        humains: [], communautes: [c.id], pres: c.figure, communaute: c.id, libelle: t("fc.chaqueMembre", { nom: c.nom, nomMin: minuscule(c.nom) }),
      }), () => this.id);
    }
    this.geste ??= new FormulaireGeste(false, () => ({ id: this.id, nom: this.nom }));
    return [this.entite.bloc, this.geste.bloc];
  }
}
