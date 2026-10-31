using System.Globalization;
using System.Text;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Stockage;

namespace Monde.Serveur.Entite;

/// <summary>Ce qui appelle l'Entité-LLM.</summary>
public enum OccasionLlm
{
    /// <summary>Le joueur demande « Que ferait l'Entité ? » sur la fiche d'un humain.</summary>
    DemandeHumain = 0,
    /// <summary>Le joueur demande « Que ferait l'Entité ? » sur la fiche d'une communauté.</summary>
    DemandeCommunaute = 1,
    /// <summary>Une prière forte (★).</summary>
    PriereForte = 2,
    /// <summary>Une prière collective.</summary>
    PriereCollective = 3,
    /// <summary>Une prière ordinaire, tirée selon la part choisie.</summary>
    PriereOrdinaire = 4,
}

/// <summary>Ce que le LLM lit : la consigne (qui il est, ce qu'il peut faire) et la demande (l'occasion, l'humain, sa communauté…).</summary>
public sealed record LectureLlm(string Consigne, string Demande, IReadOnlyList<string> Pieces);

/// <summary>
/// Rédige ce que l'Entité voit, dans la langue courante : l'humain (tiré de sa fiche), sa communauté, les autres communautés, ce
/// qu'on peut lui souffler, et ce qu'elle a fait dernièrement. L'Entité est omnisciente : jardin secret compris.
/// </summary>
public static class Contexte
{
    private static string N(double v, int d = 0) => L.Nombre(v, d);

    private const string FormeReponse = """{"pensee":"…","mots":"…","actes":[…]}""";

    private const string FormeRien = """{"type":"rien"}""";

    private const string FormeSoufflerGeste = """{"type":"souffler_geste","humain":n°,"geste":"…","a":"…","b":"…"}""";

    private const string FormeSoufflerCombinaison = """{"type":"souffler_combinaison","humain":n°,"operation":"lettre","a":"P1","b":"P2","lien":n}""";

    private const string FormeCatastrophe = """{"type":"catastrophe","catastrophe":"…","x":n,"y":n,"intensite":x}""";

    private const string FormeGeste = """{"type":"geste","geste":"…","cible":n°,"maniere":"…","param":n,"calamite":"…","force":x,"jours":x}""";

    private const string FormeExaucer = """{"type":"exaucer","priere":n°}""";

    private const string FormeRefuser = """{"type":"refuser","priere":n°,"signe":true|false}""";

    private const string FormePenchant = """{"type":"penchant","humains":[n°],"cible":n° ou 0,"mutuel":true|false,"passion":true|false}""";

    private const string FormeParlerCommunaute = """{"type":"parler_communaute","communaute":n°,"parole":"…","mode":"…","cibles":[n°],"direction":0-7}""";

    private const string FormeParler = """{"type":"parler","humains":[n°],"parole":"…","mode":"…","cible":n°,"param":n}""";

    /// <summary>La consigne : le rôle de l'Entité, la forme de la réponse et le catalogue des actes permis.</summary>
    public static string Consigne(MondeSimule monde, FamillesLlm permises)
    {
        var c = new StringBuilder();
        c.AppendLine(L.T("llm.consigne", ("langue", L.Courante.Nom), ("forme", FormeReponse)));
        c.AppendLine();
        c.AppendLine(L.T("llm.cat.titre"));
        if (permises.HasFlag(FamillesLlm.Paroles))
        {
            c.AppendLine($"- {L.T("llm.cat.parler")} : {FormeParler}");
            foreach (var p in Enum.GetValues<Parole>().Where(p => p != Parole.Penchant && !Paroles.Collective(p)))
            {
                var precision = p switch
                {
                    Parole.PartsVers => " " + L.T("llm.cat.paramDirection"),
                    Parole.PlanteDangereuse => " " + L.T("llm.cat.paramPlante"),
                    Parole.NeVaPasALaGuerre => " " + L.T("llm.cat.paramCommunaute"),
                    _ => "",
                };
                c.AppendLine($"  - {p} : {L.T($"parole.{p}")}{(Paroles.VeutCible(p) ? " " + L.T("llm.cat.veutCible") : "")}{precision}");
            }
            c.AppendLine(L.T("llm.cat.modes", ("modes", string.Join(", ", Enum.GetValues<ModeParole>().Where(m => m != ModeParole.Penchant)
                .Select(m => $"{m} ({L.T($"llm.mode.{m}")})")))));
            c.AppendLine(L.T("llm.cat.directions", ("directions", string.Join(", ", Enumerable.Range(0, 8).Select(d => $"{d} = {L.T($"direction.{d}")}")))));
            c.AppendLine($"- {L.T("llm.cat.parlerCommunaute")} : {FormeParlerCommunaute}");
            foreach (var p in Enum.GetValues<Parole>().Where(Paroles.Collective))
                c.AppendLine($"  - {p} : {L.T($"parole.{p}")}{(Paroles.VeutCommunaute(p) ? " " + L.T("llm.cat.veutCibles") : p == Parole.PartezVers ? " " + L.T("llm.cat.veutDirection") : "")}");
            c.AppendLine($"- {L.T("llm.cat.penchant")} : {FormePenchant}");
            c.AppendLine($"- {L.T("llm.cat.refuser")} : {FormeRefuser}");
        }
        if (permises.HasFlag(FamillesLlm.Bienfaits)) c.AppendLine($"- {L.T("llm.cat.exaucer")} : {FormeExaucer}");
        if ((permises & (FamillesLlm.Bienfaits | FamillesLlm.Chatiments)) != 0)
        {
            c.AppendLine($"- {L.T("llm.cat.geste")} : {FormeGeste}");
            foreach (var g in Enum.GetValues<GesteEntite>())
            {
                var famille = Gestes.EstBienfait(g) ? FamillesLlm.Bienfaits : FamillesLlm.Chatiments;
                if (!permises.HasFlag(famille)) continue;
                var (jmin, jmax) = Gestes.Jours(g);
                var (fmin, fmax) = Gestes.Force(g);
                var details = new List<string> { L.T(Gestes.SurHumain(g) ? "llm.cat.surHumain" : "llm.cat.surCommunaute"), L.T(Gestes.EstBienfait(g) ? "llm.cat.bienfait" : "llm.cat.chatiment") };
                if (fmax > 0) details.Add(L.T("llm.cat.force", ("min", N(fmin, 2)), ("max", N(fmax, 2))));
                if (jmax > 0) details.Add(L.T("llm.cat.jours", ("min", N(jmin)), ("max", N(jmax))));
                if (g == GesteEntite.FrapperDeMort)
                    details.Add(L.T("llm.cat.forme", ("formes", string.Join(", ", Enum.GetValues<FormeMort>().Select(f => $"{(int)f} = {L.T($"formeMort.{f}")}")))));
                if (g is GesteEntite.RendreMalade or GesteEntite.Epidemie) details.Add(L.T("llm.cat.maladie"));
                if (g == GesteEntite.Calamite) details.Add(L.T("llm.cat.calamite"));
                c.AppendLine($"  - {g} : {L.T($"gesteEntite.{g}")} ({string.Join(" ; ", details)})");
            }
            c.AppendLine(L.T("llm.cat.manieres", ("manieres", string.Join(", ", Enum.GetValues<ManiereGeste>().Select(m => m == ManiereGeste.Cachee ? $"{m} ({L.T("gestes.cache")})" : $"{m} ({L.T($"maniere.{m}")})")))));
            if (permises.HasFlag(FamillesLlm.Chatiments))
            {
                var maladies = Enum.GetValues<Maladie>().Where(m => m != Maladie.Aucune).Select(m => $"{(int)m} = {L.T($"maladie.{m}")}")
                    .Concat(monde.Pathogenes().Select(p => $"{Gestes.Contagieuse + p.Id} = {L.T("gestes.contagieuse", ("nom", p.Nom))}"))
                    .Append($"0 = {L.T("gestes.maladieNouvelle")}");
                c.AppendLine(L.T("llm.cat.maladies", ("maladies", string.Join(", ", maladies))));
            }
        }
        if (permises.HasFlag(FamillesLlm.Catastrophes) || permises.HasFlag(FamillesLlm.Chatiments))
        {
            var cats = Actes.Provoquables.Select(p => p.Intensite > 0 ? $"{p.Cle} ({L.T($"provoquer.{p.Cle}")}, {N(p.Intensite, 1)} {L.T($"provoquer.{p.Cle}.unite")})" : $"{p.Cle} ({L.T($"provoquer.{p.Cle}")})");
            if (permises.HasFlag(FamillesLlm.Catastrophes)) c.AppendLine($"- {L.T("llm.cat.catastrophe")} : {FormeCatastrophe}");
            c.AppendLine(L.T("llm.cat.catastrophes", ("liste", string.Join(", ", cats))));
        }
        if (permises.HasFlag(FamillesLlm.Souffles))
        {
            c.AppendLine($"- {L.T("llm.cat.soufflerCombinaison")} : {FormeSoufflerCombinaison}");
            c.AppendLine($"- {L.T("llm.cat.soufflerGeste")} : {FormeSoufflerGeste}");
        }
        c.AppendLine($"- {L.T("llm.cat.rien")} : {FormeRien}");
        return c.ToString();
    }

    /// <summary>La demande : l'occasion, l'humain et sa communauté (ou la communauté seule), les autres communautés, la mémoire de l'Entité.</summary>
    public static LectureLlm Lire(ServiceMonde service, FamillesLlm permises, OccasionLlm occasion, int humainId, int communauteId, Priere? priere,
        IReadOnlyList<EchangeEntite> memoire)
    {
        var monde = service.Monde;
        var (maintenant, humains) = monde.InstantaneHumains();
        var communautes = monde.Communautes();
        var d = new StringBuilder();
        d.AppendLine(L.T("llm.d.date", ("date", L.Date(maintenant))));
        d.AppendLine(L.T($"llm.occasion.{occasion}"));
        if (priere is not null)
        {
            var p = Religions.Prieres(service, enAttente: false).FirstOrDefault(x => x.Id == priere.Id);
            if (p is not null)
                d.AppendLine(L.T("llm.d.priere", ("id", p.Id), ("nom", p.Nom), ("humain", p.Humain), ("sujet", p.Sujet), ("ferveur", p.Ferveur), ("texte", p.Texte),
                    ("collective", p.Collective is null ? "" : L.T("llm.d.collective", ("nom", p.Collective), ("id", p.CollectiveId)))));
        }
        var pieces = new List<string>();
        if (humainId >= 1 && humainId <= humains.Count)
        {
            var h = humains[humainId - 1];
            d.AppendLine();
            d.AppendLine(L.T("llm.d.humain", ("nom", h.Nom), ("id", h.Id)));
            Humain(d, service, h.Id);
            if (permises.HasFlag(FamillesLlm.Souffles) && h.EstVivant) pieces = Souffles(d, service, h.Id);
            if (communauteId == 0) communauteId = h.CommunauteId;
        }
        if (communauteId >= 1 && communauteId <= communautes.Count)
        {
            d.AppendLine();
            d.AppendLine(L.T("llm.d.communaute", ("nom", L.Communaute(communautes[communauteId - 1].Nom)), ("id", communauteId)));
            Communaute(d, service, communauteId, avecMembres: humainId == 0);
        }
        d.AppendLine();
        d.AppendLine(L.T("llm.d.autres"));
        foreach (var c in CommunauteDto.Toutes(service).Where(c => c.DisperseeLe is null && c.Id != communauteId).OrderByDescending(c => c.Membres.Count).Take(25))
        {
            var rel = c.Relations.FirstOrDefault(r => r.Id == communauteId);
            d.AppendLine(L.T("llm.d.autre", ("id", c.Id), ("nom", c.Nom), ("n", c.Membres.Count), ("x", c.X), ("y", c.Y),
                ("relation", rel is null ? "" : $" — {rel.Etat}")));
        }
        if (memoire.Count > 0)
        {
            d.AppendLine();
            d.AppendLine(L.T("llm.d.memoire"));
            foreach (var e in memoire)
            {
                var qui = e.Humain >= 1 && e.Humain <= humains.Count ? $"{humains[e.Humain - 1].Nom} (n°{e.Humain})"
                    : e.Communaute >= 1 && e.Communaute <= communautes.Count ? L.Communaute(communautes[e.Communaute - 1].Nom) : "—";
                d.AppendLine($"- {L.Date(e.Le)}, {qui} : {(e.Mots.Length > 0 ? $"« {ClientLlm.Couper(e.Mots, 200)} » " : "")}{ClientLlm.Couper(Resume(e), 300)}");
            }
        }
        d.AppendLine();
        d.AppendLine(L.T("llm.d.fin"));
        return new LectureLlm(Consigne(monde, permises), d.ToString(), pieces);
    }

    /// <summary>Résumé des actes d'un échange et de ce qu'ils ont donné.</summary>
    public static string Resume(EchangeEntite e)
    {
        try
        {
            var resultats = System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.Resultats) ?? [];
            return resultats.Count == 0 ? L.T("llm.resultat.rien") : string.Join(" / ", resultats);
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }

    private static void Ligne(StringBuilder d, string cle, string? valeur)
    {
        if (!string.IsNullOrWhiteSpace(valeur)) d.AppendLine($"{L.T(cle)} : {valeur}");
    }

    private static void Humain(StringBuilder d, ServiceMonde service, int id)
    {
        if (FicheDto.Depuis(service, id) is not { } f) return;
        var h = f.Humain;
        var etat = new List<string> { L.T("llm.h.age", ("age", N(h.Age))), h.Sexe == "femme" ? L.T("llm.h.femme") : L.T("llm.h.homme"), h.Etape };
        if (!h.Vivant) etat.Add(L.T("llm.h.mort", ("cause", h.CauseDeMort ?? "")));
        else
        {
            etat.Add(h.Activite);
            etat.Add(L.T("llm.h.sante", ("v", N(h.Sante))));
            if (h.Malade) etat.Add(L.T("llm.h.malade"));
            if (h.Enceinte) etat.Add(L.T("llm.h.enceinte"));
            if (h.Faim >= 50) etat.Add(L.T("llm.h.faim", ("v", N(h.Faim))));
        }
        d.AppendLine(string.Join(", ", etat) + ".");
        Ligne(d, "llm.h.humeur", h.Humeur);
        Ligne(d, "llm.h.portrait", string.Join(" ", f.Portrait));
        Ligne(d, "llm.h.caractere", string.Join(" ; ", f.Caractere.Select(t => $"{t.Nom} {t.Mots}")));
        Ligne(d, "llm.h.morale", string.Join(" ; ", f.Coeur.Concat(f.Morale).Select(t => $"{t.Nom} {t.Mots}")));
        Ligne(d, "llm.h.intelligence", string.Join(" ; ", f.Intelligence.Select(t => $"{t.Nom} {N(t.Valeur)} ({t.Mots})")));
        if (f.Ame is { } a) Ligne(d, "llm.h.ame", string.Join(" ; ", a.Raisons.Prepend(a.Etat)));
        Ligne(d, "llm.h.epreuves", string.Join(" ; ", new[] { f.Epreuves.Maladie, f.Epreuves.PeurDe, f.Medecine.Sequelle, f.Medecine.Plaie, f.Medecine.Malediction }.Where(x => !string.IsNullOrEmpty(x))));
        var r = f.Regard;
        Ligne(d, "llm.h.regard", string.Join(" ; ", new[] { L.T("llm.h.karma", ("v", N(r.Karma, 1)), ("mots", r.KarmaMots)), r.CroyanceMots, r.AmourMots, r.CrainteMots, r.RevolteMots }
            .Where(x => !string.IsNullOrEmpty(x))));
        if (f.Foi is { } foi)
        {
            Ligne(d, "llm.h.religion", foi.NomReligion is null ? null : string.Join(", ", new[] { foi.NomReligion, foi.Role }.Where(x => !string.IsNullOrEmpty(x))));
            foreach (var p in foi.Prieres.Take(5))
                d.AppendLine($"- {L.T("llm.h.priere")} n°{p.Id}, {p.Date} : {p.Texte} → {p.Issue}{(p.Conclusion is null ? "" : $" ({p.Conclusion})")}");
        }
        foreach (var p in f.Paroles.Take(5)) d.AppendLine($"- {L.T("llm.h.parole")} {p.Date} ({p.Mode}) : {p.Texte} → {p.Reponse}");
        Ligne(d, "llm.h.communaute", f.Communaute is null ? L.T("llm.h.seul") : $"{L.Communaute(f.Communaute.Nom)} (n°{f.Communaute.Id})");
        Ligne(d, "llm.h.liens", string.Join(" ; ", f.Liens.Take(10).Select(l => $"{l.Nom} n°{l.Id} ({l.Statut}, {L.T("llm.h.affection")} {l.Affection}{(l.Vivant ? "" : ", " + L.T("llm.h.disparu"))})")));
        if (f.Jardin.Count > 0)
        {
            d.AppendLine(L.T("llm.h.jardin"));
            foreach (var j in f.Jardin.OrderByDescending(j => j.Intensite).Take(12)) d.AppendLine($"- {j.Type} : {j.Texte} ({j.Intensite}/100)");
        }
        if (f.Pensees.Count > 0)
        {
            d.AppendLine(L.T("llm.h.pensees"));
            foreach (var p in f.Pensees.Take(8)) d.AppendLine($"- {p.Date} : {p.Texte}");
        }
        if (f.Histoire.Count > 0)
        {
            d.AppendLine(L.T("llm.h.histoire"));
            foreach (var e in f.Histoire.OrderByDescending(e => e.Secondes).Take(15).Reverse()) d.AppendLine($"- {e.Date} : {e.Texte}");
        }
        Ligne(d, "llm.h.savoirs", string.Join(" ; ", f.Savoirs.Savoirs.Take(15).Select(s => s.Texte)));
    }

    private static void Communaute(StringBuilder d, ServiceMonde service, int id, bool avecMembres)
    {
        if (FicheCommunauteDto.Depuis(service, id) is not { } f) return;
        var b = f.Base;
        d.AppendLine(L.T("llm.c.etat", ("age", f.Age), ("vivants", f.Vivants), ("adultes", f.Adultes), ("x", b.X), ("y", b.Y), ("figure", b.NomFigure), ("idFigure", b.Figure)));
        if (b.Disette) d.AppendLine(L.T("llm.c.disette"));
        Ligne(d, "llm.c.temperament", b.Temperament);
        Ligne(d, "llm.c.moeurs", string.Join(" ", new[] { b.Moeurs, b.Ideal, b.Morale }.Where(x => !string.IsNullOrEmpty(x))));
        Ligne(d, "llm.c.relations", string.Join(" ; ", b.Relations.Select(r => $"{L.Communaute(r.Nom)} n°{r.Id} : {r.Etat}")));
        Ligne(d, "llm.c.guerre", string.Join(" ", b.Guerre));
        if (f.Autorite is { } a) d.AppendLine(L.T("llm.c.autorite", ("nom", a.NomFigure), ("autorite", a.Autorite), ("crainte", a.Crainte)));
        var foi = f.Foi;
        Ligne(d, "llm.c.religions", string.Join(" ; ", foi.Religions));
        Ligne(d, "llm.c.regard", string.Join(" ; ", new[] { foi.Croyance is { } c ? L.T("llm.c.croyance", ("v", c)) : null,
            foi.Amour is { } am ? L.T("llm.c.amour", ("v", am)) : null, foi.Crainte is { } cr ? L.T("llm.c.crainte", ("v", cr)) : null,
            foi.Revolte is { } rv ? L.T("llm.c.revolte", ("v", rv)) : null }.Where(x => x is not null)));
        Ligne(d, "llm.c.souvenirs", string.Join(" ; ", foi.Effets.Concat(f.Entite.Take(8).Select(e => $"{e.Date} : {e.Texte}"))));
        Ligne(d, "llm.c.savoirs", string.Join(" ; ", f.Savoirs.Connus.Take(20)));
        if (f.Marquants.Count > 0)
        {
            d.AppendLine(L.T("llm.c.marquants"));
            foreach (var e in f.Marquants.OrderByDescending(e => e.Secondes).Take(12).Reverse()) d.AppendLine($"- {e.Date} : {e.Texte}");
        }
        if (avecMembres)
        {
            d.AppendLine(L.T("llm.c.membres"));
            foreach (var m in f.Membres.Take(40))
                d.AppendLine($"- n°{m.Id} {m.Nom}, {m.Sexe}, {m.Age} {L.T("llm.c.ans")}, {m.Etape}{(m.Role is null ? "" : ", " + m.Role)}, QI {m.Qi}, karma {N(m.Karma, 1)}, {L.T("llm.c.croyanceCourt")} {m.Croyance}");
        }
        var prieres = foi.Prieres.Where(p => p.Exaucable).Take(8).ToList();
        if (prieres.Count > 0)
        {
            d.AppendLine(L.T("llm.c.prieres"));
            foreach (var p in prieres) d.AppendLine($"- n°{p.Id}, {p.Nom} (n°{p.Humain}) : {p.Texte}");
        }
    }

    /// <summary>Ce qu'on peut souffler à cet humain : ses pièces numérotées (P1…), les opérations et les liens ; rend les pièces.</summary>
    private static List<string> Souffles(StringBuilder d, ServiceMonde service, int id)
    {
        var choix = Arbre.Choix(service, id);
        if (choix.Pieces.Count == 0) return [];
        d.AppendLine(L.T("llm.d.souffles"));
        var pieces = choix.Pieces.Take(40).ToList();
        for (var i = 0; i < pieces.Count; i++) d.AppendLine($"  P{i + 1} = {pieces[i].Libelle}");
        d.AppendLine(L.T("llm.d.operations", ("liste", string.Join(", ", choix.Operations.Select(o => $"{o.Cle} = {o.Libelle}")))));
        if (choix.Liens.Count > 0) d.AppendLine(L.T("llm.d.liens", ("liste", string.Join(", ", choix.Liens.Select(o => $"{o.Cle} = {o.Libelle}")))));
        var gestes = Decouvertes.Choix(service, id).Gestes;
        d.AppendLine(L.T("llm.d.gestes", ("liste", string.Join(" ; ", gestes.Select(g =>
            $"{g.Cle}({string.Join("|", g.A.Select(a => a.Cle))}{(g.B.Count > 0 ? ", " + string.Join("|", g.B.Select(b => b.Cle)) : "")})")))));
        return pieces.Select(p => p.Cle).ToList();
    }
}
