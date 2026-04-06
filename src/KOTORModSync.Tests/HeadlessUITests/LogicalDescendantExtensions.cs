using System.Collections.Generic;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace KOTORModSync.Tests.HeadlessUITests
{
    internal static class LogicalDescendantExtensions
    {
        internal static IEnumerable<Visual> GetLogicalDescendants(this Visual visual)
        {
            if (!(visual is ILogical logical))
            {
                yield break;
            }

            foreach (Visual descendant in EnumerateLogicalDescendants(logical))
            {
                yield return descendant;
            }
        }

        private static IEnumerable<Visual> EnumerateLogicalDescendants(ILogical logical)
        {
            foreach (ILogical child in logical.LogicalChildren)
            {
                if (child is Visual childVisual)
                {
                    yield return childVisual;
                }

                foreach (Visual descendant in EnumerateLogicalDescendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
