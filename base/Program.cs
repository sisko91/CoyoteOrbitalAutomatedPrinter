using Sandbox.Engine.Utils;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript {
    //TODO: Refactor, clean up
    //Split up COAP specific code so other generic print heads can be used
    partial class Program : MyGridProgram {
        private static string IGC_STATION_NAME = "PrinterBase";
        private static string IGC_TUG_NAME = "PrinterTug";
        private static string IGC_CALLBACK = "broadcast";

        IMyBroadcastListener listener;
        IMyUnicastListener unicastListener;

        IMyMotorAdvancedStator rotor;
        List<IMyPistonBase> pistons = new List<IMyPistonBase>();
        List<IMyShipWelder> welders = new List<IMyShipWelder>();
        List<IMyMotorStator> hinges = new List<IMyMotorStator>();
        Dictionary<string, List<IMyLightingBlock>> lightLayers = new Dictionary<string, List<IMyLightingBlock>>();

        long broadcastID = 0;
        bool rotorWeldersOn;
        double lightTimer;
        double rotorRPM = 1;

        enum State {
            Idle,
            Packed,
            Packing,
            Packing_ResetRotor,
            Packing_RetractHinge,
            Unpacking,
            Unpacked
        }

        State state = State.Idle;
        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            listener = IGC.RegisterBroadcastListener(IGC_STATION_NAME);
            listener.SetMessageCallback(IGC_CALLBACK);
            unicastListener = IGC.UnicastListener;
            unicastListener.SetMessageCallback(IGC_CALLBACK);

            rotor = GridTerminalSystem.GetBlockWithName("Welder Rotor") as IMyMotorAdvancedStator;

            IMyBlockGroup pistonGroup = GridTerminalSystem.GetBlockGroupWithName("Pistons");
            pistonGroup.GetBlocksOfType(pistons);

            IMyBlockGroup welderGroup = GridTerminalSystem.GetBlockGroupWithName("Welders");
            welderGroup.GetBlocksOfType(welders);

            IMyBlockGroup hingeGroup = GridTerminalSystem.GetBlockGroupWithName("Hinges");
            hingeGroup.GetBlocksOfType(hinges);

            List<IMyLightingBlock> lights = new List<IMyLightingBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyLightingBlock>(lights);
            foreach (IMyLightingBlock light in lights) {
                List<IMyLightingBlock> theseLights;

                if (!lightLayers.TryGetValue(light.CustomName, out theseLights)) {
                    theseLights = new List<IMyLightingBlock>();
                }

                theseLights.Add(light);
                lightLayers[light.CustomName] = theseLights;
            }

            Stop();
        }

        public void Main(string argument, UpdateType updateSource) {
            Echo($"broadcastID: {broadcastID}");
            Echo($"{listener.Tag}");

            HandleArgs(argument);
            Echo(state.ToString());

            if (state.ToString().StartsWith("Packing")) {
                Pack();
            } else if (state.ToString().StartsWith("Unpacking")) {
                Unpack();
            }

            if (rotorWeldersOn) {
                Echo("Rotor/Welders On");
            } else if (state == State.Unpacked) {
                SetLights(100, true);
            }
        }

        private void HandleArgs(string arg) {
            if (arg == IGC_CALLBACK) {
                HandleIGCMessages();
            }else if (arg == "RotorWeldersOn") {
                state = State.Idle;
                rotorWeldersOn = true;
                ToggleWelders(true);
                ToggleRotor(true);
            } else if (arg == "RotorWeldersOff") {
                rotorWeldersOn = false;
                ToggleWelders(false);
                ToggleRotor(false);
            } else if (arg == "WeldersOn") {
                ToggleWelders(true);
            } else if (arg == "WeldersOff") {
                ToggleWelders(false);
            } else if (arg == "Pack") {
                state = State.Packing;
                rotorWeldersOn = false;
                ToggleWelders(false);
                SetHingeSpeed(0);
            } else if (arg == "Unpack") {
                state = State.Unpacking;
                rotorWeldersOn = false;
                ToggleWelders(false);

                //Set position to 2 large-grid blocks in front of the welder plane
                var rotorToWelder = Vector3D.TransformNormal(rotor.GetPosition() - welders[0].GetPosition(), MatrixD.Transpose(rotor.WorldMatrix));
                var tugStartPosition = rotor.GetPosition() - Vector3D.TransformNormal(new Vector3D(0, rotorToWelder.Y - 2 * 2.5, 0), rotor.WorldMatrix);

                var payload = MyTuple.Create<Vector3D, MatrixD>(tugStartPosition, rotor.WorldMatrix);
                if (broadcastID == 0) {
                    IGC.SendBroadcastMessage<MyTuple<Vector3D, MatrixD>>(IGC_TUG_NAME, payload, TransmissionDistance.TransmissionDistanceMax);
                } else {
                    IGC.SendUnicastMessage(broadcastID, "", payload);
                }
            } else if (arg == "Extend") {
                MovePiston(true);
                ToggleWelders(false);
            } else if (arg == "Retract") {
                MovePiston(false);
                ToggleWelders(false);
            }
        }

        private void Pack() {
            bool wait = true;
            if (state == State.Packing) {
                ResetRotor();
                state = State.Packing_ResetRotor;
            } else if (state == State.Packing_ResetRotor) {
                wait = !IsRotorReset();
            } else {
                wait = false;
            }

            //Wait for rotor to return to position before engaging pistons and hinges (just for show)
            if (wait) {
                return;
            }

            //Turn off lights while hinges retract
            wait = true;

            float hingeOpenPercent = GetHingeOpenPercent();
            if (state == State.Packing_ResetRotor) {
                SetHingeSpeed(1);
                MovePiston(false);
                state = State.Packing_RetractHinge;
            } else if (state == State.Packing_RetractHinge) {
                Echo(Convert.ToString(hingeOpenPercent));
                wait = hingeOpenPercent > 0.01; ;
            }

            SetLights(hingeOpenPercent);

            if (!wait) {
                SetHingeSpeed(0);
                state = State.Packed;
            }
        }

        public void Unpack() {
            bool wait = !IsRotorReset();
            if (wait) {
                ResetRotor();
            }

            MovePiston(false);
            SetHingeSpeed(-1);

            //Turn on lights while hinges extend
            float hingeOpenPercent = GetHingeOpenPercent();
            wait = wait || hingeOpenPercent < 99.99;

            if (!wait) {
                SetHingeSpeed(0);
                state = State.Unpacked;
                lightTimer = 0;
            }

            SetLights(hingeOpenPercent);
        }

        public void SetLights(float openPercent, bool flashing = false, Color? color = null) {
            int cornerLightLayers = 11;
            int spotlightLayers = 5;

            lightTimer = (lightTimer + Runtime.TimeSinceLastRun.TotalSeconds * 3) % cornerLightLayers;

            for (int i = 1; i <= cornerLightLayers; i++) {
                foreach (IMyLightingBlock light in lightLayers[$"Corner Light L{i}"]) {
                    float offset = (float)(i) / cornerLightLayers;
                    bool enabled = offset <= openPercent / 100 + 0.01;

                    if (enabled) {
                        light.Color = color ?? new Color(200, 140, 2);
                    } else {
                        light.Color = new Color(50, 35, 0);
                    }

                    if (flashing && Math.Abs(lightTimer - i) < 1 && light.CubeGrid != Me.CubeGrid) {
                        light.Color = new Color(255, 195, 57);
                    }
                }
            }
            for (int i = 1; i <= spotlightLayers; i++) {
                foreach (IMyLightingBlock light in lightLayers[$"Spotlight L{i}"]) {
                    float offset = (float)(i) / spotlightLayers;
                    light.Enabled = offset <= openPercent / 100 + 0.01;
                }
            }
        }

        public void Stop() {
            ToggleWelders(false);
            ToggleRotor(false);
        }

        public void MovePiston(bool extend) {
            foreach (IMyPistonBase piston in pistons) {
                if (piston.CustomName == "PistonH" && state.ToString().StartsWith("Pack")) {
                    piston.MinLimit = 1.4f;
                    piston.MaxLimit = 1.4f;
                } else {
                    piston.MinLimit = 0;
                    piston.MaxLimit = 10f;
                }

                if (extend || piston.CurrentPosition < piston.MinLimit) {
                    piston.Velocity = 2;
                } else {
                    piston.Velocity = -2;
                }
            }
        }

        public void ToggleWelders(bool on) {
            ToggleWelders(on, !on || pistons[0].CurrentPosition < 1, true);
        }

        private void ToggleWelders(bool on, bool useInner, bool useOuter) {
            foreach (IMyShipWelder welder in welders) {
                bool isInner = welder.CustomName.StartsWith("Inner");
                if (isInner && !useInner || !isInner && !useOuter) {
                    continue;
                }

                if (on) {
                    welder.ApplyAction("OnOff_On");
                } else {
                    welder.ApplyAction("OnOff_Off");
                }
            }
        }

        public void ToggleRotor(bool on) {
            if (on) {
                rotor.LowerLimitDeg = -361;
                rotor.UpperLimitDeg = 361;
                rotor.TargetVelocityRPM = Convert.ToSingle(rotorRPM);
                rotor.RotorLock = false;
            } else {
                rotor.LowerLimitDeg = -361;
                rotor.UpperLimitDeg = 361;
                rotor.TargetVelocityRPM = 0;
                rotor.RotorLock = true;
            }
        }

        public float GetHingeOpenPercent() {
            var hinge = hinges[0];
            float range = hinge.UpperLimitRad - hinge.LowerLimitRad;

            return (1 - (hinge.Angle - hinge.LowerLimitRad) / range) * 100;
        }

        public void SetHingeSpeed(float speed) {
            foreach (IMyMotorStator hinge in hinges) {
                if (Math.Abs(speed) < 0.01) {
                    hinge.RotorLock = true;
                } else {
                    hinge.RotorLock = false;
                }

                hinge.TargetVelocityRPM = speed;
            }
        }

        public void ResetRotor() {
            if (IsRotorReset()) {
                return;
            }

            float angle = rotor.Angle;
            if (angle < 0) {
                angle = Convert.ToSingle(Math.PI) * 2.0f + angle;
            }

            rotor.LowerLimitDeg = rotor.Angle > Math.PI ? 0 : -360;
            rotor.UpperLimitDeg = rotor.Angle > Math.PI ? 360 : 0;
            rotor.RotorLock = false;
            rotor.TargetVelocityRPM = rotor.Angle > Math.PI ? 2 : -2;
        }

        public bool IsRotorReset() {
            float angle = rotor.Angle;
            if (angle < 0) {
                angle = Convert.ToSingle(Math.PI) * 2.0f + angle;
            }

            return angle < 0.005 || Convert.ToSingle(2 * Math.PI) - angle < 0.005;
        }

        private void HandleIGCMessages() {
            while (listener.HasPendingMessage) {
                ProcessMessage(listener.AcceptMessage());
            }

            while (unicastListener.HasPendingMessage) {
                ProcessMessage(unicastListener.AcceptMessage(), true);
            }
        }

        private void ProcessMessage(MyIGCMessage message, bool unicast=false) {
            string arg = message.Data as string;

            if (arg == "SetRotorRPM") {
                if (!unicast) {
                    message = listener.AcceptMessage();
                } else {
                    message = unicastListener.AcceptMessage();
                }
                rotorRPM = (double) message.Data;
                if (rotor.TargetVelocityRPM > 0) {
                    rotor.TargetVelocityRPM = Convert.ToSingle(rotorRPM);
                }
                return;
            } else if (arg == "GetPosition") {
                IGC.SendUnicastMessage(message.Source, "Base", rotor.GetPosition());
                return;
            } else if (arg == "SetBroadcastID") {
                broadcastID = long.Parse(message.Tag);
            } else {
                HandleArgs(arg);
            }
        }
    }
}
