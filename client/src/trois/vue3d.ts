import * as THREE from "three/webgpu";
import { float, mix, mrt, normalView, output, pass, renderOutput, screenUV, uniform, vec4, velocity } from "three/tsl";
import { ao } from "three/addons/tsl/display/GTAONode.js";
import { bloom } from "three/addons/tsl/display/BloomNode.js";
import { traa } from "three/addons/tsl/display/TRAANode.js";
import { smaa } from "three/addons/tsl/display/SMAANode.js";
import { ssr } from "three/addons/tsl/display/SSRNode.js";
import { Rayons } from "./rayons";
import { CSMShadowNode } from "three/addons/csm/CSMShadowNode.js";
import type { Carte } from "../carte";
import { connexion } from "../connexion";
import { nombre, t } from "../langue";
import { Bruit } from "./bruit";
import { Ciel, type ReglageNuages } from "./ciel";
import { Camp3D, type CampJson } from "./camp";
import { ACTIVITES, Feux3D, Foule, lireScene } from "./foule";
import { MiniCarte } from "./minicarte";
import { Nature3D } from "./nature3d";
import { Sol3D } from "./sol3d";
import { CorpsProches } from "./proches";
import { echange } from "../dialogue";
import { Relief, type Temperatures, type TerrainVue } from "./relief";
import { Terrain3D } from "./tuiles";
import { Eau, maillageMer } from "./eau3d";
import { Matieres, Vent } from "./matieres";

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

type Qualite = "basse" | "moyenne" | "haute" | "ultra";
interface ReglageQualite {
  pixels: number; finesse: number; vegetation: number; corps: number; nombre: number; visages: number; nombreVisages: number; reels: number; porteeReels: number; silhouettes: number; camp: number; sol: number;
  /** Ombres (0.41.3) : côté des cartes d'ombre (0 : aucune), portée des cascades (m), ombres du relief et des petites plantes, ombres douces. */
  ombres: number; porteeOmbres: number; ombreRelief: boolean; ombrePetits: boolean; douces: boolean;
  /** Occlusion ambiante : résolution relative (0 : aucune). Halo : lumières vives qui débordent. */
  ao: number; halo: boolean;
  /**
   * Effets modernes (0.41.6) : nuages en volume (null : nappe peinte), lissage des bords (SMAA, ou temporel TRAA), rayons du
   * soleil dans la brume, réflexions à l'écran sur l'eau (qualité de 0 à 1, 0 : aucune ; résolution relative).
   */
  nuages: ReglageNuages | null; lissage: "aucun" | "smaa" | "traa"; rayons: boolean; reflets: number; resReflets: number;
}
/**
 * Réglages de Qualité (proposition G1, 0.41.1 ; Ultra et lumière en 0.41.3) : corps détaillés jusqu'à `corps` m (au plus `nombre`),
 * silhouettes jusqu'à `silhouettes` m, points de vie au-delà ; camp et bêtes jusqu'à `camp` m. Basse reste proche de la 0.41.2.
 */
const QUALITES: Record<Qualite, ReglageQualite> = {
  basse: { pixels: 1, finesse: 1.3, vegetation: 450, corps: 40, nombre: 16, visages: 5, nombreVisages: 4, reels: 8, porteeReels: 15, silhouettes: 1200, camp: 1200, sol: 30, ombres: 0, porteeOmbres: 0, ombreRelief: false, ombrePetits: false, douces: false, ao: 0, halo: false, nuages: null, lissage: "aucun", rayons: false, reflets: 0, resReflets: 0 },
  moyenne: { pixels: 1.5, finesse: 1.8, vegetation: 900, corps: 90, nombre: 45, visages: 8, nombreVisages: 8, reels: 16, porteeReels: 30, silhouettes: 2000, camp: 2000, sol: 55, ombres: 1024, porteeOmbres: 160, ombreRelief: false, ombrePetits: false, douces: false, ao: 0, halo: false, nuages: null, lissage: "smaa", rayons: false, reflets: 0, resReflets: 0 },
  haute: { pixels: 2, finesse: 2.4, vegetation: 1400, corps: 150, nombre: 80, visages: 25, nombreVisages: 20, reels: 40, porteeReels: 70, silhouettes: 2500, camp: 3000, sol: 85, ombres: 2048, porteeOmbres: 450, ombreRelief: true, ombrePetits: false, douces: false, ao: 0.5, halo: true, nuages: { pas: 24, pasLumiere: 4 }, lissage: "traa", rayons: true, reflets: 0.4, resReflets: 0.5 },
  ultra: { pixels: 3, finesse: 3.2, vegetation: 2200, corps: 250, nombre: 140, visages: 35, nombreVisages: 32, reels: 70, porteeReels: 120, silhouettes: 3500, camp: 4000, sol: 120, ombres: 4096, porteeOmbres: 1000, ombreRelief: true, ombrePetits: true, douces: true, ao: 1, halo: true, nuages: { pas: 48, pasLumiere: 6 }, lissage: "traa", rayons: true, reflets: 0.7, resReflets: 1 },
};

/** Vitesse la plus haute en promenade (proposition E1) : au-delà, les humains sauteraient d'une case à l'autre. */
export const FACTEUR_PROMENADE = 10;
const HAUTEUR_YEUX = 1.62;
/** Multiplicateurs de vitesse de déplacement (0.41.1 ; ×0,05 en 0.41.3) : touches 1 à 7, ou + et −. Maj multiplie encore par 6. */
const ALLURES = [0.05, 0.25, 1, 4, 16, 64, 256];
/** Plan proche de la caméra (0.41.3) : 1 cm, pour venir tout contre un visage (la profondeur logarithmique garde le lointain net). */
const PRES = new URLSearchParams(location.search).has("profondeurlineaire") ? 0.2 : 0.01;
/** Portrait (0.41.3) : distance au visage, de 30 cm à 3 m. */
const PORTRAIT_MIN = 0.3;
const PORTRAIT_MAX = 3;

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
  private readonly camera = new THREE.PerspectiveCamera(62, 1, PRES, 900000);
  private readonly relief: Relief;
  private readonly terrain: Terrain3D;
  private readonly nature: Nature3D;
  private readonly sol: Sol3D;
  private readonly ciel: Ciel;
  private readonly foule: Foule;
  private readonly feux: Feux3D;
  private readonly proches: CorpsProches;
  private readonly camp: Camp3D;
  private readonly mer: THREE.Mesh;
  private readonly vagues: THREE.Texture;
  /** Eau (0.41.5) : carte des fonds, matériaux de la mer et de l'eau douce. Vent : plantes et vagues. */
  private readonly eau: Eau;
  private readonly vent: Vent;
  private readonly matieres: Matieres | null;
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
  /**
   * Suivi automatique (0.41.2) : la caméra reste près d'un humain où qu'il aille. `angle` : d'où on le regarde (0 : de face),
   * relatif à son cap ; glisser la souris tourne autour de lui, la molette rapproche ou éloigne.
   */
  private suivi: { id: number; angle: number; distance: number; hauteur: number; perdu: number; portrait: boolean } | null = null;
  /** Lumière (0.41.3) : ombres en cascade, chaîne de rendu (occlusion, halo, exposition), exposition courante. */
  private ombres: CSMShadowNode | null = null;
  private chaine: THREE.RenderPipeline | null = null;
  private aoNoeud: ReturnType<typeof ao> | null = null;
  private readonly uExposition = uniform(0.6);
  /** Rayons du soleil dans la brume (0.41.6). */
  private readonly rayons = new Rayons();
  private exposition = 0.6;
  private imagesOmbres = 0;
  private fondA = 0;
  /** Compteur d'images détaillé (touche I) : durées des dernières images. */
  private readonly perf: HTMLElement;
  private readonly durees: number[] = [];
  private souris: { x: number; y: number } | null = null;
  private qualite: Qualite;
  private images = 0;
  private imagesDepuis = performance.now();
  private parSeconde = 0;
  private jourTemperatures = -1;
  private saisonVue = -1;
  private minuteurs: number[] = [];
  private eauAvertie = 0;
  private allure = 2;
  private readonly mini: MiniCarte;
  private miniA = 0;
  private readonly surTouche = (e: KeyboardEvent) => this.toucher(e, true);
  private readonly surRelache = (e: KeyboardEvent) => this.toucher(e, false);
  private readonly surHorloge = (h: HorlogeJson) => void this.surveillerVitesse(h);
  private readonly surTaille = () => this.cadrer();

  private constructor(
    private readonly hote: HTMLElement,
    private readonly renderer: THREE.WebGPURenderer,
    private readonly ctx: Contexte,
    terrainVue: TerrainVue | null,
    matieres: Matieres | null,
  ) {
    this.relief = new Relief(ctx.carte, terrainVue);
    this.ciel = new Ciel(this.scene);
    this.vagues = textureVagues();
    this.vent = matieres?.vent ?? new Vent();
    this.matieres = matieres;
    // 0.41.5 : sol en couches photographiées (si les textures sont là), eau qui suit la profondeur des fonds.
    let sol: THREE.Material;
    if (matieres) sol = matieres.materiauSol();
    else {
      const detail = textureSol();
      detail.repeat.set(1 / 6, 1 / 6);
      sol = new THREE.MeshStandardMaterial({ vertexColors: true, map: detail, roughness: 0.95, metalness: 0 });
    }
    this.eau = new Eau(this.relief, this.vagues, this.ciel.reflets, this.vent);
    const eauDouce = this.eau.materiau(false);
    const eauMer = this.eau.materiau(true);
    this.terrain = new Terrain3D(this.relief, sol, eauDouce, matieres?.moyennes ?? null);
    this.nature = new Nature3D(this.relief, matieres);
    this.sol = new Sol3D(this.relief, matieres);
    this.foule = new Foule(this.relief);
    this.feux = new Feux3D(this.relief);
    this.proches = new CorpsProches(this.foule);
    this.camp = new Camp3D(this.relief);
    // La mer : des anneaux autour de la caméra, fins près d'elle (houle), larges jusqu'à l'horizon.
    const plan = maillageMer();
    this.mer = new THREE.Mesh(plan, eauMer);
    this.mer.renderOrder = 2;
    this.mer.frustumCulled = false;
    this.scene.add(this.terrain.groupe, this.nature.groupe, this.sol.groupe, this.foule.groupe, this.feux.groupe, this.proches.groupe, this.camp.groupe, this.mer);

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
    this.mini = new MiniCarte(ctx.carte, this.relief.m, (x, z) => this.teleporter(x, z));
    // L'allure gardée avant la 0.41.3 comptait à partir de ×0,25 : on la décale d'un cran.
    const allureLue = lireStockage("monde.allure3d.2") ?? (lireStockage("monde.allure3d") === null ? null : String(Number(lireStockage("monde.allure3d")) + 1));
    const allure = allureLue === null ? NaN : Number(allureLue);
    this.allure = 3;
    if (Number.isInteger(allure) && allure >= 0 && allure < ALLURES.length) this.allure = allure;
    this.perf = document.createElement("div");
    this.perf.className = "perf3d";
    this.perf.hidden = lireStockage("monde.perf3d") !== "1";
    hote.append(this.etiquettes, this.bulle, this.hud, this.mini.element, this.perf);
    this.construireHud();
    this.brancherSouris();
    this.appliquerQualite();
  }

  static async creer(hote: HTMLElement, ctx: Contexte): Promise<Vue3D> {
    const forcerWebGL = new URLSearchParams(location.search).has("webgl");
    const renderer = new THREE.WebGPURenderer({ antialias: true, logarithmicDepthBuffer: !new URLSearchParams(location.search).has("profondeurlineaire"), forceWebGL: forcerWebGL });
    await renderer.init();
    // Rendu des tons « AgX » (comme un film ou un capteur : les fortes lumières passent au blanc sans virer) ; l'exposition suit la lumière.
    renderer.toneMapping = THREE.AgXToneMapping;
    renderer.toneMappingExposure = 1;
    renderer.shadowMap.enabled = true;
    const json = await connexion.invoke<TerrainJson>("Terrain").catch(() => null);
    const terrain = json
      ? { roches: decoder(json.roches), pente: decoder(json.pente), debit: decoder(json.debit), profondeur: decoder(json.profondeur), lits: json.lits }
      : null;
    const matieres = await Matieres.charger();
    const vue = new Vue3D(hote, renderer, ctx, terrain, matieres);
    // Pour les essais automatiques : ?debug3d expose la vue dans la console.
    if (new URLSearchParams(location.search).has("debug3d")) Object.assign(window, { vue3d: vue, THREE3: THREE });
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
        <label>${t("trois.allure")} <select data-action="allure">
          ${ALLURES.map((a, k) => `<option value="${k}">${k + 1} · ×${nombre(a, 2)}</option>`).join("")}
        </select></label>
        <label>${t("trois.qualite")} <select data-action="qualite">
          <option value="basse">${t("trois.qualite.basse")}</option>
          <option value="moyenne">${t("trois.qualite.moyenne")}</option>
          <option value="haute">${t("trois.qualite.haute")}</option>
          <option value="ultra">${t("trois.qualite.ultra")}</option>
        </select></label>
      </div>
      <form class="ligne" data-action="aller">
        <input list="vivants3d" placeholder="${t("trois.allerVoirNom")}" />
        <datalist id="vivants3d"></datalist>
        <button type="submit">${t("trois.allerVoir")}</button>
        <button type="button" data-action="fiche" hidden></button>
        <button type="button" data-action="suivre" hidden></button>
        <button type="button" data-action="portrait" hidden></button>
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
    const allure = q<HTMLSelectElement>('[data-action="allure"]');
    allure.value = String(this.allure);
    allure.addEventListener("change", () => this.changerAllure(Number(allure.value)));
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
    q<HTMLButtonElement>('[data-action="suivre"]').addEventListener("click", () => this.basculerSuivi());
    q<HTMLButtonElement>('[data-action="portrait"]').addEventListener("click", () => this.basculerPortrait());
    this.afficherAide();
  }

  private remplirVivants(): void {
    const liste = this.hud.querySelector("datalist")!;
    liste.replaceChildren(...this.ctx.vivants().map((v) => Object.assign(document.createElement("option"), { value: v.nom })));
  }

  private afficherAide(): void {
    const aide = this.hud.querySelector<HTMLElement>('[data-role="aide"]')!;
    aide.textContent = t(this.suivi?.portrait ? "trois.aide.portrait" : this.suivi ? "trois.aide.suivi" : this.mode === "vol" ? "trois.aide.vol" : "trois.aide.marche");
    for (const b of this.hud.querySelectorAll<HTMLButtonElement>('[data-action="vol"], [data-action="marche"]')) {
      b.classList.toggle("actif", b.dataset.action === this.mode);
    }
  }

  private appliquerQualite(): void {
    const q = QUALITES[this.qualite];
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, q.pixels));
    this.reglerLumiere(q);
    this.terrain.facteur = q.finesse;
    this.nature.portee = q.vegetation;
    if (this.matieres) this.matieres.portee.value = q.vegetation;
    this.sol.portee = q.sol;
    this.proches.reglage = { portee: q.corps, nombre: q.nombre, visages: q.visages, nombreVisages: q.nombreVisages, reels: q.reels, porteeReels: q.porteeReels };
    this.foule.porteeSilhouettes = q.silhouettes;
    this.camp.portee = q.camp;
    this.nature.vider();
    this.sol.vider();
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
    if (this.ombres?.camera) this.ombres.updateFrustums();
  }

  /**
   * Lumière selon la Qualité (0.41.3, proposition C1) : ombres du soleil et de la lune en cascades (nettes près de soi, plus
   * larges au loin), occlusion ambiante (GTAO : les creux et les plis s'assombrissent), halo des lumières vives, exposition.
   */
  private reglerLumiere(q: ReglageQualite): void {
    const astre = this.ciel.astre;
    if (q.ombres > 0) {
      astre.shadow.mapSize.set(q.ombres, q.ombres);
      astre.shadow.bias = -0.0004;
      astre.shadow.normalBias = 0.04;
      astre.shadow.camera.near = 1;
      astre.shadow.camera.far = 30000;
      if (!this.ombres) {
        this.ombres = new CSMShadowNode(astre, { cascades: 4, maxFar: q.porteeOmbres, mode: "practical", lightMargin: 800 });
        this.ombres.fade = true;
        astre.shadow.shadowNode = this.ombres;
      }
      this.ombres.maxFar = q.porteeOmbres;
      for (const l of this.ombres.lights) l.shadow?.mapSize.set(q.ombres, q.ombres);
      if (this.ombres.camera) this.ombres.updateFrustums();
    }
    astre.castShadow = q.ombres > 0;
    // Ombres douces en Ultra : un filtrage plus large (PCF) adoucit le bord des ombres.
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    astre.shadow.radius = q.douces ? 3 : 1;
    for (const l of this.ombres?.lights ?? []) if (l.shadow) l.shadow.radius = astre.shadow.radius;
    this.ombrer();
    // Chaîne de rendu : une passe de la scène (couleur, profondeur, normales, mouvement, masques), l'occlusion, les reflets de
    // l'eau, les rayons du soleil, l'exposition, le lissage, le halo, puis les tons (et le lissage SMAA, sur l'image finale).
    this.chaine?.dispose();
    this.aoNoeud?.dispose();
    this.chaine = null;
    this.aoNoeud = null;
    this.ciel.reglerNuages(q.nuages, q.rayons);
    this.eau.reglerReflets(q.reflets > 0);
    if (q.ao === 0 && !q.halo && q.lissage === "aucun" && !q.rayons && q.reflets === 0) {
      this.renderer.toneMappingExposure = this.exposition;
      return;
    }
    this.renderer.toneMappingExposure = 1;
    // Le lissage remplace l'anticrénelage matériel (MSAA) : la passe se fait sans échantillons multiples.
    const scene = pass(this.scene, this.camera, q.lissage === "aucun" ? {} : { samples: 0 });
    const sorties: Record<string, THREE.Node> = { output };
    if (q.ao > 0 || q.reflets > 0) sorties.normal = normalView;
    if (q.lissage === "traa") sorties.velocity = velocity;
    if (q.rayons || q.reflets > 0) sorties.extra = vec4(0, 0, 0, 1);
    if (Object.keys(sorties).length > 1) scene.setMRT(mrt(sorties));
    const profondeur = scene.getTextureNode("depth");
    let rendu = scene.getTextureNode("output").rgb;
    if (q.ao > 0) {
      const noeud = ao(profondeur, scene.getTextureNode("normal"), this.camera);
      noeud.resolutionScale = q.ao;
      noeud.samples.value = q.ao >= 1 ? 16 : 12;
      noeud.useTemporalFiltering = false;
      this.aoNoeud = noeud;
      rendu = rendu.mul(mix(float(1), noeud.getTextureNode().sample(screenUV).r, 0.85));
    }
    if (q.reflets > 0) {
      // Réflexions à l'écran, seulement sur l'eau (part du reflet écrite par l'eau) ; ailleurs le reflet du ciel reste.
      const poids = scene.getTextureNode("extra").r;
      const reflet = ssr(scene.getTextureNode("output"), profondeur, scene.getTextureNode("normal") as unknown as THREE.Node<"vec3">, {
        metalnessNode: poids.greaterThan(0.001).select(float(1), float(0)), roughnessNode: float(0.03), camera: this.camera,
      });
      reflet.resolutionScale = q.resReflets;
      reflet.quality.value = q.reflets;
      reflet.maxDistance.value = 600;
      reflet.thickness.value = 1.5;
      reflet.blurQuality = 2;
      const r = reflet.getTextureNode().sample(screenUV);
      rendu = mix(rendu, r.rgb, r.a.mul(poids).clamp(0, 1));
    }
    if (q.rayons) rendu = rendu.add(this.rayons.noeud(scene.getTextureNode("extra"), this.camera));
    rendu = rendu.mul(this.uExposition);
    if (q.lissage === "traa") rendu = traa(rendu, profondeur, scene.getTextureNode("velocity"), this.camera).rgb;
    if (q.halo) rendu = rendu.add(bloom(vec4(rendu, 1), 0.12, 0.4, 1.2).rgb);
    const chaine = new THREE.RenderPipeline(this.renderer);
    if (q.lissage === "smaa") {
      chaine.outputColorTransform = false;
      chaine.outputNode = smaa(renderOutput(vec4(rendu, 1)));
    } else chaine.outputNode = vec4(rendu, 1);
    // Les sorties de la passe ont changé : chaque matériau déjà compilé doit écrire les nouvelles (sinon le sol, compilé avant,
    // n'écrirait ni mouvement ni masque, et le lissage comme les reflets liraient des restes).
    const vus = new Set<THREE.Material>();
    this.scene.traverse((o) => {
      const m = (o as THREE.Mesh).material;
      if (!m) return;
      for (const x of Array.isArray(m) ? m : [m]) if (!vus.has(x)) {
        vus.add(x);
        x.needsUpdate = true;
      }
    });
    this.chaine = chaine;
  }

  /** Qui porte et qui reçoit les ombres, d'après la Qualité (refait souvent : les maillages naissent et meurent). */
  private ombrer(): void {
    const q = QUALITES[this.qualite];
    const actif = q.ombres > 0;
    const regler = (g: THREE.Object3D, porter: boolean) => g.traverse((o) => {
      const m = o as THREE.Mesh;
      if (!m.isMesh) return;
      const mat = (Array.isArray(m.material) ? m.material[0] : m.material) as THREE.Material & { isMeshBasicMaterial?: boolean };
      const basique = mat.isMeshBasicMaterial === true;
      m.castShadow = actif && porter && !basique && !mat.transparent && !mat.polygonOffset;
      m.receiveShadow = actif && !basique;
    });
    regler(this.terrain.groupe, q.ombreRelief);
    regler(this.nature.groupe, true);
    regler(this.sol.groupe, q.ombrePetits);
    regler(this.foule.groupe, true);
    regler(this.feux.groupe, true);
    regler(this.proches.groupe, true);
    regler(this.camp.groupe, true);
    this.mer.receiveShadow = actif;
  }

  private changerAllure(k: number): void {
    this.allure = Math.max(0, Math.min(ALLURES.length - 1, k));
    ecrireStockage("monde.allure3d.2", String(this.allure));
    const choix = this.hud.querySelector<HTMLSelectElement>('[data-action="allure"]');
    if (choix) choix.value = String(this.allure);
    this.ctx.message(t("trois.allureChoisie", { n: nombre(ALLURES[this.allure], 2) }));
  }

  /** Mini-carte : la caméra va au point choisi, en vol, assez haut pour voir alentour, avec le même regard. */
  private teleporter(x: number, z: number): void {
    const sol = Math.max(0, this.relief.hauteur(x, z));
    const haut = Math.max(120, Math.min(2000, this.camera.position.y - Math.max(0, this.relief.hauteur(this.camera.position.x, this.camera.position.z))));
    this.camera.position.set(x, sol + haut, z);
    this.viser = null;
    this.mode = "vol";
    this.afficherAide();
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
      if (this.suivi) {
        this.suivi.angle -= dx * 0.006;
        // En Portrait, glisser de haut en bas fait tourner autour du visage (au-dessus, en dessous) ; en suivi, monter ou descendre.
        if (this.suivi.portrait) this.suivi.hauteur = Math.max(-0.7, Math.min(1.2, this.suivi.hauteur + dy * 0.005));
        else this.suivi.hauteur = Math.max(-0.5, Math.min(12, this.suivi.hauteur - dy * 0.01));
        return;
      }
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
      if (this.suivi) {
        // Jusqu'à 35 cm du visage en suivi, de 30 cm à 3 m en Portrait (0.41.3).
        const [min, max] = this.suivi.portrait ? [PORTRAIT_MIN, PORTRAIT_MAX] : [0.35, 60];
        this.suivi.distance = Math.max(min, Math.min(max, this.suivi.distance * (e.deltaY > 0 ? 1.1 : 1 / 1.1)));
        return;
      }
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
    const chiffre = /^(?:Digit|Numpad)([1-7])$/.exec(e.code);
    if (chiffre) {
      this.changerAllure(Number(chiffre[1]) - 1);
      return;
    }
    if (e.key === "+" || e.code === "NumpadAdd" || e.key === "-" || e.code === "NumpadSubtract") {
      this.changerAllure(this.allure + (e.key === "+" || e.code === "NumpadAdd" ? 1 : -1));
      return;
    }
    if (e.key === "Escape") {
      if (this.suivi) {
        this.arreterSuivi();
        return;
      }
      this.ctx.quitter();
      return;
    }
    if (e.key === "f" || e.key === "F") {
      this.basculerSuivi();
      return;
    }
    if (e.key === "p" || e.key === "P") {
      this.basculerPortrait();
      return;
    }
    if (e.key === "i" || e.key === "I") {
      this.perf.hidden = !this.perf.hidden;
      ecrireStockage("monde.perf3d", this.perf.hidden ? "0" : "1");
      return;
    }
    const mouvement = ["KeyW", "KeyA", "KeyS", "KeyD", "KeyQ", "KeyE", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "ShiftLeft", "ShiftRight", "Space", "PageUp", "PageDown"];
    if (mouvement.includes(e.code)) {
      // Se déplacer soi-même met fin au suivi.
      if (this.suivi && e.code !== "ShiftLeft" && e.code !== "ShiftRight") this.arreterSuivi();
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
    void this.boucleCamp();
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

  /** Suivre l'humain de la fiche ouverte (ou cesser de suivre). */
  private basculerSuivi(): void {
    if (this.suivi) {
      this.arreterSuivi();
      return;
    }
    const id = this.ctx.ficheCourante();
    if (!id) {
      this.ctx.message(t("trois.suivreSansFiche"));
      return;
    }
    this.suivre(id);
  }

  /**
   * Mode Portrait (0.41.3) : la caméra tourne autour du visage de l'humain suivi (sinon de celui de la fiche, sinon du plus proche
   * devant soi), de 30 cm à 3 m ; il garde son visage détaillé. P, F ou Échap pour en sortir.
   */
  basculerPortrait(): void {
    if (this.suivi?.portrait) {
      this.arreterSuivi();
      return;
    }
    let id = this.suivi?.id ?? this.ctx.ficheCourante();
    if (!id) {
      const avant = new THREE.Vector3();
      this.camera.getWorldDirection(avant);
      let meilleur = 60;
      for (const h of this.foule.tous()) {
        const v = h.tete.clone().sub(this.camera.position);
        const d = v.length();
        if (d < meilleur && v.dot(avant) > d * 0.5) [meilleur, id] = [d, h.id];
      }
    }
    if (!id) {
      this.ctx.message(t("trois.portraitSansHumain"));
      return;
    }
    this.portrait(id);
  }

  /** Portrait d'un humain : de face, un peu de côté, à 80 cm. */
  portrait(id: number): void {
    if (this.suivi?.id === id) {
      this.suivi.portrait = true;
      this.suivi.angle = 0.25;
      this.suivi.distance = 0.8;
      this.suivi.hauteur = 0.05;
    } else {
      this.suivre(id, true);
      if (!this.suivi) return;
    }
    this.proches.force = id;
    this.afficherAide();
    this.majEtat();
    this.ctx.message(t("trois.portraitDebut", { nom: this.ctx.nom(id) }));
  }

  /** Suivi automatique d'un humain : de trois quarts, à quelques mètres, à hauteur d'homme. */
  suivre(id: number, portrait = false): void {
    const v = this.ctx.vivants().find((h) => h.id === id);
    if (!v) {
      this.ctx.message(t("trois.introuvable"));
      return;
    }
    this.viser = null;
    this.suivi = portrait ? { id, angle: 0.25, distance: 0.8, hauteur: 0.05, perdu: 0, portrait: true } : { id, angle: Math.PI * 0.8, distance: 4.5, hauteur: 0.4, perdu: 0, portrait: false };
    this.proches.force = id;
    if (!this.foule.position(id)) {
      // Pas encore dans la scène : on va à sa case, il apparaîtra au prochain flux.
      const [x, z] = this.relief.camp(v.x, v.y);
      this.camera.position.set(x + 4, Math.max(this.relief.hauteur(x, z), 0) + 3, z + 4);
    }
    this.mode = "vol";
    this.afficherAide();
    this.majEtat();
    if (!portrait) this.ctx.message(t("trois.suiviDebut", { nom: v.nom }));
  }

  private arreterSuivi(): void {
    if (!this.suivi) return;
    const nom = this.ctx.nom(this.suivi.id);
    this.suivi = null;
    this.proches.force = null;
    this.majEtat();
    this.afficherAide();
    this.ctx.message(t("trois.suiviFin", { nom }));
  }

  /** À chaque image : place la caméra près de l'humain suivi, tournée vers lui. */
  private suivreImage(dt: number): void {
    const s = this.suivi;
    if (!s) return;
    const etat = this.foule.etat(s.id);
    if (!etat) {
      // Hors de la scène (mort, ou trop loin d'un coup) : on rejoint sa case ; au bout de dix secondes, on abandonne.
      s.perdu += dt;
      const v = this.ctx.vivants().find((h) => h.id === s.id);
      if (!v || s.perdu > 10) {
        this.ctx.message(t("trois.suiviPerdu", { nom: this.ctx.nom(s.id) }));
        this.suivi = null;
        this.proches.force = null;
        this.afficherAide();
        this.majEtat();
        return;
      }
      const [x, z] = this.relief.camp(v.x, v.y);
      this.camera.position.set(x + 4, Math.max(this.relief.hauteur(x, z), 0) + 3, z + 4);
      return;
    }
    s.perdu = 0;
    const { tete, cap, enMarche, taille } = etat;
    if (s.portrait) {
      // Portrait : autour des yeux, à la distance choisie, d'après la direction de son visage (couché, on le regarde d'au-dessus) ;
      // l'angle reste attaché à son visage s'il se tourne ou marche.
      const yeux = new THREE.Vector3();
      const face = new THREE.Vector3();
      if (!this.proches.yeux(s.id, yeux, face)) {
        yeux.copy(tete).setY(tete.y + taille * 0.08);
        face.set(Math.sin(cap), 0, Math.cos(cap));
      }
      const dir = face.clone().applyAxisAngle(new THREE.Vector3(0, 1, 0), s.angle);
      dir.multiplyScalar(Math.cos(s.hauteur)).add(new THREE.Vector3(0, Math.sin(s.hauteur), 0)).normalize();
      // Visage tourné vers le sol (à plat ventre, à la nage) : on reste au-dessus.
      if (dir.y < -0.2) dir.setY(-0.2).normalize();
      const cible = yeux.clone().addScaledVector(dir, s.distance);
      const plancher = Math.max(this.relief.hauteur(cible.x, cible.z), 0) + 0.05;
      if (cible.y < plancher) cible.y = plancher;
      const p = this.camera.position;
      if (p.distanceTo(cible) > 50) p.copy(cible);
      else p.lerp(cible, Math.min(1, dt * 6));
      this.regarderVers(yeux);
      return;
    }
    // Quand il marche, on revient peu à peu derrière lui (de trois quarts).
    if (enMarche) {
      const voulu = Math.PI * 0.8;
      const ecart = Math.atan2(Math.sin(voulu - s.angle), Math.cos(voulu - s.angle));
      s.angle += ecart * Math.min(1, dt * 0.6);
    }
    const a = cap + s.angle;
    const cible = new THREE.Vector3(tete.x + Math.sin(a) * s.distance, tete.y + s.hauteur, tete.z + Math.cos(a) * s.distance);
    const plancher = Math.max(this.relief.hauteur(cible.x, cible.z), 0) + 0.6;
    if (cible.y < plancher) cible.y = plancher;
    const p = this.camera.position;
    if (p.distanceTo(cible) > 200) p.copy(cible);
    else p.lerp(cible, Math.min(1, dt * 4));
    this.regarderVers(tete);
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

  /** Huttes, champs et bêtes autour de la caméra, toutes les trois secondes (ils changent lentement). */
  private async boucleCamp(): Promise<void> {
    while (this.ouverte) {
      const p = this.camera.position;
      const cx = Math.floor(p.x / this.relief.m);
      const cz = Math.floor(p.z / this.relief.m);
      const rayon = Math.min(8, Math.max(2, Math.ceil(this.camp.portee / this.relief.m)));
      const camp = await connexion.invoke<CampJson>("Camp", cx, cz, rayon).catch(() => null);
      if (camp && this.ouverte) this.camp.recevoir(camp);
      // Plus souvent quand on se déplace vite : le camp suit la caméra.
      for (let k = 0; k < 30 && this.ouverte; k++) {
        await new Promise((r) => window.setTimeout(r, 100));
        if (Math.hypot(this.camera.position.x - p.x, this.camera.position.z - p.z) > this.relief.m) break;
      }
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
      this.sol.invalider();
    } else if (premiere) {
      this.terrain.invalider();
      this.nature.vider();
      this.sol.vider();
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
      // L'allure choisie multiplie une vitesse qui croît avec la hauteur : ×1 = 1,2 × la hauteur par seconde (au moins 10 m/s).
      const v = Math.max(10, haut * 1.2) * ALLURES[this.allure] * (rapide ? 6 : 1);
      const dir = new THREE.Vector3(-Math.sin(this.lacet) * Math.cos(this.tangage), Math.sin(this.tangage), -Math.cos(this.lacet) * Math.cos(this.tangage));
      const droite = new THREE.Vector3(Math.cos(this.lacet), 0, -Math.sin(this.lacet));
      p.addScaledVector(dir, avant * v * dt).addScaledVector(droite, cote * v * dt);
      const monte = (k.has("KeyE") || k.has("Space") || k.has("PageUp") ? 1 : 0) - (k.has("KeyQ") || k.has("PageDown") ? 1 : 0);
      p.y += monte * v * dt;
      const plancher = Math.max(this.relief.hauteur(p.x, p.z), 0) + 1.5;
      if (p.y < plancher) p.y = plancher;
      p.y = Math.min(p.y, 60000);
      // On ne sort pas de la carte (de plus de quelques kilomètres).
      p.x = Math.max(-5000, Math.min(this.relief.largeurM + 5000, p.x));
      p.z = Math.max(-5000, Math.min(this.relief.hauteurM + 5000, p.z));
    } else {
      // À pied : le pas d'un marcheur à ×1 ; l'Entité peut aller bien plus vite qu'un humain.
      const v = (rapide ? 4.5 : 1.4) * ALLURES[this.allure];
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
    this.rayons.regler(this.camera, this.ciel.direction, this.ciel.couleurSoleil, this.ciel.brume);
    this.vent.regler(secondes);
    // L'œil s'adapte à la lumière en une ou deux secondes (plus lentement vers le noir).
    const vise = this.ciel.expositionVisee;
    const vitesse = vise > this.exposition ? 0.8 : 1.6;
    this.exposition *= Math.pow(vise / this.exposition, Math.min(1, dt * vitesse));
    this.uExposition.value = this.exposition;
    if (!this.chaine) this.renderer.toneMappingExposure = this.exposition;
    this.ciel.exposition = this.exposition;
    this.foule.exposition = this.exposition;
    if (maintenant - this.fondA > 1000) {
      this.fondA = maintenant;
      this.ciel.fond = this.fondAlentour(p);
    }
    this.terrain.mettreAJour(p, 7);
    this.nature.mettreAJour(p, 3);
    this.sol.mettreAJour(p, 2);
    this.foule.interlocuteur = echange.humain;
    this.foule.animer(dt, secondes, p);
    this.suivreImage(dt);
    this.proches.mettreAJour(p, maintenant / 1000);
    this.camp.animer(p, secondes, maintenant / 1000);
    this.feux.animer(maintenant / 1000, p, 1 - this.ciel.clarte, this.exposition);
    this.mer.position.set(p.x, 0, p.z);
    this.eau.origineMer.value.set(p.x, p.z);
    this.eau.mettreAJour(p);
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
    if (++this.imagesOmbres % 30 === 1) this.ombrer();
    if (this.aoNoeud) {
      // L'occlusion se mesure à l'échelle de ce qu'on regarde : quelques millimètres sur un visage, un mètre sur un paysage.
      const haut = p.y - Math.max(0, this.relief.hauteur(p.x, p.z));
      const r = this.suivi ? Math.max(0.03, Math.min(0.8, this.suivi.distance * 0.12)) : Math.max(0.3, Math.min(2.5, haut * 0.25));
      this.aoNoeud.radius.value = r;
    }
    if (this.chaine) this.chaine.render();
    else this.renderer.render(this.scene, this.camera);
    this.montrerNoms();
    if (maintenant - this.miniA > 100) {
      this.miniA = maintenant;
      this.mini.dessiner({
        x: p.x, z: p.z, lacet: this.lacet, champ: (this.camera.fov * this.camera.aspect * Math.PI) / 360,
        portee: Math.max(1500, (p.y - Math.max(0, this.relief.hauteur(p.x, p.z))) * 4), vivants: this.ctx.vivants(),
      });
    }
    this.images++;
    this.durees.push(performance.now() - maintenant);
    if (maintenant - this.imagesDepuis > 1000) {
      this.parSeconde = (this.images * 1000) / (maintenant - this.imagesDepuis);
      this.images = 0;
      this.imagesDepuis = maintenant;
      this.majEtat();
      this.majPerf();
    }
  }

  /** Bas du relief alentour (le fond de la vallée où l'on est), pour la brume des fonds. */
  private fondAlentour(p: THREE.Vector3): number {
    let bas = Math.max(0, this.relief.hauteur(p.x, p.z));
    for (const r of [600, 1500, 3000]) {
      for (let k = 0; k < 12; k++) {
        const a = (k / 12) * Math.PI * 2;
        bas = Math.min(bas, Math.max(0, this.relief.hauteur(p.x + Math.cos(a) * r, p.z + Math.sin(a) * r)));
      }
    }
    return bas;
  }

  /** Compteur d'images (touche I) : images par seconde, durée moyenne et plus longue du travail du processeur, appels de dessin. */
  private majPerf(): void {
    if (this.perf.hidden) {
      this.durees.length = 0;
      return;
    }
    const n = this.durees.length || 1;
    const moyenne = this.durees.reduce((a, b) => a + b, 0) / n;
    const pire = Math.max(0, ...this.durees);
    this.durees.length = 0;
    const info = this.renderer.info.render as { drawCalls?: number; calls?: number; triangles?: number };
    this.perf.textContent = t("trois.perf", {
      images: Math.round(this.parSeconde),
      moyenne: nombre(moyenne, 1),
      pire: nombre(pire, 1),
      appels: nombre(info.drawCalls ?? info.calls ?? 0),
      triangles: nombre(Math.round((info.triangles ?? 0) / 1000)),
      qualite: t(`trois.qualite.${this.qualite}`),
      moteur: this.moteur,
    });
  }

  private ecran(p: THREE.Vector3): { x: number; y: number } | null {
    const v = p.clone().project(this.camera);
    if (v.z < -1 || v.z > 1) return null;
    const r = this.renderer.domElement.getBoundingClientRect();
    return { x: r.left + ((v.x + 1) / 2) * r.width, y: r.top + ((1 - v.y) / 2) * r.height };
  }

  /**
   * L'humain sous la souris (0.41.2) : tout son corps compte, des pieds à la tête, avec une marge qui suit sa taille à l'écran ;
   * entre plusieurs, le plus proche de la caméra.
   */
  private humainSous(x: number, y: number): number | null {
    let meilleur: number | null = null;
    let profondeur = Infinity;
    const avant = new THREE.Vector3();
    this.camera.getWorldDirection(avant);
    for (const h of this.foule.tous()) {
      const versLui = h.tete.clone().sub(this.camera.position);
      const loin = versLui.length();
      if (versLui.dot(avant) <= 0 || loin > 600) continue;
      const a = this.ecran(h.tete);
      const b = this.ecran(h.pied);
      if (!a || !b) continue;
      // Distance du clic au segment tête-pieds, à l'écran.
      const vx = b.x - a.x;
      const vy = b.y - a.y;
      const l2 = vx * vx + vy * vy;
      const t = l2 > 0 ? Math.max(0, Math.min(1, ((x - a.x) * vx + (y - a.y) * vy) / l2)) : 0;
      const d = Math.hypot(x - (a.x + t * vx), y - (a.y + t * vy));
      const marge = Math.max(12, Math.sqrt(l2) * 0.22);
      if (d < marge && loin < profondeur) {
        profondeur = loin;
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
      .filter(({ h, d }) => d < 45 && d > 2.5 && h.tete.clone().sub(this.camera.position).dot(avant) > 0)
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
    const portrait = this.hud.querySelector<HTMLButtonElement>('[data-action="portrait"]')!;
    const cible = this.suivi?.id ?? id;
    portrait.hidden = !cible;
    portrait.classList.toggle("actif", this.suivi?.portrait === true);
    portrait.textContent = this.suivi?.portrait ? t("trois.portraitArreter") : cible ? t("trois.portraitDe", { nom: this.ctx.nom(cible) }) : "";
    const suivre = this.hud.querySelector<HTMLButtonElement>('[data-action="suivre"]')!;
    suivre.hidden = !id && !this.suivi;
    suivre.classList.toggle("actif", this.suivi !== null && !this.suivi.portrait);
    suivre.textContent = this.suivi ? t("trois.suiviArreter", { nom: this.ctx.nom(this.suivi.id) }) : id ? t("trois.suivre", { nom: this.ctx.nom(id) }) : "";
  }
}
