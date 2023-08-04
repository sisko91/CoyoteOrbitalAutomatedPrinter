using IngameScript.Data;
using IngameScript.Logging;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript {
    internal class Printer {
        public const double LAST_BLOCK_WAIT_SECONDS = 7.5;

        public bool Enabled { get; set; } = false;

        public bool Pronting { get; set; } = false;

        public bool ProjectionMode { get; private set; } = false;

        public double TimeSinceLastRun { get; set; } = 0;

        public bool Aligned { get; private set; } = false;

        public bool ShouldAdvance { get; private set; } = false;

        public bool CheckingProjectionUpdates { get; private set; } = false;

        //List of placed blocks that are not fully welded yet. Loaded when print gets stuck due to unwelded blocks (e.g. out of materials)
        public List<string> UnfinishedBlocks { get; private set; } = new List<string>();

        //During playback it's possible to miss blocks we got in a previous print. Keep track so we can resume the print properly
        public int MissedBlocks { get; private set; } = 0;

        //The ship to print and all the data needed to print it
        public PrintRecord LoadedRecord { get; set; }
        public Dictionary<string, PrintRecord> Records { get; set; } = new Dictionary<string, PrintRecord>();

        //Seconds since mass last changed
        public double NoMassChangeTimer { get; private set; } = 0;

        //Seconds the current layer has been printing
        public double PrintLayerTimer { get; private set; } = 0;

        Tug tug;
        Projection projection;
        PrintHead printHead;

        MyGridProgram program;
        DebugAPI debug;
        Logger logger;

        //True if a recorded print is available. Prints are automatically recorded if not available
        bool playback = false;

        //Used to cache when unfinished (partially welded) blocks are found, since polling them is expensive
        bool hasUnfinishedBlocks = false;
        DateTime lastUnfinishedBlockTime = DateTime.UtcNow;

        //When the most recent block was placed
        //The printer waits for a fixed time after no new blocks have been placed, to give armor blocks a chance to fully weld - since their weld status cannot be tracked
        double lastBlockPlacedTime = 0;
        double prevRemainingBlocks = 0;

        //Seconds moving into position for this layer, used for debugging/optimizing thruster code
        double moveLayerTimer = 0;

        //Keep track of time since last mass change
        double prevMass = 0;

        //Large grid or small grid block length, depending on which is being printed
        double layerSize = 2.5;

        //Tug position along the print vector
        double tugZPosition = 0;

        //Vector starting at the print head and extending to the tug
        Vector3D printVector;

        //Each subsequent value specifies the total unwelded blocks expected for each layer and if pistons should be extended
        List<Layer> layers = new List<Layer>();

        //How many layers since the mass has last changed. Used during recording to help detect when a ship is finished printing
        int noMassChangeLayerCount = 0;
        double prevLayerMass = 0;

        bool moving = false;
        bool done = false;

        public Printer(Tug tug, Projection projection, PrintHead printHead, MyGridProgram program) {
            this.tug = tug;
            this.projection = projection;
            this.printHead = printHead;

            this.program = program;
            this.logger = Logger.INSTANCE;
            this.debug = DebugAPI.INSTANCE;

            UnfinishedBlocks = new List<string>();
        }

        public void Run(bool update100) {
            if (!PrinterPositionKnown()) {
                logger.Log("Waiting for printer head to send position");
                return;
            }

            if (ProjectionMode) {
                projection.ProjectionMode(tug.Cockpit);
                logger.Log("Projection mode");
                return;
            }

            logger.Log($"Playback?: {playback}");
            logger.Log($"Record loaded?: {LoadedRecord != null}. PHash: {projection.ProjectionHash}");
            logger.Log($"Layers set for print: {(layers == null ? 0 : layers.Count)}");
            logger.Log($"Pronting?: {Pronting}");
            if (MissedBlocks > 0) { logger.Log($"Missed blocks count: {MissedBlocks}"); }

            if (Config.DEBUG_ENABLED) {
                debug.DrawPoint(printHead.Position, Color.Blue, 1);
                debug.DrawPoint(tug.GetPosition(), Color.Blue, 1);
            }

            tug.UpdateShipStats();

            //Enable projection - if it was just disabled, it wont enable immediately - so return and try again
            if (!projection.Enable(Pronting)) {
                return;
            }

            //Tug will align orientation with print axis before other movement is allowed
            Aligned = tug.Align(printHead.Position, printHead.Orientation, printVector);
            if (!Aligned) {
                return;
            } else if (Config.DEBUG_ENABLED) {
                //Draw movement vector for tug to align with the print head plane (x, y movement)
                var printVectorWorld = Vector3D.TransformNormal(new Vector3D(printVector.X, printVector.Y, 0), tug.GetOrientation());
                var tugAlignTargetPosition = tug.GetPosition() + printVectorWorld;
                debug.DrawLine(tug.GetPosition(), tugAlignTargetPosition, Color.Green, thickness: 0.25f);
            }

            var rotorRPM = LoadedRecord != null ? LoadedRecord.RotorRPM : 1;
            printHead.SetRotorRPM(rotorRPM);

            if (Pronting) {
                CheckingProjectionUpdates = false;

                //If the print is done, wait until the tug moves to the completed position, then reset the print
                while (done) {
                    if (!Move()) {
                        //If done with a playback, reload the record so we can play back again if we start another print
                        if (playback) {
                            LoadRecord(LoadedRecord);
                        }
                        Reset();
                    }
                    return;
                }

                if (update100) {
                    DisableAnnoyingBlocks();
                    RenameGrids();
                }

                //Update tug position if printing
                int layerNum;
                if (playback) {
                    layerNum = LoadedRecord.Layers.Where(l => !l.Extended).ToList().Count - layers.Where(l => !l.Extended).ToList().Count;
                } else {
                    layerNum = layers.Where(l => !l.Extended).ToList().Count;
                }
                tugZPosition = GetPositionForLayer(layerNum);

                //Disable rotors and welders - no printing when anything is moving, mitigates clang fuckery
                bool movement = Move() || printHead.PistonsMoving();
                logger.Log($"Moving for {moveLayerTimer:0.00} seconds on this layer", LogLevel.Debug);
                if (movement) {
                    logger.Log($"Moving... (Pronting={Pronting})");
                    printHead.DisableRotorAndWelders();
                    moveLayerTimer += TimeSinceLastRun;
                    return;
                } else {
                    logger.Log("Stopped, enabling rotor/welders");
                    printHead.EnableRotorAndWelders();
                }

                if (prevMass == tug.TotalMass) {
                    NoMassChangeTimer += TimeSinceLastRun;
                } else {
                    NoMassChangeTimer = 0;
                }
                prevMass = tug.TotalMass;

                Print();
            // If not printing, periodically check the projector to see if something new is loaded
            } else if (!projection.IsShipOnSprue() || LoadedRecord == null) {
                CheckingProjectionUpdates = true;
                bool newShip = projection.Refresh();
                if (newShip) {
                    var record = new PrintRecord("ship-" + Utils.RandomString(12), projection.ProjectionHash, 1, new List<Layer>());

                    //Check if this ship is already saved. If it is, configure saved values
                    foreach (PrintRecord thisRecord in Records.Values) {
                        if (thisRecord.ProjectionHash == projection.ProjectionHash) {
                            record = thisRecord;
                            break;
                        }
                    }

                    LoadRecord(record);
                }
            }
            
            if (!Pronting && LoadedRecord != null) { //Periodically save/load config changes
                if (projection.Projector.ProjectionOffset != new Vector3I(50, 50, 50)) { //TODO: needed?
                    Save();
                }
            }
        }

        public void Advance() {
            moveLayerTimer = 0;
            NoMassChangeTimer = 0;
            ShouldAdvance = false;
            bool shouldExtend = false;

            if (!playback) {
                bool noNewBlocksThisLayer = layers.Count > 0 && layers.Last().RemainingBlocks == projection.Remaining();
                if (!noNewBlocksThisLayer || !printHead.PistonsExtended()) {
                    layers.Add(new Layer(projection.Remaining(), printHead.PistonsExtended()));
                }
                shouldExtend = !printHead.PistonsExtended() && LoadedRecord.BigPrint;
            } else {
                if (layers.Count > 0) {
                    MissedBlocks = projection.Remaining() - layers[0].RemainingBlocks;
                }

                while (layers.Count > 0 && layers[0].RemainingBlocks >= projection.Remaining() - MissedBlocks) {
                    layers.RemoveAt(0);
                }
                shouldExtend = layers.Count == 0 ? false : layers[0].Extended;
            }

            if (shouldExtend && !printHead.PistonsExtended()) {
                printHead.Extend();
            } else {
                if (prevLayerMass == tug.TotalMass) {
                    noMassChangeLayerCount += 1;
                }
                prevLayerMass = tug.TotalMass;
                printHead.Retract();
            }

            PrintLayerTimer = 0;
            lastBlockPlacedTime = 0;
            Save();
        }

        public void TogglePrintSize() {
            if (layerSize == 2.5) {
                layerSize = 1;
            } else {
                layerSize = 2.5;
            }

            projection.Disable();
            projection.ToggleSize(layerSize == 2.5);
        }

        public void UpdatePrinterPosition(Vector3D position, MatrixD orientation) {
            printHead.Position = position;
            printHead.Orientation = orientation;
        }

        public void Reset() {
            Save();

            Pronting = false;
            ProjectionMode = false;
            CheckingProjectionUpdates = false;
            ShouldAdvance = false;
            PrintLayerTimer = 0;
            NoMassChangeTimer = 0;
            noMassChangeLayerCount = 0;
            tugZPosition = 0;
            moving = false;
            moveLayerTimer = 0;
            lastBlockPlacedTime = 0;

            tug.ToggleThrusterControl(true);

            if (Enabled) {
                MissedBlocks = 0;
                done = false;
                printVector = Vector3D.Zero;
                projection.Enable(false);
                //Resets print head to default position and requests a position update
                printHead.Unpack();
            } else {
                LoadedRecord = null;
                tug.Reset();

                projection.Update(new Vector3I(50, 50, 50), new Vector3I(0, 0, 0));
                projection.Disable();
                projection.ProjectionHash = "";
                printHead.Pack();
            }
        }
        public void Save() {
            if (LoadedRecord == null) return;

            PrintRecord record;
            try {
                //Pick up user edits
                record = new PrintRecord(program.Me.CustomData);
            } catch (Exception e) {
                record = LoadedRecord;
            }

            if (Enabled) {
                record.UpdateProjection(projection);
            }

            int remaining = layers.Count > 0 ? layers.Last().RemainingBlocks : projection.Total();
            int savedRemaining = LoadedRecord.Layers.Count > 0 ? LoadedRecord.Layers.Last().RemainingBlocks : projection.Total();
            //Only save new layers if we're recording and have something new to save
            if (remaining < savedRemaining && !playback) {
                record.Layers = new List<Layer>(layers);
                record.CompletionPercentage = projection.CompletionPercentage();
            }

            LoadRecord(record);
            if (!Enabled) {
                program.Me.CustomData = "";
            }

            //Delete any existing records with the same projection as this record
            Records = Records.Where(kv => kv.Value.ProjectionHash != LoadedRecord.ProjectionHash)
                                   .ToDictionary(kv => kv.Key, kv => kv.Value);

            //Merge with saved records
            var existingRecords = GetSavedRecords();
            existingRecords.ToList().Where(r => !Records.ContainsKey(r.Key)).ToList().ForEach(r => Records[r.Key] = r.Value);

            //Don't save records with default names unless we're printing
            if (!LoadedRecord.Name.StartsWith("ship") || Pronting) {
                Records[LoadedRecord.Name] = LoadedRecord;
            }

            //Using custom data instead of Storage for manual backup and editing
            tug.Cockpit.CustomData = string.Join("---------------------------\n", Records.Values.OrderBy(x => x.Name).Select(r => r.Serialize()).ToList());
        }

        public Dictionary<string, PrintRecord> GetSavedRecords() {
            var storage = tug.Cockpit.CustomData;

            var records = new Dictionary<string, PrintRecord>();
            if (records.Count == 0 && !string.IsNullOrEmpty(storage)) {
                List<string> recordsstr = storage.Split(new string[] { "---------------------------" }, StringSplitOptions.None).ToList();
                foreach (string rstr in recordsstr) {
                    if (String.IsNullOrEmpty(rstr)) { continue; }

                    PrintRecord r = new PrintRecord(rstr.Trim());
                    records[r.Name] = r;
                }
            }

            return records;
        }

        public void ToggleProjectionMode() {
            ProjectionMode = !ProjectionMode;
            tug.ToggleThrusterControl(!ProjectionMode);
        }

        public void ReloadRecord() {
            LoadRecord(LoadedRecord);
        }

        //Display helpers (TODO: refactor, make things public that should be public)
        public bool IsLargeGridPrint() {
            return layerSize == 2.5;
        }

        public bool IsRecording() {
            return !playback;
        }

        public int CompletedLayers() {
            return LoadedRecord.Layers.Count - layers.Count;
        }

        public int CurrentLayer() {
            if (!playback) {
                return layers.Count + 1;
            }

            return LoadedRecord.Layers.Count - layers.Count + 1;
        }

        public int CurrentLayerTotal() {
            var prevLayerIndex = CurrentLayer() - 2;
            var prevLayerRemaining = prevLayerIndex >= 0 ? LoadedRecord.Layers[prevLayerIndex].RemainingBlocks + MissedBlocks: projection.Total();

            return layers.Count > 0 ? prevLayerRemaining - layers[0].RemainingBlocks : prevLayerIndex;
        }

        public int CurrentLayerRemaining() {
            return layers.Count > 0 ? projection.Remaining() - layers[0].RemainingBlocks + MissedBlocks: projection.Remaining();
        }

        public float CurrentLayerCompletionPercentage() {
            return 100 - 100 * ((float) CurrentLayerRemaining()) / CurrentLayerTotal();
        }

        public double LayerTimeout() {
            //2 full revolutions maximum
            return 2 * 60 / LoadedRecord.RotorRPM;
        }

        public double MassTimeout() {
            //Half a revolution
            return Math.Max(10, 30 / LoadedRecord.RotorRPM);
        }

        public bool Moving() {
            return moving && Pronting;
        }

        public bool PistonsMoving() {
            return printHead.PistonsMoving();
        }

        public double ZPosition() {
            return -tugZPosition;
        }

        public double TimeSinceLastBlockPlaced() {
            return PrintLayerTimer - lastBlockPlacedTime;
        }

        public bool PrintFinished() {
            var finished = layers.Count == 0 && playback;
            return finished;
        }

        private bool PrinterPositionKnown() {
            if (printHead.Position != Vector3D.Zero) {
                printVector = Vector3D.TransformNormal(printHead.Position - tug.GetPosition(), MatrixD.Transpose(tug.GetOrientation()));
            }

            return printVector != Vector3D.Zero;
        }

        private void LoadRecord(PrintRecord record) {
            LoadedRecord = record;
            projection.ProjectionHash = LoadedRecord.ProjectionHash;

            if (!Pronting) {
                layers = new List<Layer>(LoadedRecord.Layers);

                var offset = new Vector3I(LoadedRecord.ProjectionOffsetX, LoadedRecord.ProjectionOffsetY, LoadedRecord.ProjectionOffsetZ);
                var rot = new Vector3I(LoadedRecord.ProjectionRotX, LoadedRecord.ProjectionRotY, LoadedRecord.ProjectionRotZ);
                projection.Update(offset, rot);

                SetPlayback();
                //If playing back, discard layers until we're at the current layers being printed
                while (playback && layers.Count > 0 && layers[0].RemainingBlocks >= projection.Remaining()) {
                    layers.RemoveAt(0);
                }

                //If recording, discard any saved layers that no longer exist, in case the ship on the sprue was modified
                while (!playback && layers.Count > 0 && layers.Last().RemainingBlocks < projection.Remaining()) {
                    layers.RemoveAt(layers.Count - 1);
                }
            }
            program.Me.CustomData = LoadedRecord.Serialize();
        }

        private void SetPlayback() {
            if (layers.Count == 0) {
                playback = false;
                return;
            }

            // Don't playback if we can record more. projection.Buildable() isn't perfectly reliable because
            // there can be buildable blocks in earlier layers that only became buildable after they were out of welder range.
            bool canRecordMore = projection.Remaining() <= layers.Last().RemainingBlocks && projection.Buildable() > 0;

            //Only playback good recordings (>= 90% completion)
            playback = !canRecordMore && LoadedRecord.CompletionPercentage >= 90 || LoadedRecord.CompletionPercentage >= 95; //hack
        }

        private bool Move() {
            //Failsafe to mitigate total printer death
            if (projection.IsShipOnSprue() && layers.Count != 0 && projection.Remaining() < layers[0].RemainingBlocks && tugZPosition >= 0) {
                logger.Log("Ship on sprue, not moving");
                return false;
            }

            moving = tug.MoveZ(printVector.Z - tugZPosition);

            if (Config.DEBUG_ENABLED && moving) {
                //Draw tug movement vector towards/away from the print head
                var moveTo = Vector3D.TransformNormal(new Vector3D(0, tugZPosition, 0), printHead.Orientation);
                var newPosition = printHead.Position - moveTo;
                debug.DrawLine(tug.GetPosition(), newPosition, Color.Green, thickness: 0.25f);
                debug.DrawPoint(newPosition, Color.Green, 1);

                //Draw plane where welders will be after the tug moves. TODO: rewrite w/o quaternion, overly complicated
                Vector3 planeSize = new Vector3(20, 0, 20);
                Matrix connectorMatrix = tug.Connector.WorldMatrix;
                Quaternion planeRot = Quaternion.CreateFromForwardUp(-connectorMatrix.Right, -connectorMatrix.Forward);
                Vector3 planeWorldPosition = Vector3.Transform(new Vector3(0, 0, tugZPosition - Config.CONNECTOR_TO_SPRUE_OFFSET * 2.5 - 2.5), connectorMatrix);
                debug.DrawOBB(new MyOrientedBoundingBoxD(planeWorldPosition, planeSize, planeRot), Color.Green, DebugAPI.Style.SolidAndWireframe);
            }

            return moving;
        }

        private void Print() {
            if (!Pronting) { return; }
            PrintLayerTimer += TimeSinceLastRun;

            done = noMassChangeLayerCount >= 3 || projection.Remaining() == 0 || playback && layers.Count == 0;
            if (done) {
                if (HasUnfinishedBlock()) {
                    return;
                }
                Advance();
                printHead.Unpack();

                //Remove duplicates at the end of the record. There will usually be duplicates at the end when recording
                //because the printer waits a few layers of no mass changes before considering the print complete
                while (layers.Count > 1 && layers.Last().RemainingBlocks == layers[layers.Count - 2].RemainingBlocks) {
                    layers.RemoveAt(layers.Count - 1);
                }

                //Back 'er up 10 large-grid blocks
                tugZPosition = tugZPosition - 10 * 2.5;
                return;
            }

            logger.Log($"Printing. Current layer time: {PrintLayerTimer} s");

            ShouldAdvance = PrintLayerTimer > LayerTimeout() || NoMassChangeTimer > MassTimeout();
            if (prevRemainingBlocks != projection.Remaining()) {
                lastBlockPlacedTime = PrintLayerTimer;
            }
            prevRemainingBlocks = projection.Remaining();

            //Wait after last block is placed for armor welding to finish - cant be tracked via script so hardcoded wait
            bool shouldWait = PrintLayerTimer - lastBlockPlacedTime < (Config.CREATIVE ? 0 : LAST_BLOCK_WAIT_SECONDS);

            if (playback && layers.Count > 0) {
                logger.Log($"Remaining blocks at completion of this layer: {layers[0].RemainingBlocks + MissedBlocks} (at {projection.Remaining()})", LogLevel.Debug);
                ShouldAdvance = ShouldAdvance || projection.Remaining() <= layers[0].RemainingBlocks + MissedBlocks;
            }

            logger.Log($"Should wait? {shouldWait}", LogLevel.Debug);
            if (ShouldAdvance && !shouldWait) {
                //Expensive, on a fixed refresh delay
                if (HasUnfinishedBlock()) {
                    logger.Log($"Unfinished blocks: {String.Join(",", UnfinishedBlocks)}", LogLevel.Debug);
                    return;
                }

                Advance();
            }
        }

        private double GetPositionForLayer(int layer) {
            return -layer * layerSize;
        }

        private bool HasUnfinishedBlock() {
            //Cache positive hits for 5 seconds - for performance
            if (hasUnfinishedBlocks && (DateTime.UtcNow - lastUnfinishedBlockTime).TotalSeconds < 5) {
                return true;
            }

            UnfinishedBlocks.Clear();
            List<IMyTerminalBlock> functionalBlocks = new List<IMyTerminalBlock>();
            program.GridTerminalSystem.GetBlocksOfType(functionalBlocks);
            foreach (IMyTerminalBlock block in functionalBlocks) {
                IMySlimBlock slimBlock = block.CubeGrid.GetCubeBlock(block.Position);
                if (slimBlock.BuildLevelRatio < 1) {
                    UnfinishedBlocks.Add(block.CustomName);
                }
            }

            hasUnfinishedBlocks = UnfinishedBlocks.Count > 0;
            if (hasUnfinishedBlocks) { lastUnfinishedBlockTime = DateTime.UtcNow; }
            return hasUnfinishedBlocks;
        }

        /* Shuts down non-tug  blocks on printed ship that could interfere with the print
         * - Other program blocks
         * - Grav gens / mass blocks
         * - Beacons / antennas
         * - Timers
         * - Gyro override
         * - Thruster override
         * - Jump drives
         * - Shields
         * - TODO: Weapons (vanilla and weaponcore integration)
         * - TODO: Projectors
         */
        private void DisableAnnoyingBlocks() {
            List<IMyProgrammableBlock> pbs = new List<IMyProgrammableBlock>();
            program.GridTerminalSystem.GetBlocksOfType(pbs, p => p != program.Me);

            foreach (IMyProgrammableBlock pb in pbs) {
                pb.ApplyAction("OnOff_Off");
            }

            List<IMyGravityGeneratorBase> gravs = new List<IMyGravityGeneratorBase>();
            program.GridTerminalSystem.GetBlocksOfType(gravs);
            foreach (IMyGravityGeneratorBase g in gravs) {
                g.Enabled = false;
            }

            List<IMyArtificialMassBlock> massBlocks = new List<IMyArtificialMassBlock>();
            program.GridTerminalSystem.GetBlocksOfType(massBlocks);
            foreach (IMyArtificialMassBlock mb in massBlocks) {
                mb.Enabled = false;
            }

            List<IMyBeacon> beacons = new List<IMyBeacon>();
            program.GridTerminalSystem.GetBlocksOfType(beacons);
            foreach (IMyBeacon b in beacons) {
                b.Enabled = false;
            }

            List<IMyRadioAntenna> antennas = new List<IMyRadioAntenna>();
            program.GridTerminalSystem.GetBlocksOfType(antennas, r => r.CustomName != "Tug Antenna");
            foreach (IMyRadioAntenna r in antennas) {
                r.Enabled = false;
            }

            List<IMyTimerBlock> timers = new List<IMyTimerBlock>();
            program.GridTerminalSystem.GetBlocksOfType(timers);
            foreach (IMyTimerBlock t in timers) {
                t.Enabled = false;
            }

            List<IMyGyro> gyros = new List<IMyGyro>();
            program.GridTerminalSystem.GetBlocksOfType(gyros, g => !tug.Gyros.Contains(g));
            foreach (IMyGyro g in gyros) {
                g.GyroOverride = false;
            }

            List<IMyThrust> thrusters = new List<IMyThrust>();
            program.GridTerminalSystem.GetBlocksOfType(thrusters, t => !tug.Thrusters.Contains(t));
            foreach (IMyThrust thruster in thrusters) {
                thruster.Enabled = false;
            }

            List<IMyJumpDrive> jumpDrives = new List<IMyJumpDrive>();
            program.GridTerminalSystem.GetBlocksOfType(jumpDrives, jd => !jd.IsSameConstructAs(program.Me));
            foreach (IMyJumpDrive jd in jumpDrives) {
                jd.Enabled = false;
            }

            List<IMyTerminalBlock> shields = new List<IMyTerminalBlock>();
            program.GridTerminalSystem.SearchBlocksOfName("Shield Controller", shields, sc => !sc.IsSameConstructAs(program.Me));
            foreach (IMyTerminalBlock shield in shields) {
                ((IMyUpgradeModule)shield).Enabled = false;
            }

            //TODO: Weapons and projectors
        }

        private void RenameGrids() {
            if (program.Me.CubeGrid.CustomName != Config.TUG_NAME) {
                program.Me.CubeGrid.CustomName = Config.TUG_NAME;
            }

            var printGrid = tug.GetPrintGrid();
            if (LoadedRecord != null && printGrid != null && printGrid != program.Me.CubeGrid) {
                printGrid.CustomName = LoadedRecord.Name;
            }

        }
    }
}
