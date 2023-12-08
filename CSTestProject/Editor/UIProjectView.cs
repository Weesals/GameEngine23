using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.UI;

namespace Weesals.Editor {
    public class UIProjectView : TabbedWindow {
        public class FolderList : CanvasRenderable, ISelectionGroup {
            public class FolderView : Selectable {
                public readonly FolderView ParentFolder;
                public readonly string Filename;
                public TextBlock Text;
                public FolderView(string filename, string name, FolderView parentFolder = null) {
                    Filename = filename;
                    ParentFolder = parentFolder;
                    Text = new(name) {
                        FontSize = 14,
                        Alignment = TextAlignment.Left,
                    };
                    AppendChild(Text);
                    var depth = 0;
                    for (var p = ParentFolder; p != null; p = p.ParentFolder) ++depth;
                    SetTransform(CanvasTransform.MakeDefault().WithOffsets(5f + 5f * depth, 0f, 0f, 0f));
                }
                public override void OnSelected(bool _selected) {
                    base.OnSelected(_selected);
                    if (IsSelected) {
                        FindParent<UIProjectView>()?.SetContentPath(Filename);
                    }
                    Text.Color = IsSelected ? Color.Yellow : Color.White;
                }
                public override Vector2 GetDesiredSize(SizingParameters sizing) {
                    return new Vector2(100f, 20f);
                }
            }

            FolderView? selectedFolder;
            ScrollView scrollView = new() { ScrollMask = new Vector2(0f, 1f), };
            ListLayout folderList = new() { Axis = ListLayout.Axes.Vertical, };
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
            public void SetSelected(ISelectable selectable) {
                if (selectedFolder == selectable) return;
                if (selectedFolder != null) selectedFolder.OnSelected(false);
                selectedFolder = selectable as FolderView;
                if (selectedFolder != null) selectedFolder.OnSelected(true);
            }
        }
        public class FileGrid : CanvasRenderable {
            public class FileType {
                public Sprite? Icon;
                public static FileType Folder = new() { Icon = Resources.TryLoadSprite("FolderIcon"), };
                public static FileType File = new() { Icon = Resources.TryLoadSprite("FileIcon"), };
            }
            public class FileView : Selectable, IPointerClickHandler, IBeginDragHandler {
                public readonly string Filename;
                public readonly FileType Type;
                public Image Icon;
                public TextBlock Text;
                public FileView(string filename, FileType type) {
                    Filename = filename;
                    Type = type;
                    Icon = new(type.Icon) {
                        BlendMode = CanvasBlending.BlendModes.Overlay,
                    };
                    Text = new(Path.GetFileName(filename)) {
                        FontSize = 10,
                        DisplayParameters = TextDisplayParameters.Flat,
                        Color = Color.Black,
                    };
                    Icon.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 0f, 1f, 1f).WithOffsets(10f, 0f, -10f, -32f));
                    Text.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 1f, 1f, 1f).WithOffsets(0f, -32f, 0f, 0f));
                    AppendChild(Icon);
                    AppendChild(Text);
                }
                public override void OnSelected(bool _selected) {
                    base.OnSelected(_selected);
                    Icon.Color = IsSelected ? new Color(0xffaaaaaa) : Color.Gray;
                    Text.Color = IsSelected ? Color.Orange : Color.Black;
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
                    events.System.DragDropManager.BeginDrag(events, this);
                }

                private void LaunchFile() {
                    Process process = new Process();
                    process.StartInfo.FileName = "explorer";
                    process.StartInfo.Arguments = Path.GetFullPath(Filename);
                    process.Start();
                }
                public override string ToString() { return Filename; }
            }

            ScrollView scrollView = new() { ScrollMask = new Vector2(0f, 1f), Margins = new RectF(-5f, -5f, 10f, 10), };
            GridLayout filesGrid = new() { CellCount = new Int2(0, 0), };
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
                    filesGrid.AppendChild(new FileView(file, FileType.File));
                }
            }
        }

        FolderList folderList = new();
        FileGrid fileGrid = new();
        public UIProjectView(Editor editor) : base(editor, "Project") {
            folderList.SetTransform(CanvasTransform.MakeDefault().WithAnchors(0f, 0f, 0f, 1f).WithOffsets(0f, 0f, 200f, 0f));
            fileGrid.SetTransform(CanvasTransform.MakeDefault().WithOffsets(200f, 0f, 0f, 0f));
            folderList.SetRoot("./assets/");
            AppendChild(folderList);
            AppendChild(fileGrid);
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            SetContentPath("./assets/");
        }
        private void SetContentPath(string path) {
            folderList.SetContentPath(path);
            fileGrid.SetContentPath(path);
        }
    }
}
