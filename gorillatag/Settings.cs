using System.Collections.Generic;
using BepInEx.Configuration;

namespace TheLastWitness
{
    public enum Difficulty { Facil, Media, Dificil, TerrorAbsoluto }

    /// <summary>Which shape the Witness takes: either, only the black shadow gorilla, or only the grey horror rig.</summary>
    public enum EntityForm { Ambos, Sombra, Cinza }

    /// <summary>Everything the menu changes, kept in BepInEx's config file so it survives restarts.</summary>
    public static class Settings
    {
        public static ConfigEntry<Difficulty> Diff;
        public static ConfigEntry<GameMode> Mode;
        public static ConfigEntry<EntityForm> Form;
        public static ConfigEntry<bool> SoftFlashes, NoMovement, Wrist, PauseInMenu;
        /// <summary>Off at every launch on purpose: the player turns the horror on from the menu (with its intro).</summary>
        public static bool Running;
        public static ConfigEntry<float> ScareVolume, MaxDarkness, Volume, ModelYaw, MicSensitivity;
        public static readonly Dictionary<string, ConfigEntry<bool>> On = new Dictionary<string, ConfigEntry<bool>>();
        private static ConfigFile cfg;

        public static void Bind(ConfigFile c)
        {
            cfg = c;
            Diff = c.Bind("Jogo", "Intensidade", Difficulty.Media, "Facil, Media, Dificil, TerrorAbsoluto.");
            Mode = c.Bind("Jogo", "Modo", GameMode.Fitas, "Fitas: ache as 8 fitas e sobreviva ao final. Pesadelo: sem objetivo, sem fim.");
            SoftFlashes = c.Bind("Conforto", "FlashesSuaves", false, "Flashes de tela mais fracos (fotossensibilidade).");
            NoMovement = c.Bind("Conforto", "SemEfeitosDeMovimento", false, "Nada mexe no seu corpo (gravidade, puxões).");
            Wrist = c.Bind("Conforto", "SanidadeNoPulso", true, "Barra de sanidade no pulso esquerdo.");
            PauseInMenu = c.Bind("Conforto", "PausarComMenu", true, "Com o menu aberto nada novo acontece e a sanidade não cai.");
            ScareVolume = c.Bind("Conforto", "VolumeDosSustos", 1f, "0.25 a 1.");
            MaxDarkness = c.Bind("Conforto", "EscuridaoMaxima", 0.85f, "0.5 a 0.97: o máximo que a tela escurece.");
            Volume = c.Bind("Conforto", "VolumeGeral", 1f, "0.25 a 1.");
            MicSensitivity = c.Bind("Conforto", "SensibilidadeDoMicrofone", 1f, "0.5 a 2: o quanto ele ouve a sua voz.");
            Form = c.Bind("Entidade", "Forma", EntityForm.Ambos, "Ambos, Sombra (o gorila preto) ou Cinza (o modelo horror rig).");
            ModelYaw = c.Bind("Entidade", "GiroDoModelo", 0f, "Graus para virar o modelo da entidade (witness.bundle) se ele olhar para o lado errado.");
        }

        public static bool IsOn(string id)
        {
            if (!On.TryGetValue(id, out var e)) On[id] = e = cfg.Bind("Eventos", id, true, "Evento ligado.");
            return e.Value;
        }

        public static void Toggle(string id) => On[id].Value = !IsOn(id);

        /// <summary>Minutes for sanity to fall from 100 to 0 with nothing else happening.</summary>
        public static float DrainMinutes => Diff.Value switch { Difficulty.Facil => 45f, Difficulty.Media => 25f, Difficulty.Dificil => 14f, _ => 7f };

        public static string FormName(EntityForm f) => f switch { EntityForm.Sombra => "Sombra", EntityForm.Cinza => "Cinza", _ => "Ambos" };

        public static string DiffName(Difficulty d) => d switch
        {
            Difficulty.Facil => "Fácil",
            Difficulty.Media => "Média",
            Difficulty.Dificil => "Difícil",
            _ => "Terror absoluto",
        };
    }
}
