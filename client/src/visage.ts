// Portrait d'un humain (0.36.1), dessiné en SVG à partir de ses traits hérités et de ce que la vie y met (âge, faim,
// maladie, blessure, humeur). Le même humain est toujours dessiné pareil : seuls l'âge et l'état du moment le changent.
import { connexion } from "./connexion";
import { t } from "./langue";

export interface Visage {
  id: number;
  /** Traits en écarts-types, dans l'ordre de TraitVisage côté serveur. */
  traits: number[];
  sexe: "femme" | "homme";
  age: number;
  teint: number;
  symetrie: number;
  harmonie: number;
  corpulence: number;
  maigreur: number;
  paleur: number;
  fievre: boolean;
  humeur: "neutre" | "joie" | "triste" | "colere" | "peur";
  cicatrice: boolean;
  plaie: boolean;
  mort: boolean;
  gris: number;
  calvitie: number;
  coupe: number;
}

const enum T {
  Largeur, Longueur, Machoire, Pommettes, EcartYeux, TailleYeux, FormeYeux, CouleurYeux, LongueurNez, LargeurNez,
  Levres, Sourcils, Oreilles, CouleurCheveux, TextureCheveux, Calvitie, Barbe, Grisonnement,
}

const NS = "http://www.w3.org/2000/svg";
let numero = 0;

export type Rgb = [number, number, number];

function hex(c: string): Rgb {
  return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)];
}

function css(c: Rgb): string {
  return `rgb(${c.map((v) => Math.round(Math.max(0, Math.min(255, v)))).join(",")})`;
}

function melange(a: Rgb, b: Rgb, x: number): Rgb {
  const k = Math.max(0, Math.min(1, x));
  return [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
}

/** Une couleur le long d'une gamme, x de 0 à 1. */
function gamme(couleurs: string[], x: number): Rgb {
  const k = Math.max(0, Math.min(1, x)) * (couleurs.length - 1);
  const i = Math.min(couleurs.length - 2, Math.floor(k));
  return melange(hex(couleurs[i]), hex(couleurs[i + 1]), k - i);
}

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));
/** Un trait en écarts-types ramené à -1..1 (au-delà de 2,5 écarts-types, on plafonne). */
const z = (v: Visage, k: T) => clamp((v.traits[k] ?? 0) / 2.5, -1, 1);

function noeud<K extends keyof SVGElementTagNameMap>(nom: K, attributs: Record<string, string | number>, parent?: SVGElement): SVGElementTagNameMap[K] {
  const e = document.createElementNS(NS, nom);
  for (const [k, val] of Object.entries(attributs)) e.setAttribute(k, String(val));
  parent?.append(e);
  return e;
}

/** Couleurs de la peau, des cheveux et des yeux. Cheveux et yeux sont d'autant plus sombres que la peau l'est. */
export function couleurs(v: Visage): { peau: Rgb; ombre: Rgb; cheveux: Rgb; yeux: Rgb; levres: Rgb } {
  let peau = gamme(["#f3d9c6", "#e2b48f", "#c68a5e", "#8d5a3b", "#4f3122"], v.teint);
  // La pâleur de la maladie tire vers le gris ; la fièvre rosit les joues (voir plus bas).
  peau = melange(peau, [214, 206, 196], v.paleur * 0.45);
  const ombre = melange(peau, [60, 35, 25], 0.28);
  const sombre = Math.pow(1 - v.teint, 1.4);
  const clair = clamp(0.5 + 0.22 * (v.traits[T.CouleurCheveux] ?? 0), 0, 1) * sombre;
  let cheveux = v.traits[T.CouleurCheveux] > 1.8 && v.teint < 0.45
    ? gamme(["#6b3018", "#a4502a"], clair) // roux, rare
    : gamme(["#18110d", "#332116", "#5c3b22", "#8d6238", "#c8a466", "#e2cf98"], clair);
  cheveux = melange(cheveux, [205, 203, 198], v.gris);
  const yeux = gamme(["#24160d", "#4a2e19", "#6f5a2a", "#5e7646", "#5b7fa6"], clamp(0.45 + 0.25 * (v.traits[T.CouleurYeux] ?? 0), 0, 1) * Math.pow(1 - v.teint, 1.6));
  const levres = melange(peau, [150, 62, 60], 0.35 + 0.1 * (1 - v.teint));
  return { peau, ombre, cheveux, yeux, levres };
}

/**
 * Dessine le portrait. <taille> : largeur en pixels (la hauteur suit). Le dessin se fait dans un repère de 200 × 230 :
 * le centre du visage est en (100, 112).
 */
export function dessinerVisage(v: Visage, taille = 160): SVGSVGElement {
  const id = `v${++numero}`;
  const svg = noeud("svg", { viewBox: "15 22 170 196", width: taille, height: Math.round(taille * 1.15), class: "visage", role: "img" });
  svg.setAttribute("aria-label", t("visage.aide"));
  const c = couleurs(v);
  const defs = noeud("defs", {}, svg);
  const degrade = noeud("radialGradient", { id: `${id}p`, cx: "45%", cy: "40%", r: "65%" }, defs);
  noeud("stop", { offset: "0%", "stop-color": css(melange(c.peau, [255, 240, 225], 0.12)) }, degrade);
  noeud("stop", { offset: "100%", "stop-color": css(c.ombre) }, degrade);
  // Un petit enfant est dessiné plus petit dans le cadre.
  const echelle = 1 - 0.2 * clamp((10 - v.age) / 9, 0, 1);
  const cadre: Record<string, string | number> = { transform: `translate(100 140) scale(${echelle.toFixed(3)}) translate(-100 -140)` };
  if (v.mort) Object.assign(cadre, { filter: `url(#${id}m)`, opacity: 0.75 });
  const g = noeud("g", cadre, svg);
  if (v.mort) {
    const f = noeud("filter", { id: `${id}m` }, defs);
    noeud("feColorMatrix", { type: "saturate", values: 0.1 }, f);
  }

  // Proportions : l'enfant a un visage rond, de grands yeux, un petit nez ; la face s'allonge jusqu'à 18 ans environ.
  const enfance = clamp((18 - v.age) / 16, 0, 1);
  const homme = v.sexe === "homme" && v.age >= 13 ? clamp((v.age - 12) / 6, 0, 1) : 0;
  const harmonie = 1 - 0.35 * clamp(v.harmonie, 0, 1); // les traits harmonieux restent près de la moyenne
  const zz = (k: T) => z(v, k) * harmonie;
  const asym = (1 - clamp(v.symetrie, 0, 1)) * 4;
  const cx = 100, cy = 112;
  const demiLargeur = 46 * (1 + 0.12 * zz(T.Largeur) + 0.1 * (v.corpulence - 0.5) - 0.08 * v.maigreur + 0.06 * enfance + 0.03 * homme);
  const hauteur = 62 * (1 + 0.1 * zz(T.Longueur) - 0.2 * enfance + 0.03 * homme);
  const menton = 0.45 + 0.28 * zz(T.Machoire) + 0.12 * homme - 0.1 * enfance; // 0 pointu, 1 carré
  const pommettes = 1 + 0.1 * zz(T.Pommettes) - 0.1 * v.maigreur;
  const haut = cy - hauteur, bas = cy + hauteur;

  // Le buste, couvert d'une peau de bête.
  noeud("path", { d: `M${cx - 16},${bas - 12} L${cx - 14},${bas + 20} L${cx + 14},${bas + 20} L${cx + 16},${bas - 12} Z`, fill: css(c.ombre) }, g);
  noeud("path", { d: `M20,230 Q${cx - 60},${bas + 14} ${cx - 18},${bas + 16} Q${cx},${bas + 26} ${cx + 18},${bas + 16} Q${cx + 60},${bas + 14} 180,230 Z`, fill: "#7a5638" }, g);
  noeud("path", { d: `M${cx - 30},${bas + 22} q8,10 2,24 M${cx + 34},${bas + 20} q-6,12 0,26`, stroke: "#5e402a", "stroke-width": 2, fill: "none" }, g);

  // Cheveux de derrière (cheveux longs), sauf si la calvitie les a pris.
  const longueur = v.coupe * (1 - 0.6 * v.calvitie) * (v.age < 2 ? 0.2 : 1);
  const crepus = clamp(0.5 + 0.3 * (v.traits[T.TextureCheveux] ?? 0) + 0.35 * (v.teint - 0.5), 0, 1);
  if (longueur > 0.3) {
    const descente = cy + 10 + longueur * 90;
    const ampleur = demiLargeur + 8 + crepus * 14;
    noeud("path", { d: `M${cx - ampleur},${cy - 20} Q${cx - ampleur - 6},${descente - 20} ${cx - ampleur + 10},${descente} L${cx + ampleur - 10},${descente} Q${cx + ampleur + 6},${descente - 20} ${cx + ampleur},${cy - 20} Z`, fill: css(melange(c.cheveux, [0, 0, 0], 0.15)) }, g);
  }

  // Oreilles.
  const oreille = 11 * (1 + 0.25 * zz(T.Oreilles));
  for (const s of [-1, 1]) {
    noeud("ellipse", { cx: cx + s * (demiLargeur * 0.98), cy: cy + 4, rx: oreille * 0.5, ry: oreille, fill: css(c.peau), stroke: css(c.ombre), "stroke-width": 1 }, g);
  }

  // Le visage : front arrondi, pommettes, puis mâchoire carrée ou en pointe.
  const lJ = demiLargeur * (0.55 + 0.4 * menton), yJ = cy + hauteur * 0.62;
  const lP = demiLargeur * pommettes;
  const creux = v.maigreur * 6;
  const contour = [
    `M${cx},${haut}`,
    `C${cx + demiLargeur * 0.75},${haut} ${cx + demiLargeur},${haut + hauteur * 0.35} ${cx + lP},${cy - 4}`,
    `C${cx + lP - creux},${cy + hauteur * 0.25} ${cx + lJ + 4},${yJ - 8} ${cx + lJ},${yJ}`,
    `Q${cx + lJ * 0.6},${bas + 2} ${cx},${bas}`,
    `Q${cx - lJ * 0.6},${bas + 2} ${cx - lJ},${yJ}`,
    `C${cx - lJ - 4},${yJ - 8} ${cx - lP + creux},${cy + hauteur * 0.25} ${cx - lP},${cy - 4}`,
    `C${cx - demiLargeur},${haut + hauteur * 0.35} ${cx - demiLargeur * 0.75},${haut} ${cx},${haut} Z`,
  ].join(" ");
  noeud("path", { d: contour, fill: `url(#${id}p)`, stroke: css(melange(c.ombre, [0, 0, 0], 0.2)), "stroke-width": 1.2 }, g);

  // Joues : creusées par la faim, rosies par la fièvre.
  if (v.maigreur > 0.2) {
    for (const s of [-1, 1]) noeud("path", { d: `M${cx + s * lP * 0.78},${cy + 4} q${-s * 6},${16} ${-s * 2},${30}`, stroke: css(c.ombre), "stroke-width": 2 + 3 * v.maigreur, fill: "none", opacity: 0.5 * v.maigreur, "stroke-linecap": "round" }, g);
  }
  if (v.fievre) {
    for (const s of [-1, 1]) noeud("ellipse", { cx: cx + s * lP * 0.55, cy: cy + 18, rx: 12, ry: 7, fill: "#d2453c", opacity: 0.35 }, g);
  }

  // Yeux : écart, taille, forme (en amande ou ronds, inclinés), couleur de l'iris. L'humeur ouvre ou plisse les paupières.
  const ecart = demiLargeur * (0.42 + 0.07 * zz(T.EcartYeux));
  const yeux = cy - 2 + 6 * enfance;
  const oeil = 7.5 * (1 + 0.18 * zz(T.TailleYeux) + 0.25 * enfance);
  const forme = zz(T.FormeYeux); // -1 rond, +1 en amande et relevé
  const ouverture = { neutre: 1, joie: 0.75, triste: 0.85, colere: 0.7, peur: 1.3 }[v.humeur];
  for (const s of [-1, 1]) {
    const ex = cx + s * ecart + (s > 0 ? asym : 0), ey = yeux + (s > 0 ? asym * 0.4 : 0);
    const rx = oeil * (1.15 + 0.2 * forme), ry = oeil * (0.55 - 0.12 * forme) * ouverture;
    const inclinaison = -s * forme * 6;
    const groupe = noeud("g", { transform: `rotate(${inclinaison} ${ex} ${ey})` }, g);
    noeud("ellipse", { cx: ex, cy: ey, rx, ry, fill: "#efe6da" }, groupe);
    noeud("circle", { cx: ex, cy: ey, r: Math.min(ry, oeil * 0.55), fill: css(c.yeux) }, groupe);
    noeud("circle", { cx: ex, cy: ey, r: Math.min(ry, oeil * 0.55) * 0.45, fill: "#120c08" }, groupe);
    noeud("circle", { cx: ex + 1.6, cy: ey - 1.6, r: 1.2, fill: "#ffffff", opacity: 0.85 }, groupe);
    noeud("path", { d: `M${ex - rx},${ey} Q${ex},${ey - ry * 1.9} ${ex + rx},${ey}`, stroke: "#2a1a12", "stroke-width": 2.2, fill: "none", "stroke-linecap": "round" }, groupe);
    // Pattes d'oie et cernes avec l'âge, et quand la vie est dure.
    if (v.age > 38) noeud("path", { d: `M${ex + s * (rx + 2)},${ey - 2} l${s * 5},-2 M${ex + s * (rx + 2)},${ey + 2} l${s * 5},2`, stroke: css(c.ombre), "stroke-width": 1, opacity: clamp((v.age - 38) / 25, 0, 0.8) }, g);
    if (v.paleur > 0.3 || v.humeur === "triste") noeud("path", { d: `M${ex - rx * 0.8},${ey + ry + 3} Q${ex},${ey + ry + 7} ${ex + rx * 0.8},${ey + ry + 3}`, stroke: css(c.ombre), "stroke-width": 1.2, fill: "none", opacity: 0.6 }, g);
  }

  // Sourcils : épais ou fins, plus marqués chez l'homme ; l'humeur les fronce, les relève ou les abaisse.
  const epais = 2.2 + 1.4 * (0.5 + 0.5 * zz(T.Sourcils)) + 1.2 * homme;
  const couleurSourcils = css(melange(c.cheveux, [20, 14, 10], 0.25));
  for (const s of [-1, 1]) {
    const ex = cx + s * ecart + (s > 0 ? asym : 0), ey = yeux - oeil * 1.5 - 2;
    const interieur = { neutre: 0, joie: -1, triste: -5, colere: 6, peur: -6 }[v.humeur];
    const exterieur = { neutre: 0, joie: -1, triste: 3, colere: -2, peur: -4 }[v.humeur];
    noeud("path", { d: `M${ex - s * 12},${ey + interieur} Q${ex},${ey - 5 + (interieur + exterieur) / 2} ${ex + s * 13},${ey + 2 + exterieur}`, stroke: couleurSourcils, "stroke-width": epais, fill: "none", "stroke-linecap": "round" }, g);
  }

  // Nez.
  const nez = 18 * (1 + 0.25 * zz(T.LongueurNez)) * (1 - 0.35 * enfance);
  const narines = 7 * (1 + 0.3 * zz(T.LargeurNez) + 0.15 * (v.teint - 0.5));
  noeud("path", { d: `M${cx - 2},${yeux + 2} Q${cx + 3},${yeux + nez * 0.6} ${cx + 2},${yeux + nez}`, stroke: css(c.ombre), "stroke-width": 1.4, fill: "none", opacity: 0.8 }, g);
  noeud("path", { d: `M${cx - narines},${yeux + nez + 2} Q${cx - narines * 0.5},${yeux + nez + 6} ${cx},${yeux + nez + 3} Q${cx + narines * 0.5},${yeux + nez + 6} ${cx + narines},${yeux + nez + 2}`, stroke: css(melange(c.ombre, [0, 0, 0], 0.2)), "stroke-width": 1.6, fill: "none", "stroke-linecap": "round" }, g);
  if (v.age > 35) noeud("path", { d: `M${cx - narines - 3},${yeux + nez + 4} q-5,10 -2,18 M${cx + narines + 3},${yeux + nez + 4} q5,10 2,18`, stroke: css(c.ombre), "stroke-width": 1.2, fill: "none", opacity: clamp((v.age - 35) / 30, 0, 0.7) }, g);

  // Bouche : lèvres épaisses ou fines ; sourire, moue, bouche ouverte de peur, lèvres serrées de colère.
  const bouche = yeux + nez + 18 - 4 * enfance;
  const largeurBouche = 15 * (1 + 0.08 * zz(T.LargeurNez)) * (1 - 0.25 * enfance);
  const levres = 3 * (1 + 0.45 * zz(T.Levres) + 0.25 * (v.teint - 0.5));
  const courbe = { neutre: 1, joie: 7, triste: -5, colere: -2, peur: -1 }[v.humeur];
  noeud("path", { d: `M${cx - largeurBouche},${bouche} Q${cx},${bouche - levres * 1.3} ${cx + largeurBouche},${bouche} Q${cx},${bouche + courbe + levres * 1.6} ${cx - largeurBouche},${bouche} Z`, fill: css(c.levres), opacity: 0.9 }, g);
  noeud("path", { d: `M${cx - largeurBouche},${bouche} Q${cx},${bouche + courbe} ${cx + largeurBouche},${bouche}`, stroke: "#4a2620", "stroke-width": v.humeur === "colere" ? 2 : 1.4, fill: v.humeur === "peur" ? "#3a1c18" : "none" }, g);

  // Rides du front, avec l'âge.
  if (v.age > 45) {
    const o = clamp((v.age - 45) / 25, 0, 0.7);
    for (const k of [0, 1]) noeud("path", { d: `M${cx - 18},${haut + hauteur * (0.32 + 0.08 * k)} q18,-3 36,0`, stroke: css(c.ombre), "stroke-width": 1, fill: "none", opacity: o }, g);
  }

  // Barbe d'un homme adulte, plus ou moins fournie, grisonnante comme ses cheveux.
  const barbe = homme * clamp(0.5 + 0.3 * (v.traits[T.Barbe] ?? 0), 0, 1);
  if (barbe > 0.15) {
    const d = `M${cx - lJ - 2},${yJ - 18} Q${cx - lJ},${bas + 4} ${cx},${bas + 8 + 10 * barbe} Q${cx + lJ},${bas + 4} ${cx + lJ + 2},${yJ - 18} Q${cx + largeurBouche + 6},${bouche + 4} ${cx},${bouche + 8} Q${cx - largeurBouche - 6},${bouche + 4} ${cx - lJ - 2},${yJ - 18} Z`;
    noeud("path", { d, fill: css(c.cheveux), opacity: 0.25 + 0.6 * barbe }, g);
    noeud("path", { d: `M${cx - largeurBouche - 4},${bouche - 4} Q${cx},${bouche - 9} ${cx + largeurBouche + 4},${bouche - 4}`, stroke: css(c.cheveux), "stroke-width": 2 + 3 * barbe, fill: "none", opacity: 0.3 + 0.5 * barbe, "stroke-linecap": "round" }, g);
  }

  // Cicatrice d'un coup ou d'une guerre, plaie ouverte du moment.
  if (v.cicatrice) noeud("path", { d: `M${cx - lP * 0.7},${cy - 8} l12,22`, stroke: css(melange(c.peau, [240, 225, 215], 0.4)), "stroke-width": 2.2, "stroke-linecap": "round" }, g);
  if (v.plaie) noeud("path", { d: `M${cx + lP * 0.45},${haut + hauteur * 0.45} l14,-6`, stroke: "#9e2a22", "stroke-width": 3, "stroke-linecap": "round" }, g);

  // Cheveux du dessus : lisses ou crépus, dégarnis par la calvitie.
  if (v.age >= 0.3) {
    const couvre = 1 - v.calvitie;
    const front = haut + hauteur * (0.28 + 0.25 * v.calvitie);
    const volume = 6 + 10 * crepus;
    const d = `M${cx - demiLargeur - 3},${cy - 6} C${cx - demiLargeur - 6},${haut - volume} ${cx + demiLargeur + 6},${haut - volume} ${cx + demiLargeur + 3},${cy - 6} `
      + `C${cx + demiLargeur * 0.8},${front} ${cx + demiLargeur * 0.3},${front - 4} ${cx},${front} C${cx - demiLargeur * 0.3},${front - 4} ${cx - demiLargeur * 0.8},${front} ${cx - demiLargeur - 3},${cy - 6} Z`;
    if (couvre > 0.15) {
      const cheveux = noeud("path", { d, fill: css(c.cheveux), opacity: 0.35 + 0.65 * couvre }, g);
      if (crepus > 0.55) cheveux.setAttribute("stroke-dasharray", "2 3");
      if (crepus > 0.55) {
        cheveux.setAttribute("stroke", css(melange(c.cheveux, [0, 0, 0], 0.3)));
        cheveux.setAttribute("stroke-width", "3");
      }
    } else {
      // Une couronne de cheveux sur les côtés.
      for (const s of [-1, 1]) noeud("path", { d: `M${cx + s * (demiLargeur + 2)},${cy - 2} q${-s * 2},-22 ${-s * 14},-30`, stroke: css(c.cheveux), "stroke-width": 7, fill: "none", "stroke-linecap": "round", opacity: 0.85 }, g);
    }
    // Mèches qui tombent sur le front, pour les cheveux lisses et longs.
    if (crepus < 0.4 && longueur > 0.4 && couvre > 0.5) {
      noeud("path", { d: `M${cx - 6},${haut - 2} q-10,14 -24,18 M${cx + 4},${haut - 2} q10,12 22,16`, stroke: css(c.cheveux), "stroke-width": 5, fill: "none", "stroke-linecap": "round" }, g);
    }
  }

  const titre = noeud("title", {}, svg);
  titre.textContent = t("visage.aide");
  return svg;
}

// ───────────── Vignettes des listes ─────────────

const visages = new Map<number, { v: Visage; le: number }>();
let attente: Set<number> | null = null;
let rappels: (() => void)[] = [];

/** Charge les visages de ces humains (gardés 30 secondes) puis rappelle <fait>. */
function charger(ids: number[], fait: () => void): void {
  const maintenant = Date.now();
  const manquants = ids.filter((i) => (visages.get(i)?.le ?? 0) < maintenant - 30_000);
  if (manquants.length === 0) {
    fait();
    return;
  }
  rappels.push(fait);
  if (attente) {
    for (const i of manquants) attente.add(i);
    return;
  }
  attente = new Set(manquants);
  // On regroupe les demandes d'un même dessin de fiche en une seule.
  window.setTimeout(() => {
    const demande = [...(attente ?? [])];
    attente = null;
    const aRappeler = rappels;
    rappels = [];
    // Le serveur en rend 200 au plus par demande.
    const paquets: number[][] = [];
    for (let i = 0; i < demande.length; i += 200) paquets.push(demande.slice(i, i + 200));
    void Promise.all(paquets.map((p) => connexion.invoke<Visage[]>("Visages", p).then((liste) => {
      const le = Date.now();
      for (const v of liste) visages.set(v.id, { v, le });
    }))).catch(() => undefined).finally(() => aRappeler.forEach((r) => r()));
  }, 0);
}

/** Visages de ces humains, chargés au besoin (vue 3D, 0.41.2) : <fait> est rappelé quand ils sont connus. */
export function chargerVisages(ids: number[], fait: () => void): void {
  charger(ids, fait);
}

/** Le visage d'un humain s'il est déjà connu. */
export function visageConnu(id: number): Visage | null {
  return visages.get(id)?.v ?? null;
}

/** Une petite place pour le visage d'un humain, remplie dès qu'il est connu. */
export function vignette(id: number, taille = 26): HTMLElement {
  const place = document.createElement("span");
  place.className = "vignette-visage";
  place.style.width = `${taille}px`;
  place.style.height = `${Math.round(taille * 1.15)}px`;
  charger([id], () => {
    const v = visages.get(id)?.v;
    if (v) place.replaceChildren(dessinerVisage(v, taille));
  });
  return place;
}
