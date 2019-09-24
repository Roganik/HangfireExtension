// This file is part of Hangfire.
// Copyright © 2015 Sergey Odinokov.
//
// Hangfire is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as
// published by the Free Software Foundation, either version 3
// of the License, or any later version.
//
// Hangfire is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

using Hangfire.Annotations;
using System;

// ReSharper disable once CheckNamespace
namespace Hangfire.SQLite
{
    public static class SQLiteStorageExtensions
    {
        public static IGlobalConfiguration<SQLiteStorage> UseSQLiteStorage(
            [NotNull] this IGlobalConfiguration configuration,
            [NotNull] string connectionString)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            var storage = new SQLiteStorage(connectionString);
            return configuration.UseStorage(storage);
        }

        public static IGlobalConfiguration<SQLiteStorage> UseSQLiteStorage(
            [NotNull] this IGlobalConfiguration configuration,
            [NotNull] string connectionString,
            [NotNull] SQLiteStorageOptions options)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var storage = new SQLiteStorage(connectionString, options);
            return configuration.UseStorage(storage);
        }
    }
}
