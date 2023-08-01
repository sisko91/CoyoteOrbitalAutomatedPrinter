using IngameScript.Data;
using Sandbox.ModAPI.Ingame;
using System;
using VRageMath;

namespace IngameScript {
    //Print head is a remote grid
    internal class PrintHead {
        MyGridProgram program;

        //Point a few blocks "up" from the intersection of the rotor and welder plane, where the tug should begin a print
        public Vector3D Position { get; set; }
        //Rotor on the print head - "up" is towards the tug
        public MatrixD Orientation { get; set; }

        public PistonState PState = PistonState.Retracted;
        public enum PistonState {
            Extended,
            Extending,
            Retracted,
            Retracting
        }

        bool rotorWeldersOn = false;
        double rotorRPM = 0;

        public PrintHead(MyGridProgram program) {
            this.program = program;
        }

        public void EnableRotorAndWelders() {
            if (!rotorWeldersOn) {
                SendRemoteArg("RotorWeldersOn");
                rotorWeldersOn = true;
            }
        }

        public void DisableRotorAndWelders() {
            if (rotorWeldersOn) {
                SendRemoteArg("RotorWeldersOff");
                rotorWeldersOn = false;
            }
        }

        public void Extend() {
            if (PState == PistonState.Extended || PState == PistonState.Extending) { return; }
            SendRemoteArg("Extend");
            PState = PistonState.Extending;
        }

        public void Retract() {
            if (PState == PistonState.Retracted || PState == PistonState.Retracting) { return; }

            if (PState == PistonState.Extended) {
                SendRemoteArg("Retract");
                PState = PistonState.Retracting;
            } else {
                PState = PistonState.Retracted;
            }
        }

        public void SetRotorRPM(double rpm) {
            if (rotorRPM == rpm) { return; }

            rotorRPM = rpm;
            SendRemoteArg("SetRotorRPM");
            SendRemoteArg(rpm);
        }

        public void Pack() {
            rotorWeldersOn = false;
            SendRemoteArg("Pack");
        }

        public void Unpack() {
            rotorWeldersOn = false;
            SendRemoteArg("Unpack");
        }

        public bool PistonsExtended() {
            return PState == PistonState.Extended;
        }

        public bool PistonsMoving() {
            return PState == PistonState.Extending || PState == PistonState.Retracting;
        }

        private void SendRemoteArg<T>(T arg) {
            if (Config.BROADCAST_ID == 0) {
                program.IGC.SendBroadcastMessage<T>(Config.IGC_STATION_CHANNEL, arg, TransmissionDistance.TransmissionDistanceMax);
            } else {
                program.IGC.SendUnicastMessage<T>(Config.BROADCAST_ID, "", arg);
            }
        }
    }
}