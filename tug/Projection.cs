using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRageMath;

namespace IngameScript {
    internal class Projection {
        const double REFRESH_SECONDS = 3;
        public string ProjectionHash { get; set; }
        public IMyProjector Projector { get; private set; }

        IMyProjector projector;
        IMyProjector smallprojector;

        DateTime lastDisabledTime = DateTime.UtcNow;
        DateTime lastRefreshedTime = DateTime.UtcNow.AddSeconds(-REFRESH_SECONDS);

        int prevTotalBlocks = 0;
        bool projectionChanged = false;

        bool buttonPressed = false;
        DateTime buttonLastPressed = DateTime.UtcNow;
        DateTime lastUpdate = DateTime.UtcNow;

        public Projection(IMyProjector projector, IMyProjector smallProjector) {
            this.Projector = projector;
            this.projector = projector;
            this.smallprojector = smallProjector;
        }

        //Projection mode allows the user to move and rotate a projection using keyboard controls
        public void ProjectionMode(IMyShipController controller) {
            Vector3 move = controller.MoveIndicator; //left/right, forward/reverse, up/down
            float roll = controller.RollIndicator;
            Vector2 rot = controller.RotationIndicator; //pitch, yaw

            //Pitch and yaw are -9 / 9 when using arrow key presses, but higher/lower when using the mouse
            //Best effort to filter out mouse movement so user can pan the camera around without accidentally rotating when they release the alt key. Not perfect but helps
            if (Math.Abs(9 - Math.Abs(rot.X)) > 0.1) {
                rot.X = 0;
            }

            if (Math.Abs(9 - Math.Abs(rot.Y)) > 0.1) {
                rot.Y = 0;
            }

            if (move == Vector3.Zero && roll == 0 && rot == Vector2.Zero) {
                buttonPressed = false;
                return;
            }

            bool buttonHeld = buttonPressed;
            buttonPressed = true;
            if (buttonPressed && !buttonHeld) {
                buttonLastPressed = DateTime.UtcNow;
            }

            //Wait a half second after a button is held to process repeated moves, then process those every 1/10 a second
            if (buttonHeld && (DateTime.UtcNow - buttonLastPressed).TotalSeconds < 0.5) {
                return;
            }

            if (buttonHeld && (DateTime.UtcNow - lastUpdate).TotalSeconds < 0.1) {
                return;
            }
            lastUpdate = DateTime.UtcNow;

            var pOffset = Projector.ProjectionOffset;
            var pRot = Projector.ProjectionRotation;
            //All indictor values are -1 or 1 except for yaw and pitch, which can be larger numbers
            Projector.ProjectionOffset = new Vector3I(pOffset.X + move.X, pOffset.Y + move.Y, pOffset.Z + move.Z);
            Projector.ProjectionRotation = new Vector3I(pRot.X + Math.Sign(rot.X), pRot.Y + Math.Sign(rot.Y), pRot.Z + roll);
            Projector.UpdateOffsetAndRotation();
        }

        //Returns: true if the projection changed and the hash was updated
        public bool Refresh() {
            if (!CanRefresh()) return false;
            if (prevTotalBlocks == 0) { prevTotalBlocks = Projector.TotalBlocks; }

            var newHash = "";
            Func<bool> updateProjectorHash = new Func<bool>(() => {
                //Projector.RemainingBlocks will not update immediately after the projection is moved. Wait for it to show no welded blocks
                if (Projector.ProjectionOffset == new Vector3I(50, 50, 50) && Projector.RemainingBlocks != Projector.TotalBlocks) {
                    return false;
                }
                newHash = GetProjectorBlocksHash();
                return newHash != ProjectionHash;
            });

            //Optimization - total block change can signal a new projection without needing to perform an expensive block check
            projectionChanged = projectionChanged || prevTotalBlocks != Projector.TotalBlocks || updateProjectorHash();
            if (!projectionChanged) {
                return false;
            }

            //Projection must not overlap any existing blocks for proper hash computation. Moving it to 50, 50, 50 so it doesnt touch anything
            if (Projector.ProjectionOffset != new Vector3I(50, 50, 50)) {
                Update(new Vector3I(50, 50, 50), new Vector3I(0, 0, 0));
                return false;
            //Projector.RemainingBlocks will not update immediately after the projection is moved. Wait for it to show no welded blocks
            } else if (Projector.RemainingBlocks != Projector.TotalBlocks) {
                return false;
            }

            prevTotalBlocks = Projector.TotalBlocks;
            projectionChanged = false;
            if (newHash == String.Empty) {
                newHash = GetProjectorBlocksHash();
            }
            ProjectionHash = newHash;
            return true;
        }

        //Enforce a small wait after disabling before re-enabling, otherwise the projector sometimes won't update properly (keeeeen)
        //Returns: true if projection enabled
        public bool Enable(bool pronting) {
            if (!Projector.Enabled) {
                if ((DateTime.UtcNow - lastDisabledTime).TotalSeconds < 1) {
                    return false;
                }
                Projector.Enabled = true;
            }

            if (Projector.RemainingBlocks == Projector.TotalBlocks && !pronting) {
                if (Projector.ShowOnlyBuildable) {
                    Projector.ShowOnlyBuildable = false;
                    Projector.Enabled = false;
                }
            } else if (!Projector.ShowOnlyBuildable && pronting) {
                Projector.ShowOnlyBuildable = true;
                Projector.Enabled = false;
            }

            return true;
        }

        public void Disable() {
            if (Projector.Enabled) {
                lastDisabledTime = DateTime.UtcNow;
                Projector.Enabled = false;
            }
        }

        public void Update(Vector3I offset, Vector3I rot) {
            if (offset == Projector.ProjectionOffset && rot == Projector.ProjectionRotation) {
                return;
            }

            Projector.ProjectionOffset = offset;
            Projector.ProjectionRotation = rot;
            Projector.UpdateOffsetAndRotation(); //Keeeeeeeen
        }

        public bool IsShipOnSprue() {
            return Projector.Enabled && Projector.TotalBlocks - Projector.RemainingBlocks > 0;
        }

        public void ToggleSize(bool useLarge) {
            Projector = useLarge ? projector : smallprojector;
        }

        public int Remaining() {
            return Projector.RemainingBlocks;
        }

        public int Total() {
            return Projector.TotalBlocks;
        }

        public float CompletionPercentage() {
            return 100 - 100 * ((float) Remaining()) / Total();
        }

        public int Buildable() {
            return Projector.BuildableBlocksCount;
        }

        //Get a unique ID for a projection by collecting a count of every block type on the grid, sorting it, and hashing the result
        //Assumes the projection is completely unobstructed, otherwise the full block count will not be available
        //This is expensive, so call infrequently
        private string GetProjectorBlocksHash() {
            lastRefreshedTime = DateTime.UtcNow;

            SortedDictionary<string, int> sortedRemainingBlocksPerType = new SortedDictionary<string, int>();
            foreach (var entry in Projector.RemainingBlocksPerType) {
                sortedRemainingBlocksPerType[entry.Key.ToString()] = entry.Value;
            }

            string info = "";
            foreach (KeyValuePair<string, int> entry in sortedRemainingBlocksPerType) {
                info += $"{entry.Key}: {entry.Value}";
            }

            return Utils.GetHash(info);
        }

        private bool CanRefresh() {
            return (DateTime.UtcNow - lastRefreshedTime).TotalSeconds >= REFRESH_SECONDS && Projector.Enabled;
        }
    }
}
