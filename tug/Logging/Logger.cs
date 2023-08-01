using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript.Logging {
    internal class Logger {
        MyGridProgram program;
        int level;

        List<string> cached = new List<string>();

        public static Logger INSTANCE { get; private set; }
        public static void Create(int level, MyGridProgram program) {
            INSTANCE = new Logger(level, program);
        }

        private Logger(int level, MyGridProgram program) {
            this.level = level;
            this.program = program;
        }

        public void Log(string message, int level) {
            if (level <= this.level) {
                //if (DebugAPI.INSTANCE != null) {
                //    DebugAPI.INSTANCE.PrintChat(message);
                //}
                program.Echo(message);
                cached.Add(message);
            }
        }

        public void Log(string message) {
            Log(message, LogLevel.Info);
        }

        public void LogCached() {
            foreach (string message in cached) {
                program.Echo(message);
            }
        }

        public void Clear() {
            cached.Clear();
        }
    }
}
