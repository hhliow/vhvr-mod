using System.Collections.Generic;
using UnityEngine;

namespace ValheimVRMod.VRCore.Backends
{
    delegate bool XRCondition(OpenXRController left, OpenXRController right);
    delegate Vector2 XRAxisReader(OpenXRController left, OpenXRController right);

    sealed class XRBooleanBinding
    {
        public string Action;
        public XRHand Source;
        public XRCondition Condition;
        // Evaluated after every other binding, as it reads the actions those give (see OpenXRProfiles.Held).
        public bool IsChord;
    }

    sealed class XRAxisBinding
    {
        public string Action;
        public XRHand Source;
        public XRAxisReader Read;
    }

    // A binding VHVR suggests to the runtime for one of its OpenXR actions (see OpenXRLoaderSetup.ConfigureActions):
    // Input is the input's path on the hand, e.g. "a/click" for /user/hand/right/input/a/click.
    sealed class XRSuggestedBinding
    {
        public string Action;
        public XRHand Hand;
        public string Input;
        public string Path => (Hand == XRHand.Left ? "/user/hand/left" : "/user/hand/right") + "/input/" + Input;
    }

    // One OpenXR interaction profile: its path, the Input System layout of its controllers, and where its grip pose
    // is from the SteamVR raw pose. And the mod's actions on it, two ways:
    // - Suggested: the bindings VHVR suggests to the runtime for its own OpenXR actions, for each action one input
    //   can stand for. The runtime may rebind these (e.g. in SteamVR's binding UI).
    // - Booleans and Axes: the same bindings and the rest (chords, stick directions, thresholds), worked out by the
    //   mod from the controller's controls. They apply to an action of a hand only while the runtime has it unbound.
    // A worked out binding is evaluated with the profile of the hand it is reported on (its Source); a chord across
    // hands reads the other hand by the same control names, so it holds only when both hands are of the same profile.
    sealed class OpenXRProfile
    {
        public readonly string Name;
        public readonly string Layout;
        // The device's product name (kDeviceLocalizedName in the plugin), should the runtime give a layout derived
        // from the profile's.
        public readonly string Product;
        public readonly string InteractionProfile;
        public readonly List<XRSuggestedBinding> Suggested = new List<XRSuggestedBinding>();
        public readonly List<XRBooleanBinding> Booleans = new List<XRBooleanBinding>();
        public readonly List<XRAxisBinding> Axes = new List<XRAxisBinding>();
        // The right hand's grip pose in the raw pose, in OpenVR's right-handed coordinates, as SteamVR's render model
        // files give it (see OpenXRController.setGrip); the left hand's is mirrored.
        public Vector3 GripPosition;
        public Vector3 GripEuler;

        public OpenXRProfile(string name, string interactionProfile, string layout, string product)
        {
            Name = name;
            InteractionProfile = interactionProfile;
            Layout = layout;
            Product = product;
        }

        public void Suggest(string action, XRHand hand, params string[] inputs)
        {
            foreach (string input in inputs)
            {
                Suggested.Add(new XRSuggestedBinding { Action = action, Hand = hand, Input = input });
            }
        }

        public void Bool(string action, XRHand source, XRCondition condition) =>
            Booleans.Add(new XRBooleanBinding { Action = action, Source = source, Condition = condition });

        public void Axis(string action, XRHand source, XRAxisReader read) =>
            Axes.Add(new XRAxisBinding { Action = action, Source = source, Read = read });

        // A chord is reported on the hand of its last input, which is the button of a modifier + button chord.
        // SteamVR reports the buttons that make up a chord as well as the chord, and so does this (LaserPointerChords
        // counts on it).
        public void Chord(string action, XRCondition modifier, XRHand buttonHand, XRCondition button) =>
            Booleans.Add(new XRBooleanBinding
            {
                Action = action,
                Source = buttonHand,
                Condition = (l, r) => modifier(l, r) && button(l, r),
                IsChord = true,
            });
    }

    // The bindings of each interaction profile that OpenXRLoaderSetup enables, written after the mod's SteamVR
    // bindings for the same controllers (StreamingAssets/SteamVR/bindings_*.json): Touch after oculus_touch, Index
    // after knuckles, Vive after vive_controller, WMR after holographic_controller. The Quest Touch Plus and Pro
    // controllers and the HP Reverb G2 controllers, which have profiles of their own, use the Touch bindings, the
    // G2 without the touch sensors it lacks. SteamVR's scroll modes
    // (ContextScroll, HotbarScroll) and its single-axis Squeeze have no counterpart yet. There is no rebinding.
    static class OpenXRProfiles
    {
        // Before the profiles: static fields are initialized in the order they are written, and the profiles use it.
        private static readonly XRHand[] Hands = { XRHand.Left, XRHand.Right };

        public static readonly OpenXRProfile Touch =
            touch("Oculus Touch", "/interaction_profiles/oculus/touch_controller", "OculusTouchController", "Oculus Touch Controller OpenXR", touchSensors: true);
        public static readonly OpenXRProfile TouchPlus =
            touch("Meta Quest Touch Plus", "/interaction_profiles/meta/touch_controller_plus", "QuestTouchPlusController", "Meta Quest Touch Plus Controller OpenXR", touchSensors: true);
        public static readonly OpenXRProfile TouchPro =
            touch("Meta Quest Touch Pro", "/interaction_profiles/facebook/touch_controller_pro", "QuestProTouchController", "Meta Quest Pro Touch Controller OpenXR", touchSensors: true);
        public static readonly OpenXRProfile ReverbG2 =
            touch("HP Reverb G2", "/interaction_profiles/hp/mixed_reality_controller", "ReverbG2Controller", "HP Reverb G2 Controller OpenXR", touchSensors: false);
        public static readonly OpenXRProfile Index = index();
        public static readonly OpenXRProfile Vive = vive();
        public static readonly OpenXRProfile WindowsMR = windowsMR();
        public static readonly OpenXRProfile Simple = simple();
        public static readonly OpenXRProfile[] All = { Touch, TouchPlus, TouchPro, ReverbG2, Index, Vive, WindowsMR, Simple };

        public static OpenXRProfile Find(string layout, string product)
        {
            foreach (OpenXRProfile profile in All)
            {
                if (profile.Layout == layout) return profile;
            }
            foreach (OpenXRProfile profile in All)
            {
                if (profile.Product == product) return profile;
            }
            return null;
        }

        private const XRHand L = XRHand.Left;
        private const XRHand R = XRHand.Right;

        private static string D(string name) => "/actions/default/in/" + name;
        private static string V(string name) => "/actions/Valheim/in/" + name;

        private static OpenXRController On(XRHand hand, OpenXRController l, OpenXRController r) => hand == L ? l : r;
        private static XRCondition Click(XRHand hand, string control) => (l, r) => On(hand, l, r).Button(control);
        private static XRCondition AtLeast(XRHand hand, string control, float threshold) =>
            (l, r) => On(hand, l, r).Axis(control) >= threshold;
        private static XRCondition AnyOf(params XRCondition[] conditions) => (l, r) =>
        {
            foreach (XRCondition condition in conditions)
            {
                if (condition(l, r)) return true;
            }
            return false;
        };
        private static XRCondition AllOf(XRCondition a, XRCondition b) => (l, r) => a(l, r) && b(l, r);

        // A mod action as pressed on the hand this frame, by the runtime or by the bindings before the chords. The
        // chords are made of these where an action stands for the input (the grip is Grab, the trigger LeftClick,
        // the button under the right click RightClick): read from the controls instead, a chord would change at
        // another threshold and in another frame than the action it is made of. E.g. a grip + trigger chord would come
        // a frame after the trigger's LeftClick, which picks the hovered inventory item up first, and the chord is
        // ignored while an item is dragged. Rebinding the action rebinds its chords with it.
        private static XRCondition Held(XRHand hand, string action) => (l, r) => On(hand, l, r).ActionPressed(action);
        private static XRCondition GripHeld(XRHand hand) => Held(hand, V("Grab"));
        private static XRCondition ClickHeld(XRHand hand) => Held(hand, V("LeftClick"));
        private static XRCondition RightClickHeld(XRHand hand) => Held(hand, V("RightClick"));
        private static XRAxisReader Position(XRHand hand, string control) => (l, r) => On(hand, l, r).Vector2(control);

        enum Dir { North, East, South, West, Any }

        // A SteamVR dpad direction: the stick or pad out of the deadzone (a fraction of its range) within the
        // direction's quarter, widened by the overlap (a fraction of a quarter). Any is every direction.
        private static XRCondition Pad(XRHand hand, string control, Dir dir, float deadzone = 0.25f, float overlap = 0)
        {
            float center = dir == Dir.East ? 0 : dir == Dir.North ? 90 : dir == Dir.West ? 180 : -90;
            float halfWidth = 45 * (1 + overlap);
            return (l, r) =>
            {
                Vector2 v = On(hand, l, r).Vector2(control);
                if (v.magnitude <= deadzone)
                {
                    return false;
                }
                if (dir == Dir.Any)
                {
                    return true;
                }
                float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                return Mathf.Abs(Mathf.DeltaAngle(angle, center)) <= halfWidth;
            };
        }

        // A dpad of SteamVR's click sub-mode: the pad clicked in the direction.
        private static XRCondition PadClick(XRHand hand, string pad, string click, Dir dir, float deadzone = 0.5f) =>
            AllOf(Click(hand, click), Pad(hand, pad, dir, deadzone));

        // Bindings of the default action set (SteamVR's interaction system), on every profile with a trigger.
        private static void defaults(OpenXRProfile p, XRHand h, XRCondition trigger, XRCondition grip, string triggerAxis,
                                     float pinch, XRCondition teleport)
        {
            p.Bool(D("InteractUI"), h, trigger);
            p.Bool(D("GrabGrip"), h, grip);
            p.Bool(D("GrabPinch"), h, AtLeast(h, triggerAxis, pinch));
            p.Bool(D("Teleport"), h, teleport);
        }

        // The chords that the Touch, Index and Vive bindings share, on each one's own grip and trigger.
        private static void itemChords(OpenXRProfile p, System.Func<XRHand, XRCondition> grip, System.Func<XRHand, XRCondition> trigger)
        {
            foreach (XRHand h in Hands)
            {
                p.Chord(V("AddMapPin"), grip(h), h, trigger(h));
                p.Chord(V("DiscardItem"), grip(h), h, trigger(h));
            }
            p.Chord(V("SplitStack"), grip(L), R, trigger(R));
            p.Chord(V("SplitStack"), grip(R), L, trigger(L));
        }

        // Oculus Touch, and the controllers runtimes give as Touch (Quest, Rift, Pico...): bindings_oculus_touch.json.
        // Also the controllers of the same layout with profiles of their own; without touch sensors, the stick moved
        // stops gestured locomotion instead.
        private static OpenXRProfile touch(string name, string interactionProfile, string layout, string product, bool touchSensors)
        {
            var p = new OpenXRProfile(name, interactionProfile, layout, product);
            // SteamVR shares these across Quest, Rift, Pico and Vive Focus controllers ("openxr_grip").
            p.GripPosition = new Vector3(-0.007f, -0.00182941f, 0.1019482f);
            p.GripEuler = new Vector3(20.6f, 0, 0);
            const string stick = "thumbstick";
            // The grip as a button: the grip button where the runtime gives one, else squeezed most of the way.
            XRCondition Grip(XRHand h) => AnyOf(Click(h, "gripPressed"), AtLeast(h, "grip", 0.8f));
            XRCondition Trigger(XRHand h) => Click(h, "triggerPressed");
            foreach (XRHand h in Hands)
            {
                defaults(p, h, Trigger(h), Grip(h), "trigger", 0.8f, Pad(h, stick, Dir.North, 0.25f, 0.3f));
                p.Bool(V("Use"), h, Trigger(h));
                p.Bool(V("LeftClick"), h, Trigger(h));
                p.Bool(V("Grab"), h, Grip(h));
                p.Bool(V("StopGesturedLocomotion"), h, touchSensors ?
                    AnyOf(Trigger(h), Grip(h), Click(h, "thumbstickTouched"), Click(h, "primaryTouched"), Click(h, "secondaryTouched")) :
                    AnyOf(Trigger(h), Grip(h), Pad(h, stick, Dir.Any)));
            }
            p.Bool(V("ToggleMap"), L, Click(L, "thumbstickClicked"));
            p.Axis(V("Walk"), L, Position(L, stick));
            p.Bool(V("ToggleInventory"), L, Click(L, "primaryButton"));
            p.Bool(V("QuickActions"), L, Click(L, "secondaryButton"));
            p.Bool(V("RightClick"), L, Click(L, "secondaryButton"));

            p.Bool(V("ToggleMenu"), R, Click(R, "thumbstickClicked"));
            p.Axis(V("PitchAndYaw"), R, Position(R, stick));
            p.Bool(V("Jump"), R, Click(R, "primaryButton"));
            p.Bool(V("QuickSwitch"), R, Click(R, "secondaryButton"));
            p.Bool(V("RightClick"), R, Click(R, "secondaryButton"));
            p.Bool(V("ToggleRun"), R, Pad(R, stick, Dir.North));
            p.Bool(V("ToggleCrouch"), R, Pad(R, stick, Dir.South));

            p.Chord(V("FacePieceToward"), GripHeld(R), R, Pad(R, stick, Dir.South));
            p.Chord(V("FacePieceAway"), GripHeld(R), R, Pad(R, stick, Dir.North));
            p.Chord(V("RotatePieceClockwise"), GripHeld(R), R, Pad(R, stick, Dir.West));
            p.Chord(V("RotatePieceCounterclockwise"), GripHeld(R), R, Pad(R, stick, Dir.East));
            p.Chord(V("RemovePiece"), GripHeld(R), R, Click(R, "primaryButton"));
            p.Chord(V("ScrollUp"), GripHeld(R), R, Pad(R, stick, Dir.North));
            p.Chord(V("ScrollDown"), GripHeld(R), R, Pad(R, stick, Dir.South));
            p.Chord(V("Dodge"), Trigger(L), R, Click(R, "primaryButton"));
            foreach (XRHand h in Hands)
            {
                p.Chord(V("MiddleClick"), GripHeld(h), h, RightClickHeld(h));
            }
            itemChords(p, GripHeld, ClickHeld);
            // Suggested: what one input stands for. Triggers and grips are values; the runtime thresholds them.
            foreach (XRHand h in Hands)
            {
                p.Suggest(D("InteractUI"), h, "trigger/value");
                p.Suggest(D("GrabGrip"), h, "squeeze/value");
                p.Suggest(V("Use"), h, "trigger/value");
                p.Suggest(V("LeftClick"), h, "trigger/value");
                p.Suggest(V("Grab"), h, "squeeze/value");
            }
            if (touchSensors)
            {
                p.Suggest(V("StopGesturedLocomotion"), L, "trigger/value", "squeeze/value", "thumbstick/touch", "x/touch", "y/touch");
                p.Suggest(V("StopGesturedLocomotion"), R, "trigger/value", "squeeze/value", "thumbstick/touch", "a/touch", "b/touch");
            }
            p.Suggest(V("ToggleMap"), L, "thumbstick/click");
            p.Suggest(V("Walk"), L, "thumbstick");
            p.Suggest(V("ToggleInventory"), L, "x/click");
            p.Suggest(V("QuickActions"), L, "y/click");
            p.Suggest(V("RightClick"), L, "y/click");
            p.Suggest(V("ToggleMenu"), R, "thumbstick/click");
            p.Suggest(V("PitchAndYaw"), R, "thumbstick");
            p.Suggest(V("Jump"), R, "a/click");
            p.Suggest(V("QuickSwitch"), R, "b/click");
            p.Suggest(V("RightClick"), R, "b/click");
            return p;
        }

        // Valve Index: bindings_knuckles.json.
        private static OpenXRProfile index()
        {
            var p = new OpenXRProfile("Valve Index", "/interaction_profiles/valve/index_controller", "ValveIndexController", "Index Controller OpenXR");
            // SteamVR's Index model has no "openxr_grip", so its "grip" component.
            p.GripPosition = new Vector3(0, 0.003f, 0.097f);
            p.GripEuler = new Vector3(5.037f, 0, 0);
            const string stick = "thumbstick";
            // SteamVR clicks the Index grip and trackpad by force: their values follow the fingers resting on them.
            XRCondition Grip(XRHand h) => AtLeast(h, "gripForce", 0.35f);
            XRCondition Trigger(XRHand h) => Click(h, "triggerPressed");
            XRCondition TrackpadClick(XRHand h) => AtLeast(h, "trackpadForce", 0.3f);
            foreach (XRHand h in Hands)
            {
                defaults(p, h, Trigger(h), Grip(h), "trigger", 0.8f, Pad(h, stick, Dir.North, 0.25f, 0.3f));
                p.Bool(V("Use"), h, Trigger(h));
                p.Bool(V("LeftClick"), h, Trigger(h));
                p.Bool(V("Grab"), h, Grip(h));
                p.Bool(V("RightClick"), h, Click(h, "secondaryButton"));
                p.Bool(V("StopGesturedLocomotion"), h, AnyOf(
                    Trigger(h), Click(h, "trackpadTouched"), Click(h, "thumbstickTouched"), Click(h, "primaryTouched"),
                    Click(h, "secondaryTouched"), Click(h, "systemTouched")));
            }
            p.Axis(V("Walk"), L, Position(L, stick));
            p.Bool(V("ToggleMenu"), L, TrackpadClick(L));
            p.Bool(V("ToggleMap"), L, Click(L, "primaryButton"));
            p.Bool(V("QuickActions"), L, Click(L, "secondaryButton"));

            p.Axis(V("PitchAndYaw"), R, Position(R, stick));
            p.Bool(V("ToggleInventory"), R, TrackpadClick(R));
            p.Bool(V("Jump"), R, Click(R, "primaryButton"));
            p.Bool(V("QuickSwitch"), R, Click(R, "secondaryButton"));
            p.Bool(V("ToggleRun"), R, Pad(R, stick, Dir.North));
            p.Bool(V("ToggleCrouch"), R, Pad(R, stick, Dir.South));

            p.Chord(V("FacePieceToward"), GripHeld(R), R, Pad(R, stick, Dir.South));
            p.Chord(V("FacePieceAway"), GripHeld(R), R, Pad(R, stick, Dir.North));
            p.Chord(V("RotatePieceClockwise"), GripHeld(R), R, Pad(R, stick, Dir.West));
            p.Chord(V("RotatePieceCounterclockwise"), GripHeld(R), R, Pad(R, stick, Dir.East));
            p.Chord(V("RemovePiece"), GripHeld(R), R, Click(R, "primaryButton"));
            p.Chord(V("Dodge"), GripHeld(L), R, Click(R, "primaryButton"));
            foreach (XRHand h in Hands)
            {
                p.Chord(V("MiddleClick"), GripHeld(h), h, RightClickHeld(h));
            }
            itemChords(p, GripHeld, ClickHeld);
            foreach (XRHand h in Hands)
            {
                p.Suggest(D("InteractUI"), h, "trigger/click");
                p.Suggest(D("GrabGrip"), h, "squeeze/force");
                p.Suggest(V("Use"), h, "trigger/click");
                p.Suggest(V("LeftClick"), h, "trigger/click");
                p.Suggest(V("Grab"), h, "squeeze/force");
                p.Suggest(V("RightClick"), h, "b/click");
                p.Suggest(V("StopGesturedLocomotion"), h,
                          "trigger/click", "trackpad/touch", "thumbstick/touch", "a/touch", "b/touch", "system/touch");
            }
            p.Suggest(V("Walk"), L, "thumbstick");
            p.Suggest(V("ToggleMenu"), L, "trackpad/force");
            p.Suggest(V("ToggleMap"), L, "a/click");
            p.Suggest(V("QuickActions"), L, "b/click");
            p.Suggest(V("PitchAndYaw"), R, "thumbstick");
            p.Suggest(V("ToggleInventory"), R, "trackpad/force");
            p.Suggest(V("Jump"), R, "a/click");
            p.Suggest(V("QuickSwitch"), R, "b/click");
            return p;
        }

        // HTC Vive wands: bindings_vive_controller.json. The trackpad stands in for the stick.
        private static OpenXRProfile vive()
        {
            var p = new OpenXRProfile("HTC Vive", "/interaction_profiles/htc/vive_controller", "ViveController", "HTC Vive Controller OpenXR");
            p.GripPosition = new Vector3(0, -0.015f, 0.097f);
            p.GripEuler = new Vector3(5.037f, 0, 0);
            const string pad = "trackpad";
            const string click = "trackpadClicked";
            XRCondition Grip(XRHand h) => Click(h, "gripPressed");
            XRCondition Trigger(XRHand h) => Click(h, "triggerPressed");
            foreach (XRHand h in Hands)
            {
                defaults(p, h, Trigger(h), Grip(h), "trigger", 0.75f, Click(h, click));
                p.Bool(V("Use"), h, Trigger(h));
                p.Bool(V("LeftClick"), h, Trigger(h));
                p.Bool(V("Grab"), h, Grip(h));
                p.Bool(V("RightClick"), h, Click(h, "menu"));
                p.Bool(V("StopGesturedLocomotion"), h, AnyOf(Trigger(h), Grip(h), Click(h, "trackpadTouched")));
            }
            p.Axis(V("Walk"), L, Position(L, pad));
            p.Bool(V("ToggleInventory"), L, Click(L, click));
            p.Bool(V("QuickActions"), L, Click(L, "menu"));
            p.Bool(V("ScrollUp"), L, AllOf(Click(L, "trackpadTouched"), Pad(L, pad, Dir.North, 0.5f)));
            p.Bool(V("ScrollDown"), L, AllOf(Click(L, "trackpadTouched"), Pad(L, pad, Dir.South, 0.5f)));

            p.Axis(V("PitchAndYaw"), R, Position(R, pad));
            p.Bool(V("Jump"), R, Click(R, click));
            p.Bool(V("QuickSwitch"), R, Click(R, "menu"));
            p.Bool(V("ToggleRun"), R, PadClick(R, pad, click, Dir.North, 0.25f));
            p.Bool(V("ToggleCrouch"), R, PadClick(R, pad, click, Dir.South, 0.25f));

            p.Chord(V("FacePieceToward"), GripHeld(R), R, PadClick(R, pad, click, Dir.South, 0.25f));
            p.Chord(V("FacePieceAway"), GripHeld(R), R, PadClick(R, pad, click, Dir.North, 0.25f));
            p.Chord(V("RotatePieceClockwise"), GripHeld(R), R, PadClick(R, pad, click, Dir.West, 0.25f));
            p.Chord(V("RotatePieceCounterclockwise"), GripHeld(R), R, PadClick(R, pad, click, Dir.East, 0.25f));
            p.Chord(V("RemovePiece"), GripHeld(R), R, Click(R, click));
            p.Chord(V("ToggleMenu"), GripHeld(R), R, Click(R, "menu"));
            p.Chord(V("ToggleMap"), GripHeld(L), L, Click(L, "menu"));
            p.Chord(V("Dodge"), GripHeld(L), R, Click(R, click));
            p.Chord(V("MiddleClick"), GripHeld(R), R, PadClick(R, pad, click, Dir.South, 0.25f));
            itemChords(p, GripHeld, ClickHeld);
            foreach (XRHand h in Hands)
            {
                p.Suggest(D("InteractUI"), h, "trigger/click");
                p.Suggest(D("GrabGrip"), h, "squeeze/click");
                p.Suggest(D("Teleport"), h, "trackpad/click");
                p.Suggest(V("Use"), h, "trigger/click");
                p.Suggest(V("LeftClick"), h, "trigger/click");
                p.Suggest(V("Grab"), h, "squeeze/click");
                p.Suggest(V("RightClick"), h, "menu/click");
                p.Suggest(V("StopGesturedLocomotion"), h, "trigger/click", "squeeze/click", "trackpad/touch");
            }
            p.Suggest(V("Walk"), L, "trackpad");
            p.Suggest(V("ToggleInventory"), L, "trackpad/click");
            p.Suggest(V("QuickActions"), L, "menu/click");
            p.Suggest(V("PitchAndYaw"), R, "trackpad");
            p.Suggest(V("Jump"), R, "trackpad/click");
            p.Suggest(V("QuickSwitch"), R, "menu/click");
            return p;
        }

        // Windows Mixed Reality: bindings_holographic_controller.json. The stick moves and turns; the touchpad's
        // quarters are buttons.
        private static OpenXRProfile windowsMR()
        {
            var p = new OpenXRProfile("Windows Mixed Reality", "/interaction_profiles/microsoft/motion_controller", "WMRSpatialController", "Windows MR Controller OpenXR");
            // SteamVR has no offsets for these; the Touch ones stand in, as for any other controller.
            p.GripPosition = Touch.GripPosition;
            p.GripEuler = Touch.GripEuler;
            const string stick = "joystick";
            const string pad = "touchpad";
            const string click = "touchpadClicked";
            XRCondition Grip(XRHand h) => Click(h, "gripPressed");
            XRCondition Trigger(XRHand h) => Click(h, "triggerPressed");
            XRCondition Quarter(XRHand h, Dir dir) => PadClick(h, pad, click, dir);
            foreach (XRHand h in Hands)
            {
                defaults(p, h, Trigger(h), Grip(h), "trigger", 0.95f,
                         AnyOf(Click(h, click), Pad(h, stick, Dir.North, 0.25f, 0.3f)));
                p.Bool(V("Use"), h, Trigger(h));
                p.Bool(V("LeftClick"), h, Trigger(h));
                p.Bool(V("Grab"), h, Grip(h));
                p.Bool(V("QuickSwitch"), h, Quarter(h, Dir.East));
                p.Bool(V("QuickActions"), h, Quarter(h, Dir.West));
            }
            p.Axis(V("Walk"), L, Position(L, stick));
            p.Bool(V("ToggleMenu"), L, Click(L, "menu"));
            p.Bool(V("ToggleMap"), L, Quarter(L, Dir.North));
            p.Bool(V("HotbarUse"), L, Quarter(L, Dir.South));
            p.Bool(V("RightClick"), L, Quarter(L, Dir.West));

            p.Axis(V("PitchAndYaw"), R, Position(R, stick));
            p.Bool(V("ToggleInventory"), R, Click(R, "menu"));
            p.Bool(V("Jump"), R, Quarter(R, Dir.North));
            p.Bool(V("Sit"), R, Quarter(R, Dir.South));
            p.Bool(V("RightClick"), R, Quarter(R, Dir.East));
            p.Bool(V("StopGesturedLocomotion"), R, Pad(R, stick, Dir.Any));

            p.Chord(V("FacePieceToward"), GripHeld(R), R, Pad(R, stick, Dir.South));
            p.Chord(V("FacePieceAway"), GripHeld(R), R, Pad(R, stick, Dir.North));
            p.Chord(V("RotatePieceClockwise"), GripHeld(R), R, Pad(R, stick, Dir.West));
            p.Chord(V("RotatePieceCounterclockwise"), GripHeld(R), R, Pad(R, stick, Dir.East));
            p.Chord(V("RemovePiece"), GripHeld(R), R, Quarter(R, Dir.North));
            p.Chord(V("MiddleClick"), GripHeld(L), R, GripHeld(R));
            p.Chord(V("AddMapPin"), GripHeld(R), R, ClickHeld(R));
            p.Chord(V("DiscardItem"), GripHeld(R), R, ClickHeld(R));
            p.Chord(V("SplitStack"), GripHeld(L), R, ClickHeld(R));
            foreach (XRHand h in Hands)
            {
                p.Suggest(D("InteractUI"), h, "trigger/value");
                p.Suggest(D("GrabGrip"), h, "squeeze/click");
                p.Suggest(V("Use"), h, "trigger/value");
                p.Suggest(V("LeftClick"), h, "trigger/value");
                p.Suggest(V("Grab"), h, "squeeze/click");
            }
            p.Suggest(V("Walk"), L, "thumbstick");
            p.Suggest(V("ToggleMenu"), L, "menu/click");
            p.Suggest(V("PitchAndYaw"), R, "thumbstick");
            p.Suggest(V("ToggleInventory"), R, "menu/click");
            return p;
        }

        // The Khronos simple controller, which a runtime falls back to for a controller of no other profile: a select
        // and a menu button only, enough to use the menus and the GUI.
        private static OpenXRProfile simple()
        {
            var p = new OpenXRProfile("Khronos simple", "/interaction_profiles/khr/simple_controller", "KHRSimpleController", "KHR Simple Controller OpenXR");
            p.GripPosition = Touch.GripPosition;
            p.GripEuler = Touch.GripEuler;
            foreach (XRHand h in Hands)
            {
                XRCondition select = Click(h, "select");
                p.Bool(D("InteractUI"), h, select);
                p.Bool(V("Use"), h, select);
                p.Bool(V("LeftClick"), h, select);
                p.Bool(V("ToggleMenu"), h, Click(h, "menu"));
            }
            foreach (XRHand h in Hands)
            {
                p.Suggest(D("InteractUI"), h, "select/click");
                p.Suggest(V("Use"), h, "select/click");
                p.Suggest(V("LeftClick"), h, "select/click");
                p.Suggest(V("ToggleMenu"), h, "menu/click");
            }
            return p;
        }
    }
}
