# Textures de la nature (0.41.5)

`composer.py` prépare les textures du sol, des écorces et des feuillages de la vue 3D à partir de photographies CC0
(Poly Haven, ambientCG) et les écrit dans `client/public/nature` (voir `LISEZMOI.txt` de ce dossier pour les sources).

```
pip install numpy scipy pillow
python3 outils/nature/composer.py <dossier des sources> client/public/nature
python3 outils/nature/composer.py <dossier des sources> client/public/nature --feuillages   # l'atlas seul
```

Le dossier des sources contient les fichiers 1k de Poly Haven (`<nom>_diff_1k.jpg`, `<nom>_nor_gl_1k.jpg`, `fern_02_alpha_1k.png`)
et les PNG 1K d'ambientCG (`<nom>_Color.png`, `<nom>_Opacity.png`). Le script refuse tout fichier dont l'empreinte diffère :
md5 de l'API officielle de Poly Haven, ou sha256 gardé dans `empreintes.json` pour les autres.
