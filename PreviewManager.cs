using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LethalCasino;
using LethalCasino.Config;
using Unity.Netcode;
using UnityEngine;

namespace CasinoCheat
{
    /// <summary>
    /// Генерує результати наперед (коли гравець підходить до машини)
    /// і зберігає їх щоб примусово використати під час реального спіну.
    /// Працює тільки якщо гравець — хост.
    /// </summary>
    internal static class PreviewManager
    {
        // ── Enabled state ─────────────────────────────────────────────────────
        public static bool IsEnabled = false; // вмикається через Alt+F8

        // Збережені результати: ключ = instanceId машини
        private static readonly Dictionary<int, int> _rouletteResults  = new();
        private static readonly Dictionary<int, float> _wheelRotations  = new();
        private static readonly Dictionary<int, SlotResult> _slotResults = new();

        // Щоб не перегенеровувати кожен кадр
        private static int _lastHoveredGoId = -1;
        private static int _lastHoveredMachineId = -1;

        // Кеш GO → тип машини (щоб не викликати GetComponentInParent кожного разу)
        private enum MachineType { Unknown, None, Slot, Roulette, Wheel }
        private static readonly Dictionary<int, MachineType> _goTypeCache = new();

        // Кешовані типи (завантажуються один раз)
        private static System.Type? _slotType;
        private static System.Type? _rouletteType;
        private static System.Type? _wheelType;
        private static bool _typesLoaded;

        private static void EnsureTypes()
        {
            if (_typesLoaded) return;
            _slotType     = AccessTools.TypeByName("LethalCasino.Custom.SlotMachine");
            _rouletteType = AccessTools.TypeByName("LethalCasino.Custom.Roulette");
            _wheelType    = AccessTools.TypeByName("LethalCasino.Custom.TheWheel");
            _typesLoaded  = true;
        }

        // Прапор для Patches.cs — щоб знати що результат уже відомий і не затримувати анімацію
        public static readonly HashSet<int> MachinesWithStoredResult = new();

        public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        // ── Виклик з HoverPatch кожен кадр ──────────────────────────────────
        public static void OnHover(GameObject hoveredGo)
        {
            if (!IsHost || !IsEnabled) return;

            int goId = hoveredGo.GetInstanceID();
            if (goId == _lastHoveredGoId) return; // Той самий об'єкт — нічого не робимо
            _lastHoveredGoId = goId;

            EnsureTypes();

            // Беремо тип з кешу або визначаємо один раз
            if (!_goTypeCache.TryGetValue(goId, out MachineType cached))
            {
                cached = MachineType.None;
                if (_slotType     != null && hoveredGo.GetComponentInParent(_slotType)     != null) cached = MachineType.Slot;
                else if (_rouletteType != null && hoveredGo.GetComponentInParent(_rouletteType) != null) cached = MachineType.Roulette;
                else if (_wheelType    != null && hoveredGo.GetComponentInParent(_wheelType)    != null) cached = MachineType.Wheel;
                _goTypeCache[goId] = cached;
            }

            if (cached == MachineType.None) { _lastHoveredMachineId = -1; CheatHud.Hide(); return; }

            Component? comp = cached switch
            {
                MachineType.Slot     => hoveredGo.GetComponentInParent(_slotType!),
                MachineType.Roulette => hoveredGo.GetComponentInParent(_rouletteType!),
                MachineType.Wheel    => hoveredGo.GetComponentInParent(_wheelType!),
                _                    => null
            };
            if (comp == null) return;

            int machineId = comp.GetInstanceID();
            if (machineId == _lastHoveredMachineId) return; // Та сама машина — вже показали
            _lastHoveredMachineId = machineId;

            switch (cached)
            {
                case MachineType.Slot:     GenerateSlot(machineId);     break;
                case MachineType.Roulette: GenerateRoulette(machineId); break;
                case MachineType.Wheel:    GenerateWheel(machineId);    break;
            }
        }

        public static void OnStopHovering()
        {
            _lastHoveredGoId = -1;
            CheatHud.Hide();
        }

        // ── Roulette ─────────────────────────────────────────────────────────
        private static readonly int[] WheelNumbers =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11,
            30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18,
            29, 7, 28, 12, 35, 3, 26
        };
        private static readonly int[] RedNumbers =
            { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        private static void GenerateRoulette(int machineId)
        {
            int idx = new System.Random().Next(0, 37);
            _rouletteResults[machineId] = idx;
            MachinesWithStoredResult.Add(machineId);

            int number = WheelNumbers[idx];
            string colorName = number == 0 ? "ЗЕЛЕНЕ" : IsRed(number) ? "ЧЕРВОНЕ" : "ЧОРНЕ";
            string parity    = number == 0 ? "" : number % 2 == 0 ? " | ПАРНЕ" : " | НЕПАРНЕ";
            string half      = number == 0 ? "" : number <= 18 ? " | 1-18" : " | 19-36";
            Color hudColor   = number == 0 ? Color.green : IsRed(number) ? Color.red : Color.white;

            CheatHud.ShowMessage($"[РУЛЕТКА] Наступне: {number}\n{colorName}{parity}{half}", hudColor, 999f);
        }

        public static bool TryConsumeRoulette(int machineId, out int idx)
        {
            if (_rouletteResults.TryGetValue(machineId, out idx))
            {
                _rouletteResults.Remove(machineId);
                MachinesWithStoredResult.Remove(machineId);
                _lastHoveredMachineId = -1;
                CheatHud.Hide();
                return true;
            }
            return false;
        }

        private static bool IsRed(int n) { foreach (int r in RedNumbers) if (r == n) return true; return false; }

        // ── TheWheel ─────────────────────────────────────────────────────────
        private static readonly int[] SlicesZeros   = { 3, 5, 8, 12, 14, 16, 18, 20 };
        private static readonly int[] SlicesLose    = { 1, 2, 6, 9, 11, 13, 17 };
        private static readonly int[] SlicesWin     = { 0, 7, 10, 19, 21 };
        private static readonly int[] SlicesJackpot = { 4, 15 };
        private const float OneWheelSlice = 16.363636f;

        private static void GenerateWheel(int machineId)
        {
            var rand = new System.Random();
            var configValues = LethalCasino.Plugin.configFile?.configValues;

            // Кумулятивні ймовірності
            float total = 0f;
            var raw = new Dictionary<string, float>();
            if (configValues != null)
                foreach (var kv in configValues)
                    if (kv.Key.StartsWith(Constants.WHEEL_CHANCE_PREFIX))
                    { raw[kv.Key] = kv.Value; total += kv.Value; }

            if (total <= 0f)
            {
                raw[Constants.WHEEL_CHANCE_JACKPOT] = 3f;
                raw[Constants.WHEEL_CHANCE_WIN]     = 22f;
                raw[Constants.WHEEL_CHANCE_LOSE]    = 40f;
                raw[Constants.WHEEL_CHANCE_ZERO]    = 35f;
                total = 100f;
            }

            float cumul = 0f;
            var cumProbs = new Dictionary<string, float>();
            foreach (var kv in raw) { cumul += kv.Value / total * 100f; cumProbs[kv.Key] = cumul; }

            double roll = rand.NextDouble() * 100.0;
            string outcome = Constants.WHEEL_CHANCE_ZERO;
            foreach (var kv in cumProbs) if (roll < kv.Value) { outcome = kv.Key; break; }

            int[] slices = outcome == Constants.WHEEL_CHANCE_JACKPOT ? SlicesJackpot :
                           outcome == Constants.WHEEL_CHANCE_WIN     ? SlicesWin     :
                           outcome == Constants.WHEEL_CHANCE_LOSE    ? SlicesLose    : SlicesZeros;

            int sliceNum = slices[rand.Next(slices.Length)];
            float offset = (float)rand.NextDouble() * OneWheelSlice - OneWheelSlice / 2f;
            float rotation = sliceNum * OneWheelSlice + offset;

            _wheelRotations[machineId] = rotation;
            MachinesWithStoredResult.Add(machineId);

            string label; Color color;
            if (outcome == Constants.WHEEL_CHANCE_JACKPOT) { label = "ДЖЕКПОТ! (125%+)"; color = Color.yellow; }
            else if (outcome == Constants.WHEEL_CHANCE_WIN) { label = "ВИГРАШ (25-125%)"; color = Color.green; }
            else if (outcome == Constants.WHEEL_CHANCE_LOSE) { label = "ПРОГРАШ (<25%)"; color = Color.red; }
            else { label = "НУЛЬ"; color = Color.gray; }

            CheatHud.ShowMessage($"[КОЛЕСО] Наступне: {label}", color, 999f);
        }

        public static bool TryConsumeWheel(int machineId, out float rotation)
        {
            if (_wheelRotations.TryGetValue(machineId, out rotation))
            {
                _wheelRotations.Remove(machineId);
                MachinesWithStoredResult.Remove(machineId);
                _lastHoveredMachineId = -1;
                CheatHud.Hide();
                return true;
            }
            return false;
        }

        // ── SlotMachine ───────────────────────────────────────────────────────
        internal struct SlotResult { public int[] Results; public string Outcome; }

        private static readonly Dictionary<string, int[]> IconLocations = new()
        {
            { "bell",   new[] { 0 } },       { "lime",   new[] { 1, 7 } },
            { "cherry", new[] { 2, 9 } },    { "bar",    new[] { 3 } },
            { "orange", new[] { 4, 11 } },   { "seven",  new[] { 5 } },
            { "skull",  new[] { 6 } },       { "lemon",  new[] { 8, 10 } }
        };
        private static readonly string[] ReelIcons =
            { "bell", "lime", "cherry", "bar", "orange", "seven", "skull", "lime", "lemon", "cherry", "lemon", "orange" };
        private static readonly string[] Fruits = { "lime", "cherry", "orange", "lemon" };

        private static void GenerateSlot(int machineId)
        {
            var rand = new System.Random();
            var configValues = LethalCasino.Plugin.configFile?.configValues;

            float total = 0f;
            var raw = new Dictionary<string, float>();
            if (configValues != null)
                foreach (var kv in configValues)
                    if (kv.Key.StartsWith(Constants.SLOTS_CHANCE_PREFIX))
                    { raw[kv.Key] = kv.Value; total += kv.Value; }

            if (total <= 0f)
            {
                raw[Constants.SLOTS_CHANCE_THREE_MATCH_SEVENS]      = 1f;
                raw[Constants.SLOTS_CHANCE_THREE_MATCH_BARS_OR_BELLS] = 2f;
                raw[Constants.SLOTS_CHANCE_THREE_MATCH_FRUITS]      = 7f;
                raw[Constants.SLOTS_CHANCE_PAIR]                    = 35.5f;
                raw[Constants.SLOTS_CHANCE_THREE_SKULLS]            = 13.5f;
                raw[Constants.SLOTS_CHANCE_NO_MATCHES]              = 41f;
                total = 100f;
            }

            float cumul = 0f;
            var cumProbs = new Dictionary<string, float>();
            foreach (var kv in raw) { cumul += kv.Value / total * 100f; cumProbs[kv.Key] = cumul; }

            double roll = rand.NextDouble() * 100.0;
            string outcomeKey = Constants.SLOTS_CHANCE_NO_MATCHES;
            foreach (var kv in cumProbs) if (roll < kv.Value) { outcomeKey = kv.Key; break; }

            var results = new int[3];
            string resultOutcome;
            var iconList = IconLocations.Keys.ToList();

            if (outcomeKey == Constants.SLOTS_CHANCE_THREE_MATCH_SEVENS)
            {
                resultOutcome = "three_sevens";
                for (int i = 0; i < 3; i++) results[i] = Pick("seven", rand);
            }
            else if (outcomeKey == Constants.SLOTS_CHANCE_THREE_MATCH_BARS_OR_BELLS)
            {
                if (rand.Next(2) == 0) { resultOutcome = "three_bars";  for (int i = 0; i < 3; i++) results[i] = Pick("bar",  rand); }
                else                   { resultOutcome = "three_bells"; for (int i = 0; i < 3; i++) results[i] = Pick("bell", rand); }
            }
            else if (outcomeKey == Constants.SLOTS_CHANCE_THREE_MATCH_FRUITS)
            {
                resultOutcome = "three_fruits";
                string fruit = Fruits[rand.Next(Fruits.Length)];
                for (int i = 0; i < 3; i++) results[i] = Pick(fruit, rand);
            }
            else if (outcomeKey == Constants.SLOTS_CHANCE_PAIR)
            {
                resultOutcome = "two_match";
                string ico1 = iconList[rand.Next(iconList.Count)]; iconList.Remove(ico1);
                string ico2 = iconList[rand.Next(iconList.Count)];
                results[0] = Pick(ico1, rand); results[1] = Pick(ico2, rand); results[2] = Pick(ico2, rand);
            }
            else if (outcomeKey == Constants.SLOTS_CHANCE_THREE_SKULLS)
            {
                resultOutcome = "three_skulls";
                for (int i = 0; i < 3; i++) results[i] = Pick("skull", rand);
            }
            else
            {
                resultOutcome = "no_matches";
                for (int i = 0; i < 3; i++) { string ico = iconList[rand.Next(iconList.Count)]; results[i] = Pick(ico, rand); iconList.Remove(ico); }
            }

            _slotResults[machineId] = new SlotResult { Results = results, Outcome = resultOutcome };
            MachinesWithStoredResult.Add(machineId);

            string r0 = ReelIcons[results[0]].ToUpper();
            string r1 = ReelIcons[results[1]].ToUpper();
            string r2 = ReelIcons[results[2]].ToUpper();
            CheatHud.ShowMessage($"[SLOTS] Наступне: {r0} | {r1} | {r2}\n{OutcomeLabel(resultOutcome)}", Color.yellow, 999f);
        }

        public static bool TryConsumeSlot(int machineId, out int[] results, out string outcome)
        {
            if (_slotResults.TryGetValue(machineId, out var stored))
            {
                results = stored.Results; outcome = stored.Outcome;
                _slotResults.Remove(machineId);
                MachinesWithStoredResult.Remove(machineId);
                _lastHoveredMachineId = -1;
                CheatHud.Hide();
                return true;
            }
            results = null!; outcome = null!;
            return false;
        }

        private static int Pick(string icon, System.Random rand)
        {
            var set = IconLocations[icon];
            return set[rand.Next(set.Length)];
        }

        private static string OutcomeLabel(string o) => o switch
        {
            "three_sevens" => "777 — ДЖЕКПОТ!",
            "three_bars"   => "BAR BAR BAR — Великий виграш",
            "three_bells"  => "BELL BELL BELL — Великий виграш",
            "three_fruits" => "Три фрукти — Виграш",
            "two_match"    => "Два однакових — Малий виграш",
            "no_matches"   => "Нічого — Програш",
            "three_skulls" => "SKULL SKULL SKULL — Смерть!",
            _              => o ?? "?"
        };
    }
}
