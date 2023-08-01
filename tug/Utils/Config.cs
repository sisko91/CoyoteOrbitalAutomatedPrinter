namespace IngameScript.Data {
    internal class Config {
        //Faster prints for creative testing. Disables timers for blocks to weld, increases rotor speed
        public static readonly bool CREATIVE = true;

        public static readonly bool DEBUG_ENABLED = false;

        public static readonly string TUG_NAME = "COAP Tug";

        //Number of large grid blocks from the front connector to the front of the tug
        public static readonly int CONNECTOR_TO_SPRUE_OFFSET = 4;

        //ID used for multiprint support (for testing multiple printers simultaneously)
        public static long BROADCAST_ID = 0;
        public static readonly string IGC_TUG_CHANNEL = "PrinterTug";
        public static readonly string IGC_STATION_CHANNEL = "PrinterBase";
        public static readonly string IGC_CALLBACK = "broadcast";
    }
}
