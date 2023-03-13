using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using System.IO;
using System;
using Unity.XR.OpenVR;
using HarmonyLib;
using Valve.VR;
using UnityEngine;
using ValheimVRMod.Utilities;

using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.Patches
{
    [HarmonyPatch(typeof(VisEquipment), "AttachItem")]
    class PatchAttachItemRemoveLight
    {
        static void Postfix(VisEquipment __instance, GameObject __result, int itemHash)
        {

            if (__instance.m_isPlayer && __result != null && itemHash == 703889544)
            {
                var lights = __result.GetComponentsInChildren<Light>();
                if (lights.Length > 1)
                {
                    LogUtils.LogChildTree(__result.transform);
                    lights[0].intensity = 0;
                    // lights[0].enabled = false;
                }
            }
        }
    }

    class LightUpdater : MonoBehaviour
    {
        private bool isLocalPlayer;

        void Awake()
        {
            Player player = gameObject.GetComponentInParent<Player>();
            isLocalPlayer = (player != null && player == Player.m_localPlayer);
        }

        void Update()
        {
            if (isLocalPlayer)
            {
                var lights = gameObject.GetComponentsInChildren<Light>();
                if (lights.Length > 1)
                {
                    // LogUtils.LogChildTree(gameObject.transform);
                    // LogUtils.LogWarning("Lights: " + lights.Length + " --- " + lights[0].name + " --- " + lights[1].name);
                    lights[1].intensity = 0;
                }
            }
        }
    }
}
