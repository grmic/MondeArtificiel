/**
 * Relief gardé dans le navigateur (IndexedDB) : les tuiles de sol déjà calculées reviennent aussitôt après un rechargement de la
 * page ou une relance du serveur, au lieu d'être recalculées tuile par tuile depuis la graine (« relief en construction »).
 *
 * Clé d'une tuile : empreinte du monde (graine, taille, biomes, roches, pentes, rivières), version du code 3D (le nom du fichier
 * construit par Vite change dès qu'une ligne du rendu change), état des couleurs (saison, ou jour quand il gèle), position et
 * taille de la tuile. Un seul monde et un seul état des couleurs sont gardés : le reste est effacé à l'ouverture.
 * Sans IndexedDB (navigation privée, stockage bloqué), tout se calcule comme avant.
 */

/** Une tuile prête : hauteurs, normales, couleurs et couches des points (jupe comprise), nappe d'eau douce. */
export interface TuileGardee {
  y: Float32Array;
  normales: Float32Array;
  couleurs: Float32Array;
  /** Poids des 8 couches du sol, sur 0-255 (null : sol sans photographies). */
  poids: Uint8Array | null;
  lointains: Float32Array | null;
  eauPositions: Float32Array | null;
  eauIndices: Uint16Array | null;
  hMin: number;
  hMax: number;
}

const BASE = "monde-relief";
const MAGASIN = "tuiles";
/** Au-delà, plus rien n'est ajouté (≈ 60 Ko par tuile). */
const MAX_TUILES = 2500;

/** Empreinte FNV-1a de tableaux d'octets et de nombres. */
export function empreinte(parties: readonly (ArrayLike<number> | number | string)[]): string {
  let h = 0x811c9dc5;
  const ajouter = (v: number) => {
    h ^= v & 0xff;
    h = Math.imul(h, 0x01000193);
  };
  for (const p of parties) {
    if (typeof p === "number") for (let k = 0; k < 4; k++) ajouter(p >>> (k * 8));
    else if (typeof p === "string") for (let k = 0; k < p.length; k++) ajouter(p.charCodeAt(k));
    else for (let k = 0; k < p.length; k++) ajouter(p[k]);
    ajouter(0x7c);
  }
  return (h >>> 0).toString(36);
}

/** Version du code 3D : le nom du morceau construit (ex. vue3d-B-QgRa5K.js) change avec son contenu. */
function versionCode(): string {
  try {
    const u = new URL(import.meta.url);
    return u.pathname.split("/").pop() ?? "dev";
  } catch {
    return "dev";
  }
}

function requete<T>(r: IDBRequest<T>): Promise<T> {
  return new Promise((ok, ko) => {
    r.onsuccess = () => ok(r.result);
    r.onerror = () => ko(r.error);
  });
}

export class CacheRelief {
  private readonly tuiles = new Map<string, TuileGardee>();
  private cleCouleurs: string | null = null;
  private nombre = 0;
  private aEcrire: [string, TuileGardee][] = [];
  private ecriture = false;

  private constructor(private readonly base: IDBDatabase, private readonly monde: string) {}

  static async ouvrir(empreinteMonde: string): Promise<CacheRelief | null> {
    try {
      if (typeof indexedDB === "undefined") return null;
      const demande = indexedDB.open(BASE, 1);
      demande.onupgradeneeded = () => demande.result.createObjectStore(MAGASIN);
      const base = await requete(demande);
      return new CacheRelief(base, `${empreinteMonde}|${versionCode()}`);
    } catch {
      return null;
    }
  }

  /** État des couleurs connu : charge en mémoire les tuiles gardées pour lui, efface les autres (autre monde, saison, version). */
  async choisirCouleurs(cle: string): Promise<void> {
    if (cle === this.cleCouleurs) return;
    this.cleCouleurs = cle;
    this.tuiles.clear();
    this.aEcrire = [];
    const prefixe = `${this.monde}|${cle}|`;
    try {
      const t = this.base.transaction(MAGASIN, "readwrite");
      const magasin = t.objectStore(MAGASIN);
      const cles = (await requete(magasin.getAllKeys())) as string[];
      let gardees = 0;
      for (const c of cles) {
        if (c.startsWith(prefixe)) gardees++;
        else magasin.delete(c);
      }
      const valeurs = (await requete(magasin.getAll(IDBKeyRange.bound(prefixe, prefixe + "￿")))) as TuileGardee[];
      const lues = (await requete(magasin.getAllKeys(IDBKeyRange.bound(prefixe, prefixe + "￿")))) as string[];
      if (this.cleCouleurs !== cle) return;
      lues.forEach((c, i) => this.tuiles.set(c.slice(prefixe.length), valeurs[i]));
      this.nombre = gardees;
    } catch {
      // Lecture impossible : les tuiles se calculent.
    }
  }

  private cle(x0: number, z0: number, taille: number): string {
    return `${x0}|${z0}|${taille}`;
  }

  lire(x0: number, z0: number, taille: number): TuileGardee | undefined {
    return this.cleCouleurs === null ? undefined : this.tuiles.get(this.cle(x0, z0, taille));
  }

  /** Garde une tuile calculée (écrite par paquets, hors de l'image). */
  garder(x0: number, z0: number, taille: number, tuile: TuileGardee): void {
    if (this.cleCouleurs === null || this.nombre >= MAX_TUILES) return;
    const c = this.cle(x0, z0, taille);
    this.tuiles.set(c, tuile);
    this.nombre++;
    this.aEcrire.push([`${this.monde}|${this.cleCouleurs}|${c}`, tuile]);
    if (!this.ecriture) {
      this.ecriture = true;
      setTimeout(() => this.ecrire(), 500);
    }
  }

  private ecrire(): void {
    const paquet = this.aEcrire;
    this.aEcrire = [];
    this.ecriture = false;
    if (paquet.length === 0) return;
    try {
      const t = this.base.transaction(MAGASIN, "readwrite");
      const magasin = t.objectStore(MAGASIN);
      for (const [c, v] of paquet) magasin.put(v, c);
    } catch {
      // Stockage plein ou refusé : tant pis, rien n'est perdu à l'écran.
    }
  }
}
