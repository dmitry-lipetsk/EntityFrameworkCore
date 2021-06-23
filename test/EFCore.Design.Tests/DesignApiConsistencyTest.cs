// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore
{
    public class DesignApiConsistencyTest : ApiConsistencyTestBase<DesignApiConsistencyTest.DesignApiConsistencyFixture>
    {
        public DesignApiConsistencyTest(DesignApiConsistencyFixture fixture)
            : base(fixture)
        {
        }

        protected override void AddServices(ServiceCollection serviceCollection)
        {
        }

        protected override Assembly TargetAssembly
            => typeof(OperationExecutor).Assembly;

        public class DesignApiConsistencyFixture : ApiConsistencyFixtureBase
        {
            public override HashSet<Type> FluentApiTypes { get; } = new() { typeof(DesignTimeServiceCollectionExtensions) };

            public override HashSet<Type> NonSealedPrivateNestedTypes { get; } = new()
            {
                Type.GetType("Microsoft.Extensions.Hosting.HostFactoryResolver+HostingListener, Microsoft.EntityFrameworkCore.Design", throwOnError: true),
                Type.GetType("Microsoft.Extensions.Hosting.HostFactoryResolver+HostingListener+StopTheHostException, Microsoft.EntityFrameworkCore.Design", throwOnError: true)
            };
        }
    }
}
