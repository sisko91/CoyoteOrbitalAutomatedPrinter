using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using VRageRender;

namespace IngameScript {
    internal class Display {
        IMyTextPanel drawingSurface;
        RectangleF viewport;

        //TODO: Should take printer, or printer take display? Need to flesh this out
        public Display(IMyTextPanel drawingSurface, Printer printer) {
            this.drawingSurface = drawingSurface;
            this.viewport = new RectangleF((drawingSurface.TextureSize - drawingSurface.SurfaceSize) / 2f, drawingSurface.SurfaceSize);
        }

        public void Draw() {
            //TODO
        }

        public void Clear() {
            //Draw black square over screen to force a redraw
            var frame = drawingSurface.DrawFrame();
            var sprite = new MySprite() {
                Type = SpriteType.TEXTURE,
                Data = "Square",
                Position = viewport.Center,
                Size = viewport.Size,
                Color = new Color(0, 0, 0),
                Alignment = TextAlignment.CENTER
            };
            frame.Add(sprite);
            frame.Dispose();
        }
    }
}
