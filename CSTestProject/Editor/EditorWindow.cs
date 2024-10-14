using System;
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
using Weesals.Engine.Jobs;
using Weesals.Engine.Profiling;
using Weesals.Landscape;
using Weesals.UI;

public struct EntityAccessor {
    public readonly EntityManager Manager;
    public readonly Entity Entity;
    public bool IsValid => Manager != null;
    public EntityAccessor(EntityManager manager, Entity entity) {
        Manager = manager;
        Entity = entity;
    }
    public T GetComponent<T>() {
        return Manager.GetComponent<T>(Entity);
    }
    public ref T GetComponentRef<T>() {
        return ref Manager.GetComponentRef<T>(Entity);
    }
}

public static class EntityProxyExt {
    public static EntityAccessor GetAccessor(this ItemReference target) {
        if (target.Owner is IItemRedirect redirect) target = redirect.GetOwner(target.Data);
        if (target.Owner is World world) {
            var entity = UnpackEntity(target.Data);
            entity.SetDebugManager(world.Manager);
            return new(world.Manager, entity);
        }
        return
            //target.Owner is EntityProxy ? EntityProxy.UnpackEntity(target.Data) :
            default;
    }
    public static Entity GetEntity(this ItemReference target) {
        if (target.Owner is IItemRedirect redirect) target = redirect.GetOwner(target.Data);
        if (target.Owner is World world) {
            var entity = UnpackEntity(target.Data);
            entity.SetDebugManager(world.Manager);
            return entity;
        }
        return
            //target.Owner is EntityProxy ? EntityProxy.UnpackEntity(target.Data) :
            default;
    }
    public static ItemReference CreateItemReference(this EntityManager manager, Entity entity) {
        return new(manager, PackEntity(entity));
    }
    public static ulong PackEntity(Entity entity) {
        return ((ulong)(uint)entity.Index) | ((ulong)(uint)entity.Version << 32);
    }
    public static Entity UnpackEntity(ulong id) {
        return new Entity() { Index = (uint)(id), Version = (uint)(id >> 32), };
    }
}

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

        public string Title { get => TitleText.Text; set { TitleText.Text = value; MarkLayoutDirty(); MarkComposeDirty(); } }
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

        public Action<float> OnUpdate;
        public Action<float, CSGraphics> OnRender;

        private FlexLayout flex;
        private bool windowMoved = false;

        public struct Preferences {
            public RectI WindowFrame;
            public bool Maximized;
        }

        public bool FullScreen {
            get => flex.Children.Contains(Hierarchy);
            set {
                if (FullScreen == value) return;
                if (FullScreen) {
                    flex.RemoveChild(ProjectView);
                    flex.RemoveChild(Hierarchy);
                    flex.RemoveChild(Inspector);
                } else {
                    flex.InsertBelow(GameView, ProjectView, 0.3f);
                    flex.AppendRight(Hierarchy, 0.15f);
                    flex.AppendRight(Inspector, 0.25f);
                }
            }
        }

        public EditorWindow() {
            Editor = new() {
                DefaultFont = Resources.LoadFont("./Assets/Roboto-Regular.ttf"),
                AssetDatabase = new(),
            };
            Canvas = new();
            EventSystem = new EventSystem(Canvas);
            flex = new FlexLayout();
            Inspector = new(Editor) { };
            GameView = new(Editor) { };
            Hierarchy = new(Editor) { };
            ProjectView = new UIProjectView(Editor);
            flex.AppendRight(GameView);
            FullScreen = false;

            Canvas.AppendChild(flex);

            Editor.ProjectSelection.OnSelectionChanged += (selection) => {
                foreach (var selected in selection) {
                    if (selected.Owner is LandscapeRenderer landscape) {
                        ActivateLandscapeTools(landscape);
                        return;
                    }
                    var entity = selected;
                    if (entity.Owner is IItemRedirect redirect)
                        entity = redirect.GetOwner(entity.Data);
                    if (entity.Owner is World world) {
                        ActivateEntityInspector(world, entity.GetEntity());
                        return;
                    }
                    if (entity.Owner != null) {
                        ActivateGenericInspector(entity);
                        return;
                    }
                }
                Inspector.SetInspector(default);
            };
            Inspector.OnRegisterInspector += (inspector, enable) => {
                if (inspector is IInspectorGameOverlay overlay) {
                    if (enable) {
                        GameView.AppendChild(overlay.GameViewOverlay);
                    } else {
                        GameView.RemoveChild(overlay.GameViewOverlay);
                    }
                }
            };
        }

        public Preferences ReadPreferences() {
            Preferences pref = default;
            try {
                var json = new SJson(File.ReadAllText("./Config/window.txt"));
                pref.WindowFrame = new RectI(json["x"], json["y"], json["w"], json["h"]);
                pref.Maximized = json["max"];
            } catch { }
            return pref;
        }
        public void ApplyPreferences(CSWindow window, Preferences pref) {
            window.SetWindowFrame(pref.WindowFrame, pref.Maximized);
        }
        public void SavePreferences(Preferences pref) {
            Directory.CreateDirectory("./Config/");
            var frame = pref.WindowFrame;
            File.WriteAllText("./Config/window.txt", $@"{{ x:{frame.X}, y:{frame.Y}, w:{frame.Width}, h:{frame.Height}, max:{pref.Maximized} }}");
        }

        public override void RegisterRootWindow(CSWindow window) {
            window.RegisterMovedCallback(() => { windowMoved = true; }, true);

            base.RegisterRootWindow(window);
            EventSystem.SetInput(Input);
        }

        public void ActivateLandscapeTools(LandscapeRenderer landscape) {
            Inspector.LandscapeTools.Initialize(GameView, landscape);
            Inspector.SetInspector(Inspector.LandscapeTools);
        }
        public void ActivateGenericInspector(ItemReference target) {
            if (target.IsValid) {
                Inspector.GenericInspector.Editables.Clear();
                Inspector.GenericInspector.Editables.Add(target.Owner);
                Inspector.GenericInspector.Refresh();
                Inspector.SetInspector(Inspector.GenericInspector);
            } else {
                Inspector.SetInspector(default);
            }
        }
        public void ActivateEntityInspector(World world, Entity entity) {
            if (entity.IsValid) {
                Inspector.EntityInspector.Initialise(world, entity);
                Inspector.SetInspector(Inspector.EntityInspector);
            } else {
                Inspector.SetInspector(default);
            }
        }

        public override void Update(float dt) {
            using var marker = ProfileMarker_Update.Auto();
            if (windowMoved) {
                var frame = Window.GetWindowFrame();
                Debug.WriteLine("Window moved");
                SavePreferences(new() { WindowFrame = frame.Position, Maximized = frame.Maximized != 0 });
                windowMoved = false;
            }

            Weesals.Engine.Input.Initialise(Input);
            if (Input.GetKeyPressed(KeyCode.F11) && !Input.GetKeyDown(KeyCode.Alt)) FullScreen = !FullScreen;
            if (Input.GetKeyPressed(KeyCode.D) && Input.GetKeyDown(KeyCode.Control)) EventSystem.EnableDebug ^= true;

            Canvas.SetSize(Size);
            EventSystem.Update(dt);
            Canvas.Update(dt);
            Canvas.RequireComposed();
            OnUpdate?.Invoke(dt);
        }

        public override float AdjustRenderDT(float dt, int renderHash, float targetDT) {
            if (GameView != null) {
                if (GameView.GameRoot != null) {
                    renderHash += GameView.GameRoot.RenderHash;
                }
                if (GameView.EnableRealtime) targetDT = 0f;
            }
            if (FrameThrottler.IsOnBattery) {
                targetDT = Math.Max(targetDT, 1f / 60f);
            }
            renderHash += Canvas.Revision;
            return base.AdjustRenderDT(dt, renderHash, targetDT);
        }

        public override void Render(float dt, CSGraphics graphics) {
            using var marker = ProfileMarker_Render.Auto();

            graphics.Reset();

            CSGraphics.AsyncReadback.Awaiter.InvokeCallbacks(graphics);
            graphics.SetSurface(Surface);

            // Render the game world and UI
            OnRender?.Invoke(dt, graphics);

            // Render the editor chrome
            graphics.SetViewport(new RectI(0, Size));
            GameView.Update(dt);
            Canvas.Render(graphics);

            // Flush render command buffer
            graphics.Execute();
            Surface.Present();
        }

        public new void Dispose() {
            Canvas.Dispose();
            base.Dispose();
        }

    }
}
