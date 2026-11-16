using Monde.Simulation.Humains;
using static Monde.Simulation.Humains.SouvenirDialogue;

namespace Monde.Tests;

/// <summary>0.44.10 : ce qu'un humain retient de ses dialogues avec l'Entité.</summary>
public sealed class SouvenirDialogueTests
{
    private const double Heure = 3600, Jour = 24 * Heure;

    [Fact]
    public void LesMotsSEffacent_LeSensReste()
    {
        for (long g = 0; g < 50; g++)
        {
            // Une heure après, il se souvient des mots mêmes ; un mois après, du sens.
            Assert.Equal(Trace.MotPourMot, Retenu(Heure, 0.5, ModeParole.Voix, g));
            Assert.Equal(Trace.Sens, Retenu(30 * Jour, 0.5, ModeParole.Voix, g));
            // Un rêve d'il y a cinq ans est oublié ; une apparition d'il y a un an ne l'est pas.
            Assert.Equal(Trace.Oubli, Retenu(5 * 365 * Jour, 0.5, ModeParole.Reve, g));
            Assert.NotEqual(Trace.Oubli, Retenu(365 * Jour, 0.5, ModeParole.Apparition, g));
        }
    }

    [Fact]
    public void UneBonneMemoire_GardePlusLongtemps()
    {
        int bonne = 0, faible = 0;
        for (long g = 0; g < 200; g++)
        {
            bonne += (int)Retenu(200 * Jour, 1, ModeParole.Voix, g);
            faible += (int)Retenu(200 * Jour, 0, ModeParole.Voix, g);
        }
        Assert.True(bonne > faible);
    }
}
