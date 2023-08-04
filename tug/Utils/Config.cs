namespace IngameScript.Data {
    internal class Config {
        //Faster prints for creative testing. Disables timers for blocks to weld, increases rotor speed
        public static readonly bool CREATIVE = true;

        //Enable finer-grained logging and digi draw debug
        public static readonly bool DEBUG_ENABLED = false;

        //The printer will automatically rename the tug grid this
        public static readonly string TUG_NAME = "COAP Tug";

        //Number of large grid blocks from the front connector to the front of the tug
        public static readonly int CONNECTOR_TO_SPRUE_OFFSET = 4;

        //Unicast ID used for multiprint support (for testing multiple printers simultaneously)
        //If not 0, IGC will use direct unicast communication instead of broadcasting
        public static long BROADCAST_ID = 0;
        public static readonly string IGC_TUG_CHANNEL = "PrinterTug";
        public static readonly string IGC_STATION_CHANNEL = "PrinterBase";
        public static readonly string IGC_CALLBACK = "broadcast";
    }
}
