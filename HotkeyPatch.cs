using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace CasinoCheat
{
    /// <summary>
    /// Патч на PlayerControllerB.Update — найнадійніше місце для перевірки хоткеїв у LC.
    /// Alt+F8 — вмикає/вимикає чіт.
    /// </summary>
    [HarmonyPatch]
    internal static class HotkeyPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method("GameNetcodeStuff.PlayerControllerB:Update");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            // Тільки для локального гравця
            if (__instance is not UnityEngine.MonoBehaviour mb) return;
            var nb = mb as Unity.Netcode.NetworkBehaviour;
            if (nb == null || !nb.IsOwner) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            bool altHeld = kb[Key.LeftAlt].isPressed || kb[Key.RightAlt].isPressed;
            if (altHeld && kb[Key.F8].wasPressedThisFrame)
            {
                PreviewManager.IsEnabled = !PreviewManager.IsEnabled;
                string state = PreviewManager.IsEnabled ? "УВІМКНЕНО" : "ВИМКНЕНО";
                CheatHud.ShowMessage(
                    $"[CasinoCheat] {state}",
                    PreviewManager.IsEnabled ? UnityEngine.Color.green : UnityEngine.Color.red,
                    2f
                );

                if (!PreviewManager.IsEnabled)
                    PreviewManager.OnStopHovering();
            }
        }
    }
}
