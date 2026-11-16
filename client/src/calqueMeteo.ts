import { Container, Graphics, Sprite, Text, Texture, TilingSprite } from "pixi.js";
import { calqueLisse } from "./carte";
import { t } from "./langue";
import { degatsDe, etatA, lireAverses, regionDe, type Averse, type EnCoursJson, type EtatTemps, type TempsJson, type ZoneJson } from "./meteo";
export type { EnCoursJson, ZoneJson } from "./meteo";

/** Couleur des terres touchées : brûlées, sous la lave ou la cendre, sous l'eau, ravagées. */
function couleurCase(z: ZoneJson, degat: number): [number, number, number, number] {
  const d = degat / 255;
  switch (z.type) {
    case "incendie":
      return [36, 24, 16, Math.round(70 + 170 * d)];
    case "eruption":
      return z.active ? [235, 70, 25, 210] : [128, 124, 118, Math.round(60 + 170 * d)];
    case "crue":
    case "tsunami":
      return z.active ? [70, 160, 230, 170] : [125, 100, 62, Math.round(40 + 120 * d)];
    case "impact":
      return [70, 58, 46, Math.round(80 + 170 * d)];
    default:
      return [200, 180, 140, Math.round(40 + 120 * d)];
  }
}

/**
 * Calques « Temps » et « Catastrophes » de la carte (0.43.0, propositions C1 et F1) : pluie, neige et orages du moment par
 * région, aux bords adoucis, avec des traits de pluie qui défilent ; feux qui avancent et leur fumée poussée par le vent, terres
 * brûlées qui s'éclaircissent en reverdissant, eaux des crues, lave, cendres, ondes d'un séisme, tempête qui tourne ; bandeau
 * pour ce qui couvre tout le continent (froid, comète, éclipse…).
 */
export class CalqueMeteo {
  readonly racine = new Container();
  readonly temps = new Container();
  readonly catastrophes = new Container();
  private readonly pluieSprite = new Sprite();
  /** Traits de pluie : un pavé par région où il pleut (pas de masque : un masque de sprite casse le rendu au fort zoom). */
  private readonly traits = new Container();
  private readonly paves: TilingSprite[] = [];
  private readonly texTraits = texteTraits();
  private readonly eclairs = new Container();
  private readonly terres = new Container();
  private readonly fronts = new Graphics();
  private readonly fumees = new Graphics();
  private readonly mouvants = new Graphics();
  private readonly bandeau: HTMLElement;
  private json: TempsJson | null = null;
  private averses: Averse[] = [];
  private etat: EtatTemps | null = null;
  private enCours: EnCoursJson | null = null;
  private cle = "";
  private zonesCases = new Map<number, Set<number>>();
  private largeur = 0;
  private hauteur = 0;
  private temps0 = performance.now();

  constructor(hote: HTMLElement) {
    this.temps.addChild(this.pluieSprite, this.traits, this.eclairs);
    this.catastrophes.addChild(this.terres, this.fumees, this.fronts, this.mouvants);
    this.racine.addChild(this.catastrophes, this.temps);
    this.bandeau = document.createElement("div");
    this.bandeau.className = "bandeau-continent";
    this.bandeau.hidden = true;
    hote.append(this.bandeau);
  }

  /** Taille de la carte en cases (nouvelle carte : tout est effacé). */
  dimensionner(largeur: number, hauteur: number): void {
    this.largeur = largeur;
    this.hauteur = hauteur;
    this.cle = "";
    this.terres.removeChildren().forEach((c) => c.destroy({ texture: true }));
  }

  /** Nouvelles averses (lues du serveur). */
  definirTemps(json: TempsJson | null): void {
    this.json = json;
    this.averses = json ? lireAverses(json) : [];
  }

  /** Le ciel à l'instant `secondes` du monde. */
  regler(secondes: number): void {
    const j = this.json;
    if (!j) {
      this.etat = null;
      this.pluieSprite.visible = this.traits.visible = false;
      return;
    }
    const e = (this.etat = etatA(j, this.averses, secondes));
    const canvas = document.createElement("canvas");
    canvas.width = e.regionsX;
    canvas.height = e.regionsY;
    const ctx = canvas.getContext("2d")!;
    const img = ctx.createImageData(e.regionsX, e.regionsY);
    let rien = true;
    let k = 0;
    for (let r = 0; r < e.pluie.length; r++) {
      const p = e.pluie[r], n = e.neige[r], o = e.orage[r];
      const f = Math.max(p, n);
      if (f <= 0.01) continue;
      rien = false;
      const c = n > p ? [240, 246, 255] : o > 0 ? [96, 80, 190] : [58, 120, 214];
      img.data.set([c[0], c[1], c[2], Math.round(255 * (0.18 + 0.32 * f))], r * 4);
      if (n > p) continue;
      // Les traits : un pavé de la taille de la région, plus ou moins marqué selon la force.
      let t = this.paves[k];
      if (!t) {
        t = new TilingSprite({ texture: this.texTraits, width: 1, height: 1 });
        // Une case de la carte = 4 pixels de la texture des traits.
        t.tileScale.set(0.25);
        this.paves.push(t);
        this.traits.addChild(t);
      }
      const x0 = (r % e.regionsX) * e.tailleRegion, y0 = Math.floor(r / e.regionsX) * e.tailleRegion;
      t.position.set(x0, y0);
      t.width = Math.max(1, Math.min(e.tailleRegion, this.largeur - x0));
      t.height = Math.max(1, Math.min(e.tailleRegion, this.hauteur - y0));
      t.alpha = 0.55 * Math.min(1, 0.3 + f);
      t.visible = true;
      k++;
    }
    for (let j = k; j < this.paves.length; j++) this.paves[j].visible = false;
    this.eclairs.removeChildren().forEach((c) => c.destroy());
    if (rien) {
      this.pluieSprite.visible = this.traits.visible = false;
      return;
    }
    ctx.putImageData(img, 0, 0);
    this.remplacer(this.pluieSprite, canvas, e.tailleRegion);
    this.pluieSprite.visible = this.traits.visible = true;
    // Un éclair au cœur de chaque orage (ou au milieu de la région).
    for (const o of e.orages) {
      const x = o.x >= 0 ? o.x + 0.5 : (o.region % e.regionsX + 0.5) * e.tailleRegion;
      const y = o.y >= 0 ? o.y + 0.5 : (Math.floor(o.region / e.regionsX) + 0.5) * e.tailleRegion;
      const s = new Text({ text: "⚡", style: { fontSize: 64, fill: 0xfff27a } });
      s.anchor.set(0.5);
      s.scale.set(0.09);
      s.position.set(x, y);
      this.eclairs.addChild(s);
    }
  }

  private remplacer(sprite: Sprite, canvas: HTMLCanvasElement, echelle: number): void {
    const ancienne = sprite.texture;
    const tex = Texture.from(canvas);
    tex.source.scaleMode = "linear";
    sprite.texture = tex;
    // Le centre de chaque pixel au centre de sa région.
    sprite.scale.set(echelle);
    if (ancienne !== Texture.EMPTY) ancienne.destroy(true);
  }

  /** Catastrophes en cours (lues du serveur). Les terres ne sont redessinées que si elles ont changé. */
  definirEnCours(json: EnCoursJson | null): void {
    this.enCours = json;
    this.zonesCases = new Map((json?.zones ?? []).map((z) => [z.id, new Set([...z.cases, ...z.front])]));
    const noms = json?.continent.map((c) => c.nom) ?? [];
    this.bandeau.textContent = noms.join(" · ");
    this.bandeau.hidden = noms.length === 0 || !this.catastrophes.visible;
    if (!json || this.largeur === 0) return;
    const cle = json.zones.map((z) => `${z.id}:${z.active ? 1 : 0}:${z.cases.length}:${z.degats.length}:${z.degats.slice(0, 40)}`).join("|");
    if (cle !== this.cle) {
      this.cle = cle;
      this.terres.removeChildren().forEach((c) => c.destroy({ texture: true }));
      const canvas = document.createElement("canvas");
      canvas.width = this.largeur;
      canvas.height = this.hauteur;
      const ctx = canvas.getContext("2d")!;
      const img = ctx.createImageData(this.largeur, this.hauteur);
      let vide = true;
      for (const z of json.zones) {
        const d = degatsDe(z);
        z.cases.forEach((i, k) => {
          if (i < 0 || i >= this.largeur * this.hauteur) return;
          const c = couleurCase(z, d[k] ?? 255);
          if (c[3] <= img.data[i * 4 + 3]) return;
          img.data.set(c, i * 4);
          vide = false;
        });
      }
      if (!vide) {
        ctx.putImageData(img, 0, 0);
        this.terres.addChild(calqueLisse(canvas));
      }
    }
    this.fronts.clear();
    for (const z of json.zones) {
      for (const i of z.front) this.fronts.rect(i % this.largeur + 0.1, Math.floor(i / this.largeur) + 0.1, 0.8, 0.8);
    }
    this.fronts.fill({ color: 0xffa020 });
  }

  /** Une image : le feu scintille, la fumée file, la pluie tombe, les ondes et les tempêtes bougent. */
  animer(): void {
    const s = (performance.now() - this.temps0) / 1000;
    if (this.traits.visible) {
      // Les paves défilent ensemble : les traits passent d'une région à l'autre sans couture.
      for (const t of this.paves) if (t.visible) t.tilePosition.set(-s * 6 - t.x, s * 22 - t.y);
      this.eclairs.alpha = Math.sin(s * 9) > 0.6 || Math.sin(s * 2.3) > 0.97 ? 1 : 0.35;
    }
    const json = this.enCours;
    this.fumees.clear();
    this.mouvants.clear();
    if (!json || !this.catastrophes.visible) return;
    this.fronts.alpha = 0.75 + 0.25 * Math.sin(s * 11) * Math.sin(s * 4.7);
    for (const z of json.zones) {
      if (!z.active) continue;
      if (z.type === "incendie" && z.front.length > 0) {
        // Fumée : des panaches qui partent du feu et s'étirent sous le vent.
        let mx = 0, my = 0;
        for (const i of z.front) {
          mx += i % this.largeur + 0.5;
          my += Math.floor(i / this.largeur) + 0.5;
        }
        mx /= z.front.length;
        my /= z.front.length;
        const dx = Math.cos(z.direction), dy = Math.sin(z.direction);
        const etendue = 2 + Math.sqrt(z.front.length) * 0.8;
        for (let k = 0; k < 7; k++) {
          const c = (s / 6 + k / 7) % 1;
          const d = c * etendue * 3;
          this.fumees.ellipse(mx + dx * d, my + dy * d, 0.8 + c * etendue, 0.6 + c * etendue * 0.6)
            .fill({ color: 0x8a8a88, alpha: 0.22 * (1 - c) });
        }
      } else if (z.type === "seisme") {
        for (let k = 0; k < 3; k++) {
          const c = (s / 2.5 + k / 3) % 1;
          this.mouvants.circle(z.x + 0.5, z.y + 0.5, Math.max(1, z.rayon * c)).stroke({ width: 0.6, color: 0xc8a060, alpha: 0.8 * (1 - c) });
        }
      } else if (z.type === "cyclone") {
        const r = Math.max(3, z.rayon);
        for (let bras = 0; bras < 3; bras++) {
          const a0 = s * 0.8 + (bras * Math.PI * 2) / 3;
          this.mouvants.moveTo(z.x + 0.5, z.y + 0.5);
          for (let k = 1; k <= 24; k++) {
            const f = k / 24;
            const a = a0 + f * 3.2;
            this.mouvants.lineTo(z.x + 0.5 + Math.cos(a) * r * f, z.y + 0.5 + Math.sin(a) * r * f);
          }
          this.mouvants.stroke({ width: 0.9, color: 0xb0c4de, alpha: 0.75 });
        }
      } else if (z.type === "bolide") {
        this.mouvants.star(z.x + 0.5, z.y + 0.5, 5, 1.6, 0.7).fill({ color: 0xffe080, alpha: 0.6 + 0.4 * Math.sin(s * 6) });
      } else if (z.cases.length === 0 && z.front.length === 0) {
        this.mouvants.circle(z.x + 0.5, z.y + 0.5, Math.max(1.5, z.rayon)).stroke({ width: 0.7, color: 0xffffff, alpha: 0.5 });
      }
    }
  }

  /** Montre ou cache le calque Temps. */
  set tempsVisible(oui: boolean) {
    this.temps.visible = oui;
  }

  set catastrophesVisibles(oui: boolean) {
    this.catastrophes.visible = oui;
    this.bandeau.hidden = !oui || !this.enCours || this.enCours.continent.length === 0;
  }

  /** Ce qu'il fait sur une tuile, en mots, pour le survol. */
  texteEn(x: number, y: number): string[] {
    const lignes: string[] = [];
    const e = this.etat;
    if (e && this.temps.visible) {
      const r = regionDe(e, x, y);
      if (e.orage[r] > 0.05) lignes.push(t("meteo.orage"));
      else if (e.neige[r] > 0.05) lignes.push(t("meteo.neige"));
      else if (e.pluie[r] > 0.05) lignes.push(t(e.pluie[r] > 0.6 ? "meteo.forte" : "meteo.pluie"));
    }
    return lignes;
  }

  /** Catastrophes dont la zone touche une tuile (cases touchées, ou disque pour celles qui n'en ont pas). */
  zonesEn(x: number, y: number): ZoneJson[] {
    if (!this.enCours || !this.catastrophes.visible) return [];
    const i = y * this.largeur + x;
    return this.enCours.zones.filter((z) => this.zonesCases.get(z.id)?.has(i)
      || (z.active && z.cases.length === 0 && z.front.length === 0 && Math.hypot(z.x - x, z.y - y) <= z.rayon));
  }

  get zones(): ZoneJson[] {
    return this.enCours?.zones ?? [];
  }

}

/** Traits de pluie en biais, répétés (texture de 32 pixels, une case de la carte = 4 pixels). */
function texteTraits(): Texture {
  const c = document.createElement("canvas");
  c.width = c.height = 32;
  const ctx = c.getContext("2d")!;
  ctx.strokeStyle = "rgba(220,235,255,0.9)";
  ctx.lineWidth = 1.2;
  for (const [x, y] of [[4, 2], [20, 9], [11, 18], [27, 24], [6, 27]]) {
    ctx.beginPath();
    ctx.moveTo(x, y);
    ctx.lineTo(x - 2, y + 6);
    ctx.stroke();
  }
  const tex = Texture.from(c);
  tex.source.addressMode = "repeat";
  return tex;
}
