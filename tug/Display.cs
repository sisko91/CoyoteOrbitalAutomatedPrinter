using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript {
    internal class Display {
        IMyTextPanel drawingSurface;
        RectangleF viewport;
        Printer printer;
        Projection projection;

        static readonly Color FULL_COLOR = new Color(100, 250, 100);
        static readonly Color BACKGROUND_COLOR = new Color(50, 125, 50);
        static readonly Color TEXT_BAR_COLOR = Color.White;

        public Display(IMyTextPanel drawingSurface, Printer printer, Projection projection) {
            this.drawingSurface = drawingSurface;
            this.viewport = new RectangleF((drawingSurface.TextureSize - drawingSurface.SurfaceSize) / 2f, drawingSurface.SurfaceSize);
            this.printer = printer;
            this.projection = projection;
        }

        //TODO methods for relative 
        public void Draw() {
            if (!printer.Enabled) {
                Clear();
                return;
            }

            bool recordLoaded = printer.LoadedRecord != null;
            PrintRecord record = printer.LoadedRecord;

            var frame = drawingSurface.DrawFrame();

            var topBarPos = new Vector2(viewport.Center.X, 5);
            var bottomBarPos = new Vector2(viewport.Center.X, viewport.Size.Y - 5);
            var leftBarPos = new Vector2(0, viewport.Center.Y);
            var rightBarPos = new Vector2(viewport.Size.X, viewport.Center.Y);

            var horizontalLineSize = new Vector2(viewport.Size.X, 10);
            var verticalLineSize = new Vector2(5, viewport.Size.Y);

            //Draw outline
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", topBarPos, horizontalLineSize, FULL_COLOR));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", bottomBarPos, horizontalLineSize, FULL_COLOR));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", leftBarPos, verticalLineSize, FULL_COLOR));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", rightBarPos, verticalLineSize, FULL_COLOR));

            //Draw sections. Note, all positions are center points
            //1. Top title section, where the ship name and some icons are displaeyd
            //2. Middle progress section split vertically into total and layer progress
            //3. Bottom diagnostics section, with various informational text like movement, projection updates, and unfinished blocks
            var progressBarTopPos = new Vector2(viewport.Center.X, viewport.Size.Y * 0.1f); //Title section takes up top 10%
            var progressBarBottomPos = new Vector2(viewport.Center.X, viewport.Size.Y * 0.5f); //Progress section takes up next 40% (0.5 - 0.1), with diag taking the remaining 50%
            var progressBarMiddlePos = new Vector2(viewport.Center.X, (progressBarBottomPos.Y + progressBarTopPos.Y) / 2);

            //Draw inner lines
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", progressBarTopPos, horizontalLineSize, FULL_COLOR));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", progressBarBottomPos, horizontalLineSize, FULL_COLOR));
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", progressBarMiddlePos, new Vector2(verticalLineSize.X, progressBarBottomPos.Y - progressBarTopPos.Y), FULL_COLOR));

            //Diagnostic box (on the bottom, but always visible)
            frame.Add(new MySprite(SpriteType.TEXT, "Diagnostics", new Vector2(viewport.Center.X, progressBarBottomPos.Y + 15), color: FULL_COLOR, rotation: 0.6f));
            frame.Add(new MySprite(SpriteType.TEXTURE, "Danger", new Vector2(viewport.Center.X - 50, progressBarBottomPos.Y + 24), new Vector2(20, 20), FULL_COLOR * 2));
            frame.Add(new MySprite(SpriteType.TEXTURE, "Danger", new Vector2(viewport.Center.X + 50, progressBarBottomPos.Y + 24), new Vector2(20, 20), FULL_COLOR * 2));
            frame.Add(GetDiagnosticText(progressBarBottomPos));

            //Dont draw anything else if we dont have something loaded
            if (!recordLoaded) {
                frame.Dispose();
                return;
            }

            //Title box
            frame.Add(new MySprite(SpriteType.TEXT, recordLoaded ? record.Name : "", new Vector2(viewport.Center.X, 15), color: FULL_COLOR, rotation: 0.8f)); //rotation is text size (keeeen)
            //Grid size square (same icon as keen grid size in G menu, hollow square for large grid, solid square with small hollow square for small grid
            bool largeGrid = printer.IsLargeGridPrint();
            frame.Add(new MySprite(SpriteType.TEXTURE, largeGrid ? "SquareHollow" : "SquareSimple", new Vector2(15, progressBarTopPos.Y / 2), new Vector2(20, 20), FULL_COLOR));
            if (!largeGrid) {
                frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(12, progressBarTopPos.Y / 2 + 4), new Vector2(7, 7), BACKGROUND_COLOR));
            }

            if (printer.IsRecording() && printer.Pronting) {
                frame.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(viewport.Size.X - 20, progressBarTopPos.Y / 2), new Vector2(20, 20), Color.Red));
            }

            //Overall progress box
            var overallProgressCenterX = progressBarMiddlePos.X / 2f;
            frame.Add(new MySprite(SpriteType.TEXT, "Print Progress", new Vector2(overallProgressCenterX, progressBarTopPos.Y + 15), color: FULL_COLOR, rotation: 0.6f));
            foreach (MySprite s in BarMeter(new Vector2(overallProgressCenterX, progressBarTopPos.Y + 45), new Vector2(progressBarMiddlePos.X * 0.9f, 15), projection.CompletionPercentage()/100)) {
                frame.Add(s);
            }
            frame.Add(new MySprite(SpriteType.TEXT, $"{projection.Total() - projection.Remaining()} / {projection.Total()} blocks ({projection.CompletionPercentage():n2}%)",
                        new Vector2(overallProgressCenterX, progressBarTopPos.Y + 37), color: TEXT_BAR_COLOR, rotation: 0.5f));

            if (!printer.IsRecording()) {
                int totalLayers = record.Layers.Count;
                int completedLayers = printer.CompletedLayers();
                foreach (MySprite s in BarMeter(new Vector2(overallProgressCenterX, progressBarTopPos.Y + 75), new Vector2(progressBarMiddlePos.X * 0.9f, 15), ((float) completedLayers) / totalLayers)) {
                    frame.Add(s);
                }
                frame.Add(new MySprite(SpriteType.TEXT, $"{completedLayers} / {totalLayers} layers",
                            new Vector2(overallProgressCenterX, progressBarTopPos.Y + 67), color: TEXT_BAR_COLOR, rotation: 0.5f));
            }

            var printText = "";
            if (printer.Pronting) {
                printText = "Printing...";
            } else if (printer.PrintFinished()) {
                printText = "Print Complete";
            }

            frame.Add(new MySprite(SpriteType.TEXT, printText,
                      new Vector2(overallProgressCenterX, progressBarTopPos.Y + 100), color: FULL_COLOR, rotation: 0.5f));

            //Layer progress box
            if (printer.PrintFinished()) {
                frame.Dispose();
                return;
            }

            var layerProgressCenterX = progressBarMiddlePos.X + progressBarMiddlePos.X / 2f;
            var circleMeterSize = new Vector2(progressBarMiddlePos.X * 0.33f, progressBarMiddlePos.X * 0.33f);
            frame.Add(new MySprite(SpriteType.TEXT, $"Layer {printer.CurrentLayer()}", new Vector2(layerProgressCenterX, progressBarTopPos.Y + 15), color: FULL_COLOR, rotation: 0.6f));

            if (!printer.IsRecording()) {
                foreach (MySprite s in BarMeter(new Vector2(layerProgressCenterX, progressBarTopPos.Y + 45), new Vector2(progressBarMiddlePos.X * 0.9f, 15), printer.CurrentLayerCompletionPercentage()/100)) {
                    frame.Add(s);
                }
                frame.Add(new MySprite(SpriteType.TEXT, $"{printer.CurrentLayerTotal() - printer.CurrentLayerRemaining()} / {printer.CurrentLayerTotal()} blocks ({printer.CurrentLayerCompletionPercentage():n2}%)",
                          new Vector2(layerProgressCenterX, progressBarTopPos.Y + 37), color: TEXT_BAR_COLOR, rotation: 0.5f));
            }

            if (!printer.ShouldAdvance) {
                foreach (MySprite s in CircleMeter(new Vector2(layerProgressCenterX * 0.87f, progressBarTopPos.Y + 110), circleMeterSize, (float)(printer.PrintLayerTimer / printer.LayerTimeout()))) {
                    frame.Add(s);
                }
                frame.Add(new MySprite(SpriteType.TEXT, $"Layer Time{Environment.NewLine}{printer.PrintLayerTimer:n0}/{printer.LayerTimeout():n0}s",
                          new Vector2(layerProgressCenterX * 0.87f, progressBarTopPos.Y + 100), color: TEXT_BAR_COLOR, rotation: 0.4f));

                foreach (MySprite s in CircleMeter(new Vector2(layerProgressCenterX * 1.12f, progressBarTopPos.Y + 120), circleMeterSize, (float)(printer.NoMassChangeTimer / printer.MassTimeout()))) {
                    frame.Add(s);
                }
                frame.Add(new MySprite(SpriteType.TEXT, $"No{Environment.NewLine}mass change{Environment.NewLine}{printer.NoMassChangeTimer:n0}/{printer.MassTimeout():n0}s",
                          new Vector2(layerProgressCenterX * 1.12f, progressBarTopPos.Y + 100), color: TEXT_BAR_COLOR, rotation: 0.4f));
            } else {
                foreach (MySprite s in CircleMeter(new Vector2(layerProgressCenterX, progressBarTopPos.Y + 115), circleMeterSize, (float)(printer.TimeSinceLastBlockPlaced() / Printer.LAST_BLOCK_WAIT_SECONDS))) {
                    frame.Add(s);
                }
                frame.Add(new MySprite(SpriteType.TEXT, $"Next{Environment.NewLine}layer in{Environment.NewLine}{Printer.LAST_BLOCK_WAIT_SECONDS - printer.TimeSinceLastBlockPlaced():n0}s",
                          new Vector2(layerProgressCenterX, progressBarTopPos.Y + 95), color: TEXT_BAR_COLOR, rotation: 0.4f));
            }

            frame.Dispose();
        }

        public MySprite GetDiagnosticText(Vector2 progressBarBottomPos) {
            string diagnostics = "";

            if (!printer.Pronting) {
                diagnostics += printer.Aligned ? "- Aligned" : "- Aligning";
                diagnostics += Environment.NewLine;
            }

            if (printer.Moving()) {
                diagnostics += "- Moving: ";
                if (printer.Aligned) {
                    diagnostics += $"{printer.ZPosition()}m from print head";
                }
                diagnostics += Environment.NewLine;
            }

            if (printer.PistonsMoving()) {
                diagnostics += "- Pistons moving" + Environment.NewLine;
            }

            if (printer.CheckingProjectionUpdates) {
                diagnostics += "- Checking if projection changed..." + Environment.NewLine;
            }

            if (printer.MissedBlocks != 0) {
                diagnostics += $"- Missed blocks: {printer.MissedBlocks}";
            }

            if (printer.UnfinishedBlocks.Count != 0) {
                diagnostics += "- Waiting on unfinished blocks: ";
                foreach (string block in printer.UnfinishedBlocks.Take(3)) {
                    diagnostics += block + Environment.NewLine;
                }
                if (printer.UnfinishedBlocks.Count > 3) {
                    diagnostics += $"...and {printer.UnfinishedBlocks.Count - 3} more{Environment.NewLine}";
                }
            }

            return new MySprite(SpriteType.TEXT, diagnostics,
                                new Vector2(10, progressBarBottomPos.Y + 30), color: FULL_COLOR, rotation: 0.5f, alignment: TextAlignment.LEFT);
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

            Vector2 innerSize = size * 0.75f;

            full = new MySprite(SpriteType.TEXTURE, "Circle", pos, size * 0.97f, color: FULL_COLOR);
            top = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size * 0.97f, color: FULL_COLOR);
            bottom = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size, color: BACKGROUND_COLOR);
            semi = new MySprite(SpriteType.TEXTURE, "SemiCircle", pos, size, color: BACKGROUND_COLOR);
            inner = new MySprite(SpriteType.TEXTURE, "Circle", pos, innerSize, color: new Color(0, 0, 0));

            bottom.RotationOrScale = MathHelper.ToRadians(180f);
            semi.RotationOrScale = MathHelper.ToRadians(value*360f);

            var sprites = new List<MySprite>();

            if (value != 0) {
                sprites.Add(full);
            }

            if (value > 0.5) {
                sprites.Add(semi);
                if (value != 0) {
                    sprites.Add(top);
                }
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
