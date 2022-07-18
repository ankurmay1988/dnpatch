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
        /// <exception cref="InvalidOperationException"><see cref="INameService"/> is not registered</exception>
        public static IInjectBehavior RenameAndNestBehavior(TypeDef targetType) =>
            new RenameEverythingNestedPrivateDependenciesBehavior(targetType);

        public static IInjectBehavior RenameAndInternalizeBehavior() =>
            new RenameEverythingInternalDependenciesBehavior();

        public static IInjectBehavior RenameBehavior() =>
            new RenameEverythingBehavior();

        private class RenameEverythingBehavior : IInjectBehavior
        {
            internal RenameEverythingBehavior()
            {
            }

            public virtual void Process(TypeDef source, TypeDefUser injected, Importer importer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (injected == null) throw new ArgumentNullException(nameof(injected));

                // _nameService.StoreNames(_context, injected);

                injected.Name = GetName(injected);
                injected.Namespace = null;

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
                nameBuilder.Append(type.Namespace);
                nameBuilder.Append('_');
                nameBuilder.Append(type.Name);
                var declaringType = type.DeclaringType;
                if (declaringType != null)
                {
                    nameBuilder.Insert(0, '+');
                    nameBuilder.Insert(0, declaringType.Name);
                }

                nameBuilder.Replace('.', '_').Replace('/', '_');
                return GetName(nameBuilder.ToString());
            }

            private string GetName(string originalName)
            {
                var m = Regex.Match(originalName, @".*_(\d+)");
                int num = 0;
                if (m.Success)
                {
                    num = Int32.Parse(m.Groups[1].ToString()) + 1;
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
    }
}