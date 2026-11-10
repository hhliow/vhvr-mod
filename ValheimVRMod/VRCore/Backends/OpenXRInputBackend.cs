using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.VRCore.Backends
{
    // The mod's actions (VRInputActions) under OpenXR, with the same down and up edges, listeners and action sets as
    // SteamVR's, worked out once per frame. Two sources, per action and hand:
    // - VHVR's own OpenXR actions (BuildManifest, registered by VHVR.OpenXR's ActionsFeature): the runtime reads them
    //   through the bindings VHVR suggested for the controller's interaction profile, or the user's own (e.g. from
    //   SteamVR's binding UI). Used whenever the runtime has the action bound for the hand.
    // - Otherwise, the bindings the mod works out from the controller's controls (OpenXRController, the Input
    //   System device of the profile in use) through the profile's tables (OpenXRProfiles): chords, stick directions
    //   and whatever the runtime has unbound. There is no haptics yet.
    // Inert until Start(), when the runtime is up: every action is unbound and released, and nothing here touches
    // Unity, so the backend can be made and its actions resolved outside the game.
    public sealed class OpenXRInputBackend : IVRInputBackend
    {
        private const string DefaultSet = "/actions/default";

        private readonly Dictionary<string, BooleanAction> booleans = new Dictionary<string, BooleanAction>();
        private readonly Dictionary<string, AxisAction> axes = new Dictionary<string, AxisAction>();
        private readonly Dictionary<string, PoseAction> poses = new Dictionary<string, PoseAction>();
        private readonly Dictionary<string, XRActionSet> sets = new Dictionary<string, XRActionSet>();
        private readonly OpenXRController left = new OpenXRController();
        private readonly OpenXRController right = new OpenXRController();
        private string loggedDevices;
        private bool started;
        private int evaluatedFrame = -1;
        private int dispatchedFrame = -1;
        private bool loggedBindingUI;

        public event Action onNonVisualActionsUpdated;

        public OpenXRInputBackend()
        {
            left.ActionState = action => booleans.TryGetValue(action, out BooleanAction a) && a.PressedOn(XRHand.Left);
            right.ActionState = action => booleans.TryGetValue(action, out BooleanAction a) && a.PressedOn(XRHand.Right);
        }

        private IReadOnlyList<VRActionSetDecl> declaredSets = new VRActionSetDecl[0];
        private IReadOnlyList<VRActionDecl> declaredActions = new VRActionDecl[0];
        // The OpenXR action of each mod action path, and the mod set path of each OpenXR set, by index.
        private readonly Dictionary<string, int> runtimeActions = new Dictionary<string, int>();
        private readonly List<string> runtimeSets = new List<string>();
        private string loggedRuntimeBindings;

        // Kept for BuildManifest(): the OpenXR actions are created with the instance, before the actions are
        // resolved.
        public void Declare(IReadOnlyList<VRActionSetDecl> sets, IReadOnlyList<VRActionDecl> actions)
        {
            declaredSets = sets;
            declaredActions = actions;
        }

        // The OpenXR action sets and actions for the declared ones, and the bindings each profile suggests for
        // them: every boolean and vector2 action (poses come from the controller devices, there is no haptics yet).
        internal OpenXRActionManifest BuildManifest()
        {
            var manifest = new OpenXRActionManifest();
            runtimeActions.Clear();
            runtimeSets.Clear();
            foreach (VRActionSetDecl set in declaredSets)
            {
                runtimeSets.Add(set.Path);
                manifest.Sets.Add(new OpenXRActionManifest.Set
                {
                    Name = "vhvr-" + pathName(ShortName(set.Path)),
                    LocalizedName = "VHVR " + set.LocalizedName,
                });
            }
            var names = new HashSet<string>();
            foreach (VRActionDecl action in declaredActions)
            {
                if (action.Kind != VRActionKind.Boolean && action.Kind != VRActionKind.Vector2)
                {
                    continue;
                }
                int end = action.Path.IndexOf('/', "/actions/".Length);
                int set = end < 0 ? -1 : runtimeSets.IndexOf(action.Path.Substring(0, end));
                if (set < 0)
                {
                    continue;
                }
                string name = pathName(ShortName(action.Path));
                while (!names.Add(set + "/" + name))
                {
                    name += "_";
                }
                runtimeActions[action.Path] = manifest.Actions.Count;
                manifest.Actions.Add(new OpenXRActionManifest.Action
                {
                    Set = set,
                    Name = name,
                    LocalizedName = action.LocalizedName,
                    IsVector2 = action.Kind == VRActionKind.Vector2,
                });
            }
            foreach (OpenXRProfile profile in OpenXRProfiles.All)
            {
                foreach (XRSuggestedBinding binding in profile.Suggested)
                {
                    if (runtimeActions.TryGetValue(binding.Action, out int index))
                    {
                        manifest.Bindings.Add(new OpenXRActionManifest.Binding
                        {
                            Profile = profile.InteractionProfile,
                            Action = index,
                            Path = binding.Path,
                        });
                    }
                }
            }
            return manifest;
        }

        // An OpenXR path component: lower case letters, digits, '-', '_' and '.'.
        private static string pathName(string name)
        {
            var result = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.')
                {
                    result.Append(c);
                }
            }
            return result.Length == 0 ? "action" : result.ToString();
        }

        public VRBooleanAction Boolean(string path)
        {
            if (!booleans.TryGetValue(path, out BooleanAction action))
            {
                booleans[path] = action = new BooleanAction(this, path, ActionSetFor(path));
            }
            return action;
        }

        public VRVector2Action Axis(string path)
        {
            if (!axes.TryGetValue(path, out AxisAction action))
            {
                axes[path] = action = new AxisAction(this, path, ActionSetFor(path));
            }
            return action;
        }

        public VRPoseAction Pose(string path)
        {
            if (!poses.TryGetValue(path, out PoseAction action))
            {
                // As the SteamVR bindings have it: the default pose on both hands, PoseL and PoseR on their own.
                // There are no body trackers.
                bool leftHand = path == "/actions/default/in/Pose" || path == "/actions/Valheim/in/PoseL";
                bool rightHand = path == "/actions/default/in/Pose" || path == "/actions/Valheim/in/PoseR";
                poses[path] = action = new PoseAction(this, path, ActionSetFor(path), leftHand, rightHand);
            }
            return action;
        }

        public VRHapticAction Haptic(string path) => NoHaptic.Instance;

        public VRActionSet ActionSet(string path) => ActionSetFor(path);

        public void OpenBindingUI(VRActionSet actionSet)
        {
            if (!loggedBindingUI)
            {
                loggedBindingUI = true;
                LogWarning("There is no binding UI under OpenXR: the default bindings of the controller are used.");
            }
        }

        private XRActionSet ActionSetFor(string actionPath)
        {
            int end = actionPath.IndexOf('/', "/actions/".Length);
            string setPath = end < 0 ? actionPath : actionPath.Substring(0, end);
            if (!sets.TryGetValue(setPath, out XRActionSet set))
            {
                // SteamVR activates the first set (default) on start; the mod activates its own.
                sets[setPath] = set = new XRActionSet(this, setPath == DefaultSet);
            }
            return set;
        }

        // The runtime is up: from now on the actions follow the controllers. Called by OpenXRBackend.StartVR().
        internal void Start()
        {
            if (started)
            {
                return;
            }
            started = true;
            OpenXRController.AllowControllers();
            var driver = new GameObject("VHVR_OpenXRInput");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<OpenXRInputDriver>().Backend = this;
        }

        // Early in the frame (OpenXRInputDriver), before other scripts read the actions: the actions are worked out
        // and their listeners called, once per frame. A read before that works them out too.
        internal void Tick()
        {
            if (!started)
            {
                return;
            }
            Refresh();
            if (dispatchedFrame == evaluatedFrame)
            {
                return;
            }
            dispatchedFrame = evaluatedFrame;
            foreach (BooleanAction action in booleans.Values)
            {
                action.Dispatch();
            }
            foreach (AxisAction action in axes.Values)
            {
                action.Dispatch();
            }
            onNonVisualActionsUpdated?.Invoke();
        }

        private void Refresh()
        {
            if (started)
            {
                refreshStarted();
            }
        }

        // Apart from Refresh(), which is called before the runtime starts too: a method that calls into Unity cannot
        // even be compiled outside the game.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void refreshStarted()
        {
            if (evaluatedFrame == Time.frameCount)
            {
                return;
            }
            evaluatedFrame = Time.frameCount;
            left.SetDevice(OpenXRController.FindDevice(XRHand.Left), XRHand.Left);
            right.SetDevice(OpenXRController.FindDevice(XRHand.Right), XRHand.Right);
            string devices = describe(left) + " / " + describe(right);
            if (devices != loggedDevices)
            {
                loggedDevices = devices;
                LogInfo("OpenXR controllers (left / right): " + devices + ".");
                logXRDevices();
            }

            foreach (BooleanAction action in booleans.Values)
            {
                action.BeginFrame();
            }
            foreach (AxisAction action in axes.Values)
            {
                action.BeginFrame();
            }
            if (OpenXRLoaderSetup.ActionsAttached())
            {
                readRuntimeActions();
            }
            // Each hand by the bindings of its own controller's profile, for what the runtime has unbound.
            foreach (XRHand hand in Hands)
            {
                OpenXRController controller = Controller(hand);
                OpenXRProfile profile = controller.IsConnected ? controller.Profile : null;
                if (profile == null)
                {
                    continue;
                }
                bindProfile(profile, hand, chords: false);
                foreach (XRAxisBinding binding in profile.Axes)
                {
                    if (binding.Source == hand && axes.TryGetValue(binding.Action, out AxisAction action) && action.Set.active &&
                        !action.FromRuntime(hand))
                    {
                        action.Bind(hand, binding.Read(left, right));
                    }
                }
            }
            // The chords last, once both hands' actions they are made of are known.
            foreach (XRHand hand in Hands)
            {
                OpenXRController controller = Controller(hand);
                if (controller.IsConnected && controller.Profile != null)
                {
                    bindProfile(controller.Profile, hand, chords: true);
                }
            }
            foreach (BooleanAction action in booleans.Values)
            {
                action.EndFrame();
            }
            foreach (AxisAction action in axes.Values)
            {
                action.EndFrame();
            }
        }

        private OpenXRController Controller(XRHand hand) => hand == XRHand.Left ? left : right;

        // The hand's boolean bindings of the profile, either the chords or the others, for the actions the runtime
        // has unbound.
        private void bindProfile(OpenXRProfile profile, XRHand hand, bool chords)
        {
            foreach (XRBooleanBinding binding in profile.Booleans)
            {
                if (binding.IsChord == chords && binding.Source == hand &&
                    booleans.TryGetValue(binding.Action, out BooleanAction action) && action.Set.active && !action.FromRuntime(hand))
                {
                    action.Bind(hand, binding.Condition(left, right));
                }
            }
        }

        private static readonly XRHand[] Hands = { XRHand.Left, XRHand.Right };

        // VHVR's OpenXR actions, as the runtime binds them: the sets the mod has active are synced, and each action
        // of an active set takes the runtime's state for every hand the runtime has it bound for.
        private void readRuntimeActions()
        {
            for (int i = 0; i < runtimeSets.Count; i++)
            {
                OpenXRLoaderSetup.SetActionSetActive(i, ActionSetFor(runtimeSets[i]).active);
            }
            int leftCount = 0, rightCount = 0;
            foreach (BooleanAction action in booleans.Values)
            {
                int index = runtimeIndex(action.Path);
                if (index < 0 || !action.Set.active)
                {
                    continue;
                }
                foreach (XRHand hand in Hands)
                {
                    if (OpenXRLoaderSetup.ReadBoolean(index, (int)hand, out bool state))
                    {
                        action.BindFromRuntime(hand, state);
                        if (hand == XRHand.Left) leftCount++; else rightCount++;
                    }
                }
            }
            foreach (AxisAction action in axes.Values)
            {
                int index = runtimeIndex(action.Path);
                if (index < 0 || !action.Set.active)
                {
                    continue;
                }
                foreach (XRHand hand in Hands)
                {
                    if (OpenXRLoaderSetup.ReadVector2(index, (int)hand, out Vector2 axis))
                    {
                        action.BindFromRuntime(hand, axis);
                        if (hand == XRHand.Left) leftCount++; else rightCount++;
                    }
                }
            }
            string counts = leftCount + "/" + rightCount;
            if (counts != loggedRuntimeBindings)
            {
                loggedRuntimeBindings = counts;
                logRuntimeBindings();
            }
        }

        private int runtimeIndex(string path) => runtimeActions.TryGetValue(path, out int index) ? index : -1;

        // Which actions the runtime has bound on each hand; the others follow the mod's own bindings.
        private void logRuntimeBindings()
        {
            foreach (XRHand hand in Hands)
            {
                var names = new List<string>();
                foreach (BooleanAction action in booleans.Values)
                {
                    if (action.FromRuntime(hand)) names.Add(ShortName(action.Path));
                }
                foreach (AxisAction action in axes.Values)
                {
                    if (action.FromRuntime(hand)) names.Add(ShortName(action.Path));
                }
                LogInfo("OpenXR actions bound by the runtime on the " + hand + " hand: " +
                        (names.Count == 0 ? "none" : string.Join(", ", names.ToArray())) + ".");
            }
        }

        private static string describe(OpenXRController controller)
        {
            if (!controller.IsConnected) return "none";
            return controller.Name + (controller.Profile != null ? ", " + controller.Profile.Name + " bindings" : ", no bindings for it");
        }

        // Every XR device of the Input System and the hands' controls, for when a hand is not found or not the
        // expected controller.
        private void logXRDevices()
        {
            LogInfo("Input System XR devices: " + OpenXRController.DescribeDevices() + ".");
            foreach (OpenXRController controller in new[] { left, right })
            {
                if (controller.IsConnected)
                {
                    LogInfo("Controls of " + controller.Name + ": " + controller.Controls() + ".");
                }
            }
        }

        // Why a hand has or has no pose, for the log (see OpenXRHand).
        internal string PoseStatus(XRHand hand)
        {
            if (!started)
            {
                return "the runtime has not started";
            }
            Refresh();
            OpenXRController controller = Controller(hand);
            if (!controller.IsConnected)
            {
                return "no " + hand + " hand controller in the Input System";
            }
            if (!controller.Has("devicePose/isTracked") || !controller.Has("devicePose/position"))
            {
                return controller.Name + " has no devicePose";
            }
            return controller.TryGetGripPose(out Vector3 position, out _) ?
                controller.Name + " tracked at " + position.ToString("F2") :
                controller.Name + " not tracked";
        }

        internal bool IsConnected(XRHand hand)
        {
            if (!started)
            {
                return false;
            }
            Refresh();
            return Controller(hand).IsConnected;
        }

        // The SteamVR raw pose of the hand's controller in tracking space (see OpenXRBindings.GripToRaw), read as it
        // is now: the rig's hands read it again before rendering. False when the controller is not tracked.
        internal bool TryGetRawPose(XRHand hand, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!started)
            {
                return false;
            }
            Refresh();
            OpenXRController controller = Controller(hand);
            if (!controller.TryGetGripPose(out Vector3 gripPosition, out Quaternion gripRotation))
            {
                return false;
            }
            controller.GripToRaw(gripPosition, gripRotation, out position, out rotation);
            return true;
        }

        // The velocities of the raw pose in tracking space, from those the runtime gives for the grip pose; zero when
        // it gives none (see OpenXRHand, which then works them out itself).
        internal void GetRawVelocity(XRHand hand, out Vector3 velocity, out Vector3 angularVelocity)
        {
            velocity = angularVelocity = Vector3.zero;
            if (!TryGetRawPose(hand, out _, out Quaternion rawRotation))
            {
                return;
            }
            OpenXRController controller = Controller(hand);
            controller.GetGripVelocity(out Vector3 gripVelocity, out angularVelocity);
            // The raw pose is a point on the same rigid body as the grip pose.
            Vector3 gripToRaw = -controller.GripOffset(rawRotation);
            velocity = gripVelocity + Vector3.Cross(angularVelocity, gripToRaw);
        }

        private static int Index(VRInputSource source)
        {
            switch (source)
            {
                case VRInputSource.Any: return 0;
                case VRInputSource.LeftHand: return 1;
                case VRInputSource.RightHand: return 2;
                default: return -1;
            }
        }

        private static readonly VRInputSource[] Sources = { VRInputSource.Any, VRInputSource.LeftHand, VRInputSource.RightHand };

        private static string ShortName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        sealed class XRActionSet : VRActionSet
        {
            private readonly OpenXRInputBackend backend;
            internal bool active;
            public XRActionSet(OpenXRInputBackend backend, bool active) { this.backend = backend; this.active = active; }
            // Sets apply to both hands at once; the source is ignored, as the mod only ever passes Any.
            public override bool IsActive(VRInputSource source = VRInputSource.Any) => active;
            public override void Activate(VRInputSource source = VRInputSource.Any, int priority = 0, bool disableAllOtherActionSets = false)
            {
                active = true;
                if (disableAllOtherActionSets)
                {
                    foreach (XRActionSet set in backend.sets.Values)
                    {
                        set.active = set == this;
                    }
                }
            }
            public override void Deactivate(VRInputSource source = VRInputSource.Any) { active = false; }
        }

        // State per source: Any (both hands combined, with its own edges), left, right.
        sealed class BooleanAction : VRBooleanAction
        {
            private readonly OpenXRInputBackend backend;
            private readonly string path;
            internal readonly XRActionSet Set;
            private readonly bool[] pressed = new bool[3];
            private readonly bool[] previous = new bool[3];
            private readonly bool[] bound = new bool[3];
            // Per hand (left, right): the runtime gave the state this frame.
            private readonly bool[] runtime = new bool[2];
            private readonly List<KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>> downListeners =
                new List<KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>>();
            private readonly List<KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>> upListeners =
                new List<KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>>();

            public BooleanAction(OpenXRInputBackend backend, string path, XRActionSet set) { this.backend = backend; this.path = path; Set = set; }

            internal string Path => path;

            internal void BeginFrame()
            {
                for (int i = 0; i < 3; i++)
                {
                    previous[i] = pressed[i];
                    pressed[i] = bound[i] = false;
                }
                runtime[0] = runtime[1] = false;
            }
            internal bool FromRuntime(XRHand hand) => runtime[(int)hand];
            // Pressed on the hand so far this frame, while the bindings are being worked out.
            internal bool PressedOn(XRHand hand) => pressed[hand == XRHand.Left ? 1 : 2];
            internal void BindFromRuntime(XRHand hand, bool value)
            {
                runtime[(int)hand] = true;
                Bind(hand, value);
            }
            internal void Bind(XRHand hand, bool value)
            {
                int i = hand == XRHand.Left ? 1 : 2;
                bound[i] = true;
                pressed[i] |= value;
            }
            internal void EndFrame()
            {
                pressed[0] = pressed[1] || pressed[2];
                bound[0] = bound[1] || bound[2];
            }
            internal void Dispatch()
            {
                foreach (var listener in downListeners.ToArray())
                {
                    if (pressed[listener.Key] && !previous[listener.Key]) listener.Value(this, Sources[listener.Key]);
                }
                foreach (var listener in upListeners.ToArray())
                {
                    if (!pressed[listener.Key] && previous[listener.Key]) listener.Value(this, Sources[listener.Key]);
                }
            }

            private bool Read(bool[] values, VRInputSource source)
            {
                int i = Index(source);
                if (i < 0) return false;
                backend.Refresh();
                return values[i];
            }

            public override string GetShortName() => ShortName(path);
            public override bool activeBinding => Read(bound, VRInputSource.Any);
            public override bool GetActiveBinding(VRInputSource source) => Read(bound, source);
            public override bool state => Read(pressed, VRInputSource.Any);
            public override bool GetState(VRInputSource source) => Read(pressed, source);
            public override bool GetStateDown(VRInputSource source) => Read(pressed, source) && !Read(previous, source);
            public override bool GetStateUp(VRInputSource source) => !Read(pressed, source) && Read(previous, source);
            public override void AddOnStateDownListener(Action<VRBooleanAction, VRInputSource> callback, VRInputSource source)
            {
                if (Index(source) >= 0) downListeners.Add(new KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>(Index(source), callback));
            }
            public override void AddOnStateUpListener(Action<VRBooleanAction, VRInputSource> callback, VRInputSource source)
            {
                if (Index(source) >= 0) upListeners.Add(new KeyValuePair<int, Action<VRBooleanAction, VRInputSource>>(Index(source), callback));
            }
        }

        sealed class AxisAction : VRVector2Action
        {
            private readonly OpenXRInputBackend backend;
            private readonly string path;
            internal readonly XRActionSet Set;
            private readonly Vector2[] value = new Vector2[3];
            private readonly Vector2[] previous = new Vector2[3];
            private readonly bool[] bound = new bool[3];
            private readonly bool[] runtime = new bool[2];
            private readonly List<KeyValuePair<int, Action<VRVector2Action, VRInputSource, Vector2, Vector2>>> listeners =
                new List<KeyValuePair<int, Action<VRVector2Action, VRInputSource, Vector2, Vector2>>>();

            public AxisAction(OpenXRInputBackend backend, string path, XRActionSet set) { this.backend = backend; this.path = path; Set = set; }

            internal string Path => path;

            internal void BeginFrame()
            {
                for (int i = 0; i < 3; i++)
                {
                    previous[i] = value[i];
                    value[i] = Vector2.zero;
                    bound[i] = false;
                }
                runtime[0] = runtime[1] = false;
            }
            internal bool FromRuntime(XRHand hand) => runtime[(int)hand];
            internal void BindFromRuntime(XRHand hand, Vector2 axis)
            {
                runtime[(int)hand] = true;
                Bind(hand, axis);
            }
            internal void Bind(XRHand hand, Vector2 axis)
            {
                int i = hand == XRHand.Left ? 1 : 2;
                bound[i] = true;
                if (axis.sqrMagnitude > value[i].sqrMagnitude) value[i] = axis;
            }
            internal void EndFrame()
            {
                // Any is the hand that is pushed furthest.
                value[0] = value[1].sqrMagnitude >= value[2].sqrMagnitude ? value[1] : value[2];
                bound[0] = bound[1] || bound[2];
            }
            internal void Dispatch()
            {
                foreach (var listener in listeners.ToArray())
                {
                    int i = listener.Key;
                    if (value[i] != previous[i]) listener.Value(this, Sources[i], value[i], value[i] - previous[i]);
                }
            }

            public override string GetShortName() => ShortName(path);
            public override bool activeBinding => GetActiveBinding(VRInputSource.Any);
            public override bool GetActiveBinding(VRInputSource source)
            {
                int i = Index(source);
                if (i < 0) return false;
                backend.Refresh();
                return bound[i];
            }
            public override Vector2 axis => GetAxis(VRInputSource.Any);
            public override Vector2 GetAxis(VRInputSource source)
            {
                int i = Index(source);
                if (i < 0) return Vector2.zero;
                backend.Refresh();
                return value[i];
            }
            public override void AddOnChangeListener(Action<VRVector2Action, VRInputSource, Vector2, Vector2> callback, VRInputSource source)
            {
                if (Index(source) >= 0) listeners.Add(new KeyValuePair<int, Action<VRVector2Action, VRInputSource, Vector2, Vector2>>(Index(source), callback));
            }
        }

        sealed class PoseAction : VRPoseAction
        {
            private readonly OpenXRInputBackend backend;
            private readonly string path;
            private readonly XRActionSet set;
            private readonly bool leftHand;
            private readonly bool rightHand;

            public PoseAction(OpenXRInputBackend backend, string path, XRActionSet set, bool leftHand, bool rightHand)
            {
                this.backend = backend;
                this.path = path;
                this.set = set;
                this.leftHand = leftHand;
                this.rightHand = rightHand;
            }

            // The hand a source reads; Any is the left hand where the pose is on both, as the mod only asks Any of
            // the one-handed poses.
            private bool TryHand(VRInputSource source, out XRHand hand)
            {
                hand = XRHand.Left;
                switch (source)
                {
                    case VRInputSource.LeftHand: return leftHand;
                    case VRInputSource.RightHand: hand = XRHand.Right; return rightHand;
                    case VRInputSource.Any:
                        if (!leftHand) hand = XRHand.Right;
                        return leftHand || rightHand;
                    default: return false;
                }
            }

            private bool Bound(VRInputSource source, out XRHand hand) => TryHand(source, out hand) && set.active && backend.IsConnected(hand);

            public override string GetShortName() => ShortName(path);
            public override bool activeBinding => GetActiveBinding(VRInputSource.Any);
            public override bool GetActiveBinding(VRInputSource source) => Bound(source, out _);
            public override Vector3 localPosition => GetLocalPosition(VRInputSource.Any);
            public override Quaternion localRotation => GetLocalRotation(VRInputSource.Any);
            public override Vector3 GetLocalPosition(VRInputSource source)
            {
                return Bound(source, out XRHand hand) && backend.TryGetRawPose(hand, out Vector3 position, out _) ? position : Vector3.zero;
            }
            public override Quaternion GetLocalRotation(VRInputSource source)
            {
                return Bound(source, out XRHand hand) && backend.TryGetRawPose(hand, out _, out Quaternion rotation) ? rotation : Quaternion.identity;
            }
            public override Vector3 GetVelocity(VRInputSource source)
            {
                if (!Bound(source, out XRHand hand)) return Vector3.zero;
                backend.GetRawVelocity(hand, out Vector3 velocity, out _);
                return velocity;
            }
            public override Vector3 GetAngularVelocity(VRInputSource source)
            {
                if (!Bound(source, out XRHand hand)) return Vector3.zero;
                backend.GetRawVelocity(hand, out _, out Vector3 angularVelocity);
                return angularVelocity;
            }
            public override bool GetPoseIsValid(VRInputSource source) => Bound(source, out XRHand hand) && backend.TryGetRawPose(hand, out _, out _);
            public override bool GetDeviceIsConnected(VRInputSource source) => TryHand(source, out XRHand hand) && backend.IsConnected(hand);
        }

        sealed class NoHaptic : VRHapticAction
        {
            public static readonly NoHaptic Instance = new NoHaptic();
            public override void Execute(float delay, float duration, float frequency, float amplitude, VRInputSource source) { }
        }
    }

    // Works out the OpenXR actions early each frame.
    [DefaultExecutionOrder(-30000)]
    sealed class OpenXRInputDriver : MonoBehaviour
    {
        internal OpenXRInputBackend Backend;
        private void Update() => Backend?.Tick();
    }

    // VHVR's OpenXR actions for OpenXRLoaderSetup.ConfigureActions, as plain data: the backend names no OpenXR type.
    sealed class OpenXRActionManifest
    {
        public sealed class Set
        {
            public string Name;
            public string LocalizedName;
        }

        public sealed class Action
        {
            public int Set;
            public string Name;
            public string LocalizedName;
            public bool IsVector2;
        }

        public sealed class Binding
        {
            public string Profile;
            public int Action;
            public string Path;
        }

        public readonly List<Set> Sets = new List<Set>();
        public readonly List<Action> Actions = new List<Action>();
        public readonly List<Binding> Bindings = new List<Binding>();
    }
}
