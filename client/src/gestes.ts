import { connexion } from "./connexion";
import { PROVOQUABLES } from "./catastrophes";
import { t } from "./langue";

interface Choix {
  cle: string;
  libelle: string;
}

interface Geste {
  cle: string;
  libelle: string;
  surHumain: boolean;
  bienfait: boolean;
  joursMin: number;
  joursMax: number;
  forceMin: number;
  forceMax: number;
  /** « forme », « maladie », « calamite » ou null. */
  precision: string | null;
}

interface ChoixGestes {
  gestes: Geste[];
  manieres: Choix[];
  formes: Choix[];
  maladies: Choix[];
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, classe?: string, texte?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texte !== undefined) e.textContent = texte;
  return e;
}

function remplir(select: HTMLSelectElement, choix: Choix[]): void {
  const avant = select.value;
  select.replaceChildren(...choix.map((c) => Object.assign(el("option", undefined, c.libelle), { value: c.cle })));
  if (choix.some((c) => c.cle === avant)) select.value = avant;
}

function ligne(libelle: string, ...champs: (HTMLElement | string)[]): HTMLLabelElement {
  const l = el("label", "ligne");
  l.append(el("span", undefined, libelle), ...champs);
  return l;
}

/** Force affichée : facteur (× 1,5), pourcentage (part perdue ou frappée) ou note sur 100 (bénédiction). */
function unite(g: Geste): string {
  if (g.cle === "RendreFecond" || g.cle === "Fecondite") return t("agir.unite.fecondite");
  if (g.cle === "Famine") return t("agir.unite.famine");
  if (g.cle === "Epidemie") return t("agir.unite.epidemie");
  if (g.cle === "Benir") return t("agir.unite.benir");
  return "";
}

/**
 * Gestes de l'Entité sur un humain ou une communauté : frapper, rendre malade, guérir, rendre fécond ; épidémie, calamité,
 * famine et leur fin, fécondité, bénédiction. Visible (avec un signe) ou caché (il passe pour naturel). Ceux qui le voient en
 * jugent, et leur regard sur l'invisible change.
 */
export class FormulaireGeste {
  readonly bloc = el("details", "entite-bloc");
  private readonly resultat = el("p", "discret");
  private choix: ChoixGestes | null = null;

  constructor(private readonly surHumain: boolean, private readonly cible: () => { id: number; nom: string }) {
    this.bloc.append(el("summary", "entite", surHumain ? t("agir.surHumain") : t("agir.surCommunaute")));
    const geste = el("select");
    const maniere = el("select");
    const precision = el("select");
    const lignePrecision = ligne("", precision);
    const force = el("input");
    force.type = "number";
    force.step = "0.1";
    const uniteForce = el("span", "discret");
    const ligneForce = ligne(t("agir.force"), force, uniteForce);
    const jours = el("input");
    jours.type = "number";
    jours.step = "30";
    const bornesJours = el("span", "discret");
    const ligneJours = ligne(t("agir.duree"), jours, bornesJours);
    const apercu = el("p", "entite");
    const bouton = el("button", undefined, t("agir.agir"));
    bouton.type = "button";
    const actuel = () => this.choix?.gestes.find((g) => g.cle === geste.value);
    const accorder = (nouveau: boolean) => {
      const g = actuel();
      if (!g || !this.choix) return;
      lignePrecision.hidden = !g.precision;
      const titre = lignePrecision.firstElementChild as HTMLElement;
      if (g.precision === "forme") {
        titre.textContent = t("agir.forme");
        remplir(precision, this.choix.formes);
      } else if (g.precision === "maladie") {
        titre.textContent = t("agir.maladie");
        remplir(precision, this.choix.maladies);
      } else if (g.precision === "calamite") {
        titre.textContent = t("agir.calamite");
        remplir(precision, PROVOQUABLES.filter((p) => p[0] !== "epidemie").map(([cle, libelle]) => ({ cle, libelle })));
      }
      const calamite = g.precision === "calamite" ? PROVOQUABLES.find((p) => p[0] === precision.value) : undefined;
      ligneForce.hidden = calamite ? calamite[3] === "" : g.forceMax === 0;
      if (nouveau || calamite) {
        force.value = String(calamite ? calamite[2] : g.cle === "Benir" ? 50 : g.forceMin);
        jours.value = String(g.joursMin);
      }
      uniteForce.textContent = calamite ? calamite[3] : unite(g);
      if (!calamite && g.forceMax > 0) {
        force.min = String(g.forceMin);
        force.max = String(g.forceMax);
      } else {
        force.removeAttribute("min");
        force.removeAttribute("max");
      }
      ligneJours.hidden = g.joursMax === 0;
      jours.min = String(g.joursMin);
      jours.max = String(g.joursMax);
      bornesJours.textContent = t("agir.bornesJours", { min: g.joursMin, max: g.joursMax });
      // La foudre se voit toujours ; une mort dans le sommeil, jamais.
      const forme = g.precision === "forme" ? precision.value : "";
      maniere.disabled = forme === "0" || forme === "2";
      const visible = forme === "0" || (forme !== "2" && maniere.value !== "Cachee");
      apercu.textContent = t(`agir.apercu.${g.bienfait ? "bienfait" : "chatiment"}.${visible ? "visible" : "cache"}`);
      bouton.classList.toggle("danger", !g.bienfait);
    };
    geste.addEventListener("change", () => accorder(true));
    precision.addEventListener("change", () => accorder(false));
    maniere.addEventListener("change", () => accorder(false));
    this.bloc.addEventListener("toggle", async () => {
      if (!this.bloc.open) return;
      try {
        this.choix ??= await connexion.invoke<ChoixGestes>("ChoixGestes");
      } catch {
        this.resultat.textContent = t("commun.pasDeReponse");
        return;
      }
      if (geste.options.length === 0) {
        const gestes = this.choix.gestes.filter((g) => g.surHumain === this.surHumain);
        for (const [titre, bienfait] of [[t("agir.bienfaits"), true], [t("agir.chatiments"), false]] as const) {
          const groupe = el("optgroup");
          groupe.label = titre;
          for (const g of gestes.filter((x) => x.bienfait === bienfait)) groupe.append(Object.assign(el("option", undefined, g.libelle), { value: g.cle }));
          geste.append(groupe);
        }
        remplir(maniere, this.choix.manieres);
        maniere.value = "Voix";
        accorder(true);
      } else accorder(false);
    });
    bouton.addEventListener("click", async () => {
      const g = actuel();
      const c = this.cible();
      if (!g || !c.id) return;
      if (!g.bienfait && !window.confirm(t("agir.confirmer", { geste: g.libelle.toLowerCase(), nom: c.nom }))) return;
      bouton.disabled = true;
      try {
        this.resultat.textContent = await connexion.invoke<string>("Agir", {
          geste: g.cle,
          cible: c.id,
          maniere: maniere.disabled ? "Cachee" : maniere.value,
          param: g.precision === "forme" || g.precision === "maladie" ? Number(precision.value) : 0,
          calamite: g.precision === "calamite" ? precision.value : null,
          force: Number(force.value) || 0,
          jours: Number(jours.value) || 0,
        });
      } catch (e) {
        this.resultat.textContent = e instanceof Error && e.message.includes("HubException") ? e.message.split("HubException: ").pop() ?? t("commun.refuse") : t("agir.echec");
      } finally {
        bouton.disabled = false;
      }
    });
    this.bloc.append(ligne(t("agir.geste"), geste), lignePrecision, ligne(t("agir.comment"), maniere), ligneForce, ligneJours, apercu, bouton, this.resultat,
      el("p", "discret", t("agir.aide")));
  }

  /** Une autre cible : on referme le formulaire et l'on efface la réponse d'avant. */
  oublier(): void {
    this.bloc.open = false;
    this.resultat.textContent = "";
  }

  /** Vrai si l'on est en train de remplir le formulaire (il ne faut pas le redessiner). */
  get enCours(): boolean {
    return this.bloc.contains(document.activeElement) || this.bloc.matches(":hover");
  }
}
