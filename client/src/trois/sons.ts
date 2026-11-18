import type { HumainScene } from "./foule";
import { hacher, unite } from "./bruit";

/**
 * Les voix du face à face (0.45.14) : celle de l'Entité et celle des humains, dites par la synthèse vocale du système, à la file
 * (l'une attend la fin de l'autre), et la nappe sonore qui enveloppe la voix de l'Entité.
 * <para>
 * La voix du système sort directement du navigateur : la page ne peut pas la faire passer dans un effet (pas d'écho sur les mots).
 * La nappe, elle, est fabriquée ici (Web Audio) : un accord grave qui enfle et retombe, quelques harmoniques qui scintillent, une
 * réverbération de grand espace calculée (bruit qui s'éteint en quelques secondes), sans aucun fichier son.
 * </para>
 */

/** Prénoms de voix du système, pour choisir une voix d'homme ou de femme quand le système en propose plusieurs. */
const FEMMES = /amélie|amelie|audrey|aurélie|aurelie|marie|julie|céline|celine|hortense|virginie|léa|lea|chantal|sara|samantha|karen|moira|tessa|fiona|victoria|allison|ava|susan|zira|hazel|female|femme/i;
const HOMMES = /thomas|nicolas|jacques|daniel|paul|henri|claude|alex|fred|tom|aaron|arthur|oliver|rishi|david|mark|george|male|homme/i;

export type Voie = "Voix" | "Apparition" | "Reve";

export interface Parole {
  texte: string;
  /** Code de langue de la synthèse (fr-FR, en-US…). */
  lang: string;
  voix: SpeechSynthesisVoice | null;
  rate: number;
  pitch: number;
  debut?: () => void;
  fin?: () => void;
}

function voixDe(lang: string): SpeechSynthesisVoice[] {
  if (!("speechSynthesis" in window)) return [];
  const code = lang.slice(0, 2).toLowerCase();
  return speechSynthesis.getVoices().filter((v) => v.lang.toLowerCase().startsWith(code));
}

export type GenreVoix = "homme" | "femme";

/**
 * La voix de l'Entité, lente et basse : une voix d'homme ou de femme du système, au choix (0.45.16). Sans voix du genre voulu,
 * la hauteur fait la différence (une voix de femme reste plus haute qu'une voix d'homme baissée, mais plus grave que l'ordinaire).
 */
export function voixEntite(texte: string, lang: string, genre: GenreVoix = "homme"): Parole {
  const liste = voixDe(lang);
  const motif = genre === "femme" ? FEMMES : HOMMES;
  const trouvee = liste.find((v) => motif.test(v.name));
  const pitch = genre === "femme" ? (trouvee ? 0.8 : 1.15) : trouvee ? 0.6 : 0.45;
  return { texte, lang, voix: trouvee ?? liste[0] ?? null, rate: 0.82, pitch };
}

/**
 * La voix d'un humain, tirée de son corps et de son caractère : une voix d'homme ou de femme du système (toujours la même pour
 * lui), plus aiguë chez l'enfant, plus grave chez le grand, un peu plus basse et lente chez le vieux ; plus rapide chez
 * l'extraverti ou l'effrayé, plus lente chez l'émerveillé. Hauteurs : la voix d'un enfant de 8 ans est environ une octave
 * au-dessus de celle d'un homme (Lee et al., 1999) ; elle baisse un peu chez l'homme âgé, plus nettement chez la femme après la
 * ménopause (Torre et Barlow, 2009). Débit : 0,8 à 1,25 fois le débit ordinaire, selon l'émotion.
 */
export function voixHumain(s: HumainScene, texte: string, lang: string, emotion: string): Parole {
  const liste = voixDe(lang);
  const genre = liste.filter((v) => (s.femme ? FEMMES : HOMMES).test(v.name));
  const choix = genre.length ? genre : liste;
  const voix = choix.length ? choix[Math.floor(unite(hacher(s.id, 281)) * choix.length) % choix.length] : null;
  // Sans voix de son genre, la hauteur fait la différence.
  let pitch = 1 + (genre.length ? 0 : s.femme ? 0.25 : -0.2);
  if (s.age < 13) pitch += 0.6 * (1 - Math.max(0, s.age - 3) / 10) + (s.age >= 11 && !s.femme ? -0.15 : 0);
  else if (s.age > 55) pitch -= (s.femme ? 0.15 : 0.08) * Math.min(1, (s.age - 55) / 25);
  pitch -= 0.06 * (s.tailleEcart ?? 0);
  pitch += (unite(hacher(s.id, 283)) - 0.5) * 0.16;
  let rate = 1 + 0.18 * ((s.extraversion ?? 0.5) - 0.5) + 0.2 * Math.min(1, (s.peur ?? 0) / 100);
  if (s.age > 60) rate -= 0.08;
  if (emotion === "effroi") rate += 0.12;
  else if (emotion === "merveille") rate -= 0.12;
  return { texte, lang, voix, rate: Math.max(0.75, Math.min(1.3, rate)), pitch: Math.max(0.3, Math.min(1.9, pitch)) };
}

/** Les voix, à la file : rien ne coupe celle qui parle, sauf `taire`. */
export class Voix {
  private file = 0;

  dire(p: Parole): void {
    if (!("speechSynthesis" in window) || !p.texte.trim()) return;
    const u = new SpeechSynthesisUtterance(p.texte);
    u.lang = p.lang;
    u.voice = p.voix;
    u.rate = p.rate;
    u.pitch = p.pitch;
    this.file++;
    let fini = false;
    const finir = () => {
      if (fini) return;
      fini = true;
      this.file = Math.max(0, this.file - 1);
      p.fin?.();
    };
    u.onstart = () => p.debut?.();
    u.onend = finir;
    u.onerror = finir;
    speechSynthesis.speak(u);
  }

  /** Quelqu'un parle-t-il, ou attend-il son tour ? */
  get occupee(): boolean {
    return this.file > 0;
  }

  taire(): void {
    this.file = 0;
    if ("speechSynthesis" in window) speechSynthesis.cancel();
  }
}

/** Niveau et couleur de la nappe selon la voie : forte en apparition, à peine audible par la voix, feutrée en rêve. */
const NAPPE: Record<Voie, { gain: number; filtre: number }> = {
  Apparition: { gain: 0.16, filtre: 2600 },
  Voix: { gain: 0.05, filtre: 1800 },
  Reve: { gain: 0.09, filtre: 700 },
};

/** La nappe sonore sous la voix de l'Entité (B1). Créée au premier geste de l'utilisateur (un navigateur l'exige). */
export class Nappe {
  private ctx: AudioContext | null = null;
  private sortie: GainNode | null = null;
  private filtre: BiquadFilterNode | null = null;
  private sources: AudioScheduledSourceNode[] = [];
  private arret = 0;

  /** À appeler pendant un clic ou une touche : prépare le son pour qu'il puisse jouer ensuite. */
  eveiller(): void {
    if (this.ctx) {
      void this.ctx.resume();
      return;
    }
    const AC = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!AC) return;
    const ctx = (this.ctx = new AC());
    const sortie = (this.sortie = ctx.createGain());
    sortie.gain.value = 0;
    const filtre = (this.filtre = ctx.createBiquadFilter());
    filtre.type = "lowpass";
    filtre.frequency.value = 2000;
    filtre.Q.value = 0.4;
    // Réverbération : une réponse de grand espace faite de bruit qui s'éteint en 4,5 s, un peu différente à gauche et à droite.
    const reverb = ctx.createConvolver();
    const duree = 4.5;
    const n = Math.floor(ctx.sampleRate * duree);
    const ir = ctx.createBuffer(2, n, ctx.sampleRate);
    for (let c = 0; c < 2; c++) {
      const d = ir.getChannelData(c);
      for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / n, 2.6);
    }
    reverb.buffer = ir;
    const sec = ctx.createGain();
    sec.gain.value = 0.35;
    const mouille = ctx.createGain();
    mouille.gain.value = 0.9;
    filtre.connect(sec).connect(sortie);
    filtre.connect(reverb).connect(mouille).connect(sortie);
    sortie.connect(ctx.destination);
    // L'accord : une fondamentale très grave, sa quinte et son octave, légèrement désaccordées (battements lents) ; des harmoniques
    // hautes dont le volume ondule lentement (le scintillement).
    const base = 55;
    const voix: [number, number, OscillatorType][] = [
      [base, 0.5, "sine"], [base * 1.003, 0.35, "sine"], [base * 1.5, 0.28, "triangle"], [base * 2, 0.22, "sine"],
      [base * 2.997, 0.1, "sine"], [base * 8, 0.035, "sine"], [base * 12, 0.025, "sine"], [base * 16.02, 0.02, "sine"],
    ];
    voix.forEach(([f, g, type], k) => {
      const o = ctx.createOscillator();
      o.type = type;
      o.frequency.value = f;
      const gg = ctx.createGain();
      gg.gain.value = g;
      if (k >= 5) {
        // Scintillement : le volume de chaque harmonique haute monte et descend à son rythme.
        const lfo = ctx.createOscillator();
        lfo.frequency.value = 0.13 + 0.11 * k;
        const prof = ctx.createGain();
        prof.gain.value = g * 0.9;
        lfo.connect(prof).connect(gg.gain);
        lfo.start();
        this.sources.push(lfo);
      }
      o.connect(gg).connect(filtre);
      o.start();
      this.sources.push(o);
    });
    // Souffle : l'accord enfle et retombe lentement (environ toutes les 7 s).
    const souffle = ctx.createOscillator();
    souffle.frequency.value = 0.14;
    const amp = ctx.createGain();
    amp.gain.value = 350;
    souffle.connect(amp).connect(filtre.frequency);
    souffle.start();
    this.sources.push(souffle);
  }

  /** La voix de l'Entité commence : la nappe monte en moins d'une seconde. */
  monter(voie: Voie): void {
    const ctx = this.ctx;
    if (!ctx || !this.sortie || !this.filtre) return;
    void ctx.resume();
    window.clearTimeout(this.arret);
    const r = NAPPE[voie] ?? NAPPE.Voix;
    const t = ctx.currentTime;
    this.filtre.frequency.setTargetAtTime(r.filtre, t, 0.3);
    this.sortie.gain.cancelScheduledValues(t);
    this.sortie.gain.setValueAtTime(this.sortie.gain.value, t);
    this.sortie.gain.linearRampToValueAtTime(r.gain, t + 0.8);
  }

  /** Elle s'est tue : la nappe s'éteint en deux ou trois secondes (la réverbération finit de sonner). */
  descendre(): void {
    const ctx = this.ctx;
    if (!ctx || !this.sortie) return;
    const t = ctx.currentTime;
    this.sortie.gain.cancelScheduledValues(t);
    this.sortie.gain.setValueAtTime(this.sortie.gain.value, t);
    this.sortie.gain.linearRampToValueAtTime(0, t + 2.5);
    // Plus rien à jouer : on suspend le son pour ne pas tenir le processeur audio éveillé.
    this.arret = window.setTimeout(() => void ctx.suspend(), 6000);
  }

  couper(): void {
    if (!this.ctx || !this.sortie) return;
    this.sortie.gain.cancelScheduledValues(this.ctx.currentTime);
    this.sortie.gain.value = 0;
    void this.ctx.suspend();
  }
}
