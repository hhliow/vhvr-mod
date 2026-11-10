using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.XR.OpenXR.Features;

namespace VHVR.OpenXR
{
    // VHVR's own OpenXR actions, declared as an OpenXR application declares its actions: VHVR's action sets and actions
    // (Jump, Use, ToggleMenu...), with suggested bindings for each interaction profile. A runtime with a binding UI
    // (SteamVR's) then shows VHVR's actions and lets them be rebound.
    //
    // The OpenXR plugin creates, suggests, attaches and syncs only its own action sets, the ones of its interaction
    // profiles, and an instance takes one set of suggested bindings per profile and one attach per session. So this
    // feature intercepts those calls (HookGetInstanceProcAddr, as the plugin's own features do):
    // - xrSuggestInteractionProfileBindings: VHVR's bindings for the profile are added to the plugin's, and a profile
    //   the plugin suggests nothing for gets VHVR's on their own before attaching;
    // - xrAttachSessionActionSets: VHVR's sets are attached with the plugin's;
    // - xrSyncActions: VHVR's active sets are synced with the plugin's.
    // Should the runtime refuse VHVR's part of a call, the plugin's call goes through as it was, and VHVR's actions
    // read as unbound (the mod then falls back on its own bindings).
    //
    // The mod fills in Sets, Actions and Bindings before the loader initializes, and reads the actions with
    // GetBoolean() and GetVector2(). Only plain types cross into the mod.
    public sealed unsafe class ActionsFeature : OpenXRFeature
    {
        public const int BooleanAction = 1;
        public const int Vector2Action = 3;

        // An action set: Name is an OpenXR path component (lower case, digits, '-', '_', '.'), unique among every set
        // of the instance.
        public sealed class SetSpec
        {
            public string Name;
            public string LocalizedName;
        }

        public sealed class ActionSpec
        {
            public int Set;
            public string Name;
            public string LocalizedName;
            public int Kind;
        }

        // A suggested binding: Path is a full input path, e.g. /user/hand/right/input/a/click.
        public sealed class BindingSpec
        {
            public string Profile;
            public int Action;
            public string Path;
        }

        public static SetSpec[] Sets = new SetSpec[0];
        public static ActionSpec[] Actions = new ActionSpec[0];
        public static BindingSpec[] Bindings = new BindingSpec[0];
        public static Action<string> LogInfo = message => UnityEngine.Debug.Log(message);
        public static Action<string> LogWarning = message => UnityEngine.Debug.LogWarning(message);

        // The hands, as the actions' subaction paths: 0 left, 1 right.
        private static readonly string[] HandPaths = { "/user/hand/left", "/user/hand/right" };

        // ---- OpenXR types, as in openxr.h (x64) ----
        private const uint XR_TYPE_ACTION_STATE_BOOLEAN = 23;
        private const uint XR_TYPE_ACTION_STATE_VECTOR2F = 25;
        private const uint XR_TYPE_ACTION_SET_CREATE_INFO = 28;
        private const uint XR_TYPE_ACTION_CREATE_INFO = 29;
        private const uint XR_TYPE_ACTION_STATE_GET_INFO = 58;
        private const int XR_MAX_ACTION_SET_NAME_SIZE = 64;
        private const int XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE = 128;
        private const int XR_MAX_ACTION_NAME_SIZE = 64;
        private const int XR_MAX_LOCALIZED_ACTION_NAME_SIZE = 128;

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct XrActionSetCreateInfo
        {
            public uint type;
            public IntPtr next;
            public fixed byte actionSetName[XR_MAX_ACTION_SET_NAME_SIZE];
            public fixed byte localizedActionSetName[XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE];
            public uint priority;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct XrActionCreateInfo
        {
            public uint type;
            public IntPtr next;
            public fixed byte actionName[XR_MAX_ACTION_NAME_SIZE];
            public int actionType;
            public uint countSubactionPaths;
            public ulong* subactionPaths;
            public fixed byte localizedActionName[XR_MAX_LOCALIZED_ACTION_NAME_SIZE];
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XrActionSuggestedBinding
        {
            public ulong action;
            public ulong binding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct XrInteractionProfileSuggestedBinding
        {
            public uint type;
            public IntPtr next;
            public ulong interactionProfile;
            public uint countSuggestedBindings;
            public XrActionSuggestedBinding* suggestedBindings;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct XrSessionActionSetsAttachInfo
        {
            public uint type;
            public IntPtr next;
            public uint countActionSets;
            public ulong* actionSets;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XrActiveActionSet
        {
            public ulong actionSet;
            public ulong subactionPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct XrActionsSyncInfo
        {
            public uint type;
            public IntPtr next;
            public uint countActiveActionSets;
            public XrActiveActionSet* activeActionSets;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XrActionStateGetInfo
        {
            public uint type;
            public IntPtr next;
            public ulong action;
            public ulong subactionPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XrActionStateBoolean
        {
            public uint type;
            public IntPtr next;
            public uint currentState;
            public uint changedSinceLastSync;
            public long lastChangeTime;
            public uint isActive;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XrActionStateVector2f
        {
            public uint type;
            public IntPtr next;
            public float x;
            public float y;
            public uint changedSinceLastSync;
            public long lastChangeTime;
            public uint isActive;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInstanceProcAddrFn(ulong instance, IntPtr name, out IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int StringToPathFn(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string path, out ulong xrPath);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int CreateActionSetFn(ulong instance, XrActionSetCreateInfo* info, out ulong actionSet);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int CreateActionFn(ulong actionSet, XrActionCreateInfo* info, out ulong action);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int SuggestFn(ulong instance, XrInteractionProfileSuggestedBinding* info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int AttachFn(ulong session, XrSessionActionSetsAttachInfo* info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int SyncFn(ulong session, XrActionsSyncInfo* info);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetBooleanFn(ulong session, ref XrActionStateGetInfo info, ref XrActionStateBoolean state);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetVector2Fn(ulong session, ref XrActionStateGetInfo info, ref XrActionStateVector2f state);

        // The hooks, kept alive for as long as the runtime may call them.
        private static readonly GetInstanceProcAddrFn hookGetInstanceProcAddr = hookedGetInstanceProcAddr;
        private static readonly SuggestFn hookSuggest = hookedSuggest;
        private static readonly AttachFn hookAttach = hookedAttach;
        private static readonly SyncFn hookSync = hookedSync;

        private static GetInstanceProcAddrFn nextGetInstanceProcAddr;
        private static SuggestFn realSuggest;
        private static AttachFn realAttach;
        private static SyncFn realSync;
        private static StringToPathFn stringToPath;
        private static CreateActionSetFn createActionSet;
        private static CreateActionFn createAction;
        private static GetBooleanFn getBoolean;
        private static GetVector2Fn getVector2;

        private static ulong instance;
        private static ulong session;
        private static bool created;
        private static bool attached;
        private static ulong[] setHandles = new ulong[0];
        private static ulong[] actionHandles = new ulong[0];
        private static readonly ulong[] handPaths = new ulong[2];
        private static bool[] setActive = new bool[0];
        // VHVR's suggested bindings per profile path, and the profiles they have been suggested for.
        private static readonly Dictionary<ulong, XrActionSuggestedBinding[]> bindingsByProfile = new Dictionary<ulong, XrActionSuggestedBinding[]>();
        private static readonly Dictionary<ulong, string> profileNames = new Dictionary<ulong, string>();
        private static readonly HashSet<ulong> suggested = new HashSet<ulong>();

        // Whether the actions are attached to the session, so that reading them means anything.
        public static bool IsAttached => attached && session != 0;

        // Which sets are synced, as the mod activates them; a set not synced reads as unbound.
        public static void SetActive(int set, bool active)
        {
            if (set >= 0 && set < setActive.Length)
            {
                setActive[set] = active;
            }
        }

        // An action of a hand (0 left, 1 right): false when it is not bound to an input of the hand's controller.
        public static bool GetBoolean(int action, int hand, out bool state)
        {
            state = false;
            if (!IsAttached || getBoolean == null || action < 0 || action >= actionHandles.Length || actionHandles[action] == 0)
            {
                return false;
            }
            var info = new XrActionStateGetInfo { type = XR_TYPE_ACTION_STATE_GET_INFO, action = actionHandles[action], subactionPath = handPaths[hand] };
            var value = new XrActionStateBoolean { type = XR_TYPE_ACTION_STATE_BOOLEAN };
            if (getBoolean(session, ref info, ref value) < 0 || value.isActive == 0)
            {
                return false;
            }
            state = value.currentState != 0;
            return true;
        }

        public static bool GetVector2(int action, int hand, out float x, out float y)
        {
            x = y = 0;
            if (!IsAttached || getVector2 == null || action < 0 || action >= actionHandles.Length || actionHandles[action] == 0)
            {
                return false;
            }
            var info = new XrActionStateGetInfo { type = XR_TYPE_ACTION_STATE_GET_INFO, action = actionHandles[action], subactionPath = handPaths[hand] };
            var value = new XrActionStateVector2f { type = XR_TYPE_ACTION_STATE_VECTOR2F };
            if (getVector2(session, ref info, ref value) < 0 || value.isActive == 0)
            {
                return false;
            }
            x = value.x;
            y = value.y;
            return true;
        }

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            nextGetInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddrFn>(func);
            return Marshal.GetFunctionPointerForDelegate(hookGetInstanceProcAddr);
        }

        // Every function the plugin and the runtime look up goes through here: the three VHVR joins in are swapped.
        private static int hookedGetInstanceProcAddr(ulong xrInstance, IntPtr name, out IntPtr function)
        {
            int result = nextGetInstanceProcAddr(xrInstance, name, out function);
            if (result < 0 || function == IntPtr.Zero)
            {
                return result;
            }
            try
            {
                switch (Marshal.PtrToStringAnsi(name))
                {
                    case "xrSuggestInteractionProfileBindings":
                        realSuggest = Marshal.GetDelegateForFunctionPointer<SuggestFn>(function);
                        function = Marshal.GetFunctionPointerForDelegate(hookSuggest);
                        break;
                    case "xrAttachSessionActionSets":
                        realAttach = Marshal.GetDelegateForFunctionPointer<AttachFn>(function);
                        function = Marshal.GetFunctionPointerForDelegate(hookAttach);
                        break;
                    case "xrSyncActions":
                        realSync = Marshal.GetDelegateForFunctionPointer<SyncFn>(function);
                        function = Marshal.GetFunctionPointerForDelegate(hookSync);
                        break;
                }
            }
            catch (Exception e)
            {
                LogWarning("VHVR OpenXR actions: " + e);
            }
            return result;
        }

        private static T Resolve<T>(string name) where T : Delegate
        {
            IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
            try
            {
                return nextGetInstanceProcAddr(instance, namePtr, out IntPtr function) >= 0 && function != IntPtr.Zero ?
                    Marshal.GetDelegateForFunctionPointer<T>(function) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(namePtr);
            }
        }

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            instance = xrInstance;
            created = false;
            suggested.Clear();
            try
            {
                createActions();
            }
            catch (Exception e)
            {
                LogWarning("VHVR OpenXR actions could not be created: " + e);
            }
            // The plugin goes on either way: without these actions the mod uses its own bindings.
            return true;
        }

        private static unsafe void createActions()
        {
            if (nextGetInstanceProcAddr == null)
            {
                LogWarning("VHVR OpenXR actions: the function lookup was not hooked.");
                return;
            }
            stringToPath = Resolve<StringToPathFn>("xrStringToPath");
            createActionSet = Resolve<CreateActionSetFn>("xrCreateActionSet");
            createAction = Resolve<CreateActionFn>("xrCreateAction");
            getBoolean = Resolve<GetBooleanFn>("xrGetActionStateBoolean");
            getVector2 = Resolve<GetVector2Fn>("xrGetActionStateVector2f");
            realSuggest = realSuggest ?? Resolve<SuggestFn>("xrSuggestInteractionProfileBindings");
            realAttach = realAttach ?? Resolve<AttachFn>("xrAttachSessionActionSets");
            realSync = realSync ?? Resolve<SyncFn>("xrSyncActions");
            if (stringToPath == null || createActionSet == null || createAction == null || getBoolean == null || getVector2 == null)
            {
                LogWarning("VHVR OpenXR actions: the runtime lacks an action function.");
                return;
            }
            for (int hand = 0; hand < 2; hand++)
            {
                if (stringToPath(instance, HandPaths[hand], out handPaths[hand]) < 0)
                {
                    LogWarning("VHVR OpenXR actions: no path for " + HandPaths[hand] + ".");
                    return;
                }
            }

            setHandles = new ulong[Sets.Length];
            setActive = new bool[Sets.Length];
            for (int i = 0; i < Sets.Length; i++)
            {
                var info = new XrActionSetCreateInfo { type = XR_TYPE_ACTION_SET_CREATE_INFO };
                write(info.actionSetName, XR_MAX_ACTION_SET_NAME_SIZE, Sets[i].Name);
                write(info.localizedActionSetName, XR_MAX_LOCALIZED_ACTION_SET_NAME_SIZE, Sets[i].LocalizedName);
                int result = createActionSet(instance, &info, out setHandles[i]);
                if (result < 0)
                {
                    LogWarning("VHVR OpenXR actions: action set " + Sets[i].Name + " not created (" + result + ").");
                    return;
                }
            }

            actionHandles = new ulong[Actions.Length];
            ulong* subactions = stackalloc ulong[2];
            subactions[0] = handPaths[0];
            subactions[1] = handPaths[1];
            int count = 0;
            for (int i = 0; i < Actions.Length; i++)
            {
                ActionSpec spec = Actions[i];
                var info = new XrActionCreateInfo
                {
                    type = XR_TYPE_ACTION_CREATE_INFO,
                    actionType = spec.Kind,
                    countSubactionPaths = 2,
                    subactionPaths = subactions,
                };
                write(info.actionName, XR_MAX_ACTION_NAME_SIZE, spec.Name);
                write(info.localizedActionName, XR_MAX_LOCALIZED_ACTION_NAME_SIZE, spec.LocalizedName);
                int result = createAction(setHandles[spec.Set], &info, out actionHandles[i]);
                if (result < 0)
                {
                    LogWarning("VHVR OpenXR actions: action " + spec.Name + " not created (" + result + ").");
                    actionHandles[i] = 0;
                    continue;
                }
                count++;
            }

            bindingsByProfile.Clear();
            profileNames.Clear();
            var lists = new Dictionary<ulong, List<XrActionSuggestedBinding>>();
            foreach (BindingSpec binding in Bindings)
            {
                if (binding.Action < 0 || binding.Action >= actionHandles.Length || actionHandles[binding.Action] == 0)
                {
                    continue;
                }
                if (stringToPath(instance, binding.Profile, out ulong profile) < 0 || stringToPath(instance, binding.Path, out ulong path) < 0)
                {
                    LogWarning("VHVR OpenXR actions: invalid binding " + binding.Profile + " " + binding.Path + ".");
                    continue;
                }
                if (!lists.TryGetValue(profile, out List<XrActionSuggestedBinding> list))
                {
                    lists[profile] = list = new List<XrActionSuggestedBinding>();
                    profileNames[profile] = binding.Profile;
                }
                list.Add(new XrActionSuggestedBinding { action = actionHandles[binding.Action], binding = path });
            }
            foreach (KeyValuePair<ulong, List<XrActionSuggestedBinding>> entry in lists)
            {
                bindingsByProfile[entry.Key] = entry.Value.ToArray();
            }
            created = true;
            LogInfo("VHVR OpenXR actions: " + count + " actions in " + Sets.Length + " sets, bindings for " + lists.Count + " profiles.");
        }

        private static unsafe void write(byte* buffer, int size, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            int length = Math.Min(bytes.Length, size - 1);
            for (int i = 0; i < length; i++)
            {
                buffer[i] = bytes[i];
            }
            buffer[length] = 0;
        }

        private static unsafe int hookedSuggest(ulong xrInstance, XrInteractionProfileSuggestedBinding* info)
        {
            try
            {
                if (created && xrInstance == instance && info != null &&
                    bindingsByProfile.TryGetValue(info->interactionProfile, out XrActionSuggestedBinding[] ours))
                {
                    int theirs = (int)info->countSuggestedBindings;
                    XrActionSuggestedBinding* merged = stackalloc XrActionSuggestedBinding[theirs + ours.Length];
                    for (int i = 0; i < theirs; i++)
                    {
                        merged[i] = info->suggestedBindings[i];
                    }
                    for (int i = 0; i < ours.Length; i++)
                    {
                        merged[theirs + i] = ours[i];
                    }
                    XrInteractionProfileSuggestedBinding copy = *info;
                    copy.countSuggestedBindings = (uint)(theirs + ours.Length);
                    copy.suggestedBindings = merged;
                    int result = realSuggest(xrInstance, &copy);
                    if (result >= 0)
                    {
                        suggested.Add(info->interactionProfile);
                        return result;
                    }
                    LogWarning("VHVR OpenXR actions: the runtime refused the bindings for " + profileNames[info->interactionProfile] +
                               " (" + result + "); suggesting the plugin's alone.");
                }
            }
            catch (Exception e)
            {
                LogWarning("VHVR OpenXR actions: " + e);
            }
            return realSuggest(xrInstance, info);
        }

        // Before the sets are attached, the profiles the plugin suggested nothing for get VHVR's bindings alone.
        private static unsafe void suggestRemaining()
        {
            foreach (KeyValuePair<ulong, XrActionSuggestedBinding[]> entry in bindingsByProfile)
            {
                if (suggested.Contains(entry.Key))
                {
                    continue;
                }
                suggested.Add(entry.Key);
                fixed (XrActionSuggestedBinding* bindings = entry.Value)
                {
                    var info = new XrInteractionProfileSuggestedBinding
                    {
                        type = 51, // XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING
                        interactionProfile = entry.Key,
                        countSuggestedBindings = (uint)entry.Value.Length,
                        suggestedBindings = bindings,
                    };
                    int result = realSuggest(instance, &info);
                    if (result < 0)
                    {
                        // E.g. a profile whose extension the runtime has not enabled.
                        LogInfo("VHVR OpenXR actions: no bindings for " + profileNames[entry.Key] + " (" + result + ").");
                    }
                }
            }
        }

        private static unsafe int hookedAttach(ulong xrSession, XrSessionActionSetsAttachInfo* info)
        {
            try
            {
                if (created && info != null && realSuggest != null)
                {
                    suggestRemaining();
                    int theirs = (int)info->countActionSets;
                    ulong* merged = stackalloc ulong[theirs + setHandles.Length];
                    for (int i = 0; i < theirs; i++)
                    {
                        merged[i] = info->actionSets[i];
                    }
                    for (int i = 0; i < setHandles.Length; i++)
                    {
                        merged[theirs + i] = setHandles[i];
                    }
                    XrSessionActionSetsAttachInfo copy = *info;
                    copy.countActionSets = (uint)(theirs + setHandles.Length);
                    copy.actionSets = merged;
                    int result = realAttach(xrSession, &copy);
                    if (result >= 0)
                    {
                        session = xrSession;
                        attached = true;
                        LogInfo("VHVR OpenXR actions attached to the session.");
                        return result;
                    }
                    LogWarning("VHVR OpenXR actions: the runtime refused to attach them (" + result + "); attaching the plugin's alone.");
                }
            }
            catch (Exception e)
            {
                LogWarning("VHVR OpenXR actions: " + e);
            }
            return realAttach(xrSession, info);
        }

        private static int hookedSync(ulong xrSession, XrActionsSyncInfo* info)
        {
            try
            {
                if (attached && xrSession == session && info != null)
                {
                    return syncWithOurs(xrSession, info);
                }
            }
            catch (Exception e)
            {
                LogWarning("VHVR OpenXR actions: " + e);
            }
            return realSync(xrSession, info);
        }

        private static int syncWithOurs(ulong xrSession, XrActionsSyncInfo* info)
        {
            int ours = 0;
            for (int i = 0; i < setActive.Length; i++)
            {
                if (setActive[i]) ours++;
            }
            if (ours == 0)
            {
                return realSync(xrSession, info);
            }
            int theirs = (int)info->countActiveActionSets;
            XrActiveActionSet* merged = stackalloc XrActiveActionSet[theirs + ours];
            for (int i = 0; i < theirs; i++)
            {
                merged[i] = info->activeActionSets[i];
            }
            int n = theirs;
            for (int i = 0; i < setActive.Length; i++)
            {
                if (setActive[i])
                {
                    merged[n++] = new XrActiveActionSet { actionSet = setHandles[i], subactionPath = 0 };
                }
            }
            XrActionsSyncInfo copy = *info;
            copy.countActiveActionSets = (uint)n;
            copy.activeActionSets = merged;
            return realSync(xrSession, &copy);
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            if (xrSession == session)
            {
                session = 0;
                attached = false;
            }
        }

        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            if (xrInstance == instance)
            {
                instance = 0;
                created = false;
                attached = false;
                session = 0;
            }
        }
    }
}
