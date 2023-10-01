using System;
using System.Collections.Generic;
using System.Linq;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace PKHeX.Core.AutoMod
{
    public static class BattleTemplateLegality
    {
        public static string ANALYSIS_INVALID { get; set; } =
            "El análisis específico para este conjunto no está disponible.";
        public static string EXHAUSTED_ENCOUNTERS { get; set; } =
            "### Error\n- No hay un encuentro válido disponible: (Agotados **{0}/{1}** posibles encuentros).\n\n```No hay un encuentro en la base de datos que pueda corresponder al conjunto solicitado.\n\n📝Soluciones:\n• Por favor, verifica bien la informacion del conjunto e intentalo de nuevo.``` ";
        public static string SPECIES_UNAVAILABLE_FORM { get; set; } =
            "### Error\n- **{0}** con la forma **{1}** no esta disponible en este juego.\n\n```La forma solicitada para este pokemon no esta disponible en el juego.. Por favor, intentelo con la forma regular del pokemon.```";
        public static string SPECIES_UNAVAILABLE { get; set; } = "### Error\n- **{0}** no esta disponible en el juego.\n\n```📝Soluciones:\n• Comprueba que el nombre del pokemon esta escrito correctamente y en ingles.\n\n• Puede que el pokemon solicitado no se encuentre en el juego.. Por favor, verifica la lista de pokemons obtenibles en el juego e intentalo de nuevo.```";
        public static string INVALID_MOVES { get; set; } =
            "### Error\n- **{0}** no puede aprender los siguientes movimientos en este juego: **{1}**.";
        public static string ALL_MOVES_INVALID { get; set; } =
            "### Error\n- Todos los movimientos solicitados para este Pokémon no son válidos.\n\n```📝Soluciones:\n• Cambia los movimientos o verifica que no estes solicitando una forma del pokemon que no puede ser obtenida por medio de intercambio.```";
        public static string LEVEL_INVALID { get; set; } =
            "### Error\n- El nivel solicitado es inferior al nivel mínimo posible para **{0}**. El nivel mínimo requerido es **{1}**.\n\n```📝Soluciones:\n• Cambia el nivel del pokemon solicitado al nivel {1}```";
        public static string SHINY_INVALID { get; set; } =
            "### Error\n- Valor shiny establecido **(ShinyType.{0})** no es posible para el conjunto solicitado.\n\n```📝Soluciones:\n• Verificar que no se esta solicitando un pokemon con Shiny Lock, de ser el caso puedes eliminar (Shiny: Yes) del conjunto!```\n\n### Consejo\n- Puedes verificar la lista de pokemons con Shiny Lock aqui [(Click Aqui)](https://i.imgur.com/kstbtVc.png)";
        public static string ALPHA_INVALID { get; set; } = "### Error\n- El Pokémon solicitado no pueden ser alfa.";
        public static string BALL_INVALID { get; set; } =
            "### Error\n- **{0} Ball** no es posible para el conjunto solicitado.";
        public static string ONLY_HIDDEN_ABILITY_AVAILABLE { get; set; } =
            "### Error\n- Sólo se puede obtener **{0}** con habilidad oculta en este juego.";
        public static string HIDDEN_ABILITY_UNAVAILABLE { get; set; } =
            "### Error\n- No puedes obtener **{0}** con habilidad oculta en este juego.";
        public static string HOME_TRANSFER_ONLY { get; set; } =
            "### Error\n- **{0}** sólo está disponible en este juego a través de __**Home Transfer**__.";

        public static string SetAnalysis(this IBattleTemplate set, ITrainerInfo sav, PKM failed)
        {
            if (failed.Version == 0)
                failed.Version = sav.Game;
            var species_name = SpeciesName.GetSpeciesNameGeneration(
                set.Species,
                (int)LanguageID.English,
                sav.Generation
            );
            var analysis =
                set.Form == 0
                    ? string.Format(SPECIES_UNAVAILABLE, species_name)
                    : string.Format(SPECIES_UNAVAILABLE_FORM, species_name, set.FormName);

            // Species checks
            var gv = (GameVersion)sav.Game;
            if (!gv.ExistsInGame(set.Species, set.Form))
                return analysis; // Species does not exist in the game

            // Species exists -- check if it has at least one move.
            // If it has no moves and it didn't generate, that makes the mon still illegal in game (moves are set to legal ones)
            var moves = set.Moves.Where(z => z != 0).ToArray();
            var count = set.Moves.Count(z => z != 0);

            // Reusable data
            var batchedit = false;
            IReadOnlyList<StringInstruction>? filters = null;
            if (set is RegenTemplate r)
            {
                filters = r.Regen.Batch.Filters;
                batchedit = APILegality.AllowBatchCommands && r.Regen.HasBatchSettings;
            }
            var destVer = (GameVersion)sav.Game;
            if (destVer <= 0 && sav is SaveFile s)
                destVer = s.Version;
            var gamelist = APILegality.FilteredGameList(
                failed,
                destVer,
                APILegality.AllowBatchCommands,
                set
            );

            // Move checks
            List<IEnumerable<ushort>> move_combinations = new();
            for (int i = count; i >= 1; i--)
                move_combinations.AddRange(GetKCombs(moves, i));

            ushort[] original_moves = new ushort[4];
            set.Moves.CopyTo(original_moves, 0);
            ushort[] successful_combination = GetValidMoves(
                set,
                sav,
                move_combinations,
                failed,
                gamelist
            );
            if (
                !new HashSet<ushort>(original_moves.Where(z => z != 0)).SetEquals(
                    successful_combination
                )
            )
            {
                var invalid_moves = string.Join(
                    ", ",
                    original_moves
                        .Where(z => !successful_combination.Contains(z) && z != 0)
                        .Select(z => $"{(Move)z}")
                );
                return successful_combination.Length > 0
                    ? string.Format(INVALID_MOVES, species_name, invalid_moves)
                    : ALL_MOVES_INVALID;
            }

            // All moves possible, get encounters
            failed.ApplySetDetails(set);
            failed.SetMoves(original_moves);
            failed.SetRecordFlags(Array.Empty<ushort>());

            var encounters = EncounterMovesetGenerator
                .GenerateEncounters(pk: failed, moves: original_moves, gamelist)
                .ToList();
            var initialcount = encounters.Count;
            if (set is RegenTemplate rt && rt.Regen.EncounterFilters is { } x)
                encounters.RemoveAll(enc => !BatchEditing.IsFilterMatch(x, enc));

            // No available encounters
            if (encounters.Count == 0)
                return string.Format(EXHAUSTED_ENCOUNTERS, initialcount, initialcount);

            // Level checks, check if level is impossible to achieve
            if (encounters.All(z => !APILegality.IsRequestedLevelValid(set, z)))
                return string.Format(LEVEL_INVALID, species_name, encounters.Min(z => z.LevelMin));
            encounters.RemoveAll(enc => !APILegality.IsRequestedLevelValid(set, enc));

            // Shiny checks, check if shiny is impossible to achieve
            Shiny shinytype = set.Shiny ? Shiny.Always : Shiny.Never;
            if (set is RegenTemplate ret && ret.Regen.HasExtraSettings)
                shinytype = ret.Regen.Extra.ShinyType;
            if (encounters.All(z => !APILegality.IsRequestedShinyValid(set, z)))
                return string.Format(SHINY_INVALID, shinytype);
            encounters.RemoveAll(enc => !APILegality.IsRequestedShinyValid(set, enc));

            // Alpha checks
            if (encounters.All(z => !APILegality.IsRequestedAlphaValid(set, z)))
                return ALPHA_INVALID;
            encounters.RemoveAll(enc => !APILegality.IsRequestedAlphaValid(set, enc));

            // Ability checks
            var abilityreq = APILegality.GetRequestedAbility(failed, set);
            if (
                abilityreq == AbilityRequest.NotHidden
                && encounters.All(
                    z => z is IEncounterable { Ability: AbilityPermission.OnlyHidden }
                )
            )
                return string.Format(ONLY_HIDDEN_ABILITY_AVAILABLE, species_name);
            if (
                abilityreq == AbilityRequest.Hidden
                && encounters.All(z => z.Generation is 3 or 4)
                && destVer.GetGeneration() < 8
            )
                return string.Format(HIDDEN_ABILITY_UNAVAILABLE, species_name);

            // Home Checks
            if (!APILegality.AllowHOMETransferGeneration)
            {
                if (encounters.All(z => HomeTrackerUtil.IsRequired(z, failed)))
                    return string.Format(HOME_TRANSFER_ONLY, species_name);
                encounters.RemoveAll(enc => HomeTrackerUtil.IsRequired(enc, failed));
            }

            // Ball checks
            if (set is RegenTemplate regt && regt.Regen.HasExtraSettings)
            {
                var ball = regt.Regen.Extra.Ball;
                if (encounters.All(z => !APILegality.IsRequestedBallValid(set, z)))
                    return string.Format(BALL_INVALID, ball);
                encounters.RemoveAll(enc => !APILegality.IsRequestedBallValid(set, enc));
            }

            return string.Format(
                EXHAUSTED_ENCOUNTERS,
                initialcount - encounters.Count,
                initialcount
            );
        }

        private static ushort[] GetValidMoves(
            IBattleTemplate set,
            ITrainerInfo sav,
            List<IEnumerable<ushort>> move_combinations,
            PKM blank,
            GameVersion[] gamelist
        )
        {
            ushort[] successful_combination = Array.Empty<ushort>();
            foreach (var c in move_combinations)
            {
                var combination = c.ToArray();
                if (combination.Length <= successful_combination.Length)
                    continue;
                var new_moves = combination
                    .Concat(Enumerable.Repeat<ushort>(0, 4 - combination.Length))
                    .ToArray();
                blank.ApplySetDetails(set);
                blank.SetMoves(new_moves);
                blank.SetRecordFlags(Array.Empty<ushort>());

                if (sav.Generation <= 2)
                    blank.EXP = 0; // no relearn moves in gen 1/2 so pass level 1 to generator

                var encounters = EncounterMovesetGenerator.GenerateEncounters(
                    pk: blank,
                    moves: new_moves,
                    gamelist
                );
                if (set is RegenTemplate r && r.Regen.EncounterFilters is { } x)
                    encounters = encounters.Where(enc => BatchEditing.IsFilterMatch(x, enc));
                if (encounters.Any())
                    successful_combination = combination.ToArray();
            }
            return successful_combination;
        }

        private static IEnumerable<IEnumerable<T>> GetKCombs<T>(IEnumerable<T> list, int length)
            where T : IComparable
        {
            if (length == 1)
                return list.Select(t => new[] { t });

            var temp = list.ToArray();
            return GetKCombs(temp, length - 1)
                .SelectMany(
                    collectionSelector: t => temp.Where(o => o.CompareTo(t.Last()) > 0),
                    resultSelector: (t1, t2) => t1.Concat(new[] { t2 })
                );
        }
    }
}
