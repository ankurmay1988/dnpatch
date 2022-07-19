using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using dnlib.DotNet;

namespace dnpatch
{
    public interface IInjectBehavior
    {
        ModuleDef OriginModule { get; internal set; }
        ModuleDef TargetModule { get; internal set; }
        void Process(TypeDef source, TypeDefUser injected, Importer importer);
        void Process(MethodDef source, MethodDefUser injected, Importer importer);
        void Process(FieldDef source, FieldDefUser injected, Importer importer);
        void Process(EventDef source, EventDefUser injected, Importer importer);
        void Process(PropertyDef source, PropertyDefUser injected, Importer importer);
    }
    public static class InjectBehaviors
    {
        /// <summary>
        /// This inject behavior will rename every method, field and type it encounters. It will also internalize all
        /// public elements and it will declare all dependency classes as nested private classes.
        /// </summary>
        /// <param name="targetType">The "main" type. Inside of this type, all the references will be stored.</param>
        /// <returns>The inject behavior with the described properties.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is <see langword="null"/></exception>
        public static IInjectBehavior RenameAndNestBehavior(TypeDef targetType) =>
            new RenameEverythingNestedPrivateDependenciesBehavior(targetType);

        public static IInjectBehavior RenameAndInternalizeBehavior() =>
            new RenameEverythingInternalDependenciesBehavior();

        public static IInjectBehavior RenameBehavior() =>
            new RenameEverythingBehavior();

        /// <summary>
        /// This inject behavior will rename method, field and type it encounters, only if adding will create a duplicate. It will also internalize all
        /// public elements and it will declare all dependency classes as nested private classes.
        /// </summary>
        /// <param name="targetType">The "main" type. Inside of this type, all the references will be stored.</param>
        /// <returns>The inject behavior with the described properties.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="targetType"/> is <see langword="null"/></exception>
        public static IInjectBehavior RenameOnlyDuplicatesAndNestBehavior(TypeDef targetType) =>
            new RenameDuplicateNestedPrivateDependenciesBehavior(targetType);

        public static IInjectBehavior RenameOnlyDuplicatesAndInternalizeBehavior() =>
            new RenameDuplicateInternalDependenciesBehavior();

        public static IInjectBehavior RenameOnlyDuplicatesBehavior() =>
            new RenameDuplicateBehavior();

        private class RenameEverythingBehavior : IInjectBehavior
        {
            internal RenameEverythingBehavior()
            {
            }

            ModuleDef IInjectBehavior.OriginModule { get; set; }
            ModuleDef IInjectBehavior.TargetModule { get; set; }

            public virtual void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                injected.Name = GetName(injected);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(MethodDef source, MethodDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);
                if (!injected.IsSpecialName && !injected.DeclaringType.IsDelegate && !injected.IsOverride())
                    injected.Name = GetName(injected.Name);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(FieldDef source, FieldDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                    injected.Name = GetName(injected.Name);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(EventDef source, EventDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                    injected.Name = GetName(injected.Name);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(PropertyDef source, PropertyDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                    injected.Name = GetName(injected.Name);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            private string GetName(TypeDef type)
            {
                var nameBuilder = new StringBuilder();
                nameBuilder.Append(type.Name);
                var declaringType = type.DeclaringType;
                if (declaringType != null)
                {
                    nameBuilder.Insert(0, '+');
                    nameBuilder.Insert(0, declaringType.Name);
                }

                return GetName(nameBuilder.ToString());
            }

            private string GetName(string originalName)
            {
                var nameMatch = Regex.Match(originalName, @".*_(\d+)");
                int num = 0;
                if (nameMatch.Success)
                {
                    num = Int32.Parse(nameMatch.Groups[1].ToString()) + 1;
                }

                return $"{originalName}_{num}";
            }
        }

        private class RenameEverythingInternalDependenciesBehavior : RenameEverythingBehavior
        {
            internal RenameEverythingInternalDependenciesBehavior() : base()
            {
            }

            public override void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (injected.IsNested)
                {
                    if (injected.IsNestedPublic)
                        injected.Visibility = TypeAttributes.NestedAssembly;
                }
                else if (injected.IsPublic)
                    injected.Visibility = TypeAttributes.NotPublic;
            }

            public override void Process(MethodDef source, MethodDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (!injected.HasOverrides && injected.IsPublic && !injected.IsOverride())
                    injected.Access = MethodAttributes.Assembly;
            }

            public override void Process(FieldDef source, FieldDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (injected.IsPublic)
                    injected.Access = FieldAttributes.Assembly;
            }
        }

        private class RenameEverythingNestedPrivateDependenciesBehavior : RenameEverythingInternalDependenciesBehavior
        {
            private readonly TypeDef _targetType;

            internal RenameEverythingNestedPrivateDependenciesBehavior(TypeDef targetType)
                : base() =>
                _targetType = targetType ?? throw new ArgumentNullException(nameof(targetType));

            public override void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (!injected.IsNested)
                {
                    var declaringType = (TypeDef)importer.Import(_targetType);
                    if (declaringType != injected)
                    {
                        injected.DeclaringType = declaringType;
                        injected.Visibility = TypeAttributes.NestedPrivate;
                    }
                }
            }
        }

        private class RenameDuplicateBehavior : IInjectBehavior
        {
            internal RenameDuplicateBehavior()
            {
            }

            ModuleDef IInjectBehavior.OriginModule { get; set; }
            ModuleDef IInjectBehavior.TargetModule { get; set; }

            public virtual void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);
                var b = (IInjectBehavior)this;
                if (b.TargetModule.TypeExists(injected.FullName, true))
                    injected.Name = GetName(injected);

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(MethodDef source, MethodDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);
                if (!injected.IsSpecialName && !injected.DeclaringType.IsDelegate && !injected.IsOverride())
                {
                    var b = (IInjectBehavior)this;
                    if (b.TargetModule.TypeExists(injected.FullName, true))
                        injected.Name = GetName(injected.Name);
                }
                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(FieldDef source, FieldDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                {
                    var b = (IInjectBehavior)this;
                    if (b.TargetModule.TypeExists(injected.FullName, true))
                        injected.Name = GetName(injected.Name);
                }

                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(EventDef source, EventDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                {
                    var b = (IInjectBehavior)this;
                    if (b.TargetModule.TypeExists(injected.FullName, true))
                        injected.Name = GetName(injected.Name);
                }
                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            public virtual void Process(PropertyDef source, PropertyDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                if (!injected.IsSpecialName)
                {
                    var b = (IInjectBehavior)this;
                    if (b.TargetModule.TypeExists(injected.FullName, true))
                        injected.Name = GetName(injected.Name);
                }
                // There is no need for this to be renamed again.
                // _nameService.SetCanRename(_context, injected, false);
            }

            private string GetName(TypeDef type)
            {
                var nameBuilder = new StringBuilder();
                nameBuilder.Append(type.Name);
                var declaringType = type.DeclaringType;
                if (declaringType != null)
                {
                    nameBuilder.Insert(0, '+');
                    nameBuilder.Insert(0, declaringType.Name);
                }

                return GetName(nameBuilder.ToString());
            }

            private string GetName(string originalName)
            {
                var nameMatch = Regex.Match(originalName, @".*_(\d+)");
                int num = 0;
                if (nameMatch.Success)
                {
                    num = Int32.Parse(nameMatch.Groups[1].ToString()) + 1;
                }

                return $"{originalName}_{num}";
            }
        }

        private class RenameDuplicateInternalDependenciesBehavior : RenameDuplicateBehavior
        {
            internal RenameDuplicateInternalDependenciesBehavior() : base()
            {
            }

            public override void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (injected.IsNested)
                {
                    if (injected.IsNestedPublic)
                        injected.Visibility = TypeAttributes.NestedAssembly;
                }
                else if (injected.IsPublic)
                    injected.Visibility = TypeAttributes.NotPublic;
            }

            public override void Process(MethodDef source, MethodDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (!injected.HasOverrides && injected.IsPublic && !injected.IsOverride())
                    injected.Access = MethodAttributes.Assembly;
            }

            public override void Process(FieldDef source, FieldDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (injected.IsPublic)
                    injected.Access = FieldAttributes.Assembly;
            }
        }

        private class RenameDuplicateNestedPrivateDependenciesBehavior : RenameDuplicateInternalDependenciesBehavior
        {
            private readonly TypeDef _targetType;

            internal RenameDuplicateNestedPrivateDependenciesBehavior(TypeDef targetType)
                : base() =>
                _targetType = targetType ?? throw new ArgumentNullException(nameof(targetType));

            public override void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                base.Process(source, injected, importer);

                if (!injected.IsNested)
                {
                    var declaringType = (TypeDef)importer.Import(_targetType);
                    if (declaringType != injected)
                    {
                        injected.DeclaringType = declaringType;
                        injected.Visibility = TypeAttributes.NestedPrivate;
                    }
                }
            }
        }

    }
}