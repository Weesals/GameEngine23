using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Utility {
    public interface IWithParent<T> {
        T Parent { get; }
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
            component = default;
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
            parent = default;
            return false;
        }
    }
}
