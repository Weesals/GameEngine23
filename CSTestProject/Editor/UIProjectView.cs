using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Editor.Assets;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Editor {
    public class UIProjectView : TabbedWindow, ISelectionProxy {
        public class FolderList : CanvasRenderable, ISelectionGroup {
            public class FolderView : Selectable {
                public readonly FolderView? ParentFolder;
                public readonly string Filename;
                public TextBlock Text;
                public FolderView(string filename, string name, FolderView? parentFolder = null) {
                    Filename = filename;
                    ParentFolder = parentFolder;
                    Text = new(name) {
                        FontSize = 14,
                        Alignment = TextAlignment.Left,
                        TextColor = Color.DarkGray,
                    };
                    AppendChild(Text);
                    var depth = 0;
                    for (var p = ParentFolder; p != null; p = p.ParentFolder) ++depth;
                    SetTransform(CanvasTransform.MakeDefault().WithOffsets(5f + 5f * depth, 0f, 0f, 0f));
                }
                public override void OnSelected(ISelectionGroup group, bool _selected) {
                    base.OnSelected(group, _selected);
                    if (IsSelected) {
                        FindParent<UIProjectView>()?.SetContentPath(Filename);
                    }
                    Text.TextColor = IsSelected ? Color.Orange : Color.DarkGray;
                }
                public override Vector2 GetDesiredSize(SizingParameters sizing) {
                    var size = base.GetDesiredSize(sizing);
                    size.Y = 20f;
                    return size;
                }
            }

            FolderView? selectedFolder;
            ScrollView scrollView = new() { ScrollMask = new Vector2(0f, 1f), };
            ListLayout folderList = new() { Axis = ListLayout.Axes.Vertical, ScaleMode = ListLayout.ScaleModes.StretchOrClamp, };

            public IReadOnlyCollection<ItemReference> Selected => new[] { new ItemReference(selectedFolder) };

            public FolderList() {
                scrollView.AppendChild(folderList);
                AppendChild(scrollView);
            }
            public void SetRoot(string rootPath) {
                folderList.ClearChildren();
                var root = new FolderView(rootPath, "Project");
                folderList.AppendChild(root);
                foreach (var folder in Directory.EnumerateDirectories(rootPath)) {
                    folderList.AppendChild(new FolderView(folder, Path.GetFileName(folder), root));
                }
            }

            public void SetContentPath(string path) {
                foreach (var child in folderList.Children) {
                    if (child is FolderView folderView && folderView.Filename == path) {
                        folderView.Select();
                    }
                }
            }
            public void SetSelected(ItemReference selectable) {
                if (selectedFolder == selectable.Owner) return;
                if (selectedFolder is ISelectable oselectable) oselectable.OnSelected(this, false);
                selectedFolder = selectable.Owner as FolderView;
                if (selectedFolder is ISelectable nselectable) nselectable.OnSelected(this, true);
            }

            public void ClearSelected() {
                SetSelected(default);
            }
            public void AppendSelected(ItemReference selectable) {
                SetSelected(selectable);
            }
            public void RemoveSelected(ItemReference selectable) {
                if (selectedFolder == selectable.Owner) SetSelected(default);
            }
        }
        public class FileGrid : CanvasRenderable {
            public class FileType {
                public Sprite? Icon;
                public static FileType Folder = new() { Icon = Resources.TryLoadSprite("FolderIcon"), };
                public static FileType File = new() { Icon = Resources.TryLoadSprite("FileIcon"), };
                public static FileType ShaderFile = new() { Icon = Resources.TryLoadSprite("FileShader"), };
                public static FileType TextFile = new() { Icon = Resources.TryLoadSprite("FileText"), };
                public static FileType ModelFile = new() { Icon = Resources.TryLoadSprite("FileModel"), };
                public static FileType ImageFile = new() { Icon = Resources.TryLoadSprite("FileImage"), };
            }
            public class FileView : Selectable, IPointerClickHandler, IBeginDragHandler, ICustomTransformer {
                public class FileAnimator : ICustomTransformer {
                    public DateTime BeginTime;
                    public TimeSpan TimeSince => DateTime.UtcNow - BeginTime;
                    public bool IsComplete => TimeSince >= TimeSpan.FromSeconds(1f);
                    public void Begin() {
                        BeginTime = DateTime.UtcNow;
                    }
                    public void Apply(CanvasRenderable renderable, ref TransformerContext context) {
                        var scale = Easing.PowerOut(1f).Evaluate((float)TimeSince.TotalSeconds);
                        context.Layout = context.Layout.Scale(scale);
                        context.IsComplete = IsComplete;
                    }
                }
                public readonly string Filename;
                public readonly FileType Type;
                public Image Icon;
                public TextBlock Text;
                private FileAnimator animator;
                public FileView(string filename, FileType type) {
                    Filename = filename;
                    Type = type;
                    Icon = new(type.Icon) {
                        BlendMode = CanvasBlending.BlendModes.Overlay,
                    };
                    Text = new(Path.GetFileName(filename)) {
                        FontSize = 10,
                        TextColor = Color.Black,
                        DisplayParameters = TextDisplayParameters.Flat,
                    };
                    Icon.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 0f, 1f, 1f).WithOffsets(10f, 0f, -10f, -32f));
                    Text.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 1f, 1f, 1f).WithOffsets(0f, -32f, 0f, 0f));
                    AppendChild(Icon);
                    AppendChild(Text);
                }
                public override void OnSelected(ISelectionGroup group, bool _selected) {
                    base.OnSelected(group, _selected);
                    Icon.Color = IsSelected ? new Color(0xffaaaaaa) : Color.Gray;
                    Text.TextColor = IsSelected ? Color.Orange : Color.Black;
                    MarkComposeDirty();
                }
                public void OnPointerClick(PointerEvent events) {
                    base.OnPointerDown(events);
                    int clickCount = events.System.DoubleClick.NotifyClick(this);
                    if (clickCount == 2) {
                        var attrib = File.GetAttributes(Filename);
                        if (attrib.HasFlag(FileAttributes.Directory))
                            FindParent<UIProjectView>()?.SetContentPath(Filename);
                        else
                            LaunchFile();
                    }
                }
                public void OnBeginDrag(PointerEvent events) {
                    if (!events.GetIsButtonDown(0)) { events.Yield(); return; }
                    events.System.DragDropManager.BeginDrag(events, this);
                    //if (animator == null) animator = new();
                    animator?.Begin();
                    MarkTransformDirty();
                }

                private void LaunchFile() {
                    Process process = new Process();
                    process.StartInfo.FileName = "explorer";
                    process.StartInfo.Arguments = Path.GetFullPath(Filename);
                    process.Start();
                }

                public void Apply(CanvasRenderable renderable, ref TransformerContext context) {
                    if (animator != null) animator.Apply(renderable, ref context);
                    else context.IsComplete = true;
                }

                public override string ToString() { return Filename; }
                public override Vector2 GetDesiredSize(SizingParameters sizing) {
                    return new Vector2(80f);
                }

                public AssetReference GetAsset() {
                    return FindParent<UIProjectView>().Editor.AssetDatabase.RequireMetadataByPath(Filename);
                }
            }

            ScrollView scrollView = new() { ScrollMask = new Vector2(0f, 1f), Margins = new RectF(-5f, -5f, 10f, 10), };
            FixedGridLayout filesGrid = new() { CellCount = new Int2(0, 0), };
            public string ContentPath { get; private set; } = "";
            public FileGrid() {
                scrollView.AppendChild(filesGrid);
                AppendChild(scrollView);
            }
            public void SetContentPath(string path) {
                ContentPath = path;
                filesGrid.ClearChildren();
                foreach (var folder in Directory.EnumerateDirectories(ContentPath)) {
                    filesGrid.AppendChild(new FileView(folder, FileType.Folder));
                }
                foreach (var file in Directory.EnumerateFiles(ContentPath)) {
                    filesGrid.AppendChild(new FileView(file,
                        file.EndsWith("hlsl", StringComparison.OrdinalIgnoreCase) ? FileType.ShaderFile :
                        file.EndsWith("txt", StringComparison.OrdinalIgnoreCase) ? FileType.TextFile :
                        file.EndsWith("fbx", StringComparison.OrdinalIgnoreCase) ? FileType.ModelFile :
                        file.EndsWith("png", StringComparison.OrdinalIgnoreCase) ? FileType.ImageFile :
                        file.EndsWith("jpg", StringComparison.OrdinalIgnoreCase) ? FileType.ImageFile :
                        FileType.File
                    ));
                }
            }
        }

        public readonly Editor Editor;

        FolderList folderList = new();
        FileGrid fileGrid = new();

        public ISelectionGroup SelectionGroup { get; private set; }

        public UIProjectView(Editor editor) : base(editor, "Project") {
            SelectionGroup = editor.ProjectSelection;
            Editor = editor;
            folderList.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 0f, 0f, 1f).WithOffsets(0f, 0f, 150f, 0f));
            fileGrid.SetTransform(CanvasTransform.MakeDefault().WithOffsets(150f, 0f, 0f, 0f));
            folderList.SetRoot("./Assets/");
            AppendChild(folderList);
            AppendChild(fileGrid);
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            SetContentPath("./Assets/");
        }
        private void SetContentPath(string path) {
            folderList.SetContentPath(path);
            fileGrid.SetContentPath(path);
        }
    }
}
