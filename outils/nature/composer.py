#!/usr/bin/env python3
"""
Textures de la nature de la vue 3D (0.41.5) : sol, écorces et cartes de feuillage, faites de photographies CC0.

Sources (toutes CC0 1.0) :
- Poly Haven (https://polyhaven.com/license : « Our assets are all licensed as CC0 ») : sols forrest_ground_01, leafy_grass,
  mud_forest, coast_sand_01, aerial_rocks_02, mossy_rock, snow_02 ; écorces tree_bark_03, palm_bark ; fougère fern_02.
  Chaque fichier est vérifié par son empreinte md5, celle que donne l'API de Poly Haven (api.polyhaven.com/files/<nom>) ; les
  cartes de normales de coast_sand_01, aerial_rocks_02 et snow_02 n'existent qu'en copie recompressée : on garde leur sha256.
- ambientCG (https://docs.ambientcg.com/license/ : « All ambientCG assets are provided under the Creative Commons CC0 1.0
  Universal License ») : feuilles LeafSet022, LeafSet023, LeafSet024, Foliage001, Foliage008 (1K-PNG, Color et Opacity).

Usage : python3 outils/nature/composer.py <dossier des sources> client/public/nature
Le script refuse tout fichier dont l'empreinte ne correspond pas.
"""
import hashlib
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

MD5 = {
    "forrest_ground_01_diff_1k.jpg": "236e7d928f5e357a194fd92de189cbe4",
    "forrest_ground_01_nor_gl_1k.jpg": "ba4265df25aea293913d004b69ef9ab0",
    "leafy_grass_diff_1k.jpg": "0dbc071e91d6905edfcfbe8eb785a1ab",
    "leafy_grass_nor_gl_1k.jpg": "8279e096e204ea326d57a318869f99df",
    "mud_forest_diff_1k.jpg": "d3ea8fc962bbf54d66fc5723cd0096c1",
    "mud_forest_nor_gl_1k.jpg": "6e2f0d15fdf402ca5221f63d0bfbeb60",
    "mossy_rock_diff_1k.jpg": "a57fbbf55269eb64f8d40708fe3af26c",
    "mossy_rock_nor_gl_1k.jpg": "59370f9b3d068271c2bccfb414dc325b",
    "tree_bark_03_diff_1k.jpg": "7f09750a7529c52977f79dae90f1d713",
    "tree_bark_03_nor_gl_1k.jpg": "1cd06150599a6202807f71f34ce8fe41",
    "palm_bark_diff_1k.jpg": "89e6474313f1547e3c12c4b68410ee39",
    "palm_bark_nor_gl_1k.jpg": "47c13a26ef585fbb0d93f5593f8f711f",
    "fern_02_diff_1k.jpg": "82c18a6cfd5ae91c37c0e078f591181c",
    "fern_02_alpha_1k.png": "1421e5e36946c23a7ddcc907de89025c",
    "aerial_rocks_02_diff_1k.jpg": "b5795e9b9e43d718ace73448c97d57ab",
    "coast_sand_01_diff_1k.jpg": "a1e243fc8635806381505c7dc44b192a",
    "snow_02_diff_1k.jpg": "fc54766c6b36ff298699115a619d440b",
}
# Copies (pas d'empreinte officielle disponible) : sha256 des fichiers utilisés.
SHA256 = {
    "aerial_rocks_02_nor_gl_1k.jpg": None,
    "coast_sand_01_nor_gl_1k.jpg": None,
    "snow_02_nor_gl_1k.jpg": None,
    "LeafSet022_Color.png": None, "LeafSet022_Opacity.png": None,
    "LeafSet023_Color.png": None, "LeafSet023_Opacity.png": None,
    "LeafSet024_Color.png": None, "LeafSet024_Opacity.png": None,
    "Foliage001_Color.png": None, "Foliage001_Opacity.png": None,
    "Foliage008_Color.png": None, "Foliage008_Opacity.png": None,
}
SHA_FICHIER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "empreintes.json")

# Couches du sol, dans l'ordre des couches de la vue 3D (sol3d / tuiles).
SOLS = [
    ("forrest_ground_01", "sous-bois"),
    ("leafy_grass", "litiere"),
    ("mud_forest", "boue"),
    ("coast_sand_01", "sable"),
    ("aerial_rocks_02", "roche"),
    ("mossy_rock", "roche moussue"),
    ("snow_02", "neige"),
]
ECORCES = ["tree_bark_03", "palm_bark"]
COTE = 512

rng = np.random.default_rng(41)


def verifier(src: str) -> None:
    attendus = {}
    if os.path.exists(SHA_FICHIER):
        attendus = json.load(open(SHA_FICHIER))
    nouveaux = {}
    for nom, md5 in MD5.items():
        donnees = open(os.path.join(src, nom), "rb").read()
        if hashlib.md5(donnees).hexdigest() != md5:
            sys.exit(f"Refusé : {nom} n'a pas l'empreinte de Poly Haven")
    for nom in SHA256:
        h = hashlib.sha256(open(os.path.join(src, nom), "rb").read()).hexdigest()
        if nom in attendus and attendus[nom] != h:
            sys.exit(f"Refusé : {nom} a changé ({h})")
        nouveaux[nom] = h
    if not attendus:
        json.dump(nouveaux, open(SHA_FICHIER, "w"), indent=1, sort_keys=True)


def lire(src, nom, mode="RGB"):
    return np.asarray(Image.open(os.path.join(src, nom)).convert(mode), dtype=np.float32) / 255.0


def aplatir(rgb, rayon=48):
    """Ôte les grandes variations de clarté (la répétition se voit moins) en gardant la couleur moyenne."""
    flou = ndimage.gaussian_filter(rgb, sigma=(rayon, rayon, 0), mode="wrap")
    moyenne = rgb.reshape(-1, 3).mean(0)
    return np.clip(rgb / np.maximum(flou, 1e-3) * moyenne, 0, 1), moyenne


def reduire(img, cote=COTE):
    mode = "RGBA" if img.shape[2] == 4 else "RGB"
    im = Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8), mode)
    return np.asarray(im.resize((cote, cote), Image.LANCZOS), dtype=np.float32) / 255.0


def lineaire(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def bande(couches, chemin, qualite=86):
    img = np.concatenate(couches, axis=0)
    mode = "RGBA" if img.shape[2] == 4 else "RGB"
    Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8), mode).save(chemin, "WEBP", quality=qualite, method=6)


def sols(src, dest):
    couleurs, normales, moyennes = [], [], []
    for nom, _ in SOLS:
        rgb, moy = aplatir(lire(src, f"{nom}_diff_1k.jpg"))
        lum = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        # Hauteur : la clarté locale (les creux sont sombres), pour mêler les couches par leurs reliefs.
        haut = lum - ndimage.gaussian_filter(lum, 12, mode="wrap")
        haut = np.clip(0.5 + haut / (4 * haut.std() + 1e-6), 0, 1)
        # Rangée dans 0,5 à 1 : le navigateur garde la couleur exacte sous une opacité forte.
        haut = 0.5 + 0.5 * haut
        couleurs.append(reduire(np.dstack([rgb, haut])))
        normales.append(reduire(lire(src, f"{nom}_nor_gl_1k.jpg")))
        moyennes.append([round(float(v), 4) for v in lineaire(moy)])
    bande(couleurs, os.path.join(dest, "sols-couleur.webp"), 82)
    bande(normales, os.path.join(dest, "sols-normales.webp"), 90)
    return moyennes


def ecorces(src, dest):
    c, n, moyennes = [], [], []
    for nom in ECORCES:
        rgb, moy = aplatir(lire(src, f"{nom}_diff_1k.jpg"), 64)
        c.append(reduire(rgb, 256))
        n.append(reduire(lire(src, f"{nom}_nor_gl_1k.jpg"), 256))
        moyennes.append([round(float(v), 4) for v in lineaire(moy)])
    bande(c, os.path.join(dest, "ecorces-couleur.webp"))
    bande(n, os.path.join(dest, "ecorces-normales.webp"), 90)
    return moyennes


# ---------- Feuilles et cartes de feuillage ----------

def feuilles(src, nom, sens):
    """Les feuilles d'une planche ambientCG, chacune base en bas, pointe en haut (RGBA, 0 à 1)."""
    rgb = lire(src, f"{nom}_Color.png")
    a = lire(src, f"{nom}_Opacity.png", "L")
    lab, k = ndimage.label(a > 0.5)
    liste = []
    for i, o in enumerate(ndimage.find_objects(lab)):
        if (lab[o] == i + 1).sum() < 2000:
            continue
        f = np.dstack([rgb[o], a[o] * (lab[o] == i + 1)])
        if sens == "gauche":  # base à gauche : on tourne d'un quart de tour
            f = np.rot90(f, 1)
        liste.append(f)
    return liste


def fronde_fougere(src):
    rgb = lire(src, "fern_02_diff_1k.jpg")
    a = np.asarray(Image.open(os.path.join(src, "fern_02_alpha_1k.png")), dtype=np.float32) / 65535.0
    lab, _ = ndimage.label(a > 0.5)
    frondes = []
    for i, o in enumerate(ndimage.find_objects(lab)):
        if (lab[o] == i + 1).sum() < 15000:
            continue
        frondes.append(np.dstack([rgb[o], a[o] * ndimage.binary_dilation(lab[o] == i + 1, iterations=2)]))
    return frondes


def coller(toile, img, cx, cy, hauteur, angle=0.0, teinte=(1, 1, 1), largeur=1.0):
    """Colle une feuille (base en bas) : sa base au point (cx, cy) de la toile, tournée de `angle` degrés, haute de `hauteur` px."""
    h, w = img.shape[:2]
    l = max(1, int(round(w * hauteur / h * largeur)))
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGBA").resize((l, int(hauteur)), Image.LANCZOS)
    # On tourne autour de la base : on pose l'image dans un carré centré sur la base.
    c = int(hauteur) * 2 + l
    carre = Image.new("RGBA", (c, c), (0, 0, 0, 0))
    carre.paste(im, (c // 2 - l // 2, c // 2 - int(hauteur)))
    carre = carre.rotate(-angle, resample=Image.BICUBIC, center=(c // 2, c // 2))
    f = np.asarray(carre, dtype=np.float32) / 255.0
    f[..., :3] *= np.array(teinte, dtype=np.float32)
    x0, y0 = int(cx) - c // 2, int(cy) - c // 2
    H, W = toile.shape[:2]
    xa, ya, xb, yb = max(0, x0), max(0, y0), min(W, x0 + c), min(H, y0 + c)
    if xa >= xb or ya >= yb:
        return
    s = f[ya - y0:yb - y0, xa - x0:xb - x0]
    d = toile[ya:yb, xa:xb]
    al = s[..., 3:4]
    d[..., :3] = s[..., :3] * al + d[..., :3] * (1 - al)
    d[..., 3:4] = al + d[..., 3:4] * (1 - al)


def tige(toile, points, epaisseurs, couleur):
    """Une tige le long d'une ligne brisée (disques successifs)."""
    H, W = toile.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W]
    for (x0, y0), (x1, y1), (e0, e1) in zip(points, points[1:], zip(epaisseurs, epaisseurs[1:])):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) / 2) + 1
        for t in np.linspace(0, 1, n):
            x, y, e = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, e0 + (e1 - e0) * t
            r = int(e) + 2
            ya, yb, xa, xb = max(0, int(y) - r), min(H, int(y) + r + 1), max(0, int(x) - r), min(W, int(x) + r + 1)
            d = np.hypot(xx[ya:yb, xa:xb] - x, yy[ya:yb, xa:xb] - y)
            m = np.clip(e - d + 0.5, 0, 1)[..., None]
            ombre = 1 - 0.35 * np.clip((xx[ya:yb, xa:xb] - x) / max(e, 1), -1, 1)[..., None]  # un peu de volume
            c = np.array(couleur, dtype=np.float32) * ombre
            toile[ya:yb, xa:xb, :3] = c * m + toile[ya:yb, xa:xb, :3] * (1 - m)
            toile[ya:yb, xa:xb, 3:4] = np.maximum(toile[ya:yb, xa:xb, 3:4], m)


def toile(l, h):
    return np.zeros((h, l, 4), dtype=np.float32)


def carte_fougere(frondes):
    t = toile(256, 1024)
    f = frondes[0]
    coller(t, f, 128, 1020, 1010)
    return t


def carte_cycas(fines):
    """Fronde raide de cycas : un rachis, des folioles étroites de part et d'autre, plus courtes vers le bout."""
    t = toile(256, 1024)
    tige(t, [(128, 1020), (128, 30)], [9, 4], (0.30, 0.33, 0.17))
    n = 46
    for k in range(n):
        v = k / n
        y = 960 - v * 920
        lg = 124 * (1 - 0.7 * v ** 1.5) * (0.5 if v < 0.05 else 1)
        f = fines[k % len(fines)]
        for cote in (-1, 1):
            c = 0.55 + 0.12 * rng.random()
            coller(t, f, 128 + cote * 4, y, lg, cote * (62 - 18 * v), (c * 0.75, c, c * 0.6), 2.8)
    return t


def carte_preles(fines):
    """Prêles : tiges vertes à nœuds sombres, verticilles de rameaux fins (tiges coupées dans les photos de feuilles)."""
    t = toile(512, 1024)
    for k in range(6):
        x = 60 + k * 78 + rng.integers(-15, 15)
        haut = 1024 - rng.integers(80, 360)
        pts = [(x, 1023), (x + rng.integers(-20, 20), haut)]
        tige(t, pts, [9, 5], (0.42, 0.56, 0.26))
        # Nœuds et verticilles.
        y = 1000
        pas = rng.integers(55, 80)
        while y > haut + 30:
            u = (1023 - y) / max(1, 1023 - haut)
            xn = pts[0][0] + (pts[1][0] - pts[0][0]) * u
            tige(t, [(xn - 9, y), (xn + 9, y)], [2.5, 2.5], (0.16, 0.20, 0.10))
            if u > 0.2:
                for c in (-1, 1):
                    for j in range(3):
                        f = fines[(k + j) % len(fines)]
                        coller(t, f, xn, y - 2, 60 + 40 * (1 - u), c * (55 + 15 * j), (0.8, 0.9, 0.7))
            y -= pas
    return t


def carte_feuillu(larges):
    """Rameau d'un feuillu du Crétacé (magnolia, laurier) : une branche, des feuilles alternes."""
    t = toile(1024, 1024)
    pts = [(512, 1023), (500, 760), (520, 420), (512, 80)]
    tige(t, pts, [14, 10, 7, 3], (0.30, 0.24, 0.17))
    for cote, x0, y0, x1, y1 in [(-1, 505, 700, 260, 520), (1, 516, 560, 790, 360), (-1, 518, 400, 300, 200)]:
        tige(t, [(x0, y0), (x1, y1)], [6, 2], (0.30, 0.24, 0.17))
    for k in range(46):
        v = 0.25 + 0.75 * rng.random()
        branche = rng.integers(0, 4)
        if branche == 0:
            x, y = 512 + rng.integers(-12, 12), 1000 - v * 900
        else:
            seg = [((505, 700), (260, 520)), ((516, 560), (790, 360)), ((518, 400), (300, 200))][branche - 1]
            x = seg[0][0] + (seg[1][0] - seg[0][0]) * v
            y = seg[0][1] + (seg[1][1] - seg[0][1]) * v
        f = larges[rng.integers(0, len(larges))]
        a = rng.uniform(-80, 80)
        c = 0.55 + 0.25 * rng.random()
        coller(t, f, x, y, rng.uniform(150, 210), a, (c * 0.7, c * (0.85 + 0.1 * rng.random()), c * 0.55))
    return t


def carte_araucaria(larges):
    """Rameau d'araucaria : une corde de petites feuilles écailleuses serrées, base à gauche, bout à droite (deux rameaux)."""
    t = toile(1024, 512)
    for b in range(2):
        y0 = 128 + 256 * b
        courbe = lambda x: y0 + 30 * np.sin(x / 1000 * np.pi) * (1 - 2 * b) * 0.6 - 0.02 * x
        pts = [(x, courbe(x)) for x in range(0, 1011, 50)]
        tige(t, pts, list(np.linspace(9, 4, len(pts))), (0.30, 0.25, 0.16))
        for k in range(900):
            u = (k + rng.random()) / 900
            x = u * 1000
            ep = 26 * (1 - 0.5 * u) + 6
            y = courbe(x) + rng.uniform(-ep, ep) * 0.45
            f = larges[k % len(larges)]
            cote = 1 if rng.random() < 0.5 else -1
            ang = 90 - cote * rng.uniform(20, 70)  # couchées vers le bout du rameau
            c = 0.32 + 0.22 * rng.random()
            coller(t, f, x, y, ep * rng.uniform(0.9, 1.3), ang, (c * 0.75, c, c * 0.55), 0.55)
    return t


def carte_buisson(larges):
    t = toile(512, 512)
    for k in range(40):
        a = rng.uniform(-100, 100)
        r = rng.uniform(0, 160)
        x = 256 + np.sin(np.radians(a)) * r * 0.8
        y = 500 - np.cos(np.radians(a)) * r * 1.4 - 30
        c = 0.7 + 0.35 * rng.random()
        coller(t, larges[k % len(larges)], x, y, rng.uniform(90, 140), a + rng.uniform(-30, 30), (c, c, c * 0.85))
    return t


def carte_litiere(larges):
    """Feuilles mortes vues d'en haut : brunes, rousses, grises."""
    t = toile(512, 512)
    teintes = [(0.75, 0.5, 0.28), (0.62, 0.42, 0.25), (0.85, 0.6, 0.3), (0.5, 0.42, 0.33), (0.7, 0.45, 0.22)]
    for k in range(34):
        f = larges[k % len(larges)].copy()
        g = f[..., :3].mean(2, keepdims=True)
        f[..., :3] = g * 1.6
        coller(t, f, rng.uniform(40, 472), rng.uniform(60, 500), rng.uniform(70, 110), rng.uniform(0, 360), teintes[k % len(teintes)])
    return t


def deborder(img):
    """Prolonge la couleur sous les parties transparentes (pas de liseré sombre une fois réduit)."""
    a = img[..., 3:4]
    s = 1.0
    rgb = img[..., :3] * a
    poids = a.copy()
    out = img.copy()
    for sigma in (2, 6, 18, 48):
        num = ndimage.gaussian_filter(rgb, (sigma, sigma, 0))
        den = ndimage.gaussian_filter(poids, (sigma, sigma, 0))
        c = num / np.maximum(den, 1e-4)
        vide = (out[..., 3:4] < 0.01) & (den > 1e-3)
        out[..., :3] = np.where(vide & (out[..., 3:4] < 0.5), np.where(a > 0.01, out[..., :3], c), out[..., :3])
    out[..., :3] = np.where(a > 0.01, img[..., :3], out[..., :3])
    return out


def atlas(src, dest):
    larges = feuilles(src, "LeafSet022", "bas") + feuilles(src, "LeafSet024", "bas") + feuilles(src, "LeafSet023", "gauche")
    fines = feuilles(src, "Foliage008", "gauche")
    brins = feuilles(src, "Foliage001", "bas")
    frondes = fronde_fougere(src)
    a = toile(2048, 2048)
    places = {
        "fougere": (0, 0, carte_fougere(frondes)),
        "cycas": (256, 0, carte_cycas(fines)),
        "preles": (512, 0, carte_preles(brins)),
        "feuillu": (1024, 0, carte_feuillu(larges)),
        "araucaria": (0, 1024, carte_araucaria(larges)),
        "buisson": (1024, 1024, carte_buisson(larges)),
        "litiere": (1536, 1024, carte_litiere(larges)),
        "fougere2": (0, 1536, carte_fougere(frondes[1:])),
        "fougere3": (256, 1536, carte_fougere(frondes[2:])),
    }
    zones = {}
    for nom, (x, y, img) in places.items():
        h, w = img.shape[:2]
        if nom.startswith("fougere") and h > 512 and y == 1536:
            img = np.asarray(Image.fromarray((img * 255).astype(np.uint8), "RGBA").resize((128, 512), Image.LANCZOS), dtype=np.float32) / 255
            h, w = img.shape[:2]
        a[y:y + h, x:x + w] = img
        zones[nom] = [x / 2048, y / 2048, w / 2048, h / 2048]
    a = deborder(a)
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(dest, "feuillages.webp"), "WEBP", quality=88, method=6)
    return zones


def main():
    src, dest = sys.argv[1], sys.argv[2]
    os.makedirs(dest, exist_ok=True)
    verifier(src)
    if len(sys.argv) > 3 and sys.argv[3] == "--feuillages":
        ancien = json.load(open(os.path.join(dest, "nature.json")))
        m, e = ancien["sols"], ancien["ecorces"]
    else:
        m = sols(src, dest)
        e = ecorces(src, dest)
    z = atlas(src, dest)
    json.dump({"sols": m, "ecorces": e, "zones": z}, open(os.path.join(dest, "nature.json"), "w"), indent=1)
    print(json.dumps({"sols": m, "ecorces": e}))


if __name__ == "__main__":
    main()
