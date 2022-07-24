using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace dnpatch
{
    public static partial class InjectHelper
    {
        /// <summary>The stack of contexts that are parents to the current context.</summary>
		private static Stack<IImmutableDictionary<(ModuleDef SourceModule, ModuleDef TargetModule), InjectContext>>
            _parentMaps =
                new Stack<IImmutableDictionary<(ModuleDef SourceModule, ModuleDef TargetModule), InjectContext>>();

        /// <summary>The current context storage. One context for each pair of source and target module.</summary>
        private static IImmutableDictionary<(ModuleDef SourceModule, ModuleDef TargetModule), InjectContext> _contextMap
            =
            ImmutableDictionary.Create<(ModuleDef SourceModule, ModuleDef TargetModule), InjectContext>();

        private static InjectContext GetOrCreateContext(ModuleDef sourceModule, ModuleDef targetModule)
        {
            Debug.Assert(sourceModule != null, $"{nameof(sourceModule)} != null");
            Debug.Assert(targetModule != null, $"{nameof(targetModule)} != null");

            var key = (sourceModule, targetModule);
            // Check if the current map has a context for the two modules.
            if (!_contextMap.TryGetValue(key, out var context))
            {
                // Check if there is an known context on the parent map.
                if (!_parentMaps.Any() || !_parentMaps.Peek().TryGetValue(key, out context))
                {
                    // Also the parent context knows nothing about this context. So there really is known.
                    context = new InjectContext(sourceModule, targetModule);
                }
                else
                {
                    // We got a context on the parent. This means we need to create a child context that covers all
                    // injects for the current injection block.
                    context = new InjectContext(context);
                }

                _contextMap = _contextMap.Add(key, context);
            }

            return context;
        }

        /// <summary>
        ///    Create a new child injection context.
        /// </summary>
        /// <returns>The disposable used to release the context again.</returns>
        /// <remarks>
        ///     <para>
        ///         The newly created child context knows about all injected members that were inject by
        ///         the context that is active up to this point. How ever once the context is released again,
        ///         the information about the injected types is gone.
        ///     </para>
        ///     <para>
        ///         This is required, because the injection system will not inject any member twice, in case
        ///         it is already present in the currently active injection context. In case a single member
        ///         needs to be imported twice, the imports need to happen in different contexts.
        ///     </para>
        ///     <para>
        ///         It is possible to stack the child contexts as required. A new child context will always
        ///         know all injected members of every parent index. So if one method needs to be injected
        ///         multiple times, but reference a single instance of another method, it is possible to
        ///         inject the method that is only injected once first and inject the method that needs to
        ///         be around multiple times with child contexts.
        ///     </para>
        ///     <para>If the returned disposable is not properly disposed, ConfuserEx will leak memory.</para>
        /// </remarks>
        /// <example>
        ///     Injecting twice without child context.
        ///     <code>
        ///     var injectResult1 = InjectionHelper.Inject(sourceMember, targetModule, behavior);
        ///     var injectResult2 = InjectionHelper.Inject(sourceMember, targetModule, behavior);
        ///     Debug.Assert(injectResult1.Requested.Mapped == injectResult2.Requested.Mapped);
        ///     </code>
        ///     <para />
        ///     Injecting twice with child context.
        ///     <code>
        ///     InjectResult&lt;MethodDef&gt; injectResult1;
        ///     InjectResult&lt;MethodDef&gt; injectResult2;
        ///     using (InjectionHelper.CreateChildContext()) {
        ///         injectResult1 = InjectionHelper.Inject(sourceMember, targetModule, behavior);
        ///     }
        ///     using (InjectionHelper.CreateChildContext()) {
        ///         injectResult2 = InjectionHelper.Inject(sourceMember, targetModule, behavior);
        ///     }
        ///     Debug.Assert(injectResult1.Requested.Mapped != injectResult2.Requested.Mapped);
        ///     </code>
        /// </example>
        public static IDisposable CreateChildContext()
        {
            var parentMap = _contextMap;
            if (_parentMaps.Any())
            {
                var oldParentMap = _parentMaps.Peek();
                if (parentMap.Any())
                {
                    foreach (var kvp in oldParentMap)
                    {
                        if (!parentMap.ContainsKey(kvp.Key))
                            parentMap = parentMap.Add(kvp.Key, kvp.Value);
                    }
                }
                else
                {
                    parentMap = oldParentMap;
                }
            }

            _parentMaps.Push(parentMap);
            _contextMap = _contextMap.Clear();

            return new ChildContextRelease(ReleaseChildContext);
        }

        private static void ReleaseChildContext()
        {
            if (!_parentMaps.Any())
                throw new InvalidOperationException("There is not child context to release. Disposed twice?!");

            _contextMap = _parentMaps.Pop();
        }

        /// <summary>
		///     Inject a method into the target module.
		/// </summary>
		/// <param name="methodDef">The method to be injected.</param>
		/// <param name="target">The target module.</param>
		/// <param name="behavior">The behavior that is used to modify the injected members.</param>
		/// <param name="methodInjectProcessors">
		///     Any additional method code processors that are required to inject this and any dependency
		///     method.
		/// </param>
		/// <remarks>
		///     <para>Static methods are automatically added to the global type.</para>
		///     <para>Instance methods are injected along with the type.</para>
		/// </remarks>
		/// <returns>The result of the injection that contains the mapping of all injected members.</returns>
		/// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
		public static InjectResult<MethodDef> Inject(MethodDef methodDef,
            ModuleDef target,
            IInjectBehavior behavior)
        {
            if (methodDef == null) throw new ArgumentNullException(nameof(methodDef));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));

            var ctx = GetOrCreateContext(methodDef.Module, target);
            if (methodDef.IsStatic)
                ctx.ApplyMapping(methodDef.DeclaringType, target.GlobalType);
            var injector = new Injector(ctx, behavior);
            behavior.OriginModule = ctx.OriginModule;
            behavior.TargetModule = ctx.TargetModule;

            var mappedMethod = injector.Inject(methodDef);
            return InjectResult.Create(methodDef, mappedMethod,
                injector.InjectedMembers.Where(m => m.Value != mappedMethod));
        }

        /// <summary>
        ///     Inject a type into the target module.
        /// </summary>
        /// <param name="typeDef">The type to be injected.</param>
        /// <param name="target">The target module.</param>
        /// <param name="behavior">The behavior that is used to modify the injected members.</param>
        /// <param name="methodInjectProcessors">
        ///     Any additional method code processors that are required to inject this and any dependency
        ///     method.
        /// </param>
        /// <returns>The result of the injection that contains the mapping of all injected members.</returns>
        /// <exception cref="ArgumentNullException">Any parameter is <see langword="null"/>.</exception>
        public static InjectResult<TypeDef> Inject(TypeDef typeDef,
            ModuleDef target,
            IInjectBehavior behavior)
        {
            if (typeDef == null) throw new ArgumentNullException(nameof(typeDef));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));

            var ctx = GetOrCreateContext(typeDef.Module, target);
            var injector = new Injector(ctx, behavior);
            behavior.OriginModule = ctx.OriginModule;
            behavior.TargetModule = ctx.TargetModule;

            var mappedType = injector.Inject(typeDef);
            return InjectResult.Create(typeDef, mappedType, injector.InjectedMembers.Where(m => m.Value != mappedType));
        }

        private sealed class ChildContextRelease : IDisposable
        {
            private readonly Action _releaseAction;
            private bool _disposed = false;

            internal ChildContextRelease(Action releaseAction)
            {
                Debug.Assert(releaseAction != null, $"{nameof(releaseAction)} != null");

                _releaseAction = releaseAction;
            }

            void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        _releaseAction.Invoke();
                    }

                    _disposed = true;
                }
            }

            void IDisposable.Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }

    }
}