using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Pdb;
using dnlib.IO;

namespace dnpatch
{
    /// <summary>
    ///     Provides a set of utility methods about dnlib
    /// </summary>
    public static class DnlibUtils
    {
        /// <summary>
        ///     Finds all definitions of interest in a module.
        /// </summary>
        /// <param name="module">The module.</param>
        /// <returns>A collection of all required definitions</returns>
        public static IEnumerable<IDnlibDef> FindDefinitions(this ModuleDef module)
        {
            yield return module;
            foreach (TypeDef type in module.GetTypes())
            {
                yield return type;

                foreach (MethodDef method in type.Methods)
                    yield return method;

                foreach (FieldDef field in type.Fields)
                    yield return field;

                foreach (PropertyDef prop in type.Properties)
                    yield return prop;

                foreach (EventDef evt in type.Events)
                    yield return evt;
            }
        }

        /// <summary>
        ///     Finds all definitions of interest in a type.
        /// </summary>
        /// <param name="typeDef">The type.</param>
        /// <returns>A collection of all required definitions</returns>
        public static IEnumerable<IDnlibDef> FindDefinitions(this TypeDef typeDef)
        {
            yield return typeDef;

            foreach (TypeDef nestedType in typeDef.NestedTypes)
                yield return nestedType;

            foreach (MethodDef method in typeDef.Methods)
                yield return method;

            foreach (FieldDef field in typeDef.Fields)
                yield return field;

            foreach (PropertyDef prop in typeDef.Properties)
                yield return prop;

            foreach (EventDef evt in typeDef.Events)
                yield return evt;
        }

        /// <summary>
        ///     Determines whether the specified method is visible outside the containing assembly.
        /// </summary>
        /// <param name="methodDef">The method that is checked.</param>
        /// <returns><see langword="true"/> in case the method is visible outside of the assembly.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="methodDef"/> is <see langword="null"/>.</exception>
        /// <remarks>
        ///     The method is considered visible in case it is public or visible to sub-types (protected) and the
        ///     declaring type is also visible outside of the assembly.
        /// </remarks>
        public static bool IsVisibleOutside(this MethodDef methodDef)
        {
            if (methodDef == null) throw new ArgumentNullException(nameof(methodDef));

            switch (methodDef.Access)
            {
                case MethodAttributes.Family:
                case MethodAttributes.FamORAssem:
                case MethodAttributes.Public:
                    return methodDef.DeclaringType.IsVisibleOutside();
            }

            return false;
        }

        /// <summary>
        ///     Determines whether the specified field is visible outside the containing assembly.
        /// </summary>
        /// <param name="fieldDef">The field that is checked.</param>
        /// <returns><see langword="true"/> in case the field is visible outside of the assembly.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fieldDef"/> is <see langword="null"/>.</exception>
        /// <remarks>
        ///     The field is considered visible in case it is public or visible to sub-types (protected) and the
        ///     declaring type is also visible outside of the assembly.
        /// </remarks>
        public static bool IsVisibleOutside(this FieldDef fieldDef)
        {
            if (fieldDef == null) throw new ArgumentNullException(nameof(fieldDef));

            switch (fieldDef.Access)
            {
                case FieldAttributes.Family:
                case FieldAttributes.FamORAssem:
                case FieldAttributes.Public:
                    return fieldDef.DeclaringType.IsVisibleOutside();
            }

            return false;
        }

        /// <summary>
        ///     Determines whether the specified event is visible outside the containing assembly.
        /// </summary>
        /// <param name="eventDef">The event that is checked.</param>
        /// <returns><see langword="true"/> in case the event is visible outside of the assembly.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="eventDef"/> is <see langword="null"/>.</exception>
        /// <remarks>
        ///     The event is considered visible in case any of the methods related to it is visible outside.
        /// </remarks>
        public static bool IsVisibleOutside(this EventDef eventDef)
        {
            if (eventDef == null) throw new ArgumentNullException(nameof(eventDef));

            return eventDef.AllMethods().Any(IsVisibleOutside);
        }

        /// <summary>
        ///     Determines whether the specified property is visible outside the containing assembly.
        /// </summary>
        /// <param name="propertyDef">The event that is checked.</param>
        /// <returns><see langword="true"/> in case the property is visible outside of the assembly.</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="propertyDef"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        ///     The property is considered visible in case any of the getter or setter methods is visible outside.
        /// </remarks>
        public static bool IsVisibleOutside(this PropertyDef propertyDef)
        {
            if (propertyDef == null) throw new ArgumentNullException(nameof(propertyDef));

            return propertyDef.GetMethods.Any(IsVisibleOutside) || propertyDef.SetMethods.Any(IsVisibleOutside);
        }

        /// <summary>
        ///     Determines whether the specified type is visible outside the containing assembly.
        /// </summary>
        /// <param name="typeDef">The type.</param>
        /// <param name="exeNonPublic">Visibility of executable modules.</param>
        /// <returns><c>true</c> if the specified type is visible outside the containing assembly; otherwise, <c>false</c>.</returns>
        public static bool IsVisibleOutside(this TypeDef typeDef, bool exeNonPublic = true)
        {
            // Assume executable modules' type is not visible
            if (exeNonPublic &&
                (typeDef.Module.Kind == ModuleKind.Windows || typeDef.Module.Kind == ModuleKind.Console))
                return false;

            do
            {
                if (typeDef.DeclaringType == null)
                    return typeDef.IsPublic;
                if (!typeDef.IsNestedPublic && !typeDef.IsNestedFamily && !typeDef.IsNestedFamilyOrAssembly)
                    return false;
                typeDef = typeDef.DeclaringType;
            } while (typeDef != null);

            throw new UnreachableException();
        }

        /// <summary>
        ///     Determines whether the object has the specified custom attribute.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <param name="fullName">The full name of the type of custom attribute.</param>
        /// <returns><c>true</c> if the specified object has custom attribute; otherwise, <c>false</c>.</returns>
        public static bool HasAttribute(this IHasCustomAttribute obj, string fullName)
        {
            return obj.CustomAttributes.Any(attr => attr.TypeFullName == fullName);
        }

        /// <summary>
        ///     Determines whether the specified type is COM import.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if specified type is COM import; otherwise, <c>false</c>.</returns>
        public static bool IsComImport(this TypeDef type)
        {
            return type.IsImport ||
                   type.HasAttribute("System.Runtime.InteropServices.ComImportAttribute") ||
                   type.HasAttribute("System.Runtime.InteropServices.TypeLibTypeAttribute");
        }

        /// <summary>
        ///     Determines whether the specified type is compiler generated.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if specified type is compiler generated; otherwise, <c>false</c>.</returns>
        public static bool IsCompilerGenerated(this TypeDef type)
        {
            return type.HasAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute");
        }

        /// <summary>
        ///     Determines whether the specified type is a delegate.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns><c>true</c> if the specified type is a delegate; otherwise, <c>false</c>.</returns>
        public static bool IsDelegate(this TypeDef type)
        {
            if (type.BaseType == null)
                return false;

            string fullName = type.BaseType.FullName;
            return fullName == "System.Delegate" || fullName == "System.MulticastDelegate";
        }

        /// <summary>
        ///     Determines whether the specified type is inherited from a base type in corlib.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="baseType">The full name of base type.</param>
        /// <returns><c>true</c> if the specified type is inherited from a base type; otherwise, <c>false</c>.</returns>
        public static bool InheritsFromCorlib(this TypeDef type, string baseType)
        {
            if (type.BaseType == null)
                return false;

            TypeDef bas = type;
            do
            {
                bas = bas.BaseType.ResolveTypeDefThrow();
                if (bas.ReflectionFullName == baseType)
                    return true;
            } while (bas.BaseType != null && bas.BaseType.DefinitionAssembly.IsCorLib());

            return false;
        }

        /// <summary>
        ///     Determines whether the specified type is inherited from a base type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="baseType">The full name of base type.</param>
        /// <returns><c>true</c> if the specified type is inherited from a base type; otherwise, <c>false</c>.</returns>
        public static bool InheritsFrom(this TypeDef type, string baseType)
        {
            if (type.BaseType == null)
                return false;

            TypeDef bas = type;
            do
            {
                bas = bas.BaseType.ResolveTypeDefThrow();
                if (bas.ReflectionFullName == baseType)
                    return true;
            } while (bas.BaseType != null);

            return false;
        }

        /// <summary>
        ///     Determines whether the specified type implements the specified interface.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="fullName">The full name of the type of interface.</param>
        /// <returns><c>true</c> if the specified type implements the interface; otherwise, <c>false</c>.</returns>
        public static bool Implements(this TypeDef type, string fullName)
        {
            do
            {
                foreach (InterfaceImpl iface in type.Interfaces)
                {
                    if (iface.Interface.ReflectionFullName == fullName)
                        return true;
                }

                if (type.BaseType == null)
                    return false;

                type = type.BaseType.ResolveTypeDefThrow();
            } while (type != null);

            throw new UnreachableException();
        }

        /// <summary>
        ///     Resolves the method.
        /// </summary>
        /// <param name="method">The method to resolve.</param>
        /// <returns>A <see cref="MethodDef" /> instance.</returns>
        /// <exception cref="MemberRefResolveException">The method couldn't be resolved.</exception>
        public static MethodDef ResolveThrow(this IMethod method)
        {
            var def = method as MethodDef;
            if (def != null)
                return def;

            var spec = method as MethodSpec;
            if (spec != null)
                return spec.Method.ResolveThrow();

            return ((MemberRef)method).ResolveMethodThrow();
        }

        /// <summary>
        ///     Resolves the field.
        /// </summary>
        /// <param name="field">The field to resolve.</param>
        /// <returns>A <see cref="FieldDef" /> instance.</returns>
        /// <exception cref="MemberRefResolveException">The method couldn't be resolved.</exception>
        public static FieldDef ResolveThrow(this IField field)
        {
            var def = field as FieldDef;
            if (def != null)
                return def;

            return ((MemberRef)field).ResolveFieldThrow();
        }

        /// <summary>
        ///     Find the basic type reference.
        /// </summary>
        /// <param name="typeSig">The type signature to get the basic type.</param>
        /// <returns>A <see cref="ITypeDefOrRef" /> instance, or null if the typeSig cannot be resolved to basic type.</returns>
        public static ITypeDefOrRef ToBasicTypeDefOrRef(this TypeSig typeSig)
        {
            while (typeSig.Next != null)
                typeSig = typeSig.Next;

            if (typeSig is GenericInstSig)
                return ((GenericInstSig)typeSig).GenericType.TypeDefOrRef;
            if (typeSig is TypeDefOrRefSig)
                return ((TypeDefOrRefSig)typeSig).TypeDefOrRef;
            return null;
        }

        /// <summary>
        ///     Find the type references within the specified type signature.
        /// </summary>
        /// <param name="typeSig">The type signature to find the type references.</param>
        /// <returns>A list of <see cref="ITypeDefOrRef" /> instance.</returns>
        public static IList<ITypeDefOrRef> FindTypeRefs(this TypeSig typeSig)
        {
            var ret = new List<ITypeDefOrRef>();
            FindTypeRefsInternal(typeSig, ret);
            return ret;
        }

        static void FindTypeRefsInternal(TypeSig typeSig, IList<ITypeDefOrRef> ret)
        {
            while (typeSig.Next != null)
            {
                if (typeSig is ModifierSig)
                    ret.Add(((ModifierSig)typeSig).Modifier);
                typeSig = typeSig.Next;
            }

            if (typeSig is GenericInstSig)
            {
                var genInst = (GenericInstSig)typeSig;
                ret.Add(genInst.GenericType.TypeDefOrRef);
                foreach (TypeSig genArg in genInst.GenericArguments)
                    FindTypeRefsInternal(genArg, ret);
            }
            else if (typeSig is TypeDefOrRefSig)
            {
                var type = ((TypeDefOrRefSig)typeSig).TypeDefOrRef;
                while (type != null)
                {
                    ret.Add(type);
                    type = type.DeclaringType;
                }
            }
        }

        /// <summary>
        ///     Determines whether the specified property is abstract.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns><see langword="true" /> if the specified property is abstract; otherwise, <see langword="false" /></returns>
        public static bool IsAbstract(this PropertyDef property) =>
            property.AllMethods().Any(method => method.IsAbstract);

        /// <summary>
        ///     Determines whether the specified property is public.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns><c>true</c> if the specified property is public; otherwise, <c>false</c>.</returns>
        public static bool IsPublic(this PropertyDef property)
        {
            return property.AllMethods().Any(method => method.IsPublic);
        }

        /// <summary>
        ///     Determines whether the specified property is family or assembly.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns><c>true</c> if the specified property is family or assembly; otherwise, <c>false</c>.</returns>
        public static bool IsFamilyOrAssembly(this PropertyDef property)
        {
            return property.AllMethods().Any(method => method.IsFamilyOrAssembly);
        }

        /// <summary>
        ///     Determines whether the specified property is family.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns><c>true</c> if the specified property is family; otherwise, <c>false</c>.</returns>
        public static bool IsFamily(this PropertyDef property)
        {
            return property.AllMethods().Any(method => method.IsFamily);
        }

        /// <summary>
        ///     Determines whether the specified property is static.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns><c>true</c> if the specified property is static; otherwise, <c>false</c>.</returns>
        public static bool IsStatic(this PropertyDef property)
        {
            return property.AllMethods().Any(method => method.IsStatic);
        }

        /// <summary>
        ///     Determines whether the specified event is abstract.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><see langword="true" /> if the specified event is abstract; otherwise, <see langword="false" /></returns>
        public static bool IsAbstract(this EventDef evt) =>
            evt.AllMethods().Any(method => method.IsAbstract);

        /// <summary>
        ///     Determines whether the specified event is public.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><c>true</c> if the specified event is public; otherwise, <c>false</c>.</returns>
        public static bool IsPublic(this EventDef evt)
        {
            return evt.AllMethods().Any(method => method.IsPublic);
        }

        /// <summary>
        ///     Determines whether the specified event is family or assembly.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><c>true</c> if the specified property is family or assembly; otherwise, <c>false</c>.</returns>
        public static bool IsFamilyOrAssembly(this EventDef evt)
        {
            return evt.AllMethods().Any(method => method.IsFamilyOrAssembly);
        }

        /// <summary>
        ///     Determines whether the specified event is family.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><c>true</c> if the specified property is family; otherwise, <c>false</c>.</returns>
        public static bool IsFamily(this EventDef evt)
        {
            return evt.AllMethods().Any(method => method.IsFamily);
        }

        /// <summary>
        ///     Determines whether the specified event is static.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><c>true</c> if the specified event is static; otherwise, <c>false</c>.</returns>
        public static bool IsStatic(this EventDef evt)
        {
            return evt.AllMethods().Any(method => method.IsStatic);
        }

        public static bool IsInterfaceImplementation(this MethodDef method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            return IsImplicitImplementedInterfaceMember(method) || IsExplicitlyImplementedInterfaceMember(method);
        }

        /// <summary>
        ///     Determines whether the specified method is an implicitly implemented interface member.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns>
        ///     <see langword="true" /> if the specified method is an implicitly implemented interface member;
        ///     otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null" />.</exception>
        /// <exception cref="TypeResolveException">Failed to resolve required interface types.</exception>
        public static bool IsImplicitImplementedInterfaceMember(this MethodDef method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            if (method.IsPublic && method.IsNewSlot)
            {
                foreach (var iFace in method.DeclaringType.Interfaces)
                {
                    var iFaceDef = iFace.Interface.ResolveTypeDefThrow();
                    if (iFaceDef.FindMethod(method.Name, (MethodSig)method.Signature) != null)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        ///     Determines whether the specified method is an explicitly implemented interface member.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns><c>true</c> if the specified method is an explicitly implemented interface member; otherwise, <c>false</c>.</returns>
        public static bool IsExplicitlyImplementedInterfaceMember(this MethodDef method)
        {
            return method.IsFinal && method.IsPrivate;
        }

        /// <summary>
        ///     Determines whether the specified property is an explicitly implemented interface member.
        /// </summary>
        /// <param name="property">The method.</param>
        /// <returns><c>true</c> if the specified property is an explicitly implemented interface member; otherwise, <c>false</c>.</returns>
        public static bool IsExplicitlyImplementedInterfaceMember(this PropertyDef property)
        {
            return property.AllMethods().Any(IsExplicitlyImplementedInterfaceMember);
        }

        /// <summary>
        ///     Determines whether the specified event is an explicitly implemented interface member.
        /// </summary>
        /// <param name="evt">The event.</param>
        /// <returns><c>true</c> if the specified eve is an explicitly implemented interface member; otherwise, <c>false</c>.</returns>
        public static bool IsExplicitlyImplementedInterfaceMember(this EventDef evt)
        {
            return evt.AllMethods().Any(IsExplicitlyImplementedInterfaceMember);
        }

        public static bool IsOverride(this MethodDef method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (!method.IsVirtual || method.IsPrivate) return false;

            var cmpType = method.DeclaringType.BaseType?.ResolveTypeDefThrow();
            while (cmpType != null)
            {
                if (cmpType.FindMethod(method.Name, method.MethodSig) != null) return true;
                cmpType = cmpType.BaseType?.ResolveTypeDefThrow();
            }

            return false;
        }

        private static IEnumerable<MethodDef> AllMethods(this EventDef evt)
        {
            return new[] { evt.AddMethod, evt.RemoveMethod, evt.InvokeMethod }
                .Concat(evt.OtherMethods)
                .Where(m => m != null);
        }

        private static IEnumerable<MethodDef> AllMethods(this PropertyDef property)
        {
            return new[] { property.GetMethod, property.SetMethod }
                .Concat(property.OtherMethods)
                .Where(m => m != null);
        }

        /// <summary>
        ///     Replaces the specified instruction reference with another instruction.
        /// </summary>
        /// <param name="body">The method body.</param>
        /// <param name="target">The instruction to replace.</param>
        /// <param name="newInstr">The new instruction.</param>
        public static void ReplaceReference(this CilBody body, Instruction target, Instruction newInstr)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (newInstr == null) throw new ArgumentNullException(nameof(newInstr));

            body.UpdateReference(target, newInstr);

            body.FixScopeStarts(target, newInstr);
            body.FixScopeEnds(target, newInstr);

            if (target.SequencePoint != null && newInstr.SequencePoint == null)
            {
                newInstr.SequencePoint = target.SequencePoint;
            }
        }

        private static void UpdateReference(this CilBody body, Instruction oldInstr, Instruction newInstr)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (oldInstr == null) throw new ArgumentNullException(nameof(oldInstr));
            if (newInstr == null) throw new ArgumentNullException(nameof(newInstr));

            foreach (var instr in body.Instructions)
            {
                switch (instr.Operand)
                {
                    case Instruction opInstr:
                        if (oldInstr.Equals(opInstr))
                            instr.Operand = newInstr;
                        break;
                    case Instruction[] opInstrs:
                        for (var i = 0; i < opInstrs.Length; i++)
                            if (oldInstr.Equals(opInstrs[i]))
                                opInstrs[i] = newInstr;

                        break;
                }
            }
        }

        public static void InsertPrefixInstructions(this CilBody body, Instruction instr,
            params Instruction[] newInstrs) =>
            InsertPrefixInstructions(body, instr, newInstrs.AsEnumerable());

        public static void InsertPrefixInstructions(this CilBody body, int index,
            IEnumerable<Instruction> newInstrs)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));

            var instruction = body.Instructions[index];
            InsertPrefixInstructions(body, instruction, newInstrs);
        }

        /// <summary>
        /// Insert instructions before the <paramref name="instr"/> into the <paramref name="body"/>.
        /// </summary>
        /// <param name="body">the method body the instructions will be inserted into</param>
        /// <param name="instr">
        ///     The instruction to insert the instructions before;
        ///     may be <see langword="null" /> to indicate the end of the method.
        /// </param>
        /// <param name="newInstrs">the instructions to insert</param>
        public static void InsertPrefixInstructions(this CilBody body, Instruction instr,
            IEnumerable<Instruction> newInstrs)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (newInstrs == null) throw new ArgumentNullException(nameof(newInstrs));

            if (!body.HasInstructions)
            {
                Debug.Fail("No instructions in method?!");
                return;
            }

            var indexOfInstr = instr is null ? body.Instructions.Count : body.Instructions.IndexOf(instr);
            Debug.Assert(indexOfInstr >= 0, "Instruction not present in method.");
            if (indexOfInstr < 0) return;

            int index = 0;
            foreach (var newInstr in newInstrs)
                body.Instructions.Insert(indexOfInstr + (index++), newInstr);

            if (!(instr is null))
            {
                body.UpdateReference(instr, body.Instructions[indexOfInstr]);
                body.FixScopeStarts(instr, body.Instructions[indexOfInstr]);
            }
            body.FixScopeEnds(instr, body.Instructions[indexOfInstr]);
        }

        private static void FixScopeStarts(this CilBody body, Instruction oldInstr, Instruction newInstr)
        {
            Debug.Assert(body != null, $"{nameof(body)} != null");
            Debug.Assert(oldInstr != null, $"{nameof(oldInstr)} != null");
            Debug.Assert(newInstr != null, $"{nameof(newInstr)} != null");

            foreach (var exHandler in body.ExceptionHandlers)
            {
                if (oldInstr.Equals(exHandler.TryStart))
                    exHandler.TryStart = newInstr;

                if (oldInstr.Equals(exHandler.HandlerStart))
                    exHandler.HandlerStart = newInstr;

                if (oldInstr.Equals(exHandler.FilterStart))
                    exHandler.FilterStart = newInstr;
            }

            foreach (var currentScope in body.GetPdbScopes())
            {
                if (oldInstr.Equals(currentScope.Start))
                    currentScope.Start = newInstr;
            }
        }

        private static void FixScopeEnds(this CilBody body, Instruction oldInstr, Instruction newInstr)
        {
            Debug.Assert(body != null, $"{nameof(body)} != null");

            foreach (var exHandler in body.ExceptionHandlers)
            {
                if (oldInstr is null)
                {
                    if (exHandler.TryEnd is null)
                        exHandler.TryEnd = newInstr;
                    if (exHandler.HandlerEnd is null && !(exHandler.HandlerStart is null))
                        exHandler.HandlerEnd = newInstr;
                }
                else
                {
                    if (oldInstr.Equals(exHandler.TryEnd))
                        exHandler.TryEnd = newInstr;

                    if (oldInstr.Equals(exHandler.HandlerEnd))
                        exHandler.HandlerEnd = newInstr;
                }
            }

            foreach (var currentScope in body.GetPdbScopes())
            {
                if (oldInstr == currentScope.End)
                    currentScope.End = newInstr;
            }
        }

        private static IEnumerable<PdbScope> GetPdbScopes(this CilBody body)
        {
            if (body.HasPdbMethod)
            {
                Debug.Assert(body.PdbMethod != null, $"{nameof(body)}.PdbMethod != null");

                var unprocessedScopes = new Queue<PdbScope>();
                unprocessedScopes.Enqueue(body.PdbMethod.Scope);

                while (unprocessedScopes.Any())
                {
                    var currentScope = unprocessedScopes.Dequeue();
                    yield return currentScope;
                    foreach (var childScope in currentScope.Scopes)
                        unprocessedScopes.Enqueue(childScope);
                }
            }
        }

        public static void RemoveInstruction(this CilBody body, int index)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            RemoveInstruction(body, index, body.Instructions[index]);
        }

        /// <summary>
        /// This method removes an instruction from the body and fixes the references to the removed instruction.
        /// </summary>
        /// <param name="body">The body to process</param>
        /// <param name="instr">The instruction that is removed</param>
        /// <remarks>This method fixes branch instructions, exception handlers and debug symbols.</remarks>
        public static void RemoveInstruction(this CilBody body, Instruction instr)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (instr == null) throw new ArgumentNullException(nameof(instr));

            if (!body.HasInstructions)
            {
                Debug.Fail("No instructions in method?!");
                return;
            }

            var indexOfInstr = body.Instructions.IndexOf(instr);

            RemoveInstruction(body, indexOfInstr, instr);
        }

        private static void RemoveInstruction(CilBody body, int indexOfInstr, Instruction instr)
        {
            Debug.Assert(body != null, $"{nameof(body)} != null");
            Debug.Assert(indexOfInstr >= 0, "Instruction not present in method.");
            Debug.Assert(instr != null, $"{nameof(instr)} != null");

            if (indexOfInstr < 0) return;
            Debug.Assert(body.Instructions.IndexOf(instr) == indexOfInstr, "Instruction and index do not match.");

            if (indexOfInstr < body.Instructions.Count - 1)
            {
                body.UpdateReference(instr, body.Instructions[indexOfInstr + 1]);
                body.FixScopeStarts(instr, body.Instructions[indexOfInstr + 1]);
            }
            else if (indexOfInstr > 0)
            {
                body.UpdateReference(instr, body.Instructions[indexOfInstr - 1]);
                body.FixScopeStarts(instr, body.Instructions[indexOfInstr - 1]);
            }

            // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
            if (indexOfInstr < body.Instructions.Count - 1)
                body.FixScopeEnds(instr, body.Instructions[indexOfInstr + 1]);
            else
                body.FixScopeEnds(instr, null); // Scope End to end of method

            if (indexOfInstr + 1 < body.Instructions.Count)
            {
                if (instr.SequencePoint != null && body.Instructions[indexOfInstr + 1].SequencePoint == null)
                {
                    body.Instructions[indexOfInstr + 1].SequencePoint = instr.SequencePoint;
                }
            }
            else if (indexOfInstr > 0)
            {
                if (instr.SequencePoint != null && body.Instructions[indexOfInstr - 1].SequencePoint == null)
                {
                    body.Instructions[indexOfInstr - 1].SequencePoint = instr.SequencePoint;
                }
            }

            // Any now we have fixed everything and we can finally safely delete the instruction!
            body.Instructions.RemoveAt(indexOfInstr);
        }

        /// <summary>
        ///     Determines whether the specified method is array accessors.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns><c>true</c> if the specified method is array accessors; otherwise, <c>false</c>.</returns>
        public static bool IsArrayAccessors(this IMethod method)
        {
            var declType = method.DeclaringType.ToTypeSig();
            if (declType is GenericInstSig)
                declType = ((GenericInstSig)declType).GenericType;

            if (declType.IsArray)
            {
                return method.Name == "Get" || method.Name == "Set" || method.Name == "Address";
            }

            return false;
        }

        public static bool IsEntryPoint(this MethodDef methodDef)
        {
            if (methodDef == null) throw new ArgumentNullException(nameof(methodDef));

            return methodDef == methodDef.Module.EntryPoint;
        }

        public static bool IsEntryPoint(this TypeDef typeDef)
        {
            if (typeDef == null) throw new ArgumentNullException(nameof(typeDef));

            return typeDef == typeDef.Module.EntryPoint?.DeclaringType;
        }

        /// <summary>
        ///		Merges a specified call instruction into the body.
        /// </summary>
        /// <param name="targetBody">The target body</param>
        /// <param name="callInstruction">The instruction to merge in</param>
        public static void MergeCall(this CilBody targetBody, Instruction callInstruction)
        {
            if (!(callInstruction.Operand is MethodDef methodToMerge))
                throw new ArgumentException("Call instruction has invalid operand");
            if (!methodToMerge.HasBody)
                throw new Exception("Method to merge has no body!");

            var localParams = methodToMerge.Parameters.ToDictionary(param => param.Index, param => new Local(param.Type));
            var localMap = methodToMerge.Body.Variables.ToDictionary(local => local, local => new Local(local.Type));
            foreach (var local in localParams)
                targetBody.Variables.Add(local.Value);
            foreach (var local in localMap)
                targetBody.Variables.Add(local.Value);

            // Nop the call
            int index = targetBody.Instructions.IndexOf(callInstruction) + 1;
            callInstruction.OpCode = OpCodes.Nop;
            callInstruction.Operand = null;
            var afterIndex = targetBody.Instructions[index];

            // Find Exception handler index
            int exIndex = 0;
            foreach (var ex in targetBody.ExceptionHandlers)
            {
                if (targetBody.Instructions.IndexOf(ex.TryStart) < index)
                    exIndex = targetBody.ExceptionHandlers.IndexOf(ex);
            }

            // setup parameter locals
            foreach (var paramLocal in localParams.Reverse())
            {
                targetBody.Instructions.Insert(index++, new Instruction(OpCodes.Stloc, paramLocal.Value));
            }

            var instrMap = new Dictionary<Instruction, Instruction>();
            var newInstrs = new List<Instruction>();

            // Transfer instructions to list
            foreach (var instr in methodToMerge.Body.Instructions)
            {
                Instruction newInstr;
                if (instr.OpCode == OpCodes.Ret)
                {
                    newInstr = new Instruction(OpCodes.Br, afterIndex);
                }
                else if (instr.IsLdarg())
                {
                    localParams.TryGetValue(instr.GetParameterIndex(), out var lc);
                    newInstr = new Instruction(OpCodes.Ldloc, lc);
                }
                else if (instr.IsStarg())
                {
                    localParams.TryGetValue(instr.GetParameterIndex(), out var lc);
                    newInstr = new Instruction(OpCodes.Stloc, lc);
                }
                else if (instr.IsLdloc())
                {
                    localMap.TryGetValue(instr.GetLocal(methodToMerge.Body.Variables), out var lc);
                    newInstr = new Instruction(OpCodes.Ldloc, lc);
                }
                else if (instr.IsStloc())
                {
                    localMap.TryGetValue(instr.GetLocal(methodToMerge.Body.Variables), out var lc);
                    newInstr = new Instruction(OpCodes.Stloc, lc);
                }
                else
                {
                    newInstr = new Instruction(instr.OpCode, instr.Operand);
                }

                newInstrs.Add(newInstr);
                instrMap[instr] = newInstr;
            }

            // Fix branch targets & add instructions
            foreach (var instr in newInstrs)
            {
                if (instr.Operand != null && instr.Operand is Instruction instrOp && instrMap.ContainsKey(instrOp))
                    instr.Operand = instrMap[instrOp];
                else if (instr.Operand is Instruction[] instructionArrayOp)
                    instr.Operand = instructionArrayOp.Select(target => instrMap[target]).ToArray();

                targetBody.Instructions.Insert(index++, instr);
            }

            // Add Exception Handlers
            foreach (var eh in methodToMerge.Body.ExceptionHandlers)
            {
                targetBody.ExceptionHandlers.Insert(++exIndex, new ExceptionHandler(eh.HandlerType)
                {
                    CatchType = eh.CatchType,
                    TryStart = instrMap[eh.TryStart],
                    TryEnd = instrMap[eh.TryEnd],
                    HandlerStart = instrMap[eh.HandlerStart],
                    HandlerEnd = instrMap[eh.HandlerEnd],
                    FilterStart = eh.FilterStart == null ? null : instrMap[eh.FilterStart]
                });
            }
        }
    }

    public class UnreachableException : SystemException
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="UnreachableException" /> class.
        /// </summary>
        public UnreachableException() :
            base("Unreachable code reached.")
        {
            if (Debugger.IsAttached)
                Debugger.Break();
        }
    }
}
