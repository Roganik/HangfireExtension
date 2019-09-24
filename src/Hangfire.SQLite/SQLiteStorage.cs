// This file is part of Hangfire.
// Copyright ?2013-2014 Sergey Odinokov.
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
using Hangfire.Server;
using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace Hangfire.SQLite
{
    public class SQLiteStorage : JobStorage
    {
        private readonly DbConnection _existingConnection;
        private readonly SQLiteStorageOptions _options;
        private readonly string _connectionString;
        private static readonly TimeSpan ReaderWriterLockTimeout = TimeSpan.FromSeconds(30);
        private static Dictionary<string, ReaderWriterLockSlim> _dbMonitorCache = new Dictionary<string, ReaderWriterLockSlim>();

        public SQLiteStorage(string connectionString)
            : this(connectionString, new SQLiteStorageOptions())
        {
        }

        /// <summary>
        /// Initializes SQLiteStorage from the provided SQLiteStorageOptions and the provided connection string.
        /// </summary>
        /// <param name="connectionString">A SQLite connection string</param>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> argument is null.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> argument is null.</exception>
        public SQLiteStorage(string connectionString, SQLiteStorageOptions options)
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (options == null) throw new ArgumentNullException(nameof(options));

            _connectionString = connectionString;
            _options = options;

            if (!_dbMonitorCache.ContainsKey(_connectionString))
            {
                _dbMonitorCache.Add(_connectionString, new ReaderWriterLockSlim());
            }

            Initialize();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SQLiteStorage"/> class with
        /// explicit instance of the <see cref="DbConnection"/> class that will be used
        /// to query the data.
        /// </summary>
        /// <param name="existingConnection">Existing connection</param>
        public SQLiteStorage([NotNull] DbConnection existingConnection)
            : this(existingConnection, new SQLiteStorageOptions())
        {
        }

        public SQLiteStorage([NotNull] DbConnection existingConnection, [NotNull] SQLiteStorageOptions options)
        {
            if (existingConnection == null) throw new ArgumentNullException(nameof(existingConnection));
            if (options == null) throw new ArgumentNullException(nameof(options));

            _existingConnection = existingConnection;
            _options = options;

            Initialize();
        }

        public PersistentJobQueueProviderCollection QueueProviders { get; private set; }

        internal string SchemaName => _options.SchemaName;

        public override IMonitoringApi GetMonitoringApi()
        {
            return new SQLiteMonitoringApi(this, _options.DashboardJobListLimit);
        }

        public override IStorageConnection GetConnection()
        {
            return new SQLiteStorageConnection(this);
        }

        public override IEnumerable<IServerComponent> GetComponents()
        {
            yield return new ExpirationManager(this, _options.JobExpirationCheckInterval);
            //yield return new CountersAggregator(this, _options.CountersAggregateInterval);
        }

        //public override void WriteOptionsToLog(ILog logger)
        //{
        //    logger.Info("Using the following options for SQL Server job storage:");
        //    logger.InfoFormat("    Queue poll interval: {0}.", _options.QueuePollInterval);
        //}

        public override string ToString()
        {
            var connectionStringBuilder = new SqliteConnectionStringBuilder(_connectionString);
            return $"SQLite: {connectionStringBuilder.DataSource}";
        }

        internal void UseConnection([InstantHandle] Action<DbConnection> action, bool isWriteLock = false)
        {
            UseConnection(connection =>
            {
                action(connection);
                return true;
            }, isWriteLock);
        }

        internal T UseConnection<T>([InstantHandle] Func<DbConnection, T> func, bool isWriteLock = false)
        {
            DbConnection connection = null;

            try
            {
                connection = CreateAndOpenConnection(isWriteLock);
                return func(connection);
            }
            finally
            {
                ReleaseConnection(connection);
            }
        }

        internal void UseTransaction([InstantHandle] Action<DbConnection, DbTransaction> action)
        {
            UseTransaction((connection, transaction) =>
            {
                action(connection, transaction);
                return true;
            }, null);
        }

        internal T UseTransaction<T>([InstantHandle] Func<DbConnection, DbTransaction, T> func, IsolationLevel? isolationLevel)
        {
            return UseConnection(connection =>
            {
                using (var transaction = connection.BeginTransaction(isolationLevel ?? _options.TransactionIsolationLevel ?? IsolationLevel.ReadCommitted))
                {
                    var result = func(connection, transaction);
                    transaction.Commit();

                    return result;
                }
            }, true);
        }

        internal DbConnection CreateAndOpenConnection(bool isWriteLock = false)
        {
            if (_existingConnection != null)
            {
                return _existingConnection;
            }

            if (isWriteLock)
            {
                _dbMonitorCache[_connectionString].TryEnterWriteLock(ReaderWriterLockTimeout);
            }

            var connection = new SqliteConnection(_connectionString);
            connection.Open();

            return connection;
        }

        internal bool IsExistingConnection(IDbConnection connection)
        {
            return connection != null && ReferenceEquals(connection, _existingConnection);
        }

        internal void ReleaseConnection(IDbConnection connection)
        {
            if (connection != null && !IsExistingConnection(connection))
            {
                connection.Dispose();

                ReleaseDbWriteLock();
            }
        }

        internal void ReleaseDbWriteLock()
        {
            var dbMonitor = _dbMonitorCache[_connectionString];
            if (dbMonitor.IsWriteLockHeld)
            {
                dbMonitor.ExitWriteLock();
            }
        }

        private void Initialize()
        {
            if (_options.PrepareSchemaIfNecessary)
            {
                UseConnection(connection =>
                {
                    SQLiteObjectsInstaller.Install(connection, _options.SchemaName);
                });
            }

            InitializeQueueProviders();
        }

        private void InitializeQueueProviders()
        {
            var defaultQueueProvider = new SQLiteJobQueueProvider(this, _options);
            QueueProviders = new PersistentJobQueueProviderCollection(defaultQueueProvider);
        }
    }
}