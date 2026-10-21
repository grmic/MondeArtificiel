import { boutonSuivre } from "./suivis";
import { connexion } from "./connexion";
import { distanceDeCases, surfaceDeCases } from "./carte";
import { blocLangue, courbe, type Communaute, type Taille } from "./communautes";
import { lignePriere, type Priere } from "./cultes";
import { FormulaireEntite } from "./entite";
import { FormulaireGeste } from "./gestes";
import { boutonCause, el, ligneEvenement, replierCauses, signe, type Evenement } from "./fiche";

interface Nomme { id: number; nom: string; valeur: number }
interface Annee { an: number; naissances: number; morts: number; mortsMoins5: number; population: number | null; solde: number | null }
interface Indicateur { nom: string; communaute: number | null; monde: number | null; reference: string | null; unite: string; cas: number }
interface Aptitude { nom: string; moyenne: number; moyenneMonde: number; meilleur: Nomme | null }
interface Personne { id: number; nom: string; detail: string }
interface CatastropheResume { id: number; libelle: string; nom: string; origine: string; debut: string; morts: number }
interface Membre { id: number; nom: string; sexe: string; age: number; qi: number; karma: number; croyance: number; etape: string; role: string | null }

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
  };
  vie: { huttes: number; foyers: number; malades: Personne[]; mortsDeMaladie: number; catastrophes: CatastropheResume[]; captifs: Personne[] };
  foi: {
    croyance: number | null; religions: string[]; porteurs: { id: number; nom: string; role: string; source: string | null; miracles: number }[];
    lieux: string[]; prieres: Priere[];
    /** Moyennes des adultes du regard sur l'invisible (0 à 100). */
    amour: number | null; crainte: number | null; revolte: number | null;
    /** Gestes de l'Entité qui pèsent encore sur elle, et ce qu'elle se raconte de ses gestes visibles. */
    effets: string[];
    souvenirs: { date: string; texte: string; bienfait: boolean; force: number; faute: string | null; evenement: number }[];
  };
  marquants: Evenement[];
  entite: Evenement[];
  membres: Membre[];
}

const ACTUALISATION_MS = 10000;
const TRANCHES_QI = ["moins de 70", "70 à 84", "85 à 114", "115 à 129", "130 et plus"];

/** Sections ouvertes de la fiche Communauté (par leur titre), retenues d'une fiche et d'une session à l'autre. */
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
  return new Set(["Population et démographie"]);
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
  return v.toLocaleString("fr-FR", { maximumFractionDigits: decimales });
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
      this.racine.replaceChildren(el("p", "discret", "Le serveur n'a pas répondu."));
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

  private section(titre: string, libelle = titre): HTMLDetailsElement {
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
      const nom = el("span", "nom-langue", ` « ${c.nomPropre} »`);
      if (c.sens) nom.title = `Le nom qu'ils se donnent : « ${c.sens} »`;
      titre.append(nom);
    }
    tete.append(titre, boutonSuivre("communaute", c.id, c.nom));
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
    // En tête : l'essentiel, toujours visible.
    const resume = el("p", "resume");
    resume.append(c.disperseeLe
      ? `Fondée le ${c.fondeeLe}, dispersée le ${c.disperseeLe} (après ${f.age}).`
      : `Fondée le ${c.fondeeLe} (il y a ${f.age}) · ${f.vivants} vivant${f.vivants > 1 ? "s" : ""} : ${f.adultes} adulte${f.adultes > 1 ? "s" : ""}, ${f.vivants - f.adultes} enfant${f.vivants - f.adultes > 1 ? "s" : ""}${c.disette ? " · disette" : ""}`);
    corps.append(resume);
    if (c.nomPropre && c.sens) corps.append(el("p", "nom-propre", `« ${c.nomPropre} » : ${c.sens}`));
    if (!c.disperseeLe) {
      const figure = el("p", "communaute");
      figure.append("Figure centrale : ", this.lienHumain(c.figure, c.nomFigure),
        ` · vers (${c.x}, ${c.y}), dans un rayon de ${distanceDeCases(c.rayon)}`);
      corps.append(figure);
    }
    const boutons = el("div", "boutons");
    if (!c.disperseeLe) boutons.append(this.bouton("Voir sur la carte", "Centrer la carte sur la communauté", () => this.centrer(c.x, c.y)));
    boutons.append(
      this.bouton("Chronologie", "Tout ce qui est arrivé à cette communauté, dans le journal", () => this.suivre(c.id, c.nom)),
      this.bouton("Terres", "Ses terres sur la carte, aujourd'hui et au fil des saisons", () => this.montrerTerres(c.id)),
    );
    corps.append(boutons);
    for (const g of c.guerre) corps.append(el("p", "alerte", g));

    const sections = [
      this.histoire(f), this.population(f), this.sexes(f), this.intelligence(f), this.caractere(f), this.savoirs(f),
      this.guerres(f), this.vie(f), this.foi(f), this.langueEtArts(f), this.membres(f),
    ];
    const outils = el("div", "sections-boutons");
    for (const [texte, ouvrir] of [["tout ouvrir", true], ["tout fermer", false]] as const) {
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
    const s = this.section("Naissance et histoire");
    const c = f.base;
    s.append(el("p", undefined, `Fondée le ${c.fondeeLe}${c.disperseeLe ? `, dispersée le ${c.disperseeLe}` : ""}.`));
    if (c.histoire.length > 0) {
      const ul = el("ul", "histoire");
      for (const h of c.histoire) ul.append(el("li", undefined, h));
      s.append(ul);
    }
    if (c.noms.length > 1) {
      s.append(el("h4", undefined, "Noms successifs"));
      const ul = el("ul", "liste");
      for (const n of c.noms) {
        const li = el("li");
        li.append(el("span", "date", n.depuis), ` ${n.nom}${n.sens ? ` (« ${n.sens} »)` : " (nom donné par l'observateur)"} `);
        if (n.evenement > 0) li.append(boutonCause(li, n.evenement, "comment ?"));
        ul.append(li);
      }
      s.append(ul);
    }
    s.append(el("h4", undefined, "Événements marquants"));
    if (f.marquants.length === 0) s.append(el("p", "discret", "Aucun événement marquant (★) pour l'instant."));
    else {
      const ul = el("ul", "journal-liste");
      for (const e of f.marquants) ul.append(ligneEvenement(e));
      s.append(ul);
    }
    return s;
  }

  private population(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Population et démographie");
    const p = f.population;
    if (p.tailles.length > 1) {
      s.append(el("h4", undefined, "Population au fil des saisons (adultes en pointillés)"), courbe(p.tailles));
    }
    // Pyramide des âges : hommes à gauche, femmes à droite.
    if (f.vivants > 0) {
      s.append(el("h4", undefined, "Pyramide des âges"));
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
      s.append(pyr, el("p", "discret", "Hommes à gauche, femmes à droite."));
    }
    s.append(el("h4", undefined, "Indicateurs"));
    const tab = el("table", "tableau");
    const entete = el("tr");
    for (const t of ["", "Ici", "Monde", "Référence"]) entete.append(el("th", undefined, t));
    tab.append(entete);
    for (const i of p.indicateurs) {
      const tr = el("tr");
      const v = (x: number | null) => (x === null ? "—" : `${nombre(x)}${i.unite ? ` ${i.unite}` : ""}`);
      tr.append(el("td", undefined, i.nom), el("td", undefined, v(i.communaute)), el("td", undefined, v(i.monde)), el("td", "discret", i.reference ?? ""));
      if (i.communaute === null) tr.title = `Pas assez de cas ici (${i.cas})`;
      tab.append(tr);
    }
    s.append(tab);
    if (p.annees.length > 0) {
      s.append(el("h4", undefined, "Année par année"));
      const annees = this.toutesLesAnnees ? p.annees : p.annees.slice(-10);
      const t = el("table", "tableau");
      const tr0 = el("tr");
      for (const x of ["An", "Naissances", "Morts", "dont < 5 ans", "Arrivées − départs", "Population"]) tr0.append(el("th", undefined, x));
      t.append(tr0);
      for (const a of [...annees].reverse()) {
        const tr = el("tr");
        tr.append(el("td", undefined, String(a.an)), el("td", undefined, String(a.naissances)), el("td", undefined, String(a.morts)),
          el("td", undefined, String(a.mortsMoins5)), el("td", undefined, a.solde === null ? "—" : signe(a.solde)),
          el("td", undefined, a.population === null ? "—" : String(a.population)));
        t.append(tr);
      }
      s.append(t);
      if (p.annees.length > 10) {
        const b = el("button", "lien", this.toutesLesAnnees ? "les 10 dernières" : `toutes (${p.annees.length})`);
        b.type = "button";
        b.addEventListener("click", () => {
          this.toutesLesAnnees = !this.toutesLesAnnees;
          void this.rafraichir();
        });
        s.append(b);
      }
      s.append(el("p", "discret", "Naissances : enfants de mères membres. Morts : membres morts alors qu'ils y vivaient. Arrivées − départs : déduit de la population relevée à chaque saison."));
    }
    if (p.causes.length > 0) {
      s.append(el("h4", undefined, `Causes de mort (${p.mortsEnTout} morts)`), el("p", undefined, `${p.causes.join(" · ")}.`));
    }
    return s;
  }

  private sexes(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Sexes et orientations");
    const x = f.sexes;
    const t = el("table", "tableau");
    const tr0 = el("tr");
    for (const v of ["", "Enfants", "14 à 44 ans", "45 ans et plus", "Total"]) tr0.append(el("th", undefined, v));
    t.append(tr0);
    for (const [nom, l] of [["Hommes", x.hommes], ["Femmes", x.femmes]] as const) {
      const tr = el("tr");
      tr.append(el("td", undefined, nom), ...l.map((n) => el("td", undefined, String(n))), el("td", undefined, String(l.reduce((a, b) => a + b, 0))));
      t.append(tr);
    }
    s.append(t);
    const adultes = x.orientations.reduce((a, b) => a + b, 0);
    if (adultes > 0) {
      s.append(el("h4", undefined, "Attirance des adultes"));
      const ul = el("ul", "liste");
      x.classes.forEach((cl, i) => {
        if (x.orientations[i] > 0) ul.append(el("li", undefined, `${cl} : ${x.orientations[i]} (${Math.round((100 * x.orientations[i]) / adultes)} %)`));
      });
      s.append(ul);
      s.append(el("p", "discret", "Classes tirées de leurs attirances, continues, vers chaque sexe (à la manière de l'échelle de Kinsey)."));
    }
    if (x.couplesMemeSexe > 0) s.append(el("p", undefined, `Couples de même sexe : ${x.couplesMemeSexe}.`));
    if (x.moeurs) s.append(el("p", "usage", `Mœurs : ${x.moeurs}.`));
    s.append(el("p", "usage", `Usage : ${f.base.usage}.`));
    if (f.base.ideal) s.append(el("p", "discret", `Trouve beaux : ${f.base.ideal}.`));
    return s;
  }

  private intelligence(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Intelligence et aptitudes");
    const q = f.intelligence;
    if (q.mesures === 0) {
      s.append(el("p", "discret", "Aucun membre de 6 ans ou plus."));
      return s;
    }
    const p = el("p");
    p.append(`QI moyen ${nombre(q.moyenne)}, médian ${nombre(q.mediane)} (${q.mesures} membres de 6 ans et plus).`);
    if (q.plusHaut) p.append(el("br"), "Le plus haut : ", this.lienHumain(q.plusHaut.id, q.plusHaut.nom), ` (${q.plusHaut.valeur})`);
    if (q.plusBas) p.append(el("br"), "Le plus bas : ", this.lienHumain(q.plusBas.id, q.plusBas.nom), ` (${q.plusBas.valeur})`);
    s.append(p);
    s.append(el("p", "discret", `Répartition : ${TRANCHES_QI.map((t, i) => `${t} : ${q.repartition[i]}`).join(" · ")}.`));
    s.append(el("h4", undefined, "Aptitudes (moyenne ici / dans le monde)"));
    const ul = el("ul", "liste");
    for (const a of q.aptitudes) {
      const li = el("li", undefined, `${a.nom} : ${nombre(a.moyenne)} / ${nombre(a.moyenneMonde)}`);
      if (a.meilleur) li.append(" · la meilleure : ", this.lienHumain(a.meilleur.id, a.meilleur.nom), ` (${a.meilleur.valeur})`);
      ul.append(li);
    }
    s.append(ul);
    return s;
  }

  private caractere(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Caractère et morale");
    const k = f.caractere;
    const ul = el("ul", "liste");
    for (const t of k.traits) ul.append(el("li", undefined, `${t.nom} : ${nombre(t.moyenne, 2)} (monde ${nombre(t.moyenneMonde, 2)})`));
    s.append(el("h4", undefined, "Traits moyens des adultes (0 à 1)"), ul);
    if (k.distinctifs.length > 0) s.append(el("p", undefined, `Ils sont ${k.distinctifs.join(" ; ")}.`));
    const c = f.base;
    if (c.morale) s.append(el("p", "usage", `Morale : ${c.morale}.`));
    if (c.temperament) s.append(el("p", "usage", `Tempérament : ${c.temperament}.`));
    if (k.karmaMoyen !== null) {
      const p = el("p", "usage");
      p.title = "Regard de l'Entité, invisible aux humains";
      p.append(`Karma moyen des adultes : ${signe(k.karmaMoyen)}.`);
      if (k.meilleurKarma) p.append(el("br"), "Le meilleur : ", this.lienHumain(k.meilleurKarma.id, k.meilleurKarma.nom), ` (${signe(k.meilleurKarma.valeur)})`);
      if (k.pireKarma) p.append(el("br"), "Le pire : ", this.lienHumain(k.pireKarma.id, k.pireKarma.nom), ` (${signe(k.pireKarma.valeur)})`);
      s.append(p);
    }
    return s;
  }

  private savoirs(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Savoirs et découvertes");
    const x = f.savoirs;
    s.append(el("h4", undefined, "Découvertes de ses membres"));
    if (x.decouvertes.length === 0) s.append(el("p", "discret", "Aucune pour l'instant."));
    else {
      const ul = el("ul", "liste");
      for (const d of x.decouvertes) {
        const li = el("li");
        li.append(el("span", "date", d.date), ` ${d.savoir}, par `, this.lienHumain(d.humain, d.nom), ` (${d.origine})${d.estimee ? " · communauté estimée" : ""} `);
        if (d.evenement > 0) li.append(boutonCause(li, d.evenement, "comment ?"));
        ul.append(li);
      }
      s.append(ul);
    }
    if (x.connus.length > 0) s.append(el("h4", undefined, "Ce qu'ils savent"), el("p", undefined, `${x.connus.join(", ")}.`));
    if (x.appris.length > 0) s.append(el("h4", undefined, "Appris d'autres groupes"), el("p", undefined, `${x.appris.join(", ")}.`));
    if (x.perdus.length > 0) {
      s.append(el("h4", undefined, "Perdus"), el("p", undefined, `${x.perdus.join(", ")}.`),
        el("p", "discret", "Ce que savaient des membres morts et qu'aucun vivant du groupe ne sait plus."));
    }
    return s;
  }

  private guerres(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Guerres, paix et alliances");
    const c = f.base;
    if (c.relations.length > 0) {
      const rel = el("p");
      rel.append("Voisines : ");
      c.relations.forEach((r, i) => {
        if (i > 0) rel.append(", ");
        rel.append(el("span", `relation-${r.etat.split(" ")[0]}`, `${r.nom} (${r.etat})`));
        if (r.evenement > 0) rel.append(" ", boutonCause(rel, r.evenement, "pourquoi ?"));
      });
      s.append(rel);
    } else s.append(el("p", "discret", "Aucune voisine connue."));
    if (c.projet) {
      const p = c.projet;
      const ligne = el("p", "alerte projet");
      ligne.append("⚔ Prépare la guerre contre ", el("strong", undefined, p.nomCible), ` depuis ${p.depuis}, poussée par `,
        this.lienHumain(p.instigateur, p.nomInstigateur), `, ${p.raison}.`,
        p.allies.length > 0 ? ` Alliés sollicités : ${p.allies.join(", ")}.` : " Aucun allié sollicité.", ` Le groupe en débattra (${p.debat}). `);
      if (p.evenement > 0) ligne.append(boutonCause(ligne, p.evenement, "le projet"));
      s.append(ligne);
    }
    if (c.guerres.length > 0) {
      const ul = el("ul", "liste");
      for (const g of c.guerres) {
        const li = el("li");
        li.append(el("span", "date", g.finLe ? `${g.debut} → ${g.finLe}` : `depuis ${g.debut}`),
          ` contre ${g.nomContre} : ${g.raids} raid${g.raids > 1 ? "s" : ""}, ${g.mortsSiens} morts chez elle, ${g.mortsEnnemis} chez l'ennemi · ${g.issue ?? "en cours"} `);
        if (g.evenement > 0) li.append(boutonCause(li, g.evenement, "premier sang"));
        if (g.fin > 0) li.append(" ", boutonCause(li, g.fin, "fin"));
        ul.append(li);
      }
      s.append(el("h4", undefined, "Guerres"), ul);
    } else s.append(el("p", "discret", "Aucune guerre."));
    if (f.vie.captifs.length > 0) {
      const p = el("p", undefined, "Captifs parmi eux : ");
      f.vie.captifs.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), ` (${x.detail})`);
      });
      s.append(p);
    }
    for (const x of c.exiles) {
      const ligne = el("p", "alerte");
      ligne.append("A chassé ", this.lienHumain(x.id, x.nom), ` ${x.raison} (${x.jusqua ? `fin prévue : ${x.jusqua}` : "pour toujours"}). `);
      if (x.evenement > 0) ligne.append(boutonCause(ligne, x.evenement, "pourquoi ?"));
      s.append(ligne);
    }
    return s;
  }

  private vie(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Terres, nourriture et santé");
    const c = f.base;
    const v = f.vie;
    if (!c.disperseeLe) {
      s.append(el("p", c.abondance < 40 ? "alerte" : undefined,
        `Terres alentour : ${c.abondance} % de ce qu'elles donnent d'ordinaire · terres parcourues : ${surfaceDeCases(c.terres)} · faim moyenne ${c.faim} · réserves moyennes ${c.reserves}${c.disette ? " · disette en cours" : ""}.`));
      if (c.destination) s.append(el("p", "alerte", `En route vers ${c.destination}, à la recherche de terres plus riches.`));
      s.append(el("p", undefined, `Abris : ${v.huttes} hutte${v.huttes > 1 ? "s" : ""} · ${v.foyers} feu${v.foyers > 1 ? "x" : ""} allumé${v.foyers > 1 ? "s" : ""} sur ses terres.`));
    }
    if (v.malades.length > 0) {
      const p = el("p", undefined, "Malades : ");
      v.malades.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), ` (${x.detail})`);
      });
      s.append(p);
    } else if (!c.disperseeLe) s.append(el("p", "discret", "Personne n'est malade."));
    s.append(el("p", undefined, `Morts de maladie depuis sa fondation : ${v.mortsDeMaladie}.`));
    if (c.soignants && c.soignants.length > 0) {
      const p = el("p", "usage", "Soignants : ");
      c.soignants.forEach((x, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(x.id, x.nom), ` (${x.role} ; ${x.gueris} guéris, ${x.perdus} perdus)`);
      });
      s.append(p);
    }
    if (v.catastrophes.length > 0) {
      const ul = el("ul", "liste");
      for (const x of v.catastrophes) {
        ul.append(el("li", undefined, `${x.debut} · ${x.nom}${x.morts > 0 ? ` (${x.morts} morts)` : ""}${x.origine === "entite" ? " ✦ venu de l'Entité" : ""}`));
      }
      s.append(el("h4", undefined, "Catastrophes et épidémies subies"), ul);
    }
    return s;
  }

  private foi(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Foi et regard sur l'Entité");
    const x = f.foi;
    if (x.croyance !== null) {
      s.append(el("p", undefined, `Croyance moyenne des adultes en l'invisible : ${x.croyance}/100.`),
        el("p", undefined, `Regard sur l'invisible (moyennes des adultes) : amour ${x.amour ?? 0}/100, crainte ${x.crainte ?? 0}/100, révolte ${x.revolte ?? 0}/100.`));
    }
    if (x.effets.length > 0) {
      const ul = el("ul", "liste");
      for (const e of x.effets) ul.append(el("li", "entite", `✦ ${e}`));
      s.append(el("h4", undefined, "Gestes de l'Entité en cours"), ul);
    }
    if (x.souvenirs.length > 0) {
      const ul = el("ul", "liste");
      for (const r of x.souvenirs) {
        ul.append(el("li", undefined, `${r.date} · ${r.texte}${r.faute ? ` (${r.faute})` : ""} : ${r.bienfait ? "un bienfait" : "un châtiment"}, souvenir ${r.force}/100`));
      }
      s.append(el("h4", undefined, "Ce qu'on se raconte des gestes de l'Entité"), ul);
    }
    if (x.religions.length > 0) {
      const ul = el("ul", "liste");
      for (const r of x.religions) ul.append(el("li", undefined, r));
      s.append(el("h4", undefined, "Religions"), ul);
    } else s.append(el("p", "discret", "Aucune religion n'y est suivie."));
    if (x.porteurs.length > 0) {
      const p = el("p", undefined, "Ceux qui la portent : ");
      x.porteurs.forEach((y, i) => {
        if (i > 0) p.append(", ");
        p.append(this.lienHumain(y.id, y.nom), ` (${y.role}${y.source ? `, ${y.source}` : ""}${y.miracles > 0 ? `, ${y.miracles} miracle${y.miracles > 1 ? "s" : ""} attribué${y.miracles > 1 ? "s" : ""}` : ""})`);
      });
      s.append(p);
    }
    if (x.lieux.length > 0) s.append(el("p", undefined, `Lieux sacrés proches : ${x.lieux.join(" ; ")}.`));
    if (x.prieres.length > 0) {
      const ul = el("ul", "liste prieres");
      for (const p of x.prieres) ul.append(lignePriere(p, this.ouvrirHumain));
      s.append(el("h4", undefined, "Prières récentes"), ul);
    }
    s.append(el("h4", undefined, "Gestes de l'Entité sur ce groupe"));
    if (f.entite.length === 0) s.append(el("p", "discret", "Aucun geste marquant de l'Entité pour l'instant."));
    else {
      const ul = el("ul", "journal-liste");
      for (const e of f.entite) ul.append(ligneEvenement(e));
      s.append(ul);
    }
    return s;
  }

  private langueEtArts(f: FicheCommunaute): HTMLDetailsElement {
    const s = this.section("Langue, arts et rites");
    const c = f.base;
    if (c.langue) blocLangue(s, c.langue, this.ouvrirHumain);
    else if (c.mots.length > 0) s.append(el("p", "discret", `Premiers mots : ${c.mots.join(" · ")}`));
    else s.append(el("p", "discret", "Pas encore de langue commune."));
    if (c.style) s.append(el("h3", undefined, "Arts"), el("p", "usage", c.style));
    if (c.oeuvres && c.oeuvres.length > 0) {
      const p = el("p", "discret", "Peint sur la roche : ");
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
    const s = this.section("Membres", `Membres (${f.membres.length})`);
    if (f.membres.length === 0) {
      s.append(el("p", "discret", "Plus aucun membre vivant."));
      return s;
    }
    const colonnes: [keyof Membre, string][] = [["nom", "Nom"], ["age", "Âge"], ["qi", "QI"], ["karma", "Karma"], ["croyance", "Croyance"]];
    const t = el("table", "tableau membres");
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
    t.append(tr0);
    const cle = this.tri;
    const tries = [...f.membres].sort((a, b) => {
      const x = a[cle] ?? "";
      const y = b[cle] ?? "";
      const ordre = typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), "fr");
      return this.triDescendant ? -ordre : ordre;
    });
    for (const m of tries) {
      const tr = el("tr");
      const nom = el("td");
      nom.append(this.lienHumain(m.id, m.nom), el("span", "discret", ` ${m.sexe === "femme" ? "♀" : "♂"}${m.role ? ` · ${m.role}` : ""}`));
      tr.append(nom, el("td", undefined, String(m.age)), el("td", undefined, String(m.qi)), el("td", undefined, signe(m.karma)),
        el("td", undefined, String(m.croyance)));
      t.append(tr);
    }
    s.append(t);
    return s;
  }

  /** L'Entité parle ou inspire chaque membre, ou agit sur le groupe. */
  private agir(c: Communaute): HTMLElement[] {
    if (!this.entite) {
      this.entite = new FormulaireEntite(() => ({
        humains: [], communautes: [c.id], pres: c.figure, communaute: c.id, libelle: `Chaque membre de la ${minuscule(c.nom)}`,
      }));
    }
    this.geste ??= new FormulaireGeste(false, () => ({ id: this.id, nom: this.nom }));
    return [this.entite.bloc, this.geste.bloc];
  }
}
