﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Sqlite.Query.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class SqliteGlobMethodTranslator : IMethodCallTranslator
    {
        private static readonly MethodInfo _methodInfo = typeof(SqliteDbFunctionsExtensions)
            .GetRequiredMethod(nameof(SqliteDbFunctionsExtensions.Glob), typeof(DbFunctions), typeof(string), typeof(string));

        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public SqliteGlobMethodTranslator([NotNull] ISqlExpressionFactory sqlExpressionFactory)
            => _sqlExpressionFactory = Check.NotNull(sqlExpressionFactory, nameof(sqlExpressionFactory));

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            Check.NotNull(method, nameof(method));
            Check.NotNull(arguments, nameof(arguments));
            Check.NotNull(logger, nameof(logger));

            if (method.Equals(_methodInfo))
            {
                var matchExpression = arguments[1];
                var pattern = arguments[2];
                var stringTypeMapping = ExpressionExtensions.InferTypeMapping(matchExpression, pattern);

                return _sqlExpressionFactory.Function(
                    "glob",
                    new[]
                    {
                        _sqlExpressionFactory.ApplyTypeMapping(pattern, stringTypeMapping),
                        _sqlExpressionFactory.ApplyTypeMapping(matchExpression, stringTypeMapping)
                    },
                    nullable: true,
                    argumentsPropagateNullability: new[] { true, true },
                    typeof(bool));
            }

            return null;
        }
    }
}
