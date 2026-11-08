import { connexion } from "./connexion";
import { el } from "./fiche";
import { nombre, t } from "./langue";

/** Réglage d'un LLM (local ou cloud, 0.38.0). Prix en dollars par million de jetons (0 : inconnu). */
export interface ConnexionLlm { fournisseur: string; adresse: string; modele: string; contexte: number; prixLu: number; prixEcrit: number }
export interface PrixModele { fournisseur: string; modele: string; lu: number; ecrit: number; conseille: boolean }
export interface Depenses {
  mois: string; dollars: number; appels: number; jetonsLus: number; jetonsEcrits: number; sansPrix: number; plafond: number;
  atteint: boolean; alerte: boolean; compris: boolean;
}
/** Ce que le serveur dit du cloud : clés enregistrées (résumées), prix connus, dépense du mois ; local : navigateur sur la machine du serveur. */
export interface InfosCloud { cles: Record<string, string>; prix: PrixModele[]; depenses: Depenses | null; local: boolean }

const LOCAUX: [string, string][] = [["ollama", "Ollama"], ["lmstudio", "LM Studio"], ["llamacpp", "llama.cpp"]];
const CLOUDS: [string, string][] = [["openai", "OpenAI (ChatGPT)"], ["anthropic", "Anthropic (Claude)"], ["mistral", "Mistral"],
  ["infomaniak", "Infomaniak (Euria)"], ["autre", "ellm.cloud.autre"]];
const ADRESSES: Record<string, string> = {
  ollama: "http://localhost:11434", lmstudio: "http://localhost:1234", llamacpp: "http://localhost:8080",
  openai: "https://api.openai.com", anthropic: "https://api.anthropic.com", mistral: "https://api.mistral.ai",
  infomaniak: "https://api.infomaniak.com/2/ai/NUMERO/openai", autre: "https://",
};
/** Adresse imposée : la clé ne part que vers elle. */
const IMPOSEES = new Set(["openai", "anthropic", "mistral"]);
const OU_CREER_CLE: Record<string, string> = {
  openai: "platform.openai.com/api-keys", anthropic: "platform.claude.com/settings/keys", mistral: "console.mistral.ai/api-keys",
  infomaniak: "manager.infomaniak.com (AI Tools)",
};

/** Un échange type de l'Entité : environ 8 000 jetons lus et 800 écrits (mesure des propositions 0.38). */
export const LUS_PAR_ECHANGE = 8000;
export const ECRITS_PAR_ECHANGE = 800;

export const estCloud = (f: string): boolean => CLOUDS.some(([c]) => c === f);

/** Un montant en dollars, lisible. */
export function dollars(d: number): string {
  return (d === 0 || d >= 0.1 ? nombre(d, 2) : nombre(d, d < 0.01 ? 4 : 3)) + " $";
}

/** Coût estimé de 100 échanges avec ces prix (dollars). */
export function coutCent(c: ConnexionLlm): number {
  return 100 * (LUS_PAR_ECHANGE * c.prixLu + ECRITS_PAR_ECHANGE * c.prixEcrit) / 1_000_000;
}

function nomFournisseur(f: string): string {
  const n = [...LOCAUX, ...CLOUDS].find(([c]) => c === f)?.[1] ?? f;
  return n.startsWith("ellm.") ? t(n) : n;
}

export function message(e: unknown): string {
  return e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e);
}

function bouton(texte: string, action: () => void, classe?: string): HTMLButtonElement {
  const b = el("button", classe, texte);
  b.type = "button";
  b.addEventListener("click", action);
  return b;
}

function ligne(libelle: string, ...champs: (HTMLElement | string)[]): HTMLLabelElement {
  const l = el("label", "ligne");
  l.append(el("span", undefined, libelle), " ", ...champs);
  return l;
}

function champPrix(): HTMLInputElement {
  const i = el("input");
  i.type = "number";
  i.min = "0";
  i.max = "1000";
  i.step = "0.01";
  i.className = "nombre-court";
  return i;
}

/**
 * Formulaire d'un LLM : logiciel local ou service du cloud, adresse, modèle, contexte ; pour le cloud, prix, clé d'API (jamais
 * renvoyée par le serveur), coût estimé et avertissement à la première fois (E1). Sert au LLM de l'Entité et à celui du juge (G2).
 */
export class FormulaireConnexion {
  readonly racine = el("div", "formulaire-llm");
  private readonly fournisseur = el("select");
  private readonly adresse = el("input");
  private readonly produit = el("input");
  private readonly ligneProduit: HTMLLabelElement;
  private readonly modele = el("input");
  private readonly modeles = el("datalist");
  private readonly contexte = el("select");
  private readonly prixLu = champPrix();
  private readonly prixEcrit = champPrix();
  private readonly cle = el("input");
  private readonly cleResume = el("span", "discret");
  private readonly cleAide = el("p", "discret");
  private readonly cout = el("p", "cout-llm");
  private readonly cloud = el("div", "seulement-cloud");
  private readonly avertissement = el("div", "avertissement-cloud");
  private readonly compris: HTMLInputElement;
  private readonly retour = el("p", "discret");
  private infos: InfosCloud = { cles: {}, prix: [], depenses: null, local: true };

  constructor(id: string, private readonly enregistrer: (c: ConnexionLlm) => Promise<void>, private readonly rafraichir: () => Promise<void>) {
    const groupe = (libelle: string, liste: [string, string][]) => {
      const g = el("optgroup");
      g.label = libelle;
      for (const [cle, nom] of liste) g.append(Object.assign(el("option", undefined, nom.startsWith("ellm.") ? t(nom) : nom), { value: cle }));
      return g;
    };
    this.fournisseur.append(groupe(t("ellm.cloud.locaux"), LOCAUX), groupe(t("ellm.cloud.clouds"), CLOUDS));
    this.fournisseur.addEventListener("change", () => this.changerFournisseur());
    this.adresse.type = "url";
    this.adresse.size = 28;
    this.produit.inputMode = "numeric";
    this.produit.size = 10;
    this.produit.addEventListener("input", () => {
      const n = this.produit.value.replace(/\D/g, "");
      this.adresse.value = ADRESSES.infomaniak.replace("NUMERO", n || "NUMERO");
    });
    this.ligneProduit = ligne(t("ellm.cloud.produit"), this.produit);
    this.modeles.id = `listeModeles-${id}`;
    this.modele.setAttribute("list", this.modeles.id);
    this.modele.size = 28;
    this.modele.placeholder = t("ellm.modeleExemple");
    this.modele.addEventListener("change", () => this.prixDuModele(true));
    for (const n of [8192, 16384, 32768, 65536, 131072]) this.contexte.append(Object.assign(el("option", undefined, nombre(n)), { value: String(n) }));
    for (const p of [this.prixLu, this.prixEcrit]) p.addEventListener("input", () => this.montrerCout());
    this.cle.type = "password";
    this.cle.autocomplete = "off";
    this.cle.size = 28;
    this.cle.placeholder = t("ellm.cloud.clePlaceholder");

    this.cloud.append(
      ligne(t("ellm.cloud.prix"), this.prixLu, " / ", this.prixEcrit, " " + t("ellm.cloud.prixUnite")),
      el("p", "discret", t("ellm.cloud.prixAide")),
      ligne(t("ellm.cloud.cle"), this.cle, bouton(t("ellm.cloud.cleEnregistrer"), () => void this.enregistrerCle()), " ",
        bouton(t("ellm.cloud.cleEffacer"), () => void this.effacerCle(), "lien")),
      this.cleResume, this.cleAide, this.cout);

    const lc = el("label", "case-config");
    this.compris = el("input");
    this.compris.type = "checkbox";
    lc.append(this.compris, " " + t("ellm.cloud.compris"));
    this.avertissement.hidden = true;
    this.avertissement.append(el("h4", undefined, t("ellm.cloud.avertTitre")), el("p", undefined, t("ellm.cloud.avertDonnees")),
      el("p", "avert-cout"), el("p", undefined, t("ellm.cloud.avertConfidentialite")), lc, el("br"),
      bouton(t("ellm.cloud.continuer"), () => void this.continuer()), " ", bouton(t("commun.annuler"), () => { this.avertissement.hidden = true; }, "lien"));

    this.racine.append(
      ligne(t("ellm.fournisseur"), this.fournisseur), ligne(t("ellm.adresse"), this.adresse), this.ligneProduit,
      ligne(t("ellm.modele"), this.modele, this.modeles),
      ligne(t("ellm.contexte"), this.contexte, " " + t("ellm.jetons")), el("p", "discret", t("ellm.contexteAide")),
      this.cloud,
      bouton(t("ellm.tester"), () => void this.tester()), " ", bouton(t("ellm.enregistrer"), () => void this.sauver()),
      this.avertissement, this.retour);
  }

  /** Remplit le formulaire avec une connexion gardée. */
  remplir(c: ConnexionLlm, infos: InfosCloud): void {
    this.infos = infos;
    this.fournisseur.value = c.fournisseur;
    this.adresse.value = c.adresse;
    this.modele.value = c.modele;
    const ctx = String(c.contexte || 16384);
    if (![...this.contexte.options].some((o) => o.value === ctx)) this.contexte.append(Object.assign(el("option", undefined, nombre(Number(ctx))), { value: ctx }));
    this.contexte.value = ctx;
    this.prixLu.value = c.prixLu ? String(c.prixLu) : "";
    this.prixEcrit.value = c.prixEcrit ? String(c.prixEcrit) : "";
    const m = /\/ai\/(\d+)\//.exec(c.adresse);
    this.produit.value = m ? m[1] : "";
    this.majCloud();
  }

  /** Nouvelles infos du serveur (clés, dépense) sans toucher à ce qu'on est en train de saisir. */
  majInfos(infos: InfosCloud): void {
    this.infos = infos;
    this.majCloud();
  }

  lire(): ConnexionLlm {
    return {
      fournisseur: this.fournisseur.value, adresse: this.adresse.value.trim(), modele: this.modele.value.trim(),
      contexte: Number(this.contexte.value) || 16384, prixLu: Number(this.prixLu.value) || 0, prixEcrit: Number(this.prixEcrit.value) || 0,
    };
  }

  private changerFournisseur(): void {
    const f = this.fournisseur.value;
    this.adresse.value = ADRESSES[f] ?? "";
    this.produit.value = "";
    // F1 : un modèle bon marché pré-rempli pour chaque service du cloud.
    if (estCloud(f)) {
      this.modele.value = this.infos.prix.find((p) => p.fournisseur === f && p.conseille)?.modele ?? "";
      this.prixLu.value = "";
      this.prixEcrit.value = "";
      this.prixDuModele(true);
    }
    this.modeles.replaceChildren(...this.infos.prix.filter((p) => p.fournisseur === f).map((p) => Object.assign(el("option"), { value: p.modele })));
    this.majCloud();
  }

  /** Reprend le prix connu du modèle choisi (si on en connaît un). */
  private prixDuModele(remplacer: boolean): void {
    const p = this.infos.prix.find((x) => x.fournisseur === this.fournisseur.value && x.modele.toLowerCase() === this.modele.value.trim().toLowerCase());
    if (p && (remplacer || !this.prixLu.value)) {
      this.prixLu.value = String(p.lu);
      this.prixEcrit.value = String(p.ecrit);
    }
    this.montrerCout();
  }

  private majCloud(): void {
    const f = this.fournisseur.value;
    const cloud = estCloud(f);
    this.cloud.hidden = !cloud;
    this.ligneProduit.hidden = f !== "infomaniak";
    this.adresse.readOnly = IMPOSEES.has(f) || f === "infomaniak";
    const local = this.infos.local;
    this.cle.disabled = !local;
    this.cleResume.textContent = this.infos.cles[f] ? t("ellm.cloud.cleGardee", { cle: this.infos.cles[f] }) : t("ellm.cloud.cleAucune");
    this.cleAide.textContent = !local ? t("ellm.cloud.distant")
      : OU_CREER_CLE[f] ? t("ellm.cloud.ouCreer", { ou: OU_CREER_CLE[f] }) : t("ellm.cloud.autreAide");
    this.montrerCout();
  }

  private montrerCout(): void {
    const c = this.lire();
    this.cout.classList.remove("inconnu");
    if (!estCloud(c.fournisseur)) {
      this.cout.textContent = "";
      return;
    }
    if (c.prixLu <= 0 && c.prixEcrit <= 0) {
      this.cout.classList.add("inconnu");
      this.cout.textContent = t("ellm.cloud.coutInconnu");
      return;
    }
    this.cout.textContent = t("ellm.cloud.cout", { cout: dollars(coutCent(c)), lus: nombre(LUS_PAR_ECHANGE), ecrits: nombre(ECRITS_PAR_ECHANGE) });
  }

  private async tester(): Promise<void> {
    this.retour.textContent = t("ellm.test.enCours");
    try {
      const liste = await connexion.invoke<string[]>("ModelesLlm", this.lire());
      this.modeles.replaceChildren(...liste.map((m) => Object.assign(el("option"), { value: m })));
      this.retour.textContent = liste.length > 0 ? t("ellm.test.ok", { n: liste.length, liste: liste.slice(0, 40).join(", ") }) : t("ellm.test.aucun");
      if (!this.modele.value && liste.length > 0) this.modele.value = liste[0];
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  /** Enregistre ; un service du cloud passe d'abord par l'avertissement, la première fois (E1). */
  private async sauver(): Promise<void> {
    const c = this.lire();
    if (estCloud(c.fournisseur) && !this.infos.depenses?.compris) {
      this.compris.checked = false;
      const plafond = this.infos.depenses?.plafond ?? 5;
      this.avertissement.querySelector(".avert-cout")!.textContent = t("ellm.cloud.avertCout", {
        fournisseur: nomFournisseur(c.fournisseur),
        cout: c.prixLu > 0 || c.prixEcrit > 0 ? dollars(coutCent(c)) : t("ellm.cloud.prixInconnu"),
        plafond: plafond > 0 ? dollars(plafond) : t("ellm.cloud.sansPlafond"),
      });
      this.avertissement.hidden = false;
      return;
    }
    await this.envoyer(c);
  }

  private async continuer(): Promise<void> {
    if (!this.compris.checked) {
      this.retour.textContent = t("ellm.cloud.cocher");
      return;
    }
    try {
      await connexion.invoke("ComprendreCloudLlm");
      if (this.infos.depenses) this.infos.depenses.compris = true;
      this.avertissement.hidden = true;
      await this.envoyer(this.lire());
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  private async envoyer(c: ConnexionLlm): Promise<void> {
    try {
      await this.enregistrer(c);
      this.retour.textContent = t("ellm.enregistre");
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  private async enregistrerCle(): Promise<void> {
    const cle = this.cle.value.trim();
    if (!cle) return;
    try {
      await connexion.invoke("EnregistrerCleLlm", this.fournisseur.value, cle, this.adresse.value.trim());
      this.cle.value = "";
      this.retour.textContent = t("ellm.cloud.cleEnregistree");
      await this.rafraichir();
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }

  private async effacerCle(): Promise<void> {
    try {
      await connexion.invoke("EffacerCleLlm", this.fournisseur.value);
      this.retour.textContent = t("ellm.cloud.cleEffacee");
      await this.rafraichir();
    } catch (e) {
      this.retour.textContent = message(e);
    }
  }
}

/** Dépense du mois et plafond (C1), avec l'alerte à 80 % et au plafond. */
export class BlocDepenses {
  readonly racine = el("section", "bloc-llm depenses-llm");
  private readonly resume = el("p");
  private readonly alerte = el("p", "alerte-depense");
  private readonly plafond = champPrix();
  private readonly retour = el("span", "discret");

  constructor(private readonly apres: () => Promise<void>) {
    this.plafond.max = "10000";
    this.plafond.step = "1";
    this.racine.append(el("h3", undefined, t("ellm.dep.titre")), el("p", "discret", t("ellm.dep.aide")), this.resume, this.alerte,
      ligne(t("ellm.dep.plafond"), this.plafond, " $ ", bouton(t("ellm.enregistrer"), () => void this.regler()), this.retour),
      el("p", "discret", t("ellm.dep.plafondAide")));
  }

  montrer(d: Depenses | null, local: boolean, visible: boolean): void {
    this.racine.hidden = !visible || !d;
    if (!d) return;
    this.resume.textContent = t("ellm.dep.resume", {
      mois: d.mois, depense: dollars(d.dollars), appels: nombre(d.appels), lus: nombre(d.jetonsLus), ecrits: nombre(d.jetonsEcrits),
      plafond: d.plafond > 0 ? dollars(d.plafond) : t("ellm.cloud.sansPlafond"),
    });
    const morceaux: string[] = [];
    if (d.atteint) morceaux.push(t("ellm.dep.atteint"));
    else if (d.alerte) morceaux.push(t("ellm.dep.alerte", { part: nombre(Math.floor(100 * d.dollars / d.plafond)) }));
    if (d.sansPrix > 0) morceaux.push(t("ellm.dep.sansPrix", { n: d.sansPrix }));
    this.alerte.textContent = morceaux.join(" ");
    this.alerte.hidden = morceaux.length === 0;
    this.alerte.classList.toggle("grave", d.atteint || d.alerte);
    if (document.activeElement !== this.plafond) this.plafond.value = String(d.plafond);
    this.plafond.disabled = !local;
    this.retour.textContent = local ? "" : " " + t("ellm.cloud.distant");
  }

  private async regler(): Promise<void> {
    try {
      await connexion.invoke("ReglerPlafondLlm", Math.max(0, Number(this.plafond.value) || 0));
      this.retour.textContent = " " + t("ellm.enregistre");
      await this.apres();
    } catch (e) {
      this.retour.textContent = " " + message(e);
    }
  }
}
