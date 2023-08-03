using IngameScript.Logging;
using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using VRageRender;

namespace IngameScript {
    internal class Display {
        IMyTextPanel drawingSurface;
        RectangleF viewport;

        static readonly Color FULL_COLOR = new Color(100, 250, 100);
        static readonly Color BACKGROUND_COLOR = new Color(50, 125, 50);

        public Display(IMyTextPanel drawingSurface, Printer printer) {
            this.drawingSurface = drawingSurface;
            this.viewport = new RectangleF((drawingSurface.TextureSize - drawingSurface.SurfaceSize) / 2f, drawingSurface.SurfaceSize);
        }

        public void Draw() {
            var frame = drawingSurface.DrawFrame();

            frame.Dispose();
        }

        //Draw black square over screen to force a redraw
        public void Clear() {
            var frame = drawingSurface.DrawFrame();
            var sprite = new MySprite(SpriteType.TEXTURE, "Square", viewport.Center, viewport.Size, new Color(0, 0, 0));
            frame.Add(sprite);
            frame.Dispose();
        }

        private List<MySprite> CircleMeter(Vector2 pos, Vector2 size, float value) {
            MySprite full;
            MySprite semi;
            MySprite top;
            MySprite bottom;
            MySprite inner;

            Vector2 innerSize = size * 0.8f;

            full = new MySprite(SpriteType.TEXTURE, "Circle", pos, size, color: FULL_COLOR);
            top = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size, color: FULL_COLOR);
            bottom = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size, color: BACKGROUND_COLOR);
            semi = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size, color: BACKGROUND_COLOR);
            inner = new MySprite(SpriteType.TEXTURE, "Circle", pos, innerSize, color: new Color(0, 0, 0));

            bottom.RotationOrScale = MathHelper.ToRadians(180f);
            semi.RotationOrScale = MathHelper.ToRadians(value*360f);

            var sprites = new List<MySprite> { full };

            if (value > 0.5) {
                sprites.Add(semi);
                sprites.Add(top);
            } else {
                sprites.Add(semi);
                sprites.Add(bottom);
            }
            sprites.Add(inner);

            return sprites;
        }

        private List<MySprite> BarMeter(Vector2 pos, Vector2 size, float value) {
            var background = new MySprite(SpriteType.TEXTURE, "SquareSimple", pos, size, BACKGROUND_COLOR);

            var barPos = new Vector2(pos.X - size.X/2, pos.Y);
            var barSize = new Vector2(size.X * value, size.Y);
            var bar = new MySprite(SpriteType.TEXTURE, "SquareSimple", barPos, barSize, FULL_COLOR, alignment: TextAlignment.LEFT);

            var sprites = new List<MySprite> { background, bar };
            return sprites;
        }
    }
}
