using System;
using System.Linq;
using dnlib.DotNet;

namespace dnpatch
{
    public static partial class InjectHelper
    {
        public static InjectResult<TypeDef> PatchInject(TypeDef typeDef,
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

            IMemberDef targetTypeDef = ParseMapAttribute(typeDef, ctx);
            ctx.ApplyMapping(typeDef, targetTypeDef);
            if (targetTypeDef == null) throw new ArgumentException("PatchAttribute is missing");

            // Map Source and Target methods/fields/properties etc.
            foreach (var method in typeDef.Methods.Where(HasMapAttribute))
            {
                targetTypeDef = ParseMapAttribute(method, ctx);
                ctx.ApplyMapping(method, targetTypeDef);
            }
            foreach (var field in typeDef.Fields.Where(HasMapAttribute))
            {
                targetTypeDef = ParseMapAttribute(field, ctx);
                ctx.ApplyMapping(field, targetTypeDef);
            }
            foreach (var @event in typeDef.Events.Where(HasMapAttribute))
            {
                targetTypeDef = ParseMapAttribute(@event, ctx);
                ctx.ApplyMapping(@event, targetTypeDef);
            }
            foreach (var prop in typeDef.Properties.Where(HasMapAttribute))
            {
                targetTypeDef = ParseMapAttribute(prop, ctx);
                ctx.ApplyMapping(prop, targetTypeDef);
            }
            foreach (var nestedType in typeDef.NestedTypes.Where(HasMapAttribute))
            {
                targetTypeDef = ParseMapAttribute(nestedType, ctx);
                ctx.ApplyMapping(nestedType, targetTypeDef);
            }

            // Patch Methods
            foreach (var method in typeDef.Methods.Where(HasPatchAttribute))
            {
                var targetMethodDef = (MethodDef)ctx.ResolveMapped(method);
                var importer = new Importer(ctx.TargetModule, ImporterOptions.TryToUseDefs, new GenericParamContext(), injector);
                injector.CopyDef(method.Body.Instructions);
                injector.InjectRemaining(importer);
                injector.InjectMethodInstructions(method, targetMethodDef, importer);
            }

            var mappedType = ctx.ResolveMapped(typeDef);
            return InjectResult.Create(typeDef, mappedType, injector.InjectedMembers.Where(m => m.Value != mappedType));
        }

        private static string GetMapAttributeValue(IHasCustomAttribute typeDef)
        {
            var memberRef = (IMemberRef)typeDef;
            var attrName = GetMapAttrName(memberRef);
            var attr = typeDef.CustomAttributes.Find(attrName);
            var value = UTF8String.ToSystemString((UTF8String)attr.ConstructorArguments.First().Value);
            return value;
        }

        private static string GetPatchAttributeValue(IHasCustomAttribute typeDef)
        {
            var memberRef = (IMemberRef)typeDef;
            var attrName = "PatchAttribute";
            var attr = typeDef.CustomAttributes.Find(attrName);
            var value = UTF8String.ToSystemString((UTF8String)attr.ConstructorArguments.First().Value);
            return value;
        }

        private static bool HasMapAttribute(IHasCustomAttribute typeDef)
        {
            var memberRef = (IMemberRef)typeDef;
            string attrName = GetMapAttrName(memberRef);
            return typeDef.HasCustomAttributes && typeDef.CustomAttributes.IsDefined(attrName);
        }

        private static bool HasPatchAttribute(IHasCustomAttribute typeDef)
        {
            var memberRef = (IMemberRef)typeDef;
            string attrName = "PatchAttribute";
            return typeDef.HasCustomAttributes && typeDef.CustomAttributes.IsDefined(attrName);
        }

        private static string GetMapAttrName(IMemberRef memberRef)
        {
            return memberRef.IsFieldDef ? "MapFieldAttribute" :
                            memberRef.IsPropertyDef ? "MapPropertyAttribute" :
                            memberRef.IsMethodDef ? "MapMethodAttribute" :
                            memberRef.IsEventDef ? "MapEventAttribute" : "MapClassAttribute";
        }

        private static IMemberDef ParseMapAttribute(IHasCustomAttribute typeDef, InjectContext ctx)
        {
            var memberRef = (IMemberDef)typeDef;
            IMemberDef returnType = ctx.ResolveMapped(memberRef) as TypeDef;
            if (returnType != null) return returnType;

            var declaringType = memberRef.DeclaringType;

            TypeDef targetTypeDef = null;
            if (declaringType != null)
                targetTypeDef = (TypeDef)ParseMapAttribute(declaringType, ctx);

            if (targetTypeDef != null)
            {
                if (memberRef is MethodDef methodDef)
                {
                    var reflectionName = GetMapAttributeValue(typeDef);
                    if (!string.IsNullOrWhiteSpace(reflectionName))
                        returnType = targetTypeDef.FindMethod(reflectionName, methodDef.MethodSig);
                }
                else if (memberRef is FieldDef fieldDef)
                {
                    var reflectionName = GetMapAttributeValue(typeDef);
                    if (!string.IsNullOrWhiteSpace(reflectionName))
                        returnType = targetTypeDef.FindField(reflectionName, fieldDef.FieldSig);
                }
                else if (memberRef is PropertyDef propertyDef)
                {
                    var reflectionName = GetMapAttributeValue(typeDef);
                    if (!string.IsNullOrWhiteSpace(reflectionName))
                        returnType = targetTypeDef.FindProperty(reflectionName, propertyDef.PropertySig);
                }
                else if (memberRef is EventDef eventDef)
                {
                    var reflectionName = GetMapAttributeValue(typeDef);
                    if (!string.IsNullOrWhiteSpace(reflectionName))
                        returnType = targetTypeDef.FindEvent(reflectionName, eventDef.EventType);
                }
                else
                {
                    throw new ArgumentOutOfRangeException($"Unsupported attribute on {typeDef.ToString()}");
                }
            }
            else
            {
                var reflectionName = GetMapAttributeValue(typeDef);
                if (!string.IsNullOrWhiteSpace(reflectionName))
                    returnType = ctx.TargetModule.Find(reflectionName, false);
            }

            if (returnType == null)
                throw new Exception("Cannot find the type in target assembly");

            return returnType;
        }
    }
}