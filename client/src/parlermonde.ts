import { connexion } from "./connexion";
import { Designer, type Destinataires } from "./entite";
import { boutonCause, el } from "./fiche";
import { nombre, t } from "./langue";

interface Choix { cle: string; libelle: string }
interface Parole { cle: string; libelle: string; rubrique: string; cible: boolean; precision: string | null; modele: string; nuit: boolean }
interface Terre { masse: number; libelle: string; vivants: number }
interface ChoixMonde { paroles: Parole[]; modes: Choix[]; plantes: Choix[]; communautes: Choix[]; terres: Terre[]; vivants: number }
interface Resultat { humain: number; nom: string; resultat: string }
interface ResultatMonde { bilans: string[]; membres: Resultat[]; entendue: number; evenement: number }
export interface ParoleMonde {
  id: number; le: string; mode: string; phrase: string; portee: string; auteur: string; bilans: string[]; evenement: number; visee: boolean;
}

function ligne(libelle: string, ...champs: (HTMLElement | string)[]): HTMLLabelElement {
  const l = el("label", "ligne");
  l.append(el("span", undefined, libelle), ...champs);
  return l;
}

function remplir(select: HTMLSelectElement, choix: Choix[]): void {
  const avant = select.value;
  select.replaceChildren(...choix.map((c) => Object.assign(el("option", undefined, c.libelle), { value: c.cle })));
  if (choix.some((c) => c.cle === avant)) select.value = avant;
}

/**
 * « Parler au Monde » (0.37.1) : l'Entité dit une parole au Monde entier, ou à toute une terre. Chacun l'entend en même temps
 * et la reçoit selon son esprit ; le bilan par communauté s'affiche, puis l'historique des paroles au Monde.
 */
export class PanneauParlerMonde {
  private readonly parole = el("select");
  private readonly mode = el("select");
  private readonly terre = el("select");
  private readonly plante = el("select");
  private readonly communaute = el("select");
  private readonly lignePlante = ligne(t("entite.plante"), this.plante);
  private readonly ligneCommunaute = ligne(t("monde.qui"), this.communaute);
  private readonly qui = new Designer(() => this.destinataires(), t("entite.qui"));
  private readonly apercu = el("p", "entite");
  private readonly bouton = el("button", undefined, t("monde.parler"));
  private readonly resultat = el("div");
  private readonly historique = el("ul", "liste");
  private choix: ChoixMonde | null = null;
  private phrase = "";

  constructor(private readonly racine: HTMLElement, private readonly ouvrirHumain: (id: number) => void) {
    const tete = el("div", "tete");
    const fermer = el("button", "fermer", "×");
    fermer.type = "button";
    fermer.title = t("commun.fermer");
    fermer.addEventListener("click", () => this.fermer());
    tete.append(el("h2", undefined, t("monde.titreParler")), fermer);
    this.bouton.type = "button";
    this.bouton.addEventListener("click", () => void this.parler());
    for (const s of [this.parole, this.mode, this.terre, this.plante, this.communaute]) s.addEventListener("change", () => this.accorder());
    this.parole.addEventListener("change", () => {
      if (this.actuelle()?.cible) void this.qui.chercher();
    });
    this.qui.surChange(() => this.accorder());
    const corps = el("div", "corps");
    corps.append(el("p", "discret", t("monde.aide")), ligne(t("entite.parole"), this.parole), ligne(t("agir.comment"), this.mode),
      ligne(t("monde.portee"), this.terre), this.qui.bloc, this.lignePlante, this.ligneCommunaute, this.apercu, this.bouton, this.resultat,
      el("p", "discret", t("entite.rienGaranti")), el("h3", undefined, t("monde.historique")), this.historique);
    racine.append(tete, corps);
  }

  get ouvert(): boolean {
    return !this.racine.hidden;
  }

  basculer(): void {
    if (this.ouvert) this.fermer();
    else void this.ouvrir();
  }

  fermer(): void {
    this.racine.hidden = true;
  }

  async ouvrir(): Promise<void> {
    this.racine.hidden = false;
    await this.charger();
  }

  private destinataires(): Destinataires {
    return { humains: [], communautes: [], pres: 0, communaute: 0, libelle: t("monde.leMonde") };
  }

  private actuelle(): Parole | undefined {
    return this.choix?.paroles.find((p) => p.cle === this.parole.value);
  }

  private async charger(): Promise<void> {
    try {
      this.choix = await connexion.invoke<ChoixMonde>("ChoixParolesMonde");
    } catch {
      this.resultat.replaceChildren(el("p", "discret", t("commun.pasDeReponse")));
      return;
    }
    const c = this.choix;
    const avant = this.parole.value;
    this.parole.replaceChildren();
    for (const rubrique of [...new Set(c.paroles.map((p) => p.rubrique))]) {
      const g = el("optgroup");
      g.label = rubrique;
      for (const p of c.paroles.filter((x) => x.rubrique === rubrique)) g.append(Object.assign(el("option", undefined, p.libelle), { value: p.cle }));
      this.parole.append(g);
    }
    if (avant) this.parole.value = avant;
    remplir(this.mode, c.modes);
    if (!this.mode.value) this.mode.value = "Voix";
    remplir(this.terre, [{ cle: "0", libelle: t("monde.toutLeMonde", { n: nombre(c.vivants) }) },
      ...c.terres.map((x) => ({ cle: String(x.masse), libelle: t("monde.uneTerre", { terre: x.libelle, n: nombre(x.vivants) }) }))]);
    remplir(this.plante, c.plantes);
    remplir(this.communaute, c.communautes);
    this.accorder();
    if (this.actuelle()?.cible) void this.qui.chercher();
    await this.chargerHistorique();
  }

  private accorder(): void {
    const p = this.actuelle();
    if (!p) return;
    this.qui.bloc.hidden = !p.cible;
    this.lignePlante.hidden = p.precision !== "plante";
    this.ligneCommunaute.hidden = p.precision !== "communaute";
    const extra = p.precision === "plante" ? this.plante.selectedOptions[0]?.textContent ?? ""
      : p.precision === "communaute" ? this.communaute.selectedOptions[0]?.textContent ?? "" : "";
    this.phrase = p.modele.replace("{cible}", this.qui.nom).replace("{extra}", extra);
    this.apercu.textContent = t("monde.apercu", { portee: this.terre.selectedOptions[0]?.textContent ?? "", mode: this.mode.selectedOptions[0]?.textContent ?? "",
      phrase: this.phrase });
    this.bouton.classList.toggle("danger", p.nuit);
    this.bouton.disabled = p.precision === "communaute" && this.communaute.options.length === 0;
  }

  private async parler(): Promise<void> {
    const p = this.actuelle();
    if (!p) return;
    if (p.cible && !this.qui.valeur) {
      this.resultat.replaceChildren(el("p", "discret", t("entite.choisirPersonne")));
      return;
    }
    if (!window.confirm(t(p.nuit ? "entite.confirmer" : "monde.confirmer", { phrase: this.phrase || p.libelle }))) return;
    this.bouton.disabled = true;
    try {
      const r = await connexion.invoke<ResultatMonde>("ParlerAuMonde", {
        parole: p.cle, mode: this.mode.value, terre: Number(this.terre.value) || 0, cible: p.cible ? this.qui.valeur : 0,
        communaute: p.precision === "communaute" ? Number(this.communaute.value) : 0, param: p.precision === "plante" ? Number(this.plante.value) : 0,
      });
      const ul = el("ul", "liste");
      for (const b of r.bilans) ul.append(el("li", undefined, b));
      const details = el("details");
      details.append(el("summary", undefined, t("monde.chacun", { n: nombre(r.entendue) })));
      const membres = el("ul", "liste discret");
      for (const m of r.membres) {
        const li = el("li");
        const nom = el("button", "lien", m.nom);
        nom.type = "button";
        nom.addEventListener("click", () => this.ouvrirHumain(m.humain));
        li.append(nom, " : ", m.resultat);
        membres.append(li);
      }
      if (r.entendue > r.membres.length) membres.append(el("li", undefined, t("monde.etAutres", { n: nombre(r.entendue - r.membres.length) })));
      details.append(membres);
      this.resultat.replaceChildren(el("h4", undefined, t("monde.bilanTitre")), ul, details);
      await this.chargerHistorique();
    } catch (e) {
      this.resultat.replaceChildren(el("p", "discret",
        e instanceof Error && e.message.includes("HubException") ? e.message.split("HubException: ").pop() ?? t("commun.refuse") : t("entite.paroleEchec")));
    } finally {
      this.bouton.disabled = false;
    }
  }

  private async chargerHistorique(): Promise<void> {
    let liste: ParoleMonde[] = [];
    try {
      liste = await connexion.invoke<ParoleMonde[]>("ParolesAuMonde");
    } catch {
      return;
    }
    this.historique.replaceChildren(...(liste.length === 0 ? [el("li", "discret", t("monde.aucune"))] : liste.map((x) => ligneParoleMonde(x))));
  }
}

/** Une parole au Monde, avec ses bilans et le bouton qui ouvre sa ligne au journal (aussi sur la fiche Communauté). */
export function ligneParoleMonde(x: ParoleMonde): HTMLLIElement {
  const li = el("li", x.visee ? "alerte" : "entite");
  li.append(t("monde.ligne", { le: x.le, portee: x.portee, mode: x.mode, phrase: x.phrase, auteur: x.auteur }));
  if (x.evenement > 0) li.append(" ", boutonCause(li, x.evenement, t("commun.pourquoi")));
  if (x.bilans.length > 0) {
    const ul = el("ul", "discret");
    for (const b of x.bilans) ul.append(el("li", undefined, b));
    li.append(ul);
  }
  return li;
}
