﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;
using Weesals.ECS;
using Weesals.Editor.Assets;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Landscape;
using Weesals.UI;

namespace Weesals.Editor {
    public class Editor {
        public Font DefaultFont;
        public AssetDatabase AssetDatabase;
        public SelectionManager ProjectSelection = new(null);
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
            TitleText.Color = Color.DarkGray;
            TitleText.DisplayParameters = TextDisplayParameters.Header;
            TitleBG.SetSprite(Resources.TryLoadSprite("HeaderBG"));
            PanelBG.SetSprite(Resources.TryLoadSprite("PanelBG"));
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
            TitleBG.MarkLayoutDirty();
            TitleText.MarkLayoutDirty();
            PanelBG.MarkLayoutDirty();
        }
        public override void UpdateChildLayouts() {
            if (mChildren.Count == 0) return;
            var contentLayout = GetContentsLayout();
            foreach (var child in mChildren) {
                child.UpdateLayout(contentLayout);
            }
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            var layout = mLayoutCache;
            var tabHeader = layout.SliceTop(TitleText.GetPreferredHeight() + 5);
            TitleBG.UpdateLayout(Canvas, tabHeader);
            TitleText.UpdateLayout(Canvas, tabHeader);
            if (EnableBackground) PanelBG.UpdateLayout(Canvas, layout);

            TitleBG.Append(ref composer);
            TitleText.Append(ref composer);
            if (EnableBackground) PanelBG.Append(ref composer);
            base.Compose(ref composer);
        }
    }


    public class EditorWindow : ApplicationWindow, IDisposable {
        private static ProfilerMarker ProfileMarker_Update = new("Editor Update");
        private static ProfilerMarker ProfileMarker_Render = new("Editor Render");
        public Editor Editor;
        public UIInspector Inspector;
        public UIGameView GameView;
        public UIHierarchy Hierarchy;
        public UIProjectView ProjectView;
        public EventSystem EventSystem;
        public Canvas Canvas;
        public AssetDatabase AssetDatabase;

        public bool RequireRepaint;

        public EditorWindow() {
            Editor = new() {
                DefaultFont = Resources.LoadFont("./Assets/Roboto-Regular.ttf"),
                AssetDatabase = new(),
            };

            Canvas = new();
            EventSystem = new EventSystem(Canvas);
            var flex = new FlexLayout();
            Inspector = new(Editor) { };
            GameView = new(Editor) { };
            Hierarchy = new(Editor) { };
            ProjectView = new UIProjectView(Editor);
            flex.AppendRight(GameView);
            flex.InsertBelow(GameView, ProjectView, 0.3f);
            flex.AppendRight(Hierarchy, 0.15f);
            flex.AppendRight(Inspector, 0.25f);

            Canvas.AppendChild(flex);
        }

        public override void RegisterRootWindow(CSWindow window) {
            base.RegisterRootWindow(window);
            EventSystem.SetInput(Input);
        }

        public void ActivateLandscapeTools(LandscapeRenderer landscape) {
            Inspector.LandscapeTools.Initialize(GameView, landscape);
            Inspector.SetInspector(Inspector.LandscapeTools);
            GameView.AppendChild(Inspector.LandscapeTools.InputDispatcher);
        }
        public void ActivateEntityInspector(World world, Entity entity) {
            if (entity.IsValid) {
                Inspector.SetInspector(Inspector.EntityInspector);
                Inspector.EntityInspector.Initialise(world, entity);
            } else {
                Inspector.SetInspector(default);
            }
        }

        public void Update(float dt, Int2 size) {
            using var marker = ProfileMarker_Update.Auto();
            Canvas.SetSize(size);
            EventSystem.Update(dt);
            Canvas.Update(dt);
            Canvas.RequireComposed();
        }
        public void Render(CSGraphics graphics) {
            using var marker = ProfileMarker_Render.Auto();
            Canvas.Render(graphics);
        }

        public new void Dispose() {
            Canvas.Dispose();
            base.Dispose();
        }

    }
}
