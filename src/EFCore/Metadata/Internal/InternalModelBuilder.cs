// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class InternalModelBuilder : AnnotatableBuilder<Model, InternalModelBuilder>, IConventionModelBuilder
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public InternalModelBuilder([NotNull] Model metadata)
            : base(metadata, null!)
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public override InternalModelBuilder ModelBuilder
            => this;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder? Entity(
            [NotNull] string name,
            ConfigurationSource configurationSource,
            bool? shouldBeOwned = false)
            => Entity(new TypeIdentity(name), configurationSource, shouldBeOwned);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder? SharedTypeEntity(
            [NotNull] string name,
            [CanBeNull] Type? type,
            ConfigurationSource configurationSource,
            bool? shouldBeOwned = false)
            => Entity(new TypeIdentity(name, type ?? Model.DefaultPropertyBagType), configurationSource, shouldBeOwned);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder? Entity(
            [NotNull] Type type,
            ConfigurationSource configurationSource,
            bool? shouldBeOwned = false)
            => Entity(new TypeIdentity(type, Metadata), configurationSource, shouldBeOwned);

        private InternalEntityTypeBuilder? Entity(
            in TypeIdentity type,
            ConfigurationSource configurationSource,
            bool? shouldBeOwned)
        {
            if (IsIgnored(type, configurationSource))
            {
                return null;
            }

            using var batch = Metadata.DelayConventions();
            var clrType = type.Type;
            EntityType? entityType;
            EntityType.Snapshot? entityTypeSnapshot = null;
            if (type.IsNamed)
            {
                if (clrType != null)
                {
                    entityType = Metadata.FindEntityType(clrType);
                    if (entityType != null)
                    {
                        if (entityType.Name == type.Name
                            && entityType.HasSharedClrType)
                        {
                            entityType.UpdateConfigurationSource(configurationSource);
                            return entityType.Builder;
                        }

                        if (!configurationSource.OverridesStrictly(entityType.GetConfigurationSource())
                            && !entityType.IsOwned())
                        {
                            return configurationSource == ConfigurationSource.Explicit
                                ? throw new InvalidOperationException(CoreStrings.ClashingNonSharedType(type.Name, clrType.ShortDisplayName()))
                                : (InternalEntityTypeBuilder?)null;
                        }

                        entityTypeSnapshot = InternalEntityTypeBuilder.DetachAllMembers(entityType);

                        HasNoEntityType(entityType, ConfigurationSource.Explicit);
                    }
                }

                entityType = Metadata.FindEntityType(type.Name);
            }
            else
            {
                clrType = type.Type!;
                if (Metadata.IsShared(clrType))
                {
                    return configurationSource == ConfigurationSource.Explicit
                        ? throw new InvalidOperationException(CoreStrings.ClashingSharedType(clrType.ShortDisplayName()))
                        : (InternalEntityTypeBuilder?)null;
                }

                entityType = Metadata.FindEntityType(clrType);
            }

            if (shouldBeOwned == false
                && (ShouldBeOwnedType(type)
                    || entityType != null && entityType.IsOwned()))
            {
                // We always throw as configuring a type as owned always comes from user (through Explicit/DataAnnotation)
                throw new InvalidOperationException(
                    CoreStrings.ClashingOwnedEntityType(
                        clrType == null ? type.Name : clrType.ShortDisplayName()));
            }

            if (shouldBeOwned == true
                && entityType != null)
            {
                if (!entityType.IsOwned()
                    && configurationSource == ConfigurationSource.Explicit
                    && entityType.GetConfigurationSource() == ConfigurationSource.Explicit)
                {
                    throw new InvalidOperationException(CoreStrings.ClashingNonOwnedEntityType(clrType!.ShortDisplayName()));
                }

                foreach (var derivedType in entityType.GetDerivedTypes())
                {
                    if (!derivedType.IsOwned()
                        && configurationSource == ConfigurationSource.Explicit
                        && derivedType.GetConfigurationSource() == ConfigurationSource.Explicit)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.ClashingNonOwnedDerivedEntityType(entityType.DisplayName(), derivedType.DisplayName()));
                    }
                }
            }

            if (entityType != null)
            {
                if (type.Type == null
                    || entityType.ClrType == type.Type)
                {
                    entityType.UpdateConfigurationSource(configurationSource);
                    return entityType.Builder;
                }

                if (configurationSource.OverridesStrictly(entityType.GetConfigurationSource()))
                {
                    HasNoEntityType(entityType, configurationSource);
                }
                else
                {
                    return configurationSource == ConfigurationSource.Explicit
                        ? throw new InvalidOperationException(
                            CoreStrings.ClashingMismatchedSharedType(type.Name, entityType.ClrType.ShortDisplayName()))
                        : (InternalEntityTypeBuilder?)null;
                }
            }

            Metadata.RemoveIgnored(type.Name);
            entityType = type.IsNamed
                ? clrType == null
                    ? Metadata.AddEntityType(type.Name, configurationSource)
                    : Metadata.AddEntityType(type.Name, clrType, configurationSource)
                : Metadata.AddEntityType(clrType!, configurationSource);

            if (entityType != null
                && entityTypeSnapshot != null)
            {
                entityTypeSnapshot.Attach(entityType.Builder);
            }

            return entityType?.Builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder? Entity(
            [NotNull] string name,
            [NotNull] string definingNavigationName,
            [NotNull] EntityType definingEntityType,
            ConfigurationSource configurationSource)
            => Entity(new TypeIdentity(name), definingNavigationName, definingEntityType, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder? Entity(
            [NotNull] Type type,
            [NotNull] string definingNavigationName,
            [NotNull] EntityType definingEntityType,
            ConfigurationSource configurationSource)
            => Entity(new TypeIdentity(type, Metadata), definingNavigationName, definingEntityType, configurationSource);

        private InternalEntityTypeBuilder? Entity(
            in TypeIdentity type,
            string definingNavigationName,
            EntityType definingEntityType,
            ConfigurationSource configurationSource)
            => SharedTypeEntity(
                definingEntityType.GetOwnedName(type.Type?.ShortDisplayName() ?? type.Name, definingNavigationName),
                type.Type, configurationSource, shouldBeOwned: true);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? RemoveImplicitJoinEntity([NotNull] EntityType joinEntityType)
        {
            Check.NotNull(joinEntityType, nameof(joinEntityType));

            if (!joinEntityType.IsInModel)
            {
                return this;
            }

            if (!joinEntityType.IsImplicitlyCreatedJoinEntityType)
            {
                return null;
            }

            return HasNoEntityType(joinEntityType, ConfigurationSource.Convention);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IConventionOwnedEntityTypeBuilder? Owned(
            [NotNull] Type type,
            ConfigurationSource configurationSource)
        {
            if (IsIgnored(type, configurationSource))
            {
                return null;
            }

            Metadata.RemoveIgnored(type);
            Metadata.AddOwned(type, ConfigurationSource.Explicit);

            foreach (var entityType in Metadata.FindEntityTypes(type))
            {
                if (entityType.IsOwned())
                {
                    continue;
                }

                if (!configurationSource.Overrides(entityType.GetConfigurationSource()))
                {
                    return null;
                }

                if (entityType.GetConfigurationSource() == ConfigurationSource.Explicit)
                {
                    throw new InvalidOperationException(CoreStrings.ClashingNonOwnedEntityType(type.ShortDisplayName()));
                }

                foreach (var derivedType in entityType.GetDerivedTypes())
                {
                    if (!derivedType.IsOwned()
                        && configurationSource == ConfigurationSource.Explicit
                        && derivedType.GetConfigurationSource() == ConfigurationSource.Explicit)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.ClashingNonOwnedDerivedEntityType(type.ShortDisplayName(), derivedType.ShortName()));
                    }
                }

                var ownershipCandidates = entityType.GetForeignKeys().Where(
                    fk => fk.PrincipalToDependent != null
                        && !fk.PrincipalEntityType.IsInOwnershipPath(type)).ToList();
                if (ownershipCandidates.Count == 1)
                {
                    if (ownershipCandidates[0].Builder.IsOwnership(true, configurationSource) == null)
                    {
                        return null;
                    }
                }
                else if (ownershipCandidates.Count > 1)
                {
                    using (var batch = ModelBuilder.Metadata.DelayConventions())
                    {
                        var ownership = ownershipCandidates[0].Builder.IsOwnership(true, configurationSource);
                        if (ownership == null)
                        {
                            return null;
                        }
                        ownership.MakeDeclaringTypeShared(configurationSource);
                    }
                }
                else
                {
                    if (!entityType.Builder.RemoveNonOwnershipRelationships(null, configurationSource))
                    {
                        return null;
                    }
                }
            }

            return new InternalOwnedEntityTypeBuilder();
        }

        private bool ShouldBeOwnedType(in TypeIdentity type)
            => type.Type != null && Metadata.IsOwned(type.Type);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool IsIgnored([NotNull] Type type, ConfigurationSource configurationSource)
            => IsIgnored(new TypeIdentity(type, Metadata), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool IsIgnored([NotNull] string name, ConfigurationSource configurationSource)
            => IsIgnored(new TypeIdentity(name), configurationSource);

        private bool IsIgnored(in TypeIdentity type, ConfigurationSource configurationSource)
        {
            if (configurationSource == ConfigurationSource.Explicit)
            {
                return false;
            }

            var ignoredConfigurationSource = Metadata.FindIgnoredConfigurationSource(type.Name);
            return ignoredConfigurationSource.HasValue
                && ignoredConfigurationSource.Value.Overrides(configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? Ignore([NotNull] Type type, ConfigurationSource configurationSource)
            => Ignore(new TypeIdentity(type, Metadata), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? Ignore([NotNull] string name, ConfigurationSource configurationSource)
            => Ignore(new TypeIdentity(name), configurationSource);

        private InternalModelBuilder? Ignore(in TypeIdentity type, ConfigurationSource configurationSource)
        {
            var name = type.Name;
            var ignoredConfigurationSource = Metadata.FindIgnoredConfigurationSource(name);
            if (ignoredConfigurationSource.HasValue)
            {
                if (configurationSource.Overrides(ignoredConfigurationSource)
                    && configurationSource != ignoredConfigurationSource)
                {
                    Metadata.AddIgnored(name, configurationSource);
                }

                return this;
            }

            if (!CanIgnore(type, configurationSource))
            {
                return null;
            }

            using (Metadata.DelayConventions())
            {
                var entityType = Metadata.FindEntityType(name);
                if (entityType != null)
                {
                    HasNoEntityType(entityType, configurationSource);
                }

                if (type.Type == null)
                {
                    Metadata.AddIgnored(name, configurationSource);
                }
                else
                {
                    Metadata.AddIgnored(type.Type, configurationSource);
                }

                if (type.Type != null)
                {
                    Metadata.RemoveOwned(type.Type);
                }

                return this;
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanIgnore([NotNull] Type type, ConfigurationSource configurationSource)
            => CanIgnore(new TypeIdentity(type, Metadata), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanIgnore([NotNull] string name, ConfigurationSource configurationSource)
            => CanIgnore(new TypeIdentity(name), configurationSource);

        private bool CanIgnore(in TypeIdentity type, ConfigurationSource configurationSource)
        {
            var name = type.Name;
            if (Metadata.FindIgnoredConfigurationSource(name).HasValue)
            {
                return true;
            }

            if (ShouldBeOwnedType(type)
                && configurationSource != ConfigurationSource.Explicit)
            {
                return false;
            }

            if (type.Type != null
                && Metadata.FindEntityTypes(type.Type).Any(o => !configurationSource.Overrides(o.GetConfigurationSource())))
            {
                return false;
            }

            if (Metadata.FindEntityType(name)?.GetConfigurationSource().OverridesStrictly(configurationSource) == true)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? HasNoEntityType([NotNull] EntityType entityType, ConfigurationSource configurationSource)
        {
            var entityTypeConfigurationSource = entityType.GetConfigurationSource();
            if (!configurationSource.Overrides(entityTypeConfigurationSource))
            {
                return null;
            }

            using (Metadata.DelayConventions())
            {
                var entityTypeBuilder = entityType.Builder;
                foreach (var foreignKey in entityType.GetDeclaredReferencingForeignKeys().ToList())
                {
                    var removed = foreignKey.DeclaringEntityType.Builder.HasNoRelationship(foreignKey, configurationSource);
                    Check.DebugAssert(removed != null, "removed is null");
                }

                foreach (var skipNavigation in entityType.GetDeclaredReferencingSkipNavigations().ToList())
                {
                    var removed = skipNavigation.DeclaringEntityType.Builder.HasNoSkipNavigation(skipNavigation, configurationSource);
                    Check.DebugAssert(removed != null, "removed is null");
                }

                foreach (var skipNavigation in entityType.GetDeclaredForeignKeys().SelectMany(fk => fk.GetReferencingSkipNavigations())
                    .ToList())
                {
                    var removed = skipNavigation.Builder.HasForeignKey(null, configurationSource);
                    Check.DebugAssert(removed != null, "removed is null");
                }

                foreach (var directlyDerivedType in entityType.GetDirectlyDerivedTypes().ToList())
                {
                    var derivedEntityTypeBuilder = directlyDerivedType.Builder
                        .HasBaseType(entityType.BaseType, configurationSource);
                    Check.DebugAssert(derivedEntityTypeBuilder != null, "derivedEntityTypeBuilder is null");
                }

                Metadata.RemoveEntityType(entityType);
            }

            return this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? HasChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy,
            ConfigurationSource configurationSource)
        {
            if (CanSetChangeTrackingStrategy(changeTrackingStrategy, configurationSource))
            {
                Metadata.SetChangeTrackingStrategy(changeTrackingStrategy, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy,
            ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetChangeTrackingStrategyConfigurationSource())
                || Metadata.GetChangeTrackingStrategy() == changeTrackingStrategy;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalModelBuilder? UsePropertyAccessMode(
            PropertyAccessMode? propertyAccessMode,
            ConfigurationSource configurationSource)
        {
            if (CanSetPropertyAccessMode(propertyAccessMode, configurationSource))
            {
                Metadata.SetPropertyAccessMode(propertyAccessMode, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetPropertyAccessMode(
            PropertyAccessMode? propertyAccessMode,
            ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetPropertyAccessModeConfigurationSource())
                || Metadata.GetPropertyAccessMode() == propertyAccessMode;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IConventionModel IConventionModelBuilder.Metadata
        {
            [DebuggerStepThrough]
            get => Metadata;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder? IConventionModelBuilder.Entity(string name, bool? shouldBeOwned, bool fromDataAnnotation)
            => Entity(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention, shouldBeOwned);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder? IConventionModelBuilder.SharedTypeEntity(
            string name,
            Type type,
            bool? shouldBeOwned,
            bool fromDataAnnotation)
            => SharedTypeEntity(
                name, type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention, shouldBeOwned);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder? IConventionModelBuilder.Entity(Type type, bool? shouldBeOwned, bool fromDataAnnotation)
            => Entity(type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention, shouldBeOwned);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder? IConventionModelBuilder.Entity(
            string name,
            string definingNavigationName,
            IConventionEntityType definingEntityType,
            bool fromDataAnnotation)
            => Entity(
                name,
                definingNavigationName,
                (EntityType)definingEntityType,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder? IConventionModelBuilder.Entity(
            Type type,
            string definingNavigationName,
            IConventionEntityType definingEntityType,
            bool fromDataAnnotation)
            => Entity(
                type,
                definingNavigationName,
                (EntityType)definingEntityType,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionOwnedEntityTypeBuilder? IConventionModelBuilder.Owned(Type type, bool fromDataAnnotation)
            => Owned(type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.IsIgnored(Type type, bool fromDataAnnotation)
            => IsIgnored(type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.IsIgnored(string name, bool fromDataAnnotation)
            => IsIgnored(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionModelBuilder? IConventionModelBuilder.Ignore(Type type, bool fromDataAnnotation)
            => Ignore(type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionModelBuilder? IConventionModelBuilder.Ignore(string name, bool fromDataAnnotation)
            => Ignore(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionModelBuilder? IConventionModelBuilder.HasNoEntityType(IConventionEntityType entityType, bool fromDataAnnotation)
            => HasNoEntityType(
                (EntityType)entityType, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.CanIgnore(Type type, bool fromDataAnnotation)
            => CanIgnore(type, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.CanIgnore(string name, bool fromDataAnnotation)
            => CanIgnore(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionModelBuilder? IConventionModelBuilder.HasChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy,
            bool fromDataAnnotation)
            => HasChangeTrackingStrategy(
                changeTrackingStrategy, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.CanSetChangeTrackingStrategy(ChangeTrackingStrategy? changeTrackingStrategy, bool fromDataAnnotation)
            => CanSetChangeTrackingStrategy(
                changeTrackingStrategy, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionModelBuilder? IConventionModelBuilder.UsePropertyAccessMode(
            PropertyAccessMode? propertyAccessMode,
            bool fromDataAnnotation)
            => UsePropertyAccessMode(
                propertyAccessMode, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionModelBuilder.CanSetPropertyAccessMode(PropertyAccessMode? propertyAccessMode, bool fromDataAnnotation)
            => CanSetPropertyAccessMode(
                propertyAccessMode, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);
    }
}
