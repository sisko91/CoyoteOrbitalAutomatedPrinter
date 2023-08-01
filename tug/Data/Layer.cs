namespace IngameScript.Data {
    public class Layer {
        public int RemainingBlocks { get; set; }
        public bool Extended { get; set; }

        public Layer(int remainingBlocks, bool extended) {
            this.RemainingBlocks = remainingBlocks;
            this.Extended = extended;
        }
    }
}
