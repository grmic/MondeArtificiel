import { connexion } from "./connexion";
import { el } from "./fiche";
import { t as traduire, nombre as ecrireNombre } from "./langue";

interface SerieCommunaute {
  id: number;
  nom: string;
  active: boolean;
  pic: number;
  secondes: number[];
  membres: number[];
}

interface Repere {
  id: number;
  type: string;
  secondes: number;
  date: string;
  nom: string;
  morts: number;
  mortsSuites: number;
  entite: boolean;
}

interface Demographie {
  maintenant: number;
  date: string;
  secondesParAn: number;
  pas: number;
  secondes: number[];
  monde: number[];
  naissances: number[];
  morts: number[];
  communautes: SerieCommunaute[];
  communautesEnTout: number;
  reperes: Repere[];
}

/** Une courbe à tracer : points (temps, valeur) ; en escalier, la valeur tient jusqu'au point suivant. */
interface Trace {
  cle: string;
  nom: string;
  couleur: string;
  t: number[];
  v: number[];
  escalier?: boolean;
  aire?: boolean;
}

const SVG = "http://www.w3.org/2000/svg";
/** Dix teintes distinctes, lisibles sur fond sombre. */
const COULEURS = ["#6aa3e0", "#f5a04a", "#ef6f6c", "#7fd0c8", "#74c26a", "#f0d45a", "#c792c0", "#ffadb5", "#c29a7e", "#cfc8c4"];
const COULEUR_MONDE = "#8fd3a0";
const PERIODES: [string, number][] = [["demographie.tout", 0], ["", 50], ["", 10], ["", 2]];
const ACTUALISATION_MS = 5000;

function svg<K extends keyof SVGElementTagNameMap>(tag: K, attributs: Record<string, string | number> = {}): SVGElementTagNameMap[K] {
  const e = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attributs)) e.setAttribute(k, String(v));
  return e;
}

/** Graduations « rondes » (1, 2, 5 × 10ⁿ) entre min et max. */
function graduations(min: number, max: number, cible: number): number[] {
  if (max <= min) return [min];
  const brut = (max - min) / cible;
  const p = 10 ** Math.floor(Math.log10(brut));
  const pas = [1, 2, 5, 10].map((m) => m * p).find((x) => x >= brut) ?? 10 * p;
  const res: number[] = [];
  for (let v = Math.ceil(min / pas) * pas; v <= max + 1e-9; v += pas) res.push(v);
  return res;
}

const nombre = (n: number) => ecrireNombre(n, 2);

/**
 * Fenêtre « Démographie » : population du monde (avec naissances et morts) et des communautés les plus peuplées,
 * avec en repère les catastrophes meurtrières, les interventions de l'Entité et les guerres sanglantes.
 */
export class PanneauDemographie {
  private readonly corps = el("div", "corps");
  private donnees: Demographie | null = null;
  private periode = 0;
  private cachees = new Set<number>();
  private reperes = true;
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly ouvrirCatastrophe: (id: number) => void,
    private readonly ouvrirCommunaute: (id: number) => void,
  ) {
    const tete = el("div", "tete");
    tete.append(el("h2", undefined, traduire("demographie.titre")));
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = traduire("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(fermer);
    racine.append(tete, this.corps);
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
    if (!this.donnees) this.corps.replaceChildren(el("p", "discret", traduire("demographie.calcul")));
    await this.charger();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.charger(), ACTUALISATION_MS);
  }

  fermer(): void {
    this.racine.hidden = true;
    window.clearInterval(this.minuteur);
  }

  private async charger(): Promise<void> {
    const d = await connexion.invoke<Demographie>("Demographie", 10).catch(() => null);
    if (!this.ouvert) return;
    if (!d) {
      if (!this.donnees) this.corps.replaceChildren(el("p", "discret", traduire("commun.pasDeReponse")));
      return;
    }
    this.donnees = d;
    this.afficher();
  }

  private afficher(): void {
    const d = this.donnees!;
    const defilement = this.corps.scrollTop;
    const an = d.secondesParAn;
    const debut = this.periode > 0 ? Math.max(0, d.maintenant - this.periode * an) : 0;
    const fin = Math.max(d.maintenant, debut + d.pas);

    // Résumé : maintenant, pic, et l'année écoulée.
    const n = d.monde.length;
    const pic = Math.max(...d.monde);
    const iPic = d.monde.indexOf(pic);
    const ilYAUnAn = d.maintenant - an;
    let nees = 0;
    let mortes = 0;
    for (let i = 1; i < n; i++) {
      if (d.secondes[i] > ilYAUnAn) {
        nees += d.naissances[i];
        mortes += d.morts[i];
      }
    }
    const resume = el("p", undefined);
    resume.append(el("strong", undefined, traduire("demographie.vivants", { n: d.monde[n - 1] })), " " + traduire("demographie.le", { date: d.date }));
    resume.append(el("br"), el("span", "discret",
      traduire("demographie.pic", { pic, annee: annee(d.secondes[iPic], an), naissances: traduire("demographie.naissances", { n: nees }), morts: traduire("demographie.morts", { n: mortes }) })));

    // Choix de la période et des repères.
    const outils = el("div", "outils");
    for (const [libelle, ans] of PERIODES) {
      const b = el("button", ans === this.periode ? "actif" : undefined, libelle ? traduire(libelle) : traduire("age.ans", { n: ans }));
      b.type = "button";
      b.addEventListener("click", () => {
        this.periode = ans;
        this.afficher();
      });
      outils.append(b);
    }
    const coche = el("label", "discret");
    const caseReperes = el("input");
    caseReperes.type = "checkbox";
    caseReperes.checked = this.reperes;
    caseReperes.addEventListener("change", () => {
      this.reperes = caseReperes.checked;
      this.afficher();
    });
    coche.append(caseReperes, " " + traduire("demographie.reperes"));
    outils.append(coche);

    // Monde : population, puis naissances et morts par intervalle (regroupées en barres).
    const monde: Trace = { cle: "monde", nom: traduire("demographie.monde"), couleur: COULEUR_MONDE, t: d.secondes, v: d.monde, aire: true };
    const reperes = this.reperes ? d.reperes.filter((r) => r.secondes >= debut && r.secondes <= fin) : [];
    const graphiqueMonde = this.graphique([monde], debut, fin, an, 170, reperes, (t) => {
      const i = plusProche(d.secondes, t);
      return [traduire("demographie.bulleMonde", { date: dateCourte(d.secondes[i], an), vivants: traduire("demographie.vivants", { n: d.monde[i] }) })];
    });
    const barres = this.barres(d, debut, fin);

    // Communautés : les plus peuplées (au plus 10), en escalier entre deux saisons.
    const traces: Trace[] = d.communautes.map((c, i) => ({
      cle: String(c.id), nom: c.nom, couleur: COULEURS[i % COULEURS.length], t: c.secondes, v: c.membres, escalier: true,
    }));
    const visibles = traces.filter((t) => !this.cachees.has(Number(t.cle)));
    const graphiqueCommunautes = this.graphique(visibles, debut, fin, an, 200, reperes, (t) => {
      const lignes = visibles
        .map((tr) => ({ tr, v: valeurA(tr, t) }))
        .filter((x) => x.v !== null)
        .sort((a, b) => b.v! - a.v!)
        .map((x) => traduire("demographie.bulleCommunaute", { nom: x.tr.nom, n: x.v! }));
      return [dateCourte(t, an), ...lignes];
    });
    const legende = el("div", "legende");
    d.communautes.forEach((c, i) => {
      const item = el("span", `item${this.cachees.has(c.id) ? " cachee" : ""}`);
      const pastille = el("button", "pastille");
      pastille.type = "button";
      pastille.style.background = COULEURS[i % COULEURS.length];
      pastille.title = this.cachees.has(c.id) ? traduire("demographie.montrer") : traduire("demographie.cacher");
      pastille.addEventListener("click", () => {
        if (this.cachees.has(c.id)) this.cachees.delete(c.id);
        else this.cachees.add(c.id);
        this.afficher();
      });
      const nom = el("button", "lien", c.nom);
      nom.type = "button";
      nom.title = traduire(c.active ? "demographie.picCommunaute" : "demographie.picDispersee", { n: c.pic });
      nom.addEventListener("click", () => this.ouvrirCommunaute(c.id));
      item.append(pastille, nom);
      if (!c.active) item.append(el("span", "discret", " " + traduire("demographie.dispersee")));
      legende.append(item);
    });

    const titreCo = d.communautesEnTout > d.communautes.length
      ? traduire("demographie.plusPeuplees", { n: d.communautes.length, total: d.communautesEnTout })
      : traduire("demographie.communautes", { n: d.communautes.length });
    const parties: HTMLElement[] = [resume, outils, el("h3", undefined, traduire("demographie.population")), graphiqueMonde,
      el("h3", undefined, traduire("demographie.naissancesMorts")), barres, el("h3", undefined, titreCo)];
    if (d.communautes.length === 0) parties.push(el("p", "discret", traduire("demographie.aucune")));
    else parties.push(legende, graphiqueCommunautes);
    if (reperes.length > 0) parties.push(this.listeReperes(reperes));
    parties.push(el("p", "discret note", traduire("demographie.note")));
    this.corps.replaceChildren(...parties);
    this.corps.scrollTop = defilement;
  }

  /** Graphique en lignes, avec axe des années, graduations, repères verticaux et info-bulle au survol. */
  private graphique(traces: Trace[], debut: number, fin: number, an: number, hauteur: number, reperes: Repere[],
    bulle: (t: number) => string[]): HTMLElement {
    const boite = el("div", "graphique");
    const largeur = Math.max(280, (this.corps.clientWidth || 730) - 30);
    const marge = { g: 46, d: 10, h: 10, b: 22 };
    const l = largeur - marge.g - marge.d;
    const h = hauteur - marge.h - marge.b;
    let max = 0;
    for (const tr of traces) {
      // Les points de la période, et le dernier avant elle (la courbe part de lui).
      for (let i = 0; i < tr.t.length; i++) {
        if (tr.t[i] >= debut || (i + 1 < tr.t.length && tr.t[i + 1] > debut)) max = Math.max(max, tr.v[i]);
      }
    }
    max = Math.max(1, max * 1.08);
    const x = (t: number) => marge.g + ((t - debut) / (fin - debut)) * l;
    const y = (v: number) => marge.h + h - (v / max) * h;
    const s = svg("svg", { width: largeur, height: hauteur, viewBox: `0 0 ${largeur} ${hauteur}`, role: "img" });

    for (const v of graduations(0, max, 4)) {
      s.append(svg("line", { x1: marge.g, x2: marge.g + l, y1: y(v), y2: y(v), class: "grille" }));
      const texte = svg("text", { x: marge.g - 6, y: y(v) + 4, class: "axe", "text-anchor": "end" });
      texte.textContent = nombre(v);
      s.append(texte);
    }
    const anDebut = debut / an;
    const anFin = fin / an;
    for (const a of graduations(anDebut, anFin, Math.max(2, Math.floor(l / 90)))) {
      const t = a * an;
      s.append(svg("line", { x1: x(t), x2: x(t), y1: marge.h + h, y2: marge.h + h + 4, class: "axe-trait" }));
      const texte = svg("text", { x: x(t), y: hauteur - 6, class: "axe", "text-anchor": "middle" });
      // An 1 commence à 0 : la graduation a marque le début de l'an a + 1.
      texte.textContent = Number.isInteger(a) ? traduire("demographie.an", { an: String(a + 1) })
        : traduire("demographie.saisonAn", { saison: traduire(SAISONS[Math.min(3, Math.floor((a % 1) * 4))]), an: String(Math.floor(a) + 1) });
      s.append(texte);
    }
    s.append(svg("line", { x1: marge.g, x2: marge.g + l, y1: marge.h + h, y2: marge.h + h, class: "axe-trait" }));

    const clip = `clip-${Math.random().toString(36).slice(2)}`;
    const defs = svg("defs");
    const cp = svg("clipPath", { id: clip });
    cp.append(svg("rect", { x: marge.g, y: 0, width: l, height: hauteur }));
    defs.append(cp);
    s.append(defs);
    const zone = svg("g", { "clip-path": `url(#${clip})` });
    s.append(zone);

    for (const r of reperes) {
      const ligne = svg("line", { x1: x(r.secondes), x2: x(r.secondes), y1: marge.h, y2: marge.h + h, class: `repere ${classeRepere(r)}` });
      const titre = svg("title");
      titre.textContent = texteRepere(r);
      ligne.append(titre);
      zone.append(ligne);
    }

    for (const tr of traces) {
      const pts: string[] = [];
      for (let i = 0; i < tr.t.length; i++) {
        if (tr.escalier && i > 0) pts.push(`${x(tr.t[i]).toFixed(1)},${y(tr.v[i - 1]).toFixed(1)}`);
        pts.push(`${x(tr.t[i]).toFixed(1)},${y(tr.v[i]).toFixed(1)}`);
      }
      if (pts.length === 0) continue;
      if (tr.aire) {
        const aire = svg("polygon", {
          points: `${x(tr.t[0]).toFixed(1)},${y(0)} ${pts.join(" ")} ${x(tr.t[tr.t.length - 1]).toFixed(1)},${y(0)}`,
          fill: tr.couleur, "fill-opacity": 0.18,
        });
        zone.append(aire);
      }
      zone.append(svg("polyline", { points: pts.join(" "), fill: "none", stroke: tr.couleur, "stroke-width": 2, "stroke-linejoin": "round" }));
    }

    // Repères cliquables au-dessus de la zone : un petit triangle par événement.
    for (const r of reperes) {
      const cx = x(r.secondes);
      const tri = svg("polygon", { points: `${cx - 4},${marge.h - 8} ${cx + 4},${marge.h - 8} ${cx},${marge.h}`, class: `repere-tete ${classeRepere(r)}` });
      const titre = svg("title");
      titre.textContent = texteRepere(r);
      tri.append(titre);
      if (r.type !== "guerre") tri.addEventListener("click", () => this.ouvrirCatastrophe(r.id));
      s.append(tri);
    }

    // Survol : ligne verticale et info-bulle.
    const guide = svg("line", { y1: marge.h, y2: marge.h + h, class: "guide", visibility: "hidden" });
    s.append(guide);
    const info = el("div", "bulle");
    info.hidden = true;
    const capteur = svg("rect", { x: marge.g, y: marge.h, width: l, height: h, fill: "transparent" });
    capteur.addEventListener("mousemove", (e) => {
      const rect = s.getBoundingClientRect();
      const px = e.clientX - rect.left;
      const t = debut + ((px - marge.g) / l) * (fin - debut);
      guide.setAttribute("x1", String(px));
      guide.setAttribute("x2", String(px));
      guide.setAttribute("visibility", "visible");
      info.replaceChildren(...bulle(t).flatMap((ligne, i): (Node | string)[] => (i === 0 ? [el("strong", undefined, ligne)] : [el("br"), ligne])));
      info.hidden = false;
      info.style.left = `${Math.min(px + 12, largeur - 190)}px`;
      info.style.top = `${marge.h + 4}px`;
    });
    capteur.addEventListener("mouseleave", () => {
      guide.setAttribute("visibility", "hidden");
      info.hidden = true;
    });
    s.insertBefore(capteur, guide);
    boite.append(s, info);
    return boite;
  }

  /** Naissances (vers le haut) et morts (vers le bas) regroupées en une trentaine de barres sur la période. */
  private barres(d: Demographie, debut: number, fin: number): HTMLElement {
    const boite = el("div", "graphique");
    const largeur = Math.max(280, (this.corps.clientWidth || 730) - 30);
    const hauteur = 120;
    const marge = { g: 46, d: 10, h: 6, b: 6 };
    const l = largeur - marge.g - marge.d;
    const nb = Math.max(1, Math.min(40, Math.round((fin - debut) / d.pas)));
    const duree = (fin - debut) / nb;
    const nees = new Array<number>(nb).fill(0);
    const mortes = new Array<number>(nb).fill(0);
    for (let i = 1; i < d.secondes.length; i++) {
      const t = d.secondes[i];
      if (t <= debut || t > fin) continue;
      const k = Math.min(nb - 1, Math.floor((t - debut - 1) / duree));
      nees[k] += d.naissances[i];
      mortes[k] += d.morts[i];
    }
    const max = Math.max(1, ...nees, ...mortes);
    const milieu = marge.h + (hauteur - marge.h - marge.b) / 2;
    const demi = (hauteur - marge.h - marge.b) / 2;
    const s = svg("svg", { width: largeur, height: hauteur, viewBox: `0 0 ${largeur} ${hauteur}`, role: "img" });
    s.append(svg("line", { x1: marge.g, x2: marge.g + l, y1: milieu, y2: milieu, class: "axe-trait" }));
    for (const [v, haut] of [[max, true], [max, false]] as [number, boolean][]) {
      const texte = svg("text", { x: marge.g - 6, y: haut ? marge.h + 10 : hauteur - marge.b, class: "axe", "text-anchor": "end" });
      texte.textContent = nombre(v);
      s.append(texte);
    }
    const lb = l / nb;
    for (let k = 0; k < nb; k++) {
      const x0 = marge.g + k * lb + 1;
      const w = Math.max(1, lb - 2);
      const a = (k * duree + debut);
      const b = a + duree;
      const titre = traduire("demographie.barre", { de: dateCourte(a, d.secondesParAn), a: dateCourte(b, d.secondesParAn), naissances: traduire("demographie.naissances", { n: nees[k] }), morts: traduire("demographie.morts", { n: mortes[k] }) });
      const hn = (nees[k] / max) * demi;
      const hm = (mortes[k] / max) * demi;
      const rn = svg("rect", { x: x0, y: milieu - hn, width: w, height: Math.max(hn, 0), class: "naissances" });
      const rm = svg("rect", { x: x0, y: milieu, width: w, height: Math.max(hm, 0), class: "morts" });
      const capteur = svg("rect", { x: x0, y: marge.h, width: w, height: hauteur - marge.h - marge.b, fill: "transparent" });
      const t = svg("title");
      t.textContent = titre;
      capteur.append(t);
      s.append(rn, rm, capteur);
    }
    const legende = el("div", "legende discret");
    legende.append(el("span", "carre naissances"), " " + traduire("demographie.enHaut") + " ", el("span", "carre morts"), " " + traduire("demographie.enBas"));
    boite.append(s, legende);
    return boite;
  }

  private listeReperes(reperes: Repere[]): HTMLElement {
    const bloc = el("details", "reperes");
    bloc.append(el("summary", undefined, traduire("demographie.reperesPeriode", { n: reperes.length })));
    const ul = el("ul", "liste");
    for (const r of [...reperes].reverse()) {
      const li = el("li", classeRepere(r));
      li.append(el("span", "date", r.date), " ");
      if (r.type === "guerre") li.append(r.nom);
      else {
        const b = el("button", "lien", r.nom);
        b.type = "button";
        b.addEventListener("click", () => this.ouvrirCatastrophe(r.id));
        li.append(b);
      }
      li.append(el("span", "discret", ` · ${bilan(r)}`));
      ul.append(li);
    }
    bloc.append(ul);
    return bloc;
  }
}

function classeRepere(r: Repere): string {
  return r.type === "guerre" ? "guerre" : r.entite ? "entite" : "catastrophe";
}

function bilan(r: Repere): string {
  const parties = [r.morts ? traduire("demographie.morts", { n: r.morts }) : "",
    r.mortsSuites ? traduire("demographie.suites", { n: r.mortsSuites }) : ""].filter(Boolean);
  return parties.length > 0 ? parties.join(traduire("commun.virgule")) : traduire("demographie.aucunMort");
}

function texteRepere(r: Repere): string {
  return `${r.date}\n${r.entite ? "✦ " : ""}${r.nom}\n${bilan(r)}`;
}

function annee(t: number, an: number): string {
  return traduire("demographie.an", { an: String(Math.floor(t / an) + 1) });
}

const SAISONS = ["saison.printemps", "saison.ete", "saison.automne", "saison.hiver"];

function dateCourte(t: number, an: number): string {
  const saison = Math.min(3, Math.floor(((t % an) / an) * 4));
  return traduire("demographie.dateCourte", { annee: annee(t, an), saison: traduire(SAISONS[saison]) });
}

function plusProche(ts: number[], t: number): number {
  let bas = 0;
  let haut = ts.length - 1;
  while (bas < haut) {
    const m = (bas + haut) >> 1;
    if (ts[m] < t) bas = m + 1;
    else haut = m;
  }
  if (bas > 0 && Math.abs(ts[bas - 1] - t) < Math.abs(ts[bas] - t)) return bas - 1;
  return bas;
}

/** Valeur d'une courbe en escalier à un instant (null avant sa première mesure ou après sa dernière). */
function valeurA(tr: Trace, t: number): number | null {
  if (tr.t.length === 0 || t < tr.t[0] || t > tr.t[tr.t.length - 1]) return null;
  let i = 0;
  while (i + 1 < tr.t.length && tr.t[i + 1] <= t) i++;
  return tr.v[i];
}
