import * as THREE from "three/webgpu";
import {
  Fn, abs, attribute, cameraPosition, cos, exp, float, fract, length, normalize, positionGeometry, sin, smoothstep, time, uniform, uv, vec3, vec4,
} from "three/tsl";
import { connexion } from "../connexion";
import { echange, ouvrirDialogue } from "../dialogue";
import { langue, t } from "../langue";
import { hacher, unite } from "./bruit";
import type { GesteCombat, Role } from "./combat";
import type { Corps, Foule } from "./foule";
import { BOUTON } from "./manette";
import { Nappe, Voix, voixEntite, voixHumain, type GenreVoix, type Voie } from "./sons";

/**
 * Face à face avec l'Entité (0.45.11, propositions A1 à F2 validées par zem le 17/11).
 *
 * Près d'un humain, Entrée (ou appui long sur A à la manette) propose de lui parler (voix), de lui apparaître, ou d'entrer dans
 * son rêve s'il dort : c'est le vrai dialogue (le LLM joue l'humain, les règles décident), mené depuis une bande en bas de
 * l'écran, ses réponses en sous-titres. En apparition, l'Entité est une présence de lumière sans visage, qui éclaire ce qui
 * l'entoure. L'humain réagit selon ce que les règles ont décidé à chaque réplique (il ne comprend pas, n'y croit pas, y croit
 * avec effroi ou émerveillement, fuit, se détourne, hésite, refuse) et selon son caractère ; les témoins d'une apparition aussi.
 * Tout cela n'est que de l'affichage : la simulation reste la même.
 */

/** Ce que le serveur dit d'une réplique (ReactionDto). */
interface Reaction {
  comprise: boolean;
  croit: boolean;
  emotion: "incomprise" | "doute" | "effroi" | "merveille";
  fin: string;
  issues: string[];
  peur: number;
  nevrosisme: number;
  transcendance: number;
  extraversion: number;
  agreabilite: number;
  temoins: { humain: number; croit: boolean }[];
  /** Le geste qu'il dit faire en répondant (Genoux, Prosterne, Debout, Tendre, Reculer, Couvrir, Hausser, Refuser), ou "". */
  geste?: string;
}

interface Replique {
  rang: number;
  entite: string;
  mots: string;
  sens: string;
  pensee: string;
  erreur: string | null;
  reaction?: Reaction | null;
}

interface Dialogue {
  id: number;
  humain: number;
  nom: string;
  mode: string;
  enCours: boolean;
  attend: string | null;
  finTexte: string | null;
  liste: Replique[];
  erreur: string | null;
}

/** Ce que la vue 3D prête à la rencontre. */
export interface HoteRencontre {
  foule: Foule;
  camera: THREE.Camera;
  nom: (id: number) => string;
  message: (texte: string) => void;
  /** Place la caméra face à lui, à hauteur de visage : `cap` est la direction où il regardera, `angle` le côté de la caméra. */
  cadrer: (id: number, cap: number, angle: number, distance: number) => void;
  /** Rend au suivi son cap ordinaire (fin du face à face). */
  libererCadre: () => void;
}

/** Distance (m) en deçà de laquelle on peut s'adresser à un humain. */
export const PORTEE = 15;
/** Distance (m) entre l'humain et la lumière de l'Entité. */
const ECART_PRESENCE = 1.4;
/** De combien (radians) la présence se tient sur le côté de la ligne qui va de lui à la caméra. */
const COTE_PRESENCE = -0.9;

/** Où apparaît la présence : à un pas et demi de lui, de biais par rapport à la caméra (elle ne doit pas le cacher). */
function placePresence(lui: THREE.Vector3, cap: number): [number, number] {
  const a = cap + COTE_PRESENCE;
  return [lui.x + Math.sin(a) * ECART_PRESENCE, lui.z + Math.cos(a) * ECART_PRESENCE];
}
/** Témoins trop loin pour qu'on les voie réagir (m). */
const TEMOINS_VUS = 150;
/** Appui long sur A (s). */
const APPUI_LONG = 0.7;

/** Une étape d'une réaction : un geste (ou null : marcher, courir), sa durée (s ; Infinity : tient jusqu'à la suite) et où il va. */
interface Etape {
  geste: GesteCombat | null;
  duree: number;
  x: number;
  z: number;
  court: boolean;
  /** Il tourne le dos à l'Entité (il s'en va) ; sinon il lui fait face, sauf en marchant. */
  dos?: boolean;
}

interface Jeu {
  etapes: Etape[];
  debut: number;
  emotion: Record<string, number>;
}

/** Visages du moment : expressions de la tête (joie, triste, colere, peur, bouche). */
const VISAGES: Record<string, Record<string, number>> = {
  incomprise: { joie: 0, triste: 0.15, colere: 0.2, peur: 0.15, bouche: 0.1 },
  doute: { joie: 0, triste: 0, colere: 0.3, peur: 0, bouche: 0 },
  effroi: { joie: 0, triste: 0.1, colere: 0, peur: 1, bouche: 0.55 },
  merveille: { joie: 0.5, triste: 0, colere: 0, peur: 0.3, bouche: 0.35 },
  surprise: { joie: 0, triste: 0, colere: 0, peur: 0.45, bouche: 0.4 },
};

function lire(cle: string): boolean {
  try {
    return localStorage.getItem(cle) === "1";
  } catch {
    return false;
  }
}

const CLE_GENRE = "monde.rencontre.voixEntite";

/** La voix choisie pour l'Entité ; d'homme tant que rien n'a été choisi (la voix des versions d'avant). */
function lireGenre(): GenreVoix {
  try {
    return localStorage.getItem(CLE_GENRE) === "femme" ? "femme" : "homme";
  } catch {
    return "homme";
  }
}

function ecrire(cle: string, oui: boolean): void {
  try {
    localStorage.setItem(cle, oui ? "1" : "0");
  } catch {
    // Stockage refusé (navigation privée) : le choix vaut pour cette session.
  }
}

function el<K extends keyof HTMLElementTagNameMap>(tag: K, classe?: string, texte?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texte !== undefined) e.textContent = texte;
  return e;
}

function message(e: unknown): string {
  return e instanceof Error ? e.message.replace(/^.*HubException: /, "") : String(e);
}

/**
 * La présence de l'Entité apparue (A1) : une colonne de lumière à taille humaine qui respire, un halo dans l'air, une poussière
 * de lumière qui monte, et une vraie lumière qui éclaire l'humain, le sol et les arbres. Sans visage ni corps.
 */
class Presence {
  readonly groupe = new THREE.Group();
  readonly centre = new THREE.Vector3();
  private readonly uCentre = uniform(new THREE.Vector3());
  private readonly uForce = uniform(0);
  private readonly uEclat = uniform(1);
  private readonly lumiere = new THREE.PointLight(0xfff1dc, 0, 22, 1.6);
  /** De 0 (éteinte) à 1 (pleine), vers `voulue`. */
  force = 0;
  voulue = 0;

  /**
   * La lumière reste toujours dans la scène (éteinte hors apparition) : un nombre de lumières qui change ferait recompiler les
   * matériaux de toute la scène, une saccade de plusieurs secondes sous Windows.
   */
  constructor(scene: THREE.Scene) {
    scene.add(this.lumiere);
    // Ombres (C1, 0.45.14) : une carte de 512 px par face, jusqu'à 22 m ; recalculée seulement pendant une apparition.
    this.lumiere.shadow.mapSize.set(512, 512);
    this.lumiere.shadow.camera.near = 0.2;
    this.lumiere.shadow.camera.far = 22;
    this.lumiere.shadow.bias = -0.002;
    this.lumiere.shadow.normalBias = 0.03;
    this.lumiere.shadow.autoUpdate = false;
    const quad = () => new THREE.PlaneGeometry(1, 1).translate(0, 0.5, 0);
    const additif = () => new THREE.MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false });
    const c = this.uCentre;
    const p = positionGeometry;
    const vers = cameraPosition.sub(c);
    const droite = normalize(vec3(vers.z.negate(), 0, vers.x).add(vec3(0.0001, 0, 0)));
    const souffle = sin(time.mul(1.3)).mul(0.08).add(sin(time.mul(0.47)).mul(0.05)).add(0.92);

    // Colonne : tournée vers la caméra autour de la verticale ; un cœur blanc, une lueur plus large, fondue en haut et en bas.
    const colonne = additif();
    const largeur = float(1.3);
    const hauteur = float(2.6);
    colonne.positionNode = Fn(() => c.add(droite.mul(p.x.mul(largeur))).add(vec3(0, p.y.mul(hauteur).sub(0.25), 0)))();
    const q = uv();
    const x = abs(q.x.sub(0.5)).mul(largeur);
    const ondule = sin(q.y.mul(9).sub(time.mul(1.7))).mul(0.015);
    const coeur = exp(x.add(ondule).div(0.07).pow(2).negate());
    const lueur = exp(x.div(0.28).pow(2).negate()).mul(0.45);
    const fondu = smoothstep(0, 0.22, q.y).mul(smoothstep(1, 0.55, q.y));
    colonne.colorNode = vec4(vec3(1, 0.95, 0.86).mul(this.uEclat).mul(souffle).mul(1.6), coeur.add(lueur).mul(fondu).mul(this.uForce));
    const mColonne = new THREE.Mesh(quad(), colonne);
    mColonne.frustumCulled = false;
    mColonne.renderOrder = 5;

    // Halo : un disque tourné vers la caméra, très doux, à hauteur de poitrine.
    const halo = additif();
    const centreHalo = c.add(vec3(0, 1.2, 0));
    const versH = normalize(cameraPosition.sub(centreHalo));
    const droiteH = normalize(vec3(versH.z.negate(), 0, versH.x).add(vec3(0.0001, 0, 0)));
    const hautH = normalize(versH.cross(droiteH).negate());
    const rayon = float(5);
    halo.positionNode = Fn(() => centreHalo.add(droiteH.mul(p.x.mul(rayon))).add(hautH.mul(p.y.sub(0.5).mul(rayon))))();
    const r = length(uv().sub(0.5)).mul(2);
    halo.colorNode = vec4(vec3(1, 0.93, 0.8).mul(this.uEclat).mul(0.6), exp(r.mul(3.2).pow(2).negate()).mul(0.5).mul(this.uForce).mul(souffle));
    const mHalo = new THREE.Mesh(quad(), halo);
    mHalo.frustumCulled = false;
    mHalo.renderOrder = 5;

    // Poussière de lumière : 80 grains qui montent en tournant autour de la colonne et s'éteignent en haut.
    const n = 80;
    const geo = new THREE.InstancedBufferGeometry();
    geo.copy(quad() as unknown as THREE.InstancedBufferGeometry);
    const a = new Float32Array(n * 4);
    for (let k = 0; k < n; k++) a.set([unite(hacher(k, 1)), 0.15 + 0.55 * unite(hacher(k, 2)), 3 + 4 * unite(hacher(k, 3)), 0.012 + 0.02 * unite(hacher(k, 4))], k * 4);
    geo.setAttribute("ig", new THREE.InstancedBufferAttribute(a, 4));
    geo.instanceCount = n;
    const ig = attribute("ig", "vec4");
    const grains = additif();
    const cycle = fract(time.div(ig.z).add(ig.x));
    const angle = ig.x.mul(6.283).add(time.mul(0.5)).add(cycle.mul(2));
    const centreG = c.add(vec3(cos(angle).mul(ig.y), cycle.mul(2.8).sub(0.1), sin(angle).mul(ig.y)));
    const versG = normalize(cameraPosition.sub(centreG));
    const droiteG = normalize(vec3(versG.z.negate(), 0, versG.x).add(vec3(0.0001, 0, 0)));
    const hautG = normalize(versG.cross(droiteG).negate());
    grains.positionNode = Fn(() => centreG.add(droiteG.mul(p.x.mul(ig.w))).add(hautG.mul(p.y.sub(0.5).mul(ig.w))))();
    const rg = length(uv().sub(0.5)).mul(2);
    grains.colorNode = vec4(vec3(1, 0.96, 0.85).mul(this.uEclat).mul(2.2), smoothstep(1, 0.2, rg).mul(sin(cycle.mul(Math.PI))).mul(this.uForce));
    const mGrains = new THREE.Mesh(geo, grains);
    mGrains.frustumCulled = false;
    mGrains.renderOrder = 5;

    this.groupe.add(mColonne, mHalo, mGrains);
    this.groupe.visible = false;
  }

  /**
   * Ombres de la présence (C1) en qualité Haute et Ultra. Changer d'avis recompile les matériaux de la scène : on ne le fait qu'au
   * changement de qualité, qui les recompile de toute façon.
   */
  ombres(actif: boolean): void {
    if (this.lumiere.castShadow === actif) return;
    this.lumiere.castShadow = actif;
  }

  /** `exposition` : l'œil adapté à la lumière du moment ; la présence paraît aveuglante la nuit, pâle en plein jour. */
  animer(dt: number, exposition: number, secondes: number): void {
    const vitesse = this.voulue > this.force ? 1 / 1.2 : 1 / 1.6;
    this.force += Math.max(-dt * vitesse, Math.min(dt * vitesse, this.voulue - this.force));
    this.groupe.visible = this.force > 0.001;
    if (!this.groupe.visible) {
      this.lumiere.intensity = 0;
      return;
    }
    this.uCentre.value.copy(this.centre);
    this.uForce.value = this.force * this.force * (3 - 2 * this.force);
    this.uEclat.value = 1.2 / Math.pow(Math.max(exposition, 0.01), 0.85);
    this.lumiere.position.set(this.centre.x, this.centre.y + 1.3, this.centre.z);
    // De jour, l'œil adapté au soleil la verrait à peine : elle éclaire plus fort (jusqu'à quatre fois), pour rester une présence
    // qui illumine et fait des ombres même en plein jour ; la nuit, l'œil adapté à l'obscurité la voit déjà éblouissante.
    const jour = Math.max(1, Math.min(4, 2 / Math.max(exposition, 0.01)));
    this.lumiere.intensity = 4 * jour * this.uForce.value * (0.92 + 0.08 * Math.sin(secondes * 1.3));
    // Les humains bougent : l'ombre est redessinée à chaque image tant que la présence est là.
    if (this.lumiere.castShadow) this.lumiere.shadow.needsUpdate = true;
  }
}

/**
 * La rencontre : invite près d'un humain, choix de la voie, bande de dialogue, présence, réactions de l'humain et des témoins,
 * voix de l'Entité.
 */
export class Rencontre {
  private readonly presence: Presence;
  private readonly invite = el("div", "invite3d");
  private readonly choix = el("div", "choix3d");
  private readonly bande = el("div", "rencontre3d");
  private readonly sousTitres = el("div", "sous-titres");
  private readonly saisie = el("input");
  private readonly dire: HTMLButtonElement;
  private readonly finir: HTMLButtonElement;
  private readonly etat = el("p", "etat");
  private readonly casePensees = el("input");
  private readonly caseVoix = el("input");
  /** 0.45.14 (A1) : entendre les humains dire leurs mots, chacun avec sa voix. */
  private readonly caseVoixHumains = el("input");
  /** 0.45.16 : voix d'homme ou de femme pour l'Entité. */
  private readonly genreEntite = el("select");
  private readonly voix = new Voix();
  private readonly nappe = new Nappe();
  /** Humain à qui l'on peut s'adresser (0 : personne à portée). */
  private cible = 0;
  private choixBoutons: HTMLButtonElement[] = [];
  private choixIndex = 0;
  /** Le dialogue mené depuis la bande (null : aucun). */
  private dialogue: Dialogue | null = null;
  /** Un dialogue mené depuis la fenêtre habituelle, que la 3D montre aussi. */
  private fenetre: Dialogue | null = null;
  /** Répliques déjà jouées (réactions lancées, voix dite) pour ce dialogue. */
  private jouees = 0;
  private dejaCru = false;
  private readonly jeux = new Map<number, Jeu>();
  private llm: boolean | null = null;
  private tenuA = 0;
  private longA = false;

  constructor(hote: HTMLElement, private readonly h: HoteRencontre, scene: THREE.Scene) {
    this.presence = new Presence(scene);
    scene.add(this.presence.groupe);
    this.invite.hidden = true;
    this.choix.hidden = true;
    this.bande.hidden = true;
    this.saisie.type = "text";
    this.saisie.maxLength = 1200;
    this.saisie.id = "rencontre-saisie";
    this.saisie.autocomplete = "off";
    this.saisie.addEventListener("keydown", (e) => {
      if (e.key === "Escape") this.saisie.blur();
      e.stopPropagation();
    });
    const form = el("form", "saisie");
    this.dire = el("button", "entite", t("rencontre.dire"));
    this.dire.type = "submit";
    this.finir = el("button", undefined, t("rencontre.finir"));
    this.finir.type = "button";
    this.finir.title = t("dlgc.finirAide");
    this.finir.addEventListener("click", () => void this.terminer());
    const fenetre = el("button", "lien-discret", t("rencontre.fenetre"));
    fenetre.type = "button";
    fenetre.title = t("rencontre.fenetreAide");
    fenetre.addEventListener("click", () => {
      if (this.dialogue) ouvrirDialogue(this.dialogue.humain, this.dialogue.nom);
    });
    form.append(this.saisie, this.dire, this.finir, fenetre);
    form.addEventListener("submit", (e) => {
      e.preventDefault();
      void this.envoyer();
    });
    const options = el("div", "options");
    const caseLabel = (c: HTMLInputElement, cle: string, texte: string, aide: string) => {
      c.type = "checkbox";
      c.checked = lire(cle);
      c.addEventListener("change", () => {
        ecrire(cle, c.checked);
        if (c === this.casePensees) this.montrer();
        if ((c === this.caseVoix || c === this.caseVoixHumains) && !c.checked) {
          this.voix.taire();
          this.nappe.couper();
        }
        if ((c === this.caseVoix || c === this.caseVoixHumains) && c.checked) this.nappe.eveiller();
      });
      const l = el("label");
      l.title = aide;
      l.append(c, " " + texte);
      return l;
    };
    options.append(caseLabel(this.casePensees, "monde.rencontre.pensees", t("rencontre.pensees"), t("rencontre.penseesAide")));
    if ("speechSynthesis" in window) {
      options.append(caseLabel(this.caseVoix, "monde.rencontre.voix", t("rencontre.voix"), t("rencontre.voixAide")));
      for (const g of ["homme", "femme"] as const) this.genreEntite.append(new Option(t(`rencontre.voixEntite.${g}`), g));
      this.genreEntite.value = lireGenre();
      this.genreEntite.title = t("rencontre.voixEntiteAide");
      this.genreEntite.setAttribute("aria-label", t("rencontre.voixEntiteAide"));
      this.genreEntite.addEventListener("keydown", (e) => e.stopPropagation());
      this.genreEntite.addEventListener("change", () => {
        try {
          localStorage.setItem(CLE_GENRE, this.genreEntite.value);
        } catch {
          // Stockage refusé : le choix vaut pour cette session.
        }
      });
      options.append(this.genreEntite);
      options.append(caseLabel(this.caseVoixHumains, "monde.rencontre.voixHumains", t("rencontre.voixHumains"), t("rencontre.voixHumainsAide")));
    }
    this.bande.append(this.sousTitres, form, options, this.etat);
    hote.append(this.invite, this.choix, this.bande);
    connexion.on("Dialogue", this.surDialogue);
    this.h.foule.rencontre = (c) => this.role(c);
  }

  /** Qualité Haute ou Ultra : la présence fait des ombres (0.45.14). */
  ombres(actif: boolean): void {
    this.presence.ombres(actif);
  }

  /** Le dialogue est-il mené depuis la bande ? */
  get actif(): boolean {
    return this.dialogue !== null;
  }

  get menuOuvert(): boolean {
    return !this.choix.hidden;
  }

  liberer(): void {
    connexion.off("Dialogue", this.surDialogue);
    this.h.foule.rencontre = null;
    this.h.foule.entite = null;
    this.invite.remove();
    this.choix.remove();
    this.bande.remove();
    this.voix.taire();
    this.nappe.couper();
  }

  /** La vue se ferme : on quitte la bande (le dialogue continue dans la fenêtre si on l'ouvre). */
  fermer(): void {
    this.fermerChoix();
    this.bande.hidden = true;
    this.invite.hidden = true;
    this.dialogue = null;
    this.voix.taire();
    this.nappe.couper();
    this.presence.voulue = 0;
    this.h.foule.entite = null;
    for (const [id] of this.jeux) this.h.foule.emotions.delete(id);
    this.jeux.clear();
  }

  // ——— Invite et choix (B1) ———

  /** À chaque image : qui est à portée (`id` : celui que la vue propose), l'invite, la présence et les visages. */
  mettreAJour(dt: number, id: number | null, exposition: number, secondes: number): void {
    this.cible = id ?? 0;
    const montrer = this.cible !== 0 && !this.dialogue && this.choix.hidden;
    if (montrer) {
      const texte = t("rencontre.invite", { nom: this.h.nom(this.cible) });
      if (this.invite.textContent !== texte) this.invite.textContent = texte;
    }
    this.invite.hidden = !montrer;
    const d = this.dialogue ?? this.fenetre;
    if (d && d.mode === "Apparition" && d.enCours) {
      // La présence reste à côté de lui s'il bouge (il recule, il fuit, elle ne le poursuit pas : elle s'efface à la fin).
      this.presence.voulue = 1;
    } else this.presence.voulue = 0;
    this.presence.animer(dt, exposition, secondes);
    this.h.foule.entite = this.presence.force > 0.05 ? this.presence.centre : null;
    const maintenant = performance.now() / 1000;
    for (const [hid, j] of this.jeux) {
      const total = j.etapes.reduce((s, e) => s + e.duree, 0);
      if (maintenant - j.debut > total) {
        this.jeux.delete(hid);
        this.h.foule.emotions.delete(hid);
      }
    }
  }

  /** Entrée (clavier) : ouvre le choix pour l'humain à portée. Faux s'il n'y a personne. */
  proposer(): boolean {
    if (this.dialogue) {
      this.saisie.focus();
      return true;
    }
    if (!this.cible) return false;
    void this.ouvrirChoix(this.cible);
    return true;
  }

  private async ouvrirChoix(humain: number): Promise<void> {
    const nom = this.h.nom(humain);
    this.llm ??= await connexion.invoke<{ connexion: { modele: string } }>("EtatEntiteLlm").then((e) => e.connexion.modele.length > 0).catch(() => false);
    const dort = await connexion.invoke<boolean>("Endormi", humain).catch(() => false);
    this.choix.replaceChildren(el("h3", undefined, t("rencontre.titre", { nom })));
    this.choixBoutons = [];
    const voies: [string, string][] = dort ? [["Reve", "rencontre.reve"], ["Voix", "rencontre.parler"], ["Apparition", "rencontre.apparaitre"]]
      : [["Voix", "rencontre.parler"], ["Apparition", "rencontre.apparaitre"]];
    for (const [voie, cle] of voies) {
      const b = el("button", "entite", t(cle, { nom }));
      b.type = "button";
      b.title = t(`dlgc.voieAide.${voie}`);
      b.disabled = !this.llm;
      b.addEventListener("click", () => void this.commencer(humain, voie));
      this.choix.append(b);
      this.choixBoutons.push(b);
    }
    if (dort) this.choix.append(el("p", "discret", t("dlgc.dort", { nom })));
    if (!this.llm) this.choix.append(el("p", "discret", t("dlgc.sansModele")));
    const annuler = el("button", "lien-discret", t("rencontre.annuler"));
    annuler.type = "button";
    annuler.addEventListener("click", () => this.fermerChoix());
    this.choix.append(annuler, el("p", "discret", t("rencontre.choixAide")));
    this.choixIndex = 0;
    this.choix.hidden = false;
    this.invite.hidden = true;
    this.marquerChoix();
  }

  private marquerChoix(): void {
    this.choixBoutons.forEach((b, k) => b.classList.toggle("actif", k === this.choixIndex));
  }

  fermerChoix(): void {
    this.choix.hidden = true;
    this.choixBoutons = [];
  }

  /** Touches quand le choix est ouvert : ↑ ↓ pour choisir, Entrée pour valider, Échap pour fermer. */
  clavier(e: KeyboardEvent): void {
    if (e.type !== "keydown") return;
    if (e.key === "Escape") this.fermerChoix();
    else if (e.key === "ArrowUp" || e.key === "ArrowDown") {
      const n = this.choixBoutons.length;
      if (n > 0) this.choixIndex = (this.choixIndex + (e.key === "ArrowUp" ? n - 1 : 1)) % n;
      this.marquerChoix();
    } else if (e.key === "Enter") this.choixBoutons[this.choixIndex]?.click();
    else return;
    e.preventDefault();
  }

  /** Manette, choix ouvert : croix haut/bas pour choisir, A pour valider, B pour fermer. Vrai si le bouton est pris. */
  boutonManette(k: number): boolean {
    if (this.choix.hidden) return false;
    if (k === BOUTON.B) this.fermerChoix();
    else if (k === BOUTON.HAUT || k === BOUTON.BAS) {
      const n = this.choixBoutons.length;
      if (n > 0) this.choixIndex = (this.choixIndex + (k === BOUTON.HAUT ? n - 1 : 1)) % n;
      this.marquerChoix();
    } else if (k === BOUTON.A) this.choixBoutons[this.choixIndex]?.click();
    return true;
  }

  /** Appui long sur A : la manette s'adresse à l'humain devant soi (A court garde « suivre »). */
  manette(dt: number, tenu: boolean): void {
    if (!tenu) {
      this.tenuA = 0;
      this.longA = false;
      return;
    }
    this.tenuA += dt;
    if (!this.longA && this.tenuA > APPUI_LONG && this.choix.hidden) {
      this.longA = true;
      if (!this.proposer()) this.h.message(t("trois.manette.personne"));
    }
  }

  // ——— Le dialogue (C1) ———

  private async commencer(humain: number, voie: string): Promise<void> {
    this.fermerChoix();
    try {
      const d = await connexion.invoke<Dialogue | null>("OuvrirDialogue", humain, voie, 0, false);
      if (!d) return;
      this.ouvrirBande(d);
    } catch (e) {
      this.h.message(message(e));
    }
  }

  private ouvrirBande(d: Dialogue): void {
    this.dialogue = d;
    this.jouees = d.liste.length;
    this.dejaCru = d.liste.some((r) => r.reaction?.croit);
    this.bande.classList.remove("options-seules");
    this.bande.hidden = false;
    this.saisie.placeholder = t("rencontre.saisie", { nom: d.nom });
    echange.humain = d.humain;
    echange.voie = d.mode;
    // Cadrer : il fera face à l'Entité ; en apparition, la lumière est entre nous et lui, un peu de côté, la caméra derrière elle.
    const lui = this.h.foule.position(d.humain);
    const cam = this.h.camera.position;
    if (lui) {
      const cap = Math.atan2(cam.x - lui.x, cam.z - lui.z);
      if (d.mode === "Apparition") {
        const [px, pz] = placePresence(lui, cap);
        // Posée sur le sol, ou sur l'eau s'il se tient dans l'eau.
        this.presence.centre.set(px, Math.max(this.h.foule.relief.hauteur(px, pz), 0), pz);
        // La caméra de l'autre côté : lui de trois quarts, la lumière sur le bord de l'image, sans qu'elle le cache.
        this.h.cadrer(d.humain, cap, 0.55, 3.2);
        // Il la voit naître : surprise, avant même que les règles décident s'il y croit.
        this.jouer(d.humain, [{ geste: "fixer", duree: Infinity, x: NaN, z: NaN, court: false }], VISAGES.surprise);
      } else this.h.cadrer(d.humain, cap, 0.12, d.mode === "Reve" ? 1.6 : 1.9);
    }
    this.montrer();
    this.saisie.focus();
  }

  private async envoyer(): Promise<void> {
    const texte = this.saisie.value.trim();
    const d = this.dialogue;
    if (!texte || !d) return;
    this.dire.disabled = true;
    try {
      this.parler(texte, d.mode as Voie);
      const r = await connexion.invoke<Dialogue>("DireAuDialogue", d.id, texte);
      this.saisie.value = "";
      this.recevoir(r);
    } catch (e) {
      this.etat.textContent = message(e);
      this.dire.disabled = false;
    }
  }

  private async terminer(): Promise<void> {
    const d = this.dialogue;
    if (!d) return;
    if (!d.enCours) {
      this.quitterBande();
      return;
    }
    try {
      this.recevoir(await connexion.invoke<Dialogue>("TerminerDialogue", d.id));
    } catch (e) {
      this.etat.textContent = message(e);
    }
  }

  private quitterBande(): void {
    this.bande.hidden = true;
    this.dialogue = null;
    echange.humain = 0;
    this.h.libererCadre();
    this.relacher();
  }

  private readonly surDialogue = (d: Dialogue | null): void => {
    if (!this.dialogue) {
      this.suivreFenetre(d);
      return;
    }
    if (d && d.id === this.dialogue.id) this.recevoir(d);
    else if (!d && this.dialogue.enCours) {
      void connexion.invoke<Dialogue>("LireDialogue", this.dialogue.id).then((x) => this.recevoir(x)).catch(() => undefined);
    }
  };

  /**
   * Un dialogue mené depuis la fenêtre (fiche, appel) : pas de bande, mais la présence et les réactions dans la 3D quand même.
   */
  private suivreFenetre(d: Dialogue | null): void {
    if (!d || !d.enCours) {
      if (this.fenetre && (!d || d.id === this.fenetre.id)) {
        if (d) for (const r of d.liste.slice(this.jouees)) this.entendre(d, r);
        this.fenetre = null;
        this.relacher();
        this.optionsSeules(false);
      }
      return;
    }
    // 0.45.14 : les voix aussi pour un dialogue mené depuis la fenêtre ; les deux cases s'affichent seules en bas de l'écran.
    this.optionsSeules(true);
    if (d.attend && d.attend !== this.ditFenetre) {
      this.ditFenetre = d.attend;
      this.parler(d.attend, d.mode as Voie);
    }
    if (!this.fenetre || this.fenetre.id !== d.id) {
      this.fenetre = d;
      this.jouees = d.liste.length;
      this.dejaCru = d.liste.some((r) => r.reaction?.croit);
      const lui = this.h.foule.position(d.humain);
      if (lui && d.mode === "Apparition") {
        const cam = this.h.camera.position;
        const cap = Math.atan2(cam.x - lui.x, cam.z - lui.z);
        const [px, pz] = placePresence(lui, cap);
        this.presence.centre.set(px, Math.max(this.h.foule.relief.hauteur(px, pz), 0), pz);
      }
      return;
    }
    this.fenetre = d;
    for (const r of d.liste.slice(this.jouees)) {
      if (r.reaction) this.reagir(d, r.reaction, r.rang === 1);
      this.entendre(d, r);
    }
    this.jouees = d.liste.length;
  }

  /** Dernière phrase de l'Entité déjà dite tout haut pour un dialogue de la fenêtre. */
  private ditFenetre = "";

  /** La bande réduite à ses cases (voix), pour un dialogue mené depuis la fenêtre. */
  private optionsSeules(oui: boolean): void {
    if (this.dialogue) return;
    this.bande.classList.toggle("options-seules", oui);
    this.bande.hidden = !oui;
  }

  private recevoir(d: Dialogue): void {
    if (!this.dialogue || d.id !== this.dialogue.id) return;
    this.dialogue = d;
    echange.humain = d.enCours ? d.humain : 0;
    // Les répliques nouvelles : la réaction qui va avec, et sa bouche qui bouge le temps de dire ses mots.
    for (const r of d.liste.slice(this.jouees)) {
      if (r.reaction) this.reagir(d, r.reaction, r.rang === 1);
      if (r.mots) echange.parleJusqua = performance.now() + Math.min(9000, 900 + r.mots.length * 70);
      this.entendre(d, r);
    }
    this.jouees = d.liste.length;
    if (!d.enCours) this.relacher();
    this.montrer();
  }

  /** Sous-titres : ta dernière phrase, sa réponse (en clair, puis dans sa langue), sa pensée si on veut la lire. */
  private montrer(): void {
    const d = this.dialogue;
    if (!d) return;
    const r = d.liste[d.liste.length - 1];
    const lignes: HTMLElement[] = [];
    const tienne = d.attend ?? r?.entite;
    if (tienne) lignes.push(el("p", "entite", `✦ ${tienne}`));
    if (d.attend) lignes.push(el("p", "attente", t("dlgc.ecoute", { nom: d.nom })));
    else if (r) {
      const reponse = r.sens || r.mots;
      const p = el("p", "humain");
      if (reponse) p.append(el("b", undefined, `${d.nom} : `), `« ${reponse} »`);
      else p.append(t("dlgc.silence", { nom: d.nom }));
      lignes.push(p);
      if (r.mots && r.sens) lignes.push(el("p", "langue", t("dlgc.dansSaLangue", { texte: r.mots })));
      if (r.pensee && this.casePensees.checked) lignes.push(el("p", "pensee", t("rencontre.pense", { texte: r.pensee })));
      if (r.erreur) lignes.push(el("p", "erreur", r.erreur));
    }
    if (!d.enCours) lignes.push(el("p", "fin", t("dlgc.fin", { fin: d.finTexte ?? "" })));
    this.sousTitres.replaceChildren(...lignes);
    this.etat.textContent = d.erreur ?? "";
    const attend = d.attend !== null;
    this.dire.disabled = attend || !d.enCours;
    this.saisie.disabled = attend || !d.enCours;
    this.finir.textContent = t(d.enCours ? "rencontre.finir" : "rencontre.quitter");
  }

  // ——— Voix de l'Entité (F2) ———

  private codeVoix(): string {
    const code = langue();
    return code === "en" ? "en-US" : code === "fr" ? "fr-FR" : code;
  }

  /** La voix de l'Entité (F2), enveloppée de sa nappe (B1, 0.45.14) ; elle attend que l'humain ait fini de parler. */
  private parler(texte: string, voie: Voie): void {
    if (!this.caseVoix.checked || !("speechSynthesis" in window)) return;
    // Le clic sur « Dire » ou la touche Entrée : le seul moment où le navigateur laisse naître le son.
    this.nappe.eveiller();
    const p = voixEntite(texte, this.codeVoix(), this.genreEntite.value === "femme" ? "femme" : "homme");
    p.debut = () => this.nappe.monter(voie);
    p.fin = () => this.nappe.descendre();
    this.voix.dire(p);
  }

  /**
   * L'humain dit tout haut ses mots, dans sa langue, avec sa voix (A1, 0.45.14) : rien en rêve (il répond en esprit), rien s'il
   * n'a rien dit du tout ; sans mots à lui, c'est la traduction qui est dite. Ses lèvres bougent le temps qu'il parle vraiment.
   */
  private entendre(d: Dialogue, r: Replique): void {
    // Sans mots à lui (pas de langue encore, ou rien d'articulé), il dit la traduction de ce qu'il veut dire (0.45.17).
    const texte = r.mots.trim() || r.sens.trim();
    if (!this.caseVoixHumains.checked || d.mode === "Reve" || !texte) return;
    const c = this.corps(d.humain);
    if (!c) return;
    // Il attend que l'Entité ait fini : ses lèvres ne bougent qu'une fois qu'il parle.
    echange.parleJusqua = 0;
    const p = voixHumain(c.s, texte, this.codeVoix(), r.reaction?.emotion ?? "");
    p.debut = () => {
      echange.parleJusqua = performance.now() + 60_000;
    };
    p.fin = () => {
      echange.parleJusqua = performance.now();
    };
    this.voix.dire(p);
  }

  // ——— Réactions (D1, E1) ———

  /** Joue une suite de gestes chez un humain affiché (x ou z NaN : il reste où il est). */
  private jouer(id: number, etapes: Etape[], emotion: Record<string, number>): void {
    const c = this.corps(id);
    if (!c) return;
    let x = c.x;
    let z = c.z;
    for (const e of etapes) {
      if (Number.isNaN(e.x)) [e.x, e.z] = [x, z];
      [x, z] = [e.x, e.z];
    }
    this.jeux.set(id, { etapes, debut: performance.now() / 1000, emotion });
    this.h.foule.emotions.set(id, emotion);
  }

  private corps(id: number): Corps | null {
    for (const c of this.h.foule.liste) if (c.s.id === id) return Number.isNaN(c.x) ? null : c;
    return null;
  }

  /** Point à `d` mètres de lui, en s'éloignant de la présence (d < 0 : en s'en approchant). */
  private recul(c: Corps, d: number, devie = 0): [number, number] {
    const p = this.presence.force > 0 || (this.dialogue ?? this.fenetre)?.mode === "Apparition" ? this.presence.centre : this.h.camera.position;
    const a = Math.atan2(c.x - p.x, c.z - p.z) + devie;
    return [c.x + Math.sin(a) * d, c.z + Math.cos(a) * d];
  }

  /** Ce que les règles ont décidé à cette réplique, montré chez lui (et chez les témoins à la première). */
  private reagir(d: Dialogue, r: Reaction, premiere: boolean): void {
    const c = this.corps(d.humain);
    if (!c) return;
    const age = c.s.age;
    const h = (n: number) => unite(hacher(d.humain, d.id, n));
    const reve = d.mode === "Reve";
    const tenir = (geste: GesteCombat | null): Etape => ({ geste, duree: Infinity, x: NaN, z: NaN, court: false });
    const etapes: Etape[] = [];
    let visage = VISAGES[r.emotion] ?? VISAGES.doute;
    const nouveau = r.croit && !this.dejaCru;
    if (r.croit) this.dejaCru = true;
    if (reve) {
      // En rêve, il dort : son visage seul bouge (et il s'éveille, la fin venue).
      this.h.foule.emotions.set(d.humain, visage);
      window.setTimeout(() => {
        if (!this.jeux.has(d.humain)) this.h.foule.emotions.delete(d.humain);
      }, 6000);
      return;
    }
    if (r.fin === "Fuite") {
      // La peur le fait fuir : un sursaut, puis il court, à quelques dizaines de mètres, en se retournant à peine.
      visage = VISAGES.effroi;
      etapes.push({ geste: "sursaut", duree: 0.45, x: NaN, z: NaN, court: false });
      const mere = age < 10 && c.s.mere ? this.corps(c.s.mere) : null;
      if (mere) {
        etapes.push({ geste: null, duree: 3, x: mere.x + 0.6, z: mere.z + 0.4, court: true, dos: true }, { geste: "cache", duree: 6, x: mere.x + 0.6, z: mere.z + 0.4, court: false });
      } else {
        const [x, z] = this.recul(c, 25 + 20 * h(1), (h(2) - 0.5) * 0.9);
        etapes.push({ geste: null, duree: 7, x, z, court: true, dos: true });
      }
    } else if (r.fin === "Detourne") {
      // L'incrédule s'en va : un haussement d'épaules, puis il retourne à ce qu'il faisait.
      const [x, z] = this.recul(c, 3 + 2 * h(3), (h(4) - 0.5) * 1.6);
      etapes.push({ geste: "hausser", duree: 1.4, x: NaN, z: NaN, court: false }, { geste: null, duree: 3, x, z, court: false, dos: true });
    } else if (!r.comprise) {
      etapes.push({ geste: "chercher", duree: 4.5, x: NaN, z: NaN, court: false }, tenir("fixer"));
    } else if (!r.croit) {
      // Il n'y croit pas : l'anxieux se prend la tête dans les mains, l'autre hausse les épaules et la fixe, méfiant.
      if (r.nevrosisme > 0.6 && h(5) < 0.7) etapes.push({ geste: "couvrir", duree: 3, x: NaN, z: NaN, court: false }, tenir("fixer"));
      else etapes.push({ geste: "hausser", duree: 1.6, x: NaN, z: NaN, court: false }, tenir("fixer"));
    } else if (r.emotion === "effroi") {
      if (nouveau || premiere) {
        const [x, z] = this.recul(c, 1.2 + 1.2 * r.peur, (h(6) - 0.5) * 0.5);
        etapes.push({ geste: "sursaut", duree: 0.5, x: NaN, z: NaN, court: false }, { geste: "effroi", duree: 1.8, x, z, court: false });
        if (age < 10 && c.s.mere && this.corps(c.s.mere)) {
          const m = this.corps(c.s.mere)!;
          etapes.push({ geste: null, duree: 2.5, x: m.x + 0.6, z: m.z + 0.4, court: true, dos: true }, { geste: "cache", duree: Infinity, x: m.x + 0.6, z: m.z + 0.4, court: false });
        } else if (r.peur > 0.6 && h(7) < 0.6) etapes.push({ geste: "tombeAssis", duree: Infinity, x, z, court: false });
        else etapes.push({ geste: h(8) < 0.5 ? "couvrir" : "effroi", duree: 3, x, z, court: false }, { geste: "fixer", duree: Infinity, x, z, court: false });
      } else etapes.push(tenir(this.tenu(d.humain) ?? "effroi"));
    } else {
      // Émerveillé : bouche bée, puis, selon sa foi et sa déférence, à genoux, prosterné, la main tendue, ou immobile.
      const fin: GesteCombat = r.transcendance > 0.72 && r.agreabilite > 0.5 ? "prosterne"
        : r.transcendance > 0.45 || r.issues.includes("Acceptee") && r.agreabilite > 0.55 ? "genoux"
          : r.extraversion > 0.55 ? "tendre" : "fixer";
      if (nouveau || premiere) etapes.push({ geste: "bouche", duree: 2.2, x: NaN, z: NaN, court: false });
      etapes.push(tenir(this.tenu(d.humain) === "prosterne" || this.tenu(d.humain) === "genoux" ? this.tenu(d.humain)! : fin));
    }
    // Ce qu'il fait d'un ordre, s'il en a reçu un et y croit : il hésite (la main sur la nuque) ou refuse (bras croisés).
    if (r.croit && r.fin === "Aucune") {
      const garde = etapes.pop()!;
      if (r.issues.includes("Refusee")) etapes.push({ geste: "refuser", duree: 3, x: garde.x, z: garde.z, court: false }, tenir(garde.geste === "genoux" || garde.geste === "prosterne" ? "fixer" : garde.geste));
      else if (r.issues.some((x) => x === "Differee" || x === "AMoitie")) etapes.push({ geste: "hesiter", duree: 3.2, x: garde.x, z: garde.z, court: false }, garde);
      else etapes.push(garde);
    }
    // Le geste qu'il dit faire en répondant passe avant celui que l'on devine de son caractère (« Je me mets à genou » : il
    // s'agenouille). Fuir et se détourner restent ce que les règles ont décidé.
    if (r.geste && (r.fin === "Aucune" || r.fin === "Joueur")) {
      const dit = this.gesteDit(c, r.geste);
      if (dit) {
        const debut = etapes.length > 1 && etapes[0].duree < 1 ? [etapes[0]] : [];
        etapes.splice(0, etapes.length, ...debut, ...dit);
      }
    }
    this.jouer(d.humain, etapes, visage);
    if (premiere && r.temoins.length > 0) this.temoins(c, r.temoins);
  }

  /** Les étapes du geste qu'il dit faire, ou null s'il est inconnu. */
  private gesteDit(c: Corps, geste: string): Etape[] | null {
    const tenir = (g: GesteCombat, x = NaN, z = NaN): Etape => ({ geste: g, duree: Infinity, x, z, court: false });
    const puis = (g: GesteCombat, duree: number): Etape[] => [{ geste: g, duree, x: NaN, z: NaN, court: false }, tenir("fixer")];
    switch (geste) {
      case "Genoux":
        return [tenir("genoux")];
      case "Prosterne":
        return [tenir("prosterne")];
      case "Tendre":
        return [tenir("tendre")];
      case "Debout":
        return [tenir("fixer")];
      case "Couvrir":
        return puis("couvrir", 3);
      case "Hausser":
        return puis("hausser", 1.6);
      case "Refuser":
        return puis("refuser", 3);
      case "Reculer": {
        const [x, z] = this.recul(c, 1.2 + 0.6 * unite(hacher(c.s.id, 271)));
        return [{ geste: null, duree: 1.6, x, z, court: false, dos: false }, tenir("fixer", x, z)];
      }
      default:
        return null;
    }
  }

  /** Le geste qu'il tient en ce moment (dernière étape d'une réaction en cours), ou null. */
  private tenu(id: number): GesteCombat | null {
    const j = this.jeux.get(id);
    return j ? j.etapes[j.etapes.length - 1].geste : null;
  }

  /** Les témoins de l'apparition, ceux qu'on voit : ils se tournent vers la lumière, s'approchent et s'agenouillent, ou reculent. */
  private temoins(lui: Corps, liste: { humain: number; croit: boolean }[]): void {
    for (const x of liste) {
      const c = this.corps(x.humain);
      if (!c || Math.hypot(c.x - lui.x, c.z - lui.z) > TEMOINS_VUS) continue;
      const h = (n: number) => unite(hacher(x.humain, n, 263));
      const retard = 0.3 + 1.5 * h(1) + Math.hypot(c.x - lui.x, c.z - lui.z) / 60;
      const etapes: Etape[] = [{ geste: "fixer", duree: retard, x: NaN, z: NaN, court: false }];
      const d = Math.hypot(c.x - this.presence.centre.x, c.z - this.presence.centre.z);
      if (x.croit) {
        // Il s'approche un peu (jamais tout près), puis s'agenouille ou reste bouche bée.
        const pas = Math.max(0, Math.min(3 + 2 * h(2), d - 3.5));
        const [ax, az] = this.recul(c, -pas, (h(3) - 0.5) * 0.4);
        if (d < 30) etapes.push({ geste: "sursaut", duree: 0.5, x: NaN, z: NaN, court: false });
        if (pas > 0.5) etapes.push({ geste: null, duree: 1 + pas / 1.1, x: ax, z: az, court: false });
        etapes.push({ geste: h(4) < 0.65 && c.s.age >= 6 ? "genoux" : "bouche", duree: Infinity, x: ax, z: az, court: false });
        this.jouer(x.humain, etapes, VISAGES.merveille);
      } else {
        // Il doute : il reste à distance, recule d'un pas s'il a peur, et regarde.
        const peureux = c.s.nevrosisme > 0.6;
        const [rx, rz] = this.recul(c, peureux ? 1.5 + h(5) : 0.3);
        etapes.push({ geste: peureux ? "effroi" : "hausser", duree: 1.6, x: rx, z: rz, court: false }, { geste: "fixer", duree: Infinity, x: rx, z: rz, court: false });
        this.jouer(x.humain, etapes, peureux ? VISAGES.effroi : VISAGES.doute);
      }
    }
  }

  /** Fin du dialogue : chacun quitte la pose qu'il tenait, un peu plus tard pour les uns que pour les autres. */
  private relacher(): void {
    const maintenant = performance.now() / 1000;
    for (const [id, j] of this.jeux) {
      let ecoule = maintenant - j.debut;
      for (const e of j.etapes) {
        if (e.duree === Infinity) {
          e.duree = Math.max(0, ecoule) + 2 + 3 * unite(hacher(id, 269));
          break;
        }
        ecoule -= e.duree;
      }
    }
  }

  /** Le rôle qu'il joue en ce moment (pour Foule), ou null s'il n'a pas de réaction en cours. */
  private role(c: Corps): Role | null {
    const j = this.jeux.get(c.s.id);
    if (!j) return null;
    let u = performance.now() / 1000 - j.debut;
    let e = j.etapes[0];
    for (const x of j.etapes) {
      e = x;
      if (u < x.duree) break;
      u -= x.duree;
    }
    const p = this.h.foule.entite ?? this.h.camera.position;
    const enMarche = e.geste === null && Math.hypot(e.x - c.x, e.z - c.z) > 0.3;
    const cap = e.dos || enMarche ? null : Math.atan2(p.x - c.x, p.z - c.z);
    return { x: e.x, z: e.z, cap, geste: e.geste, u, arme: 0, court: e.court, mort: false, face: 0, entite: !e.dos };
  }
}
