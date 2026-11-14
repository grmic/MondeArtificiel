# Conversion de la base humaine MakeHuman

`convertir.py` lit les données CC0 de MakeHuman (maillage, cibles, squelette, poids) et écrit
`client/public/humain/makehuman.bin`, chargé par la vue 3D (`client/src/trois/humain.ts`).

```bash
git clone --depth 1 https://github.com/makehumancommunity/makehuman.git
python3 outils/makehuman/convertir.py makehuman/makehuman/data client/public/humain/makehuman.bin
```

Il faut Python 3 et NumPy. Le script vérifie la mention CC0 de chaque fichier lu et s'arrête sinon. Le code de MakeHuman
(AGPL) n'est pas utilisé.
