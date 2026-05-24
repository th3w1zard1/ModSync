namespace KOTORModSync.Common
{
    // Matching PyKotor implementation at Libraries/PyKotor/src/pykotor/common/misc.py:250-285
    // Original: class Game(IntEnum):
    // Extended to support all BioWare engine games: Odyssey (KOTOR), Aurora (NWN), Eclipse (DA/ME)
    // This enum is kept in Andastra.Parsing for backward compatibility with patcher tools (KPatcher, HolocronToolset, NCSDecomp, KotorDiff)
    /// <summary>
    /// Represents which BioWare engine game / platform variant.
    /// </summary>
    public enum Game
    {
        // Odyssey Engine (KOTOR)
        K1 = 1,
        K2 = 2,
        K1_XBOX = 3,
        K2_XBOX = 4,
        K1_IOS = 5,
        K2_IOS = 6,
        K1_ANDROID = 7,
        K2_ANDROID = 8,
        TSL = K2,

        // Eclipse Engine (Dragon Age)
        DA = 10,
        DA_ORIGINS = DA,
        DA2 = 11,
        DRAGON_AGE_2 = DA2,

        // Eclipse Engine (Mass Effect)
        ME = 20,
        MASS_EFFECT = ME,
        ME2 = 21,
        MASS_EFFECT_2 = ME2,
        ME3 = 22,
        MASS_EFFECT_3 = ME3,

        // Aurora Engine (Neverwinter Nights)
        NWN = 30,
        NEVERWINTER_NIGHTS = NWN,
        NWN2 = 31,
        NEVERWINTER_NIGHTS_2 = NWN2
    }

    public static class GameExtensions
    {
        public static bool IsK1(this Game game)
        {
            return ((int)game) % 2 != 0 && game >= Game.K1 && game <= Game.K2_ANDROID;
        }

        public static bool IsK2(this Game game)
        {
            return ((int)game) % 2 == 0 && game >= Game.K1 && game <= Game.K2_ANDROID;
        }

        public static bool IsTSL(this Game game)
        {
            return game == Game.K2;
        }

        public static bool IsXbox(this Game game)
        {
            return game == Game.K1_XBOX || game == Game.K2_XBOX;
        }

        public static bool IsPc(this Game game)
        {
            return game == Game.K1 || game == Game.K2;
        }

        public static bool IsMobile(this Game game)
        {
            return game == Game.K1_IOS || game == Game.K2_IOS || game == Game.K1_ANDROID || game == Game.K2_ANDROID;
        }

        public static bool IsAndroid(this Game game)
        {
            return game == Game.K1_ANDROID || game == Game.K2_ANDROID;
        }

        public static bool IsIOS(this Game game)
        {
            return game == Game.K1_IOS || game == Game.K2_IOS;
        }

        // Eclipse Engine (Dragon Age)
        public static bool IsDragonAge(this Game game)
        {
            return game == Game.DA || game == Game.DA_ORIGINS || game == Game.DA2 || game == Game.DRAGON_AGE_2;
        }

        public static bool IsDragonAgeOrigins(this Game game)
        {
            return game == Game.DA || game == Game.DA_ORIGINS;
        }

        public static bool IsDragonAge2(this Game game)
        {
            return game == Game.DA2 || game == Game.DRAGON_AGE_2;
        }

        // Eclipse Engine ()
        public static bool Is(this Game game)
        {
            return game == Game.ME || game == Game.MASS_EFFECT || game == Game. ||
                   game == Game. || game == Game.MASS_EFFECT_2 ||
                   game == Game. || game == Game.MASS_EFFECT_3;
        }

        public static bool Is1(this Game game)
        {
            return game == Game.ME || game == Game.MASS_EFFECT || game == Game.;
        }

        public static bool Is2(this Game game)
        {
            return game == Game. || game == Game.MASS_EFFECT_2;
        }

        public static bool Is3(this Game game)
        {
            return game == Game. || game == Game.MASS_EFFECT_3;
        }

        // Aurora Engine (Neverwinter Nights)
        public static bool IsNeverwinterNights(this Game game)
        {
            return game == Game.NWN || game == Game.NEVERWINTER_NIGHTS ||
                   game == Game.NWN2 || game == Game.NEVERWINTER_NIGHTS_2;
        }

        public static bool IsNWN1(this Game game)
        {
            return game == Game.NWN || game == Game.NEVERWINTER_NIGHTS;
        }

        public static bool IsNWN2(this Game game)
        {
            return game == Game.NWN2 || game == Game.NEVERWINTER_NIGHTS_2;
        }

        // Engine family checks
        public static bool IsOdyssey(this Game game)
        {
            return game >= Game.K1 && game <= Game.K2_ANDROID;
        }

        public static bool IsEclipse(this Game game)
        {
            return IsDragonAge(game) || Is(game);
        }

        public static bool IsAurora(this Game game)
        {
            return IsNeverwinterNights(game);
        }
    }
}
