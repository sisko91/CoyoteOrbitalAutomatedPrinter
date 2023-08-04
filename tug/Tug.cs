using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;
using IngameScript.Logging;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using IngameScript.Data;

namespace IngameScript {
    internal class Tug {
        public HashSet<IMyGyro> Gyros { get; private set; }
        public HashSet<IMyThrust> Thrusters { get; private set; }
        public IMyShipController Cockpit { get; private set; }
        public IMyShipController RemoteControl { get; private set; }
        public IMyShipConnector Connector { get; private set; }
        public Vector3D Velocity { get; private set; }
        public double TotalMass { get; private set; } = 0;

        Logger logger;

        //TODO: Force unmerge when printing (to separate pcu)
        List<IMyShipMergeBlock> mergeBlocks = new List<IMyShipMergeBlock>();

        readonly Matrix thrustIdentityMatrix = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        Dictionary<string, List<IMyThrust>> thrusterDirectionMap = new Dictionary<string, List<IMyThrust>>();
        Dictionary<string, double> thrusterDirectionForceMap = new Dictionary<string, double>();

        bool gyroOverrideOn = true;
        bool thrustersOn = true;

        Vector3D prevGyroOverride = new Vector3D(0, 0, 0);

        public Tug(IMyShipController cockpit, IMyShipController remoteControl, List<IMyGyro> gyros, List<IMyThrust> thrusters, List<IMyShipMergeBlock> mergeBlocks, IMyShipConnector frontConnector, Logger logger) {
            this.Cockpit = cockpit;
            this.RemoteControl = remoteControl;
            this.Gyros = new HashSet<IMyGyro>(gyros);
            this.Thrusters = new HashSet<IMyThrust>(thrusters);
            this.mergeBlocks = mergeBlocks;
            this.Connector = frontConnector;

            var directionNames = new Dictionary<Vector3, string> {
                { thrustIdentityMatrix.Up, "up"},
                { thrustIdentityMatrix.Down, "down"},
                { thrustIdentityMatrix.Left, "left"},
                { thrustIdentityMatrix.Right, "right"},
                { thrustIdentityMatrix.Forward, "forward"},
                { thrustIdentityMatrix.Backward, "backward"},
            };

            foreach (IMyThrust thruster in thrusters) {
                Matrix fromGridToReference;
                Cockpit.Orientation.GetMatrix(out fromGridToReference);
                Matrix.Transpose(ref fromGridToReference, out fromGridToReference);

                Matrix fromThrusterToGrid;
                thruster.Orientation.GetMatrix(out fromThrusterToGrid);
                Vector3 accelerationDirection = Vector3.Transform(fromThrusterToGrid.Backward, fromGridToReference);

                AddThruster(directionNames[accelerationDirection], thruster);
            }

            this.logger = logger;
        }

        public void UpdateShipStats() {
            TotalMass = Cockpit.CalculateShipMass().TotalMass;
            Velocity = Vector3D.TransformNormal(Cockpit.GetShipVelocities().LinearVelocity, MatrixD.Transpose(Cockpit.WorldMatrix));
        }

        public void ToggleGyroControl(bool enable) {
            Cockpit.SetValue<bool>("ControlGyros", enable);
        }

        //Only used for projection mode. Disabling thruster control also disables thrust override from working, so the script cant move the tug either
        public void ToggleThrusterControl(bool enable) {
            Cockpit.SetValue<bool>("ControlThrusters", enable);
        }

        //Swap between cockpit and remote control as main controller to disable thruster control during a print
        //TODO: This doesn't work perfectly - player wont get control back unless they leave the ship and re-enter it (╯°□°)╯︵ ┻━┻
        public void ToggleController(bool enable) {
            //Cockpit.SetValue<bool>("MainCockpit", !enable);
            //RemoteControl.SetValue<bool>("MainCockpit", enable);
            //Cockpit.SetValue<bool>("MainCockpit", !enable);
        }

        //Restore manual player control of gyros, thrusters, and cockpit
        public void Reset(bool hard = false) {
            ResetGyros(hard);
            ResetThrusters(hard);
            ToggleController(false);
        }

        //Returns the position immediately in front of the tug sprue
        //Front connector lines up with the print axis
        public Vector3D GetPosition() {
            //Add fudge so 3x3 large grid blocks like turret dont clip welder plane - they are very finnicky
            var offset = Vector3D.TransformNormal(new Vector3D(0, 0, Config.CONNECTOR_TO_SPRUE_OFFSET * 2.5 + 0.75), GetOrientation());
            return Connector.GetPosition() - offset;
        }

        public MatrixD GetOrientation() {
            return Cockpit.WorldMatrix;
        }

        //The sprue with the printed grid is a separate grid attached by a connector
        public IMyCubeGrid GetPrintGrid() {
            IMyShipConnector otherConnector = Connector.OtherConnector;
            if (otherConnector != null) {
                return otherConnector.CubeGrid;
            }

            return null;
        }

        //Returns: true if aligned
        public bool Align(Vector3D referencePosition, MatrixD referenceMatrix, Vector3D printVector) {
            double pitch, yaw, roll = 0;
            GetRotationAnglesSimultaneous(referenceMatrix.Down, referenceMatrix.Backward, Cockpit.WorldMatrix, out yaw, out pitch, out roll);
            ApplyGyroOverride(pitch, yaw, roll, Cockpit.WorldMatrix);
            logger.Log($"Yaw: {yaw:n2}°. Pitch: {pitch:n2}°. Roll: {roll:n2}°", LogLevel.Debug);

            double alignedDist = 0.1;
            double alignedMoveDist = 0.1;

            bool facingTarget = false;

            //Once we get aligned close enough, start moving in X/Y plane to align with printer head
            if (Math.Abs(pitch) < alignedDist && Math.Abs(yaw) < alignedDist && Math.Abs(roll) < alignedDist) {
                facingTarget = true;
                logger.Log($"Position:\n X:{printVector.X:0.00} Y:{printVector.Y:0.00} Z:{-1 * (printVector.Z):0.00}");

                bool inXPos = Math.Abs(printVector.X) < alignedMoveDist;
                bool inYPos = Math.Abs(printVector.Y) < alignedMoveDist;

                if (inXPos && inYPos) {
                    ResetThrusters();
                } else {
                    logger.Log("Moving into position");
                    Move(printVector.X, Velocity.X, "X", TotalMass);
                    Move(printVector.Y, Velocity.Y, "Y", TotalMass);
                    return false;
                }
            }

            bool moving = Velocity.X > 0.1 || Velocity.Y > 0.1;
            if (!moving) { logger.Log("In position, ready to print"); }
            return !moving && facingTarget;
        }

        public bool MoveZ(double distance) {
            if (Math.Abs(distance) < 0.1) {
                ResetThrusters();
            }

            if (Math.Abs(distance) < 0.1 && Math.Abs(Velocity.Z) < 0.1) {
                return false;
            }

            logger.Log($"Moving Z. Distance: {distance}. Velocity: {Velocity.Z}");
            Move(distance, Velocity.Z, "Z", TotalMass);

            return true;
        }

        //Move along a specific axis relative to the tug (forward/back, left/right, up/down)
        //TODO: Rewrite this jank
        public void Move(double axisDistance, double axisVelocity, string axis, double totalMass) {
            thrustersOn = true;
            string posDir = "";
            string negDir = "";

            if (axis == "X") {
                posDir = "right";
                negDir = "left";
            } else if (axis == "Y") {
                posDir = "up";
                negDir = "down";
            } else if (axis == "Z") {
                posDir = "backward";
                negDir = "forward";
            } else {
                throw new Exception($"Invalid move axis {axis}");
            }

            double dist = Math.Abs(axisDistance);
            double speed = Math.Abs(axisVelocity);

            //Speed and acceleration limits as the ship gets closer to the print head. Not super elegant
            double desiredAcceleration = 100;
            double speedLimit = 100;

            if (dist < 0.5) {
                desiredAcceleration = 0.15;
                speedLimit = 0.3;
            } else if (dist < 2.5) {
                desiredAcceleration = 0.5;
                speedLimit = 1;
            } else if (dist < 2.5 * 5) {
                desiredAcceleration = 0.75;
                speedLimit = 1.5;
            } else if (dist < 2.5 * 10) {
                desiredAcceleration = 2.5;
                speedLimit = 2.5;
            } else if (dist < 2.5 * 50) {
                desiredAcceleration = 5;
                speedLimit = 10;
            } else if (dist < 2.5 * 100) {
                desiredAcceleration = 5;
                speedLimit = 15;
            }

            double timeToStop = speed / desiredAcceleration;
            double distToStop = timeToStop * speed / 2;

            if (dist <= distToStop) {
                logger.Log("Applying brakes", LogLevel.Debug);
                speedLimit = -1;
            }

            double desiredForce = totalMass * desiredAcceleration;
            if (speed > speedLimit) {
                logger.Log("Speeding", LogLevel.Debug);
                desiredForce = 0;
            }

            float posDirThrustPercent = axisDistance >= 0.1 ? Convert.ToSingle(desiredForce / thrusterDirectionForceMap[posDir]) : 0;
            float negDirThrustPercent = axisDistance < 0.1 ? Convert.ToSingle(desiredForce / thrusterDirectionForceMap[negDir]) : 0;

            logger.Log($"Desired acceleration: {desiredAcceleration} m/s^2", LogLevel.Debug);
            logger.Log($"{posDir} thrust percent: {100 * posDirThrustPercent:0.00}%", LogLevel.Debug);
            logger.Log($"{negDir} thrust percent: {100 * negDirThrustPercent:0.00}%", LogLevel.Debug);

            foreach (IMyThrust t in thrusterDirectionMap[posDir]) {
                t.ThrustOverridePercentage = posDirThrustPercent;
            }
            foreach (IMyThrust t in thrusterDirectionMap[negDir]) {
                t.ThrustOverridePercentage = negDirThrustPercent;
            }
        }

        private void AddThruster(string key, IMyThrust thruster) {
            List<IMyThrust> thrusters;
            if (!thrusterDirectionMap.TryGetValue(key, out thrusters)) {
                thrusters = new List<IMyThrust>();
            }
            thrusters.Add(thruster);
            thrusterDirectionMap[key] = thrusters;

            double force = 0;
            thrusterDirectionForceMap.TryGetValue(key, out force);
            thrusterDirectionForceMap[key] = force + thruster.MaxEffectiveThrust;
        }

        private void ResetGyros(bool hard = false) {
            if (!gyroOverrideOn && !hard) { return; }

            foreach (var g in Gyros) {
                g.Pitch = 0;
                g.Yaw = 0;
                g.Roll = 0;
                g.GyroOverride = false;
            }

            gyroOverrideOn = false;
        }

        public void ResetThrusters(bool hard = false) {
            if (!thrustersOn && !hard) { return; }

            foreach (var t in Thrusters) {
                t.ThrustOverride = 0;
            }

            thrustersOn = false;
        }

        private void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, MatrixD worldMatrix) {
            var newGyroOverride = new Vector3D(pitchSpeed, yawSpeed, rollSpeed);
            //If stationary gyro override already applied, avoid extra work
            if (prevGyroOverride == newGyroOverride && prevGyroOverride == new Vector3D(0, 0, 0)) {
                return;
            }
            prevGyroOverride = newGyroOverride;

            var rotationVec = new Vector3D(-pitchSpeed, yawSpeed, rollSpeed);
            var relativeRotationVec = Vector3D.TransformNormal(rotationVec, worldMatrix);

            foreach (var g in Gyros) {
                var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(g.WorldMatrix));

                g.Pitch = (float)transformedRotationVec.X;
                g.Yaw = (float)transformedRotationVec.Y;
                g.Roll = (float)transformedRotationVec.Z;
                g.GyroOverride = true;
            }

            gyroOverrideOn = true;
        }

        private void GetRotationAnglesSimultaneous(Vector3D desiredForwardVector, Vector3D desiredUpVector, MatrixD worldMatrix, out double yaw, out double pitch, out double roll) {
            desiredForwardVector = SafeNormalize(desiredForwardVector);

            MatrixD transposedWm;
            MatrixD.Transpose(ref worldMatrix, out transposedWm);
            Vector3D.Rotate(ref desiredForwardVector, ref transposedWm, out desiredForwardVector);
            Vector3D.Rotate(ref desiredUpVector, ref transposedWm, out desiredUpVector);

            Vector3D leftVector = Vector3D.Cross(desiredUpVector, desiredForwardVector);
            Vector3D axis;
            double angle;
            if (Vector3D.IsZero(desiredUpVector) || Vector3D.IsZero(leftVector)) {
                axis = new Vector3D(desiredForwardVector.Y, -desiredForwardVector.X, 0);
                angle = Math.Acos(MathHelper.Clamp(-desiredForwardVector.Z, -1.0, 1.0));
            } else {
                leftVector = SafeNormalize(leftVector);
                Vector3D upVector = Vector3D.Cross(desiredForwardVector, leftVector);

                MatrixD targetMatrix = MatrixD.Zero;
                targetMatrix.Forward = desiredForwardVector;
                targetMatrix.Left = leftVector;
                targetMatrix.Up = upVector;

                axis = new Vector3D(targetMatrix.M23 - targetMatrix.M32,
                                    targetMatrix.M31 - targetMatrix.M13,
                                    targetMatrix.M12 - targetMatrix.M21);

                double trace = targetMatrix.M11 + targetMatrix.M22 + targetMatrix.M33;
                angle = Math.Acos(MathHelper.Clamp((trace - 1) * 0.5, -1, 1));
            }

            if (Vector3D.IsZero(axis)) {
                angle = desiredForwardVector.Z < 0 ? 0 : Math.PI;
                yaw = angle;
                pitch = 0;
                roll = 0;
                return;
            }

            axis = SafeNormalize(axis);
            yaw = -axis.Y * angle;
            pitch = axis.X * angle;
            roll = -axis.Z * angle;
        }

        private static Vector3D SafeNormalize(Vector3D a) {
            if (Vector3D.IsZero(a))
                return Vector3D.Zero;

            if (Vector3D.IsUnit(ref a))
                return a;

            return Vector3D.Normalize(a);
        }
    }
}
