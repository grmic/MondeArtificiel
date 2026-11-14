#!/usr/bin/env python3
"""
Convertit la base humaine MakeHuman (CC0) en un seul fichier binaire pour la vue 3D (0.41.4).

Source : https://github.com/makehumancommunity/makehuman, dossier makehuman/data (commit a8bc2d5, 26/06/2024).
Licence : chaque fichier lu ici porte la mention « explicitly released as CC0 in september 2020 » (base.obj, *.target)
ou le champ "license": "CC0" (default.mhskel, default_weights.mhw) ; le script le vérifie et refuse tout fichier sans elle.
Le code de MakeHuman (AGPL) n'est ni copié ni utilisé : seules les données (maillage, cibles, poids) sont lues.

Usage : python3 convertir.py <makehuman/data> <sortie.bin>

Format (petit-boutiste) : « MHUM », longueur de l'en-tête JSON (uint32), en-tête JSON, puis des blocs alignés sur 4 octets
dont l'en-tête donne la position (octets), le type et le nombre d'éléments.
"""
import json
import os
import struct
import sys

import numpy as np

DONNEES = sys.argv[1]
SORTIE = sys.argv[2]

MENTION = "released as CC0"


def verifier_licence(chemin: str) -> None:
    if chemin.endswith((".mhskel", ".mhw")):
        if json.load(open(chemin)).get("license") != "CC0":
            raise SystemExit(f"Licence CC0 introuvable dans {chemin}")
        return
    with open(chemin, encoding="utf-8", errors="replace") as f:
        debut = f.read(4000)
    if MENTION not in debut:
        raise SystemExit(f"Licence CC0 introuvable dans {chemin}")


def lire_obj(chemin: str):
    verifier_licence(chemin)
    v, vt, faces = [], [], []
    groupe = None
    for ligne in open(chemin):
        if ligne.startswith("v "):
            v.append([float(x) for x in ligne.split()[1:4]])
        elif ligne.startswith("vt "):
            vt.append([float(x) for x in ligne.split()[1:3]])
        elif ligne.startswith("g "):
            groupe = ligne.split()[1]
        elif ligne.startswith("f "):
            coins = [tuple(int(x) - 1 for x in t.split("/")[:2]) for t in ligne.split()[1:]]
            faces.append((groupe, coins))
    return np.array(v, dtype=np.float64), np.array(vt, dtype=np.float64), faces


def lire_cible(chemin: str, n: int) -> np.ndarray:
    verifier_licence(chemin)
    d = np.zeros((n, 3))
    for ligne in open(chemin):
        if ligne[:1].isdigit():
            p = ligne.split()
            d[int(p[0])] = [float(p[1]), float(p[2]), float(p[3])]
    return d


v, vt, faces = lire_obj(os.path.join(DONNEES, "3dobjs/base.obj"))
N = len(v)

# Groupes gardés : la peau, la langue, les dents, les cils, les yeux (pour leur centre), l'aide « jupe » (pagne), l'aide
# génitale (anatomie de l'homme adulte).
GARDES = {"body": "peau", "helper-tongue": "langue", "helper-upper-teeth": "dents", "helper-lower-teeth": "dents",
          "helper-l-eyelashes-1": "cils", "helper-l-eyelashes-2": "cils", "helper-r-eyelashes-1": "cils",
          "helper-r-eyelashes-2": "cils", "helper-skirt": "pagne", "helper-genital": "sexe",
          "helper-l-eye": "oeilG", "helper-r-eye": "oeilD"}
squelette = json.load(open(os.path.join(DONNEES, "rigs/default.mhskel")))
verifier_licence(os.path.join(DONNEES, "rigs/default.mhskel"))
verifier_licence(os.path.join(DONNEES, "rigs/default_weights.mhw"))
poids_mh = json.load(open(os.path.join(DONNEES, "rigs/default_weights.mhw")))["weights"]

# Articulations utiles (moyenne de sommets d'aide) : la tête de ces os de MakeHuman.
ARTICULATIONS = {
    "bassin": "root", "ventre": "spine03", "poitrine": "spine01", "cou": "neck01", "tete": "head",
    "brasG": "upperarm01.L", "avantBrasG": "lowerarm01.L", "mainG": "wrist.L",
    "brasD": "upperarm01.R", "avantBrasD": "lowerarm01.R", "mainD": "wrist.R",
    "cuisseG": "upperleg01.L", "jambeG": "lowerleg01.L", "piedG": "foot.L",
    "cuisseD": "upperleg01.R", "jambeD": "lowerleg01.R", "piedD": "foot.R",
}
OS = ["bassin", "ventre", "poitrine", "cou", "tete", "brasG", "avantBrasG", "mainG", "brasD", "avantBrasD", "mainD",
      "cuisseG", "jambeG", "piedG", "cuisseD", "jambeD", "piedD", "oeilG", "oeilD"]
joints = {}
for nom, os_mh in ARTICULATIONS.items():
    joints[nom] = squelette["joints"][squelette["bones"][os_mh]["head"]]
# Bout des pieds (pour poser les orteils au sol) et sommet de la tête : sommets de la peau.

# Sommets gardés et renumérotés.
garde = np.zeros(N, dtype=bool)
groupes_faces = {}
for g, coins in faces:
    if g in GARDES:
        for (iv, _) in coins:
            garde[iv] = True
for liste in joints.values():
    garde[liste] = True
ancien = np.nonzero(garde)[0]
nouveau = -np.ones(N, dtype=np.int64)
nouveau[ancien] = np.arange(len(ancien))
NG = len(ancien)

# Sommets de rendu : un par couple (sommet, coordonnée de texture), triangles par morceau.
rendu = {}
rendu_orig, rendu_uv = [], []
morceaux = {}
for g, coins in faces:
    if g not in GARDES:
        continue
    m = GARDES[g]
    ids = []
    for (iv, it) in coins:
        cle = (iv, it)
        if cle not in rendu:
            rendu[cle] = len(rendu_orig)
            rendu_orig.append(nouveau[iv])
            rendu_uv.append(vt[it] if it >= 0 else (0, 0))
        ids.append(rendu[cle])
    tri = morceaux.setdefault(m, [])
    if len(ids) == 4:
        tri += [ids[0], ids[1], ids[2], ids[0], ids[2], ids[3]]
    else:
        tri += ids[:3]
assert len(rendu_orig) < 65536

# Poids : os de MakeHuman → nos os, quatre au plus par sommet.
def os_de(nom: str) -> str:
    cote = "G" if nom.endswith(".L") else "D" if nom.endswith(".R") else ""
    base = nom.split(".")[0]
    if base in ("root", "pelvis", "spine05"):
        return "bassin"
    if base in ("spine04", "spine03"):
        return "ventre"
    if base in ("spine02", "spine01", "breast", "clavicle"):
        return "poitrine"
    if base.startswith("neck"):
        return "cou"
    if base == "eye":
        return "oeil" + cote
    if base in ("shoulder01", "upperarm01", "upperarm02"):
        return "bras" + cote
    if base.startswith("lowerarm"):
        return "avantBras" + cote
    if base in ("wrist",) or base.startswith("metacarpal") or base.startswith("finger"):
        return "main" + cote
    if base.startswith("upperleg"):
        return "cuisse" + cote
    if base.startswith("lowerleg"):
        return "jambe" + cote
    if base == "foot" or base.startswith("toe"):
        return "pied" + cote
    return "tete"  # tête, mâchoire, muscles du visage, langue


par_sommet = [dict() for _ in range(N)]
for nom, liste in poids_mh.items():
    o = OS.index(os_de(nom))
    for iv, w in liste:
        par_sommet[iv][o] = par_sommet[iv].get(o, 0) + w
# Les yeux suivent leur os ; tout sommet sans poids suit la tête.
for g, coins in faces:
    if g in ("helper-l-eye", "helper-r-eye"):
        for (iv, _) in coins:
            par_sommet[iv] = {OS.index("oeilG" if g == "helper-l-eye" else "oeilD"): 1.0}
os_idx = np.zeros((NG, 4), dtype=np.uint8)
os_poids = np.zeros((NG, 4), dtype=np.uint8)
for k, iv in enumerate(ancien):
    d = sorted(par_sommet[iv].items(), key=lambda p: -p[1])[:4] or [(OS.index("tete"), 1.0)]
    s = sum(w for _, w in d)
    q = [int(round(255 * w / s)) for _, w in d]
    q[0] += 255 - sum(q)
    for j, ((o, _), w) in enumerate(zip(d, q)):
        os_idx[k, j] = o
        os_poids[k, j] = w

# Cibles.
cibles = []


def ajouter(nom: str, delta: np.ndarray) -> None:
    d = delta[ancien]
    nz = np.nonzero(np.abs(d).max(axis=1) > 2e-5)[0]
    if len(nz) == 0:
        return
    vals = d[nz]
    echelle = float(np.abs(vals).max()) / 32767
    cibles.append((nom, nz.astype(np.uint16), np.round(vals / echelle).astype(np.int16), echelle))


T = os.path.join(DONNEES, "targets")
AGES = ["baby", "child", "young", "old"]
for genre in ["female", "male"]:
    for age in AGES:
        for muscle in ["minmuscle", "averagemuscle", "maxmuscle"]:
            for poids in ["minweight", "averageweight", "maxweight"]:
                nom = f"universal-{genre}-{age}-{muscle}-{poids}"
                ajouter(nom, lire_cible(f"{T}/macrodetails/{nom}.target", N))
        # Les trois « races » de MakeHuman, à parts égales (la simulation ne connaît que le teint).
        moyenne = sum(lire_cible(f"{T}/macrodetails/{r}-{genre}-{age}.target", N) for r in ["african", "asian", "caucasian"]) / 3
        ajouter(f"race-{genre}-{age}", moyenne)
for age in ["child", "young", "old"]:
    for cup in ["mincup", "averagecup", "maxcup"]:
        for ferme in ["minfirmness", "averagefirmness", "maxfirmness"]:
            nom = f"female-{age}-averagemuscle-averageweight-{cup}-{ferme}"
            chemin = f"{T}/breast/{nom}.target"
            if os.path.exists(chemin):
                ajouter(f"breast-{age}-{cup}-{ferme}", lire_cible(chemin, N))
# Expressions : unités moyennées sur les trois jeux.
for f in sorted(os.listdir(f"{T}/expression/units/caucasian")):
    if f.endswith(".target"):
        moyenne = sum(lire_cible(f"{T}/expression/units/{r}/{f}", N) for r in ["african", "asian", "caucasian"]) / 3
        ajouter("expr-" + f[:-7], moyenne)
# Traits du visage et du corps pilotés par la simulation.
TRAITS = [
    "head/head-scale-horiz-incr", "head/head-scale-horiz-decr", "head/head-scale-vert-incr", "head/head-scale-vert-decr",
    "head/head-square", "head/head-oval", "head/head-fat-incr", "head/head-fat-decr", "head/head-age-incr",
    "chin/chin-width-incr", "chin/chin-width-decr", "chin/chin-prominent-incr", "chin/chin-prominent-decr",
    "cheek/l-cheek-bones-incr", "cheek/r-cheek-bones-incr", "cheek/l-cheek-bones-decr", "cheek/r-cheek-bones-decr",
    "cheek/l-cheek-volume-decr", "cheek/r-cheek-volume-decr", "cheek/l-cheek-volume-incr", "cheek/r-cheek-volume-incr",
    "eyes/l-eye-trans-in", "eyes/r-eye-trans-in", "eyes/l-eye-trans-out", "eyes/r-eye-trans-out",
    "eyes/l-eye-scale-incr", "eyes/r-eye-scale-incr", "eyes/l-eye-scale-decr", "eyes/r-eye-scale-decr",
    "eyes/l-eye-corner2-up", "eyes/r-eye-corner2-up", "eyes/l-eye-corner2-down", "eyes/r-eye-corner2-down",
    "eyes/l-eye-bag-incr", "eyes/r-eye-bag-incr",
    "nose/nose-scale-vert-incr", "nose/nose-scale-vert-decr", "nose/nose-scale-horiz-incr", "nose/nose-scale-horiz-decr",
    "nose/nose-nostrils-width-incr", "nose/nose-nostrils-width-decr", "nose/nose-hump-incr", "nose/nose-point-width-incr",
    "mouth/mouth-upperlip-volume-incr", "mouth/mouth-upperlip-volume-decr", "mouth/mouth-lowerlip-volume-incr",
    "mouth/mouth-lowerlip-volume-decr", "mouth/mouth-scale-horiz-incr", "mouth/mouth-scale-horiz-decr",
    "ears/l-ear-scale-incr", "ears/r-ear-scale-incr", "ears/l-ear-scale-decr", "ears/r-ear-scale-decr",
    "ears/l-ear-flap-incr", "ears/r-ear-flap-incr",
    "eyebrows/eyebrows-trans-up", "eyebrows/eyebrows-trans-down", "neck/neck-double-incr",
    "stomach/stomach-pregnant-incr", "breast/breast-trans-down", "breast/breast-volume-vert-down", "breast/nipple-size-incr",
    "breast/nipple-point-incr", "buttocks/buttocks-volume-incr", "buttocks/buttocks-volume-decr",
    "genitals/penis-length-incr", "genitals/penis-length-decr", "genitals/penis-circ-incr", "genitals/penis-testicles-incr",
]
for t in TRAITS:
    ajouter(t.split("/")[1], lire_cible(f"{T}/{t}.target", N))

# Écriture.
blocs = []
entete = {"version": 1, "source": "MakeHuman (makehumancommunity/makehuman a8bc2d5), données CC0 1.0",
          "sommets": NG, "rendu": len(rendu_orig), "os": OS, "articulations": {}, "morceaux": {}, "cibles": [], "groupes": {}}
position = 0


def bloc(tableau: np.ndarray) -> dict:
    global position
    octets = tableau.tobytes()
    info = {"debut": position, "n": int(tableau.size), "type": str(tableau.dtype)}
    blocs.append(octets)
    position += len(octets)
    pad = (-len(octets)) % 4
    if pad:
        blocs.append(b"\0" * pad)
        position += pad
    return info


entete["positions"] = bloc(v[ancien].astype(np.float32))
entete["origine"] = bloc(np.array(rendu_orig, dtype=np.uint16))
entete["uv"] = bloc(np.array(rendu_uv, dtype=np.float32))
entete["osIdx"] = bloc(os_idx)
entete["osPoids"] = bloc(os_poids)
for m, tri in morceaux.items():
    entete["morceaux"][m] = bloc(np.array(tri, dtype=np.uint16))
for nom, liste in joints.items():
    entete["articulations"][nom] = [int(nouveau[i]) for i in liste]
for g, m in GARDES.items():
    ids = sorted({int(nouveau[iv]) for gg, coins in faces if gg == g for (iv, _) in coins})
    entete["groupes"].setdefault(m, [])
    entete["groupes"][m] = sorted(set(entete["groupes"][m]) | set(ids))
for nom, idx, val, echelle in cibles:
    entete["cibles"].append({"nom": nom, "idx": bloc(idx), "val": bloc(val), "echelle": echelle})
# Les listes de sommets de groupe ne servent qu'aux yeux et aux aides : on ne garde que celles-là.
entete["groupes"] = {k: entete["groupes"][k] for k in ("oeilG", "oeilD", "sexe", "pagne", "cils", "dents", "langue")}
js = json.dumps(entete, separators=(",", ":")).encode()
js += b" " * ((-len(js)) % 4)
with open(SORTIE, "wb") as f:
    f.write(b"MHUM")
    f.write(struct.pack("<I", len(js)))
    f.write(js)
    for b in blocs:
        f.write(b)
print(f"{SORTIE} : {os.path.getsize(SORTIE) / 1e6:.2f} Mo, {NG} sommets, {len(rendu_orig)} sommets de rendu, {len(cibles)} cibles")
