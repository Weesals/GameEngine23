using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Editor {
    public class Editor {
        public CSFont DefaultFont;
    }
    public class TabbedWindow : CanvasRenderable {
        public CanvasImage TitleBG = new();
        public CanvasText TitleText = new();
        public CanvasImage PanelBG = new();

        public string Title { get => TitleText.Text; set => TitleText.Text = value; }
        public bool EnableBackground { get; set; } = true;

        public TabbedWindow(Editor editor, string title) {
            Title = title;
            TitleText.SetFont(editor.DefaultFont, 16);
        }

        protected CanvasLayout GetContentsLayout() {
            var layout = mLayoutCache;
            layout.SliceTop(TitleText.GetPreferredHeight() + 5);
            return layout;
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            TitleBG.Initialize(Canvas);
            TitleText.Initialize(Canvas);
            PanelBG.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            PanelBG.Dispose(Canvas);
            TitleBG.Dispose(Canvas);
            TitleText.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            var layout = mLayoutCache;
            var tabHeader = layout.SliceTop(TitleText.GetPreferredHeight() + 5).Inset(1);
            TitleBG.UpdateLayout(Canvas, tabHeader);
            TitleText.UpdateLayout(Canvas, tabHeader);
            if (EnableBackground) PanelBG.UpdateLayout(Canvas, layout.Inset(1));
        }
        public override void UpdateChildLayouts() {
            if (mChildren.Count == 0) return;
            var contentLayout = GetContentsLayout();
            foreach (var child in mChildren) {
                child.UpdateLayout(contentLayout);
            }
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            TitleBG.Append(ref composer);
            TitleText.Append(ref composer);
            if (EnableBackground) PanelBG.Append(ref composer);
            base.Compose(ref composer);
        }
    }
    public class UIInspector : TabbedWindow {
        public UIInspector(Editor editor) : base(editor, "Inspector") {
        }
    }
    public class UIGameView : TabbedWindow {
        public UIGameView(Editor editor) : base(editor, "Game") {
            EnableBackground = false;
        }

        public RectI GetGameViewportRect() {
            var gameLayout = GetContentsLayout();
            return new RectI(
                (int)gameLayout.Position.X, (int)gameLayout.Position.Y,
                (int)gameLayout.GetWidth(), (int)gameLayout.GetHeight());
        }
    }
    public class UIProjectView : TabbedWindow {
        public UIProjectView(Editor editor) : base(editor, "Project") {
        }
    }


    public class EditorWindow : IDisposable {
        public Editor Editor;
        public UIInspector Inspector;
        public UIGameView GameView;
        public UIProjectView ProjectView;

        public Canvas Canvas;

        public EditorWindow() {
            Editor = new();
            Editor.DefaultFont = CSResources.LoadFont("./assets/Roboto-Regular.ttf");

            Canvas = new();
            var flex = new FlexLayout();
            Inspector = new(Editor) { };
            GameView = new(Editor) { };
            ProjectView = new UIProjectView(Editor);
            flex.AppendRight(GameView);
            flex.InsertBelow(GameView, ProjectView, 0.3f);
            flex.AppendRight(Inspector, 0.25f);

            Canvas.AppendChild(flex);
        }

        public void UpdateLayout(Int2 size) {
            Canvas.SetSize(size);
        }
        public void Render(CSGraphics graphics) {
            Canvas.Render(graphics, Canvas.Material);
        }

        public void Dispose() {
            Canvas.Dispose();
        }
    }
}
