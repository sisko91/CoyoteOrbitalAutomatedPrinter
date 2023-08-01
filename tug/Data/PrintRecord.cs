using IngameScript.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript {
    internal class PrintRecord {
        private static MyIni INI = new MyIni();

        public string Name;
        public List<Layer> Layers;

        //Unique ID for this ship based on what blocks the ship is made from, assuming no two ships will have the same blocks
        public string ProjectionHash = "";
        public int ProjectionOffsetX = 0;
        public int ProjectionOffsetY = 0;
        public int ProjectionOffsetZ = 0;
        public int ProjectionRotX = 0;
        public int ProjectionRotY = 0;
        public int ProjectionRotZ = 0;
        public bool BigPrint; //Only used for recording. TODO: Move to general print config? Yes, and add button to toggle it

        //Speed up rotor when creative print testing
        public double RotorRPM { get { return !Config.CREATIVE ? _rotorRPM : _rotorRPM * 5; } set { _rotorRPM = value; } }
        private double _rotorRPM;
        public double CompletionPercentage { get { return _completionPercentage == Double.NaN ? 0 : _completionPercentage; } set { _completionPercentage = value; } }
        private double _completionPercentage = 0;

        public PrintRecord(string name, string projectionHash, double rotorRPM, List<Layer> layers, bool bigPrint = false) {

            this.Name = name;
            this.ProjectionHash = projectionHash;
            this.RotorRPM = rotorRPM;
            this.Layers = layers;
            this.BigPrint = bigPrint;
        }

        //Constructor for storage from custom data
        public PrintRecord(string serialized) {
            MyIniParseResult result;
            if (String.IsNullOrEmpty(serialized)) { throw new Exception("Empty print record config"); }
            if (!INI.TryParse(serialized, out result)) { throw new Exception(result.ToString()); }

            Name = INI.Get("PrintRecord", "Name").ToString();

            ProjectionOffsetX = INI.Get("PrintRecord", "ProjectionOffsetX").ToInt32();
            ProjectionOffsetY = INI.Get("PrintRecord", "ProjectionOffsetY").ToInt32();
            ProjectionOffsetZ = INI.Get("PrintRecord", "ProjectionOffsetZ").ToInt32();
            ProjectionRotX = INI.Get("PrintRecord", "ProjectionRotX").ToInt32();
            ProjectionRotY = INI.Get("PrintRecord", "ProjectionRotY").ToInt32();
            ProjectionRotZ = INI.Get("PrintRecord", "ProjectionRotZ").ToInt32();

            ProjectionHash = INI.Get("PrintRecord", "ProjectionHash").ToString();

            if (INI.ContainsKey("PrintRecord", "PrintCompletion")) {
                CompletionPercentage = INI.Get("PrintRecord", "PrintCompletion").ToDouble();
            } else {
                CompletionPercentage = 0;
            }

            if (INI.ContainsKey("PrintRecord", "RotorRPM")) {
                RotorRPM = INI.Get("PrintRecord", "RotorRPM").ToDouble();
            } else {
                RotorRPM = 1;
            }

            Layers = new List<Layer>();
            if (INI.ContainsKey("PrintRecord", "Layers")) {
                string layersStr = INI.Get("PrintRecord", "Layers").ToString();
                if (layersStr == String.Empty) {
                    return;
                }
                foreach (var layerStr in layersStr.Split(':')) {
                    string[] vals = layerStr.Split(',');
                    bool extended = vals.Length > 1 ? bool.Parse(vals[1]) : false;
                    Layers.Add(new Layer(int.Parse(vals[0]), extended));
                }
            }
        }
        public string Serialize() {
            string layersStr = "";
            foreach (Layer layer in Layers) {
                string extendedStr = layer.Extended ? $",{layer.Extended}" : "";
                layersStr += $"{layer.RemainingBlocks}{extendedStr}:";
            }
            layersStr = layersStr.Trim(new char[] { ':' });

            return new StringBuilder()
                .AppendLine("[PrintRecord]")
                .AppendLine($"Name={Name}")
                .AppendLine($"ProjectionOffsetX={ProjectionOffsetX}")
                .AppendLine($"ProjectionOffsetY={ProjectionOffsetY}")
                .AppendLine($"ProjectionOffsetZ={ProjectionOffsetZ}")
                .AppendLine($"ProjectionRotX={ProjectionRotX}")
                .AppendLine($"ProjectionRotY={ProjectionRotY}")
                .AppendLine($"ProjectionRotZ={ProjectionRotZ}")
                .AppendLine($"ProjectionHash={ProjectionHash}")
                .AppendLine($"Layers={layersStr}")
                .AppendLine($"PrintCompletion={(CompletionPercentage != Double.NaN ? CompletionPercentage : 0):0.00}")
                .AppendLine($"RotorRPM={_rotorRPM}")
                .ToString();
        }

        public void UpdateProjection(Projection projection) {
            Vector3I offset = projection.Projector.ProjectionOffset;
            Vector3I rot = projection.Projector.ProjectionRotation;

            if (offset.X == 50 & offset.Y == 50 && offset.Z == 50) {
                return;
            }

            this.ProjectionOffsetX = offset.X;
            this.ProjectionOffsetY = offset.Y;
            this.ProjectionOffsetZ = offset.Z;
            this.ProjectionRotX = rot.X;
            this.ProjectionRotY = rot.Y;
            this.ProjectionRotZ = rot.Z;
        }
    }
}
