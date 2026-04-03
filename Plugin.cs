using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CasinoCheat
{
    [BepInPlugin("yourcheats.CasinoCheat", "CasinoCheat", "1.0.0")]
    [BepInDependency("mrgrm7.LethalCasino")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;
        private static Harmony harmony = null!;

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony("yourcheats.CasinoCheat");
            harmony.PatchAll();

            // Створюємо HUD одразу щоб Update() слухав хоткей Alt+F8
            var hudGo = new GameObject("CasinoCheatHUD");
            hudGo.AddComponent<CheatHud>();
            DontDestroyOnLoad(hudGo);

            Log.LogInfo("CasinoCheat loaded! Alt+F8 to enable.");
        }
    }
}
