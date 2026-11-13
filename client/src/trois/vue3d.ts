import * as THREE from "three/webgpu";
import type { Carte } from "../carte";
import { connexion } from "../connexion";
import { nombre, t } from "../langue";
import { Bruit } from "./bruit";
import { Ciel } from "./ciel";
import { ACTIVITES, Feux3D, Foule, lireScene } from "./foule";
import { Nature3D } from "./nature3d";
import { Relief, type Temperatures, type TerrainVue } from "./relief";
import { Terrain3D } from "./tuiles";

/** Ce que la vue 3D demande au reste du client. */
export interface Contexte {
  carte: Carte;
  nom: (id: number) => string;
  ouvrirFiche: (id: number) => void;
  ficheCourante: () => number | null;
  /** Vivants connus du client (dernière diffusion « Humains ») : pour « Aller voir » et le point de départ. */
  vivants: () => { id: number; nom: string; x: number; y: number; sexe: string }[];
  message: (texte: string) => void;
  quitter: () => void;
}

interface SceneJson {
  secondes: number;
  facteur: number;
  humains: number[];
}

interface TerrainJson {
  roches: string;
  pente: string;
  debit: string;
  profondeur: string;
  lits: number[];
}

interface HorlogeJson {
  facteur: number;
  saut: unknown;
}

type Qualite = "basse" | "moyenne" | "haute";
const QUALITES: Record<Qualite, { pixels: number; finesse: number; vegetation: number }> = {
  basse: { pixels: 1, finesse: 1.3, vegetation: 450 },
  moyenne: { pixels: 1.5, finesse: 1.8, vegetation: 900 },
  haute: { pixels: 2, finesse: 2.4, vegetation: 1400 },
};

/** Vitesse la plus haute en promenade (proposition E1) : au-delà, les humains sauteraient d'une case à l'autre. */
export const FACTEUR_PROMENADE = 10;
const HAUTEUR_YEUX = 1.62;

const decoder = (texte: string) => Uint8Array.from(atob(texte), (c) => c.charCodeAt(0));

function lireStockage(cle: string): string | null {
  try {
    return localStorage.getItem(cle);
  } catch {
    return null;
  }
}

function ecrireStockage(cle: string, valeur: string): void {
  try {
    localStorage.setItem(cle, valeur);
  } catch {
    // Stockage indisponible : le choix vaut pour cette page.
  }
}

/** Texture de détail du sol (gris autour de 0,85), répétée tous les 6 m. */
function textureSol(): THREE.Texture {
  const n = 256;
  const b = new Bruit(4242);
  const data = new Uint8Array(n * n * 4);
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      // Bruit périodique : somme de quatre échantillons pondérés (raccord sans couture).
      const fx = x / n;
      const fy = y / n;
      const e = (u: number, v: number) => b.fbm(u * 64, v * 64, 16, 4);
      const v = e(fx, fy) * (1 - fx) * (1 - fy) + e(fx - 1, fy) * fx * (1 - fy) + e(fx, fy - 1) * (1 - fx) * fy + e(fx - 1, fy - 1) * fx * fy;
      const g = Math.round(255 * Math.min(1, Math.max(0, 0.86 + v * 0.35)));
      data.set([g, g, g, 255], (y * n + x) * 4);
    }
  }
  const tex = new THREE.DataTexture(data, n, n);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.NoColorSpace;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.magFilter = THREE.LinearFilter;
  tex.anisotropy = 8;
  tex.needsUpdate = true;
  return tex;
}

/** Carte de normales des vagues, périodique. */
function textureVagues(): THREE.Texture {
  const n = 256;
  const ondes = [[1, 2, 0.3, 0.2], [3, -1, 0.15, 1.1], [-2, 5, 0.08, 2.3], [7, 3, 0.05, 0.7], [-9, -6, 0.03, 4.1], [12, -11, 0.02, 2.9]];
  const data = new Uint8Array(n * n * 4);
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      let dx = 0;
      let dy = 0;
      for (const [kx, ky, a, p] of ondes) {
        const c = Math.cos((2 * Math.PI * (kx * x + ky * y)) / n + p) * a * 2 * Math.PI;
        dx += c * kx;
        dy += c * ky;
      }
      const v = new THREE.Vector3(-dx * 0.05, -dy * 0.05, 1).normalize();
      data.set([Math.round((v.x * 0.5 + 0.5) * 255), Math.round((v.y * 0.5 + 0.5) * 255), Math.round((v.z * 0.5 + 0.5) * 255), 255], (y * n + x) * 4);
    }
  }
  const tex = new THREE.DataTexture(data, n, n);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.colorSpace = THREE.NoColorSpace;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.needsUpdate = true;
  return tex;
}

/**
 * La vue « Monde 3D » (0.41.0) : le relief, l'eau, le ciel, la végétation et les humains autour de la caméra. On y vole ou on y marche ;
 * le temps du monde y passe au plus ×10. Les panneaux habituels (fiche, journal, Entité…) restent posés par-dessus.
 */
export class Vue3D {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(62, 1, 0.2, 900000);
  private readonly relief: Relief;
  private readonly terrain: Terrain3D;
  private readonly nature: Nature3D;
  private readonly ciel: Ciel;
  private readonly foule: Foule;
  private readonly feux: Feux3D;
  private readonly mer: THREE.Mesh;
  private readonly vagues: THREE.Texture;
  private readonly hud: HTMLElement;
  private readonly bulle: HTMLElement;
  private readonly etiquettes: HTMLElement;
  private ouverte = false;
  private mode: "vol" | "marche" = "vol";
  private lacet = 0;
  private tangage = -0.35;
  private readonly touches = new Set<string>();
  private dernier = performance.now();
  private secondesScene = 0;
  private recuA = performance.now();
  private facteur = 0;
  private facteurAvant: number | null = null;
  private viser: number | null = null;
  private souris: { x: number; y: number } | null = null;
  private qualite: Qualite;
  private images = 0;
  private imagesDepuis = performance.now();
  private parSeconde = 0;
  private jourTemperatures = -1;
  private saisonVue = -1;
  private minuteurs: number[] = [];
  private eauAvertie = 0;
  private readonly surTouche = (e: KeyboardEvent) => this.toucher(e, true);
  private readonly surRelache = (e: KeyboardEvent) => this.toucher(e, false);
  private readonly surHorloge = (h: HorlogeJson) => void this.surveillerVitesse(h);
  private readonly surTaille = () => this.cadrer();

  private constructor(
    private readonly hote: HTMLElement,
    private readonly renderer: THREE.WebGPURenderer,
    private readonly ctx: Contexte,
    terrainVue: TerrainVue | null,
  ) {
    this.relief = new Relief(ctx.carte, terrainVue);
    const sol = new THREE.MeshStandardMaterial({ vertexColors: true, map: textureSol(), roughness: 0.95, metalness: 0 });
    this.vagues = textureVagues();
    const eauDouce = new THREE.MeshStandardMaterial({ color: 0x2c5a63, roughness: 0.08, metalness: 0.1, transparent: true, opacity: 0.8, normalMap: this.vagues, depthWrite: false });
    eauDouce.normalScale.set(0.35, 0.35);
    const eauMer = new THREE.MeshStandardMaterial({ color: 0x27607a, roughness: 0.06, metalness: 0.1, transparent: true, opacity: 0.86, normalMap: this.vagues, depthWrite: false });
    eauMer.normalScale.set(0.5, 0.5);
    this.terrain = new Terrain3D(this.relief, sol, eauDouce);
    this.nature = new Nature3D(this.relief);
    this.ciel = new Ciel(this.scene);
    this.foule = new Foule(this.relief);
    this.feux = new Feux3D(this.relief);
    // La mer : un plan qui suit la caméra, maillé serré près d'elle et lâche au loin (de grands triangles qui passent derrière la
    // caméra se perdent avec la profondeur logarithmique).
    const plan = new THREE.PlaneGeometry(2, 2, 96, 96).rotateX(-Math.PI / 2);
    const pos = plan.getAttribute("position");
    const uv = plan.getAttribute("uv");
    const etirer = (u: number) => Math.sign(u) * Math.abs(u) ** 3 * 800000;
    for (let i = 0; i < pos.count; i++) {
      const x = etirer(pos.getX(i));
      const z = etirer(pos.getZ(i));
      pos.setXYZ(i, x, 0, z);
      uv.setXY(i, x / 40, -z / 40);
    }
    this.mer = new THREE.Mesh(plan, eauMer);
    this.mer.renderOrder = 2;
    this.mer.frustumCulled = false;
    this.scene.add(this.terrain.groupe, this.nature.groupe, this.foule.groupe, this.feux.groupe, this.mer);

    this.qualite = (lireStockage("monde.qualite3d") as Qualite | null) ?? "moyenne";
    if (!(this.qualite in QUALITES)) this.qualite = "moyenne";

    hote.append(renderer.domElement);
    this.hud = document.createElement("div");
    this.hud.className = "hud3d";
    this.bulle = document.createElement("div");
    this.bulle.className = "bulle3d";
    this.bulle.hidden = true;
    this.etiquettes = document.createElement("div");
    this.etiquettes.className = "etiquettes3d";
    hote.append(this.etiquettes, this.bulle, this.hud);
    this.construireHud();
    this.brancherSouris();
    this.appliquerQualite();
  }

  static async creer(hote: HTMLElement, ctx: Contexte): Promise<Vue3D> {
    const forcerWebGL = new URLSearchParams(location.search).has("webgl");
    const renderer = new THREE.WebGPURenderer({ antialias: true, logarithmicDepthBuffer: true, forceWebGL: forcerWebGL });
    await renderer.init();
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 0.55;
    const json = await connexion.invoke<TerrainJson>("Terrain").catch(() => null);
    const terrain = json
      ? { roches: decoder(json.roches), pente: decoder(json.pente), debit: decoder(json.debit), profondeur: decoder(json.profondeur), lits: json.lits }
      : null;
    const vue = new Vue3D(hote, renderer, ctx, terrain);
    // Pour les essais automatiques : ?debug3d expose la vue dans la console.
    if (new URLSearchParams(location.search).has("debug3d")) (window as unknown as { vue3d: Vue3D }).vue3d = vue;
    return vue;
  }

  /** Moteur de rendu effectivement utilisé. */
  get moteur(): string {
    return (this.renderer.backend as { isWebGPUBackend?: boolean }).isWebGPUBackend ? "WebGPU" : "WebGL 2";
  }

  private construireHud(): void {
    this.hud.innerHTML = `
      <div class="ligne">
        <button type="button" data-action="carte">${t("trois.carte")}</button>
        <button type="button" data-action="vol">${t("trois.voler")}</button>
        <button type="button" data-action="marche">${t("trois.marcher")}</button>
        <label>${t("trois.qualite")} <select data-action="qualite">
          <option value="basse">${t("trois.qualite.basse")}</option>
          <option value="moyenne">${t("trois.qualite.moyenne")}</option>
          <option value="haute">${t("trois.qualite.haute")}</option>
        </select></label>
      </div>
      <form class="ligne" data-action="aller">
        <input list="vivants3d" placeholder="${t("trois.allerVoirNom")}" />
        <datalist id="vivants3d"></datalist>
        <button type="submit">${t("trois.allerVoir")}</button>
        <button type="button" data-action="fiche" hidden></button>
      </form>
      <div class="aide" data-role="aide"></div>
      <div class="etat" data-role="etat"></div>`;
    const q = <T extends HTMLElement>(s: string) => this.hud.querySelector<T>(s)!;
    q<HTMLButtonElement>('[data-action="carte"]').addEventListener("click", () => this.ctx.quitter());
    q<HTMLButtonElement>('[data-action="vol"]').addEventListener("click", () => this.changerMode("vol"));
    q<HTMLButtonElement>('[data-action="marche"]').addEventListener("click", () => this.changerMode("marche"));
    const choix = q<HTMLSelectElement>('[data-action="qualite"]');
    choix.value = this.qualite;
    choix.addEventListener("change", () => {
      this.qualite = choix.value as Qualite;
      ecrireStockage("monde.qualite3d", this.qualite);
      this.appliquerQualite();
    });
    const form = q<HTMLFormElement>('[data-action="aller"]');
    const champ = form.querySelector("input")!;
    champ.addEventListener("focus", () => this.remplirVivants());
    form.addEventListener("submit", (e) => {
      e.preventDefault();
      const nom = champ.value.trim().toLocaleLowerCase();
      const h = this.ctx.vivants().find((v) => v.nom.toLocaleLowerCase() === nom);
      if (!h) {
        this.ctx.message(t("trois.introuvable"));
        return;
      }
      champ.value = "";
      champ.blur();
      this.allerVoir(h.id);
    });
    q<HTMLButtonElement>('[data-action="fiche"]').addEventListener("click", () => {
      const id = this.ctx.ficheCourante();
      if (id) this.allerVoir(id);
    });
    this.afficherAide();
  }

  private remplirVivants(): void {
    const liste = this.hud.querySelector("datalist")!;
    liste.replaceChildren(...this.ctx.vivants().map((v) => Object.assign(document.createElement("option"), { value: v.nom })));
  }

  private afficherAide(): void {
    const aide = this.hud.querySelector<HTMLElement>('[data-role="aide"]')!;
    aide.textContent = t(this.mode === "vol" ? "trois.aide.vol" : "trois.aide.marche");
    for (const b of this.hud.querySelectorAll<HTMLButtonElement>('[data-action="vol"], [data-action="marche"]')) {
      b.classList.toggle("actif", b.dataset.action === this.mode);
    }
  }

  private appliquerQualite(): void {
    const q = QUALITES[this.qualite];
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, q.pixels));
    this.terrain.facteur = q.finesse;
    this.nature.portee = q.vegetation;
    this.nature.vider();
    this.cadrer();
  }

  private cadrer(): void {
    const l = this.hote.clientWidth || window.innerWidth;
    const h = this.hote.clientHeight || window.innerHeight;
    this.renderer.setSize(l, h, false);
    this.renderer.domElement.style.width = "100%";
    this.renderer.domElement.style.height = "100%";
    this.camera.aspect = l / h;
    this.camera.updateProjectionMatrix();
  }

  private changerMode(mode: "vol" | "marche"): void {
    this.mode = mode;
    if (mode === "marche") {
      const s = this.relief.surface(this.camera.position.x, this.camera.position.z);
      if (!Number.isNaN(s.eau) && s.eau - s.sol > 1.2) {
        this.ctx.message(t("trois.eauProfonde"));
        this.mode = "vol";
      } else {
        this.camera.position.y = s.sol + HAUTEUR_YEUX;
        this.tangage = Math.max(-0.4, Math.min(0.3, this.tangage));
      }
    }
    this.afficherAide();
  }

  private brancherSouris(): void {
    const toile = this.renderer.domElement;
    let depart: { x: number; y: number; bouge: number } | null = null;
    toile.addEventListener("pointerdown", (e) => {
      if (e.button !== 0) return;
      depart = { x: e.clientX, y: e.clientY, bouge: 0 };
      toile.setPointerCapture(e.pointerId);
      toile.classList.add("glisse");
    });
    toile.addEventListener("pointermove", (e) => {
      this.souris = { x: e.clientX, y: e.clientY };
      if (!depart) return;
      const dx = e.clientX - depart.x;
      const dy = e.clientY - depart.y;
      depart.bouge += Math.abs(dx) + Math.abs(dy);
      depart.x = e.clientX;
      depart.y = e.clientY;
      this.lacet -= dx * 0.0035;
      this.tangage = Math.max(-1.5, Math.min(1.5, this.tangage - dy * 0.0035));
    });
    const lacher = (e: PointerEvent) => {
      if (!depart) return;
      const clic = depart.bouge < 5;
      depart = null;
      toile.classList.remove("glisse");
      if (clic) {
        const id = this.humainSous(e.clientX, e.clientY);
        if (id) this.ctx.ouvrirFiche(id);
      }
    };
    toile.addEventListener("pointerup", lacher);
    toile.addEventListener("pointercancel", () => {
      depart = null;
      toile.classList.remove("glisse");
    });
    toile.addEventListener("pointerleave", () => (this.souris = null));
    toile.addEventListener("wheel", (e) => {
      e.preventDefault();
      if (this.mode !== "vol") return;
      const sol = this.relief.hauteur(this.camera.position.x, this.camera.position.z);
      const haut = Math.max(2, this.camera.position.y - sol);
      const avant = new THREE.Vector3();
      this.camera.getWorldDirection(avant);
      this.camera.position.addScaledVector(avant, -Math.sign(e.deltaY) * Math.max(3, haut * 0.15));
    }, { passive: false });
  }

  private toucher(e: KeyboardEvent, appuye: boolean): void {
    const cible = e.target as HTMLElement | null;
    if (cible && (cible.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(cible.tagName))) return;
    if (document.querySelector("dialog[open]")) return;
    if (!appuye) {
      this.touches.delete(e.code);
      return;
    }
    if (e.key === "m" || e.key === "M") {
      this.changerMode(this.mode === "vol" ? "marche" : "vol");
      return;
    }
    if (e.key === "Escape") {
      this.ctx.quitter();
      return;
    }
    const mouvement = ["KeyW", "KeyA", "KeyS", "KeyD", "KeyQ", "KeyE", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "ShiftLeft", "ShiftRight", "Space", "PageUp", "PageDown"];
    if (mouvement.includes(e.code)) {
      this.touches.add(e.code);
      e.preventDefault();
    }
  }

  /** Met la vue en marche : vitesse limitée, flux Scène, boucle d'images. */
  async ouvrir(): Promise<void> {
    if (this.ouverte) return;
    this.ouverte = true;
    this.hote.hidden = false;
    window.addEventListener("keydown", this.surTouche);
    window.addEventListener("keyup", this.surRelache);
    window.addEventListener("resize", this.surTaille);
    connexion.on("Horloge", this.surHorloge);
    this.cadrer();
    const ficheId = this.ctx.ficheCourante();
    if (ficheId) this.allerVoir(ficheId);
    else this.placerAuDepart();
    const horloge = await connexion.invoke<HorlogeJson>("Horloge").catch(() => null);
    if (horloge && horloge.facteur > FACTEUR_PROMENADE && !horloge.saut) {
      this.facteurAvant = horloge.facteur;
      await connexion.invoke("ChoisirFacteur", 1).catch(() => undefined);
      this.ctx.message(t("trois.tempsReduit"));
    }
    this.dernier = performance.now();
    void this.boucleScene();
    void this.lireFeux();
    void this.lireTemperatures();
    this.minuteurs.push(window.setInterval(() => void this.lireFeux(), 5000), window.setInterval(() => void this.lireTemperatures(), 15000));
    this.renderer.setAnimationLoop(() => this.image());
  }

  /** Arrête la vue et rend la vitesse d'avant. */
  async fermer(): Promise<void> {
    if (!this.ouverte) return;
    this.ouverte = false;
    this.renderer.setAnimationLoop(null);
    this.hote.hidden = true;
    window.removeEventListener("keydown", this.surTouche);
    window.removeEventListener("keyup", this.surRelache);
    window.removeEventListener("resize", this.surTaille);
    connexion.off("Horloge", this.surHorloge);
    for (const m of this.minuteurs) window.clearInterval(m);
    this.minuteurs = [];
    this.touches.clear();
    if (this.facteurAvant !== null) {
      const f = this.facteurAvant;
      this.facteurAvant = null;
      const h = await connexion.invoke<HorlogeJson>("Horloge").catch(() => null);
      if (h && h.facteur > 0 && !h.saut) {
        await connexion.invoke("ChoisirFacteur", f).catch(() => undefined);
        this.ctx.message(t("trois.tempsRendu"));
      }
    }
  }

  detruire(): void {
    void this.fermer();
    this.renderer.dispose();
    this.hote.replaceChildren();
  }

  /** En promenade, la vitesse ne dépasse pas ×10 (sauf un saut dans le futur demandé exprès). */
  private async surveillerVitesse(h: HorlogeJson): Promise<void> {
    if (!this.ouverte || h.saut || h.facteur <= FACTEUR_PROMENADE) return;
    this.facteurAvant ??= h.facteur;
    await connexion.invoke("ChoisirFacteur", FACTEUR_PROMENADE).catch(() => undefined);
    this.ctx.message(t("trois.tempsLimite", { max: FACTEUR_PROMENADE }));
  }

  private placerAuDepart(): void {
    const vivants = this.ctx.vivants();
    const compte = new Map<number, number>();
    for (const v of vivants) compte.set(v.y * this.relief.l + v.x, (compte.get(v.y * this.relief.l + v.x) ?? 0) + 1);
    let meilleur = -1;
    let n = 0;
    for (const [c, k] of compte) if (k > n) [meilleur, n] = [c, k];
    const [x, z] = meilleur >= 0 ? this.relief.camp(meilleur % this.relief.l, Math.floor(meilleur / this.relief.l)) : [this.relief.largeurM / 2, this.relief.hauteurM / 2];
    const sol = this.relief.hauteur(x, z);
    this.camera.position.set(x - 60, Math.max(sol, 0) + 45, z + 90);
    this.regarderVers(new THREE.Vector3(x, sol + 1, z));
    this.mode = "vol";
    this.afficherAide();
  }

  private regarderVers(p: THREE.Vector3): void {
    const d = p.clone().sub(this.camera.position);
    this.lacet = Math.atan2(-d.x, -d.z);
    this.tangage = Math.atan2(d.y, Math.hypot(d.x, d.z));
  }

  /** Va près d'un humain : on y est aussitôt, à pied, à quelques mètres, tourné vers lui. */
  allerVoir(id: number): void {
    const v = this.ctx.vivants().find((h) => h.id === id);
    if (!v) {
      this.ctx.message(t("trois.introuvable"));
      return;
    }
    const [x, z] = this.relief.camp(v.x, v.y);
    const sol = this.relief.hauteur(x, z);
    this.camera.position.set(x + 20, Math.max(sol, 0) + 30, z + 20);
    this.regarderVers(new THREE.Vector3(x, sol, z));
    this.viser = id;
    this.ctx.message(t("trois.versHumain", { nom: v.nom }));
  }

  private async boucleScene(): Promise<void> {
    while (this.ouverte) {
      const p = this.camera.position;
      const sol = this.relief.hauteur(p.x, p.z);
      const portee = Math.max(2400, (p.y - sol) * 3);
      const rayon = Math.min(40, Math.max(3, Math.ceil(portee / this.relief.m)));
      const cx = Math.floor(p.x / this.relief.m);
      const cz = Math.floor(p.z / this.relief.m);
      const scene = await connexion.invoke<SceneJson>("Scene", cx, cz, rayon).catch(() => null);
      if (scene && this.ouverte) {
        this.secondesScene = scene.secondes;
        this.recuA = performance.now();
        this.facteur = scene.facteur;
        this.foule.recevoir(lireScene(scene.humains), scene.secondes);
      }
      await new Promise((r) => window.setTimeout(r, 250));
    }
  }

  private async lireFeux(): Promise<void> {
    const f = await connexion.invoke<{ feux: { x: number; y: number; couve: boolean }[] }>("Feux").catch(() => null);
    if (f) this.feux.definir(f.feux);
  }

  private async lireTemperatures(): Promise<void> {
    const temp = await connexion.invoke<Temperatures>("Temperatures").catch(() => null);
    if (!temp || temp.jour === this.jourTemperatures) return;
    const premiere = this.jourTemperatures < 0;
    this.jourTemperatures = temp.jour;
    this.relief.definirTemperatures(temp);
    const jourAn = temp.jour % 360;
    this.relief.saison = Math.floor(jourAn / 90);
    this.relief.dansSaison = (jourAn % 90) / 90;
    // Les couleurs suivent la saison et la neige : refaites au changement de saison, ou chaque jour s'il gèle quelque part.
    const gel = temp.midi.some((v) => v < 2);
    if (!premiere && (this.relief.saison !== this.saisonVue || gel)) {
      this.terrain.invalider();
      this.nature.invalider();
    } else if (premiere) {
      this.terrain.invalider();
      this.nature.vider();
    }
    this.saisonVue = this.relief.saison;
  }

  /** Instant du monde estimé maintenant : le dernier pas, plus le temps écoulé à la vitesse atteinte (sans dépasser le pas suivant). */
  private secondesMonde(): number {
    return this.secondesScene + Math.min(600, (this.facteur * (performance.now() - this.recuA)) / 1000);
  }

  private deplacer(dt: number): void {
    const k = this.touches;
    const rapide = k.has("ShiftLeft") || k.has("ShiftRight");
    const avant = (k.has("KeyW") || k.has("ArrowUp") ? 1 : 0) - (k.has("KeyS") || k.has("ArrowDown") ? 1 : 0);
    const cote = (k.has("KeyD") ? 1 : 0) - (k.has("KeyA") ? 1 : 0);
    const tourne = (k.has("ArrowLeft") ? 1 : 0) - (k.has("ArrowRight") ? 1 : 0);
    this.lacet += tourne * dt * 1.4;
    const p = this.camera.position;
    const sol = this.relief.hauteur(p.x, p.z);
    if (this.mode === "vol") {
      const haut = Math.max(2, p.y - Math.max(sol, 0));
      const v = Math.max(8, haut * 0.9) * (rapide ? 5 : 1);
      const dir = new THREE.Vector3(-Math.sin(this.lacet) * Math.cos(this.tangage), Math.sin(this.tangage), -Math.cos(this.lacet) * Math.cos(this.tangage));
      const droite = new THREE.Vector3(Math.cos(this.lacet), 0, -Math.sin(this.lacet));
      p.addScaledVector(dir, avant * v * dt).addScaledVector(droite, cote * v * dt);
      const monte = (k.has("KeyE") || k.has("Space") || k.has("PageUp") ? 1 : 0) - (k.has("KeyQ") || k.has("PageDown") ? 1 : 0);
      p.y += monte * v * dt;
      const plancher = Math.max(this.relief.hauteur(p.x, p.z), 0) + 1.5;
      if (p.y < plancher) p.y = plancher;
      p.y = Math.min(p.y, 60000);
    } else {
      const v = rapide ? 4.5 : 1.4;
      const dx = (-Math.sin(this.lacet) * avant + Math.cos(this.lacet) * cote) * v * dt;
      const dz = (-Math.cos(this.lacet) * avant - Math.sin(this.lacet) * cote) * v * dt;
      if (dx !== 0 || dz !== 0) {
        const s = this.relief.surface(p.x + dx, p.z + dz);
        if (!Number.isNaN(s.eau) && s.eau - s.sol > 1.2) {
          if (performance.now() - this.eauAvertie > 4000) {
            this.eauAvertie = performance.now();
            this.ctx.message(t("trois.eauProfonde"));
          }
        } else {
          p.x += dx;
          p.z += dz;
        }
      }
      const cible = this.relief.hauteur(p.x, p.z) + HAUTEUR_YEUX;
      p.y += (cible - p.y) * Math.min(1, dt * 10);
    }
    this.camera.rotation.set(this.tangage, this.lacet, 0, "YXZ");
  }

  private image(): void {
    const maintenant = performance.now();
    const dt = Math.min(0.1, (maintenant - this.dernier) / 1000);
    this.dernier = maintenant;
    this.deplacer(dt);
    const p = this.camera.position;
    const secondes = this.secondesMonde();
    this.ciel.regler(secondes, p);
    this.terrain.mettreAJour(p, 7);
    this.nature.mettreAJour(p, 3);
    this.foule.animer(dt, secondes, p);
    this.feux.animer(maintenant / 1000, p, 1 - this.ciel.clarte);
    this.mer.position.set(p.x, 0, p.z);
    this.vagues.offset.set((p.x / 40 + maintenant / 60000) % 1, (-p.z / 40 + maintenant / 90000) % 1);
    // Aller voir : dès que l'humain est là, on se pose à pied à quelques mètres, face à lui.
    if (this.viser !== null) {
      const cible = this.foule.position(this.viser);
      if (cible) {
        const sol = this.relief.hauteur(cible.x - 5, cible.z + 4);
        this.camera.position.set(cible.x - 5, sol + HAUTEUR_YEUX, cible.z + 4);
        this.regarderVers(cible);
        this.mode = "marche";
        this.afficherAide();
        this.viser = null;
      }
    }
    this.camera.rotation.set(this.tangage, this.lacet, 0, "YXZ");
    this.camera.updateMatrixWorld();
    this.renderer.render(this.scene, this.camera);
    this.montrerNoms();
    this.images++;
    if (maintenant - this.imagesDepuis > 1000) {
      this.parSeconde = (this.images * 1000) / (maintenant - this.imagesDepuis);
      this.images = 0;
      this.imagesDepuis = maintenant;
      this.majEtat();
    }
  }

  private ecran(p: THREE.Vector3): { x: number; y: number } | null {
    const v = p.clone().project(this.camera);
    if (v.z < -1 || v.z > 1) return null;
    const r = this.renderer.domElement.getBoundingClientRect();
    return { x: r.left + ((v.x + 1) / 2) * r.width, y: r.top + ((1 - v.y) / 2) * r.height };
  }

  private humainSous(x: number, y: number): number | null {
    let meilleur: number | null = null;
    let dMin = 30;
    const avant = new THREE.Vector3();
    this.camera.getWorldDirection(avant);
    for (const h of this.foule.tous()) {
      const versLui = h.tete.clone().sub(this.camera.position);
      if (versLui.dot(avant) <= 0 || versLui.length() > 600) continue;
      const e = this.ecran(h.tete);
      if (!e) continue;
      const d = Math.hypot(e.x - x, e.y - y);
      if (d < dMin) {
        dMin = d;
        meilleur = h.id;
      }
    }
    return meilleur;
  }

  /** Noms au-dessus des humains proches, et bulle sur celui que survole la souris. */
  private montrerNoms(): void {
    const avant = new THREE.Vector3();
    this.camera.getWorldDirection(avant);
    const proches = [...this.foule.tous()]
      .map((h) => ({ h, d: h.tete.distanceTo(this.camera.position) }))
      .filter(({ h, d }) => d < 45 && h.tete.clone().sub(this.camera.position).dot(avant) > 0)
      .sort((a, b) => a.d - b.d)
      .slice(0, 12);
    while (this.etiquettes.children.length < proches.length) this.etiquettes.append(document.createElement("span"));
    [...this.etiquettes.children].forEach((el, k) => {
      const e = el as HTMLElement;
      const a = proches[k];
      const pos = a ? this.ecran(a.h.tete.clone().add(new THREE.Vector3(0, 0.35, 0))) : null;
      if (!a || !pos) {
        e.hidden = true;
        return;
      }
      e.hidden = false;
      e.textContent = this.ctx.nom(a.h.id);
      e.style.transform = `translate(${Math.round(pos.x)}px, ${Math.round(pos.y)}px) translate(-50%, -100%)`;
      e.style.opacity = String(Math.max(0.35, 1 - a.d / 50));
    });
    const id = this.souris ? this.humainSous(this.souris.x, this.souris.y) : null;
    if (!id || !this.souris) {
      this.bulle.hidden = true;
      this.renderer.domElement.classList.remove("survol");
      return;
    }
    const h = [...this.foule.tous()].find((x) => x.id === id)!;
    const sexe = this.ctx.vivants().find((v) => v.id === id)?.sexe;
    const activite = t(`activite.${ACTIVITES[h.s.activite] ?? "Repos"}`, { g: sexe === "femme" ? "f" : "m" });
    const age = h.s.age < 2 ? t("age.mois", { n: Math.round(h.s.age * 12) }) : t("age.ans", { n: Math.floor(h.s.age) });
    this.bulle.textContent = t("trois.survol", { nom: this.ctx.nom(id), age, activite });
    this.bulle.style.transform = `translate(${this.souris.x + 14}px, ${this.souris.y + 14}px)`;
    this.bulle.hidden = false;
    this.renderer.domElement.classList.add("survol");
  }

  private majEtat(): void {
    const p = this.camera.position;
    const sol = this.relief.hauteur(p.x, p.z);
    const etat = this.hud.querySelector<HTMLElement>('[data-role="etat"]')!;
    const morceaux = [
      t("trois.position", { x: nombre(p.x / 1000, 1), z: nombre(p.z / 1000, 1), haut: nombre(Math.round(p.y - Math.max(sol, 0))), alt: nombre(Math.round(sol)) }),
      t("trois.humainsVus", { n: this.foule.nombre }),
      `${this.moteur} · ${t("trois.images", { n: Math.round(this.parSeconde) })}`,
    ];
    if (this.terrain.attente > 0) morceaux.push(t("trois.construction"));
    etat.textContent = morceaux.join(" · ");
    const bouton = this.hud.querySelector<HTMLButtonElement>('[data-action="fiche"]')!;
    const id = this.ctx.ficheCourante();
    bouton.hidden = !id;
    if (id) bouton.textContent = t("trois.allerVoirFiche", { nom: this.ctx.nom(id) });
  }
}
