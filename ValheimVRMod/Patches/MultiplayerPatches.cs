using HarmonyLib;
using ValheimVRMod.Scripts;

namespace ValheimVRMod.Patches {

    [HarmonyPatch(typeof(Player), "Start")]
    class PatchPlayerAwake {
        public static void Postfix(Player __instance) {
            if (__instance == Player.m_localPlayer) {
                return;
            }
            __instance.gameObject.AddComponent<VRPlayerSync>();
        }
    }
}