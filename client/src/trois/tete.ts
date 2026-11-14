import * as THREE from "three/webgpu";
import { hacher, lisser, unite } from "./bruit";
import { couleurs, type Rgb, type Visage } from "../visage";

/**
 * Visages 3D (0.41.2, proposition C2 : tête calculée des traits du portrait) : à moins de quelques mètres, la tête simple du corps
 * fait place à une tête détaillée, construite des 18 traits hérités de l'humain (ceux de son portrait) et de ce que la vie y met
 * (âge, faim, maladie, cicatrice). Elle a des yeux qui regardent, des paupières qui clignent, des sourcils, des lèvres, une
 * barbe, une chevelure selon la coupe, la texture et la calvitie, et des expressions continues (joie, tristesse, colère, peur),
 * plus la bouche qui s'ouvre quand il parle.
 *
 * Repère : celui du corps au repos, pour une stature de 1 (la tête va du menton à 1). Dans ce fichier, les longueurs du visage
 * sont en « hauteurs de tête » (u) : y de −0,5 (menton) à 0,5 (sommet), z vers l'avant, x vers la gauche de l'humain.
 * Mesures d'un adulte (Farkas, 1994) : tête 23 cm, largeur 15,5 cm, yeux à 63 mm l'un de l'autre, fente de l'œil 28 × 10 mm,
 * globe de 24 mm, bouche de 50 mm.
 */

/** Traits du portrait, dans l'ordre de TraitVisage côté serveur. */
const LARGEUR = 0, LONGUEUR = 1, MACHOIRE = 2, POMMETTES = 3, ECART_YEUX = 4, TAILLE_YEUX = 5, FORME_YEUX = 6, LONGUEUR_NEZ = 8,
  LARGEUR_NEZ = 9, LEVRES = 10, SOURCILS = 11, OREILLES = 12, TEXTURE = 14, BARBE = 16;

/** Expressions, dans l'ordre des cibles de morphose. */
export const EXPRESSIONS = ["joie", "triste", "colere", "peur", "bouche", "cligne"] as const;
export type Expression = (typeof EXPRESSIONS)[number];

const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

/** Bosse lisse : 1 au centre, 0 au-delà du rayon 1. */
function bosse(dx: number, dy: number): number {
  const r2 = dx * dx + dy * dy;
  if (r2 >= 1) return 0;
  const k = 1 - r2;
  return k * Math.sqrt(k);
}

function lineaire(rgb: Rgb): THREE.Color {
  return new THREE.Color().setRGB(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255, THREE.SRGBColorSpace);
}

/** Interpolation dans une table [x, valeur] croissante en x. */
function table(x: number, points: [number, number][]): number {
  if (x <= points[0][0]) return points[0][1];
  for (let k = 1; k < points.length; k++) {
    if (x <= points[k][0]) {
      const [a0, v0] = points[k - 1];
      const [a1, v1] = points[k];
      const t = (x - a0) / (a1 - a0);
      return v0 + (v1 - v0) * t * t * (3 - 2 * t);
    }
  }
  return points[points.length - 1][1];
}

/** Tout ce que le visage tire des traits, de l'âge et du sexe. */
interface Mesures {
  enfance: number;
  homme: number;
  /** Largeurs (demi) du crâne, de la mâchoire, du menton ; profondeurs avant et arrière. */
  crane: number;
  machoire: number;
  menton: number;
  avant: number;
  arriere: number;
  yeuxY: number;
  yeuxX: number;
  /** Demi-largeur et demi-hauteur de la fente de l'œil ; inclinaison (le coin extérieur plus haut si > 0). */
  fenteA: number;
  fenteB: number;
  bridé: number;
  globe: number;
  /** Centre du globe (z) ; il est calculé d'après la surface. */
  globeZ: number;
  nezRacine: number;
  nezBout: number;
  nezBase: number;
  nezSaillie: number;
  narines: number;
  boucheY: number;
  boucheA: number;
  levreHaut: number;
  levreBas: number;
  pommettes: number;
  maigreur: number;
  sourcils: number;
  arcades: number;
  oreille: number;
  barbe: number;
  rides: number;
  asym: number;
}

function mesurer(v: Visage): Mesures {
  const enfance = clamp((18 - v.age) / 16, 0, 1);
  const homme = v.sexe === "homme" && v.age >= 12 ? clamp((v.age - 12) / 6, 0, 1) : 0;
  // Les traits harmonieux restent près de la moyenne (comme le portrait).
  const harmonie = 1 - 0.35 * clamp(v.harmonie, 0, 1);
  const z = (k: number) => clamp((v.traits[k] ?? 0) / 2.5, -1, 1) * harmonie;
  const corpulence = clamp(v.corpulence, 0, 1) - 0.5;
  const crane = 0.335 * (1 + 0.07 * z(LARGEUR) + 0.04 * corpulence - 0.03 * v.maigreur + 0.05 * enfance);
  const longueur = 1 + 0.06 * z(LONGUEUR);
  // Chez l'enfant, le crâne domine : les yeux sont plus bas, le bas du visage plus petit.
  const yeuxY = 0.02 - 0.085 * enfance;
  const fenteA = 0.052 * (1 + 0.1 * z(TAILLE_YEUX)) * (1 - 0.06 * enfance);
  const forme = z(FORME_YEUX);
  const fenteB = 0.021 * (1 + 0.2 * z(TAILLE_YEUX) - 0.22 * forme + 0.3 * enfance);
  const nezRacine = yeuxY + 0.015;
  const nezBout = yeuxY - (0.18 * (1 + 0.12 * z(LONGUEUR_NEZ)) * (1 - 0.3 * enfance)) * longueur;
  const nezBase = nezBout - 0.035 * (1 - 0.3 * enfance);
  const boucheY = nezBase - 0.068 * longueur * (1 - 0.25 * enfance);
  return {
    enfance, homme, crane,
    machoire: 0.255 * (1 + 0.12 * z(MACHOIRE) + 0.06 * homme + 0.1 * corpulence - 0.05 * enfance),
    menton: 0.07 + 0.035 * z(MACHOIRE) + 0.02 * homme,
    avant: 0.4 * (1 + 0.03 * enfance),
    arriere: 0.47 * (1 + 0.04 * enfance),
    yeuxY,
    yeuxX: 0.137 * (1 + 0.07 * z(ECART_YEUX)) * (1 + 0.06 * enfance),
    fenteA, fenteB, bridé: 0.13 * forme,
    globe: 0.053 * (1 + 0.15 * enfance),
    globeZ: 0,
    nezRacine, nezBout, nezBase,
    nezSaillie: 0.095 * (1 + 0.1 * z(LONGUEUR_NEZ)) * (1 - 0.45 * enfance),
    narines: 0.07 * (1 + 0.25 * z(LARGEUR_NEZ) + 0.15 * (v.teint - 0.5)) * (1 - 0.2 * enfance),
    boucheY,
    boucheA: 0.105 * (1 + 0.08 * z(LARGEUR_NEZ)) * (1 - 0.2 * enfance),
    levreHaut: 0.01 * (1 + 0.45 * z(LEVRES) + 0.25 * (v.teint - 0.5)),
    levreBas: 0.013 * (1 + 0.45 * z(LEVRES) + 0.25 * (v.teint - 0.5)),
    pommettes: 0.015 * (1 + 0.5 * z(POMMETTES)) + 0.015 * enfance,
    maigreur: clamp(v.maigreur, 0, 1),
    sourcils: 0.5 + 0.5 * z(SOURCILS),
    arcades: 0.012 + 0.018 * homme,
    oreille: 0.27 * (1 + 0.15 * z(OREILLES)) * (1 - 0.1 * enfance),
    barbe: homme * clamp(0.5 + 0.3 * (v.traits[BARBE] ?? 0), 0, 1),
    rides: clamp((v.age - 38) / 40, 0, 1),
    asym: (1 - clamp(v.symetrie, 0, 1)) * 0.012,
  };
}

/** Demi-largeur, profondeur avant et arrière de la section de la tête à la hauteur y (avant les traits). */
function section(m: Mesures, y: number): [number, number, number] {
  let w: number;
  if (y >= -0.05) w = m.crane;
  else if (y >= -0.3) w = m.crane + (m.machoire - m.crane) * lisser(-0.05, -0.3, y);
  else w = m.menton + (m.machoire - m.menton) * Math.sqrt(Math.max(0, 1 - ((-0.3 - y) / 0.2) ** 2));
  const f = y >= -0.05 ? m.avant : y >= -0.32 ? m.avant * (1 + 0.03 * Math.sin(((y + 0.05) / -0.27) * Math.PI)) : m.avant * (1 - 0.12 * lisser(-0.32, -0.5, y)) * (1 - 0.08 * m.enfance * lisser(-0.32, -0.5, y));
  const b = y >= -0.05 ? m.arriere : y >= -0.3 ? m.arriere + (0.2 - m.arriere) * lisser(-0.05, -0.3, y) : 0.2 - 0.06 * lisser(-0.3, -0.5, y);
  return [w, f, b];
}

/** Hauteur de la ligne des cheveux selon l'angle autour de la tête (0 : devant) : front, tempes, favoris, oreilles, nuque. */
function ligneCheveux(m: Mesures, calvitie: number, aphi: number): number {
  return table(aphi, [
    [0, m.yeuxY + 0.27 + 0.16 * calvitie], [0.75, m.yeuxY + 0.25 + 0.14 * calvitie], [1.1, m.yeuxY + 0.15 + 0.06 * calvitie],
    [1.3, m.yeuxY - 0.03 - 0.05 * m.homme], [1.45, m.yeuxY + 0.06], [1.75, m.yeuxY + 0.05], [2.05, -0.22], [Math.PI, -0.3],
  ]);
}

/** Point de la surface de base pour un paramètre de hauteur s (0 sommet, 1 sous le menton) et un angle φ (0 devant). */
function base(m: Mesures, s: number, phi: number, sortie: THREE.Vector3): THREE.Vector3 {
  let y: number;
  let k = 1;
  let zCentre = 0;
  if (s < 0.2) {
    // Calotte : du sommet au haut du front.
    const a = (s / 0.2) * (Math.PI / 2);
    y = 0.05 + 0.45 * Math.cos(a);
    k = Math.sin(a);
  } else if (s < 0.93) {
    y = 0.05 - ((s - 0.2) / 0.73) * 0.48;
  } else {
    // Dessous du menton : la section se referme vers l'avant.
    const t = (s - 0.93) / 0.07;
    y = -0.43 - 0.07 * t;
    k = Math.sqrt(Math.max(0, 1 - t * t));
    zCentre = 0.22 * t;
  }
  const [w, f, b] = section(m, Math.max(y, -0.5));
  const sx = Math.sin(phi);
  const cz = Math.cos(phi);
  // Superellipse : le visage est plus plat qu'une ellipse, l'arrière plus rond.
  const n = cz > 0 ? 2.25 : 2.05;
  const x = w * Math.sign(sx) * Math.abs(sx) ** (2 / n);
  const z = (cz > 0 ? f : b) * Math.sign(cz) * Math.abs(cz) ** (2 / n);
  return sortie.set(x * k, y, zCentre + (z - zCentre) * k);
}

/** Paramètre φ d'une colonne : serré sur le visage, lâche derrière. */
function phiDe(u: number): number {
  return Math.PI * (0.38 * u + 0.62 * u * u * u);
}

/** Paramètre s d'un anneau. */
const sDe = (t: number) => t;

const ANNEAUX = 128;
const COLONNES = 112;

interface Construit {
  positions: Float32Array;
  couleurs: Float32Array;
  morphes: Float32Array[];
  indices: number[];
}

/** Globe de l'œil : centre et rayon, en u (repère du visage). */
function globeEn(m: Mesures, cote: number): THREE.Vector3 {
  return new THREE.Vector3(cote * m.yeuxX + cote * m.asym * 0.3, m.yeuxY + (cote > 0 ? m.asym * 0.4 : 0), m.globeZ);
}

/** Distance normalisée à la fente de l'œil (amande) : < 1 dedans. */
function dansFente(m: Mesures, x: number, y: number, cote: number): number {
  const g = globeEn(m, cote);
  const dx = (x - g.x) * cote;
  // Inclinaison : le coin extérieur (dx > 0) plus haut si bridé > 0.
  const dy = y - g.y - m.bridé * dx;
  const u = dx / m.fenteA;
  if (Math.abs(u) >= 1.6) return 9;
  // Amande : plus haute près du coin intérieur, le bas plus plat.
  const haut = m.fenteB * (1 - 0.12 * u) * (dy > 0 ? 1.05 : 0.82);
  return Math.hypot(u, dy / haut);
}

/** z de la peau des paupières posées sur le globe et le gras qui l'entoure (une sphère un peu plus grande). */
function surfacePaupiere(m: Mesures, x: number, y: number, cote: number): number {
  const g = globeEn(m, cote);
  const r = m.globe * 1.8;
  const r2 = r * r - (x - g.x) ** 2 - (y - g.y) ** 2;
  return g.z + m.globe + 0.004 - r + Math.sqrt(Math.max(0, r2));
}

/** z de la surface du globe en (x, y) (son centre hors de son disque). */
function surfaceGlobe(m: Mesures, x: number, y: number, cote: number): number {
  const g = globeEn(m, cote);
  const r2 = m.globe * m.globe - (x - g.x) ** 2 - (y - g.y) ** 2;
  return g.z + Math.sqrt(Math.max(0, r2));
}

/**
 * Construit la surface du visage (positions neutres, couleurs, et le déplacement de chaque expression), et les oreilles.
 */
function construireVisage(v: Visage, m: Mesures, peau: THREE.Color, cheveux: THREE.Color): Construit {
  const c = couleurs(v);
  const levres = lineaire(c.levres);
  const ombre = lineaire(c.ombre);
  const rougeur = peau.clone().lerp(new THREE.Color(0.55, 0.16, 0.13), 0.22);
  const sombre = new THREE.Color(0.05, 0.03, 0.025);
  const sourcil = cheveux.clone().multiplyScalar(0.8);
  // Centre du globe : juste derrière la surface du visage à l'endroit de l'œil.
  {
    let a = 0;
    let b = Math.PI / 2;
    const p = new THREE.Vector3();
    const s = 0.2 + ((0.05 - m.yeuxY) / 0.48) * 0.73;
    for (let k = 0; k < 30; k++) {
      const mil = (a + b) / 2;
      base(m, s, mil, p);
      if (p.x < m.yeuxX) a = mil;
      else b = mil;
    }
    base(m, s, a, p);
    // La peau de l'orbite est creusée (voir plus bas) : le devant du globe reste un peu en retrait de la peau des paupières.
    m.globeZ = p.z - 0.014 - 0.008 - m.globe;
  }
  const nAnn = ANNEAUX + 1;
  const n = nAnn * COLONNES;
  const positions = new Float32Array(n * 3);
  const cols = new Float32Array(n * 3);
  const morphes = EXPRESSIONS.map(() => new Float32Array(n * 3));
  const p = new THREE.Vector3();
  const col = new THREE.Color();
  const yBarbe = (ax: number) => table(ax, [[0, m.boucheY + 0.05], [m.boucheA + 0.03, m.boucheY + 0.05], [0.17, m.boucheY + 0.1], [0.26, m.yeuxY - 0.1], [0.36, m.yeuxY - 0.03]]);
  for (let i = 0; i < nAnn; i++) {
    const s = sDe(i / ANNEAUX);
    for (let j = 0; j < COLONNES; j++) {
      const phi = phiDe((j / COLONNES) * 2 - 1);
      base(m, s, phi, p);
      const avant = Math.cos(phi);
      const devant = lisser(0.15, 0.55, avant);
      const ax = Math.abs(p.x);
      const cote = p.x >= 0 ? 1 : -1;
      let { x, y, z } = p;
      col.copy(peau);
      const d: number[][] = EXPRESSIONS.map(() => [0, 0, 0]);
      // Sous la chevelure, le cuir chevelu prend la couleur des cheveux : la lisière ne se voit pas en escalier.
      if (v.age >= 0.4) {
        const calv = clamp(v.calvitie, 0, 1);
        const chauve = calv > 0.45 && y > 0.47 - 0.4 * (calv - 0.45) && Math.abs(phi) < 2.4;
        const sous = lisser(0.005, 0.03, y - ligneCheveux(m, calv, Math.abs(phi)));
        if (sous > 0 && !chauve) col.lerp(cheveux.clone().multiplyScalar(0.8), sous * (v.age < 2 ? 0.5 : 1));
      }
      if (devant > 0) {
        let dz = 0;
        // Front un peu bombé chez l'enfant ; arcades sourcilières de l'homme.
        dz += 0.02 * m.enfance * bosse(x / 0.3, (y - 0.22) / 0.2);
        dz += m.arcades * bosse((ax - 0.12) / 0.16, (y - (m.yeuxY + 0.085)) / 0.05);
        // Orbites creuses autour des yeux.
        dz -= 0.014 * bosse((ax - m.yeuxX) / 0.11, (y - m.yeuxY - 0.008) / 0.07);
        // Pommettes, joues (creusées par la faim et l'âge, rondes chez l'enfant).
        dz += m.pommettes * (1 - 0.4 * m.maigreur) * bosse((ax - 0.21) / 0.13, (y - (m.yeuxY - 0.08)) / 0.08);
        dz -= (0.028 * m.maigreur + 0.008 * m.rides) * bosse((ax - 0.2) / 0.1, (y - (m.boucheY + 0.06)) / 0.08);
        dz += 0.015 * m.enfance * bosse((ax - 0.18) / 0.14, (y - (m.boucheY + 0.05)) / 0.12);
        // Museau (maxillaire) et menton.
        dz += 0.03 * bosse(x / 0.2, (y - m.boucheY) / 0.16);
        dz += (0.02 + 0.01 * m.homme) * bosse(x / (m.menton + 0.05), (y + 0.425) / 0.06);
        dz -= 0.006 * bosse(x / 0.08, (y - (m.boucheY - 0.068)) / 0.018);
        // Nez : arête de la racine au bout, bout arrondi, ailes.
        const { nezRacine: r, nezBout: b, nezBase: nb } = m;
        if (y <= r + 0.02 && y >= nb - 0.01) {
          let h: number;
          let w: number;
          if (y > b) {
            const t = clamp((r - y) / (r - b), 0, 1);
            h = 0.012 + (m.nezSaillie - 0.012) * t ** 1.25;
            w = 0.03 + 0.03 * t * t;
          } else {
            const t = clamp((b - y) / (b - nb), 0, 1);
            h = m.nezSaillie * Math.sqrt(Math.max(0, 1 - t * t)) * (1 - 0.4 * t) + 0.01 * t;
            w = 0.03 + 0.025 * (1 - t);
          }
          h *= lisser(r + 0.02, r - 0.01, y);
          dz += h * Math.max(0, 1 - (x / w) ** 2) ** 1.5;
        }
        for (const cn of [-1, 1]) dz += 0.03 * bosse((x - cn * m.narines * 0.62) / 0.032, (y - (b - 0.012)) / 0.03);
        // Lèvres : la supérieure, l'inférieure, et la fente de la bouche.
        const ligne = m.boucheY;
        const ux = ax / m.boucheA;
        if (ux < 1.15) {
          const fondu = Math.max(0, 1 - ux * ux) ** 0.6;
          // Arc de Cupidon : la lèvre supérieure se creuse un peu au milieu.
          const arc = 1 - 0.25 * bosse(x / 0.03, 0);
          dz += m.levreHaut * fondu * arc * bosse(0, (y - (ligne + 0.017)) / 0.02);
          dz += m.levreBas * fondu * bosse(0, (y - (ligne - 0.022)) / 0.024);
          if (ux < 1 && Math.abs(y - ligne) < 0.0075) dz -= 0.03 * (1 - Math.abs(y - ligne) / 0.0075) * Math.sqrt(1 - ux * ux);
        }
        // Plis du nez à la bouche, plus marqués avec l'âge.
        {
          const ax0 = m.narines * 0.75;
          const ay0 = b - 0.005;
          const bx0 = m.boucheA + 0.025;
          const by0 = ligne - 0.02;
          const vx = bx0 - ax0;
          const vy = by0 - ay0;
          const t = clamp(((ax - ax0) * vx + (y - ay0) * vy) / (vx * vx + vy * vy), 0, 1);
          const dd = Math.hypot(ax - (ax0 + t * vx), y - (ay0 + t * vy));
          if (dd < 0.014) dz -= (0.002 + 0.006 * m.rides) * (1 - dd / 0.014) * (1 - m.enfance);
        }
        // Yeux : la fente laisse voir le globe ; les paupières épousent le globe autour d'elle.
        let globe = NaN;
        let paupiere = NaN;
        let fente = 9;
        for (const cn of [-1, 1]) {
          const f = dansFente(m, x, y, cn);
          if (f < fente) {
            fente = f;
            globe = surfaceGlobe(m, x, y, cn);
            paupiere = surfacePaupiere(m, x, y, cn);
          }
        }
        z += dz * devant;
        if (fente < 2.1) {
          const g = globe;
          // Pli de la paupière supérieure.
          const pli = y > m.yeuxY ? 0.004 * bosse(0, (fente - 1.5) / 0.4) : 0;
          if (fente < 1) {
            // Dans la fente : sous le globe (caché) quand l'œil est ouvert.
            const zOuvert = g - 0.016 - 0.01 * (1 - fente);
            const zFerme = Math.max(g + 0.0045, paupiere);
            d[5][2] = zFerme - zOuvert;
            z = zOuvert;
            // Le bord des paupières (cils) assombrit le tour de l'œil ; la paupière fermée garde la couleur de la peau.
            col.copy(fente > 0.78 ? sombre : peau.clone().lerp(ombre, 0.35));
            if (fente < 0.78 && Math.abs(y - m.yeuxY) < m.fenteB * 0.18) col.copy(sombre);
          } else {
            // Paupières posées sur le globe, puis la surface du visage.
            const k = lisser(1, 2.1, fente);
            // Le bord de la paupière épouse le globe ; elle s'épaissit en s'éloignant de la fente.
            const zPaupiere = fente < 1.15 ? Math.max(g + 0.0015, Math.min(paupiere, g + 0.0015 + (fente - 1) * 0.04)) + pli : paupiere + pli;
            z = zPaupiere + (z - zPaupiere) * k;
            if (fente < 1.18) col.copy(peau).lerp(sombre, 0.5);
            // Fermer l'œil : la paupière supérieure descend un peu sur les bords.
            if (fente < 1.4) d[5][2] = 0.002;
          }
        }
        // Sourcils : un arc au-dessus de chaque œil.
        for (const cn of [-1, 1]) {
          const dx = (x - cn * m.yeuxX) * cn;
          if (dx > -0.085 && dx < 0.1) {
            const yS = m.yeuxY + 0.072 + 0.018 * (1 - (dx / 0.09) ** 2) - 0.012 * Math.max(0, dx / 0.1) ** 2;
            // Plus épais côté nez, effilé vers la tempe, les deux bouts adoucis.
            const epais = (0.009 + 0.012 * m.sourcils) * (1 - 0.5 * Math.max(0, dx / 0.1)) * lisser(-0.085, -0.06, dx) * lisser(0.1, 0.07, dx);
            const e = Math.abs(y - yS) / epais;
            if (e < 1.4) {
              const poils = lisser(1.4, 0.6, e) * (0.55 + 0.35 * m.sourcils) * (1 - m.enfance * 0.3);
              col.lerp(sourcil, poils);
              z += 0.002 * poils;
            }
          }
        }
        // Couleurs : lèvres, narines, rougeur des joues, cernes.
        if (ux < 1.05) {
          const lev = Math.max(bosse(0, (y - (ligne + 0.014)) / 0.019), bosse(0, (y - (ligne - 0.02)) / 0.025)) * Math.max(0, 1 - ux ** 4);
          if (lev > 0.1) col.copy(peau).lerp(levres, clamp(lev * 1.6, 0, 1));
          if (ux < 1 && Math.abs(y - ligne) < 0.0045) col.copy(levres).lerp(sombre, 0.6);
        }
        for (const cn of [-1, 1]) {
          const nar = Math.hypot((x - cn * m.narines * 0.3) / 0.013, (y - (nb + 0.004)) / 0.006);
          if (nar < 1) col.lerp(sombre, 0.5 * (1 - nar * nar));
        }
        col.lerp(rougeur, 0.6 * bosse((ax - 0.2) / 0.11, (y - (m.yeuxY - 0.1)) / 0.08) * (v.fievre ? 1 : 0.35));
        col.lerp(ombre, (0.15 + 0.3 * m.rides) * bosse((ax - m.yeuxX) / 0.07, (y - (m.yeuxY - 0.045)) / 0.022));
        // Rides du front.
        if (m.rides > 0 && y > m.yeuxY + 0.13 && y < m.yeuxY + 0.25 && ax < 0.2) {
          const onde = Math.cos((y - m.yeuxY) * 150);
          if (onde > 0.85) {
            col.lerp(ombre, 0.35 * m.rides);
            z -= 0.002 * m.rides;
          }
        }
        // Barbe : joues basses, moustache, menton, sans les lèvres.
        if (m.barbe > 0.1 && v.age >= 15) {
          const surLevres = ux < 1.05 && y > ligne - 0.045 && y < ligne + 0.03;
          const moustache = ax < m.boucheA + 0.025 && y >= ligne + 0.03 && y < nb - 0.004;
          if (!surLevres && (moustache || y < yBarbe(ax)) && y > -0.6) {
            // Bords adoucis : la barbe s'éclaircit vers ses limites.
            const bord = moustache ? lisser(0, 0.012, Math.min(nb - 0.004 - y, y - ligne - 0.03, m.boucheA + 0.025 - ax)) : lisser(0, 0.03, yBarbe(ax) - y);
            const dense = m.barbe * (0.75 + 0.25 * unite(hacher(i, j, 7))) * bord;
            col.lerp(cheveux, (0.1 + 0.5 * dense) * (moustache ? lisser(0.3, 0.6, m.barbe) : 1));
            z += 0.012 * m.barbe * m.barbe * bord;
          }
        }
        // Cicatrice sur la joue gauche.
        if (v.cicatrice) {
          const t = clamp(((x - 0.12) * 0.5 + (y - m.yeuxY + 0.06) * -0.87) / 0.09, 0, 1);
          if (Math.hypot(x - (0.12 + 0.045 * t), y - (m.yeuxY - 0.06 - 0.078 * t)) < 0.006) col.lerp(new THREE.Color(0.9, 0.78, 0.72), 0.5);
        }

        // Expressions : déplacements des points du visage neutre.
        const coin = (cn: number) => bosse((x - cn * m.boucheA) / 0.075, (y - ligne) / 0.07);
        const coins = coin(1) + coin(-1);
        // Joie : coins de la bouche relevés et tirés, joues hautes, paupières inférieures remontées.
        d[0][1] += 0.024 * coins;
        d[0][0] += 0.012 * cote * coins;
        d[0][2] -= 0.008 * coins;
        const joue = bosse((ax - 0.19) / 0.11, (y - (m.yeuxY - 0.1)) / 0.08);
        d[0][1] += 0.014 * joue;
        d[0][2] += 0.012 * joue;
        const sousOeil = bosse((ax - m.yeuxX) / 0.075, (y - (m.yeuxY - m.fenteB * 1.6)) / (m.fenteB * 1.3));
        if (y < m.yeuxY) d[0][1] += 0.008 * sousOeil;
        // Tristesse : intérieur des sourcils relevé, coins de la bouche tombants, lèvre inférieure avancée.
        const sourcilInt = bosse((ax - (m.yeuxX - 0.06)) / 0.07, (y - (m.yeuxY + 0.08)) / 0.05);
        const sourcilExt = bosse((ax - (m.yeuxX + 0.06)) / 0.07, (y - (m.yeuxY + 0.075)) / 0.05);
        d[1][1] += 0.018 * sourcilInt - 0.006 * sourcilExt;
        d[1][1] -= 0.02 * coins;
        d[1][2] += 0.006 * bosse(x / m.boucheA, (y - (ligne - 0.022)) / 0.025);
        // Colère : sourcils bas et rapprochés, paupière supérieure baissée, lèvres serrées.
        d[2][1] -= 0.02 * sourcilInt + 0.008 * sourcilExt;
        d[2][0] -= 0.009 * cote * sourcilInt;
        d[2][2] += 0.006 * sourcilInt;
        const dessusOeil = bosse((ax - m.yeuxX) / 0.075, (y - (m.yeuxY + m.fenteB * 1.5)) / (m.fenteB * 1.4));
        if (y > m.yeuxY) d[2][1] -= 0.007 * dessusOeil;
        if (Math.abs(y - ligne) < 0.045 && ux < 1.2) d[2][1] -= (y - ligne) * 0.35 * Math.max(0, 1 - ux * ux);
        // Peur : sourcils hauts, yeux grands ouverts, bouche étirée et entrouverte.
        d[3][1] += 0.024 * Math.max(sourcilInt, sourcilExt, bosse((ax - m.yeuxX) / 0.1, (y - (m.yeuxY + 0.075)) / 0.05));
        if (y > m.yeuxY) d[3][1] += 0.01 * dessusOeil;
        if (y < m.yeuxY) d[3][1] -= 0.004 * sousOeil;
        d[3][0] += 0.01 * cote * coins;
        // Bouche ouverte (parole) : tout ce qui est sous la ligne des lèvres descend avec la mâchoire.
        const sousLigne = y < ligne - 0.001 && ax < 0.36;
        if (sousLigne) {
          const ouverture = 0.042 * (1 - lisser(0.16, 0.34, ax)) * (1 - 0.3 * lisser(ligne, -0.5, y));
          d[4][1] -= ouverture;
          d[3][1] -= ouverture * 0.25;
          if (Math.abs(y - ligne) < 0.0075 && ux < 1) d[4][2] -= 0.01;
        }
        for (const e of d) {
          e[0] *= devant;
          e[1] *= devant;
          e[2] *= devant;
        }
      }
      // Asymétrie légère d'un visage peu symétrique.
      if (x > 0) y += m.asym * 0.5 * devant;
      const k = (i * COLONNES + j) * 3;
      positions[k] = x;
      positions[k + 1] = y;
      positions[k + 2] = z;
      cols[k] = col.r;
      cols[k + 1] = col.g;
      cols[k + 2] = col.b;
      d.forEach((e, q) => morphes[q].set(e, k));
    }
  }
  const indices: number[] = [];
  for (let i = 0; i < ANNEAUX; i++) {
    for (let j = 0; j < COLONNES; j++) {
      const a = i * COLONNES + j;
      const b = (i + 1) * COLONNES + j;
      const c2 = i * COLONNES + ((j + 1) % COLONNES);
      const e = (i + 1) * COLONNES + ((j + 1) % COLONNES);
      indices.push(a, b, c2, c2, b, e);
    }
  }
  return { positions, couleurs: cols, morphes, indices };
}

/** Oreilles : deux coquilles aplaties sur les côtés, creusées sur leur face externe. */
function oreilles(m: Mesures, peau: THREE.Color): { positions: number[]; couleurs: number[]; indices: number[] } {
  const positions: number[] = [];
  const cols: number[] = [];
  const indices: number[] = [];
  const rose = peau.clone().lerp(new THREE.Color(0.6, 0.3, 0.25), 0.15);
  const creux = peau.clone().multiplyScalar(0.7);
  const anneaux = 12;
  const cotes = 16;
  for (const cote of [-1, 1]) {
    const debut = positions.length / 3;
    const yO = m.yeuxY - 0.045;
    const [w] = section(m, yO);
    const centre = new THREE.Vector3(cote * (w - 0.005), yO, -0.07);
    const h = m.oreille / 2;
    const l = h * 0.58;
    const e = 0.03;
    for (let i = 0; i <= anneaux; i++) {
      const a = (i / anneaux) * Math.PI;
      for (let j = 0; j < cotes; j++) {
        const b = (j / cotes) * Math.PI * 2;
        // Ellipsoïde : x épaisseur (vers l'extérieur), y hauteur, z largeur ; le lobe en bas est plus étroit.
        let px = Math.cos(a) * e;
        let py = Math.sin(a) * Math.cos(b) * h;
        let pz = Math.sin(a) * Math.sin(b) * l * (py < 0 ? 1 - 0.35 * (-py / h) : 1);
        // Conque : la face externe se creuse, le bord (hélix) reste épais.
        const r = Math.sin(a);
        if (px > 0) px -= 0.035 * Math.max(0, 1 - r * 1.15) * 1.2 + 0.0;
        px = px * 1 + 0.012;
        // Inclinée vers l'arrière.
        const inc = -0.22;
        const py2 = py * Math.cos(inc) - pz * Math.sin(inc);
        pz = py * Math.sin(inc) + pz * Math.cos(inc);
        py = py2;
        positions.push(centre.x + cote * px, centre.y + py, centre.z + pz);
        const c = px < 0.0 && r < 0.75 ? creux : rose;
        cols.push(c.r, c.g, c.b);
      }
    }
    for (let i = 0; i < anneaux; i++) {
      for (let j = 0; j < cotes; j++) {
        const a = debut + i * cotes + j;
        const b = debut + (i + 1) * cotes + j;
        const c = debut + i * cotes + ((j + 1) % cotes);
        const d = debut + (i + 1) * cotes + ((j + 1) % cotes);
        if (cote > 0) indices.push(a, c, b, c, d, b);
        else indices.push(a, b, c, c, b, d);
      }
    }
  }
  return { positions, couleurs: cols, indices };
}

/** Chevelure : une coque posée sur le crâne au-dessus de la ligne des cheveux, tombant dans le dos si les cheveux sont longs. */
function chevelure(v: Visage, m: Mesures, cheveux: THREE.Color): THREE.BufferGeometry | null {
  if (v.age < 0.4) return null;
  const crepus = clamp(0.5 + 0.3 * (v.traits[TEXTURE] ?? 0) + 0.35 * (v.teint - 0.5), 0, 1);
  const calvitie = clamp(v.calvitie, 0, 1);
  const longueur = v.coupe * (1 - 0.6 * calvitie) * (v.age < 2 ? 0.2 : v.age < 6 ? 0.6 : 1);
  const epais = (v.age < 2 ? 0.006 : 0.014) + 0.05 * crepus * (0.4 + 0.6 * Math.min(1, longueur * 2)) + 0.01 * longueur;
  const anneaux = 72;
  const colonnes = 104;
  const positions: number[] = [];
  const cols: number[] = [];
  const p = new THREE.Vector3();
  const centre = new THREE.Vector3(0, 0.02, -0.02);
  const n = new THREE.Vector3();
  const ligne = (aphi: number) => ligneCheveux(m, calvitie, aphi);
  const chauveHaut = calvitie > 0.45 ? 0.47 - 0.4 * (calvitie - 0.45) : 9;
  for (let i = 0; i <= anneaux; i++) {
    const s = (i / anneaux) * 0.95;
    for (let j = 0; j < colonnes; j++) {
      const phi = phiDe((j / colonnes) * 2 - 1);
      base(m, s, phi, p);
      const aphi = Math.abs(phi);
      n.copy(p).sub(centre).normalize();
      let couvre = lisser(-0.01, 0.05, p.y - ligne(aphi));
      if (p.y > chauveHaut && aphi < 2.4) couvre *= 1 - lisser(chauveHaut, chauveHaut + 0.04, p.y);
      let h = -0.012 + (epais + 0.012) * couvre;
      const q = p.clone();
      // Cheveux longs : sur les côtés et derrière, ils tombent vers les épaules.
      if (longueur > 0.25 && aphi > 1.25 && couvre > 0.2) {
        const chute = longueur * 1.0 * lisser(m.yeuxY + 0.12, m.yeuxY - 0.5, q.y) * lisser(1.25, 1.6, aphi);
        q.y -= chute;
        h += 0.03 * lisser(m.yeuxY, m.yeuxY - 0.4, p.y);
        // Le rideau ne rentre pas dans la nuque ni le cou.
        const r = Math.hypot(q.x, q.z - centre.z);
        const rMin = 0.3 * lisser(m.yeuxY + 0.05, m.yeuxY - 0.25, p.y);
        if (r + h < rMin) h = rMin - r;
      }
      q.addScaledVector(n, h);
      // Hors de la chevelure, la coque rentre loin dans la tête (sinon, juste sous la peau, elle cacherait les yeux).
      if (couvre < 0.5) q.lerp(centre.clone().add(p.clone().sub(centre).multiplyScalar(0.55)), lisser(0.5, 0, couvre));
      positions.push(q.x, q.y, q.z);
      // Mèches : des stries le long de la chute (lisses), un grain serré (crépus).
      const strie = crepus > 0.55 ? unite(hacher(i, j, 3)) : 0.5 + 0.5 * Math.sin(phi * 47 + Math.sin(i * 0.7) * 0.6);
      const c = cheveux.clone().multiplyScalar(0.78 + 0.32 * strie);
      cols.push(c.r, c.g, c.b);
    }
  }
  const indices: number[] = [];
  for (let i = 0; i < anneaux; i++) {
    for (let j = 0; j < colonnes; j++) {
      const a = i * colonnes + j;
      const b = (i + 1) * colonnes + j;
      const c = i * colonnes + ((j + 1) % colonnes);
      const d = (i + 1) * colonnes + ((j + 1) % colonnes);
      indices.push(a, b, c, c, b, d);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  g.setAttribute("color", new THREE.Float32BufferAttribute(cols, 3));
  g.setIndex(indices);
  g.computeVertexNormals();
  return g;
}

/** Globe de l'œil : pupille, iris, limbe, blanc ; tourné vers +z. */
function globeOeil(rayon: number, iris: THREE.Color): THREE.BufferGeometry {
  const betas = [0, 0.07, 0.14, 0.19, 0.27, 0.36, 0.44, 0.5, 0.55, 0.62, 0.8, 1.05, 1.4, 1.9, 2.5, Math.PI];
  const cotes = 22;
  const positions: number[] = [];
  const cols: number[] = [];
  const pupille = new THREE.Color(0.012, 0.01, 0.01);
  const blanc = new THREE.Color(0.78, 0.74, 0.69);
  const limbe = iris.clone().multiplyScalar(0.45);
  for (const b of betas) {
    // La cornée bombe un peu devant l'iris.
    const r = rayon * (1 + 0.07 * Math.max(0, 1 - b / 0.55) ** 2);
    for (let j = 0; j < cotes; j++) {
      const a = (j / cotes) * Math.PI * 2;
      positions.push(Math.sin(b) * Math.cos(a) * r, Math.sin(b) * Math.sin(a) * r, Math.cos(b) * r);
      let c: THREE.Color;
      if (b < 0.16) c = pupille;
      else if (b < 0.5) c = iris.clone().multiplyScalar(0.75 + 0.5 * Math.sin(((b - 0.16) / 0.34) * Math.PI) * (0.8 + 0.2 * Math.sin(a * 9)));
      else if (b < 0.58) c = limbe;
      else c = blanc.clone().lerp(new THREE.Color(0.7, 0.45, 0.4), 0.15 * lisser(1, 2.5, b));
      cols.push(c.r, c.g, c.b);
    }
  }
  const indices: number[] = [];
  for (let i = 0; i < betas.length - 1; i++) {
    for (let j = 0; j < cotes; j++) {
      const a = i * cotes + j;
      const b = (i + 1) * cotes + j;
      const c = i * cotes + ((j + 1) % cotes);
      const d = (i + 1) * cotes + ((j + 1) % cotes);
      indices.push(a, b, c, c, b, d);
    }
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  g.setAttribute("color", new THREE.Float32BufferAttribute(cols, 3));
  g.setIndex(indices);
  g.computeVertexNormals();
  return g;
}

/** Normales d'une géométrie aux positions données (pour les cibles de morphose). */
function normalesDe(positions: Float32Array, indices: number[]): Float32Array {
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  g.setIndex(indices);
  g.computeVertexNormals();
  return g.getAttribute("normal").array as Float32Array;
}

const materiaux = {
  peau: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.62, metalness: 0 }),
  yeux: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.12, metalness: 0 }),
  cheveux: new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.85, metalness: 0 }),
};

/** Regards extrêmes rendus par les cibles de morphose des yeux (lacet vers la gauche de l'humain, tangage vers le bas). */
const LACET_MAX = 0.5;
const TANGAGE_MAX = 0.35;
const REGARDS: [number, number][] = [[LACET_MAX, 0], [-LACET_MAX, 0], [0, TANGAGE_MAX], [0, -TANGAGE_MAX]];

/**
 * Une tête détaillée : un seul maillage (visage, oreilles, yeux, chevelure ; trois matériaux), pour que rien ne se décale quand la
 * tête bouge. Les expressions, le clignement et le regard sont des cibles de morphose. Construite dans le repère du corps au repos
 * (stature 1) ; placée à chaque image sur l'os de la tête.
 */
export class TeteDetaillee {
  readonly groupe = new THREE.Group();
  readonly visage: THREE.Mesh;
  private readonly milieuYeux = new THREE.Vector3();
  readonly cle: string;
  /** Regard du moment (lacet, tangage) et prochain mouvement des yeux. */
  private regard = { lacet: 0, tangage: 0, cibleL: 0, cibleT: 0, prochain: 0 };
  private clignement = { debut: -1, prochain: 0 };
  private readonly graine: number;
  private readonly inverse = new THREE.Matrix4();
  private readonly local = new THREE.Vector3();

  /**
   * `menton` : hauteur du menton au repos (stature 1) ; `hauteur` : hauteur de la tête ; `zTete` : avance du centre de la tête.
   */
  constructor(v: Visage, menton: number, hauteur: number, zTete: number, peau: THREE.Color, cheveux: THREE.Color, cle: string) {
    this.cle = cle;
    this.graine = unite(hacher(v.id, 91)) * 100;
    const m = mesurer(v);
    const f = construireVisage(v, m, peau, cheveux);
    const o = oreilles(m, peau);
    // Repère du visage (u) → repère du corps : échelle de la hauteur de tête, centre à mi-hauteur.
    const yc = menton + hauteur / 2;
    const vers = (positions: ArrayLike<number>): number[] => {
      const r: number[] = [];
      for (let k = 0; k < positions.length; k += 3) r.push(positions[k] * hauteur, yc + positions[k + 1] * hauteur, zTete + positions[k + 2] * hauteur);
      return r;
    };
    // Morceaux : peau (visage + oreilles), yeux, chevelure.
    const positions: number[] = [];
    const cols: number[] = [];
    const indices: number[] = [];
    const ajouter = (pos: number[], c: ArrayLike<number>, idx: ArrayLike<number>) => {
      const decal = positions.length / 3;
      positions.push(...pos);
      for (let k = 0; k < c.length; k++) cols.push(c[k]);
      for (let k = 0; k < idx.length; k++) indices.push(idx[k] + decal);
    };
    ajouter(vers(f.positions), f.couleurs, f.indices);
    const nVisage = f.positions.length;
    ajouter(vers(o.positions), o.couleurs, o.indices);
    const finPeau = indices.length;
    // Yeux.
    const c = couleurs(v);
    const globe = globeOeil(m.globe * hauteur, lineaire(c.yeux));
    const gp = globe.getAttribute("position");
    const gc = globe.getAttribute("color");
    const gi = globe.getIndex()!;
    const centres: THREE.Vector3[] = [];
    const debutYeux = positions.length;
    for (const cote of [-1, 1]) {
      const centre = globeEn(m, cote);
      const pos = new THREE.Vector3(centre.x * hauteur, yc + centre.y * hauteur, zTete + centre.z * hauteur);
      centres.push(pos);
      const p: number[] = [];
      for (let k = 0; k < gp.count; k++) p.push(gp.getX(k) + pos.x, gp.getY(k) + pos.y, gp.getZ(k) + pos.z);
      ajouter(p, gc.array as ArrayLike<number>, gi.array as ArrayLike<number>);
    }
    this.milieuYeux.copy(centres[0]).add(centres[1]).multiplyScalar(0.5);
    const finYeux = indices.length;
    const finPositionsYeux = positions.length;
    // Chevelure.
    const ch = chevelure(v, m, cheveux);
    if (ch) ajouter(vers(ch.getAttribute("position").array as ArrayLike<number>), ch.getAttribute("color").array as ArrayLike<number>, ch.getIndex()!.array as ArrayLike<number>);
    const total = new Float32Array(positions);
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.Float32BufferAttribute(total, 3));
    g.setAttribute("color", new THREE.Float32BufferAttribute(cols, 3));
    g.setIndex(indices);
    g.addGroup(0, finPeau, 0);
    g.addGroup(finPeau, finYeux - finPeau, 1);
    g.addGroup(finYeux, indices.length - finYeux, 2);
    g.computeVertexNormals();
    const normale0 = g.getAttribute("normal").array as Float32Array;
    // Cibles de morphose relatives : expressions (sur le visage), puis regards (les yeux tournent sur leur centre).
    const deltas: Float32Array[] = f.morphes.map((d) => {
      const delta = new Float32Array(total.length);
      for (let k = 0; k < nVisage; k++) delta[k] = d[k] * hauteur;
      return delta;
    });
    const q = new THREE.Quaternion();
    const e = new THREE.Euler();
    const w = new THREE.Vector3();
    for (const [lacet, tangage] of REGARDS) {
      const delta = new Float32Array(total.length);
      q.setFromEuler(e.set(tangage, lacet, 0, "YXZ"));
      for (let k = debutYeux; k < finPositionsYeux; k += 3) {
        const centre = k - debutYeux < (finPositionsYeux - debutYeux) / 2 ? centres[0] : centres[1];
        w.set(total[k], total[k + 1], total[k + 2]).sub(centre);
        const avant = w.clone();
        w.applyQuaternion(q);
        delta[k] = w.x - avant.x;
        delta[k + 1] = w.y - avant.y;
        delta[k + 2] = w.z - avant.z;
      }
      deltas.push(delta);
    }
    const morphesP: THREE.BufferAttribute[] = [];
    const morphesN: THREE.BufferAttribute[] = [];
    for (const delta of deltas) {
      const deplace = total.slice();
      for (let k = 0; k < delta.length; k++) deplace[k] += delta[k];
      const n2 = normalesDe(deplace, indices);
      const dn = new Float32Array(total.length);
      for (let k = 0; k < dn.length; k++) dn[k] = n2[k] - normale0[k];
      morphesP.push(new THREE.Float32BufferAttribute(delta, 3));
      morphesN.push(new THREE.Float32BufferAttribute(dn, 3));
    }
    g.morphAttributes.position = morphesP;
    g.morphAttributes.normal = morphesN;
    g.morphTargetsRelative = true;
    this.visage = new THREE.Mesh(g, [materiaux.peau, materiaux.yeux, materiaux.cheveux]);
    this.visage.morphTargetInfluences = deltas.map(() => 0);
    this.visage.frustumCulled = false;
    this.visage.matrixAutoUpdate = false;
    this.groupe.add(this.visage);
    this.groupe.matrixAutoUpdate = false;
  }

  /**
   * Place la tête sur le corps et l'anime : `matrice` = repère du corps au repos → monde (maillage × os × inverse de liaison) ;
   * `expressions` : poids de chaque expression ; `regard` : point du monde à regarder (ou null : regard qui erre) ; `dort` : yeux clos.
   */
  poser(matrice: THREE.Matrix4, expressions: Record<Expression, number>, regard: THREE.Vector3 | null, dort: boolean, temps: number): void {
    this.groupe.matrix.copy(matrice);
    this.groupe.matrixWorld.copy(matrice);
    this.visage.matrixWorld.copy(matrice);
    const infl = this.visage.morphTargetInfluences!;
    EXPRESSIONS.forEach((e, k) => (infl[k] = expressions[e] ?? 0));
    // Clignement : toutes les 2 à 7 secondes, 150 ms.
    if (temps > this.clignement.prochain) {
      this.clignement.debut = temps;
      this.clignement.prochain = temps + 2 + 5 * unite(hacher(Math.floor(temps * 10), Math.floor(this.graine)));
    }
    const dt = temps - this.clignement.debut;
    const cligne = dt >= 0 && dt < 0.16 ? Math.sin((dt / 0.16) * Math.PI) : 0;
    infl[5] = dort ? 1 : Math.max(cligne, infl[5]);
    // Regard : vers le point demandé (dans les limites de l'orbite), sinon de petits mouvements de temps en temps.
    const r = this.regard;
    if (regard) {
      this.inverse.copy(matrice).invert();
      this.local.copy(regard).applyMatrix4(this.inverse).sub(this.milieuYeux);
      const d = this.local;
      r.cibleL = clamp(Math.atan2(d.x, d.z), -LACET_MAX, LACET_MAX);
      r.cibleT = clamp(-Math.atan2(d.y, Math.hypot(d.x, d.z)), -TANGAGE_MAX, TANGAGE_MAX);
    } else if (temps > r.prochain) {
      r.cibleL = (unite(hacher(Math.floor(temps * 3), Math.floor(this.graine), 1)) - 0.5) * 0.3;
      r.cibleT = (unite(hacher(Math.floor(temps * 3), Math.floor(this.graine), 2)) - 0.5) * 0.12;
      r.prochain = temps + 0.8 + 2.5 * unite(hacher(Math.floor(temps * 7), Math.floor(this.graine), 3));
    }
    // Saccades : les yeux vont vite à leur cible.
    r.lacet += (r.cibleL - r.lacet) * 0.35;
    r.tangage += (r.cibleT - r.tangage) * 0.35;
    const n = EXPRESSIONS.length;
    infl[n] = Math.max(0, r.lacet) / LACET_MAX;
    infl[n + 1] = Math.max(0, -r.lacet) / LACET_MAX;
    infl[n + 2] = Math.max(0, r.tangage) / TANGAGE_MAX;
    infl[n + 3] = Math.max(0, -r.tangage) / TANGAGE_MAX;
  }

  liberer(): void {
    this.visage.geometry.dispose();
    this.groupe.removeFromParent();
  }
}
