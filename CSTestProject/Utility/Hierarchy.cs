using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.UI;

namespace Weesals.Utility {
    public interface IWithParent {
        object? Parent { get; }
    }
    public interface IWithParent<T> : IWithParent {
        new T? Parent { get; }
        object? IWithParent.Parent => Parent;
    }
    public static class HierarchyExt {
        /// <summary>
        /// Get an interface in the self or if none exists, in a parent
        /// </summary>
        public static bool TryGetRecursive<Self, Type>(this IWithParent<Self> item, out Type component) where Self : IWithParent<Self> {
            for (var p = item; p != null; p = p.Parent) {
                if (p is not Type pvalue) continue;
                component = pvalue;
                return true;
            }
            component = default!;
            return false;
        }
        /// <summary>
        /// Get an interface in a parent, ignoring the self
        /// </summary>
        public static bool TryGetParent<Self, Type>(this IWithParent<Self> item, out Type parent) where Self : IWithParent<Self> {
            for (var p = item.Parent; p != null; p = p.Parent) {
                if (p is not Type pvalue) continue;
                parent = pvalue;
                return true;
            }
            parent = default!;
            return false;
        }


        public static Self? TryGetParent<Self>(this Self p) where Self : class, IWithParent {
            if (p is IWithParent<CanvasRenderable> withParentC) return withParentC.Parent as Self;
            return default;
        }
        public static bool TryGetRecursive<Self, Type>(this Self item, out Type component) where Self : class, IWithParent {
            for (var p = item; p != null; p = p.TryGetParent()) {
                if (p is not Type pvalue) continue;
                component = pvalue;
                return true;
            }
            component = default!;
            return false;
        }

        public static object? TryGetParent(object? item) {
            return item is IWithParent withParent ? withParent.Parent : default;
        }
        public static bool TryGetRecursive<Self>(object? item, out Self result) where Self : class{
            if (item is Self value) { result = value; return true; }
            result = default!;
            return item is IWithParent withParent && TryGetRecursive(withParent.Parent, out result);
        }
        public static Self? TryGetRecursive<Self>(object? item) where Self : IWithParent {
            if (item is Self value) { return value; }
            return item is IWithParent withParent && TryGetRecursive(withParent, out Self result) ? result : default;
        }

    }
}
