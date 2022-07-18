using System;
using System.Collections.Immutable;
using System.Diagnostics;
using dnlib.DotNet;

namespace dnpatch
{
    public static partial class InjectHelper
    {
        /// <summary>
        ///     The inject context is used to store what definitions were injected from one module into another.
        /// </summary>
        private sealed class InjectContext
        {
            /// <summary>
            ///     The mapping of origin definitions to injected definitions.
            /// </summary>
            private IImmutableDictionary<IMemberDef, IMemberDef> _map;

            /// <summary>
            ///     The module which source type originated from.
            /// </summary>
            internal ModuleDef OriginModule { get; }

            /// <summary>
            ///     The module which source type is being injected to.
            /// </summary>
            internal ModuleDef TargetModule { get; }

            /// <summary>
            ///     Initializes a new child instance of the <see cref="InjectContext"/>. It inherits the mapping table
            ///     of the original context. How ever it does not alter the parent context.
            /// </summary>
            /// <param name="parentContext">The parent context that feeds the initial data.</param>
            internal InjectContext(InjectContext parentContext)
            {
                Debug.Assert(parentContext != null, $"{nameof(parentContext)} != null");

                OriginModule = parentContext.OriginModule;
                TargetModule = parentContext.TargetModule;
                _map = parentContext._map;
            }

            /// <summary>
            ///     Initializes a new instance of the <see cref="InjectContext" /> class.
            /// </summary>
            /// <param name="module">The origin module.</param>
            /// <param name="target">The target module.</param>
            internal InjectContext(ModuleDef module, ModuleDef target)
            {
                OriginModule = module ?? throw new ArgumentNullException(nameof(module));
                TargetModule = target ?? throw new ArgumentNullException(nameof(target));

                _map = ImmutableDictionary.Create<IMemberDef, IMemberDef>();
            }

            internal void ApplyMapping(IMemberDef source, IMemberDef target)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (target == null) throw new ArgumentNullException(nameof(target));

                Debug.Assert(!_map.ContainsKey(source) || _map[source] == target,
                    "Overwritten existing mapping");
                _map = _map.SetItem(source, target);
            }

            internal TDef ResolveMapped<TDef>(TDef def) where TDef : class, IMemberDef
            {
                if (_map.TryGetValue(def, out var mappedDef) && mappedDef is TDef resultDef)
                    return resultDef;
                return null;
            }
        }
    }
}