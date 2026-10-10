import { connexion } from "./connexion";
import type { Indicateurs } from "./carte";
import { el } from "./fiche";

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
  relations: { id: number; nom: string; etat: string }[];
}

/**
 * Communautés : les groupes d'humains liés qui vivent ensemble, constatés chaque semaine par le monde.
 * Choisir une communauté entoure ses membres sur la carte.
 */
export class PanneauCommunautes {
  private choisie = 0;
  private minuteur?: number;

  constructor(
    private readonly racine: HTMLElement,
    private readonly surligner: (ids: number[]) => void,
    private readonly ouvrirFiche: (id: number) => void,
  ) {}

  basculer(): void {
    if (this.racine.hidden) void this.ouvrir();
    else this.fermer();
  }

  async ouvrir(id?: number): Promise<void> {
    if (id !== undefined) this.choisie = id;
    this.racine.hidden = false;
    await this.rafraichir();
    window.clearInterval(this.minuteur);
    this.minuteur = window.setInterval(() => void this.rafraichir(), 5000);
  }

  fermer(): void {
    window.clearInterval(this.minuteur);
    this.racine.hidden = true;
    this.choisie = 0;
    this.surligner([]);
  }

  async rafraichir(): Promise<void> {
    if (this.racine.hidden) return;
    const [liste, nature] = await Promise.all([
      connexion.invoke<Communaute[]>("Communautes").catch(() => null),
      connexion.invoke<{ indicateurs: Indicateurs }>("Nature").catch(() => null),
    ]);
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
      for (const c of dispersees.slice(0, 30)) ul.append(el("li", "discret", `${c.nom} : ${c.fondeeLe} → ${c.disperseeLe}`));
      corps.append(ul);
    }
    r.append(tete, corps);
    corps.scrollTop = defilement;

    const choisie = actives.find((c) => c.id === this.choisie);
    this.surligner(choisie ? choisie.membres.map((m) => m.id) : []);
  }

  private carte(c: Communaute): HTMLElement {
    const bloc = el("div", `communaute-carte${c.id === this.choisie ? " choisie" : ""}`);
    const titre = el("button", "titre", `${c.nom} · ${c.membres.length} membres${c.disette ? " · disette" : ""}`);
    if (c.disette) titre.classList.add("disette");
    titre.type = "button";
    titre.title = "Montrer ses membres sur la carte";
    titre.addEventListener("click", () => {
      this.choisie = this.choisie === c.id ? 0 : c.id;
      void this.rafraichir();
    });
    bloc.append(titre);
    const adultes = c.membres.filter((m) => m.etape === "adulte").length;
    const info = el("p", "discret", `Née ${c.fondeeLe} · vers (${c.x}, ${c.y}) · ${adultes} adultes, ${c.membres.length - adultes} enfants`);
    bloc.append(info);
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
    const figure = el("p");
    const lien = el("button", "lien", c.nomFigure);
    lien.type = "button";
    lien.addEventListener("click", () => this.ouvrirFiche(c.figure));
    figure.append("Figure centrale : ", lien);
    bloc.append(figure);
    if (c.id === this.choisie) {
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
    return bloc;
  }
}
