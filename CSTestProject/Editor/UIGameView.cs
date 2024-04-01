using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Editor.Assets;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.UI;
using Weesals.Utility;

namespace Weesals.Editor {
    public class UIHierarchy : TabbedWindow, IUpdatable, ISelectionProxy {
        public class EntityView : Selectable {
            public readonly Entity Entity;
            public TextBlock Text;
            public EntityView(Entity entity, string name) {
                Entity = entity;
                Text = new(name) {
                    FontSize = 14,
                    Alignment = TextAlignment.Left,
                    TextColor = Color.DarkGray,
                    DisplayParameters = TextDisplayParameters.Flat,
                };
                AppendChild(Text);
                SetTransform(CanvasTransform.MakeDefault().WithOffsets(5f, 0f, 0f, 0f));
            }
            public override void OnSelected(ISelectionGroup group, bool _selected) {
                base.OnSelected(group, _selected);
                FindParent<UIHierarchy>()?.NotifyEntitySelected(Entity, IsSelected);
                Text.TextColor = IsSelected ? Color.Orange : Color.DarkGray;
            }
            public override Vector2 GetDesiredSize(SizingParameters sizing) {
                var size = base.GetDesiredSize(sizing);
                size.Y = 20f;
                return size;
            }
        }

        public World World;
        private ListLayout list = new();

        public event Action<Entity, bool> OnEntitySelected;
        public ISelectionGroup SelectionGroup { get; private set; }

        public UIHierarchy(Editor editor) : base(editor, "Hierarchy") {
            SelectionGroup = editor.ProjectSelection;
            AppendChild(list);
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, true);
        }
        public override void Uninitialise(CanvasBinding binding) {
            if (Canvas != null) Canvas.Updatables.RegisterUpdatable(this, false);
            base.Uninitialise(binding);
        }
        public void UpdateValues() {
            if (World.Stage.GetMaximumEntityId() > 100) return;
            int i = 0;
            if (World != null) {
                foreach (var entity in World.GetEntities()) {
                    var item = i < list.Children.Count ? list.Children[i] : default;
                    if (item is not EntityView btn || btn.Entity != entity)
                        item = null;
                    if (item == null) {
                        btn = new EntityView(entity, World.GetEntityName(entity));
                        list.InsertChild(i, btn);
                    }
                    ++i;
                }
            }
            while (i < list.Children.Count) {
                list.RemoveChild(list.Children[^1]);
            }
        }
        private void NotifyEntitySelected(Entity entity, bool selected) {
            OnEntitySelected?.Invoke(entity, selected);
        }
        public void Update(float dt) {
            UpdateValues();
        }
        public void NotifySelected(Entity entity, bool selected) {
            foreach (var item in list.Children) {
                if (item is not EntityView btn || btn.Entity != entity) continue;
                btn.SetSelected(selected);
            }
        }
    }
    public class UIGameView : TabbedWindow, IDropTarget, INestedEventSystem {

        public EventSystem? EventSystem;
        EventSystem? INestedEventSystem.EventSystem => EventSystem;
        CanvasLayout INestedEventSystem.GetComputedLayout() { return GetContentsLayout(); }

        // TODO: Remove these, get them dynamically
        public Camera? Camera;
        public ScenePassManager? Scene;

        public Action<PointerEvent, AssetReference> OnReceiveDrag;

        private ToggleButton realtimeToggle;
        public bool EnableRealtime => realtimeToggle.State;

        private float timeSinceFPSUpdate = 0f;
        private int ticksSinceFPSUpdate = 0;

        public UIGameView(Editor editor) : base(editor, "Game") {
            EnableBackground = false;
            realtimeToggle = new ToggleButton();
            realtimeToggle.Transform = CanvasTransform.MakeAnchored(new(40f, 40f), new(0f, 0f));
            realtimeToggle.State = false;
            realtimeToggle.AppendChild(new TextBlock("Realtime") { FontSize = 10, });
            AppendChild(realtimeToggle);
        }

        public RectI GetGameViewportRect() {
            var gameLayout = GetContentsLayout();
            return new RectI(
                (int)gameLayout.Position.X, (int)gameLayout.Position.Y,
                (int)gameLayout.GetWidth(), (int)gameLayout.GetHeight());
        }

        public bool InitializePotentialDrop(PointerEvent events, CanvasRenderable source) {
            return true;
        }
        public void UninitializePotentialDrop(CanvasRenderable source) {
        }
        public void ReceiveDrop(PointerEvent events, CanvasRenderable item) {
            Console.WriteLine("Received " + item);
            if (item is UIProjectView.FileGrid.FileView file) {
                OnReceiveDrag?.Invoke(events, file.GetAsset());
            }
        }

        public void Update(float dt) {
            timeSinceFPSUpdate += dt;
            ++ticksSinceFPSUpdate;
            if (Canvas.GetIsComposeDirty() || timeSinceFPSUpdate > 0.125f) {
                float fps = ticksSinceFPSUpdate / Math.Max(timeSinceFPSUpdate, 0.0001f);
                Title = $"Game ({(fps):0} fps)";
                timeSinceFPSUpdate = 0f;
                ticksSinceFPSUpdate = 0;
            }
        }

    }
}
