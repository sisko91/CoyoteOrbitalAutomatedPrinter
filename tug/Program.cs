using IngameScript.Data;
using IngameScript.Logging;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace IngameScript {
    partial class Program : MyGridProgram {
        Logger logger;

        Tug tug;
        Projection projection;
        Display display;
        Printer printer;
        PrintHead printHead;

        IMyBroadcastListener listener;
        IMyUnicastListener unicastListener;

        int ticks = 0;
        double elapsed = 0;

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            if (Config.DEBUG_ENABLED) {
                DebugAPI.Create(this, true);
            }

            Logger.Create(Config.DEBUG_ENABLED ? LogLevel.Info : LogLevel.Debug, this);
            logger = Logger.INSTANCE;

            listener = IGC.RegisterBroadcastListener(Config.IGC_TUG_CHANNEL);
            listener.SetMessageCallback(Config.IGC_CALLBACK);

            unicastListener = IGC.UnicastListener;
            unicastListener.SetMessageCallback(Config.IGC_CALLBACK);

            var gyros = new List<IMyGyro>();
            var gyroGroup = GridTerminalSystem.GetBlockGroupWithName("Tug Gyros");
            gyroGroup.GetBlocksOfType(gyros);

            var cockpit = GridTerminalSystem.GetBlockWithName("Tug Seat") as IMyShipController;
            var remoteControl = GridTerminalSystem.GetBlockWithName("Tug Remote") as IMyShipController;

            var thrusters = new List<IMyThrust>();
            var thrusterGroup = GridTerminalSystem.GetBlockGroupWithName("Tug Thrusters");
            thrusterGroup.GetBlocksOfType(thrusters);

            var mergeBlocks = new List<IMyShipMergeBlock>();
            IMyBlockGroup mergeGroup = GridTerminalSystem.GetBlockGroupWithName("Tug Merge");
            mergeGroup.GetBlocksOfType(mergeBlocks);

            var frontConnector = GridTerminalSystem.GetBlockWithName("Front Connector") as IMyShipConnector;
            tug = new Tug(cockpit, remoteControl, gyros, thrusters, mergeBlocks, frontConnector, logger);

            var projector = GridTerminalSystem.GetBlockWithName("Tug Projector") as IMyProjector;
            var smallprojector = GridTerminalSystem.GetBlockWithName("Small Tug Projector") as IMyProjector;
            projection = new Projection(projector, smallprojector);

            printHead = new PrintHead(this);
            printer = new Printer(tug, projection, printHead, this);

            Load();
            printer.Reset();

            var drawingSurface = GridTerminalSystem.GetBlockWithName("Tug LCD") as IMyTextPanel;
            display = new Display(drawingSurface, printer);
        }

        public void Main(string argument, UpdateType updateSource) {
            try {
                bool update100 = false;
                HandleArgs(argument);

                //TODO: Wrap
                ticks += 1;
                elapsed += Runtime.TimeSinceLastRun.TotalSeconds;

                if (ticks % 20 != 0) {
                    logger.LogCached();
                    return;
                }
                if (ticks == 100) {
                    update100 = true;
                    ticks = 0;
                }

                logger.Clear();
                logger.Log($"broadcastID: {Config.BROADCAST_ID}");

                if (Config.DEBUG_ENABLED) {
                    DebugAPI.INSTANCE.RemoveDraw();
                }

                display.Draw();

                if (printer.Enabled) {
                    printer.TimeSinceLastRun = elapsed;
                    printer.Run(update100);
                }

                elapsed = 0;
                logger.Log("---------------------------", LogLevel.Verbose);
            } catch (Exception e) {
                Stop();
                throw new Exception($"{e.Message}:{e.StackTrace}");
            }
        }

        public void Save() {
            printer.Save();
        }

        private void Load() {
            printer.Records = printer.GetSavedRecords();
        }

        private void HandleArgs(string arg) {
            if (String.IsNullOrEmpty(arg)) {
                return;
            }

            if (arg == Config.IGC_CALLBACK) {
                HandleIGCMessages();
            } else if (arg == "Toggle") { //Turn the printer on and off
                printer.Enabled = !printer.Enabled;
                tug.ToggleGyroControl(!printer.Enabled);
                printer.Reset();
                if (!printer.Enabled) {
                    Me.CustomData = "";
                }
            } else if (arg == "TogglePrintSize") { //Change between printing large and small grids
                printer.TogglePrintSize();
            } else if (arg == "Print") { //Start or pause a print
                if (!printer.Enabled || !printer.Pronting && printer.LoadedRecord == null) { return; }
                printer.ReloadRecord();
                printer.Pronting = !printer.Pronting;
                if (!printer.Pronting) {
                    printHead.DisableRotorAndWelders();
                }
                tug.ToggleController(printer.Pronting);
            } else if (arg == "ToggleProjectionMode") { //Move projection with normal ship controls
                if (!printer.Enabled) { return; }
                printer.ToggleProjectionMode();
            } else if (arg == "Advance") { //Go to the next layer, use when recording
                printer.Advance();
            } else if (arg == "ClearData") {
                printer.LoadedRecord = null;
                printer.Records = new Dictionary<string, PrintRecord>();
                Me.CustomData = "";
                tug.Cockpit.CustomData = "";
                Save();
            } else if (arg == "DeleteRecord") {
                if (printer.LoadedRecord == null) { return; }
                printer.Records[printer.LoadedRecord.Name] = null;
                printer.LoadedRecord = null;
                Me.CustomData = "";
                Save();
            } else if (arg == "PistonsExtended") { //Used by printer base, not for manual use
                printHead.PState = PrintHead.PistonState.Extended;
            } else if (arg == "PistonsRetracted") {
                printHead.PState = PrintHead.PistonState.Retracted;
            }
        }

        private void Stop() {
            tug.Reset(true);
            printHead.Unpack();
        }

        private void HandleIGCMessages() {
            while (listener.HasPendingMessage) {
                ProcessMessage(listener.AcceptMessage());
            }

            while (unicastListener.HasPendingMessage) {
                ProcessMessage(unicastListener.AcceptMessage(), true);
            }
        }

        private void ProcessMessage(MyIGCMessage message, bool unicast = false) {
            if (message.Data is MyTuple<Vector3D, MatrixD>) {
                var payload = (MyTuple<Vector3D, MatrixD>)message.Data;
                printer.UpdatePrinterPosition(payload.Item1, payload.Item2);
                return;
            }

            string arg = message.Data as string;
            if (arg == "GetPosition") {
                IGC.SendUnicastMessage(message.Source, "Tug", tug.GetPosition());
            } else if (message.Data is MyTuple<Vector3D, MatrixD>) {
                var payload = (MyTuple<Vector3D, MatrixD>)message.Data;
                printer.UpdatePrinterPosition(payload.Item1, payload.Item2);
                return;
            } else if (arg == "SetBroadcastID") {
                Config.BROADCAST_ID = long.Parse(message.Tag);
            } else if (arg == "GetPosition") {
                IGC.SendUnicastMessage(message.Source, "Tug", tug.GetPosition());
            } else {
                HandleArgs(arg);
            }
        }
    }
}
