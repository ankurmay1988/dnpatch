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

        /// <summary>
        ///     The injector actually does the injecting.
        /// </summary>
        private sealed class Injector : ImportMapper
        {
            private readonly Dictionary<IMemberDef, IMemberDef> _injectedMembers;
            private InjectContext InjectContext { get; }

            private IInjectBehavior InjectBehavior { get; }

            internal IReadOnlyDictionary<IMemberDef, IMemberDef> InjectedMembers => _injectedMembers;

            private Queue<IMemberDef> PendingForInject { get; }

            internal Injector(InjectContext injectContext, IInjectBehavior injectBehavior)
            {
                InjectContext = injectContext ?? throw new ArgumentNullException(nameof(injectContext));
                InjectBehavior = injectBehavior ?? throw new ArgumentNullException(nameof(injectBehavior));
                PendingForInject = new Queue<IMemberDef>();
                _injectedMembers = new Dictionary<IMemberDef, IMemberDef>();
            }

            private TypeDefUser CopyDef(TypeDef source)
            {
                Debug.Assert(source is not null, $"{nameof(source)} is not null");

                if (_injectedMembers.TryGetValue(source, out var importedMember))
                    return (TypeDefUser)importedMember;

                var typeDefUser = new TypeDefUser(source.Namespace, source.Name)
                {
                    Attributes = source.Attributes
                };

                if (source.HasClassLayout)
                    typeDefUser.ClassLayout = new ClassLayoutUser(source.ClassLayout.PackingSize, source.ClassSize);

                CloneGenericParameters(source, typeDefUser);

                _injectedMembers.Add(source, typeDefUser);
                PendingForInject.Enqueue(source);
                if (source.IsDelegate)
                    foreach (var m in source.Methods)
                        PendingForInject.Enqueue(m);

                if (source.IsEnum)
                {
                    // The backing value field of a enum is required.
                    foreach (var valueField in source.Fields.Where(f => !f.IsStatic))
                        PendingForInject.Enqueue(valueField);
                }

                return typeDefUser;
            }

            private MethodDefUser CopyDef(MethodDef source)
            {
                Debug.Assert(source is not null, $"{nameof(source)} is not null");

                if (_injectedMembers.TryGetValue(source, out var importedMember))
                    return (MethodDefUser)importedMember;

                var methodDefUser = new MethodDefUser(source.Name, null, source.ImplAttributes, source.Attributes)
                {
                    Attributes = source.Attributes
                };

                CloneGenericParameters(source, methodDefUser);

                _injectedMembers.Add(source, methodDefUser);
                PendingForInject.Enqueue(source);

                return methodDefUser;
            }

            private FieldDefUser CopyDef(FieldDef source)
            {
                Debug.Assert(source is not null, $"{nameof(source)} is not null");

                if (_injectedMembers.TryGetValue(source, out var importedMember))
                    return (FieldDefUser)importedMember;

                var fieldDefUser = new FieldDefUser(source.Name, null, source.Attributes);

                _injectedMembers.Add(source, fieldDefUser);
                PendingForInject.Enqueue(source);

                return fieldDefUser;
            }

            private EventDefUser CopyDef(EventDef source)
            {
                Debug.Assert(source is not null, $"{nameof(source)} is not null");

                if (_injectedMembers.TryGetValue(source, out var importedMember))
                    return (EventDefUser)importedMember;

                var eventDefUser = new EventDefUser(source.Name, null, source.Attributes);

                _injectedMembers.Add(source, eventDefUser);
                PendingForInject.Enqueue(source);

                return eventDefUser;
            }

            private PropertyDefUser CopyDef(PropertyDef source)
            {
                Debug.Assert(source is not null, $"{nameof(source)} is not null");

                if (_injectedMembers.TryGetValue(source, out var importedMember))
                    return (PropertyDefUser)importedMember;

                var propertyDefUser = new PropertyDefUser(source.Name, null, source.Attributes);

                _injectedMembers.Add(source, propertyDefUser);
                PendingForInject.Enqueue(source);

                return propertyDefUser;
            }

            private static void CloneGenericParameters(ITypeOrMethodDef origin, ITypeOrMethodDef result)
            {
                if (origin.HasGenericParameters)
                    foreach (var genericParam in origin.GenericParameters)
                        result.GenericParameters.Add(new GenericParamUser(genericParam.Number, genericParam.Flags,
                            "-"));
            }

            private IReadOnlyCollection<IMemberDef> InjectRemaining(Importer importer)
            {
                var resultBuilder = ImmutableList.CreateBuilder<IMemberDef>();

                while (PendingForInject.Count > 0)
                {
                    var memberDef = PendingForInject.Dequeue();
                    if (memberDef is TypeDef typeDef)
                        resultBuilder.Add(InjectTypeDef(typeDef, importer));
                    else if (memberDef is MethodDef methodDef)
                        resultBuilder.Add(InjectMethodDef(methodDef, importer));
                    else if (memberDef is FieldDef fieldDef)
                        resultBuilder.Add(InjectFieldDef(fieldDef, importer));
                    else if (memberDef is EventDef eventDef)
                        resultBuilder.Add(InjectEventDef(eventDef, importer));
                    else if (memberDef is PropertyDef propertyDef)
                        resultBuilder.Add(InjectPropertyDef(propertyDef, importer));
                    else
                        Debug.Fail("Unexpected member in remaining import list:" + memberDef.GetType().Name);
                }

                return resultBuilder.ToImmutable();
            }

            internal MethodDef Inject(MethodDef methodDef)
            {
                var existingMappedMethodDef = InjectContext.ResolveMapped(methodDef);
                if (existingMappedMethodDef is not null) return existingMappedMethodDef;

                var importer = new Importer(InjectContext.TargetModule, ImporterOptions.TryToUseDefs,
                    new GenericParamContext(), this);
                var result = InjectMethodDef(methodDef, importer);
                InjectRemaining(importer);
                return result;
            }

            internal TypeDef Inject(TypeDef typeDef)
            {
                var existingMappedTypeDef = InjectContext.ResolveMapped(typeDef);
                if (existingMappedTypeDef is not null) return existingMappedTypeDef;

                var importer = new Importer(InjectContext.TargetModule, ImporterOptions.TryToUseDefs,
                    new GenericParamContext(), this);
                var result = InjectTypeDef(typeDef, importer);
                foreach (var method in typeDef.Methods) CopyDef(method);
                foreach (var field in typeDef.Fields) CopyDef(field);
                foreach (var @event in typeDef.Events) CopyDef(@event);
                foreach (var prop in typeDef.Properties) CopyDef(prop);
                foreach (var nestedType in typeDef.NestedTypes) Inject(nestedType);

                InjectRemaining(importer);
                return result;
            }

            private TypeDef InjectTypeDef(TypeDef typeDef, Importer importer)
            {
                if (typeDef is null) throw new ArgumentNullException(nameof(typeDef));

                var existingTypeDef = InjectContext.ResolveMapped(typeDef);
                if (existingTypeDef is not null) return existingTypeDef;

                var newTypeDef = CopyDef(typeDef);
                newTypeDef.BaseType = importer.Import(typeDef.BaseType);

                if (typeDef.DeclaringType is not null)
                    newTypeDef.DeclaringType = InjectTypeDef(typeDef.DeclaringType, importer);

                foreach (var iface in typeDef.Interfaces)
                    newTypeDef.Interfaces.Add(InjectInterfaceImpl(iface, importer));

                InjectCustomAttributes(typeDef, newTypeDef, importer);

                InjectBehavior.Process(typeDef, newTypeDef, importer);

                if (!newTypeDef.IsNested)
                    InjectContext.TargetModule.Types.Add(newTypeDef);

                InjectContext.TargetModule.UpdateRowId(newTypeDef);
                InjectContext.ApplyMapping(typeDef, newTypeDef);

                var defaultConstructor = typeDef.FindDefaultConstructor();
                if (defaultConstructor is not null)
                    PendingForInject.Enqueue(defaultConstructor);

                var staticConstructor = typeDef.FindStaticConstructor();
                if (staticConstructor is not null)
                    PendingForInject.Enqueue(staticConstructor);

                return newTypeDef;
            }

            private InterfaceImplUser InjectInterfaceImpl(InterfaceImpl interfaceImpl, Importer importer)
            {
                if (interfaceImpl is null) throw new ArgumentNullException(nameof(interfaceImpl));

                var typeDefOrRef = importer.Import(interfaceImpl.Interface);
                var typeDef = typeDefOrRef.ResolveTypeDefThrow();

                if (typeDef is not null && !typeDef.IsInterface)
                    throw new InvalidOperationException("Type for Interface is not a interface?!");

                var resultImpl = new InterfaceImplUser(typeDefOrRef);
                InjectContext.TargetModule.UpdateRowId(resultImpl);
                return resultImpl;
            }

            private static void InjectCustomAttributes(IHasCustomAttribute source, IHasCustomAttribute target, Importer importer)
            {
                foreach (var ca in source.CustomAttributes)
                {
                    // Nobody needs to know about suppressed messages in the runtime code!
                    if (ca.TypeFullName.Equals(typeof(SuppressMessageAttribute).FullName, StringComparison.Ordinal))
                        continue;

                    target.CustomAttributes.Add(InjectCustomAttribute(ca, importer));
                }
            }

            private static CustomAttribute InjectCustomAttribute(CustomAttribute attribute, Importer importer)
            {
                Debug.Assert(attribute is not null, $"{nameof(attribute)} is not null");

                var result = new CustomAttribute((ICustomAttributeType)importer.Import(attribute.Constructor));
                foreach (var arg in attribute.ConstructorArguments)
                    result.ConstructorArguments.Add(new CAArgument(importer.Import(arg.Type), arg.Value));

                foreach (var arg in attribute.NamedArguments)
                    result.NamedArguments.Add(
                        new CANamedArgument(arg.IsField, importer.Import(arg.Type), arg.Name,
                            new CAArgument(importer.Import(arg.Argument.Type), arg.Argument.Value)));

                return result;
            }

            private FieldDef InjectFieldDef(FieldDef fieldDef, Importer importer)
            {
                Debug.Assert(fieldDef is not null, $"{nameof(fieldDef)} is not null");

                var existingFieldDef = InjectContext.ResolveMapped(fieldDef);
                if (existingFieldDef is not null) return existingFieldDef;

                var newFieldDef = CopyDef(fieldDef);
                newFieldDef.Signature = importer.Import(fieldDef.Signature);
                newFieldDef.DeclaringType = (TypeDef)importer.Import(fieldDef.DeclaringType);
                newFieldDef.InitialValue = fieldDef.InitialValue;

                if (newFieldDef.HasFieldRVA)
                    newFieldDef.RVA = fieldDef.RVA;

                InjectCustomAttributes(fieldDef, newFieldDef, importer);

                InjectBehavior.Process(fieldDef, newFieldDef, importer);
                InjectContext.TargetModule.UpdateRowId(newFieldDef);
                InjectContext.ApplyMapping(fieldDef, newFieldDef);

                return newFieldDef;
            }

            private MethodDef InjectMethodDef(MethodDef methodDef, Importer importer)
            {
                Debug.Assert(methodDef is not null, $"{nameof(methodDef)} is not null");

                var existingMethodDef = InjectContext.ResolveMapped(methodDef);
                if (existingMethodDef is not null) return existingMethodDef;

                var newMethodDef = CopyDef(methodDef);
                newMethodDef.DeclaringType = (TypeDef)importer.Import(methodDef.DeclaringType);
                newMethodDef.Signature = importer.Import(methodDef.Signature);
                newMethodDef.Parameters.UpdateParameterTypes();

                foreach (var paramDef in methodDef.ParamDefs)
                    newMethodDef.ParamDefs.Add(new ParamDefUser(paramDef.Name, paramDef.Sequence, paramDef.Attributes));

                if (methodDef.ImplMap is not null)
                    newMethodDef.ImplMap =
                        new ImplMapUser(new ModuleRefUser(InjectContext.TargetModule, methodDef.ImplMap.Module.Name),
                            methodDef.ImplMap.Name, methodDef.ImplMap.Attributes);

                InjectCustomAttributes(methodDef, newMethodDef, importer);

                if (methodDef.HasBody)
                {
                    methodDef.Body.SimplifyBranches();
                    methodDef.Body.SimplifyMacros(methodDef.Parameters);

                    newMethodDef.Body = new CilBody(methodDef.Body.InitLocals, new List<Instruction>(),
                        new List<ExceptionHandler>(), new List<Local>());
                    newMethodDef.Body.MaxStack = methodDef.Body.MaxStack;

                    var bodyMap = new Dictionary<object, object>();

                    foreach (var local in methodDef.Body.Variables)
                    {
                        var newLocal = new Local(importer.Import(local.Type));
                        newMethodDef.Body.Variables.Add(newLocal);
                        newLocal.Name = local.Name;

                        bodyMap[local] = newLocal;
                    }

                    foreach (var instr in methodDef.Body.Instructions)
                    {
                        var newInstr = new Instruction(instr.OpCode, instr.Operand)
                        {
                            SequencePoint = instr.SequencePoint
                        };

                        newMethodDef.Body.Instructions.Add(newInstr);
                        bodyMap[instr] = newInstr;
                    }

                    foreach (var instr in newMethodDef.Body.Instructions)
                    {
                        if (instr.Operand is not null && bodyMap.ContainsKey(instr.Operand))
                            instr.Operand = bodyMap[instr.Operand];

                        else if (instr.Operand is Instruction[] instructionArrayOp)
                            instr.Operand = (instructionArrayOp).Select(target => (Instruction)bodyMap[target])
                                .ToArray();
                    }

                    foreach (var eh in methodDef.Body.ExceptionHandlers)
                        newMethodDef.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType)
                        {
                            CatchType = eh.CatchType is null ? null : importer.Import(eh.CatchType),
                            TryStart = (Instruction)bodyMap[eh.TryStart],
                            TryEnd = (Instruction)bodyMap[eh.TryEnd],
                            HandlerStart = (Instruction)bodyMap[eh.HandlerStart],
                            HandlerEnd = (Instruction)bodyMap[eh.HandlerEnd],
                            FilterStart = eh.FilterStart is null ? null : (Instruction)bodyMap[eh.FilterStart]
                        });
                }

                InjectBehavior.Process(methodDef, newMethodDef, importer);
                InjectContext.TargetModule.UpdateRowId(newMethodDef);
                InjectContext.ApplyMapping(methodDef, newMethodDef);

                return newMethodDef;
            }

            private EventDef InjectEventDef(EventDef eventDef, Importer importer)
            {
                Debug.Assert(eventDef is not null, $"{nameof(eventDef)} is not null");

                var existingEventDef = InjectContext.ResolveMapped(eventDef);
                if (existingEventDef is not null) return existingEventDef;

                var newEventDef = CopyDef(eventDef);
                newEventDef.AddMethod = CopyDef(eventDef.AddMethod);
                newEventDef.InvokeMethod = CopyDef(eventDef.InvokeMethod);
                newEventDef.RemoveMethod = CopyDef(eventDef.RemoveMethod);
                if (eventDef.HasOtherMethods)
                {
                    foreach (var otherMethod in eventDef.OtherMethods)
                        newEventDef.OtherMethods.Add(CopyDef(otherMethod));
                }

                newEventDef.DeclaringType = (TypeDef)importer.Import(eventDef.DeclaringType);

                InjectCustomAttributes(eventDef, newEventDef, importer);

                InjectBehavior.Process(eventDef, newEventDef, importer);
                InjectContext.TargetModule.UpdateRowId(newEventDef);
                InjectContext.ApplyMapping(eventDef, newEventDef);

                return newEventDef;
            }

            private PropertyDef InjectPropertyDef(PropertyDef propertyDef, Importer importer)
            {
                Debug.Assert(propertyDef is not null, $"{nameof(propertyDef)} is not null");

                var existingPropertyDef = InjectContext.ResolveMapped(propertyDef);
                if (existingPropertyDef is not null) return existingPropertyDef;

                var newPropertyDef = CopyDef(propertyDef);
                foreach (var getMethod in propertyDef.GetMethods)
                    newPropertyDef.GetMethods.Add(CopyDef(getMethod));
                foreach (var setMethod in propertyDef.SetMethods)
                    newPropertyDef.SetMethods.Add(CopyDef(setMethod));

                if (propertyDef.HasOtherMethods)
                {
                    foreach (var otherMethod in propertyDef.OtherMethods)
                        newPropertyDef.OtherMethods.Add(CopyDef(otherMethod));
                }

                newPropertyDef.DeclaringType = (TypeDef)importer.Import(propertyDef.DeclaringType);

                InjectCustomAttributes(propertyDef, newPropertyDef, importer);

                InjectBehavior.Process(propertyDef, newPropertyDef, importer);
                InjectContext.TargetModule.UpdateRowId(newPropertyDef);
                InjectContext.ApplyMapping(propertyDef, newPropertyDef);

                return newPropertyDef;
            }

            #region ImportMapper

            public override ITypeDefOrRef Map(ITypeDefOrRef typeDefOrRef)
            {
                if (typeDefOrRef is TypeDef typeDef)
                {
                    var mappedType = InjectContext.ResolveMapped(typeDef);
                    if (mappedType is not null) return mappedType;

                    if (typeDef.Module == InjectContext.OriginModule)
                        return CopyDef(typeDef);
                }

                // check if the assembly reference needs to be fixed.
                if (typeDefOrRef is TypeRef sourceRef)
                {
                    var targetAssemblyRef = InjectContext.TargetModule.GetAssemblyRef(sourceRef.DefinitionAssembly.Name);
                    if (!(targetAssemblyRef is null) && !string.Equals(targetAssemblyRef.FullName, typeDefOrRef.DefinitionAssembly.FullName, StringComparison.Ordinal))
                    {
                        // We got a matching assembly by the simple name, but not by the full name.
                        // This means the injected code uses a different assembly version than the target assembly.
                        // We'll fix the assembly reference, to avoid breaking anything.
                        return new TypeRefUser(sourceRef.Module, sourceRef.Namespace, sourceRef.Name, targetAssemblyRef);
                    }
                }

                return base.Map(typeDefOrRef);
            }

            public override IMethod Map(MethodDef methodDef)
            {
                var mappedMethod = InjectContext.ResolveMapped(methodDef);
                if (mappedMethod is not null) return mappedMethod;

                if (methodDef.Module == InjectContext.OriginModule)
                    return CopyDef(methodDef);
                return base.Map(methodDef);
            }

            public override IField Map(FieldDef fieldDef)
            {
                var mappedField = InjectContext.ResolveMapped(fieldDef);
                if (mappedField is not null) return mappedField;

                if (fieldDef.Module == InjectContext.OriginModule)
                    return CopyDef(fieldDef);
                return base.Map(fieldDef);
            }

            #endregion
        }
    }
}