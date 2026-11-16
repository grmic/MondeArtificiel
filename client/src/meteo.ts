/**
 * Le temps qu'il fait (0.43.0) : les averses que le serveur tire des jours de pluie de la simulation (hub « Temps »), et ce
 * qu'elles donnent à un instant donné sur chaque région : force de la pluie, neige, orage, sol mouillé. Partagé par la carte et
 * la vue 3D, qui voient ainsi le même ciel.
 */

export const enum Sorte {
  Pluie = 1,
  Neige = 2,
  Orage = 3,
}

export interface TempsJson {
  jour: number;
  regionsX: number;
  regionsY: number;
  tailleRegion: number;
  /** Par 7 : région, début, fin (heures depuis minuit du jour, négatives pour la veille), force, sorte, x et y du cœur de l'orage. */
  averses: number[];
}

export interface Averse {
  region: number;
  debut: number;
  fin: number;
  force: number;
  sorte: Sorte;
  x: number;
  y: number;
}

/** L'état du ciel sur chaque région à un instant. */
export interface EtatTemps {
  regionsX: number;
  regionsY: number;
  tailleRegion: number;
  /** Pluie qui tombe (0 à 1), neige qui tombe (0 à 1), orage (0 à 1), sol mouillé (0 à 1). */
  pluie: Float32Array;
  neige: Float32Array;
  orage: Float32Array;
  mouille: Float32Array;
  /** Averses d'orage en cours (pour les éclairs) : région, case du cœur (−1 si nulle part en particulier), force. */
  orages: { region: number; x: number; y: number; force: number }[];
}

export function lireAverses(t: TempsJson): Averse[] {
  const a: Averse[] = [];
  for (let k = 0; k + 6 < t.averses.length; k += 7) {
    const v = t.averses;
    a.push({ region: v[k], debut: v[k + 1], fin: v[k + 2], force: v[k + 3], sorte: v[k + 4] as Sorte, x: v[k + 5], y: v[k + 6] });
  }
  return a;
}

/** Montée et fin d'une averse (heures) : la pluie ne commence ni ne cesse d'un coup. */
const RAMPE = 0.25;

/**
 * Ce qui tombe et ce qui est mouillé à l'instant `secondes` du monde. Le sol mouillé sèche en quelques heures après la pluie,
 * plus vite par temps chaud et à midi qu'au matin : ici de 2 h après une petite pluie à 6 h après une forte (ordre de grandeur de
 * l'évaporation d'un sol nu après quelques millimètres de pluie).
 */
export function etatA(t: TempsJson, averses: Averse[], secondes: number): EtatTemps {
  const n = t.regionsX * t.regionsY;
  const e: EtatTemps = {
    regionsX: t.regionsX, regionsY: t.regionsY, tailleRegion: t.tailleRegion,
    pluie: new Float32Array(n), neige: new Float32Array(n), orage: new Float32Array(n), mouille: new Float32Array(n), orages: [],
  };
  const h = (secondes - t.jour * 86400) / 3600;
  for (const a of averses) {
    if (a.region < 0 || a.region >= n) continue;
    const dans = Math.min((h - a.debut) / RAMPE, (a.fin - h) / RAMPE, 1);
    if (dans > 0) {
      const f = a.force * dans;
      if (a.sorte === Sorte.Neige) e.neige[a.region] = Math.max(e.neige[a.region], f);
      else e.pluie[a.region] = Math.max(e.pluie[a.region], f);
      if (a.sorte === Sorte.Orage) {
        e.orage[a.region] = Math.max(e.orage[a.region], f);
        e.orages.push({ region: a.region, x: a.x, y: a.y, force: f });
      }
    }
    if (a.sorte === Sorte.Neige || h < a.debut) continue;
    // Mouillé : se mouille en une demi-heure, sèche ensuite.
    const mouille = h <= a.fin ? Math.min(1, (h - a.debut) / 0.5) * (0.5 + 0.5 * a.force) : (0.5 + 0.5 * a.force) * Math.exp(-(h - a.fin) / (2 + 4 * a.force));
    e.mouille[a.region] = Math.max(e.mouille[a.region], mouille);
  }
  return e;
}

/** Région d'une case. */
export function regionDe(t: { regionsX: number; tailleRegion: number }, x: number, y: number): number {
  return Math.floor(y / t.tailleRegion) * t.regionsX + Math.floor(x / t.tailleRegion);
}

/** Une catastrophe en cours, ou dont les terres ne sont pas remises (hub « EnCours », 0.43.0). */
export interface ZoneJson {
  id: number;
  type: string;
  nom: string;
  active: boolean;
  x: number;
  y: number;
  rayon: number;
  intensite: number;
  phase: number;
  /** Direction du vent d'un incendie (radians ; 0 : vers l'est, π/2 : vers le sud de la carte). */
  direction: number;
  debut: number;
  cases: number[];
  /** Dégâts qui restent sur chaque case (0 à 255), en Base64. */
  degats: string;
  front: number[];
}

export interface EnCoursJson {
  secondes: number;
  zones: ZoneJson[];
  /** Tout le continent : intensité (part d'une éclipse, écart de température…), début et durée prévue (secondes, 0.43.1). */
  continent: { id: number; type: string; nom: string; intensite: number; debut: number; duree: number }[];
}

/** Dégâts d'une zone, octet par case (le serveur envoie un tableau d'octets en Base64). */
export function degatsDe(z: ZoneJson): Uint8Array {
  return z.degats ? Uint8Array.from(atob(z.degats), (c) => c.charCodeAt(0)) : new Uint8Array(z.cases.length);
}
