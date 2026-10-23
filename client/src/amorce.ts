import { chargerLangue, t, traduirePage } from "./langue";

// La langue est chargée avant tout le reste : chaque fenêtre écrit ses textes dans la langue choisie.
void chargerLangue().then(async () => {
  document.title = t("app.titre");
  traduirePage();
  await import("./main");
});
