import { hacher, unite } from "./bruit";
import type { Corps } from "./foule";

/**
 * Être avec les autres (H1, 0.41.8), calculé à l'affichage seulement : ceux qui se tiennent près d'un feu se tournent vers lui
 * (le cercle se forme de lui-même, chacun assis à sa place autour du camp) ; deux voisins arrêtés se font face et causent, chacun
 * son tour, d'autant plus souvent qu'ils sont extravertis. La simulation n'en sait rien.
 */

/** Postures où l'on peut se tourner vers quelqu'un. */
const ARRETES = new Set(["debout", "assis", "accroupi", "travail"]);
/** Portée d'un feu autour duquel on s'assoit (m) et d'une conversation (m). */
const PRES_DU_FEU = 5.5;
const CAUSER = 3.2;

export function etreAvecLesAutres(liste: Iterable<Corps>, feux: readonly { x: number; z: number }[], dt: number, t: number): void {
  const candidats: Corps[] = [];
  const grille = new Map<string, Corps[]>();
  for (const c of liste) {
    const libre = ARRETES.has(c.posture) && !c.enMarche && c.ecoute < 0.3 && c.s.age >= 2 && !Number.isNaN(c.x);
    if (!libre) {
      c.interlocuteur = c.posture === "enlace" ? c.partenaire : 0;
      c.parle += (0 - c.parle) * Math.min(1, dt * 3);
      continue;
    }
    candidats.push(c);
    const k = `${Math.floor(c.x / 4)},${Math.floor(c.z / 4)}`;
    let l = grille.get(k);
    if (!l) grille.set(k, (l = []));
    l.push(c);
  }
  // Paires : les plus proches d'abord, chacun dans une seule conversation.
  const paires: { a: Corps; b: Corps; d: number }[] = [];
  for (const a of candidats) {
    const gx = Math.floor(a.x / 4);
    const gz = Math.floor(a.z / 4);
    for (let i = -1; i <= 1; i++) {
      for (let j = -1; j <= 1; j++) {
        for (const b of grille.get(`${gx + i},${gz + j}`) ?? []) {
          if (b.s.id <= a.s.id) continue;
          const d = Math.hypot(b.x - a.x, b.z - a.z);
          if (d < CAUSER) paires.push({ a, b, d });
        }
      }
    }
  }
  paires.sort((p, q) => p.d - q.d);
  const pris = new Set<number>();
  const parleurs = new Map<number, Corps>();
  for (const { a, b } of paires) {
    if (pris.has(a.s.id) || pris.has(b.s.id)) continue;
    // Une conversation dure quelques dizaines de secondes, puis on se tait un moment ; les extravertis causent plus.
    const graine = hacher(Math.min(a.s.id, b.s.id), Math.max(a.s.id, b.s.id), 151);
    const episode = Math.floor((t + 40 * unite(graine)) / 40);
    const envie = 0.25 + 0.6 * (a.s.extraversion + b.s.extraversion) / 2;
    if (unite(hacher(graine, episode, 153)) > envie) continue;
    pris.add(a.s.id);
    pris.add(b.s.id);
    parleurs.set(a.s.id, b);
    parleurs.set(b.s.id, a);
    // Chacun son tour : des tours de 2 à 7 s (Stivers et al., 2009 : les tours s'enchaînent en quelques secondes).
    const tour = Math.floor((t + 9 * unite(hacher(graine, 155))) / (2 + 5 * unite(hacher(graine, episode, 157))));
    const parle = (tour + episode) % 2 === 0 ? a : b;
    for (const c of [a, b]) c.parle += ((c === parle ? 1 : 0) - c.parle) * Math.min(1, dt * 2.5);
  }
  for (const c of candidats) {
    const autre = parleurs.get(c.s.id);
    c.interlocuteur = autre ? autre.s.id : 0;
    if (!autre) c.parle += (0 - c.parle) * Math.min(1, dt * 3);
    // Vers le feu s'il est tout près (le cercle autour du feu), sinon vers celui à qui l'on parle.
    let voulu: number | null = null;
    let mieux = PRES_DU_FEU;
    for (const f of feux) {
      const d = Math.hypot(f.x - c.x, f.z - c.z);
      if (d < mieux && d > 0.4) {
        mieux = d;
        voulu = Math.atan2(f.x - c.x, f.z - c.z);
      }
    }
    if (voulu === null && autre) voulu = Math.atan2(autre.x - c.x, autre.z - c.z);
    if (voulu === null) continue;
    const ecart = ((((voulu - c.cap + Math.PI) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) - Math.PI;
    // On se tourne sans hâte, plus lentement assis (on se décale sur les fesses).
    c.cap += ecart * Math.min(1, dt * (c.posture === "debout" ? 1.2 : 0.6));
  }
}
