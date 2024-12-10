using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public class StyleDictionary {

        public delegate ResolvedStyle Resolver(ResolvedStyle parent);

        public struct ResolvedStyle {
            public Color Background;
            public Color Foreground;
        }
        public struct StyleEntry {
            public int ParentStyleId;
            public int ReferenceCount;
            public bool IsResolved;
            public ResolvedStyle CachedStyle;
            public Resolver Resolver;
        }

        private SparseArray<StyleEntry> styles = new();

        public StyleDictionary() {
            styles.Add(new() {
                ParentStyleId = -1,
                ReferenceCount = 1,
                IsResolved = true,
                CachedStyle = new ResolvedStyle() { Background = Color.Black, Foreground = Color.White, },
            });
        }

        public void SetRootStyle(ResolvedStyle style) {
            styles[0].CachedStyle = style;
        }

        public int InheritStyle(int styleId, Resolver resolver) {
            styles[styleId].ReferenceCount++;
            if (resolver == null) return styleId;
            for (var en = styles.GetIndexEnumerator(); en.MoveNext();) {
                var style = styles[en.Current];
                if (style.ParentStyleId == styleId && style.Resolver == resolver) {
                    style.ReferenceCount++;
                    return en.Current;
                }
            }
            return styles.Add(new StyleEntry() {
                ParentStyleId = styleId,
                ReferenceCount = 1,
                Resolver = resolver,
            });
        }
        public void ReleaseStyle(int styleId) {
            if (--styles[styleId].ReferenceCount == 0) {
                styles.Return(styleId);
            }
        }

        public void MarkDirty(int styleId) {
            if (!styles[styleId].IsResolved) return;
            styles[styleId].IsResolved = false;
            for (var en = styles.GetIndexEnumerator(); en.MoveNext();) {
                if (styles[en.Current].ParentStyleId == styleId) MarkDirty(en.Current);
            }
        }
        public ResolvedStyle RequireStyle(int styleId) {
            ref var styleEntry = ref styles[styleId];
            if (!styleEntry.IsResolved) {
                var parentStyle = RequireStyle(styleEntry.ParentStyleId);
                styleEntry.CachedStyle = styleEntry.Resolver(parentStyle);
            }
            return styleEntry.CachedStyle;
        }

    }
}
