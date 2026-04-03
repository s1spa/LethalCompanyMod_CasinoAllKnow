using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace CasinoCheat
{
    // Скільки секунд показувати результат якщо гравець НЕ підходив до машини заздалегідь
    internal static class Config { public const float FallbackDelay = 3f; }

    // ── Shared helpers ────────────────────────────────────────────────────────
    internal static class H
    {
        public static int ExecStage(MonoBehaviour inst)
        {
            var f = AccessTools.Field(typeof(NetworkBehaviour), "__rpc_exec_stage");
            return f == null ? 0 : (int)f.GetValue(inst);
        }

        public static Dictionary<string, AudioClip>? Sounds()
        {
            var t = AccessTools.TypeByName("LethalCasino.Plugin");
            return AccessTools.Field(t, "Sounds")?.GetValue(null) as Dictionary<string, AudioClip>;
        }
    }

    // ── SlotMachine ───────────────────────────────────────────────────────────
    [HarmonyPatch]
    internal static class SlotMachinePatch
    {
        private static readonly string[] Icons =
            { "bell", "lime", "cherry", "bar", "orange", "seven", "skull", "lime", "lemon", "cherry", "lemon", "orange" };

        static MethodBase TargetMethod() =>
            AccessTools.Method("LethalCasino.Custom.SlotMachine:StartGambleClientRpc");

        [HarmonyPrefix]
        static void Prefix(MonoBehaviour __instance, ref int[] results, ref string resultOutcome)
        {
            if (!PreviewManager.IsHost) return;
            if (H.ExecStage(__instance) == 1) return; // клієнт-виконання

            int id = ((Component)__instance).GetInstanceID();
            if (PreviewManager.TryConsumeSlot(id, out var stored, out var storedOutcome))
            {
                results = stored;
                resultOutcome = storedOutcome;
            }
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance, int[] results, string resultOutcome)
        {
            if (H.ExecStage(__instance) != 1) return;

            var gField = AccessTools.Field(__instance.GetType(), "gambleInProgress");
            if (gField == null || !(bool)gField.GetValue(__instance)) return;

            int id = ((Component)__instance).GetInstanceID();
            if (!PreviewManager.MachinesWithStoredResult.Contains(id)) return; // вже показали на підході

            // Fallback: показуємо і затримуємо
            string r0 = Ico(results[0]); string r1 = Ico(results[1]); string r2 = Ico(results[2]);
            CheatHud.ShowMessage($"[SLOTS] {r0} | {r1} | {r2}\n{Label(resultOutcome)}", Color.yellow, Config.FallbackDelay + 5f);
            gField.SetValue(__instance, false);
            var tField = AccessTools.Field(__instance.GetType(), "gambleTime");
            __instance.StartCoroutine(DelaySlot(__instance, gField, tField));
        }

        static IEnumerator DelaySlot(MonoBehaviour inst, FieldInfo gf, FieldInfo? tf)
        {
            yield return new WaitForSeconds(Config.FallbackDelay);
            gf.SetValue(inst, true);
            tf?.SetValue(inst, 0f);
        }

        static string Ico(int i) => i >= 0 && i < Icons.Length ? Icons[i].ToUpper() : "?";
        static string Label(string o) => o switch
        {
            "three_sevens" => "777 — ДЖЕКПОТ!",        "three_bars"   => "BAR BAR BAR — Великий виграш",
            "three_bells"  => "BELL BELL BELL — Виграш","three_fruits" => "Три фрукти — Виграш",
            "two_match"    => "Два однакових — Малий виграш",
            "no_matches"   => "Нічого — Програш",       "three_skulls" => "SKULL — Смерть!",
            _              => o ?? "?"
        };
    }

    // ── Roulette ──────────────────────────────────────────────────────────────
    [HarmonyPatch]
    internal static class RoulettePatch
    {
        private static readonly int[] WheelNums =
        {
            0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11,
            30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18,
            29, 7, 28, 12, 35, 3, 26
        };
        private static readonly int[] Reds = { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };

        static MethodBase TargetMethod() =>
            AccessTools.Method("LethalCasino.Custom.Roulette:SpinWheelClientRpc");

        [HarmonyPrefix]
        static void Prefix(MonoBehaviour __instance, ref int resultDisplayIdx)
        {
            if (!PreviewManager.IsHost) return;
            if (H.ExecStage(__instance) == 1) return;

            int id = ((Component)__instance).GetInstanceID();
            if (PreviewManager.TryConsumeRoulette(id, out int stored))
                resultDisplayIdx = stored;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance, int resultDisplayIdx)
        {
            if (H.ExecStage(__instance) != 1) return;

            var sf = AccessTools.Field(__instance.GetType(), "isWheelSpinning");
            if (sf == null || !(bool)sf.GetValue(__instance)) return;

            int id = ((Component)__instance).GetInstanceID();
            if (!PreviewManager.MachinesWithStoredResult.Contains(id)) return;

            // Fallback
            if (resultDisplayIdx >= 0 && resultDisplayIdx < WheelNums.Length)
            {
                int n = WheelNums[resultDisplayIdx];
                bool red = IsRed(n);
                string c = n == 0 ? "ЗЕЛЕНЕ" : red ? "ЧЕРВОНЕ" : "ЧОРНЕ";
                Color col = n == 0 ? Color.green : red ? Color.red : Color.white;
                CheatHud.ShowMessage($"[РУЛЕТКА] Випаде: {n}\n{c}", col, Config.FallbackDelay + 6f);
            }
            sf.SetValue(__instance, false);
            var af = AccessTools.Field(__instance.GetType(), "audioSource");
            var audio = af?.GetValue(__instance) as AudioSource;
            audio?.Stop();
            __instance.StartCoroutine(DelayRoulette(__instance, sf, audio));
        }

        static IEnumerator DelayRoulette(MonoBehaviour inst, FieldInfo sf, AudioSource? audio)
        {
            yield return new WaitForSeconds(Config.FallbackDelay);
            sf.SetValue(inst, true);
            if (audio != null) { var s = H.Sounds(); if (s != null && s.TryGetValue("RouletteBallRoll", out var c)) audio.PlayOneShot(c); }
        }

        static bool IsRed(int n) { foreach (int r in Reds) if (r == n) return true; return false; }
    }

    // ── TheWheel ─────────────────────────────────────────────────────────────
    [HarmonyPatch]
    internal static class TheWheelPatch
    {
        private static readonly int[] Zeros    = { 3, 5, 8, 12, 14, 16, 18, 20 };
        private static readonly int[] Lose     = { 1, 2, 6, 9, 11, 13, 17 };
        private static readonly int[] Win      = { 0, 7, 10, 19, 21 };
        private static readonly int[] Jackpot  = { 4, 15 };
        private const float Slice = 16.363636f;

        static MethodBase TargetMethod() =>
            AccessTools.Method("LethalCasino.Custom.TheWheel:SpinWheelClientRpc");

        [HarmonyPrefix]
        static void Prefix(MonoBehaviour __instance, ref float wheelTargetRotation)
        {
            if (!PreviewManager.IsHost) return;
            if (H.ExecStage(__instance) == 1) return;

            int id = ((Component)__instance).GetInstanceID();
            if (PreviewManager.TryConsumeWheel(id, out float stored))
                wheelTargetRotation = stored;
        }

        [HarmonyPostfix]
        static void Postfix(MonoBehaviour __instance, float wheelTargetRotation)
        {
            if (H.ExecStage(__instance) != 1) return;

            var sf = AccessTools.Field(__instance.GetType(), "isWheelSpinning");
            if (sf == null || !(bool)sf.GetValue(__instance)) return;

            int id = ((Component)__instance).GetInstanceID();
            if (!PreviewManager.MachinesWithStoredResult.Contains(id)) return;

            // Fallback
            int si = Mathf.FloorToInt(wheelTargetRotation / Slice) % 22;
            string label; Color color;
            if (Has(Jackpot, si))    { label = "ДЖЕКПОТ! (125%+)"; color = Color.yellow; }
            else if (Has(Win, si))   { label = "ВИГРАШ (25-125%)"; color = Color.green;  }
            else if (Has(Lose, si))  { label = "ПРОГРАШ (<25%)";   color = Color.red;    }
            else                     { label = "НУЛЬ";             color = Color.gray;   }
            CheatHud.ShowMessage($"[КОЛЕСО] {label}", color, Config.FallbackDelay + 6f);

            sf.SetValue(__instance, false);
            var af = AccessTools.Field(__instance.GetType(), "audioSource");
            var audio = af?.GetValue(__instance) as AudioSource;
            audio?.Stop();
            __instance.StartCoroutine(DelayWheel(__instance, sf, audio));
        }

        static IEnumerator DelayWheel(MonoBehaviour inst, FieldInfo sf, AudioSource? audio)
        {
            yield return new WaitForSeconds(Config.FallbackDelay);
            sf.SetValue(inst, true);
            if (audio != null) { var s = H.Sounds(); if (s != null && s.TryGetValue("WheelTokenDrop", out var c)) audio.PlayOneShot(c, 1f); }
        }

        static bool Has(int[] arr, int v) { foreach (int x in arr) if (x == v) return true; return false; }
    }
}
