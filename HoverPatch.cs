using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace CasinoCheat
{
    [HarmonyPatch]
    internal static class HoverPatch
    {
        // Кешуємо FieldInfo щоб не шукати кожного разу
        private static FieldInfo? _hoverField;

        static MethodBase TargetMethod() =>
            AccessTools.Method("GameNetcodeStuff.PlayerControllerB:SetHoverTipAndCurrentInteractTrigger");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            // Швидка перевірка без важких операцій
            if (!PreviewManager.IsEnabled || !PreviewManager.IsHost) return;

            // Перевіряємо що це локальний гравець (кешований cast)
            if (__instance is not NetworkBehaviour nb || !nb.IsOwner) return;

            // Кешуємо FieldInfo (один раз)
            if (_hoverField == null)
                _hoverField = AccessTools.Field(__instance.GetType(), "hoveringOverTrigger");

            var hovering = _hoverField?.GetValue(__instance) as Component;

            if (hovering == null)
            {
                PreviewManager.OnStopHovering();
                return;
            }

            PreviewManager.OnHover(hovering.gameObject);
        }
    }
}
