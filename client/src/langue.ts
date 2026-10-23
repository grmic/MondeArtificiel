/**
 * Langue de l'interface. Les textes viennent des fichiers du serveur (dossier `langues/`, un fichier par langue :
 * fr.json, en.json…). Une clé absente de la langue choisie s'affiche en français. La langue choisie est retenue
 * par le navigateur ; à la première visite, celle du navigateur si elle existe, sinon le français.
 */

/** Une entrée du fichier : un texte, ou ses formes selon le pluriel (one, other…) ou le genre (m, f). */
type Entree = string | { [forme: string]: Entree };
type Catalogue = Record<string, Entree>;

/** Une langue proposée dans Configuration. `part` : part des textes français traduits (0 à 1). */
export interface LangueDisponible {
  code: string;
  nom: string;
  part: number;
}

const CLE_LANGUE = "monde.langue";
const FRANCAIS = "fr";
const FORMES_PLURIEL = new Set(["zero", "one", "two", "few", "many", "other"]);

let catalogue: Catalogue = {};
let repli: Catalogue = {};
let code = FRANCAIS;
let localeCourante = "fr-FR";
let pluriels = new Intl.PluralRules(localeCourante);
let disponibles: LangueDisponible[] = [];

/** Code de la langue de l'interface (« fr », « en »…). */
export const langue = (): string => code;

/** Locale des nombres et des tris (« fr-FR », « en-GB »…). */
export const locale = (): string => localeCourante;

export const languesDisponibles = (): readonly LangueDisponible[] => disponibles;

function lireChoix(): string | null {
  try {
    return localStorage.getItem(CLE_LANGUE);
  } catch {
    return null;
  }
}

/** Retient la langue choisie, puis recharge la page dans cette langue (le monde continue côté serveur). */
export function choisirLangue(nouvelle: string): void {
  try {
    localStorage.setItem(CLE_LANGUE, nouvelle);
  } catch {
    // Stockage indisponible : la langue ne vaut que jusqu'au prochain chargement.
  }
  const adresse = new URL(window.location.href);
  adresse.searchParams.set("langue", nouvelle);
  window.location.replace(adresse.toString());
}

async function lireCatalogue(langueVoulue: string): Promise<Catalogue> {
  const reponse = await fetch(`/api/langues/${encodeURIComponent(langueVoulue)}`);
  if (!reponse.ok) throw new Error(`HTTP ${reponse.status}`);
  return (await reponse.json()) as Catalogue;
}

/** Charge la liste des langues et les textes de la langue choisie (à appeler avant de construire la page). */
export async function chargerLangue(): Promise<void> {
  try {
    const reponse = await fetch("/api/langues");
    if (reponse.ok) disponibles = (await reponse.json()) as LangueDisponible[];
  } catch {
    disponibles = [];
  }
  const codes = new Set(disponibles.map((l) => l.code));
  const demandee = new URL(window.location.href).searchParams.get("langue");
  const navigateur = (navigator.languages ?? [navigator.language]).map((l) => l.toLowerCase());
  const candidats = [demandee, lireChoix(), ...navigateur, ...navigateur.map((l) => l.split("-")[0])];
  code = candidats.find((c): c is string => !!c && codes.has(c)) ?? FRANCAIS;
  try {
    repli = await lireCatalogue(FRANCAIS);
    catalogue = code === FRANCAIS ? repli : await lireCatalogue(code);
  } catch {
    // Serveur injoignable au chargement : les clés restent vides, la page affiche le français de secours.
    catalogue = repli;
  }
  const entete = catalogue["_langue"];
  const voulue = typeof entete === "object" && typeof entete.locale === "string" ? entete.locale : code;
  try {
    pluriels = new Intl.PluralRules(voulue);
    localeCourante = voulue;
  } catch {
    localeCourante = "fr-FR";
    pluriels = new Intl.PluralRules(localeCourante);
  }
  document.documentElement.lang = code;
}

/** Paramètres d'un texte : `n` choisit la forme du pluriel, `g` (« m », « f ») celle du genre. */
export type Parametres = Record<string, string | number | null | undefined>;

function choisirForme(entree: Entree, p: Parametres | undefined): string | undefined {
  let e: Entree | undefined = entree;
  for (let profondeur = 0; profondeur < 3 && e !== undefined && typeof e === "object"; profondeur++) {
    const formes: Record<string, Entree> = e;
    const cles = Object.keys(formes);
    if (cles.some((c) => FORMES_PLURIEL.has(c))) {
      const n = Number(p?.n ?? 0);
      // « zero », s'il est écrit, vaut pour 0 dans toutes les langues.
      e = (n === 0 ? formes.zero : undefined) ?? formes[pluriels.select(Number.isFinite(n) ? n : 0)] ?? formes.other;
    } else {
      const g = String(p?.g ?? "m");
      e = formes[g] ?? formes.m ?? formes[cles[0]];
    }
  }
  return typeof e === "string" ? e : undefined;
}

/** Nombre écrit selon la langue (espace ou virgule des milliers…). */
export function nombre(v: number, decimales = 0): string {
  return v.toLocaleString(localeCourante, { maximumFractionDigits: decimales }).replace(/ | /g, " ");
}

/**
 * Texte de la clé dans la langue de l'interface, ses accolades remplacées par les paramètres
 * (les nombres sont écrits selon la langue). Une clé inconnue de la langue choisie prend le texte français.
 */
export function t(cle: string, p?: Parametres): string {
  const entree = catalogue[cle] ?? repli[cle];
  const modele = entree === undefined ? undefined : choisirForme(entree, p) ?? (repli[cle] !== undefined ? choisirForme(repli[cle], p) : undefined);
  if (modele === undefined) return cle;
  return modele.replace(/\{(\w+)\}/g, (tout, nom: string) => {
    const v = p?.[nom];
    if (v === undefined || v === null) return tout;
    return typeof v === "number" ? nombre(v, 2) : v;
  });
}

/** Vrai si la clé existe (dans la langue choisie ou en français). */
export const existe = (cle: string): boolean => cle in catalogue || cle in repli;

/**
 * Traduit les éléments fixes de la page : `data-t` (texte), `data-t-title`, `data-t-aria-label`,
 * `data-t-placeholder` (attributs).
 */
export function traduirePage(racine: ParentNode = document): void {
  for (const e of racine.querySelectorAll<HTMLElement>("[data-t]")) e.textContent = t(e.dataset.t!);
  for (const [attribut, donnee] of [["title", "tTitle"], ["aria-label", "tAriaLabel"], ["placeholder", "tPlaceholder"]] as const) {
    for (const e of racine.querySelectorAll<HTMLElement>(`[data-t-${attribut}]`)) e.setAttribute(attribut, t(e.dataset[donnee]!));
  }
}
