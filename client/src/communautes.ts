import { connexion } from "./connexion";
import { distanceDeCases, surfaceDeCases, type Indicateurs } from "./carte";
import { boutonCause, el } from "./fiche";
import { FormulaireEntite } from "./entite";

interface Communaute {
  id: number;
  nom: string;
  fondeeLe: string;
  disperseeLe: string | null;
  figure: number;
  nomFigure: string;
  membres: { id: number; nom: string; etape: string }[];
  x: number;
  y: number;
  rayon: number;
  abondance: number;
  disette: boolean;
  destination: string | null;
  faim: number;
  reserves: number;
  relations: { id: number; nom: string; etat: string; evenement: number }[];
  /** Usage du groupe envers ceux qui s'unissent hors de leur couple. */
  usage: string;
  /** Nom que se donnent ses membres, et ce qu'il veut dire (null tant qu'ils ne se sont pas nommés). */
  nomPropre: string | null;
  sens: string | null;
  noms: { nom: string; sens: string | null; depuis: string; evenement: number }[];
  mots: string[];
  terres: number;
  histoire: string[];
  /** Goût du groupe pour la violence, en mots (null tant qu'il n'est pas établi). */
  temperament: string | null;
  /** Raid en cours, soumission, vassaux. */
  guerre: string[];
  guerres: Guerre[];
  /** Guerre qu'elle prépare (null : aucune). */
  projet: Projet | null;
  /** Son regard sur les amours entre personnes de même sexe, et ce qu'elle trouve beau (null : pas encore établis). */
  moeurs: string | null;
  ideal: string | null;
  /** Ceux qu'elle a chassés et dont l'exil dure encore. */
  exiles: { id: number; nom: string; raison: string; jusqua: string | null; evenement: number }[];
  /** Morale du groupe : moyenne des fondements moraux de ses adultes (null : aucun adulte). */
  morale: string | null;
  /** Savoirs connus de ses membres, avec combien les ont. */
  savoirs: string[];
  /** Moyennes des adultes : karma (regard de l'Entité) et croyance en l'invisible (0 à 100). */
  karma: number | null;
  croyance: number | null;
  /** Guérisseurs, chamans et charlatans de la communauté. */
  soignants: { id: number; nom: string; role: string; gueris: number; perdus: number }[] | null;
  /** Style de ses arts (chants, rythmes, danses, parures, peintures), né du caractère de ses membres. */
  style: string | null;
  /** Images peintes sur la roche par ses membres, les plus récentes d'abord. */
  oeuvres: { id: number; auteurId: number; auteur: string; le: string; description: string; evenementId: number }[] | null;
  /** Sa langue : mode, stade, ordre des mots, mots et leur histoire, langues voisines, interprètes. */
  langue: Langue | null;
}

/** Un mot de la langue, avec sa traduction et qui lui a donné sa forme. */
interface MotLangue {
  sens: string; forme: string; signe: boolean; depuis: string; auteur: number; nomAuteur: string | null;
}

interface Langue {
  mode: string; stade: string; ordre: string | null; premierMot: string | null; assemblage: string | null; ordreFixe: string | null;
  nombreDeMots: number; lexique: MotLangue[]; histoire: { date: string; texte: string; evenement: number }[];
  proches: { id: number; nom: string; pourcent: number }[]; interpretes: { id: number; nom: string; langue: number; nomLangue: string }[];
  /** Communauté dont elle parle encore un dialecte (null : sa langue est à elle). */
  dialecteDe: string | null;
}

/** Une guerre en préparation : qui la veut, contre qui, pourquoi, et les alliés sollicités. */
interface Projet {
  instigateur: number;
  nomInstigateur: string;
  cible: number;
  nomCible: string;
  raison: string;
  depuis: string;
  allies: string[];
  evenement: number;
  /** Jour du débat. */
  debat: string;
}

/** Une guerre vue par la communauté (issue null : en cours). */
interface Guerre {
  contre: number;
  nomContre: string;
  debut: string;
  finLe: string | null;
  raids: number;
  mortsSiens: number;
  mortsEnnemis: number;
  issue: string | null;
  evenement: number;
  fin: number;
}

interface Taille {
  secondes: number;
  date: string;
  membres: number;
  adultes: number;
}

/** Courbe de la taille d'une communauté au début de chaque saison (membres en bleu, adultes en pointillés verts). */
function courbe(tailles: Taille[]): SVGSVGElement {
  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("class", "courbe");
  svg.setAttribute("viewBox", "0 0 300 48");
  svg.setAttribute("preserveAspectRatio", "none");
  const max = Math.max(1, ...tailles.map((t) => t.membres));
  const points = (cle: "membres" | "adultes") => tailles
    .map((t, i) => `${tailles.length === 1 ? 150 : (i / (tailles.length - 1)) * 300},${46 - (t[cle] / max) * 44}`).join(" ");
  for (const cle of ["membres", "adultes"] as const) {
    const ligne = document.createElementNS(ns, "polyline");
    ligne.setAttribute("points", points(cle));
    if (cle === "adultes") ligne.setAttribute("class", "adultes");
    svg.append(ligne);
  }
  const titre = document.createElementNS(ns, "title");
  titre.textContent = `De ${tailles[0].date} à ${tailles[tailles.length - 1].date} : jusqu'à ${max} membres`;
  svg.append(titre);
  return svg;
}

/**
 * Communautés : les groupes d'humains liés qui vivent ensemble, constatés chaque semaine par le monde.
 * Choisir une communauté entoure ses membres sur la carte.
 */
export class PanneauCommunautes {
  private choisie = 0;
  private minuteur?: number;
  private tailles: { id: number; valeurs: Taille[]; lu: number } | null = null;
  /** Formulaire de l'Entité de chaque communauté : gardé d'un rafraîchissement à l'autre. */
  private readonly entites = new Map<number, FormulaireEntite>();

  constructor(
    private readonly racine: HTMLElement,
    private readonly surligner: (ids: number[]) => void,
    private readonly ouvrirFiche: (id: number) => void,
    private readonly suivre: (id: number, nom: string) => void = () => {},
    private readonly montrerTerres: (id: number) => void = () => {},
  ) {}

  /** Prévenu quand la communauté choisie change (0 : aucune). */
  surChoix: (id: number) => void = () => {};

  basculer(): void {
    if (this.racine.hidden) void this.ouvrir();
    else this.fermer();
  }

  async ouvrir(id?: number): Promise<void> {
    if (id !== undefined) {
      this.choisie = id;
      this.surChoix(id);
    }
    this.racine.hidden = false;
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.rafraichir(), 5000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.racine.hidden = true;
    this.choisie = 0;
    this.surChoix(0);
    this.surligner([]);
  }

  async rafraichir(): Promise<void> {
    if (this.racine.hidden) return;
    // Taille au fil des saisons de la communauté choisie : relue au plus une fois par minute.
    if (this.choisie && (this.tailles?.id !== this.choisie || Date.now() - this.tailles.lu > 60_000)) {
      const id = this.choisie;
      const valeurs = await connexion.invoke<Taille[]>("TaillesCommunaute", id).catch(() => []);
      this.tailles = { id, valeurs, lu: Date.now() };
    }
    const [liste, nature] = await Promise.all([
      connexion.invoke<Communaute[]>("Communautes").catch(() => null),
      connexion.invoke<{ indicateurs: Indicateurs }>("Nature").catch(() => null),
    ]);
    // Redessiner pendant que l'on compose une parole de l'Entité fermerait les menus.
    if ([...this.entites.values()].some((f) => f.enCours)) return;
    if (liste) this.dessiner(liste, nature?.indicateurs);
  }

  private dessiner(liste: Communaute[], monde?: Indicateurs): void {
    const r = this.racine;
    const defilement = r.querySelector(".corps")?.scrollTop ?? 0;
    r.replaceChildren();
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, "Communautés"));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);

    const corps = el("div", "corps");
    const actives = liste.filter((c) => !c.disperseeLe);
    const dispersees = liste.filter((c) => c.disperseeLe);
    corps.append(el("p", "discret",
      actives.length === 0
        ? "Aucune communauté pour l'instant : les humains vivent encore en familles isolées."
        : `${actives.length} communauté${actives.length > 1 ? "s" : ""} · ${actives.reduce((n, c) => n + c.membres.length, 0)} humains y vivent.`));
    if (monde) {
      corps.append(el("p", "discret",
        `Monde : ${monde.vivants} vivants · terres à ${Math.round(monde.abondance * 100)} % de leurs ressources naturelles · ` +
        `réserves moyennes des adultes ${Math.round(monde.reservesMoyennes)} · sécheresse dans ${monde.regionsSeches} région${monde.regionsSeches > 1 ? "s" : ""} habitée${monde.regionsSeches > 1 ? "s" : ""} sur ${monde.regionsHabitees}` +
        (monde.communautesEnDisette > 0 ? ` · ${monde.communautesEnDisette} en disette` : "")));
    }

    for (const c of actives) corps.append(this.carte(c));
    if (dispersees.length > 0) {
      corps.append(el("h3", undefined, "Dispersées"));
      const ul = el("ul", "liste");
      for (const c of dispersees.slice(0, 30)) {
        const li = el("li", "discret", `${c.nom} : ${c.fondeeLe} → ${c.disperseeLe}${c.histoire.length > 0 ? ` · ${c.histoire.join(" ")}` : ""} `);
        const chrono = el("button", "lien", "chronologie");
        chrono.type = "button";
        chrono.addEventListener("click", () => this.suivre(c.id, c.nom));
        li.append(chrono);
        ul.append(li);
      }
      corps.append(ul);
    }
    r.append(tete, corps);
    corps.scrollTop = defilement;

    const choisie = actives.find((c) => c.id === this.choisie);
    this.surligner(choisie ? choisie.membres.map((m) => m.id) : []);
  }

  /** Section « Langue » : comment on y parle, ses étapes, ses mots traduits, son histoire, les langues voisines et ses interprètes. */
  private langue(bloc: HTMLElement, l: Langue): void {
    bloc.append(el("h3", undefined, "Langue"));
    const p = el("p", "langage");
    p.append(`Langue ${l.mode} ; on y parle par ${l.stade}${l.ordre ? ` ; ordre des mots : ${l.ordre}` : ""}.`);
    if (l.dialecteDe) p.append(el("br"), `Un dialecte de la langue de la ${l.dialecteDe.charAt(0).toLowerCase()}${l.dialecteDe.slice(1)}.`);
    const etapes = [l.premierMot ? `premier mot en usage le ${l.premierMot}` : "", l.assemblage ? `mots assemblés depuis le ${l.assemblage}` : "",
      l.ordreFixe ? `ordre fixe depuis le ${l.ordreFixe}` : ""].filter(Boolean);
    if (etapes.length > 0) p.append(el("br"), `${etapes.join(" ; ")}.`);
    if (l.proches.length > 0) {
      p.append(el("br"), `Proche de : ${l.proches.map((x) => `${x.nom} (${x.pourcent} % de mots parents)`).join(", ")}.`);
    }
    if (l.interpretes.length > 0) {
      p.append(el("br"), "Interprètes : ");
      l.interpretes.forEach((x, i) => {
        if (i > 0) p.append(", ");
        const b = el("button", "lien", x.nom);
        b.type = "button";
        b.addEventListener("click", () => this.ouvrirFiche(x.id));
        p.append(b, ` (parle aussi la langue de ${x.nomLangue})`);
      });
      p.append(".");
    }
    bloc.append(p);
    if (l.lexique.length > 0) {
      const mots = el("details");
      mots.append(el("summary", undefined, `${l.nombreDeMots} mot${l.nombreDeMots > 1 ? "s" : ""}`));
      const ul = el("ul", "liste mots");
      for (const m of l.lexique) {
        const li = el("li");
        li.append(el("span", "mot", m.forme), ` « ${m.sens} »`);
        if (m.depuis) li.append(el("span", "discret", ` (${m.depuis}${m.nomAuteur ? `, ${m.nomAuteur}` : ""})`));
        ul.append(li);
      }
      mots.append(ul);
      bloc.append(mots);
    }
    if (l.histoire.length > 0) {
      const histoire = el("details");
      histoire.append(el("summary", undefined, "Histoire de la langue"));
      const ul = el("ul", "liste");
      for (const x of l.histoire) {
        const li = el("li");
        li.append(el("span", "date", x.date), ` ${x.texte} `);
        if (x.evenement > 0) li.append(boutonCause(li, x.evenement, "comment ?"));
        ul.append(li);
      }
      histoire.append(ul);
      bloc.append(histoire);
    }
  }

  private carte(c: Communaute): HTMLElement {
    const bloc = el("div", `communaute-carte${c.id === this.choisie ? " choisie" : ""}`);
    const titre = el("button", "titre", `${c.nom} · ${c.membres.length} membres${c.disette ? " · disette" : ""}`);
    if (c.disette) titre.classList.add("disette");
    titre.type = "button";
    titre.title = "Montrer ses membres sur la carte";
    titre.addEventListener("click", () => {
      this.choisie = this.choisie === c.id ? 0 : c.id;
      this.surChoix(this.choisie);
      void this.rafraichir();
    });
    bloc.append(titre);
    if (c.nomPropre && c.sens) bloc.append(el("p", "nom-propre", `« ${c.nomPropre} » : ${c.sens}`));
    const adultes = c.membres.filter((m) => m.etape === "adulte").length;
    const info = el("p", "discret", `Née ${c.fondeeLe} · vers (${c.x}, ${c.y}), dans un rayon de ${distanceDeCases(c.rayon)} · ${adultes} adultes, ${c.membres.length - adultes} enfants · terres parcourues : ${surfaceDeCases(c.terres)}`);
    bloc.append(info);
    if (c.histoire.length > 0) {
      const ul = el("ul", "histoire");
      for (const h of c.histoire) ul.append(el("li", undefined, h));
      bloc.append(ul);
    }
    bloc.append(el("p", c.abondance < 40 ? "alerte" : "discret",
      `Terres alentour : ${c.abondance} % de ce qu'elles donnent d'ordinaire · faim moyenne ${c.faim} · réserves moyennes ${c.reserves}`));
    if (c.destination) bloc.append(el("p", "alerte", `En route vers ${c.destination}, à la recherche de terres plus riches.`));
    if (c.relations.length > 0) {
      const rel = el("p");
      rel.append("Voisines : ");
      c.relations.forEach((r, i) => {
        if (i > 0) rel.append(", ");
        rel.append(el("span", `relation-${r.etat.split(" ")[0]}`, `${r.nom} (${r.etat})`));
      });
      bloc.append(rel);
    }
    for (const g of c.guerre) bloc.append(el("p", "alerte", g));
    if (c.projet) {
      const p = c.projet;
      const ligne = el("p", "alerte projet");
      const instigateur = el("button", "lien", p.nomInstigateur);
      instigateur.type = "button";
      instigateur.addEventListener("click", () => this.ouvrirFiche(p.instigateur));
      ligne.append("⚔ Prépare la guerre contre ", el("strong", undefined, p.nomCible), ` depuis ${p.depuis}, poussée par `, instigateur,
        `, ${p.raison}.`, p.allies.length > 0 ? ` Alliés sollicités : ${p.allies.join(", ")}.` : " Aucun allié sollicité.",
        ` Le groupe en débattra (${p.debat}) : il peut encore y renoncer. `);
      if (p.evenement > 0) ligne.append(boutonCause(ligne, p.evenement, "le projet"));
      bloc.append(ligne);
    }
    const enCours = c.guerres.filter((g) => g.issue === null);
    if (enCours.length > 0) {
      bloc.append(el("p", "relation-en", `En guerre contre ${enCours.map((g) => `${g.nomContre} (${g.raids} raid${g.raids > 1 ? "s" : ""}, ${g.mortsSiens} morts chez elle, ${g.mortsEnnemis} chez l'ennemi)`).join(", ")}.`));
    }
    if (!c.disperseeLe) {
      bloc.append(el("p", "usage", `Usage : ${c.usage}.${c.moeurs ? ` Mœurs : ${c.moeurs}.` : ""}${c.temperament ? ` Tempérament : ${c.temperament}.` : ""}`));
      if (c.morale) bloc.append(el("p", "usage", `Morale : ${c.morale}.`));
      if (c.savoirs.length > 0) bloc.append(el("p", "usage", `Savoirs : ${c.savoirs.join(", ")}.`));
      if (c.karma !== null && c.croyance !== null) {
        const k = Math.abs(c.karma).toLocaleString("fr-FR", { maximumFractionDigits: 1 });
        const p = el("p", "usage", `Regard de l'Entité : karma moyen ${c.karma > 0 ? "+" : c.karma < 0 ? "−" : ""}${k} ; croyance moyenne en l'invisible ${c.croyance}/100.`);
        p.title = "Moyennes des adultes, invisibles aux humains";
        bloc.append(p);
      }
      if (c.ideal) bloc.append(el("p", "discret", `Trouve beaux : ${c.ideal}.`));
      if (c.soignants && c.soignants.length > 0) {
        const p = el("p", "usage", "Soignants : ");
        c.soignants.forEach((x, i) => {
          if (i > 0) p.append(", ");
          const b = el("button", "lien", x.nom);
          b.type = "button";
          b.addEventListener("click", () => this.ouvrirFiche(x.id));
          p.append(b, ` (${x.role} ; ${x.gueris} guéris, ${x.perdus} perdus)`);
        });
        p.append(".");
        bloc.append(p);
      }
      if (c.style) bloc.append(el("p", "usage", `Arts : ${c.style}`));
      if (c.oeuvres && c.oeuvres.length > 0) {
        const p = el("p", "discret", "Peint sur la roche : ");
        c.oeuvres.forEach((o, i) => {
          if (i > 0) p.append(" ; ");
          const b = el("button", "lien", o.auteur);
          b.type = "button";
          b.addEventListener("click", () => this.ouvrirFiche(o.auteurId));
          p.append(o.description, " (", b, `, ${o.le})`);
        });
        p.append(".");
        bloc.append(p);
      }
    }
    for (const x of c.exiles) {
      const ligne = el("p", "alerte");
      const qui = el("button", "lien", x.nom);
      qui.type = "button";
      qui.addEventListener("click", () => this.ouvrirFiche(x.id));
      ligne.append("A chassé ", qui, ` ${x.raison} (${x.jusqua ? `fin prévue : ${x.jusqua}` : "pour toujours"}). `);
      if (x.evenement > 0) ligne.append(boutonCause(ligne, x.evenement, "pourquoi ?"));
      bloc.append(ligne);
    }
    const figure = el("p");
    const lien = el("button", "lien", c.nomFigure);
    lien.type = "button";
    lien.addEventListener("click", () => this.ouvrirFiche(c.figure));
    figure.append("Figure centrale : ", lien);
    bloc.append(figure);
    const boutons = el("div", "boutons");
    const chrono = el("button", undefined, "Chronologie");
    chrono.type = "button";
    chrono.title = "Tout ce qui est arrivé à cette communauté, dans le journal";
    chrono.addEventListener("click", () => this.suivre(c.id, c.nom));
    const terres = el("button", undefined, "Terres");
    terres.type = "button";
    terres.title = "Ses terres sur la carte, aujourd'hui et au fil des saisons";
    terres.addEventListener("click", () => this.montrerTerres(c.id));
    boutons.append(chrono, terres);
    bloc.append(boutons);
    if (c.id === this.choisie) {
      if (c.noms.length > 1) {
        bloc.append(el("h3", undefined, "Noms successifs"));
        const ul = el("ul", "liste");
        for (const n of c.noms) {
          const li = el("li");
          li.append(el("span", "date", n.depuis), ` ${n.nom}${n.sens ? ` (« ${n.sens} »)` : " (nom donné par l'observateur)"} `);
          if (n.evenement > 0) li.append(boutonCause(li, n.evenement, "comment ?"));
          ul.append(li);
        }
        bloc.append(ul);
      }
      if (c.langue) this.langue(bloc, c.langue);
      else if (c.mots.length > 0) bloc.append(el("p", "discret", `Premiers mots : ${c.mots.join(" · ")}`));
      if (c.guerres.length > 0) {
        bloc.append(el("h3", undefined, "Guerres"));
        const ul = el("ul", "liste");
        for (const g of c.guerres) {
          const li = el("li");
          li.append(el("span", "date", g.finLe ? `${g.debut} → ${g.finLe}` : `depuis ${g.debut}`),
            ` contre ${g.nomContre} : ${g.raids} raid${g.raids > 1 ? "s" : ""}, ${g.mortsSiens} morts chez elle, ${g.mortsEnnemis} chez l'ennemi · ${g.issue ?? "en cours"} `);
          if (g.evenement > 0) li.append(boutonCause(li, g.evenement, "premier sang"));
          if (g.fin > 0) li.append(" ", boutonCause(li, g.fin, "fin"));
          ul.append(li);
        }
        bloc.append(ul);
      }
      if (this.tailles?.id === c.id && this.tailles.valeurs.length > 1) {
        bloc.append(el("h3", undefined, "Taille au fil des saisons"), courbe(this.tailles.valeurs));
      }
      const membres = el("p", "membres");
      c.membres.forEach((m, i) => {
        if (i > 0) membres.append(", ");
        const b = el("button", "lien", m.nom);
        b.type = "button";
        b.addEventListener("click", () => this.ouvrirFiche(m.id));
        membres.append(b);
      });
      bloc.append(membres);
    }
    let entite = this.entites.get(c.id);
    if (!entite) {
      entite = new FormulaireEntite(() => ({
        humains: [], communautes: [c.id], pres: c.figure, communaute: c.id, libelle: `Chaque membre de la ${c.nom.charAt(0).toLowerCase()}${c.nom.slice(1)}`,
      }));
      this.entites.set(c.id, entite);
    }
    bloc.append(entite.bloc);
    return bloc;
  }
}
