import * as THREE from "three/webgpu";
import { hacher, unite } from "./bruit";
import type { Relief } from "./relief";
import { aPiedSec, profondeur } from "./foule";
import { ARME, type GesteCombat, type RejeuJouable, type Role } from "./combat";

/**
 * Rejeu d'une attaque de bête dans la vue 3D (0.45.25, choix B1). La simulation résout l'attaque d'un coup et en note le déroulé :
 * la victime (endormie ou non), ce qu'elle a fait (se figer, se défendre, fuir), ceux qui sont accourus et ce qu'ils tenaient, ceux
 * qui ont pris un coup de griffes, et l'issue (tuée, emportée, lâchée, repoussée, échappée ; la bête blessée, ou tuée et par qui).
 * Ici on le met en scène sur 20 à 60 secondes : l'approche à couvert, l'arrêt, le bond, la lutte, ceux qui accourent, puis l'issue.
 * Rien n'est inventé : chaque issue est celle de la simulation ; seuls la place des corps et le rythme des gestes se calculent ici.
 * Sobre (C1) : ni sang, ni ralenti sur une mort.
 */

/** Ce que le serveur envoie d'une attaque (AttaqueVueDto). */
export interface AttaqueJson {
  id: number;
  secondes: number;
  x: number;
  y: number;
  espece: number;
  fauve: number;
  victime: number;
  /** Celui que la bête visait, quand elle a pris le tout-petit qui dormait contre lui (0 sinon). */
  cible: number;
  mort: boolean;
  /** Faux pour une attaque d'avant la 0.45.25 : on n'en connaît que la victime et l'issue. */
  deroule: boolean;
  dort: boolean;
  auCamp: boolean;
  eau: boolean;
  /** 0 se fige, 1 se défend, 2 fuit. */
  reaction: number;
  armeVictime: number;
  aides: number[];
  /** Arme de chacun (ARME), ou −1 : un tison du feu. */
  armesAides: number[];
  griffes: number[];
  /** 0 tuée, 1 emportée, 2 lâchée, 3 repoussée, 4 échappée. */
  issue: number;
  beteBlessee: boolean;
  tueur: number;
}

export const ISSUE_ATTAQUE = { Tuee: 0, Emportee: 1, Lachee: 2, Repoussee: 3, Echappee: 4 } as const;
const REACTION = { SeFige: 0, SeDefend: 1, Fuit: 2 } as const;

/** Une bête à dessiner à cet instant (Faune3D.jouees). */
export interface BeteJouee {
  espece: number;
  x: number;
  z: number;
  cap: number;
  /** 0 arrêt, 1 pas, 2 course. */
  allure: number;
  /** Mètres parcourus depuis le début (la foulée en découle). */
  parcours: number;
  /** Tête basse (0 à 1), ou −1 : morte, couchée sur le flanc. */
  tete: number;
  /** Tourner la tête (rad) : secouer sa prise. */
  regard: number;
  /** Ramassée (0 à 1) : à l'affût. */
  couche: number;
  /** Enfoncée dans l'eau (0 à 1) : le grand nageur qui guette. */
  plonge: number;
}

interface Acteur {
  id: number;
  x0: number;
  z0: number;
  arme: number;
  /** Instant où il s'élance (aides), et place qu'il prend autour de la bête. */
  depart: number;
  angle: number;
  griffe: boolean;
}

/** Une bête du rejeu : la meneuse (k = 0), ou une de sa meute. */
interface Bete {
  x0: number;
  z0: number;
  /** Où elle se poste autour de la proie (meute), et d'où elle fuit. */
  ox: number;
  oz: number;
  fx: number;
  fz: number;
}

/** Durées (s, à ×1) : approche à couvert, arrêt, bond, puis la lutte (selon ceux qui accourent) et l'après. */
const APPROCHE = 9;
const ARRET = 1.6;
const BOND = 0.8;
const APRES = 12;

/** Les rôdeurs (forme 4) chassent en meute : on en voit 2 à 4 de plus autour de celui qui attaque. */
const FORME_RODEUR = 4;

export class RejeuAttaque implements RejeuJouable {
  readonly duree: number;
  readonly centre: THREE.Vector3;
  readonly gisants = new Map<number, { x: number; z: number; cap: number }>();
  /** Humains à faire venir dans la scène, où qu'ils soient (la victime, celui qu'on visait, ceux qui sont accourus). */
  readonly avec: number[];
  private readonly acteurs = new Map<number, Acteur>();
  private readonly betes: Bete[] = [];
  private readonly vx: number;
  private readonly vz: number;
  /** Point d'eau d'où surgit le grand nageur (attaque dans l'eau), ou null. */
  private readonly eau: [number, number] | null;
  private readonly impact: number;
  private readonly finLutte: number;
  private readonly tueur: Acteur | undefined;

  constructor(readonly attaque: AttaqueJson, private readonly relief: Relief, readonly forme: number,
    places: Map<number, { x: number; z: number }>, recente: boolean) {
    const a = attaque;
    const [cx, cz] = relief.camp(a.x, a.y);
    const h = (...k: number[]) => unite(hacher(a.id, ...k));
    // La victime : là où elle est si l'attaque vient d'avoir lieu et qu'elle est près d'ici ; sinon au camp ou loin dans la case.
    const ici = recente ? places.get(a.victime) : undefined;
    let vx: number;
    let vz: number;
    this.eau = null;
    if (ici && Number.isFinite(ici.x) && Math.abs(ici.x / relief.m - (a.x + 0.5)) < 1 && Math.abs(ici.z / relief.m - (a.y + 0.5)) < 1) [vx, vz] = [ici.x, ici.z];
    else if (a.eau) {
      const w = eauProche(relief, (a.x + 0.5) * relief.m, (a.y + 0.5) * relief.m, a.id);
      if (w) {
        this.eau = w;
        [vx, vz] = aPiedSec(relief, w[0], w[1], 0.05, hacher(a.id, 3));
      } else[vx, vz] = [cx, cz];
    } else if (a.auCamp) {
      const ang = h(5) * Math.PI * 2;
      const r = 4 + 8 * h(7);
      [vx, vz] = aPiedSec(relief, cx + Math.cos(ang) * r, cz + Math.sin(ang) * r, 0.05, hacher(a.id, 9));
    } else {
      const ang = h(11) * Math.PI * 2;
      const r = 70 + 160 * h(13);
      [vx, vz] = aPiedSec(relief, cx + Math.cos(ang) * r, cz + Math.sin(ang) * r, 0.05, hacher(a.id, 15));
    }
    this.vx = vx;
    this.vz = vz;
    this.centre = new THREE.Vector3(vx, relief.hauteur(vx, vz), vz);

    // La bête vient de l'extérieur du camp (ou de l'eau), à 18-26 m.
    let ux: number;
    let uz: number;
    if (this.eau) [ux, uz] = [this.eau[0] - vx, this.eau[1] - vz];
    else if (a.auCamp && Math.hypot(vx - cx, vz - cz) > 1) [ux, uz] = [vx - cx, vz - cz];
    else {
      const ang = h(17) * Math.PI * 2;
      [ux, uz] = [Math.cos(ang), Math.sin(ang)];
    }
    const n = Math.hypot(ux, uz) || 1;
    ux /= n;
    uz /= n;
    const loin = this.eau ? Math.max(6, Math.min(14, Math.hypot(this.eau[0] - vx, this.eau[1] - vz) + 4)) : 18 + 8 * h(19);
    const meute = forme === FORME_RODEUR && !a.eau ? 2 + Math.floor(h(21) * 3) : 0;
    for (let k = 0; k <= meute; k++) {
      const lat = k === 0 ? 0 : (k % 2 ? 1 : -1) * (3 + 2.5 * Math.ceil(k / 2)) + (h(23, k) - 0.5) * 2;
      const recul = k === 0 ? 0 : 3 + 4 * h(25, k);
      const x0 = vx + ux * (loin + recul) - uz * lat;
      const z0 = vz + uz * (loin + recul) + ux * lat;
      // La meute se poste en cercle autour de la proie, à 6-10 m, du côté d'où elle vient.
      const oa = Math.atan2(ux, uz) + (k === 0 ? 0 : (k % 2 ? 1 : -1) * (0.6 + 0.35 * Math.ceil(k / 2)));
      const or = k === 0 ? 0 : 6 + 4 * h(27, k);
      const fuite = 45 + 20 * h(29, k);
      this.betes.push({ x0, z0, ox: vx + Math.sin(oa) * or, oz: vz + Math.cos(oa) * or, fx: vx + ux * fuite - uz * lat * 2, fz: vz + uz * fuite + ux * lat * 2 });
    }

    // Ceux qui accourent : de là où ils sont (s'ils sont près), sinon d'un peu plus loin dans le camp ; ils encerclent la bête.
    const debutLutte = APPROCHE + ARRET + BOND;
    this.impact = debutLutte;
    const aides = a.deroule ? a.aides : [];
    aides.forEach((id, k) => {
      const p = recente ? places.get(id) : undefined;
      let x0: number;
      let z0: number;
      if (p && Number.isFinite(p.x) && Math.hypot(p.x - vx, p.z - vz) < 60) [x0, z0] = [p.x, p.z];
      else {
        const ang = Math.atan2(-ux, -uz) + (h(31, k) - 0.5) * 2.4;
        const r = (a.auCamp ? 8 : 18) + 14 * h(33, k);
        [x0, z0] = aPiedSec(relief, vx + Math.sin(ang) * r, vz + Math.cos(ang) * r, 0.05, hacher(id, 35));
      }
      const angle = Math.atan2(x0 - vx, z0 - vz) + (h(37, k) - 0.5) * 0.8;
      this.acteurs.set(id, {
        id, x0, z0, arme: (a.armesAides[k] ?? 0) < 0 ? ARME.Baton : a.armesAides[k] ?? 0, depart: debutLutte + 0.6 + k * 0.7 + 0.8 * h(39, k), angle,
        griffe: a.griffes.includes(id),
      });
    });
    // La lutte dure le temps que les aides arrivent et frappent : 3 s seule, jusqu'à 10 s.
    const arrivees = [...this.acteurs.values()].map((x) => x.depart + Math.hypot(x.x0 - vx, x.z0 - vz) / 4.5);
    this.finLutte = Math.min(debutLutte + 10, Math.max(debutLutte + 3 + (a.reaction === REACTION.SeDefend ? 1.5 : 0), ...arrivees.map((t) => t + 3)));
    this.tueur = a.tueur ? this.acteurs.get(a.tueur) : undefined;
    const emportee = a.issue === ISSUE_ATTAQUE.Emportee;
    this.duree = this.finLutte + (emportee ? APRES + 4 : APRES);
    this.avec = [...new Set([a.victime, a.cible, ...aides].filter((id) => id > 0))];
  }

  get id(): number {
    return this.attaque.id;
  }

  /** La bête qui attaque, à l'instant t. */
  private meneuse(t: number): BeteJouee {
    const a = this.attaque;
    const b = this.betes[0];
    const r: BeteJouee = { espece: a.espece, x: b.x0, z: b.z0, cap: Math.atan2(this.vx - b.x0, this.vz - b.z0), allure: 0, parcours: 0, tete: 0.3, regard: 0, couche: 0, plonge: this.eau ? 1 : 0 };
    const arret = [this.vx + (b.x0 - this.vx) * 0.28, this.vz + (b.z0 - this.vz) * 0.28];
    const dApproche = Math.hypot(arret[0] - b.x0, arret[1] - b.z0);
    if (t < APPROCHE) {
      // À couvert : ramassée, tête basse, au pas, par à-coups.
      const u = t / APPROCHE;
      const e = u + 0.04 * Math.sin(u * Math.PI * 6);
      r.x = b.x0 + (arret[0] - b.x0) * e;
      r.z = b.z0 + (arret[1] - b.z0) * e;
      r.allure = this.eau ? 0.4 : 0.55 + 0.25 * Math.max(0, Math.sin(u * Math.PI * 6));
      r.parcours = dApproche * u;
      r.tete = 0.75;
      r.couche = this.eau ? 0 : 0.35;
      return r;
    }
    if (t < APPROCHE + ARRET) {
      // Immobile, prête à bondir.
      [r.x, r.z] = arret;
      r.parcours = dApproche;
      r.tete = 0.85;
      r.couche = this.eau ? 0 : 0.45;
      r.plonge = this.eau ? 0.85 : 0;
      return r;
    }
    const contact = [this.vx + (b.x0 - this.vx) * 0.04, this.vz + (b.z0 - this.vz) * 0.04];
    const fuit = a.reaction === REACTION.Fuit && a.deroule;
    // Celle qui fuit est rattrapée quelques mètres plus loin.
    const prise = fuit ? this.posVictime(this.impact) : contact;
    if (t < this.impact) {
      const u = (t - APPROCHE - ARRET) / BOND;
      r.x = arret[0] + (prise[0] - arret[0]) * u;
      r.z = arret[1] + (prise[1] - arret[1]) * u;
      r.allure = 2;
      r.parcours = dApproche + Math.hypot(prise[0] - arret[0], prise[1] - arret[1]) * u;
      r.tete = 0.4;
      r.plonge = this.eau ? 0.85 * (1 - u) : 0;
      return r;
    }
    const base = dApproche + Math.hypot(prise[0] - arret[0], prise[1] - arret[1]);
    [r.x, r.z] = prise;
    r.parcours = base;
    if (t < this.finLutte) {
      // La lutte : tête basse sur la proie, elle secoue ; elle tourne la tête vers ceux qui frappent.
      r.tete = 1;
      r.regard = 0.45 * Math.sin((t - this.impact) * 7) * (0.6 + 0.4 * Math.sin((t - this.impact) * 1.3));
      return r;
    }
    const apres = t - this.finLutte;
    if (a.tueur) {
      // Tuée par ceux qui se défendent : elle s'effondre là.
      r.tete = apres < 0.8 ? 1 - apres * 2.5 : -1;
      return r;
    }
    if (a.issue === ISSUE_ATTAQUE.Emportee) {
      // Elle emporte sa proie, à reculons puis de face, vers d'où elle est venue (sous l'eau pour le grand nageur).
      const u = Math.min(1, apres / (APRES + 2));
      const fx = this.eau ? this.eau[0] + (this.eau[0] - this.vx) * 1.5 : b.fx;
      const fz = this.eau ? this.eau[1] + (this.eau[1] - this.vz) * 1.5 : b.fz;
      r.x = prise[0] + (fx - prise[0]) * u;
      r.z = prise[1] + (fz - prise[1]) * u;
      r.cap = Math.atan2(fx - prise[0], fz - prise[1]);
      r.allure = u < 1 ? 1 : 0;
      r.parcours = base + Math.hypot(fx - prise[0], fz - prise[1]) * u;
      r.tete = 0.7;
      r.plonge = this.eau ? Math.min(1, apres / 3) : 0;
      return r;
    }
    if (a.issue === ISSUE_ATTAQUE.Tuee && this.acteurs.size === 0) {
      // Seule avec sa proie morte : elle reste sur elle.
      r.tete = 1;
      r.regard = 0.15 * Math.sin(apres * 1.5);
      return r;
    }
    // Lâche prise : elle recule, puis s'enfuit (en boitant si elle est blessée).
    const fx = this.eau ? this.eau[0] + (this.eau[0] - this.vx) * 1.5 : b.fx;
    const fz = this.eau ? this.eau[1] + (this.eau[1] - this.vz) * 1.5 : b.fz;
    const vitesse = a.beteBlessee ? 3.5 : 8;
    const d = Math.hypot(fx - prise[0], fz - prise[1]);
    const u = Math.min(1, Math.max(0, apres - 0.6) * vitesse / Math.max(1, d));
    r.x = prise[0] + (fx - prise[0]) * u;
    r.z = prise[1] + (fz - prise[1]) * u;
    r.cap = apres < 0.6 ? r.cap : Math.atan2(fx - prise[0], fz - prise[1]);
    r.allure = u < 1 ? (a.beteBlessee ? 1.2 : 2) : 0;
    r.parcours = base + d * u;
    r.tete = 0.3;
    r.plonge = this.eau ? Math.min(1, u * 3) : 0;
    return r;
  }

  /** Les bêtes à dessiner à l'instant t (la meneuse et sa meute). */
  betesA(t: number): BeteJouee[] {
    const liste = [this.meneuse(t)];
    const m = liste[0];
    for (let k = 1; k < this.betes.length; k++) {
      const b = this.betes[k];
      const r: BeteJouee = { espece: this.attaque.espece, x: b.x0, z: b.z0, cap: m.cap, allure: 0, parcours: 0, tete: 0.6, regard: 0, couche: 0.3, plonge: 0 };
      const dPoste = Math.hypot(b.ox - b.x0, b.oz - b.z0);
      if (t < this.impact + 1.5) {
        const u = Math.min(1, t / (this.impact + 1.5));
        r.x = b.x0 + (b.ox - b.x0) * u;
        r.z = b.z0 + (b.oz - b.z0) * u;
        r.allure = u < 1 ? (t > APPROCHE + ARRET ? 1.8 : 0.6) : 0;
        r.parcours = dPoste * u;
        r.cap = u < 1 ? Math.atan2(b.ox - b.x0, b.oz - b.z0) : Math.atan2(this.vx - b.ox, this.vz - b.oz);
      } else if (t < this.finLutte + 0.6) {
        // Elle tourne autour, nerveuse, face à la proie.
        const w = (t - this.impact) * 0.8 + k;
        r.x = b.ox + Math.sin(w) * 1.2;
        r.z = b.oz + Math.cos(w) * 1.2;
        r.allure = 0.6;
        r.parcours = dPoste + (t - this.impact) * 1.2;
        r.cap = Math.atan2(this.vx - r.x, this.vz - r.z);
        r.tete = 0.5;
      } else {
        // Elle suit la meneuse quand celle-ci part.
        const apres = t - this.finLutte - 0.6;
        const d = Math.hypot(b.fx - b.ox, b.fz - b.oz);
        const u = Math.min(1, (apres * 7) / Math.max(1, d));
        if (this.attaque.issue === ISSUE_ATTAQUE.Tuee && this.acteurs.size === 0 && !this.attaque.tueur) {
          r.x = b.ox;
          r.z = b.oz;
          r.cap = Math.atan2(this.vx - b.ox, this.vz - b.oz);
          r.couche = 0.6;
        } else {
          r.x = b.ox + (b.fx - b.ox) * u;
          r.z = b.oz + (b.fz - b.oz) * u;
          r.cap = Math.atan2(b.fx - b.ox, b.fz - b.oz);
          r.allure = u < 1 ? 2 : 0;
          r.parcours = dPoste + d * u;
          r.couche = 0;
        }
      }
      liste.push(r);
    }
    return liste;
  }

  /** Où est la victime à l'instant t, avant le bond : celle qui fuit s'éloigne de la bête. */
  private posVictime(t: number): [number, number] {
    const a = this.attaque;
    if (a.reaction !== REACTION.Fuit || !a.deroule) return [this.vx, this.vz];
    const b = this.betes[0];
    const depart = APPROCHE + ARRET * 0.4;
    if (t < depart) return [this.vx, this.vz];
    const n = Math.hypot(this.vx - b.x0, this.vz - b.z0) || 1;
    const d = Math.min(t, this.impact) - depart;
    const v = 4.5;
    return [this.vx + ((this.vx - b.x0) / n) * d * v, this.vz + ((this.vz - b.z0) / n) * d * v];
  }

  foyer(t: number): { x: number; z: number; large: boolean } {
    const m = this.meneuse(t);
    if (t < APPROCHE + ARRET) return { x: (m.x + this.vx) / 2, z: (m.z + this.vz) / 2, large: true };
    if (t < this.finLutte + 2) return { x: m.x, z: m.z, large: this.acteurs.size > 1 };
    return { x: (m.x + this.vx) / 2, z: (m.z + this.vz) / 2, large: true };
  }

  role(id: number, t: number): Role | null {
    const a = this.attaque;
    if (id === a.victime) return this.victime(t);
    if (id === a.cible && a.cible > 0) return this.cible(t);
    const ac = this.acteurs.get(id);
    return ac ? this.aide(ac, t) : null;
  }

  private nouveau(x: number, z: number, cap: number | null, geste: GesteCombat | null, u = 0, arme = 0): Role {
    return { x, z, cap, geste, u, arme, court: false, mort: false, face: 0 };
  }

  private victime(t: number): Role {
    const a = this.attaque;
    const m = this.meneuse(t);
    const versBete = Math.atan2(m.x - this.vx, m.z - this.vz);
    const arme = a.armeVictime;
    if (t < APPROCHE + ARRET) {
      // Avant le bond : elle dort, ou vaque ; celle qui fuit se retourne au dernier moment.
      if (a.dort) return this.nouveau(this.vx, this.vz, null, "coucheDos", 1);
      if (a.reaction === REACTION.Fuit && a.deroule && t > APPROCHE + ARRET * 0.4) {
        const [x, z] = this.posVictime(t);
        const r = this.nouveau(x, z, versBete + Math.PI, null);
        r.court = true;
        return r;
      }
      return this.nouveau(this.vx, this.vz, t > APPROCHE ? versBete : null, t > APPROCHE + 0.6 ? "sursaut" : null, Math.min(1, (t - APPROCHE) / ARRET));
    }
    const [px, pz] = this.posVictime(t);
    if (t < this.impact) {
      if (a.reaction === REACTION.Fuit && a.deroule && !a.dort) {
        const r = this.nouveau(px, pz, versBete + Math.PI, null);
        r.court = true;
        return r;
      }
      if (a.reaction === REACTION.SeDefend && !a.dort) return this.nouveau(px, pz, versBete, "garde", 0, arme);
      return this.nouveau(px, pz, versBete, a.dort ? "coucheDos" : "effroi", 1);
    }
    const lutte = t - this.impact;
    if (t < this.finLutte) {
      // Celle qui se défend tient debout et frappe un moment, puis est renversée ; les autres sont renversées au bond.
      if (a.reaction === REACTION.SeDefend && !a.dort && lutte < (this.finLutte - this.impact) * 0.6) {
        const cycle = lutte / 1.2;
        return this.nouveau(px, pz, versBete, cycle % 1 < 0.6 ? (arme === ARME.Epieu || arme === ARME.Lance ? "estoc" : "frappe") : "parer", cycle % 1, arme);
      }
      const r = this.nouveau(px, pz, versBete, lutte < 0.9 ? "chute" : "recroqueville", Math.min(1, lutte / 0.9));
      return r;
    }
    const apres = t - this.finLutte;
    switch (a.issue) {
      case ISSUE_ATTAQUE.Tuee: {
        const r = this.nouveau(px, pz, versBete, "gisant", 1);
        r.mort = true;
        this.gisants.set(a.victime, { x: px, z: pz, cap: versBete });
        return r;
      }
      case ISSUE_ATTAQUE.Emportee: {
        // Traînée par la bête, à sa gueule.
        const r = this.nouveau(m.x + Math.sin(m.cap) * 0.9, m.z + Math.cos(m.cap) * 0.9, m.cap + Math.PI / 2, "gisant", 1);
        r.mort = true;
        return r;
      }
      case ISSUE_ATTAQUE.Echappee: {
        // Elle se relève et file vers le camp, blessée.
        const [cx, cz] = this.relief.camp(a.x, a.y);
        const loin = Math.hypot(cx - px, cz - pz) > 15 ? [cx, cz] : [px - Math.sin(versBete) * 20, pz - Math.cos(versBete) * 20];
        if (apres < 1.2) return this.nouveau(px, pz, versBete, "blesse", apres);
        const d = Math.hypot(loin[0] - px, loin[1] - pz);
        const u = Math.min(1, ((apres - 1.2) * 3.5) / Math.max(1, d));
        const r = this.nouveau(px + (loin[0] - px) * u, pz + (loin[1] - pz) * u, Math.atan2(loin[0] - px, loin[1] - pz), u < 1 ? null : "blesse", apres);
        r.court = u < 1;
        return r;
      }
      default:
        // Lâchée ou repoussée : à genoux, la main sur la plaie.
        return this.nouveau(px, pz, versBete, apres < 1 ? "recroqueville" : "blesse", apres);
    }
  }

  /** Celui contre qui dormait le tout-petit : réveillé en sursaut, il crie. */
  private cible(t: number): Role {
    const ac = this.acteurs.get(this.attaque.cible);
    if (ac) return this.aide(ac, t);
    const x = this.vx + 0.7;
    const z = this.vz + 0.4;
    const m = this.meneuse(t);
    const vers = Math.atan2(m.x - x, m.z - z);
    if (t < this.impact - 0.3) return this.nouveau(x, z, null, "coucheDos", 1);
    if (t < this.impact + 1.2) return this.nouveau(x, z, vers, "reveil", (t - this.impact + 0.3) / 1.5);
    return this.nouveau(x, z, vers, "crier", (t - this.impact - 1.2) % 1);
  }

  /** Celui qui accourt : il s'élance aux cris, encercle la bête et frappe ; griffé, il recule ; il achève la bête s'il la tue. */
  private aide(ac: Acteur, t: number): Role {
    const a = this.attaque;
    const m = this.meneuse(Math.min(t, this.finLutte));
    const px = m.x + Math.sin(ac.angle) * 1.9;
    const pz = m.z + Math.cos(ac.angle) * 1.9;
    const versBete = Math.atan2(m.x - px, m.z - pz);
    if (t < ac.depart) {
      const r = this.nouveau(ac.x0, ac.z0, t > this.impact ? Math.atan2(m.x - ac.x0, m.z - ac.z0) : null, t > this.impact ? "sursaut" : null, Math.min(1, (t - this.impact) / 0.6));
      return r;
    }
    const d = Math.hypot(px - ac.x0, pz - ac.z0);
    const arrivee = ac.depart + d / 4.5;
    if (t < arrivee) {
      const u = (t - ac.depart) / Math.max(0.1, arrivee - ac.depart);
      const r = this.nouveau(ac.x0 + (px - ac.x0) * u, ac.z0 + (pz - ac.z0) * u, Math.atan2(px - ac.x0, pz - ac.z0), null, 0, ac.arme);
      r.court = true;
      return r;
    }
    if (t < this.finLutte) {
      const dans = t - arrivee;
      // Griffé : un coup reçu au milieu de la lutte, il recule d'un pas et reste à genou.
      if (ac.griffe && dans > 1.2) {
        const recul = Math.min(1, (dans - 1.2) / 0.5);
        return this.nouveau(px + Math.sin(ac.angle) * recul, pz + Math.cos(ac.angle) * recul, versBete, dans < 2 ? "touche" : "blesse", dans - 1.2, ac.arme);
      }
      const cycle = dans / (1 + 0.3 * unite(hacher(ac.id, 41)));
      const longue = ac.arme === ARME.Epieu || ac.arme === ARME.Lance;
      return this.nouveau(px, pz, versBete, cycle % 1 < 0.65 ? (longue ? "estoc" : "frappe") : "garde", cycle % 1, ac.arme);
    }
    const apres = t - this.finLutte;
    if (ac.griffe) return this.nouveau(px + Math.sin(ac.angle), pz + Math.cos(ac.angle), versBete, "blesse", apres, ac.arme);
    if (ac === this.tueur && apres < 1.5) return this.nouveau(px, pz, versBete, "estoc", Math.min(1, apres / 1.5), ac.arme);
    // Ensuite il se tourne vers la victime, ou suit des yeux la bête qui s'enfuit.
    const m2 = this.meneuse(t);
    const voir = a.issue === ISSUE_ATTAQUE.Emportee || !a.tueur && a.issue !== ISSUE_ATTAQUE.Tuee ? Math.atan2(m2.x - px, m2.z - pz) : Math.atan2(this.vx - px, this.vz - pz);
    return this.nouveau(px, pz, voir, apres < 4 ? "crier" : "garde", apres % 1, ac.arme);
  }
}

/** Un point d'eau assez profond pour le grand nageur près de (x, z), à moins de 400 m ; null s'il n'y en a pas. */
function eauProche(rel: Relief, x: number, z: number, graine: number): [number, number] | null {
  if (profondeur(rel, x, z) > 0.8) return [x, z];
  for (let r = 10; r <= 400; r *= 1.3) {
    const depart = unite(hacher(graine, Math.round(r), 43)) * Math.PI * 2;
    for (let k = 0; k < 16; k++) {
      const a = depart + (k / 16) * Math.PI * 2;
      const px = x + Math.cos(a) * r;
      const pz = z + Math.sin(a) * r;
      if (profondeur(rel, px, pz) > 0.8) return [px, pz];
    }
  }
  return null;
}
