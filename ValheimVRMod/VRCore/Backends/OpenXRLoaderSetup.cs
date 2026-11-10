using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.VRCore.Backends
{
    // The only code that names types from Unity.XR.OpenXR.dll and from the mod's VHVR.OpenXR.dll, so that the
    // assemblies are loaded only when the OpenXR runtime is chosen, and only once VR is being initialized.
    static class OpenXRLoaderSetup
    {
        // Creates, at runtime, the XR Plug-in Management settings that a Unity project would build into its player
        // for OpenXR: the SteamVR runtime loads its own from the xrmanager asset bundle instead. Both settings objects
        // register themselves as the runtime instance from Awake(), which runs inside CreateInstance().
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static XRManagerSettings CreateManager()
        {
            if (XRGeneralSettings.Instance != null)
            {
                LogError("XR Plug-in Management settings already exist; not creating the OpenXR ones.");
                return null;
            }

            OpenXRSettings openXrSettings = ScriptableObject.CreateInstance<OpenXRSettings>();
            // Multi pass, as with OpenVR (Open VR Settings.asset): Valheim's shaders are not instancing aware.
            openXrSettings.renderMode = OpenXRSettings.RenderMode.MultiPass;
            var features = createInteractionProfiles();
            // OpenXRSettings.features is internal: a Unity project fills it in the editor, from the features found in
            // the project. The loader reads it when it initializes, so it must be set before.
            FieldInfo featuresField = typeof(OpenXRSettings).GetField("features", BindingFlags.Instance | BindingFlags.NonPublic);
            if (featuresField == null)
            {
                LogError("OpenXRSettings.features not found: the controllers will not be available.");
            }
            else
            {
                // Typed in this method's body only: no signature of the mod may name an OpenXR type.
                var typed = new OpenXRFeature[features.Length];
                for (int i = 0; i < features.Length; i++)
                {
                    typed[i] = (OpenXRFeature)features[i];
                }
                featuresField.SetValue(openXrSettings, typed);
            }

            // The loader for runtimes without pre-initialization: the mod is loaded long after the graphics device
            // is created, so the runtime gets the device Unity already has.
            OpenXRLoaderNoPreInit loader = ScriptableObject.CreateInstance<OpenXRLoaderNoPreInit>();
            loader.name = "OpenXR Loader";

            XRManagerSettings manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = "OpenXR Manager Settings";
            manager.automaticLoading = false;
            manager.automaticRunning = false;
            // The loaders list is the one to change at runtime: TryAddLoader() only accepts loaders registered in
            // the editor, and InitializeLoaderSync() goes through this list without checking registration.
#pragma warning disable CS0618
            manager.loaders.Add(loader);
#pragma warning restore CS0618

            XRGeneralSettings general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            general.name = "OpenXR General Settings";
            general.Manager = manager;
            // Kept for the session: Valheim unloads unused assets on every scene change.
            foreach (Object settings in new Object[] { openXrSettings, loader, manager, general })
            {
                settings.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            foreach (ScriptableObject feature in features)
            {
                feature.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }

            if (XRGeneralSettings.Instance != general)
            {
                LogError("The OpenXR settings did not register as the XR Plug-in Management settings.");
                return null;
            }
            return manager;
        }

        // The interaction profiles, each a feature that suggests its controller's bindings to the runtime and gives the
        // controller as an Input System device of its own layout, which is what the mod reads (OpenXRController, with
        // the bindings of OpenXRProfiles for that layout). None needs an
        // extension, except the last three, each enabled only where the runtime has its extension. The runtime picks
        // the profile of the controllers in use, or remaps them to one of these; the Khronos simple controller is the
        // last resort.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ScriptableObject[] createInteractionProfiles()
        {
            var types = new[]
            {
                typeof(OculusTouchControllerProfile),
                typeof(ValveIndexControllerProfile),
                typeof(HTCViveControllerProfile),
                typeof(MicrosoftMotionControllerProfile),
                typeof(KHRSimpleControllerProfile),
                typeof(MetaQuestTouchPlusControllerProfile),
                typeof(MetaQuestTouchProControllerProfile),
                typeof(HPReverbG2ControllerProfile),
                // Not a profile: VHVR's own OpenXR actions, which it suggests bindings for on the profiles above.
                typeof(VHVR.OpenXR.ActionsFeature),
            };
            // The extension each needs, which a project takes from the feature's attribute when it is built: the
            // plugin requests the extensions of the enabled features, and a feature whose extension the runtime has
            // not enabled turns itself off when the instance is created.
            var extensions = new Dictionary<System.Type, string>
            {
                { typeof(MetaQuestTouchPlusControllerProfile), "XR_META_touch_controller_plus" },
                { typeof(MetaQuestTouchProControllerProfile), "XR_FB_touch_controller_pro" },
                { typeof(HPReverbG2ControllerProfile), "XR_EXT_hp_mixed_reality_controller" },
            };
            FieldInfo extensionStrings =
                typeof(OpenXRFeature).GetField("openxrExtensionStrings", BindingFlags.Instance | BindingFlags.NonPublic);
            // Filled in from the feature's attribute when a project is built, and only used to sort and log.
            FieldInfo nameUi = typeof(OpenXRFeature).GetField("nameUi", BindingFlags.Instance | BindingFlags.NonPublic);
            var features = new ScriptableObject[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                var feature = (OpenXRFeature)ScriptableObject.CreateInstance(types[i]);
                feature.name = types[i].Name;
                nameUi?.SetValue(feature, types[i].Name);
                if (extensions.TryGetValue(types[i], out string extension))
                {
                    if (extensionStrings == null)
                    {
                        LogWarning("OpenXRFeature.openxrExtensionStrings not found: " + types[i].Name + " is not available.");
                        feature.enabled = false;
                        features[i] = feature;
                        continue;
                    }
                    extensionStrings.SetValue(feature, extension);
                }
                feature.enabled = true;
                features[i] = feature;
            }
            return features;
        }

        // VHVR's OpenXR actions (OpenXRInputBackend.BuildManifest), for VHVR.OpenXR's ActionsFeature to create with
        // the instance. Before CreateManager().
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ConfigureActions(OpenXRActionManifest manifest)
        {
            var sets = new VHVR.OpenXR.ActionsFeature.SetSpec[manifest.Sets.Count];
            for (int i = 0; i < sets.Length; i++)
            {
                sets[i] = new VHVR.OpenXR.ActionsFeature.SetSpec { Name = manifest.Sets[i].Name, LocalizedName = manifest.Sets[i].LocalizedName };
            }
            var actions = new VHVR.OpenXR.ActionsFeature.ActionSpec[manifest.Actions.Count];
            for (int i = 0; i < actions.Length; i++)
            {
                OpenXRActionManifest.Action action = manifest.Actions[i];
                actions[i] = new VHVR.OpenXR.ActionsFeature.ActionSpec
                {
                    Set = action.Set,
                    Name = action.Name,
                    LocalizedName = action.LocalizedName,
                    Kind = action.IsVector2 ? VHVR.OpenXR.ActionsFeature.Vector2Action : VHVR.OpenXR.ActionsFeature.BooleanAction,
                };
            }
            var bindings = new VHVR.OpenXR.ActionsFeature.BindingSpec[manifest.Bindings.Count];
            for (int i = 0; i < bindings.Length; i++)
            {
                OpenXRActionManifest.Binding binding = manifest.Bindings[i];
                bindings[i] = new VHVR.OpenXR.ActionsFeature.BindingSpec { Profile = binding.Profile, Action = binding.Action, Path = binding.Path };
            }
            VHVR.OpenXR.ActionsFeature.Sets = sets;
            VHVR.OpenXR.ActionsFeature.Actions = actions;
            VHVR.OpenXR.ActionsFeature.Bindings = bindings;
            VHVR.OpenXR.ActionsFeature.LogInfo = LogInfo;
            VHVR.OpenXR.ActionsFeature.LogWarning = LogWarning;
            LogInfo("OpenXR actions: " + actions.Length + " in " + sets.Length + " sets, " + bindings.Length + " suggested bindings.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ActionsAttached() => VHVR.OpenXR.ActionsFeature.IsAttached;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void SetActionSetActive(int set, bool active) => VHVR.OpenXR.ActionsFeature.SetActive(set, active);

        // An action's state on a hand (0 left, 1 right); false when the runtime has it unbound there.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ReadBoolean(int action, int hand, out bool state) =>
            VHVR.OpenXR.ActionsFeature.GetBoolean(action, hand, out state);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool ReadVector2(int action, int hand, out Vector2 value)
        {
            bool bound = VHVR.OpenXR.ActionsFeature.GetVector2(action, hand, out float x, out float y);
            value = new Vector2(x, y);
            return bound;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void LogRuntime()
        {
            LogInfo("OpenXR runtime: " + UnityEngine.XR.OpenXR.OpenXRRuntime.name + " " +
                    UnityEngine.XR.OpenXR.OpenXRRuntime.version + ", API " + UnityEngine.XR.OpenXR.OpenXRRuntime.apiVersion +
                    ", plugin " + UnityEngine.XR.OpenXR.OpenXRRuntime.pluginVersion);
        }
    }
}
